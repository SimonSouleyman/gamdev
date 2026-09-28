class_name Meadow
extends Node3D
## Read the meadow (design doc section 3): what grows on the surface hints at what lies below.
## Rushes and a damp patch over water, clover or nettles over nitrogen, stones over shallow rock,
## moss on the north side of the trunk. Grey-box plants, placed from Underground.surface_hints().

var _rng := RandomNumberGenerator.new()
var _mats: Dictionary = {}


func build(ground: Underground) -> void:
	for c in get_children():
		c.queue_free()
	_rng.seed = hash([ground.seed, "meadow"])
	_mats = {
		"rush": _mat(Color(0.3, 0.45, 0.32)),
		"damp": _mat(Color(0.13, 0.19, 0.09)),
		"clover": _mat(Color(0.3, 0.62, 0.25)),
		"flower": _mat(Color(0.95, 0.93, 0.9)),
		"nettle": _mat(Color(0.18, 0.36, 0.16)),
		"stone": _mat(Color(0.55, 0.54, 0.5)),
		"moss": _mat(Color(0.28, 0.5, 0.15)),
	}
	for h in ground.surface_hints():
		var p: Vector3 = Terrain.at(h["position"])
		var r: float = h["radius"]
		match str(h["kind"]):
			"damp":
				_wet_patch(p, r * 1.2, Color(0.16, 0.22, 0.09), 0.9)
			"rushes":
				_scatter(p, r, 18, func(q: Vector3) -> void: _blade(q, _rng.randf_range(0.45, 0.8), 0.012, _mats["rush"]))
			"clover":
				_scatter(p, r, 26, _clover)
			"nettles":
				_scatter(p, r, 14, func(q: Vector3) -> void: _cone(q, _rng.randf_range(0.3, 0.5), 0.07, _mats["nettle"]))
			"stones":
				_scatter(p, r, 7, _stone)
			"moss":
				# A small bare patch of earth around the trunk; nothing else grows there (Simon).
				_wet_patch(Terrain.at(Vector3.ZERO), BARE_RADIUS, Color(0.2, 0.15, 0.1), 0.95)


const WET_SHADER := """
shader_type spatial;
render_mode depth_draw_opaque;
uniform vec3 color = vec3(0.12, 0.17, 0.1);
uniform float rough = 0.25;
void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float edge = 1.0 - smoothstep(0.45, 1.0, length(c) + 0.08 * sin(atan(c.y, c.x) * 5.0));
	ALBEDO = color;
	ROUGHNESS = rough;
	ALPHA = edge * 0.75;
}
"""


## A damp patch: darker, glossy ground with a soft ragged edge, lying just on the grass floor.
func _wet_patch(at: Vector3, radius: float, color: Color = Color(0.12, 0.17, 0.1), roughness: float = 0.25) -> void:
	var plane := PlaneMesh.new()
	plane.size = Vector2.ONE * radius * 2.0
	var sh := Shader.new()
	sh.code = WET_SHADER
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("color", color)
	mat.set_shader_parameter("rough", roughness)
	var m := _add(plane, mat, at + Vector3(0, 0.01, 0), Basis(Vector3.UP, _rng.randf() * TAU))
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


func _mat(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = 0.95
	return m


## Radius of the bare earth around the trunk.
const BARE_RADIUS := 0.9


func _scatter(center: Vector3, radius: float, count: int, make: Callable) -> void:
	for _i in range(count):
		var a := _rng.randf() * TAU
		var d := radius * sqrt(_rng.randf())
		var q := center + Vector3(cos(a) * d, 0.0, sin(a) * d)
		if Vector2(q.x, q.z).length() < BARE_RADIUS + 0.1:
			continue
		make.call(Terrain.at(q))


func _add(mesh: Mesh, mat: Material, at: Vector3, basis: Basis = Basis.IDENTITY) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	m.mesh = mesh
	m.material_override = mat
	m.transform = Transform3D(basis, at)
	add_child(m)
	return m


func _disc(at: Vector3, radius: float, mat: Material, height: float = 0.01) -> void:
	var c := CylinderMesh.new()
	c.top_radius = radius
	c.bottom_radius = radius
	c.height = height
	c.radial_segments = 20
	_add(c, mat, at + Vector3(0, height * 0.5 + 0.003, 0))


func _blade(at: Vector3, height: float, radius: float, mat: Material) -> void:
	var c := CylinderMesh.new()
	c.top_radius = radius * 0.3
	c.bottom_radius = radius
	c.height = height
	c.radial_segments = 4
	c.rings = 1
	var tilt := Basis(Vector3(_rng.randf_range(-1, 1), 0, _rng.randf_range(-1, 1)).normalized(), _rng.randf_range(0.0, 0.25))
	_add(c, mat, at + tilt * Vector3(0, height * 0.5, 0), tilt)


func _cone(at: Vector3, height: float, radius: float, mat: Material) -> void:
	var c := CylinderMesh.new()
	c.top_radius = 0.0
	c.bottom_radius = radius
	c.height = height
	c.radial_segments = 6
	c.rings = 1
	_add(c, mat, at + Vector3(0, height * 0.5, 0))


func _clover(at: Vector3) -> void:
	var s := SphereMesh.new()
	s.radius = 0.06
	s.height = 0.03
	s.radial_segments = 8
	s.rings = 3
	_add(s, _mats["clover"], at + Vector3(0, 0.02, 0))
	if _rng.randf() < 0.35:
		var f := SphereMesh.new()
		f.radius = 0.025
		f.height = 0.05
		f.radial_segments = 6
		f.rings = 3
		_add(f, _mats["flower"], at + Vector3(0.02, 0.07, 0))


func _stone(at: Vector3) -> void:
	var s := SphereMesh.new()
	var r := _rng.randf_range(0.05, 0.14)
	s.radius = r
	s.height = r * 1.2
	s.radial_segments = 7
	s.rings = 4
	_add(s, _mats["stone"], at + Vector3(0, r * 0.25, 0), Basis(Vector3.UP, _rng.randf() * TAU))
