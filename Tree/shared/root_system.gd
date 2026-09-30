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
## A hard turn slows the tip by up to this share, so the turn gets tighter and the root
## no longer circles a deposit it is steered at (QA r1: constant speed, limited turn).
var turn_slowdown: float = 0.4
## Gentle magnetism: a fresh deposit within this reach, ahead of the tip (heading dot above
## MAGNET_CONE), bends the heading toward it at this rate (rad/s), less while the stick is held hard.
var magnet_radius: float = 1.8
var magnet_rate: float = 1.3
const MAGNET_CONE: float = 0.3
## The root drifts down on its own at this speed (m/s); diving also bends the heading down.
var sink_speed: float = 0.07
var dive_sink_rate: float = 1.2
## Length of one permanent root segment.
var step_length: float = 0.25
## Dots within this distance of the tip are drunk immediately.
var collect_radius: float = 0.7
## Fine roots reach dots within this distance of the new main root.
var fine_radius: float = 1.6
## Ending early: each point of leftover life force buys this many more fine-root nodes,
## and widens their reach, up to a cap (Simon, play test 2026-09-27).
var fine_nodes_per_life_force: float = 6.0
var fine_reach_per_life_force: float = 0.12
var fine_reach_max_extra: float = 6.0
## Share of a deposit's capacity the tip draws on first contact, and a fine root. QA r1: fine roots
## drew as much as the tip, so a 2 m root whose leftover sprouted fine roots 7 m around it grew a
## bigger tree than steering did; now steering to a deposit pays twice.
var tip_share: float = Underground.FIRST_SHARE
var fine_share: float = 0.1
## A calm night (QA r1: runs grew to 80-110 s): life force beyond calm_life_force makes each
## metre dearer (by the power cost_exponent), and a long planned root grows faster, so a night's
## root takes about calm_run_seconds at most. The turn speeds up with it (the same curves in metres).
var calm_life_force: float = 50.0
var cost_exponent: float = 0.5
## Tonight's run time follows the tank (sim-0.6.3): calm_run_seconds on calm_ref_life_force,
## by its square root, between MIN_RUN_SECONDS and MAX_RUN_SECONDS. A bigger tank still buys a
## longer root in a calm time, but a boosted day's smaller tank now also gives a shorter night
## (it was evened out to about 30 s either way, broken list 2).
var calm_run_seconds: float = 34.0
var calm_ref_life_force: float = 150.0
const MIN_RUN_SECONDS: float = 20.0
const MAX_RUN_SECONDS: float = 44.0
## During the run the tip's speed follows what is left (life force at the local price per metre
## over the time left), so a root near the trunk, where metres are cheap, cannot run long
## (sim-0.6.3: nights of 54 to 58 s). At most this fast.
const MAX_REPACE_SCALE: float = 3.0
## Seconds over which the speed eases to the new pace.
const REPACE_EASE: float = 1.5
var run_seconds_target: float = 34.0
var _run_time: float = 0.0
## Average cost of a metre along a typical run, relative to base_cost_per_metre (for planning).
const TYPICAL_COST: float = 2.2
const MAX_SPEED_SCALE: float = 1.8
## A small tank grows slower, so even the first nights last about 20 s.
const MIN_SPEED_SCALE: float = 0.65
var run_cost_scale: float = 1.0
var run_speed_scale: float = 1.0
var _fine_budget: int = Budgets.FINE_ROOTS_PER_MAIN_ROOT
var _fine_reach: float = 1.6
## Life force that went into extra fine roots at the end of the last run.
var leftover_spent: float = 0.0

var graph: PlantGraph
var main_root_count: int = 0
## The tree's species (set by GameState): its quirks on root cost, deposits and nodules.
var species: Species = Species.linden()
## A root counts as pointing downward (oak's taproot) below this heading.y.
const DOWNWARD_HEADING: float = -0.5
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
## Deposits the roots have reached (dot id -> true); they are drunk from every night.
var tapped: Dictionary = {}
var _run_touched: Dictionary = {}
## Share of a deposit's capacity the old roots draw each night.
var nightly_share: float = 0.05
## Water seeps back toward the old roots: they draw this many times the nightly share from water
## deposits, so a small root network still keeps the tree watered (soft failure).
var nightly_water_factor: float = 2.0
## Groundwater seeps into the main roots (not the fine roots): water per metre each night, so a
## player who never finds a deposit still keeps the tree growing, only slower (soft failure).
var seep_per_metre: float = 0.03
## A metre of fine root draws this share of a main root's seep.
var fine_seep_share: float = 0.3


func _init(random_seed: int = 1) -> void:
	rng.seed = hash([random_seed, "roots"])
	graph = PlantGraph.new(Vector3.ZERO, Budgets.MAX_MAIN_ROOTS * (Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT + Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT) + 1)


## Life force for one metre of root at `p`: rises with distance from the trunk and with depth.
## `dir` is where the root points: a taproot species (oak) pays less depth surcharge going
## down; a pioneer (birch) pays less in the topsoil.
func cost_per_metre(p: Vector3, dir: Vector3 = Vector3.ZERO) -> float:
	var horizontal := Vector2(p.x, p.z).length()
	var depth := maxf(0.0, -p.y)
	var depth_term := depth_cost * depth
	if dir.y < DOWNWARD_HEADING:
		depth_term *= species.down_depth_cost
	var cost := base_cost_per_metre * (1.0 + distance_cost * horizontal + depth_term)
	if depth < Underground.TOPSOIL:
		cost *= species.topsoil_root_cost
	return cost


## A root may start while one more full root (path and most fine roots) fits the root graph's
## budget. The 45 main roots the graph is sized for are not a cap on nights: a tree still growing
## after night 45 (a pruned one) keeps its runs, so life force never piles up unused (sim-0.6.3).
func can_start_run() -> bool:
	return not run_active and has_room_for_root()


func has_room_for_root() -> bool:
	return graph.size() + Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT + Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT <= graph.max_nodes


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
	_run_touched = {}
	_stuck_time = 0.0
	_paced = false
	run_cost_scale = 1.0
	run_speed_scale = 1.0
	_aim_from_start()
	return true


## The heading at the start: away from the trunk along the old root, a little down.
func _aim_from_start() -> void:
	var from_id := run_start_id
	if from_id == 0:
		heading = Vector3(0.0, -0.5, -1.0).normalized()
	else:
		var d := graph.direction_of(from_id)
		heading = (Vector3(d.x, 0.0, d.z).normalized() + Vector3.DOWN * 0.4).normalized()
		if Vector3(d.x, 0.0, d.z).length_squared() < 1e-4:
			heading = Vector3(0.0, -0.5, -1.0).normalized()
	_update_right()


## The start was boxed in (a deep tip against rock and floor) before a single segment grew: the
## root starts again at the trunk with the life force untouched, so the night is not lost
## (sim-0.6.3: a 0 m night of 4 s with 173 to 361 life force kept).
func _restart_at_trunk() -> void:
	run_start_id = 0
	tip_id = 0
	tip_position = graph.positions[0]
	_carry = 0.0
	run_length = 0.0
	_stuck_time = 0.0
	_aim_from_start()


var _paced: bool = false


## Tonight's pace from the life force the run starts with (see calm_life_force).
func pace_run(life_force: float) -> void:
	_paced = true
	_run_time = 0.0
	run_cost_scale = pow(maxf(1.0, life_force / calm_life_force), cost_exponent)
	run_seconds_target = run_seconds_for(life_force)
	# Birch's cheap topsoil roots reach further on the same life force.
	var metres := life_force / (base_cost_per_metre * TYPICAL_COST * species.topsoil_root_cost * run_cost_scale)
	run_speed_scale = clampf(metres / (speed * run_seconds_target), MIN_SPEED_SCALE, MAX_SPEED_SCALE)


## Real seconds a run on `life_force` is paced to take.
func run_seconds_for(life_force: float) -> float:
	return clampf(calm_run_seconds * sqrt(maxf(life_force, 0.0) / calm_ref_life_force), MIN_RUN_SECONDS, MAX_RUN_SECONDS)


## Eases the tip's speed toward what the rest of the tank needs to last the time left.
func _repace(life_force: float, delta: float) -> void:
	_run_time += delta
	var cost := cost_per_metre(tip_position, heading) * run_cost_scale
	var metres := life_force / maxf(cost, 1e-3)
	var left := maxf(run_seconds_target - _run_time, 3.0)
	var want := clampf(metres / (speed * left), MIN_SPEED_SCALE, MAX_REPACE_SCALE)
	run_speed_scale = lerpf(run_speed_scale, want, clampf(delta / REPACE_EASE, 0.0, 1.0))


## Nodes added in the current run (the permanent path).
func run_node_count() -> int:
	return 0 if run_first_new_id < 0 else graph.size() - run_first_new_id


## One frame of steering. `stick`: x = right, y = up, each -1..1. Returns false once the run ended.
func advance(stick: Vector2, dive: bool, delta: float, ground: Underground, res: Resources) -> bool:
	last_collected = PackedInt32Array()
	last_finds = []
	if not run_active:
		return false
	if not _paced:
		pace_run(res.life_force)
	_repace(res.life_force, delta)
	_steer(stick, dive, delta)
	_magnet(stick, delta, ground)
	# A hard turn slows the tip: the tighter curve reaches a deposit instead of circling it.
	var slow := 1.0 - turn_slowdown * clampf(absf(stick.x) + maxf(0.0, absf(stick.y) - 0.2), 0.0, 1.0)
	var want := (dive_speed if dive else speed * slow) * run_speed_scale * delta
	var drift := Vector3.DOWN * sink_speed * delta
	# Small substeps, so a long frame cannot tunnel into a rock or skip the dots it passed.
	# Life force pays for the distance the tip really moved, never for pushing against a wall.
	var steps := maxi(1, ceili(want / 0.1))
	var ends := false
	var start_of_frame := tip_position
	for _i in range(steps):
		var before := tip_position
		var cost_rate := cost_per_metre(tip_position, heading) * run_cost_scale
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
	if run_node_count() == 0 and run_start_id != 0 and _stuck_time > STUCK_RESTART_SECONDS:
		_restart_at_trunk()
	elif _stuck_time > STUCK_END_SECONDS:
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
## A start boxed in before the first segment starts again at the trunk after this long.
const STUCK_RESTART_SECONDS: float = 1.2
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
	var rate := turn_rate * run_speed_scale
	heading = heading.rotated(Vector3.UP, -stick.x * rate * delta)
	_update_right()
	heading = heading.rotated(_right, stick.y * rate * delta)
	if dive:
		heading = (heading + Vector3.DOWN * dive_sink_rate * delta).normalized()
	heading = _clamp_pitch(heading)
	_update_right()


## A deposit not yet drunk by any root: the tip may still draw its first share.
func is_fresh(i: int) -> bool:
	return not tapped.has(i) and not _run_touched.has(i)


## The nearest fresh deposit within `reach` of the tip and ahead of it (heading dot > `cone`); -1 if none.
func fresh_ahead(ground: Underground, reach: float, cone: float) -> int:
	var best := -1
	var best_d := reach
	for i in ground.dots_near(tip_position, reach):
		if not is_fresh(i):
			continue
		var to := ground.dot_positions[i] - tip_position
		var d := to.length()
		if d < 1e-3 or heading.dot(to / d) < cone:
			continue
		if d < best_d:
			best_d = d
			best = i
	return best


## Gentle magnetism toward a fresh deposit within reach; the stick held hard overrules it.
func _magnet(stick: Vector2, delta: float, ground: Underground) -> void:
	var i := fresh_ahead(ground, magnet_radius, MAGNET_CONE)
	if i < 0:
		return
	var want := (ground.dot_positions[i] - tip_position).normalized()
	var angle := heading.angle_to(want)
	if angle < 1e-3:
		return
	var turn := magnet_rate * run_speed_scale * (1.0 - 0.7 * clampf(stick.length(), 0.0, 1.0)) * delta
	heading = _clamp_pitch(heading.slerp(want, minf(1.0, turn / angle)).normalized())
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
	# The tip draws from each deposit once per run; after that the root has tapped it.
	var fresh := PackedInt32Array()
	for i in ground.dots_near(p, radius):
		if not _run_touched.has(i):
			fresh.append(i)
	_collect_ids(fresh, ground, res)


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
	# Leftover life force reaches a little further: the fine roots gather what lies around the new root.
	_fine_reach = fine_radius + minf(fine_reach_max_extra, leftover_spent * fine_reach_per_life_force)
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
	_collect_ids(reached, ground, res, fine_share)


func _collect_ids(ids: PackedInt32Array, ground: Underground, res: Resources, share: float = -1.0) -> void:
	if share < 0.0:
		share = tip_share
	var got := ground.collect(ids, res, share, species.water_draw)
	for j in range(got.size()):
		var i := got[j]
		run_totals[ground.dot_kinds[i]] += ground.last_drawn[j]
		_run_touched[i] = true
		tapped[i] = true
	last_collected.append_array(got)


## Every night the whole root network keeps drinking from the deposits it has reached, and
## groundwater seeps in. `room` (per Resources.Kind, empty = no limit) caps what it draws: a tree
## whose stock is full draws less, and the deposits keep the rest. Returns what it drew, by kind.
func drink_tapped(ground: Underground, res: Resources, room: PackedFloat32Array = PackedFloat32Array()) -> PackedFloat32Array:
	var totals := PackedFloat32Array([0, 0, 0, 0])
	var by_kind: Array = [PackedInt32Array(), PackedInt32Array(), PackedInt32Array(), PackedInt32Array()]
	for i in tapped.keys():
		if ground.dot_collected[i] == 0:
			by_kind[ground.dot_kinds[i]].append(i)
		else:
			tapped.erase(i)
	var water_share := species.water_draw * nightly_water_factor
	for k in range(4):
		var ids: PackedInt32Array = by_kind[k]
		var share := nightly_share
		var seep := seep_length() * seep_per_metre if k == Resources.Kind.WATER else 0.0
		if not room.is_empty():
			# What the night would bring, scaled down to the room the tree has left.
			var want := seep
			for i in ids:
				want += minf(ground.dot_amounts[i], ground.dot_capacity[i] * share * (water_share if k == Resources.Kind.WATER else 1.0))
			var scale := clampf(room[k] / want, 0.0, 1.0) if want > 1e-6 else 1.0
			share *= scale
			seep *= scale
		if share > 0.0 and not ids.is_empty():
			var got := ground.collect(ids, res, share, water_share)
			for j in range(got.size()):
				totals[k] += ground.last_drawn[j]
		if seep > 0.0:
			res.add(k, seep)
			totals[k] += seep
	return totals


## Metres of root that groundwater seeps into: main roots in full, fine roots by fine_seep_share.
func seep_length() -> float:
	var total := 0.0
	for id in range(1, graph.size()):
		var metres := graph.positions[id].distance_to(graph.positions[graph.parents[id]])
		total += metres if graph.get_flag(id, "fine", -1) < 0 else metres * fine_seep_share
	return total


## Root nodules (alder): nitrogen made overnight, per metre of the whole root network.
## Returns the nitrogen added.
## `room` caps it (what the tree can still hold, GrowthSim.stock_room): nodules make what it uses.
func nodule_nitrogen(res: Resources, room: float = INF) -> float:
	if species.nodule_nitrogen <= 0.0:
		return 0.0
	var n := minf(graph.total_length() * species.nodule_nitrogen, maxf(room, 0.0))
	res.add(Resources.Kind.NITROGEN, n)
	return n


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
		"tapped": tapped.keys(),
		"run_touched": _run_touched.keys(),
		"carry": _carry,
		"run_active": run_active,
		"tip_id": tip_id,
		"tip_position": [tip_position.x, tip_position.y, tip_position.z],
		"heading": [heading.x, heading.y, heading.z],
		"run_start_id": run_start_id,
		"run_first_new_id": run_first_new_id,
		"run_length": run_length,
		"run_cost_scale": run_cost_scale,
		"run_speed_scale": run_speed_scale,
		"paced": _paced,
		"run_seconds_target": run_seconds_target,
		"run_time": _run_time,
	}


static func from_dict(d: Dictionary, random_seed: int = 1) -> RootSystem:
	var r := RootSystem.new(random_seed)
	if d.has("graph"):
		r.graph = PlantGraph.from_json_dict(d["graph"])
	r.main_root_count = int(d.get("main_root_count", 0))
	r.rng.state = int(str(d.get("rng_state", r.rng.state)))
	r.run_totals = PackedFloat32Array(d.get("run_totals", [0, 0, 0, 0]))
	for i in d.get("tapped", []):
		r.tapped[int(i)] = true
	for i in d.get("run_touched", []):
		r._run_touched[int(i)] = true
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
	r.run_cost_scale = float(d.get("run_cost_scale", 1.0))
	r.run_speed_scale = float(d.get("run_speed_scale", 1.0))
	r._paced = bool(d.get("paced", false))
	r.run_seconds_target = float(d.get("run_seconds_target", r.calm_run_seconds))
	r._run_time = float(d.get("run_time", 0.0))
	r._update_right()
	return r
