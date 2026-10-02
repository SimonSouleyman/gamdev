class_name LiveExport
extends Node
## Renders the layers of the live picture (specs/0.8.md section 6, LivePicture): the current
## tree, its grass and ground, seen through the album camera, with a clear (transparent) sky, once
## per light (dawn, day, dusk, night), into the user folder with a meta file. The phone's TreeLive
## library (wallpaper and screen saver) draws the sky itself and lays these over it.
##
## The layers are drawn in a world of their own (a SubViewport): the tree's mesh, its crown and
## the meadow grass share the game's meshes and materials (season, species tint and all), but
## nothing of the clearing edge, the forest, the shed or the HUD is there (broken list 14b), and
## the game's own view is never touched. Done in the morning (after the album photo) and at sunset
## (the day's growth), only when the tree has changed; the images are written on a worker thread.

## Tools and the phone turn it on; a PC game does not write the layers (nothing reads them there).
static var enabled: bool = Phone.is_available()
## Rendered this much larger, then scaled down (smooth leaf edges without MSAA).
const SUPERSAMPLE := 2
## WebP quality of the layers (0..1).
const WEBP_QUALITY := 0.9

var dir: String = LivePicture.DIR
var busy: bool = false
var _last_signature: String = ""
## Why the last refresh wrote nothing ("" after a good one); also printed (logcat tag godot).
var last_error: String = ""
## A layer read back blank is drawn again this many times before the refresh gives up.
const RETRIES := 2


## The lights, one per layer: where the light comes from (toward the light), its colour and
## strength, the fill, the exposure and the colour saturation. Day and golden light follow the
## game's sun (TreeView._update_sun): morning light from the east, evening from the west, the
## night a cool moonlit fill in which the tree still reads.
const LIGHTS := {
	"dawn": {"dir": Vector3(0.93, 0.2, 0.3), "color": Color(1.0, 0.72, 0.45), "energy": 1.5, "ambient": Color(0.62, 0.62, 0.7), "ambient_energy": 0.75, "exposure": 1.2, "saturation": 1.0, "haze": Color(0.86, 0.74, 0.62)},
	"day": {"dir": Vector3(0.55, 0.7, 0.45), "color": Color(1.0, 0.96, 0.9), "energy": 1.55, "ambient": Color(0.58, 0.64, 0.72), "ambient_energy": 0.8, "exposure": 1.1, "saturation": 1.0, "haze": Color(0.62, 0.74, 0.86)},
	"dusk": {"dir": Vector3(-0.93, 0.2, 0.3), "color": Color(1.0, 0.68, 0.4), "energy": 1.5, "ambient": Color(0.66, 0.6, 0.66), "ambient_energy": 0.75, "exposure": 1.2, "saturation": 1.0, "haze": Color(0.86, 0.7, 0.58)},
	"night": {"dir": Vector3(-0.3, 0.8, 0.5), "color": Color(0.6, 0.66, 0.9), "energy": 0.35, "ambient": Color(0.42, 0.5, 0.74), "ambient_energy": 0.42, "exposure": 1.0, "saturation": 0.6, "haze": Color(0.12, 0.15, 0.24)},
}


## Renders and writes the layers if the tree changed since the last time (or `force`). Returns
## true once the new picture is on disk. Safe to call any time by day; a call while one runs is
## ignored.
func refresh(view: TreeView, state: GameState, force: bool = false) -> bool:
	if busy or view == null or state == null:
		return false
	# A seed has no tree to show yet: the last picture (or the phone's sapling) stays until the
	# first shoot is up (0.8.2.7: a planting wrote a meadow without a tree).
	if state.is_seed():
		return false
	var sig := LivePicture.signature(state.sim.species.id, state.sim.graph.size(), state.sim.height(), state.day_number(), view.season)
	if not force and sig == _last_signature:
		return false
	busy = true
	last_error = ""
	var ok := await _render(view, state)
	if ok:
		_last_signature = sig
		print("live picture: written (day %d, %d nodes)" % [state.day_number(), state.sim.graph.size()])
	else:
		if last_error == "":
			last_error = "not written"
		print("live picture: kept the last one (%s)" % last_error)
	busy = false
	return ok


func _render(view: TreeView, state: GameState) -> bool:
	var size := LivePicture.LAYER_SIZE * SUPERSAMPLE
	var sv := SubViewport.new()
	sv.size = size
	sv.transparent_bg = true
	sv.own_world_3d = true
	sv.render_target_update_mode = SubViewport.UPDATE_DISABLED
	add_child(sv)
	var cam := Camera3D.new()
	cam.near = 0.05
	cam.far = 200.0
	sv.add_child(cam)
	cam.current = true
	frame_camera(cam, Vector2(size), state.sim.height(), view.crown_width())
	var env := Environment.new()
	env.background_mode = Environment.BG_CLEAR_COLOR
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.adjustment_enabled = true
	env.adjustment_contrast = 1.12
	cam.environment = env
	var sun := DirectionalLight3D.new()
	sun.shadow_enabled = true
	sun.shadow_blur = 2.5
	sun.shadow_opacity = 0.75
	sun.directional_shadow_max_distance = maxf(30.0, cam.global_position.length() * 2.0)
	sv.add_child(sun)
	# No grass between the camera and the tree (it would stand huge in front of the lens).
	_add_scene(sv, view, Vector2(cam.global_position.x, cam.global_position.z).length())
	# A soft haze over the far meadow, so the grass line fades into the sky's foot instead of
	# ending in a hard dark band.
	var cam_dist := Vector2(cam.global_position.x, cam.global_position.z).length()
	env.fog_enabled = true
	env.fog_mode = Environment.FOG_MODE_DEPTH
	env.fog_depth_begin = cam_dist + 6.0
	env.fog_depth_end = cam_dist + 90.0
	env.fog_depth_curve = 0.8
	env.fog_density = 0.85
	# The haze at the horizon no brighter than the sky's foot above it (0.8 review: the fully hazed
	# far ground drew a near-white band along the horizon, the tone mapper lifting it).
	env.fog_light_energy = 0.72
	var foot := cam.unproject_position(Vector3.ZERO)
	var ground_y := clampf(foot.y / float(size.y), 0.05, 1.0)
	var expect := expected_shape(cam, Vector2(size), state.sim.height())
	var images := {}
	for name in LivePicture.LAYERS:
		var l: Dictionary = LIGHTS[name]
		var d: Vector3 = (l["dir"] as Vector3).normalized()
		sun.look_at_from_position(d * 20.0, Vector3.ZERO, Vector3.UP)
		sun.light_color = l["color"]
		sun.light_energy = l["energy"]
		env.ambient_light_color = l["ambient"]
		env.ambient_light_energy = l["ambient_energy"]
		env.tonemap_exposure = l["exposure"]
		env.adjustment_saturation = l["saturation"]
		env.fog_light_color = l["haze"]
		# 0.8.2.7 (phone: the wallpaper showed only its sky): a layer read back blank was written
		# all the same, and the phone drew the transparent layers over its sky. Now each layer is
		# read two frames after its draw and checked (ground and, if it stands above the horizon,
		# the tree); a blank one is drawn again, and if it stays blank nothing is written: the
		# last good picture stays, and the next trigger tries again.
		var img: Image = null
		var why := ""
		for attempt in range(RETRIES + 1):
			sv.render_target_update_mode = SubViewport.UPDATE_ONCE
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			img = sv.get_texture().get_image()
			why = check_layer(img, expect["tree_top"], expect["horizon"])
			if why == "":
				break
			print("live picture: layer %s %s (try %d)" % [name, why, attempt + 1])
		if why != "":
			sv.queue_free()
			last_error = "%s: %s" % [name, why]
			return false
		images[name] = img
	sv.queue_free()
	# The clear sky's pixels take their opaque neighbours' colour before the resize (0.8 review:
	# the cubic filter mixed the clear colour into the edge, a pale seam along the horizon).
	# 0.8.2.2: on a worker thread (on the phone these two were most of the long frames).
	var task := WorkerThreadPool.add_task(func() -> void:
		for name in images:
			var im: Image = images[name]
			im.fix_alpha_edges()
			im.resize(LivePicture.LAYER_SIZE.x, LivePicture.LAYER_SIZE.y, Image.INTERPOLATE_CUBIC))
	var tree := Engine.get_main_loop() as SceneTree
	while not WorkerThreadPool.is_task_completed(task):
		await tree.process_frame
	WorkerThreadPool.wait_for_task_completion(task)
	var shape := measure(images["day"])
	var stamp := int(Time.get_unix_time_from_system())
	var files := {}
	for name in LivePicture.LAYERS:
		files[name] = "tree_%d_%s.webp" % [stamp, name]
	var meta := LivePicture.make_meta(state.sim.species.id, state.day_number(), state.sim.height(), files,
		ground_y, minf(shape["crown_top"], ground_y - 0.02), shape["horizon_y"], shape["ground_color"], stamp)
	return await write(dir, images, meta)


## Where the camera shows the tree's top and the horizon, as shares of the layer's height.
static func expected_shape(cam: Camera3D, size: Vector2, height: float) -> Dictionary:
	var top := cam.unproject_position(Vector3(0, maxf(height, 0.0), 0)).y / size.y
	var ahead := -cam.global_transform.basis.z
	ahead.y = 0.0
	ahead = ahead.normalized() if ahead.length() > 0.001 else Vector3.FORWARD
	var far := cam.global_position + ahead * 1000.0
	var horizon := cam.unproject_position(Vector3(far.x, 0.0, far.z)).y / size.y
	return {"tree_top": top, "horizon": horizon}


## Above the horizon only the tree is opaque (the sky is clear); a tree whose top stands at least
## TREE_ABOVE of the height above the horizon must show at least TREE_MIN_SHARE of the samples
## there opaque. Below the horizon the meadow is solid: at least GROUND_MIN_SHARE of the samples
## in the bottom tenth.
const TREE_ABOVE := 0.03
const TREE_MIN_SHARE := 0.002
const GROUND_MIN_SHARE := 0.5


## "" when a layer read back from the renderer shows the ground and (if it reaches above the
## horizon) the tree; else what is missing. `tree_top` and `horizon` as shares of the height
## (expected_shape). Samples every few pixels, cheap enough for the main thread.
static func check_layer(img: Image, tree_top: float, horizon: float) -> String:
	if img == null or img.is_empty():
		return "is empty"
	var w := img.get_width()
	var h := img.get_height()
	var step := maxi(1, w / 135)
	var ground := 0
	var ground_n := 0
	for y in range(int(h * 0.9), h, step):
		for x in range(0, w, step):
			ground_n += 1
			if img.get_pixel(x, y).a > 0.5:
				ground += 1
	if ground_n == 0 or float(ground) / ground_n < GROUND_MIN_SHARE:
		return "is blank (no ground: %d of %d)" % [ground, ground_n]
	if horizon - tree_top >= TREE_ABOVE:
		var tree := 0
		var tree_n := 0
		for y in range(maxi(0, int(tree_top * h)), mini(h, int((horizon - 0.01) * h)), step):
			for x in range(0, w, step):
				tree_n += 1
				if img.get_pixel(x, y).a > 0.5:
					tree += 1
		if tree_n > 0 and float(tree) / tree_n < TREE_MIN_SHARE:
			return "has no tree (%d of %d above the horizon)" % [tree, tree_n]
	return ""


## Checks a written picture as the phone reads it: "" when the meta parses, every layer and
## cloud it names lies beside it and decodes at the meta's size, and each layer shows the ground
## and the tree above the horizon (crown_top to horizon_y); else what is wrong. Tools and tests.
static func verify_dir(from_dir: String) -> String:
	var meta := LivePicture.read_meta(from_dir)
	if meta.is_empty():
		return "no readable %s" % LivePicture.META
	var size: Vector2i = meta["size"]
	for name in LivePicture.LAYERS:
		var path := from_dir.path_join(str(meta["layers"][name]))
		if not FileAccess.file_exists(path):
			return "layer %s: %s is missing" % [name, meta["layers"][name]]
		var img := Image.load_from_file(ProjectSettings.globalize_path(path))
		if img == null or img.get_size() != size:
			return "layer %s does not decode at %s" % [name, size]
		var why := check_layer(img, float(meta["crown_top"]), float(meta["horizon_y"]))
		if why != "":
			return "layer %s %s" % [name, why]
	for f in meta["clouds"]:
		if not FileAccess.file_exists(from_dir.path_join(f)):
			return "cloud %s is missing" % f
	return ""


## Where the tree stands in the layer: its foot at FOOT, its top no higher than TOP, its crown no
## wider than WIDE of the layer (so the crown is whole on any screen and below the lock screen's
## clock once laid out, LivePicture.layout).
const FOOT := 0.9
const TOP := 0.13
const WIDE := 0.86
## The view: from the album's side (north of the tree, looking south, TreeView.ALBUM_YAW), from
## a little below eye height (lower for a small tree), so the meadow is only a strip at its foot.
const FOV := 34.0


## Places the camera so the current tree fills the frame (FOOT, TOP, WIDE): a few rounds of
## measuring the projected trunk foot, top and crown sides and correcting distance and aim.
static func frame_camera(cam: Camera3D, size: Vector2, height: float, crown_width: float) -> void:
	# A small tree stands small in the meadow, as in the game (TreeView.frame_height).
	var h := TreeView.frame_height(height)
	var half_w := maxf(crown_width * 0.5, h * 0.2)
	var eye := minf(1.1, h * 0.35 + 0.1)
	cam.fov = FOV
	var dist := h * 2.0
	var aim := h * 0.5
	for i in range(12):
		cam.global_position = Vector3(sin(TreeView.ALBUM_YAW) * dist, eye, cos(TreeView.ALBUM_YAW) * dist)
		cam.look_at(Vector3(0, aim, 0), Vector3.UP)
		var foot := cam.unproject_position(Vector3.ZERO).y / size.y
		var top := cam.unproject_position(Vector3(0, h, 0)).y / size.y
		var side := absf(cam.unproject_position(Vector3(half_w, h * 0.6, 0)).x - cam.unproject_position(Vector3(-half_w, h * 0.6, 0)).x) / size.x
		# Distance: the tree's height to span FOOT - TOP, its crown at most WIDE.
		var want := maxf((foot - top) / (FOOT - TOP), side / WIDE)
		dist *= clampf(want, 0.5, 2.0)
		# Aim: the foot at FOOT (a lower foot on screen means aiming lower).
		aim -= (foot - FOOT) * h * 0.9
	cam.global_position = Vector3(sin(TreeView.ALBUM_YAW) * dist, eye, cos(TreeView.ALBUM_YAW) * dist)
	cam.look_at(Vector3(0, aim, 0), Vector3.UP)


## The tree, its crown, the meadow grass and the ground: the game's own meshes and materials.
## The ground is laid flat to the horizon (the game's swells and the rise toward the forest would
## hide the tree's foot from this low view). The meadow's grass clumps around the tree are set on
## it and repeated in tiles from behind the tree to just before the camera, so the meadow runs
## from the picture's bottom edge to the haze (no bare dark band).
const GRASS_RADIUS := 16.0
## No grass closer to the camera than this (it would stand huge in front of the lens).
const GRASS_CLEAR := 4.0


func _add_scene(sv: SubViewport, view: TreeView, cam_dist: float) -> void:
	var trunk := MeshInstance3D.new()
	trunk.mesh = view._tree_mesh.mesh
	trunk.material_override = view._tree_mesh.material_override
	sv.add_child(trunk)
	var crown := MultiMeshInstance3D.new()
	crown.multimesh = view._leaves.multimesh
	crown.material_override = view._leaves.material_override
	crown.extra_cull_margin = 4.0
	sv.add_child(crown)
	for layer in [view._grass, view._meadow2]:
		var src := layer as MultiMeshInstance3D
		var g := MultiMeshInstance3D.new()
		g.multimesh = flat_grass(src.multimesh, GRASS_RADIUS, cam_dist, layer == view._meadow2)
		# Its own copy of the material: the game's fades the grass by its own camera's distance.
		var mat := src.material_override as ShaderMaterial
		if mat != null:
			mat = mat.duplicate() as ShaderMaterial
			mat.set_shader_parameter("fade_start", cam_dist + GRASS_RADIUS * 3.0)
			mat.set_shader_parameter("fade_end", cam_dist + GRASS_RADIUS * 3.0 + 20.0)
		g.material_override = mat
		g.custom_aabb = AABB(Vector3(-GRASS_RADIUS, -1, -cam_dist - GRASS_RADIUS), Vector3(GRASS_RADIUS * 2.0, 3, cam_dist + GRASS_RADIUS * 4.0))
		g.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		sv.add_child(g)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(400, 400)
	plane.subdivide_width = 8
	plane.subdivide_depth = 8
	ground.mesh = plane
	ground.material_override = view._ground.material_override
	sv.add_child(ground)


## The clumps of a meadow layer within `radius` of the trunk, set down on flat ground and repeated
## in tiles along the view (the camera stands `cam_dist` north of the tree, TreeView.ALBUM_YAW):
## one tile behind the tree, as many as reach toward the camera, none within GRASS_CLEAR of it.
## `flowered`: the layer's clumps carry their kind in the colour's alpha (GrassLook.apply_meadow2);
## its clover and flowers keep FLOWER_CLEAR further back, so no flower head is cut by the
## picture's bottom edge (0.8 review); the fine grass there fills the edge.
const FLOWER_CLEAR := 5.5


static func flat_grass(src: MultiMesh, radius: float, cam_dist: float, flowered: bool = false) -> MultiMesh:
	var toward := Vector3(sin(TreeView.ALBUM_YAW), 0.0, cos(TreeView.ALBUM_YAW))
	var step := radius * 1.9
	var tiles := ceili(maxf(cam_dist - GRASS_CLEAR, 0.0) / step)
	var picks: Array[Transform3D] = []
	var from: Array[int] = []
	for i in range(src.instance_count):
		var xf := src.get_instance_transform(i)
		var o := xf.origin
		if Vector2(o.x, o.z).length() >= radius:
			continue
		for k in range(-1, tiles + 1):
			var p := Vector3(o.x, -0.02, o.z) + toward * (step * k)
			if p.dot(toward) > cam_dist - GRASS_CLEAR:
				continue
			if flowered and src.use_colors and src.get_instance_color(i).a > 0.5 and p.dot(toward) > cam_dist - GRASS_CLEAR - FLOWER_CLEAR:
				continue
			var t := xf
			t.origin = p
			picks.append(t)
			from.append(i)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = src.use_colors
	mm.use_custom_data = src.use_custom_data
	mm.mesh = src.mesh
	mm.instance_count = picks.size()
	for j in range(picks.size()):
		mm.set_instance_transform(j, picks[j])
		if src.use_colors:
			mm.set_instance_color(j, src.get_instance_color(from[j]))
		if src.use_custom_data:
			mm.set_instance_custom_data(j, src.get_instance_custom_data(from[j]))
	return mm


## Reads the shape of the picture from its day layer: the crown's top (the first row with an opaque
## pixel), the horizon (where the side columns turn opaque) and the ground's colour (the bottom
## rows), all as shares of the height.
static func measure(img: Image) -> Dictionary:
	var w := img.get_width()
	var h := img.get_height()
	var top := 1.0
	for y in range(h):
		var hit := false
		for x in range(0, w, 2):
			if img.get_pixel(x, y).a > 0.5:
				hit = true
				break
		if hit:
			top = float(y) / h
			break
	var horizon := 1.0
	for y in range(h):
		if img.get_pixel(1, y).a > 0.5 and img.get_pixel(w - 2, y).a > 0.5:
			horizon = float(y) / h
			break
	var sum := Color(0, 0, 0, 0)
	var n := 0
	for y in range(h - 24, h, 4):
		for x in range(0, w, 8):
			var c := img.get_pixel(x, y)
			if c.a > 0.5:
				sum += c
				n += 1
	var ground := Color(sum.r / n, sum.g / n, sum.b / n) if n > 0 else Color(0.33, 0.42, 0.2)
	return {"crown_top": top, "horizon_y": horizon, "ground_color": ground}


## Writes the layers and then the meta (the meta last, so the phone never reads a half-written set)
## and removes older layers. Lossy WebP with alpha (a tenth of a PNG's size; Android decodes it
## natively), encoded on a worker thread.
static func write(to_dir: String, images: Dictionary, meta: Dictionary) -> bool:
	var files: Dictionary = meta["layers"]
	var ok := [false]
	var task := WorkerThreadPool.add_task(func() -> void: ok[0] = encode_layers(to_dir, images, files) and ensure_clouds(to_dir))
	var tree := Engine.get_main_loop() as SceneTree
	while not WorkerThreadPool.is_task_completed(task):
		await tree.process_frame
	WorkerThreadPool.wait_for_task_completion(task)
	return bool(ok[0]) and commit(to_dir, meta)


## The cloud images (LivePicture.cloud_image) beside the layers, written once. Any thread.
static func ensure_clouds(to_dir: String) -> bool:
	for i in range(LivePicture.CLOUD_FILES.size()):
		var path := to_dir.path_join(LivePicture.CLOUD_FILES[i])
		if not FileAccess.file_exists(path) and LivePicture.cloud_image(i).save_webp(path, true, WEBP_QUALITY) != OK:
			return false
	return true


## Writes each layer image (name -> Image) under its file name (name -> file). Any thread.
static func encode_layers(to_dir: String, images: Dictionary, files: Dictionary) -> bool:
	DirAccess.make_dir_recursive_absolute(to_dir)
	for name in files:
		if (images[name] as Image).save_webp(to_dir.path_join(files[name]), true, WEBP_QUALITY) != OK:
			return false
	return true


## Writes the meta beside the layers (through a temporary file, renamed into place) and removes
## older layers. Main thread.
static func commit(to_dir: String, meta: Dictionary) -> bool:
	var files: Dictionary = meta["layers"]
	var tmp := to_dir.path_join(LivePicture.META + ".tmp")
	var f := FileAccess.open(tmp, FileAccess.WRITE)
	if f == null:
		return false
	f.store_string(JSON.stringify(meta, "	"))
	f.close()
	var d := DirAccess.open(to_dir)
	if d == null or d.rename(tmp, LivePicture.META) != OK:
		return false
	# Older layers go; the phone has the new meta to read.
	for old in d.get_files():
		if old.begins_with("tree_") and (old.ends_with(".webp") or old.ends_with(".png")) and not files.values().has(old):
			d.remove(old)
	return true
