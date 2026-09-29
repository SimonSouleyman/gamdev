class_name Shed
extends Node3D
## The garden shed (Simon, play test 3; menus as whole scenes, like Plants vs. Zombies): the
## start menu and the place to pause. It stands at the south edge of the clearing with its door
## open toward the tree, so the player's real tree, grown so far, is the big picture in the doorway.
## 0.6 (design doc section 17, item 3): the workbench stands in the middle of the view and the
## things on it ARE the menu: the journal, the photo album, the seed bag, the flower pot with a
## seedling and a pair of garden gloves; the pinboard on the wall holds the options. Each answers
## a tap with a real sound and a small motion before its page opens. Beside the bench a small
## window with a sill waits for the bonsai (section 16): `bonsai_spot`.
## Real CC0 models from Poly Haven where they fit (workbench, gloves, clay pot, watering can,
## trowel; assets/CREDITS.md), the books and the seed bag built here from the real leather,
## paper and cloth textures, so they can open and rustle.

## The things that are menu entries, in the order of their labels.
## The bonsai on the windowsill is one too, once it is there (bonsai_ready).
const ITEMS: Array[String] = ["journal", "album", "seeds", "pot", "gloves", "options", "bonsai"]
## Seconds from the tap until the page opens: the motion and the sound come first.
const TAP_DELAY := 0.42

## Where the shed stands: the south edge of the clearing (behind the default view of the tree),
## the door facing north to the tree, which the sun lights from behind the shed.
## It moves out with the edge as the clearing grows.
static var origin := Vector3(0.0, 0.0, 15.5)
const WIDTH := 3.2
const DEPTH := 2.8
const WALL_H := 2.4
const DOOR_W := 0.8
const DOOR_H := 2.05
## The small window in the front wall, left of the door as seen from inside (local +x).
const WINDOW_X := Vector2(0.5, 0.78)
const WINDOW_Y := Vector2(1.2, 1.72)
const BENCH_Z := 0.4
const BENCH_TOP := 0.87

var camera: Camera3D
## Where the bonsai will stand: on the windowsill beside the workbench (design doc 16 A).
var bonsai_spot: Node3D
## The bonsai stands on the sill (unlocked): it answers a tap and carries a label.
var bonsai_ready: bool = false
var _items: Dictionary = {}  # name -> Node3D (the part that moves on a tap)
var _picks: Dictionary = {}  # name -> [Node3D centre, radius in metres]
var _tag_anchors: Dictionary = {}  # name -> [Node3D, below: bool] (where its label goes)
var _wood: Material
var _floor: Material
var _time: float = 0.0
var _lamp: OmniLight3D
var _lamp_base: float = 1.0
## 0 at night .. 1 by day: the window light follows the clock and the lantern takes over at night
## (0.6 review: the shed was as bright at night as by day). Set by main while in the shed.
var daylight: float = 1.0
var _window_sun: SpotLight3D
var _shaft: MeshInstance3D
var _sounds: Dictionary = {}  # name -> AudioStream
var _player: AudioStreamPlayer
var _busy: Dictionary = {}  # name -> Tween
## The wall boards while the room is built (one mesh).
var _boards: SurfaceTool
## Dust drifting in the window light by day.
var _dust: GPUParticles3D
var _blob_mat: StandardMaterial3D


func _ready() -> void:
	place()
	rotation.y = PI
	# Real weathered boards and a worn plank floor (CC0, Poly Haven; Simon, play test 4).
	_wood = _planks("weathered_planks", 0.55, Color(0.82, 0.78, 0.74))
	_floor = _planks("old_planks_02", 0.5, Color(0.75, 0.7, 0.66))
	_build_room()
	_build_window()
	_build_bench()
	_build_pinboard()
	_build_camera()
	_build_sounds()


## Stands the shed at the current edge of the clearing.
func place() -> void:
	position = Terrain.at(origin)


# --- materials -------------------------------------------------------------------------

func _box(size: Vector3, at: Vector3, mat: Material, parent: Node3D = self) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	m.mesh = b
	m.material_override = mat
	m.position = at
	parent.add_child(m)
	return m


## Board pitch of the walls, metres (each board a little wider or narrower).
const BOARD_PITCH := 0.165
const BOARD_GAP := 0.009


## A wall of vertical boards filling `size` at `at` (a box's size and centre, thin along x or z),
## with a dark backing behind the gaps. Boards follow one grid per wall (`wall`), so the pieces
## of the front wall around the door and window line up.
func _wall(size: Vector3, at: Vector3, wall: int) -> void:
	var along_x := size.x >= size.z
	var length := size.x if along_x else size.z
	var thick := size.z if along_x else size.x
	var start := (at.x if along_x else at.z) - length * 0.5
	var end := start + length
	# Inside is toward the middle of the room.
	var inward := -signf(at.z if along_x else at.x)
	var normal_axis := Vector3(0, 0, inward) if along_x else Vector3(inward, 0, 0)
	var back_size := Vector3(size.x, size.y, thick * 0.4) if along_x else Vector3(thick * 0.4, size.y, size.z)
	_st_box(_boards, at - normal_axis * thick * 0.3, back_size, Color(0.16, 0.13, 0.11))
	var first := floori(start / BOARD_PITCH) - 1
	var i := first
	while i * BOARD_PITCH + _jit(i, wall) < end:
		var a := maxf(start, i * BOARD_PITCH + _jit(i, wall))
		var b := minf(end, (i + 1) * BOARD_PITCH + _jit(i + 1, wall) - BOARD_GAP)
		i += 1
		if b - a < 0.008:
			continue
		var h := _hash(i, wall)
		var depth := thick * 0.55 + (h - 0.5) * 0.012
		var mid := (a + b) * 0.5
		var c := at + normal_axis * (thick * 0.5 - depth * 0.5)
		if along_x:
			c.x = mid
		else:
			c.z = mid
		var bsize := Vector3(b - a, size.y, depth) if along_x else Vector3(depth, size.y, b - a)
		# Each board weathered its own way: lighter, darker, a little warmer or greyer.
		var v := lerpf(0.78, 1.12, h)
		var warm := _hash(i + 17, wall + 5) - 0.5
		_st_box(_boards, c, bsize, Color(v * (1.0 + warm * 0.1), v, v * (1.0 - warm * 0.14)))


func _jit(i: int, wall: int) -> float:
	return (_hash(i, wall * 7 + 3) - 0.5) * 0.05


static func _hash(i: int, k: int) -> float:
	return fposmod(sin(i * 12.9898 + k * 78.233) * 43758.5453, 1.0)


## A box into a SurfaceTool with a flat colour.
static func _st_box(st: SurfaceTool, c: Vector3, size: Vector3, col: Color) -> void:
	var h := size * 0.5
	var faces := [
		[Vector3(1, 0, 0), Vector3(0, 1, 0), Vector3(0, 0, 1)], [Vector3(-1, 0, 0), Vector3(0, 0, 1), Vector3(0, 1, 0)],
		[Vector3(0, 1, 0), Vector3(0, 0, 1), Vector3(1, 0, 0)], [Vector3(0, -1, 0), Vector3(1, 0, 0), Vector3(0, 0, 1)],
		[Vector3(0, 0, 1), Vector3(1, 0, 0), Vector3(0, 1, 0)], [Vector3(0, 0, -1), Vector3(0, 1, 0), Vector3(1, 0, 0)]]
	for f in faces:
		var n: Vector3 = f[0]
		var u: Vector3 = f[1]
		var v: Vector3 = f[2]
		var o := c + n * h
		var du := u * h
		var dv := v * h
		var q := [o - du - dv, o + du - dv, o + du + dv, o - du + dv]
		for k in [0, 2, 1, 0, 3, 2]:
			st.set_color(col)
			st.set_normal(n)
			st.add_vertex(q[k])


## A frame member: a box of `size` at `at` with its long edges bevelled by `bevel`, so each edge
## catches a line of light instead of the flat slab it was.
func _beam(size: Vector3, at: Vector3, mat: Material, bevel: float = 0.012, parent: Node3D = self) -> MeshInstance3D:
	var ax := 0 if size.x >= size.y and size.x >= size.z else (1 if size.y >= size.z else 2)
	var u := (ax + 1) % 3
	var w := (ax + 2) % 3
	var a := size[u] * 0.5
	var b := size[w] * 0.5
	var c := minf(bevel, minf(a, b) * 0.8)
	var ring: Array[Vector2] = [Vector2(a - c, -b), Vector2(a, -b + c), Vector2(a, b - c), Vector2(a - c, b),
		Vector2(-a + c, b), Vector2(-a, b - c), Vector2(-a, -b + c), Vector2(-a + c, -b)]
	var l := size[ax] * 0.5
	var pt := func(p: Vector2, t: float) -> Vector3:
		var r := Vector3.ZERO
		r[ax] = t
		r[u] = p.x
		r[w] = p.y
		return r
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for k in range(8):
		var p0: Vector2 = ring[k]
		var p1: Vector2 = ring[(k + 1) % 8]
		var q: Array[Vector3] = [pt.call(p0, -l), pt.call(p1, -l), pt.call(p1, l), pt.call(p0, l)]
		for i in [0, 2, 1, 0, 3, 2]:
			st.add_vertex(q[i])
	for k in range(1, 7):
		for side in [-1.0, 1.0]:
			var tri: Array[Vector3] = [pt.call(ring[0], side * l), pt.call(ring[k], side * l), pt.call(ring[k + 1], side * l)]
			if side > 0.0:
				tri = [tri[0], tri[2], tri[1]]
			for v in tri:
				st.add_vertex(v)
	st.generate_normals()
	var m := MeshInstance3D.new()
	m.mesh = st.commit()
	m.material_override = mat
	m.position = at
	parent.add_child(m)
	return m


## A soft contact shadow: a dark blurred patch on the surface under a thing (cheap: one quad).
func _blob(at: Vector3, size: Vector2, turn: float, strength: float, parent: Node3D = self) -> void:
	if _blob_mat == null:
		var g := Gradient.new()
		g.set_color(0, Color(1, 1, 1, 1))
		g.set_color(1, Color(1, 1, 1, 0))
		g.add_point(0.45, Color(1, 1, 1, 0.55))
		var tex := GradientTexture2D.new()
		tex.gradient = g
		tex.fill = GradientTexture2D.FILL_RADIAL
		tex.fill_from = Vector2(0.5, 0.5)
		tex.fill_to = Vector2(0.5, 0.0)
		tex.width = 64
		tex.height = 64
		_blob_mat = StandardMaterial3D.new()
		_blob_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		_blob_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		_blob_mat.albedo_texture = tex
		_blob_mat.vertex_color_use_as_albedo = false
		_blob_mat.albedo_color = Color(0.05, 0.035, 0.02, 1.0)
		_blob_mat.disable_receive_shadows = true
	var q := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = size
	q.mesh = pm
	var mat := _blob_mat
	if strength != 1.0:
		mat = _blob_mat.duplicate() as StandardMaterial3D
		mat.albedo_color.a = strength
	q.material_override = mat
	q.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	q.position = at
	q.rotation.y = turn
	parent.add_child(q)


## A wood material from assets/wood, mapped in world space so every board has the same grain size.
func _planks(name: String, scale: float, tint: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	var base := "res://assets/wood/%s_%s_1k.jpg"
	m.albedo_texture = load(base % [name, "diff"])
	m.albedo_color = tint
	m.normal_enabled = true
	m.normal_texture = load(base % [name, "nor_gl"])
	m.roughness_texture = load(base % [name, "rough"])
	m.roughness = 1.0
	m.uv1_triplanar = true
	m.uv1_world_triplanar = true
	m.uv1_scale = Vector3.ONE * scale
	return m


## Leather or book cloth with its grain (the journal's cover normal map from the paper look).
func _leather(c: Color, grain: float = 9.0) -> StandardMaterial3D:
	var m := _mat(c, 0.62)
	m.normal_enabled = true
	m.normal_texture = preload("res://lookdev/paper/textures/leather_normal.png")
	m.normal_scale = 0.8
	m.uv1_triplanar = true
	m.uv1_scale = Vector3.ONE * grain
	return m


## Paper for page blocks, labels and the seed bag; `crumpled` adds the crumple normal map.
func _paper_mat(c: Color = Paper.PAPER, crumpled: bool = false, kind: String = "beige") -> StandardMaterial3D:
	var m := _mat(c, 0.95)
	m.albedo_texture = load("res://assets/paper/paper_%s.png" % kind)
	m.uv1_triplanar = true
	m.uv1_scale = Vector3.ONE * 3.0
	if crumpled:
		m.normal_enabled = true
		m.normal_texture = preload("res://lookdev/paper/textures/crumple_normal.png")
		m.normal_scale = 1.4
	return m


func _mat(c: Color, rough: float = 0.8) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	return m


## A real model from assets/shed (Poly Haven glTF, origin at its foot).
func _model(id: String, at: Vector3, scale_by: float, turn: float, parent: Node3D) -> Node3D:
	var n: Node3D = (load("res://assets/shed/%s/%s_1k.gltf" % [id, id]) as PackedScene).instantiate()
	n.position = at
	n.scale = Vector3.ONE * scale_by
	n.rotation.y = turn
	parent.add_child(n)
	return n


# --- the room --------------------------------------------------------------------------

func _build_room() -> void:
	var hw := WIDTH * 0.5
	var hd := DEPTH * 0.5
	var t := 0.08
	# Floor boards and walls: back, left, right.
	_box(Vector3(WIDTH, 0.1, DEPTH), Vector3(0, 0.05, 0), _floor)
	# The walls are single boards (0.6.1 review: one flat slab): each its own width, tint and
	# depth, with dark gaps between them. All boards are one mesh, one draw call.
	_boards = SurfaceTool.new()
	_boards.begin(Mesh.PRIMITIVE_TRIANGLES)
	_wall(Vector3(WIDTH, WALL_H, t), Vector3(0, WALL_H * 0.5, -hd), 0)
	_wall(Vector3(t, WALL_H, DEPTH), Vector3(-hw, WALL_H * 0.5, 0), 1)
	_wall(Vector3(t, WALL_H, DEPTH), Vector3(hw, WALL_H * 0.5, 0), 2)
	# The front wall around the open door (centred) and the window (local +x).
	var dx := DOOR_W * 0.5
	var right_w := hw - dx
	_wall(Vector3(right_w, WALL_H, t), Vector3(-dx - right_w * 0.5, WALL_H * 0.5, hd), 3)
	# Left of the door: beside, below and above the window opening.
	var wx0 := WINDOW_X.x
	var wx1 := WINDOW_X.y
	_wall(Vector3(wx0 - dx, WALL_H, t), Vector3((dx + wx0) * 0.5, WALL_H * 0.5, hd), 3)
	_wall(Vector3(hw - wx1, WALL_H, t), Vector3((wx1 + hw) * 0.5, WALL_H * 0.5, hd), 3)
	_wall(Vector3(wx1 - wx0, WINDOW_Y.x, t), Vector3((wx0 + wx1) * 0.5, WINDOW_Y.x * 0.5, hd), 3)
	_wall(Vector3(wx1 - wx0, WALL_H - WINDOW_Y.y, t), Vector3((wx0 + wx1) * 0.5, (WALL_H + WINDOW_Y.y) * 0.5, hd), 3)
	_wall(Vector3(DOOR_W, WALL_H - DOOR_H, t), Vector3(0, DOOR_H + (WALL_H - DOOR_H) * 0.5, hd), 3)
	var boards := MeshInstance3D.new()
	boards.name = "Boards"
	boards.mesh = _boards.commit()
	var bm := (_wood as StandardMaterial3D).duplicate() as StandardMaterial3D
	bm.vertex_color_use_as_albedo = true
	boards.material_override = bm
	add_child(boards)
	# Door frame: thick bevelled posts and lintel standing proud of the wall, and a thin casing
	# around them, so the doorway has depth.
	var frame := _planks("old_planks_02", 1.2, Color(0.52, 0.42, 0.34))
	for x in [-dx - 0.035, dx + 0.035]:
		_beam(Vector3(0.07, DOOR_H, 0.16), Vector3(x, DOOR_H * 0.5, hd), frame)
		_beam(Vector3(0.05, DOOR_H + 0.06, 0.025), Vector3(x + signf(x) * 0.055, DOOR_H * 0.5 + 0.03, hd - 0.09), frame)
	_beam(Vector3(DOOR_W + 0.14, 0.08, 0.16), Vector3(0, DOOR_H + 0.04, hd), frame)
	_beam(Vector3(DOOR_W + 0.26, 0.06, 0.025), Vector3(0, DOOR_H + 0.1, hd - 0.09), frame)
	# A worn threshold board.
	_beam(Vector3(DOOR_W + 0.1, 0.03, 0.2), Vector3(0, 0.115, hd), frame)
	var hinge := Node3D.new()
	hinge.position = Vector3(-dx, 0, hd + 0.05)
	hinge.rotation.y = -1.9
	add_child(hinge)
	_box(Vector3(DOOR_W, 2.0, 0.05), Vector3(DOOR_W * 0.5, 1.0, 0), _wood, hinge)
	_items["door"] = hinge
	_pick("door", Vector3(0, 1.25, hd), 0.5)
	# The gable ends above the front and back walls.
	for z in [-hd, hd]:
		var tri := MeshInstance3D.new()
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for v in [Vector3(-hw, WALL_H, z), Vector3(hw, WALL_H, z), Vector3(0, WALL_H + 0.72, z)]:
			st.add_vertex(v)
		st.generate_normals()
		tri.mesh = st.commit()
		tri.material_override = _wood
		add_child(tri)
		# Visible from both sides.
		var back := tri.duplicate() as MeshInstance3D
		back.scale = Vector3(-1, 1, 1)
		add_child(back)
	# Rafters, and a bunch of herbs drying from one.
	for x in [-1.0, 0.0, 1.0]:
		_box(Vector3(0.08, 0.1, DEPTH), Vector3(x, WALL_H + 0.05, 0), _mat(Color(0.3, 0.21, 0.13)))
	for k in range(3):
		var herb := _box(Vector3(0.1, 0.24, 0.04), Vector3(-1.0 + (k - 1) * 0.12, WALL_H - 0.16, -0.4), _mat(Color(0.35, 0.42, 0.2).lerp(Color(0.5, 0.45, 0.25), k * 0.3), 0.95))
		herb.rotation.z = (k - 1) * 0.15
	# Pitched roof.
	var roof := _mat(Color(0.3, 0.22, 0.17), 0.95)
	var r1 := _box(Vector3(WIDTH * 0.62, 0.06, DEPTH + 0.5), Vector3(-WIDTH * 0.26, WALL_H + 0.38, 0), roof)
	r1.rotation.z = 0.45
	var r2 := _box(Vector3(WIDTH * 0.62, 0.06, DEPTH + 0.5), Vector3(WIDTH * 0.26, WALL_H + 0.38, 0), roof)
	r2.rotation.z = -0.45
	# A warm lantern hanging from the beam.
	_lamp = OmniLight3D.new()
	_lamp.light_color = Color(1.0, 0.78, 0.5)
	_lamp.light_energy = 1.1
	_lamp.omni_range = 4.5
	# The phone's simpler renderer lights the room more dimly.
	if RenderingServer.get_current_rendering_method() == "gl_compatibility":
		_lamp_base = 2.0
		_lamp.omni_range = 5.5
	_lamp.position = Vector3(-0.3, WALL_H - 0.35, -0.2)
	add_child(_lamp)
	var glass := MeshInstance3D.new()
	var s := SphereMesh.new()
	s.radius = 0.05
	s.height = 0.12
	glass.mesh = s
	var gm := StandardMaterial3D.new()
	gm.albedo_color = Color(1.0, 0.85, 0.55)
	gm.emission_enabled = true
	gm.emission = Color(1.0, 0.75, 0.4)
	gm.emission_energy_multiplier = 3.0
	glass.material_override = gm
	glass.position = _lamp.position + Vector3(0, 0.05, 0)
	add_child(glass)
	_box(Vector3(0.01, 0.3, 0.01), glass.position + Vector3(0, 0.2, 0), _mat(Color(0.15, 0.12, 0.1)))


## The small window beside the workbench: a frame with a cross bar, old glass, a deep sill
## inside (empty, for the bonsai) and daylight falling in across the sill and the bench.
func _build_window() -> void:
	var hd := DEPTH * 0.5
	var cx := (WINDOW_X.x + WINDOW_X.y) * 0.5
	var cy := (WINDOW_Y.x + WINDOW_Y.y) * 0.5
	var w := WINDOW_X.y - WINDOW_X.x
	var h := WINDOW_Y.y - WINDOW_Y.x
	# A deep bevelled frame with a casing proud of the wall and thin glazing bars set back.
	var frame := _planks("old_planks_02", 1.4, Color(0.62, 0.5, 0.4))
	for y in [WINDOW_Y.x, WINDOW_Y.y]:
		_beam(Vector3(w + 0.1, 0.055, 0.14), Vector3(cx, y, hd), frame)
		_beam(Vector3(w + 0.2, 0.045, 0.022), Vector3(cx, y + signf(y - cy) * 0.035, hd - 0.08), frame)
	for x in [WINDOW_X.x, WINDOW_X.y]:
		_beam(Vector3(0.055, h, 0.14), Vector3(x, cy, hd), frame)
		_beam(Vector3(0.045, h + 0.12, 0.022), Vector3(x + signf(x - cx) * 0.035, cy, hd - 0.08), frame)
	_beam(Vector3(w, 0.026, 0.04), Vector3(cx, cy, hd + 0.01), frame, 0.006)
	_beam(Vector3(0.026, h, 0.04), Vector3(cx, cy, hd + 0.01), frame, 0.006)
	# Old, slightly dusty glass.
	var glass := StandardMaterial3D.new()
	glass.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	glass.albedo_color = Color(0.85, 0.9, 0.85, 0.12)
	glass.roughness = 0.15
	_box(Vector3(w, h, 0.005), Vector3(cx, cy, hd + 0.02), glass)
	# The sill: a deep worn board inside the window, reaching into the room far enough for the
	# bonsai's tools in a row before the pot (0.7), on two small brackets.
	var sill_top := WINDOW_Y.x + 0.01
	_box(Vector3(w + 0.24, 0.035, 0.44), Vector3(cx, sill_top - 0.0175, hd - 0.2), _planks("wood_table_worn", 0.9, Color(0.85, 0.8, 0.74)))
	for bx in [-0.16, 0.16]:
		_box(Vector3(0.035, 0.12, 0.3), Vector3(cx + bx, sill_top - 0.095, hd - 0.17), frame)
	bonsai_spot = Node3D.new()
	bonsai_spot.name = "bonsai_spot"
	bonsai_spot.position = Vector3(cx, sill_top, hd - 0.1)
	add_child(bonsai_spot)
	_pick("bonsai", bonsai_spot.position + Vector3(0.0, 0.2, -0.04), 0.17)
	var tag := Node3D.new()
	tag.position = bonsai_spot.position + Vector3(0.0, 0.08, -0.16)
	add_child(tag)
	_tag_anchors["bonsai"] = [tag, true]
	# Daylight through the window: a soft spot from outside, and a faint shaft of dusty air.
	var sun := SpotLight3D.new()
	sun.light_color = Color(1.0, 0.9, 0.72)
	sun.light_energy = 0.45
	sun.spot_range = 4.0
	sun.spot_angle = 34.0
	sun.spot_angle_attenuation = 0.5
	sun.spot_attenuation = 0.8
	sun.shadow_enabled = not Budgets.PHONE
	add_child(sun)
	_window_sun = sun
	sun.position = Vector3(cx + 0.25, cy + 0.9, hd + 1.1)
	sun.look_at(to_global(Vector3(cx - 0.3, BENCH_TOP, BENCH_Z + 0.2)), Vector3.UP)
	var shaft := MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(w * 0.95, 1.25)
	shaft.mesh = q
	var sm := ShaderMaterial.new()
	sm.shader = _shaft_shader()
	shaft.material_override = sm
	shaft.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(shaft)
	_shaft = shaft
	shaft.position = Vector3(cx - 0.05, cy - 0.28, hd - 0.42)
	shaft.rotation = Vector3(-0.55, PI, 0.0)
	_build_dust(Vector3(cx - 0.05, cy - 0.3, hd - 0.4), Vector3(w * 0.45, 0.5, 0.3))


## A little dust drifting slowly in the window light (only by day; few specks, cheap).
func _build_dust(at: Vector3, extents: Vector3) -> void:
	var p := GPUParticles3D.new()
	p.amount = 14 if Budgets.PHONE else 32
	p.lifetime = 7.0
	p.preprocess = 7.0
	p.position = at
	p.visibility_aabb = AABB(-extents * 1.5, extents * 3.0)
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = extents
	pm.gravity = Vector3(0, -0.004, 0)
	pm.direction = Vector3(0.3, 0.2, -0.2)
	pm.spread = 180.0
	pm.initial_velocity_min = 0.005
	pm.initial_velocity_max = 0.025
	pm.scale_min = 0.6
	pm.scale_max = 1.4
	var fade := Gradient.new()
	fade.set_color(0, Color(1, 1, 1, 0))
	fade.set_color(1, Color(1, 1, 1, 0))
	fade.add_point(0.3, Color(1, 1, 1, 1))
	fade.add_point(0.7, Color(1, 1, 1, 1))
	var ft := GradientTexture1D.new()
	ft.gradient = fade
	pm.color_ramp = ft
	p.process_material = pm
	var q := QuadMesh.new()
	q.size = Vector2(0.006, 0.006)
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	m.vertex_color_use_as_albedo = true
	m.albedo_color = Color(1.0, 0.9, 0.7, 0.55)
	m.albedo_texture = _blob_texture_soft()
	q.material = m
	p.draw_pass_1 = q
	p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(p)
	_dust = p


static func _blob_texture_soft() -> GradientTexture2D:
	var g := Gradient.new()
	g.set_color(0, Color(1, 1, 1, 1))
	g.set_color(1, Color(1, 1, 1, 0))
	var tex := GradientTexture2D.new()
	tex.gradient = g
	tex.fill = GradientTexture2D.FILL_RADIAL
	tex.fill_from = Vector2(0.5, 0.5)
	tex.fill_to = Vector2(0.5, 0.0)
	tex.width = 16
	tex.height = 16
	return tex


static func _shaft_shader() -> Shader:
	var s := Shader.new()
	s.code = """
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
void fragment() {
	// Brightest at the window (top), fading into the room and toward the sides.
	float side = smoothstep(0.0, 0.3, UV.x) * smoothstep(1.0, 0.7, UV.x);
	float a = (1.0 - UV.y) * side * 0.12;
	ALBEDO = vec3(1.0, 0.92, 0.75) * a;
}
"""
	return s


# --- the workbench and the things on it -------------------------------------------------

func _build_bench() -> void:
	var bench := Node3D.new()
	bench.name = "Workbench"
	bench.position = Vector3(0.0, 0.05, BENCH_Z)
	add_child(bench)
	# An old cabinet workbench with drawers (Poly Haven "Wooden Table 03"), its drawers toward us.
	var table := _model("WoodenTable_03", Vector3.ZERO, 1.0, PI, bench)
	table.scale = Vector3(0.95, 1.0, 1.05)
	var top := BENCH_TOP - 0.05  # bench-local height of the table top
	# The journal: dark leather with an elastic band and a red ribbon, lying on the left.
	var journal := _book(Vector3(0.16, 0.026, 0.22), _leather(Paper.LEATHER), Color(0.92, 0.88, 0.78), true)
	journal.position = Vector3(0.2, top, -0.08)
	journal.rotation.y = 0.25
	bench.add_child(journal)
	_register("journal", journal, Vector3(0, 0.02, 0), 0.12)
	_tag("journal", journal, Vector3(0.0, 0.0, -0.08), true)
	# The photo album: bigger, green cloth over boards, a paper label on the cover.
	var album := _book(Vector3(0.28, 0.045, 0.22), _leather(Color(0.2, 0.3, 0.19), 6.0), Color(0.78, 0.7, 0.58), false)
	album.position = Vector3(-0.04, top, 0.13)
	album.rotation.y = -0.12
	bench.add_child(album)
	_register("album", album, Vector3(0, 0.03, 0), 0.14)
	_tag("album", album, Vector3(0.0, 0.06, 0.1), false)
	# The seed bag: a kraft paper sack, the top rolled over, a few seeds spilled beside it.
	var seeds := _seed_bag()
	seeds.position = Vector3(-0.25, top, -0.04)
	seeds.rotation.y = -0.35
	bench.add_child(seeds)
	_register("seeds", seeds, Vector3(0, 0.09, 0), 0.1)
	_tag("seeds", seeds, Vector3(0.0, 0.2, 0.0), false)
	# The flower pot (Poly Haven clay pot) with a seedling: the player's tree, small.
	var pot := _flower_pot()
	pot.position = Vector3(0.3, top, 0.17)
	bench.add_child(pot)
	_register("pot", pot, Vector3(0, 0.1, 0), 0.1)
	_tag("pot", pot, Vector3(0.0, 0.22, 0.0), false)
	# Garden gloves lying at the front edge: put them on and go outside.
	var gloves := Node3D.new()
	gloves.position = Vector3(-0.03, top, -0.17)
	gloves.rotation.y = 1.45
	bench.add_child(gloves)
	_model("garden_gloves_01", Vector3.ZERO, 0.75, 0.0, gloves)
	_register("gloves", gloves, Vector3(0, 0.03, 0), 0.1)
	_tag("gloves", gloves, Vector3(0.0, 0.0, -0.04), true)
	# A trowel for the feel of the place, and a watering can on the floor under the window.
	var trowel := _model("trowel_01", Vector3(-0.36, top + 0.035, 0.2), 0.75, 0.0, bench)
	trowel.rotation = Vector3(PI * 0.5 - 0.02, -0.6, 0.0)
	_model("watering_can_metal_01", Vector3(0.78, 0.05, DEPTH * 0.5 - 0.3), 1.1, 2.4, self)
	# Contact shadows (0.6.1 review: the things floated): a soft dark patch under each thing on
	# the bench, under the bench and the can on the floor, and along the foot of the walls.
	var y := top + 0.004
	_blob(Vector3(0.2, y, -0.08), Vector2(0.24, 0.3), 0.25, 0.8, bench)
	_blob(Vector3(-0.04, y, 0.13), Vector2(0.38, 0.31), -0.12, 0.8, bench)
	_blob(Vector3(-0.25, y, -0.04), Vector2(0.2, 0.15), -0.35, 0.9, bench)
	_blob(Vector3(0.3, y, 0.17), Vector2(0.2, 0.2), 0.0, 0.9, bench)
	_blob(Vector3(-0.03, y, -0.17), Vector2(0.28, 0.2), 1.45, 0.6, bench)
	_blob(Vector3(-0.36, y, 0.2), Vector2(0.24, 0.1), -0.6, 0.6, bench)
	var floor_y := 0.1015
	_blob(Vector3(0.0, floor_y, BENCH_Z), Vector2(1.9, 1.1), 0.0, 0.85)
	_blob(Vector3(0.78, floor_y, DEPTH * 0.5 - 0.3), Vector2(0.45, 0.45), 0.0, 0.8)
	for side in [-1.0, 1.0]:
		_blob(Vector3(side * (WIDTH * 0.5 - 0.05), floor_y, 0.0), Vector2(0.5, DEPTH * 1.1), 0.0, 0.5)
	_blob(Vector3(0.0, floor_y, -DEPTH * 0.5 + 0.05), Vector2(WIDTH * 1.1, 0.5), 0.0, 0.5)


func _register(name: String, node: Node3D, centre: Vector3, radius: float) -> void:
	_items[name] = node
	var c := Node3D.new()
	c.position = centre
	node.add_child(c)
	_picks[name] = [c, radius]


## Where a thing's handwritten label goes: `offset` from the thing in its parent's space,
## the label above that point or (`below`) hanging under it.
func _tag(name: String, node: Node3D, offset: Vector3, below: bool) -> void:
	var a := Node3D.new()
	a.position = node.position + offset
	node.get_parent().add_child(a)
	_tag_anchors[name] = [a, below]


func _pick(name: String, at: Vector3, radius: float) -> void:
	var c := Node3D.new()
	c.position = at
	add_child(c)
	_picks[name] = [c, radius]


## A book lying closed: back board, page block, rounded spine and a front board on a hinge
## ("Cover") that a tap opens a little. `band`: an elastic band and a ribbon, like a notebook;
## otherwise a paper label and brass corners, like an album.
func _book(size: Vector3, cover: Material, page_tint: Color, band: bool) -> Node3D:
	var book := Node3D.new()
	var t := 0.004
	var pages := _paper_mat(page_tint, false, "cream")
	_box(Vector3(size.x, t, size.z), Vector3(0, t * 0.5, 0), cover, book)
	_box(Vector3(size.x - 0.01, size.y - t * 2.0, size.z - 0.012), Vector3(0.004, size.y * 0.5, 0), pages, book)
	var spine := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = size.y * 0.5
	cyl.bottom_radius = size.y * 0.5
	cyl.height = size.z
	cyl.radial_segments = 12
	spine.mesh = cyl
	spine.material_override = cover
	spine.rotation.x = PI * 0.5
	spine.position = Vector3(-size.x * 0.5, size.y * 0.5, 0)
	spine.scale = Vector3(0.45, 1.0, 1.0)
	book.add_child(spine)
	var hinge := Node3D.new()
	hinge.name = "Cover"
	hinge.position = Vector3(-size.x * 0.5, size.y - t * 0.5, 0)
	book.add_child(hinge)
	_box(Vector3(size.x, t, size.z), Vector3(size.x * 0.5, 0, 0), cover, hinge)
	if band:
		var elastic := _mat(Color(0.12, 0.08, 0.06), 0.5)
		_box(Vector3(0.008, size.y + 0.006, size.z + 0.004), Vector3(size.x * 0.32, size.y * 0.5, 0), elastic, book)
		_box(Vector3(0.012, 0.003, 0.07), Vector3(0.02, 0.004, size.z * 0.5 + 0.03), _mat(Color(0.62, 0.12, 0.1), 0.6), book)
	else:
		var label := _box(Vector3(size.x * 0.45, 0.002, size.z * 0.28), Vector3(size.x * 0.55, t * 0.5 + 0.001, 0), _paper_mat(Color(0.9, 0.85, 0.72)), hinge)
		var text := _ink_text("photos", 0.028)
		text.position = Vector3(0, 0.002, 0)
		text.rotation = Vector3(-PI * 0.5, PI * 0.5, 0)
		label.add_child(text)
		var brass := _mat(Color(0.62, 0.48, 0.24), 0.35)
		brass.metallic = 0.8
		for k in [-1, 1]:
			var corner := _box(Vector3(0.03, t + 0.002, 0.03), Vector3(size.x - 0.012, 0, k * (size.z * 0.5 - 0.012)), brass, hinge)
			corner.rotation.y = PI * 0.25
	return book


## Handwriting in ink on a 3D surface (the album's label, the seed bag's note, the pinboard).
func _ink_text(text: String, height: float) -> Label3D:
	var l := Label3D.new()
	l.text = text
	l.font = Paper.hand_font(true)
	l.font_size = 64
	l.pixel_size = height / 64.0
	l.modulate = Paper.INK
	l.outline_size = 0
	l.shaded = true
	l.alpha_cut = Label3D.ALPHA_CUT_OPAQUE_PREPASS
	return l


## A kraft paper seed sack with its top rolled over, a handwritten note and a few seeds.
func _seed_bag() -> Node3D:
	var bag := Node3D.new()
	var kraft := _paper_mat(Color(0.8, 0.64, 0.44), true)
	var body := Node3D.new()
	body.name = "Body"
	bag.add_child(body)
	# Slightly bulging: the bag and its fuller middle.
	_box(Vector3(0.12, 0.13, 0.075), Vector3(0, 0.065, 0), kraft, body)
	var belly := _box(Vector3(0.126, 0.07, 0.085), Vector3(0, 0.07, 0), kraft, body)
	belly.rotation.y = 0.03
	var fold := Node3D.new()
	fold.name = "Fold"
	fold.position = Vector3(0, 0.13, 0)
	body.add_child(fold)
	var roll := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.016
	cyl.bottom_radius = 0.016
	cyl.height = 0.122
	cyl.radial_segments = 10
	roll.mesh = cyl
	roll.material_override = _paper_mat(Color(0.74, 0.58, 0.38), true)
	roll.rotation.z = PI * 0.5
	roll.position = Vector3(0, 0.012, -0.012)
	fold.add_child(roll)
	# A paper note glued on the front with a word in ink.
	var note := _box(Vector3(0.075, 0.05, 0.002), Vector3(0.0, 0.07, -0.044), _paper_mat(Paper.PAPER), body)
	note.rotation.z = 0.06
	var text := _ink_text("seeds", 0.022)
	text.position = Vector3(0, 0, -0.0015)
	text.rotation.y = PI
	note.add_child(text)
	# A few seeds spilled on the bench.
	var seed_mat := _mat(Color(0.36, 0.25, 0.14), 0.6)
	var rng := RandomNumberGenerator.new()
	rng.seed = 11
	for k in range(5):
		var s := MeshInstance3D.new()
		var sp := SphereMesh.new()
		sp.radius = 0.006
		sp.height = 0.008
		sp.radial_segments = 6
		sp.rings = 3
		s.mesh = sp
		s.material_override = seed_mat
		s.position = Vector3(rng.randf_range(-0.09, 0.02), 0.003, rng.randf_range(-0.1, -0.06))
		s.scale = Vector3(1.0, 1.0, 1.5)
		s.rotation.y = rng.randf() * TAU
		bag.add_child(s)
	return bag


## The clay pot (Poly Haven "Planter Pot Clay") with dark soil and a seedling with a few leaves.
func _flower_pot() -> Node3D:
	var pot := Node3D.new()
	_model("planter_pot_clay", Vector3.ZERO, 0.52, 0.0, pot)
	var soil := MeshInstance3D.new()
	var disc := CylinderMesh.new()
	disc.top_radius = 0.058
	disc.bottom_radius = 0.058
	disc.height = 0.01
	soil.mesh = disc
	var sm := _mat(Color(0.17, 0.12, 0.09), 1.0)
	sm.normal_enabled = true
	sm.normal_texture = preload("res://lookdev/paper/textures/crumple_normal.png")
	sm.uv1_scale = Vector3.ONE * 2.0
	soil.material_override = sm
	soil.position = Vector3(0, 0.1, 0)
	pot.add_child(soil)
	var stem := MeshInstance3D.new()
	var sc := CylinderMesh.new()
	sc.top_radius = 0.0018
	sc.bottom_radius = 0.003
	sc.height = 0.09
	sc.radial_segments = 6
	stem.mesh = sc
	stem.material_override = _mat(Color(0.4, 0.3, 0.18), 0.8)
	stem.position = Vector3(0, 0.145, 0)
	stem.rotation.z = 0.06
	pot.add_child(stem)
	var leaf_mat := _mat(Color(0.3, 0.5, 0.2), 0.6)
	leaf_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	for k in range(6):
		var leaf := MeshInstance3D.new()
		var sp := SphereMesh.new()
		sp.radius = 0.024
		sp.height = 0.01
		sp.radial_segments = 8
		sp.rings = 4
		leaf.mesh = sp
		leaf.material_override = leaf_mat
		var a := k * 2.4
		leaf.position = Vector3(cos(a) * 0.026, 0.13 + k * 0.012, sin(a) * 0.026)
		leaf.scale = Vector3(1.0, 1.0, 0.65)
		leaf.rotation = Vector3(0.35 * sin(a), -a, 0.35 * cos(a))
		pot.add_child(leaf)
	return pot


## The pinboard on the front wall, right of the door: cork in a wooden frame, the option notes
## pinned on it and "options" written on a strip below them.
func _build_pinboard() -> void:
	var hd := DEPTH * 0.5
	var board := Node3D.new()
	board.position = Vector3(-0.63, 1.45, hd - 0.06)
	board.rotation.y = PI
	board.scale = Vector3.ONE * 0.72
	add_child(board)
	var cork := _mat(Color(0.62, 0.46, 0.3), 0.95)
	cork.normal_enabled = true
	cork.normal_texture = preload("res://lookdev/paper/textures/leather_normal.png")
	cork.uv1_scale = Vector3.ONE * 3.0
	_box(Vector3(0.46, 0.56, 0.025), Vector3.ZERO, cork, board)
	var frame := _mat(Color(0.36, 0.26, 0.17), 0.85)
	for y in [-0.29, 0.29]:
		_box(Vector3(0.5, 0.03, 0.035), Vector3(0, y, 0), frame, board)
	for x in [-0.24, 0.24]:
		_box(Vector3(0.03, 0.6, 0.035), Vector3(x, 0, 0), frame, board)
	var pin := _mat(Color(0.7, 0.15, 0.12), 0.4)
	for i in range(5):
		var small := i < 4
		var size := Vector3(0.16, 0.13, 0.002) if small else Vector3(0.3, 0.08, 0.002)
		var at := Vector3(-0.1 + (i % 2) * 0.2, 0.17 - (i / 2) * 0.17, 0.016) if small else Vector3(0.0, -0.19, 0.016)
		var note := _box(size, at, _paper_mat(Paper.PAPER), board)
		note.rotation.z = 0.05 * (i - 2)
		note.name = "Note%d" % i
		_box(Vector3(0.014, 0.014, 0.012), Vector3(0, size.y * 0.35, 0.006), pin, note)
		if small:
			# A few lines of ink on each note.
			for l in range(3):
				_box(Vector3(0.1 - l * 0.02, 0.004, 0.001), Vector3(-0.01, 0.01 - l * 0.025, 0.0015), _mat(Paper.FAINT_INK, 0.9), note)
	var title := _ink_text("options", 0.05)
	title.position = Vector3(0.0, -0.19, 0.019)
	board.add_child(title)
	_register("options", board, Vector3.ZERO, 0.3)
	_tag("options", board, Vector3(0.0, 0.24, 0.0), false)


func _build_camera() -> void:
	camera = Camera3D.new()
	camera.fov = 70.0
	camera.near = 0.03
	camera.far = 400.0
	add_child(camera)
	_place_camera(3.0)


## The eye stands at the back of the shed: the workbench in the middle of the view, the tree
## in the doorway above it, the window with its sill on the left and the pinboard on the right.
func _place_camera(_tree_height: float) -> void:
	camera.position = Vector3(0.0, 1.55, -0.7)
	camera.look_at(to_global(Vector3(0.0, 1.03, DEPTH * 0.5)), Vector3.UP)


func frame_tree(tree_height: float, env: Environment) -> void:
	_place_camera(tree_height)
	camera.environment = env


func _process(delta: float) -> void:
	_time += delta
	# The lantern flickers a little.
	# By night the lantern is the room's light; by day it is only a warm touch.
	# (The phone's renderer lit the bench too brightly by day: a softer lamp there by day.)
	var lamp := (lerpf(2.2, 1.0, daylight) if _lamp_base <= 1.0 else lerpf(1.5, 0.6, daylight)) * _lamp_base
	_lamp.light_energy = lamp * (1.0 + 0.1 * sin(_time * 7.3) + 0.05 * sin(_time * 13.1))
	if _window_sun:
		_window_sun.light_energy = 0.45 * daylight
		_window_sun.light_color = Color(0.7, 0.78, 1.0).lerp(Color(1.0, 0.9, 0.72), daylight)
	if _dust:
		_dust.visible = daylight > 0.15
	if _shaft:
		_shaft.visible = daylight > 0.05
		_shaft.transparency = 1.0 - daylight


# --- taps -----------------------------------------------------------------------------

## Which thing is at this screen point: one of ITEMS, "door" (go outside) or "".
## Each thing is a sphere around its centre; the nearest one relative to its size wins,
## the doorway only when no thing on the bench or wall is hit.
func item_at(screen: Vector2) -> String:
	var best := ""
	var best_score := 1.0
	for k in _picks:
		var at := (_picks[k][0] as Node3D).global_position
		if camera.is_position_behind(at) or (k == "bonsai" and not bonsai_ready):
			continue
		var r := _screen_radius(at, float(_picks[k][1]))
		var score := camera.unproject_position(at).distance_to(screen) / maxf(r, 1.0)
		if k == "door":
			score *= 1.6
		if score < best_score:
			best_score = score
			best = k
	return best


func _screen_radius(at: Vector3, radius: float) -> float:
	var side := camera.global_transform.basis.x * radius
	return camera.unproject_position(at).distance_to(camera.unproject_position(at + side))


## Where the handwritten label of a thing goes on screen, and whether it hangs below that point.
func item_tag_position(name: String) -> Vector2:
	return camera.unproject_position((_tag_anchors[name][0] as Node3D).global_position)


func item_tag_below(name: String) -> bool:
	return bool(_tag_anchors[name][1])


func _build_sounds() -> void:
	_player = AudioStreamPlayer.new()
	_player.volume_db = -4.0
	add_child(_player)
	var files := {"journal": "shed_book_open.ogg", "album": "shed_book_flip.ogg", "seeds": "shed_paper_bag.wav",
		"pot": "shed_clay_pot.ogg", "gloves": "shed_gloves.ogg", "options": "shed_pin.ogg", "door": "shed_door.ogg",
		"bonsai": "shed_clay_pot.ogg"}
	for k in files:
		var path := "res://assets/sounds/" + str(files[k])
		if ResourceLoader.exists(path):
			_sounds[k] = load(path)


## A thing answers a tap: its sound and a small motion (the book lifts and opens a little, the
## bag rustles, the pot wobbles). Returns the seconds to wait before its page opens.
func tap(name: String) -> float:
	if _sounds.has(name):
		_player.stream = _sounds[name]
		_player.pitch_scale = 0.96 + 0.08 * fposmod(_time * 7.0, 1.0)
		_player.play()
	var node: Node3D = _items.get(name)
	if node == null:
		return TAP_DELAY
	if _busy.has(name) and (_busy[name] as Tween).is_valid():
		(_busy[name] as Tween).kill()
	var rest: Transform3D = node.get_meta("rest", node.transform)
	node.set_meta("rest", rest)
	node.transform = rest
	var turn := rest.basis.get_euler().y
	var tw := create_tween()
	_busy[name] = tw
	match name:
		"journal", "album":
			var cover := node.get_node("Cover") as Node3D
			var open := 0.55 if name == "journal" else 0.4
			tw.tween_property(node, "position:y", rest.origin.y + 0.035, 0.14).set_trans(Tween.TRANS_SINE)
			tw.parallel().tween_property(cover, "rotation:z", open, 0.3).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
			tw.tween_interval(0.6)
			tw.tween_property(cover, "rotation:z", 0.0, 0.3)
			tw.parallel().tween_property(node, "position:y", rest.origin.y, 0.3)
		"seeds":
			var body := node.get_node("Body") as Node3D
			var fold := body.get_node("Fold") as Node3D
			for k in range(5):
				var s := 1.0 if k % 2 == 0 else -1.0
				tw.tween_property(body, "rotation:z", s * 0.07 * (1.0 - k * 0.18), 0.06)
				tw.parallel().tween_property(fold, "rotation:x", s * 0.25, 0.06)
			tw.tween_property(body, "rotation:z", 0.0, 0.08)
			tw.parallel().tween_property(fold, "rotation:x", 0.0, 0.08)
		"pot":
			for k in range(6):
				var s := 1.0 if k % 2 == 0 else -1.0
				tw.tween_property(node, "rotation:z", s * 0.12 * pow(0.6, k), 0.07)
			tw.tween_property(node, "rotation:z", 0.0, 0.06)
		"gloves":
			tw.tween_property(node, "position", rest.origin + Vector3(0.02, 0.04, 0.0), 0.15).set_trans(Tween.TRANS_SINE)
			tw.parallel().tween_property(node, "rotation:y", turn + 0.15, 0.15)
			tw.tween_property(node, "position", rest.origin, 0.2)
			tw.parallel().tween_property(node, "rotation:y", turn, 0.2)
		"options":
			for i in range(5):
				var note := node.get_node("Note%d" % i) as Node3D
				tw.parallel().tween_property(note, "rotation:x", -0.3, 0.1).set_delay(i * 0.03)
			tw.tween_interval(0.05)
			for i in range(5):
				tw.parallel().tween_property(node.get_node("Note%d" % i), "rotation:x", 0.0, 0.25)
		"door":
			tw.tween_property(node, "rotation:y", turn + 0.2, 0.25).set_trans(Tween.TRANS_SINE)
			tw.tween_property(node, "rotation:y", turn, 0.4)
	return TAP_DELAY
