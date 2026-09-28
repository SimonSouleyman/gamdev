shader_type spatial;
// Meadow clumps: crossed cards with a painted clump of grass (or herbs and flowers), soft and
// dense like a real meadow instead of single blades. The tops sway in the wind; far clumps
// shrink away so the meadow costs little.
render_mode cull_disabled, diffuse_lambert_wrap;

uniform sampler2D clump_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform float wind_strength = 1.0;
uniform float fade_start = 24.0;
uniform float fade_end = 42.0;
// The season's colour (SeasonLook) and the sheen of rain.
uniform vec3 season_tint = vec3(1.0);
uniform float wet = 0.0;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float cam_dist = length(world - (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz);
	VERTEX *= 1.0 - smoothstep(fade_start, fade_end, cam_dist);
	float top = 1.0 - UV.y;
	float gust = sin(TIME * 0.7 + world.x * 0.21 + world.z * 0.17) * 0.5 + 0.5;
	float sway = (sin(TIME * 2.1 + world.x * 1.3 + world.z * 0.9) * 0.6 + gust * 0.8) * wind_strength;
	VERTEX.x += sway * 0.05 * top * top;
	VERTEX.z += sway * 0.03 * top * top;
	// Light the clump like a soft surface of grass, not like upright cards.
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.85));
}

void fragment() {
	// Back faces of a card: the phone renderer flips the normal there, which turns the upward-bent
	// meadow normal downward and the clump black (0.6 look review). Keep it pointing up.
	vec3 view_up = (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
	if (dot(NORMAL, view_up) < 0.0) { NORMAL = -NORMAL; }
	vec4 tex = texture(clump_texture, UV);
	ALBEDO = tex.rgb * COLOR.rgb * season_tint * (1.0 - 0.15 * wet);
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = 0.4;
	ALPHA_ANTIALIASING_EDGE = 0.3;
	ROUGHNESS = mix(0.95, 0.45, wet);
	SPECULAR = mix(0.05, 0.4, wet);
	BACKLIGHT = tex.rgb * 0.15 * (1.0 - UV.y);
}
