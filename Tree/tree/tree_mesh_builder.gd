class_name TreeMeshBuilder
extends RefCounted
## Builds one low-poly ArrayMesh from a PlantGraph: a tapered tube per segment,
## with fewer sides for thin branches. Rendering only reads the graph.
## Prototype quality; branch-chain skinning and chunked rebuilds are a later step.

var sides_thick: int = 6
var sides_thin: int = 4
var thin_radius: float = 0.03
var min_radius: float = 0.008
var radius_scale: float = 1.0


func build(graph: PlantGraph) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var tri_count := 0
	for id in range(1, graph.size()):
		if graph.get_flag(id, "dead", false):
			continue
		var p := graph.parents[id]
		tri_count += _add_tube(st, graph.positions[p], graph.positions[id],
			_r(graph.radii[p]), _r(graph.radii[id]))
	if tri_count == 0:
		return ArrayMesh.new()
	st.generate_normals()
	return st.commit()


func _r(raw: float) -> float:
	return maxf(min_radius, raw * radius_scale)


func _add_tube(st: SurfaceTool, a: Vector3, b: Vector3, ra: float, rb: float) -> int:
	var axis := b - a
	if axis.length_squared() < 1e-10:
		return 0
	axis = axis.normalized()
	var sides := sides_thick if ra > thin_radius else sides_thin
	var u := axis.cross(Vector3.UP if absf(axis.dot(Vector3.UP)) < 0.9 else Vector3.RIGHT).normalized()
	var v := axis.cross(u).normalized()
	var tris := 0
	for i in range(sides):
		var a0 := TAU * i / sides
		var a1 := TAU * (i + 1) / sides
		var d0 := u * cos(a0) + v * sin(a0)
		var d1 := u * cos(a1) + v * sin(a1)
		var p00 := a + d0 * ra
		var p01 := a + d1 * ra
		var p10 := b + d0 * rb
		var p11 := b + d1 * rb
		st.add_vertex(p00)
		st.add_vertex(p10)
		st.add_vertex(p11)
		st.add_vertex(p00)
		st.add_vertex(p11)
		st.add_vertex(p01)
		tris += 2
	return tris
