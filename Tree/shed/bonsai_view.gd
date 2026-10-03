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
## A tool was picked up from the sill (its first-time page).
signal tool_picked(id: String)
## The sketchbook, the album card or the box of cuttings was tapped (their pages).
signal object_tapped(id: String)
## A short word for the status scrap ("not yet", "choose N, P or K first").
signal said(text: String)
## A tap below the windowsill: back to the bench (0.8.2.2, Simon: "zurück zur Werkbank, sobald man
## unterhalb der Fensterbank tippt").
signal back_requested

## Window side is +Z of the sill anchor (Shed.bonsai_spot); the room is -Z.
const FOCUS := Vector3(0.0, 0.14, -0.06)
## The default look: far enough that pot and crown sit between the status scrap and the tools.
const DIST := 0.8
## The default look down onto the sill: pot, crown and the row of tools before it.
const PITCH := 0.45
## How near a tip or a branch a touch must land (canvas pixels; 0.8.1: 44 was a fingertip's own
## radius, so a touch just beside a tip missed it).
const PICK_RADIUS := 56.0
## A coiled branch is found this much more easily than bare wood (its distance counts this share).
const WIRE_PICK_BIAS := 0.45
## The tap radius of the things on the sill: finger size on a phone (docs/notes/bonsai-tools-0.7.md).
const TOOL_TAP := 52.0
## A held tool's place on the sill is a larger target to put it back (still the nearest thing wins).
const PUT_DOWN := TOOL_TAP * 1.4
## With a tool in hand over something it can work on, another thing on the sill answers only this
## close to its mark (or inside its outline), so a touch on the soil next to the can waters.
const OBJECT_NEAR_TARGET := TOOL_TAP * 0.6
## How far a finger may wander and still tap (canvas pixels; 0.8.1: 10 to 12 px, about 1 mm on the
## phone, turned many taps into tiny turns of the view; 28 px is about 2.6 mm on a 1080 x 2400 phone).
const TAP_SLOP := 28.0
## The close-up's field of view (vertical) on a 720 x 1280 screen; a narrower screen widens it so
## the whole sill fits (0.8.1, item 18), see base_fov().
const FOV := 50.0
## The tool in hand is drawn at this share of its distance from the eye (and as much smaller).
const HOLD_NEARER := 0.55
## The windowsill board's front edge (the bottom of its front face) in this node's frame (the
## bonsai's spot on the sill): Shed's sill board, 0.52 m wide, 0.32 m in front of the spot.
## A tap below it on screen goes back to the bench.
const SILL_EDGE_X := 0.26
const SILL_EDGE := Vector3(0.0, -0.035, -0.32)
## The copper wire: its thickness and one coil turn, in bonsai units.
const WIRE_RADIUS := 0.009
const WIRE_PITCH := 0.14
## The pots as drawn: footprint half size (m), height, corner roundness (superellipse power),
## glaze colour and roughness. The nursery pot is the Poly Haven clay planter.
const POT_LOOKS := {
	"nursery": {"model": true, "scale": 0.6, "soil": 0.112, "half": Vector2(0.066, 0.066), "n": 2.0},
	"rectangle": {"half": Vector2(0.11, 0.08), "h": 0.058, "n": 7.0, "color": Color(0.26, 0.245, 0.235), "clay": Color(0.27, 0.25, 0.24), "glaze": 0.0, "rough": 0.9},
	"oval": {"half": Vector2(0.1, 0.075), "h": 0.055, "n": 2.0, "color": Color(0.1, 0.19, 0.38), "clay": Color(0.5, 0.4, 0.32), "glaze": 1.0, "rough": 0.22},
	"round": {"half": Vector2(0.078, 0.078), "h": 0.07, "n": 2.0, "color": Color(0.42, 0.56, 0.44), "clay": Color(0.52, 0.42, 0.34), "glaze": 1.0, "rough": 0.18},
	"cascade": {"half": Vector2(0.062, 0.062), "h": 0.15, "n": 5.0, "color": Color(0.84, 0.78, 0.64), "clay": Color(0.55, 0.43, 0.33), "glaze": 1.0, "rough": 0.25},
}
const JUNIPER_COLOR := "res://lookdev/bonsai/juniper_spray_color.png"
const JUNIPER_NORMAL := "res://lookdev/bonsai/juniper_spray_normal.png"
## The juniper atlas is read one and a half mips sharper (0.8.2, look review: at phone size the
## tufts blurred into lobed leaf cards).
const JUNIPER_DETAIL_BIAS := -1.5
## The roster's leaf sprays, a mipmapped copy of the crown atlas (make_juniper.py writes it).
const LEAF_COLOR := "res://lookdev/bonsai/leaf_spray_small_color.png"
const LEAF_NORMAL := "res://lookdev/bonsai/leaf_spray_small_normal.png"
const FOLIAGE_SHADER := preload("res://lookdev/bonsai/bonsai_foliage.gdshader")

var state: GameState
var camera: Camera3D
## Bonsai mode: the close camera is on and the tools answer.
var active: bool = false
var input_enabled: bool = true
## The tool in hand (BonsaiTools.HELD: "water", "fertiliser", "shears", "pinch" (the
## tweezers), "wire", "trowel"), or "".
var tool: String = ""
var pruning: Pruning
## A tool animation is playing (watering, pellets, turning, repotting).
var busy: bool = false
## The pellet tin was tapped: its slip is open, a tap on N, P or K there pours a spoon of it
## (0.8.2.2, Simon: "die Pellets sollten direkt nach Auswahl gestreut werden").
var tin_open: bool = false
## Spoons asked for on the slip while the tin still pours (each follows when it is back up).
var _spoons: Array[int] = []

var _base: Node3D
var _turn_node: Node3D
var _turn_angle: float = 0.0
var _pot_node: Node3D
var _soil: MeshInstance3D
var _soil_mat: ShaderMaterial
## The grit on the soil (the moss itself grows in the soil's shader).
var _moss: MultiMeshInstance3D
var _pellets: MultiMeshInstance3D
var _lift: Node3D
var _plant: Node3D
var _wood: MeshInstance3D
var _bark: ShaderMaterial
var _foliage: MultiMeshInstance3D
## Each pad's dark inner mass, so a pad reads as a dense cloud and not a scatter of tufts.
var _cores: MultiMeshInstance3D
## 0.8.2.7 (Simon: "green circles on the leaves after tapping the tree again"): the cores showed
## through the gaps between the cards as smooth green ovals, and came back each time the shears
## or the wire were put down (they hide while those are in hand). They stay hidden now.
const SHOW_CORES := false
var _spray_mat: ShaderMaterial
var _wires: MeshInstance3D
var _root_ball: Node3D
## Repot time shows on the pot itself (0.8, C4): the soil pushed up, roots circling on it at the
## rim and a few peeking out under the pot's foot. Built when the bonsai asks to be repotted.
var _rootbound: Node3D
var _rootbound_key: String = ""
var _rootbound_shown: bool = false
## How far the soil rises when the roots fill the pot.
const ROOTBOUND_LIFT: float = 0.007
## How far the old soil sinks in the empty pot while the tree is out (it stays under the rim).
const LIFTED_SOIL_DROP: float = 0.004
## The tools on the sill (the can, the pellet tin, secateurs, tweezers, wire, trowel...).
var tools: BonsaiTools
## The pellets the tin gave last (0 N, 1 P, 2 K), chosen on its paper slip and ringed there. Kept
## in the bonsai's save (BonsaiSim.pellet_kind); before the first choice the slip rings what the
## soil holds least of (BonsaiSim.tin_kind).
var pellet_kind: int:
	get:
		return sim().tin_kind() if sim() != null else 0
	set(kind):
		if sim() != null and kind >= -1 and kind <= 2:
			sim().pellet_kind = kind
## The sill thing under the pointer (for its label on a PC), "" if none.
var hover: String = ""
## What the tool in hand aims at now (0.8.1): {} or {"kind": "soil" | "pot" | "root_ball" | "cut" |
## "tip" | "branch", "id": graph id}. Shown before anything happens; the act comes on release.
var aimed: Dictionary = {}
## How many times the tool in hand was put to work since it was picked up (for tests).
var uses: int = 0
var _soil_ring: MeshInstance3D
var _tip_ring: MeshInstance3D
var _fov_aspect: float = -1.0
var _fov: float = FOV
var _depth: float = -1.0
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
var _pitch: float = PITCH
var _dist: float = DIST
var _focus: Vector3 = FOCUS
var _blend: float = 1.0
var _from: Transform3D
var _from_fov: float = 50.0
var _press: Vector2
var _pressing: bool = false
## The sill thing a press began on (a tap on it picks it up or opens it).
var _down_object: String = ""
## The press began on the pot (a sideways drag turns it).
var _pot_press: bool = false
var _pointer: Vector2
var _pick_at: Vector2
## The pointer moved since the tool was picked up: from then on the tool follows it.
var _hold_moved: bool = false
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
	_cores = MultiMeshInstance3D.new()
	var cm := MultiMesh.new()
	cm.transform_format = MultiMesh.TRANSFORM_3D
	cm.use_colors = true
	var blob := SphereMesh.new()
	blob.radial_segments = 12
	blob.rings = 6
	blob.radius = 1.0
	blob.height = 2.0
	cm.mesh = blob
	_cores.multimesh = cm
	var core_mat := StandardMaterial3D.new()
	core_mat.vertex_color_use_as_albedo = true
	# The phone's renderer draws these darker: its colours are taken as they are.
	core_mat.vertex_color_is_srgb = not Budgets.PHONE
	core_mat.roughness = 1.0
	core_mat.diffuse_mode = BaseMaterial3D.DIFFUSE_LAMBERT_WRAP
	core_mat.metallic_specular = 0.0
	_cores.material_override = core_mat
	_plant.add_child(_cores)
	_wires = MeshInstance3D.new()
	var copper := StandardMaterial3D.new()
	copper.albedo_color = Color(0.74, 0.42, 0.24)
	copper.metallic = 0.9
	copper.roughness = 0.35
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
	_build_aim_marks()
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
	_rootbound = Node3D.new()
	_turn_node.add_child(_rootbound)
	_build_tools()
	_build_warm_up()
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
	_soil_mat = ShaderMaterial.new()
	_soil_mat.shader = preload("res://lookdev/bonsai/bonsai_soil.gdshader")
	_soil_mat.set_shader_parameter("gravel", load("res://assets/bonsai/Gravel022_Color.jpg"))
	_soil_mat.set_shader_parameter("gravel_normal", load("res://assets/bonsai/Gravel022_NormalGL.jpg"))
	_soil_mat.set_shader_parameter("moss", load("res://assets/bonsai/Moss002_Color.jpg"))
	_soil_mat.set_shader_parameter("moss_normal", load("res://assets/bonsai/Moss002_NormalGL.jpg"))
	_soil.material_override = _soil_mat
	_pot_node.add_child(_soil)
	# Fine grit on the soil: small stones of akadama, pumice and lava in a few colours.
	_moss = MultiMeshInstance3D.new()
	var gm := MultiMesh.new()
	gm.transform_format = MultiMesh.TRANSFORM_3D
	gm.use_colors = true
	var grain := SphereMesh.new()
	grain.radius = 1.0
	grain.height = 1.4
	grain.radial_segments = 5
	grain.rings = 3
	gm.mesh = grain
	_moss.multimesh = gm
	var grit := StandardMaterial3D.new()
	grit.vertex_color_use_as_albedo = true
	grit.vertex_color_is_srgb = true
	grit.roughness = 0.95
	_moss.material_override = grit
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


## The aim's highlights (0.8.1, item 18): a soft warm ring lying round the soil, and a ring
## facing the eye round a tip. Drawn over everything, so a finger or the tool never hides them.
func _build_aim_marks() -> void:
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.no_depth_test = true
	mat.albedo_texture = BonsaiTools.ring_texture(false)
	mat.albedo_color = Color(1.0, 0.78, 0.35, 0.95)
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.render_priority = 2
	_soil_ring = MeshInstance3D.new()
	var q := QuadMesh.new()
	q.orientation = PlaneMesh.FACE_Y
	q.size = Vector2(1.0, 1.0)
	_soil_ring.mesh = q
	_soil_ring.material_override = mat
	_soil_ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_soil_ring.visible = false
	_turn_node.add_child(_soil_ring)
	_tip_ring = MeshInstance3D.new()
	var tq := QuadMesh.new()
	tq.size = Vector2(0.05, 0.05)
	_tip_ring.mesh = tq
	var tm := mat.duplicate() as StandardMaterial3D
	tm.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	_tip_ring.material_override = tm
	_tip_ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_tip_ring.visible = false
	_tip_ring.top_level = true
	add_child(_tip_ring)


## The tools lie on the sill beside the pot (shed/bonsai_tools.gd).
func _build_tools() -> void:
	tools = BonsaiTools.new()
	_base.add_child(tools)


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
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/bonsai/bonsai_pot.gdshader")
	mat.set_shader_parameter("glaze_colour", look["color"])
	mat.set_shader_parameter("clay_colour", look["clay"])
	mat.set_shader_parameter("glaze", look["glaze"])
	mat.set_shader_parameter("glaze_roughness", look["rough"])
	var feet := 0.008
	var wall := 0.008
	var inner := 1.0 - wall / half.x
	# Profile (height, scale of the footprint, edge 0..1) from the foot band up the slightly
	# flaring wall, out over a thick lip band, and down the inside to the soil.
	var profile: Array[Vector3] = [Vector3(feet, 0.9, 1.0), Vector3(feet, 0.955, 1.0), Vector3(feet + 0.004, 0.965, 1.0),
		Vector3(feet + 0.006, 0.955, 0.3), Vector3(feet + h * 0.3, 0.97, 0.0), Vector3(feet + h * 0.74, 0.995, 0.0),
		Vector3(feet + h * 0.78, 1.025, 0.6), Vector3(feet + h * 0.96, 1.035, 0.8), Vector3(feet + h, 1.02, 1.0),
		Vector3(feet + h, inner, 1.0), Vector3(feet + h - 0.012, inner, 0.0)]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var seg := 64
	for i in range(seg):
		for k in range(profile.size() - 1):
			var quad: Array[Vector3] = []
			var cols: Array[Color] = []
			for c: Array in [[i, k], [i + 1, k], [i + 1, k + 1], [i, k + 1]]:
				var a := TAU * float(c[0]) / seg
				var pr: Vector3 = profile[c[1]]
				var r := _superellipse(a, half, power) * pr.y
				quad.append(Vector3(cos(a) * r.x, pr.x, sin(a) * r.y))
				cols.append(Color(clampf((pr.x - feet) / h, 0.0, 1.0), pr.z, 0.0))
			# 0.8.2.2 (Simon: repotting showed graphics errors): wound so the outside faces out; the
			# glazed pots were drawn inside out (the front wall culled, the soil and pellets seen
			# through it, the root ball as if round the pot).
			for q in [0, 1, 2, 0, 2, 3]:
				st.set_color(cols[q])
				st.add_vertex(quad[q])
	# The floor under the pot.
	for i in range(seg):
		var a0 := TAU * float(i) / seg
		var a1 := TAU * float(i + 1) / seg
		var r0 := _superellipse(a0, half, power) * 0.9
		var r1 := _superellipse(a1, half, power) * 0.9
		st.set_color(Color(0, 1, 0))
		st.add_vertex(Vector3(0, feet, 0))
		st.add_vertex(Vector3(cos(a0) * r0.x, feet, sin(a0) * r0.y))
		st.add_vertex(Vector3(cos(a1) * r1.x, feet, sin(a1) * r1.y))
	st.generate_normals()
	var body := MeshInstance3D.new()
	body.mesh = st.commit()
	body.material_override = mat
	n.add_child(body)
	# Cloud feet under the corners: low rounded pads of the same clay.
	var foot_mat := StandardMaterial3D.new()
	foot_mat.albedo_color = (look["clay"] as Color).darkened(0.1)
	foot_mat.roughness = 0.9
	for sx in [-1.0, 1.0]:
		for sz in [-1.0, 1.0]:
			var foot := MeshInstance3D.new()
			var b := CylinderMesh.new()
			b.top_radius = 0.011
			b.bottom_radius = 0.009
			b.height = feet
			b.radial_segments = 12
			b.rings = 1
			foot.mesh = b
			foot.material_override = foot_mat
			foot.scale = Vector3(1.0, 1.0, 0.75)
			foot.position = Vector3(sx * half.x * 0.66, feet * 0.5, sz * half.y * 0.66)
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
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([state.bonsai.seed if state and state.bonsai else 0, "moss", id])
	_soil_mat.set_shader_parameter("moss_seed", Vector2(rng.randf() * 50.0, rng.randf() * 50.0))
	_soil_mat.set_shader_parameter("moss_cover", 0.25 if id == "nursery" else 0.45)
	_scatter_grit(rng, y, half, power)
	_plant.position = Vector3(0, y, 0)
	_shown_pot = id


## Grit on the soil's dome: a few hundred small stones, darker and smaller toward the rim.
func _scatter_grit(rng: RandomNumberGenerator, y: float, half: Vector2, power: float) -> void:
	var mm := _moss.multimesh
	mm.instance_count = 160 if Budgets.PHONE else 420
	var colours: Array[Color] = [Color(0.4, 0.26, 0.17), Color(0.46, 0.36, 0.27), Color(0.24, 0.21, 0.2),
		Color(0.5, 0.47, 0.42), Color(0.32, 0.18, 0.13)]
	for i in range(mm.instance_count):
		var a := rng.randf() * TAU
		var d := sqrt(rng.randf()) * 0.94
		var rim := _superellipse(a, half, power)
		var at := Vector3(cos(a) * rim.x * d, y + 0.006 * (1.0 - d), sin(a) * rim.y * d)
		var size := rng.randf_range(0.0008, 0.0017)
		var basis := Basis.from_euler(Vector3(rng.randf() * TAU, rng.randf() * TAU, rng.randf() * TAU))
		basis = basis.scaled(Vector3(size * rng.randf_range(0.8, 1.3), size * rng.randf_range(0.5, 0.9), size))
		mm.set_instance_transform(i, Transform3D(basis, at))
		var c: Color = colours[rng.randi() % colours.size()]
		mm.set_instance_color(i, c * rng.randf_range(0.6, 0.95))


# --- keeping up with the simulation ----------------------------------------------------

func _process(delta: float) -> void:
	visible = state != null and state.bonsai != null
	# Out of the shed (the tree's day) nothing of it is drawn: no rebuilds either.
	if not is_visible_in_tree():
		return
	if _warm != null:
		_warm_frames += 1
		if _warm_frames > WARM_FRAMES:
			_warm.queue_free()
			_warm = null
	var b := state.bonsai
	_timer += delta
	if _timer >= 0.5:
		_timer = 0.0
		refresh(false)
	# Wet soil is darker, dry soil pale.
	var wet := clampf((b.moisture - 0.1) / 0.8, 0.0, 1.0)
	_soil_mat.set_shader_parameter("wet", wet)
	var sun := b.clock.sun_height()
	_light.light_energy = 0.25 + 1.4 * sun
	# By night the lantern's warm light, by day the window's.
	_light.light_color = Color(1.0, 0.72, 0.45).lerp(Color(1.0, 0.93, 0.8), clampf(sun * 4.0, 0.0, 1.0))
	# With the wire or the shears the foliage in front of the branch thins out, so the wood
	# shows (the leaves dissolve toward the camera).
	if _spray_mat != null:
		var see_wood := active and (tool == "wire" or tool == "shears")
		_spray_mat.set_shader_parameter("near_fade", _dist * 0.9 if see_wood else 0.0)
		_fade = move_toward(_fade, 0.6 if see_wood else 0.0, delta * 3.0)
		# The pads' dark cores go with the first cards, so the branch shows through.
		_cores.visible = SHOW_CORES and _fade < 0.15
		_spray_mat.set_shader_parameter("fade", _fade)
	_update_camera(delta)
	_follow_pointer(delta)


## The tool in hand: lifted where it lay, then following the pointer with its working end on
## the point aimed at (the pointer itself, at the depth of what it aims at or of the tree), its
## body reaching up and aside, clear of the finger (BonsaiTools.hold_pose).
func _follow_pointer(delta: float) -> void:
	if not active or tool == "" or busy or tools.held != tool:
		return
	tools.follow(_hold_target(), delta)


func _hold_target() -> Transform3D:
	if not _hold_moved:
		return tools.lifted_pose(tool)
	var at := camera.project_position(_pointer, _aim_depth())
	var inv := tools.global_transform.affine_inverse()
	var cb := camera.global_transform.basis
	var eye := inv * camera.global_position
	var pose := tools.hold_pose(tool, inv * at, (inv.basis * cb.x).normalized(), (inv.basis * cb.y).normalized(), eye)
	# Drawn nearer the eye and smaller by the same share, so it looks just the same on screen but
	# lies in front of the crown and the trunk, never half behind them.
	return Transform3D(pose.basis * HOLD_NEARER, eye + (pose.origin - eye) * HOLD_NEARER)


## How far from the eye the tool in hand works: at what it aims at (or last aimed at), else at
## the tree.
func _aim_depth() -> float:
	var p := to_global(_focus)
	if aimed.has("id") and int(aimed["id"]) >= 0 and int(aimed["id"]) < sim().graph.size():
		p = _plant.to_global(sim().graph.positions[int(aimed["id"])])
	elif aimed.has("kind"):
		p = _base.to_global(Vector3(0, soil_height(sim().pot) + (0.12 if _lifted else 0.0), 0))
	elif _depth > 0.0:
		return _depth
	_depth = maxf(camera.global_transform.basis.z.dot(camera.global_position - p), 0.05)
	return _depth


## The screen point of the working end of the tool in hand (for tests: it sits on the aim).
func tool_tip_screen() -> Vector2:
	return camera.unproject_position(tools.to_global(tools.tip_point(tool))) if tool != "" else Vector2(-1, -1)


## The screen point of the body of the tool in hand (for tests: it is clear of the finger).
func tool_body_screen() -> Vector2:
	if tool == "":
		return Vector2(-1, -1)
	var n: Node3D = tools.items[tool]
	return camera.unproject_position(n.global_transform * (tools.boxes[tool] as AABB).get_center())


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
	_update_rootbound(b)
	var sig := [b.graph.size(), b.leafy_count(), b.day(), b.wired().size(), int(b.droop() * 4.0), b.turn, b.pot,
		int(b.hunger(0) * 4.0), int(b.hunger(1) * 4.0), int(b.hunger(2) * 4.0)]
	if not force and sig == _sig:
		return
	_sig = sig
	if not busy:
		_turn_angle = b.turn * PI * 0.5
		_turn_node.rotation.y = _turn_angle
	var pads := _pads(b)
	var look := _look_graph(b, pads)
	_wood.mesh = _build_wood(b, look)
	_populate_foliage(b, pads)
	_wires.mesh = _build_wires(b, look)
	_update_pellets(b)


func _apply_species(sp: Species) -> void:
	_shown_species = sp.id
	var b := sp.bark_tint
	if sp.conifer:
		# Red-brown bark peeling in long strips that twist up the trunk.
		# The phone's renderer shows the tint redder.
		_bark.set_shader_parameter("texture_tint", Vector3(b.r * (0.64 if Budgets.PHONE else 0.74), b.g * 0.62, b.b * 0.6))
		_bark.set_shader_parameter("twist", 0.3)
		_bark.set_shader_parameter("fibre", 1.0)
	else:
		_bark.set_shader_parameter("texture_tint", Vector3(b.r, b.g, b.b) * 1.2)
		_bark.set_shader_parameter("twist", 0.0)
		_bark.set_shader_parameter("fibre", 0.0)
	_spray_mat = ShaderMaterial.new()
	_spray_mat.shader = FOLIAGE_SHADER
	_spray_mat.set_shader_parameter("cheap", Budgets.PHONE)
	if sp.conifer:
		_spray_mat.set_shader_parameter("spray_color", _mipmapped(JUNIPER_COLOR))
		_spray_mat.set_shader_parameter("spray_normal", _mipmapped(JUNIPER_NORMAL))
		_spray_mat.set_shader_parameter("translucency", Color(0.22, 0.3, 0.08))
		_spray_mat.set_shader_parameter("detail_bias", JUNIPER_DETAIL_BIAS)
	else:
		_spray_mat.set_shader_parameter("spray_color", _mipmapped(LEAF_COLOR))
		_spray_mat.set_shader_parameter("spray_normal", _mipmapped(LEAF_NORMAL))
		_spray_mat.set_shader_parameter("translucency", Color(0.4, 0.5, 0.1))
	_spray_mat.set_shader_parameter("tint_mul", sp.leaf_tint * (1.22 if sp.conifer else 1.05))
	_foliage.material_override = _spray_mat


## Pads (design doc 16, the reference juniper): the living twigs gathered by branch into cloud
## pads at the branch ends, with air between them. A pad is the subtree of the first node (from
## the trunk out) that carries at most PAD_MAX green segments; tiny ones join the nearest pad.
## Only the look: the simulation's graph is never changed.
const PAD_MAX: int = 34
const PAD_MIN: int = 5
## Twigs thinner than this (sim units) inside a pad are hidden under its foliage.
const PAD_TWIG: float = 0.02
## The drawn trunk's height to its radius (sim units): with the builder's radius_scale its
## diameter at the foot (above the flare) is about a seventh of the tree's height, as in the
## classic proportion of 1:6 to 1:8 (0.8.1, item 32).
const TRUNK_SLENDER: float = 21.0


class Pad:
	var root: int
	var members: Array[int] = []
	var centre: Vector3
	## Horizontal radius and half height of the pad's dome, and its up (tilted with the branch).
	var radius: float
	var half_height: float
	var up: Vector3 = Vector3.UP
	var out: Vector3 = Vector3.FORWARD


## The pads of the bonsai (see Pad), outermost segments weighted, for the current graph.
func _pads(b: BonsaiSim) -> Array[Pad]:
	var g := b.graph
	var n := g.size()
	var green := PackedInt32Array()
	green.resize(n)
	for id in range(n):
		green[id] = 0 if b.is_dead(id) or b.is_jin(id) else 1
	var count := PackedInt32Array()
	count.resize(n)
	for id in range(n - 1, -1, -1):
		count[id] += green[id]
		if g.parents[id] >= 0:
			count[g.parents[id]] += count[id]
	var trunk := b.trunk_chain()
	# The broadleaves carry fewer, larger clumps: a small leafy crown, not clouds on arms.
	var pad_max := PAD_MAX if b.species.conifer else PAD_MAX * 2
	var roots: Array[int] = []
	var stack: Array[int] = [0]
	while not stack.is_empty():
		var id: int = stack.pop_back()
		if green[id] == 0 and id != 0:
			continue
		if id != 0 and count[id] <= pad_max and (not trunk.has(id) or count[id] <= pad_max * 0.6):
			roots.append(id)
			continue
		for c in g.children[id]:
			stack.append(c)
	var pads: Array[Pad] = []
	var small: Array[Pad] = []
	var conifer := b.species.conifer
	var top := maxf(b.height(), 0.5)
	for r in roots:
		var pad := Pad.new()
		pad.root = r
		for s in b.subtree(r):
			if green[s] == 1:
				pad.members.append(s)
		if pad.members.is_empty():
			continue
		(pads if pad.members.size() >= PAD_MIN else small).append(pad)
	if pads.is_empty():
		pads = small
		small = []
	for pad in pads:
		_shape_pad(g, pad, conifer, top)
	# Tiny pads join the nearest real pad (a few twigs make it denser, not a pad of their own).
	for sp in small:
		var at := g.positions[sp.members[sp.members.size() - 1]]
		var best: Pad = null
		var best_d := INF
		for pad in pads:
			var d := at.distance_to(pad.centre) - pad.radius
			if d < best_d:
				best_d = d
				best = pad
		if best != null and best_d < 0.45:
			best.members.append_array(sp.members)
		else:
			_shape_pad(g, sp, conifer, top)
			pads.append(sp)
	return pads


## The dome of a pad from its members: centred toward the outer twigs, wide and flat for the
## juniper, rounder for the broadleaves.
func _shape_pad(g: PlantGraph, pad: Pad, conifer: bool, top: float) -> void:
	var base := g.positions[g.parents[pad.root]]
	var sum := Vector3.ZERO
	var w := 0.0
	var far := 0.0
	for m in pad.members:
		var d := g.positions[m].distance_to(base)
		far = maxf(far, d)
	for m in pad.members:
		# The outer half of the branch carries the foliage.
		var k := 0.25 + g.positions[m].distance_to(base) / maxf(far, 1e-3)
		sum += g.positions[m] * k
		w += k
	pad.centre = sum / w
	var spread := 0.0
	for m in pad.members:
		var d := g.positions[m] - pad.centre
		spread += Vector2(d.x, d.z).length()
	spread /= pad.members.size()
	var size := sqrt(float(pad.members.size()))
	pad.radius = clampf(maxf(spread * 1.25, size * 0.075), 0.14, 0.62 if conifer else 0.55)
	# The juniper's pads are soft clouds, not discs (0.8.1, item 32: 0.48 read flat from the side).
	pad.half_height = pad.radius * (0.62 if conifer else 0.75)
	var out := pad.centre - base
	out.y = 0.0
	pad.out = out.normalized() if out.length_squared() > 1e-6 else Vector3.FORWARD
	# Pads lie flat, tilted a little up and out along their branch; the apex is a dome.
	var apex := pad.centre.y > top * 0.82
	pad.up = (Vector3.UP + pad.out * (0.08 if apex else 0.2)).normalized()
	pad.centre += pad.up * pad.half_height * 0.25


## The wood as drawn: the graph with a tapered trunk flaring into the soil (nebari), and the
## fine twigs inside the pads hidden under their foliage. Picking still uses the simulation's
## own positions, which this never moves.
func _look_graph(b: BonsaiSim, pads: Array[Pad]) -> PlantGraph:
	var src := b.graph
	var g := PlantGraph.new(src.positions[0], src.max_nodes)
	g.positions = src.positions.duplicate()
	g.parents = src.parents.duplicate()
	g.radii = src.radii.duplicate()
	g.ages = src.ages.duplicate()
	g.children = []
	g.flags = []
	for id in range(src.size()):
		g.children.append((src.children[id] as Array).duplicate())
		g.flags.append({"dead": true} if b.is_dead(id) else null)
	# The trunk: a steady taper from a flared foot to the apex.
	var trunk := b.trunk_chain()
	var length := 0.0
	var lens := PackedFloat32Array([0.0])
	for i in range(1, trunk.size()):
		length += src.positions[trunk[i]].distance_to(src.positions[trunk[i - 1]])
		lens.append(length)
	# Never thicker than a slim bonsai trunk for its height (0.8.1, item 32: the pipe model's
	# radius made the young juniper's trunk a quarter as thick as it was tall).
	var r0 := minf(src.radii[0], maxf(b.height(), 0.5) / TRUNK_SLENDER)
	for i in range(trunk.size()):
		var t := lens[i] / maxf(length, 1e-3)
		var taper := r0 * lerpf(1.0, 0.22, pow(t, 0.8))
		if i == 0:
			taper *= 1.45
		elif i == 1:
			taper *= 1.12
		g.radii[trunk[i]] = clampf(src.radii[trunk[i]], taper, taper * 1.2)
	# Branches stay thinner than the trunk they grow from.
	for id in range(1, src.size()):
		if not trunk.has(id):
			g.radii[id] = minf(g.radii[id], r0 * 0.62)
	# A wired branch keeps its wood, so the coil always has something to wind round.
	var keep := {}
	for w in b.wired():
		for n in b.wire_chain(w):
			keep[n] = true
	for pad in pads:
		for m in pad.members:
			if m != pad.root and src.radii[m] < PAD_TWIG and g.get_flag(m, "dead") == null and not trunk.has(m) and not keep.has(m):
				# A hidden twig takes its whole subtree along (the builder draws from parents).
				g.set_flag(m, "dead", true)
	return g


static var _mip_cache: Dictionary = {}


## The foliage atlases with mipmaps whatever their import settings say (*.import files are not
## in git): small tufts far off would shimmer without them.
static func _mipmapped(path: String) -> Texture2D:
	if _mip_cache.has(path):
		return _mip_cache[path]
	var tex: Texture2D = load(path)
	var img := tex.get_image()
	if img != null and not img.has_mipmaps():
		if img.is_compressed():
			img.decompress()
		img.generate_mipmaps()
		tex = ImageTexture.create_from_image(img)
	_mip_cache[path] = tex
	return tex


func _build_wood(b: BonsaiSim, look: PlantGraph) -> ArrayMesh:
	var g := b.graph
	var colors := PackedColorArray()
	colors.resize(g.size())
	for id in range(g.size()):
		colors[id] = Color(1.0 if b.is_jin(id) else 0.0, float(g.get_flag(id, "shari", 0.0)), b.scar(id), 1.0)
	_builder.node_colors = colors
	return _builder.build(look)


## Foliage in pads (see _pads): juniper tufts on flat domes facing the sky, or the roster's
## leaf sprays on rounder clumps; drooping and duller when the soil is dry, brown on burnt tips.
func _populate_foliage(b: BonsaiSim, pads: Array[Pad]) -> void:
	var g := b.graph
	var conifer := b.species.conifer
	var per := Budgets.BONSAI_SPRAYS_PER_TWIG
	var total := 0
	for pad in pads:
		total += pad.members.size()
	var mm := _foliage.multimesh
	mm.instance_count = total * per
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([b.seed, "bonsai foliage"])
	var droop := b.droop()
	var top := maxf(b.height(), 0.5)
	# Fewer cards on the phone: each a little larger, so a pad stays as full.
	var card_scale := sqrt(3.0 / per)
	var cores := _cores.multimesh
	cores.instance_count = pads.size()
	var dark := Color(0.1, 0.19, 0.07) if conifer else Color(0.12, 0.2, 0.07)
	# A hungry tree shows it (0.8, C4), natural signs only: short of nitrogen the needles pale
	# toward yellow-green, short of phosphorus they dull to a bronze, short of potassium some tips
	# brown.
	var hunger_n := b.hunger(0)
	var hunger_p := b.hunger(1)
	var hunger_k := b.hunger(2)
	dark = dark.lerp(Color(0.32, 0.34, 0.1), hunger_n * 0.75)
	# (0.8.2.1 look review: the cores showed as dark balls in the juniper's pads and the linden
	# cutting at phone size: smaller, and a mid green rather than near black.)
	var core_scale := (0.78 if conifer else 0.6) * (0.85 if Budgets.PHONE else 1.0)
	var i := 0
	for pi in range(pads.size()):
		var pad := pads[pi]
		var up := pad.up
		var side0 := up.cross(pad.out).normalized()
		var fwd0 := side0.cross(up).normalized()
		var pad_tint := rng.randf_range(0.92, 1.06)
		var pad_blue := rng.randf_range(0.9, 1.02)
		# Lower, inner pads see less sky.
		var low := 1.0 - clampf(pad.centre.y / top, 0.0, 1.0)
		# The juniper's pad is a cloud of two or three overlapping lobes at slightly different heights
		# (0.8.1, item 32), so its outline is soft and layered from every side, never one disc.
		var lobes: Array[Vector4] = [Vector4(0, 0, 0, 1)]
		if conifer:
			lobes.clear()
			var n_l := 3 if pad.members.size() >= PAD_MIN * 3 else 2
			var a0 := rng.randf() * TAU
			for li in range(n_l):
				var la := a0 + TAU * li / n_l + rng.randf_range(-0.4, 0.4)
				var lift := rng.randf_range(-0.3, 0.35)
				lobes.append(Vector4(cos(la) * 0.36, lift, sin(la) * 0.36, rng.randf_range(0.66, 0.78)))
		# The dark inner mass: smaller and rounder than the pad, so from below it reads as shade
		# inside the needles, not as a dark plate.
		var core := Basis(side0, up, fwd0).scaled(Vector3(pad.radius * 0.6, pad.half_height * 0.62, pad.radius * 0.6) * (core_scale if pad.members.size() >= PAD_MIN * 2 else 0.01))
		cores.set_instance_transform(pi, Transform3D(core, pad.centre))
		cores.set_instance_color(pi, dark.lerp(Color(0.2, 0.3, 0.1), 0.6 if conifer else 0.45).lerp(Color(0.5, 0.45, 0.25), droop * 0.5) * pad_tint)
		for m in pad.members:
			var burnt := g.get_flag(m, "burnt") != null
			for _k in range(per):
				# A point on the pad's dome: mostly its top and rim, a few underneath. The juniper's
				# clouds are needled all round, the underside too (seen from below).
				var lobe: Vector4 = lobes[rng.randi() % lobes.size()]
				var a := rng.randf() * TAU
				var v := rng.randf_range(-0.85, 1.0) if conifer else rng.randf_range(-0.4, 1.0)
				var h := sqrt(maxf(0.0, 1.0 - v * v))
				var unit := Vector3(cos(a) * h, v, sin(a) * h)
				var depth := rng.randf_range(0.72, 1.0)
				var local := Vector3(unit.x * pad.radius, unit.y * pad.half_height, unit.z * pad.radius) * depth * lobe.w
				local += Vector3(lobe.x * pad.radius, lobe.y * pad.half_height, lobe.z * pad.radius)
				var at := pad.centre + side0 * local.x + up * local.y + fwd0 * local.z
				var nrm_l := Vector3(unit.x / pad.radius, unit.y / pad.half_height, unit.z / pad.radius).normalized()
				var nrm := (side0 * nrm_l.x + up * nrm_l.y + fwd0 * nrm_l.z).normalized()
				var radial := (side0 * unit.x + fwd0 * unit.z)
				radial = radial.normalized() if radial.length_squared() > 1e-4 else pad.out
				var face: Vector3
				var size: float
				if conifer:
					face = (nrm + up * 0.4 + Vector3(rng.randf_range(-0.45, 0.45), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.45, 0.45))).normalized()
					# (0.8.2.1: smaller tufts, so a pad reads as fine scale foliage at 450 px, not as
					# lobed leaf clumps.)
					size = clampf(pad.radius * 0.36, 0.08, 0.15) * rng.randf_range(0.8, 1.2) * card_scale
				else:
					face = (nrm + up * 0.25 + Vector3(rng.randf_range(-0.3, 0.3), rng.randf_range(-0.2, 0.2), rng.randf_range(-0.3, 0.3))).normalized()
					size = clampf(pad.radius * 0.7, 0.16, 0.3) * rng.randf_range(0.85, 1.15) * card_scale
				# Too dry: the leaves hang.
				face = face.lerp(radial * 0.6 + Vector3.DOWN * 0.8, droop * 0.85).normalized()
				var dir := radial - face * radial.dot(face)
				if dir.length_squared() < 0.01:
					dir = face.cross(Vector3.RIGHT)
				dir = dir.normalized().rotated(face, rng.randf_range(-0.7, 0.7))
				dir = dir.lerp(Vector3.DOWN, droop * 0.7).normalized()
				var side := dir.cross(face).normalized()
				var basis := Basis(side, dir, face).scaled(Vector3.ONE * size)
				mm.set_instance_transform(i, Transform3D(basis, at - dir * size * 0.45))
				var tint := rng.randf_range(0.78, 1.14) * pad_tint
				var occ := clampf(0.05 + 0.55 * (0.5 - nrm.y * 0.5) + 0.15 * low + (1.0 - depth) * 0.5, 0.0, 0.85)
				var col := Color(tint, tint, tint * pad_blue, occ)
				col = hungry_color(col, hunger_n, hunger_p, hunger_k > 0.0 and posmod(hash([m, _k, "k tips"]), 100) < int(hunger_k * K_TIP_SHARE * 100.0))
				if burnt:
					col = Color(1.15, 0.62, 0.3, col.a)
				if droop > 0.0:
					col = col.lerp(Color(0.9, 0.84, 0.55, col.a), droop * 0.55)
				mm.set_instance_color(i, col)
				var oct := _oct(nrm)
				mm.set_instance_custom_data(i, Color(float(rng.randi() % 4), rng.randf(), oct.x, oct.y))
				i += 1


## Share of the sprays whose tips brown when the soil holds no potassium at all.
const K_TIP_SHARE: float = 0.55


## A spray's colour with the hunger signs (C4): nitrogen pales it toward yellow-green, phosphorus
## dulls it toward bronze, and `k_tip` browns this spray's tip (potassium). (0.8 review: the N and K
## signs a little stronger, so they read on the phone.)
static func hungry_color(col: Color, hunger_n: float, hunger_p: float, k_tip: bool) -> Color:
	if hunger_n > 0.0:
		col = col.lerp(Color(1.28, 1.18, 0.42, col.a), hunger_n * 0.8)
	if hunger_p > 0.0:
		col = col.lerp(Color(0.95, 0.66, 0.62, col.a), hunger_p * 0.5)
	if k_tip:
		col = col.lerp(Color(1.08, 0.66, 0.32, col.a), 0.9)
	return col


## Octahedral encoding of a unit vector into 0..1 (the foliage shader decodes it).
static func _oct(v: Vector3) -> Vector2:
	var s := absf(v.x) + absf(v.y) + absf(v.z)
	var p := Vector2(v.x, v.z) / s
	if v.y < 0.0:
		p = Vector2((1.0 - absf(p.y)) * signf(p.x if p.x != 0.0 else 1.0), (1.0 - absf(p.x)) * signf(p.y if p.y != 0.0 else 1.0))
	return p * 0.5 + Vector2(0.5, 0.5)


## Copper coils along each wired branch: a thin tube winding round the wood.
func _build_wires(b: BonsaiSim, look: PlantGraph) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var any := false
	var g := b.graph
	for id in b.wired():
		var chain: Array[int] = [g.parents[id]]
		chain.append_array(b.wire_chain(id))
		var pts := PackedVector3Array()
		var rads := PackedFloat32Array()
		for k in range(chain.size()):
			var n := chain[k]
			pts.append(g.positions[n])
			var r := maxf(_builder.min_radius, look.radii[n] * _builder.radius_scale)
			if k == 0:
				# The branch starts from its own ring, not the parent's (as the wood is drawn).
				r = minf(r, maxf(_builder.min_radius, look.radii[id] * _builder.radius_scale) * 1.35)
			elif k == chain.size() - 1 and (g.children[n] as Array).is_empty():
				r *= 0.55
			rads.append(r)
		# Wire about a third as thick as the branch it holds, lying snug on the bark.
		_helix(st, pts, rads, maxf(WIRE_RADIUS, rads[1] * 0.32))
		any = true
	if not any:
		return ArrayMesh.new()
	return st.commit()


## A coil lying on the bark: it starts a little out from the fork (clear of the parent's wood)
## and winds evenly along the branch's segments.
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
		var steps := maxi(2, int(len / (WIRE_PITCH / 20.0)))
		var last := i == pts.size() - 2
		for s in range(steps + (1 if last else 0)):
			var t := float(s) / steps
			if i == 0 and t < 0.3:
				continue
			var ang := TAU * (along + len * t) / WIRE_PITCH
			var r := lerpf(rads[i], rads[i + 1], t) * 1.06 + wire_r
			path.append(a + d * t + (side * cos(ang) + up * sin(ang)) * r)
		along += len
	_tube(st, path, wire_r)


## A round tube along `path` with smooth normals and a twist-free frame.
func _tube(st: SurfaceTool, path: PackedVector3Array, r: float) -> void:
	if path.size() < 2:
		return
	var sides := 8
	var rings: Array[PackedVector3Array] = []
	var norms: Array[PackedVector3Array] = []
	var u := Vector3.ZERO
	for i in range(path.size()):
		var ax := (path[mini(i + 1, path.size() - 1)] - path[maxi(i - 1, 0)]).normalized()
		if u == Vector3.ZERO:
			u = ax.cross(Vector3.UP if absf(ax.y) < 0.9 else Vector3.RIGHT).normalized()
		u = (u - ax * u.dot(ax)).normalized()
		var v := ax.cross(u)
		var ring := PackedVector3Array()
		var nr := PackedVector3Array()
		for k in range(sides):
			var a := TAU * k / sides
			var o := u * cos(a) + v * sin(a)
			ring.append(path[i] + o * r)
			nr.append(o)
		rings.append(ring)
		norms.append(nr)
	for i in range(path.size() - 1):
		for k in range(sides):
			var k1 := (k + 1) % sides
			for q: Array in [[i, k], [i + 1, k], [i + 1, k1], [i, k], [i + 1, k1], [i, k1]]:
				st.set_normal(norms[q[0]][q[1]])
				st.add_vertex(rings[q[0]][q[1]])


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
	return _orbit_at(_yaw, _pitch, _dist, _focus)


func _orbit_at(yaw: float, pitch: float, dist: float, focus_at: Vector3) -> Transform3D:
	var focus := to_global(focus_at)
	var offset := Vector3(sin(yaw) * cos(pitch), sin(pitch), -cos(yaw) * cos(pitch)) * dist
	var eye := to_global(focus_at + offset)
	return Transform3D(Basis(), eye).looking_at(focus, Vector3.UP)


func _update_camera(delta: float) -> void:
	if not active:
		return
	var target := _orbit()
	var fov := base_fov()
	if _blend < 1.0:
		_blend = minf(1.0, _blend + delta / 0.9)
		var t := ease(_blend, -2.0)
		camera.global_transform = _from.interpolate_with(target, t)
		# 0.8.2.4 (phone: the glide skimmed the workbench): the path arcs up over it.
		camera.global_position.y += sin(PI * t) * FLY_LIFT
		camera.fov = lerpf(_from_fov, fov, t)
	else:
		camera.global_transform = target
		camera.fov = fov


## The close-up's field of view for this screen: FOV, or wider on a narrow (tall) screen, so every
## thing on the sill is whole on screen in the default look (0.8.1, item 18: on the phone's 20:9
## screen the trowel and the box of cuttings were cut at the edges).
func base_fov() -> float:
	var vp := get_viewport()
	if vp == null:
		return FOV
	var size := vp.get_visible_rect().size
	var aspect := size.x / maxf(size.y, 1.0)
	if is_equal_approx(aspect, _fov_aspect):
		return _fov
	_fov_aspect = aspect
	var eye := _orbit_at(0.0, PITCH, DIST, FOCUS)
	var inv := eye.affine_inverse()
	var need := tan(deg_to_rad(FOV * 0.5))
	for id in tools.items:
		if not tools.rests.has(id):
			continue
		var box: AABB = tools.boxes[id]
		var xf := tools.global_transform * (tools.rests[id] as Transform3D)
		for i in range(8):
			var p := inv * (xf * box.get_endpoint(i))
			if p.z < -0.01:
				# A margin of 4 % of the half width on each side.
				need = maxf(need, absf(p.x) / -p.z / aspect / 0.96)
	_fov = clampf(rad_to_deg(atan(need)) * 2.0, FOV, 75.0)
	return _fov


## How high the glide to the sill arcs over the straight line (m, at its middle).
const FLY_LIFT := 0.22


## The glide to the sill is over (main.gd shows the paper scraps then).
func arrived() -> bool:
	return active and _blend >= 1.0


## Into bonsai mode: the camera glides from `from` (the shed's eye) close to the pot.
func enter(from: Camera3D) -> void:
	active = true
	tool = ""
	tools.reset()
	_from = from.global_transform
	_from_fov = from.fov
	_blend = 0.0
	camera.environment = from.environment
	camera.global_transform = _from
	camera.make_current()
	# Rebuilt only if the bonsai changed (0.8.2.4: a forced rebuild was a long first frame of the
	# glide; the shed's visit already brought it up to date).
	refresh(false)


## Back to the workbench: the camera glides back, then `to` takes over.
func leave(to: Camera3D, done: Callable) -> void:
	set_tool("")
	tin_open = false
	_show_aim({})
	var start := camera.global_transform
	var fov0 := camera.fov
	active = false
	var tw := create_tween()
	tw.tween_method(func(t: float) -> void:
		var e := ease(t, -2.0)
		camera.global_transform = start.interpolate_with(to.global_transform, e)
		camera.global_position.y += sin(PI * e) * FLY_LIFT
		camera.fov = lerpf(fov0, to.fov, e), 0.0, 1.0, 0.7)
	tw.tween_callback(func() -> void:
		to.make_current()
		done.call())


## Picks up a tool from the sill (`t`, one of BonsaiTools.HELD) or puts the one in hand down ("").
func set_tool(t: String) -> void:
	if busy or t == tool:
		return
	if t != "":
		tin_open = false
	_show_aim({})
	_drag = ""
	_pressing = false
	if tools.held != "":
		tools.put_down()
	tool = t
	uses = 0
	_depth = -1.0
	if t != "":
		tools.hold(t)
		_hold_moved = false
		_pick_at = _pointer
		_play("wire" if t in ["shears", "pinch", "wire", "trowel"] else "pot")
		tool_picked.emit(t)


## A tap on a thing on the sill: a tool is picked up or put down, the can waters, the tin opens
## its slip (or closes it), the trowel starts the repotting, the arrows turn the pot, the
## sketchbook, the album card and the cuttings open their pages (object_tapped). A tool in hand
## stays in hand for the can, the tin, the trowel and the arrows (0.8.2.2: one tap each).
func tap_object(id: String) -> void:
	if busy:
		return
	if id != "fertiliser":
		tin_open = false
	if id in BonsaiTools.HELD:
		set_tool("" if tool == id else id)
		return
	match id:
		"water":
			uses += 1
			water()
		"fertiliser":
			tin_open = not tin_open
			_play("pot")
		"trowel":
			var r := use_trowel()
			if r == "not_yet":
				var n := days_to_repot()
				said.emit("Not yet: it asks to be repotted about every seventh day (in %d day%s)." % [n, "" if n == 1 else "s"])
			elif r == "out":
				said.emit("It is out of its pot: tap the roots to trim them, then pick a pot on the slip.")
			object_tapped.emit(id)
		"turn_left":
			turn_pot(-1)
		"turn_right":
			turn_pot(1)
		_:
			object_tapped.emit(id)


## Where the things on the sill are on screen, id -> Vector2 (for tests and the labels).
func object_screen_points() -> Dictionary:
	return tools.screen_points(camera)


# --- input -----------------------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if not active or not input_enabled or state == null or state.bonsai == null or _blend < 1.0:
		return
	if event is InputEventMouse or event is InputEventScreenDrag or event is InputEventScreenTouch:
		_track_pointer(event.position)
	if busy:
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
			# A second finger: no tap, no aim, no cut from the first.
			_pressing = false
			_down_object = ""
			if _drag in ["aim", "prune", "wire"]:
				_show_aim({})
			_drag = ""
		return
	if event is InputEventScreenDrag:
		var d := event as InputEventScreenDrag
		_touches[d.index] = d.position
		if _touches.size() == 2 and _pinch_dist > 0.0:
			var pts: Array = _touches.values()
			_dist = clampf(_pinch_zoom * _pinch_dist / maxf((pts[0] as Vector2).distance_to(pts[1]), 1.0), 0.3, 1.1)
		return
	if event is InputEventMouseButton:
		var m := event as InputEventMouseButton
		if m.pressed and m.button_index == MOUSE_BUTTON_WHEEL_UP:
			_dist = clampf(_dist * 0.92, 0.3, 1.1)
		elif m.pressed and m.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_dist = clampf(_dist * 1.08, 0.3, 1.1)
		elif m.button_index == MOUSE_BUTTON_LEFT:
			if m.pressed:
				_begin(m.position)
			else:
				_end(m.position)
	elif event is InputEventMouseMotion:
		var mm := event as InputEventMouseMotion
		if _pressing:
			_move(mm.position, mm.relative)
		elif _touches.is_empty():
			# A PC's pointer resting: the label of the thing under it, and what the tool would do.
			hover = pick_object(mm.position)
			_show_aim(aim_target(mm.position) if hover == "" else {})


## The pointer, which the held tool follows (a finger on the phone, the cursor on a PC).
func _track_pointer(pos: Vector2) -> void:
	if tool != "" and not _hold_moved and pos.distance_to(_pick_at) > 24.0:
		_hold_moved = true
	_pointer = pos


## The thing on the sill a touch at `pos` means, or "" (0.8.1, item 18). A touch on the pot
## itself is the pot's (other things answer there only right at their marks). With a tool in hand:
## its own place (a larger target) puts it down; another thing is picked when the touch is inside
## its outline or near its mark (nearer still over something the tool can work on, so a touch on
## the soil beside the can is the tool's); the tool itself where it floats only off the tree.
func pick_object(pos: Vector2) -> String:
	var pot := on_pot(pos)
	if tool == "":
		return tools.pick(camera, pos, OBJECT_NEAR_TARGET if pot else TOOL_TAP, true, -1.0, not pot)
	var target := not aim_target(pos).is_empty()
	return tools.pick(camera, pos, OBJECT_NEAR_TARGET if target or pot else TOOL_TAP, not target and not on_bonsai(pos), PUT_DOWN, not pot)


func _begin(pos: Vector2) -> void:
	if _touches.size() >= 2:
		return
	_pressing = true
	_press = pos
	_drag = ""
	_pot_press = false
	_down_object = pick_object(pos)
	if _down_object != "":
		_show_aim({})
		_pick_at = pos
		return
	# Out of the pot: a tap (or a swipe) on the roots trims them, with or without the shears.
	if _lifted and (tool == "" or tool == "shears") and on_bonsai(pos):
		_show_aim({"kind": "root_ball"})
		_drag = "trim"
		return
	# Below the windowsill: a tap there goes back to the bench (a drag still looks round).
	if below_sill(pos):
		_drag = "back"
		return
	if tool == "":
		_pot_press = on_pot(pos)
		return
	# A tool in hand over something it works on: aim (the highlight shows), act on release.
	var target := aim_target(pos)
	if target.is_empty():
		return
	_show_aim(target)
	match str(target["kind"]):
		"cut":
			_drag = "prune"
		"branch":
			_wire_id = int(target["id"])
			_wire_target = Vector3.ZERO
			_drag = "wire"
		_:
			_drag = "aim"


func _move(pos: Vector2, rel: Vector2) -> void:
	match _drag:
		"prune":
			var id := pruning.pick(pos)
			_show_aim({"kind": "cut", "id": id} if id >= 0 else {})
			return
		"aim":
			# Sliding the finger moves the aim; off the tree nothing is aimed at.
			_show_aim(aim_target(pos))
			return
		"wire":
			if pos.distance_to(_press) > TAP_SLOP and not (sim().graph.get_flag(_wire_id, "wire") is Dictionary):
				_wire_target = _drag_point(pos)
				_draw_wire_preview()
			return
		"pot", "trim":
			return
		"back":
			if pos.distance_to(_press) > TAP_SLOP:
				_drag = "orbit"
	if _drag == "" and pos.distance_to(_press) > TAP_SLOP:
		_down_object = ""
		# A sideways drag on the pot turns it; anywhere else the view goes round the pot.
		_drag = "pot" if _pot_press and absf(pos.x - _press.x) > absf(pos.y - _press.y) else "orbit"
	if _drag == "orbit":
		# Round the pot, but only on the room's side of the window.
		_yaw = clampf(_yaw - rel.x * 0.007, -1.25, 1.25)
		_pitch = clampf(_pitch + rel.y * 0.005, -0.1, 1.2)


func _end(pos: Vector2) -> void:
	if not _pressing:
		return
	_pressing = false
	var b := state.bonsai
	var moved := pos.distance_to(_press) > TAP_SLOP
	if _down_object != "":
		var id := _down_object
		_down_object = ""
		if not moved:
			tap_object(id)
		return
	match _drag:
		"trim":
			_drag = ""
			_show_aim({})
			if tool == "shears":
				uses += 1
			repot_trim()
			return
		"back":
			_drag = ""
			back_requested.emit()
			return
		"prune":
			_drag = ""
			if pruning.target >= 0:
				uses += 1
				pruning.cut()
			_show_aim({})
			return
		"aim":
			_drag = ""
			var target := aimed
			_show_aim({})
			act(target)
			return
		"wire":
			_preview_mesh.clear_surfaces()
			_drag = ""
			_show_aim({})
			var g := b.graph
			# 0.8.2.8: a touch on a coil takes it off, even with a finger's wobble (a wired
			# branch cannot be wired again anyway).
			if b.graph.get_flag(_wire_id, "wire") is Dictionary:
				b.unwire(_wire_id)
				uses += 1
				_play("wire")
				refresh(true)
				tool_used.emit("unwire")
			elif moved:
				var pivot := g.positions[g.parents[_wire_id]]
				if b.wire(_wire_id, _wire_target - pivot):
					uses += 1
					_play("wire")
					refresh(true)
					tool_used.emit("wire")
			else:
				said.emit("Drag the branch into its new line; a tap only takes a wire off.")
			return
		"pot":
			_drag = ""
			if absf(pos.x - _press.x) > 30.0:
				turn_pot(1 if pos.x > _press.x else -1)
			return
	if _drag == "orbit":
		_drag = ""
		return


## What the tool in hand would work on at a screen point, or {} (see `aimed`).
func aim_target(pos: Vector2) -> Dictionary:
	if state == null or sim() == null:
		return {}
	if _lifted and tool == "" and on_bonsai(pos):
		return {"kind": "root_ball"}
	match tool:
		"shears":
			if _lifted:
				return {"kind": "root_ball"} if on_bonsai(pos) else {}
			var id := pruning.pick(pos)
			return {"kind": "cut", "id": id} if id >= 0 else {}
		"pinch":
			var id := pick_tip(pos)
			return {"kind": "tip", "id": id} if id >= 0 else {}
		"wire":
			var id := pick_branch(pos)
			return {"kind": "branch", "id": id} if id >= 0 else {}
	return {}


## The tool in hand used at a screen point (a tap on the tree, its roots out of the pot).
func use_at(pos: Vector2) -> void:
	var target := aim_target(pos)
	if target.get("kind", "") == "cut":
		pruning.preview(int(target["id"]))
		uses += 1
		pruning.cut()
		return
	act(target)


## The tool in hand works on `target` (from aim_target): water, a spoon of pellets, the trowel,
## a pinch, a snip round the root ball.
func act(target: Dictionary) -> void:
	if target.is_empty():
		return
	var b := state.bonsai
	match str(target["kind"]):
		"tip":
			if tool == "pinch" and b.pinch(int(target["id"])):
				uses += 1
				_play("snip")
				refresh(true)
				tool_used.emit("pinch")
		"root_ball":
			if tool == "shears":
				uses += 1
			repot_trim()
		"cut":
			if tool == "shears":
				pruning.preview(int(target["id"]))
				uses += 1
				pruning.cut()


## Shows what the tool in hand aims at (0.8.1, item 18), before it acts: a glowing ring round the
## soil for the can, the tin and the trowel, a ring round the tip for the tweezers, the branch
## traced for the wire, the cut and what falls for the shears; {} hides it.
func _show_aim(target: Dictionary) -> void:
	aimed = target
	var kind := str(target.get("kind", ""))
	_soil_ring.visible = kind in ["soil", "pot", "root_ball"]
	if _soil_ring.visible:
		var half := soil_half(sim().pot) if not _lifted or kind != "root_ball" else soil_half(sim().pot) * 1.05
		var y := soil_height(sim().pot) + 0.008 + (0.12 if _lifted else 0.0)
		if kind == "root_ball":
			y = soil_height(sim().pot) * 0.5 + 0.12
		_soil_ring.transform = Transform3D(Basis().scaled(Vector3(half.x * 2.3, 1.0, half.y * 2.3)), Vector3(0, y, 0))
	_tip_ring.visible = kind == "tip"
	if _tip_ring.visible:
		_tip_ring.position = _plant.to_global(sim().graph.positions[int(target["id"])])
	pruning.preview(int(target["id"]) if kind == "cut" else -1)
	if kind == "branch":
		_trace_branch(int(target["id"]))
	else:
		_preview_mesh.clear_surfaces()


## The branch the wire would take, traced in warm light from its fork to its tip.
func _trace_branch(id: int) -> void:
	var b := sim()
	var g := b.graph
	var chain: Array[int] = [g.parents[id]]
	chain.append_array(b.wire_chain(id))
	_preview_mesh.clear_surfaces()
	_preview_mesh.surface_begin(Mesh.PRIMITIVE_TRIANGLES)
	var eye := _plant.to_local(camera.global_position)
	for k in range(chain.size() - 1):
		var a := g.positions[chain[k]]
		var c := g.positions[chain[k + 1]]
		var along := (c - a).normalized()
		var side := along.cross((eye - a).normalized()).normalized() * 0.035
		for v in [a - side, c - side, c + side, a - side, c + side, a + side]:
			_preview_mesh.surface_add_vertex(v)
	_preview_mesh.surface_end()


## Whether a screen point lies below the windowsill's front edge on screen (the wall under the
## sill: a tap there goes back to the bench). The edge is the line through its two front corners.
func below_sill(screen: Vector2) -> bool:
	var a := to_global(SILL_EDGE + Vector3(SILL_EDGE_X, 0, 0))
	var c := to_global(SILL_EDGE - Vector3(SILL_EDGE_X, 0, 0))
	if camera.is_position_behind(a) or camera.is_position_behind(c):
		return false
	var edge := sill_edge_y(screen.x)
	return edge >= 0.0 and screen.y > edge + 6.0


## The sill's front edge on screen at a screen column, or -1 when it cannot be told (for tests).
func sill_edge_y(x: float) -> float:
	var pa := camera.unproject_position(to_global(SILL_EDGE + Vector3(SILL_EDGE_X, 0, 0)))
	var pc := camera.unproject_position(to_global(SILL_EDGE - Vector3(SILL_EDGE_X, 0, 0)))
	if absf(pc.x - pa.x) < 1.0:
		return -1.0
	return lerpf(pa.y, pc.y, (x - pa.x) / (pc.x - pa.x))


## Whether a screen point is on the bonsai, its soil or its pot (a loose column round the pot).
func on_bonsai(screen: Vector2) -> bool:
	var top := soil_height(sim().pot) + sim().height() * BonsaiSim.UNIT_METRES + 0.04 + (0.12 if _lifted else 0.0)
	return _in_column(screen, 0.17, -0.01, top)


## Whether a screen point is on the pot itself (a sideways drag there turns it).
func on_pot(screen: Vector2) -> bool:
	var half := soil_half(sim().pot)
	return _in_column(screen, maxf(half.x, half.y) + 0.015, -0.01, soil_height(sim().pot) + 0.015)


## Whether the ray through a screen point meets the upright column round the pot's axis
## (`radius`, from `y0` to `y1` above the pot's base) where it crosses the axis.
func _in_column(screen: Vector2, radius: float, y0: float, y1: float) -> bool:
	var axis := _base.global_position
	var from := camera.project_ray_origin(screen)
	var dir := camera.project_ray_normal(screen)
	var n := camera.global_position - axis
	n.y = 0.0
	if n.length() < 1e-4:
		return false
	n = n.normalized()
	var denom := dir.dot(n)
	if absf(denom) < 1e-5:
		return false
	var rel := from + dir * ((axis - from).dot(n) / denom) - axis
	return Vector2(rel.x, rel.z).length() < radius and rel.y > y0 and rel.y < y1


## The nearest living branch segment to a screen point (graph id, or -1).
func pick_branch(screen: Vector2) -> int:
	var b := state.bonsai
	var g := b.graph
	var best := -1
	var best_d := PICK_RADIUS
	# 0.8.2.8 (Simon: "taking the wire off again is hard, the branch is hard to pick"): the coil
	# runs along the whole wired branch (wire_chain), but only its first segment answered. Now a
	# touch anywhere on the coil finds the wired branch, and the coil wins over bare wood nearby.
	var coil_of := {}
	for w in b.wired():
		coil_of[w] = w
		for n in b.wire_chain(w):
			coil_of[n] = w
	for id in range(2, g.size()):
		if b.is_dead(id) or b.is_jin(id):
			continue
		var a := _plant.to_global(g.positions[g.parents[id]])
		var c := _plant.to_global(g.positions[id])
		if camera.is_position_behind(a) or camera.is_position_behind(c):
			continue
		var d := Geometry2D.get_closest_point_to_segment(screen, camera.unproject_position(a), camera.unproject_position(c)).distance_to(screen)
		var hit := id
		if coil_of.has(id):
			hit = int(coil_of[id])
			d *= WIRE_PICK_BIAS
		if d < best_d:
			best_d = d
			best = hit
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
	_preview_mesh.surface_begin(Mesh.PRIMITIVE_TRIANGLES)
	var pivot := g.positions[g.parents[_wire_id]]
	var end := pivot + (_wire_target - pivot).normalized() * maxf(1.0, g.positions[_wire_id].distance_to(pivot) * 3.0)
	var eye := _plant.to_local(camera.global_position)
	var side := (end - pivot).normalized().cross((eye - pivot).normalized()).normalized() * 0.035
	for v in [pivot - side, end - side, end + side, pivot - side, end + side, pivot + side]:
		_preview_mesh.surface_add_vertex(v)
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


## The watering can goes over the pot, tilts and pours; the soil darkens. Then it is back in
## the hand (or on the sill, if it was not picked up).
func water() -> void:
	if busy or sim() == null:
		return
	busy = true
	var can: Node3D = tools.items["water"]
	var tw := create_tween()
	# High enough that the tilted can and its spout stay above the rim of the tallest pot
	# (0.8.2.2, Simon's video: the spout sank into the pot).
	var over := Transform3D(Basis(Vector3.UP, -PI * 0.5), Vector3(0.14, soil_height(sim().pot) + 0.115, -0.02))
	var tilt := over.basis * Basis(Vector3.RIGHT, 0.7)
	tw.tween_property(can, "transform", over, 0.5).set_trans(Tween.TRANS_SINE)
	tw.tween_property(can, "transform", Transform3D(tilt, over.origin + Vector3(0, -0.01, 0)), 0.35)
	tw.tween_callback(func() -> void:
		# From the rose at the end of the spout. 0.8.2.2 (Simon: drops ran on below the sill):
		# each drop lives only as long as its fall to the soil, so the water soaks in there.
		var rose := (can.get_child(0) as Node3D).to_global(Vector3(0.0, 0.16, 0.27))
		var soil_y := _base.to_global(Vector3(0, soil_height(sim().pot), 0)).y
		tools.water_fx.global_position = rose
		tools.water_fx.lifetime = drop_time(rose.y - soil_y)
		tools.water_fx.emitting = true
		_play("water", 1.4))
	tw.tween_interval(0.7)
	tw.tween_callback(func() -> void:
		sim().water()
		tool_used.emit("water"))
	tw.tween_interval(0.6)
	tw.tween_callback(func() -> void: tools.water_fx.emitting = false)
	tw.tween_callback(_tool_home.bind("water"))


## Seconds a drop from the rose takes to fall `height` metres to the soil (its speed and the
## gravity of BonsaiTools.water_fx; never under a frame or two).
func drop_time(height: float) -> float:
	var v := tools.water_fx.initial_velocity_max
	var g := -tools.water_fx.gravity.y
	return maxf((-v + sqrt(v * v + 2.0 * g * maxf(height, 0.0))) / g, 0.03)


## A tap on N, P or K on the tin's slip: a spoon of it goes on the soil at once (0.8.2.2); a tap
## while the tin still pours gives the next spoon when it is back up.
func pour_pellets(kind: int) -> void:
	if sim() == null or kind < 0 or kind > 2:
		return
	pellet_kind = kind
	uses += 1
	if busy:
		_spoons.append(kind)
		return
	fertilise(kind)


## The tin tilts over the pot and a spoon of pellets of one nutrient lands on the soil.
func fertilise(kind: int) -> void:
	if busy or sim() == null:
		return
	busy = true
	var tin: Node3D = tools.items["fertiliser"]
	var tw := create_tween()
	var rest: Transform3D = tools.rests["fertiliser"]
	tw.tween_property(tin, "transform", Transform3D(rest.basis, Vector3(-0.05, 0.14, 0.0)), 0.45).set_trans(Tween.TRANS_SINE)
	tw.tween_property(tin, "rotation:z", -1.2, 0.3)
	tw.tween_callback(func() -> void:
		_play("pellets", 0.8)
		var burnt := sim().fertilise(kind)
		refresh(true)
		tool_used.emit("burn" if burnt > 0 else "fertiliser"))
	tw.tween_interval(0.4)
	tw.tween_property(tin, "rotation:z", 0.0, 0.3)
	tw.tween_callback(_after_spoon)


## After a spoon: a kind tapped on the slip while the tin poured gives the next spoon, else the
## tin goes back to its place.
func _after_spoon() -> void:
	if not _spoons.is_empty():
		busy = false
		fertilise(_spoons.pop_front())
		return
	_tool_home("fertiliser")


## After a tool's motion: in the hand it follows the pointer again, else it goes to its place.
func _tool_home(id: String) -> void:
	if tools.held == id:
		busy = false
		return
	var tw := create_tween()
	tw.tween_property(tools.items[id], "transform", tools.rests[id], 0.5).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: busy = false)


## The trowel, tapped where it lies: on a repot day it loosens the soil and the tree comes out
## with its root ball (0.8.2.2: one tap starts the repotting; the slip then trims and picks the
## pot). Returns "lift", "out" (it is out already) or "not_yet" ("" when nothing can happen now).
func use_trowel() -> String:
	if busy or sim() == null:
		return ""
	if _lifted:
		return "out"
	if not sim().repot_due:
		return "not_yet"
	busy = true
	var trowel: Node3D = tools.items["trowel"]
	var start := trowel.transform
	var dig := Transform3D(start.basis.rotated(Vector3.RIGHT, 0.6), Vector3(0.05, soil_height(sim().pot) + 0.02, -0.06))
	var tw := create_tween()
	tw.tween_property(trowel, "transform", dig, 0.3).set_trans(Tween.TRANS_SINE)
	tw.tween_property(trowel, "transform", Transform3D(dig.basis, dig.origin + Vector3(0, -0.015, 0.02)), 0.2)
	tw.tween_callback(func() -> void:
		busy = false
		repot_lift())
	tw.tween_property(trowel, "transform", start, 0.3).set_trans(Tween.TRANS_SINE)
	return "lift"


## Days until the bonsai asks to be repotted (0 when it asks now).
func days_to_repot() -> int:
	var b := sim()
	if b == null or b.repot_due:
		return 0
	return maxi(1, BonsaiSim.REPOT_DAYS - (b.day() - b.last_repot_day))


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
	_update_rootbound(sim())
	_play("pot")
	var tw := create_tween()
	busy = true
	tw.tween_property(_lift, "position:y", 0.12, 0.7).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: busy = false)


## One tap (or swipe) on the roots: the circling roots are trimmed evenly all round (0.8.2.2: it
## took three snips with the shears). Returns the share cut.
func repot_trim() -> float:
	if not _lifted:
		return _trim
	_trim = 1.0
	_play("snip")
	_build_root_ball()
	return _trim


## Picks the pot while the tree is out (it shows at once; repot_into also puts the tree in).
func repot_pick(pot_id: String) -> void:
	if not _lifted or not BonsaiSim.POTS.has(pot_id):
		return
	_new_pot = pot_id
	_show_pot(pot_id)
	_update_rootbound(sim())
	_build_root_ball()
	_play("pot")


## One tap on a pot on the slip: that pot, the tree goes down into it and the fresh soil fills by
## itself (0.8.2.2: repotting in at most three taps, trowel, trim, pot).
func repot_into(pot_id: String) -> void:
	if not _lifted or busy or not BonsaiSim.POTS.has(pot_id):
		return
	repot_pick(pot_id)
	repot_finish()


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
		_update_rootbound(sim())
		busy = false
		refresh(true)
		tool_used.emit("repot"))


## Back while the tree is out (0.7 review: back did nothing then): it goes down into its own pot
## again as it was, no repotting done; the pot still asks.
func repot_cancel() -> void:
	if not _lifted or busy:
		return
	busy = true
	_new_pot = sim().pot
	_show_pot(sim().pot)
	var tw := create_tween()
	tw.tween_property(_lift, "position:y", 0.0, 0.6).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void:
		_lifted = false
		_trim = 0.0
		_root_ball_visible(false)
		_update_rootbound(sim())
		busy = false
		refresh(true))


func is_lifted() -> bool:
	return _lifted


func trim_share() -> float:
	return _trim


## The pot picked while repotting.
func new_pot() -> String:
	return _new_pot


func _root_ball_visible(on: bool) -> void:
	_root_ball.visible = on
	if not on:
		for c in _root_ball.get_children():
			c.queue_free()


## Repot time on the pot (C4): the soil (with its grit and pellets) pushed up a little, thin roots
## circling on it along the rim, and a few root tips out under the pot's foot onto the sill.
func _update_rootbound(b: BonsaiSim) -> void:
	var due := b.repot_due and not _lifted
	var key := "%s %s %d %s" % [str(due), b.pot, b.last_repot_day, str(_lifted)]
	if key == _rootbound_key:
		return
	_rootbound_key = key
	for c in _rootbound.get_children():
		c.queue_free()
	# 0.8.2.2 (Simon: repotting showed graphics errors): while the tree is out, the pot does not
	# keep its full soil with grit and pellets under the root ball (two soils at once); only a
	# little dark old soil stays at its bottom, under the rim.
	var lift := ROOTBOUND_LIFT if due else (-LIFTED_SOIL_DROP if _lifted else 0.0)
	for n in [_soil, _moss, _pellets]:
		n.position.y = lift
	_moss.visible = not _lifted
	_pellets.visible = not _lifted
	_rootbound_shown = due
	if not due:
		return
	var y := soil_height(b.pot) + lift
	var half := soil_half(b.pot)
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([b.seed, "rootbound", b.last_repot_day])
	# The strands hang from one hidden node deep in the pot (under the soil, the lines to it stay
	# out of sight) and one under its foot.
	var g := PlantGraph.new(Vector3(0, y - 0.03, 0), 400)
	var foot := g.add_node(0, Vector3(0, 0.002, 0))
	# Roots circling on the soil just inside the rim.
	for s in range(6):
		var a := TAU * (s + rng.randf_range(0.0, 0.5)) / 6.0
		var last := g.add_node(0, Vector3(cos(a) * half.x * 0.9, y + 0.002, sin(a) * half.y * 0.9))
		for _k in range(rng.randi_range(3, 5)):
			a += rng.randf_range(0.1, 0.18)
			var r := rng.randf_range(0.8, 0.95)
			var n := g.add_node(last, Vector3(cos(a) * half.x * r, y + 0.002 + rng.randf_range(0.0, 0.002), sin(a) * half.y * r))
			if n < 0:
				break
			last = n
	# Out of the drainage hole: tips creeping from under the foot over the sill.
	for s in range(5):
		var a := TAU * (s + rng.randf_range(0.0, 0.6)) / 5.0
		var last := g.add_node(foot, Vector3(cos(a) * half.x * 0.55, 0.002, sin(a) * half.y * 0.55))
		for k in range(1, rng.randi_range(3, 6)):
			a += rng.randf_range(-0.12, 0.12)
			var r := 0.55 + k * rng.randf_range(0.13, 0.17)
			var n := g.add_node(last, Vector3(cos(a) * half.x * r, 0.0025, sin(a) * half.y * r))
			if n < 0:
				break
			last = n
	for id in range(g.size()):
		# The tips on the sill a little thicker, so they read beside the pot's foot.
		g.radii[id] = 0.0 if id <= 1 else (0.002 if g.positions[id].y < 0.01 else 0.0013)
	var rb := BranchMeshBuilder.new()
	rb.radius_scale = 1.0
	rb.min_radius = 0.0012
	var roots := MeshInstance3D.new()
	roots.mesh = rb.build(g)
	roots.material_override = _roots_mat(Color(0.5, 0.38, 0.27), 0.75)
	_rootbound.add_child(roots)


## Whether the pot shows it asks to be repotted (for tests).
func shows_rootbound() -> bool:
	return _rootbound_shown


## The root ball: the pot's shape in soil, with roots circling out of it (more the fuller the
## pot), shortened by each trim.
func _build_root_ball() -> void:
	for c in _root_ball.get_children():
		c.queue_free()
	var b := sim()
	# Shaped for the pot it is going into (0.8.2.2: the old pot's ball poked out of a narrower one).
	var pot := _new_pot if _lifted and _new_pot != "" else b.pot
	var y := soil_height(pot)
	var half := soil_half(pot)
	var depth := y - 0.012
	var ball := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 1.0
	cyl.bottom_radius = 0.8
	cyl.height = depth
	cyl.radial_segments = 24
	ball.mesh = cyl
	ball.scale = Vector3(half.x, 1.0, half.y)
	ball.material_override = _ball_soil_mat()
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
	roots.material_override = _roots_mat(Color(0.62, 0.5, 0.38), 0.8)
	_root_ball.add_child(roots)


static var _mats: Dictionary = {}


## The root ball's soil: one material for every root ball, so its shader is built once (and early,
## see _build_warm_up).
static func _ball_soil_mat() -> StandardMaterial3D:
	if not _mats.has("ball"):
		var soil := StandardMaterial3D.new()
		soil.albedo_color = Color(0.22, 0.16, 0.11)
		soil.albedo_texture = load("res://assets/bonsai/Gravel022_Color.jpg")
		soil.normal_enabled = true
		soil.normal_texture = load("res://assets/bonsai/Gravel022_NormalGL.jpg")
		soil.roughness = 1.0
		_mats["ball"] = soil
	return _mats["ball"]


## Plain root strands of one colour.
static func _roots_mat(c: Color, rough: float) -> StandardMaterial3D:
	var key := "roots %s %.2f" % [c, rough]
	if not _mats.has(key):
		var m := StandardMaterial3D.new()
		m.albedo_color = c
		m.roughness = rough
		_mats[key] = m
	return _mats[key]


## 0.8.2.2 (Simon's video: the picture stood still for 1.7 s at the first spoon of pellets, and
## for a second while repotting): every material the bonsai shows only later (the pellets, a
## glazed pot, the root ball and its roots, the water's drops, the aim marks) is drawn once,
## tiny and inside the pot, while the sill is first in view, so the phone builds those shaders
## then and not in the middle of a motion. Gone after a few frames.
func _build_warm_up() -> void:
	_warm = Node3D.new()
	_warm.name = "warm_up"
	_warm.position = Vector3(0.0, 0.03, 0.0)
	_base.add_child(_warm)
	var pot_mat := ShaderMaterial.new()
	pot_mat.shader = preload("res://lookdev/bonsai/bonsai_pot.gdshader")
	var plain: Array[Material] = [_ball_soil_mat(), _roots_mat(Color(0.62, 0.5, 0.38), 0.8),
		_roots_mat(Color(0.5, 0.38, 0.27), 0.75), pot_mat, _soil_ring.material_override,
		_tip_ring.material_override, _preview.material_override]
	var speck := BoxMesh.new()
	speck.size = Vector3.ONE * 0.00002
	for m in plain:
		var mi := MeshInstance3D.new()
		mi.mesh = speck
		mi.material_override = m
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_warm.add_child(mi)
	# The pellets and the drops are drawn instanced (a MultiMesh, as CPUParticles3D draws them).
	for pair in [[_pellets.material_override, false], [tools.water_fx.material_override if tools.water_fx != null else null, true]]:
		if pair[0] == null:
			continue
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_colors = true
		mm.use_custom_data = bool(pair[1])
		mm.mesh = speck
		mm.instance_count = 1
		var mmi := MultiMeshInstance3D.new()
		mmi.multimesh = mm
		mmi.material_override = pair[0]
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_warm.add_child(mmi)


## How many frames the warm-up specks have been drawn (they go after WARM_FRAMES).
var _warm: Node3D
var _warm_frames: int = 0
const WARM_FRAMES := 6


# --- photos and screens ----------------------------------------------------------------

## For tools: a point of the plant on screen.
func plant_screen_position(id: int) -> Vector2:
	return camera.unproject_position(_plant.to_global(state.bonsai.graph.positions[id]))


## The crown on screen: the rectangle round the plant's nodes (the pellet slip keeps clear of it).
func crown_screen_rect() -> Rect2:
	if state == null or state.bonsai == null or state.bonsai.graph.size() == 0:
		return Rect2()
	var g := state.bonsai.graph
	var r := Rect2(plant_screen_position(0), Vector2.ZERO)
	for i in range(g.size()):
		r = r.expand(camera.unproject_position(_plant.to_global(g.positions[i])))
	# The leaves reach a little beyond the twig ends.
	return r.grow(24.0)


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
