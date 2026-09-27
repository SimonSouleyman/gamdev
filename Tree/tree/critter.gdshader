shader_type spatial;
// Butterflies and birds: two wing quads per instance that flap by bending around the body axis
// (local x = 0). Each instance has its own flap phase from INSTANCE_CUSTOM.x.
render_mode cull_disabled, diffuse_lambert_wrap;

uniform float flap_speed = 14.0;
uniform float flap_amount = 1.1;

void vertex() {
	float phase = INSTANCE_CUSTOM.x * 6.2831;
	float speed = flap_speed * (0.8 + INSTANCE_CUSTOM.y * 0.4);
	float angle = sin(TIME * speed + phase) * flap_amount * sign(VERTEX.x);
	float x = VERTEX.x;
	VERTEX.x = x * cos(angle);
	VERTEX.y += abs(x) * sin(abs(angle));
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	// A rounded wing shape cut from the quad.
	float wing = 1.0 - smoothstep(0.75, 1.0, length(vec2(c.x * 0.9, c.y * 1.25)));
	ALBEDO = COLOR.rgb;
	ALPHA = wing;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.7;
	BACKLIGHT = COLOR.rgb * 0.5;
}
