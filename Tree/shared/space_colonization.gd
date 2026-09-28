class_name SpaceColonization
extends RefCounted
## Space colonization (Runions et al. 2007) on a PlantGraph.
## Tips grow toward nearby attraction markers; markers get consumed when reached.
## Used for the tree canopy (markers seeded around the sun) and for fine roots
## (markers = nutrient dots). Pure data, deterministic given the RNG.

## A tip only sees markers closer than this.
var influence_radius: float = 1.2
## A marker closer than this to any node is consumed.
var kill_distance: float = 0.25
## Length of one new segment.
var step_length: float = 0.15
## 0..1: how much a constant bias direction (sun, gravity) is mixed into growth.
var bias_strength: float = 0.2
var bias_direction: Vector3 = Vector3.UP
## Small random jitter so branches never look perfectly straight.
var jitter: float = 0.08

var markers: PackedVector3Array = PackedVector3Array()
var rng: RandomNumberGenerator


func _init(random: RandomNumberGenerator) -> void:
	rng = random


func add_marker(p: Vector3) -> void:
	markers.append(p)


## Seed `count` markers uniformly inside a sphere (center, radius), capped by `limit`.
## At the cap the oldest markers make room, so the crown keeps following the sun.
func seed_sphere(center: Vector3, radius: float, count: int, limit: int = Budgets.TREE_MARKERS, min_y: float = -INF, max_y: float = INF) -> void:
	count = mini(count, limit)
	var overflow := markers.size() + count - limit
	if overflow > 0:
		markers = markers.slice(overflow)
	for _i in range(count):
		var v := Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 1), rng.randf_range(-1, 1))
		while v.length_squared() > 1.0:
			v = Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 1), rng.randf_range(-1, 1))
		var p := center + v * radius
		# Markers below the floor are mirrored up, not flattened into a layer at the floor.
		if p.y < min_y:
			p.y = min_y + (min_y - p.y)
		p.y = clampf(p.y, min_y, max_y)
		markers.append(p)


## One growth iteration. Returns the number of new nodes.
## `max_new_nodes` caps growth per step (resource budget from the caller).
func step(graph: PlantGraph, max_new_nodes: int = 1_000_000) -> int:
	if markers.is_empty() or max_new_nodes <= 0 or graph.is_full():
		return 0
	var n := graph.size()
	# Accumulated pull per node and how many markers pulled it.
	var pull: Array[Vector3] = []
	var pull_count := PackedInt32Array()
	pull.resize(n)
	pull_count.resize(n)
	for i in range(n):
		pull[i] = Vector3.ZERO
	var infl2 := influence_radius * influence_radius
	var grid := _node_grid(graph, influence_radius, true)

	# Each marker influences its single nearest node within the radius (Runions 2007).
	for m in markers:
		var best := -1
		var best_d2 := infl2
		var c := _cell(m, influence_radius)
		for dx in range(-1, 2):
			for dy in range(-1, 2):
				for dz in range(-1, 2):
					var key := Vector3i(c.x + dx, c.y + dy, c.z + dz)
					if not grid.has(key):
						continue
					for id in (grid[key] as PackedInt32Array):
						var d2 := graph.positions[id].distance_squared_to(m)
						if d2 < best_d2 or d2 == best_d2 and id < best:
							best_d2 = d2
							best = id
		if best >= 0:
			pull[best] += (m - graph.positions[best]).normalized()
			pull_count[best] += 1

	# When the budget is short, buds are picked at random, weighted by how many markers pull
	# them (more light and space, a better chance), so neither old nor new buds always win.
	var candidates: Array[int] = []
	for id in range(n):
		if pull_count[id] > 0:
			candidates.append(id)
	if candidates.size() > max_new_nodes:
		var keys := {}
		for id in candidates:
			# Weighted random order (Efraimidis-Spirakis): key = u^(1/w), largest first.
			keys[id] = pow(rng.randf(), 1.0 / sqrt(float(pull_count[id])))
		candidates.sort_custom(func(a: int, b: int) -> bool: return keys[a] > keys[b])
	var grown := 0
	for id in candidates:
		if grown >= max_new_nodes:
			break
		var dir := (pull[id] / float(pull_count[id]))
		dir = dir.lerp(bias_direction, bias_strength)
		dir += Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 1), rng.randf_range(-1, 1)) * jitter
		if dir.length_squared() < 1e-8:
			continue
		var new_pos := graph.positions[id] + dir.normalized() * step_length
		if graph.add_node(id, new_pos) >= 0:
			grown += 1

	_consume_markers(graph)
	return grown


func _consume_markers(graph: PlantGraph) -> void:
	var kill2 := kill_distance * kill_distance
	var kept := PackedVector3Array()
	var grid := _node_grid(graph, kill_distance, false)
	for m in markers:
		var reached := false
		var c := _cell(m, kill_distance)
		for dx in range(-1, 2):
			for dy in range(-1, 2):
				for dz in range(-1, 2):
					var key := Vector3i(c.x + dx, c.y + dy, c.z + dz)
					if reached or not grid.has(key):
						continue
					for id in (grid[key] as PackedInt32Array):
						if graph.positions[id].distance_squared_to(m) < kill2:
							reached = true
							break
		if not reached:
			kept.append(m)
	markers = kept


static func _cell(p: Vector3, size: float) -> Vector3i:
	return Vector3i(floori(p.x / size), floori(p.y / size), floori(p.z / size))


## Spatial hash of node ids (cell size = query radius), so a query checks 27 cells, not every node.
func _node_grid(graph: PlantGraph, size: float, skip_dead: bool) -> Dictionary:
	var grid := {}
	for id in range(graph.size()):
		# A resting node (a pinched bonsai tip, silver deadwood) takes no markers: the buds
		# behind it get them instead.
		if skip_dead and (graph.get_flag(id, "dead", false) or graph.get_flag(id, "rest", false)):
			continue
		var key := _cell(graph.positions[id], size)
		# Packed arrays are values: append to a copy and store it back.
		var ids: PackedInt32Array = grid.get(key, PackedInt32Array())
		ids.append(id)
		grid[key] = ids
	return grid
