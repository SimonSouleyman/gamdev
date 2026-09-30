extends SceneTree
## Screenshots of 0.8's save backup and sharing (specs/0.8.md sections 2 and 3): the pinboard with
## "copy my tree" and "load a copy", a copy made (PC: into the game's folder), "sure? tap again"
## for a good copy, the notes for a foreign and a newer file, the pinboard in "clearer print", the
## album with its "send" words, the Polaroid as it is sent (written beside the shots as
## polaroid.png), the flip-book page with "send", and the torn page offering a sunrise save.
## Everything goes to tool folders, never the player's save, album or copies.
## Run: godot --path . -s tools/backup_shot.gd -- --shots=C:/some/folder
## For the phone's look add `--rendering-method gl_compatibility` before `--` and `--phone` after it.

var main: Node
var shots := ""
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
	DirAccess.make_dir_recursive_absolute(shots)
	Photos.DIR = "user://tool_backup_photos"
	TimeLapse.DIR = "user://tool_backup_timelapse"
	Backup.COPIES_DIR = "user://tool_backup_copies"
	Polaroid.DIR = "user://tool_backup_sent"
	ShedMenu.open_folders = false
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))
	print("shot ", name)


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


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


func _run() -> void:
	var g := GameState.new_game(42)
	for _d in range(3):
		_grow_day(g)
	main.start(g)
	main.enter_shed(false)
	Photos.clear()
	# Three mornings for the album (the tree grows a day between them).
	var tv: TreeView = main.tree_view
	main.shed.camera.current = false
	tv.camera.make_current()
	main.shed_menu.show_menu(false)
	for i in range(3):
		if i > 0:
			_grow_day(g)
			tv.refresh_clearing()
			tv._rebuild()
			tv._frame_camera(true)
		await _wait(6)
		Photos.save_from(root.get_viewport(), g.day_number(), "morning", g.sim.species.id)
	main.shed.camera.make_current()
	main.shed_menu.show_menu(true)
	await _wait(20)
	var menu: ShedMenu = main.shed_menu
	var notes := menu.backup_notes
	menu.open_options()
	await _wait(6)
	_shot("01_pinboard")
	notes._on_copy()
	await _wait(12)
	_shot("02_copied")
	var copy := Backup.newest_copy()
	print("copy: ", ProjectSettings.globalize_path(copy), " ", FileAccess.get_file_as_bytes(copy).size(), " bytes")
	notes.check(copy)
	await _wait(4)
	_shot("03_load_armed")
	var odd := Backup.COPIES_DIR.path_join("holiday.zip")
	var f := FileAccess.open(odd, FileAccess.WRITE)
	f.store_string("not a tree")
	f = null
	notes.reset()
	notes.check(odd)
	await _wait(4)
	_shot("04_refused_foreign")
	notes.set_note(Backup.NOTES["newer"])
	await _wait(4)
	_shot("05_refused_newer")
	Paper.set_clear_print(true, root)
	notes.check(copy)
	await _wait(6)
	_shot("06_pinboard_clear_print")
	Paper.set_clear_print(false, root)
	notes.reset()
	menu.close_boards()
	menu.open_album()
	menu._album_index = 0
	menu._show_spread()
	await _wait(8)
	_shot("07_album_send")
	var sent: String = await menu.send_photo(0)
	print("polaroid: ", ProjectSettings.globalize_path(sent))
	if sent != "":
		DirAccess.copy_absolute(ProjectSettings.globalize_path(sent), shots.path_join("08_polaroid.png"))
	await _wait(4)
	_shot("09_album_sent_note")
	menu.close_boards()
	menu.tree_finished = true
	menu.open_album()
	await _wait(30)
	_shot("10_flip_send")
	menu.close_boards()
	var offer := MorningOffer.new()
	menu.add_child(offer)
	offer.offer(Time.get_unix_time_from_system() - 86400.0, g.tree_name(), g.day_number())
	await _wait(6)
	_shot("11_morning_offer")
	quit()
