class_name RootView
extends Node3D
## Root mode (design doc section 5): a black void with glowing nutrient dots in a soft fog.
## PICK: tap any point on a root to start tonight's run. RUN: steer the tip with the stick,
## it sinks on its own, hold to dive. DONE: the camera pulls back while fine roots show.
## The simulation lives in RootSystem / Underground; this node only feeds input and draws.

signal run_started(from_id: int)
signal run_finished(totals: PackedFloat32Array)
signal find_touched(find: Dictionary)
signal dots_collected(count: int)
## A quick swipe down once the night's root is done: back up to the tree (play test 4; 0.8.2.2:
## down, the opposite of the dive's swipe up).
signal swipe_back
## The tip reached tonight's wish deposit (0.7): its patch id.
signal wish_reached(patch: int)
## The far view opened (0.8.2), for the first-time hint.
signal far_view_opened

enum Mode { IDLE, PICK, RUN, DONE }

const ROOT_COLOR := Color(0.93, 0.86, 0.72)
## 0.8.1 brighter nights (broken item 17, notes/look-0.8.1.md): the old roots glow a pale, warm
## light of their own with a soft rim, tonight's root warmer and brighter, and the soil is a
## very dark warm grey rather than black, so roots read at normal phone brightness while the
## dots (additive, far brighter) still stand out.
const OLD_ROOT_GLOW := Color(0.75, 0.66, 0.5)
const OLD_ROOT_RIM := Color(0.3, 0.27, 0.21)
const LIVE_ROOT_GLOW := Color(1.15, 0.85, 0.45)
const LIVE_ROOT_RIM := Color(0.55, 0.4, 0.2)
const SOIL_COLOR := Color(0.045, 0.038, 0.036)
const SOIL_AMBIENT := Color(0.34, 0.33, 0.4)
const SOIL_AMBIENT_ENERGY := 0.95
## Fine roots are drawn at least this thick (radius in m): readable from the overview.
const ROOT_MIN_RADIUS := 0.026
const OLD_ROOT_NEAR_FADE := 1.5
## 0.8.2 side roots (specs/side-roots.md): the second level a little finer than the fine roots,
## the third half as thick as the second and a shade dimmer (vertex colour, which the bark shader
## also applies to the glow rim). A main root is drawn RootSystem.thickness_of times as thick.
const SIDE2_RADIUS := 0.8
## Main roots are drawn this much thicker than the fine roots' floor before their own thickness,
## so the player's root, and a thick one after a full run, read apart from the side roots.
const MAIN_RADIUS := 1.4
const SIDE3_RADIUS := 0.4
const SIDE3_DIM := Color(0.62, 0.6, 0.58)
const REBUILD_INTERVAL := 0.15

var ground: Underground
var roots: RootSystem
var res: Resources
var mode: Mode = Mode.IDLE
## When false the view ignores player input (a journal page is open, or a transition runs).
var input_enabled: bool = true
## Asked before a run starts (GameState allows one run per night).
var can_start: Callable = func() -> bool: return true
## Test hook: when set, used instead of the joystick and keyboard.
var scripted_stick: Variant = null
var scripted_dive: bool = false

var camera: Camera3D
var _dots: MultiMeshInstance3D
var _static_roots: MeshInstance3D
var _live_roots: MeshInstance3D
var _tip: MeshInstance3D
var _tip_light: OmniLight3D
var _hover: MeshInstance3D
var _find_nodes: Array = []
## Rocks and finds of the current underground (rebuilt on setup).
var _content: Node3D
var _builder := BranchMeshBuilder.new()
var _rebuild_timer: float = 0.0
var _orbit_yaw: float = 0.6
var _orbit_pitch: float = 0.45
var _orbit_distance: float = 7.0
var _look: Vector3 = Vector3(0, -1.5, 0)
var _press_pos: Vector2 = Vector2.ZERO
var _press_msec: int = 0
var _pressing: bool = false
var _dragged: bool = false

# HUD
var hud: CanvasLayer
var joystick: ThumbStick
var dive_button: Button
var end_button: Button
## Tonight's life force, the green vial (ui/vial.gd), and the scrap with tonight's haul.
var vial: Vial
var _scrap: Panel
var _hint: PaperNote
## Tonight's catch: "tonight:" and each kind's coloured dot with its amount (0.8.1: plain dots in
## play, no shapes).
var _counts: HBoxContainer
var _count_labels: Array[Label] = []
var _life_at_start: float = 1.0
## Tonight no root grows (no life force, or the roots fill the soil): the overview says so.
var quiet_night: bool = false
## A new run waits for the player's first move, so nobody loses the root while reading.
var _waiting_for_input: bool = false
var compass: Compass

## The day's wish underground (0.7): each glow {"patch", "center", "radius", "strength"}
## (GameState.wish_glows), its haze node, and the wish deposit's dots, tinted warmer.
const WISH_WARM := Color(1.0, 0.62, 0.28)
const WISH_DOT_WARMTH: float = 0.35
## The haze spans this many patch radii.
const WISH_HAZE_SIZE: float = 3.2
## The haze's swell when the wish is reached (x its strength), then it fades.
const WISH_SWELL: float = 1.3
var _wish_glows: Array = []
var _glow_nodes: Array = []
var _warm_dots: Dictionary = {}
var _glow_root: Node3D

## 0.8.2 (specs/root-field-extras.md 1): the far view. Pinching out past NORMAL_MAX_DISTANCE while
## choosing the start, on a quiet night or after the run pulls the camera back over the whole field;
## pinching in or back returns. Never during the run.
const NORMAL_MAX_DISTANCE := 30.0
## The far camera: a high three-quarter view (pitch, radians) framing the field with this margin
## at FAR_HFOV degrees across the screen (portrait widens the vertical angle to keep that).
const FAR_PITCH := 1.2
## 0.8.2.1 (look review: the map filled about 55 % x 30 % of the portrait screen): the far camera
## frames what is known (roots, known patches, the trunk), not the whole field, turned so the
## longer side runs up the screen, within these margins (shares of the screen: sides, under the
## HUD's bar and hint, above the bottom).
const FAR_MARGIN_SIDE := 0.06
const FAR_MARGIN_TOP := 0.22
const FAR_MARGIN_BOTTOM := 0.06
## The far view's tap dots at the root ends, as a share of the camera's distance (about 14 px
## across on a 450 px wide phone), and the bands' ink stroke half-width (m).
const FAR_TIP_SHARE := 0.017
const FAR_STROKE_WIDTH := 0.9
const FAR_FIELD_MARGIN := 1.08
const FAR_HFOV := 62.0
## A rich patch counts as known within this distance of any root (the run's fog shows dots to
## about this far beside the root), or under its own meadow sign.
static var known_reach: float = 8.0
const FAR_FOG := 0.004
const FAR_SOIL := Color(0.105, 0.09, 0.08)
## The roots' line thickness as a share of the far camera's distance (about 3 to 4 px on a phone).
const FAR_ROOT_SHARE := 0.0045
## The camera eases at this rate (1/s), slower while it pulls back or flies down.
const CAM_RATE := 3.0
const CAM_RATE_FAR := 1.1
var far_view: bool = false
## Set by main: the first-time line "pinch out to see the whole field" is still wanted.
var far_hint: bool = false
## Set by main: called with "band" or "vein" the first time the tip meets one; true shows a line.
var first_note: Callable = func(_id: String) -> bool: return false
var _note_text: String = ""
var _note_time: float = 0.0
var _run_met: Dictionary = {}
var _far_roots: MeshInstance3D
var _far_clouds: MultiMeshInstance3D
var _far_trunk: MeshInstance3D
var _far_bands: MeshInstance3D
var _far_tips: MeshInstance3D
var _far_pts := PackedVector3Array()
var _far_fit_yaw: float = INF
var _far_fit_distance: float = 40.0
var _bands: MeshInstance3D
var _veins: MeshInstance3D
var _band_mat: Material
var _saved_view: Array = []
var _cam_rate: float = CAM_RATE
var _cam_slow_t: float = 0.0
var _look_now: Vector3 = Vector3(0, -1.5, 0)
var _fov_tween: Tween
var _touches: Dictionary = {}
var _pinch_from: float = 0.0
var _pinch_zoom: float = 0.0
var _pinch_done: bool = false


func _ready() -> void:
	_builder.radius_scale = 0.75
	_builder.min_radius = ROOT_MIN_RADIUS
	_builder.bark_tiling = 2.0
	_builder.extra_sides = 2
	_build_world()
	_build_hud()
	if get_parent() == get_tree().root:
		# Played on its own (F6): a fresh underground with some life force.
		var r := Resources.new()
		r.life_force = 30.0
		setup(Underground.new(1), RootSystem.new(1), r)
		begin_pick()


func setup(p_ground: Underground, p_roots: RootSystem, p_res: Resources) -> void:
	ground = p_ground
	roots = p_roots
	res = p_res
	# 0.8.2.3: the end of the night's small roots grow over a few frames (_grow_end).
	roots.end_in_frames = true
	mode = Mode.IDLE
	_reset_run_state()
	if _content != null:
		_content.queue_free()
	_content = Node3D.new()
	add_child(_content)
	_find_nodes.clear()
	_fill_dots()
	_build_rocks()
	# Fractured boulders instead of spheres (visuals thread).
	RockLook.apply_roots(self)
	_build_finds()
	_build_field()
	_leave_far(true)
	_rebuild_all()


## A new game or a loaded copy replaces the one shown (0.8.2.1, bug 5): nothing of the old
## game's run or settle carries over (a settle left running made the new game's first night
## count as settling, kept the far view shut and fired a stray run_finished).
func _reset_run_state() -> void:
	_after_grown = Callable()
	_settle_t = -1.0
	_settle_step = 0.0
	_settle_first_fine = 0
	_settle_start = 1
	_static_job = {}
	_static_end = -1
	_tips_key = []
	_waiting_for_input = false
	quiet_night = false
	_run_met = {}
	_note_time = 0.0
	_note_text = ""
	_touches.clear()
	_pressing = false
	_dragged = false
	_pinch_done = false
	_rebuild_timer = 0.0
	_life_at_start = 1.0
	_look_size = 0
	_look_mains = -1
	if _tip != null:
		_tip.visible = false
	if _hover != null:
		_hover.visible = false
	if joystick != null:
		release_controls()
		_show_run_controls(false)


# --- world ------------------------------------------------------------------

func _build_world() -> void:
	camera = Camera3D.new()
	camera.fov = 62.0
	camera.near = 0.05
	camera.far = 60.0
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = SOIL_COLOR
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = SOIL_AMBIENT
	env.ambient_light_energy = SOIL_AMBIENT_ENERGY
	env.fog_enabled = true
	env.fog_light_color = SOIL_COLOR
	env.fog_density = 0.05
	env.glow_enabled = true
	env.glow_intensity = 0.9
	env.glow_bloom = 0.15
	camera.environment = env
	add_child(camera)
	_build_soil()

	_dots = MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	mm.mesh = quad
	_dots.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://roots/dot_glow.gdshader")
	_dots.material_override = mat
	_dots.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The dots span the whole volume; never cull the MultiMesh as a whole.
	_dots.extra_cull_margin = 40.0
	add_child(_dots)

	# Roots: the bark texture, pale as fresh root skin, glowing a little in the dark.
	var root_mat := ShaderMaterial.new()
	root_mat.shader = preload("res://tree/bark.gdshader")
	var noise := NoiseTexture2D.new()
	noise.seamless = true
	noise.noise = FastNoiseLite.new()
	root_mat.set_shader_parameter("noise", noise)
	root_mat.set_shader_parameter("light_bark", ROOT_COLOR)
	root_mat.set_shader_parameter("dark_bark", ROOT_COLOR.darkened(0.35))
	Assets.apply_bark(root_mat)
	root_mat.set_shader_parameter("texture_tint", Color(1.25, 1.15, 0.95))
	root_mat.set_shader_parameter("glow", OLD_ROOT_GLOW)
	root_mat.set_shader_parameter("glow_rim", OLD_ROOT_RIM)
	# 0.8.2.1 (look review: a run started by the trunk put the camera inside the thick old roots):
	# old roots dissolve within OLD_ROOT_NEAR_FADE m of the camera, so they never fill the view.
	root_mat.set_shader_parameter("near_fade", OLD_ROOT_NEAR_FADE)
	root_mat.set_shader_parameter("near_fade_band", 0.0)
	_static_roots = MeshInstance3D.new()
	_static_roots.material_override = root_mat
	add_child(_static_roots)
	# Tonight's root glows warmer than the old ones, so it stands out among them.
	var live_mat := root_mat.duplicate() as ShaderMaterial
	live_mat.set_shader_parameter("glow", LIVE_ROOT_GLOW)
	live_mat.set_shader_parameter("glow_rim", LIVE_ROOT_RIM)
	live_mat.set_shader_parameter("near_fade", 0.6)
	_live_roots = MeshInstance3D.new()
	_live_roots.material_override = live_mat
	add_child(_live_roots)

	# No marker ball at the tip (Simon, play test): the growing root itself shows where you are;
	# only a soft light travels with it.
	_tip = MeshInstance3D.new()
	add_child(_tip)
	_tip_light = OmniLight3D.new()
	_tip_light.light_color = Color(1.0, 0.92, 0.8)
	_tip_light.omni_range = 8.0
	_tip_light.light_energy = 1.1
	_tip.add_child(_tip_light)
	_tip.visible = false

	_hover = _glow_sphere(0.14, Color(1.0, 1.0, 0.7), 4.0)
	_hover.visible = false
	add_child(_hover)

	_glow_root = Node3D.new()
	add_child(_glow_root)

	# 0.8.2: the bands and veins (one mesh each), and the far view's own roots, patch clouds and
	# trunk mark (one mesh or MultiMesh each), hidden until it opens.
	_bands = MeshInstance3D.new()
	_bands.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_bands)
	_veins = MeshInstance3D.new()
	_veins.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var vm := StandardMaterial3D.new()
	vm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	vm.vertex_color_use_as_albedo = true
	vm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	# (0.8.2.1: a little stronger, the phone did not show them.)
	vm.albedo_color = Color(1, 1, 1, 0.26)
	vm.cull_mode = BaseMaterial3D.CULL_DISABLED
	vm.depth_draw_mode = BaseMaterial3D.DEPTH_DRAW_DISABLED
	_veins.material_override = vm
	add_child(_veins)
	_far_roots = MeshInstance3D.new()
	_far_roots.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var fm := StandardMaterial3D.new()
	fm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	fm.vertex_color_use_as_albedo = true
	_far_roots.material_override = fm
	_far_roots.visible = false
	add_child(_far_roots)
	_far_clouds = MultiMeshInstance3D.new()
	var cm := MultiMesh.new()
	cm.transform_format = MultiMesh.TRANSFORM_3D
	cm.use_colors = true
	var cq := QuadMesh.new()
	cq.size = Vector2.ONE
	cm.mesh = cq
	_far_clouds.multimesh = cm
	var cmat := ShaderMaterial.new()
	cmat.shader = preload("res://roots/far_cloud.gdshader")
	_far_clouds.material_override = cmat
	_far_clouds.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_far_clouds.extra_cull_margin = 60.0
	_far_clouds.visible = false
	add_child(_far_clouds)
	_far_trunk = _glow_sphere(0.5, Color(0.95, 0.8, 0.55), 1.5)
	var trunk := CylinderMesh.new()
	trunk.top_radius = 0.35
	trunk.bottom_radius = 0.5
	trunk.height = 1.6
	trunk.radial_segments = 8
	_far_trunk.mesh = trunk
	_far_trunk.position = Vector3(0, 0.3, 0)
	_far_trunk.visible = false
	add_child(_far_trunk)
	_far_bands = _far_flat()
	_far_tips = _far_flat()


## 0.8.2.1 (phone test: below the first cluster of dots the soil was a flat black): a faint
## mottled soil all around, a big sphere seen from inside that travels with the camera (unshaded,
## unfogged, behind everything the fog has not already hidden), textured in world space so it
## turns with the view like soil around you. Its tones sit a little above SOIL_COLOR: darker
## mottling is lost in the phone renderer's dark steps.
const SOIL_SPHERE := 44.0
var _soil: MeshInstance3D


func _build_soil() -> void:
	_soil = MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = SOIL_SPHERE
	sphere.height = SOIL_SPHERE * 2.0
	sphere.radial_segments = 16
	sphere.rings = 8
	_soil.mesh = sphere
	var tex := NoiseTexture2D.new()
	tex.seamless = true
	tex.width = 256
	tex.height = 256
	var fn := FastNoiseLite.new()
	fn.seed = 11
	fn.frequency = 0.02
	fn.fractal_octaves = 4
	fn.fractal_gain = 0.6
	tex.noise = fn
	var ramp := Gradient.new()
	ramp.set_color(0, SOIL_COLOR.darkened(0.2))
	ramp.set_color(1, Color(0.1, 0.085, 0.072))
	ramp.add_point(0.55, Color(0.058, 0.05, 0.045))
	tex.color_ramp = ramp
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_texture = tex
	mat.uv1_triplanar = true
	mat.uv1_world_triplanar = true
	mat.uv1_scale = Vector3.ONE * 0.06
	mat.cull_mode = BaseMaterial3D.CULL_FRONT
	mat.disable_fog = true
	mat.disable_receive_shadows = true
	_soil.material_override = mat
	_soil.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_soil)


## An unshaded vertex-coloured mesh node for the far view's map marks (hidden until it opens).
func _far_flat() -> MeshInstance3D:
	var m := MeshInstance3D.new()
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.vertex_color_use_as_albedo = true
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	m.material_override = mat
	m.visible = false
	add_child(m)
	return m


func _glow_sphere(r: float, color: Color, energy: float) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var s := SphereMesh.new()
	s.radius = r
	s.height = r * 2.0
	s.radial_segments = 12
	s.rings = 6
	m.mesh = s
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.albedo_color = color
	mat.emission_enabled = true
	mat.emission = color
	mat.emission_energy_multiplier = energy
	m.material_override = mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return m


func _fill_dots() -> void:
	var mm := _dots.multimesh
	mm.instance_count = ground.dot_count()
	for i in range(ground.dot_count()):
		_set_dot(i)


func _set_dot(i: int) -> void:
	var mm := _dots.multimesh
	# The glow shrinks as a deposit is drawn down; tapped deposits keep glowing until empty.
	var s := 0.0 if ground.dot_collected[i] != 0 else (0.16 + 0.16 * ground.fullness(i)) * (0.8 + 0.2 * ground.dot_capacity[i] / ground.deposit_shares)
	mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3.ONE * s), ground.dot_positions[i]))
	# Deposits the roots already reach are dimmed, so the player looks for fresh ones.
	var reached := roots != null and roots.tapped.has(i)
	mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3.ONE * s * (0.7 if reached else 1.0)), ground.dot_positions[i]))
	mm.set_instance_color(i, dot_look(i))


## A dot's glow colour, as _set_dot sets it: a plain dot in its kind's colour (0.8.1, no shapes
## in play), warmer in the wish's deposit, dimmed once reached.
func dot_look(i: int) -> Color:
	var reached := roots != null and roots.tapped.has(i)
	var color: Color = Resources.KIND_COLORS[ground.dot_kinds[i]]
	# The wish deposit's dots glow a little warmer (0.7).
	if _warm_dots.has(i) and not reached:
		color = color.lerp(WISH_WARM, WISH_DOT_WARMTH * minf(1.0, float(_warm_dots[i])))
	# Dimmed when reached.
	return color * (0.4 if reached else 1.0)


## Tonight's wish glows (GameState.wish_glows): a warm haze over each deposit and its dots a little
## warmer. Also picks up deposits placed since the last night.
func set_wish_glows(glows: Array) -> void:
	if _dots.multimesh.instance_count != ground.dot_count():
		_fill_dots()
	for n in _glow_nodes:
		(n as Node).queue_free()
	_glow_nodes.clear()
	var old := _warm_dots.keys()
	_warm_dots.clear()
	for i in old:
		if i < ground.dot_count():
			_set_dot(i)
	_wish_glows = []
	for g in glows:
		var glow: Dictionary = (g as Dictionary).duplicate()
		glow["reached"] = false
		_wish_glows.append(glow)
		var m := MeshInstance3D.new()
		var quad := QuadMesh.new()
		quad.size = Vector2.ONE
		m.mesh = quad
		var mat := ShaderMaterial.new()
		mat.shader = preload("res://roots/wish_glow.gdshader")
		mat.set_shader_parameter("warm", WISH_WARM)
		mat.set_shader_parameter("strength", float(glow["strength"]))
		m.material_override = mat
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		m.extra_cull_margin = 8.0
		m.position = glow["center"]
		m.scale = Vector3.ONE * float(glow["radius"]) * WISH_HAZE_SIZE
		_glow_root.add_child(m)
		_glow_nodes.append(m)
		for i in ground.patch_dots(int(glow["patch"])):
			_warm_dots[i] = maxf(float(_warm_dots.get(i, 0.0)), float(glow["strength"]))
			_set_dot(i)


## Tonight's hint from a find (0.8.2.6, Finds.hint_for): {} or {"kind": "patch"|"gap", "pos",
## "radius"}. A faint pencil ring over a far patch (a map scrap) or a rock band's gap (a shard),
## in the run and in the far view; it only shows the place, it changes nothing in the soil.
var find_hint: Dictionary = {}
var _hint_mark: MeshInstance3D
const HINT_INK := {"patch": Color(0.86, 0.8, 0.66), "gap": Color(0.7, 0.8, 0.86)}


func set_find_hint(h: Dictionary) -> void:
	find_hint = h
	if _hint_mark == null:
		_hint_mark = MeshInstance3D.new()
		var quad := QuadMesh.new()
		quad.size = Vector2.ONE
		_hint_mark.mesh = quad
		var mat := ShaderMaterial.new()
		mat.shader = preload("res://roots/hint_mark.gdshader")
		_hint_mark.material_override = mat
		_hint_mark.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_hint_mark.extra_cull_margin = 8.0
		_glow_root.add_child(_hint_mark)
	_hint_mark.visible = not h.is_empty()
	if h.is_empty():
		return
	var mat := _hint_mark.material_override as ShaderMaterial
	mat.set_shader_parameter("ink", HINT_INK.get(str(h["kind"]), HINT_INK["patch"]))
	_hint_mark.position = h["pos"]
	_hint_mark.scale = Vector3.ONE * maxf(float(h["radius"]), 0.8) * 2.4


## The way from the newest root tip (the growing tip during a run) to tonight's wish glow, flat;
## Vector3.ZERO when no glow waits (none tonight, or the root reached it).
func wish_way() -> Vector3:
	if roots == null:
		return Vector3.ZERO
	for glow in _wish_glows:
		if glow["reached"]:
			continue
		var from := roots.tip_position if roots.run_active else roots.graph.positions[Diary.newest_tip(roots)]
		var c: Vector3 = glow["center"]
		var way := Vector3(c.x - from.x, 0.0, c.z - from.z)
		return way if way.length_squared() > 1e-4 else Vector3.ZERO
	return Vector3.ZERO


## The tip entered a glowing deposit: the haze swells once and settles (a quiet "found it").
func _check_wish_reached() -> void:
	for k in range(_wish_glows.size()):
		var glow: Dictionary = _wish_glows[k]
		if glow["reached"] or roots.tip_position.distance_to(glow["center"]) > float(glow["radius"]) + Diary.REACH_MARGIN:
			continue
		glow["reached"] = true
		var m: MeshInstance3D = _glow_nodes[k]
		var mat := m.material_override as ShaderMaterial
		var s0 := float(glow["strength"])
		# A soft warm swell that fades (0.7 review: it flared, and the dots snapped back to their
		# own colours at once): the haze lifts a little, then settles out over a few seconds, and
		# the dots lose their warmth with it.
		var tw := create_tween()
		tw.set_parallel(true)
		tw.tween_method(func(v: float) -> void: mat.set_shader_parameter("strength", v), s0, s0 * WISH_SWELL, 1.2).set_trans(Tween.TRANS_SINE)
		tw.tween_property(m, "scale", m.scale * 1.1, 1.2).set_trans(Tween.TRANS_SINE)
		tw.chain().tween_method(func(v: float) -> void: mat.set_shader_parameter("strength", v), s0 * WISH_SWELL, 0.0, 3.5).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
		var dots := ground.patch_dots(int(glow["patch"]))
		create_tween().tween_method(_cool_dots.bind(dots), s0, 0.0, 4.0).set_trans(Tween.TRANS_SINE)
		wish_reached.emit(int(glow["patch"]))


## The reached wish's dots lose their warmth (`warmth` from the glow's strength down to 0).
func _cool_dots(warmth: float, dots: PackedInt32Array) -> void:
	for i in dots:
		if i >= ground.dot_count():
			continue
		if warmth <= 0.001:
			_warm_dots.erase(i)
		else:
			_warm_dots[i] = warmth
		_set_dot(i)


func _build_rocks() -> void:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = Color(0.42, 0.38, 0.35)
	mat.roughness = 1.0
	mat.rim_enabled = true
	mat.rim = 0.8
	mat.rim_tint = 0.3
	for i in range(ground.rock_centers.size()):
		var m := MeshInstance3D.new()
		var s := SphereMesh.new()
		s.radius = ground.rock_radii[i]
		s.height = ground.rock_radii[i] * 2.0
		s.radial_segments = 14
		s.rings = 8
		m.mesh = s
		m.material_override = mat
		m.position = ground.rock_centers[i]
		# A little irregularity so rocks do not read as perfect balls.
		m.scale = Vector3(1.0 + 0.15 * sin(i * 1.3), 0.85 + 0.1 * cos(i * 2.1), 1.0 + 0.12 * cos(i * 0.7))
		_content.add_child(m)


func _build_finds() -> void:
	for f in ground.finds:
		var m := _glow_sphere(0.12, Color(1.0, 0.85, 0.45), 5.0)
		m.position = f["position"]
		m.visible = not f["found"]
		_content.add_child(m)
		_find_nodes.append(m)


## Per-node radius and colour for the mesh builder: main roots by their thickness, side roots
## finer, the third level dimmer. Extended as the graph grows; redone when a run ends (the
## thickness is set then) or the graph changed under it (a load).
var _look_size: int = 0
var _look_mains: int = -1


func _update_looks() -> void:
	var g := roots.graph
	var n := g.size()
	var from := _look_size
	if roots.main_root_count != _look_mains or n < _look_size or _builder.radius_mul.size() != _look_size:
		from = 0
	if from == n and n > 0:
		return
	_builder.radius_mul.resize(n)
	_builder.node_colors.resize(n)
	for id in range(from, n):
		var fl = g.flags[id]
		var mul := 1.0
		var col := Color.WHITE
		if fl != null:
			var d: Dictionary = fl
			if d.has("fine"):
				var level := int(d.get("side", 1))
				if level == 2:
					mul = SIDE2_RADIUS
				elif level >= 3:
					mul = SIDE3_RADIUS
					col = SIDE3_DIM
			elif d.has("main"):
				mul = MAIN_RADIUS * roots.thickness_of(int(d["main"]))
		_builder.radius_mul[id] = mul
		_builder.node_colors[id] = col
	_look_size = n
	_look_mains = roots.main_root_count


## The old roots only change between runs; during a run only the new root is rebuilt.
func _rebuild_all(static_too: bool = true) -> void:
	_update_looks()
	var split := roots.run_first_new_id if roots.run_active else roots.graph.size()
	if static_too:
		_static_job = {}
		_static_roots.mesh = _builder.build(roots.graph, 1, split)
		_static_end = split
	_live_roots.mesh = _builder.build(roots.graph, split) if roots.run_active else null


# --- HUD --------------------------------------------------------------------

func _build_hud() -> void:
	hud = CanvasLayer.new()
	hud.layer = 5
	add_child(hud)
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(root)

	# Tonight's life force is the green vial from the day, falling as the root grows (0.8.2.5);
	# tonight's haul sits on a scrap of journal paper beside it.
	vial = Vial.new()
	vial.position = Vector2(14, 12)
	vial.size = TreeView.VIAL_SIZE
	root.add_child(vial)
	_scrap = Panel.new()
	_scrap.add_theme_stylebox_override("panel", Paper.paper_box(256, 96, 64, "all", 12.0))
	_scrap.set_anchors_preset(Control.PRESET_TOP_WIDE)
	_scrap.offset_left = TreeView.VIAL_SIZE.x + 14
	_scrap.offset_right = -170
	_scrap.offset_top = 22
	_scrap.offset_bottom = 104
	_scrap.rotation_degrees = -0.6
	_scrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(_scrap)
	var tonight := _label(24, Vector2(16, 4))
	tonight.text = "tonight:"
	_scrap.add_child(tonight)
	_counts = HBoxContainer.new()
	_counts.position = Vector2(16, 40)
	_counts.add_theme_constant_override("separation", 6)
	_counts.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_scrap.add_child(_counts)
	for k in range(4):
		_counts.add_child(TreeView.ink_dot(Resources.KIND_COLORS[k]))
		var l := _label(24, Vector2.ZERO)
		_counts.add_child(l)
		_count_labels.append(l)
	_hint = PaperNote.new(27, 62)
	_hint.set_anchors_preset(Control.PRESET_TOP_WIDE)
	_hint.offset_left = 50
	# Clear of the corner scraps (shed, photo) on the right.
	_hint.offset_right = -50
	_hint.offset_top = 268
	root.add_child(_hint)

	joystick = ThumbStick.new()
	joystick.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	joystick.position = Vector2(50, -320)
	joystick.offset_left = 50
	joystick.offset_top = -330
	joystick.offset_right = 290
	joystick.offset_bottom = -90
	root.add_child(joystick)

	dive_button = _scrap_button("hold\nto dive", 30)
	dive_button.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	dive_button.offset_left = -250
	dive_button.offset_top = -330
	dive_button.offset_right = -60
	dive_button.offset_bottom = -140
	dive_button.focus_mode = Control.FOCUS_NONE
	root.add_child(dive_button)

	# End tonight's root here; the rest of the life force goes into fine roots.
	# (0.8.2.1 look review: 56 px tall with small words, well under 9 mm on the phone.)
	end_button = _scrap_button(Pages.END_ROOT_LABEL, 30)
	end_button.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	end_button.offset_left = -250
	end_button.offset_top = -124
	end_button.offset_right = -60
	end_button.offset_bottom = -18
	end_button.focus_mode = Control.FOCUS_NONE
	end_button.pressed.connect(end_early)
	root.add_child(end_button)
	_show_run_controls(false)

	compass = Compass.new()
	compass.camera = camera
	# 0.8.2.5: the needle shows the way to tonight's wish from the newest root tip.
	compass.target = wish_way
	compass.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	# The old hand compass, its ring at the top.
	compass.offset_left = -168
	compass.offset_right = -14
	compass.offset_top = 68
	compass.offset_bottom = 222
	root.add_child(compass)


func _label(font_size: int, pos: Vector2) -> Label:
	var l := Label.new()
	l.position = pos
	l.add_theme_font_override("font", Paper.hand_font(true))
	l.add_theme_font_size_override("font_size", font_size + 2)
	l.add_theme_color_override("font_color", Paper.INK)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


## A button written on a scrap of journal paper.
func _scrap_button(text: String, size: int) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_font_override("font", Paper.hand_font(true))
	b.add_theme_font_size_override("font_size", size)
	for k in ["font_color", "font_hover_color", "font_pressed_color"]:
		b.add_theme_color_override(k, Paper.INK)
	for k in ["normal", "hover", "pressed"]:
		var sb := Paper.paper_box(96, 64, 30 + size, "all", 12.0)
		if k == "pressed":
			sb.modulate_color = Color(0.85, 0.8, 0.7)
		b.add_theme_stylebox_override(k, sb)
	return b


func _show_run_controls(on: bool) -> void:
	joystick.visible = on
	dive_button.visible = on
	# 0.8.2.2 (specs/0.8.md "Stop at once"): ending is there from the moment the night opens,
	# also before the root has moved (the tank then grows small roots everywhere).
	end_button.visible = on


func set_hud_visible(on: bool) -> void:
	hud.visible = on


func _update_hud() -> void:
	if res == null:
		return
	vial.track_night(res.life_force, _life_at_start)
	var t := roots.run_totals
	_counts.visible = mode == Mode.RUN or mode == Mode.DONE and not quiet_night
	_scrap.visible = _counts.visible
	var words := ["water", "N", "P", "K"]
	for k in range(4):
		_count_labels[k].text = "%s %.1f%s" % [words[k], t[k], "  " if k < 3 else ""]
	match mode:
		Mode.PICK when far_view:
			_hint.text = "Tap a root's end to start there. Pinch in to come back."
		Mode.DONE when far_view:
			_hint.text = "Pinch in to come back."
		Mode.PICK:
			_hint.text = ("Tap a point on a root to start tonight's root." + ("\nPinch out to see the whole field." if far_hint else "")) if roots.graph.size() > 1 else ""
		Mode.RUN:
			_hint.text = "Move the stick (or WASD) to grow the root." if _waiting_for_input else (_note_text if _note_time > 0.0 else "")
		Mode.DONE:
			_hint.text = ("A quiet night below. Swipe down to wake the tree." if quiet_night else ("Small roots reach for the nearest dots." if roots.run_length < roots.step_length and roots.side_nodes_grown[0] > 0 else "The new root settles. The leftover grows side roots." if roots.side_nodes_grown[0] > 0 else "The new root settles. Fine roots reach for what is near.")) if is_settling() or quiet_night else "Swipe down to wake the tree, or wait for the morning."
		_:
			_hint.text = ""


# --- modes ------------------------------------------------------------------

func begin_pick() -> void:
	_leave_far(true)
	mode = Mode.PICK
	quiet_night = false
	_life_at_start = maxf(res.life_force, 0.001)
	_show_run_controls(false)
	# Before a start is picked the night can end at once too (0.8.2.2).
	end_button.visible = can_start.call()
	_tip.visible = false
	_frame_overview()
	_snap_camera()
	_update_hud()


## Starts the run from a root node (the tutorial starts from the seed, node 0).
func start_at(node_id: int) -> bool:
	if not can_start.call() or not roots.start_run(node_id):
		return false
	_enter_run()
	run_started.emit(node_id)
	return true


## After loading a save in the middle of a run.
func resume_run() -> void:
	_enter_run()
	_waiting_for_input = false
	# The tank the run started with (0.8.2.1, bug 6: rebuilt from the length at the base cost the
	# bar read about 9 % too full; the metre costs more than that). Saves before 0.8.2 lack it.
	if roots.run_tank > 0.0:
		_life_at_start = maxf(maxf(roots.run_tank, res.life_force), 0.001)
	else:
		_life_at_start = maxf(res.life_force + roots.run_length * roots.base_cost_per_metre, 0.001)
	camera.position = roots.tip_position - roots.travel * 2.4 + Vector3.UP * 0.8


func _enter_run() -> void:
	_leave_far(false)
	mode = Mode.RUN
	quiet_night = false
	_waiting_for_input = true
	_look_ahead = roots.travel * 1.2
	_run_met = {}
	_note_time = 0.0
	_touches.clear()
	_life_at_start = maxf(res.life_force, 0.001)
	_hover.visible = false
	_tip.visible = true
	_show_run_controls(true)
	# The old roots did not change since the last run ended: only the new root is built.
	_rebuild_all(false)
	_update_hud()


## A night without life force, or after the run: the camera just looks around.
func begin_idle_overview() -> void:
	_leave_far(true)
	mode = Mode.DONE
	_show_run_controls(false)
	_tip.visible = false
	_frame_overview()
	_update_hud()


func _frame_overview() -> void:
	var lo := Vector3(-1, -1.5, -1)
	var hi := Vector3(1, 0, 1)
	for p in roots.graph.positions:
		lo = lo.min(p)
		hi = hi.max(p)
	_look = (lo + hi) * 0.5
	_orbit_distance = clampf((hi - lo).length() * 1.1 + 3.0, 5.0, 24.0)


func _snap_camera() -> void:
	_look_now = _look
	camera.position = _orbit_position()
	camera.look_at(_look, Vector3.UP)


func _orbit_position() -> Vector3:
	return _look_now + Vector3(sin(_orbit_yaw) * cos(_orbit_pitch), sin(_orbit_pitch), cos(_orbit_yaw) * cos(_orbit_pitch)) * _orbit_distance


# --- frame ------------------------------------------------------------------

func _process(delta: float) -> void:
	if roots == null:
		return
	_soil.global_position = camera.global_position
	if _after_grown.is_valid():
		_grow_end()
		_update_hud()
		return
	match mode:
		Mode.RUN:
			(_dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", 15.0)
			_process_run(delta)
		Mode.PICK, Mode.DONE:
			if _settle_t >= 0.0:
				_process_settle(delta)
			# From the overview the whole underground glows; up close only the near dots do.
			(_dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", _orbit_distance + 12.0)
			if not _pressing and not far_view:
				_orbit_yaw += delta * 0.08
			if far_view and absf(_orbit_yaw - _far_fit_yaw) > 0.01:
				# Turned by a drag: the frame follows, so the map still fills the screen.
				_far_fit(_orbit_yaw)
			# 0.8.2: the look point eases too, slower while the far view pulls back or returns.
			_cam_slow_t = maxf(0.0, _cam_slow_t - delta)
			_cam_rate = CAM_RATE_FAR if _cam_slow_t > 0.0 else CAM_RATE
			var ease := 1.0 - exp(-_cam_rate * delta)
			_look_now = _look_now.lerp(_look, ease)
			var target := _orbit_position()
			camera.position = camera.position.lerp(target, ease)
			_look_at_safely(_look_now)
	_update_hud()


func _process_run(delta: float) -> void:
	var stick := _stick()
	var dive := _dive_held()
	if _waiting_for_input:
		_tip.position = roots.tip_position
		camera.position = camera.position.lerp(roots.tip_position - roots.travel * 2.4 + Vector3.UP * 0.8, 1.0 - exp(-4.0 * delta))
		_look_ahead = roots.travel * 1.2
		_look_at_safely(roots.tip_position + _look_ahead)
		if stick.length() < 0.2 and not dive:
			return
		_waiting_for_input = false
	if input_enabled and scripted_stick == null and (Input.is_physical_key_pressed(KEY_E) or Input.is_physical_key_pressed(KEY_ENTER)):
		end_early()
		return
	var alive := roots.advance(stick, dive, delta, ground, res)
	if not alive and roots.end_pending():
		# The rest of this frame follows once the end of the night has grown (_grow_end).
		_after_grown = _after_advance.bind(false, delta)
		return
	_after_advance(alive, delta)


## How fast the run camera's look ahead follows the tip's travel (1/s).
const RUN_LOOK_RATE := 5.0
var _look_ahead: Vector3 = Vector3.ZERO


## What follows the tip's step: the dots it drank light up, the camera follows; at the run's
## end the settle begins.
func _after_advance(alive: bool, delta: float) -> void:
	if not roots.last_collected.is_empty():
		dots_collected.emit(roots.last_collected.size())
	for i in roots.last_collected:
		_set_dot(i)
		_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]], 0.5)
	for f in roots.last_finds:
		_on_find(f)
	_check_wish_reached()
	_check_soil_notes(delta)
	_tip.position = roots.tip_position
	_rebuild_timer += delta
	if _rebuild_timer >= REBUILD_INTERVAL:
		_rebuild_timer = 0.0
		_rebuild_all(false)
	# Third-person camera behind and a little above the tip (where it really travels, 0.8.2.2).
	var h := roots.travel
	var flat := Vector3(h.x, 0.0, h.z)
	var back := (h * 0.5 + (flat.normalized() if flat.length_squared() > 1e-4 else -camera.global_basis.z) * 0.5).normalized()
	var want := _outside_rocks(roots.tip_position - back * 2.4 + Vector3.UP * 0.8)
	camera.position = _outside_roots(_outside_rocks(camera.position.lerp(want, 1.0 - exp(-4.0 * delta))))
	# 0.8.2.4 (phone: the camera stuttered through a row of dots): each drunk dot lets the pull
	# go or turns it to the next dot, so the tip's travel changes its turn at once, and the look
	# point 1.2 m ahead with it. The look ahead now eases, so the camera's turn ramps.
	_look_ahead = _look_ahead.lerp(h * 1.2, 1.0 - exp(-RUN_LOOK_RATE * delta))
	_look_at_safely(roots.tip_position + _look_ahead)
	if not alive:
		# end_run() already grew the fine roots and collected their dots.
		for i in roots.last_collected:
			_set_dot(i)
			_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]], 0.5)
		_settle()


## 0.8.2.3: the end of the night (fine roots, the leftover's side roots) took up to 28 ms in one
## frame on the PC, about 100 ms on a phone. RootSystem grows it over the next frames, about
## END_FRAME_USEC each, with the camera holding still; then `_after_grown` goes on as before.
const END_FRAME_USEC := 6000
var _after_grown: Callable = Callable()


func _grow_end() -> void:
	if roots.grow_on(END_FRAME_USEC):
		var then := _after_grown
		_after_grown = Callable()
		then.call()


## Keeps the camera out of the thick old roots near the trunk (Simon's phone test: the screen
## filled with root bark). Only segments thicker than a finger are checked.
func _outside_roots(p: Vector3) -> Vector3:
	var g := roots.graph
	for id in range(1, g.size()):
		var r := g.radii[id] * _builder.radius_scale
		if r < 0.04:
			continue
		var a := g.positions[g.parents[id]]
		var b := g.positions[id]
		var c := Geometry3D.get_closest_point_to_segment(p, a, b)
		var keep := r + 0.45
		if p.distance_squared_to(c) < keep * keep:
			var away := p - c
			if away.length_squared() < 1e-6:
				away = Vector3.UP
			p = c + away.normalized() * keep
	return p


## Keeps the camera out of rocks (and below the meadow), so it never fills the screen with stone.
func _outside_rocks(p: Vector3) -> Vector3:
	for r in range(ground.rock_centers.size()):
		var rr := ground.rock_radii[r] * 1.2 + 0.35
		var c := ground.rock_centers[r]
		if p.distance_squared_to(c) < rr * rr:
			p = c + (p - c).normalized() * rr
	p.y = minf(p.y, -0.1)
	return p


## A page opened: fingers lifted meanwhile would never be seen, so let go of stick and dive.
func release_controls() -> void:
	if joystick.is_pressed():
		joystick.release()
	dive_button.button_pressed = false


## The player ends the root here: fine roots take the rest of tonight's life force.
## 0.8.2.2: also before a start is picked (or before the root moved): the whole tank grows small
## roots from the whole network toward the nearest dots, and there is no main root tonight.
func end_early() -> void:
	if not input_enabled:
		return
	if mode == Mode.PICK:
		if not can_start.call():
			return
		roots.last_collected = PackedInt32Array()
		if not roots.end_at_once(ground, res):
			return
		run_started.emit(-1)
	elif mode != Mode.RUN or not roots.run_active:
		return
	else:
		roots.last_collected = PackedInt32Array()
		roots.finish_early(ground, res)
	if roots.end_pending():
		_after_grown = _after_end_early
		return
	_after_end_early()


func _after_end_early() -> void:
	for i in roots.last_collected:
		_set_dot(i)
		_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]], 0.5)
	if not roots.last_collected.is_empty():
		dots_collected.emit(roots.last_collected.size())
	_settle()


## After a run, a short pause on what grew tonight while the fine roots spread out from the
## new root (Simon, play test 4); only then does the night move on to the morning.
const SETTLE_GROW := 2.8
const SETTLE_HOLD := 1.8
var _settle_t: float = -1.0
var _settle_first_fine: int = 0
var _settle_start: int = 1
var _settle_step: float = 0.0


## Also while the end of the night still grows (0.8.2.3, _grow_end): the settle's first part.
func is_settling() -> bool:
	return _settle_t >= 0.0 or _after_grown.is_valid()


func _settle() -> void:
	begin_idle_overview()
	var g := roots.graph
	var start := clampi(roots.run_first_new_id, 1, g.size())
	_settle_first_fine = g.size()
	for id in range(start, g.size()):
		if g.get_flag(id, "fine", -1) != -1:
			_settle_first_fine = id
			break
	# Frame tonight's root.
	if start < g.size():
		var lo := g.positions[start]
		var hi := lo
		for id in range(start, g.size()):
			lo = lo.min(g.positions[id])
			hi = hi.max(g.positions[id])
		_look = (lo + hi) * 0.5
		_orbit_distance = clampf((hi - lo).length() * 0.9 + 3.0, 4.0, 16.0)
	_update_looks()
	# 0.8.2: tonight's root and its side roots keep the warm glow of the run while they settle,
	# so the thicker root or the new fan reads against the old roots (specs/side-roots.md,
	# broken 5); the night's end (_rebuild_all) turns them into old roots.
	_settle_start = start
	# 0.8.2.1 (bug 9): the old roots' mesh already ends where tonight's root starts, so it is not
	# rebuilt here (70 ms at day 30); the mesh for the morning, tonight's root included, is built
	# in pieces over the settle's frames (_step_static_job) and swapped in at its end.
	if _static_end != start or _static_roots.mesh == null:
		_static_roots.mesh = _builder.build(g, 1, start)
		_static_end = start
	_live_roots.mesh = _builder.build(g, start, _settle_first_fine)
	_static_job = {"next": 1, "end": g.size(), "size": g.size(), "acc": _builder.new_arrays()}
	_settle_t = 0.0
	_settle_step = 0.0


## Nodes of the old roots' mesh built per frame while tonight's root settles (about 3 ms on
## the PC; the whole field at day 30, 7000 nodes, takes some 25 frames of the settle's 4.6 s).
const STATIC_PIECE := 300
## The old roots' mesh being built over frames: {"next", "end", "size", "acc"}; empty if none.
var _static_job: Dictionary = {}
## The node id the old roots' mesh ends at (-1: not known).
var _static_end: int = -1


## Builds the next piece of the morning's old-root mesh. True when it is complete.
func _step_static_job(nodes: int) -> bool:
	if _static_job.is_empty():
		return true
	if int(_static_job["size"]) != roots.graph.size():
		# The graph changed under it: start over.
		_static_job = {"next": 1, "end": roots.graph.size(), "size": roots.graph.size(), "acc": _builder.new_arrays()}
	var from := int(_static_job["next"])
	var to := mini(from + nodes, int(_static_job["end"]))
	if to > from:
		_builder.append(roots.graph, from, to, _static_job["acc"])
	_static_job["next"] = to
	return to >= int(_static_job["end"])


## The settle's end: the rest of the pieces (if the frames were too few), then the new mesh.
func _finish_static_job() -> void:
	if _static_job.is_empty() or roots.run_active:
		_rebuild_all()
		return
	_update_looks()
	_step_static_job(1 << 30)
	_static_roots.mesh = _builder.mesh_from(_static_job["acc"])
	_static_end = int(_static_job["end"])
	_static_job = {}
	_live_roots.mesh = null


func _process_settle(delta: float) -> void:
	_settle_t += delta
	_settle_step += delta
	_step_static_job(STATIC_PIECE)
	var g := roots.graph
	if _settle_t < SETTLE_GROW + 0.15 and _settle_step >= 0.12:
		_settle_step = 0.0
		var k := smoothstep(0.0, SETTLE_GROW, _settle_t)
		_live_roots.mesh = _builder.build(g, _settle_start, _settle_first_fine + int(ceil((g.size() - _settle_first_fine) * k)))
	if _settle_t >= SETTLE_GROW + SETTLE_HOLD:
		_settle_t = -1.0
		_finish_static_job()
		run_finished.emit(roots.run_totals)


func _look_at_safely(target: Vector3) -> void:
	var d := target - camera.position
	if d.length_squared() < 1e-6:
		return
	# Straight up or down: keep the screen's top where it was (0.8.2.4: a fixed FORWARD could
	# roll the picture half round for a frame).
	var up := Vector3.UP if absf(d.normalized().dot(Vector3.UP)) < 0.98 else camera.global_basis.y
	camera.look_at(target, up)


func _stick() -> Vector2:
	if scripted_stick != null:
		return scripted_stick
	if not input_enabled:
		return Vector2.ZERO
	var v: Vector2 = joystick.value
	var k := Vector2(
		Input.get_action_strength("ui_right") - Input.get_action_strength("ui_left"),
		Input.get_action_strength("ui_up") - Input.get_action_strength("ui_down"))
	if Input.is_physical_key_pressed(KEY_D):
		k.x += 1
	if Input.is_physical_key_pressed(KEY_A):
		k.x -= 1
	if Input.is_physical_key_pressed(KEY_W):
		k.y += 1
	if Input.is_physical_key_pressed(KEY_S):
		k.y -= 1
	return (v + k).limit_length(1.0)


func _dive_held() -> bool:
	if scripted_stick != null:
		return scripted_dive
	if not input_enabled:
		return false
	return dive_button.button_pressed or Input.is_physical_key_pressed(KEY_SPACE) or Input.is_physical_key_pressed(KEY_SHIFT)


func _on_find(f: Dictionary) -> void:
	var idx := ground.finds.find(f)
	if idx >= 0 and idx < _find_nodes.size():
		(_find_nodes[idx] as Node3D).visible = false
	_flash(f["position"], Color(1.0, 0.85, 0.45), 1.2)
	find_touched.emit(f)


func _flash(p: Vector3, color: Color, size: float = 0.5) -> void:
	# A soft glow puff (the same glow as the dots) that swells and fades.
	var m := MeshInstance3D.new()
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	m.mesh = quad
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://roots/dot_glow.gdshader")
	mat.set_shader_parameter("tint", Color(color, 1.0))
	mat.set_shader_parameter("pulse", 0.0)
	m.material_override = mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	m.position = p
	m.scale = Vector3.ONE * 0.3
	add_child(m)
	var tw := create_tween()
	tw.set_parallel(true)
	tw.tween_property(m, "scale", Vector3.ONE * size * 0.9, 0.25)
	tw.tween_method(func(a: float) -> void: mat.set_shader_parameter("tint", Color(color, a)), 0.8, 0.0, 0.25)
	tw.chain().tween_callback(m.queue_free)


# --- picking a start point ---------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if not input_enabled or mode != Mode.PICK and mode != Mode.DONE:
		return
	if _zoom_input(event):
		return
	var pos := Vector2.ZERO
	var is_press := false
	var is_release := false
	var is_move := false
	if event is InputEventMouseButton and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
		pos = (event as InputEventMouseButton).position
		is_press = event.pressed
		is_release = not event.pressed
	elif event is InputEventMouseButton:
		return
	elif event is InputEventMouseMotion:
		pos = (event as InputEventMouseMotion).position
		is_move = true
	else:
		return
	if is_press:
		_pressing = true
		_dragged = false
		_press_pos = pos
		_press_msec = Time.get_ticks_msec()
	elif is_move:
		if _pressing:
			var rel := (event as InputEventMouseMotion).relative
			if pos.distance_to(_press_pos) > 12.0:
				_dragged = true
			if _dragged and _touches.size() < 2:
				_orbit_yaw -= rel.x * (0.004 if far_view else 0.006)
				if not far_view:
					_orbit_pitch = clampf(_orbit_pitch + rel.y * 0.004, -0.6, 1.2)
		elif mode == Mode.PICK and not far_view:
			var id := pick_node_at(pos)
			_hover.visible = id >= 0
			if id >= 0:
				_hover.position = roots.graph.positions[id]
	elif is_release:
		_pressing = false
		if mode == Mode.DONE and not is_settling() 				and TreeView.is_swipe(_press_pos, pos, false, (Time.get_ticks_msec() - _press_msec) / 1000.0):
			swipe_back.emit()
			return
		if _pinch_done or _touches.size() >= 2:
			return
		if not _dragged and mode == Mode.PICK and far_view:
			var tip := pick_far_tip_at(pos)
			if tip >= 0:
				start_at(tip)
		elif not _dragged and mode == Mode.PICK:
			var id := pick_node_at(pos)
			if id >= 0:
				start_at(id)


## Nearest root node to a screen point, within 90 px. -1 if none.
func pick_node_at(screen: Vector2) -> int:
	var best := -1
	var best_d := 90.0
	for id in range(roots.graph.size()):
		var p := roots.graph.positions[id]
		if camera.is_position_behind(p):
			continue
		var d := camera.unproject_position(p).distance_to(screen)
		if d < best_d:
			best_d = d
			best = id
	return best


# --- the far view (0.8.2) --------------------------------------------------------------

## The rock bands and soft veins of this soil (one mesh each).
func _build_field() -> void:
	_bands.mesh = FieldLook.band_mesh(ground)
	if _band_mat == null:
		var mat := RockLook.material(0.0, 0.45)
		# (0.8.2.1: lighter stone, so the blocks and cracks read in the dark soil.)
		mat.set_shader_parameter("stone_a", Color(0.56, 0.52, 0.47))
		mat.set_shader_parameter("stone_b", Color(0.36, 0.33, 0.3))
		# Finer grain: the big blotches read as one camouflaged slab, not as stone.
		mat.set_shader_parameter("grain_scale", 3.4)
		mat.set_shader_parameter("void_fill", 0.35)
		_band_mat = mat
	_bands.material_override = _band_mat
	_bands.visible = _bands.mesh != null
	_veins.mesh = FieldLook.vein_mesh(ground)
	_veins.visible = _veins.mesh != null


## The far view may open now: choosing the start, a quiet night, or after the run has settled.
func can_far_view() -> bool:
	return roots != null and (mode == Mode.PICK or mode == Mode.DONE and not is_settling())


## The far camera's vertical angle: FAR_HFOV across the screen, wider in portrait.
func far_fov() -> float:
	var aspect := _aspect()
	if aspect >= 1.0:
		return 62.0
	return clampf(rad_to_deg(2.0 * atan(tan(deg_to_rad(FAR_HFOV) * 0.5) / aspect)), 62.0, 112.0)


## The far camera's distance (set by _far_fit when the far view opens; before, the whole field).
func far_distance() -> float:
	if far_view and not _far_pts.is_empty():
		return _far_fit_distance
	var half_w := tan(deg_to_rad(far_fov()) * 0.5) * _aspect()
	return maxf(ground.extent * FAR_FIELD_MARGIN / maxf(half_w, 0.1), 20.0)


## Frames the far content seen from `yaw`: the look point and distance at which every point lies
## inside the screen's margins (the camera's own projection, solved per point), the look point
## moved so the content sits in the middle of the free band. Returns the distance.
func _far_fit(yaw: float) -> float:
	_far_fit_yaw = yaw
	var dir := Vector3(sin(yaw) * cos(FAR_PITCH), sin(FAR_PITCH), cos(yaw) * cos(FAR_PITCH))
	var f := -dir
	var r := f.cross(Vector3.UP).normalized()
	var u := r.cross(f).normalized()
	var tv := tan(deg_to_rad(far_fov()) * 0.5)
	var th := tv * _aspect()
	# The free band in normalised screen units (-1..1) and its middle.
	var x_half := 1.0 - 2.0 * FAR_MARGIN_SIDE
	var y_top := 1.0 - 2.0 * FAR_MARGIN_TOP
	var y_bot := -1.0 + 2.0 * FAR_MARGIN_BOTTOM
	var y_half := (y_top - y_bot) * 0.5
	var y_mid := (y_top + y_bot) * 0.5
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for p in _far_pts:
		var q := Vector2(r.dot(p), u.dot(p))
		lo = lo.min(q)
		hi = hi.max(q)
	var centre := r * (lo.x + hi.x) * 0.5 + u * (lo.y + hi.y) * 0.5
	var d := 12.0
	for p in _far_pts:
		var q := p - centre
		var depth := f.dot(q)
		d = maxf(d, absf(r.dot(q)) / (th * x_half) - depth)
		d = maxf(d, absf(u.dot(q)) / (tv * y_half) - depth)
	# The content's middle at the band's middle: the camera looks a little below it.
	var look := centre - u * (y_mid * tv * d)
	_look = look
	_orbit_pitch = FAR_PITCH
	_orbit_distance = d
	_far_fit_distance = d
	return d


## The turn of the far camera that frames the content largest (24 headings), from the current one.
func _far_best_yaw() -> float:
	var best := _orbit_yaw
	var best_d := INF
	for k in range(24):
		var yaw := _orbit_yaw + TAU * k / 24.0
		var d := _far_fit(yaw)
		if d < best_d - 0.05:
			best_d = d
			best = yaw
	return best


func _aspect() -> float:
	var vp := get_viewport().get_visible_rect().size if is_inside_tree() else Vector2(720, 1280)
	return vp.x / maxf(vp.y, 1.0)


## Opens the far view. False when it may not open now (during the run, or while settling).
func open_far_view() -> bool:
	if far_view or not can_far_view():
		return false
	far_view = true
	_saved_view = [_look, _orbit_distance, _orbit_pitch, _orbit_yaw]
	_far_pts = FieldLook.far_content(roots, ground, far_known_patches())
	if not find_hint.is_empty():
		# The whole ring in the frame, clear of the edges and the buttons.
		var hr := maxf(float(find_hint["radius"]), 0.8) * 2.4
		for o in [Vector3.ZERO, Vector3(hr, 0, 0), Vector3(-hr, 0, 0), Vector3(0, 0, hr), Vector3(0, 0, -hr)]:
			_far_pts.append((find_hint["pos"] as Vector3) + o)
	_orbit_yaw = _far_best_yaw()
	_far_fit(_orbit_yaw)
	_build_far()
	_show_far(true)
	_cam_slow_t = 2.5
	_tween_fov(far_fov(), 1.6)
	_hover.visible = false
	far_view_opened.emit()
	_update_hud()
	return true


## Back to the normal camera (pinch in, back, a far tap that starts the run).
func leave_far_view() -> void:
	_leave_far(false)


func _leave_far(instant: bool) -> void:
	if not far_view:
		return
	far_view = false
	_show_far(false)
	if _saved_view.size() >= 3:
		_look = _saved_view[0]
		_orbit_distance = _saved_view[1]
		_orbit_pitch = _saved_view[2]
	if instant:
		if _fov_tween:
			_fov_tween.kill()
		camera.fov = 62.0
	else:
		_cam_slow_t = 2.0
		_tween_fov(62.0, 1.4)
	_update_hud()


func _tween_fov(to: float, secs: float) -> void:
	if _fov_tween:
		_fov_tween.kill()
	if not is_inside_tree():
		camera.fov = to
		return
	_fov_tween = create_tween()
	_fov_tween.tween_property(camera, "fov", to, secs).set_trans(Tween.TRANS_SINE)


## What shows in the far view: the roots as lines, the known patches as clouds, the bands dark,
## the veins, the wish glow and the trunk; the dots, boulders, finds and the near roots hide.
func _show_far(on: bool) -> void:
	_dots.visible = not on
	_static_roots.visible = not on
	_live_roots.visible = not on
	if _content != null:
		_content.visible = not on
	_far_roots.visible = on and _far_roots.mesh != null
	_far_clouds.visible = on
	_far_trunk.visible = on
	# The bands as ink strokes on the map, not their rock walls seen from above (black blots).
	_bands.visible = not on and _bands.mesh != null
	_far_bands.visible = on and _far_bands.mesh != null
	_far_tips.visible = on and _far_tips.mesh != null
	var env := camera.environment
	env.fog_density = FAR_FOG if on else 0.05
	# The far view is a map on a plain ground; the run's soil is mottled (0.8.2.1).
	_soil.visible = not on
	env.background_color = FAR_SOIL if on else SOIL_COLOR
	env.fog_light_color = FAR_SOIL if on else SOIL_COLOR
	camera.far = 220.0 if on else 60.0
	for n in _glow_nodes:
		var mat := (n as MeshInstance3D).material_override as ShaderMaterial
		mat.set_shader_parameter("floor_until", 150.0 if on else 45.0)
		mat.set_shader_parameter("gone_at", 200.0 if on else 60.0)
		mat.set_shader_parameter("far_grow", 1.4 if on else 3.0)


## The far view's meshes, from the save: main roots by night, and the known patches.
func _build_far() -> void:
	_far_roots.mesh = FieldLook.far_roots_mesh(roots, far_distance() * FAR_ROOT_SHARE)
	_far_bands.mesh = FieldLook.band_strokes_mesh(ground, FAR_STROKE_WIDTH * clampf(far_distance() / 40.0, 0.6, 1.5))
	_far_tips.mesh = FieldLook.tip_dots_mesh(roots, far_distance() * FAR_TIP_SHARE)
	var known := far_known_patches()
	var mm := _far_clouds.multimesh
	mm.instance_count = known.size()
	for k in range(known.size()):
		var p: Dictionary = ground.patches[known[k]]
		var full := clampf(ground.patch_amount(known[k]) / maxf(ground.patch_amount(known[k], true), 1e-3), 0.0, 1.0)
		var size := float(p["radius"]) * 3.2
		mm.set_instance_transform(k, Transform3D(Basis.from_scale(Vector3.ONE * size), p["center"]))
		var col: Color = Resources.KIND_COLORS[int(p["kind"])]
		mm.set_instance_color(k, Color(col, 0.35 + 0.45 * full))


## The rich patches the far view shows (Underground.known_patches with known_reach and the
## clearing's edge).
func far_known_patches() -> PackedInt32Array:
	return ground.known_patches(roots.graph.positions, known_reach, Terrain.edge)


## The nearest end of a main root to a screen point in the far view (70 px), or -1.
func pick_far_tip_at(screen: Vector2) -> int:
	var best := -1
	var best_d := 70.0
	for id in far_tips():
		var p := roots.graph.positions[id]
		if camera.is_position_behind(p):
			continue
		var d := camera.unproject_position(p).distance_to(screen)
		if d < best_d:
			best_d = d
			best = id
	return best


## The main roots' ends, cached until the roots change (0.8.2.1, bug 9: 8 ms per tap at day 30).
var _tips_key: Array = []
var _tips: PackedInt32Array = PackedInt32Array()


func far_tips() -> PackedInt32Array:
	var key := [roots.get_instance_id(), roots.graph.size(), roots.main_root_count]
	if key != _tips_key:
		_tips = FieldLook.main_tips(roots)
		_tips_key = key
	return _tips


## Wheel, two-finger pinch and the trackpad's magnify: zoom the overview; past
## NORMAL_MAX_DISTANCE the far view opens, zooming in closes it. True when the event was used.
func _zoom_input(event: InputEvent) -> bool:
	if event is InputEventMouseButton and (event as InputEventMouseButton).pressed:
		var b := (event as InputEventMouseButton).button_index
		if b == MOUSE_BUTTON_WHEEL_UP:
			if far_view:
				leave_far_view()
			else:
				_orbit_distance = maxf(3.0, _orbit_distance * 0.9)
			return true
		if b == MOUSE_BUTTON_WHEEL_DOWN:
			if not far_view:
				if _orbit_distance >= NORMAL_MAX_DISTANCE * 0.999:
					open_far_view()
				else:
					_orbit_distance = minf(NORMAL_MAX_DISTANCE, _orbit_distance * 1.1)
			return true
	if event is InputEventMagnifyGesture:
		var f := (event as InputEventMagnifyGesture).factor
		if far_view:
			if f > 1.02:
				leave_far_view()
		elif f < 1.0 and _orbit_distance >= NORMAL_MAX_DISTANCE * 0.999:
			open_far_view()
		else:
			_orbit_distance = clampf(_orbit_distance / maxf(f, 0.01), 3.0, NORMAL_MAX_DISTANCE)
		return true
	if event is InputEventScreenTouch:
		var t := event as InputEventScreenTouch
		if t.pressed:
			_touches[t.index] = t.position
			if _touches.size() == 1:
				_pinch_done = false
		else:
			_touches.erase(t.index)
		if _touches.size() == 2:
			var pts := _touches.values()
			_pinch_from = (pts[0] as Vector2).distance_to(pts[1])
			_pinch_zoom = _orbit_distance
			_pinch_done = true
		return false
	if event is InputEventScreenDrag:
		var d := event as InputEventScreenDrag
		if not _touches.has(d.index):
			return false
		_touches[d.index] = d.position
		if _touches.size() == 2 and _pinch_from > 0.0:
			var pts := _touches.values()
			var now := maxf((pts[0] as Vector2).distance_to(pts[1]), 1.0)
			var ratio := _pinch_from / now
			if far_view:
				if ratio < 0.8:
					leave_far_view()
					_pinch_from = now
					_pinch_zoom = _orbit_distance
			elif _pinch_zoom * ratio > NORMAL_MAX_DISTANCE * 1.15:
				open_far_view()
				_pinch_from = now
			else:
				_orbit_distance = clampf(_pinch_zoom * ratio, 3.0, NORMAL_MAX_DISTANCE)
			return true
	return false


# --- first meetings with the field (0.8.2 first-time check) --------------------------------

const NOTE_SECONDS := 5.0
const NOTES := {
	"band": "Rock: find its gap, go round, or dive under.",
	"vein": "Soft soil: the root grows cheaper here.",
}


## The first time the tip meets a rock band or a soft vein, one line says what it is.
func _check_soil_notes(delta: float) -> void:
	_note_time = maxf(0.0, _note_time - delta)
	if ground.bands.is_empty() and ground.veins.is_empty():
		return
	var met := ""
	if not _run_met.has("band") and ground.band_at(roots.tip_position, 0.3) >= 0:
		met = "band"
	elif not _run_met.has("vein") and ground.vein_at(roots.tip_position) >= 0:
		met = "vein"
	if met == "":
		return
	_run_met[met] = true
	if first_note.call(met):
		_note_text = NOTES[met]
		_note_time = NOTE_SECONDS
