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


## A second meadow layer for variety (Simon, play test 4): short fine grass, blue-green sedge
## tufts, clover patches and drifts of meadow flowers, each kind a cell of meadow_atlas2.
## Clover and flowers grow in patches; the cell is passed to the shader in the colour's alpha.
static func apply_meadow2(view: TreeView) -> void:
	var mmi: MultiMeshInstance3D = view._meadow2
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/grass/meadow_grass.gdshader")
	mat.set_shader_parameter("clump_texture", load("res://lookdev/grass/meadow_atlas2.png"))
	mat.set_shader_parameter("cell_from_alpha", true)
	mmi.material_override = mat
	var mm := mmi.multimesh
	var count := Budgets.MEADOW_VARIETY_CLUMPS
	mm.instance_count = count
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([view.state.seed, "meadow variety"])
	var radius: float = maxf(view._clearing, Scenery.CLEARING_RADIUS) + 1.0
	# Size of each kind: width range, height range.
	var sizes := [Vector4(0.5, 0.8, 0.28, 0.45), Vector4(0.5, 0.75, 0.7, 1.05), Vector4(0.45, 0.7, 0.22, 0.32), Vector4(0.5, 0.75, 0.45, 0.7)]
	for i in range(count):
		var d := radius * pow(rng.randf(), 0.6)
		var a := rng.randf() * TAU
		var p := Vector3(cos(a) * d, 0.0, sin(a) * d)
		if d < 0.6 or Vector2(p.x - Shed.origin.x, p.z - Shed.origin.z).length() < 2.6:
			p = Vector3(cos(a) * (d + 3.0), 0.0, sin(a) * (d + 3.0))
		var clover := sin(p.x * 0.31 + 2.0) * cos(p.z * 0.27 - 1.0)
		var bloom := sin(p.x * 0.19 - 0.7) * cos(p.z * 0.23 + 1.9)
		var cell := 0
		if clover > 0.3 and rng.randf() < 0.85:
			cell = 2
		elif bloom > 0.3 and rng.randf() < 0.75:
			cell = 3
		elif rng.randf() < 0.18:
			cell = 1
		var s: Vector4 = sizes[cell]
		var w := rng.randf_range(s.x, s.y)
		var h := rng.randf_range(s.z, s.w)
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(w, h, w))
		mm.set_instance_transform(i, Transform3D(basis, Terrain.at(p) + Vector3(0, -0.02, 0)))
		var v := rng.randf_range(0.7, 0.95) if cell >= 2 else rng.randf_range(0.6, 0.85)
		mm.set_instance_color(i, Color(v, v, v, (float(cell) + 0.5) / 4.0))
