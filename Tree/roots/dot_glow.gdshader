shader_type spatial;
// Glowing nutrient dot: a camera-facing soft glow, fading into the dark with distance (the fog).
// 0.8: each nutrient glows in its own shape (NutrientMarks: water a drop, nitrogen a leaf,
// phosphorus a spark, potassium a ring), read from an atlas on the same quads (no new draw
// calls). The kind comes from the instance's custom data; far dots, too small to carry a shape,
// fade to the same round glow for every kind.
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform float fog_near = 4.0;
uniform float fog_far = 15.0;
uniform float pulse = 0.12;
// Multiplies the instance colour; single flashes use it for their colour and fade.
uniform vec4 tint : source_color = vec4(1.0);
// The four marks side by side: red the crisp shape, green its soft glow.
uniform sampler2D marks : filter_linear_mipmap, repeat_disable;
// Shapes in full up to shape_near metres, round from shape_far (the fog's half distance).
uniform float shape_near = 7.5;
uniform float shape_far = 9.5;
// The mark spans this share of the quad's half width (the round halo fills the rest).
uniform float shape_span = 0.72;
// A single flash (not the MultiMesh) sets its kind here; -1 reads the instance data, 4 = round.
uniform float kind_override = -1.0;

varying float v_fade;
varying vec4 v_color;
varying float v_kind;
varying float v_shape;

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
	v_color = COLOR * tint;
	// Each dot breathes a little, out of phase with its neighbours.
	v_fade *= 1.0 - pulse + pulse * sin(TIME * 1.7 + world_pos.x * 3.1 + world_pos.z * 2.3);
	v_kind = kind_override >= 0.0 ? kind_override : INSTANCE_CUSTOM.x;
	v_shape = (v_kind < 3.5) ? 1.0 - smoothstep(shape_near, shape_far, d) : 0.0;
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	// The old round glow: far dots, and flashes without a kind.
	float core = smoothstep(0.35, 0.0, r);
	float halo = smoothstep(1.0, 0.0, r) * 0.45;
	float round_a = core + halo;
	float round_white = core * 0.35;
	// The mark: its cell in the atlas, the shape's own glow and a faint round halo.
	vec2 q = c / shape_span;
	float shape_a = 0.0;
	float shape_white = 0.0;
	if (v_shape > 0.0) {
		vec2 cell = clamp(q * 0.5 + 0.5, vec2(0.0), vec2(1.0));
		float inside = step(abs(q.x), 1.0) * step(abs(q.y), 1.0);
		vec2 m = texture(marks, vec2((floor(v_kind + 0.5) + cell.x) * 0.25, cell.y)).rg * inside;
		shape_a = m.r * 0.85 + m.g * 0.75 + smoothstep(1.0, 0.0, r) * 0.12;
		shape_white = m.r * 0.18;
	}
	float a = mix(round_a, shape_a, v_shape) * v_fade * v_color.a;
	float white = mix(round_white, shape_white, v_shape) * v_fade;
	ALBEDO = v_color.rgb * a + vec3(white);
}
