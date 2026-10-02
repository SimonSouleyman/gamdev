extends SceneTree
## The app icon (specs/0.8.md section 7): a grown linden from the game's own growth, rendered with
## the game's bark and crown against a clear pale-blue morning sky, nothing else (0.8.2.5: no grass
## line any more, Simon: only the tree and the sky). Written as an Android adaptive icon: the sky as
## the background layer,
## the tree as the foreground layer with the whole crown inside the safe circle (66 of 108 dp), so
## round, squircle and square masks all keep it. Also the legacy square icons and the project icon.
## The old icon stays in icons/v0.7 to swap back.
## Run (needs a window): godot --path . -s tools/render_app_icon.gd -- [--seed=2026] [--days=22]
##   [--qa=C:/folder] (512, 192 and 48 px previews with round and squircle masks, light and dark)
##   [--turn=2] [--dry] (previews only, the repo's icons are not written)

var seed := 2026
## (0.8 review: day 22, a fuller crown at 48 px than day 16 after the 0.8 balance changes.)
var days := 22
## The tree turned by this many quarter turns (its fullest side toward the viewer; picked from a
## contact sheet of seeds, days and sides, 0.8).
var turn := 1
var qa_dir := ""
var dry := false

## Everything is drawn 4x larger, then scaled down.
const K := 4
## Adaptive icon layers: 108 dp as 432 px; the safe circle is 66 dp across, the visible part of
## any mask at most 72 dp.
const LAYER := 432
const SAFE_RADIUS := 66.0 / 108.0 * 0.5
const VISIBLE := 72.0 / 108.0
## Where the trunk's foot stands (share of the layer from the top) and the crown's room inside the
## safe circle (a small margin). Only the crown must be inside the circle: the lowest 30 % of the
## tree (bare trunk) reaches below it. 0.8.2.5: with no ground to stand on, the foot is below the
## visible part of every mask (72 dp, its lower edge at 0.833), so the trunk runs out of the icon's
## lower edge instead of ending in the air; below that edge it fades out (FADE_FROM to the foot),
## so a launcher that shows more of the layer (parallax) never shows a cut stump either.
const FOOT_Y := 0.86
const FADE_FROM := 0.835
const FIT := 0.97
## The sky: a clear pale blue, lighter toward the horizon (soft morning), down to the lower edge.
const SKY_TOP := Color(0.42, 0.63, 0.88)
const SKY_LOW := Color(0.85, 0.9, 0.93)
## The morning light on the tree (from the east, the viewer's left; LiveExport's dawn, higher).
const LIGHT_DIR := Vector3(0.8, 0.45, 0.4)
const LIGHT_COLOR := Color(1.0, 0.88, 0.72)


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--qa="):
			qa_dir = a.substr(5)
		elif a.begins_with("--turn="):
			turn = int(a.substr(7))
		elif a == "--dry":
			dry = true
	# A summer crown, whatever the date.
	Almanac.season_override = "summer"
	_run.call_deferred()


func _run() -> void:
	var tools := load("res://tools/live_shot.gd")
	var game: GameState = tools.grow(seed, "linden", days)
	print("linden day %d: %d nodes, %.1f m" % [game.day_number(), game.sim.graph.size(), game.sim.height()])
	var view := TreeView.new()
	root.add_child(view)
	await process_frame
	view.setup(game)
	view.set_hud_visible(false)
	for i in range(4):
		await process_frame
	var tree := await _render_tree(view, game)
	var fg := _fade_foot(_fuller(_place_tree(tree["image"], tree["foot"])))
	var bg := _background()
	var mono := _monochrome(fg)
	var full := bg.duplicate() as Image
	full.blend_rect(fg, Rect2i(Vector2i.ZERO, fg.get_size()), Vector2i.ZERO)
	# The legacy square icon: the visible middle of the adaptive icon.
	var cut := int(LAYER * K * VISIBLE)
	var legacy := full.get_region(Rect2i(Vector2i((LAYER * K - cut) / 2, (LAYER * K - cut) / 2), Vector2i(cut, cut)))
	if not dry:
		_keep_old_icons()
		_save(fg, LAYER, "res://icons/icon_fg_432.png")
		_save(bg, LAYER, "res://icons/icon_adaptive_bg_432.png")
		_save(mono, LAYER, "res://icons/icon_mono_432.png")
		_save(legacy, 192, "res://icons/icon_192.png")
		_save(legacy, 512, "res://icons/icon_full.png")
		_save(legacy, 256, "res://icon_app.png")
		print("icons written")
	if qa_dir != "":
		_previews(fg, bg, legacy)
	quit(0)


func _render_tree(view: TreeView, game: GameState) -> Dictionary:
	var size := Vector2i(2048, 2048)
	var sv := SubViewport.new()
	sv.size = size
	sv.transparent_bg = true
	sv.own_world_3d = true
	sv.msaa_3d = Viewport.MSAA_4X
	sv.render_target_update_mode = SubViewport.UPDATE_DISABLED
	root.add_child(sv)
	var cam := Camera3D.new()
	cam.near = 0.05
	cam.far = 200.0
	sv.add_child(cam)
	cam.current = true
	LiveExport.frame_camera(cam, Vector2(size), game.sim.height(), view.crown_width())
	var env := Environment.new()
	env.background_mode = Environment.BG_CLEAR_COLOR
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.6, 0.66, 0.74)
	env.ambient_light_energy = 1.15
	env.tonemap_mode = Environment.TONE_MAPPER_ACES
	env.tonemap_exposure = 1.15
	env.adjustment_enabled = true
	env.adjustment_contrast = 1.12
	env.adjustment_saturation = 1.05
	cam.environment = env
	var sun := DirectionalLight3D.new()
	sun.shadow_enabled = true
	sun.shadow_blur = 2.0
	sun.light_color = LIGHT_COLOR
	sun.light_energy = 1.6
	sun.directional_shadow_max_distance = 80.0
	sv.add_child(sun)
	sun.look_at_from_position(LIGHT_DIR.normalized() * 20.0, Vector3.ZERO, Vector3.UP)
	var holder := Node3D.new()
	holder.rotation.y = turn * PI * 0.5
	sv.add_child(holder)
	var trunk := MeshInstance3D.new()
	trunk.mesh = view._tree_mesh.mesh
	trunk.material_override = view._tree_mesh.material_override
	holder.add_child(trunk)
	var crown := MultiMeshInstance3D.new()
	crown.multimesh = view._leaves.multimesh
	crown.material_override = view._leaves.material_override
	crown.extra_cull_margin = 4.0
	holder.add_child(crown)
	var foot := cam.unproject_position(Vector3.ZERO)
	sv.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	var img := sv.get_texture().get_image()
	sv.queue_free()
	img.convert(Image.FORMAT_RGBA8)
	return {"image": img, "foot": foot}


## The tree scaled and placed in the foreground layer (LAYER * K square): the trunk's foot at
## FOOT_Y on the middle line, as large as the safe circle allows for every opaque pixel.
func _place_tree(img: Image, foot: Vector2) -> Image:
	var n := LAYER * K
	var centre := Vector2(n, n) * 0.5
	var at := Vector2(n * 0.5, n * FOOT_Y)
	var r := SAFE_RADIUS * n * FIT
	# The largest scale that keeps every opaque pixel (sampled) inside the circle.
	var s := 10.0
	var top := float(img.get_height())
	for y in range(img.get_height()):
		for x in range(0, img.get_width(), 4):
			if img.get_pixel(x, y).a >= 0.3:
				top = y
				break
		if top < img.get_height():
			break
	var crown_bottom := foot.y - (foot.y - top) * 0.3
	for y in range(0, int(crown_bottom), 3):
		for x in range(0, img.get_width(), 3):
			if img.get_pixel(x, y).a < 0.3:
				continue
			var d := Vector2(x, y) - foot
			# |at + d s - centre| = r, solved for s.
			var o := at - centre
			var a := d.dot(d)
			var b := 2.0 * o.dot(d)
			var c := o.dot(o) - r * r
			var disc := b * b - 4.0 * a * c
			if a > 0.0 and disc >= 0.0:
				s = minf(s, (-b + sqrt(disc)) / (2.0 * a))
	var w := maxi(1, int(img.get_width() * s))
	var h := maxi(1, int(img.get_height() * s))
	var small := img.duplicate() as Image
	small.resize(w, h, Image.INTERPOLATE_LANCZOS)
	var out := Image.create(n, n, false, Image.FORMAT_RGBA8)
	var pos := Vector2i((at - foot * s).round())
	out.blit_rect(small, Rect2i(Vector2i.ZERO, small.get_size()), pos)
	print("tree scale %.3f, crown in the safe circle" % s)
	return out


## A little fuller crown for 48 px (0.8 review: the sky through the crown's gaps made it read thin
## on the home screen): the small gaps inside the crown are filled with the crown's own green,
## a shade darker (the inside of a crown is in shade), behind the leaves; the outline stays.
## Worked at the layer's own size (LAYER), then laid under the full-size tree.
const FILL_RADIUS := 4
const FILL_FROM := 0.5
const FILL_SHADE := 0.25


func _fuller(fg: Image) -> Image:
	var n := LAYER
	var small := fg.duplicate() as Image
	small.resize(n, n, Image.INTERPOLATE_BILINEAR)
	var a := PackedFloat32Array()
	var rgb := PackedColorArray()
	a.resize(n * n)
	rgb.resize(n * n)
	for y in range(n):
		for x in range(n):
			var c := small.get_pixel(x, y)
			a[y * n + x] = c.a
			rgb[y * n + x] = Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a)
	for _pass in range(2):
		a = _box(a, n, FILL_RADIUS)
		rgb = _box_colors(rgb, n, FILL_RADIUS)
	var back := Image.create(n, n, false, Image.FORMAT_RGBA8)
	for y in range(n):
		for x in range(n):
			var i := y * n + x
			var k := smoothstep(FILL_FROM, 0.8, a[i])
			if k <= 0.0 or rgb[i].a <= 0.0:
				continue
			var c := Color(rgb[i].r / rgb[i].a, rgb[i].g / rgb[i].a, rgb[i].b / rgb[i].a).darkened(FILL_SHADE)
			back.set_pixel(x, y, Color(c.r, c.g, c.b, k))
	back.resize(fg.get_width(), fg.get_height(), Image.INTERPOLATE_BILINEAR)
	back.blend_rect(fg, Rect2i(Vector2i.ZERO, fg.get_size()), Vector2i.ZERO)
	return back


static func _box(src: PackedFloat32Array, n: int, r: int) -> PackedFloat32Array:
	var tmp := PackedFloat32Array()
	tmp.resize(n * n)
	var out := PackedFloat32Array()
	out.resize(n * n)
	for y in range(n):
		for x in range(n):
			var sum := 0.0
			for d in range(-r, r + 1):
				sum += src[y * n + clampi(x + d, 0, n - 1)]
			tmp[y * n + x] = sum / (2 * r + 1)
	for y in range(n):
		for x in range(n):
			var sum := 0.0
			for d in range(-r, r + 1):
				sum += tmp[clampi(y + d, 0, n - 1) * n + x]
			out[y * n + x] = sum / (2 * r + 1)
	return out


static func _box_colors(src: PackedColorArray, n: int, r: int) -> PackedColorArray:
	var tmp := PackedColorArray()
	tmp.resize(n * n)
	var out := PackedColorArray()
	out.resize(n * n)
	for y in range(n):
		for x in range(n):
			var sum := Color(0, 0, 0, 0)
			for d in range(-r, r + 1):
				sum += src[y * n + clampi(x + d, 0, n - 1)]
			tmp[y * n + x] = sum / (2 * r + 1)
	for y in range(n):
		for x in range(n):
			var sum := Color(0, 0, 0, 0)
			for d in range(-r, r + 1):
				sum += tmp[clampi(y + d, 0, n - 1) * n + x]
			out[y * n + x] = sum / (2 * r + 1)
	return out


## The background layer: only the sky, a clear pale blue growing lighter toward the visible lower
## edge (the horizon, just below the icon) and staying that light below it.
func _background() -> Image:
	var n := LAYER * K
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	var low := n * (0.5 + VISIBLE * 0.5)
	for y in range(n):
		var sky := SKY_TOP.lerp(SKY_LOW, pow(minf(float(y) / low, 1.0), 1.3))
		for x in range(n):
			img.set_pixel(x, y, sky)
	return img


## The trunk below the visible lower edge fades out toward its foot (see FOOT_Y).
func _fade_foot(fg: Image) -> Image:
	var n := fg.get_height()
	var from := int(n * FADE_FROM)
	var to := n * FOOT_Y
	for y in range(from, n):
		var k := 1.0 - smoothstep(float(from), to, float(y))
		for x in range(fg.get_width()):
			var c := fg.get_pixel(x, y)
			if c.a > 0.0:
				c.a *= k
				fg.set_pixel(x, y, c)
	return fg


## Android 13 themed icons: the tree's silhouette in white.
func _monochrome(fg: Image) -> Image:
	var out := Image.create(fg.get_width(), fg.get_height(), false, Image.FORMAT_RGBA8)
	for y in range(fg.get_height()):
		for x in range(fg.get_width()):
			var a := fg.get_pixel(x, y).a
			if a > 0.0:
				out.set_pixel(x, y, Color(1, 1, 1, a))
	return out


func _save(img: Image, px: int, path: String) -> void:
	var c := img.duplicate() as Image
	c.resize(px, px, Image.INTERPOLATE_LANCZOS)
	c.save_png(ProjectSettings.globalize_path(path))


## The 0.3 icon, kept to swap back (once; later runs leave it).
func _keep_old_icons() -> void:
	var old := "res://icons/v0.7"
	if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(old)):
		return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(old))
	for f in ["icons/icon_fg_432.png", "icons/icon_adaptive_bg_432.png", "icons/icon_192.png", "icons/icon_full.png", "icon_app.png"]:
		DirAccess.copy_absolute(ProjectSettings.globalize_path("res://" + f), ProjectSettings.globalize_path(old.path_join(f.get_file())))


## Previews for judging: the adaptive icon under a round and a squircle mask at 512, 192 and 48 px,
## on a light and a dark home screen, and the plain legacy icon.
func _previews(fg: Image, bg: Image, legacy: Image) -> void:
	DirAccess.make_dir_recursive_absolute(qa_dir)
	var full := bg.duplicate() as Image
	full.blend_rect(fg, Rect2i(Vector2i.ZERO, fg.get_size()), Vector2i.ZERO)
	var n := full.get_width()
	var cut := int(n * VISIBLE)
	var visible := full.get_region(Rect2i(Vector2i((n - cut) / 2, (n - cut) / 2), Vector2i(cut, cut)))
	for px in [512, 192, 48]:
		_save(legacy, px, qa_dir.path_join("icon_%d.png" % px))
		for shape in ["round", "squircle"]:
			for theme in ["light", "dark"]:
				var home := Color(0.93, 0.93, 0.9) if theme == "light" else Color(0.08, 0.09, 0.1)
				var m := _masked(visible, px, shape, home)
				m.save_png(qa_dir.path_join("icon_%d_%s_%s.png" % [px, shape, theme]))
	# The foreground alone over a checker, with the safe circle drawn, to see the crown is inside.
	var check := Image.create(n, n, false, Image.FORMAT_RGBA8)
	var r := SAFE_RADIUS * n
	for y in range(n):
		for x in range(n):
			var c := Color(0.8, 0.8, 0.8) if ((x / 64) + (y / 64)) % 2 == 0 else Color(0.62, 0.62, 0.62)
			var d := Vector2(x, y).distance_to(Vector2(n, n) * 0.5)
			if absf(d - r) < 4.0:
				c = Color(0.9, 0.1, 0.1)
			check.set_pixel(x, y, c)
	check.blend_rect(fg, Rect2i(Vector2i.ZERO, fg.get_size()), Vector2i.ZERO)
	check.resize(432, 432, Image.INTERPOLATE_LANCZOS)
	check.save_png(qa_dir.path_join("icon_fg_safe_circle.png"))


## The icon as a launcher shows it: scaled to px, cut by the mask, on the home screen's colour.
func _masked(visible: Image, px: int, shape: String, home: Color) -> Image:
	var c := visible.duplicate() as Image
	c.resize(px, px, Image.INTERPOLATE_LANCZOS)
	var pad := px / 4
	var out := Image.create(px + pad * 2, px + pad * 2, false, Image.FORMAT_RGBA8)
	out.fill(home)
	var half := px * 0.5
	for y in range(px):
		for x in range(px):
			var p := (Vector2(x + 0.5, y + 0.5) - Vector2(half, half)) / half
			var inside := 0.0
			if shape == "round":
				inside = clampf((1.0 - p.length()) * half + 0.5, 0.0, 1.0)
			else:
				# A superellipse, as squircle launchers cut.
				var e := pow(pow(absf(p.x), 4.0) + pow(absf(p.y), 4.0), 0.25)
				inside = clampf((1.0 - e) * half + 0.5, 0.0, 1.0)
			var col := c.get_pixel(x, y)
			out.set_pixel(x + pad, y + pad, home.lerp(col, inside))
	return out
