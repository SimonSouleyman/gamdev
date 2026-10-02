shader_type spatial;
// The bird's nest (0.8.2.4): vertex colours carry the woven twigs, grass, mud and moss; the bark
// photo adds the fine grain. It sways exactly like bark.gdshader, so it stays on its branch.

uniform sampler2D bark_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform bool use_textures = false;
uniform float sway_strength = 1.0;
uniform float rim_strength = 0.12;

void vertex() {
	// The same limb sway as the bark (keep in step with bark.gdshader).
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float limb = 0.02 * pow(max(w.y, 0.0), 1.2) * sway_strength;
	vec2 s = vec2(sin(TIME * 0.6 + w.x * 0.2), cos(TIME * 0.5 + w.z * 0.2)) * limb;
	VERTEX += inverse(mat3(MODEL_MATRIX)) * vec3(s.x, 0.0, s.y);
}

void fragment() {
	// COLOR.a: 1 for wood and grass (full grain), near 0 for the smooth eggs.
	float grain = COLOR.a;
	vec3 col = COLOR.rgb;
	if (use_textures) {
		float l = dot(texture(bark_albedo, UV).rgb, vec3(0.333));
		// The photo's mean is about 0.24 (linear): keep the colour, take the grain.
		col *= mix(1.0, clamp(l * 4.2, 0.35, 1.7), grain * 0.8);
		NORMAL_MAP = texture(bark_normal, UV).rgb;
		NORMAL_MAP_DEPTH = grain;
	} else {
		float n = texture(noise, UV * 0.5).r;
		col *= mix(1.0, 0.6 + n * 0.8, grain);
	}
	// Fibres along the twigs and grass (UV.y runs along them).
	// Two sets crossing at a slant, as woven stuff does.
	float f1 = texture(noise, vec2(UV.x * 3.0 + UV.y * 0.6, UV.y * 0.25)).r;
	float f2 = texture(noise, vec2(UV.x * 3.0 - UV.y * 0.6 + 0.37, UV.y * 0.25 + 0.21)).r;
	float fib = max(f1, f2);
	col *= mix(1.0, 0.3 + smoothstep(0.25, 0.75, fib) * 1.1, grain);
	// A little greyer, as the bark is (bark.gdshader).
	col = mix(col, vec3(dot(col, vec3(0.33))) * 0.9, 0.25 * grain);
	// Brown speckles on the eggs.
	float sp = smoothstep(0.7, 0.76, texture(noise, UV * 3.0).r);
	col = mix(col, vec3(0.32, 0.22, 0.14), sp * (1.0 - grain) * 0.8);
	ALBEDO = col;
	ROUGHNESS = mix(0.55, 0.97, grain);
	SPECULAR = mix(0.4, 0.25, grain);
	RIM = rim_strength;
	RIM_TINT = 0.5;
}
