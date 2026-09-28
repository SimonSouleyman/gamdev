class_name Shed
extends Node3D
## The garden shed (Simon, play test 3; menus as whole scenes, like Plants vs. Zombies): the
## start menu and the place to pause. It stands at the north edge of the clearing with its door
## open toward the tree, so the player's real tree, grown so far, is the big picture in the doorway.
## On the workbench: the journal, the photo album and the seed bag; on the wall a pinboard with
## the options and a handwritten note with the menu. Built from simple shapes and procedural wood.

signal continue_pressed
signal journal_pressed
signal album_pressed
signal options_pressed

## Where the shed stands: the south edge of the clearing (behind the default view of the tree),
## the door facing north to the tree, which the sun lights from behind the shed.
## It moves out with the edge as the clearing grows.
static var origin := Vector3(0.0, 0.0, 15.5)
const WIDTH := 3.2
const DEPTH := 2.8
const WALL_H := 2.4

var camera: Camera3D
var _items: Dictionary = {}  # name -> Node3D (tappable)
var _wood: ShaderMaterial
var _time: float = 0.0
var _lamp: OmniLight3D
var menu: Control  # 2D handwritten menu note, added to a CanvasLayer by the owner


func _ready() -> void:
	place()
	rotation.y = PI
	_wood = ShaderMaterial.new()
	_wood.shader = preload("res://shed/wood.gdshader")
	_build_room()
	_build_bench()
	_build_pinboard()
	_build_camera()


## Stands the shed at the current edge of the clearing.
func place() -> void:
	position = Terrain.at(origin)


func _box(size: Vector3, at: Vector3, mat: Material, parent: Node3D = self) -> MeshInstance3D:
	var m := MeshInstance3D.new()
	var b := BoxMesh.new()
	b.size = size
	m.mesh = b
	m.material_override = mat
	m.position = at
	parent.add_child(m)
	return m


func _mat(c: Color, rough: float = 0.8) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.albedo_color = c
	m.roughness = rough
	return m


func _build_room() -> void:
	var hw := WIDTH * 0.5
	var hd := DEPTH * 0.5
	var t := 0.08
	# Floor boards and walls: back, left, right, and the front wall with the open door.
	_box(Vector3(WIDTH, 0.1, DEPTH), Vector3(0, 0.05, 0), _wood)
	_box(Vector3(WIDTH, WALL_H, t), Vector3(0, WALL_H * 0.5, -hd), _wood)
	_box(Vector3(t, WALL_H, DEPTH), Vector3(-hw, WALL_H * 0.5, 0), _wood)
	_box(Vector3(t, WALL_H, DEPTH), Vector3(hw, WALL_H * 0.5, 0), _wood)
	var door_w := 1.3
	var side := (WIDTH - door_w) * 0.5
	_box(Vector3(side, WALL_H, t), Vector3(-hw + side * 0.5, WALL_H * 0.5, hd), _wood)
	_box(Vector3(side, WALL_H, t), Vector3(hw - side * 0.5, WALL_H * 0.5, hd), _wood)
	_box(Vector3(door_w, WALL_H - 2.05, t), Vector3(0, 2.05 + (WALL_H - 2.05) * 0.5, hd), _wood)
	# The door itself, swung open against the outside of the front wall.
	var door := _box(Vector3(door_w, 2.0, 0.05), Vector3(0, 0, 0), _wood)
	door.rotation.y = 1.25
	door.position = Vector3(door_w * 0.5 + 0.35, 1.0, hd + 0.62)
	# A window in the left wall: a frame of four slats.
	var frame := _mat(Color(0.35, 0.25, 0.16))
	for y in [1.1, 1.8]:
		_box(Vector3(0.1, 0.06, 0.9), Vector3(-hw + 0.02, y, -0.2), frame)
	# The gable ends above the front and back walls.
	for z in [-hd, hd]:
		var tri := MeshInstance3D.new()
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for v in [Vector3(-hw, WALL_H, z), Vector3(hw, WALL_H, z), Vector3(0, WALL_H + 0.72, z)]:
			st.add_vertex(v)
		st.generate_normals()
		tri.mesh = st.commit()
		var gm := _wood.duplicate() as ShaderMaterial
		tri.material_override = gm
		tri.set("material_override", gm)
		tri.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		add_child(tri)
		# Visible from both sides.
		var back := tri.duplicate() as MeshInstance3D
		back.scale = Vector3(-1, 1, 1)
		add_child(back)
	# Rafters, and a few things hanging from them.
	for x in [-1.0, 0.0, 1.0]:
		_box(Vector3(0.08, 0.1, DEPTH), Vector3(x, WALL_H + 0.05, 0), _mat(Color(0.3, 0.21, 0.13)))
	var twine := _box(Vector3(0.02, 0.5, 0.02), Vector3(-0.6, WALL_H - 0.25, 0.3), _mat(Color(0.6, 0.5, 0.3)))
	for k in range(3):
		_box(Vector3(0.12, 0.25, 0.04), Vector3(0.35 + k * 0.18, WALL_H - 0.2, -0.3), _mat(Color(0.35, 0.42, 0.2).lerp(Color(0.5, 0.45, 0.25), k * 0.3)))
	var rake := _box(Vector3(0.03, 1.5, 0.03), Vector3(-WIDTH * 0.5 + 0.2, 0.8, -0.9), _mat(Color(0.45, 0.33, 0.2)))
	rake.rotation.z = 0.12
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
	_lamp.position = Vector3(-1.1, WALL_H - 0.45, -0.95)
	add_child(_lamp)
	var glass := MeshInstance3D.new()
	var s := SphereMesh.new()
	s.radius = 0.06
	s.height = 0.14
	glass.mesh = s
	var gm := StandardMaterial3D.new()
	gm.albedo_color = Color(1.0, 0.85, 0.55)
	gm.emission_enabled = true
	gm.emission = Color(1.0, 0.75, 0.4)
	gm.emission_energy_multiplier = 3.0
	glass.material_override = gm
	glass.position = _lamp.position
	add_child(glass)


func _build_bench() -> void:
	var hd := DEPTH * 0.5
	var bench := Node3D.new()
	# Along the right wall near the door, so the things on it are in view of the doorway.
	# Just inside the door on the left of the view (local +x after the shed turns to face the tree).
	bench.position = Vector3(0.62, 0, 0.62)
	bench.rotation.y = PI
	bench.scale = Vector3(0.6, 0.8, 0.6)
	add_child(bench)
	_box(Vector3(1.6, 0.06, 0.7), Vector3(0, 0.9, 0), _wood, bench)
	for x in [-0.72, 0.72]:
		for z in [-0.28, 0.28]:
			_box(Vector3(0.06, 0.9, 0.06), Vector3(x, 0.45, z), _wood, bench)
	# The journal: dark leather, a paper edge showing.
	var journal := _box(Vector3(0.34, 0.06, 0.26), Vector3(-0.45, 0.96, 0.05), _mat(Paper.LEATHER, 0.6), bench)
	journal.rotation.y = 0.2
	_box(Vector3(0.32, 0.04, 0.24), Vector3(0.01, 0.0, 0.0), _mat(Paper.PAPER), journal).position.y = 0.0
	_items["journal"] = journal
	# The photo album: green cloth.
	var album := _box(Vector3(0.36, 0.07, 0.3), Vector3(0.05, 0.965, -0.05), _mat(Color(0.25, 0.38, 0.24), 0.9), bench)
	album.rotation.y = -0.15
	_items["album"] = album
	# The seed bag: a small paper sack.
	var seeds := _box(Vector3(0.14, 0.2, 0.08), Vector3(0.5, 1.03, 0.1), _mat(Color(0.78, 0.66, 0.46), 0.95), bench)
	seeds.rotation.y = 0.4
	_items["seeds"] = seeds
	# A clay pot with a seedling, and a watering can, for the feel of the place.
	var pot := MeshInstance3D.new()
	var cyl := CylinderMesh.new()
	cyl.top_radius = 0.09
	cyl.bottom_radius = 0.065
	cyl.height = 0.14
	pot.mesh = cyl
	pot.material_override = _mat(Color(0.62, 0.33, 0.2))
	pot.position = Vector3(0.7, 1.0, -0.2)
	bench.add_child(pot)
	var can := _box(Vector3(0.22, 0.2, 0.14), Vector3(-0.15, 0.1, 0.4), _mat(Color(0.35, 0.45, 0.42), 0.4))
	can.position = Vector3(1.1, 0.1, -0.9)


func _build_pinboard() -> void:
	var hw := WIDTH * 0.5
	# On the inside of the front wall, left of the door.
	# On the right-hand wall, near the door, in view.
	var board := _box(Vector3(0.05, 0.62, 0.7), Vector3(-WIDTH * 0.5 + 0.07, 1.6, 0.55), _mat(Color(0.55, 0.42, 0.28), 0.95))
	# Paper notes pinned to it.
	for i in range(4):
		var note := _box(Vector3(0.01, 0.2, 0.24), Vector3(0.03, 0.2 - (i / 2) * 0.32, -0.25 + (i % 2) * 0.45), _mat(Paper.PAPER), board)
		note.rotation.x = 0.05 * (i - 1.5)
	_items["options"] = board


func _build_camera() -> void:
	camera = Camera3D.new()
	camera.fov = 70.0
	camera.near = 0.03
	camera.far = 400.0
	add_child(camera)
	_place_camera(3.0)


## The eye stands at the back of the shed, looking out of the door at the tree.
func _place_camera(tree_height: float) -> void:
	camera.position = Vector3(0.1, 1.45, -DEPTH * 0.5 + 0.5)
	# Look out through the doorway at about eye height: bench, board and door frame the tree.
	camera.look_at(to_global(Vector3(0.0, 1.25, DEPTH * 0.5)), Vector3.UP)


func frame_tree(tree_height: float, env: Environment) -> void:
	_place_camera(tree_height)
	camera.environment = env


func _process(delta: float) -> void:
	_time += delta
	# The lantern flickers a little.
	_lamp.light_energy = 1.0 + 0.1 * sin(_time * 7.3) + 0.05 * sin(_time * 13.1)


## Which tappable item is at this screen point ("journal", "album", "seeds", "options" or "").
func item_at(screen: Vector2) -> String:
	var best := ""
	var best_d := 70.0
	for k in _items:
		var n: Node3D = _items[k]
		if camera.is_position_behind(n.global_position):
			continue
		var d := camera.unproject_position(n.global_position).distance_to(screen)
		if d < best_d:
			best_d = d
			best = k
	return best


func item_screen_position(name: String) -> Vector2:
	return camera.unproject_position((_items[name] as Node3D).global_position)
