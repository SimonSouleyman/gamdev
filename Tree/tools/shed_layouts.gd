extends SceneTree
## Photographs one of the shed's layout proposals (Shed.layout, 0 = the current view) with the
## bonsai on the sill and every label shown, by day (or --night), and prints each thing's tap
## circle in canvas pixels (100 px is about 9 mm on a phone) and whether it is whole on screen.
## Run: godot --path . -s tools/shed_layouts.gd -- --shots=C:/some/folder --layout=3 [--night]
## For the phone's look add `--rendering-method gl_compatibility` before `--` and `--phone` after it.

var main: Node
var shots := ""
var name := "shed"
var night := false
## Also photograph bonsai mode (the camera glides to the sill) and back.
var bonsai := false
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--layout="):
			Shed.layout = int(a.substr(9))
		elif a.begins_with("--name="):
			name = a.substr(7)
		elif a == "--night":
			night = true
		elif a == "--bonsai":
			bonsai = true
	DirAccess.make_dir_recursive_absolute(shots)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _run() -> void:
	var g := GameState.new_game(42, "linden")
	for _day in range(4):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if _day < 3:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()
	g.grove.append({"species": g.sim.species.id, "days": 4, "seed": 42})
	main.start(g)
	main.enter_shed(false)
	if night:
		while main.state.phase == GameState.Phase.DAY:
			main.state.tick(0.5)
		await _wait(20)
		main.tree_view._time += 60.0
	await _wait(40)
	main.journal.clear_pages()
	await _wait(6)
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))
	_report()
	if bonsai:
		main.open_shed_item("bonsai")
		await create_timer(1.6).timeout
		main.journal.clear_pages()
		await _wait(4)
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + "_bonsai.png"))
	quit()


func _report() -> void:
	var shed: Shed = main.shed
	var size := shed.get_viewport().get_visible_rect().size
	var line := "layout %d: fov %.1f, canvas %s |" % [Shed.layout, shed.camera.fov, size]
	for item in Shed.ITEMS + ["door"]:
		var c := shed._picks[item][0] as Node3D
		var p := shed.camera.unproject_position(c.global_position)
		var d := shed._screen_radius(c.global_position, float(shed._picks[item][1])) * 2.0
		var inside := Rect2(Vector2.ZERO, size).has_point(p) and not shed.camera.is_position_behind(c.global_position)
		var hit: bool = shed.item_at(p) == item
		line += " %s %.0fpx%s%s" % [item, d, "" if inside else " OFF", "" if hit else " MISS"]
	print(line)
