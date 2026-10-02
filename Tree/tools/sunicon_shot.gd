extends SceneTree
## 0.8.2.5 shots: the tree view's HUD with each candidate for the sunset picture
## (main.SUNSET_ICONS 1 to 4), the whole screen and a crop of the right-hand column of pictures.
## Run (phone look): godot --path . --rendering-method gl_compatibility -s tools/sunicon_shot.gd -- --phone --shots=C:/some/folder

var main: Node
var shots := ""
var win := Vector2i(450, 1000)
## The right-hand column of pictures, compass to sunset, in the 450x1000 window.
const COLUMN := Rect2i(322, 36, 128, 652)


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()


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
	for v in range(main.SUNSET_ICONS.size()):
		main.show_sunset_icon(v)
		await _wait(6)
		RenderingServer.force_draw(false)
		var img := root.get_viewport().get_texture().get_image()
		var name := "hud_sunset_%d_%s" % [v, main.SUNSET_ICONS[v]]
		img.save_png(shots.path_join(name + ".png"))
		img.get_region(COLUMN).save_png(shots.path_join(name + "_column.png"))
		print("shot ", name, " ", img.get_size())
	quit()
