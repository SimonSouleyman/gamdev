extends SceneTree
## Journal shots (0.8.2): a tree grown for a few days with the root bot, then the diary's day
## pages, the care page, the pages tab, several torn-out explanation pages and the seed bag (a
## finished tree: two species done, the rest locked), each in the normal hand and in "clearer
## print".
## Run: godot --path . --resolution 450x1000 -s tools/journal_shot.gd -- --shots=<folder> [--days=9] [--prefix=after_]
## For the phone's look add `--rendering-method gl_compatibility` before `--` and `--phone` after it.

var main: Node
var shots := ""
var days := 9
var prefix := ""
var frame := 0
const PAGES: Array[String] = ["planted", "first_night", "sapling", "first_sunset", "shears", "bonsai", "bonsai_wire", "species_oak"]


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--prefix="):
			prefix = a.substr(9)
	DirAccess.make_dir_recursive_absolute(shots)
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
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(prefix + name + ".png"))


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _grown_game() -> GameState:
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
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.4:
		g.tick(0.5)
	g.take_events()
	return g


func _print(on: bool) -> void:
	main._apply_setting("clearer_print", on)
	main.journal.set_setting("clearer_print", on)


func _run() -> void:
	var g := _grown_game()
	for id in PAGES:
		g.seen_pages[id] = true
	main.start(g)
	await _wait(10)
	for on in [false, true]:
		_print(on)
		var tag := "_clear" if on else ""
		var j: Journal = main.journal
		j.clear_pages()
		j.open_diary()
		j._show_tab("diary", false)
		await _wait(12)
		_shot("diary" + tag)
		print("diary line width %.0f (clearer print %s)" % [j._diary_text.size.x, on])
		j._show_tab("care", false)
		await _wait(8)
		_shot("care" + tag)
		j._show_tab("pages", false)
		await _wait(8)
		_shot("pages" + tag)
		j.close_diary()
		for id in PAGES:
			j.show_page(id, Pages.title(id), Pages.body(id))
			await _wait(14)
			_shot("page_" + id + tag)
			j._page_shown_at = -10.0
			j.close_page()
			await _wait(2)
		# The seed bag of a finished tree: the linden and the birch done, the beech next.
		main.enter_shed(false)
		await _wait(20)
		g.finished = true
		g.grove = [{"species": "linden", "days": 30, "seed": 42}, {"species": "birch", "days": 25, "seed": 43}]
		main.open_shed_item("seeds")
		await _wait(10)
		_shot("seeds" + tag)
		main.shed_menu.close_boards()
		g.finished = false
		g.grove = []
		main.open_shed_item("seeds")
		await _wait(10)
		_shot("seeds_unfinished" + tag)
		main.shed_menu.close_boards()
		main.leave_shed()
		await _wait(30)
	print("journal shots: %s" % shots)
	quit()
