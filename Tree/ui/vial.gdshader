shader_type canvas_item;
// The life force vial (0.8.2.5, specs/wish-compass-vial.md item 4): a round glass flask with green
// liquid, a cork and a lip, painted in one pass so it works on the Compatibility renderer. Units
// are the rect's width (x 0..1, y 0..aspect). The pencil mark (a calm day's fill) is drawn on the
// glass. The liquid's level, tilt and waves come from ui/vial.gd (a damped spring).

uniform float aspect = 1.3;
uniform float level : hint_range(0.0, 1.0) = 0.5;
uniform float mark : hint_range(0.0, 1.0) = 0.6;
uniform float mark_alpha : hint_range(0.0, 1.0) = 0.0;
uniform float tilt = 0.0;
uniform float wave = 0.004;
uniform float time = 0.0;
// 0 by day, 1 underground: the liquid glows a little more against the dark soil.
uniform float night : hint_range(0.0, 1.0) = 0.0;

const vec2 C = vec2(0.5, 0.86);
const float R = 0.41;
const float WALL = 0.026;
const vec3 INK = vec3(0.16, 0.11, 0.07);

float sd_box(vec2 p, vec2 b) {
	vec2 d = abs(p) - b;
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

float smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

// The glass's outer shape: the bulb, the neck and the lip, melted together.
float sd_glass(vec2 p) {
	float bulb = length(p - C) - R;
	float neck = sd_box(p - vec2(0.5, 0.40), vec2(0.098, 0.13)) - 0.004;
	float lip = sd_box(p - vec2(0.5, 0.285), vec2(0.122, 0.022)) - 0.014;
	return min(smin(bulb, neck, 0.07), lip);
}

// The liquid's surface height (y grows downward) for a level 0..1 inside the bulb.
float level_y(float l) {
	float top = C.y - (R - WALL) * 0.86;
	float bottom = C.y + (R - WALL) * 0.97;
	return mix(bottom, top, l);
}

vec4 over(vec4 under, vec4 top) {
	float a = top.a + under.a * (1.0 - top.a);
	vec3 c = (top.rgb * top.a + under.rgb * under.a * (1.0 - top.a)) / max(a, 1e-4);
	return vec4(c, a);
}

void fragment() {
	vec2 p = vec2(UV.x, UV.y * aspect);
	float px = fwidth(p.x) * 1.2;
	vec4 col = vec4(0.0);

	// A soft shadow on the paper/meadow behind, down and to the right (light from the upper left).
	float sh = length((p - C - vec2(0.035, 0.05)) * vec2(1.0, 1.15)) - R * 0.98;
	col = over(col, vec4(0.05, 0.03, 0.02, 0.32 * (1.0 - smoothstep(-0.04, 0.05, sh))));

	float g = sd_glass(p);
	float inner = g + WALL;
	vec2 n = (p - C) / R;
	float rr = length(n);
	float z = sqrt(max(0.0, 1.0 - rr * rr));

	if (g < px) {
		// The cavity: a faint tint and a darker edge where the glass is seen at a slant.
		float edge = 1.0 - smoothstep(0.0, 0.10, -inner);
		vec4 glass = vec4(mix(vec3(0.70, 0.78, 0.74), vec3(0.20, 0.26, 0.22), 0.35 + 0.65 * edge), 0.20 + 0.42 * edge);
		col = over(col, glass);

		// The liquid: below a gently tilted surface, seen a little from above (its surface an ellipse).
		float y0 = level_y(level);
		float wv = wave * (sin(p.x * 23.0 + time * 2.6) * 0.6 + sin(p.x * 41.0 - time * 3.7) * 0.4);
		float surf = y0 + tilt * (p.x - 0.5) + wv;
		float dy = y0 - C.y;
		float hw = sqrt(max(0.0, (R - WALL) * (R - WALL) - dy * dy));
		float ell = length(vec2((p.x - 0.5) / max(hw, 1e-3), (p.y - surf) / max(hw * 0.2, 1e-3)));
		float in_cavity = 1.0 - smoothstep(-px, px, inner);
		float below = smoothstep(surf - px, surf + px, p.y);
		float on_top = (1.0 - smoothstep(1.0 - px / max(hw * 0.2, 1e-3), 1.0, ell)) * step(0.004, level);
		float liquid = in_cavity * max(below, on_top) * step(0.002, level);
		if (liquid > 0.0) {
			// Deep green, brighter in its round middle and glowing a little low down.
			float depth = clamp((p.y - surf) / (R * 1.6), 0.0, 1.0);
			vec3 deep = vec3(0.03, 0.20, 0.07);
			vec3 mid = vec3(0.16, 0.56, 0.18);
			vec3 core = vec3(0.50, 0.90, 0.36);
			float glow = smoothstep(0.95, 0.0, length((p - C - vec2(-0.03, 0.09)) / R));
			vec3 lc = mix(deep, mid, 0.25 + 0.75 * z);
			lc = mix(lc, core, glow * (0.42 + 0.18 * night));
			lc *= 1.0 - depth * 0.25;
			// Slow swirls in the liquid.
			lc *= 0.9 + 0.2 * noise(p * vec2(9.0, 13.0) + vec2(time * 0.05, -time * 0.08));
			// Drifting bright motes, very slow.
			for (int i = 0; i < 5; i++) {
				float fi = float(i);
				float bx = 0.5 + (hash(vec2(fi, 3.1)) - 0.5) * 0.5;
				float by = C.y + R * 0.8 - fract(time * (0.025 + 0.02 * hash(vec2(fi, 7.7))) + hash(vec2(fi, 1.3))) * R * 1.7;
				float bd = length(p - vec2(bx + 0.01 * sin(time + fi), by));
				float vis = step(surf + 0.02, by);
				lc += vec3(0.5, 0.9, 0.5) * (1.0 - smoothstep(0.004, 0.010, bd)) * 0.35 * vis;
			}
			// The surface: lighter, the far half a touch darker, a bright meniscus line in front.
			if (below < 0.5) {
				float far = step(p.y, surf);
				lc = mix(vec3(0.36, 0.74, 0.30), vec3(0.24, 0.58, 0.22), far * 0.6);
				lc += vec3(0.25, 0.3, 0.15) * (1.0 - smoothstep(0.0, 0.25, abs(ell - 0.9))) * (1.0 - far);
			}
			float men = 1.0 - smoothstep(0.0, 0.008, abs(p.y - surf - hw * 0.2 * sqrt(max(0.0, 1.0 - pow((p.x - 0.5) / max(hw, 1e-3), 2.0)))));
			lc += vec3(0.25, 0.35, 0.2) * men * 0.6;
			col = over(col, vec4(lc, 0.94 * liquid));
		}

		// The pencil mark: a calm day's fill, a short sketchy graphite line on the glass.
		if (mark_alpha > 0.0) {
			float my = level_y(mark) + 0.0035 * sin(p.x * 57.0 + 1.3) + tilt * 0.0;
			float mdy = my - C.y;
			float mhw = sqrt(max(0.0, (R - WALL) * (R - WALL) - mdy * mdy)) * 0.78;
			float along = smoothstep(0.5 - mhw - 0.01, 0.5 - mhw + 0.02, p.x) * (1.0 - smoothstep(0.5 + mhw - 0.02, 0.5 + mhw + 0.01, p.x));
			float d = abs(p.y - my);
			float grain = 0.75 + 0.25 * noise(p * vec2(260.0, 40.0));
			float lw = max(0.008, px * 0.6);
			float line = (1.0 - smoothstep(lw, lw + px, d)) * along * grain;
			// Two short ticks at its ends, as a hand marks a level.
			float tick_l = (1.0 - smoothstep(lw, lw + px, abs(p.x - (0.5 - mhw) - (p.y - my) * 0.35))) * step(abs(p.y - my - 0.014), 0.02);
			float tick_r = (1.0 - smoothstep(lw, lw + px, abs(p.x - (0.5 + mhw) - (p.y - my) * 0.35))) * step(abs(p.y - my - 0.014), 0.02);
			float tick = max(tick_l, tick_r) * grain;
			line = max(line, tick);
			// Silvery graphite on the glass, with a dark edge so it reads on liquid and sky alike.
			float edge = (1.0 - smoothstep(lw + px * 0.6, lw + px * 1.8, d)) * along;
			col = over(col, vec4(0.08, 0.08, 0.10, 0.45 * max(edge, tick) * mark_alpha));
			col = over(col, vec4(0.80, 0.81, 0.84, 0.92 * line * mark_alpha));
		}

		// The glass wall: brighter where it turns away, an ink edge outside.
		float wall = smoothstep(-px, px, inner) * (1.0 - smoothstep(-px, px, g));
		col = over(col, vec4(mix(vec3(0.82, 0.90, 0.86), vec3(0.95, 0.98, 0.96), 0.5), 0.40 * wall));
		float outline = 1.0 - smoothstep(0.0, px * 1.6, abs(g + 0.002));
		col = over(col, vec4(INK, 0.55 * outline));
		// Highlights: a broad soft crescent upper left, a bright glint, a green caustic low right,
		// a long streak down the neck.
		float ang = atan(n.y, n.x);
		float cres = (1.0 - smoothstep(0.0, 0.07, abs(rr - 0.80))) * smoothstep(1.2, 0.0, abs(ang + 2.25));
		col = over(col, vec4(1.0, 1.0, 0.96, 0.42 * cres * step(-0.01, -g)));
		float glint = 1.0 - smoothstep(0.03, 0.055, length((p - C - vec2(-0.19, -0.21)) * vec2(1.0, 1.4)));
		col = over(col, vec4(1.0, 1.0, 0.98, 0.85 * glint));
		float glint2 = 1.0 - smoothstep(0.010, 0.022, length(p - C - vec2(-0.12, -0.27)));
		col = over(col, vec4(1.0, 1.0, 0.98, 0.7 * glint2));
		float caustic = (1.0 - smoothstep(0.0, 0.05, abs(rr - 0.84))) * smoothstep(0.9, 0.0, abs(ang - 0.75)) * step(0.05, level);
		col = over(col, vec4(0.70, 1.0, 0.55, 0.35 * caustic * step(-0.01, -g)));
		float streak = (1.0 - smoothstep(0.008, 0.016, abs(p.x - 0.435))) * smoothstep(0.30, 0.34, p.y) * (1.0 - smoothstep(0.48, 0.55, p.y));
		col = over(col, vec4(1.0, 1.0, 0.97, 0.55 * streak));
	}

	// The cork, pressed into the lip: speckled, lit from the left, its top a lighter oval.
	float cork_top = 0.075;
	float cork_bot = 0.315;
	float t = clamp((p.y - cork_top) / (cork_bot - cork_top), 0.0, 1.0);
	float cw = mix(0.118, 0.096, t);
	float cork = sd_box(p - vec2(0.5, (cork_top + cork_bot) * 0.5), vec2(cw, (cork_bot - cork_top) * 0.5)) - 0.012;
	if (cork < px) {
		float cx = (p.x - 0.5) / cw;
		vec3 cc = vec3(0.68, 0.50, 0.31);
		cc *= 0.72 + 0.38 * (1.0 - smoothstep(-0.7, 1.0, cx));
		float sp = noise(p * 90.0);
		cc *= 0.82 + 0.3 * sp;
		cc = mix(cc, vec3(0.30, 0.19, 0.10), smoothstep(0.62, 0.8, noise(p * 160.0 + 3.0)) * 0.7);
		float top = length(vec2((p.x - 0.5) / 0.12, (p.y - cork_top - 0.012) / 0.026));
		cc = mix(cc, vec3(0.80, 0.64, 0.44) * (0.85 + 0.25 * sp), (1.0 - smoothstep(0.85, 1.0, top)) * 0.8);
		// Where the lip grips it, a shadow.
		cc *= 1.0 - 0.35 * smoothstep(0.25, 0.30, p.y);
		float a = 1.0 - smoothstep(-px, px, cork);
		col = over(col, vec4(cc, a));
		float co = 1.0 - smoothstep(0.0, px * 1.6, abs(cork + 0.002));
		col = over(col, vec4(INK, 0.6 * co));
	}
	// The glass lip in front of the cork's foot.
	float lipd = sd_box(p - vec2(0.5, 0.29), vec2(0.122, 0.016)) - 0.012;
	if (lipd < px) {
		float a = 1.0 - smoothstep(-px, px, lipd);
		float lx = (p.x - 0.5) / 0.134;
		vec3 lc = mix(vec3(0.62, 0.72, 0.68), vec3(0.95, 0.99, 0.97), 1.0 - smoothstep(-0.9, 0.2, lx));
		col = over(col, vec4(lc, 0.75 * a));
		col = over(col, vec4(INK, 0.55 * (1.0 - smoothstep(0.0, px * 1.6, abs(lipd + 0.002)))));
	}
	// Twine wound under the lip.
	float tw = abs(p.y - 0.335 - 0.006 * sin(p.x * 60.0)) - 0.007;
	if (abs(p.x - 0.5) < 0.104 && tw < px) {
		col = over(col, vec4(0.48, 0.36, 0.22, 1.0 - smoothstep(-px, px, tw)));
	}
	COLOR = col;
}
