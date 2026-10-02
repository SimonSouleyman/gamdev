extends SceneTree
## Screenshots of the wish place (0.8.2.5, specs/wish-compass-vial.md items 1 and 2): the wish
## plant by day with the compass pointing to it (wish_day, and wish_plant / wish_compass crops),
## then the ink ring around it at sunset (wish_ring). Prints where the needle points and where the
## plant lies on the screen. Plays main.tscn in an ephemeral run.
## Run: godot --path . [--rendering-method gl_compatibility] -s tools/wish_place_shot.gd --
##      --shots=C:/some/folder [--days=6] [--seed=42] [--size=450x1000] [--phone]

var main: Node
var shots := ""
var days := 6
var seed := 42
var win := Vector2i(450, 1000)
var frame := 0
var _ring_frame := -1


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			win = Vector2i(int(p[0]), int(p[1]))
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> Image:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots.path_join(name + ".png"))
	print("shot ", name, " ", img.get_size())
	return img


## A crop around `at` (screen pixels), doubled, for a closer look.
func _crop(img: Image, at: Vector2, half: Vector2i, name: String) -> void:
	var r := Rect2i(Vector2i(at) - half, half * 2).intersection(Rect2i(Vector2i.ZERO, img.get_size()))
	if r.size.x <= 0 or r.size.y <= 0:
		return
	var c := img.get_region(r)
	c.resize(r.size.x * 2, r.size.y * 2, Image.INTERPOLATE_LANCZOS)
	c.save_png(shots.path_join(name + ".png"))


func _game() -> GameState:
	var g := GameState.new_game(seed, "linden")
	for _day in range(days):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else Diary.newest_tip(g.roots))
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if _day < days - 1:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.35:
		g.tick(0.5)
	g.take_events()
	for id in ["first_sunset", "compass", "sapling", "planted", "first_night", "first_run_done"]:
		g.seen_pages[id] = true
	return g


## Turns the camera so the wish plant stands beside the tree, low in the frame.
func _aim() -> void:
	var tv: TreeView = main.tree_view
	var best := INF
	var best_yaw: float = tv._yaw
	var vp := root.get_viewport().get_visible_rect().size
	for k in range(36):
		tv._yaw = TAU * k / 36.0
		tv._frame_camera(true)
		var cam: Camera3D = tv.camera
		if cam.is_position_behind(tv.wish_plant.place):
			continue
		var s := cam.unproject_position(tv.wish_plant.place)
		if s.x < vp.x * 0.15 or s.x > vp.x * 0.85 or s.y < vp.y * 0.4 or s.y > vp.y * 0.85:
			continue
		var score := absf(s.x - vp.x * 0.5) + absf(s.y - vp.y * 0.72) * 0.5
		if score < best:
			best = score
			best_yaw = tv._yaw
	tv._yaw = best_yaw
	tv._frame_camera(true)


func _report(img: Image, name: String) -> void:
	var tv: TreeView = main.tree_view
	var cam: Camera3D = tv.camera
	var plant := cam.unproject_position(tv.wish_plant.place)
	var trunk := cam.unproject_position(Vector3.ZERO)
	var s := plant - trunk
	var compass: Compass = null
	for c in tv.hud.get_children():
		if c is Compass:
			compass = c
	var needle := compass.needle_angle() if compass != null else NAN
	print("camera %s, plant %s, trunk on screen %s" % [str(cam.global_position), str(tv.wish_plant.place), str(trunk)])
	print("%s: plant at %s on screen, %.1f m from the trunk; trunk->plant %.2f rad, needle %.2f rad, north %.2f rad" % [name, str(plant), Vector2(tv.wish_plant.place.x, tv.wish_plant.place.z).length(), atan2(s.x, -s.y), needle, Compass.north_angle_for(cam.global_basis)])
	# The viewport's own size may differ from the window's (stretch): crops in picture pixels.
	var k := float(img.get_width()) / root.get_viewport().get_visible_rect().size.x
	_crop(img, plant * k, Vector2i(110, 90), name + "_plant")
	if compass != null:
		var r := compass.get_global_rect()
		_crop(img, r.get_center() * k, Vector2i(r.size * 0.5 * k) + Vector2i(4, 4), name + "_compass")


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		main.start(_game())
		main.enter_shed(false)
	if frame == 20:
		main.leave_shed()
	if frame == 90:
		main.journal.close_diary()
		var g: GameState = main.state
		print("day %d wish: %s (patch %d, %s)" % [g.day_number(), g.diary.wish, g.diary.wish_patch, "far" if Diary.is_far(g.ground, g.diary.wish_patch) else "near"])
		_aim()
	if frame == 150:
		_report(_shot("wish_day"), "wish_day")
		# Just before the sunset, so the game's own sunset event draws the ring.
		var clk: DayCycle = main.state.sim.clock
		clk.time_of_day = clk.daylight_fraction - 0.0015
	if _ring_frame < 0 and frame > 150 and main.state.phase == GameState.Phase.SUNSET:
		_ring_frame = frame
	if _ring_frame > 0 and frame == _ring_frame + 1:
		main.journal.close_diary()
		_aim()
	if _ring_frame > 0 and frame == _ring_frame + 50:
		print("ring state ", main.tree_view.wish_plant.ring_state())
		_report(_shot("wish_ring"), "wish_ring")
		quit(0)
	if frame > 3000:
		print("no sunset")
		quit(1)
	return false
