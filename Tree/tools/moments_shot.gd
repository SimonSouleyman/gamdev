extends SceneTree
## Screenshots of a day's moments (0.8.2.6, specs/journal-drawers-loop.md G2, G3): the real
## sunrise after a root night with the morning glow along the new roots (morning_glow_*), the
## afternoon's weather turn forced to each kind (weather_*), then the day run with the sunset
## picture from just after sunrise: each moment as fast-forward slows for it (moment_morning,
## moment_visitor, moment_weather), the ink ring at sunset (moment_sunset). Prints the clock and
## the speed around each moment. Plays main.tscn in an ephemeral run.
## Run: godot --path . [--rendering-method gl_compatibility] -s tools/moments_shot.gd --
##      --shots=C:/some/folder [--days=5] [--seed=42] [--size=450x1000] [--phone]

var main: Node
var shots := ""
var days := 5
var seed := 42
var win := Vector2i(450, 1000)


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			win = Vector2i(int(p[0]), int(p[1]))
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()


func _shot(name: String) -> Image:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots.path_join(name + ".png"))
	print("shot ", name, " ", img.get_size())
	return img


## A game on the night after `days` days, its root run done: main fast-forwards it to sunrise.
func _game() -> GameState:
	var g := GameState.new_game(seed, "linden")
	for day in range(days):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else Diary.newest_tip(g.roots))
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		if day < days - 1:
			while g.phase == GameState.Phase.NIGHT:
				g.tick(0.25)
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	g.take_events()
	for id in ["first_sunset", "compass", "sapling", "planted", "first_night", "first_run_done", "empty_night", "spent", "nutrient_spent"]:
		g.seen_pages[id] = true
	return g


func _wait(seconds: float) -> void:
	var until := Time.get_ticks_msec() + int(seconds * 1000.0)
	while Time.get_ticks_msec() < until:
		await process_frame


func _set_hour(h: float) -> void:
	var clk: DayCycle = main.state.sim.clock
	clk.time_of_day = (h - 6.0) / DayCycle.DAYLIGHT_HOURS * clk.daylight_fraction


## Turns the camera so the wish plant stands beside the tree, low in the frame.
func _aim() -> void:
	var tv: TreeView = main.tree_view
	if tv.wish_plant.patch_id < 0:
		return
	var best := INF
	var best_yaw: float = tv._yaw
	var vp := root.get_viewport().get_visible_rect().size
	for k in range(36):
		tv._yaw = TAU * k / 36.0
		tv._frame_camera(true)
		var cam: Camera3D = tv.camera
		if cam.is_position_behind(tv.wish_plant.place):
			continue
		var s := cam.unproject_position(tv.wish_plant.place)
		if s.x < vp.x * 0.15 or s.x > vp.x * 0.85 or s.y < vp.y * 0.4 or s.y > vp.y * 0.85:
			continue
		var score := absf(s.x - vp.x * 0.5) + absf(s.y - vp.y * 0.72) * 0.5
		if score < best:
			best = score
			best_yaw = tv._yaw
	tv._yaw = best_yaw
	tv._frame_camera(true)


func _close_pages() -> void:
	main.journal.close_diary()
	main.journal.clear_pages()


func _run() -> void:
	await process_frame
	main.start(_game())
	var tv: TreeView = main.tree_view
	print("night %d: new roots %s" % [main.state.day_number(), str(main.state.roots.night_starts[-1])])
	# The real sunrise: wait for the glow (main._rise starts it as the black lifts).
	var guard := 0
	while not tv.root_glow.visible and guard < 3000:
		await process_frame
		guard += 1
	if not tv.root_glow.visible:
		print("no morning glow")
		quit(1)
		return
	print("morning glow: %d segments, night roots %s, dawn burst %d new" % [tv.root_glow.segment_count, str(main.state.night_roots), main.state.sim.graph.size() - main.state.sim.dawn_size])
	var segs := Moments.night_segments(main.state.roots, main.state.night_roots.x, main.state.night_roots.y)
	var on := 0
	var far := 0.0
	var vr := root.get_viewport().get_visible_rect()
	for sg in segs:
		var p: Vector3 = sg["b"]
		far = maxf(far, Vector2(p.x, p.z).length())
		if not tv.camera.is_position_behind(p) and vr.has_point(tv.camera.unproject_position(Vector3(p.x, 0.0, p.z))):
			on += 1
	print("glow: %d of %d segments on screen, out to %.1f m; camera at %s" % [on, segs.size(), far, str(tv.camera.global_position)])
	await _wait(0.6)
	_shot("morning_glow_a")
	print("glow state ", tv.root_glow.glow_state(), " twinkles ", tv.twinkle_count())
	await _wait(0.7)
	_shot("morning_glow_b")
	await _wait(2.0)
	_close_pages()
	var g: GameState = main.state
	print("day %d wish: %s (patch %d)" % [g.day_number(), g.diary.wish, g.diary.wish_patch])
	for e in g.diary.lines_for_day(g.day_number()):
		print("  diary: ", e["text"])
	# The weather turn, each kind, in the afternoon light.
	_set_hour(13.5)
	_aim()
	await _wait(0.5)
	_shot("weather_calm")
	for kind in ["cloud", "shower", "breeze"]:
		tv.moment(Moments.WEATHER)
		tv.turn_kind = kind
		await _wait(Moments.TURN_IN + 1.2)
		_shot("weather_" + kind)
		tv._turn_t = -1.0
		await _wait(0.6)
	# The day with the sunset picture from just after sunrise: each moment slows it down.
	_set_hour(6.9)
	g.seen_pages.erase("visitor_butterflies")
	g.brush.wren_day = -1
	tv.wish_plant.set_open(false)
	_close_pages()
	_aim()
	await _wait(0.3)
	_shot("moment_before_morning_buds")
	tv.run_to_sunset(true)
	var last_hour := g.sim.clock.clock_hour()
	var watching := ""
	var since := 0
	var shot_taken := true
	var resumed := true
	guard = 0
	while g.phase == GameState.Phase.DAY and guard < 20000:
		await process_frame
		guard += 1
		var h := g.sim.clock.clock_hour()
		if tv.last_moment != watching and tv.moment_slow_left() > 0.0:
			watching = tv.last_moment
			since = Time.get_ticks_msec()
			shot_taken = false
			resumed = false
			print("moment %s at %.2f h, speed %.1f" % [watching, h, tv.time_speed()])
		var ms := Time.get_ticks_msec() - since
		if not shot_taken and ms >= 1200:
			shot_taken = true
			_shot("moment_" + watching)
			print("  %.1f s in: %.2f h, speed %.1f" % [ms / 1000.0, h, tv.time_speed()])
		if not resumed and tv.moment_slow_left() <= 0.0 and tv.time_speed() > 1.5:
			resumed = true
			print("  resumed at %.2f h (%.1f s after), speed %.1f" % [h, ms / 1000.0, tv.time_speed()])
		last_hour = h
	print("sunset at %.2f h; diary of the day:" % last_hour)
	for e in g.diary.lines_for_day(g.day_number()):
		print("  diary: ", e["text"])
	await _wait(0.2)
	_close_pages()
	_aim()
	await _wait(0.6)
	print("ring state ", tv.wish_plant.ring_state())
	_shot("moment_sunset")
	quit(0)
