extends RefCounted
var t


func test_new_graph_has_base_node() -> void:
	var g := PlantGraph.new()
	t.check_eq(g.size(), 1, "base node exists")
	t.check(g.is_tip(0), "base is a tip")


func test_add_node_links_parent_and_child() -> void:
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	t.check_eq(a, 1, "second id")
	t.check_eq(g.parents[a], 0, "parent set")
	t.check_eq(g.children[0], [1], "child listed")
	t.check(not g.is_tip(0) and g.is_tip(a), "tip moved")
	t.check_near(g.direction_of(a).y, 1.0, 1e-6, "direction up")


func test_budget_is_enforced() -> void:
	var g := PlantGraph.new(Vector3.ZERO, 3)
	t.check(g.add_node(0, Vector3.UP) >= 0, "node 2 ok")
	t.check(g.add_node(0, Vector3.RIGHT) >= 0, "node 3 ok")
	t.check_eq(g.add_node(0, Vector3.LEFT), -1, "node 4 refused")
	t.check(g.is_full(), "full")


func test_pipe_model_radii() -> void:
	var g := PlantGraph.new()
	var trunk := g.add_node(0, Vector3(0, 1, 0))
	g.add_node(trunk, Vector3(-0.5, 2, 0))
	g.add_node(trunk, Vector3(0.5, 2, 0))
	g.update_radii()
	var tip := PlantGraph.TIP_RADIUS
	var expected := pow(2.0 * pow(tip, PlantGraph.PIPE_EXPONENT), 1.0 / PlantGraph.PIPE_EXPONENT)
	t.check_near(g.radii[trunk], expected, 1e-6, "fork radius from two tips")
	t.check(g.radii[0] >= g.radii[trunk], "base at least as thick as trunk")
	t.check(g.radii[trunk] > tip, "fork thicker than a tip")


func test_round_trip_dict() -> void:
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	g.add_node(a, Vector3(0, 2, 0))
	g.set_flag(a, "dead", true)
	g.update_radii()
	var g2 := PlantGraph.from_dict(g.to_dict())
	t.check_eq(g2.size(), 3, "size kept")
	t.check_eq(g2.children[a], [2], "children rebuilt")
	t.check_eq(g2.get_flag(a, "dead", false), true, "flags kept")
	t.check_near(g2.total_length(), 2.0, 1e-6, "lengths kept")
