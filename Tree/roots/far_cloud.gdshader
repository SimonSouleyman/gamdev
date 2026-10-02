shader_type spatial;
// 0.8.2 far view (specs/root-field-extras.md 1): a known rich patch as a soft coloured cloud, a
// camera-facing gaussian in the patch's kind colour (instance colour; alpha = how full it is).
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

varying vec4 v_color;

void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	vec3 p = world_pos + (right * VERTEX.x + up * VERTEX.y) * s;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	v_color = COLOR;
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r2 = dot(c, c);
	float a = exp(-r2 * 3.2) * smoothstep(1.0, 0.7, sqrt(r2)) * v_color.a;
	ALBEDO = v_color.rgb * a * 1.4 + vec3(0.12) * a;
}
