class_name RootSystem
extends RefCounted
## The roots: one PlantGraph under the trunk, grown one steered main root per night
## (design doc section 5). The player steers the tip; the path becomes permanent nodes;
## at the end of the run fine roots sprout by space colonization toward nearby dots.
## Pure data: the view only feeds joystick input in and reads the graph.

## Life force per metre at the trunk, near the surface. 0.8.2 (the dearer metre, tuning "Dearer
## metre in the wide field"): base_cost_wide in the wider field (layout 3; fit_soil), so a calm
## night buys about 12 to 18 m of straight root instead of 30 to 55 m, in the same 20 to 44 s (the
## run is paced to the tank's time). Older soils keep BASE_COST_OLD.
var base_cost_per_metre: float = 1.0
const BASE_COST_OLD: float = 1.0
static var base_cost_wide: float = 2.5
## Extra cost per metre for each metre of horizontal distance from the trunk. 0.8.1: 0.03 in the
## wider root field (layout 3; fit_soil), or a 25 m drive would cost about 2.5x a metre by the
## trunk and far patches would never pay (specs/0.8.md, "A wider root field"). An older soil
## keeps 0.06.
var distance_cost: float = 0.06
const DISTANCE_COST_OLD: float = 0.06
static var distance_cost_wide: float = 0.03
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
## MAGNET_CONE), bends the tip toward it at this rate (rad/s), less while the stick is held hard.
## 0.8.2.2 (Simon: "too strong, sharp curves, loses the direction"; specs/0.8.md "Gentler
## auto-steering"): 1.2 m (was 1.8) and 0.6 rad/s (was 1.3); the pull is a bend on top of the held
## heading, at most MAGNET_MAX_ANGLE, and eases back off at magnet_rate once the deposit is passed
## or drunk, so the root carries on in the direction the stick held.
var magnet_radius: float = 1.2
var magnet_rate: float = 0.6
const MAGNET_CONE: float = 0.3
const MAGNET_MAX_ANGLE: float = deg_to_rad(30.0)
## The root drifts down on its own at this speed (m/s); diving also bends the heading down.
var sink_speed: float = 0.07
var dive_sink_rate: float = 1.2
## Length of one permanent root segment.
var step_length: float = 0.25
## Dots within this distance of the tip are drunk immediately.
var collect_radius: float = 0.7
## Fine roots reach dots within this distance of the new main root.
var fine_radius: float = 1.6
## 0.8.2 (specs/side-roots.md): the first-level fine roots no longer grow with the leftover; they
## sprout toward dots within fine_radius of the new root, at most Budgets.FINE_ROOTS_PER_MAIN_ROOT
## nodes. The leftover goes into a second and a third level of side roots instead, and the share
## of the tank spent on the root itself makes that root thicker.
## Second level: from the first level's tips toward fresh dots within side_reach_base +
## side_reach_per_life_force x leftover (at most side_reach_max) of its first-level root;
## side_nodes_per_life_force nodes per point of leftover, side_level3_share of them to the third.
## side_reach_max 3.0 (spec: about 2.5, range 1.5 to 3): at 2.5 a never-steered oak finished a
## day later, past day 42 (tuning 10a). 0.8.2.2: 2.5 (specs/0.8.md "small roots everywhere": the
## side roots now grow from the whole root network, so the shorter reach keeps ending early behind
## steering).
var side_nodes_per_life_force: float = 4.0
var side_reach_base: float = 1.0
var side_reach_per_life_force: float = 0.05
var side_reach_max: float = 2.5
## The deepest root level the second level may start from (0 main, 1 fine, 2 and 3 side roots).
## 1 (0.8.2.2): every night's main and fine roots, not the side roots of earlier nights. From side
## roots too, the small roots crept a reach further out each night and a month of ending every
## night at once finished 3 days behind steering (linden seed 14: day 33 vs 30; the test wants
## 4 or more; beech seed 3 never steered: day 32, the test wants 33 or later); from main and fine
## roots: day 36 vs 30, beech 36, linden 37.
var side_start_level: float = 1.0
var side_level3_share: float = 0.3
## Second and third level together at most this many nodes a night (never past the budget).
var side_nodes_max: int = 250
## Third level: from second-level roots at least side3_min_length long, toward dots within
## side3_reach_base + side3_reach_per_life_force x leftover (at most side3_reach_max).
var side3_reach_base: float = 0.3
var side3_reach_per_life_force: float = 0.02
var side3_reach_max: float = 0.8
const SIDE3_MIN_LENGTH: float = 0.5
## Less than this share of the night's tank left: no second (or third) level at all.
var side_min_left_share: float = 0.1
## Where a level finds no dot in reach, each start still sprouts SIDE_SHORT_TIPS to
## SIDE_SHORT_TIPS_MAX short tips (more with more leftover to spend), so the fan shows in the
## sparse wide field and the leftover is seen.
const SIDE_SHORT_TIPS: int = 2
const SIDE_SHORT_TIPS_MAX: int = 5
## A first level with fewer tips than this also starts the second level from points along the
## new root (a root that ended at once still gets a small fan).
const SIDE_MIN_STARTS: int = 4
## Thickness: the share of the night's tank spent on the player's own root sets how thick that
## root is drawn, 1.0x (ended at once) to 1.0 + thick_gain (tank run dry); a thicker root takes
## 1 + seep_thick_gain x (thickness - 1) / thick_gain of the groundwater seep per metre.
var thick_gain: float = 0.5
var seep_thick_gain: float = 0.25
## Per main root (index = its "main" flag): its thickness, set once at the end of its run. Old
## saves have none (1.0).
var thickness: PackedFloat32Array = PackedFloat32Array()
## The life force tonight's run started with (for the thickness); saved for a mid-run save.
var run_tank: float = 0.0
## Share of a deposit's capacity the tip draws on first contact, and a fine root. QA r1: fine roots
## drew as much as the tip, so a 2 m root whose leftover sprouted fine roots 7 m around it grew a
## bigger tree than steering did; now steering to a deposit pays twice.
var tip_share: float = Underground.FIRST_SHARE
## 0.8.2: the tip's first contact in the wider field (layout 3; fit_soil). With the dearer metre a
## steered root touches fewer deposits a night, so each one it reaches pays more; older soils keep
## Underground.FIRST_SHARE.
static var tip_share_wide: float = 0.2
var fine_share: float = 0.1
## Third-level side roots drink at this share (0.8.2: half the fine share).
var side3_share: float = 0.05
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
## A small tank grows slower, so even the first nights last about 20 s (0.8: 0.3; 0.65 left the
## first nights after a boosted day at 10 to 15 s).
const MIN_SPEED_SCALE: float = 0.3
var run_cost_scale: float = 1.0
var run_speed_scale: float = 1.0
## Life force left at the end of the last run (it went into side roots, or was lost on a
## near-empty tank), and the side-root nodes it bought (second and third level).
var leftover_spent: float = 0.0
var side_nodes_grown: PackedInt32Array = PackedInt32Array([0, 0])

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
## The held heading: what the stick (and the dive) steer; rocks slide it as before.
var heading: Vector3 = Vector3.DOWN
## 0.8.2.2: the direction the tip really travels: the held heading bent by a deposit's pull and
## around old roots (specs/0.8.md "Roots go around old roots"). It eases back onto the heading.
var travel: Vector3 = Vector3.DOWN
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
## What tonight's root may still bring into the tree, per Resources.Kind (GrowthSim.stock_room at
## dusk, set by GameState.dive); empty = no limit. A deposit reached while the tree is full is
## tapped all the same: the old roots draw it on later nights (0.8, balance-0.8.md).
var run_room: Array = []
## Share of a deposit's capacity the old roots draw each night (0.8: 0.05 left a young tree
## short for its first week or two, while its few tapped deposits gave too little; the tree's
## room, GrowthSim.hold_days, still caps what they bring).
var nightly_share: float = 0.08
## Water seeps back toward the old roots: they draw this many times the nightly share from water
## deposits, so a small root network still keeps the tree watered (soft failure).
var nightly_water_factor: float = 2.0
## Groundwater seeps into the main roots (not the fine roots): water per metre each night, so a
## player who never finds a deposit still keeps the tree growing, only slower (soft failure).
var seep_per_metre: float = 0.03
## A metre of fine root draws this share of a main root's seep.
var fine_seep_share: float = 0.3
## 0.8.2, the wider field (fit_soil): with the dearer metre a night's root is about 2.5x shorter,
## so a metre seeps 2.5x as much (the seep per life force spent stays), and the fine and side
## roots, which no longer grow with the leftover's 6 nodes a point and 7.6 m reach, seep as much
## per metre as a main root: a tree never steered stays watered (tuning 10a). The seep cap
## (seep_day_cover) still keeps thirst able to show.
static var seep_per_metre_wide: float = 0.075
static var fine_seep_share_wide: float = 1.0
## 0.8.1: the seep covers at most this share of a calm day's water (GameState passes the cap). In
## the wider field a steered tree's main roots grow to 700 m by day 20 and their seep alone covered
## 0.75 of its water, so a tree whose water deposits ran dry never showed thirst (care broken
## list; tuning "Water upkeep"). Below Care.NEED_START, so neglect can still show.
var seep_day_cover: float = 0.65


func _init(random_seed: int = 1) -> void:
	rng.seed = hash([random_seed, "roots"])
	graph = PlantGraph.new(Vector3.ZERO, Budgets.MAX_MAIN_ROOTS * (Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT + Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT) + 1)


## The soil the roots grow in (fit_soil): its soft veins make a metre cheaper (0.8.2).
var soil: Underground = null
## The soil tonight's fine and side roots grow in (end_run): none grows into rock.
var _grow_ground: Underground = null


## The price of distance for this soil: the wider field (layout 3) halves it.
func fit_soil(ground: Underground) -> void:
	soil = ground
	distance_cost = distance_cost_wide if ground.layout >= 3 else DISTANCE_COST_OLD
	base_cost_per_metre = base_cost_wide if ground.layout >= 3 else BASE_COST_OLD
	tip_share = tip_share_wide if ground.layout >= 3 else Underground.FIRST_SHARE
	seep_per_metre = seep_per_metre_wide if ground.layout >= 3 else 0.03
	fine_seep_share = fine_seep_share_wide if ground.layout >= 3 else 0.3


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
	# 0.8.2: a soft vein's crumbly soil (Underground.vein_cost).
	if soil != null:
		cost *= soil.soil_factor(p)
	return cost


## A root may start while one more full root (path and most fine roots) fits the root graph's
## budget. The 45 main roots the graph is sized for are not a cap on nights: a tree still growing
## after night 45 (a pruned one) keeps its runs, so life force never piles up unused (sim-0.6.3).
func can_start_run() -> bool:
	return not run_active and not _ending and has_room_for_root()


func has_room_for_root() -> bool:
	return graph.size() + Budgets.ROOT_MAX_NODES_PER_MAIN_ROOT + Budgets.FINE_ROOTS_MAX_PER_MAIN_ROOT <= graph.max_nodes


## Starts tonight's run from any existing root node (not only a tip).
func start_run(from_id: int) -> bool:
	finish_pending()
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
	_reset_bends()
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
	_reset_bends()


var _paced: bool = false


## Tonight's pace from the life force the run starts with (see calm_life_force).
func pace_run(life_force: float) -> void:
	_paced = true
	_run_time = 0.0
	run_tank = life_force
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
	var pulled := _magnet(stick, delta, ground)
	_avoid(pulled, delta)
	# A hard turn slows the tip: the tighter curve reaches a deposit instead of circling it.
	var slow := 1.0 - turn_slowdown * clampf(absf(stick.x) + maxf(0.0, absf(stick.y) - 0.2), 0.0, 1.0)
	var want := (dive_speed if dive else speed * slow) * run_speed_scale * delta
	# The root sinks per metre, not per second (0.8.2): a tip paced slower than the base speed (the
	# wider field's dearer metre: 12 to 18 m in the same 20 to 44 s) would otherwise sink about
	# three times as steeply and the continued roots ended on the floor at 10 m. A faster tip
	# sinks as before.
	var drift := Vector3.DOWN * sink_speed * delta * minf(1.0, run_speed_scale)
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
## `clear`: only one whose straight line from the tip passes no rock or band (0.8.2: the
## magnetism never pulls a tip into rock).
func fresh_ahead(ground: Underground, reach: float, cone: float, clear: bool = false) -> int:
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
			if clear and ground.line_blocked(tip_position, ground.dot_positions[i], 0.2, 0.08):
				continue
			best_d = d
			best = i
	return best


## Gentle magnetism toward a fresh deposit within reach; the stick held hard overrules it.
## 0.8.2.2: a bend of at most MAGNET_MAX_ANGLE on top of the held heading (the heading itself is
## never turned), growing at magnet_rate while the deposit is ahead and easing off at the same rate
## once it is passed or drunk. Returns the pulled direction.
var _pull_angle: float = 0.0
var _pull_axis: Vector3 = Vector3.ZERO


func _magnet(stick: Vector2, delta: float, ground: Underground) -> Vector3:
	var rate := magnet_rate * run_speed_scale * (1.0 - 0.7 * clampf(stick.length(), 0.0, 1.0)) * delta
	var i := fresh_ahead(ground, magnet_radius, MAGNET_CONE, true)
	if i >= 0:
		var want := (ground.dot_positions[i] - tip_position).normalized()
		var axis := heading.cross(want)
		if axis.length_squared() > 1e-8:
			_pull_axis = axis.normalized()
		_pull_angle = move_toward(_pull_angle, minf(MAGNET_MAX_ANGLE, heading.angle_to(want)), rate)
	else:
		_pull_angle = move_toward(_pull_angle, 0.0, magnet_rate * run_speed_scale * delta)
	if _pull_angle < 1e-4 or _pull_axis == Vector3.ZERO:
		return heading
	# The axis stays square to the heading while the stick turns it.
	var square := _pull_axis - heading * _pull_axis.dot(heading)
	if square.length_squared() < 1e-8:
		return heading
	return _clamp_pitch(heading.rotated(square.normalized(), _pull_angle))


## A new start: no pull, no bend. The obstacle grid is kept (0.8.2.3: it only grows); the
## segments near the tip are looked up afresh.
func _reset_bends() -> void:
	travel = heading
	_pull_angle = 0.0
	_pull_axis = Vector3.ZERO
	_avoid_dir = Vector3.ZERO
	_avoid_segs = PackedInt32Array()
	_near_key = []
	_start_main = int(graph.get_flag(run_start_id, "main", -1)) if run_start_id > 0 else -1


# --- going around old roots (0.8.2.2) --------------------------------------------------

## specs/0.8.md "Roots go around old roots": the tip keeps AVOID_CLEARANCE from the main roots of
## earlier nights and the earlier part of tonight's root, looking AVOID_LOOK ahead along the held
## (pulled) heading; when that line comes closer, the tip bends to the side that needs the smaller
## turn (left, right, over or under; sideways first in the topsoil), keeps the distance while
## passing and eases back onto the held heading. Fine and side roots do not block. The root the
## night starts from (and the trunk's fan) is ignored for the first AVOID_START_FREE metres.
## No way round within AVOID_MAX_DEG (about 2 m of soil either side at the look-ahead): the tip
## runs on, stops at AVOID_HARD as at a rock, and the boxed-in rule (_unstick) applies.
const AVOID_CLEARANCE: float = 0.35
const AVOID_LOOK: float = 1.0
const AVOID_HARD: float = 0.2
const AVOID_STEP_DEG: float = 10.0
const AVOID_MAX_DEG: float = 90.0
## How fast the travel direction turns toward the free one (rad/s, with the tip's pace), on top of
## the stick's own turn rate (so a steered turn is never delayed).
const AVOID_TURN: float = 2.6
## Tonight's own last metres behind the tip never count (the root cannot curl back that tight).
const AVOID_OWN_SKIP: float = 1.5
const AVOID_START_FREE: float = 1.0
const _OBST_CELL: float = 1.0
## Main-root segments (node id, to its parent) by grid cell. 0.8.2.3: built once and kept across
## nights (it was rebuilt from the whole network at every start, up to 3 ms on the PC by day 30);
## each frame only adds the segments grown since. Fine and side roots are never in it.
var _obst_grid: Dictionary = {}
var _obst_upto: int = 0
var _obst_graph: PlantGraph = null
## This frame's segments near the tip: those of the grid cells within reach that pass within
## _NEAR_RADIUS + _NEAR_SLACK of where they were looked up (_near_center).
var _avoid_segs: PackedInt32Array = PackedInt32Array()
var _start_main: int = -1
## 0.8.2.3: the near segments are looked up again only when the cells, the graph or the run's
## start rule change, or the tip moved _NEAR_SLACK; in between the list is reused. The list is
## filtered by distance, but only segments within AVOID_LOOK + AVOID_CLEARANCE (plus a step) of
## the tip can ever block, bend or push it, so the result is the same as with every segment of
## the cells; `_near_any` keeps whether the cells held any (that alone chooses the bend's path).
const _NEAR_RADIUS: float = AVOID_LOOK + AVOID_CLEARANCE + 0.3
const _NEAR_SLACK: float = 0.3
var _near_key: Array = []
var _near_cells: PackedInt32Array = PackedInt32Array()
var _near_any: bool = false
var _near_center: Vector3 = Vector3.ZERO


func _index_obstacles() -> void:
	if _obst_upto > graph.size() or _obst_graph != graph:
		_obst_grid = {}
		_obst_upto = 0
		_obst_graph = graph
	for id in range(maxi(_obst_upto, 1), graph.size()):
		var fl = graph.flags[id]
		if fl != null and (fl as Dictionary).has("fine"):
			continue
		var a := Vector3i((graph.positions[id] / _OBST_CELL).floor())
		var b := Vector3i((graph.positions[graph.parents[id]] / _OBST_CELL).floor())
		for c in ([a] if a == b else [a, b]):
			if not _obst_grid.has(c):
				_obst_grid[c] = PackedInt32Array()
			var arr: PackedInt32Array = _obst_grid[c]
			arr.append(id)
			_obst_grid[c] = arr
	_obst_upto = graph.size()


## Looks up the segments near the tip (`_avoid_segs`, `_near_any`) when needed (see _near_key).
func _update_near(p: Vector3) -> void:
	var lo := Vector3i(((p - Vector3.ONE * _NEAR_RADIUS) / _OBST_CELL).floor())
	var hi := Vector3i(((p + Vector3.ONE * _NEAR_RADIUS) / _OBST_CELL).floor())
	var key := [lo, hi, graph.size(), run_active, run_active and run_length < AVOID_START_FREE, run_start_id, run_first_new_id]
	if key != _near_key:
		_near_key = key
		_near_cells = _segments_near(p, _NEAR_RADIUS)
		_near_any = not _near_cells.is_empty()
	elif p.distance_to(_near_center) <= _NEAR_SLACK:
		return
	_near_center = p
	var keep := (_NEAR_RADIUS + _NEAR_SLACK) * (_NEAR_RADIUS + _NEAR_SLACK)
	_avoid_segs = PackedInt32Array()
	for id in _near_cells:
		var c := Geometry3D.get_closest_point_to_segment(p, graph.positions[graph.parents[id]], graph.positions[id])
		if p.distance_squared_to(c) <= keep:
			_avoid_segs.append(id)


## The obstacle segments (by their child node) in the grid cells within `radius` of `p`.
func _segments_near(p: Vector3, radius: float) -> PackedInt32Array:
	var out := PackedInt32Array()
	var seen := {}
	var lo := Vector3i(((p - Vector3.ONE * radius) / _OBST_CELL).floor())
	var hi := Vector3i(((p + Vector3.ONE * radius) / _OBST_CELL).floor())
	# Tonight's last AVOID_OWN_SKIP metres (and the partial step at the tip) are not obstacles.
	var own_from := graph.size() - int(ceil(AVOID_OWN_SKIP / step_length)) if run_active else graph.size()
	var start_free := run_active and run_length < AVOID_START_FREE
	var start_at := graph.positions[run_start_id] if run_start_id >= 0 and run_start_id < graph.size() else tip_position
	for x in range(lo.x, hi.x + 1):
		for y in range(lo.y, hi.y + 1):
			for z in range(lo.z, hi.z + 1):
				var key := Vector3i(x, y, z)
				if not _obst_grid.has(key):
					continue
				for id in (_obst_grid[key] as PackedInt32Array):
					if seen.has(id) or (id >= own_from and id >= run_first_new_id):
						continue
					seen[id] = true
					if start_free:
						if _start_main >= 0 and int(graph.get_flag(id, "main", -1)) == _start_main:
							continue
						if graph.positions[id].distance_to(start_at) < AVOID_START_FREE or graph.positions[graph.parents[id]].distance_to(start_at) < AVOID_START_FREE:
							continue
					out.append(id)
	return out


## Distance from `p` to the nearest of `segs`.
func _seg_distance(p: Vector3, segs: PackedInt32Array) -> float:
	var best := INF
	for id in segs:
		var c := Geometry3D.get_closest_point_to_segment(p, graph.positions[graph.parents[id]], graph.positions[id])
		best = minf(best, p.distance_squared_to(c))
	return sqrt(best)


## The closest point to `p` on the nearest of `segs`.
func _closest_on(p: Vector3, segs: PackedInt32Array) -> Vector3:
	var best := INF
	var out := p
	for id in segs:
		var c := Geometry3D.get_closest_point_to_segment(p, graph.positions[graph.parents[id]], graph.positions[id])
		var d := p.distance_squared_to(c)
		if d < best:
			best = d
			out = c
	return out


## The segment the line AVOID_LOOK ahead along `dir` first comes closer than `keep` to; -1 if
## none (a candidate bend must also stay in the soil: -2 when it would leave it).
func _blocker(dir: Vector3, keep: float, candidate: bool) -> int:
	for k in range(1, 5):
		var q := tip_position + dir * (AVOID_LOOK * k * 0.25)
		if candidate and (q.y > -0.08 or q.y < -Underground.DEPTH + 0.05):
			return -2
		for id in _avoid_segs:
			var c := Geometry3D.get_closest_point_to_segment(q, graph.positions[graph.parents[id]], graph.positions[id])
			if q.distance_squared_to(c) < keep * keep:
				return id
	return -1


## Distance from the tip to the nearest obstacle this frame (INF when none is near).
func obstacle_distance() -> float:
	return _seg_distance(tip_position, _avoid_segs) if not _avoid_segs.is_empty() else INF


## Bends the travel direction around old roots and eases it back onto `want` (the held heading,
## pulled by a deposit).
func _avoid(want: Vector3, delta: float) -> void:
	_index_obstacles()
	_update_near(tip_position)
	if not _near_any:
		# Nothing near: the tip follows the held heading as before 0.8.2.2 (no lag).
		_avoid_dir = Vector3.ZERO
		travel = want
		return
	var target := want
	var here := _seg_distance(tip_position, _avoid_segs)
	# A tip already closer than the clearance (the first metre by its own start) only must not
	# come closer still.
	var keep := minf(AVOID_CLEARANCE, here) - 0.02
	var b := _blocker(want, keep, false)
	if b < 0:
		_avoid_dir = Vector3.ZERO
	else:
		target = _free_bend(want, keep, b)
	var angle := travel.angle_to(target)
	var most := (AVOID_TURN + turn_rate) * maxf(run_speed_scale, 0.5) * delta
	travel = target if angle <= most or angle < 1e-4 else travel.slerp(target, most / angle).normalized()
	travel = _clamp_pitch(travel)


## The side last bent to (a direction square to the held heading): tried first while it still
## works, so the tip passes an old root on one side and never shakes between two.
var _avoid_dir: Vector3 = Vector3.ZERO


## The smallest bend off `want` whose line ahead is clear; `want` itself when none is (boxed in).
## The way round a thin root is square to both the heading and that root (over or under a root
## across the way, left or right of one standing up), on the side the tip already is; then the
## plain sides (sideways first in the topsoil).
func _free_bend(want: Vector3, keep: float, blocker: int) -> Vector3:
	var sides: Array[Vector3] = []
	if _avoid_dir != Vector3.ZERO:
		var keep_side := _avoid_dir - want * _avoid_dir.dot(want)
		if keep_side.length_squared() > 1e-4:
			sides.append(keep_side.normalized())
	var topsoil := tip_position.y > -Underground.TOPSOIL
	var up := Vector3.UP - want * want.y
	up = up.normalized() if up.length_squared() > 1e-4 else _right.cross(want).normalized()
	var left := Vector3.UP.cross(want)
	left = left.normalized() if left.length_squared() > 1e-4 else _right
	if blocker >= 0:
		var along := graph.positions[blocker] - graph.positions[graph.parents[blocker]]
		var n := want.cross(along.normalized()) if along.length_squared() > 1e-8 else Vector3.ZERO
		if n.length() > 0.25:
			n = n.normalized()
			var c := Geometry3D.get_closest_point_to_segment_uncapped(tip_position, graph.positions[graph.parents[blocker]], graph.positions[blocker])
			var s := (tip_position - c).dot(n)
			if absf(s) < 0.02:
				# Square in its way: under it in the topsoil (and in the upper half), over it deeper.
				s = -n.y if tip_position.y > -Underground.DEPTH * 0.5 else n.y
			sides.append(n if s >= 0.0 else -n)
			sides.append(-n if s >= 0.0 else n)
	if topsoil:
		sides.append_array([left, -left, -up, up])
	else:
		sides.append_array([left, -left, up, -up])
	var deg := AVOID_STEP_DEG
	while deg <= AVOID_MAX_DEG + 0.1:
		var a := deg_to_rad(deg)
		for side in sides:
			var d := _clamp_pitch((want * cos(a) + side * sin(a)).normalized())
			if _blocker(d, keep, true) == -1:
				_avoid_dir = side
				return d
		deg += AVOID_STEP_DEG
	_avoid_dir = Vector3.ZERO
	return want


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
	var p := tip_position + travel * distance + drift
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
				_slide(n)
				moved = true
		# 0.8.2: the rock bands are walls like the rocks: the tip slides along their face.
		if not ground.bands.is_empty():
			var out := ground.band_push(p, 0.08)
			if out != p:
				var n := (out - p).normalized()
				p = out
				_slide(n)
				moved = true
		if p.y < -Underground.DEPTH or p.y > -0.05:
			p.y = clampf(p.y, -Underground.DEPTH, -0.05)
			heading = _flattened(Vector3(heading.x, 0.0, heading.z))
			travel = _flattened(Vector3(travel.x, 0.0, travel.z))
			moved = true
		var flat := Vector2(p.x, p.z)
		if flat.length() > ground.extent:
			var n := Vector3(flat.x, 0.0, flat.y).normalized()
			# Turn back inward a little, so the corner of floor and edge cannot trap the root.
			heading = _flattened(heading - n * maxf(0.0, heading.dot(n)) - n * 0.3)
			travel = _flattened(travel - n * maxf(0.0, travel.dot(n)) - n * 0.3)
			flat = flat.normalized() * ground.extent
			p = Vector3(flat.x, p.y, flat.y)
			moved = true
		# 0.8.2.2: old main roots are walls too, at AVOID_HARD (never closer than the tip already
		# is): the bend in _avoid normally keeps the tip clear long before this.
		if not _avoid_segs.is_empty():
			var c := _closest_on(p, _avoid_segs)
			var keep := minf(AVOID_HARD, _seg_distance(tip_position, _avoid_segs))
			if p.distance_to(c) < keep - 1e-4:
				var away := p - c
				if away.length_squared() < 1e-10:
					away = tip_position - c
				if away.length_squared() > 1e-10:
					p = c + away.normalized() * keep
					moved = true
		if not moved:
			break
	heading = _clamp_pitch(heading)
	travel = _clamp_pitch(travel)
	_update_right()
	if ground.is_inside_rock(p, 0.0):
		return  # boxed in: stay put this step (and pay nothing)
	# Squeezed between old roots (pushed out of one into the next): stay put too.
	if not _avoid_segs.is_empty() and _seg_distance(p, _avoid_segs) < minf(AVOID_HARD, _seg_distance(tip_position, _avoid_segs)) - 0.01:
		return
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


## A wall's face (normal `n`) slides the held heading as before (0.8.2), and the travel direction
## with it.
func _slide(n: Vector3) -> void:
	var slid := heading - n * minf(0.0, heading.dot(n))
	heading = slid.normalized() if slid.length_squared() > 1e-4 else _right
	var t := travel - n * minf(0.0, travel.dot(n))
	travel = t.normalized() if t.length_squared() > 1e-4 else heading


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


# --- the end of the night over several frames (0.8.2.3) ----------------------------------

## The small roots at the end of the night (fine roots, the side roots of the leftover; the view's
## settle not included) took up to 28 ms in one frame on the PC (about 100 ms on a phone). With
## `end_in_frames` (the root view sets it) they grow over the next frames instead: end_run()
## grows until END_FIRST_USEC is spent and returns with end_pending() true; grow_on() goes on
## each frame. The steps and their order are exactly those of one go (the work only pauses
## between them, and the night's sim does not touch the roots or the stock meanwhile), so the
## roots are the same. Without it (tools, tests, GameState.steer) everything grows at once.
signal _grow_on
signal _start_end
var end_in_frames: bool = false
## Time for the end's first share, in the frame the run ended (the tip's step ran there too).
const END_FIRST_USEC: int = 2000
const _NO_PAUSE: int = 1 << 62
var _ending: bool = false
var _end_until: int = _NO_PAUSE


## True while the end of the night is still growing (see end_in_frames).
func end_pending() -> bool:
	return _ending


## Grows the end of the night on for about `budget_usec`; true once it is complete.
func grow_on(budget_usec: int) -> bool:
	if _ending:
		_end_until = Time.get_ticks_usec() + budget_usec
		_grow_on.emit()
	return not _ending


## Grows whatever is left of the end of the night now (before a save, a new run or a lookup).
func finish_pending() -> void:
	if _ending:
		_end_until = _NO_PAUSE
		_grow_on.emit()


func _pause_due() -> bool:
	return _ending and Time.get_ticks_usec() > _end_until


## Runs `job` (a coroutine of the end of the night). It is started through a signal, so it may
## pause at `await _grow_on` without a caller waiting for it.
func _begin_end(job: Callable) -> void:
	_ending = true
	_end_until = Time.get_ticks_usec() + END_FIRST_USEC if end_in_frames else _NO_PAUSE
	_start_end.connect(job, CONNECT_ONE_SHOT)
	_start_end.emit()


func _end_done() -> void:
	_ending = false
	_end_until = _NO_PAUSE


## Ends the run: fine roots sprout along the new path toward the dots within fine_radius
## (first level). Whatever life force is left (the player ended early, or the root reached its
## node budget) goes into a second and third level of side roots (0.8.2, specs/side-roots.md);
## what was spent on the root itself makes it thicker. All of it is set once, here (0.8.2.3:
## over a few frames with end_in_frames).
func end_run(ground: Underground, res: Resources) -> void:
	if not run_active:
		return
	_begin_end(_end_run_job.bind(ground, res))


func _end_run_job(ground: Underground, res: Resources) -> void:
	run_active = false
	leftover_spent = 0.0
	side_nodes_grown = PackedInt32Array([0, 0])
	if run_node_count() == 0:
		# 0.8.2.2: ended before the root grew at all: the whole tank grows small roots from the
		# whole network (no main root tonight).
		await _spend_at_once(ground, res)
		_end_done()
		return
	leftover_spent = res.life_force
	res.life_force = 0.0
	var tank := maxf(run_tank, leftover_spent)
	var left_share := leftover_spent / tank if tank > 1e-6 else 0.0
	while thickness.size() <= main_root_count:
		thickness.append(1.0)
	thickness[main_root_count] = 1.0 + thick_gain * clampf(1.0 - left_share, 0.0, 1.0)
	var path := PackedInt32Array()
	for id in range(run_first_new_id, graph.size()):
		path.append(id)
	_grow_ground = ground
	var first_fine := graph.size()
	await _grow_fine_roots(path, ground, res)
	if left_share >= side_min_left_share and leftover_spent > 0.0:
		await _grow_side_roots(path, first_fine, ground, res)
	if _pause_due():
		await _grow_on
	main_root_count += 1
	graph.update_radii()
	_end_done()


## The player ends tonight's root here.
func finish_early(ground: Underground, res: Resources) -> void:
	end_run(ground, res)


## 0.8.2.2 (specs/0.8.md "Stop at once"): the night ends before a start was picked: the whole tank
## grows second- and third-level side roots from the whole root network toward the nearest fresh
## dots; no main root tonight. False if there was nothing to spend.
func end_at_once(ground: Underground, res: Resources) -> bool:
	if run_active or res.life_force <= 1e-4:
		return false
	run_first_new_id = graph.size()
	run_start_id = -1
	run_length = 0.0
	run_totals = PackedFloat32Array([0, 0, 0, 0])
	_run_touched = {}
	run_tank = res.life_force
	_begin_end(_at_once_job.bind(ground, res))
	return true


func _at_once_job(ground: Underground, res: Resources) -> void:
	await _spend_at_once(ground, res)
	_end_done()


func _spend_at_once(ground: Underground, res: Resources) -> void:
	leftover_spent = res.life_force
	res.life_force = 0.0
	side_nodes_grown = PackedInt32Array([0, 0])
	_grow_ground = ground
	if leftover_spent > 0.0:
		await _grow_side_roots(PackedInt32Array(), graph.size(), ground, res)
	if _pause_due():
		await _grow_on
	graph.update_radii()


## First level: space colonization from the new path toward the dots within fine_radius.
func _grow_fine_roots(path: PackedInt32Array, ground: Underground, res: Resources) -> void:
	var marker_ids := PackedInt32Array()
	var seen := {}
	for id in path:
		if id & 15 == 15 and _pause_due():
			await _grow_on
		for d in ground.dots_near(graph.positions[id], fine_radius):
			if not seen.has(d):
				seen[d] = true
				marker_ids.append(d)
	if marker_ids.is_empty():
		return
	var markers := PackedVector3Array()
	for d in marker_ids:
		markers.append(ground.dot_positions[d])
	var grown: PackedInt32Array = await _colonize(path, markers, Budgets.FINE_ROOTS_PER_MAIN_ROOT, fine_radius, 0.22, 0.14, INF, 1)
	if _pause_due():
		await _grow_on
	_collect_ids(_reached_by(grown, marker_ids, ground, 0.22), ground, res, fine_share)


## Second and third level (0.8.2): the leftover buys side_nodes_per_life_force nodes per point
## (at most side_nodes_max), side_level3_share of them for the third level.
## 0.8.2.2 (specs/0.8.md "Small roots everywhere"): the second level grows from the whole root
## network, not only tonight's root: toward the fresh dots nearest to any root first (main, fine
## or side, of any night), within its reach, until the nodes run out. Nodes no dot in reach can
## use still sprout the short tips around tonight's root (or the newest root ends), so the fan shows.
func _grow_side_roots(path: PackedInt32Array, first_fine: int, ground: Underground, res: Resources) -> void:
	var total := mini(mini(side_nodes_max, Budgets.SIDE_ROOTS_PER_MAIN_ROOT), int(round(leftover_spent * side_nodes_per_life_force)))
	if total <= 0:
		return
	var budget3 := int(round(total * side_level3_share))
	var budget2 := total - budget3
	var reach2 := minf(side_reach_max, side_reach_base + side_reach_per_life_force * leftover_spent)
	var first2 := graph.size()
	var near: Dictionary = await _nearest_fresh(reach2, budget2, 0.12, ground)
	var got2 := PackedInt32Array()
	if not (near["markers"] as PackedVector3Array).is_empty():
		got2 = await _colonize(near["starts"], near["markers"], budget2, reach2, 0.18, 0.12, reach2, 2)
		if _pause_due():
			await _grow_on
		_collect_ids(_reached_by(got2, _fresh_near(got2, 0.18, ground), ground, 0.18), ground, res, fine_share)
	var rest := budget2 - got2.size()
	if rest >= SIDE_SHORT_TIPS * 4:
		# Starts: the first level's tips, and points along the new root when it has few.
		var starts := PackedInt32Array()
		for id in range(first_fine, graph.size()):
			if id < first2 and graph.is_tip(id):
				starts.append(id)
		if starts.size() < SIDE_MIN_STARTS and not path.is_empty():
			var want := SIDE_MIN_STARTS - starts.size()
			for k in range(want):
				starts.append(path[mini(path.size() - 1, int(float(k + 1) / float(want) * (path.size() - 1)))])
		if starts.is_empty():
			starts = _newest_root_ends(SIDE_MIN_STARTS)
		var more: PackedInt32Array = await _side_level(starts, rest, reach2, 0.18, 0.12, 2, ground)
		if _pause_due():
			await _grow_on
		_collect_ids(_reached_by(more, _fresh_near(more, 0.18, ground), ground, 0.18), ground, res, fine_share)
		got2.append_array(more)
	side_nodes_grown[0] = got2.size()
	if budget3 <= 0 or got2.is_empty():
		return
	# Third level: from along and at the tips of second-level roots at least SIDE3_MIN_LENGTH long.
	var dist := {}
	for id in got2:
		var p := graph.parents[id]
		dist[id] = float(dist.get(p, 0.0)) + graph.positions[id].distance_to(graph.positions[p])
	var long_tips := {}
	for id in got2:
		if graph.is_tip(id) and float(dist[id]) >= SIDE3_MIN_LENGTH:
			long_tips[id] = true
	# Every node on a long enough second-level root may start one: its tip, and every third node
	# along it from 0.25 m out.
	var on_long := {}
	for tip in long_tips:
		var cur: int = tip
		while cur >= first2:
			on_long[cur] = true
			cur = graph.parents[cur]
	var starts3 := PackedInt32Array()
	for id in got2:
		if not on_long.has(id):
			continue
		if long_tips.has(id) or posmod(id, 3) == 0 and float(dist[id]) >= 0.25:
			starts3.append(id)
	if starts3.is_empty():
		return
	var reach3 := minf(side3_reach_max, side3_reach_base + side3_reach_per_life_force * leftover_spent)
	var got3: PackedInt32Array = await _side_level(starts3, budget3, reach3, 0.12, 0.08, 3, ground)
	side_nodes_grown[1] = got3.size()
	if _pause_due():
		await _grow_on
	_collect_ids(_reached_by(got3, _fresh_near(got3, 0.12, ground), ground, 0.12), ground, res, side3_share)


## The fresh dots within `reach` of any root node, nearest first, as many as about `budget` nodes
## of `step` reach (each dot costs its distance in steps): {"starts": the nearest node of each,
## "markers": their positions, "dots": their ids}.
func nearest_fresh(reach: float, budget: int, step: float, ground: Underground) -> Dictionary:
	finish_pending()
	# Called dynamically: with nothing pending it never pauses, so it returns its result at once.
	return _nearest_fresh.call(reach, budget, step, ground)


func _nearest_fresh(reach: float, budget: int, step: float, ground: Underground) -> Dictionary:
	var starts := PackedInt32Array()
	var markers := PackedVector3Array()
	var dots := PackedInt32Array()
	# Root nodes by cells of the reach (a dot looks at its cell and the 26 around it).
	var cell := maxf(reach, 0.5)
	var grid := {}
	for id in range(graph.size()):
		if id & 255 == 255 and _pause_due():
			await _grow_on
		if side_start_level < 3.0 and root_level(id) > int(side_start_level):
			continue
		var k := Vector3i((graph.positions[id] / cell).floor())
		if not grid.has(k):
			grid[k] = PackedInt32Array()
		var arr: PackedInt32Array = grid[k]
		arr.append(id)
		grid[k] = arr
	var cands: Array = []
	var r2 := reach * reach
	for d in range(ground.dot_positions.size()):
		if d & 127 == 127 and _pause_due():
			await _grow_on
		if ground.dot_collected[d] != 0 or not is_fresh(d):
			continue
		var p := ground.dot_positions[d]
		var k := Vector3i((p / cell).floor())
		var best := -1
		var best_d := r2
		for dx in range(-1, 2):
			for dy in range(-1, 2):
				for dz in range(-1, 2):
					var key := k + Vector3i(dx, dy, dz)
					if not grid.has(key):
						continue
					for id in (grid[key] as PackedInt32Array):
						var dd := graph.positions[id].distance_squared_to(p)
						if dd < best_d:
							best_d = dd
							best = id
		if best >= 0:
			cands.append(Vector3(sqrt(best_d), d, best))
	cands.sort_custom(func(a: Vector3, b: Vector3) -> bool: return a.x < b.x or a.x == b.x and a.y < b.y)
	var used := 0
	var seen := {}
	for c in cands:
		var cost := maxi(1, ceili(c.x / step))
		if used + cost > budget:
			break
		used += cost
		var start := int(c.z)
		if not seen.has(start):
			seen[start] = true
			starts.append(start)
		markers.append(ground.dot_positions[int(c.y)])
		dots.append(int(c.y))
	return {"starts": starts, "markers": markers, "dots": dots}


## The ends of the newest main roots (the trunk's root node if there are none): where an empty
## night's short tips sprout.
func _newest_root_ends(n: int) -> PackedInt32Array:
	var out := PackedInt32Array()
	for id in range(graph.size() - 1, 0, -1):
		if out.size() >= n:
			break
		var fl = graph.flags[id]
		if (fl == null or not (fl as Dictionary).has("fine")) and graph.is_tip(id):
			out.append(id)
	if out.is_empty():
		out.append(0)
	return out


## One side-root level from `starts`: toward the fresh dots within `reach` of a start, and where
## a start has none, SIDE_SHORT_TIPS short tips into the soil. No node goes further than `reach`
## from its start. Returns the new nodes' ids (flagged "fine" and "side" = level).
func _side_level(starts: PackedInt32Array, budget: int, reach: float, kill: float, step: float, level: int, ground: Underground) -> PackedInt32Array:
	var markers := PackedVector3Array()
	var seen := {}
	# Short tips per empty start: enough to spend about the budget (a tip grows about
	# 0.65 x reach in steps of `step`).
	var per_tip := maxf(1.0, 0.65 * reach / step)
	var tips := clampi(int(round(float(budget) / (per_tip * maxf(1.0, starts.size())))), SIDE_SHORT_TIPS, SIDE_SHORT_TIPS_MAX)
	for s in starts:
		var at := graph.positions[s]
		var own := 0
		for d in ground.dots_near(at, reach):
			if not is_fresh(d) or ground.dot_collected[d] != 0:
				continue
			own += 1
			if not seen.has(d):
				seen[d] = true
				markers.append(ground.dot_positions[d])
		if own > 0:
			continue
		# Nothing to drink in reach: a few short tips still sprout, outward and a little down.
		var out := graph.direction_of(s)
		for _k in range(tips):
			var v := (out + Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.9, 0.4), rng.randf_range(-1, 1))).normalized()
			var m := at + v * rng.randf_range(0.45, 0.85) * reach
			m.y = minf(m.y, -0.1)
			if not ground.is_inside_rock(m, 0.05):
				markers.append(m)
	if markers.is_empty():
		return PackedInt32Array()
	return await _colonize(starts, markers, budget, reach, kill, step, reach, level)


## Space colonization on a small temporary graph holding only `starts` (copied under a resting
## root), toward `markers`, then grafted onto the real graph. `max_from_start` drops nodes that
## would end further than that from the start they grew from. Returns the grafted ids.
func _colonize(starts: PackedInt32Array, markers: PackedVector3Array, budget: int, influence: float, kill: float, step: float, max_from_start: float, level: int) -> PackedInt32Array:
	var grafted := PackedInt32Array()
	if starts.is_empty() or budget <= 0:
		return grafted
	var temp := PlantGraph.new(graph.positions[starts[0]] + Vector3.UP * 50.0, starts.size() + 1 + budget)
	temp.set_flag(0, "rest", true)
	var to_real := {}
	var origin := {}
	for s in starts:
		var t := temp.add_node(0, graph.positions[s])
		to_real[t] = s
		origin[t] = graph.positions[s]
	var first_new := temp.size()
	var sc := SpaceColonization.new(rng)
	sc.influence_radius = influence
	sc.kill_distance = kill
	sc.step_length = step
	sc.bias_direction = Vector3.DOWN
	sc.bias_strength = 0.1
	sc.jitter = 0.15
	sc.markers = markers
	var guard := 0
	while not sc.markers.is_empty() and not temp.is_full() and guard < 120:
		if _pause_due():
			await _grow_on
		if sc.step(temp) == 0:
			break
		guard += 1
	for t_id in range(first_new, temp.size()):
		if t_id & 31 == 31 and _pause_due():
			await _grow_on
		var tp := temp.parents[t_id]
		if not to_real.has(tp):
			continue  # its parent was dropped
		var o: Vector3 = origin[tp]
		if temp.positions[t_id].distance_to(o) > max_from_start:
			continue
		# Side and fine roots do not grow into rock either (0.8.2: the bands are rock too).
		if _grow_ground != null and _grow_ground.is_inside_rock(temp.positions[t_id]):
			continue
		var real := graph.add_node(to_real[tp], temp.positions[t_id])
		if real < 0:
			break
		graph.set_flag(real, "fine", main_root_count)
		if level > 1:
			graph.set_flag(real, "side", level)
		to_real[t_id] = real
		origin[t_id] = o
		grafted.append(real)
	return grafted


## Fresh dots within `radius` of the nodes `ids`.
func _fresh_near(ids: PackedInt32Array, radius: float, ground: Underground) -> PackedInt32Array:
	var out := PackedInt32Array()
	var seen := {}
	for id in ids:
		for d in ground.dots_near(graph.positions[id], radius):
			if not seen.has(d) and is_fresh(d) and ground.dot_collected[d] == 0:
				seen[d] = true
				out.append(d)
	return out


## Of the dots `candidates`, those a node of `ids` came within `radius` of (a root reached them).
func _reached_by(ids: PackedInt32Array, candidates: PackedInt32Array, ground: Underground, radius: float) -> PackedInt32Array:
	var out := PackedInt32Array()
	if ids.is_empty():
		return out
	var want := {}
	for d in candidates:
		want[d] = true
	var r2 := radius * radius * 1.05
	for id in ids:
		var p := graph.positions[id]
		for d in ground.dots_near(p, radius):
			if want.has(d) and ground.dot_positions[d].distance_squared_to(p) <= r2:
				want.erase(d)
				out.append(d)
	return out


## The level a root node belongs to: 0 a main root, 1 a fine root, 2 or 3 a side root.
func root_level(id: int) -> int:
	if graph.get_flag(id, "fine", -1) < 0:
		return 0
	return int(graph.get_flag(id, "side", 1))


## How thick a main root is drawn and how much seep it takes (1.0 for old saves).
func thickness_of(main: int) -> float:
	return thickness[main] if main >= 0 and main < thickness.size() else 1.0


func _collect_ids(ids: PackedInt32Array, ground: Underground, res: Resources, share: float = -1.0) -> void:
	if share < 0.0:
		share = tip_share
	var got := ground.collect(ids, res, share, species.water_draw, run_room)
	for j in range(got.size()):
		var i := got[j]
		run_totals[ground.dot_kinds[i]] += ground.last_drawn[j]
		_run_touched[i] = true
		tapped[i] = true
	last_collected.append_array(got)
	if not run_room.is_empty():
		# Reached but not drunk (the tree was full of that kind): tapped for the old roots.
		for i in ids:
			if ground.dot_collected[i] == 0 and ground.dot_amounts[i] > 0.0:
				_run_touched[i] = true
				tapped[i] = true


## Every night the whole root network keeps drinking from the deposits it has reached, and
## groundwater seeps in. `room` (per Resources.Kind, empty = no limit) caps what it draws: a tree
## whose stock is full draws less, and the deposits keep the rest. Returns what it drew, by kind.
## `seep_cap`: the most water the seep brings tonight (seep_day_cover of a calm day's water).
func drink_tapped(ground: Underground, res: Resources, room: PackedFloat32Array = PackedFloat32Array(), seep_cap: float = INF) -> PackedFloat32Array:
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
		var seep := minf(seep_length() * seep_per_metre, seep_cap) if k == Resources.Kind.WATER else 0.0
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


## Metres of root that groundwater seeps into: main roots in full (a thicker root more, 0.8.2),
## fine and second-level roots by fine_seep_share, third-level roots by half of that.
func seep_length() -> float:
	var total := 0.0
	var per_main := PackedFloat32Array()
	for k in range(thickness.size()):
		per_main.append(1.0 + seep_thick_gain * (thickness[k] - 1.0) / maxf(thick_gain, 1e-6))
	for id in range(1, graph.size()):
		var metres := graph.positions[id].distance_to(graph.positions[graph.parents[id]])
		var fl = graph.flags[id]
		if fl == null or not (fl as Dictionary).has("fine"):
			var main := int((fl as Dictionary).get("main", -1)) if fl != null else -1
			total += metres * (per_main[main] if main >= 0 and main < per_main.size() else 1.0)
		elif int((fl as Dictionary).get("side", 1)) >= 3:
			total += metres * fine_seep_share * 0.5
		else:
			total += metres * fine_seep_share
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
	finish_pending()
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
		"run_tank": run_tank,
		# 0.8.2.1 (bug 7): the cap on tonight's intake, so a game loaded mid-run keeps it.
		"run_room": run_room.duplicate(),
		"thickness": Array(thickness),
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
	r.travel = r.heading
	r._start_main = int(r.graph.get_flag(r.run_start_id, "main", -1)) if r.run_start_id > 0 and r.run_start_id < r.graph.size() else -1
	r.run_start_id = int(d.get("run_start_id", -1))
	r.run_first_new_id = int(d.get("run_first_new_id", -1))
	r.run_length = float(d.get("run_length", 0.0))
	r.run_cost_scale = float(d.get("run_cost_scale", 1.0))
	r.run_speed_scale = float(d.get("run_speed_scale", 1.0))
	r._paced = bool(d.get("paced", false))
	r.run_seconds_target = float(d.get("run_seconds_target", r.calm_run_seconds))
	r._run_time = float(d.get("run_time", 0.0))
	r.run_tank = float(d.get("run_tank", 0.0))
	r.run_room = []
	for v in d.get("run_room", []):
		r.run_room.append(float(v))
	# Old saves: every root 1.0x (no entry).
	r.thickness = PackedFloat32Array(d.get("thickness", []))
	r._update_right()
	return r
