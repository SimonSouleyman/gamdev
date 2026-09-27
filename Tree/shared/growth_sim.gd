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
var cost_per_node: float = 0.08
## Life force produced per tip per second at full light.
var life_force_per_tip: float = 0.02  # scaled with the 2-minute day (play test 3)
## Segments the tree may add per second at full light and full nutrients.
## Low enough that one night's nutrients last a good part of the day.
var max_growth_per_second: float = 1.0
## Dawn burst: this share of what the nutrients can buy is released in the first seconds after sunrise.
var dawn_burst_share: float = 0.25
var dawn_burst_seconds: float = 10.0
var dawn_burst_max_nodes: int = 30
var _burst_nodes_left: int = 0
var _burst_rate: float = 0.0
var _burst_accum: float = 0.0
## Calm growth pace for today (segments per second at full light), set at sunrise so the
## night's nutrients last until sunset without boosting. 0 = use max_growth_per_second.
var day_pace: float = 0.0
## While the player moves the sun on, the tree rests (leaves still gather life force), so the
## nutrients wait for the hour the player picked to boost.
var growth_paused: bool = false
## Markers seeded per second on the sun side while the sun is up.
var markers_per_second: float = 12.0
## Fractional growth and markers carried over between ticks (so growth scales with time, not tick count).
var _growth_accum: float = 0.0
var _marker_accum: float = 0.0
var _leader_accum: float = 0.0
var marker_distance: float = 1.5
## Only the newest markers stay alive, so the crown follows today's sun, not last week's.
var live_markers: int = 150
## Without boosting the sun steers the crown only this much (the boost is the steering).
var passive_steering: float = 0.2
## New markers never go below this height, so a seedling does not creep along the ground.
const MARKER_MIN_Y: float = 0.25
var marker_radius: float = 0.8


func _init(random_seed: int = 1) -> void:
	seed = random_seed
	rng.seed = hash([seed, "tree"])
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

	# Life force from leaves: every tip counts as a leaf cluster. Inner leaves shade each other,
	# so a big crown yields less per leaf (a stand-in until the shadow grid exists).
	resources.life_force += effective_leaves() * life_force_per_tip * clock.life_force_light() * delta

	# Seed markers on the sun's side, above the current crown, capped by the species size.
	var sun := clock.sun_direction()
	var top := height()
	if growth_paused:
		graph.age_all()
		return
	if _affordable_nodes() > 0 and not graph.is_full():
		_seed_markers(sun, top, markers_per_second * maxf(1.0, crown_radius(top)) * delta,
				1.0 if clock.boost_active else passive_steering)

	# Growth budget: light x nutrient factor x species need.
	var factor := Resources.growth_factor(resources.stock, species.needs)
	# Light is not capped at 1: the boosted sun (up to 3x) speeds growth at any hour.
	var pace := max_growth_per_second if day_pace <= 0.0 else minf(day_pace, max_growth_per_second)
	_growth_accum += pace * light * factor * delta
	var budget := int(_growth_accum)
	_growth_accum -= budget
	budget += _dawn_burst_budget(delta, sun, top)
	colonizer.bias_direction = (Vector3.UP * (1.0 - species.phototropism) + sun * species.phototropism).normalized()
	# Longer shoots on a bigger tree, so the node budget reaches the species size.
	colonizer.step_length = 0.15 + top * 0.014
	colonizer.kill_distance = colonizer.step_length * 1.6
	# Buds sense space further away in a bigger crown, so side branches can reach its edge.
	colonizer.influence_radius = clampf(crown_radius(top) * 0.5, 1.2, 4.0)
	var affordable := _affordable_nodes()
	var grown := colonizer.step(graph, mini(budget, affordable))
	if grown > 0:
		_pay_for(grown)
		graph.update_radii()
	graph.age_all()


## Seeds `amount` markers (fractions carry over): part just above the leader (apical
## dominance), the rest in a sphere around the upper crown, shifted toward the sun by `steer`.
## A low sun shifts it sideways, a high sun lifts it (sun steering in three dimensions).
func _seed_markers(sun: Vector3, top: float, amount: float, steer: float) -> void:
	var r := crown_radius(top)
	# A high sun feeds the leader (grow up), a low sun the sides (grow sideways).
	var leader_share := species.apical_dominance * clampf(0.2 + 1.3 * sun.y, 0.0, 1.3)
	# The leader slows as the tree nears its species height: a linden broadens into a dome.
	leader_share *= pow(clampf(1.0 - top / species.max_height, 0.0, 1.0), 2.0)
	# Separate accumulators, so small ticks (60 fps) seed the leader as well as big ones.
	_leader_accum += amount * leader_share
	_marker_accum += amount * (1.0 - leader_share)
	var leader := int(_leader_accum)
	_leader_accum -= leader
	var crown := int(_marker_accum)
	_marker_accum -= crown
	var limit := mini(live_markers, Budgets.TREE_MARKERS)
	var flat := Vector3(sun.x, 0.0, sun.z) * steer
	# Nothing is seeded above the species' full height: the tree stops growing taller there.
	var cap := species.max_height
	colonizer.seed_sphere(Vector3(0, top + 0.45, 0) + flat * 0.4, 0.45, leader, limit, MARKER_MIN_Y, cap)
	colonizer.seed_sphere(marker_center(sun, top, steer), r, crown, limit, MARKER_MIN_Y, cap)


## Centre of the crown sphere for new markers.
func marker_center(sun: Vector3, top: float, steer: float = 1.0) -> Vector3:
	var r := crown_radius(top)
	var flat := Vector3(sun.x, 0.0, sun.z) * steer
	var c := Vector3(0, maxf(top, 0.15) * 0.6 + 0.3 + 0.7 * r * maxf(sun.y, 0.0) * steer, 0) + flat * r
	c.y = maxf(c.y, r * 0.5 + 0.1)
	return c


func crown_radius(top: float) -> float:
	return clampf(0.3 + top * 0.6, 0.4, species.max_crown_radius)


## Starts the dawn burst: part of what last night's nutrients buy is grown in the first
## seconds of the day. It only changes when the growth happens, not how much: nutrients cap it.
func start_dawn_burst() -> void:
	var factor := Resources.growth_factor(resources.stock, species.needs)
	_burst_nodes_left = mini(dawn_burst_max_nodes, int(_affordable_nodes() * dawn_burst_share * factor))
	_burst_rate = _burst_nodes_left / dawn_burst_seconds
	_burst_accum = 0.0
	# Spread the rest over the day: without boosting it lasts until about sunset, so a boost
	# at any hour, evening included, still has something to grow with.
	var day_seconds := clock.seconds_per_day * clock.daylight_fraction
	# tick() grows at pace x light x factor, so the factor divides out here.
	var rest := maxf(0.0, _affordable_nodes() - _burst_nodes_left)
	day_pace = maxf(0.05, rest / (day_seconds * 0.9 * maxf(factor, 0.15)))


func dawn_burst_active() -> bool:
	return _burst_nodes_left > 0


func _dawn_burst_budget(delta: float, sun: Vector3, top: float) -> int:
	if _burst_nodes_left <= 0:
		return 0
	_burst_accum += _burst_rate * delta
	var n := mini(int(_burst_accum), _burst_nodes_left)
	_burst_accum -= n
	_burst_nodes_left -= n
	# The burst needs room to grow into: extra markers, not steered (it is the night's growth,
	# not the morning's), so the low dawn sun does not pull every tree east.
	if n > 0:
		_seed_markers(sun, top, n * 2.0, 0.0)
	return n


## After the player moved the sun on: spread what is left over the rest of the day.
func repace_rest_of_day() -> void:
	var rest_seconds := maxf(10.0, (clock.daylight_fraction - clock.time_of_day) * clock.seconds_per_day)
	var factor := maxf(Resources.growth_factor(resources.stock, species.needs), 0.15)
	day_pace = maxf(0.05, _affordable_nodes() / (rest_seconds * 0.9 * factor))


## True when the tree has nothing left to grow with, so the day may be moved on: no water for
## a single segment, a needed nutrient used up (soft Liebig: growth would only crawl), or a full tree.
## (A missing N, P or K only slows the tree, soft Liebig: the HUD marks it, the day is not "spent".)
func nutrients_spent() -> bool:
	return _affordable_nodes() <= 0 or graph.is_full()


## A needed nutrient is used up: growth only crawls at the soft Liebig floor.
func nutrient_missing() -> bool:
	for k in range(4):
		if species.needs[k] > 0.0 and resources.stock[k] < cost_per_node * species.needs[k]:
			return true
	return false


## Leaf clusters, for life force and the HUD.
func tip_count() -> int:
	var n := 0
	for id in graph.tips():
		if not graph.get_flag(id, "dead", false):
			n += 1
	return n


## Water is needed for all growth and caps it hard; N, P and K follow the soft Liebig rule:
## a shortage slows growth (growth_factor) but never stops it.
## Leaf clusters after self-shading: grows like sqrt beyond the first 50.
func effective_leaves() -> float:
	var tips := float(graph.tips().size())
	return tips if tips <= 50.0 else sqrt(50.0 * tips)


## A bigger tree needs more material per new segment (it also thickens everything below),
## so the growth spreads over the whole month instead of filling the budget early.
func node_cost() -> float:
	return cost_per_node * (1.0 + graph.size() / 380.0)


func _affordable_nodes() -> int:
	var per_node := node_cost() * species.needs[Resources.Kind.WATER]
	if per_node <= 0.0:
		return 1_000_000
	return int(resources.stock[Resources.Kind.WATER] / per_node + 1e-4)


func _pay_for(nodes: int) -> void:
	for k in range(4):
		resources.stock[k] = maxf(0.0, resources.stock[k] - nodes * node_cost() * species.needs[k])


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
## The in-game clock does not move while the app is closed (it only runs while open), so
## offline growth runs at a fixed mid-morning light and the clock is restored afterwards.
func apply_offline(real_seconds: float, active_seconds_per_real_day: float = 20.0) -> void:
	var active := real_seconds / 86400.0 * active_seconds_per_real_day
	var saved_time := clock.time_of_day
	var saved_day := clock.day_count
	var saved_boost := clock.boost_active
	clock.boost_active = false
	var step := 0.5
	while active > 0.0:
		clock.time_of_day = clock.daylight_fraction * 0.3
		clock.day_count = saved_day
		tick(minf(step, active))
		active -= step
	clock.time_of_day = saved_time
	clock.day_count = saved_day
	clock.boost_active = saved_boost
	# The player always returns with a little life force, even to a tiny seedling.
	resources.life_force += minf(real_seconds / 86400.0 * 4.0, 8.0)


func to_dict() -> Dictionary:
	return {
		"version": 1,
		"seed": seed,
		# As a string: JSON numbers are doubles and would lose the low bits of the 64-bit state.
		"rng_state": str(rng.state),
		"burst": [_burst_nodes_left, _burst_rate, _burst_accum],
		"day_pace": day_pace,
		# The fractional carry-overs, so a loaded game grows exactly like an uninterrupted one.
		"accum": [_growth_accum, _marker_accum, _leader_accum],
		"species": species.id,
		"graph": graph.to_dict(),
		"markers": colonizer.markers,
		"resources": resources.to_dict(),
		"clock": clock.to_dict(),
	}


static func from_dict(d: Dictionary) -> GrowthSim:
	var s := GrowthSim.new(int(d.get("seed", 1)))
	s.rng.state = int(str(d.get("rng_state", s.rng.state)))
	var burst: Array = d.get("burst", [0, 0.0, 0.0])
	s._burst_nodes_left = int(burst[0])
	s._burst_rate = float(burst[1])
	s._burst_accum = float(burst[2])
	s.day_pace = float(d.get("day_pace", 0.0))
	var acc: Array = d.get("accum", [0.0, 0.0, 0.0])
	s._growth_accum = float(acc[0])
	s._marker_accum = float(acc[1])
	s._leader_accum = float(acc[2])
	s.species = Species.from_id(str(d.get("species", "linden")))
	s.graph = PlantGraph.from_dict(d["graph"])
	s.colonizer = SpaceColonization.new(s.rng)
	s.colonizer.markers = PackedVector3Array(d.get("markers", []))
	s.resources = Resources.from_dict(d.get("resources", {}))
	s.clock = DayCycle.from_dict(d.get("clock", {}))
	return s
