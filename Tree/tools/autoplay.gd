extends SceneTree
## End-to-end autoplay of the real game scene: plants a seed, dives, steers the first root
## with the RootBot, rises at sunrise, boosts, moves the day on by dragging the sun, dives again
## and starts the second root by tapping a root. Uses real input events for taps, holds and drags.
## Writes screenshots when a window exists. Never touches the player's save.
##
## Run:  godot --path . -s tools/autoplay.gd -- --shots=C:/some/folder [--days=2]
##       godot --headless --path . -s tools/autoplay.gd            (logic only, no screenshots)
## Exit code 0 when every stage was reached.

var main: Node
var bot := RootBot.new()
var shots_dir: String = ""
var days: int = 2
var stage: int = 0
var stage_time: float = 0.0
var total_time: float = 0.0
var log_lines: PackedStringArray = PackedStringArray()
var failed: bool = false
var nights_done: int = 0
var seed: int = 42


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
	if DisplayServer.get_name() == "headless":
		shots_dir = ""
	if shots_dir != "":
		DirAccess.make_dir_recursive_absolute(shots_dir)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_log("seed %d" % seed)


func _log(s: String) -> void:
	var line := "[%6.1fs] %s" % [total_time, s]
	print(line)
	log_lines.append(line)


func _shot(name: String) -> void:
	if shots_dir == "":
		return
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots_dir.path_join(name + ".png"))
	_log("shot " + name)


func _click(pos: Vector2, pressed: bool) -> void:
	var e := InputEventMouseButton.new()
	e.button_index = MOUSE_BUTTON_LEFT
	e.pressed = pressed
	e.position = pos
	e.global_position = pos
	root.push_input(e, true)


func _move(pos: Vector2, rel: Vector2) -> void:
	var e := InputEventMouseMotion.new()
	e.position = pos
	e.global_position = pos
	e.relative = rel
	e.button_mask = MOUSE_BUTTON_MASK_LEFT
	root.push_input(e, true)


func _next(msg: String) -> void:
	_log("stage %d done: %s" % [stage, msg])
	stage += 1
	stage_time = 0.0


func _fail(msg: String) -> void:
	_log("FAIL at stage %d: %s" % [stage, msg])
	failed = true
	_finish()


func _finish() -> void:
	_log("finished: day %d, tree %d nodes %.2f m, roots %d nodes, %d diary lines" % [
		main.state.day_number(), main.state.sim.graph.size(), main.state.sim.height(),
		main.state.roots.graph.size(), main.state.diary.entries.size()])
	for e in main.state.diary.entries:
		_log("  diary day %d: %s" % [e["day"], e["text"]])
	quit(1 if failed else 0)


func _page() -> String:
	return main.journal.current_page()


func _process(delta: float) -> bool:
	if not has_meta("started"):
		# After main's own _ready (which runs once the tree starts): the seeded game.
		set_meta("started", true)
		main.start(GameState.new_game(seed))
		return false
	stage_time += delta
	total_time += delta
	if stage_time > 240.0:
		_fail("timed out waiting")
		return false
	var s: GameState = main.state
	var rv: RootView = main.root_view
	var tv: TreeView = main.tree_view
	# The bot steers whenever a run is active.
	if s.roots.run_active and rv.mode == RootView.Mode.RUN:
		rv.scripted_stick = bot.stick_for(s.roots, s.ground)
	else:
		rv.scripted_stick = null
	match stage:
		0:
			if stage_time > 1.0:
				if _page() != "planted":
					_fail("expected the planting page, got '%s'" % _page())
					return false
				_shot("01_planted_page")
				main.journal.close_page()
				_next("planting page read")
		1:
			if stage_time > 0.8:
				_shot("02_seed_at_sunset")
				_click(Vector2(360, 1050), true)
				_click(Vector2(360, 1050), false)
				_next("tapped the ground")
		2:
			if stage_time > 1.0 and not main._transitioning and _page() == "first_night":
				_shot("03_first_night_page")
				main.journal.close_page()
				_next("first night page read")
			elif stage_time > 8.0:
				_fail("no dive (phase %d, page '%s')" % [s.phase, _page()])
		3:
			if not s.roots.run_active and stage_time > 0.5:
				_fail("the first run did not start at the seed")
			elif stage_time > 5.0:
				_shot("04_first_root_run")
				_next("steering the first root")
		4:
			if _page() == "first_run_done":
				_log("run: %.1f m, drank %s" % [s.roots.run_length, s.roots.run_totals])
				_shot("05_run_done")
				main.journal.close_page()
				nights_done += 1
				_next("first run done")
		5:
			if s.phase == GameState.Phase.DAY and main._transitioning and stage_time > 0.5 and tv.dive_amount < 0.6:
				_shot("06_rising_at_dawn")
				_log("twinkles at dawn: %d" % tv.twinkle_count())
				_next("rising")
		6:
			if stage_time > 1.5 and stage_time - delta <= 1.5:
				_log("twinkles after 1.5 s of dawn: %d, tree %d nodes" % [tv.twinkle_count(), s.sim.graph.size()])
				_shot("06b_dawn_burst")
			if not main._transitioning and _page() == "sapling":
				_shot("07_sapling_page")
				main.journal.close_page()
				_next("sapling page read")
		7:
			# Hold anywhere to boost.
			if stage_time > 0.3 and not has_meta("held"):
				set_meta("held", true)
				_click(Vector2(360, 640), true)
			elif stage_time > 3.0:
				if not s.sim.clock.boost_active:
					_fail("holding did not boost")
					return false
				_shot("08_boosting")
				_click(Vector2(360, 640), false)
				_next("boosted for 3 s")
		8:
			# Orbit by dragging.
			if stage_time < 0.1:
				_click(Vector2(200, 700), true)
			elif stage_time < 1.5:
				_move(Vector2(200 + stage_time * 200, 700), Vector2(12, 0))
			else:
				_click(Vector2(500, 700), false)
				_shot("09_orbited")
				_next("orbited")
		9:
			# Let the day run fast until the nutrients are spent.
			main.time_scale = 20.0
			if s.sim.clock.time_of_day > 0.35 and not s.sim.nutrients_spent():
				_log("nutrients left at 0.35 of the day; emptying the stock to try the sun drag")
				for k in range(4):
					s.sim.resources.stock[k] = 0.0
			if _page() == "spent" or s.can_skip_time():
				main.time_scale = 1.0
				if _page() == "spent":
					_shot("10_spent_page")
					main.journal.close_page()
				_next("nutrients spent at %.2f of the day" % s.sim.clock.time_of_day)
			elif s.phase == GameState.Phase.SUNSET:
				main.time_scale = 1.0
				_log("the day ended before the nutrients were spent")
				stage = 11
				stage_time = 0.0
		10:
			# Drag the sun along its arc (the sky chart at the top of the screen).
			var arc: SunArc = tv.sun_arc
			if _page() != "":
				_shot("10_page_" + _page())
				main.journal.close_page()
				stage_time = -0.3
				return false
			if stage_time < 0.0:
				return false
			if not has_meta("t0"):
				if not arc.visible:
					_fail("the sun arc is not shown once nutrients are spent (phase %d, t %.3f)" % [s.phase, s.sim.clock.time_of_day])
					return false
				var p: Vector2 = arc.get_global_rect().position + arc.knob_position()
				_click(p, true)
				set_meta("t0", s.sim.clock.time_of_day)
				set_meta("p0", arc.progress)
				set_meta("sun_pos", p)
			elif stage_time < 1.0:
				# Move the finger a quarter of the way along the arc over one second.
				var target: Vector2 = arc.get_global_rect().position + arc._arc_point(float(get_meta("p0")) + 0.25 * stage_time)
				var p: Vector2 = get_meta("sun_pos")
				_move(target, target - p)
				set_meta("sun_pos", target)
			else:
				_click(get_meta("sun_pos"), false)
				_log("dragging the sun moved time from %.3f to %.3f" % [get_meta("t0"), s.sim.clock.time_of_day])
				_shot("11_sun_dragged")
				_next("sun dragged")
		11:
			main.time_scale = 20.0
			if s.phase == GameState.Phase.SUNSET:
				main.time_scale = 1.0
				_next("sunset")
		12:
			if stage_time > 0.5 and _page() != "":
				_shot("12_sunset_page_" + _page())
				main.journal.close_page()
				stage_time = 0.0
			elif stage_time > 0.5:
				_shot("13_sunset")
				_click(Vector2(360, 1050), true)
				_click(Vector2(360, 1050), false)
				_next("tapped the ground again")
		13:
			if stage_time > 1.0 and not main._transitioning:
				if s.night_empty:
					_log("empty night")
					_shot("14_empty_night")
					main.journal.close_page()
					stage = 15
					stage_time = 0.0
					return false
				if _page() == "pick":
					_shot("14_pick_page")
					main.journal.close_page()
					stage_time = 1.0
				elif stage_time > 2.0 and _page() == "":
					# Tap the middle of the first root.
					var id := s.roots.graph.size() / 3
					var p := rv.camera.unproject_position(s.roots.graph.positions[id])
					_click(p, true)
					_click(p, false)
					_log("tapped root node %d at %s" % [id, p])
					_next("picked a start point")
		14:
			if stage_time > 0.5 and not s.roots.run_active and not s.night_done:
				_fail("tapping a root did not start the run")
			if stage_time > 4.0 and stage_time - delta <= 4.0:
				_shot("15_second_run")
			if s.night_done and stage_time > 1.0:
				main.journal.close_page()
				_log("second run: %.1f m, drank %s" % [s.roots.run_length, s.roots.run_totals])
				_shot("16_second_run_done")
				nights_done += 1
				_next("second run done")
		15:
			if s.phase == GameState.Phase.DAY and not main._transitioning:
				if _page() != "":
					_shot("17_morning_page")
					main.journal.close_page()
				main.journal.open_diary()
				_next("second morning")
		16:
			if stage_time > 0.5:
				_shot("18_diary")
				main.journal.close_diary()
				_finish()
	return false
