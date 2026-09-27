class_name TreeView
extends Node3D
## Tree mode (design doc section 3): the tree on the meadow under the real sun arc.
## Hold anywhere to boost the sun; drag to orbit, wheel or pinch to zoom; once nutrients are
## spent, drag the glowing sun along its arc to move the day on; at sunset tap the ground.
## Reads GameState; the only things it writes are boost, time skips and the ground tap.

signal ground_tapped

const REBUILD_INTERVAL := 0.5
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
var _sky_mat: PhysicalSkyMaterial
var _grass: MultiMeshInstance3D
var _herbs: MultiMeshInstance3D
var _noise_tex: NoiseTexture2D
var _bark_mat: ShaderMaterial
var _leaf_mat: ShaderMaterial
var _ground_mat: ShaderMaterial
var _scenery: Scenery
var _env: Environment
var _meadow: Meadow
var _builder := BranchMeshBuilder.new()
var _rebuild_timer: float = 0.0
var _built_size: int = -1
var _births: Dictionary = {}  # node id -> time it appeared
var _time: float = 0.0

# Camera
## PI: the camera stands north of the tree and looks south, toward the sun's arc,
## so sunrise (east) is on the left as on the sun arc chart.
var _yaw: float = PI
var _pitch: float = 0.15
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
var _drag_mode: String = ""
var _press_phase: int = -1
var _press_time: float = 0.0  # "", "orbit", "sun", "boost"
var _touches: Dictionary = {}
var _pinch_start: float = 0.0
var _pinch_zoom: float = 1.0

# HUD
var hud: CanvasLayer
var _day_label: Label
var _life_label: Label
var _res_labels: Array[Label] = []
var _hint: PaperNote
var _boost_label: Label
var sun_arc: SunArc


func _ready() -> void:
	_builder.radius_scale = 3.2
	_build_world()
	_build_hud()
	if get_parent() == get_tree().root:
		setup(GameState.new_game(1))


func setup(p_state: GameState) -> void:
	state = p_state
	_meadow.build(state.ground)
	_plant_grass(state.seed)
	_scenery.build(state.seed, _bark_mat, _leaf_mat, _noise_tex)
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
	# A physically based sky lit by the sun light itself: blue by day, warm at the low sun.
	_sky_mat = PhysicalSkyMaterial.new()
	_sky_mat.rayleigh_coefficient = 2.0
	_sky_mat.mie_coefficient = 0.004
	_sky_mat.turbidity = 6.0
	_sky_mat.sun_disk_scale = 1.4
	_sky_mat.ground_color = Color(0.22, 0.3, 0.14)
	_sky_mat.energy_multiplier = 1.0
	var sky := Sky.new()
	sky.sky_material = _sky_mat
	sky.radiance_size = Sky.RADIANCE_SIZE_64
	_env = Environment.new()
	_env.background_mode = Environment.BG_SKY
	_env.sky = sky
	_env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	# Half sky, half a soft fill colour: the physical sky alone is too dark at a low sun.
	_env.ambient_light_sky_contribution = 0.45
	_env.ambient_light_color = Color(0.58, 0.64, 0.72)
	_env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	_env.tonemap_mode = Environment.TONE_MAPPER_ACES
	_env.tonemap_exposure = 1.1
	_env.glow_enabled = true
	_env.glow_intensity = 0.35
	_env.glow_bloom = 0.05
	_env.fog_enabled = true
	# Haze lies between the tree and the forest: the wood recedes, the tree stays crisp.
	_env.fog_density = 0.011
	_env.fog_light_color = Color(0.45, 0.55, 0.5)
	_env.fog_aerial_perspective = 0.4
	_env.fog_sky_affect = 0.05
	_env.adjustment_enabled = true
	_env.adjustment_saturation = 1.0
	_env.adjustment_contrast = 1.05
	camera.environment = _env
	add_child(camera)

	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(160, 160)
	ground.mesh = plane
	_noise_tex = NoiseTexture2D.new()
	_noise_tex.width = 512
	_noise_tex.height = 512
	_noise_tex.seamless = true
	_noise_tex.generate_mipmaps = true
	var fnl := FastNoiseLite.new()
	fnl.seed = 3
	fnl.frequency = 0.012
	fnl.fractal_octaves = 4
	_noise_tex.noise = fnl
	_ground_mat = ShaderMaterial.new()
	_ground_mat.shader = preload("res://tree/ground.gdshader")
	_ground_mat.set_shader_parameter("noise", _noise_tex)
	Assets.apply_ground(_ground_mat)
	ground.material_override = _ground_mat
	var gmat := _ground_mat
	add_child(ground)
	# (The open meadow had hills on the horizon; the clearing is closed in by forest instead.)
	for i in range(0):
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
	_scenery = Scenery.new()
	add_child(_scenery)

	_tree_mesh = MeshInstance3D.new()
	_bark_mat = ShaderMaterial.new()
	_bark_mat.shader = preload("res://tree/bark.gdshader")
	_bark_mat.set_shader_parameter("noise", _noise_tex)
	Assets.apply_bark(_bark_mat)
	_tree_mesh.material_override = _bark_mat
	_bark_mat.set_shader_parameter("rim_strength", 0.12)
	add_child(_tree_mesh)

	# Leaf clusters: crossed leaf cards per living tip, alpha-cut, swaying in the wind.
	_leaves = MultiMeshInstance3D.new()
	var lmm := MultiMesh.new()
	lmm.transform_format = MultiMesh.TRANSFORM_3D
	lmm.use_colors = true
	lmm.mesh = Foliage.cluster_mesh(8, 1.0, Assets.has_leaf_atlas())
	_leaves.multimesh = lmm
	_leaf_mat = ShaderMaterial.new()
	_leaf_mat.shader = preload("res://tree/leaf.gdshader")
	Assets.apply_leaf(_leaf_mat)
	_leaves.material_override = _leaf_mat
	# The player's tree catches the light at its edges, so it reads against the forest wall.
	_leaf_mat.set_shader_parameter("rim_strength", 0.1)
	_leaves.extra_cull_margin = 4.0

	# The meadow: dense soft clumps of grass on crossed cards, with herb and wildflower clumps
	# in between (Simon, play test: single blades did not fit the picture).
	_grass = _clump_layer(Foliage.clump_texture(false, 11))
	_herbs = _clump_layer(Foliage.clump_texture(true, 12))
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


const GRASS_CLUMPS := Budgets.MEADOW_GRASS_CLUMPS
const HERB_CLUMPS := Budgets.MEADOW_HERB_CLUMPS
const GRASS_RADIUS := 20.0


func _clump_layer(tex: Texture2D) -> MultiMeshInstance3D:
	var mmi := MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = Foliage.clump_mesh()
	mmi.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://tree/grass_card.gdshader")
	mat.set_shader_parameter("clump_texture", tex)
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.custom_aabb = AABB(Vector3(-24, -1, -24), Vector3(48, 3, 48))
	add_child(mmi)
	return mmi


func _plant_grass(seed: int) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "meadow clumps"])
	for layer in [[_grass, GRASS_CLUMPS, Vector2(0.45, 0.8), Vector2(0.24, 0.42)], [_herbs, HERB_CLUMPS, Vector2(0.35, 0.55), Vector2(0.2, 0.36)]]:
		var mm: MultiMesh = (layer[0] as MultiMeshInstance3D).multimesh
		var count: int = layer[1]
		mm.instance_count = count
		for i in range(count):
			var d := GRASS_RADIUS * pow(rng.randf(), 0.7)
			if d < 0.3:
				d = 0.3 + rng.randf() * 0.4
			var a := rng.randf() * TAU
			var w := rng.randf_range(layer[2].x, layer[2].y)
			var h := rng.randf_range(layer[3].x, layer[3].y)
			var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(w, h, w))
			mm.set_instance_transform(i, Transform3D(basis, Vector3(cos(a) * d, -0.02, sin(a) * d)))
			var v := rng.randf_range(0.82, 1.12)
			var pos := Vector2(cos(a) * d, sin(a) * d)
			# Soft patches: dry yellowish, lush and dark green, shade under the forest edge.
			var dry := clampf(0.5 + 0.5 * sin(pos.x * 0.23 + 1.3) * cos(pos.y * 0.19 - 0.4), 0.0, 1.0)
			var col := Color(v, v, v).lerp(Color(1.15 * v, 1.05 * v, 0.6 * v), dry * 0.8)
			if d > 16.0:
				col = col.darkened(clampf((d - 16.0) / 5.0, 0.0, 0.55))
			mm.set_instance_color(i, col)


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

	_hint = PaperNote.new(28, 61)
	_hint.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_hint.offset_left = 50
	_hint.offset_right = -50
	_hint.offset_top = -220
	_hint.offset_bottom = -120
	_hint.grow_vertical = Control.GROW_DIRECTION_BEGIN
	hud.add_child(_hint)


## A readout on a scrap of journal paper, handwritten, with an ink dot in the resource's colour.
func _pill(parent: Control, dot: Color) -> Label:
	var panel := PanelContainer.new()
	var sb := Paper.paper_box(96, 48, 20 + parent.get_child_count(), "all", 12.0)
	sb.content_margin_top = 4
	sb.content_margin_bottom = 4
	panel.add_theme_stylebox_override("panel", sb)
	panel.rotation_degrees = [-1.5, 1.0, -0.5, 1.8, -1.0][parent.get_child_count() % 5]
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
	var l := Paper.ink_label("", 25, Paper.INK, true)
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
	# The sun's arc is always there by day: drag the sun to move the day on (brighter once
	# there is nothing left to grow with).
	sun_arc.visible = state.can_skip_time()
	# Faint unless it matters: while dragging, or once the day has nothing left to grow with.
	sun_arc.modulate.a = 1.0 if state.day_is_spent() or sun_arc.is_dragging() else 0.35
	sun_arc.progress = s.clock.time_of_day / s.clock.daylight_fraction
	match state.phase:
		GameState.Phase.SUNSET:
			_hint.text = "The sun has set. Tap the ground to follow the roots down."
		GameState.Phase.DAY:
			if state.day_is_spent():
				_hint.text = ("Almost nothing left to grow with today" if state.sim.nutrient_missing() and not state.sim.graph.is_full() and state.sim.resources.stock[0] >= state.sim.cost_per_node else "Nothing left to grow with today") + ". Drag the sun along its arc to move the day on."
			elif state.day_number() <= 3 and not state.is_seed():
				# The first days: a quiet reminder of what can be done while the tree grows.
				_hint.text = "Hold anywhere to boost the sun. Drag the sun along its arc to pick the hour."
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
	if not input_enabled:
		# Releases can be swallowed while a page is open: forget every finger then.
		if _pressing:
			_end_press(false)
		_touches.clear()
		sun_arc.cancel_drag()
		_pinch_start = 0.0
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
	# Leaf clusters on every living twig (thin wood), so the crown fills out, not just the tips.
	var spots := PackedInt32Array()
	for id in range(2, g.size()):
		var bare_below := state.sim.height() * 0.3 if state.sim.height() > 4.0 else 0.0
	# Leaves on the thin twigs of the crown; the lower trunk of a grown tree stays bare.
		if g.radii[id] < 0.06 and not g.get_flag(id, "dead", false) and g.positions[id].y >= bare_below:
			spots.append(id)
	var mm := _leaves.multimesh
	mm.instance_count = spots.size()
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([state.seed, "leaf clusters"])
	# Each card stands for a spray of many leaves: a big crown needs bigger sprays to read as dense.
	var grow := 1.0 + state.sim.height() * 0.08
	var centre := state.sim.centroid()
	var crown_r := maxf(0.5, state.sim.height() * 0.35)
	for i in range(spots.size()):
		var id := spots[i]
		var s := (0.09 + 0.05 * rng.randf()) * grow
		var basis := Basis(Vector3.UP, rng.randf() * TAU) * Basis(Vector3.RIGHT, rng.randf_range(-0.4, 0.4))
		mm.set_instance_transform(i, Transform3D(basis.scaled(Vector3.ONE * s), g.positions[id]))
		var tint := rng.randf_range(0.85, 1.12)
		# Leaves deep inside the crown are in shade.
		var inner := clampf(1.0 - g.positions[id].distance_to(centre) / crown_r, 0.0, 1.0)
		tint *= lerpf(1.0, 0.55, inner)
		mm.set_instance_color(i, Color(tint * rng.randf_range(0.9, 1.05), tint, tint * rng.randf_range(0.85, 1.0)))


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
	var skippable := state.day_is_spent()
	if dir == Vector3.ZERO:
		# Sunset hold or night: the sun rests just below the western horizon.
		dir = Vector3(-1, -0.05, 0).normalized()
	_sun_disc.position = dir * SUN_DISTANCE
	var pulse := 1.0 + (0.35 * sin(_time * 3.0) if skippable else 0.0)
	_sun_disc.scale = Vector3.ONE * (1.4 if skippable else 1.0) * pulse
	_sun_disc.visible = skippable and dir.y > -0.1
	# The light stays on at the sunset hold, just under the horizon, so the physical sky glows.
	# At the sunset hold the sun rests right on the western horizon: the sky keeps its afterglow.
	var light_dir := dir if h > 0.0 else Vector3(-1, 0.012, 0.1).normalized()
	_sun_light.visible = true
	_sun_light.look_at_from_position(light_dir * 20.0, Vector3.ZERO, Vector3.UP if absf(light_dir.y) < 0.99 else Vector3.FORWARD)
	# A low sun still lights the clearing warmly (dawn burst, evening): at least 0.9.
	_sun_light.light_energy = maxf(0.9, 0.45 + 1.4 * minf(clock.light_level(), 1.6)) if h > 0.0 else 0.55
	_sun_light.shadow_enabled = h > 0.03
	_sun_light.shadow_blur = 2.5
	_sun_light.shadow_opacity = 0.8
	# Warm light and haze spread over the morning and evening, not just the first minutes.
	_sun_light.light_color = Color(1.0, 0.68, 0.42).lerp(Color(1.0, 0.96, 0.9), smoothstep(0.05, 0.5, h))
	var k := smoothstep(0.0, 0.6, h)
	_scenery.update(get_process_delta_time(), h > 0.0, h, _sun_light.light_color, state.sim.height(), camera.global_position)
	# The sky glows brighter near the horizon hours, and the haze takes the sun's colour.
	# A low sun: a bright golden sky and haze, the ground in raking light (the reference photos).
	_sky_mat.energy_multiplier = 1.5 + 1.7 * (1.0 - k) + (0.35 if clock.boost_active else 0.0)
	_env.fog_light_color = Color(0.45, 0.55, 0.5).lerp(_sun_light.light_color * 0.9, 0.4 * (1.0 - k))
	_env.fog_sun_scatter = 0.35 * (1.0 - k)
	# The eye adapts: a low sun and the dusk are exposed brighter, so the tree stays readable.
	_env.tonemap_exposure = 1.1 + 0.6 * (1.0 - k)
	# Never too dark by day: the dawn burst must be seen.
	# Brighter dusk (Simon: the start at sunset was too dark).
	_env.ambient_light_energy = (1.2 + 0.3 * k) if state.phase == GameState.Phase.DAY else 1.2


# --- camera -----------------------------------------------------------------

func _frame_camera(snap: bool, delta: float = 0.0) -> void:
	var real_height := maxf(state.sim.height(), 0.2)
	# Re-framed at snap, when the tree outgrows the frame, and once the day is over (sunset),
	# so the night's growth shows at dawn as a bigger tree in the same frame.
	var day_over := state.phase != GameState.Phase.DAY and state.day_number() != _framed_day
	if snap or day_over or real_height > _framed_height * 1.35:
		_framed_height = real_height
		_framed_day = state.day_number()
	var height := _framed_height
	var want_focus := Vector3(0, clampf(height * 0.5, 0.25, 30.0), 0)
	# The camera stays inside the clearing; a tall tree is seen with a wider lens instead.
	# The camera stays in the clearing; a tall tree is seen from lower down with a wider lens,
	# and the forest trees right behind the camera dissolve (near_fade in the scenery materials).
	# Never beyond the bushes at the clearing edge (about 17 m): the forest stays behind the camera.
	var want_distance := clampf(clampf(height * 1.7 + 2.2, 2.4, 16.0) * _zoom, 1.5, 16.5)
	camera.fov = clampf(55.0 + height * 1.2, 55.0, 85.0)
	var k := 1.0 if snap else 1.0 - exp(-2.0 * delta)
	_focus = _focus.lerp(want_focus, k)
	_distance = lerpf(_distance, want_distance, k)
	var orbit := _focus + Vector3(sin(_yaw) * cos(_pitch), sin(_pitch), cos(_yaw) * cos(_pitch)) * _distance
	orbit.y = maxf(orbit.y, 0.25)
	# The dive ends low beside the trunk looking into the soil; sunrise starts there and rises.
	var dive_point := Vector3(sin(_yaw), 0.0, cos(_yaw)) * 1.0 + Vector3(0, 0.8, 0)
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
	_press_phase = state.phase
	_press_time = _time
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
	# Only a short tap that began at sunset dives (not the end of a boost held through sunset).
	if is_release and _drag_mode != "orbit" and _drag_mode != "sun" and state.phase == GameState.Phase.SUNSET 			and _press_phase == GameState.Phase.SUNSET and _time - _press_time < 0.6:
		if _hits_ground(pos):
			ground_tapped.emit()
	_drag_mode = ""


func _near_sun(pos: Vector2) -> bool:
	# Only the visible, glowing sun can be grabbed; elsewhere a press is a boost.
	if not _sun_disc.visible or camera.is_position_behind(_sun_disc.global_position):
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
