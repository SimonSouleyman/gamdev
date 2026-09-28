class_name NightSky
extends Node3D
## The sky after sunset (design doc section 17, part 2): stars and the moon in its real phase
## (Almanac), with a weak bluish moonlight, so the night view is never black. Cheap on a phone:
## the stars are one mesh of small quads far away (one draw call, no textures), the moon one quad
## drawn by a small shader, and the moonlight casts no shadow there. Both follow the camera, so
## they stay at "infinity"; they draw before the clouds, so clouds pass in front of them.

const RADIUS := 300.0
## The moon stands between these heights: never below the forest's crown line (about 19 degrees
## from the middle of a young clearing), and low enough to be seen above the tree in portrait.
const MOON_LOW := deg_to_rad(21.0)
const MOON_HIGH := deg_to_rad(33.0)
## Angular diameter of the moon's disc (the real one is 0.5 degrees; larger reads better).
const MOON_SIZE_DEG := 2.3
static var STARS: int = 500 if Budgets.PHONE else 1400

var _dome: MeshInstance3D
var _dome_mat: ShaderMaterial
var _stars: MeshInstance3D
var _star_mat: ShaderMaterial
var _moon: MeshInstance3D
var _moon_mat: ShaderMaterial
var moonlight: DirectionalLight3D
var _phase: float = 0.0
var _compat: bool = RenderingServer.get_current_rendering_method() == "gl_compatibility"
var _moon_dir: Vector3 = Vector3.UP


func _init() -> void:
	name = "NightSky"


func _ready() -> void:
	_build_dome()
	_build_stars()
	_build_moon()
	moonlight = DirectionalLight3D.new()
	# Lights the scene only: the physical sky keeps taking its colour from the sun.
	moonlight.sky_mode = DirectionalLight3D.SKY_MODE_LIGHT_ONLY
	moonlight.light_color = Color(0.62, 0.72, 1.0)
	moonlight.light_energy = 0.0
	moonlight.shadow_enabled = false
	moonlight.visible = false
	add_child(moonlight)
	set_phase(Almanac.moon_phase_now())


## Sets the moon's phase (0 new, 0.5 full) and where it stands.
func set_phase(phase: float) -> void:
	_phase = phase
	var a := Almanac.moon_arc_angle(phase)
	if sin(a) <= 0.02:
		# Around new moon it stays below the horizon.
		_moon_dir = Vector3(cos(a), -0.2, sin(a)).normalized()
	else:
		# East (a = 0) through south to west (a = PI), highest in the south.
		var e := lerpf(MOON_LOW, MOON_HIGH, sin(a))
		_moon_dir = Vector3(cos(a) * cos(e), sin(e), sin(a) * cos(e))
	_moon_mat.set_shader_parameter("phase", phase)
	_dome_mat.set_shader_parameter("moon_dir", _moon_dir)
	_dome_mat.set_shader_parameter("moon_glow", Almanac.moon_lit_fraction(phase) if moon_up() else 0.0)


func moon_direction() -> Vector3:
	return _moon_dir


func moon_up() -> bool:
	return _moon_dir.y > 0.02


## `night`: 0 by day, 1 in the full night. `veil`: 0..1 how much of the sky clouds and mist hide.
## The stars and the moon follow `eye` (the camera), so they never come closer.
func update(eye: Vector3, night: float, veil: float = 0.0) -> void:
	var show := night > 0.01
	_stars.visible = show
	_dome.visible = show
	_dome.global_position = eye
	# The phone's renderer shows the night blue darker.
	_dome_mat.set_shader_parameter("night", night * (1.6 if _compat else 1.0))
	_moon.visible = show and moon_up()
	moonlight.visible = show
	if not show:
		return
	_stars.global_position = eye
	_star_mat.set_shader_parameter("night", night * (1.0 - veil * 0.85))
	_moon.global_position = eye + _moon_dir * RADIUS
	_moon_mat.set_shader_parameter("night", night * (1.0 - veil * 0.6))
	# Moonlight grows with the lit disc; without a moon, a faint light from the starry sky above.
	var lit := Almanac.moon_lit_fraction(_phase) if moon_up() else 0.0
	var from := _moon_dir if moon_up() else Vector3(0.2, 1.0, 0.3).normalized()
	moonlight.look_at_from_position(from * 20.0, Vector3.ZERO, Vector3.UP if absf(from.y) < 0.99 else Vector3.FORWARD)
	moonlight.light_energy = night * (0.14 + 0.3 * lit) * (1.0 - veil * 0.5)
	# Soft moon shadows on a PC under a bright moon; a phone saves the second shadow map.
	moonlight.shadow_enabled = not Budgets.PHONE and lit > 0.3 and night > 0.5
	moonlight.shadow_opacity = 0.6
	moonlight.shadow_blur = 3.0


# --- the night blue -------------------------------------------------------------

const DOME_SHADER := """
shader_type spatial;
// The night's own blue, added over the physical sky (which goes black once the sun is down):
// lighter at the horizon, deep at the zenith, and a soft brightening around a bright moon.
render_mode unshaded, blend_add, depth_draw_never, cull_front, fog_disabled, shadows_disabled;
uniform float night = 0.0;
uniform vec3 moon_dir = vec3(0.0, 0.5, 0.8);
uniform float moon_glow = 0.0;
uniform vec3 horizon : source_color = vec3(0.06, 0.08, 0.14);
uniform vec3 zenith : source_color = vec3(0.012, 0.02, 0.055);
varying vec3 dir;
void vertex() {
	dir = normalize(VERTEX);
}
void fragment() {
	vec3 d = normalize(dir);
	vec3 col = mix(horizon, zenith, pow(clamp(d.y, 0.0, 1.0), 0.6));
	col += vec3(0.05, 0.065, 0.1) * pow(max(dot(d, normalize(moon_dir)), 0.0), 12.0) * moon_glow;
	// A little noise against banding in the dark gradient.
	float grain = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
	ALBEDO = (col + grain * 0.004) * night;
}
"""


func _build_dome() -> void:
	_dome = MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = RADIUS + 20.0
	sphere.height = (RADIUS + 20.0) * 2.0
	sphere.radial_segments = 24
	sphere.rings = 12
	_dome.mesh = sphere
	_dome_mat = ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = DOME_SHADER
	_dome_mat.shader = sh
	# First of all see-through things: behind the stars, the moon and the clouds.
	_dome_mat.render_priority = -110
	_dome.material_override = _dome_mat
	_dome.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_dome.visible = false
	add_child(_dome)


# --- the stars ---------------------------------------------------------------

const STAR_SHADER := """
shader_type spatial;
// Stars: one quad per star, grown to its size in view space so it always faces the camera.
// Soft round points, a slow twinkle, fading toward the horizon where the air is thick.
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled, skip_vertex_transform;
uniform float night = 0.0;
varying vec2 corner;
varying float elevation;
void vertex() {
	elevation = normalize(VERTEX).y;
	corner = UV;
	VERTEX = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz + vec3(UV * UV2.x, 0.0);
}
void fragment() {
	float r2 = dot(corner, corner);
	float twinkle = 0.78 + 0.22 * sin(TIME * (1.3 + COLOR.a * 3.0) + COLOR.a * 60.0);
	float horizon = smoothstep(0.03, 0.3, elevation);
	ALBEDO = COLOR.rgb;
	ALPHA = exp(-r2 * 5.0) * twinkle * horizon * night;
}
"""


func _build_stars() -> void:
	var rng := RandomNumberGenerator.new()
	# The same sky over every clearing (the stars do not depend on the save).
	rng.seed = hash("tree night sky")
	var verts := PackedVector3Array()
	var uvs := PackedVector2Array()
	var sizes := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	# A band of denser stars across the sky, like the Milky Way.
	var band := Vector3(0.35, 0.25, 1.0).normalized()
	var corners := [Vector2(-1, -1), Vector2(1, -1), Vector2(1, 1), Vector2(-1, 1)]
	var n := 0
	while n < STARS:
		var d := Vector3(rng.randf_range(-1, 1), rng.randf_range(0.0, 1.0), rng.randf_range(-1, 1))
		if d.length() > 1.0 or d.length() < 0.1:
			continue
		d = d.normalized()
		if d.y < 0.03:
			continue
		# Outside the band only every second candidate is kept.
		if absf(d.dot(band)) > 0.18 and rng.randf() < 0.5:
			continue
		var mag := pow(rng.randf(), 3.5)
		var size := lerpf(0.4, 1.25, mag) * RADIUS / 300.0
		var tint := Color(0.78, 0.86, 1.0).lerp(Color(1.0, 0.9, 0.75), rng.randf())
		var bright := lerpf(0.3, 2.6, mag)
		var base := verts.size()
		for c in corners:
			verts.append(d * RADIUS)
			uvs.append(c)
			sizes.append(Vector2(size, 0.0))
			colors.append(Color(tint.r * bright, tint.g * bright, tint.b * bright, rng.randf()))
		indices.append_array([base, base + 1, base + 2, base, base + 2, base + 3])
		n += 1
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = sizes
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	_stars = MeshInstance3D.new()
	_stars.mesh = mesh
	_star_mat = ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = STAR_SHADER
	_star_mat.shader = sh
	# Before every other see-through thing, so clouds pass in front of the stars.
	_star_mat.render_priority = -100
	_stars.material_override = _star_mat
	_stars.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_stars.custom_aabb = AABB(Vector3(-RADIUS, -10.0, -RADIUS), Vector3(RADIUS * 2.0, RADIUS + 10.0, RADIUS * 2.0))
	_stars.visible = false
	add_child(_stars)


# --- the moon --------------------------------------------------------------------

const MOON_SHADER := """
shader_type spatial;
// The moon: a lit sphere drawn on a camera-facing quad. The phase turns the light around it:
// 0.25 lights the right half (waxing), 0.5 the whole face, 0.75 the left half. Grey maria from a
// noise texture, a faint earthshine on the dark part and a soft halo around the lit disc.
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
uniform float phase = 0.5;
uniform float night = 0.0;
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
const float DISC = 0.42;
void vertex() {
	// Billboard, keeping the node's scale.
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
}
void fragment() {
	vec2 q = (UV * 2.0 - 1.0) * vec2(1.0, -1.0) / DISC;
	float r = length(q);
	float lit_share = 0.5 - 0.5 * cos(6.2831853 * phase);
	float a = 6.2831853 * phase;
	vec3 sun = vec3(sin(a), 0.0, -cos(a));
	vec3 col = vec3(0.0);
	float alpha = 0.0;
	if (r < 1.0) {
		vec3 n = vec3(q, sqrt(1.0 - r * r));
		float lit = smoothstep(-0.04, 0.08, dot(n, sun));
		float maria = texture(noise, q * 0.32 + vec2(0.41, 0.63)).r;
		float fine = texture(noise, q * 1.4 + vec2(0.1, 0.2)).r;
		float albedo = mix(0.58, 1.0, smoothstep(0.38, 0.62, maria)) * (0.85 + 0.3 * fine);
		albedo *= mix(0.78, 1.0, n.z);
		col = vec3(1.0, 0.97, 0.9) * albedo * 1.05 * lit + vec3(0.1, 0.12, 0.16) * (1.0 - lit);
		alpha = smoothstep(1.0, 0.96, r) * max(lit, 0.18);
	} else {
		// The halo: a bright moon lights the thin haze around it.
		float glow = exp(-(r - 1.0) * 3.2) * 0.18 * lit_share;
		col = vec3(0.75, 0.82, 1.0);
		alpha = glow;
	}
	ALBEDO = col;
	ALPHA = alpha * night;
}
"""


func _build_moon() -> void:
	_moon = MeshInstance3D.new()
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	_moon.mesh = quad
	_moon_mat = ShaderMaterial.new()
	var sh := Shader.new()
	sh.code = MOON_SHADER
	_moon_mat.shader = sh
	_moon_mat.render_priority = -99
	var tex := NoiseTexture2D.new()
	tex.width = 256
	tex.height = 256
	tex.seamless = true
	tex.generate_mipmaps = true
	var fnl := FastNoiseLite.new()
	fnl.seed = 11
	fnl.frequency = 0.02
	fnl.fractal_octaves = 4
	tex.noise = fnl
	_moon_mat.set_shader_parameter("noise", tex)
	_moon.material_override = _moon_mat
	_moon.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The disc takes DISC (0.42) of the quad's half width.
	_moon.scale = Vector3.ONE * RADIUS * tan(deg_to_rad(MOON_SIZE_DEG * 0.5)) * 2.0 / 0.42
	_moon.extra_cull_margin = 50.0
	_moon.visible = false
	add_child(_moon)
