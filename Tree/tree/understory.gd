class_name Understory
extends Node3D
## The living clearing (design doc section 17.6), drawn: the shade plants Clearing.plan() puts
## under the crown, as painted cards from lookdev/grass/understory_atlas.png (wood anemone, fern
## and mushrooms stand up on crossed cards; moss lies flat on the ground), and the meadow grass
## thinned and darkened inside the shade. Rebuilt when the tree has grown noticeably or the
## mushrooms come or go; TreeView asks at night and at sunrise, while the scene is hidden.

const ATLAS := "res://lookdev/grass/understory_atlas.png"
## Atlas cell per kind (moss is drawn flat from cell 2).
const CELL := {"anemone": 0, "fern": 1, "moss": 2, "mushroom": 3}
## Card width and height per kind, in metres (the painted plant fills the lower part of a cell).
const SIZES := {"anemone": Vector2(0.42, 0.36), "fern": Vector2(1.0, 0.95), "mushroom": Vector2(0.45, 0.4), "moss": Vector2(0.9, 0.9)}
## Living segments per step of growth that counts as "grown noticeably".
const GROWTH_STEP: int = 40

## The shade map of the last rebuild (Clearing.shade_map).
var map: PackedFloat32Array = PackedFloat32Array()
## The last plan, for tools and tests.
var planned: Array = []
var _cards: MultiMeshInstance3D
var _moss: MultiMeshInstance3D
var _key: String = ""
## The meadow layers as planted (MultiMesh -> buffer), so each rebuild thins from the full meadow.
var _grass_base: Dictionary = {}


func _ready() -> void:
	_cards = MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = Foliage.clump_mesh()
	_cards.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/grass/meadow_grass.gdshader")
	mat.set_shader_parameter("clump_texture", load(ATLAS))
	mat.set_shader_parameter("cell_from_alpha", true)
	mat.set_shader_parameter("wind_strength", 0.5)
	_cards.material_override = mat
	_cards.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_cards)

	_moss = MultiMeshInstance3D.new()
	var mm2 := MultiMesh.new()
	mm2.transform_format = MultiMesh.TRANSFORM_3D
	mm2.use_colors = true
	var plane := PlaneMesh.new()
	plane.size = Vector2.ONE
	mm2.mesh = plane
	_moss.multimesh = mm2
	var mmat := StandardMaterial3D.new()
	mmat.albedo_texture = load(ATLAS)
	mmat.uv1_scale = Vector3(0.5, 0.5, 1.0)
	mmat.uv1_offset = Vector3(0.0, 0.5, 0.0)
	mmat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
	mmat.alpha_scissor_threshold = 0.45
	mmat.vertex_color_use_as_albedo = true
	mmat.roughness = 1.0
	mmat.specular_mode = BaseMaterial3D.SPECULAR_DISABLED
	_moss.material_override = mmat
	_moss.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_moss)


## The cards fade out with the meadow (TreeView._frame_camera).
func set_fade(start: float, end: float) -> void:
	var m := _cards.material_override as ShaderMaterial
	m.set_shader_parameter("fade_start", start)
	m.set_shader_parameter("fade_end", end)


## By night the white anemones dim (the eye sees little white in the dark), 0 day .. 1 night.
func set_night(n: float) -> void:
	(_cards.material_override as ShaderMaterial).set_shader_parameter("night_dim", lerpf(1.0, 0.3, n))


## The meadow was planted anew (a wider clearing): thin it from its new full state next time.
func forget_grass() -> void:
	_grass_base.clear()
	_key = ""


## Rebuilds the shade plants and thins the meadow `layers` when the tree has grown noticeably
## (GROWTH_STEP living segments) or the mushrooms came or went since the last time.
func refresh(state: GameState, layers: Array, ground: ShaderMaterial = null) -> void:
	var day := state.day_number()
	var key := "%d/%d" % [state.sim.living_nodes() / GROWTH_STEP, state.clearing.wet_day(day)]
	if key == _key:
		return
	_key = key
	map = Clearing.shade_map(state.sim)
	planned = state.clearing.plan(map, day, Budgets.UNDERSTORY_PLANTS, Budgets.UNDERSTORY_MUSHROOMS)
	_place(planned)
	if ground != null:
		ground.set_shader_parameter("shade_map", shade_texture())
		ground.set_shader_parameter("shade_half", Clearing.HALF)
		ground.set_shader_parameter("use_shade", true)
	for layer in layers:
		_thin((layer as MultiMeshInstance3D).multimesh, state.seed)


## The shade map as a one-channel texture, one texel per cell (row = z), for the ground shader.
func shade_texture() -> ImageTexture:
	var img := Image.create_from_data(Clearing.N, Clearing.N, false, Image.FORMAT_RF, map.to_byte_array())
	return ImageTexture.create_from_image(img)


func _place(p: Array) -> void:
	var cards: Array = []
	var moss: Array = []
	# Wood anemones flower in spring only; the rest of the year their spots are leaf litter
	# (0.6 look review: they were in bloom in autumn).
	var anemones := Almanac.season_now() == Almanac.Season.SPRING
	for e in p:
		var pos: Vector2 = e["pos"]
		if e["kind"] == "anemone" and not anemones:
			continue
		# Nothing grows inside the garden shed.
		if Vector2(pos.x - Shed.origin.x, pos.y - Shed.origin.z).length() < 2.6:
			continue
		(moss if e["kind"] == "moss" else cards).append(e)
	var mm := _cards.multimesh
	mm.instance_count = cards.size()
	for i in range(cards.size()):
		var e: Dictionary = cards[i]
		var pos: Vector2 = _loose(e)
		# Smaller and sparser toward the edge of the shade, full grown where it is deep, so the
		# ferns and flowers thin out into the meadow instead of ending in a block.
		var grade := _grade(pos)
		var s: Vector2 = SIZES[e["kind"]] * float(e["size"]) * grade
		var basis := Basis(Vector3.UP, float(e["rot"])).scaled(Vector3(s.x, s.y, s.x))
		mm.set_instance_transform(i, Transform3D(basis, Terrain.at(Vector3(pos.x, 0.0, pos.y)) + Vector3(0, -0.01, 0)))
		# White anemones would glare in the shade: a little dimmer than the ferns.
		var v := (0.78 if e["kind"] == "anemone" else 0.9) + 0.2 * fposmod(float(e["rot"]) * 7.13, 1.0)
		mm.set_instance_color(i, Color(v, v, v, (float(CELL[e["kind"]]) + 0.5) / 4.0))
	var mm2 := _moss.multimesh
	mm2.instance_count = moss.size()
	for i in range(moss.size()):
		var e: Dictionary = moss[i]
		var pos: Vector2 = _loose(e)
		var w: float = SIZES["moss"].x * float(e["size"]) * _grade(pos)
		# Lying on the ground, tilted with its slope.
		var at := Terrain.at(Vector3(pos.x, 0.0, pos.y))
		var n := Vector3(Terrain.height(pos.x - 0.3, pos.y) - Terrain.height(pos.x + 0.3, pos.y), 0.6, Terrain.height(pos.x, pos.y - 0.3) - Terrain.height(pos.x, pos.y + 0.3)).normalized()
		var tilt := Basis(Vector3.UP.cross(n).normalized(), Vector3.UP.angle_to(n)) if n.y < 0.999 else Basis.IDENTITY
		var basis := (tilt * Basis(Vector3.UP, float(e["rot"]))).scaled(Vector3(w, 1.0, w))
		mm2.set_instance_transform(i, Transform3D(basis, at + Vector3(0, 0.02 + 0.004 * (i % 5), 0)))
		var v := 0.85 + 0.25 * fposmod(float(e["rot"]) * 5.31, 1.0)
		mm2.set_instance_color(i, Color(v, v, v))


## A plant's spot, loosened from the plan's half-metre grid (the plants lined up in cells).
func _loose(e: Dictionary) -> Vector2:
	var r := float(e["rot"])
	var pos: Vector2 = e["pos"]
	var j := pos + Vector2(sin(r * 3.7), cos(r * 5.3)) * 0.28
	return j if j.length() > Clearing.BARE_RADIUS + 0.15 else pos


## How grown a plant is at this spot: 0.5 at the fraying edge of the shade .. 1.1 in deep shade.
func _grade(pos: Vector2) -> float:
	var s := Clearing.shade_at(map, pos.x, pos.y) + _ragged(pos.x, pos.y)
	return lerpf(0.5, 1.1, smoothstep(0.2, 0.75, s))


## A soft noise that frays the edge of the shade (the ground shader frays its litter alike).
func _ragged(x: float, z: float) -> float:
	return (sin(x * 1.7 + z * 0.6) * cos(z * 1.9 - x * 0.4) + sin(x * 4.1 - z * 3.3) * 0.4) * 0.12


## The sun meadow gives way in the shade: most clumps go, the rest stand lower and darker.
func _thin(mm: MultiMesh, seed: int) -> void:
	var stride := 12 + (4 if mm.use_colors else 0) + (4 if mm.use_custom_data else 0)
	if not _grass_base.has(mm):
		_grass_base[mm] = mm.buffer.duplicate()
	var base: PackedFloat32Array = _grass_base[mm]
	# (A headless run keeps no instance data to read back: nothing to thin there.)
	if base.size() != mm.instance_count * stride:
		_grass_base.erase(mm)
		return
	mm.buffer = base
	var salt := posmod(seed, 1000) * 0.618
	for i in range(mm.instance_count):
		var xf := mm.get_instance_transform(i)
		var s := Clearing.shade_at(map, xf.origin.x, xf.origin.z)
		if s > 0.02:
			s = clampf(s + _ragged(xf.origin.x, xf.origin.z) * 1.5, 0.0, 1.0)
		if s < 0.12:
			continue
		var keep := 1.0 - 0.9 * smoothstep(0.15, 0.8, s)
		if fposmod(sin(i * 12.9898 + salt) * 43758.5453, 1.0) > keep:
			xf.basis = Basis.from_scale(Vector3.ZERO)
		else:
			xf.basis = xf.basis.scaled(Vector3(1.0, 1.0 - 0.4 * s, 1.0))
			mm.set_instance_color(i, mm.get_instance_color(i).darkened(0.35 * s))
		mm.set_instance_transform(i, xf)
