extends SceneTree
## 0.8.2.4 shots (notes/ff-0.8.2.4.md): the tree view's HUD with the sunset picture, the run to
## the sunset under way (the picture lit, the hourglass), a drawer of the bench open, and the
## bonsai lamp at night on the sill and in bonsai mode. Plays main.tscn in an ephemeral run.
## Run (phone look): godot --path . --rendering-method gl_compatibility -s tools/ff_shot.gd -- --phone --shots=C:/some/folder [--size=450x1000]

var main: Node
var shots := ""
var win := Vector2i(450, 1000)


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			win = Vector2i(int(p[0]), int(p[1]))
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots.path_join(name + ".png"))
	print("shot ", name, " ", img.get_size())


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _game() -> GameState:
	var g := GameState.new_game(42, "linden")
	for _day in range(6):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if _day < 5:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()
	# A finished first tree: the bonsai stands on the sill.
	g.grove.append({"species": "linden", "days": 6, "seed": 42})
	return g


func _run() -> void:
	await _wait(2)
	main.start(_game())
	main.enter_shed(false)
	await _wait(18)
	main.leave_shed()
	await _wait(70)
	main.journal.close_diary()
	main.journal.clear_pages()
	await _wait(10)
	_shot("hud_sunset_icon")
	main.toggle_run_to_sunset()
	await _wait(45)
	_shot("hud_sunset_running")
	print("running: speed x%.1f, hour %.2f" % [main.tree_view.time_speed(), main.state.sim.clock.clock_hour()])
	main.toggle_run_to_sunset()
	# The shed by day: two drawers open.
	main.enter_shed(false)
	main.shed_menu.show_menu(true)
	await _wait(20)
	for item in Shed.ITEMS:
		main.state.seen_pages["shed_used_" + item] = true
	main.shed.toggle_drawer("drawer01")
	main.shed.toggle_drawer("drawer03")
	await _wait(40)
	_shot("shed_drawers_open")
	main.shed.toggle_drawer("drawer03")
	await _wait(30)
	# Night: the sill under its lamp, then bonsai mode.
	var st: GameState = main.state
	while st.phase == GameState.Phase.DAY:
		st.tick(0.5)
	st.take_events()
	main.tree_view._time += 60.0
	await _wait(20)
	main.journal.clear_pages()
	await _wait(20)
	_shot("shed_night_bonsai_lamp")
	main.open_shed_item("bonsai")
	for _i in range(80):
		await process_frame
	main.journal.clear_pages()
	await _wait(10)
	_shot("bonsai_night_lamp")
	print("lamp: energy %.2f, shed daylight %.2f" % [main.shed.bonsai_lamp.light_energy, main.shed.daylight])
	quit()
