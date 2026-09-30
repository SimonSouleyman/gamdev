extends RefCounted
## 0.8: the brush pile with a hedgehog (specs/0.8.md section 4; broken list items 11 to 14).
var t

const FRAME: float = 1.0 / 30.0


func _night(g: GameState) -> void:
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, FRAME) and guard < 20000:
		guard += 1
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)


## Runs the day to its sunset hold, calling `each` with the share of the daylight after each tick.
func _day(g: GameState, each: Callable = Callable()) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
		if each.is_valid():
			each.call(minf(1.0, g.sim.clock.time_of_day / g.sim.clock.daylight_fraction))


## Runs `body` on a summer's day of the real calendar (the hedgehog's season).
func _in_summer(body: Callable) -> void:
	var before := Almanac.now_override
	Almanac.now_override = int(Time.get_unix_time_from_datetime_string("2026-07-14T12:00:00"))
	body.call()
	Almanac.now_override = before


func test_cut_branches_lie_on_the_pile_from_the_next_sunrise() -> void:
	var b := BrushPile.new()
	t.check_eq(b.stick_count(), 0, "no cuts, no pile")
	b.add_cut(12)
	t.check_eq(b.wood, 0, "a cut lies in the grass until sunrise")
	t.check(b.sunrise(3), "the pile appears at sunrise")
	t.check_eq(b.wood, 12, "with the cut's wood")
	var first := b.stick_count()
	t.check(first >= BrushPile.FIRST_STICKS + 3, "the first cut already shows as a few sticks (%d)" % first)
	b.add_cut(25)
	t.check(not b.sunrise(4), "only the first morning is the pile's first")
	t.check(b.stick_count() > first, "the pile grows visibly with the next cut")
	b.add_cut(10000)
	b.sunrise(5)
	t.check_eq(b.stick_count(), BrushPile.MAX_STICKS, "never more sticks than the phone budget")
	# Through the game: the diary notes the pile once.
	var g := GameState.new_game(14)
	_night(g)
	_day(g)
	g.cut_to_pile(9)
	g.phase = GameState.Phase.SUNSET
	_night(g)
	t.check_eq(g.brush.wood, 9, "the game puts yesterday's cut on the pile at sunrise")
	var lines := 0
	for e in g.diary.entries:
		if e["text"] == GameState.PILE_LINE:
			lines += 1
	t.check_eq(lines, 1, "one diary line when the pile first appears")


## Broken 12: never by day, never twice a day, never in late autumn, never before any wood.
func test_the_hedgehog_only_comes_at_dusk_once_a_day_in_season_after_cutting() -> void:
	var summer := {"month": 7, "day": 14}
	var b := BrushPile.new()
	for day in range(60):
		for share in [0.5, 0.95, 1.0]:
			t.check(not b.hedgehog_due(day, share, summer, 14), "no wood, no hedgehog")
	b.add_cut(BrushPile.HEDGEHOG_WOOD)
	b.sunrise(10)
	t.check(not b.hedgehog_due(10, 1.0, summer, 14) and not b.hedgehog_due(11, 1.0, summer, 14), "not before it has moved in")
	var shows := 0
	for day in range(12, 212):
		for share in [0.1, 0.3, 0.5, 0.7, 0.9, 0.92]:
			t.check(not b.hedgehog_due(day, share, summer, 14), "never by day (day %d, %.2f)" % [day, share])
		if b.hedgehog_due(day, 0.95, summer, 14):
			shows += 1
			b.hedgehog_day = day
			t.check(not b.hedgehog_due(day, 1.0, summer, 14), "not twice on day %d" % day)
	t.check(shows > 70 and shows < 130, "about every other evening (%d of 200)" % shows)
	for date in [{"month": 11, "day": 20}, {"month": 12, "day": 24}, {"month": 2, "day": 1}, {"month": 3, "day": 19}]:
		t.check_eq(BrushPile.hedgehog_chance(date), 0.0, "hibernating on %d/%d" % [date["month"], date["day"]])
		for day in range(300, 340):
			t.check(not b.hedgehog_due(day, 1.0, date, 14), "none in late autumn or winter")
	t.check(BrushPile.hedgehog_chance({"month": 11, "day": 5}) < BrushPile.hedgehog_chance(summer), "less often in late autumn before 20 November")
	t.check(BrushPile.hedgehog_chance({"month": 3, "day": 25}) > 0.0, "back in spring")


## Through the game: the hedgehog shows at dusk, once a day, and the first time writes its
## line with a sketch; it arrives within about a week of steady pruning (a cut a day).
func test_the_hedgehog_arrives_within_a_week_of_steady_pruning() -> void:
	_in_summer(func() -> void:
		for seed in [3, 14, 27, 42]:
			var g := GameState.new_game(seed)
			_night(g)
			_day(g)
			g.phase = GameState.Phase.SUNSET
			var first_cut := -1
			var seen := -1
			var outs: Dictionary = {}
			for _d in range(14):
				_night(g)
				var day := g.day_number()
				if first_cut < 0 and day >= 2:
					first_cut = day
				if first_cut >= 0:
					g.cut_to_pile(25)
				var before := g.brush.hedgehog_day
				_day(g, func(share: float) -> void:
					if g.brush.hedgehog_day != before and not outs.has(day):
						outs[day] = share
						t.check(share >= BrushPile.DUSK_FROM, "seed %d day %d: out at dusk (%.2f)" % [seed, day, share]))
				if g.brush.hedgehog_day == day and seen < 0:
					seen = day
			t.check(seen >= 0 and seen - first_cut <= 7, "seed %d: the hedgehog came %d days after the first cut" % [seed, seen - first_cut])
			t.check(Visitors.has_come(g, "hedgehog"), "seed %d: counts as this tree's visitor" % seed)
			var sketches := 0
			for e in g.diary.entries:
				if e.get("drawing", "") == "hedgehog":
					sketches += 1
			t.check_eq(sketches, 1, "seed %d: one line with a sketch, the first time" % seed)
	)


## Broken 11 and 14: the pile, the hedgehog and the wren change nothing in the tree. A game that
## feeds a big pile every day grows exactly like one without, so cutting for the pile never pays.
func test_the_pile_and_its_visitors_are_mood_only() -> void:
	_in_summer(func() -> void:
		var a := GameState.new_game(27)
		var b := GameState.new_game(27)
		for g in [a, b]:
			_night(g)
			_day(g)
			g.phase = GameState.Phase.SUNSET
		for _d in range(10):
			for g in [a, b]:
				_night(g)
			a.cut_to_pile(60)
			for g in [a, b]:
				_day(g)
		t.check(a.brush.hedgehog_day >= 0 and a.brush.wren_day >= 0, "the hedgehog and the wren came to the fed pile")
		t.check(b.brush.wood == 0 and b.brush.hedgehog_day < 0, "no pile, no visitors")
		t.check(SaveData.flatten_sim(a.sim.to_dict()) == SaveData.flatten_sim(b.sim.to_dict()), "the tree, its life force and nutrients are the same")
		t.check(a.roots.to_dict() == b.roots.to_dict() and a.ground.to_dict() == b.ground.to_dict(), "the roots and the soil are the same")
	)


func test_a_new_tree_starts_a_fresh_pile_and_the_bonsai_feeds_none() -> void:
	var g := GameState.new_game(14)
	g.brush.add_cut(90)
	g.brush.sunrise(4)
	g.brush.hedgehog_day = 7
	g.seen_pages["visitor_hedgehog"] = true
	var next := GameState.new_tree(15, "birch", g)
	t.check_eq(next.brush.wood, 0, "a new tree, a fresh pile")
	t.check(not Visitors.has_come(next, "hedgehog"), "the hedgehog comes anew to the next tree")
	var bonsai := g.ensure_bonsai(true)
	var fallen := g.brush.fallen
	var id := bonsai.graph.size() - 1
	bonsai.prune(id)
	t.check_eq(g.brush.fallen, fallen, "the bonsai's cuttings do not go to the pile")
	var back := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check(back.brush.to_dict() == g.brush.to_dict(), "the pile is saved and loaded")


## Broken 13: the pile lies at the edge away from the shed, outside the camera's orbit, and costs
## one draw call (the hedgehog one more): one mesh each, no shadow.
func test_the_pile_stays_out_of_the_way_and_costs_one_draw_call() -> void:
	for r in [18.0, 24.0, 30.0, 36.0, 42.0]:
		var p := BrushPile.position(r)
		var shed := Vector3(0.0, 0.0, r - 2.5)
		t.check(p.z < 0.0 and p.distance_to(shed) > r * 1.6, "r %.0f: on the far side from the shed (%.1f m)" % [r, p.distance_to(shed)])
		# TreeView keeps the camera within `room + 0.5` of the tree: max(r, 18) - 2.5.
		var orbit := maxf(r, Scenery.CLEARING_RADIUS) - 2.5
		t.check(Vector2(p.x, p.z).length() - BrushPile.HALF_DEPTH > orbit, "r %.0f: the pile lies outside the camera's orbit" % r)
		t.check(Vector2(p.x, p.z).length() + BrushPile.HALF_DEPTH < r + 0.5, "r %.0f: at the clearing edge" % r)
	var g := GameState.new_game(14)
	g.brush.add_cut(5000)
	g.brush.sunrise(3)
	var v := BrushPileView.new()
	t.root.add_child(v)
	v.setup(g)
	var geometry: Array = []
	for c in v.get_children():
		if c is GeometryInstance3D:
			geometry.append(c)
	t.check_eq(geometry.size(), 3, "three meshes: the pile, the hedgehog and the wren")
	for m: MeshInstance3D in geometry:
		t.check_eq(m.mesh.get_surface_count(), 1, "%s: one surface" % m.name)
		t.check(m.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF, "%s: no shadow pass" % m.name)
	t.check(v.pile.visible and not v.hedgehog.visible and not v.wren.visible, "by day only the pile shows")
	var tris: int = v.pile.mesh.surface_get_array_len(0) / 3
	t.check(tris < 8000, "a full pile stays small (%d triangles)" % tris)
	t.check(v.hedgehog.mesh.surface_get_array_len(0) / 3 < 3000, "the hedgehog is a low-count model")
	# Rain darkens the sticks.
	v.update(0.0, 0.0)
	var dry := v._pile_mat.albedo_color
	v.update(0.0, 1.0)
	t.check(v._pile_mat.albedo_color.get_luminance() < dry.get_luminance() * 0.8, "wet sticks are darker")
	# The hedgehog plays its moment at dusk, and not in the night.
	g.brush.hedgehog_day = g.day_number()
	g.phase = GameState.Phase.NIGHT
	v.update(0.1, 0.0)
	t.check(not v.hedgehog.visible, "not underground at night")
	g.phase = GameState.Phase.SUNSET
	v.update(0.1, 0.0)
	v.update(10.0, 0.0)
	t.check(v.hedgehog.visible, "out of the pile at dusk")
	v.update(BrushPileView.HOG_SECONDS, 0.0)
	t.check(not v.hedgehog.visible, "and back in again")
	v.update(0.1, 0.0)
	t.check(not v.hedgehog.visible, "once a day")
	v.free()
