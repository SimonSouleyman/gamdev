class_name GrowthSim
extends RefCounted
## The tree's growth simulation for Prototype 1, step 3/4 of the build plan:
## space colonization toward markers seeded on the sun's side, paid with nutrients,
## producing life force from light. No shadow grid / Borchert-Honda yet (later milestone).
## Pure data, deterministic from `seed`.

var seed: int = 1
var rng := RandomNumberGenerator.new()
var species: Species = Species.linden()
var graph: PlantGraph
var colonizer: SpaceColonization
var resources := Resources.new()
var clock := DayCycle.new()

## Nutrient cost of one new segment, scaled by species.needs.
var cost_per_node: float = 0.05
## Life force produced per tip per second at full light.
var life_force_per_tip: float = 0.02
## Segments the tree may add per second at full light and full nutrients.
var max_growth_per_second: float = 24.0
## Markers seeded per second on the sun side while the sun is up.
var markers_per_second: float = 12.0
## Fractional growth and markers carried over between ticks (so growth scales with time, not tick count).
var _growth_accum: float = 0.0
var _marker_accum: float = 0.0
var marker_distance: float = 1.5
var marker_radius: float = 0.8


func _init(random_seed: int = 1) -> void:
	seed = random_seed
	rng.seed = seed
	graph = PlantGraph.new(Vector3.ZERO, Budgets.TREE_MAX_NODES)
	# A short trunk stub so the seedling has something to grow from.
	graph.add_node(0, Vector3(0, 0.15, 0))
	colonizer = SpaceColonization.new(rng)
	colonizer.bias_direction = Vector3.UP


## Height of the highest node.
func height() -> float:
	var h := 0.0
	for p in graph.positions:
		h = maxf(h, p.y)
	return h


## Crown centre of mass, for tests (is the tree leaning east?).
func centroid() -> Vector3:
	var c := Vector3.ZERO
	for p in graph.positions:
		c += p
	return c / float(graph.size())


## One simulation tick of `delta` real seconds.
func tick(delta: float) -> void:
	clock.advance(delta)
	var light := clock.light_level()
	if light <= 0.0:
		graph.age_all()
		return

	# Life force from leaves: every tip counts as a leaf cluster.
	var tip_count := graph.tips().size()
	resources.life_force += tip_count * life_force_per_tip * clock.life_force_light() * delta

	# Seed markers on the sun's side, above the current crown, capped by the species size.
	var sun := clock.sun_direction()
	var top := height()
	if top < species.max_height:
		var center := Vector3(0, maxf(top, 0.3), 0) + sun * marker_distance
		center.y = maxf(center.y, 0.2)
		_marker_accum += markers_per_second * delta
		var to_seed := int(_marker_accum)
		_marker_accum -= to_seed
		colonizer.seed_sphere(center, marker_radius, to_seed)

	# Growth budget: light x nutrient factor x species need.
	var factor := Resources.growth_factor(resources.stock, species.needs)
	_growth_accum += max_growth_per_second * minf(light, 1.0) * factor * delta
	var budget := int(_growth_accum)
	_growth_accum -= budget
	colonizer.bias_direction = (Vector3.UP * (1.0 - species.phototropism) + sun * species.phototropism).normalized()
	var affordable := _affordable_nodes()
	var grown := colonizer.step(graph, mini(budget, affordable))
	if grown > 0:
		_pay_for(grown)
		graph.update_radii()
	graph.age_all()


func _affordable_nodes() -> int:
	var n := 1_000_000
	for k in range(4):
		var per_node := cost_per_node * species.needs[k]
		if per_node > 0.0:
			n = mini(n, int(resources.stock[k] / per_node))
	return n


func _pay_for(nodes: int) -> void:
	for k in range(4):
		resources.stock[k] = maxf(0.0, resources.stock[k] - nodes * cost_per_node * species.needs[k])


## Prune: mark a node and its whole subtree dead. The mesh builder hides dead nodes,
## and the colonizer ignores them, so resources go to the rest of the crown.
func prune(node_id: int) -> int:
	var count := 0
	var stack: Array[int] = [node_id]
	while not stack.is_empty():
		var id: int = stack.pop_back()
		if graph.get_flag(id, "dead", false):
			continue
		graph.set_flag(id, "dead", true)
		count += 1
		for child in graph.children[id]:
			stack.append(child)
	return count


## Offline catch-up: `real_seconds` closed become a much slower growth.
## Design doc first guess: one real day closed = about 20 s of active game time.
func apply_offline(real_seconds: float, active_seconds_per_real_day: float = 20.0) -> void:
	var active := real_seconds / 86400.0 * active_seconds_per_real_day
	var step := 0.5
	while active > 0.0:
		tick(minf(step, active))
		active -= step


func to_dict() -> Dictionary:
	return {
		"version": 1,
		"seed": seed,
		"rng_state": rng.state,
		"species": species.id,
		"graph": graph.to_dict(),
		"markers": colonizer.markers,
		"resources": resources.to_dict(),
		"clock": clock.to_dict(),
	}


static func from_dict(d: Dictionary) -> GrowthSim:
	var s := GrowthSim.new(int(d.get("seed", 1)))
	s.rng.state = int(d.get("rng_state", s.rng.state))
	s.species = Species.from_id(str(d.get("species", "linden")))
	s.graph = PlantGraph.from_dict(d["graph"])
	s.colonizer = SpaceColonization.new(s.rng)
	s.colonizer.markers = PackedVector3Array(d.get("markers", []))
	s.resources = Resources.from_dict(d.get("resources", {}))
	s.clock = DayCycle.from_dict(d.get("clock", {}))
	return s
