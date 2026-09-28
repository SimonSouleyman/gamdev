extends SceneTree
## Screenshots of the garden shed (start menu), its options board, album and loading page.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6]

var main: Node
var shots := ""
var days := 6
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
	DirAccess.make_dir_recursive_absolute(shots)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		# A tree grown for a few days, at mid-morning.
		var g := GameState.new_game(42)
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
	if frame == 40:
		_shot("shed_menu")
	if frame == 42:
		# Two photos for the album, into a tool folder (never the player's album).
		Photos.DIR = "user://tool_photos"
		Photos.clear()
		main.shed.camera.current = false
		main.tree_view.camera.make_current()
		main.shed_menu.show_menu(false)
	if frame == 44:
		Photos.save_from(root.get_viewport(), 5, "morning")
		main.tree_view._yaw += 1.2
	if frame == 44 + 5:
		Photos.save_from(root.get_viewport(), 6, "camera")
		main.shed.camera.make_current()
		main.shed_menu.show_menu(true)
	if frame == 52:
		main.shed_menu.open_options()
	if frame == 66:
		_shot("shed_options")
		main.shed_menu._options.visible = false
		main.shed_menu.open_album()
	if frame == 75:
		_shot("shed_album")
		main.shed_menu._album.visible = false
		main.journal.open_diary()
	if frame == 90:
		_shot("shed_journal")
		main.journal.close_diary()
		main.shed_menu.show_loading(true)
	if frame == 100:
		_shot("loading")
		quit()
	return false
