shader_type spatial;
render_mode cull_disabled;
// The bonsai's soil surface (shed/bonsai_view.gd): fine gravel with low cushions of moss
// growing in soft patches, most of them close to the trunk's foot. Wet soil is darker and a
// little shiny. UV is the pot-local xz position (metres) times 3.

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
	vec3 col = mix(g, mo, m);
	col *= mix(1.0, 0.55, wet * (1.0 - m * 0.5));
	ALBEDO = col;
	NORMAL_MAP = mix(texture(gravel_normal, UV * gravel_tiling / 3.0).rgb, texture(moss_normal, UV * moss_tiling / 3.0).rgb, m);
	ROUGHNESS = mix(mix(0.95, 0.5, wet), 1.0, m);
	SPECULAR = 0.35;
}
