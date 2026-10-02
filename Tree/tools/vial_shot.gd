extends SceneTree
## 0.8.2.5 shots (notes/vial-0.8.2.5.md): the life force vial through a day and a night in the
## real scene. A calm day early, at noon and at sunset; a boosted day with the pencil mark (and a
## close-up of the vial); the night's pick, the root run with the liquid falling, the run's end.
## Run (phone look): godot --path . --rendering-method gl_compatibility -s tools/vial_shot.gd -- --phone --shots=C:/some/folder [--size=450x1000]

var main: Node
var shots := ""
var win := Vector2i(450, 1000)
var bot := RootBot.new()


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


func _shot(name: String, crop: bool = false) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	if crop:
		# The vial's corner, three times larger.
		var s := float(img.get_width()) / 720.0
		var r := Rect2i(0, 0, int(300 * s), int(170 * s))
		img = img.get_region(r)
		img.resize(r.size.x * 3, r.size.y * 3, Image.INTERPOLATE_NEAREST)
	img.save_png(shots.path_join(name + ".png"))
	var v: Vial = main.tree_view.vial if main.state.phase != GameState.Phase.NIGHT else main.root_view.vial
	print("shot %s: life force %.1f, vial %.2f (shown %.2f), mark %.2f %s" % [name, main.state.sim.resources.life_force, v.level, v.shown_level(), v.mark, "shown" if v.mark_visible else "hidden"])


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _quiet() -> void:
	main.journal.close_diary()
	main.journal.clear_pages()


func _game(seed: int) -> GameState:
	var g := GameState.new_game(seed, "linden")
	for _day in range(6):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if _day < 5:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	for k in Pages.TEXTS:
		g.seen_pages[k] = true
	g.take_events()
	return g


## Lets the day run fast in the scene until the time of day reaches `share` of the daylight.
func _day_to(share: float) -> void:
	main.time_scale = 20.0
	var guard := 0
	while main.state.sim.clock.time_of_day < main.state.sim.clock.daylight_fraction * share and main.state.phase == GameState.Phase.DAY and guard < 4000:
		_quiet()
		await process_frame
		guard += 1
	main.time_scale = 1.0
	await _wait(50)
	_quiet()
	await _wait(10)


func _run() -> void:
	await _wait(2)
	# A calm day.
	main.start(_game(42))
	await _wait(30)
	_quiet()
	await _day_to(0.12)
	_shot("01_day_low")
	await _day_to(0.5)
	_shot("02_day_half")
	await _day_to(0.985)
	_shot("03_day_full")
	_shot("03b_day_full_vial", true)
	# The night: dive, pick, the root run, its end.
	while main.state.phase == GameState.Phase.DAY:
		main.state.tick(0.5)
	main.state.take_events()
	await _wait(10)
	main._on_ground_tapped()
	await _wait(200)
	_quiet()
	await _wait(20)
	_shot("06_night_pick")
	var rv: RootView = main.root_view
	rv.start_at(main.state.roots.graph.size() - 1)
	var frames := 0
	var took_mid := false
	var start: float = main.state.sim.resources.life_force
	while main.state.roots.run_active and frames < 6000:
		rv.scripted_stick = bot.stick_for(main.state.roots, main.state.ground, main.state.sim.resources)
		await process_frame
		frames += 1
		_quiet()
		if frames == 90:
			_shot("07_run_start")
		if not took_mid and main.state.sim.resources.life_force < start * 0.45:
			took_mid = true
			_shot("08_run_falling")
			_shot("08b_run_falling_vial", true)
		if main.state.sim.resources.life_force < start * 0.12 and frames % 10 == 0 and not has_meta("low"):
			set_meta("low", true)
			_shot("09_run_nearly_empty")
	rv.scripted_stick = null
	await _wait(60)
	_quiet()
	_shot("10_night_done")
	# A boosted day: three boost hours from mid-morning; the mark stays above the liquid.
	main.start(_game(7))
	await _wait(30)
	_quiet()
	await _day_to(0.18)
	for _i in range(3):
		main.state.boost_hour()
	await _day_to(0.34)
	_shot("04_day_boosted")
	_shot("04b_boosted_vial", true)
	await _day_to(0.7)
	_shot("05_after_boost_mark")
	_shot("05b_after_boost_vial", true)
	quit()
