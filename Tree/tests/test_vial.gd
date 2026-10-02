extends RefCounted
## 0.8.2.5 (specs/wish-compass-vial.md item 4): life force as a green vial, no number in play.
## Broken list 49 (a number visible, or a vial that does not fall in the run) and 50 (a boosted
## hour where the liquid and the calm-day mark stay level).
var t

const SIM_STEP := 1.0 / 30.0
var _number := RegEx.create_from_string("life force\\s*\\d")


func _day_game(seed: int) -> GameState:
	var g := GameState.new_game(seed, "linden")
	for _day in range(3):
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
			guard += 1
		while g.phase != GameState.Phase.DAY:
			g.tick(0.25)
	g.take_events()
	return g


func _vial() -> Vial:
	var v := Vial.new()
	v.size = Vector2(140, 165)
	t.root.add_child(v)
	return v


## Runs the day for `seconds` in small steps, the vial reading it as the HUD does each frame.
func _run(g: GameState, v: Vial, seconds: float) -> void:
	for _i in range(int(seconds / 0.25)):
		g.tick(0.25)
		v.track_day(g)


func test_the_sun_integral_matches_the_arc() -> void:
	var c := DayCycle.new()
	var whole := Vial.light_seconds(c, 0.0, c.daylight_fraction)
	t.check_near(whole, c.seconds_per_day * c.daylight_fraction * 2.0 / PI, 1e-3, "a whole day's sun")
	t.check_near(Vial.light_seconds(c, 0.0, c.daylight_fraction * 0.5), whole * 0.5, 1e-3, "half by noon")
	t.check_eq(Vial.light_seconds(c, 0.4, 0.2), 0.0, "never backwards")


func test_the_vial_fills_by_day_and_a_calm_day_shows_no_mark() -> void:
	var g := _day_game(5)
	var v := _vial()
	v.track_day(g)
	var morning := v.level
	_run(g, v, 30.0)
	t.check(v.level > morning + 0.1, "the liquid rises by day (%.2f to %.2f)" % [morning, v.level])
	t.check(not v.mark_visible, "a calm day has no pencil mark")
	t.check_near(v.mark, v.level, 0.02, "the mark is the liquid on a calm day")
	# A calm day fills it to the top by sunset.
	while g.phase == GameState.Phase.DAY:
		g.tick(0.25)
		v.track_day(g)
	t.check(v.level > 0.95, "a calm day ends full (%.2f)" % v.level)
	v.queue_free()


func test_a_boost_opens_a_gap_to_the_calm_mark() -> void:
	var g := _day_game(5)
	var v := _vial()
	_run(g, v, 10.0)
	var before := v.level
	g.boost_hour()
	_run(g, v, g.sim.clock.hour_seconds())
	t.check(v.mark_visible, "a boosted hour shows the pencil mark")
	t.check(v.mark > v.level + Vial.MARK_MIN_GAP, "the mark stands above the liquid (%.2f over %.2f)" % [v.mark, v.level])
	t.check(v.level > before, "the liquid still rises while boosted, only slower")
	# The gap stays for the rest of the day (the boost's cost).
	var gap := v.mark - v.level
	_run(g, v, 20.0)
	t.check(v.mark_visible and v.mark - v.level > gap * 0.5, "the gap stays after the boost")
	v.queue_free()


func test_the_vial_falls_in_the_root_run() -> void:
	var g := _day_game(5)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.take_events()
	var rv := RootView.new()
	var holder := Node.new()
	t.root.add_child(holder)
	holder.add_child(rv)
	rv.setup(g.ground, g.roots, g.sim.resources)
	g.dive()
	rv.begin_pick()
	t.check(rv.start_at(g.roots.graph.size() - 1), "the run starts")
	rv._update_hud()
	var start := rv.vial.level
	t.check(start > 0.5, "the night starts with the day's fill (%.2f)" % start)
	var bot := RootBot.new()
	for _i in range(int(8.0 / SIM_STEP)):
		if not g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP):
			break
	rv._update_hud()
	t.check(rv.vial.level < start - 0.05, "the liquid falls as the root grows (%.2f to %.2f)" % [start, rv.vial.level])
	t.check(not rv.vial.mark_visible, "no pencil mark at night")
	# No life force number on the night's HUD.
	for l in rv.hud.find_children("*", "Label", true, false):
		t.check(_number.search((l as Label).text) == null, "no life force number: '%s'" % (l as Label).text)
	holder.queue_free()


func test_no_life_force_number_on_the_day_hud() -> void:
	var tv := TreeView.new()
	t.root.add_child(tv)
	var g := _day_game(5)
	tv.setup(g)
	tv._update_hud()
	t.check(tv.vial != null and tv.vial.level > 0.0, "the day HUD has its vial")
	for l in tv.hud.find_children("*", "Label", true, false):
		t.check(_number.search((l as Label).text) == null, "no life force number: '%s'" % (l as Label).text)
	tv.queue_free()
