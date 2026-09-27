class_name BranchMeshBuilder
extends RefCounted
## Smooth wood for the tree: every node gets one ring oriented along the bisector of the
## branch coming in and its main continuation, with a twist-free frame carried down each
## branch (parallel transport), so a branch reads as one bending limb, not a chain of pipes.
## Side branches start from their own ring sized to the side branch. UVs run around the
## wood (u) and along it in metres (v), with tangents, so a tiling bark texture fits.
## Rendering only reads the graph.

var radius_scale: float = 1.3
var min_radius: float = 0.006
## Bark texture repeats per metre along the wood.
var bark_tiling: float = 1.2


func sides_for(r: float) -> int:
	if r > 0.12:
		return 10
	if r > 0.04:
		return 7
	if r > 0.015:
		return 5
	return 4


func _r(raw: float) -> float:
	return maxf(min_radius, raw * radius_scale)


## Builds the segments of nodes `first_id` .. `end_id` - 1 (-1: to the last node). Frames are
## only computed for those nodes and their ancestors, so rebuilding a growing root is cheap.
func build(g: PlantGraph, first_id: int = 1, end_id: int = -1) -> ArrayMesh:
	var n := g.size()
	if n < 2:
		return ArrayMesh.new()
	var last := n if end_id < 0 else mini(end_id, n)
	var first := maxi(1, first_id)
	# The nodes to mesh plus their ancestors (for the twist-free frames), in id order.
	# A range build (the growing root) touches only its own chain, not the whole graph.
	var seen := {}
	var order := PackedInt32Array()
	for id in range(first, last):
		var cur := id
		while cur >= 0 and not seen.has(cur):
			seen[cur] = true
			order.append(cur)
			cur = g.parents[cur]
	order.sort()
	var alive := PackedByteArray()
	alive.resize(n)
	for id in order:
		alive[id] = 0 if g.get_flag(id, "dead", false) else 1
		for c in (g.children[id] as Array):
			alive[c] = 0 if g.get_flag(c, "dead", false) else 1
	# The main continuation of each node: its thickest living child.
	var main_child := PackedInt32Array()
	main_child.resize(n)
	for id in order:
		var best := -1
		var best_r := -1.0
		for c in (g.children[id] as Array):
			if alive[c] == 1 and g.radii[c] > best_r:
				best_r = g.radii[c]
				best = c
		main_child[id] = best
	# Frames: axis (bisector) and a twist-free side vector per node.
	var axis := PackedVector3Array()
	var side := PackedVector3Array()
	var dir_in := PackedVector3Array()
	var along := PackedFloat32Array()
	axis.resize(n)
	side.resize(n)
	dir_in.resize(n)
	along.resize(n)
	for id in order:
		var p := g.parents[id]
		var din := Vector3.UP
		if p >= 0:
			var d := g.positions[id] - g.positions[p]
			din = d.normalized() if d.length_squared() > 1e-10 else Vector3.UP
			along[id] = along[p] + d.length()
		dir_in[id] = din
		var a := din
		var mc := main_child[id]
		if mc >= 0:
			var dout := g.positions[mc] - g.positions[id]
			if dout.length_squared() > 1e-10:
				a = (din + dout.normalized()).normalized() if p >= 0 else dout.normalized()
		if a.length_squared() < 1e-6:
			a = din
		axis[id] = a
		var s := Vector3.RIGHT if p < 0 else side[p]
		s = s - a * s.dot(a)
		if s.length_squared() < 1e-6:
			s = a.cross(Vector3.FORWARD if absf(a.dot(Vector3.FORWARD)) < 0.9 else Vector3.RIGHT)
		side[id] = s.normalized()

	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var tangents := PackedFloat32Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	for id in range(first, last):
		if alive[id] == 0:
			continue
		var p := g.parents[id]
		var r_top := _r(g.radii[id])
		var is_tip := main_child[id] < 0
		if is_tip:
			r_top *= 0.55
		var sides := sides_for(r_top)
		var bottom_axis: Vector3
		var bottom_side: Vector3
		var r_bottom: float
		if main_child[p] == id:
			bottom_axis = axis[p]
			bottom_side = side[p]
			r_bottom = _r(g.radii[p])
		else:
			# A side branch starts from its own ring, only a little thicker than itself.
			bottom_axis = dir_in[id]
			var s := side[p] - bottom_axis * side[p].dot(bottom_axis)
			if s.length_squared() < 1e-6:
				s = side[id]
			bottom_side = s.normalized()
			r_bottom = minf(_r(g.radii[p]), _r(g.radii[id]) * 1.35)
		var base := verts.size()
		# One bark repeat count per segment, so the texture never shears between its two rings.
		var reps := maxf(1.0, round(r_bottom * 20.0))
		_ring(g.positions[p], bottom_axis, bottom_side, r_bottom, sides, along[p] * bark_tiling, reps, verts, normals, tangents, uvs)
		_ring(g.positions[id], axis[id], side[id], r_top, sides, along[id] * bark_tiling, reps, verts, normals, tangents, uvs)
		var stride := sides + 1
		for i in range(sides):
			var a0 := base + i
			var a1 := base + i + 1
			var b0 := base + stride + i
			var b1 := base + stride + i + 1
			indices.append_array([a0, b0, b1, a0, b1, a1])
	if indices.is_empty():
		return ArrayMesh.new()
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TANGENT] = tangents
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _ring(center: Vector3, a: Vector3, s: Vector3, r: float, sides: int, v: float, reps: float,
		verts: PackedVector3Array, normals: PackedVector3Array, tangents: PackedFloat32Array, uvs: PackedVector2Array) -> void:
	var t := a.cross(s)
	for i in range(sides + 1):
		var ang := TAU * float(i) / float(sides)
		var nrm := s * cos(ang) + t * sin(ang)
		verts.append(center + nrm * r)
		normals.append(nrm)
		var tan := (-s * sin(ang) + t * cos(ang))
		tangents.append_array([tan.x, tan.y, tan.z, 1.0])
		uvs.append(Vector2(float(i) / float(sides) * reps, v))
