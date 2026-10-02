class_name Shed
extends Node3D
## The garden shed (Simon, play test 3; menus as whole scenes, like Plants vs. Zombies): the
## start menu and the place to pause. It stands at the south edge of the clearing with its door
## open toward the tree, so the player's real tree, grown so far, is the big picture in the doorway.
## 0.6 (design doc section 17, item 3): the workbench stands in the middle of the view and the
## things on it ARE the menu: the journal, the photo album, the seed bag and a pair of garden
## gloves; the pinboard (0.8.2.5: on the open door's inside) holds the options (0.8.1: the
## flower pot with the seedling left the bench, its page is the journal's first). Each answers a
## tap with a real sound and a small motion before its page opens. Beside the bench a small
## window with a sill waits for the bonsai (section 16): `bonsai_spot`.
## Real CC0 models from Poly Haven where they fit (workbench, gloves, clay pot, watering can,
## trowel; assets/CREDITS.md), the books and the seed bag built here from the real leather,
## paper and cloth textures, so they can open and rustle.

## The things that are menu entries, in the order of their labels.
## The bonsai on the windowsill is one too, once it is there (bonsai_ready).
## (0.8.1, item 28: the flower pot left the bench; the tree's page is the journal's first page.)
const ITEMS: Array[String] = ["journal", "album", "seeds", "gloves", "options", "bonsai"]
## Seconds from the tap until the page opens: the motion and the sound come first.
const TAP_DELAY := 0.42

## Where the shed stands: the south edge of the clearing (behind the default view of the tree),
## the door facing north to the tree, which the sun lights from behind the shed.
## It moves out with the edge as the clearing grows.
static var origin := Vector3(0.0, 0.0, 15.5)
const WIDTH := 3.2
const DEPTH := 2.8
const WALL_H := 2.4
## 0.8.2.5, the shed's layout (Simon picked proposal 08 of ten, "Pinnwand an der offenen Tür,
## Fensterbank rechts, Werkbank unten"): the door a little left of the middle (seen from inside),
## its leaf open into the room with the pinboard on its inside; the window with the sill right of
## the doorway; the workbench low across the picture's foot.
## The door's middle along the front wall (shed frame x; +x is the screen's left) and its size.
const DOOR_X := 0.18
const DOOR_W := 0.78
const DOOR_H := 2.05
## The leaf hangs on the doorway's +x post and stands turned this far into the room.
const DOOR_TURN := -0.86
## The small window in the front wall, right of the door as seen from inside (local -x).
const WINDOW_X := Vector2(-0.8, -0.5)
const WINDOW_Y := Vector2(1.18, 1.7)
## The workbench's foot (shed frame) and its middle's z.
const BENCH_AT := Vector3(0.0, 0.05, -0.05)
const BENCH_Z := -0.05
## The pinboard on the door leaf (in the leaf's frame: x along the leaf from the hinge, z out of
## its inside face) and its size.
const PIN_AT := Vector3(-0.4, 1.38, -0.045)
const PINBOARD_SCALE := 0.74
const BENCH_TOP := 0.87
## The things on the bench (bench frame, on its top; +x is screen left): name -> [position, turn].
## 0.8.2.7 (Simon: not centred on the phone): spread round the middle of the table's visible
## part, two at the back and two in front; each is set down so its lowest point lies on the top.
const THINGS := {
	"journal": [Vector3(0.16, 0.0, -0.19), 0.15], "gloves": [Vector3(-0.23, 0.0, -0.17), 1.4],
	"album": [Vector3(0.06, 0.0, 0.12), -0.08], "seeds": [Vector3(-0.37, 0.0, 0.1), -0.3]}
## The things on the bench (in THINGS) that rest on its top.
const ON_TOP: Array[String] = ["journal", "album", "seeds", "gloves"]
## The lantern hanging from the roof (shed frame).
const LAMP_AT := Vector3(-0.3, 2.06, -0.6)

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
## The lantern's reach by day (its night pool is smaller).
var _lamp_range: float = 4.5
## The window's old glass.
var _glass: StandardMaterial3D
## 0 at night .. 1 by day: the window light follows the clock and the lantern takes over at night
## (0.6 review: the shed was as bright at night as by day). Set by main while in the shed.
var daylight: float = 1.0
var _window_sun: SpotLight3D
## 0.8.2.4 (Simon: "a small lamp over the bonsai so you can work on it at night"): a small enamel
## shade on a wall bracket above the window, its warm spot a pool on the bonsai by night only.
var bonsai_lamp: SpotLight3D
var _bonsai_bulb: StandardMaterial3D
## The bonsai lamp's energy at full night (the phone's renderer is dimmer: BONSAI_LAMP_PHONE).
const BONSAI_LAMP_ENERGY := 1.0
const BONSAI_LAMP_PHONE := 1.4
## The bench's drawers (0.8.2.4, Simon: "if the drawers are visible, they should open"): name ->
## the drawer node of the workbench model; a tap slides one out or back.
var drawers: Dictionary = {}
var _drawer_open: Dictionary = {}  # name -> bool
## How far a drawer slides out (m; the drawers are 0.44 deep). 0.8.2.6: further, so the finds
## lying in it show (0.24 before).
const DRAWER_OUT := 0.32
const DRAWER_TIME := 0.38
## 0.8.2.6, the finds (specs/journal-drawers-loop.md D1): each drawer's things, and the view
## leaning over the open drawer so they read on a phone. A tap on a find shows its note.
## Each shown find: {"node": Node3D, "drawer": key, "item": Dictionary (Finds.list()'s entry)}.
var _finds_shown: Array = []
var _finds_key: Array = []
## Where each kind lies in its drawer (the Contents node's frame: x across, z toward the room,
## its floor about 0.3 by 0.37) and its turn.
const FIND_SLOTS := {
	"fossil": [Vector3(-0.072, 0.0, -0.115), 0.3], "coin": [Vector3(0.075, 0.0, -0.115), 0.0],
	"old_root": [Vector3(-0.07, 0.0, 0.035), -0.12], "water_vein": [Vector3(0.075, 0.0, 0.035), 0.5],
	"map_scrap": [Vector3(-0.072, 0.0, -0.04), 0.12], "shard": [Vector3(0.075, 0.0, -0.04), 1.45]}
## At most this many of one kind lie in its place (more stay listed in the journal).
const FIND_COPIES := 3
## The lean over an open drawer: 0 the room's view .. 1 looking down into it.
var _lean: float = 0.0
var _lean_key: String = ""
var _lean_xf := Transform3D()
var _lean_fov: float = 60.0
var _lean_tween: Tween
var _fit_xf := Transform3D()
var _fit_fov: float = 70.0
const LEAN_TIME := 0.55
const LEAN_FOV := 58.0
## Half the width (m) of what the lean keeps on screen across, and half its depth.
const LEAN_HALF := Vector2(0.2, 0.17)
var _note_layer: CanvasLayer
var _note: PaperNote
var _note_find: int = -1
var _shaft: MeshInstance3D
var _sounds: Dictionary = {}  # name -> AudioStream
var _player: AudioStreamPlayer
var _busy: Dictionary = {}  # name -> Tween
## The wall boards while the room is built (one mesh).
var _boards: SurfaceTool
## Dust drifting in the window light by day.
var _dust: GPUParticles3D
var _blob_mat: StandardMaterial3D
## The door leaf's hinge (the pinboard hangs on it).
var _door_leaf: Node3D
## The window's frame (the bonsai's spot and the sill's light hang in it).
var _win: Node3D


func _ready() -> void:
	place()
	rotation.y = PI
	# Real weathered boards and a worn plank floor (CC0, Poly Haven; Simon, play test 4).
	_wood = _planks("weathered_planks", 0.55, Color(0.82, 0.78, 0.74))
	_floor = _planks("old_planks_02", 0.5, Color(0.75, 0.7, 0.66))
	_build_room()
	_build_window()
	_build_bonsai_lamp()
	_build_door_lamp()
	_build_bench()
	_build_pinboard()
	_build_camera()
	_build_sounds()
	_build_note()


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


## A wall of boards with rectangular openings ([from, to, bottom, top] along its axis); `w`: 0 the
## front wall (+Z), 1 the left (+X), 2 the right (-X), 3 the back.
func _wall_with_holes(w: int, seed: int, holes: Array, t: float) -> void:
	var along_x := w == 0 or w == 3
	var half := (WIDTH if along_x else DEPTH) * 0.5
	var out := (DEPTH if along_x else WIDTH) * 0.5 * (1.0 if w == 0 or w == 1 else -1.0)
	holes.sort_custom(func(a: Array, b: Array) -> bool: return float(a[0]) < float(b[0]))
	var piece := func(u0: float, u1: float, y0: float, y1: float) -> void:
		if u1 - u0 < 0.004 or y1 - y0 < 0.004:
			return
		var mid := (u0 + u1) * 0.5
		var size := Vector3(u1 - u0, y1 - y0, t) if along_x else Vector3(t, y1 - y0, u1 - u0)
		var at := Vector3(mid, (y0 + y1) * 0.5, out) if along_x else Vector3(out, (y0 + y1) * 0.5, mid)
		_wall(size, at, seed)
	var cursor := -half
	for h in holes:
		piece.call(cursor, float(h[0]), 0.0, WALL_H)
		piece.call(float(h[0]), float(h[1]), 0.0, float(h[2]))
		piece.call(float(h[0]), float(h[1]), float(h[3]), WALL_H)
		cursor = float(h[1])
	piece.call(cursor, half, 0.0, WALL_H)

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


## The small lamp over the bonsai (0.8.2.4): an iron bracket from the wall above the window, a
## short chain, a dark green enamel shade (cream inside) with a warm bulb. Its spot lights the
## bonsai and the sill by night; by day it is off (shed.daylight drives it in _process).
func _build_bonsai_lamp() -> void:
	var hd := DEPTH * 0.5
	var cx := (WINDOW_X.x + WINDOW_X.y) * 0.5
	var sill := WINDOW_Y.x + 0.01
	var iron := _mat(Color(0.1, 0.09, 0.08), 0.55)
	var top := WINDOW_Y.y + 0.2
	var reach := 0.17
	# The bracket: a plate on the wall and an arm out into the room with a brace under it.
	_box(Vector3(0.05, 0.09, 0.012), Vector3(cx, top, hd - 0.01), iron)
	_box(Vector3(0.014, 0.014, reach), Vector3(cx, top, hd - 0.01 - reach * 0.5), iron)
	var brace := _box(Vector3(0.01, 0.01, reach * 0.75), Vector3(cx, top - 0.04, hd - 0.01 - reach * 0.36), iron)
	brace.rotation.x = -0.45
	var at := Vector3(cx, top - 0.09, hd - 0.01 - reach + 0.01)
	_box(Vector3(0.004, 0.08, 0.004), at + Vector3(0, 0.05, 0), iron)
	# The shade: dark green enamel outside, cream enamel inside, a brass cap.
	var enamel := _mat(Color(0.12, 0.24, 0.17), 0.35)
	enamel.metallic_specular = 0.7
	var shade := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 0.016
	cm.bottom_radius = 0.06
	cm.height = 0.055
	cm.cap_bottom = false
	cm.radial_segments = 20
	shade.mesh = cm
	shade.material_override = enamel
	shade.position = at
	shade.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(shade)
	var inner := MeshInstance3D.new()
	var im := cm.duplicate() as CylinderMesh
	im.top_radius = 0.0145
	im.bottom_radius = 0.0585
	im.flip_faces = true
	inner.mesh = im
	inner.material_override = _mat(Color(0.93, 0.89, 0.78), 0.4)
	inner.position = at
	add_child(inner)
	var cap := MeshInstance3D.new()
	var capm := CylinderMesh.new()
	capm.top_radius = 0.008
	capm.bottom_radius = 0.016
	capm.height = 0.016
	cap.mesh = capm
	cap.material_override = _mat(Color(0.7, 0.52, 0.25), 0.35)
	cap.position = at + Vector3(0, 0.034, 0)
	add_child(cap)
	# The bulb, just showing under the rim.
	var bulb := MeshInstance3D.new()
	var bm := SphereMesh.new()
	bm.radius = 0.016
	bm.height = 0.03
	bulb.mesh = bm
	_bonsai_bulb = StandardMaterial3D.new()
	_bonsai_bulb.albedo_color = Color(1.0, 0.9, 0.7)
	_bonsai_bulb.emission_enabled = true
	_bonsai_bulb.emission = Color(1.0, 0.7, 0.36)
	bulb.material_override = _bonsai_bulb
	bulb.position = at + Vector3(0, -0.012, 0)
	bulb.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(bulb)
	# Its light: a warm cone onto the bonsai, a pool on the sill, falling off before the bench.
	bonsai_lamp = SpotLight3D.new()
	bonsai_lamp.light_color = Color(1.0, 0.76, 0.5)
	bonsai_lamp.spot_range = 1.1
	bonsai_lamp.spot_angle = 38.0
	bonsai_lamp.spot_angle_attenuation = 1.6
	bonsai_lamp.spot_attenuation = 1.2
	bonsai_lamp.light_specular = 0.3
	bonsai_lamp.shadow_enabled = false
	bonsai_lamp.position = at + Vector3(0, -0.02, 0)
	# Aimed in the shed's own frame (the shed is turned when placed).
	bonsai_lamp.basis = Basis.looking_at(Vector3(cx, sill + 0.1, hd - 0.14) - bonsai_lamp.position, Vector3.FORWARD)
	add_child(bonsai_lamp)
	_set_bonsai_lamp(0.0)


## 0.8.2.7 (Simon: at night the shed was too dark, the left side most): a second lamp like the
## bonsai's, on the front wall left of the door (on screen), mostly above the picture's edge. Its
## wide warm cone lights the pinboard, the door and the bench's left half, calmly; by night only.
var door_lamp: SpotLight3D
var _door_bulb: StandardMaterial3D
## Where it hangs (shed frame x, height) and its energy at full night (phone: a little more).
const DOOR_LAMP_X := 0.92
const DOOR_LAMP_ENERGY := 1.4
const DOOR_LAMP_PHONE := 2.2


func _build_door_lamp() -> void:
	var hd := DEPTH * 0.5
	var iron := _mat(Color(0.1, 0.09, 0.08), 0.55)
	var top := WINDOW_Y.y + 0.2
	var reach := 0.17
	var cx := DOOR_LAMP_X
	_box(Vector3(0.05, 0.09, 0.012), Vector3(cx, top, hd - 0.01), iron)
	_box(Vector3(0.014, 0.014, reach), Vector3(cx, top, hd - 0.01 - reach * 0.5), iron)
	var brace := _box(Vector3(0.01, 0.01, reach * 0.75), Vector3(cx, top - 0.04, hd - 0.01 - reach * 0.36), iron)
	brace.rotation.x = -0.45
	var at := Vector3(cx, top - 0.09, hd - 0.01 - reach + 0.01)
	_box(Vector3(0.004, 0.08, 0.004), at + Vector3(0, 0.05, 0), iron)
	var enamel := _mat(Color(0.12, 0.24, 0.17), 0.35)
	enamel.metallic_specular = 0.7
	var shade := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = 0.016
	cm.bottom_radius = 0.06
	cm.height = 0.055
	cm.cap_bottom = false
	cm.radial_segments = 20
	shade.mesh = cm
	shade.material_override = enamel
	shade.position = at
	shade.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(shade)
	var bulb := MeshInstance3D.new()
	var bm := SphereMesh.new()
	bm.radius = 0.016
	bm.height = 0.03
	bulb.mesh = bm
	_door_bulb = StandardMaterial3D.new()
	_door_bulb.albedo_color = Color(1.0, 0.9, 0.7)
	_door_bulb.emission_enabled = true
	_door_bulb.emission = Color(1.0, 0.7, 0.36)
	bulb.material_override = _door_bulb
	bulb.position = at + Vector3(0, -0.012, 0)
	bulb.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(bulb)
	door_lamp = SpotLight3D.new()
	door_lamp.light_color = Color(1.0, 0.76, 0.5)
	door_lamp.spot_range = 3.2
	door_lamp.spot_angle = 55.0
	door_lamp.spot_angle_attenuation = 1.3
	door_lamp.spot_attenuation = 1.1
	door_lamp.light_specular = 0.25
	door_lamp.shadow_enabled = false
	door_lamp.position = at + Vector3(0, -0.02, 0)
	# Down and in: toward the pinboard on the door and the bench's left half.
	door_lamp.basis = Basis.looking_at(Vector3(0.35, 0.95, hd - 0.6) - door_lamp.position, Vector3.FORWARD)
	add_child(door_lamp)
	_set_bonsai_lamp(0.0)


## The bonsai lamp (and the lamp by the door) at `night` (0 by day .. 1 at full night).
func _set_bonsai_lamp(night: float) -> void:
	if bonsai_lamp == null:
		return
	if door_lamp != null:
		var dfull := DOOR_LAMP_PHONE if RenderingServer.get_current_rendering_method() == "gl_compatibility" else DOOR_LAMP_ENERGY
		door_lamp.light_energy = dfull * night
		door_lamp.visible = night > 0.01
		_door_bulb.emission_energy_multiplier = lerpf(0.0, 2.2, night)
		_door_bulb.albedo_color = Color(0.55, 0.5, 0.42).lerp(Color(1.0, 0.9, 0.7), night)
	var full := BONSAI_LAMP_PHONE if RenderingServer.get_current_rendering_method() == "gl_compatibility" else BONSAI_LAMP_ENERGY
	bonsai_lamp.light_energy = full * night
	bonsai_lamp.visible = night > 0.01
	_bonsai_bulb.emission_energy_multiplier = lerpf(0.0, 2.2, night)
	_bonsai_bulb.albedo_color = Color(0.55, 0.5, 0.42).lerp(Color(1.0, 0.9, 0.7), night)


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
	var path := "res://assets/shed/%s_1k.gltf" % id if id.contains("/") else "res://assets/shed/%s/%s_1k.gltf" % [id, id]
	var n: Node3D = (load(path) as PackedScene).instantiate()
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
	# The front wall around the open door and the window; the other three plain.
	var dx := DOOR_W * 0.5
	var wall_of := {0: 3, 1: 2, 2: 1, 3: 0}
	for w in [0, 1, 2, 3]:
		var holes: Array = []
		if w == 0:
			holes.append([DOOR_X - dx, DOOR_X + dx, 0.0, DOOR_H])
			holes.append([WINDOW_X.x, WINDOW_X.y, WINDOW_Y.x, WINDOW_Y.y])
		_wall_with_holes(w, wall_of[w], holes, t)
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
	var ox := DOOR_X
	for x in [-dx - 0.035, dx + 0.035]:
		_beam(Vector3(0.07, DOOR_H, 0.16), Vector3(ox + x, DOOR_H * 0.5, hd), frame)
		_beam(Vector3(0.05, DOOR_H + 0.06, 0.025), Vector3(ox + x + signf(x) * 0.055, DOOR_H * 0.5 + 0.03, hd - 0.09), frame)
	_beam(Vector3(DOOR_W + 0.14, 0.08, 0.16), Vector3(ox, DOOR_H + 0.04, hd), frame)
	_beam(Vector3(DOOR_W + 0.26, 0.06, 0.025), Vector3(ox, DOOR_H + 0.1, hd - 0.09), frame)
	# A worn threshold board.
	_beam(Vector3(DOOR_W + 0.1, 0.03, 0.2), Vector3(ox, 0.115, hd), frame)
	# The leaf, on the +x post, stands open into the room (the pinboard hangs on its inside).
	var hinge := _door_leaf_at(Vector3(ox + dx, 0, hd + 0.05), 1.0, DOOR_TURN, DOOR_W)
	_items["door"] = hinge
	_door_leaf = hinge
	_pick("door", Vector3(ox, 1.25, hd), 0.5 * DOOR_W / 0.66)
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
	# (0.8.1: the middle rafter ran from the eye to the door, a long lit wedge at the picture's top.)
	for x in [-1.0, 1.0]:
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
	_lamp_range = _lamp.omni_range
	# Over the bench, near the eye (it shows small at the picture's top and lights the bench).
	_lamp.position = LAMP_AT
	add_child(_lamp)
	var glass := MeshInstance3D.new()
	var s := SphereMesh.new()
	# Small and warm (0.8.1: in the wider view a large white egg hung at the picture's top).
	s.radius = 0.032
	s.height = 0.075
	glass.mesh = s
	var gm := StandardMaterial3D.new()
	gm.albedo_color = Color(1.0, 0.8, 0.5)
	gm.emission_enabled = true
	gm.emission = Color(1.0, 0.62, 0.28)
	gm.emission_energy_multiplier = 1.4
	glass.material_override = gm
	glass.position = _lamp.position + Vector3(0, 0.05, 0)
	add_child(glass)
	_box(Vector3(0.01, 0.3, 0.01), glass.position + Vector3(0, 0.2, 0), _mat(Color(0.15, 0.12, 0.1)))


## A door leaf hung at `at` (its hinge post; `side` -1: the leaf reaches toward +X when shut),
## turned open by `turn`. Returns the hinge (the leaf's frame: x along the leaf, y up).
func _door_leaf_at(at: Vector3, side: float, turn: float, w: float) -> Node3D:
	var hinge := Node3D.new()
	hinge.position = at
	hinge.rotation.y = turn
	add_child(hinge)
	_box(Vector3(w, 2.0, 0.05), Vector3(w * 0.5 * (-side if side > 0.0 else 1.0), 1.0, 0), _wood, hinge)
	return hinge


## The small window beside the workbench: a frame with a cross bar, old glass, a deep sill
## inside (empty, for the bonsai) and daylight falling in across the sill and the bench.
func _build_window() -> void:
	# Built in the front wall's frame.
	_win = Node3D.new()
	_win.name = "Window"
	add_child(_win)
	var hd := DEPTH * 0.5
	var wx := WINDOW_X
	var wy := WINDOW_Y
	var cx := (wx.x + wx.y) * 0.5
	var cy := (wy.x + wy.y) * 0.5
	var w := wx.y - wx.x
	var h := wy.y - wy.x
	# A deep bevelled frame with a casing proud of the wall and thin glazing bars set back.
	var frame := _planks("old_planks_02", 1.4, Color(0.62, 0.5, 0.4))
	for y in [wy.x, wy.y]:
		_beam(Vector3(w + 0.1, 0.055, 0.14), Vector3(cx, y, hd), frame, 0.012, _win)
		_beam(Vector3(w + 0.2, 0.045, 0.022), Vector3(cx, y + signf(y - cy) * 0.035, hd - 0.08), frame, 0.012, _win)
	for x in [wx.x, wx.y]:
		_beam(Vector3(0.055, h, 0.14), Vector3(x, cy, hd), frame, 0.012, _win)
		_beam(Vector3(0.045, h + 0.12, 0.022), Vector3(x + signf(x - cx) * 0.035, cy, hd - 0.08), frame, 0.012, _win)
	_beam(Vector3(w, 0.026, 0.04), Vector3(cx, cy, hd + 0.01), frame, 0.006, _win)
	_beam(Vector3(0.026, h, 0.04), Vector3(cx, cy, hd + 0.01), frame, 0.006, _win)
	# Old, slightly dusty glass.
	var glass := StandardMaterial3D.new()
	glass.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	glass.albedo_color = Color(0.85, 0.9, 0.85, 0.12)
	glass.roughness = 0.15
	_glass = glass
	_box(Vector3(w, h, 0.005), Vector3(cx, cy, hd + 0.02), glass, _win)
	# The sill: a deep worn board inside the window, reaching into the room far enough for the
	# bonsai's tools in a row before the pot (0.7), on two small brackets.
	var sill_top := wy.x + 0.01
	_box(Vector3(w + 0.24, 0.035, 0.44), Vector3(cx, sill_top - 0.0175, hd - 0.2), _planks("wood_table_worn", 0.9, Color(0.85, 0.8, 0.74)), _win)
	for bx in [-0.16, 0.16]:
		_box(Vector3(0.035, 0.12, 0.3), Vector3(cx + bx, sill_top - 0.095, hd - 0.17), frame, _win)
	bonsai_spot = Node3D.new()
	bonsai_spot.name = "bonsai_spot"
	bonsai_spot.position = Vector3(cx, sill_top, hd - 0.1)
	_win.add_child(bonsai_spot)
	_pick("bonsai", bonsai_spot.position + Vector3(0.0, 0.2, -0.04), 0.17, _win)
	var tag := Node3D.new()
	# 0.8.2 (look review): the label hangs under the sill's front edge, not over the tools on it.
	tag.position = bonsai_spot.position + BONSAI_TAG
	_win.add_child(tag)
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
	_win.add_child(sun)
	_window_sun = sun
	sun.position = Vector3(cx + 0.25, cy + 0.9, hd + 1.1)
	var aim := Vector3(cx - 0.3, BENCH_TOP, hd - 1.2)
	sun.basis = Basis.looking_at(aim - sun.position, Vector3.UP)
	var shaft := MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(w * 0.95, 1.25)
	shaft.mesh = q
	var sm := ShaderMaterial.new()
	sm.shader = _shaft_shader()
	shaft.material_override = sm
	shaft.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_win.add_child(shaft)
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
	_win.add_child(p)
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
	bench.position = BENCH_AT
	add_child(bench)
	# An old cabinet workbench with drawers (Poly Haven "Wooden Table 03"), its drawers toward us.
	var table := _model("WoodenTable_03", Vector3.ZERO, 1.0, PI, bench)
	table.scale = Vector3(0.95, 1.0, 1.05)
	_build_drawers(table)
	var top := BENCH_TOP - 0.05  # bench-local height of the table top
	var spot := func(name: String) -> Array:
		var p: Vector3 = THINGS[name][0]
		return [Vector3(p.x, top + p.y, p.z), float(THINGS[name][1])]
	# The journal: dark leather with an elastic band and a red ribbon, lying on the left.
	var journal := _book(Vector3(0.16, 0.026, 0.22), _leather(Paper.LEATHER), Color(0.92, 0.88, 0.78), true)
	var js: Array = spot.call("journal")
	journal.position = js[0]
	journal.rotation.y = js[1]
	bench.add_child(journal)
	_register("journal", journal, Vector3(0, 0.02, 0), 0.12)
	_tag("journal", journal, Vector3(0.0, 0.0, -0.08), true)
	# The photo album: bigger, green cloth over boards, a paper label on the cover.
	var album := _book(Vector3(0.28, 0.045, 0.22), _leather(Color(0.2, 0.3, 0.19), 6.0), Color(0.78, 0.7, 0.58), false)
	var als: Array = spot.call("album")
	album.position = als[0]
	album.rotation.y = als[1]
	bench.add_child(album)
	_register("album", album, Vector3(0, 0.03, 0), 0.14)
	_tag("album", album, Vector3(0.0, 0.06, 0.1), false)
	# The seed bag: a kraft paper sack, the top rolled over, a few seeds spilled beside it.
	var seeds := _seed_bag()
	var ss: Array = spot.call("seeds")
	seeds.position = ss[0]
	seeds.rotation.y = ss[1]
	bench.add_child(seeds)
	_register("seeds", seeds, Vector3(0, 0.09, 0), 0.1)
	_tag("seeds", seeds, Vector3(0.0, 0.2, 0.0), false)
	# Garden gloves lying at the front edge: put them on and go outside.
	var gloves := Node3D.new()
	var gs: Array = spot.call("gloves")
	gloves.position = gs[0]
	gloves.rotation.y = gs[1]
	bench.add_child(gloves)
	_model("garden_gloves_01", Vector3.ZERO, 0.75, 0.0, gloves)
	_register("gloves", gloves, Vector3(0, 0.03, 0), 0.1)
	_tag("gloves", gloves, Vector3(0.0, 0.0, -0.04), true)
	# Each thing set down on the top: its lowest point on the table (0.8.2.7: they sank in).
	_table_top = _top_of(table, bench)
	for k in ON_TOP:
		var n: Node3D = _items[k]
		n.position.y += _table_top - thing_bounds(k).position.y
	# 0.8.2.7: the trowel that lay behind the seed bag stood on its end (a stick poking up
	# behind the bag); it is gone, the bonsai's sill has its own. A watering can on the floor
	# under the window.
	var can := Vector3(0.78, 0.05, DEPTH * 0.5 - 0.3)
	_model("watering_can_metal_01", can, 1.1, 2.4, self)
	# Contact shadows (0.6.1 review: the things floated): a soft dark patch under each thing on
	# the bench, under the bench and the can on the floor, and along the foot of the walls.
	_blob(js[0] + Vector3(0, 0.004, 0), Vector2(0.24, 0.3), js[1], 0.8, bench)
	_blob(als[0] + Vector3(0, 0.004, 0), Vector2(0.38, 0.31), als[1], 0.8, bench)
	_blob(ss[0] + Vector3(0, 0.004, 0), Vector2(0.2, 0.15), ss[1], 0.9, bench)
	_blob(gs[0] + Vector3(0, 0.004, 0), Vector2(0.28, 0.2), gs[1], 0.6, bench)
	var floor_y := 0.1015
	_blob(Vector3(0.0, floor_y - 0.05, 0.0), Vector2(1.9, 1.1), 0.0, 0.85, bench)
	_blob(Vector3(can.x, floor_y, can.z), Vector2(0.45, 0.45), 0.0, 0.8)
	for side in [-1.0, 1.0]:
		_blob(Vector3(side * (WIDTH * 0.5 - 0.05), floor_y, 0.0), Vector2(0.5, DEPTH * 1.1), 0.0, 0.5)
	_blob(Vector3(0.0, floor_y, -DEPTH * 0.5 + 0.05), Vector2(WIDTH * 1.1, 0.5), 0.0, 0.5)
	# The rug before the bench, where you stand (it shows only on a wider screen).
	_build_rug(floor_y, Vector3(0.05, 0.0, BENCH_Z - 0.62), 0.04)


## The table's top surface (bench frame), measured from the model.
var _table_top: float = BENCH_TOP - 0.05


func table_top() -> float:
	return _table_top


## The highest point of the table model's top (its drawers left out), in `frame`'s space.
func _top_of(table: Node3D, frame: Node3D) -> float:
	var best := -INF
	for c in table.find_children("*", "MeshInstance3D", true, false):
		var mi := c as MeshInstance3D
		if String(mi.name).contains("drawer") or mi.get_parent() is MeshInstance3D:
			continue
		var a := frame.global_transform.affine_inverse() * mi.global_transform * mi.get_aabb()
		best = maxf(best, a.end.y)
	return best if best > -INF else BENCH_TOP - 0.05


## A thing on the bench's bounds in the bench's frame (all its meshes).
func thing_bounds(name: String) -> AABB:
	var n: Node3D = _items[name]
	var frame := n.get_parent() as Node3D
	var inv := frame.global_transform.affine_inverse()
	var acc: AABB
	var first := true
	for c in [n] + n.find_children("*", "MeshInstance3D", true, false):
		var mi := c as MeshInstance3D
		if mi == null or mi.mesh == null:
			continue
		var a := inv * mi.global_transform * mi.get_aabb()
		acc = a if first else acc.merge(a)
		first = false
	return acc


## The workbench model's drawers become drawers (0.8.2.4): each gets a paper liner on its floor
## and an empty "Contents" node on it, the hook for what Simon puts in them later (drawer_contents).
func _build_drawers(table: Node3D) -> void:
	var liner := _paper_mat(Color(0.8, 0.7, 0.55), true, "beige")
	for n in table.find_children("WoodenTable_03_drawer*", "MeshInstance3D", true, false):
		var d := n as MeshInstance3D
		var key := String(d.name).trim_prefix("WoodenTable_03_")
		drawers[key] = d
		_drawer_open[key] = false
		d.set_meta("rest", d.position)
		var box := d.get_aabb()
		# The drawer's floor: the box's foot plus its bottom board; the front is at the box's +z.
		var floor_y := box.position.y + 0.022
		var lin := _box(Vector3(box.size.x - 0.07, 0.002, box.size.z - 0.07), Vector3(box.get_center().x, floor_y, box.get_center().z), liner, d)
		lin.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var contents := Node3D.new()
		contents.name = "Contents"
		contents.position = Vector3(box.get_center().x, floor_y + 0.002, box.get_center().z)
		d.add_child(contents)


## The node to put a drawer's things on (its floor's middle, in the drawer's frame; x across,
## z from back to front, the drawer about 0.37 or 0.78 wide and 0.44 deep). Empty for now.
func drawer_contents(key: String) -> Node3D:
	var d: Node3D = drawers.get(key)
	return null if d == null else d.get_node("Contents") as Node3D


func is_drawer_open(key: String) -> bool:
	return bool(_drawer_open.get(key, false))


## Which drawer's front is under this screen point ("" if none).
func drawer_at(screen: Vector2) -> String:
	for key in drawers:
		var d := drawers[key] as MeshInstance3D
		var box := d.get_aabb()
		var z := box.end.z
		var poly := PackedVector2Array()
		for c in [Vector2(box.position.x, box.position.y), Vector2(box.end.x, box.position.y), Vector2(box.end.x, box.end.y), Vector2(box.position.x, box.end.y)]:
			var w := d.global_transform * Vector3(c.x, c.y, z)
			if camera.is_position_behind(w):
				poly = PackedVector2Array()
				break
			poly.append(camera.unproject_position(w))
		if poly.size() == 4 and Geometry2D.is_point_in_polygon(screen, poly):
			return key
	return ""


## Slides a drawer out (or back in) with a little overshoot, and a wooden scrape.
func toggle_drawer(key: String) -> void:
	var d: Node3D = drawers.get(key)
	if d == null:
		return
	var open := not bool(_drawer_open[key])
	if open:
		# One drawer out at a time: the other slides back first.
		for k in _drawer_open:
			if k != key and bool(_drawer_open[k]):
				toggle_drawer(k)
	_drawer_open[key] = open
	hide_note()
	if open and lean_on_open:
		_lean_to(key)
	elif not open and _lean_key == key:
		_lean_to("")
	var rest: Vector3 = d.get_meta("rest")
	var to := rest + Vector3(0, 0, DRAWER_OUT if open else 0.0)
	var busy_key := "drawer_" + key
	if _busy.has(busy_key) and (_busy[busy_key] as Tween).is_valid():
		(_busy[busy_key] as Tween).kill()
	var tw := create_tween()
	_busy[busy_key] = tw
	if open:
		# A small tug first (it sticks a little), then it runs out and settles.
		tw.tween_property(d, "position", d.position + Vector3(0, 0, 0.012), 0.06).set_trans(Tween.TRANS_SINE)
		tw.tween_property(d, "position", to, DRAWER_TIME).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	else:
		tw.tween_property(d, "position", to, DRAWER_TIME * 0.8).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
		tw.tween_property(d, "position", to + Vector3(0, 0, 0.004), 0.04)
		tw.tween_property(d, "position", to, 0.05)
	if _sounds.has("drawer"):
		_player.stream = _sounds["drawer"]
		_player.pitch_scale = (1.35 if open else 1.5) + 0.06 * fposmod(_time * 7.0, 1.0)
		_player.play()


## Slides every drawer back at once (leaving the shed), the view upright again.
func close_drawers() -> void:
	for key in drawers:
		if bool(_drawer_open[key]):
			_drawer_open[key] = false
			var d: Node3D = drawers[key]
			if _busy.has("drawer_" + key) and (_busy["drawer_" + key] as Tween).is_valid():
				(_busy["drawer_" + key] as Tween).kill()
			d.position = d.get_meta("rest")
	hide_note()
	if _lean_tween != null and _lean_tween.is_valid():
		_lean_tween.kill()
	_lean_key = ""
	if _lean > 0.0:
		_lean = 0.0
		if camera != null:
			camera.transform = _fit_xf
			camera.fov = _fit_fov


# --- the finds in the drawers (0.8.2.6) -------------------------------------------------

## Opening a drawer leans the view over it (tools that only want the drawer may switch it off).
var lean_on_open: bool = true


## The drawer the view leans over ("" when upright or leaning back).
func drawer_look() -> String:
	return _lean_key


## Lays the garden's finds (Finds.list()) into their drawers: each kind in its place, up to
## FIND_COPIES of it slightly apart. Rebuilt only when the list changed.
func fill_drawers(list: Array) -> void:
	var key: Array = []
	for f in list:
		key.append([f["kind"], f["day"], f["tree"]])
	if key == _finds_key and not _finds_shown.is_empty():
		return
	_finds_key = key
	for e in _finds_shown:
		(e["node"] as Node).free()
	_finds_shown.clear()
	var copies := {}
	for f in list:
		var kind := str(f["kind"])
		var c := int(copies.get(kind, 0))
		copies[kind] = c + 1
		if c >= FIND_COPIES or not FIND_SLOTS.has(kind):
			continue
		var box := drawer_contents(str(f["drawer"]))
		if box == null:
			continue
		var n := FindModels.build(kind, c)
		var slot: Array = FIND_SLOTS[kind]
		var at: Vector3 = slot[0]
		# A second or third one lies beside the first, a little turned (paper on paper).
		at += Vector3([0.0, 0.018, -0.016][c], 0.0025 * c if kind == "map_scrap" else 0.0, [0.0, -0.012, 0.014][c])
		n.position = at
		n.rotation.y += float(slot[1]) + [0.0, 0.25, -0.2][c] * (0.4 if kind == "old_root" else 1.0)
		box.add_child(n)
		n.set_meta("rest", n.transform)
		# A soft contact shadow under it (it lies, it does not float).
		var sz := float(FindModels.SIZE[kind])
		_blob(Vector3(0, 0.0015, 0), Vector2(sz * 1.05, sz * (0.5 if kind == "old_root" else 0.8)), 0.0, 0.45, n)
		_finds_shown.append({"node": n, "drawer": str(f["drawer"]), "item": f})


## The shown find under this screen point while the view leans over its open drawer (-1: none).
func find_at(screen: Vector2) -> int:
	if _lean_key == "":
		return -1
	var best := -1
	var best_score := 1.0
	for i in range(_finds_shown.size()):
		var e: Dictionary = _finds_shown[i]
		if str(e["drawer"]) != _lean_key:
			continue
		var n: Node3D = e["node"]
		var at := n.global_position + Vector3(0, 0.01, 0)
		if camera.is_position_behind(at):
			continue
		var r := maxf(_screen_radius(at, float(FindModels.SIZE.get(str(e["item"]["kind"]), 0.06)) * 0.6), 34.0)
		var score := camera.unproject_position(at).distance_to(screen) / r
		if score < best_score:
			best_score = score
			best = i
	return best


## The finds shown in a drawer (their Finds.list() entries).
func finds_in(drawer: String) -> Array:
	var out: Array = []
	for e in _finds_shown:
		if str(e["drawer"]) == drawer:
			out.append(e["item"])
	return out


## Where a shown find is on screen (for tools and tests).
func find_screen(i: int) -> Vector2:
	return camera.unproject_position((_finds_shown[i]["node"] as Node3D).global_position + Vector3(0, 0.01, 0))


func find_count() -> int:
	return _finds_shown.size()


## A tap on a find: it lifts a little and its note shows (one line and the night it was found).
func show_note(i: int) -> void:
	if i < 0 or i >= _finds_shown.size():
		return
	var e: Dictionary = _finds_shown[i]
	var item: Dictionary = e["item"]
	_note.text = "%s\n%s" % [Finds.note(str(item["kind"])), Finds.when_line(item)]
	_note_find = i
	_place_note()
	var n: Node3D = e["node"]
	var rest: Transform3D = n.get_meta("rest")
	var tw := create_tween()
	tw.tween_property(n, "position", rest.origin + Vector3(0, 0.012, 0), 0.14).set_trans(Tween.TRANS_SINE)
	tw.tween_property(n, "position", rest.origin, 0.3).set_trans(Tween.TRANS_BOUNCE).set_ease(Tween.EASE_OUT)
	if _sounds.has("drawer"):
		_player.stream = _sounds["drawer"]
		_player.pitch_scale = 2.2
		_player.play()


func hide_note() -> void:
	_note_find = -1
	if _note != null:
		_note.text = ""


func note_text() -> String:
	return _note.text if _note != null else ""


func _build_note() -> void:
	_note_layer = CanvasLayer.new()
	_note_layer.layer = 18
	add_child(_note_layer)
	_note = PaperNote.new(30, 64)
	_note_layer.add_child(_note)


## The note at the top of the screen, over the drawer's back (the find stays in view below it).
func _place_note() -> void:
	var vp := get_viewport()
	if vp == null:
		return
	var size := vp.get_visible_rect().size
	var w := minf(size.x * 0.86, 620.0)
	_note.custom_minimum_size = Vector2(w, 0)
	_note.size = Vector2(w, 0)
	_note.position = Vector2((size.x - w) * 0.5, size.y * 0.08)


## Starts the lean over `key`'s drawer ("" back upright).
func _lean_to(key: String) -> void:
	if key != "":
		_lean_xf = _lean_pose(key)
		_lean_fov = LEAN_FOV
	_lean_key = key
	if _lean_tween != null and _lean_tween.is_valid():
		_lean_tween.kill()
	if not is_inside_tree():
		_lean = 1.0 if key != "" else 0.0
		return
	_lean_tween = create_tween()
	_lean_tween.tween_property(self, "_lean", 1.0 if key != "" else 0.0, LEAN_TIME * (1.0 if key != "" else 0.8))
	_lean_tween.tween_callback(_apply_lean.bind(true))


## Ends the lean at once (tests and tools).
func finish_lean() -> void:
	if _lean_tween != null and _lean_tween.is_valid():
		_lean_tween.kill()
	_lean = 1.0 if _lean_key != "" else 0.0
	_apply_lean(true)


## The eye over the open drawer: above it and toward the room, looking down at its floor, as far
## back as keeps LEAN_HALF on screen.
func _lean_pose(key: String) -> Transform3D:
	var d: Node3D = drawers[key]
	var rest: Vector3 = d.get_meta("rest")
	var parent := (d.get_parent() as Node3D).global_transform
	var out := parent * Transform3D(d.basis, rest + Vector3(0, 0, DRAWER_OUT))
	var contents := drawer_contents(key)
	var target := out * (contents.position + Vector3(0, 0.02, -0.055))
	var front := (out.basis * Vector3(0, 0, 1)).normalized()
	var vp := get_viewport() if is_inside_tree() else null
	var size := vp.get_visible_rect().size if vp != null else Vector2(720, 1600)
	var aspect := size.x / maxf(size.y, 1.0)
	var t := tan(deg_to_rad(LEAN_FOV * 0.5))
	var dist := maxf(LEAN_HALF.x / (t * aspect), LEAN_HALF.y / t) * 1.05
	var dir := (Vector3.UP * 0.9 + front * 0.44).normalized()
	var eye := target + dir * dist
	# In the shed's frame, like the room's eye (the camera is the shed's child).
	var local := global_transform.affine_inverse()
	return Transform3D(Basis(), local * eye).looking_at(local * target, Vector3.UP)


func _apply_lean(force: bool = false) -> void:
	if camera == null or (_lean <= 0.0 and not force):
		return
	var k := clampf(_lean, 0.0, 1.0)
	k = k * k * (3.0 - 2.0 * k)
	camera.transform = _fit_xf.interpolate_with(_lean_xf, k)
	camera.fov = lerpf(_fit_fov, _lean_fov, k)
	if _note_find >= 0:
		_place_note()


## A woven rag rug on the strip of floor before the bench (0.8.2.2, the tighter view: the little
## floor left in the picture's foot is a place to stand, not bare boards).
func _build_rug(floor_y: float, at: Vector3, turn: float) -> void:
	var rug := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(1.5, 0.9)
	rug.mesh = pm
	var m := ShaderMaterial.new()
	m.shader = _rug_shader()
	rug.material_override = m
	rug.position = Vector3(at.x, floor_y + 0.002, at.z)
	rug.rotation.y = turn
	rug.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(rug)


static func _rug_shader() -> Shader:
	var s := Shader.new()
	s.code = """
shader_type spatial;
render_mode cull_disabled;
float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
	// Bands of rag in faded colours across the rug, a woven texture, a dark border and a frayed edge.
	vec2 uv = UV;
	float band = floor(uv.y * 14.0 + sin(uv.x * 9.0) * 0.08);
	float pick = hash(vec2(band, 3.0));
	vec3 col = pick < 0.3 ? vec3(0.42, 0.2, 0.15) : (pick < 0.55 ? vec3(0.55, 0.47, 0.33) : (pick < 0.8 ? vec3(0.26, 0.3, 0.33) : vec3(0.5, 0.36, 0.22)));
	float weave = 0.85 + 0.15 * sin(uv.x * 420.0) * sin(uv.y * 260.0);
	col *= weave * (0.85 + 0.3 * hash(floor(uv * vec2(160.0, 90.0))));
	float edge = min(min(uv.x, 1.0 - uv.x) * 1.6, min(uv.y, 1.0 - uv.y));
	col = mix(vec3(0.16, 0.11, 0.08), col, smoothstep(0.03, 0.05, edge));
	if (edge < 0.012 && hash(floor(uv * 300.0)) > 0.5) discard;
	ALBEDO = col * 0.8;
	ROUGHNESS = 1.0;
}
"""
	return s


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


func _pick(name: String, at: Vector3, radius: float, parent: Node3D = self) -> void:
	var c := Node3D.new()
	c.position = at
	parent.add_child(c)
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
	# 0.8.2.7: no crease map on the bag: on its sides the triplanar creases read as black
	# blotches close up (the paper's own grain stays).
	kraft.normal_enabled = false
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
	var roll_mat := _paper_mat(Color(0.74, 0.58, 0.38), true)
	roll_mat.normal_enabled = false
	roll.material_override = roll_mat
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


## The pinboard on the inside of the open door's leaf: cork in a wooden frame, the option notes
## pinned on it and "options" written on a strip below them.
func _build_pinboard() -> void:
	var board := Node3D.new()
	board.position = PIN_AT
	board.rotation.y = PI
	board.scale = Vector3.ONE * PINBOARD_SCALE
	_door_leaf.add_child(board)
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


## The eye stands near the back wall facing the door, tilted down onto the bench: the bench across
## the picture's foot, the open door with the pinboard on its leaf above it on the left, the tree in
## the doorway, the window with the bonsai's sill on the right (0.8.2.5, layout 08).
## The field of view fits the screen's shape (0.8.1, item 26): the narrowest that holds must_see()
## with a margin, so on the phone's tall 20:9 screen the sill, the pinboard and every thing are
## whole and reachable; the tilt and a small turn are chosen with it (see fit_view()).
const EYE := Vector3(-0.05, 1.64, -1.3)
## The preferred tilt (degrees) and how far the eye may turn from facing the door (radians).
const PITCH := -20.0
const YAW_RANGE := 0.2
## Where the middle of what must be seen sits on screen, -1 bottom .. 1 top (a little below the
## middle: the wall above shows more than the floor).
const VMID := -0.05
## The view never gets narrower than this (a wide screen keeps about the old look).
const MIN_FOV := 62.0
## A margin of this share of the half width (and height) on each side of the screen.
const VIEW_MARGIN := 0.05
var _fit_aspect: float = -1.0
var _fit_pitch: float = -0.2


func _place_camera(_tree_height: float) -> void:
	_fit_aspect = -1.0
	fit_view()


## Where the bonsai's label hangs from, from the bonsai's spot: under the sill board's front edge.
const BONSAI_TAG := Vector3(0.0, -0.05, -0.33)
## The sill's tools (BonsaiTools.RESTS, from the bonsai's spot) and the room kept beside them, so
## the watering can and the front row never touch the screen's edge (0.8.2, look review: they sat
## within about 10 px of the left edge on a 450 px wide phone screen).
const SILL_TOOLS_EDGE := 0.27


## The view for this screen's shape: the field of view that holds must_see(), with the turn and
## tilt nearest the preferred ones.
func fit_view() -> void:
	var vp := get_viewport()
	if vp == null or camera == null:
		return
	var size := vp.get_visible_rect().size
	var aspect := size.x / maxf(size.y, 1.0)
	if is_equal_approx(aspect, _fit_aspect):
		return
	_fit_for(aspect)


## Fits the view to a screen of this shape (width over height): from EYE, the turn and tilt nearest
## its own that hold every thing, label, the door and the sill whole, with the narrowest field of view.
func _fit_for(aspect: float) -> void:
	_fit_aspect = aspect
	var yaw0 := PI
	var pitch0 := deg_to_rad(PITCH)
	var pts := must_see()
	var best := INF
	var best_b := Basis()
	var best_fov := MIN_FOV
	for i in range(41):
		var yaw := yaw0 + YAW_RANGE * (i / 20.0 - 1.0)
		for j in range(71):
			var pitch := pitch0 + deg_to_rad(j - 35.0)
			var b := Basis(Vector3.UP, yaw) * Basis(Vector3.RIGHT, pitch)
			var fit := _fit_of(b, EYE, pts, aspect)
			var cost := fit.x + absf(yaw - yaw0) * 8.0 + absf(fit.y - VMID) * 25.0 + absf(rad_to_deg(pitch - pitch0)) * 0.02
			if cost < best:
				best = cost
				best_b = b
				best_fov = fit.x
				_fit_pitch = pitch
	_fit_xf = Transform3D(best_b, EYE)
	_fit_fov = best_fov
	if _lean <= 0.0:
		camera.position = EYE
		camera.basis = best_b
		camera.fov = best_fov


## The field of view (degrees) that holds these points from this eye, and where the middle of
## their height sits on screen (-1 bottom .. 1 top).
func _fit_of(b: Basis, eye: Vector3, pts: Array[Vector3], aspect: float) -> Vector2:
	var inv := Transform3D(b, eye).affine_inverse()
	var need := tan(deg_to_rad(MIN_FOV * 0.5))
	var lo := INF
	var hi := -INF
	for p in pts:
		var c := inv * p
		if c.z > -0.05:
			return Vector2(179.0, 0.0)
		var ty := c.y / -c.z
		lo = minf(lo, ty)
		hi = maxf(hi, ty)
		need = maxf(need, maxf(absf(c.x) / -c.z / aspect, absf(ty)) / (1.0 - VIEW_MARGIN))
	var fov := clampf(rad_to_deg(atan(need)) * 2.0, MIN_FOV, 179.0)
	var t := tan(deg_to_rad(fov * 0.5))
	return Vector2(fov, (lo + hi) * 0.5 / t)


## What must be whole on screen (shed frame): each thing's tap circle and its label, the pinboard's
## corners and label, the doorway, the sill with the bonsai, its tools with a margin and its label.
func must_see() -> Array[Vector3]:
	var pts: Array[Vector3] = []
	for k in ["journal", "album", "seeds", "gloves"]:
		var c := _in_shed(_picks[k][0] as Node3D)
		var r := float(_picks[k][1]) * 0.8
		for d in [Vector3(r, 0, 0), Vector3(-r, 0, 0), Vector3(0, r, 0), Vector3(0, -r * 0.5, 0), Vector3(0, 0, r), Vector3(0, 0, -r)]:
			pts.append(c + d)
	for k in ITEMS:
		var a := _in_shed(_tag_anchors[k][0] as Node3D)
		pts.append(a)
		pts.append(a + Vector3(0, -0.07 if bool(_tag_anchors[k][1]) else 0.07, 0))
	var bx := _xf_in_shed(_items["options"] as Node3D)
	for cx in [-0.27, 0.27]:
		for cy in [-0.31, 0.31]:
			pts.append(bx * Vector3(cx, cy, 0.0))
	var hd := DEPTH * 0.5
	var dx := DOOR_W * 0.5 + 0.08
	for x in [-dx, dx]:
		for y in [0.2, DOOR_H + 0.12]:
			pts.append(Vector3(DOOR_X + x, y, hd))
	var sx := _xf_in_shed(bonsai_spot)
	for p in [Vector3(0.14, 0, -0.18), Vector3(-0.14, 0, -0.18), Vector3(0, 0.45, -0.04), Vector3(0.12, 0.08, -0.16),
			Vector3(SILL_TOOLS_EDGE, 0.14, 0.05), Vector3(SILL_TOOLS_EDGE, 0, -0.26), Vector3(-0.2, 0, -0.26)]:
		pts.append(sx * p)
	# The lamp over the bonsai.
	pts.append(_in_shed(bonsai_lamp) + Vector3(0, 0.05, 0))
	return pts


## A node's transform in the shed's own frame (built up from its parents, in the tree or not).
func _xf_in_shed(n: Node3D) -> Transform3D:
	var xf := n.transform
	var p := n.get_parent()
	while p != null and p != self:
		xf = (p as Node3D).transform * xf
		p = p.get_parent()
	return xf


func _in_shed(n: Node3D) -> Vector3:
	return _xf_in_shed(n).origin


func frame_tree(tree_height: float, env: Environment) -> void:
	_place_camera(tree_height)
	camera.environment = env


func _process(delta: float) -> void:
	_time += delta
	# The screen may turn or change size (a PC window): the view fits it again.
	fit_view()
	_apply_lean()
	# The lantern flickers a little.
	# By night the lantern is the room's light; by day it is only a warm touch.
	# (The phone's renderer lit the bench too brightly by day: a softer lamp there by day.)
	var lamp := (lerpf(2.2, 1.0, daylight) if _lamp_base <= 1.0 else lerpf(1.5, 0.6, daylight)) * _lamp_base
	_lamp.light_energy = lamp * (1.0 + 0.1 * sin(_time * 7.3) + 0.05 * sin(_time * 13.1))
	# By night its light is a warm pool on the bench that falls off into dark walls; by day it
	# reaches the whole room (0.6.3 review: at night the room was lit almost as by day).
	_lamp.omni_range = lerpf(2.8, _lamp_range, daylight)
	_lamp.omni_attenuation = lerpf(2.6, 1.0, daylight)
	if _glass:
		# The old glass mirrors the bright sky by day, not the dark night.
		_glass.metallic_specular = lerpf(0.05, 0.5, daylight)
	if _window_sun:
		_window_sun.light_energy = 0.45 * daylight
		_window_sun.light_color = Color(0.7, 0.78, 1.0).lerp(Color(1.0, 0.9, 0.72), daylight)
	_set_bonsai_lamp(clampf(1.0 - daylight, 0.0, 1.0))
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
		"gloves": "shed_gloves.ogg", "options": "shed_pin.ogg", "door": "shed_door.ogg",
		"bonsai": "shed_clay_pot.ogg", "drawer": "shed_door.ogg"}
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
