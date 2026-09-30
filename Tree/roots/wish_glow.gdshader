shader_type spatial;
// The day's wish underground (0.7): a soft warm haze over its deposit that breathes slowly.
// Plain up close, and still a faint warm smudge beyond the fog where the ordinary dots have
// faded out, so it reads as "over there". Warmth, size and a slow pulse, not only hue.
// 0.7 review: from the overview it was nearly lost, so far off the haze grows (a warm patch of
// sky-glow, not a point) and it ignores the depth buffer: old roots and stones never hide it
// (it is additive and soft, so drawn over them it reads as light behind them).
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform vec4 warm : source_color = vec4(1.0, 0.66, 0.3, 1.0);
// 1 for today's wish, less for yesterday's missed one; swells for a moment when reached.
uniform float strength = 1.0;
// Full strength up to near_full metres; down to far_floor by fog_edge; held to floor_until;
// gone by gone_at.
uniform float near_full = 7.0;
uniform float fog_edge = 15.0;
uniform float far_floor = 0.9;
uniform float floor_until = 45.0;
uniform float gone_at = 60.0;
// Far off the haze grows by up to this factor (by floor_until), so it stays a patch on screen.
uniform float far_grow = 3.0;
uniform float pulse_period = 5.2;
uniform float pulse_depth = 0.35;
// Brightness of the haze close up (additive, so it never hides the dots inside it).
uniform float near_level = 0.32;

varying float v_fade;

void vertex() {
	vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	float d = length((VIEW_MATRIX * vec4(world_pos, 1.0)).xyz);
	float grow = mix(1.0, far_grow, smoothstep(near_full, floor_until, d));
	vec3 p = world_pos + (right * VERTEX.x + up * VERTEX.y) * s * grow;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	float fade = mix(1.0, far_floor, smoothstep(near_full, fog_edge, d));
	fade *= 1.0 - smoothstep(floor_until, gone_at, d);
	// Inside the haze (the camera behind the tip, right at the patch) it thins out.
	fade *= mix(0.3, 1.0, smoothstep(0.5 * s, 1.0 * s, d));
	float breath = 0.5 + 0.5 * sin(TIME * 6.2831853 / pulse_period);
	v_fade = fade * strength * (1.0 - pulse_depth + pulse_depth * breath);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float haze = pow(clamp(1.0 - r, 0.0, 1.0), 1.6);
	float core = smoothstep(0.45, 0.0, r) * 0.5;
	float a = (haze * near_level + core * near_level) * v_fade;
	ALBEDO = warm.rgb * a;
}
