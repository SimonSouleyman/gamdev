extends RefCounted
var t


func _setup(life: float = 20.0, seed: int = 7) -> Array:
	var u := Underground.new(seed)
	var r := RootSystem.new(seed)
	var res := Resources.new()
	res.life_force = life
	return [u, r, res]


func _run(r: RootSystem, u: Underground, res: Resources, stick: Vector2 = Vector2.ZERO, dive: bool = false, max_frames: int = 20000) -> int:
	var frames := 0
	while r.advance(stick, dive, 1.0 / 30.0, u, res) and frames < max_frames:
		frames += 1
	return frames


func test_life_force_drains_per_metre_and_run_stops_when_empty() -> void:
	var s := _setup(10.0)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	t.check(r.start_run(0), "run starts at the seed")
	r.advance(Vector2.ZERO, false, 0.5, u, res)
	t.check(res.life_force < 10.0, "life force drains while growing")
	_run(r, u, res)
	t.check(not r.run_active, "run ended")
	t.check_near(res.life_force, 0.0, 1e-5, "ended because life force is used up")
	t.check(r.run_length > 5.0 and r.run_length < 10.0, "about one metre per life force near the trunk (%f m)" % r.run_length)


func test_cost_rises_with_distance_and_depth() -> void:
	var r := RootSystem.new(1)
	var near := r.cost_per_metre(Vector3(0, -0.5, 0))
	t.check(r.cost_per_metre(Vector3(8, -0.5, 0)) > near, "further from the trunk costs more")
	t.check(r.cost_per_metre(Vector3(0, -6, 0)) > near, "deeper costs more")


func test_path_becomes_permanent_nodes() -> void:
	var s := _setup(15.0)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	_run(r, u, res, Vector2(0.3, 0.0))
	var main_nodes := r.count_flagged("main", 0)
	t.check(main_nodes >= 20, "steered path kept as nodes (%d)" % main_nodes)
	t.check_eq(r.main_root_count, 1, "one main root")
	var d := RootSystem.from_dict(JSON.parse_string(JSON.stringify(r.to_dict())))
	t.check_eq(d.graph.size(), r.graph.size(), "roots survive a save round trip")



func test_fine_roots_sprout_within_radius_and_budget() -> void:
	var s := _setup(40.0, 11)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	_run(r, u, res, Vector2(0.2, 0.1))
	var fine := r.count_flagged("fine", 0)
	t.check(fine > 0, "fine roots sprouted (%d)" % fine)
	t.check(fine <= Budgets.FINE_ROOTS_PER_MAIN_ROOT, "fine root budget respected (%d)" % fine)
	var far := 0
	for id in range(r.graph.size()):
		if r.graph.get_flag(id, "fine", -1) == 0:
			var nearest := INF
			for m in range(r.graph.size()):
				if r.graph.get_flag(m, "main", -1) == 0 or m == 0:
					nearest = minf(nearest, r.graph.positions[id].distance_to(r.graph.positions[m]))
			if nearest > r.fine_radius + 0.3:
				far += 1
	t.check_eq(far, 0, "fine roots stay near their main root")


func test_dots_collected_into_matching_resource() -> void:
	var s := _setup(20.0, 21)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	var before := 0.0
	for a in u.dot_amounts:
		before += a
	r.start_run(0)
	_run(r, u, res, Vector2(0.4, 0.0))
	var after := 0.0
	for a in u.dot_amounts:
		after += a
	t.check(before - after > 3.0, "a run drinks from deposits (%f)" % (before - after))
	t.check(r.tapped.size() > 5, "and taps them for later nights (%d)" % r.tapped.size())
	var total := 0.0
	for k in range(4):
		t.check_near(res.amount(k), r.run_totals[k], 1e-4, "run totals match stock for kind %d" % k)
		total += res.amount(k)
	t.check(total > 3.0, "resources gained (%f)" % total)


func test_rocks_are_walls() -> void:
	var s := _setup(200.0, 5)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	var inside := 0
	var frames := 0
	# Dive straight down for a long time: deep rocks are common, the tip must never enter one.
	while r.advance(Vector2(0.15, -0.3), true, 1.0 / 30.0, u, res) and frames < 3000:
		frames += 1
		if u.is_inside_rock(r.tip_position):
			inside += 1
	t.check_eq(inside, 0, "tip never inside a rock")
	for id in range(r.graph.size()):
		if u.is_inside_rock(r.graph.positions[id]):
			inside += 1
	t.check_eq(inside, 0, "no root node inside a rock")


func test_one_run_at_a_time_and_start_anywhere() -> void:
	var s := _setup(8.0)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	t.check(not r.start_run(0), "no second run while one is active")
	_run(r, u, res)
	var mid := r.run_first_new_id + 5
	res.life_force = 5.0
	t.check(r.start_run(mid), "a new run can start from the middle of a root")
	var frames := _run(r, u, res)
	t.check(frames > 0, "and grows")
	t.check_eq(r.graph.parents[r.run_first_new_id], mid, "branching from the picked point")


func test_run_node_budget() -> void:
	var s := _setup(100000.0)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	_run(r, u, res, Vector2(1.0, 0.2))
	t.check(r.count_flagged("main", 0) <= Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT, "main root node budget")
	t.check(not r.run_active, "run ends at the budget")


func test_deterministic() -> void:
	var a := _setup(15.0, 3)
	var b := _setup(15.0, 3)
	(a[1] as RootSystem).start_run(0)
	(b[1] as RootSystem).start_run(0)
	_run(a[1], a[0], a[2], Vector2(0.5, 0))
	_run(b[1], b[0], b[2], Vector2(0.5, 0))
	t.check_eq((a[1] as RootSystem).graph.positions, (b[1] as RootSystem).graph.positions, "same seed and input, same roots")


func test_pushing_against_a_wall_costs_nothing() -> void:
	# QA found the tip stuck in the corner of floor and world edge, draining life force.
	var s := _setup(50.0, 42)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	r.tip_position = Vector3(10.3, -9.95, 9.5)
	r.heading = Vector3(0.6, -0.9, 0.5).normalized()
	var before := res.life_force
	var start := r.tip_position
	for _i in range(300):
		if not r.advance(Vector2.ZERO, true, 1.0 / 30.0, u, res):
			break
	var spent := before - res.life_force
	var moved := r.run_length
	t.check(moved > 1.0, "the root got out of the corner (%f m)" % moved)
	t.check(spent <= moved * r.cost_per_metre(Vector3(14, -10, 0)) + 0.01, "paid only for metres grown (%f for %f m)" % [spent, moved])


func test_overlapping_rocks_are_still_walls() -> void:
	var u := Underground.new(42)
	# Two overlapping rocks right in the root's way.
	u.rock_centers.append(Vector3(0, -3, -2))
	u.rock_radii.append(1.0)
	u.rock_centers.append(Vector3(0.8, -3, -2.6))
	u.rock_radii.append(1.0)
	var r := RootSystem.new(1)
	var res := Resources.new()
	res.life_force = 60.0
	r.start_run(0)
	var inside := 0
	var guard := 0
	while r.advance(Vector2(0.05, -0.2), true, 1.0 / 20.0, u, res) and guard < 3000:
		guard += 1
		if u.is_inside_rock(r.tip_position):
			inside += 1
	t.check_eq(inside, 0, "never inside overlapping rocks")


func test_diving_does_not_lock_the_steering() -> void:
	var s := _setup(100.0)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	for _i in range(150):
		r.advance(Vector2.ZERO, true, 1.0 / 30.0, u, res)
	var before := Vector2(r.heading.x, r.heading.z).normalized()
	for _i in range(30):
		r.advance(Vector2(1, 0), true, 1.0 / 30.0, u, res)
	var after := Vector2(r.heading.x, r.heading.z).normalized()
	t.check(absf(before.angle_to(after)) > 0.5, "turning still works while diving (%f rad)" % absf(before.angle_to(after)))


func test_run_is_about_the_same_at_any_frame_rate() -> void:
	var lengths: Array = []
	for dt in [1.0 / 120.0, 1.0 / 30.0, 0.25]:
		var s := _setup(15.0, 3)
		var r: RootSystem = s[1]
		r.start_run(0)
		var guard := 0
		while r.advance(Vector2(0.4, 0.0), false, dt, s[0], s[2]) and guard < 50000:
			guard += 1
		lengths.append(r.run_length)
	t.check(absf(lengths[0] - lengths[2]) < 1.0, "run length does not depend on the frame rate (%s)" % str(lengths))


func test_a_wedged_root_ends_instead_of_hanging() -> void:
	# QA: a rock through both the floor and the world edge could box the tip in for good.
	var u := Underground.new(5)
	u.rock_centers.append(Vector3(0.0, -9.6, -13.4))
	u.rock_radii.append(1.2)
	var r := RootSystem.new(5)
	var res := Resources.new()
	res.life_force = 500.0
	r.start_run(0)
	r.tip_position = Vector3(1.0, -9.95, -13.9)
	r.heading = Vector3(-0.5, -0.9, -0.3).normalized()
	var frames := 0
	while r.advance(Vector2(-1.0, -1.0), true, 1.0 / 30.0, u, res) and frames < 30 * 120:
		frames += 1
	t.check(not r.run_active, "the run ends or escapes within two minutes (%d frames)" % frames)


func test_ending_early_spends_everything_on_more_fine_roots() -> void:
	# Simon, play test: a run can end early; the leftover life force is still spent, on fine roots.
	var a := _setup(60.0, 11)
	var b := _setup(60.0, 11)
	var ra: RootSystem = a[1]
	var rb: RootSystem = b[1]
	ra.start_run(0)
	rb.start_run(0)
	for _i in range(90):
		ra.advance(Vector2(0.3, 0), false, 1.0 / 30.0, a[0], a[2])
		rb.advance(Vector2(0.3, 0), false, 1.0 / 30.0, b[0], b[2])
	# a ends now with lots of life force left; b grows the same path to the end.
	ra.finish_early(a[0], a[2])
	t.check_near((a[2] as Resources).life_force, 0.0, 1e-6, "ending early still spends all life force")
	t.check(not ra.run_active, "the run is over")
	var fine_a := ra.count_flagged("fine", 0)
	t.check(fine_a > 0, "fine roots grew (%d)" % fine_a)
	t.check(fine_a <= Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT, "within the fine-root budget")
	var early := RootSystem.new(11)
	var ground := Underground.new(11)
	var res := Resources.new()
	res.life_force = 3.0
	early.start_run(0)
	for _i in range(90):
		early.advance(Vector2(0.3, 0), false, 1.0 / 30.0, ground, res)
	early.finish_early(ground, res)
	t.check(fine_a > early.count_flagged("fine", 0), "more leftover life force, more fine roots (%d vs %d)" % [fine_a, early.count_flagged("fine", 0)])


func test_tapped_deposits_are_drunk_night_after_night() -> void:
	# Simon, play test 3: a deposit holds a set amount; roots that reached it keep drawing on it.
	var s := _setup(20.0, 21)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	_run(r, u, res, Vector2(0.4, 0.0))
	var nights: Array = []
	for _n in range(6):
		var got := r.drink_tapped(u, res)
		nights.append(got[0] + got[1] + got[2] + got[3])
	t.check(float(nights[0]) > 0.5, "the first night after the run draws more (%s)" % str(nights))
	t.check(float(nights[2]) > 0.1, "and later nights still draw from the same roots (%s)" % str(nights))
	var d := RootSystem.from_dict(JSON.parse_string(JSON.stringify(r.to_dict())))
	t.check_eq(d.tapped.size(), r.tapped.size(), "tapped deposits survive a save")


func test_a_tip_draws_each_deposit_once_per_run() -> void:
	var s := _setup(40.0, 9)
	var u: Underground = s[0]
	var r: RootSystem = s[1]
	var res: Resources = s[2]
	r.start_run(0)
	# Circle tightly so the tip passes the same dots again and again.
	var frames := 0
	while r.advance(Vector2(1.0, 0.0), false, 1.0 / 30.0, u, res) and frames < 600:
		frames += 1
	var over := 0
	for i in r.tapped.keys():
		if u.dot_amounts[i] < u.dot_capacity[i] * (1.0 - 2.0 / Underground.DEPOSIT_SHARES) - 1e-4:
			over += 1
	t.check_eq(over, 0, "no deposit drained by lingering in one run")
