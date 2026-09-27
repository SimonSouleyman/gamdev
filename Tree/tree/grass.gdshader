shader_type spatial;
// Instanced grass blades: darker at the root, lighter at the tip, bending in the wind,
// shrinking away with distance so far grass costs nothing.
render_mode cull_disabled;

uniform float wind_strength = 1.0;
uniform float fade_start = 35.0;
uniform float fade_end = 70.0;
uniform vec3 root_color : source_color = vec3(0.12, 0.22, 0.06);
uniform vec3 tip_color : source_color = vec3(0.44, 0.58, 0.22);

varying float v_height;

void vertex() {
	v_height = UV.y;
	vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float cam_dist = length(world - (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz);
	float keep = 1.0 - smoothstep(fade_start, fade_end, cam_dist);
	VERTEX.y *= keep;
	VERTEX.x *= mix(1.0, 2.2, 1.0 - keep);
	float gust = sin(TIME * 0.8 + world.x * 0.25 + world.z * 0.18) * 0.5 + 0.5;
	float bend = (sin(TIME * 2.3 + world.x * 1.7 + world.z * 1.3) * 0.5 + 0.5 * gust) * wind_strength;
	float h = UV.y * UV.y;
	VERTEX.x += bend * 0.12 * h;
	VERTEX.z += bend * 0.06 * h;
	// Blade normals lean up, so the meadow lights like a lawn, not like cards.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.9));
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	ALBEDO = mix(root_color, tip_color, v_height) * COLOR.rgb;
	ROUGHNESS = 0.8;
	SPECULAR = 0.25;
	BACKLIGHT = vec3(0.3, 0.4, 0.1) * v_height;
}
