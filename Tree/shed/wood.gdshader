shader_type spatial;
// Weathered wooden boards for the shed: vertical planks with gaps, grain and a little moss.

varying vec3 wpos;
varying vec3 wnrm;

float hash(float n) { return fract(sin(n) * 43758.5453); }

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float a = hash(i.x + i.y * 57.0);
	float b = hash(i.x + 1.0 + i.y * 57.0);
	float c = hash(i.x + (i.y + 1.0) * 57.0);
	float d = hash(i.x + 1.0 + (i.y + 1.0) * 57.0);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	// Planks run vertically on walls, along x on the floor.
	vec2 p = abs(wnrm.y) > 0.7 ? wpos.xz : vec2(wpos.x + wpos.z, wpos.y);
	float plank = floor(p.x / 0.16);
	float u = fract(p.x / 0.16);
	float gap = smoothstep(0.0, 0.05, u) * smoothstep(1.0, 0.95, u);
	float grain = noise(vec2(p.x * 40.0, p.y * 3.0 + plank * 7.0));
	float tone = hash(plank) * 0.25;
	vec3 col = mix(vec3(0.22, 0.15, 0.09), vec3(0.42, 0.31, 0.2), 0.35 + tone + grain * 0.35);
	col *= mix(0.35, 1.0, gap);
	// Moss low on the outside.
	float moss = abs(wnrm.y) > 0.7 ? 0.0 : smoothstep(0.55, 0.9, noise(p * 3.0)) * smoothstep(0.6, 0.1, wpos.y - 0.0) * 0.5;
	col = mix(col, vec3(0.2, 0.3, 0.12), moss);
	ALBEDO = col * 0.6;
	ROUGHNESS = 0.9;
	SPECULAR = 0.15;
}
