shader_type spatial;
// Procedural stone: triplanar layered noise for mineral colour and grain, darker crevices from
// the mesh's cavity (COLOR.r), lichen and moss on the upward faces (surface stones), a damp
// darker underside. No textures beyond one tiling noise.

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform vec3 stone_a : source_color = vec3(0.46, 0.44, 0.41);
uniform vec3 stone_b : source_color = vec3(0.33, 0.31, 0.29);
uniform vec3 vein : source_color = vec3(0.62, 0.6, 0.56);
uniform vec3 moss : source_color = vec3(0.29, 0.36, 0.13);
uniform vec3 lichen : source_color = vec3(0.7, 0.72, 0.55);
uniform float moss_amount = 0.6;
uniform float grain_scale = 1.6;
uniform float wetness = 0.0;
// Underground void: no sky light, so the stone shows by a faint fill and a cool rim glow,
// like an asteroid against space.
uniform float void_fill = 0.0;
uniform vec3 rim_colour : source_color = vec3(0.45, 0.55, 0.7);

varying vec3 obj_pos;
varying vec3 obj_n;

void vertex() {
	obj_pos = VERTEX;
	obj_n = NORMAL;
}

float tri(vec3 p, vec3 n, float s) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	return texture(noise, p.yz * s).r * w.x + texture(noise, p.xz * s).r * w.y + texture(noise, p.xy * s).r * w.z;
}

void fragment() {
	vec3 p = obj_pos;
	vec3 n = normalize(obj_n);
	float big = tri(p, n, 0.18 * grain_scale);
	float mid = tri(p + 3.1, n, 1.4 * grain_scale);
	float fine = tri(p + 7.7, n, 6.0 * grain_scale);
	float cavity = COLOR.r;
	vec3 col = mix(stone_b, stone_a, smoothstep(0.3, 0.7, big));
	col *= 0.9 + 0.2 * mid;
	// Thin pale mineral veins.
	float v = 1.0 - abs(mid * 2.0 - 1.0);
	col = mix(col, vein, smoothstep(0.93, 0.99, v) * 0.5);
	col *= 0.93 + 0.14 * fine;
	// Moss sits in the sheltered upward crevices, lichen as crusty spots on exposed tops.
	float up = smoothstep(0.2, 0.8, n.y);
	float moss_mask = smoothstep(0.55, 0.75, big + (1.0 - cavity) * 0.35 + up * 0.25) * up * moss_amount;
	col = mix(col, moss * (0.8 + 0.4 * fine), moss_mask);
	float lichen_mask = smoothstep(0.78, 0.85, tri(p + 13.0, n, 3.0 * grain_scale)) * up * moss_amount;
	col = mix(col, lichen, lichen_mask * 0.7);
	// Crevices collect dirt and shade.
	col *= mix(0.55, 1.05, cavity);
	float wet = clamp(wetness + smoothstep(0.1, -0.6, n.y) * 0.4, 0.0, 1.0);
	col *= mix(1.0, 0.65, wet);
	ALBEDO = col;
	if (void_fill > 0.0) {
		float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
		EMISSION = col * void_fill * (0.5 + 0.5 * cavity) + rim_colour * fres * void_fill * 2.5 * cavity;
	}
	ROUGHNESS = mix(0.92, 0.45, wet) - moss_mask * 0.0;
	SPECULAR = 0.35;
	AO = mix(0.35, 1.0, cavity);
	AO_LIGHT_AFFECT = 0.25;
	// Grain bump from the fine noise.
	float h1 = tri(p + vec3(0.01, 0, 0) + 7.7, n, 6.0 * grain_scale);
	float h2 = tri(p + vec3(0, 0.01, 0) + 7.7, n, 6.0 * grain_scale);
	NORMAL_MAP = normalize(vec3((fine - h1) * 3.0, (fine - h2) * 3.0, 1.0)) * 0.5 + 0.5;
	NORMAL_MAP_DEPTH = 0.5;
}
