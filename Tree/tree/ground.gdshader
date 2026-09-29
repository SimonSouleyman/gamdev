shader_type spatial;
// The meadow floor under the grass: patches of green, dry and earthy tones from world-space noise,
// so the ground never tiles visibly. A CC0 ground texture can be layered in later.

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D ground_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_texture = false;
// The phone renderer shows this ground brighter and more saturated: calmer there.
uniform float brightness = 1.0;
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
// The meadow's patches (MeadowMap, shared with the grass clumps): r lush, g tall grass, b path,
// a colour drift; meadow_half metres each way.
uniform sampler2D meadow_map : filter_linear, repeat_disable;
uniform float meadow_half = 48.0;
uniform bool use_meadow = false;
uniform vec3 trodden : source_color = vec3(0.47, 0.47, 0.3);
// Where the meadow meets the forest (the clearing grows with the tree).
uniform float edge = 18.0;
// Calmer greens: 1 as is, less toward grey.
uniform float saturation = 1.0;

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
	vec4 m = vec4(0.5, 0.0, 0.0, 0.5);
	if (use_meadow) {
		m = texture(meadow_map, p / (2.0 * meadow_half) + 0.5);
		col = mix(dry, lush, smoothstep(0.2, 0.8, m.r));
		col *= mix(vec3(1.04, 1.0, 0.93), vec3(0.96, 1.0, 1.05), m.a);
	}
	col = mix(col, earth, smoothstep(0.65, 0.9, mid) * 0.3);
	col *= 0.9 + 0.2 * fine;
	if (use_texture) {
		col *= texture(ground_albedo, p * 0.5).rgb * 1.6 * brightness;
	}
	// A trodden path: pale flattened grass with bare earth showing through in spots.
	float path = smoothstep(0.1, 0.9, m.b) * (0.5 + 0.8 * fine);
	col = mix(col, trodden * (0.75 + 0.4 * fine) * brightness, clamp(path * 0.28, 0.0, 0.36));
	if (use_shade) {
		vec2 suv = p / (2.0 * shade_half) + 0.5;
		float inside = step(0.0, suv.x) * step(suv.x, 1.0) * step(0.0, suv.y) * step(suv.y, 1.0);
		float s = texture(shade_map, clamp(suv, 0.0, 1.0)).r * inside;
		// A ragged, soft edge instead of a rug's (0.6.1 review): the shade frays into the meadow.
		float ragged = texture(noise, p * 0.23 + vec2(0.61, 0.17)).r;
		float sh = s + (ragged - 0.5) * 0.5 * smoothstep(0.02, 0.2, s) + (fine - 0.5) * 0.12;
		float k = smoothstep(0.14, 0.62, sh);
		// Leaf litter only where the shade is deep; lighter shade is darker, mossy meadow floor.
		float deep = smoothstep(0.45, 0.85, sh + (mid - 0.5) * 0.25);
		vec3 shaded = col * vec3(0.8, 0.86, 0.76);
		vec3 floor_col = mix(litter, lush * 0.7, 0.25) * (0.85 + 0.3 * fine);
		if (use_floor) {
			floor_col = texture(floor_albedo, p * 0.45).rgb * mix(vec3(0.72, 0.74, 0.58), vec3(0.55, 0.55, 0.46), s);
		}
		vec3 under = mix(shaded, floor_col, deep * 0.9);
		// Dappled light: flecks of sun through the crown, drifting a little in the wind.
		vec2 wob = vec2(sin(TIME * 0.45), cos(TIME * 0.37)) * 0.06;
		float fleck = smoothstep(0.56, 0.74, texture(noise, p * 0.85 + wob + vec2(0.3, 0.9)).r);
		under *= 0.94 + 0.4 * fleck * (1.0 - 0.4 * deep);
		col = mix(col, under, k);
	}
	// Shade under the forest edge: the ground darkens toward the trees.
	col *= mix(1.0, 0.4, smoothstep(edge - 2.0, edge + 3.0, length(p)));
	ALBEDO = mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, saturation);
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
