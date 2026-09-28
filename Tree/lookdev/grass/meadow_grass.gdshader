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

varying float root_shade;

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
}

void fragment() {
	vec4 tex = texture(clump_texture, UV);
	if (tex.a < 0.4) { discard; }
	ALBEDO = tex.rgb * COLOR.rgb;
	// Blades shade each other near the ground.
	AO = mix(0.35, 1.0, smoothstep(0.0, 0.6, root_shade));
	AO_LIGHT_AFFECT = 0.5;
	ROUGHNESS = 0.95;
	SPECULAR = 0.05;
	BACKLIGHT = translucency * smoothstep(0.3, 1.0, root_shade);
}
