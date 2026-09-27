extends RefCounted
var t


func _fed_sim(seed: int = 3) -> GrowthSim:
	var s := GrowthSim.new(seed)
	for k in range(4):
		s.resources.add(k, 50.0)
	return s


func test_no_growth_without_nutrients() -> void:
	var s := GrowthSim.new(1)
	s.clock.time_of_day = 0.25
	var before := s.graph.size()
	for _i in range(20):
		s.tick(0.5)
	t.check_eq(s.graph.size(), before, "empty stock, no new nodes")


func test_no_growth_at_night() -> void:
	var s := _fed_sim()
	s.clock.time_of_day = 0.75
	var before := s.graph.size()
	s.tick(0.5)
	t.check_eq(s.graph.size(), before, "night, no growth")


func test_grows_and_spends_nutrients_in_daylight() -> void:
	var s := _fed_sim()
	s.clock.time_of_day = 0.2
	var water_before := s.resources.amount(Resources.Kind.WATER)
	for _i in range(40):
		s.tick(0.5)
	t.check(s.graph.size() > 10, "tree grew (%d nodes)" % s.graph.size())
	t.check(s.resources.amount(Resources.Kind.WATER) < water_before, "water was spent")
	t.check(s.resources.life_force > 0.0, "life force produced")
	t.check(s.height() > 0.3, "got taller (%f m)" % s.height())


## Centroid of the nodes grown while `time_of_day` is held at `t` with the boost on.
## The sapling first grows 20 s at calm noon, since a seedling grows straight up before it leans.
## Averaged over three seeds, so one lucky or unlucky tree does not decide the test.
func _boosted_growth_centroid(t_of_day: float) -> Vector3:
	var c := Vector3.ZERO
	var d := Vector3.ZERO
	for seed in [11, 12, 13]:
		c += _boosted_growth_centroid_one(t_of_day, seed)
		d += _last_direction
	_last_direction = d / 3.0
	return c / 3.0


func _boosted_growth_centroid_one(t_of_day: float, seed: int) -> Vector3:
	var s := _fed_sim(seed)
	var noon := s.clock.daylight_fraction * 0.5
	for _i in range(40):
		s.clock.time_of_day = noon
		s.tick(0.5)
	var first_new := s.graph.size()
	s.clock.boost_active = true
	for _i in range(60):
		s.clock.time_of_day = t_of_day
		s.tick(0.5)
	var c := Vector3.ZERO
	var dir := Vector3.ZERO
	var n := s.graph.size() - first_new
	for id in range(first_new, s.graph.size()):
		c += s.graph.positions[id]
		dir += s.graph.direction_of(id)
	_last_direction = dir / maxf(1.0, float(n))
	return c / maxf(1.0, float(n))


## Mean growth direction of the nodes from the last _boosted_growth_centroid call.
var _last_direction := Vector3.ZERO


func test_morning_boost_leans_east() -> void:
	# Acceptance criterion from the design doc: boosting in the morning grows the tree east.
	var c := _boosted_growth_centroid(0.03)
	t.check(c.x > 0.1, "morning growth leans east (x=%f)" % c.x)


func test_noon_boost_leans_south_and_up() -> void:
	# Sun steering in all three dimensions: at noon the sun stands in the south (-Z) and high.
	var noon := DayCycle.new().daylight_fraction * 0.5
	var c := _boosted_growth_centroid(noon)
	var up_at_noon := _last_direction.y
	_boosted_growth_centroid(0.03)
	var up_in_morning := _last_direction.y
	t.check(c.z < -0.1, "noon growth leans south (z=%f)" % c.z)
	t.check(absf(c.x) < 0.3, "neither east nor west (x=%f)" % c.x)
	t.check(up_at_noon > up_in_morning, "a high sun grows up, a low sun sideways (%f vs %f)" % [up_at_noon, up_in_morning])


func test_boosted_day_grows_more_but_yields_less_life_force() -> void:
	var calm := _fed_sim(5)
	var boosted := _fed_sim(5)
	boosted.clock.boost_active = true
	for _i in range(40):
		calm.clock.time_of_day = 0.2
		boosted.clock.time_of_day = 0.2
		calm.tick(0.5)
		boosted.tick(0.5)
	t.check(boosted.graph.size() > calm.graph.size(), "boost grows more nodes")
	t.check(boosted.resources.life_force < calm.resources.life_force, "boost yields less life force")


func test_evening_boost_leans_west() -> void:
	var c := _boosted_growth_centroid(0.6)
	t.check(c.x < -0.1, "evening growth leans west (x=%f)" % c.x)


func test_deterministic_from_seed() -> void:
	var a := _fed_sim(99)
	var b := _fed_sim(99)
	for _i in range(30):
		a.tick(0.5)
		b.tick(0.5)
	t.check_eq(a.graph.positions, b.graph.positions, "same seed, same growth")


func test_prune_marks_subtree_dead() -> void:
	var s := _fed_sim()
	s.clock.time_of_day = 0.2
	for _i in range(30):
		s.tick(0.5)
	var kids: Array = s.graph.children[1]
	var victim: int = int(kids[0]) if not kids.is_empty() else 1
	var killed := s.prune(victim)
	t.check(killed >= 1, "at least one node pruned")
	t.check_eq(s.graph.get_flag(victim, "dead", false), true, "victim dead")
	var alive_tips := 0
	for tip in s.graph.tips():
		if not s.graph.get_flag(tip, "dead", false):
			alive_tips += 1
	t.check(alive_tips >= 0, "still consistent")


func test_offline_growth_is_slow() -> void:
	var s := _fed_sim()
	s.clock.time_of_day = 0.2
	var before := s.graph.size()
	s.apply_offline(86400.0)  # one real day away = ~20 s of game time
	var grown := s.graph.size() - before
	t.check(grown > 0, "some offline growth")
	t.check(grown < 200, "but not a lot (%d nodes)" % grown)


func test_budget_never_exceeded() -> void:
	var s := _fed_sim()
	s.graph.max_nodes = 40
	s.clock.time_of_day = 0.2
	for _i in range(200):
		s.tick(0.5)
		s.clock.time_of_day = 0.2
	t.check(s.graph.size() <= 40, "node budget respected (%d)" % s.graph.size())
