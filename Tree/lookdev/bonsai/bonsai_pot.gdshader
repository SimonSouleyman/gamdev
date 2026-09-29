shader_type spatial;
// The bonsai's pots (shed/bonsai_view.gd): stoneware with a glaze that runs thinner over the
// lip and edges (lighter, the clay showing through), pools darker toward the foot, and shows a
// fine mottling and iron speckles. The unglazed grey pot (glaze = 0) is plain speckled clay.
// COLOR.r: 0 at the foot .. 1 at the lip; COLOR.g: 1 on edges (lip, foot band), 0 elsewhere.

uniform vec3 glaze_colour : source_color = vec3(0.2, 0.3, 0.5);
uniform vec3 clay_colour : source_color = vec3(0.45, 0.36, 0.3);
uniform float glaze = 1.0;
uniform float glaze_roughness = 0.3;

varying vec3 local_pos;

float hash(vec3 p) {
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash(i), hash(i + vec3(1, 0, 0)), u.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), u.x), u.y);
	float b = mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), u.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), u.x), u.y);
	return mix(a, b, u.z);
}

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec3 p = local_pos * 60.0;
	float mottle = noise(p) * 0.6 + noise(p * 3.1) * 0.3 + noise(p * 9.0) * 0.1;
	float speck = step(0.93, hash(floor(local_pos * 900.0)));
	float t = COLOR.r;
	float edge = COLOR.g;
	// The glaze: darker where it pooled low on the wall, thin and lighter over the edges.
	vec3 g = glaze_colour * (0.8 + 0.4 * mottle);
	g = mix(g, g * 0.62, (1.0 - t) * 0.6);
	g = mix(g, mix(g, clay_colour, 0.45) * 1.15, edge * 0.8);
	vec3 clay = clay_colour * (0.85 + 0.3 * mottle);
	vec3 col = mix(clay, g, glaze);
	col = mix(col, col * 0.35, speck * 0.7);
	ALBEDO = col;
	ROUGHNESS = mix(0.9, glaze_roughness + 0.1 * mottle, glaze);
	SPECULAR = mix(0.3, 0.6, glaze);
}
