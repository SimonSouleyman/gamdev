class_name BonsaiTools
extends Node3D
## The bonsai's tools as real things on the windowsill (0.7, docs/notes/bonsai-tools-0.7.md):
## the watering can and the pellet tin beside the pot, a front row of secateurs, tweezers, a
## coil of copper wire, the trowel and the style sketchbook, two arrows carved into the board
## for turning the pot, the album card tucked into the window frame and a small box of
## cuttings. A tool is picked up (it lifts and then follows the pointer) and put down again;
## the others answer a tap. Only the things and their places: BonsaiView does the input and the
## care. Lies in the sill's frame of the pot's base (+Z toward the window, +X on screen left).

## Tools that are picked up, and things that answer a tap. The ids of the held tools are the
## care kinds BonsaiView.tool knows ("pinch" are the tweezers, "fertiliser" the pellet tin).
const HELD: Array[String] = ["water", "fertiliser", "shears", "pinch", "wire", "trowel"]
const TAPPED: Array[String] = ["turn_left", "turn_right", "styles", "album", "cuttings"]
## Their paper labels (first time, and with "clearer print").
const LABELS := {
	"water": "watering can", "fertiliser": "pellets N P K", "shears": "shears",
	"pinch": "tweezers", "wire": "copper wire", "trowel": "trowel", "turn_left": "turn",
	"turn_right": "turn", "styles": "styles", "album": "album page", "cuttings": "cuttings",
}
## Where the tools rest (position, yaw[, pitch]): the can and the tin beside the pot (clear of the
## widest pot), a front row of the four hand tools and the box of cuttings along the board's edge,
## and the album card and the sketchbook tucked into the window frame either side (0.8.1, item 18:
## the front row lost the sketchbook, so its five things lie 6 cm apart instead of 5, about 12 mm
## on the phone; tests/test_bonsai.gd checks it at 720 x 1600 and 720 x 1280).
const FRONT_Z := -0.24
const ROW_GAP := 0.06
const RESTS := {
	"water": [Vector3(0.165, 0.0, 0.055), -0.75],
	"fertiliser": [Vector3(-0.15, 0.0, 0.03), 0.3],
	"trowel": [Vector3(2.0 * ROW_GAP, 0.0, FRONT_Z + 0.01), 0.3],
	"shears": [Vector3(ROW_GAP, 0.0, FRONT_Z), -0.45],
	"pinch": [Vector3(0.0, 0.0, FRONT_Z), 0.2],
	"wire": [Vector3(-ROW_GAP, 0.0, FRONT_Z), 0.0],
	"cuttings": [Vector3(-2.0 * ROW_GAP - 0.005, 0.0, FRONT_Z + 0.01), 1.5],
	"album": [Vector3(0.15, 0.19, 0.03), 0.0],
	"styles": [Vector3(-0.148, 0.165, 0.035), 0.12, -1.2],
}
## The carved arrows: arcs round the pot's front, at this radius.
const ARROW_R := 0.135
const ARROW_SPAN := Vector2(0.28, 0.82)
## Labels hang under their thing; these hang lower, so neighbours do not overlap.
const LABEL_DROP := {"shears": 44.0, "wire": 44.0}
## How a held tool is held (0.8.1, item 18): its working end ("tip": the can's rose, the tin's
## mouth, the blades' points, the trowel's blade, the wire's loose end) sits on the aim point, and
## its body ("grip") reaches up and to the side, away from the finger, so a finger on the phone
## never hides the tool and the tool never hides what it aims at. "face" turns toward the eye.
## In the thing's own frame; "scale" shrinks the big can in the hand.
const HOLD := {
	"water": {"tip": Vector3(0.0, 0.061, 0.103), "grip": Vector3(0.0, 0.03, -0.04), "face": Vector3(1, 0, 0), "scale": 0.85},
	"fertiliser": {"tip": Vector3(0.0, 0.055, 0.0), "grip": Vector3(0.0, 0.0, 0.0), "face": Vector3(0, 0, -1), "scale": 1.0},
	"shears": {"tip": Vector3(0.0, 0.005, 0.037), "grip": Vector3(0.0, 0.005, -0.04), "face": Vector3(0, 1, 0), "scale": 1.0},
	"pinch": {"tip": Vector3(0.0, 0.003, 0.038), "grip": Vector3(0.0, 0.003, -0.04), "face": Vector3(0, 1, 0), "scale": 1.0},
	"wire": {"tip": Vector3(0.043, 0.012, -0.02), "grip": Vector3(-0.01, 0.01, 0.01), "face": Vector3(0, 1, 0), "scale": 1.0},
	"trowel": {"tip": Vector3(0.0, 0.017, 0.07), "grip": Vector3(0.0, 0.017, -0.045), "face": Vector3(0, 1, 0), "scale": 1.0},
}
## Screen direction from a held tool's tip to its body: up and a little to the right (a right
## hand's finger and palm lie below and to the right of the touch, the body clears both).
const HOLD_DIR := Vector2(0.45, -0.9)

var items: Dictionary = {}  # id -> Node3D
var rests: Dictionary = {}  # id -> Transform3D
## The point each thing answers a tap at (its visual middle), id -> Node3D.
var marks: Dictionary = {}
## Each thing's box in its own frame (for the tap hull on screen), id -> AABB.
var boxes: Dictionary = {}
## Where the tool in hand goes back: a dashed ring on the board at its place (0.8.1).
var ghost: MeshInstance3D
## The tool in hand ("" = none).
var held: String = ""
var _returning: Dictionary = {}
## The water's stream (under BonsaiView's base, not in the can, so it falls straight).
var water_fx: CPUParticles3D


func _ready() -> void:
	_build_can()
	_build_tin()
	_build_trowel()
	_build_shears()
	_build_tweezers()
	_build_wire()
	_build_sketchbook()
	_build_album()
	_build_cuttings()
	_build_arrows()
	for id in items:
		var n: Node3D = items[id]
		boxes[id] = _local_box(n)
		if not RESTS.has(id):
			continue
		var r: Array = RESTS[id]
		n.position = r[0]
		n.rotation = Vector3(float(r[2]) if r.size() > 2 else 0.0, r[1], 0.0)
		rests[id] = n.transform
	_build_ghost()


## The screen points of the things that can be tapped now, id -> Vector2 (skips the hidden
## ones and those behind the camera).
func screen_points(camera: Camera3D) -> Dictionary:
	var out := {}
	for id in marks:
		var m: Node3D = marks[id]
		if not m.is_visible_in_tree() or _returning.has(id):
			continue
		var p := m.global_position
		if camera.is_position_behind(p):
			continue
		out[id] = camera.unproject_position(p)
	return out


## The place a held tool came from, on screen (a tap there puts it down).
func rest_point(camera: Camera3D, id: String) -> Vector2:
	return camera.unproject_position(_rest_global(id, marks[id]))


## Where a thing's tap mark is when it lies at its place.
func _rest_global(id: String, m: Node3D) -> Vector3:
	var item: Node3D = items[id]
	var in_item := item.global_transform.affine_inverse() * m.global_position
	return global_transform * (rests[id] as Transform3D) * in_item


## The thing under a screen point, or "": a tap inside a thing's outline on screen (its box,
## grown by HULL_GROW) or within `radius` of its mark finds it; of several, the nearest mark wins.
## A held tool answers at its place (within `put_radius`, its put-down target), and where it
## floats only when `held_too` (a tap on the tree uses it instead). Without `outlines` only the
## marks answer (over the pot, whose own outline wins).
func pick(camera: Camera3D, screen: Vector2, radius: float, held_too: bool = true, put_radius: float = -1.0, outlines: bool = true) -> String:
	var best := ""
	var best_d := INF
	var pts := screen_points(camera)
	if held != "" and marks.has(held):
		var d := rest_point(camera, held).distance_to(screen)
		if d < (put_radius if put_radius > 0.0 else radius):
			best_d = d
			best = held
		if not held_too:
			pts.erase(held)
	for id in pts:
		var d := (pts[id] as Vector2).distance_to(screen)
		var inside := outlines and Geometry2D.is_point_in_polygon(screen, hull(camera, id))
		if (d < radius or inside) and d < best_d:
			best_d = d
			best = id
	return best


## How far a thing's outline reaches beyond its box on screen (canvas pixels): a finger's edge.
const HULL_GROW := 10.0


## A thing's outline on screen: the convex hull of its box's corners, grown a little.
func hull(camera: Camera3D, id: String) -> PackedVector2Array:
	var n: Node3D = items[id]
	var box: AABB = boxes[id]
	var pts := PackedVector2Array()
	for i in range(8):
		var p := n.global_transform * box.get_endpoint(i)
		if camera.is_position_behind(p):
			return PackedVector2Array()
		pts.append(camera.unproject_position(p))
	var h := Geometry2D.convex_hull(pts)
	if h.size() < 3:
		return PackedVector2Array()
	var grown := Geometry2D.offset_polygon(h, HULL_GROW)
	return grown[0] if not grown.is_empty() else h


## A thing's box in its own frame (all its meshes).
func _local_box(n: Node3D) -> AABB:
	var acc: Array = [null]
	for c in n.get_children():
		_gather(c, Transform3D(), acc)
	return acc[0] if acc[0] != null else AABB(Vector3(-0.02, 0, -0.02), Vector3(0.04, 0.02, 0.04))


func _gather(n: Node, xf: Transform3D, acc: Array) -> void:
	var t := xf
	if n is Node3D:
		t = xf * (n as Node3D).transform
	if n is MeshInstance3D and (n as MeshInstance3D).mesh != null:
		var a := t * (n as MeshInstance3D).get_aabb()
		acc[0] = a if acc[0] == null else (acc[0] as AABB).merge(a)
	for c in n.get_children():
		_gather(c, t, acc)


func hold(id: String) -> void:
	held = id
	_place_ghost()


func put_down() -> void:
	if held == "":
		return
	var id := held
	held = ""
	_place_ghost()
	# On its way back it answers no tap (it may pass over the tree).
	_returning[id] = true
	var tw := create_tween()
	tw.tween_property(items[id], "transform", rests[id], 0.35).set_trans(Tween.TRANS_SINE)
	tw.tween_callback(func() -> void: _returning.erase(id))


## Puts every tool back at once (a new bonsai, a test).
func reset() -> void:
	held = ""
	_place_ghost()
	_returning.clear()
	for id in rests:
		(items[id] as Node3D).transform = rests[id]


## Moves the held tool a step toward `target` (in this node's frame).
func follow(target: Transform3D, delta: float) -> void:
	if held == "":
		return
	var n: Node3D = items[held]
	n.transform = n.transform.interpolate_with(target, clampf(delta * 14.0, 0.0, 1.0))


## The held tool just lifted where it lies, tilted a little.
func lifted_pose(id: String) -> Transform3D:
	var r: Transform3D = rests[id]
	return Transform3D(r.basis.rotated(r.basis.x.normalized(), -0.18), r.origin + Vector3(0, 0.035, 0))


## The held tool's pose (this node's frame) with its working end on `aim` (a point in this
## node's frame), its body reaching toward the screen direction HOLD_DIR, its face to the eye.
## `right` and `up` are the camera's axes and `eye` its position, in this node's frame.
func hold_pose(id: String, aim: Vector3, right: Vector3, up: Vector3, eye: Vector3) -> Transform3D:
	var h: Dictionary = HOLD.get(id, {"tip": Vector3.ZERO, "grip": Vector3(0, 0, -0.04), "face": Vector3.UP, "scale": 1.0})
	var s: float = h["scale"]
	var tip: Vector3 = h["tip"]
	var lx: Vector3 = ((h["grip"] as Vector3) - tip).normalized()
	var lf: Vector3 = h["face"]
	var lz := (lf - lx * lf.dot(lx)).normalized()
	var ly := lz.cross(lx)
	var to_eye := (eye - aim).normalized()
	# Up and to the side on screen, and a little toward the eye, so it reads in front of the tree.
	var wx := (right * HOLD_DIR.x - up * HOLD_DIR.y + to_eye * 0.35).normalized()
	var wz := (to_eye - wx * to_eye.dot(wx)).normalized()
	var wy := wz.cross(wx)
	var basis := Basis(wx, wy, wz) * Basis(lx, ly, lz).transposed()
	basis = basis.scaled_local(Vector3.ONE * s)
	return Transform3D(basis, aim - basis * tip)


## The working end of the tool `id`, in this node's frame (where its action lands).
func tip_point(id: String) -> Vector3:
	var h: Dictionary = HOLD.get(id, {"tip": Vector3.ZERO})
	return (items[id] as Node3D).transform * (h["tip"] as Vector3)


## The dashed ring on the board where the tool in hand goes back (shown while one is held).
func _build_ghost() -> void:
	ghost = MeshInstance3D.new()
	ghost.name = "put_back"
	var q := QuadMesh.new()
	q.size = Vector2(0.07, 0.07)
	q.orientation = PlaneMesh.FACE_Y
	ghost.mesh = q
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	m.albedo_texture = ring_texture(true)
	m.albedo_color = Color(0.98, 0.9, 0.7, 0.85)
	m.cull_mode = BaseMaterial3D.CULL_DISABLED
	ghost.material_override = m
	ghost.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	ghost.visible = false
	add_child(ghost)


func _place_ghost() -> void:
	if ghost == null:
		return
	ghost.visible = held != "" and rests.has(held)
	if not ghost.visible:
		return
	var r: Transform3D = rests[held]
	var box: AABB = boxes[held]
	# Round the thing's footprint, flat on the board.
	var size := clampf(maxf(box.size.x, box.size.z) * 0.8, 0.065, 0.11)
	var centre := r * box.get_center()
	ghost.transform = Transform3D(Basis().scaled(Vector3(size / 0.07, 1.0, size / 0.07)), Vector3(centre.x, 0.0015, centre.z))


static var _rings: Dictionary = {}


## A soft ring (dashed for the put-back mark) to lay round a target: clear inside and out.
static func ring_texture(dashed: bool) -> Texture2D:
	if _rings.has(dashed):
		return _rings[dashed]
	var n := 128
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	for y in range(n):
		for x in range(n):
			var d := Vector2(x + 0.5 - n * 0.5, y + 0.5 - n * 0.5)
			var r := d.length() / (n * 0.5)
			var a := clampf(1.0 - absf(r - 0.86) / 0.07, 0.0, 1.0)
			if dashed and fposmod(atan2(d.y, d.x) / TAU * 16.0, 1.0) > 0.6:
				a = 0.0
			img.set_pixel(x, y, Color(1, 1, 1, a))
	img.generate_mipmaps()
	var tex := ImageTexture.create_from_image(img)
	_rings[dashed] = tex
	return tex


# --- building ------------------------------------------------------------------------------

func _add(id: String, n: Node3D, mark_at: Vector3) -> void:
	n.name = id
	add_child(n)
	items[id] = n
	var m := Node3D.new()
	m.name = "mark"
	m.position = mark_at
	n.add_child(m)
	marks[id] = m


static func _mat(c: Color, rough: float = 0.8, metal: float = 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	m.metallic = metal
	return m


static func _box(parent: Node3D, size: Vector3, at: Vector3, mat: Material, rot := Vector3.ZERO) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.material_override = mat
	mi.position = at
	mi.rotation = rot
	parent.add_child(mi)
	return mi


static func _cyl(parent: Node3D, r: float, h: float, at: Vector3, mat: Material, rot := Vector3.ZERO, segs: int = 12) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var cm := CylinderMesh.new()
	cm.top_radius = r
	cm.bottom_radius = r
	cm.height = h
	cm.radial_segments = segs
	cm.rings = 1
	mi.mesh = cm
	mi.material_override = mat
	mi.position = at
	mi.rotation = rot
	parent.add_child(mi)
	return mi


func _build_can() -> void:
	var can := (load("res://assets/shed/watering_can_metal_01/watering_can_metal_01_1k.gltf") as PackedScene).instantiate() as Node3D
	var n := Node3D.new()
	can.scale = Vector3.ONE * 0.38
	n.add_child(can)
	_add("water", n, Vector3(0.0, 0.07, 0.0))
	water_fx = CPUParticles3D.new()
	water_fx.emitting = false
	water_fx.amount = 160
	water_fx.lifetime = 0.5
	water_fx.direction = Vector3(0, -1, 0)
	water_fx.spread = 6.0
	water_fx.initial_velocity_min = 0.25
	water_fx.initial_velocity_max = 0.4
	water_fx.gravity = Vector3(0, -3.0, 0)
	water_fx.emission_shape = CPUParticles3D.EMISSION_SHAPE_SPHERE
	water_fx.emission_sphere_radius = 0.012
	var drop := SphereMesh.new()
	drop.radius = 0.0016
	drop.height = 0.007
	drop.radial_segments = 5
	drop.rings = 2
	water_fx.mesh = drop
	var wm := StandardMaterial3D.new()
	wm.albedo_color = Color(0.8, 0.88, 0.98, 0.55)
	wm.metallic_specular = 1.0
	wm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	wm.roughness = 0.05
	water_fx.material_override = wm
	water_fx.local_coords = false
	add_child(water_fx)


## The fertiliser tin: an old tin with a paper label "N P K" and its lid.
func _build_tin() -> void:
	var tin := Node3D.new()
	var tin_mat := _mat(Color(0.5, 0.52, 0.5), 0.45, 0.8)
	_cyl(tin, 0.026, 0.05, Vector3(0, 0.025, 0), tin_mat, Vector3.ZERO, 20)
	var label := _cyl(tin, 0.0265, 0.028, Vector3(0, 0.024, 0), null, Vector3.ZERO, 20)
	var paper := _mat(Color(0.92, 0.84, 0.66), 0.95)
	paper.albedo_texture = load("res://assets/paper/paper_beige.png")
	label.material_override = paper
	var words := Label3D.new()
	words.text = "N P K"
	words.font = Paper.hand_font(true)
	words.font_size = 48
	words.pixel_size = 0.00032
	words.modulate = Paper.INK
	words.outline_size = 0
	words.shaded = true
	words.position = Vector3(0, 0.025, -0.0275)
	words.rotation.y = PI
	tin.add_child(words)
	_cyl(tin, 0.0275, 0.006, Vector3(0, 0.052, 0), tin_mat, Vector3.ZERO, 20)
	_add("fertiliser", tin, Vector3(0, 0.03, 0))


## The Poly Haven trowel, small, lying along the board toward the window.
func _build_trowel() -> void:
	var t := (load("res://assets/shed/trowel_01/trowel_01_1k.gltf") as PackedScene).instantiate() as Node3D
	var n := Node3D.new()
	t.scale = Vector3.ONE * 0.36
	t.position = Vector3(0, 0.017, 0)
	n.add_child(t)
	_add("trowel", n, Vector3(0, 0.012, 0.0))


## Bonsai secateurs: two short curved steel blades on a pivot and two red-sleeved handles.
func _build_shears() -> void:
	var s := Node3D.new()
	var steel := _mat(Color(0.72, 0.73, 0.75), 0.3, 0.9)
	var grip := _mat(Color(0.62, 0.12, 0.08), 0.6)
	# Lying flat; the blades point toward the window (+Z), the handles toward the room.
	for side: float in [-1.0, 1.0]:
		var blade := _box(s, Vector3(0.009, 0.004, 0.034), Vector3(side * 0.004, 0.004, 0.02), steel, Vector3(0, side * 0.12, 0))
		blade.name = "blade"
		_box(s, Vector3(0.01, 0.007, 0.05), Vector3(side * 0.012, 0.005, -0.024), grip, Vector3(0, -side * 0.28, 0))
	_cyl(s, 0.005, 0.01, Vector3(0, 0.005, 0.002), steel)
	# The spring between the handles.
	_cyl(s, 0.0015, 0.016, Vector3(0, 0.005, -0.01), steel, Vector3(0, 0, PI * 0.5))
	_add("shears", s, Vector3(0, 0.006, 0.0))


## Bonsai tweezers: two long thin steel legs joined at the back, a little spatula at the end.
func _build_tweezers() -> void:
	var t := Node3D.new()
	var steel := _mat(Color(0.2, 0.2, 0.21), 0.35, 0.85)
	for side: float in [-1.0, 1.0]:
		_box(t, Vector3(0.0035, 0.003, 0.075), Vector3(side * 0.0045, 0.003, 0.0), steel, Vector3(0, side * 0.07, 0))
	_box(t, Vector3(0.012, 0.004, 0.012), Vector3(0, 0.003, -0.04), steel)
	_add("pinch", t, Vector3(0, 0.004, 0.0))


## A coil of copper wire, a loose end sticking out.
func _build_wire() -> void:
	var w := Node3D.new()
	var copper := _mat(Color(0.74, 0.42, 0.24), 0.35, 0.9)
	for i in range(6):
		var ring := MeshInstance3D.new()
		var tm := TorusMesh.new()
		tm.inner_radius = 0.0205 + (i % 3) * 0.0012
		tm.outer_radius = tm.inner_radius + 0.0032
		tm.rings = 24
		tm.ring_segments = 6
		ring.mesh = tm
		ring.material_override = copper
		ring.position = Vector3((i % 2) * 0.0012, 0.0018 + i * 0.0026, (i % 3) * 0.001)
		ring.rotation = Vector3(0.05 * (i % 2), 0.0, 0.04 * ((i + 1) % 2))
		w.add_child(ring)
	_cyl(w, 0.0016, 0.035, Vector3(0.028, 0.012, -0.012), copper, Vector3(0, 0.5, PI * 0.5), 5)
	_add("wire", w, Vector3(0, 0.01, 0.0))


## A small sketchbook lying open: two style drawings on its pages.
func _build_sketchbook() -> void:
	var b := Node3D.new()
	var cover := _mat(Color(0.2, 0.26, 0.22), 0.9)
	_box(b, Vector3(0.078, 0.004, 0.056), Vector3(0, 0.002, 0), cover)
	for side: float in [-1.0, 1.0]:
		var page := MeshInstance3D.new()
		var q := QuadMesh.new()
		q.size = Vector2(0.036, 0.052)
		page.mesh = q
		var pm := _mat(Color(0.97, 0.94, 0.86), 0.95)
		pm.albedo_texture = load("res://ui/bonsai_styles/%s.png" % ("informal_upright" if side < 0.0 else "cascade"))
		page.material_override = pm
		# Lying face up, bottom edge toward the room, each page tipped toward the spine.
		page.rotation = Vector3(-PI * 0.5, 0.0, 0.0)
		page.position = Vector3(side * 0.0185, 0.0055, 0.0)
		var holder := Node3D.new()
		holder.rotation.z = side * 0.06
		holder.add_child(page)
		b.add_child(holder)
	# A pencil across the pages.
	_cyl(b, 0.0017, 0.06, Vector3(0.004, 0.009, -0.012), _mat(Color(0.85, 0.65, 0.2), 0.6), Vector3(0, 0.35, PI * 0.5), 6)
	_add("styles", b, Vector3(0, 0.006, 0.0))


## The bonsai's album page: a small card tucked into the window frame, "my bonsai" and a sketch.
func _build_album() -> void:
	var c := Node3D.new()
	var card := MeshInstance3D.new()
	var q := QuadMesh.new()
	q.size = Vector2(0.056, 0.072)
	card.mesh = q
	var pm := _mat(Color(0.96, 0.92, 0.82), 0.95)
	pm.albedo_texture = load("res://assets/paper/paper_cream.png")
	card.material_override = pm
	# Facing the room (-Z), leaning back against the frame.
	card.rotation = Vector3(-0.12, PI, 0.08)
	c.add_child(card)
	var words := Label3D.new()
	words.text = "my bonsai"
	words.font = Paper.hand_font(true)
	words.font_size = 40
	words.pixel_size = 0.00034
	words.modulate = Paper.INK
	words.outline_size = 0
	words.shaded = true
	words.position = Vector3(0, -0.024, -0.003)
	words.rotation = Vector3(0.12, PI, -0.08)
	c.add_child(words)
	var pic := MeshInstance3D.new()
	var pq := QuadMesh.new()
	pq.size = Vector2(0.04, 0.04)
	pic.mesh = pq
	var sm := _mat(Color(1, 1, 1), 0.95)
	sm.albedo_texture = load("res://ui/bonsai_styles/broom.png")
	sm.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR
	pic.material_override = sm
	pic.position = Vector3(0, 0.008, -0.002)
	pic.rotation = Vector3(-0.12, PI, 0.08)
	c.add_child(pic)
	_add("album", c, Vector3(0, 0.0, 0.0))


## A little wooden box (Poly Haven "Cheese Box 01") holding the cuttings: twigs with a leaf or two.
func _build_cuttings() -> void:
	var box := (load("res://assets/shed/cheese_box_01/CheeseBox_01_1k.gltf") as PackedScene).instantiate() as Node3D
	var n := Node3D.new()
	box.scale = Vector3.ONE * 0.36
	n.add_child(box)
	# The lid is off (0.7 review: leaning at the side, edge-on at the screen's rim, it read as a
	# stray unlabelled stick).
	var lid := box.find_child("CheeseBox_01_lid", true, false) as Node3D
	if lid != null:
		lid.visible = false
	var bark := _mat(Color(0.36, 0.26, 0.18), 0.9)
	var leaf := _mat(Color(0.3, 0.46, 0.2), 0.8)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	for i in range(5):
		var x := -0.03 + i * 0.015
		var tw := _cyl(n, 0.0017, 0.06, Vector3(x, 0.035, rng.randf_range(-0.006, 0.006)), bark, Vector3(rng.randf_range(-0.25, 0.25), 0.0, rng.randf_range(-0.35, 0.35)), 5)
		var tip := MeshInstance3D.new()
		var sm := SphereMesh.new()
		sm.radius = 0.006
		sm.height = 0.009
		sm.radial_segments = 6
		sm.rings = 3
		tip.mesh = sm
		tip.material_override = leaf
		tip.position = Vector3(0, 0.03, 0)
		tw.add_child(tip)
	_add("cuttings", n, Vector3(0, 0.03, 0.0))


## Two arrows carved into the board, curving round the pot's front: turn it this way.
func _build_arrows() -> void:
	var carved := _mat(Color(0.2, 0.14, 0.09), 1.0)
	for side: float in [1.0, -1.0]:
		var id := "turn_left" if side > 0.0 else "turn_right"
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		st.set_normal(Vector3.UP)
		var w := 0.006
		var steps := 10
		var a0 := ARROW_SPAN.x
		var a1 := ARROW_SPAN.y - 0.1
		for k in range(steps):
			var fa: float = side * lerpf(a0, a1, float(k) / steps)
			var fb: float = side * lerpf(a0, a1, float(k + 1) / steps)
			var pa := _arc(fa)
			var pb := _arc(fb)
			var na := pa.normalized() * w
			var nb := pb.normalized() * w
			st.add_vertex(pa - na)
			st.add_vertex(pb - nb)
			st.add_vertex(pb + nb)
			st.add_vertex(pa - na)
			st.add_vertex(pb + nb)
			st.add_vertex(pa + na)
		# The head.
		var tip := _arc(side * ARROW_SPAN.y)
		var base := _arc(side * a1)
		var out := base.normalized() * w * 2.8
		st.add_vertex(base - out)
		st.add_vertex(tip)
		st.add_vertex(base + out)
		var mesh := st.commit()
		var mi := MeshInstance3D.new()
		mi.mesh = mesh
		mi.material_override = carved
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		# Both faces: which way the winding faces depends on the side.
		carved.cull_mode = BaseMaterial3D.CULL_DISABLED
		var n := Node3D.new()
		n.position = Vector3(0, 0.0012, 0)
		n.add_child(mi)
		_add(id, n, _arc(side * (ARROW_SPAN.x + ARROW_SPAN.y) * 0.5))


## A point on the carved arc round the pot's front (0 is straight in front, + toward +X).
static func _arc(a: float) -> Vector3:
	return Vector3(sin(a) * ARROW_R, 0.0, -cos(a) * ARROW_R)
