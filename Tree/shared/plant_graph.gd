class_name PlantGraph
extends RefCounted
## A tree-shaped graph of segments ("internodes"). Used for the tree and for the roots.
## Pure data: no Node, no scene. Rendering reads it, the simulation writes it.

## Exponent of the pipe model: r_parent^N = sum(r_child^N). 2 = Da Vinci's rule.
const PIPE_EXPONENT: float = 2.5
## Radius of a growing tip that has no children yet.
const TIP_RADIUS: float = 0.01

## Parallel arrays, indexed by node id. Node 0 is the base (trunk foot or root crown).
var positions: PackedVector3Array = PackedVector3Array()
var parents: PackedInt32Array = PackedInt32Array()
var radii: PackedFloat32Array = PackedFloat32Array()
var ages: PackedInt32Array = PackedInt32Array()
## Children lists, one Array[int] per node.
var children: Array = []
## Free-form per-node flags (e.g. "dead", "pruned"), a Dictionary per node or null.
var flags: Array = []

var max_nodes: int = Budgets.TREE_MAX_NODES


func _init(root_position: Vector3 = Vector3.ZERO, node_limit: int = Budgets.TREE_MAX_NODES) -> void:
	max_nodes = node_limit
	_append(root_position, -1)


func size() -> int:
	return positions.size()


func is_full() -> bool:
	return size() >= max_nodes


## Adds a child segment ending at `position`. Returns the new id, or -1 if the budget is spent.
func add_node(parent_id: int, position: Vector3) -> int:
	assert(parent_id >= 0 and parent_id < size(), "parent out of range")
	if is_full():
		return -1
	return _append(position, parent_id)


func _append(position: Vector3, parent_id: int) -> int:
	var id := positions.size()
	positions.append(position)
	parents.append(parent_id)
	radii.append(TIP_RADIUS)
	ages.append(0)
	children.append([])
	flags.append(null)
	if parent_id >= 0:
		(children[parent_id] as Array).append(id)
	return id


func is_tip(id: int) -> bool:
	return (children[id] as Array).is_empty()


func tips() -> PackedInt32Array:
	var out := PackedInt32Array()
	for id in range(size()):
		if is_tip(id):
			out.append(id)
	return out


func direction_of(id: int) -> Vector3:
	## Unit direction from the parent to this node; up for the base node.
	var p := parents[id]
	if p < 0:
		return Vector3.UP
	var d := positions[id] - positions[p]
	return d.normalized() if d.length_squared() > 0.0 else Vector3.UP


func depth_of(id: int) -> int:
	var d := 0
	var cur := id
	while parents[cur] >= 0:
		cur = parents[cur]
		d += 1
	return d


func set_flag(id: int, key: String, value: Variant) -> void:
	if flags[id] == null:
		flags[id] = {}
	(flags[id] as Dictionary)[key] = value


func get_flag(id: int, key: String, default: Variant = null) -> Variant:
	if flags[id] == null:
		return default
	return (flags[id] as Dictionary).get(key, default)


func age_all() -> void:
	for id in range(size()):
		ages[id] += 1


## Pipe model: recompute all radii from the tips down. O(n) with a reverse pass,
## because children always have larger ids than their parent.
func update_radii() -> void:
	var n := size()
	var acc := PackedFloat32Array()
	acc.resize(n)
	for id in range(n - 1, -1, -1):
		var kids := children[id] as Array
		if kids.is_empty():
			radii[id] = TIP_RADIUS
		else:
			radii[id] = pow(acc[id], 1.0 / PIPE_EXPONENT)
		var p := parents[id]
		if p >= 0:
			acc[p] += pow(radii[id], PIPE_EXPONENT)


## Total length of all segments, useful for tests and for resource costs.
func total_length() -> float:
	var total := 0.0
	for id in range(1, size()):
		total += positions[id].distance_to(positions[parents[id]])
	return total


func to_dict() -> Dictionary:
	return {
		"positions": positions,
		"parents": parents,
		"radii": radii,
		"ages": ages,
		"flags": flags,
		"max_nodes": max_nodes,
	}


static func from_dict(d: Dictionary) -> PlantGraph:
	var g := PlantGraph.new(Vector3.ZERO, int(d.get("max_nodes", Budgets.TREE_MAX_NODES)))
	g.positions = PackedVector3Array(d["positions"])
	g.parents = PackedInt32Array(d["parents"])
	g.radii = PackedFloat32Array(d["radii"])
	g.ages = PackedInt32Array(d["ages"])
	g.flags = d.get("flags", [])
	g.children = []
	for _i in range(g.positions.size()):
		g.children.append([])
	for id in range(g.positions.size()):
		var p := g.parents[id]
		if p >= 0:
			(g.children[p] as Array).append(id)
	while g.flags.size() < g.positions.size():
		g.flags.append(null)
	return g


## JSON-safe variant: positions stored as a flat [x, y, z, ...] list.
func to_json_dict() -> Dictionary:
	var d := to_dict()
	var flat := PackedFloat32Array()
	for p in positions:
		flat.append_array([p.x, p.y, p.z])
	d["positions"] = flat
	return d


static func from_json_dict(d: Dictionary) -> PlantGraph:
	var copy := d.duplicate()
	var flat: Array = Array(d.get("positions", []))
	var out := PackedVector3Array()
	var i := 0
	while i + 2 < flat.size():
		out.append(Vector3(float(flat[i]), float(flat[i + 1]), float(flat[i + 2])))
		i += 3
	copy["positions"] = out
	return from_dict(copy)
