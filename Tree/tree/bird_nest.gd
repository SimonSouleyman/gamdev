class_name BirdNest
extends MeshInstance3D
## The blackbirds' nest in the crown, once it has come (Visitors; 0.8.2.4 rework, Simon: the
## old torus "looks odd"). A small cup of woven twigs and grass sitting on a fork of a side limb:
## rough rim, dark mud lining inside, loose straws, a little moss, twigs bound along the limb it
## sits on, and in spring a clutch of blue-green eggs. One merged mesh (about 1500 triangles at
## most), built from the save seed, rebuilt only when its fork or the season changes. It sways
## with the bark (bird_nest.gdshader).

## Size of a blackbird's nest: about 23 cm across, a 13 cm cup, 9 cm high.
const RIM_OUT := 0.115
const SIDES := 18
const WALL_TWIGS := 44
const RIM_TWIGS := 12
const STRAWS := 8
const MOSS := 6
## Room the cup needs round it: when the fork is chosen, with the wood as thick as it will grow
## (TreeView.wood_scale at most 3.2), so it rarely has to move on later; and to stay.
const ROOM_PICK := 0.1
const ROOM_KEEP := 0.0

var _fork := -1
var _key := ""
var _rng := RandomNumberGenerator.new()
var _v := PackedVector3Array()
var _n := PackedVector3Array()
var _c := PackedColorArray()
var _uv := PackedVector2Array()
var _ix := PackedInt32Array()


func _init() -> void:
	var m := ShaderMaterial.new()
	m.shader = preload("res://tree/bird_nest.gdshader")
	var d: Texture2D = Assets._tex(Assets.BARK_DIFF)
	if d != null:
		m.set_shader_parameter("bark_albedo", d)
		m.set_shader_parameter("bark_normal", Assets._tex(Assets.BARK_NORMAL))
		m.set_shader_parameter("use_textures", true)
	var nt := NoiseTexture2D.new()
	var fn := FastNoiseLite.new()
	fn.frequency = 0.04
	nt.noise = fn
	nt.seamless = true
	nt.width = 128
	nt.height = 128
	m.set_shader_parameter("noise", nt)
	material_override = m
	visible = false


## Shows the nest once it has come and seats it on its fork.
func update(state: GameState) -> void:
	visible = Visitors.has_come(state, "nest")
	if not visible:
		return
	var g := state.sim.graph
	# The wood is drawn thicker than the graph's radii as the tree grows (TreeView.wood_scale).
	var scale := TreeView.wood_scale(state.sim.height())
	if not _fork_ok(g, _fork) or room(g, _fork, scale) < ROOM_KEEP:
		_fork = pick_fork(g, state.sim.height(), scale)
	if _fork < 0:
		visible = false
		return
	var eggs := Almanac.season_now() == Almanac.Season.SPRING
	var r := g.radii[_fork] * scale
	var key := "%d:%d:%s:%.3f" % [state.seed, _fork, eggs, r]
	if key == _key:
		return
	_key = key
	# Seated on the limb's top, its base sunk a little into the bark.
	position = g.positions[_fork] + Vector3.UP * r * 0.55
	var dirs: Array[Vector3] = []
	dirs.append(-g.direction_of(_fork))
	for c: int in g.children[_fork]:
		if not g.get_flag(c, "dead", false):
			dirs.append(g.direction_of(c))
	mesh = build(hash([state.seed, "nest"]), r, dirs, eggs)


func triangle_count() -> int:
	return _ix.size() / 3


static func _fork_ok(g: PlantGraph, id: int) -> bool:
	if id < 2 or id >= g.size() or g.get_flag(id, "dead", false):
		return false
	var kids := 0
	for c: int in g.children[id]:
		if not g.get_flag(c, "dead", false):
			kids += 1
	return kids >= 2


## A fork where a blackbird would build: a living side limb a few metres up, thick enough to
## carry the cup, its branches spreading sideways rather than shooting up through it, and with
## room round the cup (no thicker wood, such as the trunk, growing into it).
static func pick_fork(g: PlantGraph, height: float, scale: float) -> int:
	var want := clampf(height * 0.5, 2.5, 6.0)
	var ranked: Array[Vector2] = []
	for id in range(2, g.size()):
		if not _fork_ok(g, id):
			continue
		var r := g.radii[id] * scale
		if r < 0.03 or r > 0.12:
			continue
		var y := g.positions[id].y
		if y < 1.8:
			continue
		var s := absf(y - want) * 0.5
		s += absf(g.direction_of(id).y) * 1.2
		var kids: Array[Vector3] = []
		for c: int in g.children[id]:
			if not g.get_flag(c, "dead", false):
				var d := g.direction_of(c)
				kids.append(d)
				s += maxf(0.0, d.y - 0.5) * 2.0
		if kids.size() >= 2:
			s += maxf(0.0, kids[0].dot(kids[1])) * 0.6
		ranked.append(Vector2(s, id))
	ranked.sort()
	# The best that will have room as the wood thickens; else the roomiest now.
	var roomiest := -1
	var most := -INF
	for k in mini(ranked.size(), 80):
		var id := int(ranked[k].y)
		if room(g, id, 3.2) > ROOM_PICK:
			return id
		var now := room(g, id, scale)
		if now > most:
			most = now
			roomiest = id
	return roomiest


## How much room the cup has: the least gap between the nest's middle and any wood other than
## the fork it sits on, less the nest's own radius.
static func room(g: PlantGraph, id: int, scale: float) -> float:
	var at := g.positions[id] + Vector3.UP * (g.radii[id] * scale * 0.55 + 0.05)
	var least := INF
	for j in range(1, g.size()):
		var p := g.parents[j]
		if p < 0 or j == id or p == id or g.get_flag(j, "dead", false):
			continue
		var a := g.positions[p]
		var b := g.positions[j]
		if a.distance_to(at) > 1.5 and b.distance_to(at) > 1.5:
			continue
		var q := Geometry3D.get_closest_point_to_segment(at, a, b)
		least = minf(least, at.distance_to(q) - maxf(g.radii[j], g.radii[p] * 0.9) * scale)
	return least - RIM_OUT


## The nest's mesh in its own frame (y up, the cup's base at the origin), around a limb of radius
## branch_r whose parts leave in dirs (the parent first).
func build(seed_value: int, branch_r: float, dirs: Array[Vector3], eggs: bool) -> ArrayMesh:
	_rng.seed = seed_value
	_v.clear()
	_n.clear()
	_c.clear()
	_uv.clear()
	_ix.clear()
	_cup()
	for i in WALL_TWIGS:
		_wall_twig(i)
	for i in RIM_TWIGS:
		_rim_twig()
	for i in STRAWS:
		_straw()
	for i in MOSS:
		_moss()
	_binding(branch_r, dirs)
	if eggs:
		var count := _rng.randi_range(3, 4)
		var a0 := _rng.randf() * TAU
		for i in count:
			var a := a0 + TAU * i / count + _rng.randf_range(-0.3, 0.3)
			_egg(Vector3(cos(a) * 0.021, 0.054, sin(a) * 0.021), a + PI * 0.5 + _rng.randf_range(-0.5, 0.5))
	# The colours above are picked as seen (sRGB); the shader works in linear light.
	for i in _c.size():
		var lin := _c[i].srgb_to_linear()
		lin.a = _c[i].a
		_c[i] = lin
	var arr := []
	arr.resize(Mesh.ARRAY_MAX)
	arr[Mesh.ARRAY_VERTEX] = _v
	arr[Mesh.ARRAY_NORMAL] = _n
	arr[Mesh.ARRAY_COLOR] = _c
	arr[Mesh.ARRAY_TEX_UV] = _uv
	arr[Mesh.ARRAY_INDEX] = _ix
	var m := ArrayMesh.new()
	m.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arr)
	return m


# --- the cup ------------------------------------------------------------------------

## Profile of the cup from the base out and up over the rim and down into the hollow: (r, y),
## and how far along it is inside (0 outside .. 1 lining).
const PROFILE := [
	[0.0, 0.0, 0.0], [0.04, 0.002, 0.0], [0.078, 0.014, 0.0], [0.103, 0.036, 0.0],
	[0.115, 0.062, 0.0], [0.11, 0.084, 0.2], [0.09, 0.094, 0.5], [0.07, 0.086, 0.8],
	[0.062, 0.068, 1.0], [0.046, 0.05, 1.0], [0.0, 0.042, 1.0],
]


func _wobble(n: int) -> Array[float]:
	# A few slow waves round the cup, so the rim is lumpy and not round.
	var w: Array[float] = []
	for k in n:
		w.append(_rng.randf() * TAU)
	return w


func _cup() -> void:
	var ph := _wobble(3)
	var hph := _wobble(3)
	var base := _v.size()
	var rows := PROFILE.size()
	var grid: Array[PackedVector3Array] = []
	var jit: Array[float] = []
	for j in SIDES:
		jit.append(_rng.randf_range(-1.0, 1.0))
	for i in rows:
		var p: Array = PROFILE[i]
		var row := PackedVector3Array()
		for j in SIDES:
			var a := TAU * j / SIDES
			var bulge := 1.0 + 0.07 * sin(2.0 * a + ph[0]) + 0.05 * sin(3.0 * a + ph[1]) + 0.03 * sin(5.0 * a + ph[2])
			var rim := float(p[2]) > 0.1 and float(p[2]) < 0.9
			var r: float = p[0] * (bulge + (0.03 * jit[j] if rim else 0.012 * jit[j]))
			var y: float = p[1]
			if y > 0.03:
				# The rim rises and dips unevenly; the hollow stays a hollow.
				var t := clampf((y - 0.03) / 0.06, 0.0, 1.0)
				y += t * (0.009 * sin(3.0 * a + hph[0]) + 0.006 * sin(4.0 * a + hph[1]) + 0.005 * sin(7.0 * a + hph[2]) + (0.004 * jit[j] if rim else 0.0))
			row.append(Vector3(cos(a) * r, y, sin(a) * r))
		grid.append(row)
	for i in rows:
		var p: Array = PROFILE[i]
		var inside: float = p[2]
		for j in SIDES:
			var a := TAU * j / SIDES
			var pt := grid[i][j]
			var up := grid[mini(i + 1, rows - 1)][j] - grid[maxi(i - 1, 0)][j]
			var round := grid[i][(j + 1) % SIDES] - grid[i][(j + SIDES - 1) % SIDES]
			if round.length() < 0.0001:
				round = Vector3(-sin(a), 0, cos(a))
			var nrm := up.cross(round).normalized()
			if i == 0:
				nrm = Vector3.DOWN
			elif i == rows - 1:
				nrm = Vector3.UP
			_v.append(pt)
			_n.append(nrm)
			_c.append(_cup_colour(inside, pt, a))
			_uv.append(Vector2(pt.y * 30.0, a * 2.5))
	for i in rows - 1:
		for j in SIDES:
			var a0 := base + i * SIDES + j
			var a1 := base + i * SIDES + (j + 1) % SIDES
			var b0 := a0 + SIDES
			var b1 := a1 + SIDES
			_quad(a0, a1, b1, b0)


func _cup_colour(inside: float, p: Vector3, a: float) -> Color:
	var twig := Color(0.33, 0.29, 0.24).lerp(Color(0.43, 0.41, 0.37), _rng.randf() * 0.7)
	var straw := Color(0.5, 0.46, 0.36)
	var mud := Color(0.36, 0.3, 0.22)
	var c := twig
	if inside > 0.0 and inside < 0.9:
		c = twig.lerp(straw, 0.5 + 0.3 * _rng.randf())
	elif inside >= 0.9:
		c = mud.lerp(Color(0.5, 0.44, 0.32), _rng.randf() * 0.5)
	# Moss low on the outside, in patches.
	if inside == 0.0 and p.y < 0.05 and sin(a * 2.0 + 1.3) + sin(a * 5.0) > 0.9:
		c = c.lerp(Color(0.25, 0.32, 0.13), 0.7)
	c.a = 1.0
	return c


# --- twigs and grass ------------------------------------------------------------------

## The outside of the cup at height y and angle a (without the wobble; twigs lie a little proud).
func _wall(a: float, y: float, out: float) -> Vector3:
	var r := 0.0
	for i in range(PROFILE.size() - 1):
		var p0: Array = PROFILE[i]
		var p1: Array = PROFILE[i + 1]
		if y >= float(p0[1]) and y <= float(p1[1]):
			r = lerpf(p0[0], p1[0], (y - float(p0[1])) / maxf(float(p1[1]) - float(p0[1]), 0.0001))
			break
		if i >= 4:
			r = RIM_OUT
			break
	r += out
	return Vector3(cos(a) * r, y, sin(a) * r)


func _twig_colour() -> Color:
	var k := _rng.randf()
	var c: Color
	if k < 0.5:
		c = Color(0.29, 0.25, 0.2)
	elif k < 0.8:
		c = Color(0.44, 0.42, 0.38)
	else:
		c = Color(0.48, 0.44, 0.34)
	c = c * _rng.randf_range(0.8, 1.15)
	c.a = 1.0
	return c


## Twigs woven round the outside, sloping a little, their ends sticking out.
func _wall_twig(i: int) -> void:
	var a := _rng.randf() * TAU
	var y := lerpf(0.008, 0.088, (i + _rng.randf()) / WALL_TWIGS)
	var span := _rng.randf_range(0.6, 1.3)
	var slope := _rng.randf_range(-0.045, 0.045)
	var pts := PackedVector3Array()
	for k in 3:
		var t := k / 2.0
		var out := 0.003 + (_rng.randf_range(0.002, 0.012) if k != 1 else 0.0)
		if k == 2 and i % 4 == 0:
			out += _rng.randf_range(0.01, 0.03)
		var yy := clampf(y + slope * (t - 0.5), 0.004, 0.095)
		pts.append(_wall(a + span * t, yy, out))
	_strand(pts, _rng.randf_range(0.0028, 0.005), _twig_colour())


## Twigs and grass laid over the rim, some ends sticking up and out.
func _rim_twig() -> void:
	var a := _rng.randf() * TAU
	var span := _rng.randf_range(0.5, 1.1)
	var pts := PackedVector3Array()
	for k in 3:
		var t := k / 2.0
		var aa := a + span * t
		var r := RIM_OUT * lerpf(0.97, 0.78, sin(t * PI)) + (0.03 * _rng.randf() if k == 2 else 0.0)
		var y := 0.09 + 0.006 * sin(t * PI) + (0.015 * _rng.randf() if k == 2 else 0.0)
		pts.append(Vector3(cos(aa) * r, y, sin(aa) * r))
	var c := _twig_colour() if _rng.randf() < 0.5 else Color(0.52, 0.47, 0.34) * _rng.randf_range(0.85, 1.05)
	c.a = 1.0
	_strand(pts, _rng.randf_range(0.0022, 0.0035), c)


## Loose dry grass hanging from the cup.
func _straw() -> void:
	var a := _rng.randf() * TAU
	var y := _rng.randf_range(0.02, 0.08)
	var s := _wall(a, y, 0.003)
	var out := Vector3(cos(a), 0, sin(a))
	var side := Vector3(-sin(a), 0, cos(a)) * _rng.randf_range(-0.03, 0.03)
	var len := _rng.randf_range(0.06, 0.13)
	var pts := PackedVector3Array([s, s + out * 0.025 + side * 0.5 + Vector3.DOWN * 0.01,
		s + out * 0.04 + side + Vector3.DOWN * len])
	var c := Color(0.55, 0.5, 0.36) * _rng.randf_range(0.85, 1.05)
	c.a = 1.0
	_strand(pts, 0.0018, c)


## A tuft of moss pressed into the outside.
func _moss() -> void:
	var a := _rng.randf() * TAU
	var y := _rng.randf_range(0.012, 0.06)
	var p := _wall(a, y, 0.0)
	var out := Vector3(cos(a), 0, sin(a))
	var tan := Vector3(-sin(a), 0, cos(a))
	var c := Color(0.24, 0.32, 0.12) * _rng.randf_range(0.8, 1.15)
	c.a = 1.0
	var w := _rng.randf_range(0.014, 0.024)
	var h := w * 0.7
	var axes := [out * w * 0.5, -out * w * 0.2, tan * w, -tan * w, Vector3.UP * h, Vector3.DOWN * h]
	var b := _v.size()
	for ax: Vector3 in axes:
		_v.append(p + ax)
		_n.append(ax.normalized().lerp(out, 0.5).normalized())
		_c.append(c)
		_uv.append(Vector2(ax.x, ax.z) * 20.0)
	for f in [[0, 2, 4], [0, 4, 3], [0, 3, 5], [0, 5, 2], [1, 4, 2], [1, 3, 4], [1, 5, 3], [1, 2, 5]]:
		_tri(b + f[0], b + f[1], b + f[2])


## Twigs bound along the limb the nest sits on, winding round it, so the cup holds on.
func _binding(branch_r: float, dirs: Array[Vector3]) -> void:
	var centre := Vector3.DOWN * branch_r * 0.55
	for d in dirs:
		var along := Vector3(d.x, d.y * 0.6, d.z)
		if along.length() < 0.2:
			continue
		along = along.normalized()
		var side := along.cross(Vector3.UP)
		if side.length() < 0.1:
			continue
		side = side.normalized()
		var up := side.cross(along).normalized()
		for k in 2:
			var a0 := _rng.randf_range(-0.6, 0.6)
			var turn := _rng.randf_range(1.2, 2.2) * (1.0 if k == 0 else -1.0)
			var reach := _rng.randf_range(0.13, 0.2)
			var pts := PackedVector3Array()
			for s in 4:
				var t := s / 3.0
				var a := a0 + turn * t
				var rr := branch_r + 0.004
				pts.append(centre + along * lerpf(0.05, reach, t) + (up * cos(a) + side * sin(a)) * rr)
			_strand(pts, _rng.randf_range(0.0025, 0.004), _twig_colour())


## A thin three-sided strand through pts.
func _strand(pts: PackedVector3Array, w: float, c: Color) -> void:
	var b := _v.size()
	var n := pts.size()
	var ref := Vector3.UP
	var acc := 0.0
	for k in n:
		var t := (pts[mini(k + 1, n - 1)] - pts[maxi(k - 1, 0)]).normalized()
		if absf(t.dot(ref)) > 0.9:
			ref = Vector3.RIGHT
		var n1 := t.cross(ref).normalized()
		var n2 := t.cross(n1)
		if k > 0:
			acc += pts[k].distance_to(pts[k - 1])
		# Tips taper.
		var ww := w * (0.55 if k == n - 1 else 1.0)
		var shade := c * _rng.randf_range(0.9, 1.08)
		shade.a = 1.0
		for s in 3:
			var ang := TAU * s / 3.0
			var o := n1 * cos(ang) + n2 * sin(ang)
			_v.append(pts[k] + o * ww)
			_n.append(o)
			_c.append(shade)
			_uv.append(Vector2(s / 3.0 * 0.3, acc * 6.0))
	for k in n - 1:
		for s in 3:
			var a0 := b + k * 3 + s
			var a1 := b + k * 3 + (s + 1) % 3
			_quad(a0, a1, a1 + 3, a0 + 3)


## A blackbird's egg (29 by 21 mm), pale blue-green with brown speckles (the shader).
func _egg(at: Vector3, yaw: float) -> void:
	var b := _v.size()
	var rings := 5
	var segs := 6
	var ax := Vector3(cos(yaw), -0.15, sin(yaw)).normalized()
	var s1 := ax.cross(Vector3.UP).normalized()
	var s2 := s1.cross(ax)
	var c := Color(0.56, 0.65, 0.56)
	c.a = 0.08
	var verts := [[at + ax * 0.0145, ax]]
	for i in range(1, rings):
		var th := PI * i / rings
		var lon := cos(th) * 0.0145
		var rad := sin(th) * 0.0105 * (1.0 + 0.1 * cos(th))
		for j in segs:
			var ph := TAU * j / segs
			var o := s1 * cos(ph) + s2 * sin(ph)
			verts.append([at + ax * lon + o * rad, (ax * cos(th) * 0.7 + o * sin(th)).normalized()])
	verts.append([at - ax * 0.0145, -ax])
	for k in verts.size():
		_v.append(verts[k][0])
		_n.append(verts[k][1])
		_c.append(c)
		_uv.append(Vector2(k * 0.37, k * 0.21))
	var last := b + verts.size() - 1
	for j in segs:
		_tri(b, b + 1 + j, b + 1 + (j + 1) % segs)
		_tri(last, b + 1 + (rings - 2) * segs + (j + 1) % segs, b + 1 + (rings - 2) * segs + j)
	for i in rings - 2:
		for j in segs:
			var a0 := b + 1 + i * segs + j
			var a1 := b + 1 + i * segs + (j + 1) % segs
			_quad(a0, a1, a1 + segs, a0 + segs)


# --- triangles ----------------------------------------------------------------------

func _quad(a: int, b: int, c: int, d: int) -> void:
	_tri(a, b, c)
	_tri(a, c, d)


## One triangle, wound so its front faces the way its vertex normals point (Godot's front faces
## are clockwise).
func _tri(a: int, b: int, c: int) -> void:
	var f := (_v[b] - _v[a]).cross(_v[c] - _v[a])
	if f.length_squared() < 1e-14:
		return
	if f.dot(_n[a] + _n[b] + _n[c]) > 0.0:
		_ix.append_array([a, c, b])
	else:
		_ix.append_array([a, b, c])
