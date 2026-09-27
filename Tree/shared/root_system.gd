class_name RootSystem
extends RefCounted
## The roots: one PlantGraph under the trunk, grown one steered main root per night
## (design doc section 5). The player steers the tip; the path becomes permanent nodes;
## at the end of the run fine roots sprout by space colonization toward nearby dots.
## Pure data: the view only feeds joystick input in and reads the graph.

## Life force per metre at the trunk, near the surface.
var base_cost_per_metre: float = 1.0
## Extra cost per metre for each metre of horizontal distance from the trunk.
var distance_cost: float = 0.06
## Extra cost per metre for each metre of depth.
var depth_cost: float = 0.12
## Tip speed in metres per second, and while diving.
var speed: float = 0.9
var dive_speed: float = 1.3
## Radians per second at full joystick deflection.
var turn_rate: float = 1.7
## The root drifts down on its own at this speed (m/s); diving also bends the heading down.
var sink_speed: float = 0.07
var dive_sink_rate: float = 1.2
## Length of one permanent root segment.
var step_length: float = 0.25
## Dots within this distance of the tip are drunk immediately.
var collect_radius: float = 0.55
## Fine roots reach dots within this distance of the new main root.
var fine_radius: float = 1.6
## Ending early: each point of leftover life force buys this many more fine-root nodes,
## and widens their reach a little (Simon, play test 2026-09-27).
var fine_nodes_per_life_force: float = 6.0
var _fine_budget: int = Budgets.FINE_ROOTS_PER_MAIN_ROOT
var _fine_reach: float = 1.6
## Life force that went into extra fine roots at the end of the last run.
var leftover_spent: float = 0.0

var graph: PlantGraph
var main_root_count: int = 0
var rng := RandomNumberGenerator.new()

# Run state.
var run_active: bool = false
var tip_id: int = -1
var tip_position: Vector3 = Vector3.ZERO
var heading: Vector3 = Vector3.DOWN
var run_start_id: int = -1
var run_first_new_id: int = -1
var run_length: float = 0.0
var _carry: float = 0.0
var _right: Vector3 = Vector3.RIGHT

## Filled by advance() and end_run() for the view: ids of dots drunk and finds touched.
var last_collected: PackedInt32Array = PackedInt32Array()
var last_finds: Array = []
## Totals collected during the current (or last) run, by Resources.Kind.
var run_totals: PackedFloat32Array = PackedFloat32Array([0, 0, 0, 0])


func _init(random_seed: int = 1) -> void:
	rng.seed = hash([random_seed, "roots"])
	graph = PlantGraph.new(Vector3.ZERO, Budgets.MAX_MAIN_ROOTS * (Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT + Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT) + 1)


## Life force for one metre of root at `p`: rises with distance from the trunk and with depth.
func cost_per_metre(p: Vector3) -> float:
	var horizontal := Vector2(p.x, p.z).length()
	return base_cost_per_metre * (1.0 + distance_cost * horizontal + depth_cost * maxf(0.0, -p.y))


func can_start_run() -> bool:
	return not run_active and main_root_count < Budgets.MAX_MAIN_ROOTS


## Starts tonight's run from any existing root node (not only a tip).
func start_run(from_id: int) -> bool:
	if not can_start_run() or from_id < 0 or from_id >= graph.size():
		return false
	run_active = true
	run_start_id = from_id
	run_first_new_id = graph.size()
	tip_id = from_id
	tip_position = graph.positions[from_id]
	run_length = 0.0
	_carry = 0.0
	run_totals = PackedFloat32Array([0, 0, 0, 0])
	_stuck_time = 0.0
	if from_id == 0:
		heading = Vector3(0.0, -0.5, -1.0).normalized()
	else:
		var d := graph.direction_of(from_id)
		heading = (Vector3(d.x, 0.0, d.z).normalized() + Vector3.DOWN * 0.4).normalized()
		if Vector3(d.x, 0.0, d.z).length_squared() < 1e-4:
			heading = Vector3(0.0, -0.5, -1.0).normalized()
	_update_right()
	return true


## Nodes added in the current run (the permanent path).
func run_node_count() -> int:
	return 0 if run_first_new_id < 0 else graph.size() - run_first_new_id


## One frame of steering. `stick`: x = right, y = up, each -1..1. Returns false once the run ended.
func advance(stick: Vector2, dive: bool, delta: float, ground: Underground, res: Resources) -> bool:
	last_collected = PackedInt32Array()
	last_finds = []
	if not run_active:
		return false
	_steer(stick, dive, delta)
	var want := (dive_speed if dive else speed) * delta
	var drift := Vector3.DOWN * sink_speed * delta
	# Small substeps, so a long frame cannot tunnel into a rock or skip the dots it passed.
	# Life force pays for the distance the tip really moved, never for pushing against a wall.
	var steps := maxi(1, ceili(want / 0.1))
	var ends := false
	var start_of_frame := tip_position
	for _i in range(steps):
		var before := tip_position
		var cost_rate := cost_per_metre(tip_position)
		var step := want / steps
		if res.life_force < step * cost_rate:
			step = res.life_force / cost_rate
			ends = true
		_move(step, drift / steps * (step / maxf(want / steps, 1e-6)), ground)
		res.life_force = maxf(0.0, res.life_force - before.distance_to(tip_position) * cost_rate)
		_collect(tip_position, collect_radius, ground, res)
		if ends or not run_active:
			break
	last_finds = ground.touch_finds(tip_position)
	_unstick(start_of_frame, want, delta)
	if _stuck_time > STUCK_END_SECONDS:
		ends = true  # truly wedged: the root ends here; the life force left feeds fine roots
	if res.life_force <= 1e-4:
		ends = true
	if run_node_count() >= Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT:
		ends = true
	if ends or not run_active:
		run_active = true  # end_run() finishes the run properly (fine roots) even at the node budget
		end_run(ground, res)
		return false
	return true


## A tip wedged between rocks, the floor and the world edge turns back toward the trunk and up;
## if it still cannot move after a few seconds the run ends, so a night can never hang.
const STUCK_TURN_SECONDS: float = 0.6
const STUCK_END_SECONDS: float = 4.0
var _stuck_time: float = 0.0


func _unstick(start: Vector3, want: float, delta: float) -> void:
	if start.distance_to(tip_position) > want * 0.15:
		_stuck_time = 0.0
		return
	_stuck_time += delta
	if _stuck_time > STUCK_TURN_SECONDS:
		var inward := Vector3(-tip_position.x, 0.0, -tip_position.z)
		heading = _clamp_pitch((inward.normalized() + Vector3.UP * 0.6).normalized() if inward.length_squared() > 0.01 else Vector3.UP)
		_update_right()


func _update_right() -> void:
	var r := heading.cross(Vector3.UP)
	if r.length_squared() > 1e-4:
		_right = r.normalized()


## The steepest the root may point down or up, so turning left and right always works
## (a straight-down heading would only spin around itself).
const MAX_DOWN: float = -0.92
const MAX_UP: float = 0.4


func _steer(stick: Vector2, dive: bool, delta: float) -> void:
	stick = stick.limit_length(1.0)
	heading = heading.rotated(Vector3.UP, -stick.x * turn_rate * delta)
	_update_right()
	heading = heading.rotated(_right, stick.y * turn_rate * delta)
	if dive:
		heading = (heading + Vector3.DOWN * dive_sink_rate * delta).normalized()
	heading = _clamp_pitch(heading)
	_update_right()


func _clamp_pitch(h: Vector3) -> Vector3:
	var flat := Vector2(h.x, h.z)
	if flat.length_squared() < 1e-6:
		flat = Vector2(_right.z, -_right.x)
	var y := clampf(h.y, MAX_DOWN, MAX_UP)
	flat = flat.normalized() * sqrt(1.0 - y * y)
	return Vector3(flat.x, y, flat.y)


func _move(distance: float, drift: Vector3, ground: Underground) -> void:
	if distance <= 0.0:
		return
	var p := tip_position + heading * distance + drift
	# Walls: rocks, the floor, the surface and the edge of the world. Resolve them together a few
	# times, since pushing out of one rock can push into its neighbour or through the floor.
	for _pass in range(4):
		var moved := false
		for r in range(ground.rock_centers.size()):
			var c := ground.rock_centers[r]
			var rr := ground.rock_radii[r] + 0.08
			if p.distance_squared_to(c) < rr * rr:
				var n := (p - c).normalized()
				p = c + n * rr
				var slid := heading - n * minf(0.0, heading.dot(n))
				heading = slid.normalized() if slid.length_squared() > 1e-4 else _right
				moved = true
		if p.y < -Underground.DEPTH or p.y > -0.05:
			p.y = clampf(p.y, -Underground.DEPTH, -0.05)
			heading = _flattened(Vector3(heading.x, 0.0, heading.z))
			moved = true
		var flat := Vector2(p.x, p.z)
		if flat.length() > Underground.EXTENT:
			var n := Vector3(flat.x, 0.0, flat.y).normalized()
			# Turn back inward a little, so the corner of floor and edge cannot trap the root.
			heading = _flattened(heading - n * maxf(0.0, heading.dot(n)) - n * 0.3)
			flat = flat.normalized() * Underground.EXTENT
			p = Vector3(flat.x, p.y, flat.y)
			moved = true
		if not moved:
			break
	heading = _clamp_pitch(heading)
	_update_right()
	if ground.is_inside_rock(p, 0.0):
		return  # boxed in: stay put this step (and pay nothing)
	_carry += p.distance_to(tip_position)
	run_length += p.distance_to(tip_position)
	tip_position = p
	while _carry >= step_length:
		_carry -= step_length
		var id := graph.add_node(tip_id, tip_position)
		if id < 0:
			run_active = false
			return
		graph.set_flag(id, "main", main_root_count)
		tip_id = id


func _flattened(v: Vector3) -> Vector3:
	if v.length_squared() < 1e-4:
		v = _right
	var out := v.normalized()
	_update_right()
	return out


func _collect(p: Vector3, radius: float, ground: Underground, res: Resources) -> void:
	var ids := ground.collect(ground.dots_near(p, radius), res)
	for i in ids:
		run_totals[ground.dot_kinds[i]] += ground.dot_amounts[i]
	last_collected.append_array(ids)


## Ends the run: fine roots sprout along the new path and drink the dots they reach.
## Ends the run. Whatever life force is left (the player ended early, or the root reached its
## node budget) is spent too: it buys more and longer fine roots around the new root.
func end_run(ground: Underground, res: Resources) -> void:
	if not run_active:
		return
	run_active = false
	leftover_spent = 0.0
	if run_node_count() == 0:
		# Ended before the root grew at all: nothing to feed, the life force stays for later.
		graph.update_radii()
		return
	leftover_spent = res.life_force
	res.life_force = 0.0
	_fine_budget = mini(Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT,
		Budgets.FINE_ROOTS_PER_MAIN_ROOT + int(leftover_spent * fine_nodes_per_life_force))
	# Leftover life force reaches further: the fine roots gather what lies around the new root.
	_fine_reach = fine_radius + minf(5.0, leftover_spent * 0.08)
	if run_node_count() > 0:
		_grow_fine_roots(ground, res)
		main_root_count += 1
	graph.update_radii()


## The player ends tonight's root here.
func finish_early(ground: Underground, res: Resources) -> void:
	end_run(ground, res)


func _grow_fine_roots(ground: Underground, res: Resources) -> void:
	# Space colonization on a small temporary graph holding only the new path,
	# then the fine roots are grafted onto the real graph.
	var path := PackedInt32Array()
	for id in range(run_first_new_id, graph.size()):
		path.append(id)
	var temp := PlantGraph.new(graph.positions[run_start_id], path.size() + 1 + _fine_budget)
	var to_real := {0: run_start_id}
	var to_temp := {run_start_id: 0}
	for id in path:
		var t_id := temp.add_node(to_temp[graph.parents[id]], graph.positions[id])
		to_temp[id] = t_id
		to_real[t_id] = id
	var path_temp_count := temp.size()

	var marker_ids := PackedInt32Array()
	var seen := {}
	for id in path:
		for d in ground.dots_near(graph.positions[id], _fine_reach):
			if not seen.has(d):
				seen[d] = true
				marker_ids.append(d)
	if marker_ids.is_empty() and leftover_spent < 1.0:
		return
	var sc := SpaceColonization.new(rng)
	sc.influence_radius = _fine_reach
	sc.kill_distance = 0.22
	sc.step_length = 0.14
	sc.bias_direction = Vector3.DOWN
	sc.bias_strength = 0.1
	sc.jitter = 0.15
	for d in marker_ids:
		sc.add_marker(ground.dot_positions[d])
	var guard := 0
	# Leftover life force also sends fine roots out into the soil where no dot waits.
	# Only where there is nothing to drink: then the leftover still shows as more roots.
	var extra := int(leftover_spent * 2.0) if marker_ids.size() < 10 else 0
	for i in range(extra):
		var anchor := graph.positions[path[rng.randi() % path.size()]]
		var v := Vector3(rng.randf_range(-1, 1), rng.randf_range(-1, 0.4), rng.randf_range(-1, 1)).normalized()
		var m := anchor + v * rng.randf_range(0.5, _fine_reach)
		m.y = minf(m.y, -0.1)
		if not ground.is_inside_rock(m, 0.05):
			sc.add_marker(m)
	while not sc.markers.is_empty() and not temp.is_full() and guard < 120:
		if sc.step(temp) == 0:
			break
		guard += 1

	for t_id in range(path_temp_count, temp.size()):
		var real := graph.add_node(to_real[temp.parents[t_id]], temp.positions[t_id])
		if real < 0:
			break
		graph.set_flag(real, "fine", main_root_count)
		to_real[t_id] = real
	# Dots whose markers were consumed were reached by a fine root.
	var left := {}
	for m in sc.markers:
		left[m] = true
	var reached := PackedInt32Array()
	for d in marker_ids:
		if not left.has(ground.dot_positions[d]):
			reached.append(d)
	_collect_ids(reached, ground, res)


func _collect_ids(ids: PackedInt32Array, ground: Underground, res: Resources) -> void:
	var got := ground.collect(ids, res)
	for i in got:
		run_totals[ground.dot_kinds[i]] += ground.dot_amounts[i]
	last_collected.append_array(got)


## Nearest root node to `p` (for picking a start point). -1 if none within `max_distance`.
func nearest_node(p: Vector3, max_distance: float = INF) -> int:
	var best := -1
	var best_d := max_distance
	for id in range(graph.size()):
		var d := graph.positions[id].distance_to(p)
		if d < best_d:
			best_d = d
			best = id
	return best


func count_flagged(key: String, value: int) -> int:
	var n := 0
	for id in range(graph.size()):
		if graph.get_flag(id, key, -1) == value:
			n += 1
	return n


func to_dict() -> Dictionary:
	return {
		"graph": graph.to_json_dict(),
		"main_root_count": main_root_count,
		"rng_state": str(rng.state),
		"run_totals": run_totals,
		"carry": _carry,
		"run_active": run_active,
		"tip_id": tip_id,
		"tip_position": [tip_position.x, tip_position.y, tip_position.z],
		"heading": [heading.x, heading.y, heading.z],
		"run_start_id": run_start_id,
		"run_first_new_id": run_first_new_id,
		"run_length": run_length,
	}


static func from_dict(d: Dictionary, random_seed: int = 1) -> RootSystem:
	var r := RootSystem.new(random_seed)
	if d.has("graph"):
		r.graph = PlantGraph.from_json_dict(d["graph"])
	r.main_root_count = int(d.get("main_root_count", 0))
	r.rng.state = int(str(d.get("rng_state", r.rng.state)))
	r.run_totals = PackedFloat32Array(d.get("run_totals", [0, 0, 0, 0]))
	r._carry = float(d.get("carry", 0.0))
	r.run_active = bool(d.get("run_active", false))
	r.tip_id = int(d.get("tip_id", -1))
	var tp: Array = d.get("tip_position", [0, 0, 0])
	r.tip_position = Vector3(tp[0], tp[1], tp[2])
	var hd: Array = d.get("heading", [0, -1, 0])
	r.heading = Vector3(hd[0], hd[1], hd[2])
	r.run_start_id = int(d.get("run_start_id", -1))
	r.run_first_new_id = int(d.get("run_first_new_id", -1))
	r.run_length = float(d.get("run_length", 0.0))
	r._update_right()
	return r
