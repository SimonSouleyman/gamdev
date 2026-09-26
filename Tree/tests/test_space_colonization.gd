extends RefCounted
var t


func _rng(seed: int = 7) -> RandomNumberGenerator:
	var r := RandomNumberGenerator.new()
	r.seed = seed
	return r


func test_grows_toward_markers_and_consumes_them() -> void:
	var g := PlantGraph.new()
	var sc := SpaceColonization.new(_rng())
	sc.jitter = 0.0
	sc.bias_strength = 0.0
	sc.add_marker(Vector3(0, 1, 0))
	var steps := 0
	while not sc.markers.is_empty() and steps < 50:
		sc.step(g)
		steps += 1
	t.check(sc.markers.is_empty(), "marker reached")
	t.check(g.size() > 1, "nodes were added")
	var top := g.positions[g.size() - 1]
	t.check(top.y > 0.7, "grew upward toward the marker (y=%f)" % top.y)


func test_ignores_markers_out_of_reach() -> void:
	var g := PlantGraph.new()
	var sc := SpaceColonization.new(_rng())
	sc.influence_radius = 0.5
	sc.add_marker(Vector3(0, 5, 0))
	t.check_eq(sc.step(g), 0, "no growth when marker too far")


func test_growth_cap_per_step() -> void:
	var g := PlantGraph.new()
	var sc := SpaceColonization.new(_rng())
	sc.influence_radius = 10.0
	g.add_node(0, Vector3(1, 0, 0))
	g.add_node(0, Vector3(-1, 0, 0))
	sc.add_marker(Vector3(1, 1, 0))
	sc.add_marker(Vector3(-1, 1, 0))
	t.check_eq(sc.step(g, 1), 1, "capped to one new node")


func test_deterministic_with_same_seed() -> void:
	var results: Array = []
	for _i in range(2):
		var g := PlantGraph.new()
		var sc := SpaceColonization.new(_rng(42))
		sc.seed_sphere(Vector3(0, 2, 0), 1.0, 40)
		for _s in range(15):
			sc.step(g)
		results.append(g.positions)
	t.check_eq(results[0], results[1], "same seed, same tree")


func test_marker_budget() -> void:
	var sc := SpaceColonization.new(_rng())
	sc.seed_sphere(Vector3.ZERO, 1.0, 100, 10)
	t.check_eq(sc.markers.size(), 10, "marker limit respected")
