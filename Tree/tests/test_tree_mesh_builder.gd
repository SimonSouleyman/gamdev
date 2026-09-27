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


func test_branch_builder_makes_smooth_uv_mapped_wood() -> void:
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	var b := g.add_node(a, Vector3(0.1, 2, 0))
	g.add_node(a, Vector3(0.6, 1.5, 0))
	var dead := g.add_node(b, Vector3(0.1, 3, 0))
	g.set_flag(dead, "dead", true)
	g.update_radii()
	var mesh := BranchMeshBuilder.new().build(g)
	t.check_eq(mesh.get_surface_count(), 1, "one surface")
	var arrays := mesh.surface_get_arrays(0)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	t.check(verts.size() > 0, "has vertices")
	t.check_eq((arrays[Mesh.ARRAY_TEX_UV] as PackedVector2Array).size(), verts.size(), "every vertex has a UV")
	t.check_eq((arrays[Mesh.ARRAY_TANGENT] as PackedFloat32Array).size(), verts.size() * 4, "and a tangent (for bark normal maps)")
	var top := 0.0
	for v in verts:
		top = maxf(top, v.y)
	t.check(top < 2.6, "the dead branch is not meshed (top %f)" % top)


func test_branch_rings_meet_without_gaps() -> void:
	# A straight chain: the top ring of one segment must equal the bottom ring of the next.
	var g := PlantGraph.new()
	var a := g.add_node(0, Vector3(0, 1, 0))
	var b := g.add_node(a, Vector3(0.2, 2, 0))
	g.add_node(b, Vector3(0.5, 3, 0.1))
	g.update_radii()
	var verts: PackedVector3Array = BranchMeshBuilder.new().build(g).surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
	var sides := (verts.size() / 3) / 2 - 1
	var top_of_first := verts[sides + 1]
	var bottom_of_second := verts[(sides + 1) * 2]
	t.check(top_of_first.distance_to(bottom_of_second) < 1e-4, "segments share their joint ring")
