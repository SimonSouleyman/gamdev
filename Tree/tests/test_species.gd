extends RefCounted
## The six species (design doc section 15): profiles, unlock order, saving, each quirk as a
## small hook in an existing mechanic, the finished tree and the next seed.
var t


func test_all_six_profiles_in_unlock_order() -> void:
	t.check_eq(Species.ORDER, ["linden", "birch", "beech", "sycamore", "alder", "oak"] as Array[String], "unlock order as in the table")
	var needs := {
		"linden": [1.0, 0.8, 0.3, 0.4], "birch": [0.7, 0.4, 0.2, 0.3], "beech": [0.9, 0.7, 0.4, 0.6],
		"sycamore": [1.0, 1.0, 0.4, 0.6], "alder": [1.4, 0.1, 0.5, 0.3], "oak": [0.8, 0.6, 0.5, 0.7],
	}
	var sizes := {"linden": [30, 12], "birch": [25, 6], "beech": [35, 12], "sycamore": [30, 10], "alder": [25, 6], "oak": [30, 14]}
	for sp in Species.all():
		t.check_eq(Species.from_id(sp.id).id, sp.id, "from_id round trip " + sp.id)
		for k in range(4):
			t.check_near(sp.needs[k], needs[sp.id][k], 1e-4, "%s need %d as in the table" % [sp.id, k])
		t.check_near(sp.max_height, sizes[sp.id][0], 1e-4, sp.id + " height")
		t.check_near(sp.max_crown_radius, sizes[sp.id][1], 1e-4, sp.id + " crown")
		t.check(sp.display_name != "" and sp.quirk_text != "" and sp.look_text != "", sp.id + " has a name and page texts")
	t.check_eq(Species.from_id("nonsense").id, "linden", "unknown ids fall back to the linden")
	t.check(Species.oak().target_days > Species.linden().target_days and Species.birch().target_days < Species.linden().target_days, "birch short, oak long")


func test_the_next_species_unlocks_when_the_previous_finishes() -> void:
	t.check_eq(Species.unlocked([]), ["linden"] as Array[String], "the linden always")
	t.check_eq(Species.unlocked(["linden"]), ["linden", "birch"] as Array[String], "a finished linden unlocks the birch")
	t.check_eq(Species.unlocked(["linden", "birch", "beech"]).size(), 4, "three finished, four offered")
	t.check_eq(Species.unlocked(["birch"]).size(), 2, "only the one after a finished species")
	t.check_eq(Species.unlocked([], true).size(), 6, "the test switch offers all six")


func test_species_is_saved_with_the_game() -> void:
	var g := GameState.new_game(3, "oak")
	g.grove.append({"species": "linden", "days": 30, "seed": 1})
	g.finished = true
	var back := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(back.sim.species.id, "oak", "species kept")
	t.check_eq(back.roots.species.id, "oak", "the roots know the species after a load")
	t.check(back.finished, "finished kept")
	t.check_eq(back.finished_species(), ["linden"], "grove kept")
	t.check(g.diary.entries[0]["text"].contains("pedunculate oak"), "the first diary line names the species")


func _noon_life_force(species: Species, day: int, boosted: bool) -> float:
	var s := GrowthSim.new(2)
	s.species = species
	for i in range(8):
		s.graph.add_node(1, Vector3(cos(i), 1.0, sin(i)) * 0.5)
	s.clock.day_count = day
	s.clock.time_of_day = s.clock.daylight_fraction * 0.5
	s.clock.boost_active = boosted
	s.growth_paused = true
	s.tick(0.5)
	return s.resources.life_force


func test_linden_blossom_week_with_bees() -> void:
	var l := Species.linden()
	t.check_near(l.life_force_factor(20, false), 1.2, 1e-4, "blossom week: a fifth more life force")
	t.check_near(l.life_force_factor(10, false), 1.0, 1e-4, "not before")
	t.check_near(l.life_force_factor(23, false), 1.0, 1e-4, "not after")
	t.check(_noon_life_force(l, 19, false) > _noon_life_force(l, 12, false) * 1.15, "the leaves really gather more")
	t.check_near(Species.birch().life_force_factor(20, false), 0.9, 1e-4, "no blossom week for others")
	var g := GameState.new_game(6)
	g.sim.graph.add_node(1, Vector3(0, 1, 0))
	g.sim.graph.add_node(2, Vector3(0, 1.3, 0))
	g.sim.clock.day_count = 18
	g._sunrise()
	var bees := false
	for e in g.diary.entries:
		bees = bees or str(e["text"]).contains("bees")
	t.check(bees, "a diary line when the bees come")
	t.check(g.take_events().has("blossom"), "a blossom event")


## A sim with a tall trunk and a skirt of tips below it, all in one column: the tips are shaded.
func _shaded_sim(species: Species) -> GrowthSim:
	var s := GrowthSim.new(9)
	s.species = species
	var prev := 1
	for i in range(20):
		prev = s.graph.add_node(prev, Vector3(0.1, 0.5 + i * 0.4, 0.1))
	for i in range(200):
		s.graph.add_node(2, Vector3(0.2 + (i % 10) * 0.05, 0.6, 0.2 + (i / 10) * 0.03))
	return s


func test_shade_dieback_birch_twice_beech_never() -> void:
	t.check(_shaded_sim(Species.linden()).shaded_tips().size() >= 200, "tips under the crown are shaded")
	var totals := {}
	for sid in ["linden", "birch", "beech"]:
		var n := 0
		for day in range(10):
			n += _shaded_sim(Species.from_id(sid)).shade_dieback(day)
		totals[sid] = n
	t.check(int(totals["linden"]) > 0, "shaded twigs die back slowly (%d)" % totals["linden"])
	t.check(int(totals["birch"]) > int(totals["linden"]) * 1.5, "birch about twice as fast (%d vs %d)" % [totals["birch"], totals["linden"]])
	t.check_eq(int(totals["beech"]), 0, "beech never")
	var s := _shaded_sim(Species.birch())
	var tips := s.tip_count()
	var died := s.shade_dieback(3)
	t.check_eq(s.tip_count(), tips - died, "dead tips are no leaves any more")


func test_birch_pioneer_roots_and_thin_crown() -> void:
	var lin := RootSystem.new(1)
	var bir := RootSystem.new(1)
	bir.species = Species.birch()
	var top := Vector3(2, -1, 0)
	t.check_near(bir.cost_per_metre(top), lin.cost_per_metre(top) * 0.7, 1e-4, "topsoil roots cost 30 % less")
	var deep := Vector3(2, -6, 0)
	t.check_near(bir.cost_per_metre(deep), lin.cost_per_metre(deep), 1e-4, "deep roots cost the same")
	t.check(_noon_life_force(Species.birch(), 5, false) < _noon_life_force(Species.linden(), 5, false), "the thin crown gives a little less life force")


func test_birch_twigs_hang() -> void:
	for sid in ["birch", "linden"]:
		var s := GrowthSim.new(1)
		s.species = Species.from_id(sid)
		var id := s.graph.add_node(1, Vector3(3.0, 2.0, 0.0))
		s._droop_twigs(id, 4.0)
		if sid == "birch":
			t.check(s.graph.positions[id].y < 2.0, "a birch twig out in the crown hangs")
		else:
			t.check_near(s.graph.positions[id].y, 2.0, 1e-5, "a linden twig does not")


func test_beech_patient() -> void:
	var b := Species.beech()
	t.check_near(b.life_force_factor(12, false), 1.25, 1e-4, "calm hours give a quarter more")
	t.check(b.life_force_factor(12, true) < Species.linden().life_force_factor(12, true), "a boost gives less")
	var lin := _shaded_sim(Species.linden())
	var bee := _shaded_sim(Species.beech())
	lin.resources.stock[0] = 100.0
	bee.resources.stock[0] = 100.0
	t.check_near(bee.drink_upkeep(), lin.drink_upkeep() * 1.3, 1e-3, "the leaves drink a third more water")
	var early := GrowthSim.new(1)
	early.species = b
	early.clock.day_count = 5
	var later := GrowthSim.new(1)
	later.species = b
	later.clock.day_count = 12
	t.check(early.max_pace() < GrowthSim.new(1).max_pace(), "slow for the first ten days")
	t.check(later.max_pace() >= GrowthSim.new(1).max_pace(), "then strong")


func test_water_upkeep_drinks_but_never_below_zero() -> void:
	var s := _shaded_sim(Species.linden())
	s.resources.stock[0] = 0.5
	s.drink_upkeep()
	t.check(s.resources.stock[0] >= 0.0 and s.resources.stock[0] < 0.5, "the leaves drink what there is")


func test_sycamore_twin_buds_fork_a_cut_tip() -> void:
	for sid in ["sycamore", "linden"]:
		var s := GrowthSim.new(1)
		s.species = Species.from_id(sid)
		var a := s.graph.add_node(1, Vector3(0, 0.5, 0))
		var tip := s.graph.add_node(a, Vector3(0.1, 0.7, 0))
		var before := s.graph.size()
		t.check_eq(s.prune(tip), 1, sid + ": one segment cut")
		var live_children := 0
		for c in s.graph.children[a]:
			if not s.graph.get_flag(c, "dead", false):
				live_children += 1
		if sid == "sycamore":
			t.check_eq(s.graph.size(), before + 2, "the cut tip forks into two new shoots")
			t.check_eq(live_children, 2, "both grow from the cut")
		else:
			t.check_eq(s.graph.size(), before, "no fork for other species")
	# A whole branched limb is not a shoot tip.
	var m := GrowthSim.new(1)
	m.species = Species.sycamore()
	var limb := m.graph.add_node(1, Vector3(0, 0.5, 0))
	m.graph.add_node(limb, Vector3(0.2, 0.7, 0))
	m.graph.add_node(limb, Vector3(-0.2, 0.7, 0))
	var n := m.graph.size()
	m.prune(limb)
	t.check_eq(m.graph.size(), n, "cutting a branched limb does not fork")


func test_alder_nodules_and_thirsty_roots() -> void:
	var res := Resources.new()
	var r := RootSystem.new(1)
	r.graph.add_node(0, Vector3(0, -5, 0))
	t.check_near(r.nodule_nitrogen(res), 0.0, 1e-6, "no nodules on a linden")
	r.species = Species.alder()
	var n := r.nodule_nitrogen(res)
	t.check_near(n, 5.0 * Species.alder().nodule_nitrogen, 1e-4, "nitrogen per metre of root")
	t.check_near(res.stock[Resources.Kind.NITROGEN], n, 1e-5, "added to the stock")
	# The same water deposit, drunk by a linden's and an alder's roots.
	var drawn := {}
	for sid in ["linden", "alder"]:
		var ground := Underground.new(4)
		var water := -1
		for i in range(ground.dot_count()):
			if ground.dot_kinds[i] == Resources.Kind.WATER:
				water = i
				break
		var roots := RootSystem.new(4)
		roots.species = Species.from_id(sid)
		roots.tapped[water] = true
		drawn[sid] = roots.drink_tapped(ground, Resources.new())[Resources.Kind.WATER]
	t.check_near(float(drawn["alder"]), float(drawn["linden"]) * 1.5, 1e-3, "alder drains water deposits faster")


func test_oak_taproot_and_crooked_wide_crown() -> void:
	var lin := RootSystem.new(1)
	var oak := RootSystem.new(1)
	oak.species = Species.oak()
	var p := Vector3(0, -6, 0)
	var surcharge := lin.cost_per_metre(p) - lin.cost_per_metre(Vector3(0, -0.01, 0))
	t.check_near(oak.cost_per_metre(p, Vector3(0, -0.9, 0.44)), lin.cost_per_metre(p) - surcharge * 0.5, 0.01, "pointing down: half the depth surcharge")
	t.check_near(oak.cost_per_metre(p, Vector3(1, 0, 0)), lin.cost_per_metre(p), 1e-4, "sideways: the full surcharge")
	t.check_near(lin.cost_per_metre(p, Vector3(0, -0.9, 0.44)), lin.cost_per_metre(p), 1e-4, "no taproot for a linden")
	var s := GrowthSim.new(1)
	s.species = Species.oak()
	s.clock.time_of_day = 0.3
	s.tick(0.1)
	t.check(s.colonizer.jitter > GrowthSim.BASE_JITTER, "crooked branches")
	t.check(s.crown_radius(30.0) > GrowthSim.new(1).crown_radius(30.0), "wider crown")
	t.check(Species.oak().pace < 1.0, "slow")


func test_a_finished_tree_offers_the_next_seed() -> void:
	var g := GameState.new_game(5, "linden")
	g.sim.species.finish_nodes = 4
	# The finished tree must also stand tall enough (FINISH_HEIGHT_SHARE of its species' height).
	g.sim.species.max_height = 1.0
	t.check(not g.can_plant_next(), "an unfinished tree plants nothing")
	t.check(g.can_plant_next(true), "unless the test switch is on")
	g.sim.graph.add_node(1, Vector3(0, 0.4, 0))
	g.sim.graph.add_node(2, Vector3(0, 0.6, 0))
	g.phase = GameState.Phase.DAY
	g.sim.clock.time_of_day = 0.2
	g.take_events()
	g.tick(0.1)
	t.check(g.finished, "full species size: finished")
	t.check(g.take_events().has("finished"), "a finished event for the page")
	t.check_eq(g.finished_species(), ["linden"], "into the grove")
	t.check_eq(g.unlocked_species(), ["linden", "birch"] as Array[String], "the birch unlocks")
	g.tick(0.1)
	t.check_eq(g.grove.size(), 1, "finished only once")
	g.seen_pages["planted"] = true
	var next := GameState.new_tree(77, "birch", g)
	t.check_eq(next.sim.species.id, "birch", "the new tree is a birch")
	t.check(not next.finished and next.is_seed(), "a new seed")
	t.check_eq(next.finished_species(), ["linden"], "the grove carries over")
	t.check(next.seen_pages.has("planted"), "pages already read carry over")
	t.check_eq(next.phase, GameState.Phase.SUNSET, "planted at sunset like the first")


func test_species_pages_in_the_journal() -> void:
	t.check(Pages.has("species_oak"), "a page per species")
	t.check(not Pages.has("species_pine"), "no page for species not in the game")
	t.check_eq(Pages.title("species_birch"), "Silver birch", "titled with the species name")
	t.check(Pages.body("species_sycamore").contains("Twin buds"), "the body names the quirk")
	t.check_eq(Pages.ids().size(), Pages.TEXTS.size() + 6, "the pages tab can list all six")
	t.check(Pages.has("finished"), "the finished page can be read again")


func test_photo_captions_name_the_tree() -> void:
	t.check_eq(Photos.caption("user://p/birch_day004_morning_123.png"), "Silver birch, day 4, morning", "species from the file name")
	t.check_eq(Photos.caption("user://p/linden_day012_camera_9.png"), "Linden, day 12, my photo", "older photos are lindens")


func test_the_test_switch_is_a_saved_setting() -> void:
	var j := Journal.new()
	t.check(j.settings.has("any_species") and not bool(j.settings["any_species"]), "off by default, saved with the settings")
	j.free()


func test_every_species_grows_every_day_early_on() -> void:
	for sid in Species.ORDER:
		var g := GameState.new_game(14, sid)
		for day in range(4):
			g.dive()
			g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
			var bot := RootBot.new()
			var guard := 0
			while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
				guard += 1
			while g.phase == GameState.Phase.NIGHT:
				g.tick(0.2)
			var before := g.sim.graph.size()
			while g.phase == GameState.Phase.DAY:
				g.tick(1.0)
			t.check(g.sim.graph.size() - before >= 20, "%s grows on day %d (%d)" % [sid, day + 1, g.sim.graph.size() - before])
