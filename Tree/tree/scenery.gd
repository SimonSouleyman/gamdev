class_name Scenery
extends Node3D
## The world around the tree (design doc: "a living, cohesive world"): other trees in the
## distance, grown by the same algorithm as the hero tree; drifting clouds; wildflowers in the
## grass; butterflies around the crown, birds crossing the sky, pollen in the sunlight and
## fireflies at dusk. All mood: nothing here touches the simulation. Seeded like everything else.

const DISTANT_TREES := 34
const TREE_VARIANTS := 4
const CLOUDS := 16
const FLOWERS := 1800
const BUTTERFLIES := 6
const BIRDS := 5

var _rng := RandomNumberGenerator.new()
var _clouds: Array[MeshInstance3D] = []
var _cloud_mats: Array[ShaderMaterial] = []
var _butterflies: MultiMeshInstance3D
var _birds: MultiMeshInstance3D
var _pollen: GPUParticles3D
var _fireflies: GPUParticles3D
var _time: float = 0.0
var _bird_t: float = -1.0
var _bird_from: Vector3
var _bird_to: Vector3
var _bird_wait: float = 8.0
var _butterfly_params: Array = []


func build(seed: int, bark: Material, leaf: Material, noise: Texture2D) -> void:
	for c in get_children():
		c.queue_free()
	_clouds.clear()
	_cloud_mats.clear()
	_rng.seed = hash([seed, "scenery"])
	_build_distant_trees(seed, bark, leaf)
	_build_clouds(noise)
	_build_flowers()
	_build_butterflies()
	_build_birds()
	_pollen = _motes(Color(1.0, 0.95, 0.75, 0.9), 60, Vector3(5, 3, 5), Vector3(0, 3, 0), 0.025)
	_fireflies = _motes(Color(0.85, 1.0, 0.45, 1.0), 40, Vector3(9, 0.8, 9), Vector3(0, 0.6, 0), 0.04)


# --- distant trees -------------------------------------------------------------

## Grows a small tree with the real growth model (fed and in noon light), then bakes wood and
## leaf cards into one mesh with two surfaces. Distant trees are therefore the same species,
## grown the same way, as the player's tree.
func _grow_variant(seed: int, variant: int, bark: Material, leaf: Material) -> ArrayMesh:
	var sim := GrowthSim.new(hash([seed, "background tree", variant]))
	sim.graph.max_nodes = 420 + variant * 90
	# Grown quickly with small steps (big steps would make bushes): only the shape matters here.
	sim.max_growth_per_second = 6.0
	sim.markers_per_second = 30.0
	for _step in range(1500):
		for k in range(4):
			sim.resources.stock[k] = 30.0
		sim.clock.time_of_day = sim.clock.daylight_fraction * _rng.randf_range(0.3, 0.7)
		sim.tick(0.45)
		if sim.graph.is_full():
			break
	var builder := BranchMeshBuilder.new()
	builder.radius_scale = 2.2
	var wood := builder.build(sim.graph)
	var mesh := ArrayMesh.new()
	if wood.get_surface_count() > 0:
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, wood.surface_get_arrays(0))
		mesh.surface_set_material(0, bark)
	# Leaf cards, baked: one cluster per twig, bigger than on the hero tree (seen from afar).
	var cluster := Foliage.cluster_mesh(5, 1.0).surface_get_arrays(0)
	var cv: PackedVector3Array = cluster[Mesh.ARRAY_VERTEX]
	var cn: PackedVector3Array = cluster[Mesh.ARRAY_NORMAL]
	var cu: PackedVector2Array = cluster[Mesh.ARRAY_TEX_UV]
	var ci: PackedInt32Array = cluster[Mesh.ARRAY_INDEX]
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	var g := sim.graph
	for id in range(2, g.size()):
		if g.radii[id] >= 0.03:
			continue
		var s := _rng.randf_range(0.35, 0.5)
		var xf := Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3.ONE * s), g.positions[id])
		var tint := _rng.randf_range(0.8, 1.1)
		var base := verts.size()
		for i in range(cv.size()):
			verts.append(xf * cv[i])
			normals.append((xf.basis * cn[i]).normalized())
			uvs.append(cu[i])
			colors.append(Color(tint, tint, tint * 0.95))
		for i in ci:
			indices.append(base + i)
	if not indices.is_empty():
		var arrays := []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX] = verts
		arrays[Mesh.ARRAY_NORMAL] = normals
		arrays[Mesh.ARRAY_TEX_UV] = uvs
		arrays[Mesh.ARRAY_COLOR] = colors
		arrays[Mesh.ARRAY_INDEX] = indices
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		mesh.surface_set_material(mesh.get_surface_count() - 1, leaf)
	return mesh


func _build_distant_trees(seed: int, bark: Material, leaf: Material) -> void:
	var variants: Array[ArrayMesh] = []
	for v in range(TREE_VARIANTS):
		variants.append(_grow_variant(seed, v, bark, leaf))
	var per_variant: Array = []
	for _v in range(TREE_VARIANTS):
		per_variant.append([])
	# A few groves and hedgerow lines on the far meadow, some single trees in between.
	var groves := 7
	for gi in range(groves):
		var a := TAU * gi / groves + _rng.randf_range(-0.4, 0.4)
		var d := _rng.randf_range(60.0, 140.0)
		var center := Vector3(cos(a) * d, 0, sin(a) * d)
		for _t in range(DISTANT_TREES / groves):
			var p := center + Vector3(_rng.randf_range(-14, 14), 0, _rng.randf_range(-14, 14))
			(per_variant[_rng.randi() % TREE_VARIANTS] as Array).append(p)
	# A few lone trees nearer by, and a loose tree line along the far horizon.
	for _t in range(8):
		var a := _rng.randf() * TAU
		var d := _rng.randf_range(35.0, 70.0)
		(per_variant[_rng.randi() % TREE_VARIANTS] as Array).append(Vector3(cos(a) * d, 0, sin(a) * d))
	for _t in range(70):
		var a := _rng.randf() * TAU
		var d := _rng.randf_range(170.0, 280.0)
		(per_variant[_rng.randi() % TREE_VARIANTS] as Array).append(Vector3(cos(a) * d, 0, sin(a) * d))
	for v in range(TREE_VARIANTS):
		var spots: Array = per_variant[v]
		if spots.is_empty():
			continue
		var mmi := MultiMeshInstance3D.new()
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = variants[v]
		mm.instance_count = spots.size()
		for i in range(spots.size()):
			var s := _rng.randf_range(0.8, 1.35)
			mm.set_instance_transform(i, Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3.ONE * s), spots[i]))
		mmi.multimesh = mm
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)


# --- sky ------------------------------------------------------------------------

func _build_clouds(noise: Texture2D) -> void:
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	quad.orientation = PlaneMesh.FACE_Y
	for i in range(CLOUDS):
		var m := MeshInstance3D.new()
		m.mesh = quad
		var mat := ShaderMaterial.new()
		mat.shader = preload("res://tree/cloud.gdshader")
		mat.set_shader_parameter("noise", noise)
		mat.set_shader_parameter("seed", _rng.randf() * 10.0)
		mat.set_shader_parameter("coverage", _rng.randf_range(0.42, 0.52))
		m.material_override = mat
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var a := _rng.randf() * TAU
		var d := _rng.randf_range(40.0, 260.0)
		m.position = Vector3(cos(a) * d, _rng.randf_range(70.0, 110.0), sin(a) * d)
		var w := _rng.randf_range(70.0, 140.0)
		m.scale = Vector3(w, 1.0, w * _rng.randf_range(0.45, 0.8))
		m.rotation.y = _rng.randf() * TAU
		add_child(m)
		_clouds.append(m)
		_cloud_mats.append(mat)


# --- meadow life ----------------------------------------------------------------

func _build_flowers() -> void:
	var head := CylinderMesh.new()
	head.top_radius = 0.028
	head.bottom_radius = 0.018
	head.height = 0.012
	head.radial_segments = 6
	head.rings = 1
	var mmi := MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = head
	var palette: Array[Color] = [Color(0.95, 0.95, 0.92), Color(1.0, 0.85, 0.2), Color(0.62, 0.45, 0.85), Color(0.4, 0.55, 0.95), Color(0.95, 0.5, 0.6)]
	var spots: Array[Transform3D] = []
	var colors: Array[Color] = []
	# Flowers grow in little drifts, one colour each.
	for _d in range(60):
		var a := _rng.randf() * TAU
		var r := 1.2 + 22.0 * sqrt(_rng.randf())
		var center := Vector3(cos(a) * r, 0, sin(a) * r)
		var col := palette[_rng.randi() % palette.size()]
		for _f in range(FLOWERS / 60):
			var p := center + Vector3(_rng.randf_range(-1.2, 1.2), _rng.randf_range(0.1, 0.28), _rng.randf_range(-1.2, 1.2))
			spots.append(Transform3D(Basis(Vector3.RIGHT, _rng.randf_range(-0.3, 0.3)).scaled(Vector3.ONE * _rng.randf_range(0.7, 1.3)), p))
			colors.append(col * _rng.randf_range(0.9, 1.05))
	mm.instance_count = spots.size()
	for i in range(spots.size()):
		mm.set_instance_transform(i, spots[i])
		mm.set_instance_color(i, colors[i])
	mmi.multimesh = mm
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.roughness = 0.7
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(mmi)


## Two wing quads meeting at the body (x = 0); the shader flaps them.
func _wings_mesh(span: float, depth: float) -> ArrayMesh:
	var hw := span * 0.5
	var hd := depth * 0.5
	var verts := PackedVector3Array([
		Vector3(-hw, 0, -hd), Vector3(0, 0, -hd), Vector3(0, 0, hd), Vector3(-hw, 0, hd),
		Vector3(0, 0, -hd), Vector3(hw, 0, -hd), Vector3(hw, 0, hd), Vector3(0, 0, hd)])
	var uvs := PackedVector2Array([
		Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1),
		Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)])
	var normals := PackedVector3Array()
	for _i in range(8):
		normals.append(Vector3.UP)
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _critters(count: int, span: float, flap: float) -> MultiMeshInstance3D:
	var mmi := MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = _wings_mesh(span, span * 0.6)
	mm.instance_count = count
	mmi.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://tree/critter.gdshader")
	mat.set_shader_parameter("flap_speed", flap)
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.extra_cull_margin = 200.0
	add_child(mmi)
	return mmi


func _build_butterflies() -> void:
	_butterflies = _critters(BUTTERFLIES, 0.11, 16.0)
	var colors: Array[Color] = [Color(1.0, 0.95, 0.5), Color(0.95, 0.95, 0.92), Color(0.95, 0.5, 0.2), Color(0.45, 0.6, 1.0)]
	_butterfly_params.clear()
	for i in range(BUTTERFLIES):
		_butterflies.multimesh.set_instance_color(i, colors[i % colors.size()])
		_butterflies.multimesh.set_instance_custom_data(i, Color(_rng.randf(), _rng.randf(), 0, 0))
		_butterfly_params.append([_rng.randf_range(1.2, 3.5), _rng.randf_range(0.15, 0.35), _rng.randf() * TAU, _rng.randf_range(0.3, 1.4)])


func _build_birds() -> void:
	_birds = _critters(BIRDS, 0.9, 6.0)
	for i in range(BIRDS):
		_birds.multimesh.set_instance_color(i, Color(0.12, 0.12, 0.14))
		_birds.multimesh.set_instance_custom_data(i, Color(_rng.randf(), _rng.randf(), 0, 0))
	_birds.visible = false


## Small glowing specks drifting in a box: pollen in the sun, fireflies at dusk.
func _motes(color: Color, amount: int, extents: Vector3, center: Vector3, size: float) -> GPUParticles3D:
	var p := GPUParticles3D.new()
	p.amount = amount
	p.lifetime = 6.0
	p.preprocess = 6.0
	p.position = center
	p.visibility_aabb = AABB(-extents * 2.0, extents * 4.0)
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = extents
	pm.gravity = Vector3(0, 0.02, 0)
	pm.initial_velocity_min = 0.02
	pm.initial_velocity_max = 0.12
	pm.direction = Vector3(1, 0.3, 0.2)
	pm.spread = 180.0
	pm.turbulence_enabled = true
	pm.turbulence_noise_strength = 0.4
	pm.turbulence_noise_scale = 3.0
	var fade := Gradient.new()
	fade.set_color(0, Color(color, 0.0))
	fade.set_color(1, Color(color, 0.0))
	fade.add_point(0.25, color)
	fade.add_point(0.75, color)
	var ramp := GradientTexture1D.new()
	ramp.gradient = fade
	pm.color_ramp = ramp
	p.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE * size
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	mat.vertex_color_use_as_albedo = true
	var dot := GradientTexture2D.new()
	dot.fill = GradientTexture2D.FILL_RADIAL
	dot.fill_from = Vector2(0.5, 0.5)
	dot.fill_to = Vector2(1.0, 0.5)
	var g := Gradient.new()
	g.set_color(0, Color(1, 1, 1, 1))
	g.set_color(1, Color(1, 1, 1, 0))
	dot.gradient = g
	dot.width = 32
	dot.height = 32
	mat.albedo_texture = dot
	quad.material = mat
	p.draw_pass_1 = quad
	p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(p)
	return p


# --- every frame ------------------------------------------------------------------

## `day`: the sun is up. `h`: sun height 0..1. `tree_height`: the hero tree, for butterflies and pollen.
func update(delta: float, day: bool, h: float, sun_color: Color, tree_height: float) -> void:
	_time += delta
	# Clouds: drift with the wind, white by day, warm and dim at the low sun.
	var cloud_col := Color(1.0, 0.72, 0.55).lerp(Color(1, 1, 1), clampf(h * 3.0, 0.0, 1.0))
	var bright := 0.35 + 0.75 * clampf(h * 2.5, 0.0, 1.0) if day else 0.3
	for i in range(_clouds.size()):
		var c := _clouds[i]
		c.position.x += delta * 1.2
		if c.position.x > 280.0:
			c.position.x = -280.0
		_cloud_mats[i].set_shader_parameter("sun_color", cloud_col)
		_cloud_mats[i].set_shader_parameter("brightness", bright)
	# Butterflies: only by day, once the tree has leaves, fluttering around the crown.
	_butterflies.visible = day and tree_height > 0.6
	if _butterflies.visible:
		var mm := _butterflies.multimesh
		for i in range(mm.instance_count):
			var prm: Array = _butterfly_params[i]
			var r: float = prm[0] * (0.6 + tree_height * 0.08)
			var t := _time * float(prm[1]) + float(prm[2])
			var p := Vector3(cos(t) * r + sin(t * 2.3) * 0.4, float(prm[3]) + tree_height * 0.25 + sin(t * 3.1) * 0.3, sin(t) * r * 0.8 + cos(t * 1.7) * 0.4)
			var ahead := Vector3(-sin(t), 0, cos(t) * 0.8).normalized()
			var basis := Basis.looking_at(ahead, Vector3.UP)
			mm.set_instance_transform(i, Transform3D(basis, p))
	# Birds: now and then a small flock crosses the sky, flapping.
	_update_birds(delta, day)
	_pollen.emitting = day and h > 0.15
	_pollen.position = Vector3(0, maxf(1.5, tree_height * 0.5), 0)
	_fireflies.emitting = not day


func _update_birds(delta: float, day: bool) -> void:
	if _bird_t < 0.0:
		_birds.visible = false
		_bird_wait -= delta
		if _bird_wait <= 0.0 and day:
			var a := _rng.randf() * TAU
			var h := _rng.randf_range(22.0, 40.0)
			_bird_from = Vector3(cos(a) * 160.0, h, sin(a) * 160.0)
			_bird_to = Vector3(-cos(a + 0.5) * 160.0, h + _rng.randf_range(-5, 5), -sin(a + 0.5) * 160.0)
			_bird_t = 0.0
		return
	_bird_t += delta / 28.0
	if _bird_t >= 1.0:
		_bird_t = -1.0
		_bird_wait = _rng.randf_range(25.0, 60.0)
		return
	_birds.visible = true
	var head := _bird_from.lerp(_bird_to, _bird_t)
	var dir := (_bird_to - _bird_from).normalized()
	var side := dir.cross(Vector3.UP).normalized()
	var basis := Basis.looking_at(dir, Vector3.UP)
	var mm := _birds.multimesh
	for i in range(mm.instance_count):
		# A loose V behind the leader.
		var rank := float((i + 1) / 2)
		var s := -1.0 if i % 2 == 0 else 1.0
		var p := head - dir * rank * 2.2 + side * s * rank * 1.8 + Vector3(0, sin(_time + i) * 0.4, 0)
		mm.set_instance_transform(i, Transform3D(basis, p))
