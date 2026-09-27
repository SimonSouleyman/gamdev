shader_type spatial;
// Leaf cards of the crown: alpha-cut textured quads, lit softly from outside the cluster,
// translucent against the sun, moving in the wind.
render_mode cull_disabled, depth_draw_opaque, diffuse_lambert_wrap;

uniform sampler2D leaf_texture : source_color, filter_linear_mipmap, repeat_disable;
// With the photo atlas (ambientCG LeafSet004, CC0): colour + separate opacity + normal map.
uniform bool use_atlas = false;
uniform sampler2D leaf_opacity : hint_default_white, filter_linear_mipmap, repeat_disable;
uniform sampler2D leaf_normal : hint_normal, filter_linear_mipmap, repeat_disable;
uniform float wind_strength = 1.0;
// > 0: fragments closer to the camera than this dissolve (forest trees in front of a far camera).
uniform float near_fade = 0.0;
uniform vec3 translucency = vec3(0.22, 0.32, 0.06);

void vertex() {
	// Normals bend outward from the cluster centre, so a cluster shades like a soft ball of leaves.
	vec3 outward = normalize(VERTEX + vec3(0.0, 0.15, 0.0));
	NORMAL = normalize(mix(NORMAL, outward, 0.65));
	vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float gust = sin(TIME * 0.9 + world.x * 0.35 + world.z * 0.27) * 0.5 + 0.5;
	float sway = sin(TIME * 1.9 + world.x * 1.3 + world.y * 0.7) * 0.04 * (0.4 + gust);
	float flutter = sin(TIME * 7.0 + VERTEX.x * 9.0 + VERTEX.z * 7.0 + world.y * 3.0) * 0.025 * UV.y;
	VERTEX += vec3(sway, flutter * 0.5, sway * 0.6 + flutter) * wind_strength;
}

void fragment() {
	if (near_fade > 0.0) {
		float d = length(VERTEX);
		float keep = smoothstep(near_fade * 0.6, near_fade, d);
		float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
		if (dither > keep) { discard; }
	}
	vec4 tex = texture(leaf_texture, UV);
	ALBEDO = tex.rgb * COLOR.rgb;
	ALPHA = use_atlas ? texture(leaf_opacity, UV).r : tex.a;
	if (use_atlas) {
		NORMAL_MAP = texture(leaf_normal, UV).rgb;
		NORMAL_MAP_DEPTH = 0.6;
	}
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 0.8;
	SPECULAR = 0.12;
	BACKLIGHT = translucency * COLOR.rgb;
}
