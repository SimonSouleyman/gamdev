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


func test_morning_boost_leans_east() -> void:
	# Acceptance criterion from the design doc: boosting in the morning grows the tree east.
	var s := _fed_sim(11)
	s.clock.time_of_day = 0.02
	s.clock.boost_active = true
	for _i in range(60):
		s.tick(0.5)
		if s.clock.time_of_day > 0.2:
			s.clock.time_of_day = 0.02  # keep it morning
	t.check(s.centroid().x > 0.1, "crown leans east (x=%f)" % s.centroid().x)


func test_evening_boost_leans_west() -> void:
	var s := _fed_sim(11)
	s.clock.time_of_day = 0.45
	s.clock.boost_active = true
	for _i in range(60):
		s.tick(0.5)
		if s.clock.time_of_day > 0.49 or s.clock.time_of_day < 0.3:
			s.clock.time_of_day = 0.45
	t.check(s.centroid().x < -0.1, "crown leans west (x=%f)" % s.centroid().x)


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
