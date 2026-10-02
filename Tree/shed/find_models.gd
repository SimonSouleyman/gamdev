class_name FindModels
extends RefCounted
## The finds as small real things for the bench's drawers (0.8.2.6, specs/journal-drawers-loop.md
## D1): a fossil shell in a stone, an old root, a green coin, a stone with a water vein, a scrap of
## an old map, a shard of a clay drain pipe. Built here from a few hundred triangles each, one
## plain material per part (vertex colours where a part has two), so they cost next to nothing on
## the Compatibility renderer. Sizes are true to life, a little generous. Origin: where it rests
## on the drawer's floor; +x across, +z toward the room.

## How big each lies (its longest side, metres) and its tap radius.
const SIZE := {"fossil": 0.085, "old_root": 0.14, "coin": 0.035, "water_vein": 0.075, "map_scrap": 0.11, "shard": 0.095}

static var _cache: Dictionary = {}


## The thing of `kind`; `variant` turns and shades a second or third copy a little differently.
static func build(kind: String, variant: int = 0) -> Node3D:
	var n := Node3D.new()
	n.name = kind
	match kind:
		"fossil":
			_add(n, _stone_mesh(Vector3(0.085, 0.032, 0.066), Color(0.44, 0.41, 0.36), Color(0.34, 0.31, 0.27), -1.0, 11 + variant), _mat_vc(0.95))
			var shell := _add(n, _shell_mesh(), _mat(Color(0.62, 0.56, 0.45), 0.9))
			shell.position = Vector3(0.004, 0.026, 0.0)
			shell.rotation.y = 0.4
		"old_root":
			_add(n, _root_mesh(variant), _mat_vc(0.95))
		"coin":
			var c := CylinderMesh.new()
			c.top_radius = 0.0175
			c.bottom_radius = 0.0175
			c.height = 0.0028
			c.radial_segments = 20
			c.rings = 1
			var coin := _add(n, c, _mat(Color(0.36, 0.52, 0.42), 0.55, 0.55))
			coin.position.y = 0.0016
			# A raised rim and a worn head in the middle, a shade lighter.
			var t := TorusMesh.new()
			t.inner_radius = 0.015
			t.outer_radius = 0.0175
			t.rings = 20
			t.ring_segments = 4
			var rim := _add(n, t, _mat(Color(0.44, 0.6, 0.48), 0.5, 0.6))
			rim.scale = Vector3(1, 0.35, 1)
			rim.position.y = 0.003
			var head := CylinderMesh.new()
			head.top_radius = 0.0075
			head.bottom_radius = 0.009
			head.height = 0.0012
			head.radial_segments = 12
			head.rings = 1
			var h := _add(n, head, _mat(Color(0.5, 0.64, 0.5), 0.5, 0.6))
			h.position = Vector3(0.001, 0.0034, -0.001)
			h.scale = Vector3(0.85, 1, 1)
		"water_vein":
			_add(n, _stone_mesh(Vector3(0.075, 0.04, 0.052), Color(0.24, 0.25, 0.27), Color(0.66, 0.74, 0.8), 0.42, 23 + variant), _mat_vc(0.85))
		"map_scrap":
			var m := _add(n, _scrap_mesh(variant), _map_mat())
			m.position.y = 0.0015
		"shard":
			_add(n, _shard_mesh(variant), _mat_vc(0.8))
	n.rotation.y = [0.0, 0.5, -0.35][variant % 3]
	for c in n.get_children():
		(c as GeometryInstance3D).cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return n


static func _add(parent: Node3D, mesh: Mesh, mat: Material) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	m.mesh = mesh
	m.material_override = mat
	parent.add_child(m)
	return m


static func _mat(c: Color, rough: float, metal: float = 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	m.metallic = metal
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	return m


static func _mat_vc(rough: float) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.vertex_color_use_as_albedo = true
	m.vertex_color_is_srgb = true
	m.roughness = rough
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	return m


## A pebble: a squashed, slightly lumpy sphere resting on its flat side, its colour `a` with
## speckles of `b`; with `vein` > 0 a pale band of `b` runs across it (the water vein).
static func _stone_mesh(size: Vector3, a: Color, b: Color, vein: float, seed: int) -> ArrayMesh:
	var key := "stone%s%s%d" % [size, vein, seed]
	if _cache.has(key):
		return _cache[key]
	var sph := SphereMesh.new()
	sph.radius = 0.5
	sph.height = 1.0
	sph.radial_segments = 24
	sph.rings = 12
	var arr := sph.get_mesh_arrays()
	var v: PackedVector3Array = arr[Mesh.ARRAY_VERTEX]
	var cols := PackedColorArray()
	cols.resize(v.size())
	var axis := Vector3(0.6, 0.15, 0.8).normalized()
	for i in range(v.size()):
		var p := v[i]
		var lump := 1.0 + 0.07 * sin(p.x * 9.0 + seed) * cos(p.z * 7.0 - seed * 0.3) + 0.04 * sin(p.y * 13.0 + seed * 1.7)
		p *= lump
		# Flatter underneath, so it lies.
		if p.y < 0.0:
			p.y *= 0.55
		v[i] = Vector3(p.x * size.x, (p.y + 0.5 * 0.55) * size.y, p.z * size.z)
		var speck := fposmod(sin(i * 12.9898 + seed) * 43758.5453, 1.0)
		var c := a.lerp(b, 0.35) if speck > 0.9 else a.darkened(0.06 * speck)
		if vein > 0.0:
			var d := absf(p.normalized().dot(axis))
			var w := vein * (0.8 + 0.4 * sin(p.y * 11.0 + p.x * 5.0))
			if d < w * 0.5:
				c = b.lerp(Color(0.62, 0.74, 0.86), 0.3 * speck)
			elif d < w * 0.7:
				c = a.lerp(b, 0.4)
		cols[i] = c
	var idx: PackedInt32Array = arr[Mesh.ARRAY_INDEX]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in idx:
		st.set_color(cols[i])
		st.add_vertex(v[i])
	st.index()
	st.generate_normals()
	var mesh := st.commit()
	_cache[key] = mesh
	return mesh


## A ribbed scallop shell, a fan of ribs rising a little to its middle, about 4.5 cm across.
static func _shell_mesh() -> ArrayMesh:
	if _cache.has("shell"):
		return _cache["shell"]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	const NA := 24
	const NR := 6
	var pts: Array = []
	for j in range(NR + 1):
		var row: Array = []
		var r := float(j) / NR
		for i in range(NA + 1):
			var a := lerpf(-1.25, 1.25, float(i) / NA)
			var rib := 0.0035 * absf(sin(a * 9.0)) * r
			var h := 0.008 * (1.0 - r * r) + rib
			row.append(Vector3(sin(a) * r * 0.026, h, -cos(a) * r * 0.026 + 0.012))
		pts.append(row)
	for j in range(NR):
		for i in range(NA):
			var p00: Vector3 = pts[j][i]
			var p10: Vector3 = pts[j][i + 1]
			var p01: Vector3 = pts[j + 1][i]
			var p11: Vector3 = pts[j + 1][i + 1]
			for p in [p00, p11, p01, p00, p10, p11]:
				st.add_vertex(p)
	# The hinge's two small ears.
	for s in [-1.0, 1.0]:
		var e0 := Vector3(0.0, 0.007, 0.012)
		st.add_vertex(e0)
		st.add_vertex(Vector3(s * 0.009, 0.004, 0.016) if s > 0 else Vector3(s * 0.009, 0.004, 0.008))
		st.add_vertex(Vector3(s * 0.009, 0.004, 0.008) if s > 0 else Vector3(s * 0.009, 0.004, 0.016))
	st.generate_normals()
	var mesh := st.commit()
	_cache["shell"] = mesh
	return mesh


## A gnarled old root: a curved tapering tube with one side root, its bark dry and grey-brown,
## darker underneath.
static func _root_mesh(variant: int) -> ArrayMesh:
	var key := "root%d" % (variant % 3)
	if _cache.has(key):
		return _cache[key]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var w := float(variant % 3)
	var main := _curve([Vector3(-0.068, 0.014, 0.012), Vector3(-0.04, 0.015, -0.014 + 0.004 * w), Vector3(-0.008, 0.013, 0.006),
		Vector3(0.022, 0.01, -0.01), Vector3(0.048, 0.007, 0.007), Vector3(0.07, 0.004, -0.002)], 3)
	_tube(st, main, 0.0135, 0.0025, variant)
	_tube(st, _curve([Vector3(-0.014, 0.011, 0.003), Vector3(-0.004, 0.008, 0.022), Vector3(0.012, 0.005, 0.04)], 3), 0.0065, 0.0016, variant + 3)
	st.index()
	st.generate_normals()
	var mesh := st.commit()
	_cache[key] = mesh
	return mesh


## Catmull-Rom through `pts`, `steps` samples per span.
static func _curve(pts: Array, steps: int) -> Array:
	var out: Array = []
	for k in range(pts.size() - 1):
		var p0: Vector3 = pts[maxi(k - 1, 0)]
		var p1: Vector3 = pts[k]
		var p2: Vector3 = pts[k + 1]
		var p3: Vector3 = pts[mini(k + 2, pts.size() - 1)]
		for i in range(steps):
			var t := float(i) / steps
			out.append(0.5 * ((2.0 * p1) + (-p0 + p2) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t * t + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t * t * t))
	out.append(pts[-1])
	return out


static func _tube(st: SurfaceTool, pts: Array, r0: float, r1: float, seed: int) -> void:
	const SIDES := 8
	var top := Color(0.36, 0.28, 0.2)
	var under := Color(0.14, 0.1, 0.07)
	var rings: Array = []
	var cols: Array = []
	for k in range(pts.size()):
		var p: Vector3 = pts[k]
		var t: Vector3 = ((pts[mini(k + 1, pts.size() - 1)] as Vector3) - (pts[maxi(k - 1, 0)] as Vector3)).normalized()
		var side := t.cross(Vector3.UP).normalized()
		var up := side.cross(t).normalized()
		var r := lerpf(r0, r1, pow(float(k) / (pts.size() - 1), 0.8))
		var ring: Array = []
		var col: Array = []
		for s in range(SIDES):
			var a := TAU * s / SIDES
			var knot := 1.0 + 0.16 * sin(k * 1.1 + s * 1.7 + seed) + 0.1 * sin(k * 2.9 + seed)
			var off := side * cos(a) + up * sin(a)
			ring.append(p + off * r * knot)
			# Bark: lighter on top, a ridge here and there.
			col.append(under.lerp(top, 0.5 + 0.5 * off.y).lightened(0.12 * maxf(sin(s * 2.0 + k * 0.7 + seed), 0.0)))
		rings.append(ring)
		cols.append(col)
	for k in range(rings.size() - 1):
		for s in range(SIDES):
			var s2 := (s + 1) % SIDES
			for e in [[k, s], [k + 1, s], [k, s2], [k, s2], [k + 1, s], [k + 1, s2]]:
				st.set_color(cols[e[0]][e[1]])
				st.add_vertex(rings[e[0]][e[1]])
	# Caps: the cut ends a paler wood.
	for e in [0, rings.size() - 1]:
		var centre: Vector3 = pts[e]
		for s in range(SIDES):
			var a: Vector3 = rings[e][s]
			var b: Vector3 = rings[e][(s + 1) % SIDES]
			for p in ([centre, a, b] if e == 0 else [centre, b, a]):
				st.set_color(Color(0.42, 0.33, 0.24))
				st.add_vertex(p)


## A torn scrap of map paper, a little curled, its outline ragged.
static func _scrap_mesh(variant: int) -> ArrayMesh:
	var key := "scrap%d" % (variant % 3)
	if _cache.has(key):
		return _cache[key]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	const N := 22
	var w := 0.105
	var h := 0.075
	var ring: Array = []
	for i in range(N):
		var a := TAU * i / N
		var rx := cos(a)
		var rz := sin(a)
		# A rounded rectangle, its edge torn (ragged in and out).
		var k := 1.0 / maxf(absf(rx), absf(rz)) if maxf(absf(rx), absf(rz)) > 0.0 else 1.0
		k = lerpf(1.0, k, 0.8)
		var tear := 1.0 - 0.08 * absf(sin(i * 3.7 + variant * 1.3)) - 0.05 * absf(sin(i * 7.1 + variant))
		var x := rx * k * tear * w * 0.5
		var z := rz * k * tear * h * 0.5
		ring.append(Vector3(x, 0.004 * (x / (w * 0.5)) * (x / (w * 0.5)), z))
	var centre := Vector3.ZERO
	for i in range(N):
		var a: Vector3 = ring[i]
		var b: Vector3 = ring[(i + 1) % N]
		for p in [centre, a, b]:
			st.set_uv(Vector2(p.x / w + 0.5, p.z / h + 0.5))
			st.set_normal(Vector3.UP)
			st.add_vertex(p)
	var mesh := st.commit()
	_cache[key] = mesh
	return mesh


## Old map paper: yellowed, a few faint ink lines (a path, a field's edge) and a pencil circle.
static func _map_mat() -> StandardMaterial3D:
	if _cache.has("map_mat"):
		return _cache["map_mat"]
	var img := Image.create(96, 72, false, Image.FORMAT_RGB8)
	var paper := Color(0.72, 0.64, 0.47)
	for y in range(72):
		for x in range(96):
			var n := 0.04 * sin(x * 0.7 + y * 0.3) * sin(y * 0.9 - x * 0.2) + 0.05 * fposmod(sin(x * 12.9898 + y * 78.233) * 43758.5453, 1.0)
			var edge := minf(minf(x, 95 - x), minf(y, 71 - y))
			img.set_pixel(x, y, paper.darkened(n + (0.18 if edge < 4 else 0.0) * (1.0 - edge / 4.0)))
	var ink := Color(0.34, 0.27, 0.2)
	# A winding path, a field's edge and a few hatch marks.
	for x in range(6, 90):
		var y := int(36 + 12 * sin(x * 0.09) + 4 * sin(x * 0.31))
		img.set_pixel(x, y, ink)
		if x % 3 != 0:
			img.set_pixel(x, y + 1, ink.lightened(0.2))
	for y in range(8, 64):
		var x := int(20 + 3 * sin(y * 0.2))
		if y % 4 != 0:
			img.set_pixel(x, y, ink.lightened(0.15))
	for k in range(6):
		for t in range(5):
			img.set_pixel(30 + k * 4 + t, 14 + t, ink.lightened(0.3))
	# The pencil circle round a patch.
	var pencil := Color(0.4, 0.4, 0.42)
	for i in range(48):
		var a := TAU * i / 48.0
		var px := int(66 + cos(a) * 9.0)
		var py := int(22 + sin(a) * 7.0)
		img.set_pixel(px, py, pencil)
		img.set_pixel(px + 1, py, pencil.lightened(0.2))
	var tex := ImageTexture.create_from_image(img)
	var m := StandardMaterial3D.new()
	m.albedo_texture = tex
	m.roughness = 0.95
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	m.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	_cache["map_mat"] = m
	return m


## A curved piece of a clay drain pipe: terracotta outside, the inside darker with old silt, its
## broken edges uneven. Lies on its outside, the hollow up.
static func _shard_mesh(variant: int) -> ArrayMesh:
	var key := "shard%d" % (variant % 3)
	if _cache.has(key):
		return _cache[key]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	const NA := 9
	const NL := 6
	var r_out := 0.05
	var thick := 0.007
	var length := 0.09
	var outside := Color(0.5, 0.25, 0.15)
	var inside := Color(0.3, 0.2, 0.14)
	var edge := Color(0.58, 0.34, 0.24)
	var grid_o: Array = []
	var grid_i: Array = []
	for j in range(NL + 1):
		var ro: Array = []
		var ri: Array = []
		var u := float(j) / NL
		# The break: the arc is a little wider or narrower along the length.
		var span := 0.75 + 0.08 * sin(u * 4.0 + variant * 2.0)
		for i in range(NA + 1):
			var a := lerpf(-span, span, float(i) / NA)
			var zl := (u - 0.5) * length + 0.003 * sin(i * 1.3 + variant) * (1.0 if j == 0 or j == NL else 0.0)
			ro.append(Vector3(sin(a) * r_out, r_out - cos(a) * r_out, zl))
			ri.append(Vector3(sin(a) * (r_out - thick), r_out - cos(a) * (r_out - thick) + thick, zl))
		grid_o.append(ro)
		grid_i.append(ri)
	for j in range(NL):
		for i in range(NA):
			_quad(st, grid_o[j][i], grid_o[j][i + 1], grid_o[j + 1][i + 1], grid_o[j + 1][i], outside, true)
			_quad(st, grid_i[j][i], grid_i[j][i + 1], grid_i[j + 1][i + 1], grid_i[j + 1][i], inside, false)
	# The broken edges: the two long sides and the two ends.
	for j in range(NL):
		_quad(st, grid_o[j][0], grid_o[j + 1][0], grid_i[j + 1][0], grid_i[j][0], edge, true)
		_quad(st, grid_o[j][NA], grid_o[j + 1][NA], grid_i[j + 1][NA], grid_i[j][NA], edge, false)
	for i in range(NA):
		_quad(st, grid_o[0][i], grid_o[0][i + 1], grid_i[0][i + 1], grid_i[0][i], edge, false)
		_quad(st, grid_o[NL][i], grid_o[NL][i + 1], grid_i[NL][i + 1], grid_i[NL][i], edge, true)
	st.generate_normals()
	var mesh := st.commit()
	_cache[key] = mesh
	return mesh


static func _quad(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3, col: Color, flip: bool) -> void:
	var tri := [a, b, c, a, c, d] if flip else [a, c, b, a, d, c]
	for p in tri:
		st.set_color(col)
		st.add_vertex(p)
