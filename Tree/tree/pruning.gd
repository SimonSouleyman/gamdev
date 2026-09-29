class_name Pruning
extends Node3D
## Pruning (Simon, play test 4). With the shears on, the branch under the mouse or finger is
## marked where it would be cut, and the part that would fall is outlined. A click (or lifting
## the finger) cuts: the branch tips over at the cut, falls into the grass and sinks away.
## Cutting is free; the tree puts its strength into what is left (GrowthSim.prune).

signal cut_done(segments: int)

var view: TreeView
## Anything else with shears (the bonsai, BonsaiView): an object with pruning_graph(),
## pruning_camera(), pruning_bark() and pruning_cut(id) -> int. Null for the tree.
var host: Object = null
## Soft failure only: a cut never takes more than this share of the living tree (the bonsai: a
## third), nor anything below `first_id` (the trunk base).
var max_share: float = 0.2
var min_cut: int = 8
var first_id: int = 3
## How far a cut piece falls before it sinks away (the grass; the bonsai's sill is closer).
var fall_depth: float = 1.5
## The node whose segment (from its parent) would be cut; -1 for none.
var target: int = -1
var _marker: MeshInstance3D
var _outline: MeshInstance3D
var _outline_mesh := ImmediateMesh.new()
var _builder := BranchMeshBuilder.new()
## How close (pixels) the pointer must be to a branch.
const PICK_RADIUS := 60.0


func _ready() -> void:
	_builder.radius_scale = 3.2
	var ring := TorusMesh.new()
	ring.inner_radius = 0.06
	ring.outer_radius = 0.09
	ring.rings = 12
	ring.ring_segments = 6
	_marker = MeshInstance3D.new()
	_marker.mesh = ring
	var mm := StandardMaterial3D.new()
	mm.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mm.albedo_color = Color(1.0, 0.35, 0.2)
	mm.no_depth_test = true
	_marker.material_override = mm
	_marker.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_marker.visible = false
	add_child(_marker)
	_outline = MeshInstance3D.new()
	_outline.mesh = _outline_mesh
	var om := StandardMaterial3D.new()
	om.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	# A cool light blue: it stands out against a green crown and a yellow autumn one alike
	# (0.6.3 review: the warm yellow vanished in a yellow crown).
	om.albedo_color = Color(0.6, 0.92, 1.0, 1.0)
	om.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	om.no_depth_test = true
	_outline.material_override = om
	_outline.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_outline)


## The branch nearest to a screen point: the node whose segment passes closest, never the trunk
## base. -1 when nothing is near.
func pick(screen: Vector2) -> int:
	var g := _graph()
	var cam := _camera()
	# How much living wood hangs on each node (children have higher ids than their parents).
	var below := PackedInt32Array()
	below.resize(g.size())
	var living := 0
	for id in range(g.size() - 1, -1, -1):
		if g.get_flag(id, "dead", false):
			continue
		below[id] += 1
		living += 1
		if id > 0:
			below[g.parents[id]] += below[id]
	# Soft failure only: never the trunk, never a cut that takes more than a fifth of the tree.
	var limit := maxi(min_cut, int(living * max_share))
	var best := -1
	var best_d := PICK_RADIUS
	for id in range(first_id, g.size()):
		if g.get_flag(id, "dead", false) or below[id] > limit:
			continue
		var a := to_global(g.positions[g.parents[id]])
		var b := to_global(g.positions[id])
		if cam.is_position_behind(a) or cam.is_position_behind(b):
			continue
		var d := Geometry2D.get_closest_point_to_segment(screen, cam.unproject_position(a), cam.unproject_position(b)).distance_to(screen)
		# Prefer thinner branches a little, so a click near a twig does not cut the whole limb.
		d += g.radii[id] * 40.0
		if d < best_d:
			best_d = d
			best = id
	return best


func _graph() -> PlantGraph:
	return host.call("pruning_graph") if host != null else view.state.sim.graph


func _camera() -> Camera3D:
	return host.call("pruning_camera") if host != null else view.camera


func _bark() -> Material:
	return host.call("pruning_bark") if host != null else view._bark_mat


func _subtree(id: int) -> Array[int]:
	var g := _graph()
	var out: Array[int] = []
	var stack: Array[int] = [id]
	while not stack.is_empty():
		var n: int = stack.pop_back()
		if g.get_flag(n, "dead", false):
			continue
		out.append(n)
		for c in g.children[n]:
			stack.append(c)
	return out


## Shows where `id` would be cut and outlines what would fall (-1 hides it).
func preview(id: int) -> void:
	target = id
	_outline_mesh.clear_surfaces()
	if id < 0:
		_marker.visible = false
		return
	var g := _graph()
	var a := g.positions[g.parents[id]]
	var b := g.positions[id]
	_marker.visible = true
	_marker.position = a.lerp(b, 0.35)
	var dir := (b - a).normalized()
	if dir.length_squared() > 0.0:
		_marker.basis = Basis(Quaternion(Vector3.UP, dir)).scaled(Vector3.ONE * clampf(g.radii[id] * 12.0 + 0.6, 0.6, 3.0))
	_outline_mesh.surface_begin(Mesh.PRIMITIVE_LINES)
	for n in _subtree(id):
		_outline_mesh.surface_add_vertex(g.positions[g.parents[n]] if n != id else _marker.position)
		_outline_mesh.surface_add_vertex(g.positions[n])
	_outline_mesh.surface_end()


## Cuts at the previewed branch. Returns the number of segments cut.
func cut() -> int:
	if target < 0:
		return 0
	var g := _graph()
	var id := target
	var nodes := _subtree(id)
	var cut_at := g.positions[g.parents[id]].lerp(g.positions[id], 0.35)
	# A small graph of just the falling branch, built like the tree's own wood.
	var piece := PlantGraph.new(cut_at, nodes.size() + 2)
	var remap := {g.parents[id]: 0}
	for n in nodes:
		var pid: int = remap.get(g.parents[n], 0)
		remap[n] = piece.add_node(pid, g.positions[n])
		piece.radii[remap[n]] = g.radii[n]
	piece.radii[0] = g.radii[id]
	var mesh := _builder.build(piece)
	var count: int = host.call("pruning_cut", id) if host != null else view.state.sim.prune(id)
	preview(-1)
	_fall(mesh, cut_at, (g.positions[id] - cut_at).normalized())
	# A short snip in the hand (0.6; the pinboard switch "vibration" turns it off).
	Haptics.buzz("cut")
	cut_done.emit(count)
	return count


func _fall(mesh: ArrayMesh, cut_at: Vector3, dir: Vector3) -> void:
	var pivot := Node3D.new()
	pivot.position = cut_at
	add_child(pivot)
	var m := MeshInstance3D.new()
	m.mesh = mesh
	m.material_override = _bark()
	m.position = -cut_at
	pivot.add_child(m)
	# It tips over away from the trunk, drops to the ground and sinks into the grass.
	var side := Vector3(dir.x, 0.0, dir.z)
	if side.length_squared() < 1e-4:
		side = Vector3.RIGHT
	var axis := side.normalized().cross(Vector3.UP).normalized() * -1.0
	var drop := minf(cut_at.y + 0.1, fall_depth)
	var tw := pivot.create_tween()
	tw.set_parallel(true)
	tw.tween_property(pivot, "basis", Basis(axis, 1.1), 0.9).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	tw.tween_property(pivot, "position:y", cut_at.y - drop, 0.9).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)
	tw.chain().tween_interval(0.8)
	tw.chain().tween_property(pivot, "position:y", cut_at.y - drop - fall_depth, 1.2)
	tw.chain().tween_callback(pivot.queue_free)
