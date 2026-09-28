extends SceneTree
## Bakes the pictures the phone's forest is drawn from (ForestImpostors): every forest tree kind
## and every shrub kind, grown and sprayed exactly as on a PC, photographed from two sides into
## an atlas: one picture of the colour (lit by an even white sky only, so the crown's own shading
## stays in it but no sun), one of the surface normals, so the phone can light the cards with
## the real sun of the hour. Run once after the forest generator changes, windowed (it renders):
##   godot --path . -s tools/bake_forest_impostors.gd
## Writes assets/forest_impostors/*.png and cells.tres.

const OUT := "res://assets/forest_impostors"
## Pixels of one cell in the atlas; rendered at twice the size and scaled down, for smooth edges.
const TREE_CELL := Vector2i(768, 960)
const SHRUB_CELL := Vector2i(256, 256)
## Two sides of each plant, so neighbouring cards of the same kind differ.
const YAWS: Array[float] = [0.0, 2.1]
const SEED := 1

const NORMAL_SHADER := """
shader_type spatial;
render_mode unshaded, cull_disabled;
uniform sampler2D alpha_tex : filter_linear_mipmap;
// 0 opaque (bark, blossoms), 1 alpha channel (leaf sprays), 2 red channel (photo leaf opacity).
uniform int alpha_mode = 0;
uniform float wind_strength = 0.0;
// The colour pass of the leaves: their own colour and tint, and a third of the crown occlusion
// (in the game it darkens the sky light fully but the sun only by a third, as here).
uniform bool colour = false;
uniform sampler2D colour_tex : source_color, filter_linear_mipmap;
uniform vec3 tint_mul : source_color = vec3(1.0);
uniform vec3 extra_tint = vec3(1.0);
uniform float ao_share = 0.35;
varying vec3 vn;
void vertex() {
	// The mesh's own normal, never flipped for back faces: sprays carry the crown's normal.
	vn = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
}
void fragment() {
	if (alpha_mode == 1 && texture(alpha_tex, UV).a < 0.5) { discard; }
	if (alpha_mode == 2 && texture(alpha_tex, UV).r < 0.45) { discard; }
	vec3 n = normalize(vn) * 0.5 + 0.5;
	// The viewport stores sRGB: undo it, so the file holds the plain encoded normal.
	ALBEDO = mix(pow((n + 0.055) / 1.055, vec3(2.4)), n / 12.92, lessThan(n, vec3(0.04045)));
	if (colour) {
		ALBEDO = texture(colour_tex, UV).rgb * COLOR.rgb * tint_mul * extra_tint * (1.0 - COLOR.a * ao_share);
	}
}
"""

var _vp: SubViewport
var _cam: Camera3D
var _env: Environment
var _normal_shader: Shader


func _initialize() -> void:
	_bake()


func _bake() -> void:
	# The forest exactly as a PC builds it.
	var view := TreeView.new()
	root.add_child(view)
	await process_frame
	view.setup(GameState.new_game(SEED))
	view.visible = false
	_normal_shader = Shader.new()
	_normal_shader.code = NORMAL_SHADER
	_make_viewport()
	var trees := {}
	var shrubs := {}
	for n in get_nodes_in_group("forest_trees"):
		if n.has_meta("kind"):
			trees[int(n.get_meta("kind"))] = (n as MultiMeshInstance3D).multimesh.mesh
	for n in get_nodes_in_group("shrubs"):
		if n.has_meta("kind"):
			shrubs[int(n.get_meta("kind"))] = (n as MultiMeshInstance3D).multimesh.mesh
	var cells := Resource.new()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT))
	cells.set_meta("trees", await _atlas(trees, Scenery.KINDS.size(), TREE_CELL, "trees"))
	cells.set_meta("shrubs", await _atlas(shrubs, Scenery.SHRUBS.size(), SHRUB_CELL, "shrubs"))
	ResourceSaver.save(cells, OUT.path_join("cells.tres"))
	print("baked forest impostors into ", OUT)
	quit()


func _make_viewport() -> void:
	_vp = SubViewport.new()
	_vp.own_world_3d = true
	_vp.transparent_bg = true
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_vp.msaa_3d = Viewport.MSAA_4X
	root.add_child(_vp)
	_cam = Camera3D.new()
	_cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	_cam.keep_aspect = Camera3D.KEEP_HEIGHT
	_cam.far = 400.0
	# Only an even white sky light: the colour with the crown's own shade, no sun, no haze.
	_env = Environment.new()
	_env.background_mode = Environment.BG_CLEAR_COLOR
	_env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	_env.ambient_light_color = Color.WHITE
	_env.ambient_light_energy = 1.0
	_env.reflected_light_source = Environment.REFLECTION_SOURCE_DISABLED
	_env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	_cam.environment = _env
	_vp.add_child(_cam)


## One atlas: a row per side, a column per kind.
## Returns the cells [{kind, uv, size, x_off, y_min, cuts}].
func _atlas(meshes: Dictionary, kinds: int, cell: Vector2i, name: String) -> Array:
	var albedo := Image.create_empty(cell.x * kinds, cell.y * YAWS.size(), false, Image.FORMAT_RGBA8)
	var normal := Image.create_empty(cell.x * kinds, cell.y * YAWS.size(), false, Image.FORMAT_RGBA8)
	var out: Array = []
	for k in range(kinds):
		if not meshes.has(k):
			continue
		for r in range(YAWS.size()):
			var shot: Dictionary = await _photograph(meshes[k], YAWS[r], cell)
			var at := Vector2i(k * cell.x, r * cell.y)
			albedo.blit_rect(shot["albedo"], Rect2i(Vector2i.ZERO, cell), at)
			normal.blit_rect(shot["normal"], Rect2i(Vector2i.ZERO, cell), at)
			var used: Rect2i = shot["used"]
			var size := albedo.get_size()
			out.append({"kind": k,
				"uv": Rect2(Vector2(at + used.position) / Vector2(size), Vector2(used.size) / Vector2(size)),
				"size": Vector2(used.size) * shot["metres_per_pixel"],
				# Where the trunk stands across the card (0 = left edge, 1 = right edge).
				"x_off": (shot["trunk_px"] - float(used.position.x)) / float(used.size.x),
				"y_min": shot["top"] - float(used.end.y) * shot["metres_per_pixel"],
				"cuts": _corner_cuts(shot["albedo"].get_region(used))})
	# The normal picture is all opaque: its colour bleeds into the empty space, so the card's
	# edges are lit like the crown's rim and not like a flat wall.
	normal.fix_alpha_edges()
	# Beyond that, a normal facing the camera (never black, which would read as pointing away).
	var flat := Image.create_empty(normal.get_width(), normal.get_height(), false, Image.FORMAT_RGBA8)
	flat.fill(Color(0.5, 0.5, 1.0))
	flat.blend_rect(normal, Rect2i(Vector2i.ZERO, normal.get_size()), Vector2i.ZERO)
	normal = flat
	normal.convert(Image.FORMAT_RGB8)
	albedo.save_png(OUT.path_join(name + "_albedo.png"))
	normal.save_png(OUT.path_join(name + "_normal.png"))
	return out


## How far each corner of the card can be cut off without losing leaves: the card becomes an
## octagon hugging the crown, and the empty corners (beside the trunk, above the crown's
## shoulders) are no longer shaded only to be thrown away. Per corner (x, y) as fractions of the
## card: [bottom left, bottom right, top left, top right].
func _corner_cuts(img: Image) -> PackedVector2Array:
	var small := img.duplicate() as Image
	var w := 48
	var h := 60
	small.resize(w, h, Image.INTERPOLATE_BILINEAR)
	var solid := PackedByteArray()
	solid.resize(w * h)
	for y in range(h):
		for x in range(w):
			solid[y * w + x] = 1 if small.get_pixel(x, y).a > 0.1 else 0
	var out := PackedVector2Array()
	for corner in [Vector2i(0, 1), Vector2i(1, 1), Vector2i(0, 0), Vector2i(1, 0)]:
		var best := Vector2.ZERO
		for i in range(1, 11):
			for j in range(1, 11):
				var cut := Vector2(i, j) * 0.05
				if cut.x * cut.y > best.x * best.y and _cut_is_empty(solid, w, h, corner, cut):
					best = cut
		out.append(best)
	return out


## True if no solid pixel lies in the corner triangle (corner in image pixels: (0, 1) = bottom left).
func _cut_is_empty(solid: PackedByteArray, w: int, h: int, corner: Vector2i, cut: Vector2) -> bool:
	for y in range(h):
		for x in range(w):
			if solid[y * w + x] == 0:
				continue
			# Distance from the corner, as fractions of the card, measured towards the middle.
			var fx := (float(x) + 0.5) / w
			var fy := (float(y) + 0.5) / h
			var dx := fx if corner.x == 0 else 1.0 - fx
			var dy := fy if corner.y == 0 else 1.0 - fy
			if dx / cut.x + dy / cut.y < 1.0:
				return false
	return true


## Photographs one plant from one side: colour and normals, scaled down to the cell.
func _photograph(mesh: Mesh, yaw: float, cell: Vector2i) -> Dictionary:
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.rotation.y = yaw
	_vp.add_child(mi)
	for s in range(mesh.get_surface_count()):
		var m := mesh.surface_get_material(s)
		if m is ShaderMaterial:
			# Still leaves: the colour and normal pictures must line up.
			(m as ShaderMaterial).set_shader_parameter("wind_strength", 0.0)
			(m as ShaderMaterial).set_shader_parameter("sway_strength", 0.0)
	var box := Transform3D(Basis(Vector3.UP, yaw), Vector3.ZERO) * mesh.get_aabb()
	var big := cell * 2
	_vp.size = big
	var aspect := float(cell.x) / float(cell.y)
	var view_h := maxf(box.size.y, box.size.x / aspect) * 1.03
	var view_w := view_h * aspect
	_cam.size = view_h
	var centre_x := box.get_center().x
	var bottom := box.position.y - view_h * 0.01
	_cam.position = Vector3(centre_x, bottom + view_h * 0.5, 150.0)
	# Colour: the leaves through the stand-in (their shading tricks are relit on the phone), the
	# wood and blossoms with their own materials under the white sky light.
	for s in range(mesh.get_surface_count()):
		var leaf := _normal_material(mesh.surface_get_material(s), true)
		if leaf != null:
			mi.set_surface_override_material(s, leaf)
	var albedo := await _grab(big)
	for s in range(mesh.get_surface_count()):
		mi.set_surface_override_material(s, _normal_material(mesh.surface_get_material(s), false))
	var normal := await _grab(big)
	mi.queue_free()
	for img in [albedo, normal]:
		img.fix_alpha_edges()
		img.resize(cell.x, cell.y, Image.INTERPOLATE_LANCZOS)
	var mpp := view_h / float(cell.y)
	return {"albedo": albedo, "normal": normal, "used": albedo.get_used_rect(), "metres_per_pixel": mpp,
		"trunk_px": (0.0 - (centre_x - view_w * 0.5)) / mpp, "top": bottom + view_h}


func _grab(size: Vector2i) -> Image:
	for _i in range(3):
		await process_frame
	RenderingServer.force_draw(false)
	var img := _vp.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	return img


## The stand-in for a surface, cut out like the leaves it replaces: its normals, or (`colour`)
## a leaf surface's plain colour. Null for a surface that keeps its own colour material.
func _normal_material(src: Material, colour: bool) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = _normal_shader
	mat.set_shader_parameter("colour", colour)
	var sm := src as ShaderMaterial
	var path := sm.shader.resource_path if sm != null and sm.shader != null else ""
	if path.ends_with("leaf_spray.gdshader"):
		mat.set_shader_parameter("alpha_tex", sm.get_shader_parameter("spray_color"))
		mat.set_shader_parameter("colour_tex", sm.get_shader_parameter("spray_color"))
		mat.set_shader_parameter("alpha_mode", 1)
	elif path.ends_with("tree/leaf.gdshader"):
		mat.set_shader_parameter("colour_tex", sm.get_shader_parameter("leaf_texture"))
		# The photo leaves' own warm-up (leaf.gdshader); their clusters carry no occlusion.
		mat.set_shader_parameter("extra_tint", Vector3(0.88, 0.92, 0.78))
		mat.set_shader_parameter("ao_share", 0.0)
		if sm.get_shader_parameter("use_atlas"):
			mat.set_shader_parameter("alpha_tex", sm.get_shader_parameter("leaf_opacity"))
			mat.set_shader_parameter("alpha_mode", 2)
		else:
			mat.set_shader_parameter("alpha_tex", sm.get_shader_parameter("leaf_texture"))
			mat.set_shader_parameter("alpha_mode", 1)
	elif colour:
		return null
	if path != "":
		mat.set_shader_parameter("tint_mul", sm.get_shader_parameter("tint_mul"))
	return mat
