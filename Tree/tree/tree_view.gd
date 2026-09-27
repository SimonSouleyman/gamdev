class_name TreeView
extends Node3D
## Tree mode (design doc section 3): the tree on the meadow under the real sun arc.
## Hold anywhere to boost the sun; drag to orbit, wheel or pinch to zoom; once nutrients are
## spent, drag the glowing sun along its arc to move the day on; at sunset tap the ground.
## Reads GameState; the only things it writes are boost, time skips and the ground tap.

signal ground_tapped

const REBUILD_INTERVAL := 0.25
const TWINKLE_SECONDS := 2.5
const DRAG_THRESHOLD := 14.0
const SUN_DISTANCE := 60.0

var state: GameState
var input_enabled: bool = true
var camera: Camera3D

var _tree_mesh: MeshInstance3D
var _leaves: MultiMeshInstance3D
var _twinkles: MultiMeshInstance3D
var _seed: MeshInstance3D
var _sun_light: DirectionalLight3D
var _sun_disc: MeshInstance3D
var _sky_mat: ProceduralSkyMaterial
var _env: Environment
var _meadow: Meadow
var _builder := TreeMeshBuilder.new()
var _rebuild_timer: float = 0.0
var _built_size: int = -1
var _births: Dictionary = {}  # node id -> time it appeared
var _time: float = 0.0

# Camera
## PI: the camera stands north of the tree and looks south, toward the sun's arc,
## so sunrise (east) is on the left as on the sun arc chart.
var _yaw: float = PI
var _pitch: float = 0.18
var _zoom: float = 1.0
var _focus: Vector3 = Vector3(0, 0.6, 0)
var _distance: float = 4.0
## The height the camera frames. It only follows the tree in steps (and each morning), so a
## day's growth shows as a bigger tree on screen instead of being zoomed away.
var _framed_height: float = 0.2
var _framed_day: int = -1
## 0 = orbit view, 1 = at the dive point below the ground (for the dive and sunrise).
var dive_amount: float = 0.0

# Input
var _pressing: bool = false
var _press_pos: Vector2
var _drag_mode: String = ""  # "", "orbit", "sun", "boost"
var _touches: Dictionary = {}
var _pinch_start: float = 0.0
var _pinch_zoom: float = 1.0

# HUD
var hud: CanvasLayer
var _day_label: Label
var _life_label: Label
var _res_labels: Array[Label] = []
var _hint: Label
var _boost_label: Label
var sun_arc: SunArc


func _ready() -> void:
	_builder.radius_scale = 1.3
	_build_world()
	_build_hud()
	if get_parent() == get_tree().root:
		setup(GameState.new_game(1))


func setup(p_state: GameState) -> void:
	state = p_state
	_meadow.build(state.ground)
	_built_size = -1
	_births.clear()
	# Nodes that already exist do not twinkle.
	_rebuild()
	_frame_camera(true)


# --- world ------------------------------------------------------------------

func _build_world() -> void:
	camera = Camera3D.new()
	camera.fov = 55.0
	camera.near = 0.05
	camera.far = 400.0
	_sky_mat = ProceduralSkyMaterial.new()
	_sky_mat.ground_bottom_color = Color(0.2, 0.3, 0.15)
	_sky_mat.ground_horizon_color = Color(0.6, 0.7, 0.55)
	var sky := Sky.new()
	sky.sky_material = _sky_mat
	_env = Environment.new()
	_env.background_mode = Environment.BG_SKY
	_env.sky = sky
	_env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	_env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	_env.glow_enabled = true
	_env.glow_intensity = 0.5
	_env.fog_enabled = true
	_env.fog_density = 0.004
	_env.fog_aerial_perspective = 0.6
	camera.environment = _env
	add_child(camera)

	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(900, 900)
	ground.mesh = plane
	var gmat := StandardMaterial3D.new()
	gmat.albedo_color = Color(0.34, 0.52, 0.24)
	gmat.roughness = 1.0
	ground.material_override = gmat
	add_child(ground)
	# Gentle hills on the horizon.
	for i in range(7):
		var hill := MeshInstance3D.new()
		var s := SphereMesh.new()
		s.radius = 1.0
		s.height = 2.0
		hill.mesh = s
		hill.material_override = gmat
		var a := TAU * i / 7.0 + 0.4
		hill.position = Vector3(cos(a) * 70.0, -9.0, sin(a) * 70.0)
		hill.scale = Vector3(28, 12 + 4 * sin(i * 1.7), 22)
		add_child(hill)

	_meadow = Meadow.new()
	add_child(_meadow)

	_tree_mesh = MeshInstance3D.new()
	var bark := StandardMaterial3D.new()
	bark.albedo_color = Color(0.42, 0.31, 0.22)
	bark.roughness = 0.9
	_tree_mesh.material_override = bark
	add_child(_tree_mesh)

	_leaves = MultiMeshInstance3D.new()
	var lmm := MultiMesh.new()
	lmm.transform_format = MultiMesh.TRANSFORM_3D
	lmm.use_colors = true
	var leaf := SphereMesh.new()
	leaf.radius = 1.0
	leaf.height = 1.4
	leaf.radial_segments = 6
	leaf.rings = 3
	lmm.mesh = leaf
	_leaves.multimesh = lmm
	var lmat := StandardMaterial3D.new()
	lmat.vertex_color_use_as_albedo = true
	lmat.roughness = 0.8
	_leaves.material_override = lmat
	add_child(_leaves)

	_twinkles = MultiMeshInstance3D.new()
	var tmm := MultiMesh.new()
	tmm.transform_format = MultiMesh.TRANSFORM_3D
	tmm.use_colors = true
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	tmm.mesh = quad
	_twinkles.multimesh = tmm
	var tmat := StandardMaterial3D.new()
	tmat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	tmat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	tmat.billboard_keep_scale = true
	tmat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	tmat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	tmat.vertex_color_use_as_albedo = true
	tmat.albedo_texture = _star_texture()
	_twinkles.material_override = tmat
	_twinkles.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_twinkles)

	_seed = MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = 0.045
	sm.height = 0.12
	_seed.mesh = sm
	var seedmat := StandardMaterial3D.new()
	seedmat.albedo_color = Color(0.45, 0.3, 0.16)
	_seed.material_override = seedmat
	_seed.position = Vector3(0, 0.03, 0)
	_seed.rotation = Vector3(0.4, 0, 0.9)
	add_child(_seed)

	_sun_light = DirectionalLight3D.new()
	_sun_light.shadow_enabled = true
	_sun_light.directional_shadow_max_distance = 60.0
	add_child(_sun_light)

	_sun_disc = MeshInstance3D.new()
	var ds := SphereMesh.new()
	ds.radius = 2.2
	ds.height = 4.4
	_sun_disc.mesh = ds
	var dmat := StandardMaterial3D.new()
	dmat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	dmat.albedo_color = Color(1.0, 0.95, 0.75)
	dmat.emission_enabled = true
	dmat.emission = Color(1.0, 0.9, 0.6)
	dmat.emission_energy_multiplier = 2.0
	dmat.disable_fog = true
	_sun_disc.material_override = dmat
	_sun_disc.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_sun_disc)


## A soft four-pointed sparkle, drawn once into a small texture.
func _star_texture() -> Texture2D:
	var n := 32
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	for y in range(n):
		for x in range(n):
			var p := Vector2(x + 0.5, y + 0.5) / n * 2.0 - Vector2.ONE
			var r := p.length()
			var cross := maxf(0.0, 1.0 - absf(p.x) * 7.0) * maxf(0.0, 1.0 - absf(p.y)) + maxf(0.0, 1.0 - absf(p.y) * 7.0) * maxf(0.0, 1.0 - absf(p.x))
			var a := clampf(maxf(1.0 - r * 2.2, 0.0) + cross * 0.8, 0.0, 1.0)
			img.set_pixel(x, y, Color(1, 1, 1, a))
	return ImageTexture.create_from_image(img)


# --- HUD --------------------------------------------------------------------

func _build_hud() -> void:
	hud = CanvasLayer.new()
	hud.layer = 5
	add_child(hud)
	var bar := HBoxContainer.new()
	bar.set_anchors_preset(Control.PRESET_TOP_WIDE)
	bar.offset_left = 24
	bar.offset_top = 24
	bar.offset_right = -150
	bar.add_theme_constant_override("separation", 10)
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(bar)
	_day_label = _pill(bar, Color(1, 1, 1, 0.0))
	_life_label = _pill(bar, Color(1.0, 0.9, 0.55))
	var bar2 := HBoxContainer.new()
	bar2.set_anchors_preset(Control.PRESET_TOP_WIDE)
	bar2.offset_left = 24
	bar2.offset_top = 80
	bar2.add_theme_constant_override("separation", 10)
	bar2.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(bar2)
	for k in range(4):
		_res_labels.append(_pill(bar2, Resources.KIND_COLORS[k]))
	_boost_label = _pill(bar, Color(1.0, 0.95, 0.6))
	_boost_label.get_parent().get_parent().visible = false

	sun_arc = SunArc.new()
	sun_arc.set_anchors_preset(Control.PRESET_TOP_WIDE)
	sun_arc.offset_left = 30
	sun_arc.offset_right = -30
	sun_arc.offset_top = 140
	sun_arc.offset_bottom = 400
	sun_arc.visible = false
	sun_arc.dragged.connect(func(f: float) -> void:
		state.skip_time(f * state.sim.clock.daylight_fraction))
	hud.add_child(sun_arc)

	var compass := Compass.new()
	compass.camera = camera
	compass.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	compass.offset_left = -130
	compass.offset_right = -20
	compass.offset_top = 80
	compass.offset_bottom = 190
	hud.add_child(compass)

	_hint = Label.new()
	_hint.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_hint.offset_left = 40
	_hint.offset_right = -40
	_hint.offset_top = -260
	_hint.offset_bottom = -120
	_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hint.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_hint.add_theme_font_size_override("font_size", 28)
	_hint.add_theme_color_override("font_color", Color(1, 1, 0.97))
	_hint.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.7))
	_hint.add_theme_constant_override("shadow_offset_x", 2)
	_hint.add_theme_constant_override("shadow_offset_y", 2)
	_hint.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(_hint)


func _pill(parent: Control, dot: Color) -> Label:
	var panel := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0.98, 0.96, 0.9, 0.82)
	sb.set_corner_radius_all(18)
	sb.content_margin_left = 14
	sb.content_margin_right = 14
	sb.content_margin_top = 4
	sb.content_margin_bottom = 4
	panel.add_theme_stylebox_override("panel", sb)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(row)
	if dot.a > 0.0:
		var d := Label.new()
		d.text = "●"
		d.add_theme_color_override("font_color", dot.darkened(0.15))
		d.add_theme_font_size_override("font_size", 22)
		row.add_child(d)
	var l := Label.new()
	l.add_theme_font_size_override("font_size", 22)
	l.add_theme_color_override("font_color", Color(0.25, 0.2, 0.15))
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(l)
	parent.add_child(panel)
	return l


func set_hud_visible(on: bool) -> void:
	hud.visible = on


func _update_hud() -> void:
	var s := state.sim
	_day_label.text = "the seed" if state.is_seed() and state.day_number() == 0 else "day %d" % state.day_number()
	_life_label.text = "life force %.0f" % s.resources.life_force
	var short: Array[String] = ["water", "N", "P", "K"]
	for k in range(4):
		_res_labels[k].text = "%s %.1f" % [short[k], s.resources.amount(k)]
	_boost_label.get_parent().get_parent().visible = s.clock.boost_active
	_boost_label.text = "sun boost"
	sun_arc.visible = state.can_skip_time()
	sun_arc.progress = s.clock.time_of_day / s.clock.daylight_fraction
	match state.phase:
		GameState.Phase.SUNSET:
			_hint.text = "The sun has set. Tap the ground to follow the roots down."
		GameState.Phase.DAY:
			if state.can_skip_time():
				_hint.text = "Nothing left to grow with today. Drag the sun along its arc to move the day on."
			else:
				_hint.text = ""
		_:
			_hint.text = ""


# --- frame ------------------------------------------------------------------

func _process(delta: float) -> void:
	if state == null:
		return
	# A journal page or a transition took the input: a hold in progress ends here, or the
	# release would never arrive and the boost would stay on.
	if not input_enabled and _pressing:
		_end_press(false)
	_time += delta
	_rebuild_timer += delta
	if _rebuild_timer >= REBUILD_INTERVAL and state.sim.graph.size() != _built_size:
		_rebuild_timer = 0.0
		_rebuild()
	_update_twinkles()
	_update_sun()
	_frame_camera(false, delta)
	_update_hud()


func _rebuild() -> void:
	var g := state.sim.graph
	var first_build := _built_size < 0
	if not first_build:
		for id in range(_built_size, g.size()):
			_births[id] = _time
	_built_size = g.size()
	_tree_mesh.mesh = _builder.build(g)
	_seed.visible = state.is_seed()
	# Leaf clusters on the living tips (leaf cards come with the real assets).
	var tips := PackedInt32Array()
	for id in g.tips():
		if id > 1 and not g.get_flag(id, "dead", false):
			tips.append(id)
	var mm := _leaves.multimesh
	mm.instance_count = tips.size()
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	for i in range(tips.size()):
		var id := tips[i]
		# Bigger clusters on a bigger tree, until real leaf cards replace these blobs.
		var s := (0.08 + 0.05 * rng.randf()) * (1.0 + state.sim.height() * 0.06)
		var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(s, s * 0.7, s))
		mm.set_instance_transform(i, Transform3D(basis, g.positions[id]))
		mm.set_instance_color(i, Color(0.25, 0.5, 0.18).lerp(Color(0.45, 0.68, 0.25), rng.randf()))


func _update_twinkles() -> void:
	var g := state.sim.graph
	var alive: Array = []
	for id in _births.keys():
		if _time - float(_births[id]) < TWINKLE_SECONDS and id < g.size():
			alive.append(id)
		else:
			_births.erase(id)
	var mm := _twinkles.multimesh
	mm.instance_count = alive.size()
	for i in range(alive.size()):
		var id: int = alive[i]
		var age := (_time - float(_births[id])) / TWINKLE_SECONDS
		var flicker := 0.6 + 0.4 * sin(_time * 14.0 + id * 1.7)
		var s := 0.22 * (1.0 - age) * flicker + 0.04
		mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3.ONE * s), g.positions[id]))
		mm.set_instance_color(i, Color(1.0, 0.92, 0.6, (1.0 - age) * flicker))


func twinkle_count() -> int:
	return _twinkles.multimesh.instance_count


func _update_sun() -> void:
	var clock := state.sim.clock
	var dir := clock.sun_direction()
	var h := clock.sun_height()
	var skippable := state.can_skip_time()
	if dir == Vector3.ZERO:
		# Sunset hold or night: the sun rests just below the western horizon.
		dir = Vector3(-1, -0.05, 0).normalized()
	_sun_disc.position = dir * SUN_DISTANCE
	var pulse := 1.0 + (0.35 * sin(_time * 3.0) if skippable else 0.0)
	_sun_disc.scale = Vector3.ONE * (1.4 if skippable else 1.0) * pulse
	_sun_disc.visible = dir.y > -0.1
	if h > 0.0:
		_sun_light.visible = true
		_sun_light.look_at_from_position(dir * 20.0, Vector3.ZERO, Vector3.UP if absf(dir.y) < 0.99 else Vector3.FORWARD)
		_sun_light.light_energy = 0.25 + 1.1 * minf(clock.light_level(), 1.6)
		_sun_light.light_color = Color(1.0, 0.72, 0.45).lerp(Color(1.0, 0.97, 0.9), clampf(h * 2.0, 0.0, 1.0))
	else:
		_sun_light.visible = false
	# Sky: warm at the low sun, deep blue at the sunset hold and at night.
	var k := clampf(h * 3.0, 0.0, 1.0)
	_sky_mat.sky_top_color = Color(0.2, 0.22, 0.42).lerp(Color(0.32, 0.52, 0.86), k)
	_sky_mat.sky_horizon_color = Color(0.95, 0.6, 0.4).lerp(Color(0.75, 0.82, 0.9), k)
	_sky_mat.sky_energy_multiplier = 0.45 + 0.55 * k + (0.35 if clock.boost_active else 0.0)
	# Never too dark by day: the dawn burst must be seen.
	_env.ambient_light_energy = 0.55 + 0.45 * k if state.phase == GameState.Phase.DAY else 0.35 + 0.65 * k


# --- camera -----------------------------------------------------------------

func _frame_camera(snap: bool, delta: float = 0.0) -> void:
	var real_height := maxf(state.sim.height(), 0.2)
	if snap or state.day_number() != _framed_day or real_height > _framed_height * 1.35:
		_framed_height = real_height
		_framed_day = state.day_number()
	var height := _framed_height
	var want_focus := Vector3(0, clampf(height * 0.5, 0.25, 30.0), 0)
	var want_distance := clampf(height * 1.9 + 2.2, 2.4, 70.0) * _zoom
	var k := 1.0 if snap else 1.0 - exp(-2.0 * delta)
	_focus = _focus.lerp(want_focus, k)
	_distance = lerpf(_distance, want_distance, k)
	var orbit := _focus + Vector3(sin(_yaw) * cos(_pitch), sin(_pitch), cos(_yaw) * cos(_pitch)) * _distance
	orbit.y = maxf(orbit.y, 0.25)
	# The dive ends low beside the trunk looking into the soil; sunrise starts there and rises.
	var dive_point := Vector3(sin(_yaw), 0.0, cos(_yaw)) * 0.7 + Vector3(0, 0.3, 0)
	camera.position = orbit.lerp(dive_point, dive_amount)
	var look := _focus.lerp(Vector3(0, -1.0, 0), dive_amount)
	var d := look - camera.position
	if d.length_squared() > 1e-6:
		camera.look_at(look, Vector3.UP if absf(d.normalized().y) < 0.98 else Vector3.FORWARD)


# --- input ------------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	if state == null or not input_enabled:
		return
	if event is InputEventScreenTouch:
		var t := event as InputEventScreenTouch
		if t.pressed:
			_touches[t.index] = t.position
		else:
			_touches.erase(t.index)
		if _touches.size() == 2:
			_start_pinch()
			_end_press(false)
		return
	if event is InputEventScreenDrag:
		var d := event as InputEventScreenDrag
		_touches[d.index] = d.position
		if _touches.size() == 2 and _pinch_start > 0.0:
			var pts: Array = _touches.values()
			var dist := (pts[0] as Vector2).distance_to(pts[1])
			_zoom = clampf(_pinch_zoom * _pinch_start / maxf(dist, 1.0), 0.35, 6.0)
		return
	if event is InputEventMagnifyGesture:
		_zoom = clampf(_zoom / (event as InputEventMagnifyGesture).factor, 0.35, 6.0)
		return
	if event is InputEventMouseButton:
		var m := event as InputEventMouseButton
		if m.button_index == MOUSE_BUTTON_WHEEL_UP and m.pressed:
			_zoom = clampf(_zoom * 0.9, 0.35, 6.0)
		elif m.button_index == MOUSE_BUTTON_WHEEL_DOWN and m.pressed:
			_zoom = clampf(_zoom * 1.1, 0.35, 6.0)
		elif m.button_index == MOUSE_BUTTON_LEFT:
			if m.pressed:
				_begin_press(m.position)
			else:
				_end_press(true, m.position)
	elif event is InputEventMouseMotion and _pressing:
		_drag((event as InputEventMouseMotion).position, (event as InputEventMouseMotion).relative)


func _start_pinch() -> void:
	var pts: Array = _touches.values()
	_pinch_start = (pts[0] as Vector2).distance_to(pts[1])
	_pinch_zoom = _zoom


func _begin_press(pos: Vector2) -> void:
	if _touches.size() >= 2:
		return
	_pressing = true
	_press_pos = pos
	_drag_mode = ""
	if state.phase == GameState.Phase.DAY:
		if state.can_skip_time() and _near_sun(pos):
			_drag_mode = "sun"
		else:
			# Hold anywhere: the sun shines brighter while held.
			_drag_mode = "boost"
			state.sim.clock.boost_active = true


func _drag(pos: Vector2, rel: Vector2) -> void:
	if _drag_mode == "sun":
		_drag_sun(rel)
		return
	if _drag_mode != "orbit" and pos.distance_to(_press_pos) > DRAG_THRESHOLD:
		_drag_mode = "orbit"
		state.sim.clock.boost_active = false
	if _drag_mode == "orbit":
		_yaw -= rel.x * 0.006
		_pitch = clampf(_pitch + rel.y * 0.004, 0.02, 1.25)


func _end_press(is_release: bool, pos: Vector2 = Vector2.ZERO) -> void:
	if not _pressing:
		return
	_pressing = false
	state.sim.clock.boost_active = false
	if is_release and _drag_mode != "orbit" and _drag_mode != "sun" and state.phase == GameState.Phase.SUNSET:
		if _hits_ground(pos):
			ground_tapped.emit()
	_drag_mode = ""


func _near_sun(pos: Vector2) -> bool:
	if camera.is_position_behind(_sun_disc.global_position):
		return false
	return camera.unproject_position(_sun_disc.global_position).distance_to(pos) < 110.0


## Moves time on in proportion to how far the finger moves along the sun's screen path.
func _drag_sun(rel: Vector2) -> void:
	var clock := state.sim.clock
	var step := 0.01
	var here := clock.sun_direction() * SUN_DISTANCE
	var saved := clock.time_of_day
	clock.time_of_day = minf(saved + step, clock.daylight_fraction - 0.0001)
	var there := clock.sun_direction() * SUN_DISTANCE
	clock.time_of_day = saved
	if camera.is_position_behind(here) or camera.is_position_behind(there):
		return
	var path := camera.unproject_position(there) - camera.unproject_position(here)
	if path.length() < 0.5:
		return
	var along := rel.dot(path.normalized())
	if along > 0.0:
		state.skip_time(along / path.length() * step)


func _hits_ground(pos: Vector2) -> bool:
	var from := camera.project_ray_origin(pos)
	var dir := camera.project_ray_normal(pos)
	if dir.y >= -0.01:
		return false
	var hit := from + dir * (-from.y / dir.y)
	return Vector2(hit.x, hit.z).length() < 60.0


## For tools and tests: where the sun disc is on screen.
func sun_screen_position() -> Vector2:
	return camera.unproject_position(_sun_disc.global_position)
