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


## An empty underground with only the given deposits (no rocks), for steering tests.
func _clean_ground(dots: Array) -> Underground:
	var u := Underground.new(7)
	u.rock_centers = PackedVector3Array()
	u.rock_radii = PackedFloat32Array()
	u.dot_positions = PackedVector3Array(dots)
	u.dot_kinds = PackedInt32Array()
	u.dot_amounts = PackedFloat32Array()
	u.dot_capacity = PackedFloat32Array()
	u.dot_collected = PackedByteArray()
	for _p in dots:
		u.dot_kinds.append(Resources.Kind.WATER)
		u.dot_amounts.append(4.0)
		u.dot_capacity.append(4.0)
		u.dot_collected.append(0)
	u._build_grid()
	return u


## A run from a set point and heading on `u`; returns the root after `seconds` (or when it ended).
func _steer_from(u: Underground, at: Vector3, heading: Vector3, stick: Vector2, seconds: float, magnet: bool = true) -> RootSystem:
	var r := RootSystem.new(3)
	if not magnet:
		r.magnet_rate = 0.0
	var res := Resources.new()
	res.life_force = 30.0
	r.start_run(0)
	r.pace_run(30.0)
	r.tip_position = at
	r.heading = heading.normalized()
	r._update_right()
	for _i in range(int(seconds * 30.0)):
		if not r.advance(stick, false, 1.0 / 30.0, u, res):
			break
	return r


func test_the_tip_draws_more_from_a_deposit_than_a_fine_root() -> void:
	# QA r1: ending early and letting fine roots gather beat steering to the deposits.
	var r := RootSystem.new(1)
	t.check(r.tip_share >= 2.0 * r.fine_share, "the tip draws at least twice a fine root's share (%.2f vs %.2f)" % [r.tip_share, r.fine_share])
	var u := _clean_ground([Vector3(0, -1.0, -1.0), Vector3(0, -1.0, 1.0)])
	var res := Resources.new()
	r._collect_ids(PackedInt32Array([0]), u, res)
	var tip := res.amount(Resources.Kind.WATER)
	r._collect_ids(PackedInt32Array([1]), u, res, r.fine_share)
	var fine := res.amount(Resources.Kind.WATER) - tip
	t.check_near(tip, 4.0 * r.tip_share, 1e-5, "the tip's first contact")
	t.check(tip > fine * 1.9, "touching a deposit pays about twice what a fine root gets (%.2f vs %.2f)" % [tip, fine])


func test_leftover_fine_roots_reach_only_so_far() -> void:
	var s := _setup(1000.0, 11)
	var r: RootSystem = s[1]
	r.start_run(0)
	for _i in range(45):
		r.advance(Vector2(0.3, 0), false, 1.0 / 30.0, s[0], s[2])
	r.finish_early(s[0], s[2])
	t.check(r._fine_reach <= r.fine_radius + r.fine_reach_max_extra + 1e-4, "the leftover widens the fine roots' reach only up to a cap (%.1f m)" % r._fine_reach)
	t.check(r.count_flagged("fine", 0) <= Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT, "within the fine-root budget")


func test_a_night_stays_calm_as_life_force_grows() -> void:
	# QA r1: nights grew from 18 s to 80-110 s as the crown gathered more life force.
	var lengths: Array = []
	for life in [30.0, 120.0, 300.0]:
		var s := _setup(life, 5)
		var r: RootSystem = s[1]
		r.start_run(0)
		var seconds := _run(r, s[0], s[2], Vector2(0.25, -0.1)) / 30.0
		t.check(seconds <= 60.0, "a root on %.0f life force takes at most a minute (%.0f s)" % [life, seconds])
		t.check(seconds >= 15.0, "and is no rush either (%.0f s)" % seconds)
		lengths.append(r.run_length)
	t.check(lengths[1] > lengths[0] and lengths[2] > lengths[1], "more life force still grows a longer root (%s)" % str(lengths))
	var r2 := RootSystem.new(1)
	r2.pace_run(r2.calm_life_force * 0.5)
	t.check_near(r2.run_cost_scale, 1.0, 1e-6, "a small tank pays the plain price per metre")
	r2.pace_run(r2.calm_life_force * 4.0)
	t.check(r2.run_cost_scale > 1.5, "a big tank pays more per metre (%.2f)" % r2.run_cost_scale)
	t.check(r2.run_speed_scale > 1.0, "and its root grows faster (%.2f)" % r2.run_speed_scale)


func test_magnetism_pulls_the_tip_onto_a_nearby_deposit() -> void:
	# QA r1: steering onto a dot was hard, the root circled it. A deposit just off the heading
	# is reached with the stick at rest; without the pull the root would pass it by.
	var dot := Vector3(0, -2.0, -1.5)
	var heading := Vector3(sin(deg_to_rad(40.0)), 0, -cos(deg_to_rad(40.0)))
	var at := Vector3(0, -2.0, 0)
	var u := _clean_ground([dot])
	_steer_from(u, at, heading, Vector2.ZERO, 3.0)
	t.check(u.dot_amounts[0] < u.dot_capacity[0], "the pull brings the tip onto the deposit")
	var v := _clean_ground([dot])
	_steer_from(v, at, heading, Vector2.ZERO, 3.0, false)
	t.check_near(v.dot_amounts[0], v.dot_capacity[0], 1e-5, "without it the root passes by")


func test_a_hard_turn_slows_the_tip_for_a_tighter_curve() -> void:
	var u := _clean_ground([])
	var at := Vector3(0, -3.0, -4.0)
	var straight := _steer_from(u, at, Vector3.FORWARD, Vector2.ZERO, 2.0)
	var hard := _steer_from(u, at, Vector3.FORWARD, Vector2(1, 0), 2.0)
	t.check(hard.run_length < straight.run_length * 0.75, "full stick grows slower (%.2f vs %.2f m)" % [hard.run_length, straight.run_length])
	# A full circle at full stick stays within about a metre across.
	var circle := _steer_from(u, at, Vector3.FORWARD, Vector2(1, 0), TAU / hard.turn_rate)
	var across := 0.0
	for id in range(circle.run_first_new_id, circle.graph.size()):
		across = maxf(across, circle.graph.positions[id].distance_to(at))
	t.check(across < 1.0, "the tightest circle is under a metre across (%.2f m)" % across)


func test_the_pace_survives_a_save() -> void:
	var s := _setup(200.0, 5)
	var r: RootSystem = s[1]
	r.start_run(0)
	r.advance(Vector2.ZERO, false, 1.0 / 30.0, s[0], s[2])
	var d := RootSystem.from_dict(JSON.parse_string(JSON.stringify(r.to_dict())))
	t.check_near(d.run_cost_scale, r.run_cost_scale, 1e-5, "the night's price per metre is kept")
	t.check_near(d.run_speed_scale, r.run_speed_scale, 1e-5, "and its speed")


func test_the_bot_aims_at_fresh_deposits_ahead() -> void:
	var at := Vector3(0, -2.0, 0)
	# Ahead and close but already tapped; ahead and further, fresh; close but behind.
	var u := _clean_ground([Vector3(0, -2.0, -1.5), Vector3(0.8, -2.0, -3.0), Vector3(0, -2.0, 1.0)])
	var r := RootSystem.new(3)
	r.start_run(0)
	r.tip_position = at
	r.heading = Vector3.FORWARD
	r._update_right()
	r.tapped[0] = true
	var bot := RootBot.new()
	var stick := bot.stick_for(r, u)
	t.check_eq(bot.target_id, 1, "the bot skips the tapped deposit and the one behind")
	t.check(stick.x > 0.0, "and turns toward the fresh one (right)")
