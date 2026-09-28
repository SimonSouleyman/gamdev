shader_type canvas_item;
// One sheet of paper, drawn in a single pass over its rect plus `pad` pixels on every side:
// the soft cast shadow (longer under a lifted corner), torn or worn edges with loose fibres,
// printed ruling, age (mottling, foxing, handled edges, a tide-line stain), a binding gutter for
// book pages, and a crumple normal map plus fold creases lit by one fixed light, so the creases
// read as relief rather than as a printed pattern.
// Used on a StyleBoxTexture (1 px white texture, expand margins = pad), so UV runs 0..1 over
// the whole padded quad. PaperLook (paper_look.gd) sets the uniforms.

uniform vec2 sheet_size = vec2(320.0, 240.0);   // the sheet itself, in px (without pad)
uniform float pad = 28.0;                       // room around the sheet for the shadow
uniform float seed = 0.0;
uniform float opacity = 1.0;                    // < 1 for translucent things (tape)

uniform vec3 paper_color : source_color = vec3(0.95, 0.915, 0.82);
uniform vec3 age_color : source_color = vec3(0.80, 0.70, 0.52);
uniform float mottle = 0.35;                    // blotchy yellowing across the sheet
uniform float grain = 1.0;                      // fibre grain
uniform float foxing = 0.5;                     // rust-brown age spots
uniform float edge_age = 0.5;                   // darker, handled rim
uniform vec4 stain = vec4(0.0);                 // tide-line ring: x, y (sheet uv), radius px, strength

// 0 plain, 1 squared (graph) paper, 2 lined with a red margin
uniform int ruling = 0;
uniform float rule_step = 26.0;
uniform vec2 rule_offset = vec2(12.0, 40.0);
uniform vec3 rule_color : source_color = vec3(0.45, 0.58, 0.72);

// Edge depths in px: left, top, right, bottom. 0 = a cut (slightly worn) edge.
uniform vec4 torn = vec4(0.0);
uniform float tear_scale = 1.0;

uniform sampler2D crumple_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_tex : filter_linear_mipmap, repeat_enable;
uniform float crumple = 0.6;                    // relief strength of the crumple map
uniform float crumple_scale = 1.0;              // texture px per sheet px
uniform vec4 fold_a = vec4(0.5, 0.5, 1.5708, 0.0);   // x, y (sheet uv), angle, strength
uniform vec4 fold_b = vec4(0.5, 0.5, 0.0, 0.0);

uniform vec3 light_dir = vec3(-0.45, -0.6, 0.66);   // from the top left, towards the viewer
uniform float light_amount = 0.9;
uniform float curl = 0.0;                       // bottom-right corner lifted by this many px
uniform float gutter = 0.0;                     // -1 binding on the left, +1 on the right, 0 none
uniform float gutter_width = 60.0;

uniform vec2 shadow_offset = vec2(3.0, 6.0);
uniform float shadow_soft = 9.0;
uniform float shadow_alpha = 0.42;

float n1(float t, float row) {
	// Smooth 1D noise from the detail texture's mottle and tooth channels.
	return texture(detail_tex, vec2(t, row)).b * 0.7 + texture(detail_tex, vec2(t * 3.7, row + 0.31)).a * 0.3;
}

// Where the edge lies, measured inward from the nominal border, at position t along it.
float edge_offset(float t, float depth, float side) {
	float row = fract(seed * 0.173 + side * 0.257);
	if (depth <= 0.0) {
		// A cut edge that has been handled: tiny wobble and the odd nick.
		float nick = smoothstep(0.5, 0.95, texture(detail_tex, vec2(t / 25.0, row)).g);
		return 0.6 + 0.7 * n1(t / 90.0, row) + nick * 1.6;
	}
	float s = tear_scale;
	float big = n1(t / (260.0 * s), row);
	float mid = n1(t / (60.0 * s), row + 0.11);
	float jag = texture(detail_tex, vec2(t / (9.0 * s), row + 0.43)).r;
	return depth * (0.15 + 0.55 * big + 0.3 * mid) + jag * 2.2;
}

float box_sdf(vec2 p, vec2 size, float r) {
	vec2 q = abs(p - size * 0.5) - size * 0.5 + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float grid_line(float x, float stp, float width) {
	float d = abs(fract(x / stp + 0.5) - 0.5) * stp;
	float aa = clamp(fwidth(x), 0.001, 2.0);
	return 1.0 - smoothstep(width * 0.5, width * 0.5 + aa, d);
}

vec2 fold_tilt(vec4 f, vec2 p, inout float line) {
	if (f.w == 0.0) {
		return vec2(0.0);
	}
	vec2 o = f.xy * sheet_size;
	vec2 dir = vec2(cos(f.z), sin(f.z));
	vec2 nrm = vec2(-dir.y, dir.x);
	float d = dot(p - o, nrm);
	// Wobble: a hand-made fold is never ruler straight.
	d += (n1(dot(p, dir) / 300.0, fract(seed * 0.31 + 0.6)) - 0.5) * 3.0;
	line += exp(-abs(d) / 1.1) * abs(f.w);
	// Both halves tip away from (or into) the crease, strongest close to it.
	return nrm * sign(d) * f.w * (0.12 + 0.88 * exp(-abs(d) / 70.0));
}

void fragment() {
	vec2 full = sheet_size + 2.0 * pad;
	vec2 p = UV * full - pad;
	vec2 suv = p / sheet_size;
	vec2 seed_off = vec2(fract(seed * 0.618), fract(seed * 0.382)) * 1024.0;

	// --- the outline: four edges, torn or cut --------------------------------------------
	float dl = p.x - edge_offset(p.y, torn.x, 0.0);
	float dt = p.y - edge_offset(p.x, torn.y, 1.0);
	float dr = sheet_size.x - p.x - edge_offset(p.y, torn.z, 2.0);
	float db = sheet_size.y - p.y - edge_offset(p.x, torn.w, 3.0);
	float sdf = min(min(dl, dr), min(dt, db));
	// Rounded, softened corners.
	float corner = box_sdf(p, sheet_size, 5.0);
	sdf = min(sdf, -corner - 0.5);
	// Clamped: derivatives on the quad's outermost pixels can be wild on some GPUs.
	float aa = clamp(fwidth(p.x), 0.5, 2.0);
	float a_sheet = clamp(sdf / aa + 0.5, 0.0, 1.0);

	// Which torn edge is nearest, and how deep it is: the fresh tear shows paler fibres.
	float torn_near = 0.0;
	if (torn.x > 0.0 && dl < 6.0) torn_near = max(torn_near, 1.0 - dl / 6.0);
	if (torn.y > 0.0 && dt < 6.0) torn_near = max(torn_near, 1.0 - dt / 6.0);
	if (torn.z > 0.0 && dr < 6.0) torn_near = max(torn_near, 1.0 - dr / 6.0);
	if (torn.w > 0.0 && db < 6.0) torn_near = max(torn_near, 1.0 - db / 6.0);
	torn_near = clamp(torn_near, 0.0, 1.0);
	float fibre = texture(detail_tex, (p + seed_off) / 512.0).r;
	// Loose fibres sticking out past a tear.
	float whisker = 0.0;
	if (torn_near > 0.0 && sdf < 0.0) {
		whisker = smoothstep(0.62, 0.8, fibre) * smoothstep(-3.5, 0.0, sdf) * 0.8 * (1.0 - step(1.0, corner));
	}

	// --- paper colour ----------------------------------------------------------------------
	vec4 det = texture(detail_tex, (p * 0.9 + seed_off) / 512.0);
	vec4 det_big = texture(detail_tex, (p * 0.35 + seed_off.yx) / 512.0);
	vec3 col = paper_color;
	col = mix(col, age_color, clamp((det_big.b - 0.35) * mottle * 1.6, 0.0, 1.0));
	col *= 1.0 + (det.r - 0.5) * 0.09 * grain;
	col *= 1.0 + (texture(detail_tex, (p * 3.1 + seed_off) / 512.0).a - 0.5) * 0.05 * grain;
	// Foxing: rust spots, denser towards the edges where damp gets in first.
	float edge_near = 1.0 - smoothstep(0.0, 0.35 * min(sheet_size.x, sheet_size.y), sdf);
	float fox = det_big.g * foxing * (0.45 + 0.9 * edge_near);
	col = mix(col, vec3(0.62, 0.40, 0.2), clamp(fox, 0.0, 0.85));

	// Printed ruling (under the stains and the light).
	if (ruling == 1) {
		vec2 q = p - rule_offset;
		float g = max(grid_line(q.x, rule_step, 1.0), grid_line(q.y, rule_step, 1.0));
		col = mix(col, col * rule_color * 1.15, g * 0.55);
	} else if (ruling == 2) {
		float g = grid_line(p.y - rule_offset.y, rule_step, 1.2) * step(rule_offset.y - rule_step * 0.5, p.y);
		col = mix(col, col * rule_color * 1.15, g * 0.5);
		float m = 1.0 - smoothstep(0.6, 1.4, abs(p.x - rule_offset.x));
		col = mix(col, vec3(0.78, 0.35, 0.33), m * 0.5);
	}

	// A tide-line stain: pale inside, a darker wavy ring where the water dried.
	if (stain.w > 0.0) {
		vec2 sc = stain.xy * sheet_size;
		vec2 v = p - sc;
		float ang = atan(v.y, v.x);
		float wob = n1(ang * 0.35 + 0.5, fract(seed * 0.7)) - 0.5;
		float r = length(v) / (stain.z * (1.0 + wob * 0.22));
		float ring = exp(-pow((r - 1.0) / 0.035, 2.0)) + 0.5 * exp(-pow((r - 0.93) / 0.05, 2.0));
		float fill = (1.0 - smoothstep(0.85, 1.0, r)) * 0.25;
		col = mix(col, col * vec3(0.78, 0.62, 0.42), clamp((ring * 0.7 + fill) * stain.w, 0.0, 1.0));
	}

	// Handled rim: darker and greyer towards the edge, unevenly.
	float rim = 1.0 - smoothstep(0.0, 22.0, sdf);
	col = mix(col, col * vec3(0.78, 0.7, 0.58), rim * edge_age * (0.55 + 0.6 * det_big.b));
	// The fresh fibres of a tear are paler than the aged surface.
	col = mix(col, vec3(0.985, 0.97, 0.93), torn_near * torn_near * 0.75);

	// --- relief and light ------------------------------------------------------------------
	vec4 cr = texture(crumple_tex, (p * crumple_scale + seed_off) / 1024.0);
	vec3 n = vec3((cr.xy * 2.0 - 1.0) * crumple, 1.0);
	float crease_line = 0.0;
	n.xy += fold_tilt(fold_a, p, crease_line);
	n.xy += fold_tilt(fold_b, p, crease_line);
	// A lifted corner rises towards the bottom right.
	float lift = curl * pow(clamp(suv.x * suv.y, 0.0, 1.0), 3.0);
	n.xy -= normalize(vec2(1.0, 1.0)) * curl * 0.03 * pow(clamp(suv.x * suv.y, 0.0, 1.0), 2.0);
	// A bound page curves down into the gutter.
	float gut = 0.0;
	if (gutter != 0.0) {
		float gd = gutter < 0.0 ? p.x : sheet_size.x - p.x;
		gut = exp(-gd / gutter_width);
		n.x += gutter * gut * 0.5;
	}
	n = normalize(n);
	vec3 L = normalize(light_dir);
	float lit = dot(n, L) - L.z;
	// Paper is already near white: faces turned to the light brighten less than others darken.
	float shade = 1.0 + (lit > 0.0 ? lit * 0.5 : lit) * light_amount;
	shade += (cr.a - 0.5) * 0.35 * crumple;
	shade -= crease_line * 0.12;
	shade *= 1.0 - gut * 0.4;
	// Light falls off a little across the sheet (top left brighter).
	shade *= 1.03 - 0.07 * dot(clamp(suv, 0.0, 1.0), vec2(0.5, 0.5));
	col *= shade;
	// Fibre whiskers are the paper's own pale colour.
	col = mix(col, vec3(0.96, 0.94, 0.89), whisker * (1.0 - a_sheet));
	float a = max(a_sheet, whisker);

	// --- the cast shadow -----------------------------------------------------------------
	vec2 so = shadow_offset + vec2(lift, lift * 1.2) * 0.5;
	float sd = box_sdf(p - so, sheet_size, 6.0);
	float soft = shadow_soft * (1.0 + lift * 0.04);
	// Penumbra falls off outward from the offset sheet; almost nothing shows up-light.
	float sh = shadow_alpha * pow(1.0 - smoothstep(-soft * 0.25, soft, sd), 1.6);
	// A thin contact shadow right under the edge.
	sh += shadow_alpha * 0.35 * (1.0 - smoothstep(-0.5, 1.8, box_sdf(p - shadow_offset * 0.3, sheet_size, 5.0)));
	sh = clamp(sh, 0.0, 0.9) * opacity;

	float out_a = a * opacity + sh * (1.0 - a * opacity);
	vec3 out_c = col * a * opacity / max(out_a, 0.0001);
	COLOR = vec4(out_c, out_a);
}
