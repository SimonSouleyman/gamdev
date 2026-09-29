class_name WeatherFx
extends Node3D
## The day's weather and the season you can see move (design doc section 17, parts 4 and 5):
## rain streaks around the camera during a shower, leaves drifting down from the crown in autumn,
## and the moments distant thunder rolls. Mood only, seeded like the rest (Almanac). A phone
## draws fewer drops and leaves.

## Distant thunder rolls now (the ambience plays it; sound only).
signal thunder

static var RAIN_DROPS: int = 450 if Budgets.PHONE else 1800
static var FALLING_LEAVES: int = 36 if Budgets.PHONE else 120

var _rain: GPUParticles3D
var _leaves: GPUParticles3D
var _last_t: float = -1.0
var _leaf_top: float = -1.0


func _ready() -> void:
	_build_rain()
	_build_leaves()


## `t`: point of the daylight (0 sunrise, 1 sunset; -1 while the sun is down).
## `rain`: shower strength 0..1. `fall`: how many leaves drift down (Almanac.season_look).
func update(eye: Vector3, forward: Vector3, t: float, w: Dictionary, rain: float, fall: float) -> void:
	_start(_rain, rain > 0.02)
	_rain.amount_ratio = clampf(rain, 0.05, 1.0)
	var ahead := Vector3(forward.x, 0.0, forward.z).normalized() * 6.0 if Vector2(forward.x, forward.z).length() > 0.01 else Vector3.ZERO
	_rain.global_position = eye + ahead + Vector3(0, 7.0, 0)
	_start(_leaves, fall > 0.02)
	_leaves.amount_ratio = clampf(fall, 0.05, 1.0)
	# Thunder rolls at its two moments of the afternoon, once each.
	if t >= 0.0 and w.get("thunder", false):
		for at in w.get("thunder_at", []):
			if _last_t >= 0.0 and _last_t < float(at) and t >= float(at):
				thunder.emit()
	_last_t = t


## Switches a particle system on already filled (its preprocess), or off.
func _start(p: GPUParticles3D, on: bool) -> void:
	if on and not p.emitting:
		p.restart()
	p.emitting = on


## Leaves fall from the crown's bounds (TreeView, after each rebuild).
func set_crown(crown: AABB) -> void:
	var top := crown.end.y
	if crown.size.y < 0.3 or top < 1.0:
		_leaves.visible = false
		return
	_leaves.visible = true
	_leaves.position = crown.get_center()
	var pm := _leaves.process_material as ParticleProcessMaterial
	pm.emission_box_extents = crown.size * 0.4
	# Long enough to drift all the way to the grass; only reset when the crown grew a lot.
	if absf(top - _leaf_top) > 1.5:
		_leaf_top = top
		_leaves.lifetime = clampf(top / 0.6, 4.0, 30.0)
		_leaves.preprocess = _leaves.lifetime
		_leaves.visibility_aabb = AABB(-crown.size * 0.5 - Vector3(4, top + 2.0, 4), crown.size + Vector3(8, top + 4.0, 8))


func _build_rain() -> void:
	_rain = GPUParticles3D.new()
	_rain.amount = RAIN_DROPS
	_rain.lifetime = 1.3
	_rain.preprocess = 1.3
	_rain.local_coords = false
	_rain.emitting = false
	_rain.visibility_aabb = AABB(Vector3(-16, -16, -16), Vector3(32, 24, 32))
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = Vector3(11, 0.5, 11)
	pm.direction = Vector3(0.12, -1, 0.04)
	pm.spread = 2.0
	pm.initial_velocity_min = 8.5
	pm.initial_velocity_max = 10.5
	pm.gravity = Vector3(0, -2.0, 0)
	_rain.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2(0.012, 0.42)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	# Upright streaks that turn to the camera around their long axis.
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_FIXED_Y
	mat.billboard_keep_scale = true
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color = Color(0.78, 0.82, 0.9, 0.22)
	# Drops right at the lens would be long bright poles: fade them in from 2 to 5 m.
	mat.distance_fade_mode = BaseMaterial3D.DISTANCE_FADE_PIXEL_ALPHA
	mat.distance_fade_min_distance = 2.0
	mat.distance_fade_max_distance = 5.0
	var fade := GradientTexture2D.new()
	fade.fill_from = Vector2(0.5, 0.0)
	fade.fill_to = Vector2(0.5, 1.0)
	var g := Gradient.new()
	g.set_color(0, Color(1, 1, 1, 0))
	g.set_color(1, Color(1, 1, 1, 0))
	g.add_point(0.5, Color(1, 1, 1, 1))
	fade.gradient = g
	fade.width = 4
	fade.height = 32
	mat.albedo_texture = fade
	quad.material = mat
	_rain.draw_pass_1 = quad
	_rain.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_rain)


func _build_leaves() -> void:
	_leaves = GPUParticles3D.new()
	_leaves.amount = FALLING_LEAVES
	_leaves.lifetime = 10.0
	_leaves.preprocess = 10.0
	_leaves.local_coords = false
	_leaves.emitting = false
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = Vector3(2, 2, 2)
	pm.direction = Vector3(0.3, -1.0, 0.15)
	pm.spread = 30.0
	pm.initial_velocity_min = 0.5
	pm.initial_velocity_max = 0.9
	# Light leaves: the air holds them against gravity (damping as strong as gravity), so they keep
	# sailing down at the speed they left the twig with, spinning. (Turbulence would stop them.)
	pm.gravity = Vector3(0.1, -1.0, 0.04)
	pm.damping_min = 1.0
	pm.damping_max = 1.0
	pm.angle_min = 0.0
	pm.angle_max = 360.0
	pm.angular_velocity_min = -160.0
	pm.angular_velocity_max = 160.0
	pm.scale_min = 0.8
	pm.scale_max = 1.3
	var colours := Gradient.new()
	colours.set_color(0, Color(0.95, 0.78, 0.25))
	colours.set_color(1, Color(0.62, 0.36, 0.16))
	colours.add_point(0.35, Color(0.96, 0.58, 0.16))
	colours.add_point(0.7, Color(0.78, 0.26, 0.12))
	var ramp := GradientTexture1D.new()
	ramp.gradient = colours
	pm.color_initial_ramp = ramp
	_leaves.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2(0.13, 0.13)
	var mat := StandardMaterial3D.new()
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
	mat.alpha_scissor_threshold = 0.5
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.vertex_color_use_as_albedo = true
	mat.albedo_texture = _leaf_texture()
	mat.roughness = 0.8
	quad.material = mat
	_leaves.draw_pass_1 = quad
	_leaves.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_leaves)


## A single leaf: an oval with a pointed tip and a pale midrib, white so the particle colours it.
func _leaf_texture() -> Texture2D:
	var n := 32
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	for y in range(n):
		for x in range(n):
			var p := Vector2(x + 0.5, y + 0.5) / n * 2.0 - Vector2.ONE
			# Along the leaf (y) it narrows to a tip at the top and a stalk at the bottom.
			var along := (p.y + 1.0) * 0.5
			var half := 0.55 * sin(PI * pow(along, 0.8))
			var inside := absf(p.x) < half and p.y > -0.9 and p.y < 0.95
			var stalk := absf(p.x) < 0.05 and p.y >= 0.85
			var shade := 0.85 + 0.15 * (1.0 - absf(p.x) / maxf(half, 0.01))
			if absf(p.x) < 0.04:
				shade = 1.1
			# Coloured in the texture itself: some renderers drop the particle colour, and white
			# ovals drifted down (0.6 review). The particle colour still varies it.
			var c := Color(0.92, 0.62, 0.22).lerp(Color(0.7, 0.3, 0.12), clampf(p.x * 0.5 + 0.5, 0.0, 1.0)) * shade
			img.set_pixel(x, y, Color(c.r, c.g, c.b, 1.0 if inside or stalk else 0.0))
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)
