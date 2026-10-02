extends SceneTree
## Renders the HUD's picture icons into ui/icons/*.png: the journal (a leather volume), the shed
## (a small wooden hut built here from the shed's plank textures), the shears (hand secateurs
## built here from swept tubes and extruded outlines), the camera (a vintage rangefinder) and the
## hand compass (brass case and dial, the needle on its own) and the sunset (0.8.2.4: a glowing
## sun half sunk behind a grassy hill with a small tree on it, built here). Warm light, transparent background,
## a soft ink edge and shadow so they sit on the paper HUD. CC0 models from Poly Haven live in
## tools/icon_models (a .gdignore keeps them out of the game; they are read here with GLTFDocument).
## Needs a window (the headless driver draws nothing):
##   godot --path . -s tools/render_icons.gd [-- --only=shears] [-- --raw]

const RENDER := 1024
const OUT := 256
const COMPASS_OUT := 320
const OUT_DIR := "res://ui/icons"
const MODELS := "res://tools/icon_models"
const INK := Color(0.16, 0.11, 0.07)

var _vp: SubViewport
var _cam: Camera3D
var _stage: Node3D
var _only := ""
var _raw := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--only="):
			_only = a.substr(7)
		elif a == "--raw":
			_raw = true
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	_build_studio()
	_run.call_deferred()


func _run() -> void:
	for job in ["journal", "shed", "shears", "camera", "compass", "sunset"]:
		if _only != "" and job != _only:
			continue
		await call("_icon_" + job)
		print("rendered ", job)
	quit()


# --- the studio ---------------------------------------------------------------------

func _build_studio() -> void:
	_vp = SubViewport.new()
	_vp.size = Vector2i(RENDER, RENDER)
	_vp.transparent_bg = true
	_vp.own_world_3d = true
	_vp.msaa_3d = Viewport.MSAA_8X
	_vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(_vp)
	var env := Environment.new()
	env.background_mode = Environment.BG_CLEAR_COLOR
	var sky := Sky.new()
	var sm := ProceduralSkyMaterial.new()
	sm.sky_top_color = Color(0.55, 0.62, 0.72)
	sm.sky_horizon_color = Color(0.95, 0.82, 0.62)
	sm.ground_bottom_color = Color(0.25, 0.18, 0.12)
	sm.ground_horizon_color = Color(0.6, 0.48, 0.35)
	sky.sky_material = sm
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.55
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.tonemap_white = 6.0
	var we := WorldEnvironment.new()
	we.environment = env
	_vp.add_child(we)
	# Late afternoon light from the upper left, a cool fill and a warm rim from behind.
	var key := DirectionalLight3D.new()
	key.light_color = Color(1.0, 0.86, 0.66)
	key.light_energy = 2.2
	key.shadow_enabled = true
	key.rotation_degrees = Vector3(-50, -35, 0)
	_vp.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.light_color = Color(0.7, 0.78, 0.95)
	fill.light_energy = 0.45
	fill.rotation_degrees = Vector3(-20, 120, 0)
	_vp.add_child(fill)
	var rim := DirectionalLight3D.new()
	rim.light_color = Color(1.0, 0.8, 0.55)
	rim.light_energy = 1.1
	rim.rotation_degrees = Vector3(-25, 160, 0)
	_vp.add_child(rim)
	_cam = Camera3D.new()
	_cam.fov = 24.0
	_vp.add_child(_cam)
	_cam.make_current()


func _new_stage() -> Node3D:
	if _stage != null:
		_stage.queue_free()
	_stage = Node3D.new()
	_vp.add_child(_stage)
	return _stage


## Points the camera at the stage's bounds from a direction (yaw, pitch in degrees).
func _frame(yaw: float, pitch: float, fill: float = 1.0) -> void:
	var box := _bounds(_stage)
	var c := box.get_center()
	var r := box.size.length() * 0.5 / fill
	var dir := Basis.from_euler(Vector3(deg_to_rad(-pitch), deg_to_rad(yaw), 0)) * Vector3(0, 0, 1)
	_cam.projection = Camera3D.PROJECTION_PERSPECTIVE
	var dist := r / sin(deg_to_rad(_cam.fov * 0.5))
	_cam.position = c + dir * dist
	_cam.near = dist * 0.05
	_cam.far = dist * 4.0
	_cam.look_at(c, Vector3.UP)


func _bounds(n: Node) -> AABB:
	var box := AABB()
	var first := true
	for m in n.find_children("*", "MeshInstance3D", true, false):
		var mi := m as MeshInstance3D
		if not mi.visible or mi.mesh == null:
			continue
		var b := mi.global_transform * mi.get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box


func _capture() -> Image:
	for i in range(8):
		await process_frame
	await RenderingServer.frame_post_draw
	var img := _vp.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	return img


func _save(img: Image, name: String) -> void:
	img.save_png(ProjectSettings.globalize_path(OUT_DIR.path_join(name + ".png")))


## Crops to the object, fits it into `out` pixels (its longer side) and adds the ink edge and
## shadow. The icons keep the object's own shape so a wide one fills a wide button; the pointer
## stays square.
func _finish(img: Image, out: int = OUT, square: bool = false) -> Image:
	if _raw:
		return img
	var used := img.get_used_rect()
	var pad := int(out * 0.045)
	var k := float(out - pad * 2) / maxi(used.size.x, used.size.y)
	var fit := Vector2i(maxi(1, roundi(used.size.x * k)), maxi(1, roundi(used.size.y * k)))
	var obj := img.get_region(used)
	obj.resize(fit.x, fit.y, Image.INTERPOLATE_LANCZOS)
	var dims := Vector2i(out, out) if square else fit + Vector2i(pad, pad) * 2
	var icon := Image.create(dims.x, dims.y, false, Image.FORMAT_RGBA8)
	icon.blit_rect(obj, Rect2i(Vector2i.ZERO, fit), (dims - fit) / 2)
	_unpremultiply_edges(icon)
	return _ink_edge(icon)


## Lanczos rings a little at the alpha edge; clamp the colour there.
func _unpremultiply_edges(img: Image) -> void:
	for y in range(img.get_height()):
		for x in range(img.get_width()):
			var c := img.get_pixel(x, y)
			if c.a < 0.02:
				img.set_pixel(x, y, Color(0, 0, 0, 0))
			else:
				img.set_pixel(x, y, Color(clampf(c.r, 0, 1), clampf(c.g, 0, 1), clampf(c.b, 0, 1), clampf(c.a, 0, 1)))


## A thin ink line around the object and a soft shadow down and to the right, under it.
func _ink_edge(icon: Image) -> Image:
	var w := icon.get_width()
	var h := icon.get_height()
	var alpha := PackedFloat32Array()
	alpha.resize(w * h)
	for y in range(h):
		for x in range(w):
			alpha[y * w + x] = icon.get_pixel(x, y).a
	var edge := _dilate(alpha, w, h, 2)
	var shadow := _blur(_dilate(alpha, w, h, 2), w, h, 5)
	var out := Image.create(w, h, false, Image.FORMAT_RGBA8)
	var off := Vector2i(int(w * 0.012) + 2, int(w * 0.02) + 3)
	for y in range(h):
		for x in range(w):
			var sx := x - off.x
			var sy := y - off.y
			var s := 0.0
			if sx >= 0 and sy >= 0 and sx < w and sy < h:
				s = shadow[sy * w + sx] * 0.42
			var base := Color(0.1, 0.07, 0.04, s)
			base = _over(Color(INK, edge[y * w + x] * 0.85), base)
			out.set_pixel(x, y, _over(icon.get_pixel(x, y), base))
	return out


func _over(top: Color, below: Color) -> Color:
	var a := top.a + below.a * (1.0 - top.a)
	if a <= 0.0001:
		return Color(0, 0, 0, 0)
	var rgb := (Vector3(top.r, top.g, top.b) * top.a + Vector3(below.r, below.g, below.b) * below.a * (1.0 - top.a)) / a
	return Color(rgb.x, rgb.y, rgb.z, a)


func _dilate(a: PackedFloat32Array, w: int, h: int, r: int) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(w * h)
	for y in range(h):
		for x in range(w):
			var m := 0.0
			for dy in range(-r, r + 1):
				for dx in range(-r, r + 1):
					if dx * dx + dy * dy > r * r + 1:
						continue
					var xx := x + dx
					var yy := y + dy
					if xx >= 0 and yy >= 0 and xx < w and yy < h:
						m = maxf(m, a[yy * w + xx])
			out[y * w + x] = m
	return out


func _blur(a: PackedFloat32Array, w: int, h: int, r: int) -> PackedFloat32Array:
	var tmp := PackedFloat32Array()
	tmp.resize(w * h)
	var out := PackedFloat32Array()
	out.resize(w * h)
	for y in range(h):
		for x in range(w):
			var s := 0.0
			for d in range(-r, r + 1):
				s += a[y * w + clampi(x + d, 0, w - 1)]
			tmp[y * w + x] = s / (2 * r + 1)
	for y in range(h):
		for x in range(w):
			var s := 0.0
			for d in range(-r, r + 1):
				s += tmp[clampi(y + d, 0, h - 1) * w + x]
			out[y * w + x] = s / (2 * r + 1)
	return out


func _load_gltf(name: String) -> Node3D:
	var path := ProjectSettings.globalize_path(MODELS.path_join(name).path_join(name + ".gltf"))
	var doc := GLTFDocument.new()
	var st := GLTFState.new()
	var err := doc.append_from_file(path, st, 0, path.get_base_dir())
	assert(err == OK, "could not read " + path)
	return doc.generate_scene(st) as Node3D


func _mat(c: Color, rough: float, metal: float = 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	m.metallic = metal
	return m


# --- the icons ----------------------------------------------------------------------

## The journal: one leather volume lying flat, the page edges towards the viewer (the spine's
## print away), with a red ribbon bookmark.
func _icon_journal() -> void:
	var s := _new_stage()
	var books := _load_gltf("book_encyclopedia_set_01")
	s.add_child(books)
	var book: Node3D
	for n in books.get_children():
		if n.name.ends_with("book03"):
			book = n
	# Lying flat, the front cover up, spine to the left.
	books.remove_child(book)
	s.remove_child(books)
	books.free()
	s.add_child(book)
	# A chunky journal: the volume made thicker, lying flat, the spine towards the viewer.
	book.transform = Transform3D(Basis.from_euler(Vector3(0, 0, deg_to_rad(90))) * Basis.from_scale(Vector3(1.8, 1, 1)), Vector3.ZERO)
	var box := _bounds(book)
	var mid := box.get_center().y
	var thick := box.size.y
	# Darker, redder leather than the encyclopedia's (it faces the warm light here).
	# The encyclopedia's cover is polished; make it a worn, matte and darker leather.
	var mi := book as MeshInstance3D
	for i in range(mi.mesh.get_surface_count()):
		var m := mi.mesh.surface_get_material(i) as StandardMaterial3D
		if m != null and m.resource_name.ends_with("cover"):
			m = m.duplicate() as StandardMaterial3D
			m.albedo_color = Color(0.8, 0.6, 0.48)
			m.roughness_texture = null
			m.roughness = 0.62
			m.metallic_specular = 0.3
			mi.set_surface_override_material(i, m)
	# A red ribbon bookmark out at the bottom.
	var ribbon := MeshInstance3D.new()
	var rm := BoxMesh.new()
	rm.size = Vector3(0.06, 0.002, 0.012)
	ribbon.mesh = rm
	ribbon.material_override = _mat(Color(0.6, 0.08, 0.06), 0.6)
	ribbon.position = Vector3(box.end.x + 0.012, mid - thick * 0.1, box.get_center().z - 0.02)
	ribbon.rotation.y = 0.35
	s.add_child(ribbon)
	_frame(150, 38)
	_save(_finish(await _capture()), "journal")


## The camera: a vintage rangefinder, strap left off.
func _icon_camera() -> void:
	var s := _new_stage()
	var cam := _load_gltf("Camera_01")
	s.add_child(cam)
	for n in cam.get_children():
		(n as Node3D).visible = not n.name.ends_with("strap")
	_frame(-28, 16)
	_save(_finish(await _capture()), "camera")


# --- the shed: a small wooden hut ---------------------------------------------------

func _planks(name: String, scale: float, tint: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	var base := "res://assets/wood/%s_%s_1k.jpg"
	m.albedo_texture = load(base % [name, "diff"])
	m.albedo_color = tint
	m.normal_enabled = true
	m.normal_texture = load(base % [name, "nor_gl"])
	m.roughness_texture = load(base % [name, "rough"])
	m.roughness = 1.0
	m.uv1_triplanar = true
	m.uv1_world_triplanar = true
	m.uv1_scale = Vector3.ONE * scale
	return m


func _add_box(size: Vector3, at: Vector3, mat: Material, rot_z: float = 0.0) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	m.mesh = b
	m.material_override = mat
	m.position = at
	m.rotation.z = rot_z
	_stage.add_child(m)
	return m


func _icon_shed() -> void:
	var s := _new_stage()
	const W := 1.0
	const D := 0.8
	const H := 0.72
	const RISE := 0.42
	var walls := _planks("weathered_planks", 1.6, Color(0.9, 0.84, 0.76))
	var trim := _planks("old_planks_02", 2.5, Color(0.95, 0.9, 0.82))
	var roof := _planks("old_planks_02", 1.4, Color(0.5, 0.45, 0.4))
	var door := _planks("wood_table_worn", 2.0, Color(0.7, 0.55, 0.42))
	# A low stone footing, the walls and the gable (a prism over the whole depth).
	_add_box(Vector3(W + 0.08, 0.06, D + 0.08), Vector3(0, 0.03, 0), _mat(Color(0.42, 0.4, 0.37), 0.95))
	_add_box(Vector3(W, H, D), Vector3(0, 0.06 + H * 0.5, 0), walls)
	var gable := MeshInstance3D.new()
	var pm := PrismMesh.new()
	pm.size = Vector3(W, RISE, D)
	gable.mesh = pm
	gable.material_override = walls
	gable.position = Vector3(0, 0.06 + H + RISE * 0.5, 0)
	s.add_child(gable)
	# The roof: two plank panels with an overhang and a ridge board.
	var a := atan2(RISE, W * 0.5)
	var ridge := Vector3(0, 0.06 + H + RISE, 0)
	var length := Vector2(W * 0.5, RISE).length() + 0.14
	for side in [-1.0, 1.0]:
		var down := Vector3(cos(a) * side, -sin(a), 0)
		var normal := Vector3(sin(a) * side, cos(a), 0)
		var c: Vector3 = ridge + down * length * 0.5 + normal * 0.035
		_add_box(Vector3(length, 0.05, D + 0.2), c, roof, -a * side)
	_add_box(Vector3(0.07, 0.06, D + 0.22), ridge + Vector3(0, 0.06, 0), trim)
	# Corner boards.
	for x in [-1.0, 1.0]:
		for z in [-1.0, 1.0]:
			_add_box(Vector3(0.05, H, 0.05), Vector3(x * (W * 0.5 + 0.005), 0.06 + H * 0.5, z * (D * 0.5 + 0.005)), trim)
	# The door with its Z brace and a brass knob, on the front at the left.
	var fz := D * 0.5 + 0.015
	_add_box(Vector3(0.34, 0.6, 0.03), Vector3(-0.2, 0.06 + 0.3, fz), door)
	var brace := _planks("wood_table_worn", 2.0, Color(0.6, 0.46, 0.35))
	_add_box(Vector3(0.3, 0.05, 0.02), Vector3(-0.2, 0.06 + 0.52, fz + 0.02), brace)
	_add_box(Vector3(0.3, 0.05, 0.02), Vector3(-0.2, 0.06 + 0.08, fz + 0.02), brace)
	_add_box(Vector3(0.05, 0.5, 0.02), Vector3(-0.2, 0.06 + 0.3, fz + 0.02), brace, -0.62)
	var knob := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.02
	sm.height = 0.04
	knob.mesh = sm
	knob.material_override = _mat(Color(0.8, 0.6, 0.3), 0.3, 1.0)
	knob.position = Vector3(-0.07, 0.06 + 0.3, fz + 0.035)
	s.add_child(knob)
	# A small four-pane window at the right.
	_add_box(Vector3(0.28, 0.24, 0.03), Vector3(0.22, 0.06 + 0.42, fz), trim)
	var glass := _mat(Color(0.12, 0.16, 0.18), 0.12, 0.6)
	_add_box(Vector3(0.22, 0.18, 0.03), Vector3(0.22, 0.06 + 0.42, fz + 0.006), glass)
	_add_box(Vector3(0.022, 0.18, 0.03), Vector3(0.22, 0.06 + 0.42, fz + 0.012), trim)
	_add_box(Vector3(0.22, 0.022, 0.03), Vector3(0.22, 0.06 + 0.42, fz + 0.012), trim)
	_add_box(Vector3(0.32, 0.03, 0.06), Vector3(0.22, 0.06 + 0.29, fz + 0.02), trim)
	_frame(-32, 18)
	_save(_finish(await _capture()), "shed")


# --- the shears: hand pruning secateurs ----------------------------------------------

## Samples a Catmull-Rom curve through the points (in the x-z plane at height y).
func _spline(pts: Array, samples: int, y: float = 0.0) -> PackedVector3Array:
	var out := PackedVector3Array()
	var n := pts.size()
	for i in range(samples + 1):
		var t := float(i) / samples * (n - 1)
		var k := mini(int(t), n - 2)
		var f := t - k
		var p0: Vector2 = pts[maxi(k - 1, 0)]
		var p1: Vector2 = pts[k]
		var p2: Vector2 = pts[k + 1]
		var p3: Vector2 = pts[mini(k + 2, n - 1)]
		var p := 0.5 * ((2.0 * p1) + (-p0 + p2) * f + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * f * f + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * f * f * f)
		out.append(Vector3(p.x, y, p.y))
	return out


## A tube along a path, with an elliptic section (in-plane width, height) per sample.
func _tube(path: PackedVector3Array, radii: Array, mat: Material, sides: int = 14) -> MeshInstance3D:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var rings: Array = []
	var normals: Array = []
	for i in range(path.size()):
		var t := (path[mini(i + 1, path.size() - 1)] - path[maxi(i - 1, 0)]).normalized()
		var up := Vector3.UP
		if absf(t.dot(up)) > 0.95:
			up = Vector3.RIGHT
		var side := t.cross(up).normalized()
		var up2 := side.cross(t).normalized()
		var r: Vector2 = radii[i]
		var ring := PackedVector3Array()
		var nr := PackedVector3Array()
		for k in range(sides):
			var a := TAU * k / sides
			ring.append(path[i] + side * cos(a) * r.x + up2 * sin(a) * r.y)
			nr.append((side * cos(a) / maxf(r.x, 1e-4) + up2 * sin(a) / maxf(r.y, 1e-4)).normalized())
		rings.append(ring)
		normals.append(nr)
	for i in range(path.size() - 1):
		for k in range(sides):
			var k2 := (k + 1) % sides
			for v in [[i, k], [i + 1, k2], [i + 1, k], [i, k], [i, k2], [i + 1, k2]]:
				st.set_normal(normals[v[0]][v[1]])
				st.add_vertex(rings[v[0]][v[1]])
	var m := MeshInstance3D.new()
	m.mesh = st.commit()
	m.material_override = mat
	_stage.add_child(m)
	return m


## A flat part: the outline (x-z) extruded between two heights.
func _slab(outline: PackedVector2Array, y0: float, y1: float, mat: Material) -> MeshInstance3D:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var tris := Geometry2D.triangulate_polygon(outline)
	for i in range(0, tris.size(), 3):
		for k in [0, 1, 2]:
			var p := outline[tris[i + k]]
			st.set_normal(Vector3.UP)
			st.add_vertex(Vector3(p.x, y1, p.y))
		for k in [0, 2, 1]:
			var p := outline[tris[i + k]]
			st.set_normal(Vector3.DOWN)
			st.add_vertex(Vector3(p.x, y0, p.y))
	var n := outline.size()
	var area := 0.0
	for i in range(n):
		area += outline[i].cross(outline[(i + 1) % n])
	for i in range(n):
		var a := outline[i]
		var b := outline[(i + 1) % n]
		var e := (b - a).normalized()
		var out2 := Vector2(e.y, -e.x) * (1.0 if area > 0.0 else -1.0)
		var nrm := Vector3(out2.x, 0.0, out2.y)
		for v in [Vector3(a.x, y0, a.y), Vector3(b.x, y0, b.y), Vector3(b.x, y1, b.y), Vector3(a.x, y0, a.y), Vector3(b.x, y1, b.y), Vector3(a.x, y1, a.y)]:
			st.set_normal(nrm)
			st.add_vertex(v)
	var m := MeshInstance3D.new()
	m.mesh = st.commit()
	m.material_override = mat
	_stage.add_child(m)
	return m


## An outline around a curved centreline, with a width to each side per sample.
func _band(center: PackedVector3Array, left: Array, right: Array) -> PackedVector2Array:
	var l := PackedVector2Array()
	var r := PackedVector2Array()
	var n := center.size()
	for i in range(n):
		var p := Vector2(center[i].x, center[i].z)
		var q := Vector2(center[mini(i + 1, n - 1)].x, center[mini(i + 1, n - 1)].z)
		var o := Vector2(center[maxi(i - 1, 0)].x, center[maxi(i - 1, 0)].z)
		var t := (q - o).normalized()
		var nrm := Vector2(-t.y, t.x)
		l.append(p + nrm * float(left[i]))
		r.append(p - nrm * float(right[i]))
	r.reverse()
	l.append_array(r)
	return l


func _build_secateurs() -> void:
	var steel := _mat(Color(0.86, 0.86, 0.88), 0.28, 0.75)
	var dark_steel := _mat(Color(0.45, 0.45, 0.47), 0.36, 0.9)
	var alu := _mat(Color(0.82, 0.82, 0.84), 0.3, 0.85)
	var red := _mat(Color(0.72, 0.07, 0.05), 0.4)
	var brass := _mat(Color(0.85, 0.66, 0.34), 0.3, 1.0)
	const N := 24
	# The cutting blade: a slim crescent with its point towards -z, curving to the left.
	var blade_c := _spline([Vector2(0.02, 0.1), Vector2(-0.08, -0.4), Vector2(-0.06, -0.8), Vector2(0.06, -1.1)], N)
	var bl: Array = []
	var br: Array = []
	for i in range(N + 1):
		var t := float(i) / N
		bl.append(lerpf(0.17, 0.004, pow(t, 1.3)))
		br.append(lerpf(0.1, 0.004, t))
	_slab(_band(blade_c, bl, br), 0.02, 0.055, steel)
	# The hook (anvil) under it, shorter and blunt, curving to the right.
	var hook_c := _spline([Vector2(-0.02, 0.1), Vector2(0.1, -0.3), Vector2(0.12, -0.62), Vector2(0.04, -0.78)], N)
	var hl: Array = []
	var hr: Array = []
	for i in range(N + 1):
		var t := float(i) / N
		hl.append(lerpf(0.06, 0.03, t))
		hr.append(lerpf(0.16, 0.05, t))
	_slab(_band(hook_c, hl, hr), -0.04, 0.015, dark_steel)
	# The two handles, crossing at the pivot: metal necks, then the red grips.
	var handles := [
		[Vector2(-0.02, 0.0), Vector2(0.12, 0.35), Vector2(0.22, 0.8), Vector2(0.26, 1.3), Vector2(0.22, 1.7)],
		[Vector2(0.02, 0.0), Vector2(-0.1, 0.35), Vector2(-0.14, 0.8), Vector2(-0.12, 1.3), Vector2(-0.02, 1.68)],
	]
	for h in range(2):
		var y := 0.035 if h == 0 else -0.015
		var path := _spline(handles[h], 40, y)
		var neck := PackedVector3Array()
		var grip := PackedVector3Array()
		var nr: Array = []
		var gr: Array = []
		for i in range(path.size()):
			var t := float(i) / (path.size() - 1)
			if t <= 0.3:
				neck.append(path[i])
				nr.append(Vector2(0.07, 0.035))
			if t >= 0.26:
				grip.append(path[i])
				var end := clampf((1.0 - t) / 0.05, 0.0, 1.0)
				var cap := sqrt(1.0 - pow(1.0 - end, 2.0))
				var start := clampf((t - 0.26) / 0.03, 0.0, 1.0)
				gr.append(Vector2(0.085, 0.06) * maxf(cap, 0.05) * lerpf(0.85, 1.0, start))
		_tube(neck, nr, alu)
		_tube(grip, gr, red)
	# The pivot bolt with its slot.
	var bolt := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 0.065
	cm.bottom_radius = 0.065
	cm.height = 0.13
	bolt.mesh = cm
	bolt.material_override = brass
	bolt.position = Vector3(0, 0.02, 0.0)
	_stage.add_child(bolt)
	var slot := MeshInstance3D.new()
	var sb := BoxMesh.new()
	sb.size = Vector3(0.1, 0.02, 0.016)
	slot.mesh = sb
	slot.material_override = dark_steel
	slot.position = Vector3(0, 0.08, 0)
	slot.rotation.y = 0.6
	_stage.add_child(slot)
	# A coil spring between the handles.
	var coil := PackedVector3Array()
	var cr: Array = []
	for i in range(121):
		var t := float(i) / 120
		var a := t * TAU * 6.0
		coil.append(Vector3(lerpf(-0.08, 0.14, t), 0.01 + sin(a) * 0.035, 0.36 + cos(a) * 0.035))
		cr.append(Vector2(0.011, 0.011))
	_tube(coil, cr, steel, 8)


func _icon_shears() -> void:
	_new_stage()
	_build_secateurs()
	_stage.rotation_degrees = Vector3(0, -62, 0)
	_frame(0, 62)
	_save(_finish(await _capture()), "shears")
	# The mouse pointer: the same shears, the blade's point at the top left.
	_stage.rotation_degrees = Vector3(0, 35, 0)
	_frame(0, 70)
	_save(_finish(await _capture(), 64, true), "shears_cursor")


# --- the sunset (0.8.2.4) -------------------------------------------------------------

## The sunset picture (run the rest of the day): a warm glowing sun half behind a rounded hill of
## real grass, short rays fanned over it, and a small dark tree on the hill's shoulder.
func _icon_sunset() -> void:
	var s := _new_stage()
	var glow := func(c: Color, e: float) -> StandardMaterial3D:
		var m := StandardMaterial3D.new()
		m.albedo_color = c
		m.emission_enabled = true
		m.emission = c
		m.emission_energy_multiplier = e
		m.roughness = 0.6
		return m
	# The sun: a sphere whose lower half the hill hides.
	var sun := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.55
	sm.height = 1.1
	sun.mesh = sm
	sun.material_override = glow.call(Color(1.0, 0.45, 0.13), 0.75)
	sun.position = Vector3(0.1, 0.0, -0.25)
	s.add_child(sun)
	# The rays: short tapered wedges in a fan over the horizon.
	for i in range(7):
		var a := deg_to_rad(18.0 + i * 24.0)
		var ray := MeshInstance3D.new()
		var pm := PrismMesh.new()
		pm.size = Vector3(0.11, 0.24, 0.04)
		ray.mesh = pm
		ray.material_override = glow.call(Color(1.0, 0.62, 0.2), 0.9)
		var dir := Vector3(cos(a), sin(a), 0.0)
		ray.position = sun.position + dir * 0.8
		# The prism's point (+y) away from the sun.
		ray.rotation.z = a - PI * 0.5
		s.add_child(ray)
	# The hill: a squashed dome of grass in front.
	var grass := StandardMaterial3D.new()
	grass.albedo_texture = load("res://assets/ground/Grass004_1K-JPG_Color.jpg")
	grass.normal_enabled = true
	grass.normal_texture = load("res://assets/ground/Grass004_1K-JPG_NormalGL.jpg")
	grass.albedo_color = Color(0.85, 0.95, 0.7)
	grass.roughness = 0.9
	grass.uv1_triplanar = true
	grass.uv1_scale = Vector3.ONE * 3.0
	var hill := MeshInstance3D.new()
	var hm := SphereMesh.new()
	hm.radius = 1.25
	hm.height = 1.25
	hm.is_hemisphere = true
	hill.mesh = hm
	hill.material_override = grass
	hill.scale = Vector3(0.95, 0.36, 0.5)
	hill.position = Vector3(0.0, -0.4, 0.2)
	s.add_child(hill)
	# A small tree on the left shoulder: a dark trunk and a round crown, lit warm from behind.
	var bark := _mat(Color(0.22, 0.14, 0.08), 0.9)
	var trunk := MeshInstance3D.new()
	var tm := CylinderMesh.new()
	tm.top_radius = 0.018
	tm.bottom_radius = 0.03
	tm.height = 0.26
	trunk.mesh = tm
	trunk.material_override = bark
	trunk.position = Vector3(-0.62, 0.04, 0.3)
	s.add_child(trunk)
	var crown := MeshInstance3D.new()
	var cm := SphereMesh.new()
	cm.radius = 0.15
	cm.height = 0.27
	crown.mesh = cm
	crown.material_override = _mat(Color(0.2, 0.36, 0.14), 0.85)
	crown.position = Vector3(-0.62, 0.24, 0.3)
	s.add_child(crown)
	_frame(0, 8, 1.05)
	_save(_finish(await _capture()), "sunset")


# --- the hand compass ----------------------------------------------------------------

## The brass case (lid off, ring at the top), its dial cut out on its own so it can turn, the
## needle on its own. The compass draws the glass over them (ui/compass.gd).
func _icon_compass() -> void:
	var s := _new_stage()
	var c := _load_gltf("seadogs_compass")
	s.add_child(c)
	var needle: Node3D
	for n in c.get_children():
		if n.name.ends_with("lid"):
			(n as Node3D).visible = false
		if n.name.ends_with("needle"):
			needle = n
	# Looking straight down; the ring (at +z in the model) at the top of the picture.
	needle.rotation = Vector3.ZERO
	const CASE_R := 0.0376
	const SHIFT := 0.011
	_cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	_cam.size = 0.118
	_cam.position = Vector3(0, 0.3, SHIFT)
	_cam.near = 0.01
	_cam.far = 1.0
	_cam.look_at(Vector3(0, 0, SHIFT), Vector3(0, 0, 1))
	needle.visible = false
	var body := await _capture()
	for n in c.get_children():
		(n as Node3D).visible = n == needle
	var nimg := await _capture()
	if _raw:
		_save(body, "compass_raw")
		_save(nimg, "needle_raw")
		return
	# The dial's centre in the render (the model's origin, SHIFT below the middle).
	var ppm := RENDER / _cam.size
	var centre := Vector2(RENDER * 0.5, RENDER * 0.5 + SHIFT * ppm)
	var dial_r := CASE_R * 0.86 * ppm
	var dial := Image.create(RENDER, RENDER, false, Image.FORMAT_RGBA8)
	for y in range(RENDER):
		for x in range(RENDER):
			var d := Vector2(x + 0.5, y + 0.5).distance_to(centre)
			if d < dial_r + 1.0:
				var col := body.get_pixel(x, y)
				col.a *= clampf(dial_r + 0.5 - d, 0.0, 1.0)
				dial.set_pixel(x, y, col)
	var scale := float(COMPASS_OUT) / RENDER
	_red_north(nimg, centre)
	# Dial and needle as squares centred on the dial, so they turn about their middle.
	var side := int(ceil(dial_r)) * 2 + 4
	var sq := Rect2i(Vector2i(centre) - Vector2i(side / 2, side / 2), Vector2i(side, side))
	dial = dial.get_region(sq)
	nimg = nimg.get_region(sq)
	var small := int(round(side * scale))
	body.resize(COMPASS_OUT, COMPASS_OUT, Image.INTERPOLATE_LANCZOS)
	for img in [body, dial, nimg]:
		if img != body:
			(img as Image).resize(small, small, Image.INTERPOLATE_LANCZOS)
		_unpremultiply_edges(img)
	# The dial's north is at the bottom of the render; turn dial and needle so it is up.
	for img in [dial, nimg]:
		(img as Image).rotate_180()
	_save(_ink_edge(body), "compass_case")
	_save(dial, "compass_dial")
	_save(nimg, "compass_needle")
	# Compass.DIAL_CENTRE in ui/compass.gd (fractions of the case picture).
	print("compass dial centre ", centre / RENDER, " dial size ", float(side) / RENDER)


## The needle's north half (towards the bottom of the render, where the dial's N is) in red.
func _red_north(img: Image, centre: Vector2) -> void:
	for y in range(int(centre.y) + 2, img.get_height()):
		for x in range(img.get_width()):
			var c := img.get_pixel(x, y)
			if c.a > 0.0:
				var l := c.get_luminance()
				img.set_pixel(x, y, Color(0.55 + l * 0.5, 0.1 + l * 0.2, 0.07 + l * 0.15, c.a))
