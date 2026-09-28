shader_type spatial;
// Look-test meadow grass: crossed cards from a 2 x 2 atlas of painted clumps (thin arching
// blades), the cell picked per clump from its position. Dark at the root where the blades
// shade each other, light glowing through the tips, normals mostly up so the meadow shades
// as one soft surface. Far clumps shrink away.
render_mode cull_disabled, diffuse_lambert_wrap;

uniform sampler2D clump_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform float wind_strength = 1.0;
uniform float fade_start = 14.0;
uniform float fade_end = 30.0;
uniform vec3 translucency : source_color = vec3(0.3, 0.38, 0.08);
// The second meadow layer picks its cell per clump (in the instance colour's alpha).
uniform bool cell_from_alpha = false;
// The season's colour (SeasonLook), dew sparkling in the early sun and the sheen of rain.
uniform vec3 season_tint = vec3(1.0);
uniform float dew = 0.0;
uniform float wet = 0.0;

varying float root_shade;
varying vec3 clump_pos;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float h = fract(sin(dot(world.xz, vec2(12.9898, 78.233))) * 43758.5453);
	float cell = cell_from_alpha ? floor(COLOR.a * 4.0) : floor(h * 4.0);
	float top = 1.0 - UV.y;
	UV = UV * 0.5 + vec2(mod(cell, 2.0), floor(cell / 2.0)) * 0.5;
	float cam_dist = length(world - (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz);
	VERTEX *= 1.0 - smoothstep(fade_start, fade_end, cam_dist);
	float gust = sin(TIME * 0.7 + world.x * 0.21 + world.z * 0.17) * 0.5 + 0.5;
	float sway = (sin(TIME * 2.1 + world.x * 1.3 + world.z * 0.9) * 0.6 + gust * 0.8) * wind_strength;
	VERTEX.x += sway * 0.05 * top * top;
	VERTEX.z += sway * 0.03 * top * top;
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.5));
	root_shade = top;
	clump_pos = world;
}

void fragment() {
	vec4 tex = texture(clump_texture, UV);
	// Alpha to coverage with the phone's MSAA: soft blade edges instead of hard ones that
	// pop in and out between frames (the meadow shimmered, Simon 0.5.1).
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = 0.4;
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALBEDO = tex.rgb * COLOR.rgb * season_tint;
	// Blades shade each other near the ground.
	AO = mix(0.35, 1.0, smoothstep(0.0, 0.6, root_shade));
	AO_LIGHT_AFFECT = 0.5;
	ROUGHNESS = mix(0.95, 0.4, max(wet, dew * 0.6));
	SPECULAR = mix(0.05, 0.45, max(wet, dew * 0.6));
	ALBEDO *= 1.0 - 0.15 * wet;
	if (dew > 0.001) {
		// Dew: a few tiny drops on the blades catch the light and glint as the eye moves.
		vec2 cell = floor(UV * 150.0);
		float h = fract(sin(dot(cell + clump_pos.xz * 3.1, vec2(12.9898, 78.233))) * 43758.5453);
		vec2 f = fract(UV * 150.0) - 0.5;
		float drop = step(0.93, h) * smoothstep(0.25, 0.0, length(f)) * step(0.5, tex.a);
		float glint = pow(0.5 + 0.5 * sin(TIME * 1.7 + h * 40.0 + dot(VIEW, vec3(9.0, 5.0, 7.0))), 6.0);
		EMISSION = vec3(1.0, 0.98, 0.9) * drop * (0.25 + 1.6 * glint) * dew;
	}
	BACKLIGHT = translucency * smoothstep(0.3, 1.0, root_shade);
}
