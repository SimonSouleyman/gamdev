extends RefCounted
## 0.7: the day's wish points at a real, bigger deposit underground that glows at night
## (specs/0.7-candidates.md section 1, docs/notes/wish-0.7.md).
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


func test_wish_deposits_are_real_bigger_patches_with_a_meadow_hint() -> void:
	for seed in SEEDS:
		var u := Underground.new(seed)
		var ids := u.wish_patch_ids()
		t.check_eq(ids.size(), Underground.WISH_DEPOSITS, "seed %d: every wish deposit is placed" % seed)
		var hints := u.surface_hints()
		for p in ids:
			var patch: Dictionary = u.patches[p]
			var c: Vector3 = patch["center"]
			var ratio := u.patch_amount(p, true) / _normal_mean(u, int(patch["kind"]))
			t.check(ratio > 1.3 and ratio < 1.8, "seed %d patch %d holds about 1.5x a normal rich patch (%.2f)" % [seed, p, ratio])
			t.check(-c.y < Underground.HINT_MAX_DEPTH, "in the topsoil, under a meadow hint")
			var hinted := false
			for h in hints:
				if (h["position"] as Vector3).distance_to(Vector3(c.x, 0, c.z)) < 0.01:
					hinted = hinted or str(h["kind"]) == ("rushes" if int(patch["kind"]) == Resources.Kind.WATER else "clover")
			t.check(hinted, "seed %d patch %d: the meadow shows what the wish names" % [seed, p])


func test_old_saves_keep_their_deposits() -> void:
	# A save from before the wish deposits holds only the older dots: they keep their state and
	# the new deposits start full.
	var u := Underground.new(5)
	var first := int(u.patches[u.wish_patch_ids()[0]]["first"])
	u.collect(PackedInt32Array([10, 11, 12]), Resources.new(), 1.0)
	var d := u.to_dict()
	var raw := Marshalls.base64_to_raw(d["collected"]).slice(0, first)
	var am := Marshalls.base64_to_raw(d["amounts"]).to_float32_array().slice(0, first)
	d["collected"] = Marshalls.raw_to_base64(raw)
	d["amounts"] = Marshalls.raw_to_base64(am.to_byte_array())
	var v := Underground.from_dict(d)
	t.check(v.dot_collected[10] == 1 and v.dot_collected[12] == 1, "old dots keep their state")
	t.check_near(v.dot_amounts[11], 0.0, 1e-4, "old amounts kept")
	t.check(v.dot_collected[first] == 0 and v.fullness(first) > 0.99, "the new deposits start full")


func test_the_wish_points_only_at_reachable_patches() -> void:
	var underground := 0
	var days := 0
	for seed in SEEDS:
		var u := Underground.new(seed)
		var roots := RootSystem.new(seed)
		for day in range(1, 21):
			var w := Diary.make_wish_target(u, day, seed, roots)
			days += 1
			var p := int(w["patch"])
			if p < 0:
				continue
			underground += 1
			t.check(Diary.reachable(u, p, roots), "seed %d day %d: the wish deposit is reachable" % [seed, day])
			t.check(str(w["text"]).contains(Underground.compass(u.patches[p]["center"])), "the wish names its direction")
	t.check(underground > days * 0.55, "most wishes point underground (%d of %d)" % [underground, days])


func test_a_patch_behind_rock_is_not_wished_for() -> void:
	var u := Underground.new(14)
	var p := u.wish_patch_ids()[0]
	t.check(Diary.reachable(u, p), "reachable from the trunk as generated")
	# Wall it in: a big rock between the trunk and the patch.
	var c: Vector3 = u.patches[p]["center"]
	u.rock_centers.append(c * 0.5)
	u.rock_radii.append(c.length() * 0.45)
	t.check(not Diary.reachable(u, p), "behind rock: not reachable from the trunk")
	for day in range(1, 40):
		t.check(int(Diary.make_wish_target(u, day, 14)["patch"]) != p, "never wished for while walled in")


## One night with a full calm tank from the best start; the wish deposit is aimed at, or the
## stick drifts at random from the trunk (a player who does not plan). Returns
## [reached (bool), nutrients drunk].
func _night(u: Underground, roots: RootSystem, diary: Diary, day: int, aim: bool, wander: RandomNumberGenerator) -> Array:
	var res := Resources.new()
	res.life_force = roots.calm_life_force
	var glow: Dictionary = diary.glows(u)[0]
	var from := 0
	var bot := RootBot.new()
	if aim:
		from = Diary.nearest_start_ids(roots, glow["center"], 1)[0]
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


func test_aiming_for_the_glow_reaches_it_and_beats_random_steering() -> void:
	var stats := {true: [0, 0, 0.0], false: [0, 0, 0.0]}
	for seed in SEEDS:
		for aim in [true, false]:
			var u := Underground.new(seed)
			var roots := RootSystem.new(seed)
			var diary := Diary.new()
			var wander := RandomNumberGenerator.new()
			wander.seed = hash([seed, "wander"])
			for day in range(1, 11):
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
	t.check(a[0] >= 20, "enough wish nights to judge (%d)" % a[0])
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
	if diary.wish_patch != p:
		t.check_near(float(faint[0]["strength"]), Diary.GLOW_YESTERDAY, 1e-4, "fainter")
	# The morning after, it is gone (unless the new wish happens to point at it again).
	diary.new_wish(u, day + 1, 27, roots)
	var later := diary.glows(u).filter(func(g: Dictionary) -> bool: return int(g["patch"]) == p)
	t.check(later.is_empty() or diary.wish_patch == p, "then it fades")


func test_reaching_it_writes_a_line_with_a_drawing_and_survives_a_save() -> void:
	var g := GameState.new_game(14)
	# The first night from the seed, then to the first morning.
	g.dive()
	g.start_run(0)
	while g.steer(Vector2(0.3, 0.05), false, FRAME):
		pass
	while g.phase != GameState.Phase.DAY:
		g.tick(0.5)
	# Point today's wish at the nearest wish deposit.
	var best := -1
	for p in g.ground.wish_patch_ids():
		if best < 0 or (g.ground.patches[p]["center"] as Vector3).length() < (g.ground.patches[best]["center"] as Vector3).length():
			best = p
	g.diary.wish_patch = best
	g.diary.wish = Diary.wish_text(g.ground, best)
	g.diary.wish_reached = false
	g.skip_time(1.0)
	t.check(g.phase == GameState.Phase.SUNSET, "evening")
	g.dive()
	t.check_eq(g.wish_glows().size(), 1, "the wish deposit glows tonight")
	g.sim.resources.life_force = g.roots.calm_life_force
	var glow: Dictionary = g.wish_glows()[0]
	g.start_run(Diary.nearest_start_ids(g.roots, glow["center"], 1)[0])
	var bot := RootBot.new()
	bot.goal = glow["center"]
	bot.goal_radius = float(glow["radius"]) * 0.5
	var lines_before := g.diary.entries.size()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME) and guard < 6000:
		guard += 1
	var events := g.take_events()
	t.check(events.has("wish:%d" % best), "the wish event: " + str(events))
	var drawn := g.diary.entries.slice(lines_before).filter(func(e: Dictionary) -> bool: return e.has("drawing"))
	t.check_eq(drawn.size(), 1, "one diary line with a drawing")
	if drawn.size() == 1:
		t.check(str(drawn[0]["text"]).contains("wished for"), "the line: " + str(drawn[0]["text"]))
		t.check_eq(str(drawn[0]["drawing"]), Diary.drawing_for(g.ground, best), "rushes for water, clover for nitrogen")
	t.check(g.diary.wish_reached and g.wish_glows().is_empty(), "the glow settles once reached")
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(h.diary.wish_patch, best, "the wish deposit survives a save")
	t.check(h.diary.wish_reached, "reached survives a save")
	var kept := h.diary.entries.filter(func(e: Dictionary) -> bool: return e.has("drawing"))
	t.check_eq(kept.size(), 1, "the drawing survives a save")
	# Reached: tomorrow it does not glow as yesterday's missed wish.
	while g.phase != GameState.Phase.DAY:
		g.tick(0.5)
	t.check(g.diary.last_patch != best, "a reached wish does not linger")


func test_a_night_without_life_force_still_shows_the_glow() -> void:
	var g := GameState.new_game(3)
	var p := g.ground.wish_patch_ids()[1]
	g.diary.wish_patch = p
	g.sim.resources.life_force = 0.0
	g.dive()
	t.check(g.night_empty, "a quiet night")
	t.check_eq(g.wish_glows().size(), 1, "the glow is there on the quiet visit")
