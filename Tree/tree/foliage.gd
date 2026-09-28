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
## Where each of the six leaves of the LeafSet004 atlas sits (u0, v0, u1, v1), stems at the bottom.
const ATLAS_CELLS: Array[Rect2] = [
	Rect2(0.18, 0.16, 0.19, 0.34), Rect2(0.37, 0.15, 0.23, 0.35), Rect2(0.6, 0.17, 0.2, 0.33),
	Rect2(0.19, 0.5, 0.18, 0.34), Rect2(0.4, 0.5, 0.2, 0.34), Rect2(0.62, 0.5, 0.19, 0.33)]


static func cluster_mesh(count: int = 7, card: float = 1.0, atlas: bool = false) -> ArrayMesh:
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
		if atlas:
			var r: Rect2 = ATLAS_CELLS[(i + rng.randi()) % ATLAS_CELLS.size()]
			uvs.append_array([Vector2(r.position.x, r.end.y), r.end, Vector2(r.end.x, r.position.y), r.position])
		else:
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
		var h := rng.randf_range(0.09, 0.24)
		var w := rng.randf_range(0.012, 0.022)
		var basis := Basis(Vector3.UP, rng.randf() * TAU) * Basis(Vector3.RIGHT, rng.randf_range(-0.2, 0.2))
		basis = basis.scaled(Vector3(w, h, w))
		out.append(Transform3D(basis, Vector3(cos(a) * d, 0.0, sin(a) * d)))
	return out


## A painted clump of meadow grass (or, with `herbs`, low leaves and wildflowers) for the
## crossed-card meadow: many thin curved blades in varied greens, a few dry ones, soft tips.
## Seeded; the root of the clump is at the bottom centre of the image.
static func clump_texture(herbs: bool, seed: int, size: int = 256) -> ImageTexture:
	# Painted once, then kept in the user folder.
	var cache := "user://cache/clump_%s_%d_%d_v2.png" % ["herbs" if herbs else "grass", seed, size]
	if FileAccess.file_exists(cache):
		var cached := Image.load_from_file(ProjectSettings.globalize_path(cache))
		if cached != null and not cached.is_empty():
			cached.generate_mipmaps()
			return ImageTexture.create_from_image(cached)
	var img := Image.create(size, size, true, Image.FORMAT_RGBA8)
	img.fill(Color(0.2, 0.3, 0.1, 0.0))
	var rng := RandomNumberGenerator.new()
	rng.seed = seed
	var blades := 70 if not herbs else 40
	for _b in range(blades):
		var base := Vector2(size * rng.randf_range(0.18, 0.82), size - 1.0)
		var h := size * rng.randf_range(0.45, 0.97) * (0.6 if herbs else 1.0)
		var lean := rng.randf_range(-0.35, 0.35)
		var bend := rng.randf_range(-0.25, 0.25)
		var dry := rng.randf() < 0.12 and not herbs
		var base_col := Color(0.18, 0.28, 0.08)
		var tip_col := Color(0.62, 0.72, 0.3).lerp(Color(0.62, 0.62, 0.3), 0.6 if dry else rng.randf() * 0.2)
		tip_col = tip_col * rng.randf_range(0.8, 1.1)
		var width := rng.randf_range(2.2, 3.6)
		var steps := int(h)
		for s in range(steps):
			var t := float(s) / steps
			var p := base + Vector2((lean * t + bend * t * t) * h, -t * h)
			var r := width * (1.0 - t) + 0.4
			var col := base_col.lerp(tip_col, pow(t, 0.7))
			_dab(img, p, r, col)
	if herbs:
		# Low leaves and a few flower heads: white, yellow, violet.
		var flower_cols: Array[Color] = [Color(0.96, 0.95, 0.9), Color(1.0, 0.86, 0.25), Color(0.66, 0.5, 0.86), Color(0.95, 0.55, 0.62)]
		for _l in range(26):
			var p := Vector2(size * rng.randf_range(0.2, 0.8), size * rng.randf_range(0.55, 0.95))
			var leaf_col := Color(0.2, 0.36, 0.1) * rng.randf_range(0.8, 1.2)
			for k in range(6):
				_dab(img, p + Vector2(k * 1.5 - 4.0, -k * 1.2), 4.0 - k * 0.4, leaf_col)
		var fc := flower_cols[rng.randi() % flower_cols.size()]
		for _f in range(rng.randi_range(3, 7)):
			var stem := Vector2(size * rng.randf_range(0.25, 0.75), size - 1.0)
			var top := stem + Vector2(rng.randf_range(-20, 20), -size * rng.randf_range(0.45, 0.8))
			for s in range(40):
				_dab(img, stem.lerp(top, s / 40.0), 1.2, Color(0.2, 0.32, 0.1))
			for k in range(5):
				var a := TAU * k / 5.0
				_dab(img, top + Vector2(cos(a), sin(a)) * 5.0, 3.6, fc)
			_dab(img, top, 2.5, Color(0.95, 0.8, 0.2))
	DirAccess.make_dir_recursive_absolute("user://cache")
	img.save_png(cache)
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


static func _dab(img: Image, p: Vector2, r: float, col: Color) -> void:
	var ri := int(ceil(r))
	for dy in range(-ri, ri + 1):
		for dx in range(-ri, ri + 1):
			var x := int(p.x) + dx
			var y := int(p.y) + dy
			if x < 0 or y < 0 or x >= img.get_width() or y >= img.get_height():
				continue
			var d := Vector2(dx, dy).length()
			if d <= r:
				var a := clampf(r - d + 0.5, 0.0, 1.0)
				var under := img.get_pixel(x, y)
				img.set_pixel(x, y, Color(under.lerp(col, a), maxf(under.a, a)))


## Three crossed cards, 1 m wide and 1 m tall, standing on the origin. UV.y = 1 at the ground.
static func clump_mesh() -> ArrayMesh:
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	for i in range(3):
		var a := PI * i / 3.0
		var side := Vector3(cos(a), 0, sin(a)) * 0.5
		var n := Vector3(-sin(a), 0, cos(a))
		var b := verts.size()
		verts.append_array([-side, side, side + Vector3.UP, -side + Vector3.UP])
		uvs.append_array([Vector2(0, 1), Vector2(1, 1), Vector2(1, 0), Vector2(0, 0)])
		for _k in range(4):
			normals.append(n)
		indices.append_array([b, b + 1, b + 2, b, b + 2, b + 3])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	# White vertex colours: the phone renderer multiplies the per-clump colour by the vertex colour
	# and reads a missing one as black (black grass clumps on the phone, 0.6 look review).
	var white := PackedColorArray()
	white.resize(verts.size())
	white.fill(Color.WHITE)
	arrays[Mesh.ARRAY_COLOR] = white
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh
