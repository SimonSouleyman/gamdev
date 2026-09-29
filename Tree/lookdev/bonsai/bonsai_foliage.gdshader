shader_type spatial;
// The bonsai's foliage (shed/bonsai_view.gd): tuft cards from a four-cell atlas, gathered into
// pads. Each card carries the normal of its pad's dome (INSTANCE_CUSTOM.zw, octahedral), so a pad
// shades as one soft cloud: a bright top, a dark underside. Instance colour: rgb tint, a the
// occlusion (inner and lower pads see less sky). While wiring or cutting whole cards drop out
// (clean gaps instead of pixel noise), so the wood shows.
render_mode cull_disabled, depth_draw_opaque, diffuse_lambert_wrap, specular_schlick_ggx;

uniform sampler2D spray_color : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D spray_normal : hint_normal, filter_linear_mipmap, repeat_disable;
uniform float pad_normal_mix = 0.7;
uniform float wind_strength = 0.06;
uniform vec3 translucency : source_color = vec3(0.25, 0.35, 0.1);
uniform vec3 tint_mul : source_color = vec3(1.0);
// 0..1: this share of the cards drops out (wire and shears).
uniform float fade = 0.0;
// > 0: cards whose middle is closer to the camera than this drop out.
uniform float near_fade = 0.0;
uniform bool cheap = false;

varying float card_key;
varying float drop;

vec3 oct_decode(vec2 e) {
	e = e * 2.0 - 1.0;
	vec3 v = vec3(e.x, 1.0 - abs(e.x) - abs(e.y), e.y);
	if (v.y < 0.0) {
		vec2 s = vec2(v.x >= 0.0 ? 1.0 : -1.0, v.z >= 0.0 ? 1.0 : -1.0);
		v.xz = (1.0 - abs(v.zx)) * s;
	}
	return normalize(v);
}

void vertex() {
	float cell = INSTANCE_CUSTOM.x;
	card_key = INSTANCE_CUSTOM.y;
	float tip = 1.0 - UV.y;
	UV = UV * 0.5 + vec2(mod(cell, 2.0), floor(cell / 2.0)) * 0.5;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Hardly a breath of wind indoors: the tuft tips stir.
	float t = TIME;
	world += vec3(sin(t * 1.3 + card_key * 20.0), 0.0, cos(t * 1.1 + card_key * 17.0)) * 0.004 * tip * wind_strength * 10.0;
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	vec3 pad_n = normalize((MODEL_MATRIX * vec4(oct_decode(INSTANCE_CUSTOM.zw), 0.0)).xyz);
	vec3 card_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	if (dot(card_n, pad_n) < 0.0) { card_n = -card_n; }
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(card_n, pad_n, pad_normal_mix)), 0.0)).xyz);
	// Dropping out, per card: a share of them, and those close to the camera.
	vec3 mid = (MODEL_MATRIX * vec4(0.0, 0.5, 0.0, 1.0)).xyz;
	float near = near_fade > 0.0 ? 1.0 - smoothstep(near_fade * 0.55, near_fade, length(mid - CAMERA_POSITION_WORLD)) : 0.0;
	drop = (card_key < fade || fract(card_key * 7.31) < near) ? 1.0 : 0.0;
}

void fragment() {
	if (drop > 0.5) { discard; }
	vec4 tex = texture(spray_color, UV);
	float dither = fract(sin(dot(floor(FRAGCOORD.xy), vec2(12.9898, 78.233))) * 43758.5453);
	if (tex.a < 0.4 + 0.2 * dither) { discard; }
	ALBEDO = tex.rgb * COLOR.rgb * tint_mul * (1.0 - 0.45 * COLOR.a);
	AO = 1.0 - COLOR.a;
	AO_LIGHT_AFFECT = 0.5;
	if (!cheap) {
		NORMAL_MAP = texture(spray_normal, UV).rgb;
		NORMAL_MAP_DEPTH = 0.6;
	}
	ROUGHNESS = 0.8;
	SPECULAR = 0.12;
	BACKLIGHT = translucency * COLOR.rgb * (1.0 - COLOR.a * 0.7);
}
