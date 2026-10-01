extends SceneTree
## Grows a tree for N in-game days with the root bot and calm days, then takes screenshots of
## the tree view at noon from three sides. For judging the look of the growth.
## Run: godot --path . -s tools/grow_shot.gd -- --days=10 --shots=C:/some/folder [--seed=42] [--species=oak] [--boost] [--dive]
## --dive: instead, five frames of the fall into the ground (dive_amount 0 to 1) from the south.
## The brush pile (0.8): --cut=<n> cuts a side branch at noon on each of the last n days (onto
## the pile at the next sunrise); --hedgehog=<s> shows the hedgehog s seconds into its evening
## walk, --wren=<s> the wren on the pile; --pile_close adds a photo from a tool camera 4 m in front
## of the pile (a closer look than the game's camera gives).
## --roots: instead, the underground after the last night (0.8, the dots' shapes): the overview,
## a close view as in a run and a medium one, each also saved in greyscale (`*_grey.png`); then
## (0.8.1) the whole root field from a tool camera far out, slanted and from above.
## --stats: also print draw calls and primitives of each view, with and without the forest ring
## and shrub belt. Add `--phone` (and `--rendering-method gl_compatibility` before `--`) to
## measure the phone path on a PC.
## --overdraw: photograph the overdraw view instead (Forward+ and Mobile renderers only):
## the brighter, the more layers each pixel was shaded in.
## --pitch=0.5 --zoom=0.8: look down more / step closer (the ground under the crown).
## --marks: the twigs the tree marks for pruning (0.7): a photo from the side of the most visible
## marked twig, then each marked twig is cut at its fork and a second photo follows
## (<tag>marks_before.png, <tag>marks_after.png); prints where the marks are.
## --hint_patches: a phosphorus and a potassium wish deposit in front of the tree in the game's
## default view (north of the trunk), so their meadow signs (nettles, comfrey) are in the photos.
## --rain: a shower on the last day (Clearing.after_rain), so the mushrooms are up.
## Mood (section 17): --season=spring|summer|autumn|late_autumn, --weather=rain|mist|dew|clear,
## --moon=<phase 0..1>, --date=YYYY-MM-DD (Almanac.read_cmdline); --night=<0..1> photographs the
## sunset hold that far into the night instead of noon; --yaw=<radians> and --pitch= one view only;
## --settle=<frames> waits before the first photo; --face_moon turns the view toward the moon; --look_up=<radians> tilts the camera toward the sky; --tag=<name> prefixes the file names.

var shots_dir := ""
var days := 10
var seed := 42
var species := "linden"
var boost := false
var hour := 0.5  # fraction of the daylight, 0.5 = noon
var view: TreeView
var frame := 0
var dive := false
var roots_mode := false
var cut_days := 0
var hog_at := -1.0
var wren_at := -1.0
var pile_close := false
var rview: RootView
var prune := false
var stats := false
var overdraw := false
var pitch := 0.12
var zoom := 1.0
var rain := false
var night := -1.0
var only_yaw := -100.0
var tag := ""
var look_up := 0.0
var face_moon := false
var settle := 0
var marks := false
var hint_patches := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--hour="):
			hour = float(a.substr(7))
		elif a.begins_with("--pitch="):
			pitch = float(a.substr(8))
		elif a.begins_with("--zoom="):
			zoom = float(a.substr(7))
		elif a == "--rain":
			rain = true
		elif a == "--prune":
			prune = true
		elif a == "--marks":
			marks = true
		elif a == "--hint_patches":
			hint_patches = true
		elif a == "--dive":
			dive = true
		elif a == "--roots":
			roots_mode = true
		elif a.begins_with("--cut="):
			cut_days = int(a.substr(6))
		elif a.begins_with("--hedgehog="):
			hog_at = float(a.substr(11))
		elif a.begins_with("--wren="):
			wren_at = float(a.substr(7))
		elif a == "--pile_close":
			pile_close = true
			settle = maxi(settle, 15)
		elif a == "--overdraw":
			overdraw = true
		elif a == "--stats":
			stats = true
		elif a == "--boost":
			boost = true
		elif a.begins_with("--night="):
			night = float(a.substr(8))
		elif a.begins_with("--yaw="):
			only_yaw = float(a.substr(6))
		elif a.begins_with("--pitch="):
			pitch = float(a.substr(8))
		elif a.begins_with("--settle="):
			settle = int(a.substr(9))
		elif a == "--face_moon":
			face_moon = true
		elif a.begins_with("--look_up="):
			look_up = float(a.substr(10))
		elif a.begins_with("--tag="):
			tag = a.substr(6) + "_"
	Almanac.read_cmdline()
	DirAccess.make_dir_recursive_absolute(shots_dir)
	var g := GameState.new_game(seed, species)
	for day in range(days):
		g.dive()
		var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
		g.start_run(start)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if day < days - 1:
			if day >= days - 1 - cut_days:
				# Noon, then one side branch cut, as a player trims the tree.
				while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.5:
					g.tick(0.5)
				var id := _side_branch(g.sim)
				if id >= 0:
					var n := g.sim.prune(id)
					g.cut_to_pile(n)
					print("day %d: cut %d segments" % [g.day_number(), n])
			while g.phase == GameState.Phase.DAY:
				# Optional: boost through the mornings, so the crown should lean east.
				g.sim.clock.boost_active = boost and g.sim.clock.time_of_day < 0.2
				g.tick(0.5)
	# Stop at noon of the last day (or at sunset for --night).
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * hour and g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	# --marks: go on a day at a time (at most a week) until two twigs or more show the sign.
	var extra := 0
	while marks and g.sim.marks.size() < 2 and extra < 7:
		extra += 1
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot2 := RootBot.new()
		var guard2 := 0
		while g.steer(bot2.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard2 < 20000:
			guard2 += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * hour and g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	while night >= 0.0 and g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	if rain:
		g.after_rain()
	if hint_patches:
		var depth := minf(0.8, Underground.HINT_MAX_DEPTH - 0.1)
		print("hint patches: ", g.ground.add_wish_deposit(g.day_number(), Resources.Kind.PHOSPHORUS, Vector3(2.6, -depth, -3.6), 1.3, 30), " ", g.ground.add_wish_deposit(g.day_number(), Resources.Kind.POTASSIUM, Vector3(-2.4, -depth, -4.2), 1.3, 30))
	g.take_events()
	if cut_days > 0:
		print("brush pile: %d segments, %d sticks" % [g.brush.wood, g.brush.stick_count()])
	print("clearing: found %s, plan %s" % [g.clearing.found, Clearing.counts(g.clearing.plan(Clearing.shade_map(g.sim), g.day_number(), Budgets.UNDERSTORY_PLANTS))])
	print("day %d: %d nodes, %.1f m, %d tips, crown centre %s" % [g.day_number(), g.sim.graph.size(), g.sim.height(), g.sim.tip_count(), g.sim.centroid()])
	set_meta("game", g)
	if roots_mode:
		rview = RootView.new()
		root.add_child(rview)
		return
	view = TreeView.new()
	root.add_child(view)


func _process(_delta: float) -> bool:
	frame += 1
	if roots_mode:
		return _roots_frames()
	if frame == 1:
		# After the view's own _ready, which runs once the tree starts.
		view.setup(get_meta("game"))
		view.set_hud_visible(false)
		if overdraw:
			root.get_viewport().debug_draw = Viewport.DEBUG_DRAW_OVERDRAW
		view.night_override = night
		view.look_up = look_up
		var bp := view.brush_pile
		if hog_at >= 0.0:
			bp.state.brush.hedgehog_day = bp.state.day_number()
			bp._hog_day = bp.state.day_number()
			bp._start_hog()
			bp._hog_t = hog_at
		if wren_at >= 0.0:
			bp.state.brush.wren_day = bp.state.day_number()
			bp._wren_day = bp.state.day_number()
			bp._wren_t = wren_at
			bp._songs_left = 0
	if pile_close and frame == 2:
		# A tool camera 4 m in front of the pile, a little above the grass.
		var bp := view.brush_pile
		var cam := Camera3D.new()
		cam.fov = 40.0
		view.add_child(cam)
		cam.environment = view.camera.environment
		var front := bp.global_position + bp.global_basis.z * 4.0 + Vector3.UP * 1.1
		cam.look_at_from_position(front, bp.global_position + Vector3.UP * 0.2 + bp.global_basis.z * 0.8)
		cam.make_current()
	if pile_close and frame == 12:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%spile_close.png" % tag))
		view.camera.make_current()
		if face_moon:
			# Stand opposite the moon, so it hangs above the tree.
			var d := view._night_sky.moon_direction()
			only_yaw = atan2(-d.x, -d.z)
	if dive:
		return _dive_frames()
	if prune:
		return _prune_frames()
	if marks:
		return _marks_frames()
	var names := ["from_north", "from_east", "from_south"]
	var yaws := [PI, PI * 0.5, 0.0]
	if only_yaw > -99.0:
		names = ["view"]
		yaws = [only_yaw]
	# --settle: let rain and falling leaves fill the air before the first photo.
	var f := frame - settle
	if f < 1:
		view._yaw = yaws[0]
		view._pitch = pitch
		return false
	var i := f / 20
	if f % 20 == 0 and i - 1 < names.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%stree_day%d_h%d_%s.png" % [tag, days, int(hour * 100), names[i - 1]]))
		if stats:
			_print_stats(names[i - 1])
		# Where the hedgehog is in the photo (0.8 review: it was a few pixels at the edge).
		var bp := view.brush_pile
		if bp.hedgehog.visible and view.camera.is_position_in_frustum(bp.hedgehog.global_position):
			print("hedgehog on screen at ", view.camera.unproject_position(bp.hedgehog.global_position), ", ", snappedf(view.camera.global_position.distance_to(bp.hedgehog.global_position), 0.1), " m from the camera")
		elif bp.hedgehog.visible:
			print("hedgehog out of frame")
	if i < names.size():
		view._yaw = yaws[i]
		view._pitch = pitch
		view._zoom = zoom
	else:
		quit()
	return false


## What the frame just drawn cost, and what it costs without the forest (trees, impostors, shrubs).
func _print_stats(label: String) -> void:
	var all := _render_info()
	var forest: Array[Node] = get_nodes_in_group("forest_trees") + get_nodes_in_group("shrubs")
	for n in forest:
		(n as Node3D).visible = false
	RenderingServer.force_draw(false)
	var bare := _render_info()
	for n in forest:
		(n as Node3D).visible = true
	if OS.get_cmdline_user_args().has("--objects"):
		# Where the objects come from: visible geometry per parent node.
		var by := {}
		var stack: Array[Node] = [view]
		while not stack.is_empty():
			var n: Node = stack.pop_back()
			stack.append_array(n.get_children())
			var gi := n as GeometryInstance3D
			if gi != null and gi.is_visible_in_tree():
				var key := "%s/%s" % [gi.get_parent().name, gi.get_class()]
				by[key] = int(by.get(key, 0)) + 1
		print(by)
	print("%s  %s: draw calls %d (shadow %d), primitives %d (shadow %d), objects %d | without forest: draw calls %d, primitives %d | forest: %d draw calls, %d primitives" % [
		RenderingServer.get_current_rendering_method(), label, all[0], all[2], all[1], all[3], all[4], bare[0], bare[1], all[0] - bare[0], all[1] - bare[1]])


## [draw calls, primitives, shadow draw calls, shadow primitives, objects] of the last frame.
func _render_info() -> Array[int]:
	var vp := root.get_viewport()
	var vis := Viewport.RENDER_INFO_TYPE_VISIBLE
	var sh := Viewport.RENDER_INFO_TYPE_SHADOW
	return [vp.get_render_info(vis, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME), vp.get_render_info(vis, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME),
		vp.get_render_info(sh, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME), vp.get_render_info(sh, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME),
		vp.get_render_info(vis, Viewport.RENDER_INFO_OBJECTS_IN_FRAME)]


## A side branch around mid height with at least a few segments (like tools/grow_shot --prune).
func _side_branch(sim: GrowthSim) -> int:
	var g := sim.graph
	var best := -1
	var best_n := 0
	for id in range(3, g.size()):
		if g.get_flag(id, "dead", false) or g.children[id].is_empty():
			continue
		if Vector2(g.positions[id].x, g.positions[id].z).length() < 0.6 or g.positions[id].y < sim.height() * 0.3:
			continue
		var n := sim._subtree_size(id)
		if n > best_n and n <= maxi(8, int(sim.living_nodes() * 0.12)):
			best_n = n
			best = id
	return best


## --roots: the overview while picking a start, then two views as in a run (the camera a few
## metres from a spot where dots of several kinds lie close together), in colour and in grey.
func _roots_frames() -> bool:
	var g: GameState = get_meta("game")
	if frame == 1:
		rview.setup(g.ground, g.roots, g.sim.resources)
		rview.hud.visible = false
		rview.begin_pick()
	if frame == 60:
		_save_both("roots_overview")
		# Hold the camera still from here on.
		rview.mode = RootView.Mode.IDLE
		var spot := _mixed_spot(g.ground)
		rview.camera.position = spot + Vector3(-2.6, 0.9, -2.6)
		rview.camera.look_at(spot, Vector3.UP)
		(rview._dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", 15.0)
		set_meta("spot", spot)
	if frame == 70:
		_save_both("roots_close")
		var spot: Vector3 = get_meta("spot")
		rview.camera.position = spot + Vector3(-5.5, 1.6, -5.5)
		rview.camera.look_at(spot, Vector3.UP)
	if frame == 80:
		_save_both("roots_medium")
		# 0.8.1: the whole wider field from a tool camera far out (the game's own far view is
		# 0.8.2), slanted and from above, with the fog pushed back so the far ring shows.
		var reach := g.ground.extent
		rview.camera.position = Vector3(0.0, reach * 0.55, reach * 1.55)
		rview.camera.look_at(Vector3(0, -3, 0), Vector3.UP)
		rview.camera.far = 400.0
		(rview._dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", reach * 4.0)
	if frame == 90:
		_save_both("roots_field")
		var reach := g.ground.extent
		rview.camera.position = Vector3(0.0, reach * 2.3, 0.5)
		rview.camera.look_at(Vector3(0, -3, 0), Vector3.UP)
	if frame == 100:
		_save_both("roots_field_top")
		quit()
	return false


## Where the most kinds lie within 3 m of a dot (the first such spot in the topsoil).
func _mixed_spot(u: Underground) -> Vector3:
	var best := Vector3(0, -2, 0)
	var best_score := -1
	for i in range(0, u.dot_count(), 3):
		var p := u.dot_positions[i]
		if u.dot_collected[i] != 0 or p.y < -6.0:
			continue
		var kinds := {}
		var n := 0
		for j in range(u.dot_count()):
			if u.dot_collected[j] == 0 and u.dot_positions[j].distance_to(p) < 3.0:
				kinds[u.dot_kinds[j]] = true
				n += 1
		var score := kinds.size() * 100 + mini(n, 40)
		if score > best_score:
			best_score = score
			best = p
	return best


func _save_both(name: String) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots_dir.path_join(name + ".png"))
	img.convert(Image.FORMAT_RGBA8)
	for y in range(img.get_height()):
		for x in range(img.get_width()):
			var c := img.get_pixel(x, y)
			var l := c.r * 0.299 + c.g * 0.587 + c.b * 0.114
			img.set_pixel(x, y, Color(l, l, l))
	img.save_png(shots_dir.path_join(name + "_grey.png"))


func _dive_frames() -> bool:
	var steps := [0.0, 0.3, 0.55, 0.75, 0.9]
	var i := frame / 15
	view._yaw = 0.0
	view._pitch = 0.12
	if i < steps.size():
		view.dive_amount = steps[i]
	if frame % 15 == 14 and i < steps.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("dive_%d.png" % int(steps[i] * 100)))
	if i >= steps.size():
		quit()
	return false


## --prune: preview a cut on a side branch, cut it, and photograph the fall.
func _prune_frames() -> bool:
	view._yaw = 0.0
	view._pitch = 0.12
	if frame == 20:
		# A side branch around mid height.
		var g := view.state.sim.graph
		var best := -1
		for id in range(3, g.size()):
			if g.children[id].size() > 0 and absf(g.positions[id].y - view.state.sim.height() * 0.5) < 1.0 and Vector2(g.positions[id].x, g.positions[id].z).length() > 1.0:
				best = id
				break
		view.pruning.preview(best)
	for k in [[30, "prune_preview"], [36, "prune_fall1"], [48, "prune_fall2"], [80, "prune_after"]]:
		if frame == k[0]:
			RenderingServer.force_draw(false)
			root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join(k[1] + ".png"))
	if frame == 31:
		print("cut ", view.pruning.cut(), " segments")
		view._rebuild()
	if frame > 82:
		quit()
	return false


## --marks: photograph the marked twigs from their side, cut them at the fork, photograph again.
func _marks_frames() -> bool:
	var sim := view.state.sim
	if frame == 2:
		var best := Vector3.ZERO
		var best_out := -1.0
		for m in sim.marks:
			var p := sim.graph.positions[int(m["id"])]
			print("mark: tip %d at (%.1f, %.1f, %.1f), due day %d, shown since day %d, strength %.2f, twig %d segments" % [int(m["id"]), p.x, p.y, p.z, int(m["due"]), int(m["since"]), sim.mark_strength(m), sim.marked_twig(int(m["id"])).size()])
			if Vector2(p.x, p.z).length() > best_out:
				best_out = Vector2(p.x, p.z).length()
				best = p
		view._yaw = atan2(best.x, best.z) if only_yaw < -99.0 else only_yaw
		view._pitch = pitch
		view._zoom = zoom
	if frame == 250:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%smarks_before.png" % tag))
		for m in sim.marks:
			var at := view.camera.unproject_position(view.to_global(sim.graph.positions[int(m["id"])]))
			print("mark on screen: tip %d at (%d, %d)" % [int(m["id"]), int(at.x), int(at.y)])
	if frame == 252:
		var n := 0
		for m in sim.marks.duplicate():
			var twig := sim.marked_twig(int(m["id"]))
			if not twig.is_empty():
				n += sim.prune(twig[-1])
		print("cut %d segments of marked twigs" % n)
		view._rebuild()
	if frame == 400:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%smarks_after.png" % tag))
	if frame > 402:
		quit()
	return false
