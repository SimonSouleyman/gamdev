shader_type spatial;
// The meadow floor under the grass: patches of green, dry and earthy tones from world-space noise,
// so the ground never tiles visibly. A CC0 ground texture can be layered in later.

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D ground_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;
uniform vec3 lush : source_color = vec3(0.16, 0.26, 0.08);
uniform vec3 dry : source_color = vec3(0.27, 0.3, 0.13);
uniform vec3 earth : source_color = vec3(0.27, 0.24, 0.15);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world_pos.xz;
	float big = texture(noise, p * 0.012).r;
	float mid = texture(noise, p * 0.07 + vec2(0.3, 0.7)).r;
	float fine = texture(noise, p * 0.6).r;
	vec3 col = mix(lush, dry, smoothstep(0.3, 0.85, big) * 0.45);
	col = mix(col, earth, smoothstep(0.65, 0.9, mid) * 0.3);
	col *= 0.9 + 0.2 * fine;
	if (use_texture) {
		col *= texture(ground_albedo, p * 0.5).rgb * 1.6;
	}
	ALBEDO = col;
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
