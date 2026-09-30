extends RefCounted
## 0.6.3 care (docs/notes/care-0.6.3.md): the tree shows what it lacks, and pruning answers.
## Broken list items 11 to 14 (docs/tuning.md) each have a test here.
var t


## A night with the root bot; `before_dawn` runs once the root is done, before the sunrise.
func _night(g: GameState, before_dawn: Callable = Callable()) -> void:
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	if before_dawn.is_valid():
		before_dawn.call()
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)


func _day(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(1.0)


## Plays `days` whole days (night, then day) with the root bot.
func _play(days: int, seed: int = 14, species_id: String = "linden") -> GameState:
	var g := GameState.new_game(seed, species_id)
	for _d in range(days):
		_night(g)
		_day(g)
	return g


## Runs the day on to `share` of the daylight.
func _day_to(g: GameState, share: float) -> void:
	while g.phase == GameState.Phase.DAY and g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * share:
		g.tick(0.5)


func test_need_follows_the_days_coverage() -> void:
	t.check_near(Care.need_from(1.0), 0.0, 1e-4, "a full stock: no need")
	t.check_near(Care.need_from(Care.NEED_START), 0.0, 1e-4, "three quarters of a day: still no need")
	t.check_near(Care.need_from(0.0), 1.0, 1e-4, "an empty stock: a full need")
	t.check(Care.need_from(0.5) > 0.2 and Care.need_from(0.5) < 0.8, "half a day: a clear but partial need")


## Broken 11: a signal shows only while something is lacking, and is gone by mid-morning after
## a night that brought the missing kind.
func test_signal_shows_only_while_lacking_and_clears_next_morning() -> void:
	var g := _play(6)
	# A night that brought plenty: nothing shows.
	_night(g)
	for k in range(4):
		g.sim.resources.stock[k] = 500.0
	g.sim.assess_needs()
	g.sim.care_prev = PackedFloat32Array([0, 0, 0, 0])
	_day_to(g, 0.5)
	var sig := g.care_signals()
	t.check(sig[0] + sig[1] + sig[2] + sig[3] == 0.0, "nothing lacking, no signal (%s)" % sig)
	_day(g)
	# A night without water: the tree shows thirst the next day.
	_night(g)
	g.sim.resources.stock[Resources.Kind.WATER] = 0.0
	g.sim.assess_needs()
	_day_to(g, 0.5)
	sig = g.care_signals()
	t.check(sig[Resources.Kind.WATER] > 0.5, "thirsty by noon (%s)" % sig)
	t.check(sig[1] + sig[2] + sig[3] == 0.0, "one sign at a time (%s)" % sig)
	_day(g)
	# The night brings water again: gone well before noon.
	_night(g, func() -> void: g.sim.resources.stock[Resources.Kind.WATER] = 500.0)
	t.check_eq(g.sim.care_need[0], 0.0, "the sunrise sees the water")
	t.check(g.care_signals()[0] > 0.3, "at dawn the old sign is still there, easing out")
	_day_to(g, Care.EASE_SHARE + 0.02)
	t.check_eq(g.care_signals()[0], 0.0, "gone by mid-morning after the need was met")


## The real sunrise judges the need (not only assess_needs by hand).
func test_sunrise_judges_the_need() -> void:
	var g := _play(5)
	_day(g)
	g.dive()
	g.night_done = true
	g.sim.resources.stock[Resources.Kind.NITROGEN] = 0.0
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	# The old roots still trickle some nitrogen in overnight (softer tuning), so not a full 1.
	t.check(g.sim.care_need[Resources.Kind.NITROGEN] > 0.3, "no nitrogen at dawn: a clear need (%s)" % g.sim.care_need)
	t.check(g.sim.care_need[Resources.Kind.WATER] < 0.5, "water was there (%s)" % g.sim.care_need)


## Broken 12: the tree never shows a need the player cannot act on tonight.
func test_signal_only_when_a_deposit_is_in_reach() -> void:
	var g := _play(6)
	_night(g)
	# Plenty of the others, so potassium is the strongest need (only the strongest shows).
	for k in [Resources.Kind.WATER, Resources.Kind.NITROGEN, Resources.Kind.PHOSPHORUS]:
		g.sim.resources.stock[k] = 1000.0
	g.sim.resources.stock[Resources.Kind.POTASSIUM] = 0.0
	g.sim.assess_needs()
	_day_to(g, 0.5)
	var sig := g.care_signals()
	t.check(sig[Resources.Kind.POTASSIUM] > 0.5, "short of potassium, a deposit in reach: shown (%s)" % sig)
	var r := g.reach_for(Resources.Kind.POTASSIUM)
	t.check(not r.is_empty(), "the care page names a deposit")
	if not r.is_empty():
		t.check(float(r["distance"]) * g.roots.cost_per_metre(r["position"]) <= Care.expected_life_force(g) + 1e-3, "within tonight's life force")
		t.check(not g.roots.tapped.has(int(r["dot"])) and g.ground.dot_kinds[int(r["dot"])] == Resources.Kind.POTASSIUM, "an untapped potassium deposit")
	# Every potassium deposit drunk: nothing to act on tonight, so nothing shows.
	for i in range(g.ground.dot_count()):
		if g.ground.dot_kinds[i] == Resources.Kind.POTASSIUM:
			g.ground.dot_collected[i] = 1
	g.roots.tapped[-1] = true  # the cache key changes with the tapped deposits
	t.check_eq(g.care_signals()[Resources.Kind.POTASSIUM], 0.0, "no deposit in reach: no signal")
	# No life force and no calm day left (the evening): nothing is in reach either.
	var empty := Care.reachable(g, Resources.Kind.WATER, 0.0)
	t.check(empty.is_empty(), "no life force, nothing in reach")


func test_no_signal_on_a_seedling_or_a_finished_tree() -> void:
	var g := GameState.new_game(3)
	g.sim.care_need = PackedFloat32Array([1, 1, 1, 1])
	g.sim.care_prev = PackedFloat32Array([1, 1, 1, 1])
	t.check_eq(g.care_signals()[0], 0.0, "the seed shows nothing")
	var h := _play(4)
	h.sim.care_need = PackedFloat32Array([1, 0, 0, 0])
	h.sim.care_prev = PackedFloat32Array([1, 0, 0, 0])
	h.finished = true
	t.check_eq(h.care_signals()[0], 0.0, "a finished tree shows nothing")


## Broken 14: a thirsty or hungry tree grows slower, but never loses wood or height.
func test_a_short_tree_never_loses_wood_or_height() -> void:
	var g := _play(5)
	for kind in [Resources.Kind.WATER, Resources.Kind.NITROGEN]:
		for _d in range(3):
			var grown := g.sim.grown_nodes()
			var height := g.sim.height()
			_night(g)
			# The old roots drank at sunrise: take it away again, a night that brought none.
			g.sim.resources.stock[kind] = 0.0
			g.sim.assess_needs()
			t.check(g.sim.care_need[kind] > 0.9, "%s short at dawn" % Resources.KIND_NAMES[kind])
			# Shade dieback is the one soft loss the growth model has anyway (never from thirst).
			grown -= g.last_dieback
			_day(g)
			t.check(g.sim.grown_nodes() >= grown, "%s short: no wood lost (%d -> %d)" % [Resources.KIND_NAMES[kind], grown, g.sim.grown_nodes()])
			t.check(g.sim.height() >= height - 1e-3, "%s short: no height lost (%.2f -> %.2f)" % [Resources.KIND_NAMES[kind], height, g.sim.height()])


## The largest branch no bigger than `limit` segments (never the trunk base).
func _branch(sim: GrowthSim, limit: int, at_least: int = 4) -> int:
	var best := -1
	var best_n := 0
	for id in range(3, sim.graph.size()):
		if sim.graph.get_flag(id, "dead", false):
			continue
		var n := sim._subtree_size(id)
		if n <= limit and n >= at_least and n > best_n and sim.graph.positions[id].y > sim.height() * 0.3:
			best_n = n
			best = id
	return best


## Broken 13, second half: a cut shows by the next day: buds wake below it, and a share (not
## more) of the cut wood comes back.
func test_a_cut_answers_by_the_next_morning() -> void:
	var g := _play(8)
	_night(g)
	_day_to(g, 0.5)
	var sim := g.sim
	var id := _branch(sim, 40, 10)
	t.check(id >= 0, "found a branch to cut")
	var cut := sim.prune(id)
	var at := Vector3(float(sim.cuts[0]["at"][0]), float(sim.cuts[0]["at"][1]), float(sim.cuts[0]["at"][2]))
	var refund := sim.pending_refund()
	t.check(refund >= int(cut * 0.2) and refund <= int(ceil(cut * 0.4)), "a share of the cut comes back (%d of %d)" % [refund, cut])
	_day(g)
	var before := sim.graph.size()
	g.dive()
	g.night_done = true
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	# Right after sunrise: new shoots just below the cut.
	var near := 0
	for n in range(before, sim.graph.size()):
		if not sim.graph.get_flag(n, "dead", false) and sim.graph.positions[n].distance_to(at) < GrowthSim.PRUNE_BUD_REACH + 0.8:
			near += 1
	t.check(near >= 2, "new shoots near the cut by the next morning (%d)" % near)
	t.check(int(sim.last_cut.get("buds", 0)) >= 2, "two or three buds woke (%s)" % sim.last_cut)
	t.check(int(sim.last_cut.get("regrown", 0)) <= int(ceil(cut * 0.4)), "never more than 0.4 of the cut comes back")
	# The new shoots carry leaves (the crown fills in there).
	var leafy := HeroCrown.leafy_nodes(sim)
	var leafy_near := 0
	for n in leafy:
		if n >= before and sim.graph.positions[n].distance_to(at) < GrowthSim.PRUNE_BUD_REACH + 0.8:
			leafy_near += 1
	t.check(leafy_near >= 2, "leaves on the new shoots (%d)" % leafy_near)


## Broken 13, first half: pruning as much as allowed every day never gets a tree nearer its
## finish than not pruning (the finish counts grown wood: living and shed).
func test_pruning_every_day_never_finishes_sooner() -> void:
	var calm := _play(10)
	var cut := GameState.new_game(14)
	for _d in range(10):
		_night(cut)
		_day_to(cut, 0.5)
		var budget := int(cut.sim.living_nodes() * 0.2)
		var done := 0
		var guard := 0
		while done < budget and guard < 30:
			guard += 1
			var id := _branch(cut.sim, budget - done, 2)
			if id < 0:
				break
			done += cut.sim.prune(id)
		_day(cut)
	t.check(cut.sim.grown_nodes() < calm.sim.grown_nodes(), "pruned daily: further from the finish (%d vs %d grown)" % [cut.sim.grown_nodes(), calm.sim.grown_nodes()])
	t.check(not cut.finished, "not finished early")


## Several cuts in a day add up, capped by a share of a fifth of the tree.
func test_refund_is_capped_by_the_fifth() -> void:
	var g := _play(8)
	var sim := g.sim
	sim.cuts = [{"at": [0, 3, 0], "from": 3, "nodes": 5000}]
	t.check_eq(sim.pending_refund(), int(GrowthSim.PRUNE_REFUND * sim.living_nodes() * 0.2), "capped")
	sim.cuts = [{"at": [0, 3, 0], "from": 3, "nodes": 10}]
	t.check_eq(sim.pending_refund(), 3, "0.3 of a small cut")


## Thinning the crowded inside means fewer shaded twigs (and so less shade dieback).
func test_thinning_the_inside_lowers_shade() -> void:
	var g := _play(14)
	var sim := g.sim
	var shaded := sim.shaded_tips()
	t.check(shaded.size() > 0, "a grown crown has shaded twigs (%d)" % shaded.size())
	# Cut the branches whose wood stands above the shaded twigs.
	var above := {}
	for tip in shaded:
		var p := sim.graph.positions[tip]
		for id in range(3, sim.graph.size()):
			var q := sim.graph.positions[id]
			if not sim.graph.get_flag(id, "dead", false) and q.y > p.y + 0.5 and floori(q.x) == floori(p.x) and floori(q.z) == floori(p.z) and sim.graph.radii[id] < 0.03:
				above[id] = true
	var before := shaded.size()
	var n := 0
	for id in above.keys():
		if n >= 6:
			break
		if not sim.graph.get_flag(id, "dead", false) and sim._subtree_size(id) <= 40:
			sim.prune(id)
			n += 1
	t.check(sim.shaded_tips().size() < before, "fewer shaded twigs after thinning (%d -> %d)" % [before, sim.shaded_tips().size()])


func test_dead_wood_compaction_keeps_the_tree() -> void:
	var g := _play(6)
	var sim := g.sim
	var id := _branch(sim, 30, 5)
	sim.prune(id)
	sim.cuts.clear()
	var living := sim.living_nodes()
	var grown := sim.grown_nodes()
	var cost := sim.node_cost()
	var height := sim.height()
	var dropped := sim.compact_dead_wood(true)
	t.check(dropped > 0, "pruned wood left the graph (%d)" % dropped)
	t.check_eq(sim.living_nodes(), living, "every living segment kept")
	t.check_eq(sim.grown_nodes(), grown, "the finish count unchanged")
	t.check_near(sim.node_cost(), cost, 1e-5, "node cost unchanged")
	t.check_near(sim.height(), height, 1e-4, "height unchanged")
	for n in range(1, sim.graph.size()):
		t.check(sim.graph.parents[n] < n, "parents before children")
		if sim.graph.parents[n] >= n:
			break


func test_care_state_is_saved() -> void:
	var g := _play(4)
	g.sim.care_need = PackedFloat32Array([0.5, 0, 0, 0.25])
	g.sim.cuts = [{"at": [1.0, 2.0, 3.0], "from": 5, "nodes": 7}]
	g.sim.last_cut = {"day": 4, "nodes": 7, "buds": 0, "regrown": 0, "woken": false}
	g.sim.removed_nodes = 12
	var back := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_near(back.sim.care_need[0], 0.5, 1e-4, "need saved")
	t.check_eq(back.sim.cuts.size(), 1, "cuts saved")
	t.check_eq(int(back.sim.last_cut["nodes"]), 7, "last cut saved")
	t.check_eq(back.sim.removed_nodes, 12, "removed nodes saved")


func _mm() -> MultiMesh:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	return mm


## Shape first: nitrogen thins the new shoots' sprays, phosphorus or potassium leaves masses bare;
## no signal leaves the crown exactly as it was.
func test_crown_shape_cues() -> void:
	var g := _play(9)
	var plain := _mm()
	HeroCrown.populate(plain, g.sim, g.seed)
	var none := _mm()
	HeroCrown.populate(none, g.sim, g.seed, PackedFloat32Array([0, 0, 0, 0]))
	t.check_eq(none.instance_count, plain.instance_count, "no signal, the same crown")
	t.check(none.get_instance_transform(5) == plain.get_instance_transform(5), "sprays in the same places")
	var hungry := _mm()
	HeroCrown.populate(hungry, g.sim, g.seed, PackedFloat32Array([0, 1, 0, 0]))
	t.check(hungry.instance_count < plain.instance_count, "short of nitrogen: fewer sprays (%d < %d)" % [hungry.instance_count, plain.instance_count])
	var bare := _mm()
	HeroCrown.populate(bare, g.sim, g.seed, PackedFloat32Array([0, 0, 0, 1]))
	t.check(bare.instance_count < plain.instance_count * 0.95, "short of potassium: bare leaf masses (%d of %d sprays)" % [bare.instance_count, plain.instance_count])
	t.check(bare.instance_count > plain.instance_count * 0.5, "but most of the crown stays")
	var mat := HeroCrown.material()
	for u in ["thirst", "pale", "dull", "scorch", "sun_lift", "day_fill"]:
		t.check(mat.shader.get_shader_uniform_list().any(func(d: Dictionary) -> bool: return d["name"] == u), "crown shader has %s" % u)


func test_care_page_reads_the_tree() -> void:
	var g := _play(6)
	_night(g)
	g.sim.resources.stock[Resources.Kind.WATER] = 0.0
	g.sim.assess_needs()
	_day_to(g, 0.5)
	var text := ""
	for part in Care.page(g):
		text += str(part["title"]) + ": " + str(part["text"]) + "\n"
	t.check("Thirsty" in text, "names the thirst:\n" + text)
	t.check("blue drop dots" in text, "says which dots to steer for, by colour and shape (0.8)")
	t.check("The crown" in text and "The last cut" in text, "the crown's shape and the last cut")
	var id := _branch(g.sim, 30, 5)
	g.sim.prune(id)
	var after := ""
	for part in Care.page(g):
		after += str(part["text"]) + "\n"
	t.check("buds will wake" in after, "a fresh cut: what dawn will bring\n" + after)


## Broken 13 on sycamore (sim-0.6.3): cutting shoot tips every day forks each into twin buds;
## the forks are part of the day's growth, so the tree ends no nearer its finish than uncut.
func test_twin_buds_never_speed_a_sycamore_up() -> void:
	var calm := _play(8, 14, "sycamore")
	var cut := GameState.new_game(14, "sycamore")
	var forked := 0
	for _d in range(8):
		_night(cut)
		_day_to(cut, 0.5)
		var n := 0
		for id in range(3, cut.sim.graph.size()):
			if n >= 20:
				break
			if cut.sim.is_shoot_tip(id) and cut.sim._subtree_size(id) == 1:
				var size_before := cut.sim.graph.size()
				cut.sim.prune(id)
				forked += cut.sim.graph.size() - size_before
				n += 1
		_day(cut)
	t.check(forked > 0, "the cut tips forked into twin buds (%d new shoots)" % forked)
	t.check(cut.sim.grown_nodes() < calm.sim.grown_nodes(), "20 tips a day: further from the finish (%d vs %d grown)" % [cut.sim.grown_nodes(), calm.sim.grown_nodes()])


## A bush is not a finished tree (sim-0.6.3: a sycamore pruned hard every day "finished" at 1.8 m).
func test_a_short_bush_is_not_finished() -> void:
	var g := _play(4)
	var sim := g.sim
	sim.species.finish_nodes = sim.grown_nodes()
	t.check(sim.height() < sim.finish_height(), "the young tree is short (%.1f of %.1f m)" % [sim.height(), sim.finish_height()])
	t.check(not sim.is_finished(), "enough segments, too short: not finished")
	sim.species.max_height = sim.height() / GrowthSim.FINISH_HEIGHT_SHARE - 0.01
	t.check(sim.is_finished(), "tall enough for its species: finished")


## sim-0.6.3: the tree holds at most two days of water (GrowthSim.hold_days), so roots that stop
## finding water leave it thirsty within a few nights; before, the stock covered 5 to 10 days
## and thirst never showed, even with the roots neglected.
func test_neglected_roots_can_leave_the_tree_thirsty() -> void:
	var g := _play(20)
	var sim := g.sim
	var want := sim.day_capacity() * sim.node_cost() * sim.species.needs[Resources.Kind.WATER]
	t.check(sim.resources.stock[Resources.Kind.WATER] <= 2.0 * want + 1.0, "the stock holds about two days at most (%.0f of a day's %.0f)" % [sim.resources.stock[Resources.Kind.WATER], want])
	# The roots keep finding N, P and K (the tree grows at full pace), but the water deposits
	# they reached run dry: only groundwater seeps in.
	for i in range(g.ground.dot_count()):
		if g.ground.dot_kinds[i] == Resources.Kind.WATER:
			g.ground.dot_amounts[i] = 0.0
			g.ground.dot_collected[i] = 1
	var thirsty_on := -1
	for n in range(6):
		for k in [Resources.Kind.NITROGEN, Resources.Kind.PHOSPHORUS, Resources.Kind.POTASSIUM]:
			sim.resources.stock[k] = 1000.0
		g.dive()
		g.night_done = true
		while g.phase != GameState.Phase.DAY:
			g.tick(0.25)
		if sim.care_need[Resources.Kind.WATER] > Care.SHOW_MIN:
			thirsty_on = n + 1
			break
		_day(g)
	t.check(thirsty_on > 0, "thirst shows within six nights without new water (night %d)" % thirsty_on)
