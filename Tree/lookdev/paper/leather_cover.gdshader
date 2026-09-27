shader_type canvas_item;
// A leather book cover: pebbled grain lit by the same light as the paper, darker mottling,
// corners and edges rubbed pale, a stitched border, and a soft shadow on what it lies on.
// Drawn like paper_sheet: a StyleBoxTexture with expand margins = pad.

uniform vec2 sheet_size = vec2(640.0, 1200.0);
uniform float pad = 24.0;
uniform vec3 leather_color : source_color = vec3(0.34, 0.2, 0.12);
uniform sampler2D leather_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 light_dir = vec3(-0.45, -0.6, 0.66);
uniform float corner_radius = 16.0;
uniform float stitch_inset = 11.0;
uniform float spine_x = -1.0;      // x (px) of a spine crease down the middle; < 0 = none
uniform float shadow_alpha = 0.5;

float box_sdf(vec2 p, vec2 size, float r) {
	vec2 q = abs(p - size * 0.5) - size * 0.5 + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
	vec2 full = sheet_size + 2.0 * pad;
	vec2 p = UV * full - pad;
	float sdf = -box_sdf(p, sheet_size, corner_radius);
	float wob = texture(detail_tex, p / 300.0).b - 0.5;
	sdf += wob * 1.2;
	float a = clamp(sdf + 0.5, 0.0, 1.0);

	vec4 g = texture(leather_tex, p / 380.0);
	vec3 n = normalize(vec3((g.xy * 2.0 - 1.0) * 0.7, 1.0));
	// The boards bulge a little: the rim rolls away from the light at the right and bottom.
	float rim_d = clamp(sdf / 10.0, 0.0, 1.0);
	vec2 to_edge = normalize(p - sheet_size * 0.5 + 0.001);
	n.xy += to_edge * (1.0 - rim_d) * 0.5;
	if (spine_x >= 0.0) {
		float d = p.x - spine_x;
		n.x += sign(d) * exp(-abs(d) / 5.0) * 0.8;
	}
	n = normalize(n);
	vec3 L = normalize(light_dir);
	float shade = 1.0 + (dot(n, L) - L.z) * 1.1;
	float mott = texture(detail_tex, p / 700.0).b;
	vec3 col = leather_color * mix(0.72, 1.12, mott);
	col *= 0.9 + 0.2 * g.a;
	// Rubbed: the high points and the edges wear to a paler, drier brown.
	float wear = (1.0 - smoothstep(0.0, 14.0, sdf)) * 0.6 + smoothstep(0.62, 0.9, g.a) * 0.25;
	wear *= 0.6 + 0.8 * texture(detail_tex, p / 120.0).b;
	col = mix(col, vec3(0.55, 0.4, 0.28), clamp(wear, 0.0, 0.7));
	col *= shade;
	// Stitches: short thread dashes on a pressed groove.
	float sd = abs(sdf - stitch_inset);
	float groove = exp(-sd / 1.2);
	col *= 1.0 - groove * 0.35;
	float along = (abs(p.x - sheet_size.x * 0.5) > sheet_size.x * 0.5 - stitch_inset - 1.0) ? p.y : p.x;
	float dash = step(0.45, fract(along / 11.0));
	float thread = (1.0 - smoothstep(0.8, 1.6, sd)) * dash;
	vec3 thread_col = vec3(0.78, 0.68, 0.48) * (0.85 + 0.3 * (1.0 - fract(along / 11.0)));
	col = mix(col, thread_col * shade, thread * 0.9);

	float sh = shadow_alpha * (1.0 - smoothstep(-6.0, 16.0, box_sdf(p - vec2(4.0, 8.0), sheet_size, corner_radius)));
	float out_a = a + sh * (1.0 - a);
	COLOR = vec4(col * a / max(out_a, 0.0001), out_a);
}
