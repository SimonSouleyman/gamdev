class_name GrassLook
extends RefCounted
## Swaps the meadow's grass clumps (not the herbs and flowers) to the painted blade atlas and
## gives them calmer colours and the meadow's patches (MeadowMap): lush and dry stretches, tall
## grass, a drift of colour and trodden paths; the ground shader draws the same patches.


static func apply(view: TreeView) -> void:
	var mmi: MultiMeshInstance3D = view._grass
	var compat := RenderingServer.get_current_rendering_method() == "gl_compatibility"
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/grass/meadow_grass.gdshader")
	mat.set_shader_parameter("clump_texture", load("res://lookdev/grass/grass_atlas.png"))
	# Calmer greens (0.6.1 review: one saturated carpet); the phone renderer shows them stronger.
	mat.set_shader_parameter("saturation", 0.74 if compat else 0.84)
	mat.set_shader_parameter("far_lift", 0.55 if compat else 0.2)
	mmi.material_override = mat
	# Lusher and drier stretches, tall patches and trodden paths, shared with the ground.
	var edge: float = maxf(view._clearing, Scenery.CLEARING_RADIUS)
	var map := MeadowMap.build(view.state.seed, edge)
	view.set_meta("meadow_map", map)
	_apply_ground(view, map, edge, compat)
	var mm := mmi.multimesh
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([view.state.seed, "lookdev grass"])
	for i in range(mm.instance_count):
		var xf := mm.get_instance_transform(i)
		var p := xf.origin
		var m := map.sample(p.x, p.z)
		# Meadow grass stands 30 to 60 cm, in tall patches up to 80; lush ground grows it higher,
		# and on a path it is trodden down: short and lying over.
		var tall := lerpf(0.78, 1.6, m.g) * lerpf(0.88, 1.12, m.r)
		var trod := smoothstep(0.15, 0.8, m.b)
		var basis := xf.basis.scaled(Vector3(0.9, 1.6 * tall * lerpf(1.0, 0.35, trod), 0.9))
		if trod > 0.05:
			var axis := Vector3(rng.randf_range(-1, 1), 0.0, rng.randf_range(-1, 1)).normalized()
			basis = Basis(axis, trod * rng.randf_range(0.6, 1.1)) * basis
		xf.basis = basis
		mm.set_instance_transform(i, xf)
		mm.set_instance_color(i, grass_colour(m, rng.randf_range(0.55, 0.78), Vector2(p.x, p.z).length(), edge, compat))
	_trample(view._herbs, map, rng)


## A clump's colour from the meadow map: lush stretches deeper and a little bluer, dry ones
## olive and straw, a slow drift of warmth across the meadow, trodden grass paler; darker toward
## the forest.
static func grass_colour(m: Color, v: float, d: float, edge: float, compat: bool) -> Color:
	var lush := Color(0.84 * v, 0.95 * v, 0.9 * v)
	var dry := Color(1.18 * v, 1.07 * v, 0.66 * v)
	var col := dry.lerp(lush, smoothstep(0.2, 0.8, m.r))
	col *= Color(1.04, 1.0, 0.93).lerp(Color(0.96, 1.0, 1.05), m.a)
	col = col.lerp(Color(1.12 * v, 1.08 * v, 0.84 * v), smoothstep(0.2, 0.9, m.b) * 0.6)
	# The phone renderer shows greens brighter and more saturated: calmer there.
	if compat:
		col = Color(col.r * 0.85, col.g * 0.78, col.b * 0.8)
	if d > edge - 2.0:
		col = col.darkened(clampf((d - edge + 2.0) / 5.0, 0.0, 0.5))
	return col


## The meadow floor draws the same patches and paths, and darkens at this clearing's edge.
static func _apply_ground(view: TreeView, map: MeadowMap, edge: float, compat: bool) -> void:
	var g: ShaderMaterial = view._ground_mat
	g.set_shader_parameter("meadow_map", map.texture)
	g.set_shader_parameter("meadow_half", MeadowMap.HALF)
	g.set_shader_parameter("use_meadow", true)
	g.set_shader_parameter("edge", edge)
	g.set_shader_parameter("saturation", 0.72 if compat else 0.85)


## The herb clumps: a little lighter (their dark low leaves read as black dots in the far
## meadow), and trodden down on a path too.
static func _trample(mmi: MultiMeshInstance3D, map: MeadowMap, rng: RandomNumberGenerator) -> void:
	var mm := mmi.multimesh
	for i in range(mm.instance_count):
		var xf := mm.get_instance_transform(i)
		var c := mm.get_instance_color(i)
		mm.set_instance_color(i, Color(c.r * 1.22, c.g * 1.18, c.b * 1.1))
		var trod := smoothstep(0.15, 0.8, map.sample(xf.origin.x, xf.origin.z).b)
		if trod <= 0.05:
			continue
		var axis := Vector3(rng.randf_range(-1, 1), 0.0, rng.randf_range(-1, 1)).normalized()
		xf.basis = Basis(axis, trod * 0.8) * xf.basis.scaled(Vector3(1.0, lerpf(1.0, 0.4, trod), 1.0))
		mm.set_instance_transform(i, xf)


## A second meadow layer for variety (Simon, play test 4): short fine grass, blue-green sedge
## tufts, clover patches and drifts of meadow flowers, each kind a cell of meadow_atlas2.
## Clover and flowers grow in patches; the cell is passed to the shader in the colour's alpha.
static func apply_meadow2(view: TreeView) -> void:
	var mmi: MultiMeshInstance3D = view._meadow2
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/grass/meadow_grass.gdshader")
	mat.set_shader_parameter("clump_texture", load("res://lookdev/grass/meadow_atlas2.png"))
	mat.set_shader_parameter("cell_from_alpha", true)
	mat.set_shader_parameter("saturation", 0.8 if RenderingServer.get_current_rendering_method() == "gl_compatibility" else 0.88)
	mmi.material_override = mat
	var map: MeadowMap = view.get_meta("meadow_map", null)
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
		if d < Meadow.BARE_RADIUS + 0.1 or Vector2(p.x - Shed.origin.x, p.z - Shed.origin.z).length() < 2.6:
			p = Vector3(cos(a) * (d + 3.0), 0.0, sin(a) * (d + 3.0))
		var m := map.sample(p.x, p.z) if map != null else Color(0.5, 0.0, 0.0, 0.5)
		var clover := sin(p.x * 0.31 + 2.0) * cos(p.z * 0.27 - 1.0)
		var bloom := sin(p.x * 0.19 - 0.7) * cos(p.z * 0.23 + 1.9)
		var cell := 0
		if clover > 0.3 and rng.randf() < 0.85:
			cell = 2
		elif bloom > 0.3 and rng.randf() < 0.75:
			cell = 3
		elif rng.randf() < 0.18:
			cell = 1
		# Short fine grass and clover hold on along a path; flowers and sedge do not.
		var trod := smoothstep(0.15, 0.8, m.b)
		if trod > 0.4 and (cell == 1 or cell == 3):
			cell = 0 if cell == 1 else 2
		var s: Vector4 = sizes[cell]
		var w := rng.randf_range(s.x, s.y)
		var h := rng.randf_range(s.z, s.w) * lerpf(1.0, 0.5, trod) * lerpf(0.85, 1.2, m.g)
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(w, h, w))
		mm.set_instance_transform(i, Transform3D(basis, Terrain.at(p) + Vector3(0, -0.02, 0)))
		var v := rng.randf_range(0.7, 0.95) if cell >= 2 else rng.randf_range(0.6, 0.85)
		var col := Color(v, v, v)
		if cell < 2:
			# The fine grass follows the meadow's lush and dry stretches (flowers keep their colour).
			col = grass_colour(m, v, 0.0, 99.0, false)
		col.a = (float(cell) + 0.5) / 4.0
		mm.set_instance_color(i, col)
