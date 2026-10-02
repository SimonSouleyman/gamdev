extends RefCounted
## 0.8.2.2: Simon's phone notes on 0.8.2.1 (specs/0.8.md "0.8.2.2", broken items 35 to 37 and
## 40 to 42): the swipe up dives, roots go around old roots, a gentler pull toward deposits,
## ending the night at once. Fast-forward at 8x is in test_fast_forward.gd.
var t

const STEP := 1.0 / 30.0


## An empty underground with only the given deposits (no rocks, no bands).
func _ground(dots: Array = []) -> Underground:
	var u := Underground.new(7)
	u.rock_centers = PackedVector3Array()
	u.rock_radii = PackedFloat32Array()
	u.bands = []
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


## A straight old root from `a` to `b` in steps of 0.25 m, hung under the trunk's root node by a
## first segment far away (so only the line itself is near the test). `flag`: "main" or "fine".
func _old_root(r: RootSystem, a: Vector3, b: Vector3, flag: String = "main") -> void:
	var parent := r.graph.add_node(0, a + Vector3(0, -6.0, 0))
	r.graph.set_flag(parent, flag, 0)
	var n := int(ceil(a.distance_to(b) / 0.25))
	for i in range(n + 1):
		var id := r.graph.add_node(parent, a.lerp(b, float(i) / n))
		r.graph.set_flag(id, flag, 0)
		parent = id
	if flag == "main":
		r.main_root_count = 1


## A run from `at` with the stick at `stick` for `seconds`; returns the tip positions of each frame.
func _drive(r: RootSystem, u: Underground, at: Vector3, heading: Vector3, stick: Vector2, seconds: float, life: float = 60.0) -> PackedVector3Array:
	var res := Resources.new()
	res.life_force = life
	r.start_run(0)
	r.pace_run(life)
	r.tip_position = at
	r.heading = heading.normalized()
	r.travel = r.heading
	r.run_length = 2.0  # past the start's free first metre
	r._update_right()
	var path := PackedVector3Array()
	for _i in range(int(seconds / STEP)):
		if not r.advance(stick, false, STEP, u, res):
			break
		path.append(r.tip_position)
	return path


## Distance from `p` to the old root's line.
func _to_line(p: Vector3, a: Vector3, b: Vector3) -> float:
	return p.distance_to(Geometry3D.get_closest_point_to_segment(p, a, b))


# --- 36: swipe up dives, swipe down comes back -------------------------------------------

func test_the_swipe_up_dives_and_the_swipe_down_comes_back() -> void:
	t.check(TreeView.is_swipe(Vector2(300, 900), Vector2(310, 600), true, 0.3), "a quick swipe up is the dive")
	t.check(not TreeView.is_swipe(Vector2(300, 600), Vector2(310, 900), true, 0.3), "a swipe down is not")
	t.check(TreeView.is_swipe(Vector2(300, 600), Vector2(310, 900), false, 0.3), "a swipe down is the way back")
	t.check(not TreeView.is_swipe(Vector2(300, 900), Vector2(310, 600), false, 0.3), "and a swipe up is not")
	t.check(not TreeView.is_swipe(Vector2(300, 900), Vector2(310, 600), true, 1.5), "a slow drag is no swipe")
	# The tree view at sunset: up dives, down does not.
	var tv := TreeView.new()
	t.root.add_child(tv)
	var g := GameState.new_game(7)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	tv.setup(g)
	var dived := [0]
	tv.ground_tapped.connect(func() -> void: dived[0] += 1)
	tv._begin_press(Vector2(300, 600))
	tv._drag(Vector2(305, 900), Vector2(5, 300))
	tv._end_press(true, Vector2(305, 900))
	t.check_eq(dived[0], 0, "a swipe down at sunset no longer dives")
	tv._begin_press(Vector2(300, 900))
	tv._drag(Vector2(305, 600), Vector2(5, -300))
	tv._end_press(true, Vector2(305, 600))
	t.check_eq(dived[0], 1, "a swipe up at sunset dives")
	tv._update_hud()
	t.check("swipe up" in tv._hint.text, "the sunset hint names the swipe up (%s)" % tv._hint.text)
	t.check("swipe up" in Pages.body("first_sunset"), "and so does the sunset page")
	tv.free()
	var src := FileAccess.get_file_as_string("res://roots/root_view.gd")
	t.check("Swipe down to wake the tree" in src and not "Swipe up to wake" in src, "the night's hint says swipe down")


# --- 37: around old roots -----------------------------------------------------------------

## A held heading straight at an old main root across the way: the tip goes round it, never
## through it, and carries on in the held direction.
func test_a_root_goes_around_an_old_root_and_keeps_its_direction() -> void:
	for depth in [-1.5, -4.0]:
		var r := RootSystem.new(3)
		var a := Vector3(-6.0, depth, -3.0)
		var b := Vector3(6.0, depth, -3.0)
		_old_root(r, a, b)
		var path := _drive(r, _ground(), Vector3(0.0, depth, 0.0), Vector3.FORWARD, Vector2.ZERO, 12.0)
		var closest := INF
		for p in path:
			closest = minf(closest, _to_line(p, a, b))
		t.check(closest >= RootSystem.AVOID_HARD - 0.01, "at %.1f m the tip never comes closer than %.2f m (%.2f)" % [depth, RootSystem.AVOID_HARD, closest])
		t.check(path[path.size() - 1].z < -4.0, "it got past the old root (z %.2f)" % path[path.size() - 1].z)
		t.check(r.heading.angle_to(Vector3.FORWARD) < deg_to_rad(8.0), "the held heading is unchanged (%.1f deg, the sink only)" % rad_to_deg(r.heading.angle_to(Vector3.FORWARD)))
		t.check(r.travel.angle_to(r.heading) < deg_to_rad(3.0), "and the tip is back on it (%.1f deg)" % rad_to_deg(r.travel.angle_to(r.heading)))
		# No shaking: the bend's side changes rarely.
		var flips := 0
		var last := 0.0
		for i in range(2, path.size()):
			var side := (path[i] - path[i - 1]).y - (path[i - 1] - path[i - 2]).y
			if absf(side) > 1e-4:
				if last != 0.0 and signf(side) != signf(last):
					flips += 1
				last = side
		t.check(flips <= 6, "it bends smoothly, no shaking (%d turns of the curve)" % flips)


func test_fine_roots_do_not_block() -> void:
	var r := RootSystem.new(3)
	var a := Vector3(-6.0, -4.0, -3.0)
	var b := Vector3(6.0, -4.0, -3.0)
	_old_root(r, a, b, "fine")
	var path := _drive(r, _ground(), Vector3(0.0, -4.0, 0.0), Vector3.FORWARD, Vector2.ZERO, 6.0)
	var closest := INF
	for p in path:
		closest = minf(closest, _to_line(p, a, b))
	t.check(closest < RootSystem.AVOID_HARD, "a fine root is passed straight through (%.2f m)" % closest)


func test_a_root_never_runs_through_its_own_earlier_part() -> void:
	var r := RootSystem.new(3)
	# Full stick: circles of about a metre, again and again over the same ground.
	var path := _drive(r, _ground(), Vector3(0.0, -4.0, 0.0), Vector3.FORWARD, Vector2(1, 0), 14.0, 200.0)
	var own_from := r.run_first_new_id
	var worst := INF
	var skip := int(ceil(RootSystem.AVOID_OWN_SKIP / r.step_length)) + 2
	for id in range(own_from + skip + 1, r.graph.size()):
		var p := r.graph.positions[id]
		for k in range(own_from + 1, id - skip):
			worst = minf(worst, p.distance_to(Geometry3D.get_closest_point_to_segment(p, r.graph.positions[r.graph.parents[k]], r.graph.positions[k])))
	t.check(path.size() > 60, "the root grew (%d frames)" % path.size())
	t.check(worst >= RootSystem.AVOID_HARD - 0.03, "it never crosses its own earlier part (%.2f m)" % worst)


func test_boxed_in_among_roots_ends_instead_of_hanging() -> void:
	var r := RootSystem.new(3)
	var c := Vector3(0, -4.0, 0)
	# A cage of old roots all round the tip, 0.5 m out, from the floor to the meadow.
	for k in range(12):
		var ang := TAU * k / 12.0
		var o := Vector3(cos(ang), 0, sin(ang)) * 0.5
		_old_root(r, Vector3(o.x, -9.9, o.z), Vector3(o.x, -0.06, o.z))
	_old_root(r, c + Vector3(-1, 0.6, -1), c + Vector3(1, 0.6, 1))
	_old_root(r, c + Vector3(-1, -0.6, 1), c + Vector3(1, -0.6, -1))
	var path := _drive(r, _ground(), c, Vector3.FORWARD, Vector2.ZERO, 20.0)
	t.check(not r.run_active, "the boxed-in root ended (after %.1f s)" % (path.size() * STEP))
	var far := 0.0
	for p in path:
		far = maxf(far, Vector2(p.x - c.x, p.z - c.z).length())
	t.check(far < 0.5, "without slipping through the cage (%.2f m out)" % far)


# --- 40: a gentler pull ----------------------------------------------------------------

func test_a_deposit_bends_the_tip_at_most_thirty_degrees_and_it_returns() -> void:
	var r := RootSystem.new(3)
	t.check_near(r.magnet_radius, 1.2, 0.01, "the pull reaches 1.2 m")
	t.check_near(r.magnet_rate, 0.6, 0.01, "at 0.6 rad/s")
	# A deposit ahead and off to the side, beyond the tip's reach unless it bends.
	var at := Vector3(0, -4.0, 0)
	var dot := at + Vector3(0.8, 0, -1.3)
	var u := _ground([dot])
	var res := Resources.new()
	res.life_force = 60.0
	r.start_run(0)
	r.pace_run(60.0)
	r.tip_position = at
	r.heading = Vector3.FORWARD
	r.travel = r.heading
	r._update_right()
	var most := 0.0
	for _i in range(int(6.0 / STEP)):
		if not r.advance(Vector2.ZERO, false, STEP, u, res):
			break
		most = maxf(most, r.travel.angle_to(r.heading))
	t.check(most <= deg_to_rad(31.0), "one deposit bends the tip by at most about 30 degrees (%.1f)" % rad_to_deg(most))
	t.check(most > deg_to_rad(10.0), "but it does bend (%.1f)" % rad_to_deg(most))
	t.check(u.dot_amounts[0] < u.dot_capacity[0], "the bend brings the tip to the deposit")
	t.check(r.travel.angle_to(r.heading) < deg_to_rad(2.0), "after it the tip is back on the held heading (%.1f)" % rad_to_deg(r.travel.angle_to(r.heading)))
	t.check(Vector2(r.heading.x, r.heading.z).normalized().distance_to(Vector2(0, -1)) < 0.01, "which the pull never turned")


# --- 41, 42: ending at once -------------------------------------------------------------------

## The first night with the root bot, the second night's dusk: a few old roots to grow from.
func _second_night(seed: int) -> GameState:
	var g := GameState.new_game(seed)
	var bot := RootBot.new()
	for _night in range(3):
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		g.dive()
		if g.can_start_run():
			g.start_run(0)
			var guard := 0
			while g.steer(bot.stick_for(g.roots, g.ground), false, STEP) and guard < 20000:
				guard += 1
		while g.phase != GameState.Phase.DAY:
			g.tick(0.25)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	return g


func test_ending_the_night_at_once_grows_small_roots_everywhere() -> void:
	var g := _second_night(14)
	var r := g.roots
	t.check(g.can_start_run(), "a night with a root to grow")
	var mains := r.main_root_count
	var before := r.graph.size()
	var tank := g.sim.resources.life_force
	# The nearest fresh dot to any root, within the side roots' reach.
	var reach := minf(r.side_reach_max, r.side_reach_base + r.side_reach_per_life_force * tank)
	var near := r.nearest_fresh(reach, 1 << 20, 0.12, g.ground)
	var dots: PackedInt32Array = near["dots"]
	t.check(not dots.is_empty(), "fresh dots lie within reach of the network")
	var first := dots[0] if not dots.is_empty() else -1
	var parents := {}
	g.finish_run_early()
	t.check(g.run_used and g.night_done, "the night is done at once")
	t.check_near(g.sim.resources.life_force, 0.0, 1e-4, "the whole tank was spent")
	t.check_eq(r.main_root_count, mains, "no main root tonight")
	t.check(r.side_nodes_grown[0] > 0, "small roots grew (%d)" % r.side_nodes_grown[0])
	for id in range(before, r.graph.size()):
		t.check(r.root_level(id) >= 2, "only side roots grew")
		var p := r.graph.parents[id]
		if p < before:
			parents[p] = true
	var mains_from := {}
	for p in parents:
		mains_from[int(r.graph.get_flag(p, "main", r.graph.get_flag(p, "fine", -1)))] = true
	t.check(parents.size() >= 2, "from several points of the network (%d)" % parents.size())
	if first >= 0:
		t.check(r.tapped.has(first) or g.ground.dot_collected[first] != 0 or g.ground.dot_amounts[first] < g.ground.dot_capacity[first], "the nearest fresh dot was reached")
	# Sorted nearest first.
	var near2 := r.nearest_fresh(2.5, 1 << 20, 0.12, g.ground)
	var ok := true
	var ms: PackedInt32Array = near2["dots"]
	for i in range(1, ms.size()):
		var da := _nearest_node_dist(r, g.ground.dot_positions[ms[i - 1]])
		var db := _nearest_node_dist(r, g.ground.dot_positions[ms[i]])
		if da > db + 1e-3:
			ok = false
	t.check(ok, "the side roots take the nearest dots first")


func _nearest_node_dist(r: RootSystem, p: Vector3) -> float:
	var best := INF
	for id in range(r.graph.size()):
		best = minf(best, r.graph.positions[id].distance_to(p))
	return best


func test_ending_before_the_root_moved_spends_the_tank_too() -> void:
	var g := _second_night(5)
	var r := g.roots
	var mains := r.main_root_count
	g.start_run(0)
	g.finish_run_early()
	t.check(not r.run_active and g.night_done, "the run ended at once")
	t.check_near(g.sim.resources.life_force, 0.0, 1e-4, "its tank grew small roots")
	t.check_eq(r.main_root_count, mains, "and no main root")
	t.check(r.side_nodes_grown[0] > 0, "small roots grew (%d)" % r.side_nodes_grown[0])
	t.check(r.side_nodes_grown[0] + r.side_nodes_grown[1] <= r.side_nodes_max, "within the night's cap")
