class_name BrushPileView
extends Node3D
## Draws the brush pile (0.8, shared/brush_pile.gd): the cut branches as a low pile of sticks at
## the clearing edge away from the shed, one merged mesh; the hedgehog snuffling out of it and
## back at dusk; the wren singing from its top by day. Each one mesh, one material and no
## shadow, so the pile and the hedgehog cost the phone one draw call each. Only reads the state.

## Real seconds the hedgehog is out (it comes out a little before the sunset hold).
const HOG_SECONDS: float = 48.0
## Its walking pace (a snuffling hedgehog, metres per second) and how far it wanders out.
const HOG_SPEED: float = 0.16
const HOG_REACH: float = 2.2
## Real seconds the wren sits on the pile and sings, and how many songs it gives.
const WREN_SECONDS: float = 36.0
const WREN_SONGS: int = 3
## Wet sticks after rain are this much darker (weather mood).
const WET_DARKEN: float = 0.45

var state: GameState
var pile: MeshInstance3D
var hedgehog: MeshInstance3D
var wren: MeshInstance3D
var _song: AudioStreamPlayer3D
var _pile_mat: StandardMaterial3D
var _base_color := Color(1, 1, 1)
var _built_sticks: int = -1
var _built_species: String = ""
var _radius: float = -1.0
## The highest point of the pile (the wren's perch), in the pile's frame.
var _top := Vector3(0, 0.3, 0)
## The hedgehog's visit: the day it played, its time and path (pile frame).
var _hog_day: int = -1
var _hog_t: float = -1.0
var _hog_path: Array[Vector3] = []
var _hog_pauses: Array[float] = []
var _wren_day: int = -1
var _wren_t: float = -1.0
var _songs_left: int = 0


func _ready() -> void:
	pile = MeshInstance3D.new()
	pile.name = "pile"
	pile.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_pile_mat = StandardMaterial3D.new()
	var bark := load(Assets.BARK_DIFF) as Texture2D if ResourceLoader.exists(Assets.BARK_DIFF) else null
	_pile_mat.albedo_texture = bark
	_pile_mat.vertex_color_use_as_albedo = true
	_pile_mat.roughness = 0.95
	pile.material_override = _pile_mat
	add_child(pile)
	hedgehog = MeshInstance3D.new()
	hedgehog.name = "hedgehog"
	hedgehog.mesh = hedgehog_mesh()
	hedgehog.material_override = _critter_mat(0.8)
	hedgehog.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	hedgehog.visible = false
	add_child(hedgehog)
	wren = MeshInstance3D.new()
	wren.name = "wren"
	wren.mesh = wren_mesh()
	wren.material_override = _critter_mat(0.75)
	wren.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	wren.visible = false
	add_child(wren)
	_song = AudioStreamPlayer3D.new()
	_song.stream = AmbienceSynth.wren_song()
	# A wren is very loud for its size: heard across the clearing.
	_song.unit_size = 14.0
	_song.volume_db = -4.0
	add_child(_song)


func _critter_mat(rough: float) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.vertex_color_use_as_albedo = true
	m.vertex_color_is_srgb = true
	m.roughness = rough
	# The spines and the tail are single thin cones: seen from any side.
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	return m


func setup(p_state: GameState) -> void:
	state = p_state
	_built_sticks = -1
	_radius = -1.0
	# A visit already over before a load does not play again.
	_hog_day = state.brush.hedgehog_day
	_wren_day = state.brush.wren_day
	_hog_t = -1.0
	_wren_t = -1.0
	hedgehog.visible = false
	wren.visible = false
	update(0.0, 0.0)


## Each frame: the pile follows the state (new sticks after a sunrise), the visitors play their
## moment once, and rain darkens the sticks.
func update(delta: float, rain: float) -> void:
	if state == null:
		return
	var r := Scenery.radius_for(state.sim.height())
	if r != _radius:
		_radius = r
		var c := BrushPile.position(r)
		position = Terrain.at(c)
		# The pile lies along the edge: its x axis along the edge, z toward the clearing's centre.
		var inward := -Vector3(c.x, 0.0, c.z).normalized()
		basis = Basis(Vector3.UP.cross(inward).normalized(), Vector3.UP, inward)
	var n := state.brush.stick_count()
	if n != _built_sticks or state.sim.species.id != _built_species:
		_build_pile(n)
	pile.visible = n > 0
	_pile_mat.albedo_color = _base_color.darkened(WET_DARKEN * clampf(rain, 0.0, 1.0))
	_update_hedgehog(delta)
	_update_wren(delta)


# --- the pile ----------------------------------------------------------------------------

func _build_pile(count: int) -> void:
	_built_sticks = count
	_built_species = state.sim.species.id
	# The sticks are the tree's own wood, greyed a little by lying out.
	var b := state.sim.species.bark_tint
	_base_color = Color(b.r, b.g, b.b).lerp(Color(0.46, 0.43, 0.39), 0.4) * 1.45
	_base_color.a = 1.0
	# The sticks follow the ground (it rises toward the trees).
	var ground := func(p: Vector3) -> float: return to_local(Terrain.at(to_global(Vector3(p.x, 0.0, p.z)))).y
	pile.mesh = pile_mesh(state.seed, count, ground)
	_top = Vector3(0, 0.25, 0)
	var arrays := pile.mesh.surface_get_arrays(0) if pile.mesh != null and pile.mesh.get_surface_count() > 0 else []
	if not arrays.is_empty():
		for v: Vector3 in arrays[Mesh.ARRAY_VERTEX]:
			if v.y > _top.y and absf(v.x) < 0.6 and absf(v.z) < 0.5:
				_top = v


## The pile as one mesh: `count` sticks, each seeded by its own index, so a growing pile keeps
## the sticks it had and new ones land on top. Frame: x along the edge, z toward the clearing.
static func pile_mesh(seed: int, count: int, ground: Callable = Callable()) -> ArrayMesh:
	if count <= 0:
		return null
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in range(count):
		var rng := RandomNumberGenerator.new()
		rng.seed = hash([seed, "pile", i])
		var grow := float(i) / BrushPile.MAX_STICKS
		# Later sticks land further in and higher: the pile grows up and out.
		var spread := 0.4 + 0.6 * sqrt(grow)
		var a := rng.randf() * TAU
		var d := sqrt(rng.randf())
		var cx := cos(a) * d * BrushPile.HALF_LENGTH * spread
		var cz := sin(a) * d * BrushPile.HALF_DEPTH * spread
		var dome := 1.0 - d * d
		var y := 0.03 + dome * (0.2 + 0.5 * grow) * rng.randf_range(0.55, 1.0)
		var yaw := rng.randf_range(-0.5, 0.5) + (PI * 0.5 if rng.randf() < 0.3 else 0.0)
		var pitch := rng.randf_range(-0.42, 0.42)
		var dir := Vector3(cos(yaw) * cos(pitch), sin(pitch), sin(yaw) * cos(pitch))
		var length := rng.randf_range(0.7, 1.8)
		var r0 := rng.randf_range(0.014, 0.04)
		var mid := Vector3(cx, y, cz)
		var p0 := mid - dir * length * 0.5
		var p1 := mid + dir * length * 0.5
		# Keep both ends on or above the ground.
		p0.y = maxf(p0.y, r0)
		p1.y = maxf(p1.y, r0 * 0.6)
		if ground.is_valid():
			p0.y += float(ground.call(p0))
			p1.y += float(ground.call(p1))
		var bend := Vector3(rng.randf_range(-0.09, 0.09), rng.randf_range(-0.05, 0.05), rng.randf_range(-0.09, 0.09))
		var shade := rng.randf_range(0.8, 1.05) * lerpf(0.55, 1.0, clampf(y / 0.35, 0.0, 1.0))
		var col := Color(shade, shade * rng.randf_range(0.96, 1.0), shade * rng.randf_range(0.9, 0.98))
		_tube(st, [p0, (p0 + p1) * 0.5 + bend, p1], r0, r0 * 0.55, col, rng.randf())
		# One to three side twigs, as a cut branch keeps them.
		for _k in range(rng.randi_range(2, 4)):
			var at := p0.lerp(p1, rng.randf_range(0.3, 0.9))
			var side := dir.cross(Vector3.UP).normalized() * (1.0 if rng.randf() < 0.5 else -1.0)
			var tdir := (dir * 0.6 + side * 0.7 + Vector3.UP * rng.randf_range(-0.1, 0.35)).normalized()
			var tl := rng.randf_range(0.2, 0.6)
			var tip := at + tdir * tl
			if ground.is_valid():
				tip.y = maxf(tip.y, float(ground.call(tip)) + 0.01)
			else:
				tip.y = maxf(tip.y, 0.01)
			_tube(st, [at, tip], r0 * 0.45, r0 * 0.2, col * 0.95, rng.randf())
	st.generate_normals()
	return st.commit()


## A tapered tube along `path` (5 sides), with bark UVs and a colour.
static func _tube(st: SurfaceTool, path: Array, r0: float, r1: float, col: Color, u0: float) -> void:
	var sides := 5
	var total := 0.0
	for i in range(1, path.size()):
		total += (path[i] as Vector3).distance_to(path[i - 1])
	var rings: Array = []
	var along := 0.0
	var ax := ((path[1] as Vector3) - (path[0] as Vector3)).normalized()
	var u := ax.cross(Vector3.UP if absf(ax.y) < 0.9 else Vector3.RIGHT).normalized()
	for i in range(path.size()):
		if i > 0:
			along += (path[i] as Vector3).distance_to(path[i - 1])
		var a := ((path[mini(i + 1, path.size() - 1)] as Vector3) - (path[maxi(i - 1, 0)] as Vector3)).normalized()
		u = (u - a * u.dot(a)).normalized()
		var v := a.cross(u)
		var r := lerpf(r0, r1, along / maxf(total, 1e-4))
		var ring: Array = []
		for k in range(sides + 1):
			var ang := TAU * k / sides
			ring.append([(path[i] as Vector3) + (u * cos(ang) + v * sin(ang)) * r, Vector2(u0 + float(k) / sides * 0.25, along * 1.6)])
		rings.append(ring)
	for i in range(rings.size() - 1):
		for k in range(sides):
			var q := [rings[i][k], rings[i + 1][k], rings[i + 1][k + 1], rings[i][k], rings[i + 1][k + 1], rings[i][k + 1]]
			for vtx: Array in q:
				st.set_color(col)
				st.set_uv(vtx[1])
				st.add_vertex(vtx[0])
	# Cut ends: a pale disc of fresh wood at the thick end.
	var c0 := path[0] as Vector3
	var pale := Color(col.r * 1.5, col.g * 1.35, col.b * 1.1)
	for k in range(sides):
		st.set_color(pale)
		st.set_uv(Vector2(0.5, 0.5))
		st.add_vertex(c0)
		st.set_color(pale)
		st.set_uv(Vector2(0.5, 0.5))
		st.add_vertex(rings[0][k][0])
		st.set_color(pale)
		st.set_uv(Vector2(0.5, 0.5))
		st.add_vertex(rings[0][k + 1][0])


# --- the hedgehog ------------------------------------------------------------------------

func _update_hedgehog(delta: float) -> void:
	var b := state.brush
	var dusk := state.phase == GameState.Phase.DAY or state.phase == GameState.Phase.SUNSET
	if _hog_t < 0.0 and b.hedgehog_day == state.day_number() and _hog_day != b.hedgehog_day and dusk and pile.visible:
		_hog_day = b.hedgehog_day
		_start_hog()
	if _hog_t < 0.0:
		return
	_hog_t += delta
	if _hog_t >= HOG_SECONDS or not dusk:
		_hog_t = -1.0
		hedgehog.visible = false
		return
	hedgehog.visible = true
	_pose_hog(_hog_t)


## Its evening's walk: out of the pile's clearing side, a few snuffling stops, back in.
func _start_hog() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([state.seed, "hedgehog", _hog_day])
	var door := Vector3(rng.randf_range(-0.5, 0.5), 0.0, BrushPile.HALF_DEPTH * 0.55)
	_hog_path = [door - Vector3(0, 0, 0.35), door]
	_hog_pauses = [0.0, 1.5]
	var at := door
	for _i in range(3):
		var next := at + Vector3(rng.randf_range(-0.9, 0.9), 0.0, rng.randf_range(0.2, 0.9))
		next.z = clampf(next.z, door.z + 0.3, door.z + HOG_REACH)
		next.x = clampf(next.x, -1.6, 1.6)
		_hog_path.append(next)
		_hog_pauses.append(rng.randf_range(2.0, 4.5))
		at = next
	_hog_path.append(door)
	_hog_pauses.append(0.8)
	_hog_path.append(door - Vector3(0, 0, 0.4))
	_hog_pauses.append(0.0)
	_hog_t = 0.0


## Where the hedgehog is at `t` seconds into its walk: walking between points at its pace,
## stopping at each to snuffle (nose down, a little side to side).
func _pose_hog(t: float) -> void:
	var clock := 0.0
	var pos := _hog_path[0]
	var heading := _hog_path[1] - _hog_path[0]
	var sniff := 0.0
	var walking := false
	for i in range(1, _hog_path.size()):
		var seg := _hog_path[i] - _hog_path[i - 1]
		var walk := seg.length() / HOG_SPEED
		if t < clock + walk:
			pos = _hog_path[i - 1] + seg * ((t - clock) / walk)
			heading = seg
			walking = true
			break
		clock += walk
		pos = _hog_path[i]
		heading = seg
		if t < clock + _hog_pauses[i]:
			sniff = 1.0
			break
		clock += _hog_pauses[i]
	var ground := to_local(Terrain.at(to_global(pos)))
	var yaw := atan2(heading.x, heading.z)
	var bob := absf(sin(t * 13.0)) * 0.006 if walking else 0.0
	var nose := (0.12 + 0.08 * sin(t * 9.0)) * sniff
	var sway := sin(t * 2.3) * 0.25 * sniff + sin(t * 13.0) * 0.04 * (1.0 if walking else 0.0)
	hedgehog.position = Vector3(pos.x, ground.y + bob, pos.z)
	hedgehog.basis = Basis(Vector3.UP, yaw + sway) * Basis(Vector3.RIGHT, nose)


## A hedgehog, about 28 cm from nose to rump: a round body under a coat of banded spines, a
## pointed brown face with a dark nose and eyes, small feet. Local frame: +z forward, y up.
static func hedgehog_mesh() -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var c := Vector3(0.0, 0.066, -0.01)
	var rad := Vector3(0.078, 0.056, 0.118)
	var fur := Color(0.38, 0.28, 0.2)
	var skin := Color(0.2, 0.15, 0.1)
	var rings := 10
	var segs := 14
	# The body: an ellipsoid flattened underneath; the spine coat's skin dark, the face and belly
	# in brown fur.
	var grid: Array = []
	for i in range(rings + 1):
		var th := PI * i / rings
		var row: Array = []
		for j in range(segs + 1):
			var ph := TAU * j / segs
			var n := Vector3(sin(th) * sin(ph), cos(th), sin(th) * cos(ph))
			var p := c + Vector3(n.x * rad.x, n.y * rad.y, n.z * rad.z)
			p.y = maxf(p.y, 0.022)
			var coat := n.y > -0.25 and n.z < 0.55
			row.append([p, skin if coat else fur])
		grid.append(row)
	for i in range(rings):
		for j in range(segs):
			for q: Array in [grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i][j], grid[i + 1][j + 1], grid[i + 1][j]]:
				st.set_color(q[1])
				st.add_vertex(q[0])
	# The spines: thin three-sided cones over the back and sides, lying back toward the rump,
	# dark at the root and pale at the tip.
	for _s in range(320):
		var th := acos(rng.randf_range(-0.2, 1.0))
		var ph := rng.randf() * TAU
		var n := Vector3(sin(th) * sin(ph), cos(th), sin(th) * cos(ph))
		if n.z > 0.5:
			continue
		var base := c + Vector3(n.x * rad.x, n.y * rad.y, n.z * rad.z) * 0.96
		var dir := (n + Vector3(0, 0.1, -0.9)).normalized()
		var length := rng.randf_range(0.022, 0.034)
		var tip := base + dir * length
		var side := dir.cross(Vector3.UP if absf(dir.y) < 0.95 else Vector3.RIGHT).normalized()
		var up := dir.cross(side)
		var w := 0.0035
		var b0 := base + side * w
		var b1 := base + (-side * 0.5 + up * 0.87) * w
		var b2 := base + (-side * 0.5 - up * 0.87) * w
		var root := Color(0.1, 0.075, 0.06)
		var pale := Color(0.78, 0.72, 0.6).lerp(Color(0.55, 0.47, 0.37), rng.randf())
		for tri: Array in [[b0, b1], [b1, b2], [b2, b0]]:
			st.set_color(root)
			st.add_vertex(tri[0])
			st.set_color(root)
			st.add_vertex(tri[1])
			st.set_color(pale)
			st.add_vertex(tip)
	# The face: a pointed snout, the nose a dark tip.
	var snout_base := c + Vector3(0.0, -0.012, rad.z * 0.8)
	var snout_tip := Vector3(0.0, 0.036, 0.168)
	_cone(st, snout_base, snout_tip, 0.03, 0.009, 8, fur, fur.darkened(0.2))
	_cone(st, snout_tip - (snout_tip - snout_base).normalized() * 0.004, snout_tip + (snout_tip - snout_base).normalized() * 0.008, 0.009, 0.002, 6, Color(0.04, 0.03, 0.03), Color(0.02, 0.02, 0.02))
	# Eyes: two small dark beads; ears: two small rounded bumps.
	for sx in [-1.0, 1.0]:
		_bead(st, Vector3(sx * 0.024, 0.066, 0.118), 0.0055, Color(0.02, 0.02, 0.02))
		_bead(st, Vector3(sx * 0.036, 0.09, 0.092), 0.008, fur.darkened(0.15))
	# Feet: small dark pads under the body, just showing.
	for fx in [-0.045, 0.045]:
		for fz in [-0.06, 0.06]:
			_cone(st, Vector3(fx, 0.03, fz), Vector3(fx, 0.0, fz + 0.01), 0.012, 0.009, 5, fur.darkened(0.3), fur.darkened(0.4))
	st.generate_normals()
	return st.commit()


static func _cone(st: SurfaceTool, a: Vector3, b: Vector3, r0: float, r1: float, sides: int, c0: Color, c1: Color) -> void:
	var ax := (b - a).normalized()
	var u := ax.cross(Vector3.UP if absf(ax.y) < 0.9 else Vector3.RIGHT).normalized()
	var v := ax.cross(u)
	for k in range(sides):
		var a0 := TAU * k / sides
		var a1 := TAU * (k + 1) / sides
		var o0 := u * cos(a0) + v * sin(a0)
		var o1 := u * cos(a1) + v * sin(a1)
		for q: Array in [[a + o0 * r0, c0], [b + o0 * r1, c1], [b + o1 * r1, c1], [a + o0 * r0, c0], [b + o1 * r1, c1], [a + o1 * r0, c0]]:
			st.set_color(q[1])
			st.add_vertex(q[0])
		# Close the narrow end.
		st.set_color(c1)
		st.add_vertex(b)
		st.set_color(c1)
		st.add_vertex(b + o1 * r1)
		st.set_color(c1)
		st.add_vertex(b + o0 * r1)


## A small octahedron (at this size it reads as a bead).
static func _bead(st: SurfaceTool, p: Vector3, r: float, col: Color) -> void:
	var dirs := [Vector3.RIGHT, Vector3.FORWARD, Vector3.LEFT, Vector3.BACK]
	for k in range(4):
		var d0: Vector3 = dirs[k] * r
		var d1: Vector3 = dirs[(k + 1) % 4] * r
		for tri: Array in [[p + Vector3.UP * r, p + d1, p + d0], [p + Vector3.DOWN * r, p + d0, p + d1]]:
			for q: Vector3 in tri:
				st.set_color(col)
				st.add_vertex(q)


# --- the wren ----------------------------------------------------------------------------

func _update_wren(delta: float) -> void:
	var b := state.brush
	var by_day := state.phase == GameState.Phase.DAY
	if _wren_t < 0.0 and b.wren_day == state.day_number() and _wren_day != b.wren_day and by_day and pile.visible:
		_wren_day = b.wren_day
		_wren_t = 0.0
		_songs_left = WREN_SONGS
	if _wren_t < 0.0:
		return
	_wren_t += delta
	if _wren_t >= WREN_SECONDS or not by_day:
		_wren_t = -1.0
		wren.visible = false
		_song.stop()
		return
	wren.visible = true
	# It sits on the top of the pile, tail cocked, and turns now and then; before each song it
	# lifts its head.
	var turn := floorf(_wren_t / 4.0)
	wren.position = _top + Vector3(0, 0.01, 0)
	var singing := _song.playing
	wren.basis = Basis(Vector3.UP, sin(turn * 2.4) * 1.2) * Basis(Vector3.RIGHT, -0.35 if singing else 0.0)
	var due := WREN_SECONDS * (1.0 - float(_songs_left) / (WREN_SONGS + 0.5))
	if _songs_left > 0 and not singing and _wren_t >= due:
		_songs_left -= 1
		_song.position = wren.position
		_song.play()


## A wren, about 10 cm: a round brown body, a short cocked tail, a fine beak. Frame: +z forward.
static func wren_mesh() -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var brown := Color(0.45, 0.31, 0.2)
	var pale := Color(0.62, 0.5, 0.38)
	var rings := 6
	var segs := 8
	for part: Array in [[Vector3(0, 0.035, 0), Vector3(0.024, 0.024, 0.036), brown, pale], [Vector3(0, 0.058, 0.026), Vector3(0.015, 0.015, 0.016), brown, pale]]:
		var c: Vector3 = part[0]
		var rad: Vector3 = part[1]
		var grid: Array = []
		for i in range(rings + 1):
			var th := PI * i / rings
			var row: Array = []
			for j in range(segs + 1):
				var ph := TAU * j / segs
				var n := Vector3(sin(th) * sin(ph), cos(th), sin(th) * cos(ph))
				row.append([c + Vector3(n.x * rad.x, n.y * rad.y, n.z * rad.z), part[2] if n.y > -0.2 else part[3]])
			grid.append(row)
		for i in range(rings):
			for j in range(segs):
				for q: Array in [grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i][j], grid[i + 1][j + 1], grid[i + 1][j]]:
					st.set_color(q[1])
					st.add_vertex(q[0])
	# The cocked tail and the fine beak.
	_cone(st, Vector3(0, 0.042, -0.028), Vector3(0, 0.082, -0.05), 0.011, 0.007, 5, brown.darkened(0.1), brown.darkened(0.25))
	_cone(st, Vector3(0, 0.058, 0.04), Vector3(0, 0.055, 0.056), 0.004, 0.0008, 5, Color(0.25, 0.2, 0.15), Color(0.2, 0.15, 0.1))
	_bead(st, Vector3(0.011, 0.063, 0.034), 0.0025, Color(0.03, 0.03, 0.03))
	_bead(st, Vector3(-0.011, 0.063, 0.034), 0.0025, Color(0.03, 0.03, 0.03))
	st.generate_normals()
	return st.commit()
