class_name GrassLook
extends RefCounted
## Swaps the meadow's grass clumps (not the herbs and flowers) to the painted blade atlas and
## gives them calmer colours: less yellow, small variations of green, a few drier patches.


static func apply(view: TreeView) -> void:
	var mmi: MultiMeshInstance3D = view._grass
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/grass/meadow_grass.gdshader")
	mat.set_shader_parameter("clump_texture", load("res://lookdev/grass/grass_atlas.png"))
	mmi.material_override = mat
	var mm := mmi.multimesh
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([view.state.seed, "lookdev grass"])
	for i in range(mm.instance_count):
		var xf := mm.get_instance_transform(i)
		var p := xf.origin
		# Taller, a little narrower: meadow grass stands 30 to 60 cm.
		xf.basis = xf.basis.scaled(Vector3(0.9, 1.6, 0.9))
		mm.set_instance_transform(i, xf)
		var v := rng.randf_range(0.55, 0.78)
		var dry := clampf(0.5 + 0.5 * sin(p.x * 0.23 + 1.3) * cos(p.z * 0.19 - 0.4), 0.0, 1.0)
		var col := Color(v, v, v).lerp(Color(1.08 * v, 1.02 * v, 0.8 * v), dry * 0.5)
		var d := Vector2(p.x, p.z).length()
		if d > 16.0:
			col = col.darkened(clampf((d - 16.0) / 5.0, 0.0, 0.5))
		mm.set_instance_color(i, col)
