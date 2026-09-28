extends SceneTree
## Screenshots of the tree view with the real HUD (grow_shot.gd hides it): the pictures at the
## middle right, the hand compass, then the camera turned (the compass turns with it) and the
## shears out (glowing; the secateurs pointer is drawn into the picture where the mouse is, as a
## screenshot of the viewport has no pointer). Plays main.tscn in an ephemeral run.
## Run: godot --path . -s tools/hud_shot.gd -- --shots=C:/some/folder [--days=6] [--size=540x1200]

var main: Node
var shots := ""
var days := 6
var win := Vector2i(540, 1200)
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			win = Vector2i(int(p[0]), int(p[1]))
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String, pointer: Vector2 = Vector2(-1, -1)) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	if pointer.x >= 0.0:
		img.convert(Image.FORMAT_RGBA8)
		var cur: Image = main.SHEARS_CURSOR.get_image()
		cur.convert(Image.FORMAT_RGBA8)
		var at := Vector2i(pointer - main.shears_hotspot())
		img.blend_rect(cur, Rect2i(Vector2i.ZERO, cur.get_size()), at)
	img.save_png(shots.path_join(name + ".png"))
	print("shot ", name, " ", img.get_size())


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		# A tree grown for a few days, at mid-morning (as tools/shed_shot.gd).
		var g := GameState.new_game(42, "linden")
		for _day in range(days):
			g.dive()
			g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
			var bot := RootBot.new()
			var guard := 0
			while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
				guard += 1
			while g.phase == GameState.Phase.NIGHT:
				g.tick(0.25)
			if _day < days - 1:
				while g.phase == GameState.Phase.DAY:
					g.tick(0.5)
		while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
			g.tick(0.5)
		g.take_events()
		main.start(g)
		main.enter_shed(false)
	if frame == 20:
		main.leave_shed()
	if frame == 90:
		main.journal.close_diary()
	if frame == 100:
		_shot("hud_tree")
		main.tree_view._yaw += 1.9
	if frame == 130:
		_shot("hud_turned")
		main._set_shears(true)
		main.journal.close_diary()
	if frame == 160:
		main.journal.close_diary()
	if frame == 170:
		var s: Vector2 = root.get_viewport().get_visible_rect().size
		_shot("hud_shears", Vector2(s.x * 0.42, s.y * 0.45) * (Vector2(root.get_viewport().get_texture().get_size()) / s))
		main._set_shears(false)
		quit()
	return false
