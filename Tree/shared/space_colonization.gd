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
func seed_sphere(center: Vector3, radius: float, count: int, limit: int = Budgets.TREE_MARKERS) -> void:
	for _i in range(count):
		if markers.size() >= limit:
			return
		var v := Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 1), rng.randf_range(-1, 1))
		while v.length_squared() > 1.0:
			v = Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 1), rng.randf_range(-1, 1))
		markers.append(center + v * radius)


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

	# Each marker influences its single nearest node within the radius (Runions 2007).
	for m in markers:
		var best := -1
		var best_d2 := infl2
		for id in range(n):
			if graph.get_flag(id, "dead", false):
				continue
			var d2 := graph.positions[id].distance_squared_to(m)
			if d2 < best_d2:
				best_d2 = d2
				best = id
		if best >= 0:
			pull[best] += (m - graph.positions[best]).normalized()
			pull_count[best] += 1

	var grown := 0
	for id in range(n):
		if pull_count[id] == 0 or grown >= max_new_nodes:
			continue
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
	var n := graph.size()
	for m in markers:
		var reached := false
		# Only the newest nodes can newly reach a marker, but checking all is simpler and n is capped.
		for id in range(n):
			if graph.positions[id].distance_squared_to(m) < kill2:
				reached = true
				break
		if not reached:
			kept.append(m)
	markers = kept
