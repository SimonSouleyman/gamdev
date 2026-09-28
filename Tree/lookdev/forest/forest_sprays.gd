class_name ForestSprays
extends RefCounted
## Look test for the forest wall and the bushes: their big single-leaf clusters are rebuilt as
## leaf sprays (the same atlas and shader as the hero crown), merged into one mesh per variant
## with the crown normals and occlusion baked in. The spruce keeps its needle cards.
## In the real game this belongs in Scenery._add_leaf_surface / _build_bushes; here it
## rewrites the built scenery meshes after the fact.


static func apply(scenery: Node3D) -> void:
	var template := CrownSprays.material()
	template.set_shader_parameter("baked", true)
	# Darker, cooler and without the hero tree's glow, so the player's tree stands out.
	template.set_shader_parameter("tint_mul", Color(0.78, 0.84, 0.82))
	template.set_shader_parameter("translucency", Color(0.25, 0.3, 0.06))
	template.set_shader_parameter("near_fade", 9.0)
	var done := {}
	for c in scenery.get_children():
		var mmi := c as MultiMeshInstance3D
		if mmi == null or mmi.multimesh == null or mmi.multimesh.mesh == null:
			continue
		var mesh := mmi.multimesh.mesh as ArrayMesh
		if mesh == null:
			continue
		var leaf_surface := _leaf_surface(mesh)
		if leaf_surface < 0:
			continue
		if not done.has(mesh):
			var cards := Budgets.FOREST_SPRAY_CARDS if mesh.get_surface_count() == 2 else maxi(3, Budgets.FOREST_SPRAY_CARDS - 2)
			done[mesh] = _respray(mesh, leaf_surface, cards, template)
		if done[mesh] != null:
			mmi.multimesh.mesh = done[mesh]


static func _leaf_surface(mesh: ArrayMesh) -> int:
	for s in range(mesh.get_surface_count()):
		var mat := mesh.surface_get_material(s) as ShaderMaterial
		if mat != null and mat.shader != null and mat.shader.resource_path.ends_with("tree/leaf.gdshader"):
			return s
	return -1


## A new mesh: the old surfaces, with the leaf surface rebuilt as sprays. Null for the spruce.
static func _respray(mesh: ArrayMesh, leaf_surface: int, cards: int, template: ShaderMaterial) -> Variant:
	var arrays := mesh.surface_get_arrays(leaf_surface)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var colors: PackedColorArray = arrays[Mesh.ARRAY_COLOR]
	if verts.is_empty():
		return null
	# The spruce's needle tint is as blue as it is red; broadleaf tints are warmer.
	if not colors.is_empty() and colors[0].b > colors[0].r * 0.9:
		return null
	var per := cards * 4
	var bounds := AABB(verts[0], Vector3.ZERO)
	for v in verts:
		bounds = bounds.expand(v)
	var centre := bounds.get_center()
	var radii := (bounds.size * 0.5).max(Vector3.ONE * 0.2)
	var rng := RandomNumberGenerator.new()
	rng.seed = verts.size()
	var nv := PackedVector3Array()
	var nn := PackedVector3Array()
	var nu := PackedVector2Array()
	var nu2 := PackedVector2Array()
	var nc := PackedColorArray()
	var ni := PackedInt32Array()
	for k in range(0, verts.size() - per + 1, per):
		var c := Vector3.ZERO
		for i in range(per):
			c += verts[k + i]
		c /= per
		var r := 0.0
		for i in range(per):
			r = maxf(r, c.distance_to(verts[k + i]))
		var col := colors[k] if not colors.is_empty() else Color.WHITE
		for _s in range(5):
			var at := c + Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.5, 0.5), rng.randf_range(-1, 1)) * r * 0.35
			var rel := (at - centre) / radii
			var out := rel.normalized()
			var face := (out * 0.8 + Vector3.UP * 0.35 + Vector3(rng.randf_range(-0.35, 0.35), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.35, 0.35))).normalized()
			var dir := face.cross(Vector3.RIGHT if absf(face.x) < 0.9 else Vector3.FORWARD).normalized().rotated(face, rng.randf() * TAU)
			var side := dir.cross(face).normalized()
			var s := r * rng.randf_range(1.6, 2.1)
			var n := face.lerp(out, 0.6).normalized()
			var occlusion := clampf(clampf(1.0 - rel.length(), 0.0, 1.0) * 0.9 + clampf(-rel.y, 0.0, 1.0) * 0.35, 0.0, 0.85)
			var cell := rng.randi() % 4
			var off := Vector2(cell % 2, cell / 2) * 0.5
			var b := nv.size()
			var hw := side * s * 0.5
			nv.append_array([at - hw, at + hw, at + hw + dir * s, at - hw + dir * s])
			nu.append_array([off + Vector2(0, 0.5), off + Vector2(0.5, 0.5), off + Vector2(0.5, 0), off])
			nu2.append_array([Vector2(0, 0), Vector2(0, 0), Vector2(1, 0), Vector2(1, 0)])
			for _q in range(4):
				nn.append(n)
				nc.append(Color(col.r, col.g, col.b, occlusion))
			ni.append_array([b, b + 1, b + 2, b, b + 2, b + 3])
	var out_mesh := ArrayMesh.new()
	for sidx in range(mesh.get_surface_count()):
		if sidx == leaf_surface:
			var a := []
			a.resize(Mesh.ARRAY_MAX)
			a[Mesh.ARRAY_VERTEX] = nv
			a[Mesh.ARRAY_NORMAL] = nn
			a[Mesh.ARRAY_TEX_UV] = nu
			a[Mesh.ARRAY_TEX_UV2] = nu2
			a[Mesh.ARRAY_COLOR] = nc
			a[Mesh.ARRAY_INDEX] = ni
			out_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, a)
			out_mesh.surface_set_material(out_mesh.get_surface_count() - 1, template)
		else:
			out_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, mesh.surface_get_arrays(sidx))
			out_mesh.surface_set_material(out_mesh.get_surface_count() - 1, mesh.surface_get_material(sidx))
	return out_mesh
