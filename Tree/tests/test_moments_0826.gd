extends RefCounted
## 0.8.2.6 (specs/journal-drawers-loop.md G2, G3; notes/moments-0.8.2.6.md): four moments at set
## times, fast-forward slowing to normal at each, the morning reveal (the glow along last night's
## new roots, the dawn burst) and the morning line naming the best thing the night reached.
## Broken items 57 and 58.
var t
const JOURNAL := preload("res://tests/test_journal.gd")

const SIM_STEP := 1.0 / 30.0
const FRAME := 1.0 / 60.0
var _accum := 0.0


func _night(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(Diary.newest_tip(g.roots) if g.roots.graph.size() > 1 else 0)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)


## Sunrise after `nights` nights with a root each (the first is the seed's).
func _morning_after(seed: int, nights: int) -> GameState:
	var g := GameState.new_game(seed)
	for _i in range(nights):
		_night(g)
	return g


## One frame as main.gd runs it: fixed steps, stopped at a moment while fast-forwarding, and the
## moments handed to the view as main._moment does.
func _frame(tv: TreeView, seen: Array) -> void:
	_accum += FRAME * tv.time_speed()
	while _accum >= SIM_STEP:
		tv.state.tick(SIM_STEP)
		_accum -= SIM_STEP
		if tv.state.moment_due() and tv.time_speed() > 1.0:
			_accum = minf(_accum, SIM_STEP)
			break
	for e in tv.state.take_events():
		if e.begins_with("moment:"):
			seen.append([e.substr(7), tv.state.sim.clock.clock_hour()])
			tv.moment(e.substr(7))
	tv._process(FRAME)


func test_the_moments_come_at_their_hours() -> void:
	var g := _morning_after(7, 2)
	g.take_events()
	var seen: Array = []
	while g.phase == GameState.Phase.DAY:
		var before := g.sim.clock.clock_hour()
		g.tick(SIM_STEP)
		for e in g.take_events():
			if e.begins_with("moment:"):
				seen.append(e.substr(7))
				var at := float(Moments.HOURS[e.substr(7)])
				t.check(before < at and g.sim.clock.clock_hour() >= at, "%s on its hour (%.2f)" % [e, at])
	t.check(seen.has(Moments.MORNING), "the morning moment came: %s" % str(seen))
	t.check(seen.has(Moments.WEATHER), "the afternoon's weather moment came")
	t.check(seen.find(Moments.MORNING) < seen.find(Moments.WEATHER), "in the day's order")
	t.check_eq(Moments.crossed(6.0, 20.0), [Moments.MORNING, Moments.VISITOR, Moments.WEATHER], "three by day; the sunset is the clock's hold")
	t.check(float(Moments.HOURS[Moments.MORNING]) > 6.0 + GameState.MORNING_DELAY / g.sim.clock.hour_seconds(), "the morning moment after the morning's wish scrap")


func test_the_visitor_moment_brings_one_visitor_and_is_quiet_without() -> void:
	var g := GameState.new_game(31)
	g.sim.clock.day_count = 3
	g._moment(Moments.VISITOR)
	t.check(not g.take_events().has("moment:visitor"), "a seed has no visitor: no moment, no slow-down")
	var h := _morning_after(31, 5)
	h.take_events()
	var came := false
	while h.phase == GameState.Phase.DAY:
		h.tick(SIM_STEP)
		if h.take_events().has("moment:visitor"):
			came = true
			t.check(h.today_visitor != "", "the day's visitor is known (%s)" % h.today_visitor)
	t.check(came or Visitors.has_come(h, "butterflies"), "the butterflies came at a late-morning moment")


func test_fast_forward_slows_to_normal_at_each_moment_then_carries_on() -> void:
	# Broken item 57: a moment skipped without being shown while fast-forwarding.
	for hold in [false, true]:
		var g := _morning_after(7, 3)
		while g.sim.clock.clock_hour() < 6.6:
			g.tick(SIM_STEP)
		g.take_events()
		var tv := TreeView.new()
		t.root.add_child(tv)
		tv.setup(g)
		_accum = 0.0
		var seen: Array = []
		if hold:
			# A still finger held by day (the hold), as _update_hold makes it.
			tv._pressing = true
			tv._drag_mode = "hold"
			tv._press_phase = GameState.Phase.DAY
		else:
			t.check(tv.run_to_sunset(true), "the sunset picture runs the day")
		var slow_frames := 0
		var after_moment := false
		var resumed := 0
		var guard := 0
		while g.phase == GameState.Phase.DAY and guard < 60000:
			var n := seen.size()
			_frame(tv, seen)
			if seen.size() > n:
				after_moment = true
				slow_frames = 0
			elif after_moment:
				if tv.time_speed() <= 1.0001:
					slow_frames += 1
				else:
					if hold:
						t.check(slow_frames * FRAME >= Moments.SLOW_SECONDS - 2.0 * FRAME, "%s (hold): normal speed for %.2f s" % [str(seen[-1][0]), slow_frames * FRAME])
					else:
						# 0.8.2.8: the hourglass keeps its seven seconds; moments do not slow it.
						t.check(slow_frames * FRAME < 0.2, "%s (hourglass): not slowed (%.2f s)" % [str(seen[-1][0]), slow_frames * FRAME])
					after_moment = false
					resumed += 1
			guard += 1
		var names: Array = seen.map(func(s: Array) -> String: return str(s[0]))
		t.check(names.has(Moments.MORNING) and names.has(Moments.WEATHER), "hold %s: each moment shown while fast-forwarding: %s" % [str(hold), str(names)])
		t.check(resumed >= names.size(), "hold %s: and the speed came back after each (%d of %d)" % [str(hold), resumed, names.size()])
		for s in seen:
			t.check(float(s[1]) - float(Moments.HOURS[s[0]]) < 0.1, "%s: stopped on its hour (%.3f)" % [str(s[0]), float(s[1])])
		t.check(g.phase == GameState.Phase.SUNSET, "hold %s: on to the sunset (the ring then the dive)" % str(hold))
		tv.free()


func test_moments_do_not_change_the_tree() -> void:
	var a := _morning_after(5, 2)
	while a.phase == GameState.Phase.DAY:
		a.tick(SIM_STEP)
	var b := _morning_after(5, 2)
	var tv := TreeView.new()
	t.root.add_child(tv)
	tv.setup(b)
	_accum = 0.0
	tv.run_to_sunset(true)
	var seen: Array = []
	var guard := 0
	while b.phase == GameState.Phase.DAY and guard < 60000:
		_frame(tv, seen)
		guard += 1
	t.check(seen.size() >= 2, "moments were shown")
	t.check_eq(b.sim.graph.size(), a.sim.graph.size(), "the same tree")
	tv.free()


func test_the_weather_turn_is_look_only_and_eases() -> void:
	t.check_eq(Moments.turn_kind(3, 4, false), Moments.turn_kind(3, 4, false), "the same turn for the same seed and day")
	var kinds := {}
	for d in range(40):
		kinds[Moments.turn_kind(3, d, false)] = true
		t.check(Moments.turn_kind(3, d, true) != "shower", "no extra shower on a rainy day")
	t.check_eq(kinds.size(), 3, "breeze, cloud and shower all come")
	t.check_near(Moments.turn_amount(0.0), 0.0, 1e-6, "starts calm")
	t.check_near(Moments.turn_amount(Moments.TURN_IN + 1.0), 1.0, 1e-6, "turns")
	t.check_near(Moments.turn_amount(Moments.TURN_IN + Moments.TURN_HOLD + Moments.TURN_OUT), 0.0, 1e-6, "and passes")
	var g := _morning_after(7, 2)
	var tv := TreeView.new()
	t.root.add_child(tv)
	tv.setup(g)
	var w := g.weather_today().duplicate()
	tv.moment(Moments.WEATHER)
	for _i in range(90):
		tv._process(FRAME)
	t.check(tv.turn_amount() > 0.9, "the %s shows" % tv.turn_kind)
	t.check_eq(g.weather_today(), w, "today's weather is unchanged")
	tv.free()


func test_the_wish_plant_opens_its_flowers_at_the_morning_moment() -> void:
	var g := _morning_after(7, 2)
	var tv := TreeView.new()
	t.root.add_child(tv)
	tv.setup(g)
	t.check(g.sim.clock.clock_hour() < float(Moments.HOURS[Moments.MORNING]), "just after sunrise")
	t.check_near(tv.wish_plant.bloom, 0.0, 1e-6, "in bud before the morning moment")
	tv.moment(Moments.MORNING)
	for _i in range(int(WishPlant.BLOOM_SECONDS / FRAME) + 4):
		tv.wish_plant._process(FRAME)
	t.check_near(tv.wish_plant.bloom, 1.0, 1e-6, "open after it")
	tv.free()


func test_a_morning_after_a_root_run_glows_along_the_new_roots() -> void:
	# Broken item 58.
	var g := _morning_after(7, 3)
	var nr := g.night_roots
	t.check(nr.x > 0 and nr.y > nr.x, "last night's new roots are known (%s)" % str(nr))
	# The rise takes a few seconds: the dawn burst has begun when the black lifts.
	for _i in range(120):
		g.tick(SIM_STEP)
	var tv := TreeView.new()
	t.root.add_child(tv)
	tv.setup(g)
	t.check(g.sim.graph.size() > g.sim.dawn_size, "the dawn burst grew (%d new)" % (g.sim.graph.size() - g.sim.dawn_size))
	t.check(tv.morning_reveal(), "the reveal glows")
	t.check(tv.root_glow.visible and tv.root_glow.segment_count > 0, "along %d new segments" % tv.root_glow.segment_count)
	tv._update_twinkles()
	t.check(tv.twinkle_count() > 0, "the dawn burst sparkles (%d)" % tv.twinkle_count())
	var steps := 0
	while tv.root_glow.visible and steps < 600:
		tv.root_glow._process(FRAME)
		steps += 1
	t.check(steps * FRAME > 1.5 and steps * FRAME < 3.0, "about two seconds (%.2f s)" % (steps * FRAME))
	# A night without a root: no glow.
	g.night_roots = Vector2i(-1, -1)
	t.check(not tv.morning_reveal(), "no glow without new roots")
	tv.free()
	# The segments lie under the roots, ordered by growth.
	var segs := Moments.night_segments(g.roots, nr.x, nr.y)
	t.check(segs.size() > 0 and float(segs[-1]["u"]) <= 1.0, "segments in growth order")


func test_the_morning_line_names_the_best_thing_reached() -> void:
	var lines := [Diary.morning_line("wish"), Diary.morning_line("rich:1"), Diary.morning_line("find:coin"), Diary.morning_line("find:fossil"), Diary.morning_line("rich"), Diary.morning_line("find:unknown")]
	for k in range(4):
		lines.append(Diary.morning_line("rich:%d" % k))
	for f in Underground.FIND_TEXTS:
		lines.append(Diary.morning_line("find:" + f))
	var fonts: Array[Font] = [load(Paper.BODY_FONT), load(Paper.CLEAR_FONT)]
	for l: String in lines:
		t.check(l.split(" ").size() <= Diary.MAX_WORDS, "short: " + l)
		var w := 0.0
		for f in fonts:
			w = maxf(w, f.get_string_size(l, HORIZONTAL_ALIGNMENT_LEFT, -1, JOURNAL.BODY_SIZE if f == fonts[0] else roundi(JOURNAL.BODY_SIZE * Paper.CLEAR_PRINT_SCALE)).x)
		t.check(w <= JOURNAL.LINE_WIDTH, "one line on the phone (%.0f px): %s" % [w, l])
		t.check(l != Diary.DRANK_LINE, "not the plain line: " + l)
	t.check_eq(lines[0], "The roots reached the wish.", "the wish")
	t.check_eq(lines[1], "The roots found rich soil by the clover.", "a rich patch")
	t.check_eq(lines[2], "The roots found a lost coin.", "a find")
	t.check_eq(lines[3], "The roots found a fossil shell.", "a find")
	# The best of the night: a wish beats a rich patch, a rich patch beats a find.
	var g := GameState.new_game(3)
	g._note_best("find:coin")
	g._note_best("rich:0")
	t.check_eq(g.night_best, "rich:0", "a rich patch over a find")
	g._note_best("find:fossil")
	g._note_best("wish")
	g._note_best("rich:2")
	t.check_eq(g.night_best, "wish", "the wish over all")
	# Written once, on the morning after, as the day's "drank" line (one line).
	var h := _morning_after(9, 2)
	while h.phase == GameState.Phase.DAY:
		h.tick(0.5)
	h.dive()
	h.finish_run_early()
	h.night_best = "find:coin"
	var day := h.day_number()
	var k := GameState.from_dict(JSON.parse_string(JSON.stringify(h.to_dict())))
	t.check_eq(k.night_best, "find:coin", "kept over a save")
	while h.phase != GameState.Phase.DAY:
		h.tick(0.25)
	var said := h.diary.lines_for_day(day + 1).filter(func(e: Dictionary) -> bool: return str(e.get("topic", "")) == "drank")
	t.check_eq(said.size(), 1, "one morning line")
	t.check(not said.is_empty() and str(said[0]["text"]) == "The roots found a lost coin.", "naming the find: %s" % str(said))
	t.check_eq(h.night_best, "", "once")
