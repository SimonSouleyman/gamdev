class_name CrownSprays
extends RefCounted
## Look-test crown: instead of one cluster of big single leaves per twig, every living twig
## carries several leaf sprays (a small twig with a dozen leaves each, from the painted atlas
## leaf_spray_color.png). The crown reads as a mass of small leaves, like a real linden, while
## staying at a few thousand quads. The shader bends each card's normal towards the crown
## surface, so the whole crown shades like one soft volume with a bright sunny side.

const COLOR := "res://lookdev/crown/leaf_spray_color.png"
const NORMAL := "res://lookdev/crown/leaf_spray_normal.png"


## One card: 1 m wide, 1 m long, stem end at the origin, growing along +Y. UV.y = 1 at the stem.
static func card_mesh() -> ArrayMesh:
	var verts := PackedVector3Array([Vector3(-0.5, 0, 0), Vector3(0.5, 0, 0), Vector3(0.5, 1, 0), Vector3(-0.5, 1, 0)])
	var normals := PackedVector3Array([Vector3.BACK, Vector3.BACK, Vector3.BACK, Vector3.BACK])
	var uvs := PackedVector2Array([Vector2(0, 1), Vector2(1, 1), Vector2(1, 0), Vector2(0, 0)])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 1, 2, 0, 2, 3])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


static func material() -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/crown/leaf_spray.gdshader")
	mat.set_shader_parameter("spray_color", load(COLOR))
	mat.set_shader_parameter("spray_normal", load(NORMAL))
	mat.set_shader_parameter("cheap", Budgets.PHONE)
	return mat


## Fills `mm` (TRANSFORM_3D, colours and custom data on) with sprays for the tree in `sim`.
## Seeded; returns the crown bounds the shader needs.
static func populate(mm: MultiMesh, sim: GrowthSim, seed: int) -> AABB:
	var g := sim.graph
	var height := sim.height()
	var bare_below := height * 0.3 if height > 4.0 else 0.0
	var spots := PackedInt32Array()
	var bounds := AABB()
	var first := true
	for id in range(2, g.size()):
		if g.radii[id] < 0.06 and not g.get_flag(id, "dead", false) and g.positions[id].y >= bare_below:
			spots.append(id)
			if first:
				bounds = AABB(g.positions[id], Vector3.ZERO)
				first = false
			else:
				bounds = bounds.expand(g.positions[id])
	var centre := bounds.get_center()
	var radii := (bounds.size * 0.5).max(Vector3.ONE * 0.3)
	# A small sapling has few twigs: few sprays. A grown tree has a budget-capped number of
	# twigs but a huge crown, so each twig stands for more sprays, spread around it.
	var per_twig := 2 + int(height * 0.2)
	var spread := 0.1 + height * 0.016
	var size := 0.3 + height * 0.03
	# A phone draws fewer, larger sprays on a big tree (each spray is overdraw it pays for).
	if Budgets.PHONE and per_twig > 4:
		size *= sqrt(float(per_twig) / 4.0)
		per_twig = 4
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "crown sprays"])
	mm.instance_count = spots.size() * per_twig
	var i := 0
	for id in spots:
		var p := g.positions[id]
		var parent := g.parents[id]
		var along := (p - g.positions[parent]).normalized() if parent >= 0 else Vector3.UP
		var out := ((p - centre) / radii).normalized()
		for _k in range(per_twig):
			var at := p + Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.6, 0.6), rng.randf_range(-1, 1)) * spread
			# Sprays lie in the crown's skin, their leaves facing out and up to the light (as real
			# leaves do): seen from outside they show their faces, from below they glow through.
			var face := (out * 0.8 + Vector3.UP * 0.35 + Vector3(rng.randf_range(-0.35, 0.35), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.35, 0.35))).normalized()
			var dir := (along - face * along.dot(face))
			if dir.length_squared() < 0.01:
				dir = face.cross(Vector3.RIGHT)
			dir = dir.normalized().rotated(face, rng.randf_range(-1.2, 1.2))
			var side := dir.cross(face).normalized()
			var s := size * rng.randf_range(0.75, 1.25)
			var basis := Basis(side, dir, face).scaled(Vector3(s, s, s))
			mm.set_instance_transform(i, Transform3D(basis, at))
			# Leaves deep inside the crown and on its underside get little sky: ambient occlusion
			# in the alpha channel. Colour varies a little from spray to spray.
			var rel := (at - centre) / radii
			var depth := clampf(1.0 - rel.length(), 0.0, 1.0)
			var under := clampf(-rel.y, 0.0, 1.0)
			var occlusion := clampf(depth * 0.9 + under * 0.35, 0.0, 0.85)
			var tint := rng.randf_range(0.88, 1.08)
			mm.set_instance_color(i, Color(tint * rng.randf_range(0.94, 1.05), tint, tint * rng.randf_range(0.85, 1.0), occlusion))
			mm.set_instance_custom_data(i, Color(float(rng.randi() % 4), rng.randf(), 0, 0))
			i += 1
	return AABB(centre - radii, radii * 2.0)
