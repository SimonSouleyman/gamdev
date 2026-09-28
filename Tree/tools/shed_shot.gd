extends SceneTree
## Screenshots of the garden shed (start menu), its options board, album and loading page.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6] [--species=linden]
## --flip=25: instead, grow a tree for 25 days with a morning photo each day (into a tool folder),
## then open the album on the finished tree's flip-book page, photograph it mid-turn and at the
## end, and save the month as a video (the path is printed).

var main: Node
var shots := ""
var days := 6
var species := "linden"
var frame := 0
var flip := 0
var _game: GameState
var _flip_day := 0
var _flip_frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--flip="):
			flip = int(a.substr(7))
	DirAccess.make_dir_recursive_absolute(shots)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))


## Grows the game one day on: tonight's root with the bot, then to mid-morning.
func _grow_day(g: GameState) -> void:
	if g.phase == GameState.Phase.DAY:
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()


func _flip_frames() -> bool:
	frame += 1
	if frame == 2:
		_game = GameState.new_game(42, species)
		_grow_day(_game)
		main.start(_game)
		main.enter_shed(false)
		Photos.DIR = "user://tool_flip_photos"
		TimeLapse.DIR = "user://tool_timelapse"
		Photos.clear()
		main.shed.camera.current = false
		main.tree_view.camera.make_current()
		main.shed_menu.show_menu(false)
		_flip_day = 1
		_flip_frame = frame
	if frame < 3:
		return false
	var tv: TreeView = main.tree_view
	if _flip_day <= flip:
		# Each day: grow (from day 2 on), let the view catch up, then the morning photo.
		if frame == _flip_frame + 1 and _flip_day > 1:
			_grow_day(_game)
			tv.refresh_clearing()
			tv._rebuild()
			tv._frame_camera(true)
		if frame == _flip_frame + 5:
			Photos.save_from(root.get_viewport(), _game.day_number(), "morning", species)
			_flip_day += 1
			_flip_frame = frame
		return false
	var menu: ShedMenu = main.shed_menu
	if _flip_frame > 0:
		# The album of the finished tree: its flip-book page comes last.
		_flip_frame = -frame
		main.shed.camera.make_current()
		menu.show_menu(true)
		menu.tree_finished = true
		menu.open_album()
		print("album spreads: ", menu._spreads)
	var since := frame + _flip_frame
	var book: FlipBook = menu._flip
	if since == 20:
		_shot("flip_playing")
		# Stop the pages and turn one by hand, half way, for a still of the turn.
		book.playing = false
		book._show(book.pages.size() / 2, 0.8)
	if since == 24:
		_shot("flip_turning")
		book._show(book.pages.size() - 1, 0.0)
	if since == 28:
		_shot("flip_grown")
		_save_video(menu)
	if since > 400:
		quit()
	return false


func _save_video(menu: ShedMenu) -> void:
	var path: String = await menu.save_video(menu._flip_tree)
	var data := FileAccess.get_file_as_bytes(path)
	print("video: %s (%d bytes, %d frames)" % [ProjectSettings.globalize_path(path), data.size(), MjpegAvi.frame_count(data)])
	_shot("flip_saved")
	quit()


func _process(_d: float) -> bool:
	if flip > 0:
		return _flip_frames()
	frame += 1
	if frame == 2:
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
		# The clearing's collection page (what came up under the crown).
		main.journal._show_tab("clearing", false)
	if frame == 94:
		_shot("shed_journal_clearing")
		main.journal.close_diary()
		# The seed bag of a finished tree: plant the next seed.
		main.state.finished = true
		main.state.grove.append({"species": main.state.sim.species.id, "days": days, "seed": 42})
		main.shed_menu.open_seeds(main.state, false)
	if frame == 100:
		_shot("shed_seeds")
		main.shed_menu.close_boards()
		main.shed_menu.show_loading(true)
	if frame == 110:
		_shot("loading")
		quit()
	return false
