extends RefCounted
## 0.8.2.4: the bird's nest (tree/bird_nest.gd) sits on a side limb's fork with room round it,
## stays cheap (one mesh, about 1500 triangles at most) and is the same for the same seed.
var t


## A straight 8 m trunk with two forks: one right against the trunk at 4 m, one out on a
## sideways limb at 4.8 m. Returns [graph, the fork against the trunk, the fork on the limb].
func _tree() -> Array:
	var g := PlantGraph.new()
	var last := 0
	var trunk: Array[int] = []
	for i in range(1, 17):
		last = g.add_node(last, Vector3(0, i * 0.5, 0))
		trunk.append(last)
	# Against the trunk: a stub 15 cm out that forks at once.
	var near := g.add_node(trunk[7], Vector3(0.15, 4.0, 0.0))
	g.add_node(near, Vector3(0.5, 4.1, 0.3))
	g.add_node(near, Vector3(0.5, 4.1, -0.3))
	# Out on a limb.
	var a := g.add_node(trunk[8], Vector3(0.5, 4.6, 0))
	var b := g.add_node(a, Vector3(1.0, 4.7, 0))
	var fork := g.add_node(b, Vector3(1.5, 4.8, 0))
	g.add_node(fork, Vector3(2.0, 4.9, 0.4))
	g.add_node(fork, Vector3(2.0, 4.9, -0.4))
	for id in g.size():
		g.radii[id] = 0.1 if trunk.has(id) or id == 0 else 0.025
	return [g, near, fork]


func test_the_nest_takes_a_fork_with_room_not_one_against_the_trunk() -> void:
	var tr := _tree()
	var g: PlantGraph = tr[0]
	t.check(BirdNest.room(g, tr[1], 1.0) < 0.0, "no room against the trunk")
	t.check(BirdNest.room(g, tr[2], 1.0) > 0.1, "room out on the limb")
	t.check_eq(BirdNest.pick_fork(g, 8.0, 1.5), tr[2], "the nest goes out on the limb")


func test_the_nest_is_one_cheap_mesh_the_same_for_the_same_seed() -> void:
	var dirs: Array[Vector3] = [Vector3(-1, -0.2, 0).normalized(), Vector3(1, 0.2, 0.8).normalized(), Vector3(1, 0.2, -0.8).normalized()]
	for eggs in [false, true]:
		var n1 := BirdNest.new()
		var m1 := n1.build(7, 0.04, dirs, eggs)
		t.check_eq(m1.get_surface_count(), 1, "one surface")
		t.check(n1.triangle_count() <= 1500, "at most 1500 triangles (got %d)" % n1.triangle_count())
		var n2 := BirdNest.new()
		var m2 := n2.build(7, 0.04, dirs, eggs)
		t.check_eq(m2.surface_get_arrays(0)[Mesh.ARRAY_VERTEX], m1.surface_get_arrays(0)[Mesh.ARRAY_VERTEX], "same seed, same nest")
		var box := m1.get_aabb()
		t.check(box.size.x < 0.5 and box.size.x > 0.18, "about a blackbird's nest across (%.2f m)" % box.size.x)
		n1.free()
		n2.free()
