class_name TreeView
extends Node3D
## Tree mode (design doc section 3): the tree on the meadow under the real sun arc.
## Hold anywhere to boost the sun; drag to orbit, wheel or pinch to zoom; once nutrients are
## spent, drag the glowing sun along its arc to move the day on; at sunset tap the ground.
## Reads GameState; the only things it writes are boost, time skips and the ground tap.

signal ground_tapped
## A branch was cut with the shears (segments cut).
signal pruned(segments: int)

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
## The phone's painted sky (Compatibility renderer only); null on a PC.
var _paint_sky: ProceduralSkyMaterial
var _grass: MultiMeshInstance3D
var _herbs: MultiMeshInstance3D
## Fine grass, sedge, clover and meadow flowers (GrassLook.apply_meadow2).
var _meadow2: MultiMeshInstance3D
var _noise_tex: NoiseTexture2D
var _bark_mat: ShaderMaterial
var _leaf_mat: ShaderMaterial
var _ground_mat: ShaderMaterial
var _spray_mat: ShaderMaterial
## The forest's bark template: the linden's tint, never the hero species' (Scenery duplicates it).
var _forest_bark: ShaderMaterial
var _compat: bool = RenderingServer.get_current_rendering_method() == "gl_compatibility"
var _ground: MeshInstance3D
## Radius of the clearing the world was last built for.
var _clearing: float = -1.0
var _scenery: Scenery
var _env: Environment
var _meadow: Meadow
## Today's wish place on the meadow (0.8.2.5).
var wish_plant: WishPlant
## Shade plants under the crown (the living clearing).
var _understory: Understory
var _builder := BranchMeshBuilder.new()
## Moon, stars and moonlight after sunset; rain, falling leaves and thunder (section 17).
var _night_sky: NightSky
var weather_fx: WeatherFx
## 0 by day, 1 in the full night: the sky deepens from dusk to night during the sunset hold.
var night_amount: float = 0.0
## Tools: a fixed night amount instead of the clock (-1 = from the clock).
var night_override: float = -1.0
## Tools: tilts the camera up by this many radians (to photograph the sky).
var look_up: float = 0.0
var _sunset_since: float = -1.0
## The garden shed is open (its camera looks out of the door into this world).
var _in_shed: bool = false
## The season's look (Almanac.season_look) and today's weather, for the mood.
var season: Dictionary = {}
var weather: Dictionary = {}
var rain_now: float = 0.0
var _grass_mats: Array[ShaderMaterial] = []
var _forest_leaf_mats: Array[ShaderMaterial] = []
var _wet_set: float = -1.0
var _dew_set: float = -1.0
## The shears: while on, taps cut branches instead of boosting (Pruning).
var prune_mode: bool = false:
	set(on):
		prune_mode = on
		if on and state != null:
			prune_height = state.sim.height() * PRUNE_FOCUS
## Where on the trunk the camera looks while the shears are out.
var prune_height: float = 1.0
var pruning: Pruning
var _rebuild_timer: float = 0.0
var _built_size: int = -1
## Care (0.6.3): the signals the crown was last built with, and the ones set on the shader.
var _built_care := PackedFloat32Array([0, 0, 0, 0])
var _built_marks := ""
var _shown_care := PackedFloat32Array([-1, -1, -1, -1])
## Tools may force a care look (a shot of a thirsty tree); empty: the game's own signals.
var care_override := PackedFloat32Array()
## Night adaptation (0.8.1, notes/look-0.8.1.md): exposure and the moonlit fill lifted by these
## shares at full night, so the tree reads on the phone without the night looking like day.
const NIGHT_EXPOSURE_LIFT := 0.45
const NIGHT_AMBIENT_LIFT := 0.6
## Daylight fill on the crown (hero_crown.gdshader day_fill): shadowed sprays dark green, not black.
const DAY_FILL := 0.09
## The crown's colour on the phone renderer (0.7 review, notes/fix-0.7.md): less red in the
## summer green (lime to mid green), a little less saturation, a softer sunlit lift.
## (0.8 review: the sunlit south side still went pale cream-lime at noon on the phone, where
## the tone mapper whitens bright greens: a deeper grade, no lift and less sheen there; notes/fix-0.8.md.)
const CROWN_GRADE_PHONE := Vector3(0.8, 0.92, 0.94)
const CROWN_SATURATION_PHONE := 0.8
const CROWN_SUN_LIFT_PHONE := 0.06
const CROWN_SHEEN_PHONE := 0.08
## And a little more daylight fill there, so the calmer green does not sink into black blotches.
const DAY_FILL_PHONE := 0.13
## The crown's moonlit fill at full night (0.8.2.1, look review: the crown nearly vanished
## against the black forest at night) and the wood's (young bark by day, all wood by night).
const NIGHT_FILL := 0.24
const NIGHT_FILL_PHONE := 0.32
const BARK_YOUNG_FILL := 0.22
const BARK_NIGHT_FILL := 0.3
var _fill_set: float = -1.0
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
## Hold to fast-forward the day (specs/fast-forward.md, 0.8.2): a still finger held this long
## by day starts it; it runs while the finger stays down. A tap stays the boost.
const HOLD_START := 0.6
## The day's clock while held (tuning lever; 0.8.2.2: 8x, was 4x, Simon: "twice as fast";
## 0.8.2.4: 16x, Simon: "doubled again").
const FAST_FORWARD := 16.0
## Real seconds the speed takes to ease in from 1x to FAST_FORWARD (no hitch when it starts).
const FAST_EASE := 0.5
## 0..1: how far the fast-forward has eased in (0 = the normal clock).
var _ff_amount: float = 0.0
## The small ink hourglass by the day scrap, shown only while held.
var hourglass: Hourglass
## The sunset picture (0.8.2.4, Simon): one tap runs the rest of the day to the sunset hold with
## the fast-forward's speed and steps; a tap anywhere (or on the picture again) stops it. It never
## goes past the sunset hold: the dive, and so the night's root run, still waits for the player.
var _to_sunset: bool = false
## The picture is offered only while at least this much of the day is left (game hours).
const SUNSET_RUN_MIN_HOURS := 0.5
## The run slows into the sunset: its speed is at most the game seconds left over this (real s),
## so the last game hour eases down instead of stopping at full speed.
const SUNSET_EASE_OUT := 0.35
var _touches: Dictionary = {}
var _pinch_start: float = 0.0
var _pinch_zoom: float = 1.0

# HUD
var hud: CanvasLayer
var _day_label: Label
## Life force, as a green vial (ui/vial.gd).
var vial: Vial
const VIAL_SIZE := Vector2(140, 165)
var _res_labels: Array[Label] = []
var _hint: PaperNote
## Today's wish, shown on the hint scrap in the morning (main._morning).
var _wish: String = ""


func show_wish(text: String) -> void:
	_wish = text
var _boost_label: Label
var sun_arc: SunArc
## True while a journal page is open (set by main): hints then stay quiet, the page says it.
var page_open: Callable = func() -> bool: return false


func _ready() -> void:
	_builder.radius_scale = 3.2
	_build_world()
	_build_hud()
	if get_parent() == get_tree().root:
		setup(GameState.new_game(1))


func setup(p_state: GameState) -> void:
	state = p_state
	_mood_day = -1
	_light_snap = true
	apply_species(state.sim.species)
	_clearing = -1.0
	# Another game's wish place (a load): built again.
	wish_plant.patch_id = -2
	refresh_clearing()
	_built_size = -1
	_births.clear()
	# Nodes that already exist do not twinkle.
	_rebuild()
	update_visitors()
	brush_pile.setup(state)
	_frame_camera(true)
	if vial != null:
		vial.track_day(state)
		vial.settle()


## The hero tree's look per species: bark and leaf tint on the existing materials
## (a birch reads white-barked, an oak dark).
func apply_species(sp: Species) -> void:
	var b := sp.bark_tint
	_bark_mat.set_shader_parameter("texture_tint", Vector3(b.r, b.g, b.b))
	season = Almanac.season_look_now()
	_spray_mat.set_shader_parameter("tint_mul", sp.leaf_tint * SeasonLook.leaf_tint(season))


## The season's look on the crown, forest, shrubs and meadow (after the calendar, or forced).
func apply_season() -> void:
	season = Almanac.season_look_now()
	apply_species(state.sim.species)
	SeasonLook.apply(self, season, state.sim.species.id)
	_grass_mats = SeasonLook.grass_materials(self)
	_forest_leaf_mats = SeasonLook.leaf_materials(_scenery)
	_wet_set = -1.0
	_dew_set = -1.0
	_wind_set = -1.0


# --- the day's moments (0.8.2.6, specs/journal-drawers-loop.md G2, G3) -------------

## The morning glow along last night's new roots.
var root_glow: RootGlow
## Real seconds the fast-forward still runs at normal speed for a moment (Moments.SLOW_SECONDS).
var _moment_left: float = 0.0
## The moment shown last (tests, tools) and its note on the scrap.
var last_moment: String = ""
var _note: String = ""
var _note_left: float = 0.0
const NOTE_SECONDS := 4.5
## The weather turn: its kind (Moments.TURNS) and real seconds since it began (-1: none).
var turn_kind: String = ""
var _turn_t: float = -1.0
var _wind_set: float = -1.0
## The dawn burst sparkles brighter for this long at the morning reveal.
var _reveal_left: float = 0.0


## A moment of the day: a fast-forward runs at normal speed for Moments.SLOW_SECONDS, then eases
## in again; and the moment shows (the flowers open, the visitor's line, the weather turns).
func moment(m: String, note: String = "") -> void:
	last_moment = m
	_moment_left = Moments.SLOW_SECONDS
	_ff_amount = 0.0
	match m:
		Moments.MORNING:
			refresh_wish()
			wish_plant.open_flowers()
		Moments.VISITOR:
			if note != "":
				_note = note
				_note_left = NOTE_SECONDS
		Moments.WEATHER:
			turn_kind = Moments.turn_kind(state.seed, state.day_number(), bool(state.weather_today().get("rain", false)))
			_turn_t = 0.0


## Seconds the moment's normal speed still lasts (0: none).
func moment_slow_left() -> float:
	return _moment_left


## The first view of a morning (main._rise, as the black lifts): last night's new roots glow
## through the soil for Moments.GLOW_SECONDS and the dawn burst's new twigs sparkle. False when
## no roots grew last night.
func morning_reveal() -> bool:
	_reveal_left = Moments.GLOW_SECONDS
	var g := state.sim.graph
	for id in range(maxi(1, state.sim.dawn_size), g.size()):
		if id < _built_size:
			_births[id] = _time
	var nr := state.night_roots
	if nr.x < 0:
		return false
	return root_glow.show_roots(Moments.night_segments(state.roots, nr.x, nr.y))


## Behind the sunrise's black (main._rise): the camera turns, if need be, until last night's new
## roots lie within FACE_NIGHT of straight ahead, beyond the tree, so the morning glow is seen.
const FACE_NIGHT := deg_to_rad(30.0)


func face_night_roots() -> void:
	var nr := state.night_roots
	if nr.x < 0:
		return
	var mid := Moments.night_middle(state.roots, nr.x, nr.y)
	if mid.length() < 0.5:
		return
	# The camera stands at (sin yaw, cos yaw) from the tree: opposite the roots to look at them.
	var want := atan2(-mid.x, -mid.y)
	var off := wrapf(want - _yaw, -PI, PI)
	if absf(off) > FACE_NIGHT:
		_yaw += off - signf(off) * FACE_NIGHT


func _update_moments(delta: float) -> void:
	_note_left = maxf(0.0, _note_left - delta)
	_reveal_left = maxf(0.0, _reveal_left - delta)
	if _turn_t >= 0.0:
		_turn_t += delta
		if _turn_t > Moments.TURN_IN + Moments.TURN_HOLD + Moments.TURN_OUT:
			_turn_t = -1.0
	# Not while the clearing is not shown (the shed, the night).
	if state.phase != GameState.Phase.DAY:
		_turn_t = -1.0
		root_glow.stop()


## The weather turn's strength now, 0..1 (0 when none).
func turn_amount() -> float:
	return Moments.turn_amount(_turn_t) if _turn_t >= 0.0 else 0.0


# --- world ------------------------------------------------------------------

## Rebuilds the ground, meadow and forest ring when the tree has outgrown its clearing.
## Called at setup and at night, while the tree scene is hidden.
## The ground under the crown follows its shade every time (Understory: only when grown).
func refresh_clearing() -> void:
	var r := Scenery.radius_for(state.sim.height())
	if r != _clearing:
		_build_clearing(r)
	_understory.refresh(state, [_grass, _herbs, _meadow2], _ground_mat)


## The meadow's plants again, after the day's wish placed a new deposit (its rushes or clover).
func refresh_meadow() -> void:
	_meadow.build(state.ground)
	RockLook.apply_meadow(_meadow)
	refresh_wish()


## Today's wish place (0.8.2.5): its plant large and in flower; none once the wish was reached.
func refresh_wish() -> void:
	var d := state.diary
	var pid := d.wish_patch if not d.wish_reached else -1
	wish_plant.show_wish(state.ground, pid, Terrain.edge)
	# In bud from sunrise until the morning moment (0.8.2.6), open the rest of the day.
	if not wish_plant.bloom_running():
		wish_plant.set_open(state.phase != GameState.Phase.DAY or state.sim.clock.clock_hour() >= float(Moments.HOURS[Moments.MORNING]))


## At sunset, a small ink ring is drawn around the wish plant for a moment (0.8.2.5).
func show_wish_ring() -> void:
	refresh_wish()
	wish_plant.ring()


func _build_clearing(r: float) -> void:
	_clearing = r
	Terrain.edge = r
	Shed.origin = Vector3(0.0, 0.0, r - 2.5)
	_ground.mesh = Terrain.ground_mesh(160.0 + (r - Scenery.CLEARING_RADIUS) * 2.0, 110)
	_meadow.build(state.ground)
	RockLook.apply_meadow(_meadow)
	refresh_wish()
	_plant_grass(state.seed)
	GrassLook.apply(self)
	GrassLook.apply_meadow2(self)
	_scenery.build(state.seed, _forest_bark, _leaf_mat, _noise_tex, r)
	ForestSprays.apply(_scenery)
	# The haze begins further out as the clearing grows, so the forest ring is not buried.
	_env.fog_depth_begin = r
	_env.fog_depth_end = r * 2.0 + 34.0
	_understory.forget_grass()
	_fog_begin = _env.fog_depth_begin
	_fog_end = _env.fog_depth_end
	apply_season()


## The brush pile at the clearing edge, with its hedgehog and wren (0.8).
var brush_pile: BrushPileView
## The nest in the crown, once it has come (Visitors; tree/bird_nest.gd). (No bench: Simon did not like it.)
var _nest: BirdNest


func update_visitors() -> void:
	if _nest == null:
		_nest = BirdNest.new()
		add_child(_nest)
	_nest.update(state)


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
	if _compat:
		# The phone renderer turns the physical sky grey at dawn, dusk and in rain (0.6 review): a
		# painted gradient sky instead, keyed to the hour and the weather (_paint_phone_sky).
		_paint_sky = ProceduralSkyMaterial.new()
		_paint_sky.ground_bottom_color = Color(0.1, 0.14, 0.08)
		_paint_sky.ground_horizon_color = Color(0.3, 0.36, 0.3)
		_paint_sky.sun_angle_max = 8.0
		_paint_sky.sky_curve = 0.12
		sky.sky_material = _paint_sky
	sky.radiance_size = Sky.RADIANCE_SIZE_64
	if Budgets.PHONE:
		# The sun moves every frame; the real-time path is the cheap one on a phone.
		sky.radiance_size = Sky.RADIANCE_SIZE_256
		sky.process_mode = Sky.PROCESS_MODE_REALTIME
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
	# Glow costs a phone more than it gives.
	_env.glow_enabled = not Budgets.PHONE
	_env.glow_intensity = 0.35
	_env.glow_bloom = 0.05
	_env.fog_enabled = true
	# Haze lies between the tree and the forest: the wood recedes, the tree stays crisp.
	# Depth fog: the tree and meadow stay crisp, only what lies beyond recedes.
	_env.fog_mode = Environment.FOG_MODE_DEPTH
	_env.fog_depth_begin = 18.0
	_env.fog_depth_end = 70.0
	_env.fog_depth_curve = 1.0
	_env.fog_density = 0.6
	# The haze lies over the land, not the sky (on the phone renderer it turned the sky grey).
	_env.fog_sky_affect = 0.15
	_env.fog_light_color = Color(0.45, 0.55, 0.5)
	_env.fog_aerial_perspective = 0.25
	_env.fog_sky_affect = 0.05
	_env.adjustment_enabled = true
	_env.adjustment_saturation = 1.0
	_env.adjustment_contrast = 1.12
	camera.environment = _env
	add_child(camera)

	var ground := MeshInstance3D.new()
	_ground = ground
	# Uneven ground: a level spot at the trunk, swells and hollows, rising toward the forest.
	ground.mesh = Terrain.ground_mesh(160.0, 110)
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
	if _compat:
		_ground_mat.set_shader_parameter("brightness", 0.72)
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
	wish_plant = WishPlant.new()
	add_child(wish_plant)
	root_glow = RootGlow.new()
	add_child(root_glow)
	_understory = Understory.new()
	add_child(_understory)
	_scenery = Scenery.new()
	add_child(_scenery)
	# The brush pile of cut branches, its hedgehog and wren (0.8).
	brush_pile = BrushPileView.new()
	add_child(brush_pile)

	_tree_mesh = MeshInstance3D.new()
	_bark_mat = ShaderMaterial.new()
	_bark_mat.shader = preload("res://tree/bark.gdshader")
	_bark_mat.set_shader_parameter("noise", _noise_tex)
	Assets.apply_bark(_bark_mat)
	_tree_mesh.material_override = _bark_mat
	_bark_mat.set_shader_parameter("rim_strength", 0.12)
	# Grey-brown linden bark instead of the warm orange (visuals thread).
	_bark_mat.set_shader_parameter("texture_tint", Vector3(0.38, 0.36, 0.33))
	_forest_bark = _bark_mat.duplicate()
	add_child(_tree_mesh)

	# Leaf clusters: crossed leaf cards per living tip, alpha-cut, swaying in the wind.
	_leaves = MultiMeshInstance3D.new()
	var lmm := MultiMesh.new()
	lmm.transform_format = MultiMesh.TRANSFORM_3D
	lmm.use_colors = true
	# Painted leaf sprays (visuals thread, approved by Simon): a dozen small leaves per card.
	lmm.use_custom_data = true
	lmm.mesh = CrownSprays.card_mesh()
	_leaves.multimesh = lmm
	_leaf_mat = ShaderMaterial.new()
	_leaf_mat.shader = preload("res://tree/leaf.gdshader")
	Assets.apply_leaf(_leaf_mat)
	# The forest keeps _leaf_mat as its template; the player's crown has its own leaf masses.
	_spray_mat = HeroCrown.material()
	# The player's tree stands out (Simon, play test 4): lighter, warmer leaves with a rim of light,
	# against a darker, cooler forest and a calmer meadow.
	_spray_mat.set_shader_parameter("tint_mul", Color(1.02, 1.03, 0.95))
	_spray_mat.set_shader_parameter("rim_strength", 0.12)
	if _compat:
		# The phone renderer shows the crown lime at noon and the autumn orange loud (0.7 review):
		# a calmer, mid green there, closer to the PC look (reasons in notes/fix-0.7.md).
		_spray_mat.set_shader_parameter("green_grade", CROWN_GRADE_PHONE)
		_spray_mat.set_shader_parameter("saturation", CROWN_SATURATION_PHONE)
		_spray_mat.set_shader_parameter("sun_lift", CROWN_SUN_LIFT_PHONE)
		_spray_mat.set_shader_parameter("sheen", CROWN_SHEEN_PHONE)
	_leaves.material_override = _spray_mat
	# The player's tree catches the light at its edges, so it reads against the forest wall.
	_leaf_mat.set_shader_parameter("rim_strength", 0.1)
	_leaves.extra_cull_margin = 4.0

	# The meadow: dense soft clumps of grass on crossed cards, with herb and wildflower clumps
	# in between (Simon, play test: single blades did not fit the picture).
	_grass = _clump_layer(Foliage.clump_texture(false, 11))
	_herbs = _clump_layer(Foliage.clump_texture(true, 12))
	_meadow2 = _clump_layer(Foliage.clump_texture(false, 13))
	add_child(_leaves)

	pruning = Pruning.new()
	pruning.view = self
	add_child(pruning)
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
	_night_sky = NightSky.new()
	add_child(_night_sky)
	weather_fx = WeatherFx.new()
	add_child(weather_fx)


static var GRASS_CLUMPS: int = Budgets.MEADOW_GRASS_CLUMPS
static var HERB_CLUMPS: int = Budgets.MEADOW_HERB_CLUMPS


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
	# The phone renderer shows the painted greens stronger: calmer, like the meadow grass
	# (0.6.3 review: the upright tufts read neon).
	if _compat:
		mat.set_shader_parameter("saturation", 0.72)
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.custom_aabb = AABB(Vector3(-46, -1, -46), Vector3(92, 6, 92))
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
			# Denser in the open meadow a few metres out, where the camera looks.
			var d := (_clearing + 2.0) * pow(rng.randf(), 0.55)
			# Bare earth right around the trunk (Meadow.BARE_RADIUS).
			if d < Meadow.BARE_RADIUS + 0.05:
				d = Meadow.BARE_RADIUS + 0.05 + rng.randf() * 0.5
			var a := rng.randf() * TAU
			# No grass inside the garden shed.
			if Vector2(cos(a) * d - Shed.origin.x, sin(a) * d - Shed.origin.z).length() < 2.4:
				d = maxf(0.3, d - 4.0)
			# A wider clearing spreads the same clumps further: they grow fuller to keep it a meadow.
			var fuller := 1.0 if Budgets.PHONE else sqrt(_clearing / Scenery.CLEARING_RADIUS)
			var w := rng.randf_range(layer[2].x, layer[2].y) * fuller
			var h := rng.randf_range(layer[3].x, layer[3].y) * lerpf(1.0, fuller, 0.5)
			var basis := Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(w, h, w))
			mm.set_instance_transform(i, Transform3D(basis, Terrain.at(Vector3(cos(a) * d, 0.0, sin(a) * d)) + Vector3(0, -0.02, 0)))
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
	# Life force is a glass vial in the top left corner (0.8.2.5, no number); the scraps sit beside it.
	vial = Vial.new()
	vial.position = Vector2(14, 12)
	vial.size = Vector2(VIAL_SIZE.x, VIAL_SIZE.y)
	hud.add_child(vial)
	var bar := HBoxContainer.new()
	bar.set_anchors_preset(Control.PRESET_TOP_WIDE)
	bar.offset_left = VIAL_SIZE.x + 8
	bar.offset_top = 24
	bar.offset_right = -150
	bar.add_theme_constant_override("separation", 10)
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(bar)
	_day_label = _pill(bar, Color(1, 1, 1, 0.0))
	hourglass = Hourglass.new()
	hourglass.visible = false
	_day_label.get_parent().add_child(hourglass)
	var bar2 := HBoxContainer.new()
	bar2.set_anchors_preset(Control.PRESET_TOP_WIDE)
	bar2.offset_left = VIAL_SIZE.x + 8
	bar2.offset_top = 80
	bar2.add_theme_constant_override("separation", 6)
	bar2.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(bar2)
	for k in range(4):
		_res_labels.append(_pill(bar2, Resources.KIND_COLORS[k], k))
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
	# 0.8.2.5: the needle points from the trunk to today's wish place.
	compass.target = func() -> Vector3: return Compass.wish_way(state, Vector3.ZERO)
	compass.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	# The old hand compass, its ring at the top.
	compass.offset_left = -168
	compass.offset_right = -14
	compass.offset_top = 68
	compass.offset_bottom = 222
	hud.add_child(compass)

	_hint = PaperNote.new(28, 61)
	_hint.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_hint.offset_left = 50
	_hint.offset_right = -50
	_hint.offset_top = -220
	_hint.offset_bottom = -120
	_hint.grow_vertical = Control.GROW_DIRECTION_BEGIN
	hud.add_child(_hint)


## A small ink dot in a resource's colour, for a readout (0.8.1: plain coloured dots in play,
## the shapes only in the journal's key and on the pellet tins).
static func ink_dot(color: Color, font_size: int = 22) -> Label:
	var d := Label.new()
	d.text = "●"
	d.name = "dot"
	d.add_theme_color_override("font_color", color.darkened(0.15))
	d.add_theme_font_size_override("font_size", font_size)
	d.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	d.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return d


## A readout on a scrap of journal paper, handwritten, with an ink dot in the resource's colour.
func _pill(parent: Control, dot: Color, kind: int = -1) -> Label:
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
	if kind >= 0:
		row.add_child(ink_dot(Resources.KIND_COLORS[kind]))
	elif dot.a > 0.0:
		row.add_child(ink_dot(dot))
	var l := Paper.ink_label("", 25, Paper.INK, true)
	row.add_child(l)
	parent.add_child(panel)
	return l


func set_shed_open(on: bool) -> void:
	_scenery.set_shed_open(on)
	_in_shed = on


func set_hud_visible(on: bool) -> void:
	hud.visible = on


## What the crown shows it lacks (the strongest sign), and where tonight's root finds it; ""
## while it shows nothing. The HUD's letters N, P and K go with the full names.
func _care_words() -> String:
	var c := care_now()
	for k in range(4):
		if c[k] >= Care.SHOW_MIN:
			return "%s Steer tonight's root toward the %s dots." % [CARE_LOOKS[k], Care.DOT_WORDS[k]]
	return ""


const CARE_LOOKS: Array[String] = ["The leaves hang: thirsty.",
	"The new shoots stay small and sparse: short of nitrogen (N).",
	"Some leaf masses stay bare: short of phosphorus (P).",
	"Some leaf masses stay bare: short of potassium (K)."]


## Names and dot colours of the nutrients the tree lacks right now, for the hint.
func _missing_nutrients() -> Array:
	var names := ["water", "nitrogen (N)", "phosphorus (P)", "potassium (K)"]
	var colours := Care.DOT_WORDS
	var n: Array[String] = []
	var c: Array[String] = []
	var sim := state.sim
	for k in range(4):
		if sim.species.needs[k] > 0.0 and sim.resources.stock[k] < sim.cost_per_node * sim.species.needs[k]:
			n.append(names[k])
			c.append(colours[k])
	return [" and ".join(n), " and ".join(c)]


func _update_hud() -> void:
	var s := state.sim
	_day_label.text = "the seed" if state.is_seed() and state.day_number() == 0 else "day %d" % state.day_number()
	vial.track_day(state)
	var short: Array[String] = ["water", "N", "P", "K"]
	for k in range(4):
		_res_labels[k].text = "%s %.1f" % [short[k], s.resources.amount(k)]
	_boost_label.get_parent().get_parent().visible = s.clock.boost_active
	_boost_label.text = "sun boost %d:%02d" % [int(s.clock.boost_remaining / s.clock.hour_seconds() * 60.0) / 60, int(s.clock.boost_remaining / s.clock.hour_seconds() * 60.0) % 60]
	# The sun's arc is always there by day: drag the sun to move the day on (brighter once
	# there is nothing left to grow with).
	# The arc shows the time of day and the boost; it is no longer dragged.
	sun_arc.visible = state.phase == GameState.Phase.DAY
	# Faint unless it matters: while dragging, or once the day has nothing left to grow with.
	sun_arc.modulate.a = 0.75
	sun_arc.boost_hours = s.clock.boost_remaining / s.clock.hour_seconds()
	sun_arc.progress = s.clock.time_of_day / s.clock.daylight_fraction
	match state.phase:
		GameState.Phase.SUNSET:
			_hint.text = "The sun has set. Tap the ground or swipe up to dive to the roots."
		GameState.Phase.DAY:
			if state.day_is_spent():
				_hint.text = ("Almost nothing left to grow with today" if state.sim.nutrient_missing() and not state.sim.graph.is_full() and state.sim.resources.stock[0] >= state.sim.cost_per_node else "Nothing left to grow with today") + ". The leaves still gather life force for tonight."
			elif prune_mode:
				_hint.text = "Touch a branch to see where the shears would cut; lift the finger to cut."
			elif _note_left > 0.0:
				# The day's visitor (0.8.2.6 moment): its line for a few seconds.
				_hint.text = _note
			elif _care_words() != "":
				# What the crown shows, in the same words as the care page (0.6.3 review: the line
				# named a need the tree did not show).
				_hint.text = _care_words()
			elif state.sim.nutrient_missing() and not state.is_seed():
				# Today's stock ran out, but the tree lacks nothing it shows: what tonight can bring.
				_hint.text = "Today's %s ran out; tonight's root can bring more from the %s dots." % _missing_nutrients()
			elif _wish != "" and state.sim.clock.time_of_day < state.sim.clock.daylight_fraction * 0.35:
				# The morning's wish, until mid-morning.
				_hint.text = _wish
			elif state.day_number() <= 3 and not state.is_seed():
				# The first days: a quiet reminder of what can be done while the tree grows.
				_hint.text = "Tap to let the sun shine brighter for an hour."
			else:
				_hint.text = ""
		_:
			_hint.text = ""
	if page_open.call():
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
		# A page or the shed took over: the run to the sunset stops with it (0.8.2.4).
		_to_sunset = false
	_time += delta
	_update_hold(delta)
	_update_moments(delta)
	_rebuild_timer += delta
	_update_care()
	if _rebuild_timer >= REBUILD_INTERVAL and (state.sim.graph.size() != _built_size or _care_changed() or marks_key() != _built_marks):
		_rebuild_timer = 0.0
		var t0 := Time.get_ticks_usec()
		_rebuild()
		_perf_rebuild_ms = maxf(_perf_rebuild_ms, (Time.get_ticks_usec() - t0) / 1000.0)
	_perf_log(delta)
	_update_twinkles()
	_update_sun()
	_frame_camera(false, delta)
	_update_hud()


## Debug builds (the phone test APK) log the frame rate and the slowest tree rebuild every
## five seconds, so performance can be read over adb logcat.
var _perf_rebuild_ms: float = 0.0
var _perf_timer: float = 0.0
var _perf_worst: float = 0.0
var _perf_last: int = 0


func _perf_log(delta: float) -> void:
	if not OS.is_debug_build() or OS.has_feature("editor"):
		return
	_perf_timer += delta
	# 0.8.2.4: the wall clock between frames, not delta. Godot caps a frame's delta (about
	# 150 ms on the phone), so the log showed 150 ms for the 0.95 s freezes. This also counts the
	# switch frames (the shed, the dive, the bonsai), since the tree view always processes.
	var now := Time.get_ticks_usec()
	if _perf_last > 0 and now - _perf_last < 10000000:  # (not the time spent in the background)
		_perf_worst = maxf(_perf_worst, (now - _perf_last) / 1000000.0)
	_perf_last = now
	if _perf_timer >= 5.0:
		print("perf: fps %d, worst frame %.0f ms, slowest rebuild %.0f ms, nodes %d, clearing %.0f m" % [Engine.get_frames_per_second(), _perf_worst * 1000.0, _perf_rebuild_ms, state.sim.graph.size(), _clearing])
		_perf_timer = 0.0
		_perf_worst = 0.0
		_perf_rebuild_ms = 0.0


func _rebuild() -> void:
	# A cut or dieback changes the crown's reach (crown_reach).
	_reach_key = -1
	var g := state.sim.graph
	var first_build := _built_size < 0
	if not first_build:
		for id in range(_built_size, g.size()):
			_births[id] = _time
	_built_size = g.size()
	# Wood as thick as the tree is big: a sapling's whip is a finger thick, not a pole.
	_builder.radius_scale = wood_scale(state.sim.height())
	# Greying bark on a twig the tree marks for pruning, and on twigs that died back (0.7).
	_builder.node_colors = HeroCrown.bark_colors(state.sim)
	_tree_mesh.mesh = _builder.build(g)
	_seed.visible = state.is_seed()
	# Leaf masses at the twig ends, shaded dark inside and light at the sunny rim.
	_built_care = care_now()
	_built_marks = marks_key()
	var autumn := maxf(float(season.get("autumn", 0.0)), float(season.get("late", 0.0)))
	var crown := HeroCrown.populate(_leaves.multimesh, state.sim, state.seed, _built_care, autumn)
	weather_fx.set_crown(crown)


## What the crown shows it lacks (GameState.care_signals, or the tools' override).
func care_now() -> PackedFloat32Array:
	return care_override if care_override.size() == 4 else state.care_signals()


## Shape first: thirst hangs the sprays (a shader uniform, every frame); the colour cues too.
func _update_care() -> void:
	# The crown's daylight fill (0.6.3 noon lift): full by day, gone at night.
	var fill := (DAY_FILL_PHONE if _compat else DAY_FILL) * (1.0 - night_amount) * (1.0 - 0.6 * rain_now)
	# 0.8.2.1: by night a faint moonlit fill instead, so the crown stays a shape against the black
	# forest (it nearly vanished on the phone), far below the day's.
	fill += (NIGHT_FILL_PHONE if _compat else NIGHT_FILL) * night_amount
	if absf(fill - _fill_set) > 0.002:
		_fill_set = fill
		_spray_mat.set_shader_parameter("day_fill", fill)
		# The wood the same way, and a young tree's thin bark (near black on days 1 to 6) a little
		# lifted by day: its own colour, not a glow.
		var young := 1.0 - smoothstep(2.0, 8.0, state.sim.height())
		var g := BARK_YOUNG_FILL * young * (1.0 - night_amount) + BARK_NIGHT_FILL * night_amount
		_bark_mat.set_shader_parameter("glow", Color(g, g, g * 1.1))
	var c := care_now()
	var names := ["thirst", "pale", "dull", "scorch"]
	for k in range(4):
		if absf(c[k] - _shown_care[k]) > 0.01:
			_shown_care[k] = c[k]
			_spray_mat.set_shader_parameter(names[k], c[k])


## The marked twigs and how strongly each shows, in steps of an eighth (0.7): the crown and the
## bark are rebuilt when it changes (a mark eases in over the morning, or a cut took it).
func marks_key() -> String:
	var key := ""
	for m in state.sim.marks:
		key += "%d:%d," % [int(m["id"]), int(round(state.sim.mark_strength(m) * 8.0))]
	return key


## The baked cues (nitrogen: new shoots; phosphorus, potassium: bare masses) changed enough to
## rebuild the crown.
func _care_changed() -> bool:
	var c := care_now()
	for k in [1, 2, 3]:
		if absf(c[k] - _built_care[k]) > 0.12 or (c[k] == 0.0) != (_built_care[k] == 0.0):
			return true
	return false


## Scale from the pipe-model radii to the drawn wood: thin for a young tree, 3.2 for a grown one.
static func wood_scale(height: float) -> float:
	return clampf(0.9 + height * 0.1, 1.0, 3.2)


func _update_twinkles() -> void:
	var g := state.sim.graph
	var alive: Array = []
	for id in _births.keys():
		if _time - float(_births[id]) < TWINKLE_SECONDS and id < g.size():
			alive.append(id)
		else:
			_births.erase(id)
	# Only every fourth new segment glows, and softly: many fast-flickering yellow points read
	# as screen flicker (Simon, 0.5.1).
	# At the morning reveal (0.8.2.6) every second one, a little larger: the dawn burst shows.
	var every := 2 if _reveal_left > 0.0 else 4
	# None right in front of the lens (a camera rising past the sapling saw one fill the picture).
	var eye := camera.global_position
	alive = alive.filter(func(id: int) -> bool: return id % every == 0 and eye.distance_squared_to(g.positions[id]) > 1.0)
	var big := 1.4 if _reveal_left > 0.0 else 1.0
	var mm := _twinkles.multimesh
	mm.instance_count = alive.size()
	for i in range(alive.size()):
		var id: int = alive[i]
		var age := (_time - float(_births[id])) / TWINKLE_SECONDS
		var flicker := 0.85 + 0.15 * sin(_time * 2.0 + id * 1.7)
		var s := (0.22 * (1.0 - age) * flicker + 0.04) * big
		mm.set_instance_transform(i, Transform3D(Basis.from_scale(Vector3.ONE * s), g.positions[id]))
		mm.set_instance_color(i, Color(1.0, 0.92, 0.6, (1.0 - age) * flicker * 0.6))


func twinkle_count() -> int:
	return _twinkles.multimesh.instance_count


## How long the light takes from the last daylight to the evening's (and back), in seconds.
const SUNSET_EASE := 0.8
## How fast the night falls once the player dives before the dusk has deepened (seconds).
const NIGHT_EASE := 0.9
var _day_w: float = 1.0
## The next frame sets the light at once (setup, snap_camera: behind a black screen).
var _light_snap: bool = true


## 1 while the sun is up, 0 after it set, eased over SUNSET_EASE (smoothstepped).
func _day_weight(h: float) -> float:
	var want := 1.0 if h > 0.0 else 0.0
	_day_w = want if _light_snap else move_toward(_day_w, want, get_process_delta_time() / SUNSET_EASE)
	return smoothstep(0.0, 1.0, _day_w)


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
	var light_dir := dir if h > 0.0 else Vector3(-1, 0.06, 0.1).normalized()
	_sun_light.visible = true
	_sun_light.look_at_from_position(light_dir * 20.0, Vector3.ZERO, Vector3.UP if absf(light_dir.y) < 0.99 else Vector3.FORWARD)
	# A low sun still lights the clearing warmly (dawn burst, evening): at least 0.9.
	# 0.8.2.4 (phone: dusk turned to night in one frame): the day's light eases into the
	# evening's over SUNSET_EASE seconds instead of switching when the sun touches the horizon.
	var dw := _day_weight(h)
	_sun_light.light_energy = lerpf(0.55, maxf(0.9, 0.45 + 1.4 * minf(clock.light_level(), 1.6)), dw)
	_sun_light.shadow_enabled = h > 0.03
	_sun_light.shadow_blur = 2.5
	_sun_light.shadow_opacity = 0.8
	# Warm light and haze spread over the morning and evening, not just the first minutes.
	_sun_light.light_color = Color(1.0, 0.68, 0.42).lerp(Color(1.0, 0.96, 0.9), smoothstep(0.05, 0.5, h))
	var k := smoothstep(0.0, 0.6, h)
	_scenery.update(get_process_delta_time(), h > 0.0, h, _sun_light.light_color, state.sim.height(), camera.global_position)
	_meadow.set_daylight(clampf(h * 3.0, 0.0, 1.0))
	wish_plant.set_daylight(clampf(h * 3.0, 0.0, 1.0))
	# The sky glows brighter near the horizon hours, and the haze takes the sun's colour.
	# A low sun: a bright golden sky and haze, the ground in raking light (the reference photos).
	_sky_mat.energy_multiplier = 1.5 + 1.7 * (1.0 - k) + (0.35 if clock.boost_active else 0.0)
	# After sunset the haze stays cool blue-grey; only while the sun is up does it warm.
	var warm := 0.25 * (1.0 - k) * dw
	_env.fog_light_color = Color(0.32, 0.38, 0.48).lerp(Color(0.45, 0.55, 0.5), dw).lerp(_sun_light.light_color * 0.9, warm)
	_env.fog_sun_scatter = 0.08 * (1.0 - k)
	# The eye adapts: a low sun and the dusk are exposed brighter, so the tree stays readable.
	_env.tonemap_exposure = 1.1 + 0.3 * (1.0 - k)
	# Never too dark by day: the dawn burst must be seen.
	# Brighter dusk (Simon: the start at sunset was too dark).
	_env.ambient_light_energy = (1.2 + 0.3 * k) if state.phase == GameState.Phase.DAY else 1.2


# --- camera -----------------------------------------------------------------
	# Golden hour (visuals thread): less sun glare in the haze, real sun and shade by day, a sun
	# that stays warm until it is well up, and haze that takes its colour.
	_env.fog_sun_scatter *= 0.35
	# (By day; weighted by dw, so the evening takes over gradually.)
	# Less flat fill by day, but never a black dawn.
	_env.ambient_light_energy *= lerpf(1.0, lerpf(0.95, 0.6, smoothstep(0.0, 0.2, h)), dw)
	var golden := 1.0 - smoothstep(0.03, 0.55, h)
	_sun_light.light_color = _sun_light.light_color.lerp(Color(1.0, 0.95, 0.88).lerp(Color(1.0, 0.7, 0.4), golden), dw)
	_sun_light.light_energy *= lerpf(1.0, 1.45 * (1.0 + 0.25 * golden), dw)
	_env.fog_light_color = _env.fog_light_color.lerp(Color(0.85, 0.7, 0.5), golden * (0.2 if _compat else 0.5) * dw)
	# The phone's simpler renderer lights more brightly: tone it down to match the PC.
	if _compat:
		# The phone renderer shows the sky darker and does not fog the far wood: lift the sky, haze
		# the wall by hand (0.5.1, from Simon's phone test).
		_sky_mat.energy_multiplier *= 1.8
		_scenery.set_haze(_env.fog_light_color * 0.9, 0.55)
		_sun_light.light_energy *= 0.7
		_env.ambient_light_energy *= 0.8
		_env.tonemap_exposure *= 0.85
	_update_mood(h)
	if _paint_sky:
		_paint_phone_sky(h)


## The phone's painted sky: a clear blue by day, warm near the horizon at a low sun, a deep blue
## night (the stars and moon are drawn on top), grey in a shower.
func _paint_phone_sky(h: float) -> void:
	var golden := 1.0 - smoothstep(0.0, 0.45, h) if h > -0.05 else 0.0
	var top := Color(0.3, 0.5, 0.8).lerp(Color(0.34, 0.42, 0.66), golden)
	var horizon := Color(0.7, 0.8, 0.9).lerp(Color(0.98, 0.7, 0.45), golden)
	var n := night_amount
	top = top.lerp(Color(0.05, 0.08, 0.18), n)
	horizon = horizon.lerp(Color(0.14, 0.18, 0.3), n)
	var r := rain_now
	top = top.lerp(Color(0.44, 0.48, 0.53), r * 0.7)
	horizon = horizon.lerp(Color(0.58, 0.62, 0.64), r * 0.7)
	_paint_sky.sky_top_color = top
	_paint_sky.sky_horizon_color = horizon
	_paint_sky.ground_horizon_color = horizon.darkened(0.45)
	_paint_sky.sky_energy_multiplier = 1.0
	_paint_sky.sun_curve = 0.1


# --- night, weather and season (design doc section 17) ------------------------------

var _fog_begin: float = 18.0
var _mood_day: int = -1
var _fog_end: float = 70.0


## After the sun: deepens dusk into night with the moon and stars, and lays today's weather over
## the light (a shower darkens and greys the sky, mist thickens the haze, dew glints on the grass).
func _update_mood(_h: float) -> void:
	var clock := state.sim.clock
	# The night deepens over the sunset hold: dusk first, a starry night half a minute later.
	match state.phase:
		GameState.Phase.DAY:
			_sunset_since = -1.0
			night_amount = 0.0
		GameState.Phase.SUNSET:
			if _sunset_since < 0.0:
				_sunset_since = _time
			night_amount = smoothstep(3.0, 30.0, _time - _sunset_since)
		GameState.Phase.NIGHT:
			# 0.8.2.4 (phone: a dive early in the dusk brightened the picture in one frame, the
			# night's exposure lift): the night falls over NIGHT_EASE while the clearing still shows.
			night_amount = 1.0 if _light_snap or not visible else move_toward(night_amount, 1.0, get_process_delta_time() / NIGHT_EASE)
	_light_snap = false
	if night_override >= 0.0:
		night_amount = night_override
	var n := night_amount
	# Once a game day: the calendar's season and the moon's phase (the real date moves on).
	if state.day_number() != _mood_day:
		_mood_day = state.day_number()
		apply_season()
		_night_sky.set_phase(Almanac.moon_phase_now())
	weather = state.weather_today()
	var t := clock.time_of_day / clock.daylight_fraction if state.phase == GameState.Phase.DAY else -1.0
	rain_now = Almanac.rain_amount(weather, t) if t >= 0.0 else 0.0
	# The afternoon's weather turn (0.8.2.6): look only.
	var turn := turn_amount()
	if turn_kind == "shower":
		rain_now = maxf(rain_now, 0.7 * turn)
	var mist := Almanac.mist_amount(weather, t) if t >= 0.0 else 0.0
	var dew := Almanac.dew_amount(weather, t) if t >= 0.0 else 0.0
	var r := rain_now
	if n > 0.0:
		# Night: the sky dims to a deep blue, the sun's warm fill fades, a cool moonlit fill stays
		# so the tree and the meadow still read.
		# The physical sky takes its brightness from the sun light: dimming the light for the scene
		# is paid back on the sky (near the horizon its colour goes about as the energy to the 0.8),
		# then the sky dims to a deep blue.
		var dim := 1.0 - 0.97 * n
		_sun_light.light_energy *= dim
		_sky_mat.energy_multiplier *= pow(dim, -0.8) * lerpf(1.0, 0.35, n)
		_sun_light.light_color = _sun_light.light_color.lerp(Color(0.6, 0.66, 0.9), n)
		_env.ambient_light_color = Color(0.58, 0.64, 0.72).lerp(Color(0.42, 0.5, 0.74), n)
		_env.ambient_light_sky_contribution = lerpf(0.45, 0.15, n)
		_env.fog_light_color = _env.fog_light_color.lerp(Color(0.1, 0.13, 0.22), n)
		if _compat:
			_scenery.set_haze(_env.fog_light_color * 0.9, 0.55)
	else:
		_env.ambient_light_color = Color(0.58, 0.64, 0.72)
		_env.ambient_light_sky_contribution = 0.45
	# 0.8.1 (broken item 17, Simon: "es ist noch sehr dunkel"): the eye adapts to the night, so
	# the moonlit tree and meadow read at normal phone brightness; the sky stays a deep blue and
	# the moonlight cool, so it still reads as night (notes/look-0.8.1.md).
	# (Not inside the shed, whose night the lantern lights.)
	var adapt := 0.0 if _in_shed else n
	_env.tonemap_exposure *= 1.0 + NIGHT_EXPOSURE_LIFT * adapt
	_env.ambient_light_energy *= 1.0 + NIGHT_AMBIENT_LIFT * adapt
	# The eye sees fewer colours by night (and the meadow's green would glow).
	_env.adjustment_saturation = 1.0 - 0.4 * n
	if r > 0.0:
		# A shower: an overcast, greyer sky, flat light, the haze closer.
		_sky_mat.energy_multiplier *= 1.0 - (0.25 if _compat else 0.45) * r
		_sun_light.light_energy *= 1.0 - 0.6 * r
		_sun_light.shadow_opacity = 0.8 * (1.0 - 0.7 * r)
		_env.fog_light_color = _env.fog_light_color.lerp(Color(0.36, 0.4, 0.43), r * 0.7)
		_env.adjustment_saturation *= 1.0 - 0.25 * r
	# Mist: the haze comes down into the clearing, soft and pale, and lifts as the morning goes on.
	if turn_kind == "cloud" and turn > 0.0:
		# A cloud passes over the sun: the light dims and the shadows soften, then it comes back.
		_sun_light.light_energy *= 1.0 - 0.5 * turn
		_sun_light.shadow_opacity *= 1.0 - 0.6 * turn
		_env.ambient_light_energy *= 1.0 - 0.15 * turn
	var wind := 1.0 + (1.8 * turn if turn_kind == "breeze" else 0.6 * turn)
	if absf(wind - _wind_set) > 0.03:
		_wind_set = wind
		_spray_mat.set_shader_parameter("wind_strength", wind)
		for mat in _grass_mats:
			mat.set_shader_parameter("wind_strength", wind)
	var m := maxf(mist, r * 0.25)
	_env.fog_depth_begin = lerpf(_fog_begin, 4.0, m)
	_env.fog_depth_end = lerpf(_fog_end, _fog_begin + 40.0, m)
	_env.fog_density = lerpf(0.6, 0.72, m)
	if mist > 0.0:
		_env.fog_light_color = _env.fog_light_color.lerp(Color(0.66, 0.7, 0.7), mist * 0.6)
		_env.fog_sun_scatter += 0.08 * mist
		if _compat:
			_scenery.set_haze(_env.fog_light_color, lerpf(0.55, 0.85, mist))
	if _in_shed and n > 0.0:
		# From inside the shed at night the eye is used to the lantern: the moonlit fill that lets
		# the tree read outside would light the room like day (0.6.3 review), so it falls away and
		# the lantern's warm pool lights the bench; the night outside the door reads darker.
		_env.ambient_light_energy *= lerpf(1.0, 0.3, n)
		_sun_light.light_energy *= lerpf(1.0, 0.55, n)
		_env.tonemap_exposure *= lerpf(1.0, 0.85, n)
	_scenery.set_mood(n, r)
	brush_pile.update(get_process_delta_time(), r)
	_understory.set_night(n)
	var cam := get_viewport().get_camera_3d()
	var eye := cam.global_position if cam != null else camera.global_position
	_night_sky.update(eye, n, maxf(r, mist * 0.5))
	weather_fx.night = night_amount
	weather_fx.update(eye, -camera.global_basis.z, t, weather, r, float(season.get("fall", 0.0)))
	# Wet leaves and grass during and after a shower; dew in the early morning.
	var wet := r
	if absf(wet - _wet_set) > 0.02:
		_wet_set = wet
		_spray_mat.set_shader_parameter("wet", wet)
		# The wood only a little: far wet leaves glinting read as snow.
		for mat in _forest_leaf_mats:
			mat.set_shader_parameter("wet", wet * 0.25)
		for mat in _grass_mats:
			mat.set_shader_parameter("wet", wet)
	if absf(dew - _dew_set) > 0.02:
		_dew_set = dew
		for mat in _grass_mats:
			mat.set_shader_parameter("dew", dew)

## Vertical field of view the camera prefers; it only widens when the clearing is too small.
const FRAME_FOV := 50.0
## The album's morning photos all look from here (the default view: from the north, sun behind).
const ALBUM_YAW := PI
var _framed_width: float = 1.0
var _framed_reach: float = 0.5
var _album: bool = false


## The height to frame for a tree this tall: at least a few metres, so a sapling stands small
## in the clearing with the forest beside it, and the tree's height once it is grown.
static func frame_height(tree_height: float) -> float:
	return maxf(tree_height, 0.2) + 3.0 * (1.0 - smoothstep(0.0, 12.0, tree_height))


## Tree-mode framing (0.8.1, broken item 25, notes/look-0.8.1.md): the frame follows the tree's
## real height and crown width on the portrait screen. The tree's foot sits at FRAME_BASE and its
## top at FRAME_TOP (shares of the screen height from the top, clear of the HUD's pills), and the
## crown keeps FRAME_SIDE of the screen width free on each side.
const FRAME_TOP := 0.17
const FRAME_BASE := 0.86
const FRAME_SIDE := 0.05
## The young tree's frame: its height plus a little meadow (m), fading out by FRAME_PAD_UNTIL m,
## so a seedling stands clearly in view rather than small and low in a field of grass.
const FRAME_PAD := 0.8
const FRAME_PAD_UNTIL := 6.0
## The closest the camera comes for a small tree (m): far enough to look over the meadow grass
## (at 1.4 m the dawn view stood in the grass blades); a longer lens, down to FRAME_MIN_FOV
## degrees, then holds the seedling large.
const FRAME_MIN_DISTANCE := 2.4
const FRAME_MIN_FOV := 34.0
## For a crown too broad to frame from inside the clearing's usual orbit (a grown linden is wider
## than tall): the camera may step back to FRAME_EDGE metres inside the clearing's edge (it is high
## up then, above the shed and the brush pile) and widen the lens up to FRAME_MAX_FOV degrees
## (vertical; on a 9:16 screen about 64 degrees across).
const FRAME_EDGE := 1.0
const FRAME_MAX_FOV := 96.0
## The frame follows a tree that grew this much (height or crown reach) since it was set.
const REFRAME_GROWTH := 1.12
## How far the frame's middle may move off the trunk toward a one-sided crown (m).
const FRAME_SHIFT_MAX := 2.5


## The height to frame in play for a tree this tall (0.8.1): its own height and a little meadow.
## The shears' first view: the camera looks at this share of the tree's height ...
const PRUNE_FOCUS := 0.5
## ... and the tree stands between the HUD (FRAME_TOP) and the hint scrap at the bottom.
const PRUNE_BASE := 0.78


## Half the height of the shears' view (metres at the focus) that holds the whole tree, its top
## below the HUD and its foot above the hint, and the crown's width with a margin at the sides.
static func prune_frame(height: float, width: float, aspect: float) -> float:
	var up := height * (1.0 - PRUNE_FOCUS) / ((0.5 - FRAME_TOP) * 2.0)
	var down := height * PRUNE_FOCUS / ((PRUNE_BASE - 0.5) * 2.0)
	var across := width * 0.5 / (aspect * (1.0 - 2.0 * FRAME_SIDE))
	return maxf(maxf(up, down), across)


static func play_frame_height(tree_height: float) -> float:
	return maxf(tree_height, 0.2) + FRAME_PAD * (1.0 - smoothstep(0.0, FRAME_PAD_UNTIL, tree_height))


## Half the vertical extent to show and the look-at height for a tree `height` tall with a crown
## `width` across, on a screen of this `aspect` (width / height): [half, focus height].
static func play_frame(height: float, width: float, aspect: float) -> Vector2:
	var hf := play_frame_height(height)
	# The tree spans FRAME_BASE - FRAME_TOP of the screen height ...
	var half := hf / (FRAME_BASE - FRAME_TOP) * 0.5
	# ... unless the crown is too broad for the screen's width: then the frame grows to hold it.
	half = maxf(half, width * 0.5 / (aspect * (1.0 - 2.0 * FRAME_SIDE)))
	# The tree's middle sits in the middle of the band between FRAME_TOP and FRAME_BASE (its foot
	# at FRAME_BASE when the height sets the frame; a broad crown sits a little higher).
	var mid := (FRAME_TOP + FRAME_BASE) * 0.5
	return Vector2(half, hf * 0.5 + (mid - 0.5) * 2.0 * half)


## How far the crown reaches out from the trunk (living wood plus the leaf masses at its tips):
## the widest reach in any direction, for noticing a broader crown.
func crown_reach() -> float:
	_update_sectors()
	var r := 0.0
	for v in _sectors:
		r = maxf(r, v)
	return r + HeroCrown.mass_size(state.sim.height()) * 0.5


## The crown's reach to the camera's right and left of the trunk, seen from `yaw` at `distance`
## metres (leaf masses included): [right, left] in metres at the trunk's depth. A branch reaching
## toward the camera looks wider in perspective, and counts so; one reaching away counts less.
func crown_across(yaw: float, distance: float) -> Vector2:
	_update_sectors()
	var right := Vector2(cos(yaw), -sin(yaw))
	var toward := Vector2(sin(yaw), cos(yaw))
	var margin := HeroCrown.mass_size(state.sim.height()) * 0.5
	var out := Vector2.ZERO
	var n := _sectors.size()
	for i in range(n):
		var r := _sectors[i]
		if r <= 0.0:
			continue
		for a in [TAU * i / n, TAU * (i + 1) / n]:
			var p := Vector2(cos(a), sin(a)) * (r + margin)
			var near := distance / maxf(distance - p.dot(toward), distance * 0.3)
			var x := p.dot(right) * near
			out.x = maxf(out.x, x)
			out.y = maxf(out.y, -x)
	return out


## The crown's reach per direction (CROWN_SECTORS around the trunk, on the ground plane),
## measured again only when the tree changed (framing asks every frame).
func _update_sectors() -> void:
	var g := state.sim.graph
	if g.size() == _reach_key:
		return
	_reach_key = g.size()
	_sectors.resize(CROWN_SECTORS)
	_sectors.fill(0.0)
	for id in range(g.size()):
		if g.get_flag(id, "dead", false):
			continue
		var p := Vector2(g.positions[id].x, g.positions[id].z)
		var i := clampi(int(fposmod(p.angle(), TAU) / TAU * CROWN_SECTORS), 0, CROWN_SECTORS - 1)
		_sectors[i] = maxf(_sectors[i], p.length())


const CROWN_SECTORS := 24
var _reach_key: int = -1
var _sectors := PackedFloat32Array()


## About the height this species reaches when finished (the album's last frame).
func album_height() -> float:
	return state.sim.species.max_height * 0.85


## The album's frame (0.8.2.1, phone test: framed for the grown tree from day one, a new sapling
## was a speck and a forest tree behind it read as the old tree). The frame now grows in steps:
## twice the tree's height rounded up to ALBUM_STAGES, at most the grown size. The flip-book keeps
## one frame for days at a time and steps back a few times in a month.
const ALBUM_STAGES: Array[float] = [2.5, 5.0, 10.0, 20.0]


static func album_stage(tree_height: float, grown: float) -> float:
	var want := maxf(tree_height, 0.2) * 2.0
	for s in ALBUM_STAGES:
		if want <= s:
			return minf(s, grown)
	return grown


## Width of the crown (living wood), for framing on a narrow screen.
func crown_width() -> float:
	var g := state.sim.graph
	var r := 0.3
	for id in range(g.size()):
		if not g.get_flag(id, "dead", false):
			r = maxf(r, Vector2(g.positions[id].x, g.positions[id].z).length())
	return r * 2.0


## The album's morning photo: a camera framed for the species' grown size, from the same spot
## every day, so the flip-book shows the tree growing (on) and back to the player's view (off).
## How close above the meadow the dive's camera comes (it never goes under the ground).
const DIVE_FLOOR := 0.35
## The sunrise (main.gd sets `rising`): the camera rises from this high above the meadow (over
## the grass) and at least this far outside the crown's reach (0.8.2.4).
const RISE_FLOOR := 1.3
const RISE_CLEAR := 1.2
var rising: bool = false


func album_pose(on: bool) -> void:
	_album = on
	_frame_camera(true)


## The camera placed at once where play has it (no glide from where it was): after the shed and
## at sunrise, behind the black (0.8.2.2). The light too (0.8.2.4: it eases at sunset and dive).
func snap_camera() -> void:
	_light_snap = true
	if state != null:
		_frame_camera(false, 0.0)


## Where the album's camera stands ([global transform, fov]), worked out without moving the
## player's camera: its pose is taken and everything the framing touched is put back.
func album_camera() -> Array:
	var keep := [camera.transform, camera.fov, _focus, _distance, _framed_height, _framed_width, _framed_reach, _framed_day, dive_amount]
	# (Taken behind the sunrise's black, while the dive's camera is still under the meadow.)
	dive_amount = 0.0
	album_pose(true)
	var out := [camera.global_transform, camera.fov]
	_album = false
	camera.transform = keep[0]
	camera.fov = keep[1]
	_focus = keep[2]
	_distance = keep[3]
	_framed_height = keep[4]
	_framed_width = keep[5]
	_framed_reach = keep[6]
	_framed_day = keep[7]
	dive_amount = keep[8]
	# The grass fade follows the player's camera again (k = 0: nothing moves).
	_frame_camera(false, 0.0)
	return out


## The album's morning photo, drawn off screen by a camera of its own in the same world (0.8.2.2:
## the player's camera jumped to the album pose for a frame, the "wrong camera" on the phone).
## A coroutine; null when nothing could be drawn (headless).
func album_photo() -> Image:
	var pose := album_camera()
	var main_vp := get_viewport()
	var vp := SubViewport.new()
	# The size the screen's picture has (the window's pixels), as the old photo had.
	vp.size = (main_vp as Window).size if main_vp is Window else Vector2i(main_vp.get_visible_rect().size)
	vp.render_target_update_mode = SubViewport.UPDATE_ONCE
	vp.msaa_3d = main_vp.msaa_3d
	vp.screen_space_aa = main_vp.screen_space_aa
	vp.scaling_3d_mode = main_vp.scaling_3d_mode
	vp.scaling_3d_scale = main_vp.scaling_3d_scale
	vp.positional_shadow_atlas_size = main_vp.positional_shadow_atlas_size
	var cam := Camera3D.new()
	cam.environment = camera.environment
	cam.attributes = camera.attributes
	cam.near = camera.near
	cam.far = camera.far
	cam.cull_mask = camera.cull_mask
	cam.keep_aspect = camera.keep_aspect
	cam.fov = pose[1]
	vp.add_child(cam)
	add_child(vp)
	cam.global_transform = pose[0]
	cam.make_current()
	await RenderingServer.frame_post_draw
	var img := vp.get_texture().get_image()
	vp.queue_free()
	return img


func _frame_camera(snap: bool, delta: float = 0.0) -> void:
	var real_height := maxf(state.sim.height(), 0.2)
	# Re-framed at snap, when the tree outgrows the frame, and once the day is over (sunset),
	# so the night's growth shows at dawn as a bigger tree in the same frame.
	var day_over := state.phase != GameState.Phase.DAY and state.day_number() != _framed_day
	# (0.8.1: re-framed sooner, by REFRAME_GROWTH, and for a broader crown too, so the growing
	# crown never pushes past the frame's top or sides.)
	if snap or day_over or real_height > _framed_height * REFRAME_GROWTH or crown_reach() > _framed_reach * REFRAME_GROWTH:
		_framed_height = real_height
		_framed_width = crown_width()
		_framed_reach = crown_reach()
		_framed_day = state.day_number()
	# The frame is sized by the tree's real height (0.6.2): a young tree stands small in its
	# clearing, the forest ring beside it for scale; a grown one fills the frame from further
	# back. The album's morning photo frames the species' final size instead (album_pose).
	var height := frame_height(_framed_height)
	var width := _framed_width
	if _album:
		height = album_stage(real_height, frame_height(album_height()))
		width = height * 0.8
	var want_focus := Vector3(0, clampf(height * 0.47, 0.25, 30.0), 0)
	if _album:
		# A little lower and from a little above, so a seedling's foot is never behind the swell.
		want_focus.y = height * 0.4
	if prune_mode:
		want_focus = Vector3(0, prune_height, 0)
	# The camera stays inside the clearing, which grows with the tree (refresh_clearing), so a
	# grown linden is seen whole from further back rather than through a wide lens.
	var room := maxf(_clearing, Scenery.CLEARING_RADIUS) - 3.0
	# The extent to hold on screen: the height, or the crown's width on a narrow portrait screen.
	var vp := get_viewport().get_visible_rect().size if is_inside_tree() else Vector2(720, 1280)
	var aspect := clampf(vp.x / maxf(vp.y, 1.0), 0.3, 2.0)
	var half := maxf(height * 0.56, width * 0.55 / aspect)
	var fov_cap := 86.0 if _album else 78.0
	var fov_min := FRAME_FOV
	var min_distance := 3.0
	if not _album and not prune_mode:
		# The crown's middle (seen from here) in the middle of the screen: a one-sided crown is
		# framed whole without leaving the other side empty.
		var across := crown_across(_yaw, maxf(_distance, 2.0))
		var pf := play_frame(_framed_height, across.x + across.y, aspect)
		half = pf.x
		# (At most FRAME_SHIFT_MAX off the trunk, so the camera stays inside the clearing.)
		want_focus = Vector3(cos(_yaw), 0.0, -sin(_yaw)) * clampf((across.x - across.y) * 0.5, -FRAME_SHIFT_MAX, FRAME_SHIFT_MAX)
		want_focus.y = pf.y
		min_distance = FRAME_MIN_DISTANCE
		fov_cap = FRAME_MAX_FOV
		fov_min = FRAME_MIN_FOV
		room = maxf(_clearing, Scenery.CLEARING_RADIUS) - FRAME_EDGE - 0.5
	var base_distance := clampf(half / tan(deg_to_rad(FRAME_FOV * 0.5)), min_distance, room)
	var want_distance := base_distance * _zoom
	want_distance = clampf(want_distance, minf(1.5, min_distance), room + 0.5)
	if _album:
		# Always from the same side, as far back as the stage needs (the clearing's edge at most):
		# a sapling's stage is shot from a few metres, not through a long lens from the edge, which
		# filled the photo with the forest behind it.
		want_distance = clampf(half / tan(deg_to_rad(FRAME_FOV * 0.5)), 3.0, room + 0.5)
	var prune_half := 0.0
	if prune_mode:
		# 0.8.2 (look review: the shears' view cut the crown at the top): the shears come out on
		# the whole crown, between the HUD and the hint (prune_frame); a pinch closes in, a drag
		# beside the tree rides the trunk at that distance.
		prune_half = prune_frame(maxf(state.sim.height(), 0.5), crown_width(), aspect)
		want_distance = clampf(prune_half / tan(deg_to_rad(FRAME_FOV * 0.5)) * _zoom, 1.5, room + 0.5)
	# Wide enough to hold the frame at this distance (a lens, not a step back, once the clearing
	# is too small to step back in).
	camera.fov = clampf(rad_to_deg(2.0 * atan(half / maxf(want_distance, 0.1))), fov_min, fov_cap)
	if not _album and not prune_mode:
		# In play the lens is set by the frame, the pinch only moves the camera (a real zoom).
		camera.fov = clampf(rad_to_deg(2.0 * atan(half / maxf(base_distance, 0.1))), fov_min, fov_cap)
	if prune_mode:
		# The lens widens only where the clearing is too small to step back in.
		camera.fov = clampf(rad_to_deg(2.0 * atan(prune_half * _zoom / maxf(want_distance, 0.1))), FRAME_MIN_FOV, FRAME_MAX_FOV)
	# The meadow grass fades out beyond the tree, however far back the camera stands.
	# (A phone lets it fade sooner: meadow cards are what it pays most for.)
	var fade := maxf(14.0 if Budgets.PHONE else 24.0, want_distance + (8.0 if Budgets.PHONE else 22.0))
	for layer in [_grass, _herbs, _meadow2]:
		var gm := (layer as MultiMeshInstance3D).material_override as ShaderMaterial
		gm.set_shader_parameter("fade_start", fade)
		gm.set_shader_parameter("fade_end", fade + 18.0)
	_understory.set_fade(fade, fade + 18.0)
	var k := 1.0 if snap else 1.0 - exp(-2.0 * delta)
	_focus = _focus.lerp(want_focus, k)
	_distance = lerpf(_distance, want_distance, k)
	# A small tree is seen from a little above, so the young plant and the meadow fill the frame
	# rather than the forest wall behind it (review: day 1 showed mostly forest).
	var pitch := maxf(_pitch, lerpf(0.34, 0.02, clampf(height / 8.0, 0.0, 1.0)))
	if _album:
		# A small stage from a little higher: meadow behind the sapling, not the forest wall.
		pitch = lerpf(0.38, 0.2, clampf(height / 10.0, 0.0, 1.0))
	var orbit := _focus + Vector3(sin(_yaw) * cos(pitch), sin(pitch), cos(_yaw) * cos(pitch)) * _distance
	orbit.y = maxf(orbit.y, 0.25)
	# The dive: the camera falls straight down into the ground beside the tree, turning a little
	# and closing in (Simon, play test 4). Sunrise plays the same move backwards, rising out.
	var fall := dive_amount * dive_amount
	var spin := dive_amount * 0.55
	var r := Vector2(orbit.x - _focus.x, orbit.z - _focus.z).length() * lerpf(1.0, 0.6 if not rising else 1.0, dive_amount)
	if rising:
		# 0.8.2.4 (phone: the morning opened inside the grass, then the camera rose through the
		# crown): the sunrise rises outside the crown's reach, from above the grass.
		var clear := minf(crown_reach() + RISE_CLEAR, maxf(_clearing, Scenery.CLEARING_RADIUS) - 1.0)
		r = lerpf(r, maxf(r, clear), dive_amount)
	var a := (ALBUM_YAW if _album else _yaw) + spin
	camera.position = Vector3(_focus.x + sin(a) * r, lerpf(orbit.y, -1.6, fall), _focus.z + cos(a) * r)
	# 0.8.2.2 (phone: one to three flat grey frames on the dive, the ground's underside): the
	# camera stops just above the meadow; the black fade covers the rest of the fall.
	if dive_amount > 0.0:
		var floor_y := RISE_FLOOR if rising else DIVE_FLOOR
		camera.position.y = maxf(camera.position.y, Terrain.height(camera.position.x, camera.position.z) + floor_y)
	camera.fov *= lerpf(1.0, 0.8, dive_amount)
	var look := Vector3(_focus.x, lerpf(_focus.y, -4.0, fall), _focus.z)
	var d := look - camera.position
	if d.length_squared() > 1e-6:
		camera.look_at(look, Vector3.UP if absf(d.normalized().y) < 0.98 else Vector3.FORWARD)
	if look_up != 0.0:
		camera.rotate_object_local(Vector3.RIGHT, look_up)


# --- hold to fast-forward ---------------------------------------------------

## How fast the day's clock runs now: 1, or up to FAST_FORWARD while a still finger is held by
## day. main.gd multiplies the sim's real time by it; the sim itself still steps in fixed game
## time, so a held day grows exactly the tree a watched one does.
func time_speed() -> float:
	var speed := lerpf(1.0, FAST_FORWARD, _ff_amount)
	if _to_sunset and state != null:
		var clk := state.sim.clock
		var left := (clk.daylight_fraction - clk.time_of_day) * clk.seconds_per_day
		speed = minf(speed, maxf(2.0, left / SUNSET_EASE_OUT))
	return speed


func fast_forwarding() -> bool:
	if state == null or state.phase != GameState.Phase.DAY:
		return false
	return _to_sunset or (_drag_mode == "hold" and _pressing)


## The sunset picture may start a run: by day, with the day's end still some way off.
func can_run_to_sunset() -> bool:
	if state == null or state.phase != GameState.Phase.DAY:
		return false
	var clk := state.sim.clock
	return clk.daylight_fraction - clk.time_of_day > SUNSET_RUN_MIN_HOURS * clk.hour_seconds() / clk.seconds_per_day


func running_to_sunset() -> bool:
	return _to_sunset


## Starts (or, while it runs, stops) the run to the sunset. Returns whether it runs now.
func run_to_sunset(on: bool = true) -> bool:
	if on and not can_run_to_sunset():
		on = false
	if on and prune_mode:
		return false
	_to_sunset = on
	if not on:
		_ff_amount = 0.0
	return _to_sunset


func _update_hold(delta: float) -> void:
	# A still finger (no move past the drag threshold, one finger only, not with the shears)
	# that began by day and is still down after HOLD_START becomes the hold.
	if _pressing and _drag_mode == "" and not prune_mode and _touches.size() < 2 			and _press_phase == GameState.Phase.DAY and state.phase == GameState.Phase.DAY 			and _time - _press_time >= HOLD_START:
		_drag_mode = "hold"
	# The sunset run ends at the sunset hold (or when the shears come out).
	if _to_sunset and (state.phase != GameState.Phase.DAY or prune_mode):
		run_to_sunset(false)
	if _moment_left > 0.0:
		# A moment: normal speed for a little while, then the speed eases in again.
		_moment_left = maxf(0.0, _moment_left - delta)
		_ff_amount = 0.0
	elif fast_forwarding():
		_ff_amount = minf(1.0, _ff_amount + delta / FAST_EASE)
	else:
		# Stops on release, at the sunset hold, and when a page or the shed takes the input.
		_ff_amount = 0.0
	if hourglass != null:
		hourglass.visible = fast_forwarding()
		hourglass.hours = state.sim.clock.clock_hour()
	_scenery.cloud_speed = time_speed()


# --- input ------------------------------------------------------------------

## Ends a press whose release may never come: focus lost, the app paused, a cancelled touch
## (0.8.2.1, bug 8: a hold whose release was lost kept the day at 4x until the next click).
## A hold just stops; nothing is boosted.
func cancel_press() -> void:
	if _pressing:
		_end_press(false)
	_touches.clear()
	_ff_amount = 0.0
	_to_sunset = false
	if sun_arc != null:
		sun_arc.cancel_drag()


func _unhandled_input(event: InputEvent) -> void:
	if state == null or not input_enabled:
		return
	if (event is InputEventScreenTouch or event is InputEventMouseButton) and event.get("canceled"):
		cancel_press()
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
		if prune_mode and m.pressed and (m.button_index == MOUSE_BUTTON_WHEEL_UP or m.button_index == MOUSE_BUTTON_WHEEL_DOWN):
			# With the shears: the wheel rides the camera up and down the trunk.
			prune_height = clampf(prune_height + (0.5 if m.button_index == MOUSE_BUTTON_WHEEL_UP else -0.5), 0.3, state.sim.height())
		elif m.button_index == MOUSE_BUTTON_WHEEL_UP and m.pressed:
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
	elif event is InputEventMouseMotion and prune_mode and state.phase == GameState.Phase.DAY:
		# Hovering with the mouse shows where the shears would cut.
		pruning.preview(pruning.pick((event as InputEventMouseMotion).position))


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
	if _to_sunset:
		# A tap while the day runs to the sunset stops it, and only that (no boost, no hold).
		run_to_sunset(false)
		_drag_mode = "stop"
		return
	# (Play test 3: no holding and no dragging the sun; a tap boosts, see _end_press.)
	if prune_mode and state.phase == GameState.Phase.DAY:
		# On a branch: choose where to cut. Beside the tree: move the camera along the trunk.
		var id := pruning.pick(pos)
		_drag_mode = "prune" if id >= 0 else "trunk"
		pruning.preview(id)


func _drag(pos: Vector2, rel: Vector2) -> void:
	if _drag_mode == "hold" or _drag_mode == "stop":
		# One gesture, one meaning: once the day runs fast, the finger does not turn the camera.
		return
	if _drag_mode == "trunk":
		# With the shears out the camera rides the trunk (Simon, 0.5.1): up and down along it,
		# around it sideways, so every branch can be reached.
		_yaw -= rel.x * 0.006
		prune_height = clampf(prune_height + rel.y * 0.02 * maxf(1.0, state.sim.height() / 10.0), 0.3, state.sim.height())
		return
	if _drag_mode == "prune":
		# The finger slides along the tree; the mark follows. Off the tree, nothing is cut.
		pruning.preview(pruning.pick(pos))
		return
	if _drag_mode != "orbit" and _drag_mode != "swipe" and pos.distance_to(_press_pos) > DRAG_THRESHOLD:
		_drag_mode = "orbit"
		state.sim.clock.boost_active = false
		# At sunset a mostly upward drag is the dive swipe, not a turn of the camera (the pitch
		# it left behind made the next mornings look straight down on the tree; 0.6 review).
		# 0.8.2.2 (Simon): the dive is a swipe UP, the way back from the roots a swipe down.
		if state.phase == GameState.Phase.SUNSET and -(pos - _press_pos).y > absf((pos - _press_pos).x):
			_drag_mode = "swipe"
	if _drag_mode == "orbit":
		_yaw -= rel.x * 0.006
		_pitch = clampf(_pitch + rel.y * 0.004, 0.02, 1.25)


func _end_press(is_release: bool, pos: Vector2 = Vector2.ZERO) -> void:
	if not _pressing:
		return
	_pressing = false
	if _drag_mode == "trunk" or _drag_mode == "stop":
		_drag_mode = ""
		return
	if _drag_mode == "hold":
		# A hold never boosts, never ends the day and never dives (also when it ran into sunset).
		_drag_mode = ""
		_ff_amount = 0.0
		return
	if _drag_mode == "prune":
		_drag_mode = ""
		if is_release and state.phase == GameState.Phase.DAY:
			var cut := pruning.cut()
			if cut > 0:
				_rebuild()
				update_visitors()
				# The cut branch goes onto the brush pile at sunrise (0.8).
				state.cut_to_pile(cut)
				pruned.emit(cut)
		else:
			pruning.preview(-1)
		return
	# A short tap by day boosts the sun for one game hour; the clock keeps running.
	if is_release and _drag_mode != "orbit" and state.phase == GameState.Phase.DAY and _press_phase == GameState.Phase.DAY and _time - _press_time < 0.6:
		state.boost_hour()
	# A quick swipe up at sunset dives too (0.8.2.2, Simon: was a swipe down; play test 4).
	if is_release and state.phase == GameState.Phase.SUNSET and _press_phase == GameState.Phase.SUNSET 			and is_swipe(_press_pos, pos, true, _time - _press_time):
		_drag_mode = ""
		ground_tapped.emit()
		return
	# Only a short tap that began at sunset dives (not the end of a boost held through sunset).
	if is_release and _drag_mode != "orbit" and state.phase == GameState.Phase.SUNSET 			and _press_phase == GameState.Phase.SUNSET and _time - _press_time < 0.6:
		if _hits_ground(pos):
			ground_tapped.emit()
	_drag_mode = ""


## A quick, mostly vertical swipe of at least SWIPE_MIN px within SWIPE_TIME s: up (the dive at
## sunset) or down (back from the roots after the night). Shared by both views (0.8.2.2).
const SWIPE_MIN := 160.0
const SWIPE_TIME := 0.9


static func is_swipe(from: Vector2, to: Vector2, up: bool, seconds: float) -> bool:
	var along := (from.y - to.y) if up else (to.y - from.y)
	return along > SWIPE_MIN and absf(to.x - from.x) < along * 0.7 and seconds < SWIPE_TIME


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
