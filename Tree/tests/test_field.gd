extends RefCounted
## 0.8.1 (docs/notes/sim-0.8.1.md): a tree played without roots finishes again (a), the wider root
## field (b, specs/0.8.md item 29) and wishes that often point far (c, item 34).
var t

const FRAME: float = 1.0 / 30.0
const Strategies := preload("res://tools/strategies.gd")


## Rich topsoil patches of the layout (not the starter patch, not the deep veins, not wishes).
func _rich(u: Underground) -> Array:
	var out: Array = []
	for p in u.patches:
		var c: Vector3 = p["center"]
		if bool(p.get("wish", false)) or -c.y > Underground.TOPSOIL or Vector2(c.x, c.z).length() < 2.0:
			continue
		out.append(p)
	return out


func _ring_of(p: Dictionary) -> int:
	var c: Vector3 = p["center"]
	var r := Vector2(c.x, c.z).length()
	return 0 if r < 12.0 else (1 if r < 20.0 else 2)


# --- (a) a tree played without roots -----------------------------------------------

## The boost costs life force only as far as its extra light buys growth, for a tree that starts
## the day hungry: a well-fed tree pays the full price, also once its boost has used a nutrient up
## (broken list item 2); a tree hungry since sunrise with a nutrient missing keeps its life force.
func test_the_boost_costs_a_hungry_tree_less_life_force() -> void:
	var gained := {}
	for case in ["calm", "fed", "used_up", "hungry"]:
		var sim := GrowthSim.new(5)
		sim.species = Species.linden()
		for _i in range(40):
			sim.tick(1.0)
		sim.clock.time_of_day = sim.clock.daylight_fraction * 0.5
		var empty: bool = case == "used_up" or case == "hungry"
		sim.resources.stock = PackedFloat32Array([50, 50, 0, 50]) if empty else PackedFloat32Array([50, 50, 50, 50])
		if case == "hungry":
			sim.care_need = PackedFloat32Array([0, 0, 1, 0])
		sim.growth_paused = true
		sim.clock.boost_active = case != "calm"
		sim.clock.boost_remaining = 1000.0 if case != "calm" else 0.0
		var before := sim.resources.life_force
		sim.tick(2.0)
		gained[case] = sim.resources.life_force - before
	var calm: float = gained["calm"]
	var price := 1.0 - sim_factor()
	t.check(calm > 0.0, "the leaves gather life force (%.3f)" % calm)
	t.check_near(float(gained["fed"]) / calm, sim_factor(), 0.03, "a well-fed tree pays the boost's full price")
	t.check_near(float(gained["used_up"]) / calm, sim_factor(), 0.03, "and so does one whose boost used a nutrient up")
	t.check_near(float(gained["hungry"]) / calm, 1.0 - price * (1.0 - GrowthSim.new(1).boost_cost_liebig), 0.03, "a tree hungry since sunrise with a nutrient missing pays only what the boost buys (%.3f against %.3f calm)" % [gained["hungry"], calm])
	t.check(float(gained["hungry"]) > float(gained["fed"]) * 1.5, "much less than a fed tree")


func sim_factor() -> float:
	return DayCycle.new().boost_life_force_factor


## The 0.8 check's failing runs: boost all day and end every root at once, linden and beech on
## seed 3. They finish by day 45 again, and clearly later than a steered tree (item 10a, "Softer").
func test_a_tree_played_without_roots_finishes() -> void:
	for sp in ["linden", "beech"]:
		var r: Dictionary = Strategies.play("boost_quit", sp, 3, 45)
		var day := int(r["finish"])
		t.check(day > 0 and day <= 42, "%s seed 3, boost all day and end every root at once: finished on day %d" % [sp, day])
		t.check(day >= 33, "%s: clearly later than a steered month (day %d)" % [sp, day])


# --- (b) the wider root field ------------------------------------------------------

## About 14 rich patches far apart in a near, a middle and a far ring of a 30 m field, the near ring
## with every kind of patch; fewer scattered dots, room left in the dot budget for the wishes.
func test_the_field_is_wide_with_patches_far_apart() -> void:
	for seed in [3, 14, 27, 42]:
		var u := Underground.new(seed)
		t.check_eq(u.layout, 3, "a new game gets the wider field")
		t.check_near(u.extent, 30.0, 0.01, "30 m around the trunk")
		var rich := _rich(u)
		t.check(rich.size() >= 12 and rich.size() <= 16, "seed %d: about 14 rich patches (%d)" % [seed, rich.size()])
		var rings := [{}, {}, {}]
		var counts := [0, 0, 0]
		for p in rich:
			var c: Vector3 = p["center"]
			var r := Vector2(c.x, c.z).length()
			t.check(r >= Underground.rings[0][0] - 0.01 and r <= u.extent, "seed %d: a patch %.1f m out, from 5 m on" % [seed, r])
			rings[_ring_of(p)][int(p["kind"])] = true
			counts[_ring_of(p)] += 1
		for k in [Resources.Kind.WATER, Resources.Kind.NITROGEN, Resources.Kind.PHOSPHORUS]:
			t.check(rings[0].has(k), "seed %d: the near ring holds %s" % [seed, Resources.KIND_NAMES[k]])
		t.check(counts[1] >= 3 and counts[2] >= 3, "seed %d: the middle and far rings hold patches too (%d, %d)" % [seed, counts[1], counts[2]])
		var close := 0
		for a in range(rich.size()):
			for b in range(a + 1, rich.size()):
				var ca: Vector3 = rich[a]["center"]
				var cb: Vector3 = rich[b]["center"]
				if Vector2(ca.x - cb.x, ca.z - cb.z).length() < Underground.gap3 * 0.75:
					close += 1
		t.check(close <= 1, "seed %d: patches about 7 m apart (%d pairs closer than %.1f m)" % [seed, close, Underground.gap3 * 0.75])
		var n := u.dot_count()
		t.check(n >= 1800 and n <= 2800, "seed %d: about 1200 scattered dots, %d dots in all" % [seed, n])
		t.check(n + 20 * 70 <= Budgets.NUTRIENT_DOTS_LOADED, "seed %d: room for a month of wish deposits in the dot budget (%d)" % [seed, n])
		# The starter patch is the 0.8 one: the young tree's first nights stay as they were.
		var old := Underground.new(seed, 2)
		for k in range(4):
			t.check_eq(u.patches[k]["center"], old.patches[k]["center"], "the starter patch lies where it did")
			t.check_eq(int(u.patches[k]["end"]) - int(u.patches[k]["first"]), int(old.patches[k]["end"]) - int(old.patches[k]["first"]), "with as many dots")


## An old save keeps its soil, its 14 m field and the old price of distance; a new game prices
## distance at half.
func test_old_saves_keep_their_soil_and_price() -> void:
	for layout in [1, 2]:
		var g := GameState.new_game(14, "linden")
		g.ground = Underground.new(14, layout)
		g.roots.fit_soil(g.ground)
		var d := g.to_dict()
		var back := GameState.from_dict(JSON.parse_string(JSON.stringify(d)))
		t.check_eq(back.ground.layout, layout, "layout %d stays" % layout)
		t.check_eq(back.ground.dot_count(), Underground.new(14, layout).dot_count(), "with its dots")
		t.check_near(back.ground.extent, Underground.EXTENT, 0.01, "and its 14 m field")
		t.check_near(back.roots.distance_cost, RootSystem.DISTANCE_COST_OLD, 1e-6, "and the old price of distance")
	var fresh := GameState.new_game(14, "linden")
	t.check_near(fresh.roots.distance_cost, RootSystem.distance_cost_wide, 1e-6, "a new game: distance at half the price")
	var again := GameState.from_dict(JSON.parse_string(JSON.stringify(fresh.to_dict())))
	t.check_eq(again.ground.layout, 3, "and it keeps the wider field after a load")
	t.check_near(again.roots.distance_cost, RootSystem.distance_cost_wide, 1e-6, "at its price")


## The root reaches the edge of the wider field and stops there, never beyond.
func test_a_root_stays_inside_the_field() -> void:
	for layout in [2, 3]:
		var u := Underground.new(9, layout)
		var roots := RootSystem.new(9)
		roots.fit_soil(u)
		var res := Resources.new()
		res.life_force = 5000.0
		roots.start_run(0)
		roots.heading = Vector3(1, 0, 0)
		var far := 0.0
		for _f in range(4000):
			if not roots.advance(Vector2(0, 0.35), false, FRAME, u, res):
				break
			far = maxf(far, Vector2(roots.tip_position.x, roots.tip_position.z).length())
		t.check(far <= u.extent + 0.01, "layout %d: the root stays within %.0f m (%.2f)" % [layout, u.extent, far])
		t.check(far >= u.extent - 3.0 or layout == 3, "layout %d: and gets out to its edge (%.1f)" % [layout, far])


## A patch beyond the clearing shows its sign at the clearing's edge, in its direction; one under
## the clearing keeps its sign over it.
func test_far_patches_show_their_sign_at_the_clearing_edge() -> void:
	var u := Underground.new(14)
	var edge := 18.0
	var rim := edge - Underground.EDGE_INSET
	var hints := u.surface_hints(edge)
	var signs := {"0": "rushes", "1": "clover", "2": "nettles"}
	var beyond := 0
	for p in _rich(u):
		var c: Vector3 = p["center"]
		var flat := Vector2(c.x, c.z)
		var want := Vector3(c.x, 0, c.z) if flat.length() <= rim else Vector3(flat.normalized().x * rim, 0, flat.normalized().y * rim)
		if flat.length() > rim:
			beyond += 1
		var kind: String = signs[str(int(p["kind"]))]
		var found := hints.any(func(h: Dictionary) -> bool: return str(h["kind"]) == kind and (h["position"] as Vector3).distance_to(want) < 0.01)
		t.check(found, "%s for the patch %.1f m out, at %.1f m" % [kind, flat.length(), Vector2(want.x, want.z).length()])
	t.check(beyond >= 3, "some patches lie beyond the clearing (%d)" % beyond)
	for h in hints:
		t.check(Vector2((h["position"] as Vector3).x, (h["position"] as Vector3).z).length() <= rim + 0.01 or str(h["kind"]) == "stones", "every sign inside the clearing")
	# Without an edge every sign stays over its patch (the 0.8 behaviour).
	var plain := u.surface_hints()
	var outside := plain.filter(func(h: Dictionary) -> bool: return Vector2((h["position"] as Vector3).x, (h["position"] as Vector3).z).length() > rim)
	t.check(outside.size() >= beyond, "without an edge the far signs lie over their patches")


## A month of meadow play in the wider field (item 29, with B2 and B3 of the 0.8 checks): the
## near ring gives every needed kind within one calm night in the first week, the kind the tree
## is shortest of lies within two calm nights every night, a night's root touches a few rich
## patches and never many, the tree is rarely at the soft floor or overstocked, and it finishes
## in its month.
func test_a_month_in_the_wider_field() -> void:
	var g := GameState.new_game(14, "linden")
	var calm := g.roots.calm_life_force
	var rich := {}
	for pi in range(g.ground.patches.size()):
		var p: Dictionary = g.ground.patches[pi]
		var c: Vector3 = p["center"]
		if -c.y <= Underground.TOPSOIL and Vector2(c.x, c.z).length() >= 2.0:
			for i in range(int(p["first"]), int(p["end"])):
				rich[i] = pi
	var most := 0
	var days := 0
	var floor_days := 0
	var over := PackedInt32Array([0, 0, 0, 0])
	var needs := g.sim.species.needs
	for day in range(40):
		var st := g.sim.resources.stock
		var scarce := 0
		for k in range(4):
			if needs[k] > 0.0 and st[k] / needs[k] < st[scarce] / maxf(needs[scarce], 1e-6):
				scarce = k
		g.dive()
		if g.can_start_run():
			if day < 7:
				for k in range(4):
					if needs[k] > 0.0:
						t.check(not Care.reachable(g, k, calm).is_empty(), "night %d: %s within a calm night" % [day + 1, Resources.KIND_NAMES[k]])
			t.check(not Care.reachable(g, scarce, 2.0 * calm).is_empty(), "night %d: the scarcest kind (%s) within two calm nights" % [day + 1, Resources.KIND_NAMES[scarce]])
			var starter := RootBot.new()
			g.start_run(starter.pick_start(g.roots, g.ground, g.sim.resources))
			var bot := RootBot.new()
			while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
				pass
			var hit := {}
			for i in g.roots._run_touched:
				if rich.has(int(i)):
					hit[rich[int(i)]] = true
			most = maxi(most, hit.size())
		while g.phase != GameState.Phase.DAY:
			g.tick(0.25)
		var light := 0.0
		var at_floor := 0.0
		while g.phase == GameState.Phase.DAY:
			var w := maxf(g.sim.clock.sun_height(), 0.0)
			light += w
			if Resources.growth_factor(g.sim.resources.stock, needs, g.sim.growth_floor()) <= g.sim.growth_floor() + 0.05:
				at_floor += w
			g.tick(0.5)
		if g.finished:
			break
		days += 1
		if at_floor >= 0.5 * light:
			floor_days += 1
		var want := g.sim.day_capacity() * g.sim.node_cost()
		for k in range(4):
			if g.sim.resources.stock[k] > 2.0 * want * needs[k]:
				over[k] += 1
	t.check(g.finished and days >= 25 and days <= 34, "finished in about a month (%d days)" % days)
	t.check(most >= 1 and most <= 4, "a night's root touches a few rich patches at most (%d)" % most)
	t.check(floor_days <= days / 4, "B2: at the soft floor on at most a quarter of the days (%d of %d)" % [floor_days, days])
	for k in range(4):
		t.check(over[k] <= days / 3, "B3: %s over two days on at most a third of the days (%d of %d)" % [Resources.KIND_NAMES[k], over[k], days])


# --- (c) far wishes ----------------------------------------------------------------

## About half the underground wishes point at the middle or far ring, a long drive beyond the
## newest tip that continuing the root reaches in two or three nights; the glow stays until then;
## following it gets there.
func test_about_half_the_wishes_point_far_and_are_reached() -> void:
	var wish_days := 0
	var far_days := 0
	var far_wishes := 0
	var reached_in := []
	for seed in [3, 27]:
		var g := GameState.new_game(seed, "linden")
		var placed := {}
		for day in range(24):
			g.dive()
			var wp := g.diary.wish_patch
			if wp >= 0:
				wish_days += 1
				if Diary.is_far(g.ground, wp):
					far_days += 1
					var p: Dictionary = g.ground.patches[wp]
					var c: Vector3 = p["center"]
					if not placed.has(wp):
						placed[wp] = g.day_number()
						far_wishes += 1
						t.check(Vector2(c.x, c.z).length() >= Underground.FAR_RING, "seed %d: a far wish lies 12 m or more out (%.1f)" % [seed, Vector2(c.x, c.z).length()])
						t.check(Vector2(c.x, c.z).length() <= g.ground.extent, "inside the field")
						t.check(g.day_number() - int(p["day"]) < Diary.FAR_DAYS, "it glows on the days it was placed for")
			if g.can_start_run():
				var from := Diary.newest_tip(g.roots)
				var bot := RootBot.new()
				var glows := g.wish_glows()
				if not glows.is_empty():
					bot.goal = glows[0]["center"]
					bot.goal_radius = float(glows[0]["radius"]) * 0.5
				g.start_run(from)
				while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
					pass
				if wp >= 0 and placed.has(wp) and g.diary.wish_reached:
					reached_in.append(g.day_number() - int(placed[wp]) + 1)
			while g.phase != GameState.Phase.DAY:
				g.tick(0.25)
			while g.phase == GameState.Phase.DAY:
				g.tick(1.0)
	var share := float(far_days) / maxf(wish_days, 1)
	t.check(share >= 0.3 and share <= 0.7, "about half the underground-wish days point far (%d of %d)" % [far_days, wish_days])
	t.check(far_wishes >= 4, "several far wishes (%d)" % far_wishes)
	t.check(reached_in.size() >= far_wishes * 0.75, "following the glow reaches most far wishes (%d of %d)" % [reached_in.size(), far_wishes])
	for n in reached_in:
		t.check(n <= 3, "within three nights (%d)" % n)


## A far wish lies within FAR_REACH_NIGHTS calm reaches of the newest tip and at least
## FAR_AHEAD_MIN beyond it; it stays for FAR_DAYS mornings while no root reaches it, then a new
## wish comes; it survives a save.
func test_a_far_wish_waits_two_or_three_mornings() -> void:
	var u := Underground.new(14)
	var roots := RootSystem.new(14)
	roots.fit_soil(u)
	var keep := Diary.far_share
	Diary.far_share = 1.0
	var diary := Diary.new()
	var first := -1
	# 0.8.2.1: far wishes start on day 5 (Diary.FAR_FROM_DAY).
	var day := Diary.FAR_FROM_DAY
	while day < 30 and first < 0:
		diary.new_wish(u, day, 14, roots)
		if Diary.is_far(u, diary.wish_patch):
			first = diary.wish_patch
		day += 1
	Diary.far_share = keep
	t.check(first >= 0, "a far wish was placed")
	if first < 0:
		return
	var c: Vector3 = u.patches[first]["center"]
	var tip := roots.graph.positions[Diary.newest_tip(roots)]
	var ahead := Vector2(c.x - tip.x, c.z - tip.z).length()
	t.check(ahead >= Diary.FAR_AHEAD_MIN - 0.01 and ahead <= Diary.FAR_AHEAD_MAX + 0.01, "a long drive beyond the newest tip (%.1f m)" % ahead)
	var goal := c - (c - tip).normalized() * float(u.patches[first]["radius"]) * 0.6
	t.check(Diary.line_cost(u, roots, tip, goal) <= Diary.calm_reach(roots) * Diary.FAR_REACH_NIGHTS, "two or three calm nights reach it")
	var text := diary.wish
	var placed := int(u.patches[first]["day"])
	for d in range(placed + 1, placed + Diary.FAR_DAYS):
		diary.new_wish(u, d, 14, roots)
		t.check_eq(diary.wish_patch, first, "day %d: the far wish still glows" % d)
		t.check_eq(diary.wish, text, "with the same words")
	var back := Underground.from_dict(JSON.parse_string(JSON.stringify(u.to_dict())))
	t.check(Diary.is_far(back, first), "a far wish stays far after a load")
	diary.new_wish(u, placed + Diary.FAR_DAYS, 14, roots)
	t.check(diary.wish_patch != first, "after %d mornings a new wish comes" % Diary.FAR_DAYS)
