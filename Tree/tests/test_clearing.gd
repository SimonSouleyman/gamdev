extends RefCounted
## The living clearing (design doc 17.6): shade under the crown, shade plants, mushrooms after
## rain, the collection and its diary lines.
var t


## A tree with a round crown of leaf clusters 4 to 7 m up (no growth needed).
func _crowned_sim(seed: int = 3) -> GrowthSim:
	var sim := GrowthSim.new(seed)
	var top := 1
	for y in [1.0, 2.0, 3.0, 4.0, 5.0]:
		top = sim.graph.add_node(top, Vector3(0, y, 0))
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	for i in range(160):
		var p := Vector3(rng.randf_range(-2.5, 2.5), rng.randf_range(4.0, 7.0), rng.randf_range(-2.5, 2.5))
		sim.graph.add_node(top, p)
	return sim


func test_a_seedling_casts_no_shade_worth_the_name() -> void:
	var sim := GrowthSim.new(3)
	var map := Clearing.shade_map(sim)
	t.check_eq(map.size(), Clearing.N * Clearing.N, "a full grid")
	t.check(not Clearing.has_shade(map), "no shade from a seedling")
	var c := Clearing.new(3)
	t.check_eq(c.update(sim, 3).size(), 0, "nothing comes up")
	t.check_eq(c.shade_since, -1, "the shade has not begun")


func test_the_crown_shades_the_north_side_most() -> void:
	var map := Clearing.shade_map(_crowned_sim())
	var north := Clearing.shade_at(map, 0.0, -3.0)
	var south := Clearing.shade_at(map, 0.0, 3.0)
	t.check(north > 0.3, "shade north of the trunk (%.2f)" % north)
	t.check(north > south + 0.2, "the sun stands in the south: north %.2f, south %.2f" % [north, south])
	t.check(Clearing.shade_at(map, 0.0, -14.0) < 0.05, "open meadow far out")
	t.check(Clearing.has_shade(map), "real shade")


func test_nothing_grows_on_the_bare_earth_and_plans_repeat() -> void:
	var c := Clearing.new(7)
	c.shade_since = 0
	var map := Clearing.shade_map(_crowned_sim())
	var a := c.plan(map, 30, 5000)
	t.check(a.size() > 50, "a shaded ground fills with plants (%d)" % a.size())
	for e in a:
		if (e["pos"] as Vector2).length() < Clearing.BARE_RADIUS:
			t.check(false, "a plant on the bare earth at %s" % e["pos"])
			return
	t.check_eq(c.plan(map, 30, 5000), a, "the same seed and shade give the same plants")
	t.check_eq(c.plan(map, 30, 40).size(), 40, "the budget caps the plants (no mushrooms today)")


func test_the_ground_changes_slowly_anemones_first_moss_last() -> void:
	var c := Clearing.new(7)
	var map := Clearing.shade_map(_crowned_sim())
	t.check_eq(c.plan(map, 5, 5000).size(), 0, "no plants before the shade began")
	c.shade_since = 5
	var early := Clearing.counts(c.plan(map, 6, 5000))
	t.check(early["anemone"] > 0, "anemones come first")
	t.check_eq(early["fern"] + early["moss"], 0, "no fern or moss after one day")
	var later := Clearing.counts(c.plan(map, 5 + Clearing.UNLOCK["fern"], 5000))
	t.check(later["fern"] > 0 and later["moss"] == 0, "ferns next, moss still to come")
	var last := Clearing.counts(c.plan(map, 5 + Clearing.UNLOCK["moss"] + 2, 5000))
	t.check(last["moss"] > 0, "moss in the deep shade at last")
	t.check(Clearing.counts(c.plan(map, 5 + 20, 5000))["anemone"] > early["anemone"], "the plants spread over the shade")


func test_mushrooms_come_after_rain_for_a_few_days() -> void:
	var c := Clearing.new(7)
	c.shade_since = 0
	var map := Clearing.shade_map(_crowned_sim())
	# Before FIRST_DAMP_DAY no damp morning brings them: only the rain does.
	t.check(not c.mushrooms_out(1), "no mushrooms on a dry day")
	t.check_eq(Clearing.counts(c.plan(map, 1, 5000))["mushroom"], 0, "none planned")
	c.after_rain(1)
	for d in range(1, 1 + Clearing.MUSHROOM_DAYS):
		t.check(c.mushrooms_out(d), "mushrooms stand on day %d" % d)
	t.check(Clearing.counts(c.plan(map, 2, 5000))["mushroom"] > 0, "mushrooms in the shade")
	t.check(Clearing.counts(c.plan(map, 2, 5000, 2))["mushroom"] <= 2, "the mushroom budget holds")
	t.check(not c.mushrooms_out(1 + Clearing.MUSHROOM_DAYS), "gone again after a few days")


func test_damp_mornings_are_seeded_and_rare() -> void:
	var c := Clearing.new(11)
	var damp := 0
	for d in range(Clearing.FIRST_DAMP_DAY, Clearing.FIRST_DAMP_DAY + 400):
		if c.is_damp_morning(d):
			damp += 1
	t.check(damp > 20 and damp < 110, "about %d%% of mornings are damp (%d of 400)" % [int(Clearing.DAMP_CHANCE * 100), damp])
	t.check(not c.is_damp_morning(1), "none while the tree is young")
	var again := Clearing.new(11)
	for d in range(20):
		t.check_eq(again.is_damp_morning(d), c.is_damp_morning(d), "same seed, same mornings")


func test_the_collection_records_each_kind_once_with_a_diary_line() -> void:
	var g := GameState.new_game(21)
	g.sim = _crowned_sim(21)
	var c := g.clearing
	t.check_eq(c.update(g.sim, 4), [] as Array[String], "the shade begins on day 4, nothing up yet")
	t.check_eq(c.shade_since, 4, "the shade is noted")
	var first := c.update(g.sim, 6)
	t.check(first.has("anemone"), "anemones on day 6")
	t.check_eq(c.update(g.sim, 7).has("anemone"), false, "each kind only once")
	c.after_rain(30)
	for d in range(8, 31):
		c.update(g.sim, d)
	for k in Clearing.KINDS:
		t.check(c.found.has(k), "%s found by day 30" % k)
	# The game writes the diary line at sunrise.
	var g2 := GameState.new_game(21)
	g2.sim = _crowned_sim(21)
	g2.clearing.shade_since = 0
	g2.sim.clock.day_count = 5
	g2._sunrise()
	var lines := g2.diary.lines_for_day(5).map(func(e: Dictionary) -> String: return str(e["text"]))
	t.check(lines.has(Clearing.FIRST_LINES["anemone"]), "a diary line when the anemones first come up")
	t.check(g2.take_events().has("clearing:anemone"), "and an event for the scenes")


func test_the_clearing_is_saved_and_its_collection_goes_on_to_the_next_tree() -> void:
	var g := GameState.new_game(5)
	g.clearing.found = {"anemone": 6, "fern": 12}
	g.clearing.shade_since = 4
	g.clearing.after_rain(9)
	var back := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(back.clearing.found, {"anemone": 6, "fern": 12}, "found kinds survive a save")
	t.check_eq(back.clearing.shade_since, 4, "the start of the shade survives")
	t.check(back.clearing.mushrooms_out(10), "the rain survives")
	var next := GameState.new_tree(8, "birch", g)
	t.check_eq(next.clearing.found, g.clearing.found, "the collection is kept")
	t.check_eq(next.clearing.shade_since, -1, "the new tree's ground starts sunny")
	t.check_eq(GameState.from_dict({"seed": 2, "sim": g.to_dict()["sim"]}).clearing.found, {}, "an old save without a clearing loads")
