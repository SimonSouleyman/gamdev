extends RefCounted
## 0.8.2 roots (docs/notes/roots-0.8.2.md): the dearer metre in the wider field, far wishes
## balanced per seed, side roots in a second and third level from the leftover, and a thicker
## root for a full run (specs/side-roots.md).
var t

const FRAME := 1.0 / 30.0
## The month player of tools/strategies.gd (a SceneTree script, so no class_name).
const Strategies = preload("res://tools/strategies.gd")


## A run from the trunk on `life` life force in the wide field (layout 3, fit_soil), steered
## gently; ended after `metres` (or when the tank is dry, with `metres` < 0).
func _night(seed: int, life: float, metres: float, stick: Vector2 = Vector2(0.25, 0.0)) -> Array:
	var u := Underground.new(seed, 3)
	var r := RootSystem.new(seed)
	r.fit_soil(u)
	var res := Resources.new()
	res.life_force = life
	r.start_run(0)
	var frames := 0
	while r.advance(stick, false, FRAME, u, res) and frames < 60000:
		frames += 1
		if metres >= 0.0 and r.run_length >= metres:
			r.finish_early(u, res)
			break
	return [u, r, res]


func _count_level(r: RootSystem, level: int) -> int:
	var n := 0
	for id in range(r.graph.size()):
		if r.graph.get_flag(id, "main", -1) < 0 and r.root_level(id) == level:
			n += 1
	return n


# --- 1. the dearer metre ----------------------------------------------------------

## The wider field's metre costs base_cost_wide (old soils keep 1.0), and a calm night's root
## (meadow start, chasing deposits, no boost) is about 12 to 18 m long, in the same 20 to 44 s.
func test_a_calm_night_buys_12_to_18_metres() -> void:
	var r := RootSystem.new(1)
	r.fit_soil(Underground.new(1, 2))
	t.check_near(r.base_cost_per_metre, 1.0, 1e-6, "an old soil keeps the old price")
	r.fit_soil(Underground.new(1, 3))
	t.check(r.base_cost_per_metre >= 2.0 and r.base_cost_per_metre <= 3.0, "the wider field's metre is dearer (%.2f)" % r.base_cost_per_metre)
	var play: Dictionary = Strategies.play("dots", "linden", 14, 20)
	var lens: Array = (play["lens"] as Array).slice(3)
	var secs: Array = (play["secs"] as Array).slice(3)
	lens.sort()
	var median := int(lens[lens.size() / 2])
	t.check(median >= 12 and median <= 18, "a calm night's root: median %d m (%s)" % [median, str(play["lens"])])
	for sec in secs:
		t.check(int(sec) >= 20 and int(sec) <= 44, "and it still takes 20 to 44 s (%d s)" % int(sec))


# --- 2. far wishes balanced per seed -------------------------------------------------

## Item 34 per seed: the far draw keeps a running share, so every seed gets about half of its
## underground-wish mornings far (0.8.1: seed 3 got 25 to 35 %).
func test_far_wishes_are_balanced_on_every_seed() -> void:
	for seed in [3, 14, 27]:
		var u := Underground.new(seed, 3)
		var roots := RootSystem.new(seed)
		roots.fit_soil(u)
		var diary := Diary.new()
		var wish_days := 0
		var far_days := 0
		for day in range(1, 41):
			var placed := u.wish_deposits.size()
			diary.new_wish(u, day, seed, roots)
			# 0.8.2.5: a morning whose wish points at a deposit already in the soil because no new
			# one could be placed (here: the soil's dot budget full from about day 28, nothing is
			# collected) does not count in the share of new wishes.
			var fallback := u.wish_deposits.size() == placed and not Diary.is_far(u, diary.wish_patch)
			# 0.8.2.1: far wishes, and the running share, start on day 5 (Diary.FAR_FROM_DAY).
			if diary.wish_patch >= 0 and day >= Diary.FAR_FROM_DAY and not fallback:
				wish_days += 1
				if Diary.is_far(u, diary.wish_patch):
					far_days += 1
			# A player who reaches every wish the night it shows (the draw itself is measured).
			if diary.wish_patch >= 0:
				u.mark_wish_reached(diary.wish_patch)
				diary.wish_reached = true
		var share := float(far_days) / maxf(1.0, wish_days)
		t.check(share >= 0.4 and share <= 0.6, "seed %d: %d of %d underground-wish mornings far (%.0f %%)" % [seed, far_days, wish_days, share * 100.0])
		t.check_eq(diary.wish_days, wish_days, "seed %d: the diary counts the mornings" % seed)
		var back := Diary.from_dict(JSON.parse_string(JSON.stringify(diary.to_dict())))
		t.check(back.wish_days == diary.wish_days and back.far_days == diary.far_days, "the running share survives a save")


# --- 4. side roots and thicker roots ---------------------------------------------------

## Ending early: the leftover grows a second and a third level of side roots, flagged by level,
## within the node cap; the first level stays at its fixed budget.
func test_the_leftover_grows_two_levels_of_side_roots() -> void:
	var s := _night(14, 120.0, 1.5)
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	t.check_near(res.life_force, 0.0, 1e-6, "the leftover is spent")
	t.check(r.leftover_spent > 100.0, "most of the tank was left (%.0f)" % r.leftover_spent)
	var l1 := _count_level(r, 1)
	var l2 := _count_level(r, 2)
	var l3 := _count_level(r, 3)
	t.check(l2 > 0, "a second level grew (%d)" % l2)
	t.check(l3 > 0, "and a third (%d)" % l3)
	t.check(l1 <= Budgets.FINE_ROOTS_PER_MAIN_ROOT, "the first level keeps its fixed budget (%d)" % l1)
	t.check(l2 + l3 <= r.side_nodes_max and r.side_nodes_max <= Budgets.SIDE_ROOTS_PER_MAIN_ROOT, "second and third level within the night's cap (%d)" % (l2 + l3))
	t.check(l1 + l2 + l3 <= Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT, "all within a main root's fine budget")
	t.check(l3 <= l2, "the third level is the smaller one (%d vs %d)" % [l3, l2])
	t.check_eq(Vector2i(r.side_nodes_grown[0], r.side_nodes_grown[1]), Vector2i(l2, l3), "the run reports what grew")


## Broken 3: no side root grows further than its reach from where it started (2.5 m for the
## second level, 0.8 m for the third), however much life force is left.
func test_side_roots_stay_short() -> void:
	for seed in [3, 14, 27]:
		var s := _night(seed, 600.0, 1.0)
		var r: RootSystem = s[1]
		var g := r.graph
		var worst2 := 0.0
		var worst3 := 0.0
		for id in range(g.size()):
			var level := r.root_level(id)
			if level < 2:
				continue
			# The start: the first ancestor of a lower level.
			var cur := g.parents[id]
			while r.root_level(cur) >= level and g.get_flag(cur, "main", -1) < 0:
				cur = g.parents[cur]
			var d := g.positions[id].distance_to(g.positions[cur])
			if level == 2:
				worst2 = maxf(worst2, d)
			else:
				worst3 = maxf(worst3, d)
		t.check(worst2 <= r.side_reach_max + 1e-3, "seed %d: second level at most %.1f m from its start (%.2f)" % [seed, r.side_reach_max, worst2])
		t.check(worst3 <= r.side3_reach_max + 1e-3, "seed %d: third level at most %.1f m (%.2f)" % [seed, r.side3_reach_max, worst3])


## The sparse wide field: where no dot lies in reach, the second level still sprouts short tips,
## so a root ended at once still shows a fan.
func test_a_fan_shows_where_no_dot_is_in_reach() -> void:
	var u := Underground.new(7, 3)
	u.dot_positions = PackedVector3Array()
	u.dot_kinds = PackedInt32Array()
	u.dot_amounts = PackedFloat32Array()
	u.dot_capacity = PackedFloat32Array()
	u.dot_collected = PackedByteArray()
	u.rock_centers = PackedVector3Array()
	u.rock_radii = PackedFloat32Array()
	u._build_grid()
	var r := RootSystem.new(7)
	r.fit_soil(u)
	var res := Resources.new()
	res.life_force = 60.0
	r.start_run(0)
	while r.advance(Vector2(0.2, 0.0), false, FRAME, u, res):
		if r.run_length >= 1.0:
			r.finish_early(u, res)
			break
	t.check_eq(_count_level(r, 1), 0, "no dot: no first-level fine roots")
	t.check(_count_level(r, 2) >= 2 * RootSystem.SIDE_MIN_STARTS, "short second-level tips still sprout (%d)" % _count_level(r, 2))


## Running the tank dry: no second level, and the root is drawn about 1.5x as thick; ending at
## once: about 1.0x. Set once at the run's end and kept through a save; old saves read 1.0.
func test_a_full_run_makes_a_thicker_root() -> void:
	var dry := _night(14, 80.0, -1.0)
	var rd: RootSystem = dry[1]
	t.check(rd.thickness_of(0) >= 1.45, "tank run dry: a thick root (%.2f)" % rd.thickness_of(0))
	t.check_eq(_count_level(rd, 2) + _count_level(rd, 3), 0, "and no side roots")
	var early := _night(14, 80.0, 1.0)
	var re: RootSystem = early[1]
	t.check(re.thickness_of(0) <= 1.05, "ended at once: about as thin as before (%.2f)" % re.thickness_of(0))
	t.check(rd.thickness_of(0) > re.thickness_of(0), "thicker for the full run")
	# The near-empty threshold: less than a tenth of the tank left, no second level.
	var u0 := Underground.new(14, 3)
	var r0 := RootSystem.new(14)
	r0.fit_soil(u0)
	var res0 := Resources.new()
	res0.life_force = 80.0
	r0.start_run(0)
	while r0.advance(Vector2(0.25, 0.0), false, FRAME, u0, res0):
		if res0.life_force < 0.08 * 80.0:
			r0.finish_early(u0, res0)
			break
	t.check(r0.leftover_spent > 0.0, "a little was left (%.1f)" % r0.leftover_spent)
	t.check_eq(_count_level(r0, 2) + _count_level(r0, 3), 0, "near-empty tank: no side roots")
	t.check(r0.thickness_of(0) > 1.45, "but nearly the full thickness (%.2f)" % r0.thickness_of(0))
	# Set once: another night does not touch the first root's thickness.
	var first := rd.thickness_of(0)
	var u: Underground = dry[0]
	var res := Resources.new()
	res.life_force = 40.0
	rd.start_run(rd.graph.size() - 1)
	while rd.advance(Vector2(0.2, 0.0), false, FRAME, u, res):
		if rd.run_length >= 1.0:
			rd.finish_early(u, res)
			break
	t.check_near(rd.thickness_of(0), first, 1e-6, "the first root's thickness stays after the next night")
	t.check(rd.thickness_of(1) < first, "the second, ended early, is thinner (%.2f)" % rd.thickness_of(1))
	var back := RootSystem.from_dict(JSON.parse_string(JSON.stringify(rd.to_dict())))
	t.check_near(back.thickness_of(0), first, 1e-4, "kept through a save")
	var d := rd.to_dict()
	d.erase("thickness")
	var old := RootSystem.from_dict(JSON.parse_string(JSON.stringify(d)))
	t.check_near(old.thickness_of(0), 1.0, 1e-6, "an old save's roots are 1.0x")


## A thicker root takes more groundwater per metre, up to 1 + seep_thick_gain.
func test_a_thick_root_seeps_more() -> void:
	var s := _night(14, 80.0, -1.0)
	var r: RootSystem = s[1]
	var with_thick := r.seep_length()
	r.thickness[0] = 1.0
	var plain := r.seep_length()
	t.check(with_thick > plain, "the thick root seeps more (%.1f vs %.1f m)" % [with_thick, plain])
	t.check(with_thick <= plain * (1.0 + r.seep_thick_gain) + 1e-3, "at most %.2fx" % (1.0 + r.seep_thick_gain))


## The look: per node, the root view draws main roots by their thickness and the side-root
## levels finer, the third dimmer; one merged mesh for the old roots (no node per root).
func test_the_levels_read_apart_in_one_mesh() -> void:
	var s := _night(14, 120.0, 1.5)
	var r: RootSystem = s[1]
	var view := RootView.new()
	view.roots = r
	view._update_looks()
	var b := view._builder
	t.check_eq(b.radius_mul.size(), r.graph.size(), "a radius per node")
	var seen := {}
	for id in range(1, r.graph.size()):
		var level := r.root_level(id)
		seen[level] = true
		if level == 2:
			t.check_near(b.radius_mul[id], RootView.SIDE2_RADIUS, 1e-6, "second level finer")
		elif level == 3:
			t.check_near(b.radius_mul[id], RootView.SIDE3_RADIUS, 1e-6, "third level half as thick as the second")
			t.check(b.node_colors[id].get_luminance() < 0.8, "and dimmer")
	t.check(seen.has(2) and seen.has(3), "both side levels drawn")
	var mesh := b.build(r.graph, 1)
	t.check_eq(mesh.get_surface_count(), 1, "the roots stay one surface")
	view.free()
