class_name Foliage
extends RefCounted
## Procedural stand-ins for the foliage assets: a linden leaf texture (heart-shaped, serrated,
## with veins), a leaf-cluster mesh of crossed cards, and grass blades. A CC0 leaf atlas can
## replace the texture later without changing anything else.


## A single linden leaf on transparent ground, stem at the bottom centre. Seeded, so it never changes.
static func leaf_texture(size: int = 128) -> ImageTexture:
	var img := Image.create(size, size, true, Image.FORMAT_RGBA8)
	var rng := RandomNumberGenerator.new()
	rng.seed = 1234
	for y in range(size):
		for x in range(size):
			# Leaf space: u across (-1..1), v from base (0) to tip (1).
			var u := (float(x) + 0.5) / size * 2.0 - 1.0
			var v := 1.0 - (float(y) + 0.5) / size
			var inside := _leaf_inside(u, v)
			if inside <= 0.0:
				# Stem.
				if absf(u) < 0.03 and v < 0.1:
					img.set_pixel(x, y, Color(0.3, 0.38, 0.15, 1.0))
				else:
					img.set_pixel(x, y, Color(0.25, 0.4, 0.12, 0.0))
				continue
			var vein := 0.0
			vein = maxf(vein, 1.0 - absf(u) * 40.0)  # midrib
			for k in range(1, 6):
				var vy := 0.12 + k * 0.14
				var side_vein := absf(v - vy - absf(u) * 0.55)
				vein = maxf(vein, (1.0 - side_vein * 45.0) * (1.0 - absf(u)))
			vein = clampf(vein, 0.0, 1.0)
			var shade := 0.85 + 0.15 * sin(u * 3.0 + v * 5.0)
			var edge := clampf(inside * 12.0, 0.0, 1.0)
			var col := Color(0.17, 0.3, 0.09).lerp(Color(0.27, 0.42, 0.13), v * 0.6) * shade
			col = col.lerp(Color(0.55, 0.66, 0.3), vein * 0.5)
			col = col.lerp(Color(0.2, 0.33, 0.1), 1.0 - edge)
			col.a = 1.0
			img.set_pixel(x, y, col)
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


## > 0 inside the leaf: a heart-shaped linden leaf with a pointed tip and a serrated edge.
static func _leaf_inside(u: float, v: float) -> float:
	if v < 0.06 or v > 0.98:
		return -1.0
	var t := (v - 0.06) / 0.92
	# Width: two rounded lobes near the base (the heart), tapering to a drawn-out tip.
	var w := 0.95 * pow(sin(PI * pow(t, 0.62)), 0.9) * (1.0 - 0.25 * t)
	w += 0.12 * exp(-pow((t - 0.12) / 0.08, 2.0))
	# The notch where the stem meets the blade.
	if t < 0.1 and absf(u) < (0.1 - t) * 1.5:
		return -1.0
	var serration := 0.025 * absf(sin(t * 60.0))
	return w - serration - absf(u)


## One leaf cluster: `count` cards around the origin, each with its stem pointing inward.
## UV.y runs from stem (0) to tip (1) so the shader can flutter the tips.
static func cluster_mesh(count: int = 7, card: float = 1.0) -> ArrayMesh:
	var rng := RandomNumberGenerator.new()
	rng.seed = 77
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	for i in range(count):
		# A direction on the upper hemisphere mostly, a few hanging down.
		var yaw := TAU * (float(i) + rng.randf() * 0.6) / count
		var pitch := rng.randf_range(-0.5, 1.0)
		var out := Vector3(cos(yaw) * cos(pitch), sin(pitch), sin(yaw) * cos(pitch)).normalized()
		var side := out.cross(Vector3.UP)
		if side.length_squared() < 1e-4:
			side = Vector3.RIGHT
		side = side.normalized().rotated(out, rng.randf_range(-0.6, 0.6))
		var s := card * rng.randf_range(0.75, 1.15)
		var base := out * 0.12 * card
		var tip := base + out * s
		var hw := side * s * 0.5
		var n := side.cross(out).normalized()
		var b := verts.size()
		verts.append_array([base - hw, base + hw, tip + hw, tip - hw])
		for _k in range(4):
			normals.append(n)
		uvs.append_array([Vector2(0, 1), Vector2(1, 1), Vector2(1, 0), Vector2(0, 0)])
		indices.append_array([b, b + 1, b + 2, b, b + 2, b + 3])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


## One grass blade, 1 m tall and 1 m wide at the root (instances scale it), slightly curved.
## UV.y is 0 at the root and 1 at the tip.
static func blade_mesh() -> ArrayMesh:
	var verts := PackedVector3Array([
		Vector3(-0.5, 0, 0), Vector3(0.5, 0, 0),
		Vector3(-0.35, 0.45, 0.03), Vector3(0.35, 0.45, 0.03),
		Vector3(-0.15, 0.8, 0.09), Vector3(0.15, 0.8, 0.09),
		Vector3(0.0, 1.0, 0.16)])
	var uvs := PackedVector2Array([
		Vector2(0, 0), Vector2(1, 0), Vector2(0.15, 0.45), Vector2(0.85, 0.45),
		Vector2(0.35, 0.8), Vector2(0.65, 0.8), Vector2(0.5, 1.0)])
	var normals := PackedVector3Array()
	for _i in range(verts.size()):
		normals.append(Vector3(0, 0.3, -1).normalized())
	var indices := PackedInt32Array([0, 1, 3, 0, 3, 2, 2, 3, 5, 2, 5, 4, 4, 5, 6])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


## Grass instance transforms around the trunk: dense near the tree, thinning outward.
## Seeded; skips the trunk itself.
static func grass_transforms(count: int, radius: float, seed: int) -> Array[Transform3D]:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "grass"])
	var out: Array[Transform3D] = []
	for _i in range(count):
		var d := radius * pow(rng.randf(), 0.75)
		if d < 0.35:
			continue
		var a := rng.randf() * TAU
		var h := rng.randf_range(0.18, 0.45)
		var w := rng.randf_range(0.03, 0.055)
		var basis := Basis(Vector3.UP, rng.randf() * TAU) * Basis(Vector3.RIGHT, rng.randf_range(-0.2, 0.2))
		basis = basis.scaled(Vector3(w, h, w))
		out.append(Transform3D(basis, Vector3(cos(a) * d, 0.0, sin(a) * d)))
	return out
