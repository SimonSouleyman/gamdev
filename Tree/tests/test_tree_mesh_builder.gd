extends RefCounted
var t


func test_mesh_has_expected_triangles() -> void:
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	g.add_node(a, Vector3(0.3, 2, 0))
	g.update_radii()
	var mesh := TreeMeshBuilder.new().build(g)
	t.check_eq(mesh.get_surface_count(), 1, "one surface")
	var arrays := mesh.surface_get_arrays(0)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	# 2 segments x 4 sides (thin) x 2 triangles x 3 vertices
	t.check_eq(verts.size(), 2 * 4 * 2 * 3, "vertex count")


func test_dead_branches_are_hidden() -> void:
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	var b := g.add_node(a, Vector3(0.3, 2, 0))
	g.set_flag(b, "dead", true)
	g.update_radii()
	var mesh := TreeMeshBuilder.new().build(g)
	var verts: PackedVector3Array = mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	t.check_eq(verts.size(), 1 * 4 * 2 * 3, "only the live segment is meshed")


func test_empty_graph_gives_empty_mesh() -> void:
	var mesh := TreeMeshBuilder.new().build(PlantGraph.new())
	t.check_eq(mesh.get_surface_count(), 0, "no surface for a lone base node")
