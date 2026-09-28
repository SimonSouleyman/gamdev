class_name BonsaiView
extends Node3D
## The bonsai on the shed's windowsill (design doc section 16), drawn from GameState.bonsai:
## the pot (a few shapes and glazes), soil with fine gravel and moss that darkens when wet,
## the fertiliser pellets lying on it, the wood with silver deadwood and wire scars, the
## foliage (scale-like juniper pads or the roster's leaf sprays, drooping when dry), the copper
## wire coils, and for repotting the root ball. Bonsai mode is a close camera orbiting the pot
## with the tools: watering can, fertiliser tin, turning the pot, shears (tree/pruning.gd with
## a third as the limit), pinching and wire. It only reads the simulation and calls its methods.

signal tool_used(kind: String)

## Window side is +Z of the sill anchor (Shed.bonsai_spot); the room is -Z.
const FOCUS := Vector3(0.0, 0.2, 0.0)
## The default look: far enough that pot and crown sit between the status scrap and the tools.
const DIST := 0.8
const PICK_RADIUS := 44.0
## The copper wire: its thickness and one coil turn, in bonsai units.
const WIRE_RADIUS := 0.02
const WIRE_PITCH := 0.14
## The pots as drawn: footprint half size (m), height, corner roundness (superellipse power),
## glaze colour and roughness. The nursery pot is the Poly Haven clay planter.
const POT_LOOKS := {
	"nursery": {"model": true, "scale": 0.6, "soil": 0.112, "half": Vector2(0.066, 0.066), "n": 2.0},
	"rectangle": {"half": Vector2(0.11, 0.08), "h": 0.058, "n": 7.0, "color": Color(0.2, 0.195, 0.19), "rough": 0.92},
	"oval": {"half": Vector2(0.1, 0.075), "h": 0.055, "n": 2.0, "color": Color(0.09, 0.15, 0.3), "rough": 0.6},
	"round": {"half": Vector2(0.078, 0.078), "h": 0.07, "n": 2.0, "color": Color(0.4, 0.54, 0.42), "rough": 0.28},
	"cascade": {"half": Vector2(0.062, 0.062), "h": 0.15, "n": 5.0, "color": Color(0.86, 0.8, 0.67), "rough": 0.3},
}
const JUNIPER_COLOR := "res://lookdev/bonsai/juniper_spray_color.png"
const JUNIPER_NORMAL := "res://lookdev/bonsai/juniper_spray_normal.png"

var state: GameState
var camera: Camera3D
## Bonsai mode: the close camera is on and the tools answer.
var active: bool = false
var input_enabled: bool = true
## "", "shears", "pinch" or "wire".
var tool: String = ""
var pruning: Pruning
## A tool animation is playing (watering, pellets, turning, repotting).
var busy: bool = false

var _base: Node3D
var _turn_node: Node3D
var _turn_angle: float = 0.0
var _pot_node: Node3D
var _soil: MeshInstance3D
var _soil_mat: StandardMaterial3D
var _moss: Node3D
var _pellets: MultiMeshInstance3D
var _lift: Node3D
var _plant: Node3D
var _wood: MeshInstance3D
var _bark: ShaderMaterial
var _foliage: MultiMeshInstance3D
var _spray_mat: ShaderMaterial
var _wires: MeshInstance3D
var _root_ball: Node3D
var _can: Node3D
var _can_rest: Transform3D
var _water: CPUParticles3D
var _tin: Node3D
var _tin_rest: Transform3D
var _light: SpotLight3D
var _preview: MeshInstance3D
var _preview_mesh := ImmediateMesh.new()
var _builder := BranchMeshBuilder.new()
var _shown_pot: String = ""
var _shown_species: String = ""
var _sig: Array = []
var _timer: float = 0.0
## Camera orbit around the pot (sill frame): yaw 0 looks from the room toward the window.
var _yaw: float = 0.0
var _pitch: float = 0.28
var _dist: float = DIST
var _focus: Vector3 = FOCUS
var _blend: float = 1.0
var _from: Transform3D
var _from_fov: float = 50.0
var _press: Vector2
var _pressing: bool = false
var _drag: String = ""
var _wire_id: int = -1
var _wire_target: Vector3
var _touches: Dictionary = {}
var _pinch_dist: float = 0.0
var _pinch_zoom: float = 0.0
var _sounds: Dictionary = {}
var _fade: float = 0.0
var _player: AudioStreamPlayer3D
## The repot flow: the root ball is out of the pot.
var _lifted: bool = false
var _trim: float = 0.0
var _new_pot: String = ""


func _ready() -> void:
	# The pot stands toward the room on the deep sill, clear of the glass.
	_base = Node3D.new()
	_base.position = Vector3(0.0, 0.0, -0.035)
	add_child(_base)
	_turn_node = Node3D.new()
	_base.add_child(_turn_node)
	_pot_node = Node3D.new()
	_turn_node.add_child(_pot_node)
	_lift = Node3D.new()
	_turn_node.add_child(_lift)
	_plant = Node3D.new()
	_plant.scale = Vector3.ONE * BonsaiSim.UNIT_METRES
	_lift.add_child(_plant)
	_bark = ShaderMaterial.new()
	_bark.shader = preload("res://shed/bonsai_bark.gdshader")
	_bark.set_shader_parameter("bark_albedo", load(Assets.BARK_DIFF))
	_bark.set_shader_parameter("bark_normal", load(Assets.BARK_NORMAL))
	_wood = MeshInstance3D.new()
	_wood.material_override = _bark
	_plant.add_child(_wood)
	_builder.radius_scale = 1.5
	_builder.min_radius = 0.012
	_builder.bark_tiling = 1.6
	_bark.set_shader_parameter("scar_pitch", WIRE_PITCH * _builder.bark_tiling)
	_foliage = MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = CrownSprays.card_mesh()
	_foliage.multimesh = mm
	_plant.add_child(_foliage)
	_wires = MeshInstance3D.new()
	var copper := StandardMaterial3D.new()
	copper.albedo_color = Color(0.72, 0.4, 0.2)
	copper.metallic = 0.85
	copper.roughness = 0.32
	_wires.material_override = copper
	_plant.add_child(_wires)
	_preview = MeshInstance3D.new()
	_preview.mesh = _preview_mesh
	var pm := StandardMaterial3D.new()
	pm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	pm.albedo_color = Color(0.95, 0.6, 0.3)
	pm.no_depth_test = true
	_preview.material_override = pm
	_plant.add_child(_preview)
	pruning = Pruning.new()
	pruning.host = self
	pruning.max_share = BonsaiSim.PRUNE_SHARE
	pruning.min_cut = BonsaiSim.PRUNE_MIN
	pruning.first_id = 2
	pruning.fall_depth = 1.2
	_plant.add_child(pruning)
	pruning.cut_done.connect(func(_n: int) -> void:
		_play("snip")
		refresh(true)
		tool_used.emit("shears"))
	_build_soil_things()
	_build_tools()
	_build_light()
	camera = Camera3D.new()
	camera.fov = 50.0
	camera.near = 0.02
	camera.far = 300.0
	add_child(camera)
	_player = AudioStreamPlayer3D.new()
	_player.unit_size = 3.0
	add_child(_player)
	var files := {"water": "water_flowing.ogg", "pot": "shed_clay_pot.ogg", "wire": "shed_pin.ogg", "snip": "shed_pin.ogg", "pellets": "shed_paper_bag.wav"}
	for k in files:
		var path: String = "res://assets/sounds/" + str(files[k])
		if ResourceLoader.exists(path):
			_sounds[k] = load(path)
	visible = false


func setup(p_state: GameState) -> void:
	state = p_state
	_shown_pot = ""
	_shown_species = ""
	_sig = []
	_lifted = false
	_root_ball_visible(false)
	refresh(true)


func sim() -> BonsaiSim:
	return state.bonsai if state != null else null


# --- building ---------------------------------------------------------------------------

func _build_soil_things() -> void:
	_soil = MeshInstance3D.new()
	_soil_mat = StandardMaterial3D.new()
	_soil_mat.albedo_texture = load("res://assets/bonsai/Gravel022_Color.jpg")
	_soil_mat.normal_enabled = true
	_soil_mat.normal_texture = load("res://assets/bonsai/Gravel022_NormalGL.jpg")
	_soil_mat.roughness = 0.95
	_soil_mat.uv1_scale = Vector3(4.0, 4.0, 1.0)
	_soil_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	_soil.material_override = _soil_mat
	_pot_node.add_child(_soil)
	_moss = Node3D.new()
	_pot_node.add_child(_moss)
	_pellets = MultiMeshInstance3D.new()
	var pm := MultiMesh.new()
	pm.transform_format = MultiMesh.TRANSFORM_3D
	pm.use_colors = true
	var bead := SphereMesh.new()
	bead.radius = 0.0028
	bead.height = 0.005
	bead.radial_segments = 6
	bead.rings = 3
	pm.mesh = bead
	_pellets.multimesh = pm
	var bm := StandardMaterial3D.new()
	bm.vertex_color_use_as_albedo = true
	bm.roughness = 0.85
	_pellets.material_override = bm
	_pot_node.add_child(_pellets)
	_root_ball = Node3D.new()
	_lift.add_child(_root_ball)


## The watering can and the fertiliser tin wait on the sill beside the pot.
func _build_tools() -> void:
	_can = (load("res://assets/shed/watering_can_metal_01/watering_can_metal_01_1k.gltf") as PackedScene).instantiate()
	_can.scale = Vector3.ONE * 0.42
	_can.position = Vector3(0.17, 0.0, -0.02)
	_can.rotation.y = -2.2
	_base.add_child(_can)
	_can_rest = _can.transform
	_water = CPUParticles3D.new()
	_water.emitting = false
	_water.amount = 160
	_water.lifetime = 0.5
	_water.direction = Vector3(0, -1, 0)
	_water.spread = 6.0
	_water.initial_velocity_min = 0.25
	_water.initial_velocity_max = 0.4
	_water.gravity = Vector3(0, -3.0, 0)
	_water.emission_shape = CPUParticles3D.EMISSION_SHAPE_SPHERE
	_water.emission_sphere_radius = 0.012
	var drop := SphereMesh.new()
	drop.radius = 0.0016
	drop.height = 0.007
	drop.radial_segments = 5
	drop.rings = 2
	_water.mesh = drop
	var wm := StandardMaterial3D.new()
	wm.albedo_color = Color(0.8, 0.88, 0.98, 0.55)
	wm.metallic_specular = 1.0
	wm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	wm.roughness = 0.05
	_water.material_override = wm
	_water.local_coords = false
	_base.add_child(_water)
	# The fertiliser tin: an old tin with a paper label and its lid.
	_tin = Node3D.new()
	_tin.position = Vector3(-0.16, 0.0, 0.02)
	_base.add_child(_tin)
	var tin_mat := StandardMaterial3D.new()
	tin_mat.albedo_color = Color(0.5, 0.52, 0.5)
	tin_mat.metallic = 0.8
	tin_mat.roughness = 0.45
	var body := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.026
	cyl.bottom_radius = 0.026
	cyl.height = 0.05
	cyl.radial_segments = 20
	body.mesh = cyl
	body.material_override = tin_mat
	body.position.y = 0.025
	_tin.add_child(body)
	var label := MeshInstance3D.new()
	var band := CylinderMesh.new()
	band.top_radius = 0.0265
	band.bottom_radius = 0.0265
	band.height = 0.028
	band.radial_segments = 20
	label.mesh = band
	var paper := StandardMaterial3D.new()
	paper.albedo_texture = load("res://assets/paper/paper_beige.png")
	paper.albedo_color = Color(0.92, 0.84, 0.66)
	paper.roughness = 0.95
	label.material_override = paper
	label.position.y = 0.024
	_tin.add_child(label)
	var words := Label3D.new()
	words.text = "N P K"
	words.font = Paper.hand_font(true)
	words.font_size = 48
	words.pixel_size = 0.00032
	words.modulate = Paper.INK
	words.outline_size = 0
	words.shaded = true
	words.position = Vector3(0, 0.025, -0.0275)
	words.rotation.y = PI
	_tin.add_child(words)
	var lid := MeshInstance3D.new()
	var lc := CylinderMesh.new()
	lc.top_radius = 0.0275
	lc.bottom_radius = 0.0275
	lc.height = 0.006
	lc.radial_segments = 20
	lid.mesh = lc
	lid.material_override = tin_mat
	lid.position.y = 0.052
	_tin.add_child(lid)
	_tin_rest = _tin.transform


## The window is the bonsai's sun: light from one fixed side, as bright as the day outside.
func _build_light() -> void:
	_light = SpotLight3D.new()
	_light.light_color = Color(1.0, 0.93, 0.8)
	_light.spot_range = 2.5
	_light.spot_angle = 22.0
	_light.spot_attenuation = 0.6
	_light.shadow_enabled = not Budgets.PHONE
	_light.position = Vector3(0.05, 0.75, 1.25)
	add_child(_light)
	_light.look_at_from_position(_light.position, FOCUS, Vector3.UP)


func _pot_mesh(look: Dictionary) -> Node3D:
	var n := Node3D.new()
	if look.get("model", false):
		var m: Node3D = (load("res://assets/shed/planter_pot_clay/planter_pot_clay_1k.gltf") as PackedScene).instantiate()
		m.scale = Vector3.ONE * float(look["scale"])
		n.add_child(m)
		return n
	var half: Vector2 = look["half"]
	var h: float = look["h"]
	var power: float = look["n"]
	var mat := StandardMaterial3D.new()
	mat.albedo_color = look["color"]
	mat.roughness = look["rough"]
	mat.normal_enabled = true
	mat.normal_texture = preload("res://lookdev/paper/textures/crumple_normal.png")
	mat.normal_scale = 0.35
	mat.uv1_triplanar = true
	mat.uv1_scale = Vector3.ONE * 6.0
	var feet := 0.008
	var wall := 0.008
	# Profile from the foot up the outside, over the lip and down the inside to the soil.
	var profile: Array[Vector3] = [Vector3(0.0, feet, 0.9), Vector3(0.0, feet, 0.93), Vector3(0.0, feet + h * 0.25, 0.97),
		Vector3(0.0, feet + h * 0.9, 1.0), Vector3(0.0, feet + h, 1.02), Vector3(0.0, feet + h, 1.0 - wall / half.x),
		Vector3(0.0, feet + h - 0.012, 1.0 - wall / half.x)]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var seg := 48
	for i in range(seg):
		for k in range(profile.size() - 1):
			var quad: Array[Vector3] = []
			for c: Array in [[i, k], [i + 1, k], [i + 1, k + 1], [i, k + 1]]:
				var a := TAU * float(c[0]) / seg
				var r := _superellipse(a, half, power) * profile[c[1]].z
				quad.append(Vector3(cos(a) * r.x, profile[c[1]].y, sin(a) * r.y))
			st.add_vertex(quad[0])
			st.add_vertex(quad[2])
			st.add_vertex(quad[1])
			st.add_vertex(quad[0])
			st.add_vertex(quad[3])
			st.add_vertex(quad[2])
	# The floor under the pot.
	for i in range(seg):
		var a0 := TAU * float(i) / seg
		var a1 := TAU * float(i + 1) / seg
		var r0 := _superellipse(a0, half, power) * 0.9
		var r1 := _superellipse(a1, half, power) * 0.9
		st.add_vertex(Vector3(0, feet, 0))
		st.add_vertex(Vector3(cos(a0) * r0.x, feet, sin(a0) * r0.y))
		st.add_vertex(Vector3(cos(a1) * r1.x, feet, sin(a1) * r1.y))
	st.generate_normals()
	var body := MeshInstance3D.new()
	body.mesh = st.commit()
	body.material_override = mat
	n.add_child(body)
	# Small feet under the corners.
	for sx in [-1.0, 1.0]:
		for sz in [-1.0, 1.0]:
			var foot := MeshInstance3D.new()
			var b := BoxMesh.new()
			b.size = Vector3(0.018, feet, 0.014)
			foot.mesh = b
			foot.material_override = mat
			foot.position = Vector3(sx * half.x * 0.62, feet * 0.5, sz * half.y * 0.62)
			n.add_child(foot)
	return n


## Radius of a superellipse footprint at angle `a` (power 2 an ellipse, higher a rounded box).
static func _superellipse(a: float, half: Vector2, power: float) -> Vector2:
	var c := absf(cos(a))
	var s := absf(sin(a))
	var r := pow(pow(c, power) + pow(s, power), -1.0 / power)
	return half * r


## Soil top (m) and its footprint for the pot `id`.
static func soil_height(id: String) -> float:
	var look: Dictionary = POT_LOOKS.get(id, POT_LOOKS["nursery"])
	if look.get("model", false):
		return float(look["soil"])
	return 0.008 + float(look["h"]) - 0.007


static func soil_half(id: String) -> Vector2:
	var look: Dictionary = POT_LOOKS.get(id, POT_LOOKS["nursery"])
	return (look["half"] as Vector2) * (1.0 if look.get("model", false) else 0.93)


func _show_pot(id: String) -> void:
	for c in _pot_node.get_children():
		if c != _soil and c != _moss and c != _pellets:
			c.queue_free()
	var look: Dictionary = POT_LOOKS.get(id, POT_LOOKS["nursery"])
	_pot_node.add_child(_pot_mesh(look))
	var y := soil_height(id)
	var half := soil_half(id)
	var power: float = look["n"]
	# The soil surface: a slight dome of fine gravel.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var seg := 40
	for i in range(seg):
		var a0 := TAU * float(i) / seg
		var a1 := TAU * float(i + 1) / seg
		var r0 := _superellipse(a0, half, power)
		var r1 := _superellipse(a1, half, power)
		var c := Vector3(0, y + 0.006, 0)
		var p0 := Vector3(cos(a0) * r0.x, y, sin(a0) * r0.y)
		var p1 := Vector3(cos(a1) * r1.x, y, sin(a1) * r1.y)
		for p in [c, p1, p0]:
			st.set_uv(Vector2(p.x, p.z) * 3.0)
			st.set_normal(Vector3.UP)
			st.add_vertex(p)
	_soil.mesh = st.commit()
	# Moss around the trunk's foot, a few cushions.
	for c in _moss.get_children():
		c.queue_free()
	var moss_mat := StandardMaterial3D.new()
	moss_mat.albedo_texture = load("res://assets/bonsai/Moss002_Color.jpg")
	moss_mat.normal_enabled = true
	moss_mat.normal_texture = load("res://assets/bonsai/Moss002_NormalGL.jpg")
	moss_mat.roughness = 1.0
	moss_mat.uv1_scale = Vector3(2.0, 2.0, 1.0)
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([state.bonsai.seed if state and state.bonsai else 0, "moss", id])
	for k in range(6):
		var m := MeshInstance3D.new()
		var sp := SphereMesh.new()
		sp.radius = rng.randf_range(0.014, 0.026)
		sp.height = sp.radius * 0.7
		sp.radial_segments = 12
		sp.rings = 5
		m.mesh = sp
		m.material_override = moss_mat
		var a := rng.randf() * TAU
		var d := rng.randf_range(0.2, 0.75)
		m.position = Vector3(cos(a) * half.x * d, y + 0.001, sin(a) * half.y * d)
		m.rotation.y = rng.randf() * TAU
		m.scale = Vector3(1.0, 0.45, rng.randf_range(0.7, 1.2))
		_moss.add_child(m)
	_plant.position = Vector3(0, y, 0)
	_shown_pot = id


# --- keeping up with the simulation ----------------------------------------------------

func _process(delta: float) -> void:
	visible = state != null and state.bonsai != null
	# Out of the shed (the tree's day) nothing of it is drawn: no rebuilds either.
	if not is_visible_in_tree():
		return
	var b := state.bonsai
	_timer += delta
	if _timer >= 0.5:
		_timer = 0.0
		refresh(false)
	# Wet soil is darker, dry soil pale.
	var wet := clampf((b.moisture - 0.1) / 0.8, 0.0, 1.0)
	_soil_mat.albedo_color = Color(1.0, 0.98, 0.95).lerp(Color(0.55, 0.5, 0.46), wet)
	_soil_mat.roughness = lerpf(0.95, 0.55, wet)
	_light.light_energy = 0.25 + 1.4 * b.clock.sun_height()
	# With the wire or the shears the foliage in front of the branch thins out, so the wood
	# shows (the leaves dissolve toward the camera).
	if _spray_mat != null:
		var see_wood := active and (tool == "wire" or tool == "shears")
		_spray_mat.set_shader_parameter("near_fade", _dist * 0.95 if see_wood else 0.0)
		_fade = move_toward(_fade, 0.72 if see_wood else 0.0, delta * 3.0)
		_spray_mat.set_shader_parameter("fade", _fade)
	_update_camera(delta)


## Rebuilds what changed (every half second; `force` after a tool).
func refresh(force: bool) -> void:
	var b := sim()
	if b == null:
		visible = false
		return
	if _shown_pot != b.pot and not _lifted:
		_show_pot(b.pot)
	if _shown_species != b.species.id:
		_apply_species(b.species)
	var sig := [b.graph.size(), b.leafy_count(), b.day(), b.wired().size(), int(b.droop() * 4.0), b.turn, b.pot]
	if not force and sig == _sig:
		return
	_sig = sig
	if not busy:
		_turn_angle = b.turn * PI * 0.5
		_turn_node.rotation.y = _turn_angle
	_wood.mesh = _build_wood(b)
	_populate_foliage(b)
	_wires.mesh = _build_wires(b)
	_update_pellets(b)


func _apply_species(sp: Species) -> void:
	_shown_species = sp.id
	var b := sp.bark_tint
	_bark.set_shader_parameter("texture_tint", Vector3(b.r, b.g, b.b) * (1.0 if sp.conifer else 1.2))
	_spray_mat = CrownSprays.material()
	if sp.conifer:
		_spray_mat.set_shader_parameter("spray_color", load(JUNIPER_COLOR))
		_spray_mat.set_shader_parameter("spray_normal", load(JUNIPER_NORMAL))
		_spray_mat.set_shader_parameter("translucency", Color(0.25, 0.35, 0.1))
	_spray_mat.set_shader_parameter("tint_mul", sp.leaf_tint)
	# Indoors: hardly a breath of wind.
	_spray_mat.set_shader_parameter("wind_strength", 0.06)
	_spray_mat.set_shader_parameter("crown_normal_mix", 0.45)
	_foliage.material_override = _spray_mat


func _build_wood(b: BonsaiSim) -> ArrayMesh:
	var g := b.graph
	var colors := PackedColorArray()
	colors.resize(g.size())
	for id in range(g.size()):
		colors[id] = Color(1.0 if b.is_jin(id) else 0.0, float(g.get_flag(id, "shari", 0.0)), b.scar(id), 1.0)
	_builder.node_colors = colors
	return _builder.build(g)


## Foliage on the living twigs: juniper pads lying flat and facing up, or the roster's leaf
## sprays; drooping and duller when the soil is dry, brown on burnt tips.
func _populate_foliage(b: BonsaiSim) -> void:
	var g := b.graph
	var spots: Array[int] = []
	for id in range(2, g.size()):
		if b.is_dead(id) or b.is_jin(id):
			continue
		if g.radii[id] < 0.035:
			spots.append(id)
	var conifer := b.species.conifer
	var per := Budgets.BONSAI_SPRAYS_PER_TWIG if conifer else 1
	var mm := _foliage.multimesh
	mm.instance_count = spots.size() * per
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([b.seed, "bonsai foliage"])
	var droop := b.droop()
	var centre := Vector3(0, b.height() * 0.6, 0)
	var i := 0
	var lo := Vector3.ONE * INF
	var hi := -Vector3.ONE * INF
	for id in spots:
		var p := g.positions[id]
		var along := g.direction_of(id)
		var tip: bool = (g.children[id] as Array).is_empty()
		for _k in range(per):
			var at := p + Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.4, 0.4), rng.randf_range(-1, 1)) * (0.13 if conifer else 0.08)
			var out := (at - centre)
			out.y *= 0.4
			out = out.normalized() if out.length_squared() > 1e-6 else Vector3.FORWARD
			var face: Vector3
			var size: float
			if conifer:
				# Pads: lying almost flat, facing the sky, reaching out along the twig.
				# Pads: dense tufts of small sprays, most facing the sky, some tilted out, so a
				# pad has volume from the side too.
				face = (Vector3.UP * 0.9 + out * 0.45 + Vector3(rng.randf_range(-0.6, 0.6), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.6, 0.6))).normalized()
				size = rng.randf_range(0.3, 0.46) * (1.0 if tip else 0.8)
			else:
				face = (out * 0.8 + Vector3.UP * 0.45 + Vector3(rng.randf_range(-0.3, 0.3), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.3, 0.3))).normalized()
				size = rng.randf_range(0.32, 0.46)
			# Too dry: the leaves hang.
			face = face.lerp(out * 0.6 + Vector3.DOWN * 0.8, droop * 0.85).normalized()
			var dir := along - face * along.dot(face)
			if dir.length_squared() < 0.01:
				dir = face.cross(Vector3.RIGHT)
			dir = dir.normalized().rotated(face, rng.randf_range(-0.9, 0.9))
			dir = dir.lerp(Vector3.DOWN, droop * 0.7).normalized()
			var side := dir.cross(face).normalized()
			var basis := Basis(side, dir, face).scaled(Vector3.ONE * size)
			mm.set_instance_transform(i, Transform3D(basis, at - dir * size * 0.3))
			var tint := rng.randf_range(0.88, 1.06)
			var col := Color(tint, tint, tint * rng.randf_range(0.88, 1.0), 0.3 if tip else 0.5)
			if g.get_flag(id, "burnt") != null:
				col = Color(1.15, 0.62, 0.3, col.a)
			if droop > 0.0:
				col = col.lerp(Color(0.9, 0.84, 0.55, col.a), droop * 0.55)
			mm.set_instance_color(i, col)
			mm.set_instance_custom_data(i, Color(float(rng.randi() % 4), rng.randf(), 0, 0))
			lo = lo.min(at)
			hi = hi.max(at)
			i += 1
	if i > 0 and _spray_mat != null and is_inside_tree():
		var c := _plant.to_global((lo + hi) * 0.5)
		_spray_mat.set_shader_parameter("crown_centre", c)
		_spray_mat.set_shader_parameter("crown_radii", (hi - lo) * 0.5 * BonsaiSim.UNIT_METRES + Vector3.ONE * 0.04)


## Copper coils along each wired branch: a thin tube winding round the wood.
func _build_wires(b: BonsaiSim) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var any := false
	var g := b.graph
	for id in b.wired():
		var chain: Array[int] = [g.parents[id]]
		chain.append_array(b.wire_chain(id))
		var pts := PackedVector3Array()
		var rads := PackedFloat32Array()
		for n in chain:
			pts.append(g.positions[n])
			rads.append(maxf(_builder.min_radius, g.radii[n] * _builder.radius_scale))
		# Wire about a third as thick as the branch it holds.
		_helix(st, pts, rads, maxf(WIRE_RADIUS, rads[1] * 0.4))
		any = true
	if not any:
		return ArrayMesh.new()
	st.generate_normals()
	return st.commit()


func _helix(st: SurfaceTool, pts: PackedVector3Array, rads: PackedFloat32Array, wire_r: float) -> void:
	var path := PackedVector3Array()
	var along := 0.0
	var side := Vector3.ZERO
	for i in range(pts.size() - 1):
		var a := pts[i]
		var bb := pts[i + 1]
		var d := bb - a
		var len := d.length()
		if len < 1e-5:
			continue
		var axis := d / len
		if side == Vector3.ZERO:
			side = axis.cross(Vector3.UP if absf(axis.y) < 0.9 else Vector3.RIGHT).normalized()
		side = (side - axis * side.dot(axis)).normalized()
		var up := axis.cross(side)
		var steps := maxi(2, int(len / (WIRE_PITCH / 16.0)))
		for s in range(steps):
			var t := float(s) / steps
			var ang := TAU * (along + len * t) / WIRE_PITCH
			var r := lerpf(rads[i], rads[i + 1], t) * 1.15 + wire_r * 1.1
			path.append(a + d * t + (side * cos(ang) + up * sin(ang)) * r)
		along += len
	_tube(st, path, wire_r)


func _tube(st: SurfaceTool, path: PackedVector3Array, r: float) -> void:
	var sides := 6
	for i in range(path.size() - 1):
		var d := path[i + 1] - path[i]
		if d.length_squared() < 1e-10:
			continue
		var ax := d.normalized()
		var u := ax.cross(Vector3.UP if absf(ax.y) < 0.9 else Vector3.RIGHT).normalized()
		var v := ax.cross(u)
		for k in range(sides):
			var a0 := TAU * k / sides
			var a1 := TAU * (k + 1) / sides
			var o0 := (u * cos(a0) + v * sin(a0)) * r
			var o1 := (u * cos(a1) + v * sin(a1)) * r
			st.add_vertex(path[i] + o0)
			st.add_vertex(path[i + 1] + o0)
			st.add_vertex(path[i + 1] + o1)
			st.add_vertex(path[i] + o0)
			st.add_vertex(path[i + 1] + o1)
			st.add_vertex(path[i] + o1)


## The pellets on the soil: as many as the nutrient stands above fresh soil, in its colour.
func _update_pellets(b: BonsaiSim) -> void:
	var mm := _pellets.multimesh
	var counts: Array[int] = []
	var total := 0
	for k in range(3):
		var n := clampi(int((b.soil[k] - BonsaiSim.FRESH_SOIL) * 30.0), 0, 30)
		counts.append(n)
		total += n
	mm.instance_count = total
	var half := soil_half(b.pot) * 0.85
	var y := soil_height(b.pot) + 0.005
	var i := 0
	for k in range(3):
		var rng := RandomNumberGenerator.new()
		rng.seed = hash([b.seed, "pellets", k])
		var col: Color = Resources.KIND_COLORS[k + 1].darkened(0.35)
		for _j in range(counts[k]):
			var a := rng.randf() * TAU
			var d := sqrt(rng.randf())
			mm.set_instance_transform(i, Transform3D(Basis(), Vector3(cos(a) * half.x * d, y, sin(a) * half.y * d)))
			mm.set_instance_color(i, col)
			i += 1


# --- the camera ------------------------------------------------------------------------

func _orbit() -> Transform3D:
	var focus := to_global(_focus)
	var offset := Vector3(sin(_yaw) * cos(_pitch), sin(_pitch), -cos(_yaw) * cos(_pitch)) * _dist
	var eye := to_global(_focus + offset)
	return Transform3D(Basis(), eye).looking_at(focus, Vector3.UP)


func _update_camera(delta: float) -> void:
	if not active:
		return
	var target := _orbit()
	if _blend < 1.0:
		_blend = minf(1.0, _blend + delta / 0.9)
		var t := ease(_blend, -2.0)
		camera.global_transform = _from.interpolate_with(target, t)
		camera.fov = lerpf(_from_fov, 50.0, t)
	else:
		camera.global_transform = target
		camera.fov = 50.0


## Into bonsai mode: the camera glides from `from` (the shed's eye) close to the pot.
func enter(from: Camera3D) -> void:
	active = true
	tool = ""
	_from = from.global_transform
	_from_fov = from.fov
	_blend = 0.0
	camera.environment = from.environment
	camera.global_transform = _from
	camera.make_current()
	refresh(true)


## Back to the workbench: the camera glides back, then `to` takes over.
func leave(to: Camera3D, done: Callable) -> void:
	set_tool("")
	var start := camera.global_transform
	var fov0 := camera.fov
	active = false
	var tw := create_tween()
	tw.tween_method(func(t: float) -> void:
		var e := ease(t, -2.0)
		camera.global_transform = start.interpolate_with(to.global_transform, e)
		camera.fov = lerpf(fov0, to.fov, e), 0.0, 1.0, 0.7)
	tw.tween_callback(func() -> void:
		to.make_current()
		done.call())


func set_tool(t: String) -> void:
	tool = t
	pruning.preview(-1)
	_preview_mesh.clear_surfaces()
	_drag = ""
	_pressing = false


# --- input -----------------------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if not active or not input_enabled or busy or state == null or state.bonsai == null or _blend < 1.0:
		return
	if event is InputEventScreenTouch:
		var t := event as InputEventScreenTouch
		if t.pressed:
			_touches[t.index] = t.position
		else:
			_touches.erase(t.index)
		if _touches.size() == 2:
			var pts: Array = _touches.values()
			_pinch_dist = (pts[0] as Vector2).distance_to(pts[1])
			_pinch_zoom = _dist
			_pressing = false
		return
	if event is InputEventScreenDrag:
		var d := event as InputEventScreenDrag
		_touches[d.index] = d.position
		if _touches.size() == 2 and _pinch_dist > 0.0:
			var pts: Array = _touches.values()
			_dist = clampf(_pinch_zoom * _pinch_dist / maxf((pts[0] as Vector2).distance_to(pts[1]), 1.0), 0.3, 1.05)
		return
	if event is InputEventMouseButton:
		var m := event as InputEventMouseButton
		if m.pressed and m.button_index == MOUSE_BUTTON_WHEEL_UP:
			_dist = clampf(_dist * 0.92, 0.3, 1.05)
		elif m.pressed and m.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_dist = clampf(_dist * 1.08, 0.3, 1.05)
		elif m.button_index == MOUSE_BUTTON_LEFT:
			if m.pressed:
				_begin(m.position)
			else:
				_end(m.position)
	elif event is InputEventMouseMotion:
		var mm := event as InputEventMouseMotion
		if _pressing:
			_move(mm.position, mm.relative)
		elif tool == "shears":
			pruning.preview(pruning.pick(mm.position))


func _begin(pos: Vector2) -> void:
	if _touches.size() >= 2:
		return
	_pressing = true
	_press = pos
	_drag = ""
	match tool:
		"shears":
			var id := pruning.pick(pos)
			pruning.preview(id)
			_drag = "prune" if id >= 0 else ""
		"wire":
			_wire_id = pick_branch(pos)
			_drag = "wire" if _wire_id >= 0 else ""


func _move(pos: Vector2, rel: Vector2) -> void:
	match _drag:
		"prune":
			pruning.preview(pruning.pick(pos))
			return
		"wire":
			_wire_target = _drag_point(pos)
			_draw_wire_preview()
			return
	if _drag == "" and pos.distance_to(_press) > 10.0:
		_drag = "orbit"
	if _drag == "orbit":
		# Round the pot, but only on the room's side of the window.
		_yaw = clampf(_yaw - rel.x * 0.007, -1.25, 1.25)
		_pitch = clampf(_pitch + rel.y * 0.005, -0.1, 1.2)


func _end(pos: Vector2) -> void:
	if not _pressing:
		return
	_pressing = false
	var b := state.bonsai
	var moved := pos.distance_to(_press) > 12.0
	match _drag:
		"prune":
			if pruning.target >= 0:
				pruning.cut()
			_drag = ""
			return
		"wire":
			_preview_mesh.clear_surfaces()
			_drag = ""
			var g := b.graph
			if b.graph.get_flag(_wire_id, "wire") is Dictionary and not moved:
				b.unwire(_wire_id)
				_play("wire")
				refresh(true)
				tool_used.emit("unwire")
			elif moved:
				var pivot := g.positions[g.parents[_wire_id]]
				if b.wire(_wire_id, _wire_target - pivot):
					_play("wire")
					refresh(true)
					tool_used.emit("wire")
			return
	if _drag == "orbit":
		_drag = ""
		return
	if tool == "pinch" and not moved:
		var id := pick_tip(pos)
		if id >= 0 and b.pinch(id):
			_play("snip")
			refresh(true)
			tool_used.emit("pinch")


## The nearest living branch segment to a screen point (graph id, or -1).
func pick_branch(screen: Vector2) -> int:
	var b := state.bonsai
	var g := b.graph
	var best := -1
	var best_d := PICK_RADIUS
	for id in range(2, g.size()):
		if b.is_dead(id) or b.is_jin(id):
			continue
		var a := _plant.to_global(g.positions[g.parents[id]])
		var c := _plant.to_global(g.positions[id])
		if camera.is_position_behind(a) or camera.is_position_behind(c):
			continue
		var d := Geometry2D.get_closest_point_to_segment(screen, camera.unproject_position(a), camera.unproject_position(c)).distance_to(screen)
		# A wired branch is easy to find again (tap to take the wire off).
		if g.get_flag(id, "wire") is Dictionary:
			d *= 0.6
		if d < best_d:
			best_d = d
			best = id
	return best


## The nearest fresh tip (pinching), or -1.
func pick_tip(screen: Vector2) -> int:
	var b := state.bonsai
	var best := -1
	var best_d := PICK_RADIUS
	for id in b.living_tips():
		if not b.is_fresh_tip(id):
			continue
		var p := _plant.to_global(b.graph.positions[id])
		if camera.is_position_behind(p):
			continue
		var d := camera.unproject_position(p).distance_to(screen)
		if d < best_d:
			best_d = d
			best = id
	return best


## Where a drag points in the plant's frame: on the plane through the branch's base that
## faces the camera.
func _drag_point(screen: Vector2) -> Vector3:
	var g := state.bonsai.graph
	var pivot := _plant.to_global(g.positions[g.parents[_wire_id]])
	var from := camera.project_ray_origin(screen)
	var dir := camera.project_ray_normal(screen)
	var n := -camera.global_transform.basis.z
	var denom := dir.dot(n)
	var hit := pivot if absf(denom) < 1e-5 else from + dir * ((pivot - from).dot(n) / denom)
	return _plant.to_local(hit)


func _draw_wire_preview() -> void:
	var g := state.bonsai.graph
	_preview_mesh.clear_surfaces()
	_preview_mesh.surface_begin(Mesh.PRIMITIVE_LINES)
	var pivot := g.positions[g.parents[_wire_id]]
	_preview_mesh.surface_add_vertex(pivot)
	_preview_mesh.surface_add_vertex(pivot + (_wire_target - pivot).normalized() * maxf(1.0, g.positions[_wire_id].distance_to(pivot) * 3.0))
	_preview_mesh.surface_end()


# --- Pruning's host ----------------------------------------------------------------------

func pruning_graph() -> PlantGraph:
	return state.bonsai.graph


func pruning_camera() -> Camera3D:
	return camera


func pruning_bark() -> Material:
	return _bark


func pruning_cut(id: int) -> int:
	return state.bonsai.prune(id)


# --- the tools -------------------------------------------------------------------------

func _play(kind: String, seconds: float = 0.0) -> void:
	if not _sounds.has(kind):
		return
	_player.stream = _sounds[kind]
	_player.global_position = to_global(FOCUS)
	_player.play()
	if seconds > 0.0:
		get_tree().create_timer(seconds).timeout.connect(_player.stop)


## The watering can lifts, tilts over the pot and pours; the soil darkens.
func water() -> void:
	if busy or sim() == null:
		return
	busy = true
	var tw := create_tween()
	var over := Transform3D(Basis(Vector3.UP, -PI * 0.5), Vector3(0.14, 0.2, -0.02))
	var tilt := over.basis * Basis(Vector3.RIGHT, 0.7)
	tw.tween_property(_can, "transform", Transform3D(over.basis.scaled(Vector3.ONE * 0.42), over.origin), 0.5).set_trans(Tween.TRANS_SINE)
	tw.tween_property(_can, "transform", Transform3D(tilt.scaled(Vector3.ONE * 0.42), over.origin + Vector3(0, -0.01, 0)), 0.35)
	tw.tween_callback(func() -> void:
		# From the rose at the end of the spout.
		_water.global_position = _can.to_global(Vector3(0.0, 0.16, 0.27))
		_water.emitting = true
		_play("water", 1.4))
	tw.tween_interval(0.7)
	tw.tween_callback(func() -> void:
		sim().water()
		tool_used.emit("water"))
	tw.tween_interval(0.6)
	tw.tween_callback(func() -> void: _water.emitting = false)
	tw.tween_property(_can, "transform", _can_rest, 0.6).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: busy = false)


## The tin tilts over the pot and a spoon of pellets of one nutrient lands on the soil.
func fertilise(kind: int) -> void:
	if busy or sim() == null:
		return
	busy = true
	var tw := create_tween()
	var over := Vector3(-0.05, 0.14, 0.0)
	tw.tween_property(_tin, "position", over, 0.45).set_trans(Tween.TRANS_SINE)
	tw.tween_property(_tin, "rotation:z", -1.2, 0.3)
	tw.tween_callback(func() -> void:
		_play("pellets", 0.8)
		var burnt := sim().fertilise(kind)
		refresh(true)
		tool_used.emit("burn" if burnt > 0 else "fertiliser"))
	tw.tween_interval(0.4)
	tw.tween_property(_tin, "rotation:z", 0.0, 0.3)
	tw.tween_property(_tin, "transform", _tin_rest, 0.45).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: busy = false)


## Turns the pot a quarter (the window side changes).
func turn_pot(step: int) -> void:
	if busy or sim() == null:
		return
	busy = true
	_play("pot")
	_turn_angle += step * PI * 0.5
	var tw := create_tween()
	tw.tween_property(_turn_node, "rotation:y", _turn_angle, 0.6).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void:
		sim().turn_pot(step)
		busy = false
		refresh(true)
		tool_used.emit("turn"))


# --- repotting (section 16 B): lift out, trim the root ball, pick the pot, fresh soil ------

func repot_lift() -> void:
	if busy or _lifted or sim() == null:
		return
	_lifted = true
	_trim = 0.0
	_new_pot = sim().pot
	_build_root_ball()
	_root_ball_visible(true)
	_play("pot")
	var tw := create_tween()
	busy = true
	tw.tween_property(_lift, "position:y", 0.12, 0.7).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: busy = false)


## One snip round the root ball: the long circling roots get shorter. Returns the share cut.
func repot_trim() -> float:
	if not _lifted:
		return _trim
	_trim = minf(1.0, _trim + 0.34)
	_play("snip")
	_build_root_ball()
	return _trim


func repot_pick(pot_id: String) -> void:
	if not _lifted or not BonsaiSim.POTS.has(pot_id):
		return
	_new_pot = pot_id
	_show_pot(pot_id)
	_play("pot")


## Fresh soil and down into the pot: the simulation repots.
func repot_finish() -> void:
	if not _lifted or busy:
		return
	busy = true
	var tw := create_tween()
	tw.tween_property(_lift, "position:y", 0.0, 0.6).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void:
		sim().repot(_new_pot, _trim)
		_lifted = false
		_root_ball_visible(false)
		busy = false
		refresh(true)
		tool_used.emit("repot"))


func is_lifted() -> bool:
	return _lifted


func trim_share() -> float:
	return _trim


func _root_ball_visible(on: bool) -> void:
	_root_ball.visible = on
	if not on:
		for c in _root_ball.get_children():
			c.queue_free()


## The root ball: the pot's shape in soil, with roots circling out of it (more the fuller the
## pot), shortened by each trim.
func _build_root_ball() -> void:
	for c in _root_ball.get_children():
		c.queue_free()
	var b := sim()
	var y := soil_height(b.pot)
	var half := soil_half(b.pot)
	var depth := y - 0.012
	var ball := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 1.0
	cyl.bottom_radius = 0.8
	cyl.height = depth
	cyl.radial_segments = 24
	ball.mesh = cyl
	ball.scale = Vector3(half.x, 1.0, half.y)
	var soil := StandardMaterial3D.new()
	soil.albedo_color = Color(0.22, 0.16, 0.11)
	soil.albedo_texture = load("res://assets/bonsai/Gravel022_Color.jpg")
	soil.normal_enabled = true
	soil.normal_texture = load("res://assets/bonsai/Gravel022_NormalGL.jpg")
	soil.roughness = 1.0
	ball.material_override = soil
	ball.position = Vector3(0, y - depth * 0.5, 0)
	_root_ball.add_child(ball)
	# Roots as a small plant graph (drawn like the wood), circling the ball and hanging below.
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([b.seed, "root ball", b.day()])
	var g := PlantGraph.new(Vector3(0, y - depth * 0.5, 0), 900)
	var strands := 10 + int(30.0 * clampf(b.root_fill, 0.0, 1.5))
	var keep := 1.0 - _trim * 0.8
	for _s in range(strands):
		var a := rng.randf() * TAU
		var h := rng.randf_range(0.1, 0.95)
		var ring := Vector3(cos(a) * half.x, y - depth * h, sin(a) * half.y)
		var last := g.add_node(0, ring * Vector3(0.98, 1.0, 0.98))
		var steps := int(rng.randf_range(4, 10) * keep)
		for k in range(steps):
			# Round the ball, and at the bottom hanging free.
			a += 0.28
			var p := Vector3(cos(a) * half.x * (1.02 + k * 0.004), g.positions[last].y - rng.randf_range(0.0, 0.01), sin(a) * half.y * (1.02 + k * 0.004))
			if p.y < y - depth:
				p = g.positions[last] + Vector3(rng.randf_range(-0.01, 0.01), -0.012, rng.randf_range(-0.01, 0.01))
			var n := g.add_node(last, p)
			if n < 0:
				break
			last = n
	for id in range(g.size()):
		g.radii[id] = 0.0012 if id > 0 else 0.0
	var rb := BranchMeshBuilder.new()
	rb.radius_scale = 1.0
	rb.min_radius = 0.0011
	var roots := MeshInstance3D.new()
	roots.mesh = rb.build(g)
	var rm := StandardMaterial3D.new()
	rm.albedo_color = Color(0.62, 0.5, 0.38)
	rm.roughness = 0.8
	roots.material_override = rm
	_root_ball.add_child(roots)


# --- photos and screens ----------------------------------------------------------------

## For tools: a point of the plant on screen.
func plant_screen_position(id: int) -> Vector2:
	return camera.unproject_position(_plant.to_global(state.bonsai.graph.positions[id]))


## For tools: the orbit (yaw, pitch, distance) at once, without the glide.
func look_from(yaw: float, pitch: float, dist: float, focus: Vector3 = FOCUS) -> void:
	_yaw = yaw
	_pitch = pitch
	_dist = dist
	_focus = focus
	_blend = 1.0


## For tools: where a node of the plant is, in the sill's frame (a focus for look_from).
func node_in_sill(id: int) -> Vector3:
	return to_local(_plant.to_global(state.bonsai.graph.positions[id]))
