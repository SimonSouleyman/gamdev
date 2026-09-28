extends SceneTree
## Screenshots of the garden shed (start menu): the workbench with its things and their labels,
## each thing hovered and mid-tap, the options pinboard (also in "clearer print"), the album,
## the journal, the seed bag, the flower pot's page, the "while you were away" page and the
## loading page.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6] [--species=linden]

var main: Node
var shots := ""
var days := 6
var species := "linden"
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
	DirAccess.make_dir_recursive_absolute(shots)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _grown_game() -> GameState:
	# A tree grown for a few days, at mid-morning.
	var g := GameState.new_game(42, species)
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
	return g


func _run() -> void:
	main.start(_grown_game())
	main.enter_shed(false)
	# First visit: every thing on the bench carries its label.
	await _wait(38)
	_shot("shed_menu")
	# Two photos for the album, into a tool folder (never the player's album).
	Photos.DIR = "user://tool_photos"
	Photos.clear()
	main.shed.camera.current = false
	main.tree_view.camera.make_current()
	main.shed_menu.show_menu(false)
	await _wait(2)
	Photos.save_from(root.get_viewport(), 5, "morning")
	main.tree_view._yaw += 1.2
	await _wait(5)
	Photos.save_from(root.get_viewport(), 6, "camera")
	main.shed.camera.make_current()
	main.shed_menu.show_menu(true)
	# Each thing hovered (only its label) and caught mid-motion after a tap.
	for item in Shed.ITEMS:
		main.state.seen_pages["shed_used_" + item] = true
	for item in Shed.ITEMS + ["door"]:
		main._shed_hover = item
		await _wait(12)
		main.shed.tap(item)
		await _wait(9)
		_shot("shed_tap_" + item)
		await _wait(40)
	main._shed_hover = ""
	await _wait(12)
	_shot("shed_used")
	main.open_shed_item("options")
	await _wait(12)
	_shot("shed_options")
	main._apply_setting("clearer_print", true)
	main.journal.set_setting("clearer_print", true)
	main.shed_menu.open_options()
	await _wait(6)
	_shot("shed_options_clear")
	main.shed_menu.close_boards()
	main.open_shed_item("pot")
	await _wait(8)
	_shot("shed_pot_page_clear")
	main.shed_menu.close_boards()
	main.open_shed_item("journal")
	await _wait(15)
	_shot("shed_journal_clear")
	main.journal.close_diary()
	main._apply_setting("clearer_print", false)
	main.journal.set_setting("clearer_print", false)
	main.open_shed_item("album")
	await _wait(10)
	_shot("shed_album")
	main.shed_menu.close_boards()
	main.open_shed_item("journal")
	await _wait(15)
	_shot("shed_journal")
	main.journal.close_diary()
	main.open_shed_item("pot")
	await _wait(8)
	_shot("shed_pot_page")
	main.shed_menu.close_boards()
	# Closed for two days and five hours: the torn "while you were away" page.
	main.state.apply_offline(2.0 * 86400.0 + 5.0 * 3600.0)
	main._show_away_page()
	await _wait(8)
	_shot("shed_away")
	main.shed_menu.close_boards()
	# The seed bag of a finished tree: plant the next seed.
	main.state.finished = true
	main.state.grove.append({"species": main.state.sim.species.id, "days": days, "seed": 42})
	main.open_shed_item("seeds")
	await _wait(10)
	_shot("shed_seeds")
	main.shed_menu.close_boards()
	main.shed_menu.show_loading(true)
	await _wait(10)
	_shot("loading")
	quit()
