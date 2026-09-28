shader_type spatial;
// The meadow floor under the grass: patches of green, dry and earthy tones from world-space noise,
// so the ground never tiles visibly. A CC0 ground texture can be layered in later.

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D ground_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;
uniform vec3 lush : source_color = vec3(0.38, 0.54, 0.19);
uniform vec3 dry : source_color = vec3(0.42, 0.46, 0.2);
uniform vec3 earth : source_color = vec3(0.27, 0.24, 0.15);
// The living clearing: under the crown's shade (Clearing.shade_map, 0 sun to 1 deep shade,
// shade_half metres each way) the meadow floor turns to leaf litter.
uniform sampler2D shade_map : filter_linear, repeat_disable;
uniform float shade_half = 16.0;
uniform bool use_shade = false;
uniform sampler2D floor_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_floor = false;
uniform vec3 litter : source_color = vec3(0.3, 0.24, 0.14);

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
	if (use_shade) {
		vec2 suv = p / (2.0 * shade_half) + 0.5;
		float inside = step(0.0, suv.x) * step(suv.x, 1.0) * step(0.0, suv.y) * step(suv.y, 1.0);
		float s = texture(shade_map, clamp(suv, 0.0, 1.0)).r * inside;
		float k = smoothstep(0.12, 0.55, s + (mid - 0.5) * 0.2);
		vec3 floor_col = mix(litter, lush * 0.7, 0.25) * (0.85 + 0.3 * fine);
		if (use_floor) {
			// Brown leaf litter, a little greener and darker where the shade is lighter.
			floor_col = texture(floor_albedo, p * 0.45).rgb * mix(vec3(0.72, 0.74, 0.58), vec3(0.55, 0.55, 0.46), s);
		}
		col = mix(col, floor_col, k * 0.85);
	}
	// Shade under the forest edge: the ground darkens toward the trees.
	col *= mix(1.0, 0.4, smoothstep(16.0, 21.0, length(p)));
	ALBEDO = col;
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
