shader_type spatial;
// Glowing nutrient dot: a camera-facing soft disc, fading into the dark with distance (the fog).
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform float fog_near = 4.0;
uniform float fog_far = 15.0;
uniform float pulse = 0.12;
uniform float near_hide = 1.0;
// Multiplies the instance colour; single flashes use it for their colour and fade.
uniform vec4 tint : source_color = vec4(1.0);

varying float v_fade;
varying vec4 v_color;

void vertex() {
	// Billboard, keeping the instance scale.
	vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	vec3 p = world_pos + (right * VERTEX.x + up * VERTEX.y) * s;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	float d = length((VIEW_MATRIX * vec4(world_pos, 1.0)).xyz);
	v_fade = 1.0 - smoothstep(fog_near, fog_far, d);
	// Right at the camera a dot would fill the view as a big blurred blob (0.7 review, the wish
	// reached): it fades out there instead, as out-of-focus light does.
	v_fade *= smoothstep(near_hide, near_hide * 3.0, d);
	v_color = COLOR * tint;
	// Each dot breathes a little, out of phase with its neighbours.
	v_fade *= 1.0 - pulse + pulse * sin(TIME * 1.7 + world_pos.x * 3.1 + world_pos.z * 2.3);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float core = smoothstep(0.35, 0.0, r);
	float halo = smoothstep(1.0, 0.0, r) * 0.45;
	float a = (core + halo) * v_fade * v_color.a;
	ALBEDO = v_color.rgb * a + vec3(core * 0.35 * v_fade);
}
