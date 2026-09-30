extends RefCounted
## 0.7: the day's wish places a real, bigger deposit ahead of the newest root tip, and it glows
## at night (specs/0.7-candidates.md section 1, docs/notes/wish-0.7.md).
var t

const SEEDS: Array[int] = [3, 14, 27, 42]
const FRAME: float = 1.0 / 30.0


func _normal_mean(u: Underground, kind: int) -> float:
	var total := 0.0
	var n := 0
	for i in range(u.patches.size()):
		var p: Dictionary = u.patches[i]
		var c: Vector3 = p["center"]
		# Rich topsoil patches of the same kind (not the starter patch, not the deep water veins).
		if bool(p["wish"]) or int(p["kind"]) != kind or -c.y > Underground.TOPSOIL or Vector2(c.x, c.z).length() < 2.0:
			continue
		total += u.patch_amount(i, true)
		n += 1
	return total / maxf(1.0, n)


## One night with a full calm tank. Aimed: from the newest root tip toward the glow, then chasing
## deposits. Otherwise the stick drifts at random from the trunk (a player who does not plan).
## Returns [reached (bool), nutrients drunk].
func _night(u: Underground, roots: RootSystem, diary: Diary, day: int, aim: bool, wander: RandomNumberGenerator) -> Array:
	var res := Resources.new()
	res.life_force = roots.calm_life_force
	var glow: Dictionary = diary.glows(u)[0]
	var from := 0
	var bot := RootBot.new()
	if aim:
		from = Diary.newest_tip(roots)
		bot.goal = glow["center"]
		bot.goal_radius = float(glow["radius"]) * 0.5
	roots.start_run(from)
	var stick := Vector2.ZERO
	for frame in range(6000):
		if aim:
			stick = bot.stick_for(roots, u, res)
		elif frame % 60 == 0:
			stick = Vector2(wander.randf_range(-0.6, 0.6), wander.randf_range(-0.6, 0.3))
		if not roots.advance(stick, false, FRAME, u, res):
			break
	if roots.run_active:
		roots.end_run(u, res)
	var t4 := roots.run_totals
	return [diary.check_reached(u, roots, day, day + 1) >= 0, t4[0] + t4[1] + t4[2] + t4[3]]


func test_the_wish_places_a_bigger_deposit_ahead_of_the_newest_tip() -> void:
	var placed := 0
	var days := 0
	for seed in SEEDS:
		var u := Underground.new(seed)
		var roots := RootSystem.new(seed)
		var diary := Diary.new()
		var wander := RandomNumberGenerator.new()
		for day in range(1, 13):
			var tip := roots.graph.positions[Diary.newest_tip(roots)]
			diary.new_wish(u, day, seed, roots)
			days += 1
			var p := diary.wish_patch
			if p < 0:
				t.check(not diary.wish.is_empty(), "a day wish instead")
				continue
			placed += 1
			var patch: Dictionary = u.patches[p]
			var c: Vector3 = patch["center"]
			var r := float(patch["radius"])
			var ratio := u.patch_amount(p, true) / _normal_mean(u, int(patch["kind"]))
			t.check(ratio > 1.25 and ratio < 1.8, "seed %d day %d: about 1.5x a normal rich patch (%.2f)" % [seed, day, ratio])
			var ahead := Vector2(c.x - tip.x, c.z - tip.z).length()
			t.check(ahead > Diary.AHEAD_MIN - 0.01 and ahead < Diary.AHEAD_MAX + 0.01, "a few metres beyond the newest tip (%.1f m)" % ahead)
			t.check(-c.y < Underground.HINT_MAX_DEPTH, "in the topsoil, under a meadow hint")
			var goal := c - (c - tip).normalized() * r * 0.6
			t.check(Diary.line_cost(u, roots, tip, goal) <= roots.calm_life_force * Diary.REACH_SHARE, "a calm tank reaches it from the newest tip")
			t.check(diary.wish.contains(Underground.compass(c)), "the wish names its direction: " + diary.wish)
			var hinted := false
			for h in u.surface_hints():
				if (h["position"] as Vector3).distance_to(Vector3(c.x, 0, c.z)) < 0.01:
					hinted = hinted or str(h["kind"]) == ("rushes" if int(patch["kind"]) == Resources.Kind.WATER else "clover")
			t.check(hinted, "the meadow shows what the wish names")
			_night(u, roots, diary, day, true, wander)
	t.check(placed > days * 0.55, "most wishes point underground (%d of %d)" % [placed, days])


func test_the_wish_follows_what_the_tree_lacks() -> void:
	var u := Underground.new(8)
	var roots := RootSystem.new(8)
	var res := Resources.new()
	res.stock = PackedFloat32Array([1.0, 30.0, 10.0, 10.0])
	var dry := 0
	var any := 0
	for day in range(1, 30):
		var w := Diary.plan_wish(u, day, 8, roots, res)
		if int(w["kind"]) >= 0:
			any += 1
			dry += 1 if int(w["kind"]) == Resources.Kind.WATER else 0
	t.check(any > 0 and dry == any, "short of water: every wish underground is a damp patch (%d of %d)" % [dry, any])


func test_wish_deposits_survive_a_save_and_old_saves_load() -> void:
	var u := Underground.new(5)
	var before := u.dot_count()
	var diary := Diary.new()
	var roots := RootSystem.new(5)
	for day in range(1, 6):
		diary.new_wish(u, day, 5, roots)
	t.check(u.wish_deposits.size() >= 2, "a few deposits placed (%d)" % u.wish_deposits.size())
	var last := u.dot_count() - 1
	u.collect(PackedInt32Array([10, last]), Resources.new(), 1.0)
	var v := Underground.from_dict(JSON.parse_string(JSON.stringify(u.to_dict())))
	t.check_eq(v.dot_count(), u.dot_count(), "the same dots after a load")
	t.check(v.dot_positions[last].distance_to(u.dot_positions[last]) < 1e-4, "the placed dots come back where they were")
	t.check(v.dot_collected[last] == 1 and v.dot_collected[10] == 1, "and keep their state")
	t.check_eq(v.wish_patch_ids().size(), u.wish_patch_ids().size(), "the wish patches come back")
	# A save from before 0.7: no wish deposits, the older dots keep their state.
	var old := Underground.new(5)
	old.collect(PackedInt32Array([12]), Resources.new(), 1.0)
	var od := old.to_dict()
	od.erase("wish_deposits")
	var w := Underground.from_dict(od)
	t.check(w.dot_count() == before and w.dot_collected[12] == 1, "an old save loads as it was")


func test_a_patch_is_never_placed_in_or_behind_rock() -> void:
	var u := Underground.new(14)
	var roots := RootSystem.new(14)
	var first := {}
	var day := 1
	while int(first.get("kind", -1)) < 0:
		first = Diary.plan_wish(u, day, 14, roots)
		day += 1
	day -= 1
	# Wall in the planned spot: a big rock over it. The same morning now places it elsewhere,
	# clear of the rock and with a free line from the tip.
	var c: Vector3 = first["center"]
	u.rock_centers.append(c)
	u.rock_radii.append(2.5)
	var again := Diary.plan_wish(u, day, 14, roots)
	if int(again["kind"]) >= 0:
		var c2: Vector3 = again["center"]
		t.check(c2.distance_to(c) > 2.5 + float(again["radius"]), "placed away from the rock")
		t.check(Diary.line_cost(u, roots, Vector3.ZERO, c2) < INF, "with a free line from the tip")
	else:
		t.check(not str(again["text"]).is_empty(), "or a day wish")
	# The trunk walled in completely: nothing reachable, so only day wishes.
	u.rock_centers.append(Vector3(0, -1, 0))
	u.rock_radii.append(13.0)
	for d in range(1, 20):
		t.check_eq(int(Diary.plan_wish(u, d, 14, roots)["kind"]), -1, "nothing in reach: a day wish")


func test_aiming_for_the_glow_reaches_it_and_beats_random_steering() -> void:
	var stats := {true: [0, 0, 0.0], false: [0, 0, 0.0]}
	for seed in SEEDS:
		for aim in [true, false]:
			var u := Underground.new(seed)
			var roots := RootSystem.new(seed)
			var diary := Diary.new()
			var wander := RandomNumberGenerator.new()
			wander.seed = hash([seed, "wander"])
			for day in range(1, 13):
				diary.new_wish(u, day, seed, roots)
				if diary.glows(u).is_empty():
					continue
				var r := _night(u, roots, diary, day, aim, wander)
				stats[aim][0] += 1
				stats[aim][1] += 1 if r[0] else 0
				stats[aim][2] += r[1]
				u.regrow(GameState.REGROW_SHARE, day)
	var a: Array = stats[true]
	var w: Array = stats[false]
	print("  wish reached: aimed %d of %d (%.0f nutrients), random %d of %d (%.0f nutrients)" % [a[1], a[0], a[2], w[1], w[0], w[2]])
	t.check(a[0] >= 24, "enough wish nights to judge (%d)" % a[0])
	t.check(a[1] >= a[0] * 0.8, "a calm tank aimed at the glow reaches it on most nights (%d of %d)" % [a[1], a[0]])
	t.check(float(a[1]) / a[0] > 2.0 * float(w[1]) / maxf(1, w[0]), "aiming reaches it far more often than random steering")
	t.check(a[2] / a[0] > w[2] / maxf(1, w[0]), "and drinks more on those nights")


func test_a_missed_wish_glows_one_more_night_then_fades() -> void:
	var u := Underground.new(27)
	var roots := RootSystem.new(27)
	var diary := Diary.new()
	var day := 1
	while diary.wish_patch < 0:
		diary.new_wish(u, day, 27, roots)
		day += 1
	var p := diary.wish_patch
	var tonight := diary.glows(u)
	t.check_eq(tonight.size(), 1, "one glow on the wish's night")
	t.check_near(float(tonight[0]["strength"]), Diary.GLOW_TODAY, 1e-4, "at full strength")
	# Missed: the next morning brings a new wish, the old deposit glows faintly one more night.
	diary.new_wish(u, day, 27, roots)
	var faint := diary.glows(u).filter(func(g: Dictionary) -> bool: return int(g["patch"]) == p)
	t.check_eq(faint.size(), 1, "the missed deposit still glows the next night")
	t.check_near(float(faint[0]["strength"]), Diary.GLOW_YESTERDAY, 1e-4, "fainter")
	t.check(u.patch_amount(p) > u.patch_amount(p, true) * 0.99, "missing it cost nothing: it is all still there")
	# The morning after, it is gone.
	diary.new_wish(u, day + 1, 27, roots)
	var later := diary.glows(u).filter(func(g: Dictionary) -> bool: return int(g["patch"]) == p)
	t.check(later.is_empty(), "then it fades")


func _first_morning(seed: int) -> GameState:
	var g := GameState.new_game(seed)
	g.dive()
	g.start_run(0)
	while g.steer(Vector2(0.3, 0.05), false, FRAME):
		pass
	while g.phase != GameState.Phase.DAY:
		g.tick(0.5)
	return g


func test_the_wish_is_deterministic_from_the_seed() -> void:
	var a := _first_morning(13)
	var b := _first_morning(13)
	t.check(a.diary.wish.begins_with("Today"), "a wish: " + a.diary.wish)
	t.check_eq(a.diary.wish, b.diary.wish, "same seed, same wish")
	t.check_eq(a.ground.wish_deposits.size(), b.ground.wish_deposits.size(), "same deposits")


func test_reaching_it_writes_a_line_with_a_drawing_and_survives_a_save() -> void:
	# The first seed whose first morning wishes for something underground.
	var g: GameState = null
	for seed in range(14, 40):
		g = _first_morning(seed)
		if g.diary.wish_patch >= 0:
			break
	var p := g.diary.wish_patch
	t.check(p >= 0, "a wish underground")
	g.skip_time(1.0)
	t.check(g.phase == GameState.Phase.SUNSET, "evening")
	g.dive()
	t.check_eq(g.wish_glows().size(), 1, "the wish deposit glows tonight")
	g.sim.resources.life_force = g.roots.calm_life_force
	var glow: Dictionary = g.wish_glows()[0]
	g.start_run(Diary.newest_tip(g.roots))
	var bot := RootBot.new()
	bot.goal = glow["center"]
	bot.goal_radius = float(glow["radius"]) * 0.5
	var lines_before := g.diary.entries.size()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME) and guard < 6000:
		guard += 1
	var events := g.take_events()
	t.check(events.has("wish:%d" % p), "the wish event: " + str(events))
	var drawn := g.diary.entries.slice(lines_before).filter(func(e: Dictionary) -> bool: return e.has("drawing"))
	t.check_eq(drawn.size(), 1, "one diary line with a drawing")
	if drawn.size() == 1:
		t.check(str(drawn[0]["text"]).contains("wished for"), "the line: " + str(drawn[0]["text"]))
		t.check_eq(str(drawn[0]["drawing"]), Diary.drawing_for(g.ground, p), "rushes for water, clover for nitrogen")
	t.check(g.diary.wish_reached and g.wish_glows().is_empty(), "the glow settles once reached")
	t.check(g.roots.run_totals[int(g.ground.patches[p]["kind"])] > 1.0, "and the root drank from it")
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(h.diary.wish_patch, p, "the wish deposit survives a save")
	t.check(h.diary.wish_reached, "reached survives a save")
	t.check_eq(h.ground.dot_count(), g.ground.dot_count(), "the deposits survive a save")
	var kept := h.diary.entries.filter(func(e: Dictionary) -> bool: return e.has("drawing"))
	t.check_eq(kept.size(), 1, "the drawing survives a save")
	# Reached: tomorrow it does not glow as yesterday's missed wish.
	while g.phase != GameState.Phase.DAY:
		g.tick(0.5)
	t.check(g.diary.last_patch != p, "a reached wish does not linger")


func test_a_night_without_life_force_still_shows_the_glow() -> void:
	var g := GameState.new_game(3)
	g.diary.wish_patch = g.ground.add_wish_deposit(1, Resources.Kind.WATER, Vector3(4, -1, 3), 1.3, 40)
	g.sim.resources.life_force = 0.0
	g.dive()
	t.check(g.night_empty, "a quiet night")
	t.check_eq(g.wish_glows().size(), 1, "the glow is there on the quiet visit")


func test_the_ink_drawings_are_drawn() -> void:
	for kind in ["rushes", "clover"]:
		var img := InkSketch.image(kind)
		var inked := 0
		for y in range(0, img.get_height(), 2):
			for x in range(0, img.get_width(), 2):
				if img.get_pixel(x, y).a > 0.5:
					inked += 1
		t.check(inked > 150 and inked < 2500, "%s: a small line drawing (%d inked samples)" % [kind, inked])
