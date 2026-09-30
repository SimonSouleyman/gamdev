shader_type spatial;
// The player's crown (0.6.2): leaf sprays gathered into leaf masses at the twig ends
// (HeroCrown). Each spray carries a bent normal (the outside of its leaf mass, blended with the
// outside of the crown) and an occlusion value: the crown shades as lumpy masses with a dark
// interior and a bright, sunlit rim, and sky shows through the gaps between the masses.
// A clean alpha cut (alpha to coverage where the renderer has MSAA), no dither speckle.
render_mode cull_disabled, depth_draw_opaque, diffuse_lambert_wrap, specular_schlick_ggx;

uniform sampler2D spray_color : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D spray_normal : hint_normal, filter_linear_mipmap, repeat_disable;
// How far the card normal bends to the leaf mass's normal (1 = the mass shades as one lump).
uniform float mass_normal_mix = 0.85;
uniform float wind_strength = 1.0;
uniform vec3 translucency : source_color = vec3(0.55, 0.6, 0.18);
uniform vec3 tint_mul : source_color = vec3(1.0);
// How dark the inside of the crown gets (albedo and light), 0..1.
uniform float interior_dark = 0.5;
uniform bool cheap = false;
uniform float rim_strength = 0.0;
// Seasons as a look (Almanac.season_look), the same as leaf_spray.gdshader.
uniform float fresh = 0.0;
uniform float autumn = 0.0;
uniform float late = 0.0;
uniform float autumn_hue = -1.0;
uniform float wet = 0.0;
// Care (0.6.3, GameState.care_signals): shape first, colour second. Thirst hangs the sprays
// (tips drop and swing in, a sixth fold away); the others add a colour cue on top of the
// shape cues HeroCrown bakes (smaller sprays on new shoots, fewer leaf masses).
uniform float thirst = 0.0;
uniform float pale = 0.0;
uniform float dull = 0.0;
uniform float scorch = 0.0;
// Noon lift (0.6.3): the sunlit outside of the crown a little brighter, so it reads at noon.
uniform float sun_lift = 0.3;
// A soft daylight fill (0.6.3): sprays in the crown's own shadow read dark green, not black
// (the blotches at noon). Set by the tree view from the daylight, so the night stays dark.
uniform float day_fill = 0.0;
// The phone's colour (0.7 review): the compatibility renderer shows the painted greens lime and
// the autumn orange loud. A calmer grade there (the tree view sets it on the phone renderer only):
// a multiply on the summer green, then a saturation cut.
uniform vec3 green_grade = vec3(1.0);
uniform float saturation = 1.0;
// The leaves' sheen (0.8 review: on the phone the noon sheen and the tone mapper together turned
// the sunlit side cream; the tree view lowers it there).
uniform float sheen = 0.18;

varying vec2 leaf_hash;
varying float occ;
// A twig the tree marks for pruning (0.7, HeroCrown): its strength rides in the fractional part
// of the spray's cell index (INSTANCE_CUSTOM.x = cell + 0.9 * strength).
varying float tired;


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
	float cell = floor(INSTANCE_CUSTOM.x + 0.02);
	tired = clamp((INSTANCE_CUSTOM.x - cell) / 0.9, 0.0, 1.0);
	float phase = INSTANCE_CUSTOM.y;
	float tip = 1.0 - UV.y;
	UV = UV * 0.5 + vec2(mod(cell, 2.0), floor(cell / 2.0)) * 0.5;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	leaf_hash = vec2(fract(phase * 7.13 + 1.85), 0.5);
	// A marked twig's few dry leaves let the light through: less of the crown's inner shade, so
	// the sign does not sink into it (0.7 review).
	occ = COLOR.a * (1.0 - 0.55 * tired);
	// Wind: limbs sway slowly with height, the spray tip bends, the leaves flutter.
	float t = TIME;
	float gust = sin(t * 0.7 + origin.x * 0.21 + origin.z * 0.17) * 0.5 + 0.5;
	float limb = 0.012 * pow(max(origin.y, 0.0), 1.1) * (0.5 + gust);
	vec3 sway = vec3(sin(t * 0.6 + origin.x * 0.2), 0.0, cos(t * 0.5 + origin.z * 0.2)) * limb;
	float flutter = sin(t * 5.0 + world.x * 3.1 + world.y * 2.3 + phase * 20.0) * 0.025 * tip;
	vec3 bend = vec3(sin(t * 1.7 + phase * 9.0), -0.3, cos(t * 1.3 + phase * 7.0)) * 0.04 * tip * (0.4 + gust);
	world += (sway + bend + vec3(0.0, flutter, 0.0)) * wind_strength;
	// Thirst: the spray hangs from its stem, the tip drops and swings in towards the stem.
	if (thirst > 0.001) {
		float s = length(MODEL_MATRIX[0].xyz);
		float hang = thirst * tip * tip;
		world.y -= hang * s * 0.55;
		world.xz = mix(world.xz, origin.xz, hang * 0.3);
	}
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	vec3 mass_n = oct_decode(INSTANCE_CUSTOM.zw);
	vec3 card_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	if (dot(card_n, mass_n) < 0.0) { card_n = -card_n; }
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(card_n, mass_n, mass_normal_mix)), 0.0)).xyz);
}

void fragment() {
	// Both faces of a card shade with its leaf mass's normal (the renderer flips it on the back).
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	vec4 tex = texture(spray_color, UV);
	// Mipmaps average the alpha down, so far sprays would thin to nothing: lift the alpha by the
	// mip level (Castano), then cut cleanly at one half.
	vec2 texel = UV * vec2(textureSize(spray_color, 0));
	vec2 dx = dFdx(texel);
	vec2 dy = dFdy(texel);
	float lod = max(0.0, 0.5 * log2(max(dot(dx, dx), dot(dy, dy))));
	ALPHA = clamp(tex.a * (1.0 + lod * 0.22), 0.0, 1.0);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALPHA_TEXTURE_COORDINATE = texel;
	vec3 col = tex.rgb * COLOR.rgb * tint_mul * green_grade;
	// A marked twig (0.7): the few leaves it keeps turn early, dull and dry: a washed-out
	// yellow-grey going brownish, never the bright yellow of a nitrogen-pale shoot. In autumn the
	// twig is the first to go bare: most of its sprays are gone, the rest are dry brown.
	float autumn_now = max(autumn, late);
	if (tired > 0.01) {
		if (leaf_hash.x < tired * smoothstep(0.1, 0.6, autumn_now) * 0.75) { discard; }
		float l0 = dot(col, vec3(0.3, 0.59, 0.11));
		col = mix(col, l0 * vec3(1.4, 1.2, 0.78), tired * 0.9);
	}
	if (fresh + autumn > 0.001) {
		float lum = dot(col, vec3(0.3, 0.59, 0.11));
		col = mix(col, lum * vec3(0.95, 1.5, 0.45), fresh * 0.5);
		float hue = autumn_hue < 0.0 ? leaf_hash.y : autumn_hue;
		hue = clamp(hue + (leaf_hash.x - 0.5) * 0.45, 0.0, 1.0);
		vec3 yellow = vec3(1.55, 1.2, 0.3);
		vec3 orange = vec3(1.7, 0.8, 0.22);
		vec3 red = vec3(1.4, 0.36, 0.16);
		vec3 turned = hue < 0.5 ? mix(yellow, orange, hue * 2.0) : mix(orange, red, hue * 2.0 - 1.0);
		turned = mix(turned, vec3(0.95, 0.62, 0.3), late * 0.55);
		float turn = smoothstep(leaf_hash.x * 0.55, leaf_hash.x * 0.55 + 0.45, autumn);
		col = mix(col, lum * turned, turn * (1.0 - tired));
		col = mix(col, lum * vec3(0.95, 0.72, 0.5), turn * tired);
		if (leaf_hash.x > 1.0 - late * 0.3) { discard; }
	}
	// Care cues, second to the shape: thirst greys and dulls, nitrogen pales towards
	// yellow-green, phosphorus darkens with a purple tinge, potassium scorches the leaf edges.
	// In autumn the season's own yellows and browns would drown (or fake) these tints, so they
	// fade out with the turning leaves and the shape alone carries the sign (0.6.3 review).
	if (thirst + pale + dull + scorch > 0.001) {
		if (leaf_hash.x < thirst * 0.17) { discard; }
		float green = 1.0 - smoothstep(0.1, 0.5, max(autumn, late));
		float l2 = dot(col, vec3(0.3, 0.59, 0.11));
		col = mix(col, l2 * vec3(1.0, 1.02, 0.78), thirst * 0.3 * green);
		col = mix(col, l2 * vec3(1.3, 1.36, 0.42), pale * 0.6 * green);
		col = mix(col, col * vec3(0.72, 0.58, 0.78), dull * 0.4 * green);
		float edge = smoothstep(0.9, 0.55, tex.a);
		col = mix(col, l2 * vec3(1.35, 0.8, 0.35), scorch * edge * 0.8 * green);
	}
	// The inside of the crown: darker leaves that see little sky and little sun; the outside a
	// little lifted, so the sunlit side reads at noon (0.6.3).
	col *= (1.0 - interior_dark * 0.32 * occ) * (1.0 + sun_lift * (1.0 - occ));
	col = mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, saturation);
	ALBEDO = col * (1.0 - 0.2 * wet);
	AO = 1.0 - occ * 0.45;
	AO_LIGHT_AFFECT = 0.25;
	if (!cheap) {
		NORMAL_MAP = texture(spray_normal, UV).rgb;
		NORMAL_MAP_DEPTH = 0.5;
	}
	ROUGHNESS = mix(0.7, 0.45, wet);
	SPECULAR = mix(sheen, 0.3, wet);
	// Thin leaves glow when the sun is behind them, most at the crown's edge.
	BACKLIGHT = translucency * col * 1.35 * (1.0 - occ * 0.8);
	RIM = rim_strength * (1.0 - occ);
	EMISSION = col * day_fill * (1.0 - occ * 0.5);
	RIM_TINT = 0.5;
}
