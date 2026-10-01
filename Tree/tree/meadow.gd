class_name Meadow
extends Node3D
## Read the meadow (design doc section 3): what grows on the surface hints at what lies below.
## Rushes and a damp patch over water, clover over nitrogen, nettles over phosphorus (0.8; clover
## or nettles over nitrogen in an older soil), comfrey over a potassium wish, stones over shallow
## rock, moss on the north side of the trunk. Placed from Underground.surface_hints(). The nettles
## and the comfrey are real plant shapes (0.8 review: the grey-box cones were lost at phone size),
## one merged mesh per patch.

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
				_patch_mesh(p, r, "nettles")
			"comfrey":
				_patch_mesh(p, r, "comfrey")
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
const BARE_RADIUS := Clearing.BARE_RADIUS


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
	# Hundreds of tiny hint meshes: on the phone each would cost a shadow draw call (0.6 QA).
	if Budgets.PHONE:
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
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


## Nettle and comfrey stands (0.8 review): readable at phone size in a summer look-down, each
## patch one mesh with one material. Nettles: a dense stand of upright stems 0.6 to 1.1 m tall
## (nettles grow in thick colonies), pairs of pointed, drooping, dark blue-green leaves crossing
## up the stem, hanging green-brown flower tassels; they read as a dark, spiky, taller block in the
## meadow. Comfrey: a few broad clumps about 0.7 m high, big rough mid-green leaves arching out of
## the base and leafy stems topped with curled sprays of hanging violet bells; they read as broad
## mounds with violet dots. Real colours of the plants, a little deeper than the grass around.
const NETTLE_STEMS: int = 40
const COMFREY_CLUMPS: int = 8
static var _plant_mat: StandardMaterial3D


func _patch_mesh(center: Vector3, radius: float, kind: String) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var n := NETTLE_STEMS if kind == "nettles" else COMFREY_CLUMPS
	var spread := radius * (0.8 if kind == "nettles" else 0.7)
	for _i in range(n):
		var a := _rng.randf() * TAU
		# Nettles crowd toward the middle of their colony.
		var d := spread * pow(_rng.randf(), 0.7 if kind == "nettles" else 0.5)
		var q := center + Vector3(cos(a) * d, 0.0, sin(a) * d)
		if Vector2(q.x, q.z).length() < BARE_RADIUS + 0.2:
			continue
		var at := Terrain.at(q)
		if kind == "nettles":
			_nettle(st, at)
		else:
			_comfrey(st, at)
	st.generate_normals()
	var m := _add(st.commit(), _plant_material(), Vector3.ZERO)
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


static func _plant_material() -> StandardMaterial3D:
	if _plant_mat == null:
		_plant_mat = StandardMaterial3D.new()
		_plant_mat.vertex_color_use_as_albedo = true
		_plant_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
		_plant_mat.roughness = 0.85
		# Leaves let a little light through (seen against the sun they do not go black).
		_plant_mat.backlight_enabled = true
		_plant_mat.backlight = Color(0.25, 0.3, 0.12)
	return _plant_mat


## One nettle stem: pairs of leaves crossing up the stem, smaller toward the top.
func _nettle(st: SurfaceTool, at: Vector3) -> void:
	var h := _rng.randf_range(0.6, 1.1)
	var lean := Vector3(_rng.randf_range(-0.08, 0.08), 1.0, _rng.randf_range(-0.08, 0.08)).normalized()
	var dark := Color(0.1, 0.2, 0.1) * _rng.randf_range(0.85, 1.1)
	var leaf_col := Color(0.15, 0.28, 0.09) * _rng.randf_range(0.85, 1.15)
	_strip(st, at, at + lean * h, 0.008, dark)
	var turn := _rng.randf() * PI
	var pairs := 6
	for k in range(pairs):
		var t := 0.22 + 0.74 * float(k) / (pairs - 1)
		var node := at + lean * h * t
		var yaw := turn + k * PI * 0.5
		var size := lerpf(0.16, 0.06, t)
		for side in [-1.0, 1.0]:
			var out := Vector3(cos(yaw) * side, 0.0, sin(yaw) * side)
			# Drooping: the leaf goes out and down; upper leaves stand up more.
			var dir := (out + Vector3(0, lerpf(-0.55, 0.35, t), 0)).normalized()
			_leaf(st, node, dir, size, size * 0.42, leaf_col.lightened(0.12 * t))
		# Hanging flower tassels from the upper leaf joints.
		if t > 0.45 and _rng.randf() < 0.6:
			var hang := Vector3(cos(yaw + 0.8), -0.9, sin(yaw + 0.8)).normalized()
			_strip(st, node, node + hang * 0.09, 0.006, Color(0.32, 0.33, 0.16))
	# The top: a small tuft of young leaves, a little lighter.
	_leaf(st, at + lean * h, (lean + Vector3(0.3, 0, 0)).normalized(), 0.04, 0.02, leaf_col.lightened(0.2))


## One comfrey clump: broad leaves arching out of the base, two or three leafy flowering stems.
func _comfrey(st: SurfaceTool, at: Vector3) -> void:
	var leaf_col := Color(0.2, 0.34, 0.13) * _rng.randf_range(0.9, 1.1)
	var base_leaves := _rng.randi_range(7, 10)
	var turn := _rng.randf() * TAU
	for k in range(base_leaves):
		var yaw := turn + TAU * k / base_leaves + _rng.randf_range(-0.2, 0.2)
		var out := Vector3(cos(yaw), 0.0, sin(yaw))
		var length := _rng.randf_range(0.34, 0.48)
		# Arching: up and out, then the tip bends over.
		var mid := at + out * length * 0.45 + Vector3(0, length * 0.55, 0)
		_leaf(st, at + Vector3(0, 0.02, 0), (mid - at).normalized(), length * 0.55, length * 0.2, leaf_col)
		_leaf(st, mid, (out + Vector3(0, -0.35, 0)).normalized(), length * 0.55, length * 0.2, leaf_col.lightened(0.06))
	for _s in range(_rng.randi_range(2, 3)):
		var lean := Vector3(_rng.randf_range(-0.25, 0.25), 1.0, _rng.randf_range(-0.25, 0.25)).normalized()
		var h := _rng.randf_range(0.55, 0.8)
		var top := at + lean * h
		_strip(st, at, top, 0.012, leaf_col.darkened(0.2))
		for k in range(3):
			var node := at + lean * h * (0.35 + 0.2 * k)
			var yaw := _rng.randf() * TAU
			_leaf(st, node, Vector3(cos(yaw), 0.15, sin(yaw)).normalized(), 0.13 - 0.03 * k, 0.05, leaf_col.lightened(0.05))
		# The curled spray of hanging bells: violet (the common purple comfrey), a few paler.
		var curl := Vector3(_rng.randf_range(-1, 1), 0.0, _rng.randf_range(-1, 1)).normalized()
		for b in range(8):
			var p := top + curl * 0.025 * b + Vector3(0, 0.01 - 0.003 * b * b, 0)
			var bell := Color(0.46, 0.2, 0.5).lerp(Color(0.72, 0.55, 0.78), _rng.randf() * 0.4)
			_bell(st, p, 0.03, bell)


## A leaf as a flat pointed oval from `base` along `dir`, `length` long and `width` wide, with a
## slight fold along its midrib.
func _leaf(st: SurfaceTool, base: Vector3, dir: Vector3, length: float, width: float, col: Color) -> void:
	var side := dir.cross(Vector3.UP)
	if side.length() < 0.1:
		side = dir.cross(Vector3.RIGHT)
	side = side.normalized()
	var up := side.cross(dir).normalized()
	var spine: Array[Vector3] = [base]
	var left: Array[Vector3] = []
	var right: Array[Vector3] = []
	for k in range(1, 5):
		var t := float(k) / 5.0
		var w := sin(t * PI) * width * (1.15 - t * 0.4)
		var c := base + dir * length * t
		spine.append(c)
		left.append(c + side * w + up * w * 0.25)
		right.append(c - side * w + up * w * 0.25)
	var tip := base + dir * length
	var edge_col := col.darkened(0.08)
	for edge: Array[Vector3] in [left, right]:
		for k in range(4):
			var e0: Vector3 = spine[0] if k == 0 else edge[k - 1]
			_tri(st, spine[k], e0, edge[k], col, edge_col, edge_col)
			_tri(st, spine[k], edge[k], spine[k + 1], col, edge_col, col)
		_tri(st, spine[4], edge[3], tip, col, edge_col, col)


func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, ca: Color, cb: Color, cc: Color) -> void:
	st.set_color(ca)
	st.add_vertex(a)
	st.set_color(cb)
	st.add_vertex(b)
	st.set_color(cc)
	st.add_vertex(c)


## A thin stem as two crossed strips.
func _strip(st: SurfaceTool, a: Vector3, b: Vector3, width: float, col: Color) -> void:
	for side in [Vector3(width, 0, 0), Vector3(0, 0, width)]:
		_tri(st, a - side, a + side, b + side * 0.5, col, col, col)
		_tri(st, a - side, b + side * 0.5, b - side * 0.5, col, col, col)


## A hanging bell flower: a small five-sided tube, open end down.
func _bell(st: SurfaceTool, top: Vector3, size: float, col: Color) -> void:
	var sides := 5
	var bottom := top + Vector3(0, -size * 1.3, 0)
	for k in range(sides):
		var a0 := TAU * k / sides
		var a1 := TAU * (k + 1) / sides
		var t0 := top + Vector3(cos(a0), 0, sin(a0)) * size * 0.35
		var t1 := top + Vector3(cos(a1), 0, sin(a1)) * size * 0.35
		var b0 := bottom + Vector3(cos(a0), 0, sin(a0)) * size * 0.6
		var b1 := bottom + Vector3(cos(a1), 0, sin(a1)) * size * 0.6
		_tri(st, t0, b0, b1, col, col.lightened(0.1), col.lightened(0.1))
		_tri(st, t0, b1, t1, col, col.lightened(0.1), col)


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
