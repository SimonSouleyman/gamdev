shader_type spatial;
// A soft cloud on a large quad high above the meadow: layered noise for its shape, a round
// falloff so it never shows the quad's edge, lit warm or white by the sun's colour.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform vec3 sun_color : source_color = vec3(1.0);
uniform float brightness = 1.0;
uniform float coverage = 0.45;
uniform float seed = 0.0;

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float round_mask = 1.0 - smoothstep(0.35, 1.0, length(c));
	vec2 drift = vec2(TIME * 0.004, TIME * 0.0015);
	// Low frequencies only: soft cumulus, not speckle.
	float n = texture(noise, UV * 0.28 + vec2(seed) + drift).r * 0.75
		+ texture(noise, UV * 0.8 + vec2(seed * 1.7) - drift * 2.0).r * 0.25;
	n = n * round_mask + (1.0 - round_mask) * 0.0;
	float a = smoothstep(coverage * 0.7, coverage * 0.7 + 0.35, n);
	float shade = mix(0.72, 1.0, smoothstep(coverage, coverage + 0.45, n));
	ALBEDO = sun_color * shade * brightness;
	ALPHA = a * 0.9;
}
