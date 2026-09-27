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
	var before := u.remaining_dots()
	r.start_run(0)
	_run(r, u, res, Vector2(0.4, 0.0))
	var collected := before - u.remaining_dots()
	t.check(collected > 5, "a run drinks dots (%d)" % collected)
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
