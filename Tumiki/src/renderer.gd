## Batched block renderer. Replaces the OpenGL display lists of the original:
## every tumiki shape/shade combination becomes a MultiMesh that is refilled each frame.
class_name BlockRenderer
extends Node3D

const SHAPE_NUM := 10
const SHADE_NUM := 4
const DEPTH := -2.0
const LP := 0.03

enum Layer { WORLD, OVER, TOP }

const COLORS := [
	Color(0.9, 0.6, 0.6), Color(0.6, 0.9, 0.6), Color(0.6, 0.6, 0.9),
	Color(0.8, 0.8, 0.6), Color(0.8, 0.6, 0.8), Color(0.6, 0.8, 0.8),
	Color(0.8, 0.8, 0.8), Color(0.5, 0.5, 0.5),
	Color(1, 0.7, 0.5), Color(0.7, 0.9, 1), Color(1, 0.5, 0.8),
	Color(0.6, 0.6, 0.3),
]

const FILL_SHADER := """
shader_type spatial;
render_mode unshaded, cull_disabled%s;
void vertex() {
	
}
void fragment() {
	ALBEDO = COLOR.rgb;
	%s
}
"""

class Batch:
	var mmi: MultiMeshInstance3D
	var mm: MultiMesh
	var buf := PackedFloat32Array()
	var n := 0
	var cap := 0

var batches := {}  # key -> Batch
var fill_meshes := []  # [list][shade] -> ArrayMesh
var line_meshes := []
var materials := {}
var particle_batch: Batch


func _ready() -> void:
	_build_meshes()
	var pmesh := ArrayMesh.new()
	var arr := []
	arr.resize(Mesh.ARRAY_MAX)
	arr[Mesh.ARRAY_VERTEX] = PackedVector3Array([
		Vector3(-1, -1, 0), Vector3(1, -1, 0), Vector3(1, 1, 0),
		Vector3(-1, -1, 0), Vector3(1, 1, 0), Vector3(-1, 1, 0)])
	var pc := PackedColorArray()
	for i in 6:
		pc.append(Color(1, 1, 1, 1))
	arr[Mesh.ARRAY_COLOR] = pc
	pmesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arr)
	var sh := Shader.new()
	sh.code = """
shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_test_disabled, depth_draw_never;
void vertex() {  }
void fragment() { ALBEDO = COLOR.rgb; ALPHA = COLOR.a; }
"""
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.render_priority = 1
	particle_batch = _make_batch(pmesh, mat)


func _material(layer: int) -> ShaderMaterial:
	if materials.has(layer):
		return materials[layer]
	var sh := Shader.new()
	match layer:
		Layer.WORLD:
			sh.code = FILL_SHADER % ["", ""]
		_:
			sh.code = FILL_SHADER % [", depth_test_disabled, depth_draw_never", "ALPHA = 1.0;"]
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.render_priority = 0 if layer == Layer.WORLD else (2 if layer == Layer.OVER else 4)
	materials[layer] = mat
	return mat


func _make_batch(mesh: Mesh, mat: Material) -> Batch:
	var b := Batch.new()
	b.mm = MultiMesh.new()
	b.mm.transform_format = MultiMesh.TRANSFORM_3D
	b.mm.use_colors = true
	b.mm.mesh = mesh
	b.mmi = MultiMeshInstance3D.new()
	b.mmi.multimesh = b.mm
	b.mmi.material_override = mat
	b.mmi.extra_cull_margin = 16384
	b.mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(b.mmi)
	return b


func _get_batch(list: int, shade: int, layer: int, lines: bool) -> Batch:
	var key := ((layer * 2 + int(lines)) * SHADE_NUM + shade) * SHAPE_NUM + list
	var b: Batch = batches.get(key)
	if b == null:
		var mesh: Mesh = line_meshes[list][shade] if lines else fill_meshes[list][shade]
		b = _make_batch(mesh, _material(layer))
		if lines:
			b.mmi.sorting_offset = 0.1
		batches[key] = b
	return b


static func _push(b: Batch, r0: Vector4, r1: Vector4, r2: Vector4, c: Color) -> void:
	var o := b.n * 16
	if o + 16 > b.buf.size():
		b.buf.resize(maxi(256, b.buf.size() * 2))
	b.buf[o] = r0.x; b.buf[o + 1] = r0.y; b.buf[o + 2] = r0.z; b.buf[o + 3] = r0.w
	b.buf[o + 4] = r1.x; b.buf[o + 5] = r1.y; b.buf[o + 6] = r1.z; b.buf[o + 7] = r1.w
	b.buf[o + 8] = r2.x; b.buf[o + 9] = r2.y; b.buf[o + 10] = r2.z; b.buf[o + 11] = r2.w
	b.buf[o + 12] = c.r; b.buf[o + 13] = c.g; b.buf[o + 14] = c.b; b.buf[o + 15] = c.a
	b.n += 1


## Draws one block: display list `list` (0..9), color index, shade (0..3),
## at (x, y, z), rotated by `deg` (radians) around z and scaled.
func block(list: int, color: int, shade: int, x: float, y: float, z: float,
		deg: float, sx: float, sy: float, sz: float, layer: int = Layer.WORLD) -> void:
	var c := cos(deg)
	var s := sin(deg)
	var r0 := Vector4(c * sx, -s * sy, 0, x)
	var r1 := Vector4(s * sx, c * sy, 0, y)
	var r2 := Vector4(0, 0, sz, z)
	var col: Color = COLORS[color % COLORS.size()]
	_push(_get_batch(list, shade, layer, false), r0, r1, r2, col)
	if line_meshes[list][shade] != null:
		_push(_get_batch(list, shade, layer, true), r0, r1, r2, col)


func block_t(list: int, color: int, shade: int, t: Transform3D, layer: int = Layer.WORLD) -> void:
	var bs := t.basis
	var r0 := Vector4(bs.x.x, bs.y.x, bs.z.x, t.origin.x)
	var r1 := Vector4(bs.x.y, bs.y.y, bs.z.y, t.origin.y)
	var r2 := Vector4(bs.x.z, bs.y.z, bs.z.z, t.origin.z)
	var col: Color = COLORS[color % COLORS.size()]
	_push(_get_batch(list, shade, layer, false), r0, r1, r2, col)
	if line_meshes[list][shade] != null:
		_push(_get_batch(list, shade, layer, true), r0, r1, r2, col)


func particle(x: float, y: float, size: float, col: Color) -> void:
	_push(particle_batch, Vector4(size, 0, 0, x), Vector4(0, size, 0, y), Vector4(0, 0, 1, 0), col)


func begin_frame() -> void:
	for b in batches.values():
		b.n = 0
	particle_batch.n = 0


func end_frame() -> void:
	for b in batches.values():
		_flush(b)
	_flush(particle_batch)


func _flush(b: Batch) -> void:
	if b.n == 0:
		if b.mm.visible_instance_count != 0:
			b.mm.visible_instance_count = 0
		return
	var need := b.buf.size() / 16
	if b.cap != need:
		b.cap = need
		b.mm.instance_count = need
	b.mm.buffer = b.buf
	b.mm.visible_instance_count = b.n


# --- Mesh construction (port of Tumiki.createDisplayLists) ---

func _factor_front(i: int) -> float:
	return [0.9, 0.8, 0.5, 0.9][i]


func _factor_side(i: int) -> float:
	return [0.7, 0.6, 0.4, 0.0][i]


func _build_meshes() -> void:
	for l in SHAPE_NUM:
		var fl := []
		var ll := []
		for i in SHADE_NUM:
			var tris := PackedVector3Array()
			var cols := PackedColorArray()
			var lines := PackedVector3Array()
			var lcols := PackedColorArray()
			_build_shape(l, i, tris, cols, lines, lcols)
			fl.append(_mesh(tris, cols, Mesh.PRIMITIVE_TRIANGLES))
			ll.append(_mesh(lines, lcols, Mesh.PRIMITIVE_LINES) if lines.size() > 0 else null)
		fill_meshes.append(fl)
		line_meshes.append(ll)


func _mesh(v: PackedVector3Array, c: PackedColorArray, prim: int) -> ArrayMesh:
	var m := ArrayMesh.new()
	var arr := []
	arr.resize(Mesh.ARRAY_MAX)
	arr[Mesh.ARRAY_VERTEX] = v
	arr[Mesh.ARRAY_COLOR] = c
	m.add_surface_from_arrays(prim, arr)
	return m


static func _quad(t: PackedVector3Array, c: PackedColorArray, a: Vector3, b: Vector3, cc: Vector3, d: Vector3, f: float) -> void:
	var col := Color(f, f, f)
	for v in [a, b, cc, a, cc, d]:
		t.append(v)
		c.append(col)


static func _tri(t: PackedVector3Array, c: PackedColorArray, a: Vector3, b: Vector3, cc: Vector3, f: float) -> void:
	var col := Color(f, f, f)
	for v in [a, b, cc]:
		t.append(v)
		c.append(col)


static func _strip(l: PackedVector3Array, c: PackedColorArray, pts: Array, f: float) -> void:
	var col := Color(f, f, f)
	for k in pts.size() - 1:
		l.append(pts[k])
		l.append(pts[k + 1])
		c.append(col)
		c.append(col)


static func _segs(l: PackedVector3Array, c: PackedColorArray, pts: Array, f: float) -> void:
	var col := Color(f, f, f)
	for p in pts:
		l.append(p)
		c.append(col)


static func _rot(v: Vector3, k: int) -> Vector3:
	# glRotatef(-90 * k, 0, 0, 1)
	var a := deg_to_rad(-90.0 * k)
	return Vector3(v.x * cos(a) - v.y * sin(a), v.x * sin(a) + v.y * cos(a), v.z)


func _build_shape(l: int, i: int, t: PackedVector3Array, c: PackedColorArray,
		ln: PackedVector3Array, lc: PackedColorArray) -> void:
	var ff := _factor_front(i)
	var fs := _factor_side(i)
	var D := DEPTH
	if l == 0 or l == 9:
		_quad(t, c, Vector3(1, 1, 0), Vector3(-1, 1, 0), Vector3(-1, -1, 0), Vector3(1, -1, 0), ff)
		if l == 9:
			_quad(t, c, Vector3(1, 1, D), Vector3(1, -1, D), Vector3(-1, -1, D), Vector3(-1, 1, D), ff)
		if i < 3:
			_quad(t, c, Vector3(-1, 1, 0), Vector3(1, 1, 0), Vector3(1, 1, D), Vector3(-1, 1, D), fs)
			_quad(t, c, Vector3(-1, -1, 0), Vector3(-1, 1, 0), Vector3(-1, 1, D), Vector3(-1, -1, D), fs)
			_quad(t, c, Vector3(1, -1, 0), Vector3(-1, -1, 0), Vector3(-1, -1, D), Vector3(1, -1, D), fs)
			_quad(t, c, Vector3(1, 1, 0), Vector3(1, -1, 0), Vector3(1, -1, D), Vector3(1, 1, D), fs)
		if l == 0 and (i == 0 or i == 3):
			if i == 0:
				var p := 1 + LP
				_strip(ln, lc, [Vector3(p, p, LP), Vector3(-p, p, LP), Vector3(-p, -p, LP), Vector3(p, -p, LP), Vector3(p, p, LP)], 1.0)
				_segs(ln, lc, [Vector3(p, p, D), Vector3(p, p, LP), Vector3(-p, p, D), Vector3(-p, p, LP),
					Vector3(-p, -p, D), Vector3(-p, -p, LP), Vector3(p, -p, D), Vector3(p, -p, LP)], 0.8)
			else:
				_strip(ln, lc, [Vector3(1, 1, 0), Vector3(-1, 1, 0), Vector3(-1, -1, 0), Vector3(1, -1, 0), Vector3(1, 1, 0)], 1.0)
		return
	var p := 1 + LP
	if l >= 1 and l <= 4:
		var k := l - 1
		_tri(t, c, _rot(Vector3(1, 1, 0), k), _rot(Vector3(-1, 1, 0), k), _rot(Vector3(-1, -1, 0), k), ff)
		if i < 3:
			_quad(t, c, _rot(Vector3(-1, 1, 0), k), _rot(Vector3(1, 1, 0), k), _rot(Vector3(1, 1, D), k), _rot(Vector3(-1, 1, D), k), fs)
			_quad(t, c, _rot(Vector3(-1, -1, 0), k), _rot(Vector3(-1, 1, 0), k), _rot(Vector3(-1, 1, D), k), _rot(Vector3(-1, -1, D), k), fs)
			_quad(t, c, _rot(Vector3(1, 1, 0), k), _rot(Vector3(-1, -1, 0), k), _rot(Vector3(-1, -1, D), k), _rot(Vector3(1, 1, D), k), fs)
		if i == 0 or i == 3:
			_strip(ln, lc, [_rot(Vector3(p, p, LP), k), _rot(Vector3(-p, p, LP), k), _rot(Vector3(-p, -p, LP), k), _rot(Vector3(p, p, LP), k)], 1.0)
			if i == 0:
				_segs(ln, lc, [_rot(Vector3(p, p, D), k), _rot(Vector3(p, p, LP), k), _rot(Vector3(-p, p, D), k), _rot(Vector3(-p, p, LP), k),
					_rot(Vector3(-p, -p, D), k), _rot(Vector3(-p, -p, LP), k)], 0.8)
		return
	# 5..8: isosceles triangles u, r, d, l
	var k := l - 5
	_tri(t, c, _rot(Vector3(1, -1, 0), k), _rot(Vector3(0, 1, 0), k), _rot(Vector3(-1, -1, 0), k), ff)
	if i < 3:
		_quad(t, c, _rot(Vector3(0, 1, 0), k), _rot(Vector3(1, -1, 0), k), _rot(Vector3(1, -1, D), k), _rot(Vector3(0, 1, D), k), fs)
		_quad(t, c, _rot(Vector3(-1, -1, 0), k), _rot(Vector3(0, 1, 0), k), _rot(Vector3(0, 1, D), k), _rot(Vector3(-1, -1, D), k), fs)
		_quad(t, c, _rot(Vector3(1, -1, 0), k), _rot(Vector3(-1, -1, 0), k), _rot(Vector3(-1, -1, D), k), _rot(Vector3(1, -1, D), k), fs)
	if i == 0 or i == 3:
		_strip(ln, lc, [_rot(Vector3(p, -1 + LP, LP), k), _rot(Vector3(0, p, LP), k), _rot(Vector3(-p, -p, LP), k), _rot(Vector3(p, -p, LP), k)], 1.0)
		if i == 0:
			_segs(ln, lc, [_rot(Vector3(p, -p, D), k), _rot(Vector3(p, -1 + LP, LP), k), _rot(Vector3(0, p, D), k), _rot(Vector3(0, p, LP), k),
				_rot(Vector3(-p, -p, D), k), _rot(Vector3(-p, -p, LP), k)], 0.8)
