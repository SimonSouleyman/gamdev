extends RefCounted
## 0.8.2: hold the screen to fast-forward the day (specs/fast-forward.md, its "broken" list).
var t

## main.gd's fixed sim step.
const SIM_STEP := 1.0 / 30.0
const FRAME := 1.0 / 60.0


## A tree view in the scene tree with its own seeded game, by day.
func _view(seed: int = 7) -> TreeView:
	var tv := TreeView.new()
	t.root.add_child(tv)
	var g := GameState.new_game(seed)
	_play_to_day(g)
	tv.setup(g)
	return tv


## The first night with the root bot, then to the next morning: a sapling by day.
func _play_to_day(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(0)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.2:
		g.tick(0.5)
	g.take_events()


var _accum := 0.0


## One frame as main.gd runs it: the sim in fixed steps at the view's speed, then the view.
func _frame(tv: TreeView, dt: float = FRAME) -> void:
	_accum += dt * tv.time_speed()
	while _accum >= SIM_STEP:
		tv.state.tick(SIM_STEP)
		_accum -= SIM_STEP
	tv._process(dt)


func _frames(tv: TreeView, seconds: float) -> void:
	for _i in range(int(round(seconds / FRAME))):
		_frame(tv)


## Broken 1: a hold never boosts and a tap never starts the fast-forward.
func test_a_tap_boosts_and_a_hold_fast_forwards() -> void:
	var tv := _view()
	var clk := tv.state.sim.clock
	# A tap: under 0.6 s.
	tv._begin_press(Vector2(200, 600))
	var fastest := 1.0
	for _i in range(20):
		_frame(tv)
		fastest = maxf(fastest, tv.time_speed())
	tv._end_press(true, Vector2(200, 600))
	t.check(clk.boost_remaining > 0.0, "a short tap boosts an hour")
	t.check_eq(fastest, 1.0, "a tap never runs the day fast")
	clk.boost_remaining = 0.0
	clk.boost_active = false
	# A hold: a still finger past 0.6 s.
	tv._begin_press(Vector2(200, 600))
	_frames(tv, 0.5)
	t.check_eq(tv.time_speed(), 1.0, "not yet at 0.5 s")
	_frames(tv, 0.2)
	t.check(tv.fast_forwarding(), "held 0.7 s: the day runs fast")
	t.check(tv.hourglass.visible, "the hourglass shows while held")
	t.check(tv.time_speed() < TreeView.FAST_FORWARD, "it eases in, no jump (%.2f)" % tv.time_speed())
	_frames(tv, 0.6)
	t.check_eq(tv.time_speed(), TreeView.FAST_FORWARD, "then 4x")
	var hour := clk.clock_hour()
	_frames(tv, 1.0)
	var game_s := (clk.clock_hour() - hour) * clk.hour_seconds()
	t.check_near(game_s, TreeView.FAST_FORWARD, 0.15, "a held second is four seconds of the day (%.2f)" % game_s)
	tv._end_press(true, Vector2(200, 600))
	t.check_eq(clk.boost_remaining, 0.0, "a hold never boosts")
	t.check_eq(tv.time_speed(), 1.0, "release: the normal clock at once")
	_frame(tv)
	t.check(not tv.hourglass.visible, "the hourglass goes with the release")
	tv.free()


## Broken 5: while it runs, moving the finger turns nothing; a move before 0.6 s still turns the
## camera and never starts it. Two fingers (pinch) never start it, nor do the shears.
func test_hold_and_orbit_and_pinch_keep_apart() -> void:
	var tv := _view()
	tv._begin_press(Vector2(200, 600))
	_frames(tv, 0.8)
	var yaw := tv._yaw
	var pitch := tv._pitch
	tv._drag(Vector2(380, 700), Vector2(180, 100))
	t.check_eq(tv._yaw, yaw, "the camera does not turn while the day runs fast")
	t.check_eq(tv._pitch, pitch, "nor tilt")
	t.check(tv.fast_forwarding(), "and the hold goes on")
	tv._end_press(true, Vector2(380, 700))
	# A drag before 0.6 s turns the camera, and then a long press is still a turn.
	tv._begin_press(Vector2(200, 600))
	_frames(tv, 0.2)
	tv._drag(Vector2(260, 600), Vector2(60, 0))
	t.check(tv._yaw != yaw, "an early move turns the camera")
	_frames(tv, 1.0)
	t.check_eq(tv.time_speed(), 1.0, "a turning finger never fast-forwards")
	tv._end_press(true, Vector2(260, 600))
	# Two fingers.
	var a := InputEventScreenTouch.new()
	a.index = 0
	a.pressed = true
	a.position = Vector2(150, 600)
	tv._unhandled_input(a)
	tv._begin_press(a.position)
	var b := InputEventScreenTouch.new()
	b.index = 1
	b.pressed = true
	b.position = Vector2(300, 600)
	tv._unhandled_input(b)
	tv._begin_press(b.position)
	_frames(tv, 1.0)
	t.check_eq(tv.time_speed(), 1.0, "a pinch never starts it")
	tv._touches.clear()
	tv._pressing = false
	# The shears out: a long press on the tree is aiming, not fast-forward.
	tv.prune_mode = true
	tv._begin_press(Vector2(200, 600))
	_frames(tv, 1.0)
	t.check_eq(tv.time_speed(), 1.0, "with the shears out a hold aims the cut")
	tv._end_press(false)
	tv.prune_mode = false
	tv.free()


## Broken 3: it stops at the sunset hold, never dives, never runs underground; a page or the
## shed opening mid-hold ends it.
func test_it_stops_at_sunset_and_never_dives() -> void:
	var tv := _view()
	var g := tv.state
	var clk := g.sim.clock
	var dived := [false]
	tv.ground_tapped.connect(func() -> void: dived[0] = true)
	# A few game minutes before sunset.
	while clk.time_of_day < clk.daylight_fraction - 20.0 / clk.seconds_per_day:
		g.tick(0.5)
	tv._begin_press(Vector2(200, 900))
	var guard := 0
	while g.phase == GameState.Phase.DAY and guard < 2000:
		_frame(tv)
		guard += 1
	t.check_eq(g.phase, GameState.Phase.SUNSET, "the hold ran into the sunset hold")
	t.check_eq(tv.time_speed(), 1.0, "and stopped there")
	_frames(tv, 3.0)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "time holds at sunset while the finger stays down")
	tv._end_press(true, Vector2(200, 900))
	t.check(not dived[0], "releasing the hold does not dive")
	# A hold that starts at sunset is nothing (a tap there still dives, as before).
	tv._begin_press(Vector2(200, 900))
	_frames(tv, 1.0)
	t.check_eq(tv.time_speed(), 1.0, "no fast-forward at the sunset hold")
	tv._end_press(true, Vector2(200, 900))
	t.check(not dived[0], "nor a dive from a long press")
	# Underground: no fast-forward.
	g.dive()
	tv._begin_press(Vector2(200, 900))
	_frames(tv, 1.0)
	t.check_eq(tv.time_speed(), 1.0, "no fast-forward at night")
	tv._end_press(false)
	tv.free()
	# A page opening mid-hold ends it.
	var tv2 := _view(11)
	tv2._begin_press(Vector2(200, 600))
	_frames(tv2, 1.0)
	t.check(tv2.fast_forwarding(), "held")
	tv2.input_enabled = false
	_frame(tv2)
	t.check_eq(tv2.time_speed(), 1.0, "a page or the shed ends the hold")
	t.check_eq(tv2.state.sim.clock.boost_remaining, 0.0, "without a boost")
	tv2.free()


## A day run in fixed steps at a speed profile (game seconds per real second at each frame),
## with taps at fixed game hours. Returns what the night depends on.
func _held_day(seed: int, speeds: Callable) -> Dictionary:
	var g := GameState.new_game(seed)
	_play_to_day(g)
	var taps := [8.0, 8.5, 13.0]
	var accum := 0.0
	var real := 0.0
	var frame := 0
	while g.phase == GameState.Phase.DAY:
		accum += FRAME * float(speeds.call(frame))
		frame += 1
		real += FRAME
		while accum >= SIM_STEP and g.phase == GameState.Phase.DAY:
			g.tick(SIM_STEP)
			accum -= SIM_STEP
			if not taps.is_empty() and g.sim.clock.clock_hour() >= taps[0]:
				taps.pop_front()
				g.boost_hour()
	var spot := Vector3.ZERO
	for p in g.sim.graph.positions:
		spot += p
	var life := g.sim.resources.life_force
	g.dive()
	return {"nodes": g.sim.graph.size(), "spot": spot, "life": life, "empty": g.night_empty,
		"room": g.roots.run_room, "real": real, "metres": life / g.roots.cost_per_metre(Vector3.DOWN)}


## Broken 2: the same seed and taps give the same tree, tank and night when held. And the held
## day takes about a quarter of the real time.
func test_a_held_day_grows_the_same_tree_quicker() -> void:
	var calm := _held_day(5, func(_f: int) -> float: return 1.0)
	# Held from 9 o'clock to sunset with the ease, released twice on the way.
	var held := _held_day(5, func(f: int) -> float:
		var s := f * FRAME
		if s < 20.0 or (s > 40.0 and s < 42.0) or (s > 50.0 and s < 50.4):
			return 1.0
		return minf(TreeView.FAST_FORWARD, 1.0 + 6.0 * fmod(s, 1.0)))
	t.check_eq(held["nodes"], calm["nodes"], "the same tree (%d nodes)" % calm["nodes"])
	t.check(held["spot"].is_equal_approx(calm["spot"]), "node for node")
	t.check_eq(held["life"], calm["life"], "the same life force at sunset (%.2f)" % calm["life"])
	t.check_eq(held["empty"], calm["empty"], "the same kind of night")
	t.check_eq(held["room"], calm["room"], "the same room for tonight's root")
	t.check_eq(held["metres"], calm["metres"], "the same night length in metres")
	t.check(held["real"] < calm["real"] * 0.5, "held, the day took %.0f s instead of %.0f s" % [held["real"], calm["real"]])
	# Held all day from the morning: about a quarter of the real time (spec: about 30 s of daylight).
	var all_day := _held_day(5, func(_f: int) -> float: return TreeView.FAST_FORWARD)
	t.check_eq(all_day["nodes"], calm["nodes"], "held all day: the same tree")
	t.check_near(all_day["real"] / calm["real"], 0.25, 0.01, "a quarter of the time")


## Broken 6: no number, timer or skip button: only the world and a small drawn hourglass.
func test_it_shows_no_number_or_skip_button() -> void:
	var tv := _view()
	var glass: Object = tv.hourglass
	t.check(not (glass is Label) and not (glass is BaseButton), "the hourglass is a drawing, not text or a button")
	t.check_eq(tv.hourglass.mouse_filter, Control.MOUSE_FILTER_IGNORE, "and takes no touches")
	tv._begin_press(Vector2(200, 600))
	_frames(tv, 1.5)
	var words := []
	for l in tv.hud.find_children("*", "Label", true, false):
		words.append((l as Label).text.to_lower())
	var text := " ".join(words)
	t.check(not ("x4" in text or "4x" in text or "skip" in text or "fast" in text), "no speed or skip words in the HUD")
	t.check(tv._scenery.cloud_speed > 1.0, "the clouds race instead")
	tv._end_press(true, Vector2(200, 600))
	tv.free()
