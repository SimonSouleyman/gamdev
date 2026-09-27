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

enum Mode { IDLE, PICK, RUN, DONE }

const ROOT_COLOR := Color(0.93, 0.86, 0.72)
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
var _pressing: bool = false
var _dragged: bool = false

# HUD
var hud: CanvasLayer
var joystick: ThumbStick
var dive_button: Button
var end_button: Button
var _life_label: Label
var _hint: PaperNote
var _life_bar: ColorRect
var _life_bar_bg: ColorRect
var _counts: Label
var _life_at_start: float = 1.0
## Tonight no root grows (no life force, or the roots fill the soil): the overview says so.
var quiet_night: bool = false
## A new run waits for the player's first move, so nobody loses the root while reading.
var _waiting_for_input: bool = false
var compass: Compass


func _ready() -> void:
	_builder.radius_scale = 0.75
	_builder.min_radius = 0.02
	_builder.bark_tiling = 2.0
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
	mode = Mode.IDLE
	if _content != null:
		_content.queue_free()
	_content = Node3D.new()
	add_child(_content)
	_find_nodes.clear()
	_fill_dots()
	_build_rocks()
	_build_finds()
	_rebuild_all()


# --- world ------------------------------------------------------------------

func _build_world() -> void:
	camera = Camera3D.new()
	camera.fov = 62.0
	camera.near = 0.05
	camera.far = 60.0
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.01, 0.01, 0.02)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.25, 0.28, 0.4)
	env.ambient_light_energy = 0.6
	env.fog_enabled = true
	env.fog_light_color = Color(0.01, 0.01, 0.02)
	env.fog_density = 0.05
	env.glow_enabled = true
	env.glow_intensity = 0.9
	env.glow_bloom = 0.15
	camera.environment = env
	add_child(camera)

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
	root_mat.set_shader_parameter("glow", Color(0.35, 0.3, 0.22))
	_static_roots = MeshInstance3D.new()
	_static_roots.material_override = root_mat
	add_child(_static_roots)
	# Tonight's root glows warmer than the old ones, so it stands out among them.
	var live_mat := root_mat.duplicate() as ShaderMaterial
	live_mat.set_shader_parameter("glow", Color(0.95, 0.72, 0.35))
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
	var s := 0.0 if ground.dot_collected[i] != 0 else 0.22 + 0.08 * ground.dot_amounts[i]
	mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3.ONE * s), ground.dot_positions[i]))
	mm.set_instance_color(i, Resources.KIND_COLORS[ground.dot_kinds[i]])


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


## The old roots only change between runs; during a run only the new root is rebuilt.
func _rebuild_all(static_too: bool = true) -> void:
	var split := roots.run_first_new_id if roots.run_active else roots.graph.size()
	if static_too:
		_static_roots.mesh = _builder.build(roots.graph, 1, split)
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

	# The readouts sit on a scrap of journal paper, like the day's.
	var scrap := Panel.new()
	scrap.add_theme_stylebox_override("panel", Paper.paper_box(256, 96, 64, "all", 12.0))
	scrap.set_anchors_preset(Control.PRESET_TOP_WIDE)
	scrap.offset_left = 22
	scrap.offset_right = -160
	scrap.offset_top = 22
	scrap.offset_bottom = 140
	scrap.rotation_degrees = -0.6
	scrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(scrap)
	_life_bar_bg = ColorRect.new()
	_life_bar_bg.color = Color(Paper.INK, 0.15)
	_life_bar_bg.position = Vector2(40, 40)
	_life_bar_bg.size = Vector2(640, 18)
	_life_bar_bg.set_anchors_preset(Control.PRESET_TOP_WIDE)
	_life_bar_bg.offset_left = 40
	_life_bar_bg.offset_right = -180
	_life_bar_bg.offset_top = 40
	_life_bar_bg.offset_bottom = 58
	_life_bar_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(_life_bar_bg)
	_life_bar = ColorRect.new()
	_life_bar.color = Color(0.78, 0.55, 0.12, 0.9)
	_life_bar.size = Vector2(0, 18)
	_life_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_life_bar_bg.add_child(_life_bar)
	_life_label = _label(24, Vector2(40, 64))
	root.add_child(_life_label)
	_counts = _label(24, Vector2(40, 98))
	root.add_child(_counts)
	_hint = PaperNote.new(27, 62)
	_hint.set_anchors_preset(Control.PRESET_TOP_WIDE)
	_hint.offset_left = 50
	_hint.offset_right = -50
	_hint.offset_top = 190
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
	dive_button.offset_top = -300
	dive_button.offset_right = -60
	dive_button.offset_bottom = -110
	dive_button.focus_mode = Control.FOCUS_NONE
	root.add_child(dive_button)

	# End tonight's root here; the rest of the life force goes into fine roots.
	end_button = _scrap_button("end root here", 26)
	end_button.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	end_button.offset_left = -250
	end_button.offset_top = -80
	end_button.offset_right = -60
	end_button.offset_bottom = -24
	end_button.focus_mode = Control.FOCUS_NONE
	end_button.pressed.connect(end_early)
	root.add_child(end_button)
	_show_run_controls(false)

	compass = Compass.new()
	compass.camera = camera
	compass.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	compass.offset_left = -130
	compass.offset_right = -20
	compass.offset_top = 80
	compass.offset_bottom = 190
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
	# Ending only makes sense once the root has started to grow.
	end_button.visible = on and not _waiting_for_input


func set_hud_visible(on: bool) -> void:
	hud.visible = on


func _update_hud() -> void:
	if res == null:
		return
	var frac := clampf(res.life_force / maxf(_life_at_start, 0.001), 0.0, 1.0)
	_life_bar.size = Vector2(_life_bar_bg.size.x * frac, _life_bar_bg.size.y)
	_life_label.text = "life force %.1f" % res.life_force
	var t := roots.run_totals
	_counts.visible = mode == Mode.RUN or mode == Mode.DONE and not quiet_night
	_counts.text = "tonight:  water %.1f   N %.1f   P %.1f   K %.1f" % [t[0], t[1], t[2], t[3]]
	match mode:
		Mode.PICK:
			_hint.text = "Tap a point on a root to start tonight's root." if roots.graph.size() > 1 else ""
		Mode.RUN:
			_hint.text = "Move the stick (or WASD) to grow the root." if _waiting_for_input else ""
		Mode.DONE:
			_hint.text = "A quiet night below. Morning comes soon." if quiet_night else "The new root settles. Fine roots reach for what is near."
		_:
			_hint.text = ""


# --- modes ------------------------------------------------------------------

func begin_pick() -> void:
	mode = Mode.PICK
	quiet_night = false
	_life_at_start = maxf(res.life_force, 0.001)
	_show_run_controls(false)
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
	camera.position = roots.tip_position - roots.heading * 2.4 + Vector3.UP * 0.8


func _enter_run() -> void:
	mode = Mode.RUN
	quiet_night = false
	_waiting_for_input = true
	_life_at_start = maxf(res.life_force, 0.001)
	_hover.visible = false
	_tip.visible = true
	_show_run_controls(true)
	# The old roots did not change since the last run ended: only the new root is built.
	_rebuild_all(false)
	_update_hud()


## A night without life force, or after the run: the camera just looks around.
func begin_idle_overview() -> void:
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
	camera.position = _orbit_position()
	camera.look_at(_look, Vector3.UP)


func _orbit_position() -> Vector3:
	return _look + Vector3(sin(_orbit_yaw) * cos(_orbit_pitch), sin(_orbit_pitch), cos(_orbit_yaw) * cos(_orbit_pitch)) * _orbit_distance


# --- frame ------------------------------------------------------------------

func _process(delta: float) -> void:
	if roots == null:
		return
	match mode:
		Mode.RUN:
			(_dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", 15.0)
			_process_run(delta)
		Mode.PICK, Mode.DONE:
			# From the overview the whole underground glows; up close only the near dots do.
			(_dots.material_override as ShaderMaterial).set_shader_parameter("fog_far", _orbit_distance + 12.0)
			if not _pressing:
				_orbit_yaw += delta * 0.08
			var target := _orbit_position()
			camera.position = camera.position.lerp(target, 1.0 - exp(-3.0 * delta))
			_look_at_safely(_look)
	_update_hud()


func _process_run(delta: float) -> void:
	var stick := _stick()
	var dive := _dive_held()
	if _waiting_for_input:
		_tip.position = roots.tip_position
		camera.position = camera.position.lerp(roots.tip_position - roots.heading * 2.4 + Vector3.UP * 0.8, 1.0 - exp(-4.0 * delta))
		_look_at_safely(roots.tip_position + roots.heading * 1.2)
		if stick.length() < 0.2 and not dive:
			return
		_waiting_for_input = false
		end_button.visible = true
	if input_enabled and scripted_stick == null and (Input.is_physical_key_pressed(KEY_E) or Input.is_physical_key_pressed(KEY_ENTER)):
		end_early()
		return
	var alive := roots.advance(stick, dive, delta, ground, res)
	if not roots.last_collected.is_empty():
		dots_collected.emit(roots.last_collected.size())
	for i in roots.last_collected:
		_set_dot(i)
		_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]])
	for f in roots.last_finds:
		_on_find(f)
	_tip.position = roots.tip_position
	_rebuild_timer += delta
	if _rebuild_timer >= REBUILD_INTERVAL:
		_rebuild_timer = 0.0
		_rebuild_all(false)
	# Third-person camera behind and a little above the tip.
	var h := roots.heading
	var flat := Vector3(h.x, 0.0, h.z)
	var back := (h * 0.5 + (flat.normalized() if flat.length_squared() > 1e-4 else -camera.global_basis.z) * 0.5).normalized()
	var want := _outside_rocks(roots.tip_position - back * 2.4 + Vector3.UP * 0.8)
	camera.position = _outside_rocks(camera.position.lerp(want, 1.0 - exp(-4.0 * delta)))
	_look_at_safely(roots.tip_position + h * 1.2)
	if not alive:
		# end_run() already grew the fine roots and collected their dots.
		for i in roots.last_collected:
			_set_dot(i)
			_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]])
		_rebuild_all()
		begin_idle_overview()
		run_finished.emit(roots.run_totals)


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
func end_early() -> void:
	if mode != Mode.RUN or not roots.run_active or not input_enabled:
		return
	roots.last_collected = PackedInt32Array()
	roots.finish_early(ground, res)
	for i in roots.last_collected:
		_set_dot(i)
		_flash(ground.dot_positions[i], Resources.KIND_COLORS[ground.dot_kinds[i]])
	if not roots.last_collected.is_empty():
		dots_collected.emit(roots.last_collected.size())
	_rebuild_all()
	begin_idle_overview()
	run_finished.emit(roots.run_totals)


func _look_at_safely(target: Vector3) -> void:
	var d := target - camera.position
	if d.length_squared() < 1e-6:
		return
	var up := Vector3.UP if absf(d.normalized().dot(Vector3.UP)) < 0.98 else Vector3.FORWARD
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
	var pos := Vector2.ZERO
	var is_press := false
	var is_release := false
	var is_move := false
	if event is InputEventMouseButton and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
		pos = (event as InputEventMouseButton).position
		is_press = event.pressed
		is_release = not event.pressed
	elif event is InputEventMouseButton and mode == Mode.PICK:
		var b := (event as InputEventMouseButton).button_index
		if b == MOUSE_BUTTON_WHEEL_UP:
			_orbit_distance = maxf(3.0, _orbit_distance * 0.9)
		elif b == MOUSE_BUTTON_WHEEL_DOWN:
			_orbit_distance = minf(30.0, _orbit_distance * 1.1)
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
	elif is_move:
		if _pressing:
			var rel := (event as InputEventMouseMotion).relative
			if pos.distance_to(_press_pos) > 12.0:
				_dragged = true
			if _dragged:
				_orbit_yaw -= rel.x * 0.006
				_orbit_pitch = clampf(_orbit_pitch + rel.y * 0.004, -0.6, 1.2)
		elif mode == Mode.PICK:
			var id := pick_node_at(pos)
			_hover.visible = id >= 0
			if id >= 0:
				_hover.position = roots.graph.positions[id]
	elif is_release:
		_pressing = false
		if not _dragged and mode == Mode.PICK:
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
