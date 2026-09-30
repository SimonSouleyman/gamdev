shader_type spatial;
// Bark: with a texture set (CC0 photo bark later) it tiles along the wood's UVs; without one,
// procedural vertical fissures from noise stand in, with a normal from the same noise.

uniform sampler2D bark_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_roughness : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_textures = false;
// The photo bark is a pale grey; linden bark is darker and browner.
uniform vec3 texture_tint : source_color = vec3(0.62, 0.52, 0.42);
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform vec3 light_bark : source_color = vec3(0.58, 0.53, 0.47);
uniform vec3 dark_bark : source_color = vec3(0.24, 0.2, 0.16);
uniform float normal_strength = 1.0;
uniform float near_fade = 0.0;
uniform vec3 tint_mul : source_color = vec3(1.0);
uniform float rim_strength = 0.0;
// Roots underground glow faintly so they read in the dark.
uniform vec3 glow : source_color = vec3(0.0);

float fissure(vec2 uv) {
	// Long ridges along the wood, broken up by a second noise.
	float n = texture(noise, uv * vec2(3.0, 0.45)).r;
	float m = texture(noise, uv * vec2(9.0, 1.6) + vec2(0.37, 0.11)).r;
	return smoothstep(0.35, 0.75, n * 0.7 + m * 0.3);
}

uniform float sway_strength = 1.0;

void vertex() {
	// Slow limb sway, growing with height, so the crown moves as a whole in the wind.
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float limb = 0.02 * pow(max(w.y, 0.0), 1.2) * sway_strength;
	vec2 s = vec2(sin(TIME * 0.6 + w.x * 0.2), cos(TIME * 0.5 + w.z * 0.2)) * limb;
	VERTEX += inverse(mat3(MODEL_MATRIX)) * vec3(s.x, 0.0, s.y);
}

void fragment() {
	if (near_fade > 0.0) {
		float keep = smoothstep(near_fade * 0.6, near_fade, length(VERTEX));
		float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
		if (dither > keep) { discard; }
	}
	if (use_textures) {
		ALBEDO = texture(bark_albedo, UV).rgb * texture_tint;
		NORMAL_MAP = texture(bark_normal, UV).rgb;
		ROUGHNESS = texture(bark_roughness, UV).r;
	} else {
		float f = fissure(UV);
		float moss = smoothstep(0.62, 0.8, texture(noise, UV * vec2(1.3, 0.3) + vec2(0.5)).r);
		vec3 col = mix(dark_bark, light_bark, f);
		col = mix(col, vec3(0.3, 0.36, 0.16), moss * 0.35);
		ALBEDO = col;
		float e = 0.004;
		float dx = fissure(UV + vec2(e, 0.0)) - fissure(UV - vec2(e, 0.0));
		float dy = fissure(UV + vec2(0.0, e * 4.0)) - fissure(UV - vec2(0.0, e * 4.0));
		NORMAL_MAP = normalize(vec3(-dx * 3.0 * normal_strength, -dy * 1.0 * normal_strength, 1.0)) * 0.5 + 0.5;
		ROUGHNESS = mix(0.95, 0.8, f);
	}
	// Per-species bark tint from vertex colours (white where a mesh has none).
	ALBEDO *= COLOR.rgb * tint_mul;
	// Thin twigs (high in the UV repeat count, i.e. small radius) are greyer, not orange.
	ALBEDO = mix(ALBEDO, vec3(dot(ALBEDO, vec3(0.33))) * 0.8, 0.35);
	// Greying bark (0.7, a twig the tree marks for pruning or one that died back in the shade):
	// the vertex alpha below 1 turns the wood a weathered silver grey (1 where a mesh has none).
	float grey = 1.0 - COLOR.a;
	if (grey > 0.001) {
		float l = dot(ALBEDO, vec3(0.33));
		ALBEDO = mix(ALBEDO, vec3(l * 1.25 + 0.1) * vec3(0.97, 0.98, 1.0), grey);
	}
	RIM = rim_strength;
	RIM_TINT = 0.5;
	SPECULAR = 0.3;
	EMISSION = glow * ALBEDO;
}
