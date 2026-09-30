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
## Soft Liebig floor: growth with a needed nutrient (N, P or K) used up, as a share of full speed.
var liebig_floor: float = 0.45
## The seed's own reserves: on the first SEEDLING_DAYS days (natural_form) a missing N, P or K
## slows growth only to this share, so every early day shows (sim-0.6.3: unsteered oak and beech
## grew 21 to 24 segments on days 2 and 3).
const SEEDLING_FLOOR: float = 0.6
const SEEDLING_DAYS: int = 3
## The tree takes up at most this many days of a full calm day's need of each kind (day_capacity):
## above it the old roots draw less and the deposits keep the rest for later (sim-0.6.3: water
## stocks covered 5 to 10 days, so thirst never showed and the night's water hardly mattered).
## The new root's own finds are never held back.
var hold_days: float = 2.0
## How far a missing nutrient takes away the boost's extra light (0 = not at all, 1 = fully).
var boost_liebig: float = 1.0
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
## The colonizer's own twig jitter; species crookedness is added to it.
const BASE_JITTER: float = 0.08
## An unbranched end of up to this many segments counts as a shoot tip (twin buds).
const SHOOT_TIP_SEGMENTS: int = 3
## Shade dieback: column width, living nodes above a tip that shade it, share dying per day.
const SHADE_CELL: float = 1.0
const SHADE_NODES: int = 12
const SHADE_DIEBACK_SHARE: float = 0.05
## Branches the tree marks (0.7, notes/marks-0.7.md): a shaded twig that the seeded dieback will
## take within MARK_WARN_DAYS shows it first (thin, dull leaves, greying bark). At most MARK_MAX
## at a time; MARK_TWIG segments from the tip towards its fork carry the sign.
const MARK_MAX := 3
const MARK_WARN_DAYS := 2
const MARK_TWIG := 6
## A new mark sits at least this far above the crown base: the crown lifts about 0.3 to 0.8 m a
## day in the second week, and a twig it sheds first would lose its sign without dying back.
const MARK_ABOVE_BASE := 0.5
## The sign covers at least this many segments: a shaded tip right at a fork is one leaf clump,
## too small to see from the normal camera, so the sign reaches back over the fork into the
## little branch it grows on, as long as that branch holds at most MARK_BRANCH_MAX segments.
const MARK_TWIG_MIN := 3
const MARK_BRANCH_MAX := 12
## The sign on the last day before the dieback, and on the first of two days' warning.
const MARK_LAST_DAY := 1.0
const MARK_FIRST_DAY := 0.7
## Water each leaf cluster drinks per day.
const WATER_UPKEEP_PER_LEAF: float = 0.01
var marker_radius: float = 0.8
## The game's own trees follow a real growth curve (0.6.2): a thin whip for the first days, one
## clear leader, the height after the species' curve over its month (target_height), the pace
## rising as the tree grows (growth_ramp) and the crown lifting (shed_lower_branches). Off for
## the bare simulation (the forest's trees, the colonizer's tests).
var natural_form: bool = false

## Care (0.6.3): what the tree lacks today, judged at sunrise from what the night brought
## (assess_needs), 0..1 per Resources.Kind; `care_prev` is yesterday's, eased out over the morning.
var care_need: PackedFloat32Array = PackedFloat32Array([0, 0, 0, 0])
var care_prev: PackedFloat32Array = PackedFloat32Array([0, 0, 0, 0])
## Graph size at today's and yesterday's sunrise: nodes from `young_from` on are the new shoots.
var dawn_size: int = 0
var young_from: int = 0
## Pruning with an effect (0.6.3): cuts since the last sunrise, each {"at": [x, y, z], "from":
## node the branch was cut from, "nodes": segments cut}. At sunrise a share comes back as vigour.
var cuts: Array = []
## The last cut, for the care page: {"day", "nodes", "buds", "regrown", "woken" (bool)}.
var last_cut: Dictionary = {}
## Refund nodes waiting for the dawn burst (the whole crown's share of a cut).
var _vigour_nodes: int = 0
## Pruned wood dropped from the graph (compact_dead_wood); node costs still count it.
var removed_nodes: int = 0
## The twigs marked for pruning (0.7): [{"id": tip, "due": day it dies back, "since": first day
## shown}], set at sunrise (update_marks). Only a forecast of shade_dieback: it changes nothing.
var marks: Array = []
## Share of the cut segments that come back as vigour at the next sunrise (spec: 0.2 to 0.4).
const PRUNE_REFUND := 0.3
## Share of a cut's refund that wakes buds right below the cut; the rest goes to the crown.
const PRUNE_NEAR_SHARE := 0.7
## Buds are woken on the branch within this distance below the cut.
const PRUNE_BUD_REACH := 1.2
## Markers seeded around a cut at dawn (half the cut, within these bounds): today's growth
## is drawn there.
const PRUNE_MARKERS_MIN := 6
const PRUNE_MARKERS_MAX := 30
## A cut of this many segments or more wakes three buds instead of two.
const PRUNE_BIG_CUT := 20
## Pruned wood leaves the graph at sunrise once the graph is this full.
const COMPACT_AT := 0.85
## A tree counts as finished only once it is at least this share of its species' full height
## (natural_form): a pruned bush with the segments of a tree is not a finished tree (sim-0.6.3).
const FINISH_HEIGHT_SHARE := 0.4
## Segments owed to the day's growth: sycamore's twin buds grow at once when a shoot tip is cut,
## and are then taken from the ordinary growth that follows, so pruning never adds growth.
var _growth_debt: int = 0


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
	# Only living wood counts: a cut or shaded-off branch no longer makes the tree taller.
	var h := 0.0
	for id in range(graph.size()):
		if graph.positions[id].y > h and not graph.get_flag(id, "dead", false):
			h = graph.positions[id].y
	return h


## Segments that are alive (not pruned, not died back).
func living_nodes() -> int:
	var n := 0
	for id in range(graph.size()):
		if not graph.get_flag(id, "dead", false):
			n += 1
	return n


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
	resources.life_force += effective_leaves() * life_force_per_tip * clock.life_force_light() * delta 			* species.life_force_factor(clock.day_count, clock.boost_active) * young_leaf_bonus()

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
	var factor := Resources.growth_factor(resources.stock, species.needs, growth_floor())
	# Light is not capped at 1: the boosted sun (up to 3x) speeds growth at any hour.
	var cap := max_pace()
	var pace := cap if day_pace <= 0.0 else minf(day_pace, cap)
	var grow_light := light
	if clock.boost_active:
		# A brighter sun cannot make up for a missing nutrient: the boost's extra light only
		# counts as far as N, P and K allow (without the soft floor).
		var calm := clock.sun_height()
		grow_light = calm + (light - calm) * lerpf(1.0, Resources.growth_factor(resources.stock, species.needs, 0.0), boost_liebig)
	_growth_accum += pace * grow_light * factor * delta
	var budget := int(_growth_accum)
	_growth_accum -= budget
	budget += _dawn_burst_budget(delta, sun, top)
	# Twin buds grown at a cut are part of the day's growth, not extra.
	if _growth_debt > 0 and budget > 0:
		var owed := mini(_growth_debt, budget)
		budget -= owed
		_growth_debt -= owed
	# Gravitropism adds an upward pull (alder) or takes some away (beech's flat layers).
	colonizer.bias_direction = (Vector3.UP * maxf(0.05, 1.0 - species.phototropism + species.gravitropism) + sun * species.phototropism).normalized()
	# Oak's zigzag branches: an extra kink per segment.
	colonizer.jitter = BASE_JITTER + species.crookedness
	# Longer shoots on a bigger tree, so the node budget reaches the species size.
	colonizer.step_length = 0.15 + top * 0.014
	if natural_form:
		# A sapling's internodes are short: a slender whip with short side shoots.
		colonizer.step_length = 0.09 + top * 0.017
	colonizer.kill_distance = colonizer.step_length * 1.6
	# Buds sense space further away in a bigger crown, so side branches can reach its edge.
	colonizer.influence_radius = clampf(crown_radius(top) * 0.5, 1.2, 4.0)
	if natural_form:
		# The grown crown reaches out to its edge: buds sense the space further away.
		colonizer.influence_radius = clampf(crown_radius(top) * 0.6, 1.2, 5.0)
	var affordable := _affordable_nodes()
	var first_new := graph.size()
	var grown := colonizer.step(graph, mini(budget, affordable))
	if grown > 0:
		_droop_twigs(first_new, top)
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
	var cap := species.max_height
	var crown_floor := maxf(MARKER_MIN_Y, top * 0.35)
	if natural_form:
		crown_floor = maxf(MARKER_MIN_Y, top * 0.42)
		# A young tree races for the light on one leader; it broadens as it nears its height.
		var youth := 1.0 - smoothstep(0.0, 0.45, top / species.max_height)
		leader_share = lerpf(leader_share, 0.6, youth)
		# Never taller than today's point on the growth curve: the rest fills the crown.
		cap = minf(cap, target_height() + 0.2)
		if top >= target_height():
			leader_share = 0.0
		crown_floor = maxf(crown_floor, crown_base())
	# Separate accumulators, so small ticks (60 fps) seed the leader as well as big ones.
	_leader_accum += amount * leader_share
	_marker_accum += amount * (1.0 - leader_share)
	var leader := int(_leader_accum)
	_leader_accum -= leader
	var crown := int(_marker_accum)
	_marker_accum -= crown
	var limit := mini(live_markers * (2 if natural_form else 1), Budgets.TREE_MARKERS)
	var flat := Vector3(sun.x, 0.0, sun.z) * steer
	# Nothing is seeded above the species' full height: the tree stops growing taller there.
	if leader > 0:
		colonizer.seed_sphere(Vector3(0, top + 0.45, 0) + flat * 0.4, 0.45, leader, limit, MARKER_MIN_Y, maxf(cap, top + 0.05))
	# The crown starts above a clear trunk, so the base does not keep branching into a bush.
	colonizer.seed_sphere(marker_center(sun, top, steer), r, crown, limit, crown_floor, maxf(cap, top))


## Centre of the crown sphere for new markers.
func marker_center(sun: Vector3, top: float, steer: float = 1.0) -> Vector3:
	var r := crown_radius(top)
	var flat := Vector3(sun.x, 0.0, sun.z) * steer
	var c := Vector3(0, maxf(top, 0.15) * 0.6 + 0.3 + 0.7 * r * maxf(sun.y, 0.0) * steer, 0) + flat * r
	c.y = maxf(c.y, r * 0.5 + 0.1)
	return c


## The soft Liebig floor today: higher while the seedling lives off its seed (natural_form).
func growth_floor() -> float:
	if natural_form and clock.day_count <= SEEDLING_DAYS:
		return maxf(liebig_floor, SEEDLING_FLOOR)
	return liebig_floor


## Growth speed cap today: the species' pace (slow start, fast start) on the common maximum.
func max_pace() -> float:
	return max_growth_per_second * pace_factor()


## The species' pace today, and with natural_form the growth ramp.
func pace_factor() -> float:
	return species.pace_on(clock.day_count) * (growth_ramp() if natural_form else 1.0)


## Days since the first sunrise, with the part of today that has passed.
func age_days() -> float:
	return maxf(0.0, clock.day_count - 1 + minf(clock.time_of_day / clock.daylight_fraction, 1.0))


## The growth curve's height today (natural_form): about a metre a day through the first half
## of the species' month, easing off towards its full height at the end.
func target_height() -> float:
	var p := (age_days() + 0.25) / float(species.target_days)
	var f := minf(p - 0.1 * pow(maxf(0.0, (p - 0.5) / 0.5), 2.0), 0.96)
	return maxf(0.5, species.max_height * f)


## Share of the full pace today (natural_form): a seedling adds a few shoots a day, a grown
## crown many, so the month's segments go mostly into the big tree.
func growth_ramp() -> float:
	var p := age_days() / float(species.target_days)
	return lerpf(RAMP_START, RAMP_END, clampf(p / 0.75, 0.0, 1.0))


## A young tree (natural_form) has few leaves but each works harder, so its nights still
## have a real root run: life force per leaf rises as the growth ramp falls (up to x2.2).
func young_leaf_bonus() -> float:
	if not natural_form:
		return 1.0
	return clampf(1.0 / growth_ramp(), 1.0, 2.2)


const RAMP_START := 0.36
## At most this share of the living crown is shed each sunrise as the crown lifts.
const SHED_SHARE_PER_DAY := 0.06
const RAMP_END := 1.15


## Birch: new shoots out in the crown hang their tips (the leader stays upright).
func _droop_twigs(first_new: int, top: float) -> void:
	if species.twig_droop <= 0.0:
		return
	var r := crown_radius(top)
	for id in range(first_new, graph.size()):
		var p := graph.positions[id]
		var out := Vector2(p.x, p.z).length() / r
		if out > 0.35:
			p.y -= species.twig_droop * colonizer.step_length * clampf(out, 0.0, 1.0)
			graph.positions[id] = p


## Height of the bare trunk below the crown: none on a young tree, then the crown lifts.
func crown_base() -> float:
	var h := height()
	return h * species.crown_base * smoothstep(3.0, 10.0, h)


func crown_radius(top: float) -> float:
	if not natural_form:
		return clampf(0.3 + top * 0.6, 0.4, species.max_crown_radius)
	# A slender young tree, a broad grown one; a full crown keeps widening a little past the
	# species' radius, so a tree at its full height still has room to finish.
	var widen := 1.0 + 0.35 * clampf(float(graph.size() + removed_nodes) / species.finish_nodes, 0.0, 1.0)
	return clampf(0.25 + top * lerpf(0.35, 0.72, smoothstep(2.0, 10.0, top)), 0.3, species.max_crown_radius * widen)


## The crown lifts (natural_form, each sunrise): side branches on the trunk below the crown
## base are shed. The trunk is followed up its thickest living child; a fork almost as thick as
## the trunk is kept (a real second stem is not dropped overnight). Returns segments shed.
func shed_lower_branches() -> int:
	var base := crown_base()
	if not natural_form or base < 0.5:
		return 0
	# Side branches on the trunk below the crown base, lowest first.
	var low: Array[int] = []
	var cur := 0
	while graph.positions[cur].y < base:
		var main := -1
		var main_r := -1.0
		for c in graph.children[cur]:
			if not graph.get_flag(c, "dead", false) and graph.radii[c] > main_r:
				main_r = graph.radii[c]
				main = c
		if main < 0:
			break
		for c in graph.children[cur]:
			if c != main and not graph.get_flag(c, "dead", false) and graph.radii[c] < main_r * 0.7:
				low.append(c)
		cur = main
	# A few a night, never a big part of the crown at once: the lift is slow, as in a real tree.
	var allowance := int(living_nodes() * SHED_SHARE_PER_DAY) + 1
	var shed := 0
	for c in low:
		var size := _subtree_size(c)
		if shed + size > allowance:
			break
		shed += _kill_subtree(c, true)
	if shed > 0:
		graph.update_radii()
	return shed


## Living segments in the subtree of `node_id`.
func _subtree_size(node_id: int) -> int:
	var n := 0
	var stack: Array[int] = [node_id]
	while not stack.is_empty():
		var id: int = stack.pop_back()
		if graph.get_flag(id, "dead", false):
			continue
		n += 1
		for child in graph.children[id]:
			stack.append(child)
	return n


## Starts the dawn burst: part of what last night's nutrients buy is grown in the first
## seconds of the day. It only changes when the growth happens, not how much: nutrients cap it.
func start_dawn_burst() -> void:
	shed_lower_branches()
	# Yesterday's cuts answer: buds wake below them, the rest of the refund joins the burst.
	wake_buds_after_cuts()
	compact_dead_wood()
	update_marks(clock.day_count)
	young_from = dawn_size if dawn_size > 0 else graph.size()
	dawn_size = graph.size()
	assess_needs()
	var factor := Resources.growth_factor(resources.stock, species.needs, growth_floor())
	var burst_max := int(dawn_burst_max_nodes * pace_factor())
	_burst_nodes_left = mini(burst_max, int(_affordable_nodes() * dawn_burst_share * factor))
	# The crown's share of a cut comes on top of the burst's cap (still paid like any growth).
	_burst_nodes_left += maxi(0, mini(_vigour_nodes, _affordable_nodes() - _burst_nodes_left))
	_vigour_nodes = 0
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
	var factor := maxf(Resources.growth_factor(resources.stock, species.needs, growth_floor()), 0.15)
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
	var tips := float(tip_count())
	return tips if tips <= 50.0 else sqrt(50.0 * tips)


## A bigger tree needs more material per new segment (it also thickens everything below),
## so the growth spreads over the whole month instead of filling the budget early.
func node_cost() -> float:
	# Pruned wood dropped from the graph still counts: compaction changes no price.
	return cost_per_node * (1.0 + (graph.size() + removed_nodes) / 380.0)


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
## Twin buds (sycamore): cutting a shoot tip makes it fork into two new shoots at the cut.
## Pruning with an effect (0.6.3): the cut is remembered, and at the next sunrise a share of
## its wood comes back as new shoots below it and growth for the rest of the crown
## (wake_buds_after_cuts). A finished tree is pruned for its look only.
func prune(node_id: int) -> int:
	var fork := species.twin_buds and is_shoot_tip(node_id)
	var was_finished := is_finished()
	var marked_alive: Array[int] = []
	for m in marks:
		if not graph.get_flag(int(m["id"]), "dead", false):
			marked_alive.append(int(m["id"]))
	var count := _kill_subtree(node_id)
	# A marked tip was dying anyway (0.7): it gives nothing back, so cutting marked twigs never
	# grows a tree sooner than letting them die back (broken list 9).
	var dying := 0
	for id in marked_alive:
		if graph.get_flag(id, "dead", false):
			dying += 1
	var twins := 0
	if fork and count > 0:
		twins = fork_at(graph.parents[node_id], graph.positions[node_id] - graph.positions[graph.parents[node_id]])
		# The twin buds are this cut's answer (no refund on top of them), paid with nutrients and
		# taken from the day's growth that follows: cutting tips shapes a sycamore, never speeds it
		# up (sim-0.6.3: 20 tips a day finished 3 days sooner, each fork 2 free segments).
		if not was_finished:
			_pay_for(twins)
			_growth_debt += twins
	if count > 0:
		var p := graph.parents[node_id]
		var at := graph.positions[p].lerp(graph.positions[node_id], 0.35)
		if not was_finished and twins == 0 and count - dying > 0:
			cuts.append({"at": [at.x, at.y, at.z], "from": p, "nodes": count - dying})
		last_cut = {"day": clock.day_count, "nodes": count, "buds": twins, "regrown": twins,
			"woken": was_finished or twins > 0, "at": [at.x, at.y, at.z]}
	return count


## Segments that come back at the next sunrise for the cuts made since the last one: a share of
## the cut wood, capped by a share of a fifth of the living tree (several cuts add up, the fifth
## caps them).
func pending_refund() -> int:
	var cut := 0
	for c in cuts:
		cut += int(c["nodes"])
	var cap := maxi(3, int(PRUNE_REFUND * living_nodes() * 0.2))
	return mini(int(round(PRUNE_REFUND * cut)), cap)


## At sunrise: each cut wakes two or three buds on the branch just below it; they put out new
## shoots outward and toward the light (not back into the crowded inside). The rest of the
## refund joins the dawn burst for the whole crown. Paid with nutrients like any growth.
## Returns the segments grown near the cuts.
func wake_buds_after_cuts() -> int:
	if cuts.is_empty():
		return 0
	var total_cut := 0
	for c in cuts:
		total_cut += int(c["nodes"])
	var refund := pending_refund()
	var step := _shoot_step()
	var grown_near := 0
	var buds_woken := 0
	var vigour_before := _vigour_nodes
	var first_new := graph.size()
	for c in cuts:
		var r := int(round(float(refund) * int(c["nodes"]) / maxf(1.0, total_cut)))
		var near := int(round(r * PRUNE_NEAR_SHARE))
		var at := Vector3(float(c["at"][0]), float(c["at"][1]), float(c["at"][2]))
		# Two or three buds, each with at least a shoot of two segments (a single one would not show).
		var want_buds := maxi(1, mini(3 if int(c["nodes"]) >= PRUNE_BIG_CUT else 2, (near + 1) / 2))
		var buds := _buds_below(int(c["from"]), at, want_buds)
		var made := 0
		if not buds.is_empty() and near > 0:
			var per := maxi(1, int(ceil(float(near) / buds.size())))
			for b in buds:
				var n := mini(mini(per, near - made), _affordable_nodes())
				if n <= 0:
					break
				var g := _grow_shoot(b, at, n, step)
				if g > 0:
					buds_woken += 1
				made += g
				_pay_for(g)
		# The strength goes where the cut let the light in: markers around the cut draw part of
		# today's ordinary growth there (the same growth, placed near the cut, not extra).
		var room := clampi(int(c["nodes"]) / 2, PRUNE_MARKERS_MIN, PRUNE_MARKERS_MAX)
		colonizer.seed_sphere(at + _outward(at) * PRUNE_BUD_REACH * 0.5, PRUNE_BUD_REACH, room, Budgets.TREE_MARKERS, MARKER_MIN_Y, species.max_height)
		grown_near += made
		_vigour_nodes += maxi(0, r - made)
	if graph.size() > first_new:
		graph.update_radii()
	last_cut["buds"] = buds_woken
	last_cut["regrown"] = grown_near + _vigour_nodes - vigour_before
	last_cut["woken"] = true
	cuts.clear()
	return grown_near


## Up to `count` living nodes on the branch below a cut, within PRUNE_BUD_REACH of it, spread
## along it (never the trunk base).
func _buds_below(from: int, at: Vector3, count: int) -> Array[int]:
	var chain: Array[int] = []
	var cur := from
	while cur >= 3 and not graph.get_flag(cur, "dead", false) and graph.positions[cur].distance_to(at) <= PRUNE_BUD_REACH:
		chain.append(cur)
		cur = graph.parents[cur]
	if chain.is_empty() and from >= 2 and not graph.get_flag(from, "dead", false):
		chain.append(from)
	var out: Array[int] = []
	if chain.is_empty():
		return out
	for i in range(count):
		var id: int = chain[int(float(i) * chain.size() / count)]
		if not out.has(id):
			out.append(id)
	return out


## The side of the crown a point is on: away from the trunk's axis (up for a point on it).
func _outward(p: Vector3) -> Vector3:
	var flat := Vector3(p.x, 0.0, p.z)
	return flat.normalized() if flat.length_squared() > 1e-4 else Vector3.UP


## Length of one new segment for a tree this tall (as tick() sets it).
func _shoot_step() -> float:
	var top := height()
	return (0.09 + top * 0.017) if natural_form else (0.15 + top * 0.014)


## A new shoot of `segments` from bud `bud`: out of the crown and up to the light, splayed by
## the node, so two buds on one branch do not grow into each other. Returns segments grown.
func _grow_shoot(bud: int, cut_at: Vector3, segments: int, step: float) -> int:
	var h := hash([seed, "bud", bud, clock.day_count])
	var twist := float(posmod(h, 1000)) / 1000.0 * TAU
	var along := graph.direction_of(bud)
	var side := along.cross(Vector3.UP)
	if side.length_squared() < 1e-4:
		side = Vector3.RIGHT
	side = side.normalized().rotated(along, twist)
	var dir := (_outward(cut_at) * 0.8 + Vector3.UP * 0.6 + side * 0.5 + along * 0.3).normalized()
	var cur := bud
	var n := 0
	for _i in range(segments):
		var id := graph.add_node(cur, graph.positions[cur] + dir * step)
		if id < 0:
			break
		n += 1
		cur = id
		dir = (dir + Vector3.UP * 0.15).normalized()
	return n


## Pruned wood (dead, not shed) leaves the graph once it is COMPACT_AT full, so pruning never
## fills the node budget with dead wood. Shed wood stays (it counts toward the finished tree).
## Ids change; node costs keep counting what was dropped. Returns the nodes dropped.
func compact_dead_wood(force: bool = false) -> int:
	if not force and graph.size() < int(graph.max_nodes * COMPACT_AT):
		return 0
	var keep := PackedInt32Array()
	var remap := PackedInt32Array()
	remap.resize(graph.size())
	remap.fill(-1)
	for id in range(graph.size()):
		var dead: bool = graph.get_flag(id, "dead", false)
		var parent_kept := id == 0 or remap[graph.parents[id]] >= 0
		if parent_kept and (id < 2 or not dead or graph.get_flag(id, "shed", false)):
			remap[id] = keep.size()
			keep.append(id)
	var dropped := graph.size() - keep.size()
	if dropped <= 0:
		return 0
	var g := PlantGraph.new(graph.positions[0], graph.max_nodes)
	g.radii[0] = graph.radii[0]
	g.ages[0] = graph.ages[0]
	g.flags[0] = graph.flags[0]
	for i in range(1, keep.size()):
		var old := keep[i]
		var nid := g.add_node(remap[graph.parents[old]], graph.positions[old])
		g.radii[nid] = graph.radii[old]
		g.ages[nid] = graph.ages[old]
		g.flags[nid] = graph.flags[old]
	# The new-shoot boundary moves with the ids.
	var first := keep.size()
	for i in range(keep.size()):
		if keep[i] >= dawn_size:
			first = i
			break
	dawn_size = first
	graph = g
	var moved: Array = []
	for m in marks:
		var id := int(m["id"])
		if id < remap.size() and remap[id] >= 0:
			moved.append({"id": remap[id], "due": m["due"], "since": m["since"]})
	marks = moved
	removed_nodes += dropped
	return dropped


## What the tree lacks today (0.6.3 care), judged at sunrise from the stock the night brought:
## for each kind the share of a full calm day's growth the stock covers; Care.need_from turns it
## into 0..1. Yesterday's value is kept to ease out over the morning.
func assess_needs() -> void:
	care_prev = care_need.duplicate()
	var cover := day_coverage()
	for k in range(4):
		care_need[k] = Care.need_from(cover[k])


## Share of a full calm day's growth (day_capacity) each kind's stock covers, 0..1 (1 when the
## species needs none of it).
func day_coverage() -> PackedFloat32Array:
	var out := PackedFloat32Array([1, 1, 1, 1])
	var want := day_capacity() * node_cost()
	for k in range(4):
		if species.needs[k] > 0.0 and want > 0.0:
			out[k] = clampf(resources.stock[k] / (want * species.needs[k]), 0.0, 1.0)
	return out


## Segments the tree would grow in a full calm day if nothing were short: the growth cap over
## the day's light (the sun's arc averages 2/pi of noon) plus the dawn burst.
func day_capacity() -> float:
	var day_seconds := clock.seconds_per_day * clock.daylight_fraction
	return max_pace() * day_seconds * 2.0 / PI + dawn_burst_max_nodes * pace_factor()


## The care signal of each kind right now: yesterday's need eased into today's over the morning
## (Care.EASE_SHARE of the daylight), today's need from mid-morning on and through the night.
func care_shown() -> PackedFloat32Array:
	var e := 1.0
	if clock.is_day():
		e = smoothstep(0.0, Care.EASE_SHARE * clock.daylight_fraction, clock.time_of_day)
	var out := PackedFloat32Array([0, 0, 0, 0])
	for k in range(4):
		out[k] = lerpf(care_prev[k], care_need[k], e)
	return out


## A shoot tip: an unbranched living end of at most SHOOT_TIP_SEGMENTS segments.
func is_shoot_tip(node_id: int) -> bool:
	if node_id <= 0 or node_id >= graph.size() or graph.get_flag(node_id, "dead", false):
		return false
	var cur := node_id
	for _i in range(SHOOT_TIP_SEGMENTS):
		var alive: Array[int] = []
		for c in graph.children[cur]:
			if not graph.get_flag(c, "dead", false):
				alive.append(c)
		if alive.is_empty():
			return true
		if alive.size() > 1:
			return false
		cur = alive[0]
	return false


## Two new shoots from `parent`, splayed to either side of `dir`. Returns how many grew.
func fork_at(parent: int, dir: Vector3) -> int:
	if dir.length_squared() < 1e-8:
		dir = Vector3.UP
	dir = dir.normalized()
	var side := dir.cross(Vector3.UP)
	if side.length_squared() < 1e-4:
		side = Vector3.RIGHT
	side = side.normalized()
	var n := 0
	for k: float in [-1.0, 1.0]:
		var d := (dir + side * 0.75 * k + Vector3.UP * 0.15).normalized()
		if graph.add_node(parent, graph.positions[parent] + d * colonizer.step_length) >= 0:
			n += 1
	if n > 0:
		graph.update_radii()
	return n


func _kill_subtree(node_id: int, shed: bool = false) -> int:
	var count := 0
	var stack: Array[int] = [node_id]
	while not stack.is_empty():
		var id: int = stack.pop_back()
		if graph.get_flag(id, "dead", false):
			continue
		graph.set_flag(id, "dead", true)
		if shed:
			graph.set_flag(id, "shed", true)
		count += 1
		for child in graph.children[id]:
			stack.append(child)
	return count


## Shade dieback, once a day at sunrise: a leaf tip with much living crown right above it is
## shaded, and some shaded tips die back each day (design doc: soft failure). The species
## sets the speed (birch twice, beech never). Deterministic from the seed, the node and the day.
## Returns how many tips died.
func shade_dieback(day: int) -> int:
	var rate := SHADE_DIEBACK_SHARE * species.shade_dieback
	if rate <= 0.0:
		return 0
	var died := 0
	for id in shaded_tips():
		if _dies_back(id, day):
			graph.set_flag(id, "dead", true)
			# The twig it ended stays bare (HeroCrown.leafy_nodes): in the sim it is no leaf
			# cluster any more either (it has a child, so it is not a tip).
			graph.set_flag(graph.parents[id], "withered", true)
			died += 1
	return died


## The seeded roll of shade dieback: a shaded tip `id` dies back at the sunrise of `day`.
func _dies_back(id: int, day: int) -> bool:
	var rate := SHADE_DIEBACK_SHARE * species.shade_dieback
	return rate > 0.0 and float(posmod(hash([seed, "shade", id, day]), 1000)) < rate * 1000.0


## Branches the tree marks (0.7), once a day at sunrise after the dieback and the crown's lift:
## the shaded tips whose seeded roll takes them within MARK_WARN_DAYS, at most MARK_MAX, the
## ones already shown first, then the soonest, then the ones furthest out of the crown (seen
## from the normal camera). A forecast only: the dieback itself is unchanged, so a mark ends by
## the dieback, a cut, or light let in above it (the tip no longer shaded).
func update_marks(day: int) -> void:
	if SHADE_DIEBACK_SHARE * species.shade_dieback <= 0.0:
		marks.clear()
		return
	var shaded := {}
	for id in shaded_tips():
		shaded[id] = true
	var kept: Array = []
	var taken := {}
	for m in marks:
		var id := int(m["id"])
		if int(m["due"]) > day and shaded.has(id) and not graph.get_flag(id, "dead", false):
			kept.append(m)
			taken[id] = true
	var base := crown_base() + MARK_ABOVE_BASE if crown_base() > 0.0 else 0.0
	var r := maxf(crown_radius(height()), 0.3)
	var fresh: Array = []
	for id: int in shaded:
		if taken.has(id) or graph.positions[id].y < base:
			continue
		for ahead in range(1, MARK_WARN_DAYS + 1):
			if _dies_back(id, day + ahead):
				var p := graph.positions[id]
				fresh.append({"id": id, "due": day + ahead, "since": day, "out": Vector2(p.x, p.z).length() / r})
				break
	fresh.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if int(a["due"]) != int(b["due"]):
			return int(a["due"]) < int(b["due"])
		if not is_equal_approx(float(a["out"]), float(b["out"])):
			return float(a["out"]) > float(b["out"])
		return int(a["id"]) < int(b["id"]))
	for m in fresh:
		if kept.size() >= MARK_MAX:
			break
		kept.append({"id": m["id"], "due": m["due"], "since": m["since"]})
	marks = kept


## The twig a mark sits on: the tip and its segments back towards the fork (at most MARK_TWIG,
## never the trunk base); a shorter one reaches over the fork into the little branch it grows on
## (MARK_TWIG_MIN, MARK_BRANCH_MAX). The last node is where a gardener would cut it.
func marked_twig(tip: int) -> PackedInt32Array:
	var out := PackedInt32Array()
	var cur := tip
	while cur >= 3 and out.size() < MARK_TWIG and not graph.get_flag(cur, "dead", false):
		out.append(cur)
		var p := graph.parents[cur]
		var living := 0
		for c in graph.children[p]:
			if not graph.get_flag(c, "dead", false):
				living += 1
		if living > 1 and (out.size() >= MARK_TWIG_MIN or p < 3 or _subtree_size(p) > MARK_BRANCH_MAX):
			break
		cur = p
	return out


## How strongly a mark shows now, 0..1: MARK_FIRST_DAY on the first of two days' warning,
## MARK_LAST_DAY on the day before the dieback, eased in over the morning like the care signals.
func mark_strength(m: Dictionary) -> float:
	var day := clock.day_count
	var due := int(m["due"])
	var since := int(m["since"])
	if graph.get_flag(int(m["id"]), "dead", false) or day >= due or day < since:
		return 0.0
	var now := MARK_LAST_DAY if due - day <= 1 else MARK_FIRST_DAY
	var before := 0.0
	if day > since:
		before = MARK_LAST_DAY if due - day + 1 <= 1 else MARK_FIRST_DAY
	var e := 1.0
	if clock.is_day():
		e = smoothstep(0.0, Care.EASE_SHARE * clock.daylight_fraction, clock.time_of_day)
	return lerpf(before, now, e)


## The nodes that show a mark now, {node id: strength 0..1} (the rendering reads this).
func tired_nodes() -> Dictionary:
	var out := {}
	for m in marks:
		var s := mark_strength(m)
		if s <= 0.0:
			continue
		for id in marked_twig(int(m["id"])):
			out[id] = maxf(float(out.get(id, 0.0)), s)
	return out


## Living tips with at least SHADE_NODES living nodes above them in their column of the crown.
func shaded_tips() -> PackedInt32Array:
	var columns := {}
	for id in range(graph.size()):
		if graph.get_flag(id, "dead", false):
			continue
		var p := graph.positions[id]
		var key := Vector2i(floori(p.x / SHADE_CELL), floori(p.z / SHADE_CELL))
		var ys: PackedFloat32Array = columns.get(key, PackedFloat32Array())
		ys.append(p.y)
		columns[key] = ys
	var out := PackedInt32Array()
	for id in graph.tips():
		if graph.get_flag(id, "dead", false):
			continue
		var p := graph.positions[id]
		var above := 0
		for y in columns.get(Vector2i(floori(p.x / SHADE_CELL), floori(p.z / SHADE_CELL)), PackedFloat32Array()):
			if y > p.y + 0.5:
				above += 1
		if above >= SHADE_NODES:
			out.append(id)
	return out


## Room left in the tree's stock tonight, per kind: hold_days of a full calm day's need (water
## also the leaves' upkeep) minus what it holds. What the old roots may still draw overnight.
func stock_room() -> PackedFloat32Array:
	var out := PackedFloat32Array([0, 0, 0, 0])
	var want := day_capacity() * node_cost()
	for k in range(4):
		var hold := want * species.needs[k] * hold_days
		if k == Resources.Kind.WATER:
			hold += effective_leaves() * WATER_UPKEEP_PER_LEAF * species.water_upkeep
		out[k] = maxf(0.0, hold - resources.stock[k])
	return out


## The leaves drink water every day (design doc section 3: water upkeep). Taken at sunrise,
## before the day's growth; the species sets the thirst. Returns the water drunk.
func drink_upkeep() -> float:
	var want := effective_leaves() * WATER_UPKEEP_PER_LEAF * species.water_upkeep
	var take := minf(want, resources.stock[Resources.Kind.WATER])
	resources.stock[Resources.Kind.WATER] -= take
	return take


## Grown to the species' full size (or the node budget): the tree is finished.
## With natural_form the lower branches the crown shed on its way up count too: the tree grew them.
func is_finished() -> bool:
	if graph.is_full():
		return true
	return grown_nodes() >= species.finish_nodes and (not natural_form or height() >= finish_height())


## The least height a finished tree stands (natural_form): FINISH_HEIGHT_SHARE of the species'.
func finish_height() -> float:
	return species.max_height * FINISH_HEIGHT_SHARE


## Living segments, plus (natural_form) those shed as the crown lifted.
func grown_nodes() -> int:
	var n := 0
	for id in range(graph.size()):
		if not graph.get_flag(id, "dead", false) or graph.get_flag(id, "shed", false):
			n += 1
	return n


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
		"natural_form": natural_form,
		"graph": graph.to_dict(),
		"markers": colonizer.markers,
		"resources": resources.to_dict(),
		"clock": clock.to_dict(),
		"care": [Array(care_need), Array(care_prev), dawn_size, young_from, removed_nodes, _vigour_nodes],
		"growth_debt": _growth_debt,
		"cuts": cuts,
		"last_cut": last_cut,
		"marks": marks,
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
	s.natural_form = bool(d.get("natural_form", false))
	s.graph = PlantGraph.from_dict(d["graph"])
	s.colonizer = SpaceColonization.new(s.rng)
	s.colonizer.markers = PackedVector3Array(d.get("markers", []))
	s.resources = Resources.from_dict(d.get("resources", {}))
	s.clock = DayCycle.from_dict(d.get("clock", {}))
	var care: Array = d.get("care", [])
	if care.size() >= 6:
		s.care_need = PackedFloat32Array(care[0])
		s.care_prev = PackedFloat32Array(care[1])
		s.dawn_size = int(care[2])
		s.young_from = int(care[3])
		s.removed_nodes = int(care[4])
		s._vigour_nodes = int(care[5])
	s._growth_debt = int(d.get("growth_debt", 0))
	for c in d.get("cuts", []):
		if c is Dictionary:
			s.cuts.append(c)
	if d.get("last_cut") is Dictionary:
		s.last_cut = d["last_cut"]
	for m in d.get("marks", []):
		if m is Dictionary:
			s.marks.append({"id": int(m["id"]), "due": int(m["due"]), "since": int(m["since"])})
	return s
