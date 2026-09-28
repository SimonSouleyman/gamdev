class_name RockMesh
extends RefCounted
## Procedural boulders: a subdivided icosphere squashed and cut by a few random planes (the
## flat fractured faces real stones have), then roughened with fractal noise. Each vertex gets
## a cavity value in COLOR.r (1 = exposed ridge, 0 = crevice) that the shader uses for
## occlusion and dirt. Seeded; radius 1, scale the instance.


static func build(seed: int, detail: int = 3) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "rock"])
	var ico := _icosphere(detail)
	var verts: PackedVector3Array = ico[0]
	var tris: PackedInt32Array = ico[1]
	var noise := FastNoiseLite.new()
	noise.seed = rng.randi()
	noise.frequency = 1.3
	noise.fractal_octaves = 4
	# Overall shape: a slightly flattened, elongated lump.
	var stretch := Vector3(rng.randf_range(0.9, 1.3), rng.randf_range(0.6, 0.85), rng.randf_range(0.8, 1.1))
	# Fracture planes: every point beyond a plane is pushed back onto it.
	var planes: Array[Plane] = []
	for _i in range(rng.randi_range(6, 10)):
		var n := Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.6, 1), rng.randf_range(-1, 1)).normalized()
		planes.append(Plane(n, rng.randf_range(0.5, 0.78)))
	for i in range(verts.size()):
		var v := verts[i] * stretch
		v *= 1.0 + 0.1 * noise.get_noise_3dv(v * 1.0)
		for pl in planes:
			var d := pl.distance_to(v)
			if d > 0.0:
				# Soft cut: nearly flat face, the edge slightly rounded.
				v -= pl.normal * d * 0.97
		v += v.normalized() * 0.018 * noise.get_noise_3dv(v * 4.0)
		verts[i] = v
	# Smooth normals, then the cavity from how much each vertex sits below its neighbours.
	var normals := PackedVector3Array()
	normals.resize(verts.size())
	var neighbour_sum := PackedVector3Array()
	neighbour_sum.resize(verts.size())
	var neighbour_count := PackedInt32Array()
	neighbour_count.resize(verts.size())
	for t in range(0, tris.size(), 3):
		var a := tris[t]
		var b := tris[t + 1]
		var c := tris[t + 2]
		var fn := (verts[b] - verts[a]).cross(verts[c] - verts[a])
		for k in [a, b, c]:
			normals[k] += fn
		for pair in [[a, b], [b, c], [c, a], [b, a], [c, b], [a, c]]:
			neighbour_sum[pair[0]] += verts[pair[1]]
			neighbour_count[pair[0]] += 1
	var colors := PackedColorArray()
	colors.resize(verts.size())
	for i in range(verts.size()):
		# Clockwise winding: the face cross products point inward.
		normals[i] = -normals[i].normalized()
		var avg := neighbour_sum[i] / float(maxi(neighbour_count[i], 1))
		var convex := (verts[i] - avg).dot(normals[i])
		colors[i] = Color(clampf(0.5 + convex * 40.0, 0.0, 1.0), 0, 0)
	# Flat bottom a little under the ground line, so the rock sits instead of balancing.
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = tris
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


static func _icosphere(detail: int) -> Array:
	var t := (1.0 + sqrt(5.0)) / 2.0
	var verts := PackedVector3Array([
		Vector3(-1, t, 0), Vector3(1, t, 0), Vector3(-1, -t, 0), Vector3(1, -t, 0),
		Vector3(0, -1, t), Vector3(0, 1, t), Vector3(0, -1, -t), Vector3(0, 1, -t),
		Vector3(t, 0, -1), Vector3(t, 0, 1), Vector3(-t, 0, -1), Vector3(-t, 0, 1)])
	for i in range(verts.size()):
		verts[i] = verts[i].normalized()
	var tris := PackedInt32Array([0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4,
		11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11,
		6, 2, 10, 8, 6, 7, 9, 8, 1])
	for _d in range(detail):
		var cache := {}
		var next := PackedInt32Array()
		for i in range(0, tris.size(), 3):
			var a := tris[i]
			var b := tris[i + 1]
			var c := tris[i + 2]
			var ab := _mid(verts, cache, a, b)
			var bc := _mid(verts, cache, b, c)
			var ca := _mid(verts, cache, c, a)
			next.append_array([a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca])
		tris = next
	# Godot's front faces wind clockwise.
	for i in range(0, tris.size(), 3):
		var tmp := tris[i + 1]
		tris[i + 1] = tris[i + 2]
		tris[i + 2] = tmp
	return [verts, tris]


static func _mid(verts: PackedVector3Array, cache: Dictionary, a: int, b: int) -> int:
	var key := Vector2i(mini(a, b), maxi(a, b))
	if cache.has(key):
		return cache[key]
	verts.append(((verts[a] + verts[b]) * 0.5).normalized())
	cache[key] = verts.size() - 1
	return verts.size() - 1
