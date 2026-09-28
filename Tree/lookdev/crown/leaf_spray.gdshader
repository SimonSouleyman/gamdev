shader_type spatial;
// Look-test crown: leaf-spray cards from a four-cell atlas. Normals are bent towards the crown
// surface (the crown shades as one soft volume), light glows through the leaves against the
// sun, and a dithered alpha keeps edges soft without sorting.
render_mode cull_disabled, depth_draw_opaque, diffuse_lambert_wrap, specular_schlick_ggx;

uniform sampler2D spray_color : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D spray_normal : hint_normal, filter_linear_mipmap, repeat_disable;
uniform vec3 crown_centre = vec3(0.0, 8.0, 0.0);
uniform vec3 crown_radii = vec3(5.0);
uniform float crown_normal_mix = 0.6;
uniform float wind_strength = 1.0;
uniform vec3 translucency : source_color = vec3(0.5, 0.6, 0.1);
uniform vec3 tint_mul : source_color = vec3(1.0);
// Forest trees and bushes: sprays merged into one mesh with the atlas cell already in the UVs
// and crown normals already in the vertex normals.
uniform bool baked = false;
// > 0: fragments closer to the camera than this dissolve (forest trees in front of the camera).
uniform float near_fade = 0.0;


void vertex() {
	// Atlas cell from the instance's custom data (0..3).
	float cell = INSTANCE_CUSTOM.x;
	float phase = baked ? fract(VERTEX.x * 0.37 + VERTEX.z * 0.61) : INSTANCE_CUSTOM.y;
	// 0 at the stem, 1 at the spray tip (baked meshes carry it in UV2.x).
	float tip = baked ? UV2.x : 1.0 - UV.y;
	if (!baked) {
		UV = UV * 0.5 + vec2(mod(cell, 2.0), floor(cell / 2.0)) * 0.5;
	}
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	// Wind: whole limbs sway slowly with height, the spray tip bends, the leaves flutter.
	float t = TIME;
	float gust = sin(t * 0.7 + origin.x * 0.21 + origin.z * 0.17) * 0.5 + 0.5;
	float limb = 0.018 * pow(max(origin.y, 0.0), 1.15) * (0.5 + gust);
	vec3 sway = vec3(sin(t * 0.6 + origin.x * 0.2), 0.0, cos(t * 0.5 + origin.z * 0.2)) * limb;
	float flutter = sin(t * 5.0 + world.x * 3.1 + world.y * 2.3 + phase * 20.0) * 0.03 * tip;
	vec3 bend = vec3(sin(t * 1.7 + phase * 9.0), -0.3, cos(t * 1.3 + phase * 7.0)) * 0.05 * tip * (0.4 + gust);
	world += (sway + bend + vec3(0.0, flutter, 0.0)) * wind_strength;
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	if (!baked) {
		vec3 crown_n = normalize((world - crown_centre) / (crown_radii * crown_radii));
		vec3 card_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
		// Face the card normal to the crown's outside, then blend towards the crown surface normal.
		if (dot(card_n, crown_n) < 0.0) { card_n = -card_n; }
		NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(card_n, crown_n, crown_normal_mix)), 0.0)).xyz);
	}
}

void fragment() {
	if (near_fade > 0.0) {
		float keep = smoothstep(near_fade * 0.6, near_fade, length(VERTEX));
		float d2 = fract(sin(dot(floor(FRAGCOORD.xy), vec2(39.3468, 11.1353))) * 24634.6345);
		if (d2 > keep) { discard; }
	}
	vec4 tex = texture(spray_color, UV);
	// Dithered alpha: soft, sort-free edges that also cast matching shadows.
	float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
	if (tex.a < 0.35 + 0.3 * dither) { discard; }
	ALBEDO = tex.rgb * COLOR.rgb * tint_mul;
	// Instance alpha carries the crown occlusion: inner and lower leaves see little sky.
	AO = 1.0 - COLOR.a;
	AO_LIGHT_AFFECT = 0.35;
	NORMAL_MAP = texture(spray_normal, UV).rgb;
	NORMAL_MAP_DEPTH = 0.7;
	ROUGHNESS = 0.85;
	SPECULAR = 0.06;
	// Leaves are thin: the sun shines through them, stronger on the crown's shaded side.
	BACKLIGHT = translucency * COLOR.rgb * (1.0 - COLOR.a * 0.6);
}
