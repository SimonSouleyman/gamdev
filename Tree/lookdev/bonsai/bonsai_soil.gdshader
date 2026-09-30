shader_type spatial;
render_mode cull_disabled;
// The bonsai's soil surface (shed/bonsai_view.gd): fine gravel with low cushions of moss
// growing in soft patches, most of them close to the trunk's foot. Wet soil is dark and glossy;
// dry soil is pale and its top cracks, the moss goes dull and yellow (0.8, C4 of the 0.7 check:
// the two must read apart at phone size). UV is the pot-local xz position (metres) times 3.

uniform sampler2D gravel : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D gravel_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D moss : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D moss_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float gravel_tiling = 4.0;
uniform float moss_tiling = 6.0;
// 0 dry .. 1 soaked.
uniform float wet = 0.5;
// How much of the surface the moss covers (0..1), and a per-pot offset for its patches.
uniform float moss_cover = 0.4;
uniform vec2 moss_seed = vec2(0.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Distance to the nearest edge between random cells: thin lines where the dry top cracks.
float cracks(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 c = o + vec2(hash(i + o), hash(i + o + 17.3)) * 0.8 + 0.1;
			float d = length(c - f);
			if (d < d1) {
				d2 = d1;
				d1 = d;
			} else if (d < d2) {
				d2 = d;
			}
		}
	}
	return d2 - d1;
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec2 p = UV / 3.0;
	float n = noise(p * 38.0 + moss_seed) * 0.6 + noise(p * 90.0 + moss_seed * 1.7) * 0.3 + noise(p * 260.0) * 0.1;
	// More moss near the trunk's foot, patches further out.
	float near = 1.0 - smoothstep(0.02, 0.09, length(p));
	float m = smoothstep(0.62 - moss_cover * 0.35, 0.7 - moss_cover * 0.35, n + near * 0.25);
	vec3 g = texture(gravel, UV * gravel_tiling / 3.0).rgb;
	// Akadama and grit: warm brown-grey.
	g *= vec3(0.95, 0.82, 0.68);
	vec3 mo = texture(moss, UV * moss_tiling / 3.0).rgb * vec3(0.95, 1.05, 0.8);
	// Dry: the grit bleaches to a pale dusty tan, the moss shrinks back and dries to straw.
	float dry = 1.0 - smoothstep(0.1, 0.45, wet);
	m *= 1.0 - dry * 0.45;
	float lum = dot(g, vec3(0.3, 0.5, 0.2));
	g = mix(g, vec3(0.9, 0.8, 0.64) * (0.75 + 0.6 * lum), dry * 0.85);
	float mlum = dot(mo, vec3(0.3, 0.5, 0.2));
	mo = mix(mo, vec3(0.78, 0.68, 0.38) * (0.6 + 0.8 * mlum), dry * 0.75);
	vec3 col = mix(g, mo, m);
	// Its top cracks into small plates (not over the moss).
	float crack = 1.0 - smoothstep(0.02, 0.07, cracks(p * 26.0 + moss_seed));
	col *= 1.0 - crack * dry * (1.0 - m) * 0.7;
	// Soaked: dark and glossy.
	float soak = smoothstep(0.35, 0.9, wet);
	col *= mix(1.0, 0.42, soak * (1.0 - m * 0.4));
	ALBEDO = col;
	NORMAL_MAP = mix(texture(gravel_normal, UV * gravel_tiling / 3.0).rgb, texture(moss_normal, UV * moss_tiling / 3.0).rgb, m);
	ROUGHNESS = mix(mix(1.0, 0.18, soak), 1.0, m * 0.8);
	SPECULAR = mix(0.25, 0.7, soak);
}
