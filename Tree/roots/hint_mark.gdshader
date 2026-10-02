shader_type spatial;
// A find's hint for one night (0.8.2.6, specs/journal-drawers-loop.md D2): a faint dashed pencil
// ring over a far patch (from a map scrap) or a rock band's gap (from a drain-pipe shard). Seen in
// the run and in the far view; never hidden by roots or stones (soft and additive, like the wish).
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform vec4 ink : source_color = vec4(0.85, 0.8, 0.68, 1.0);
uniform float strength = 0.42;
// Far off the ring grows by up to this factor, so it stays a mark on screen.
uniform float far_grow = 1.6;
uniform float near_full = 6.0;
uniform float far_at = 60.0;
// Dashes around the ring.
uniform float dashes = 14.0;

varying float v_fade;

void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	float d = length((VIEW_MATRIX * vec4(world_pos, 1.0)).xyz);
	float grow = mix(1.0, far_grow, smoothstep(near_full, far_at, d));
	vec3 p = world_pos + (right * VERTEX.x + up * VERTEX.y) * s * grow;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	// Thin when the camera is inside the ring.
	v_fade = strength * mix(0.35, 1.0, smoothstep(0.3 * s, 0.8 * s, d));
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float a = atan(c.y, c.x);
	// A hand-drawn ring: a little wobble in its radius, dashed, soft edged.
	float wob = 0.78 + 0.03 * sin(a * 3.0 + 1.3) + 0.015 * sin(a * 7.0);
	float line = smoothstep(0.06, 0.0, abs(r - wob));
	float dash = smoothstep(0.25, 0.45, 0.5 + 0.5 * sin(a * dashes));
	// A faint dot in the middle: "here".
	float dot = smoothstep(0.1, 0.0, r) * 0.6;
	float glow = smoothstep(0.95, 0.4, r) * 0.08;
	ALBEDO = ink.rgb * (line * dash + dot + glow) * v_fade;
}
