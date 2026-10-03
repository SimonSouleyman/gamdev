extends RefCounted
## 0.8.2.4 (notes/ff-0.8.2.4.md): the sunset picture runs the rest of the day; the fast-forward's mark at
## 16x; the bonsai lamp; the bench's drawers; a night ended at once grows its whole budget
## (specs/0.8.md item 44). 16x itself is in test_fast_forward.gd.
var t

const SIM_STEP := 1.0 / 30.0
const FRAME := 1.0 / 60.0
var _accum := 0.0


func _view(seed: int = 7) -> TreeView:
	var tv := TreeView.new()
	t.root.add_child(tv)
	var g := GameState.new_game(seed)
	_play_to_day(g)
	tv.setup(g)
	_accum = 0.0
	return tv


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


## One frame as main.gd runs it.
func _frame(tv: TreeView) -> void:
	_accum += FRAME * tv.time_speed()
	while _accum >= SIM_STEP:
		tv.state.tick(SIM_STEP)
		_accum -= SIM_STEP
	tv._process(FRAME)


# --- the sunset picture -----------------------------------------------------------------

func test_the_sunset_picture_runs_to_the_sunset_hold_and_never_dives() -> void:
	var tv := _view()
	var g := tv.state
	var dived := [false]
	tv.ground_tapped.connect(func() -> void: dived[0] = true)
	t.check(tv.can_run_to_sunset(), "offered by day")
	t.check(tv.run_to_sunset(true), "a tap starts the run")
	_frame(tv)
	t.check(tv.fast_arrows.visible, "the ink arrows show while it runs")
	var real := 0.0
	var fastest := 1.0
	var guard := 0
	while g.phase == GameState.Phase.DAY and guard < 20000:
		_frame(tv)
		fastest = maxf(fastest, tv.time_speed())
		real += FRAME
		guard += 1
	t.check_eq(g.phase, GameState.Phase.SUNSET, "it ends at the sunset hold")
	t.check_eq(TreeView.FAST_FORWARD, 32.0, "0.8.2.8: holding runs 32x (Simon: doubled)")
	t.check(absf(real - TreeView.SUNSET_RUN_SECONDS) < 1.0, "0.8.2.8: the rest of the day passes in about 7 s (%.1f s)" % real)
	var clk := g.sim.clock
	var day_left_calm := clk.daylight_fraction * 0.8 * clk.seconds_per_day
	t.check(real < day_left_calm / 10.0, "quickly: %.1f s instead of %.0f s" % [real, day_left_calm])
	_frame(tv)
	t.check(not tv.running_to_sunset(), "and stops there")
	t.check_eq(tv.time_speed(), 1.0, "the normal clock again")
	t.check(not tv.fast_arrows.visible, "the arrows go")
	for _i in range(300):
		_frame(tv)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "time holds at the sunset: no dive, the night's root waits")
	t.check(not dived[0], "it never dives")
	t.check(not tv.can_run_to_sunset(), "not offered at dusk")
	t.check(not tv.run_to_sunset(true), "and cannot start there")
	g.dive()
	t.check(not tv.can_run_to_sunset(), "nor at night")
	t.check(g.can_start_run(), "the night's root run is still there to play")
	tv.free()


func test_it_eases_into_the_sunset() -> void:
	var tv := _view(3)
	var g := tv.state
	var clk := g.sim.clock
	tv.run_to_sunset(true)
	var last_speeds: Array = []
	var guard := 0
	while g.phase == GameState.Phase.DAY and guard < 20000:
		_frame(tv)
		last_speeds.append(tv.time_speed())
		guard += 1
	# The frames just before the sunset ran slower than full speed.
	var tail: Array = last_speeds.slice(maxi(0, last_speeds.size() - 4))
	var top: float = last_speeds.max()
	t.check(float(tail[0]) < top * 0.5, "the last frames slow down (%s)" % str(tail))
	t.check(clk.time_of_day >= clk.daylight_fraction - 1e-4, "and land on the sunset")
	tv.free()


func test_a_tap_stops_the_run_without_a_boost() -> void:
	var tv := _view(9)
	var clk := tv.state.sim.clock
	tv.run_to_sunset(true)
	for _i in range(60):
		_frame(tv)
	t.check(tv.fast_forwarding(), "running")
	var hour := clk.clock_hour()
	tv._begin_press(Vector2(200, 600))
	_frame(tv)
	t.check(not tv.running_to_sunset(), "a tap on the screen stops it")
	t.check_eq(tv.time_speed(), 1.0, "at once")
	tv._end_press(true, Vector2(200, 600))
	t.check_eq(clk.boost_remaining, 0.0, "that tap is no boost")
	t.check(clk.clock_hour() - hour < 1.0, "the day stopped running on")
	# The picture again: starts and stops it.
	t.check(tv.run_to_sunset(true), "the picture starts it again")
	tv.run_to_sunset(false)
	t.check(not tv.fast_forwarding(), "and stops it")
	# A page or the shed taking the input ends it too.
	tv.run_to_sunset(true)
	tv.input_enabled = false
	_frame(tv)
	t.check(not tv.running_to_sunset(), "a page or the shed ends it")
	tv.input_enabled = true
	# The shears out: no run.
	tv.prune_mode = true
	t.check(not tv.run_to_sunset(true), "not with the shears out")
	tv.prune_mode = false
	tv.free()


func test_not_offered_in_the_day_s_last_half_hour() -> void:
	var tv := _view(4)
	var g := tv.state
	var clk := g.sim.clock
	while clk.daylight_fraction - clk.time_of_day > 0.4 * clk.hour_seconds() / clk.seconds_per_day:
		g.tick(0.5)
	t.check_eq(g.phase, GameState.Phase.DAY, "still day")
	t.check(not tv.can_run_to_sunset(), "with under half an hour left the picture is not offered")
	tv.free()


## The run is the fast-forward's fixed steps: the same tree, tank and night as a watched day.
func test_a_run_day_grows_the_same_tree() -> void:
	var a := GameState.new_game(5)
	_play_to_day(a)
	while a.phase == GameState.Phase.DAY:
		a.tick(SIM_STEP)
	var tv := TreeView.new()
	t.root.add_child(tv)
	var b := GameState.new_game(5)
	_play_to_day(b)
	tv.setup(b)
	_accum = 0.0
	tv.run_to_sunset(true)
	var guard := 0
	while b.phase == GameState.Phase.DAY and guard < 40000:
		_frame(tv)
		guard += 1
	t.check_eq(b.sim.graph.size(), a.sim.graph.size(), "the same tree (%d nodes)" % a.sim.graph.size())
	t.check_near(b.sim.resources.life_force, a.sim.resources.life_force, 0.05, "about the same life force at sunset (%.2f)" % a.sim.resources.life_force)
	tv.free()


## 0.8.2.5: the fast-forward shows ink arrows (the sunset picture is the hourglass now); they
## pulse calmly, about once a second, whatever the day's speed.
func test_the_fast_arrows_pulse_about_once_a_second() -> void:
	var tv := _view()
	var arrows: Object = tv.fast_arrows
	t.check(arrows is FastArrows and not (arrows is Label), "a drawing, not a word")
	t.check(FastArrows.PULSE_SECONDS > 0.8 and FastArrows.PULSE_SECONDS < 1.6, "one pulse takes %.2f s" % FastArrows.PULSE_SECONDS)
	tv.run_to_sunset(true)
	_frame(tv)
	var lo := 1.0
	var hi := 0.0
	for _i in range(int(FastArrows.PULSE_SECONDS / FRAME) + 2):
		arrows._process(FRAME)
		lo = minf(lo, arrows.pulse())
		hi = maxf(hi, arrows.pulse())
	t.check(lo < 0.1 and hi > 0.9, "it swells and fades within a pulse (%.2f to %.2f)" % [lo, hi])
	tv.free()


func test_the_sunset_picture_exists() -> void:
	t.check(ResourceLoader.exists("res://ui/icons/sunset.png"), "the sunset picture is rendered (tools/render_icons.gd)")
	var tex := load("res://ui/icons/sunset.png") as Texture2D
	t.check(tex != null and tex.get_width() >= 128, "at the other pictures' size")


# --- the shed: bonsai lamp and drawers --------------------------------------------------

func _shed(canvas: Vector2i = Vector2i(450, 1000)) -> Array:
	var vp := SubViewport.new()
	vp.size = canvas
	vp.disable_3d = false
	t.root.add_child(vp)
	var shed := Shed.new()
	vp.add_child(shed)
	shed.bonsai_ready = true
	shed.camera.current = true
	shed.fit_view()
	return [vp, shed]


func test_the_bonsai_lamp_lights_the_bonsai_by_night_only() -> void:
	var pair := _shed()
	var shed: Shed = pair[1]
	var lamp := shed.bonsai_lamp
	t.check(lamp != null, "a lamp over the bonsai")
	shed.daylight = 1.0
	shed._process(0.016)
	t.check(not lamp.visible or lamp.light_energy < 0.01, "off by day")
	shed.daylight = 0.0
	shed._process(0.016)
	t.check(lamp.visible and lamp.light_energy > 0.5, "on at night (%.2f)" % lamp.light_energy)
	t.check(lamp.light_energy <= 3.0, "not too bright (%.2f)" % lamp.light_energy)
	# Above the bonsai's crown, aimed at it, and the bonsai within its reach and cone.
	var spot := shed.bonsai_spot.global_position
	var crown := spot + Vector3(0, 0.45, 0)
	t.check(lamp.global_position.y > crown.y, "the lamp hangs above the crown")
	var to := spot + Vector3(0, 0.15, 0) - lamp.global_position
	var aim := -lamp.global_transform.basis.z
	t.check(rad_to_deg(aim.angle_to(to)) < lamp.spot_angle * 0.5, "aimed at the bonsai (%.1f deg off)" % rad_to_deg(aim.angle_to(to)))
	t.check(to.length() < lamp.spot_range * 0.8, "the bonsai well within its reach")
	# Its light stays a pool: the bench is out of its reach.
	var bench := shed.to_global(Vector3(0.0, Shed.BENCH_TOP, Shed.BENCH_Z))
	t.check(bench.distance_to(lamp.global_position) > lamp.spot_range, "the bench lies outside its reach")
	# It shows in the shed's picture.
	var s := shed.camera.unproject_position(lamp.global_position)
	t.check(s.x > 0 and s.x < 450 and s.y > 0 and s.y < 1000, "the lamp shows in the shed's picture (%s)" % s)
	(pair[0] as Node).free()


func test_the_visible_drawers_open_and_close() -> void:
	var pair := _shed()
	var shed: Shed = pair[1]
	t.check(shed.drawers.size() >= 4, "the bench's drawers (%d)" % shed.drawers.size())
	var opened := 0
	for key in shed.drawers:
		var d: MeshInstance3D = shed.drawers[key]
		var box := d.get_aabb()
		var front := d.global_transform * Vector3(box.get_center().x, box.get_center().y, box.end.z)
		if shed.camera.is_position_behind(front):
			continue
		# The middle of the part of its front that shows on screen (a drawer cut by the edge
		# still opens from what shows of it).
		var s := shed.camera.unproject_position(front)
		var left := shed.camera.unproject_position(d.global_transform * Vector3(box.position.x, box.get_center().y, box.end.z))
		var right := shed.camera.unproject_position(d.global_transform * Vector3(box.end.x, box.get_center().y, box.end.z))
		var lo := clampf(minf(left.x, right.x), 0.0, 450.0)
		var hi := clampf(maxf(left.x, right.x), 0.0, 450.0)
		if hi - lo < 30.0 or s.y < 0 or s.y > 1000:
			continue
		s.x = (lo + hi) * 0.5
		# A tap on its front finds it (and the things on the bench still win their own taps).
		t.check_eq(shed.item_at(s), "", "%s: no menu thing over the drawer's front" % key)
		t.check_eq(shed.drawer_at(s), key, "%s: a tap on its front finds it" % key)
		var rest := d.position
		shed.toggle_drawer(key)
		t.check(shed.is_drawer_open(key), "%s opens" % key)
		t.check(shed.drawer_contents(key) != null, "%s has a place for its things" % key)
		opened += 1
		# The slide-out, end state (the tween's target): out by DRAWER_OUT toward the room.
		var tw: Tween = shed._busy["drawer_" + key]
		tw.custom_step(5.0)
		var moved: Vector3 = d.global_position - (d.get_parent() as Node3D).global_transform * rest
		var toward_eye := (shed.camera.global_position - d.global_position).normalized()
		t.check_near(moved.length(), Shed.DRAWER_OUT * 1.05, 0.03, "%s slid out (%.2f m)" % [key, moved.length()])
		t.check(moved.normalized().dot(toward_eye) > 0.3, "%s toward the room" % key)
		shed.toggle_drawer(key)
		(shed._busy["drawer_" + key] as Tween).custom_step(5.0)
		t.check(not shed.is_drawer_open(key), "%s closes" % key)
		t.check(d.position.is_equal_approx(rest), "%s back in place" % key)
	# (0.8.2.5, layout 08: the bench stands low, its upper row of drawers shows.)
	t.check(opened >= 2, "every visible drawer opens (%d)" % opened)
	t.check_eq(shed.drawer_at(Vector2(225, 80)), "", "a tap on the wall is no drawer")
	(pair[0] as Node).free()


# --- 44: a night ended at once grows its whole budget -----------------------------------

## Nights ended at once (from the second night): each grows nearly its whole node budget.
func test_a_night_ended_at_once_grows_its_whole_budget() -> void:
	var g := GameState.new_game(14)
	var bot := RootBot.new()
	var checked := 0
	for night in range(9):
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		g.dive()
		var tank := g.sim.resources.life_force
		if night == 0:
			g.start_run(0)
			var guard := 0
			while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
				guard += 1
		else:
			var r := g.roots
			var mains := r.main_root_count
			g.finish_run_early()
			var grown := r.side_nodes_grown[0] + r.side_nodes_grown[1]
			var allowed := mini(r.side_nodes_max, int(round(tank * r.side_nodes_per_life_force)))
			if tank >= 60.0:
				checked += 1
				t.check(grown >= mini(200, allowed - 20), "night %d, tank %.0f: %d of %d small-root nodes grown" % [night + 1, tank, grown, allowed])
			t.check(grown <= r.side_nodes_max, "never past the night's cap")
			t.check_eq(r.main_root_count, mains, "no main root")
			t.check_eq(r.side_start_level, 1.0, "nights with a root keep starting from main and fine roots only")
		while g.phase != GameState.Phase.DAY:
			g.tick(0.25)
	t.check(checked >= 3, "several nights with a tank of 60 or more (%d)" % checked)
	t.check_eq(g.roots.night_starts.size(), 9, "each night's start is kept")
	var again := RootSystem.from_dict(g.roots.to_dict(), 14)
	t.check_eq(again.night_starts, g.roots.night_starts, "and saved")


## The month: ended at once every night, linden seed 14 finishes by about day 40, and at least
## 4 days behind steering (dots finished day 30, notes/sim-0.8.2.3-at-once.md).
func test_ended_at_once_every_night_finishes_behind_steering() -> void:
	var g := GameState.new_game(14)
	var day := -1
	# As tools/strategies.gd counts: night, then the day; finished on that day.
	for d in range(45):
		g.dive()
		g.finish_run_early()
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		if g.finished:
			day = d + 1
			break
	t.check(g.finished and day <= 40, "ended at once every night: finished on day %d (by about 40)" % day)
	t.check(day >= 34, "at least 4 days behind steering (day 30): day %d" % day)


## 0.8.2.8: a phone too slow for the fixed steps takes coarser ones instead of a slower day.
func test_a_slow_phone_takes_coarser_steps() -> void:
	var main_script := load("res://main.gd")
	var m: Node = main_script.new()
	m._tick_usec = 300.0
	t.check_eq(m._ff_step(0.5), main_script.SIM_STEP, "a fast phone keeps the fixed steps")
	m._tick_usec = 6000.0
	t.check(m._ff_step(1.0) > main_script.SIM_STEP * 10.0, "a slow one owes a second in two steps (%.3f)" % m._ff_step(1.0))
	t.check_eq(m._ff_step(100.0), main_script.FF_MAX_STEP, "never coarser than FF_MAX_STEP")
	m.free()
