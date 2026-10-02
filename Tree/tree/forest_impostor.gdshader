shader_type spatial;
// The phone's forest (ForestImpostors): each tree or shrub is one upright card with its baked
// picture, turned about its trunk to face the camera. The baked normals let the card take the
// sun of the hour like the real crowns. One flat layer per plant and a hard alpha cut keep the
// overdraw low: no dithered edges, no near fade.
render_mode world_vertex_coords, cull_disabled, depth_draw_opaque, diffuse_lambert_wrap, specular_disabled, shadows_disabled;

// Imported premultiplied (colour times alpha), so the small mip levels of a card are not
// darkened by the empty black around the plant.
uniform sampler2D albedo_atlas : source_color, filter_linear_mipmap, repeat_disable;
// Encoded as n * 0.5 + 0.5: x right, y up, z towards the camera it was baked from.
uniform sampler2D normal_atlas : filter_linear_mipmap, repeat_disable;
// Each cell's rectangle in the atlas (ForestImpostors.MAX_CELLS).
uniform vec4 cells[10];
// How far each corner is cut off (ForestImpostors.card_mesh), as fractions of the card:
// bottom: left (x, y), right (x, y); top likewise.
uniform vec4 cuts_bottom[10];
uniform vec4 cuts_top[10];
// A linear colour factor (may go above 1), for tuning a set by hand.
uniform vec3 tint_mul = vec3(1.0);
uniform vec3 translucency : source_color = vec3(0.25, 0.3, 0.06);
uniform float wind_strength = 1.0;
// The flat card lacks the real crowns' many sprays facing every way (some always catch the
// sun): its normals lean this far up, towards the sky and the high sun.
uniform float lift = 0.3;
// A little lower than the crowns' 0.5: mipmaps thin the far cards' leaves out otherwise.
uniform float alpha_cut = 0.4;
// The season (SeasonLook): each cell's autumn colour and how far its kind turns (0.8.2.1), the
// autumn and late-autumn amounts. Each tree turns a little differently (its wind phase).
uniform vec4 cell_fall[10];
uniform float autumn = 0.0;
uniform float late = 0.0;
varying vec4 v_fall;

varying vec3 v_right;
varying vec3 v_facing;
varying float v_bright;

void vertex() {
	// INSTANCE_CUSTOM: x = cell, y = where the trunk stands across the card, z = brightness,
	// w = wind phase. The instance basis carries the card's width and height in metres.
	vec3 origin = MODEL_MATRIX[3].xyz;
	float w = length(MODEL_MATRIX[0].xyz);
	float h = length(MODEL_MATRIX[1].xyz);
	vec3 facing = CAMERA_POSITION_WORLD - origin;
	facing.y = 0.0;
	facing = normalize(facing + vec3(0.0001, 0.0, 0.0));
	vec3 right = cross(vec3(0.0, 1.0, 0.0), facing);
	// The corner comes from the UV (UV.y = 1 at the ground), moved in along the edge by the cut.
	int c = int(INSTANCE_CUSTOM.x + 0.5);
	vec2 card = vec2(UV.x, 1.0 - UV.y);
	vec4 cuts = card.y < 0.5 ? cuts_bottom[c] : cuts_top[c];
	card += UV2 * (card.x < 0.5 ? cuts.xy : cuts.zw);
	vec2 corner = vec2(card.x - INSTANCE_CUSTOM.y, card.y);
	// The crown sways slowly, the trunk foot stays put.
	float sway = sin(TIME * 0.6 + INSTANCE_CUSTOM.w) * 0.012 * h * corner.y * corner.y * wind_strength;
	VERTEX = origin + right * (corner.x * w + sway) + vec3(0.0, corner.y * h, 0.0);
	NORMAL = facing;
	vec4 cell = cells[c];
	UV = cell.xy + vec2(card.x, 1.0 - card.y) * cell.zw;
	v_right = right;
	v_facing = facing;
	v_bright = INSTANCE_CUSTOM.z;
	v_fall = cell_fall[c];
	// Some trees turn early, some late: the turn by this tree's own share.
	float own = fract(INSTANCE_CUSTOM.w * 3.71);
	v_fall.a *= clamp((max(autumn, late) - own * 0.45) / 0.55, 0.0, 1.0);
}

void fragment() {
	vec4 tex = texture(albedo_atlas, UV);
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	vec3 n = texture(normal_atlas, UV).rgb * 2.0 - 1.0;
	vec3 world_n = normalize(v_right * n.x + vec3(0.0, n.y, 0.0) + v_facing * n.z);
	world_n = normalize(mix(world_n, vec3(0.0, 1.0, 0.0), lift));
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	ALBEDO = tex.rgb / max(tex.a, 0.01) * tint_mul * v_bright;
	if (v_fall.a > 0.0) {
		// Only the leaves turn (green-dominant texels), keeping their light and shade.
		vec3 c = ALBEDO;
		float leafy = clamp((c.g - max(c.r, c.b)) * 8.0 + 0.5, 0.0, 1.0);
		float lum = dot(c, vec3(0.3, 0.55, 0.15));
		vec3 turned = v_fall.rgb * (lum * 3.0 + 0.03);
		turned = mix(turned, vec3(0.42, 0.3, 0.2) * lum * 1.8, late * 0.6);
		ALBEDO = mix(c, turned, leafy * v_fall.a);
	}
	// Leaves are thin: the low sun shines through the wall a little, as through the real crowns.
	BACKLIGHT = translucency * 0.35;
	ROUGHNESS = 0.9;
}
