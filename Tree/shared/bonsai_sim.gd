class_name BonsaiSim
extends RefCounted
## The bonsai on the windowsill (design doc section 16): a second, small plant graph grown by
## the same space colonization and pipe model as the tree, in a pot that caps it. Pure data, no
## scene: BonsaiView only reads it. All randomness from its own seeded RandomNumberGenerator.
##
## The loop, mirrored from the tree: the same day clock (it follows the tree's clock, with its
## own count of care days), window light from one fixed side (turn the pot a quarter at a time),
## soil moisture that dries over the day (watering can), N, P and K in the soil (fertiliser
## pellets, the soft Liebig rule, too much burns a few tips), a repotting every seventh day,
## the shears (a third at most), pinching and wire. Soft failure only: nothing ever dies.
##
## Lengths are in bonsai units: one unit is 10 cm on the sill (UNIT_METRES), so the pipe model's
## tip radius is a millimetre and a segment about 1.7 cm.

const UNIT_METRES: float = 0.1
## Segment length (finer than the tree's).
const STEP: float = 0.17
## Segments per second at full light, full water and full soil (about 35 a day).
const BASE_RATE: float = 0.5
## The tallest the crown grows, and its widest radius (units).
const MAX_HEIGHT: float = 2.8
const MAX_RADIUS: float = 1.6
## The window glass, this far from the pot's centre toward the window: nothing grows through it.
const GLASS: float = 1.25
## The window: pot-local +Z at turn 0, a little from above.
const WINDOW := Vector3(0.0, 0.35, 1.0)
## The crown is a flattened ball (a bonsai is wider than tall), and its apex is weaker than a
## tree's in the open (APICAL_SHARE of the species' apical dominance).
const CROWN_FLAT: float = 0.62
const APICAL_SHARE: float = 0.55
## Wood keeps thickening a little every day (secondary growth), up to a trunk of this radius.
const THICKEN_PER_DAY: float = 1.012
const MAX_RADIUS_UNITS: float = 0.3
## How far the crown's markers sit toward the window (share of the crown radius).
const WINDOW_PULL: float = 0.5

## Soil moisture 0..1: dries by day (faster in the sun), a little by night.
const DRY_PER_SECOND: float = 0.0045
const WATER_POUR: float = 0.45
const DRY_LEVEL: float = 0.2
const WET_LEVEL: float = 0.85
## Growth when too wet, and at the dry end (drooping).
const WET_FACTOR: float = 0.55
const DRY_FACTOR: float = 0.3

## N, P, K in the soil: each segment uses this much per unit of the species' need.
const SOIL_USE_PER_NODE: float = 0.004
const PELLET: float = 0.35
const FRESH_SOIL: float = 0.5
## Above this a nutrient burns a few leaf tips (they rest a few days, then grow on).
const BURN_LEVEL: float = 1.0
const BURN_DAYS: int = 3
const SOIL_MAX: float = 1.6

## Repotting: the roots fill the pot in about a week; after that it asks to be repotted and,
## until it is, grows a little slower (root bound).
const REPOT_DAYS: int = 7
const ROOTBOUND_FLOOR: float = 0.6

## Wire: bends the branch half way now, then a share of the rest each day; set after
## WIRE_SET_DAYS; left on after WIRE_BITE_DAYS it bites into the bark and leaves a scar.
const WIRE_NOW: float = 0.5
const WIRE_DAILY: float = 0.4
const WIRE_SET_DAYS: int = 4
const WIRE_BITE_DAYS: int = 6
## A wire taken off before the branch set springs back by this share of what is not set yet.
const SPRING_BACK: float = 0.6

## The shears take at most a third of the living tree at once (the tree's rule is a fifth).
const PRUNE_SHARE: float = 1.0 / 3.0
const PRUNE_MIN: int = 4
## A tip may be pinched while it is fresh: grown today or yesterday.
const FRESH_DAYS: int = 1

## The pots (a few shapes and glazes): the volume sets the living segments it carries.
const POTS: Dictionary = {
	"nursery": {"name": "clay nursery pot", "nodes": 560},
	"rectangle": {"name": "grey rectangle", "nodes": 600},
	"oval": {"name": "blue glazed oval", "nodes": 500},
	"round": {"name": "green glazed round", "nodes": 450},
	"cascade": {"name": "tall cream cascade pot", "nodes": 420},
}
const POT_ORDER: Array[String] = ["nursery", "rectangle", "oval", "round", "cascade"]
## Care days that earn an album line.
const DAY_MILESTONES: Array[int] = [7, 14, 30, 60, 100, 200, 365]
const SIZE_MILESTONES: Array[int] = [100, 250, 400]

var seed: int = 1
var rng := RandomNumberGenerator.new()
var species: Species = Species.juniper()
var graph: PlantGraph
var colonizer: SpaceColonization
## The bonsai's copy of the shared day clock: the same hour as the tree, its own day count
## (care days since it came to the sill).
var clock := DayCycle.new()
var pot: String = "nursery"
## Quarter turns of the pot (0..3); the window side of the pot is where it grows.
var turn: int = 0
var moisture: float = 0.6
## Nitrogen, phosphorus, potassium in the pot's soil.
var soil := PackedFloat32Array([FRESH_SOIL, FRESH_SOIL, FRESH_SOIL])
## How full of roots the pot is (1 = a week after repotting).
var root_fill: float = 0.0
var last_repot_day: int = 0
var repot_due: bool = false
## The album page: {"day": int, "text": String}, oldest first.
var milestones: Array = []
var _events: Array[String] = []
var _growth_accum: float = 0.0
var _marker_accum: float = 0.0
var _leader_accum: float = 0.0


func _init(random_seed: int = 1, species_id: String = "juniper") -> void:
	seed = random_seed
	rng.seed = hash([seed, "bonsai", species_id])
	species = Species.from_id(species_id)
	graph = PlantGraph.new(Vector3.ZERO, Budgets.BONSAI_MAX_NODES)
	colonizer = SpaceColonization.new(rng)
	colonizer.step_length = STEP
	colonizer.kill_distance = STEP * 1.6
	colonizer.influence_radius = 0.75


## The starter: a young juniper from the nursery, a trunk with a first bend and a few days of
## growth, in the clay nursery pot.
static func starter(random_seed: int) -> BonsaiSim:
	var b := BonsaiSim.new(random_seed, "juniper")
	var trunk: Array[Vector3] = [Vector3(0.0, 0.25, 0.0), Vector3(0.06, 0.5, 0.02), Vector3(0.16, 0.74, 0.0),
		Vector3(0.18, 0.98, -0.04), Vector3(0.1, 1.2, -0.06)]
	var last := 0
	for p in trunk:
		last = b.graph.add_node(last, p)
	b._pregrow(90)
	# Nursery stock: an old, thick trunk that tapers into the young crown.
	var trunk_ids := b.trunk_chain()
	for i in range(trunk_ids.size()):
		var id := trunk_ids[i]
		b.graph.radii[id] = maxf(b.graph.radii[id], lerpf(0.13, 0.035, clampf(float(i) / 11.0, 0.0, 1.0)))
	b.note("A young juniper came to the windowsill in a clay nursery pot.")
	return b


## A cutting of a clearing tree (section 16 E): a short stick that roots in the nursery pot.
static func cutting(random_seed: int, species_id: String) -> BonsaiSim:
	var b := BonsaiSim.new(random_seed, species_id)
	var last := 0
	for k in range(3):
		last = b.graph.add_node(last, Vector3(0.02 * k, 0.25 + 0.2 * k, 0.0))
	b._pregrow(18)
	b.note("A cutting of the %s rooted in a clay nursery pot." % b.plant_name())
	return b


## Grows a young plant to about `nodes` segments off the clock (the nursery's time).
func _pregrow(nodes: int) -> void:
	var guard := 0
	while leafy_count() < nodes and guard < 400:
		_grow_nodes(3)
		guard += 1
	_update_radii()
	graph.age_all()
	graph.age_all()


func plant_name() -> String:
	return species.display_name.to_lower()


func pot_name() -> String:
	return str(POTS.get(pot, POTS["nursery"])["name"])


## Living segments the pot can carry.
func capacity() -> int:
	return int(POTS.get(pot, POTS["nursery"])["nodes"])


func is_dead(id: int) -> bool:
	return graph.get_flag(id, "dead", false)


func is_jin(id: int) -> bool:
	return graph.get_flag(id, "jin", false)


## Living green wood: not cut, not deadwood. The pot caps this.
func leafy_count() -> int:
	var n := 0
	for id in range(graph.size()):
		if not is_dead(id) and not is_jin(id):
			n += 1
	return n


func height() -> float:
	var h := 0.0
	for id in range(graph.size()):
		if not is_dead(id):
			h = maxf(h, graph.positions[id].y)
	return h


## Care days since the bonsai came to the sill.
func day() -> int:
	return clock.day_count


## The window's direction in the pot's own frame: turning the pot turns it the other way.
func window_dir() -> Vector3:
	return WINDOW.normalized().rotated(Vector3.UP, -turn * PI * 0.5)


func take_events() -> Array[String]:
	var out := _events
	_events = []
	return out


func note(text: String) -> void:
	milestones.append({"day": day(), "text": text})
	_events.append("milestone")


# --- time ----------------------------------------------------------------------------

## Follows the shared clock: `seconds` of game time passed on the tree's clock.
func follow(tree_clock: DayCycle, seconds: float) -> void:
	if seconds > 0.0:
		advance(seconds)
	# The same hour as the tree (a new tree starts at sunset: the bonsai only jumps the hour).
	if absf(clock.time_of_day - tree_clock.time_of_day) > 1e-4:
		if tree_clock.time_of_day < clock.time_of_day - 0.5:
			_new_day_passed()
		clock.time_of_day = tree_clock.time_of_day


func advance(seconds: float) -> void:
	while seconds > 0.0:
		var dt := minf(seconds, 1.0)
		seconds -= dt
		var before := clock.day_count
		clock.advance(dt)
		for _d in range(clock.day_count - before):
			_new_day()
		_step(dt, clock.sun_height())


func _new_day_passed() -> void:
	clock.day_count += 1
	_new_day()


## One stretch of `dt` game seconds at window `light` (0..1).
func _step(dt: float, light: float) -> void:
	moisture = maxf(0.0, moisture - DRY_PER_SECOND * (0.4 + light) * dt)
	if light <= 0.0:
		return
	var room := capacity() - leafy_count()
	if room <= 0 or graph.is_full():
		_growth_accum = 0.0
		return
	_growth_accum += BASE_RATE * light * growth_factor() * species.pace_on(day()) * dt
	var budget := mini(int(_growth_accum), room)
	_growth_accum -= int(_growth_accum)
	if budget > 0:
		_grow_nodes(budget)
		_update_radii()
		_check_size_milestones()


## Everything that slows growth today: water, soil (soft Liebig), a full pot.
func growth_factor() -> float:
	return moisture_factor() * nutrient_factor() * rootbound_factor()


func moisture_factor() -> float:
	if moisture > WET_LEVEL:
		return WET_FACTOR
	if moisture < DRY_LEVEL:
		return lerpf(DRY_FACTOR, 1.0, moisture / DRY_LEVEL)
	return 1.0


## 0..1: how much the leaves hang (too dry).
func droop() -> float:
	return clampf((DRY_LEVEL - moisture) / DRY_LEVEL, 0.0, 1.0)


func is_too_wet() -> bool:
	return moisture > WET_LEVEL


## The same soft Liebig rule as the tree: the scarcest nutrient against the species' need.
func nutrient_factor() -> float:
	return Resources.growth_factor(PackedFloat32Array([1.0, soil[0] * 2.0, soil[1] * 2.0, soil[2] * 2.0]), species.needs)


func rootbound_factor() -> float:
	return 1.0 if root_fill <= 1.0 else maxf(ROOTBOUND_FLOOR, 1.0 - (root_fill - 1.0) * 0.4)


func _grow_nodes(budget: int) -> void:
	var top := maxf(height(), 0.3)
	var r := crown_radius(top)
	var win := window_dir()
	var flat := Vector3(win.x, 0.0, win.z)
	# Markers: a few above the leader (apical dominance, fading near the full height), the rest
	# in the crown, pulled toward the window: the side facing it grows, the back stays sparse.
	var amount := budget * 3.0
	var leader_share := species.apical_dominance * APICAL_SHARE * pow(clampf(1.0 - top / MAX_HEIGHT, 0.0, 1.0), 1.5)
	_leader_accum += amount * leader_share
	_marker_accum += amount * (1.0 - leader_share)
	var leader := int(_leader_accum)
	_leader_accum -= leader
	var crown := int(_marker_accum)
	_marker_accum -= crown
	colonizer.seed_sphere(Vector3(0, top + 0.25, 0) + flat * 0.15, 0.3, leader, Budgets.BONSAI_MARKERS, 0.3, MAX_HEIGHT)
	var centre := Vector3(0, top * 0.6 + 0.12, 0) + flat * r * WINDOW_PULL
	colonizer.seed_sphere(centre, r, crown, Budgets.BONSAI_MARKERS, -INF, INF)
	# Flatten the new ones into the crown's ellipsoid, above the bare lower trunk.
	var n := colonizer.markers.size()
	for i in range(maxi(0, n - crown), n):
		var m := colonizer.markers[i]
		m.y = clampf(centre.y + (m.y - centre.y) * CROWN_FLAT, maxf(0.3, top * 0.25), MAX_HEIGHT)
		colonizer.markers[i] = m
	_drop_glass_markers()
	colonizer.bias_direction = (Vector3.UP * maxf(0.05, 1.0 - species.phototropism + species.gravitropism) + win * species.phototropism).normalized()
	colonizer.jitter = 0.1 + species.crookedness * 0.4
	var grown := colonizer.step(graph, budget)
	if grown > 0:
		_steady(graph.size() - grown)
		_use_soil(grown)
		_droop_new(graph.size() - grown, r)


## A shoot keeps some of its heading (the markers alone would curl the fine segments into
## rings): each new continuation segment turns at most part of the way toward its pull.
const HEADING_KEEP: float = 0.45


func _steady(first: int) -> void:
	for id in range(maxi(first, 2), graph.size()):
		var p := graph.parents[id]
		if graph.parents[p] < 0 or (graph.children[p] as Array).size() != 1:
			continue
		var d := (graph.positions[id] - graph.positions[p]).normalized()
		var keep := graph.direction_of(p)
		graph.positions[id] = graph.positions[p] + (d * (1.0 - HEADING_KEEP) + keep * HEADING_KEEP).normalized() * STEP


## Markers beyond the window glass (in the sill's frame, whichever way the pot is turned).
func _drop_glass_markers() -> void:
	var kept := PackedVector3Array()
	for m in colonizer.markers:
		if m.rotated(Vector3.UP, turn * PI * 0.5).z < GLASS:
			kept.append(m)
	colonizer.markers = kept


## Birch-like species hang their new twig tips a little (the quirk carries over).
func _droop_new(first: int, r: float) -> void:
	if species.twig_droop <= 0.0:
		return
	for id in range(maxi(first, 1), graph.size()):
		var p := graph.positions[id]
		if Vector2(p.x, p.z).length() > r * 0.35:
			p.y -= species.twig_droop * STEP * 0.5
			graph.positions[id] = p


func crown_radius(top: float) -> float:
	return clampf(0.35 + top * 0.55, 0.45, MAX_RADIUS)


func _use_soil(nodes: int) -> void:
	for k in range(3):
		soil[k] = maxf(0.0, soil[k] - nodes * SOIL_USE_PER_NODE * species.needs[k + 1])


## Pipe model radii, but wood never gets thinner again: a pruned trunk keeps its girth.
func _update_radii() -> void:
	var old := graph.radii.duplicate()
	graph.update_radii()
	for id in range(mini(old.size(), graph.size())):
		graph.radii[id] = maxf(graph.radii[id], old[id])


## Once a day at sunrise: the roots fill the pot, wires set and bite, burnt tips recover.
func _new_day() -> void:
	graph.age_all()
	# The wood thickens a little (a lifelong bonsai gets its old trunk).
	for id in range(graph.size()):
		if not is_dead(id) and not is_jin(id) and not (graph.children[id] as Array).is_empty():
			graph.radii[id] = minf(MAX_RADIUS_UNITS, graph.radii[id] * THICKEN_PER_DAY)
	root_fill += 1.0 / REPOT_DAYS
	if not repot_due and day() - last_repot_day >= REPOT_DAYS:
		repot_due = true
		_events.append("repot_due")
	for id in wired():
		_wire_day(id)
	for id in range(graph.size()):
		var burnt: Variant = graph.get_flag(id, "burnt")
		if burnt != null and day() - int(burnt) >= BURN_DAYS:
			(graph.flags[id] as Dictionary).erase("burnt")
			_update_rest(id)
	# Alder's root nodules keep a little nitrogen in the pot too.
	if species.nodule_nitrogen > 0.0:
		soil[0] = minf(SOIL_MAX, soil[0] + species.nodule_nitrogen * 2.0)
	if DAY_MILESTONES.has(day()):
		note(_day_words(day()))


static func _day_words(d: int) -> String:
	match d:
		7:
			return "A week on the windowsill."
		14:
			return "Two weeks of care: a shape begins to show."
		30:
			return "A month together."
		365:
			return "A whole year on the windowsill."
	return "%d days of care." % d


func _check_size_milestones() -> void:
	for n in SIZE_MILESTONES:
		var key := "size_%d" % n
		if leafy_count() >= n and not _has_milestone(key):
			milestones.append({"day": day(), "text": "It carries %d green twigs now." % n, "key": key})
			_events.append("milestone")


func _has_milestone(key: String) -> bool:
	for m in milestones:
		if str((m as Dictionary).get("key", "")) == key:
			return true
	return false


## An album line only the first time something happens.
func _first(key: String, text: String) -> void:
	if not _has_milestone(key):
		milestones.append({"day": day(), "text": text, "key": key})
		_events.append("milestone")


# --- care ------------------------------------------------------------------------------

## The watering can: the soil darkens; too much keeps it wet and slows growth for a while.
func water() -> void:
	moisture = minf(1.0, moisture + WATER_POUR)
	_first("water", "First watering.")


## A spoon of pellets of one nutrient (0 = N, 1 = P, 2 = K). Too much burns a few leaf tips.
## Returns how many tips burnt.
func fertilise(kind: int) -> int:
	soil[kind] = minf(SOIL_MAX, soil[kind] + PELLET)
	_first("fertiliser", "First fertiliser pellets on the soil.")
	if soil[kind] <= BURN_LEVEL:
		return 0
	var tips := living_tips()
	var n := mini(tips.size(), 1 + int((soil[kind] - BURN_LEVEL) * 12.0))
	var burnt := 0
	for i in range(tips.size()):
		if burnt >= n:
			break
		var id: int = tips[posmod(hash([seed, "burn", day(), i, kind]), tips.size())]
		if graph.get_flag(id, "burnt") == null:
			graph.set_flag(id, "burnt", day())
			_update_rest(id)
			burnt += 1
	if burnt > 0:
		_events.append("burn")
	return burnt


## Turns the pot a quarter (+1 clockwise seen from above, -1 back). The markers of the old
## window side fade, so the new side facing the window starts to grow.
func turn_pot(step: int) -> void:
	turn = posmod(turn + step, 4)
	colonizer.markers = PackedVector3Array()


## Repotting (section 16 B): lift it out, trim the root ball (`trim` 0..1 of the roots cut),
## pick a pot, fresh soil. The roots have room again.
func repot(pot_id: String, trim: float) -> void:
	if not POTS.has(pot_id):
		pot_id = pot
	var moved := pot_id != pot
	pot = pot_id
	root_fill = 0.35 * (1.0 - clampf(trim, 0.0, 1.0))
	moisture = 0.6
	soil = PackedFloat32Array([FRESH_SOIL, FRESH_SOIL, FRESH_SOIL])
	last_repot_day = day()
	repot_due = false
	var line := "Repotted into the %s with fresh soil." % pot_name() if moved else "Repotted with fresh soil and trimmed roots."
	milestones.append({"day": day(), "text": line})
	_events.append("milestone")


func living_tips() -> PackedInt32Array:
	var out := PackedInt32Array()
	for id in range(1, graph.size()):
		if is_dead(id) or is_jin(id):
			continue
		var tip := true
		for c in graph.children[id]:
			if not is_dead(c):
				tip = false
				break
		if tip:
			out.append(id)
	return out


## A tip that grew today or yesterday and still grows.
func is_fresh_tip(id: int) -> bool:
	if id < 2 or id >= graph.size() or is_dead(id) or is_jin(id) or graph.get_flag(id, "pinched", false):
		return false
	for c in graph.children[id]:
		if not is_dead(c):
			return false
	return graph.ages[id] <= FRESH_DAYS


## Pinching: the fresh tip stops, and its strength goes to the buds behind it (a denser pad).
## A species with twin buds (sycamore) forks there instead.
func pinch(id: int) -> bool:
	if not is_fresh_tip(id):
		return false
	graph.set_flag(id, "pinched", true)
	_update_rest(id)
	var p := graph.parents[id]
	if species.twin_buds:
		_fork_at(p, graph.positions[id] - graph.positions[p])
	# A few markers close around the buds behind it.
	for _k in range(4):
		var v := Vector3(rng.randf_range(-1, 1), rng.randf_range(-0.4, 0.8), rng.randf_range(-1, 1)).normalized() * 0.3
		colonizer.add_marker(graph.positions[p] + v)
	_first("pinch", "First fresh tips pinched.")
	return true


func _fork_at(parent: int, dir: Vector3) -> void:
	dir = dir.normalized() if dir.length_squared() > 1e-8 else Vector3.UP
	var side := dir.cross(Vector3.UP)
	side = side.normalized() if side.length_squared() > 1e-4 else Vector3.RIGHT
	for k: float in [-1.0, 1.0]:
		graph.add_node(parent, graph.positions[parent] + (dir + side * 0.75 * k).normalized() * STEP)
	_update_radii()


func _update_rest(id: int) -> void:
	var rest: bool = bool(graph.get_flag(id, "pinched", false)) or is_jin(id) or graph.get_flag(id, "burnt") != null
	graph.set_flag(id, "rest", rest)


## Living segments hanging on `id` (itself included), green wood only.
func subtree_leafy(id: int) -> int:
	var n := 0
	for s in subtree(id):
		if not is_jin(s):
			n += 1
	return n


func subtree(id: int) -> Array[int]:
	var out: Array[int] = []
	var stack: Array[int] = [id]
	while not stack.is_empty():
		var n: int = stack.pop_back()
		if is_dead(n):
			continue
		out.append(n)
		for c in graph.children[n]:
			stack.append(c)
	return out


## The shears may cut here: never the trunk base, never more than a third of the tree.
func can_prune(id: int) -> bool:
	if id < 2 or id >= graph.size() or is_dead(id):
		return false
	return subtree_leafy(id) <= prune_limit()


func prune_limit() -> int:
	return maxi(PRUNE_MIN, int(leafy_count() * PRUNE_SHARE))


## Cuts at `id`. The juniper keeps the cut branch's first segment as a silver jin, and a cut
## at the trunk strips a line of bark below it (shari). The cut wood leaves the graph, so the
## pot has room again: the inner buds take the markers (the tree grows denser, not bigger).
## Returns the green segments removed.
func prune(id: int) -> int:
	if not can_prune(id):
		return 0
	var count := subtree_leafy(id)
	var p := graph.parents[id]
	var at_trunk := trunk_chain().has(p)
	if species.deadwood:
		for c in graph.children[id]:
			_kill(c)
		graph.set_flag(id, "jin", true)
		graph.set_flag(id, "wire", null)
		_update_rest(id)
		if at_trunk:
			_shari_below(p)
		_first("jin", "The first silver jin: a cut branch left as deadwood.")
	else:
		var fork := species.twin_buds and _is_short_end(id)
		_kill(id)
		if fork:
			_fork_at(p, graph.positions[id] - graph.positions[p])
	compact()
	_first("cut", "First cut with the shears.")
	return count


func _is_short_end(id: int) -> bool:
	var cur := id
	for _i in range(3):
		var alive: Array[int] = []
		for c in graph.children[cur]:
			if not is_dead(c):
				alive.append(c)
		if alive.is_empty():
			return true
		if alive.size() > 1:
			return false
		cur = alive[0]
	return false


func _kill(id: int) -> void:
	for n in subtree(id):
		graph.set_flag(n, "dead", true)


## The trunk: from the base along the thickest living child.
func trunk_chain() -> Array[int]:
	var out: Array[int] = [0]
	var cur := 0
	while true:
		var best := -1
		var best_r := -1.0
		for c in graph.children[cur]:
			if not is_dead(c) and not is_jin(c) and graph.radii[c] > best_r:
				best_r = graph.radii[c]
				best = c
		if best < 0:
			break
		out.append(best)
		cur = best
	return out


func _shari_below(from_id: int) -> void:
	var cur := from_id
	var k := 0
	while cur > 0 and k < 6:
		graph.set_flag(cur, "shari", maxf(float(graph.get_flag(cur, "shari", 0.0)), 1.0 - k * 0.12))
		cur = graph.parents[cur]
		k += 1


# --- wire ------------------------------------------------------------------------------

## Ids of the wired branches.
func wired() -> Array[int]:
	var out: Array[int] = []
	for id in range(graph.size()):
		if not is_dead(id) and graph.get_flag(id, "wire") is Dictionary:
			out.append(id)
	return out


## Wires the branch at `id` (from its parent) toward `dir`: half of the bend now, the rest
## over the next days while it sets.
func wire(id: int, dir: Vector3) -> bool:
	if id < 2 or id >= graph.size() or is_dead(id) or is_jin(id) or graph.get_flag(id, "wire") is Dictionary:
		return false
	if dir.length_squared() < 1e-6:
		return false
	dir = dir.normalized()
	var from := graph.direction_of(id)
	if from.angle_to(dir) < 0.05:
		return false
	graph.set_flag(id, "wire", {"to": _vec(dir), "from": _vec(from), "days": 0})
	_bend(id, dir, WIRE_NOW)
	_first("wire", "The first wire: a branch coiled in copper, bent into a new line.")
	return true


## Takes the wire off. Before the branch has set it springs back part of the way; a wire left
## on too long already bit in (the scar stays).
func unwire(id: int) -> bool:
	var w: Variant = graph.get_flag(id, "wire")
	if not (w is Dictionary):
		return false
	var set := wire_set(id)
	if set < 1.0:
		_bend(id, _unvec((w as Dictionary)["from"]), (1.0 - set) * SPRING_BACK)
	graph.set_flag(id, "wire", null)
	return true


## 0..1: how far the wired branch has set in its new line.
func wire_set(id: int) -> float:
	var w: Variant = graph.get_flag(id, "wire")
	if not (w is Dictionary):
		return 0.0
	return clampf(float((w as Dictionary).get("days", 0)) / WIRE_SET_DAYS, 0.0, 1.0)


func wire_days(id: int) -> int:
	var w: Variant = graph.get_flag(id, "wire")
	return int((w as Dictionary).get("days", 0)) if w is Dictionary else 0


## 0..1: the scar a wire left in the bark.
func scar(id: int) -> float:
	return float(graph.get_flag(id, "scar", 0.0))


func _wire_day(id: int) -> void:
	var w: Dictionary = graph.get_flag(id, "wire")
	w["days"] = int(w.get("days", 0)) + 1
	if int(w["days"]) <= WIRE_SET_DAYS:
		_bend(id, _unvec(w["to"]), WIRE_DAILY)
	if int(w["days"]) > WIRE_BITE_DAYS:
		# The wire bites along its whole length.
		for n in wire_chain(id):
			graph.set_flag(n, "scar", minf(1.0, scar(n) + 0.34))
		_first("scar", "A wire stayed on too long and bit into the bark. The scar will stay.")


## Turns the branch at `id` (with everything on it) about its base toward `dir` by `share`
## of the angle between them. Wires further out turn along with it.
func _bend(id: int, dir: Vector3, share: float) -> void:
	var cur := graph.direction_of(id)
	var axis := cur.cross(dir)
	if axis.length_squared() < 1e-8:
		return
	var turn_basis := Basis(axis.normalized(), cur.angle_to(dir) * share)
	var pivot := graph.positions[graph.parents[id]]
	for n in subtree(id):
		graph.positions[n] = pivot + turn_basis * (graph.positions[n] - pivot)
		var w: Variant = graph.get_flag(n, "wire")
		if n != id and w is Dictionary:
			(w as Dictionary)["to"] = _vec(turn_basis * _unvec((w as Dictionary)["to"]))
			(w as Dictionary)["from"] = _vec(turn_basis * _unvec((w as Dictionary)["from"]))


## The segments the wire coils along: the branch's base segment and its main line outward.
func wire_chain(id: int, length: int = 5) -> Array[int]:
	var out: Array[int] = [id]
	var cur := id
	for _i in range(length - 1):
		var best := -1
		var best_r := -1.0
		for c in graph.children[cur]:
			if not is_dead(c) and graph.radii[c] > best_r:
				best_r = graph.radii[c]
				best = c
		if best < 0:
			break
		out.append(best)
		cur = best
	return out


static func _vec(v: Vector3) -> Array:
	return [v.x, v.y, v.z]


static func _unvec(a: Variant) -> Vector3:
	var l: Array = a
	return Vector3(float(l[0]), float(l[1]), float(l[2]))


## Removes cut wood from the graph (ids change; flags, wires and scars move with their nodes),
## so a lifelong bonsai never runs out of node budget.
func compact() -> void:
	var keep := PackedInt32Array()
	var remap := {}
	for id in range(graph.size()):
		if id == 0 or not is_dead(id):
			remap[id] = keep.size()
			keep.append(id)
	if keep.size() == graph.size():
		return
	var g := PlantGraph.new(graph.positions[0], graph.max_nodes)
	g.radii[0] = graph.radii[0]
	g.ages[0] = graph.ages[0]
	g.flags[0] = graph.flags[0]
	for i in range(1, keep.size()):
		var old := keep[i]
		var nid := g.add_node(int(remap[graph.parents[old]]), graph.positions[old])
		g.radii[nid] = graph.radii[old]
		g.ages[nid] = graph.ages[old]
		g.flags[nid] = graph.flags[old]
	graph = g


# --- offline and save ----------------------------------------------------------------

## Time away grows the bonsai like the tree: a real day closed is about 20 s of active
## game time, at a mid-morning light; the hour and the day do not move.
func apply_offline(real_seconds: float, active_seconds_per_real_day: float = 20.0) -> void:
	var active := real_seconds / 86400.0 * active_seconds_per_real_day
	while active > 0.0:
		var dt := minf(active, 0.5)
		_step(dt, 0.8)
		active -= dt


func to_dict() -> Dictionary:
	var markers := PackedFloat32Array()
	for m in colonizer.markers:
		markers.append_array([m.x, m.y, m.z])
	return {
		"version": 1,
		"seed": seed,
		"rng_state": str(rng.state),
		"species": species.id,
		"graph": graph.to_json_dict(),
		"markers": markers,
		"clock": clock.to_dict(),
		"pot": pot,
		"turn": turn,
		"moisture": moisture,
		"soil": soil,
		"root_fill": root_fill,
		"last_repot_day": last_repot_day,
		"repot_due": repot_due,
		"milestones": milestones,
		"accum": [_growth_accum, _marker_accum, _leader_accum],
	}


static func from_dict(d: Dictionary) -> BonsaiSim:
	var b := BonsaiSim.new(int(d.get("seed", 1)), str(d.get("species", "juniper")))
	b.rng.state = int(str(d.get("rng_state", b.rng.state)))
	b.graph = PlantGraph.from_json_dict(d["graph"])
	b.graph.max_nodes = Budgets.BONSAI_MAX_NODES
	# JSON turns the flags' numbers into floats and their vectors into lists: both read back.
	var flat: Array = Array(d.get("markers", []))
	var i := 0
	while i + 2 < flat.size():
		b.colonizer.markers.append(Vector3(float(flat[i]), float(flat[i + 1]), float(flat[i + 2])))
		i += 3
	b.clock = DayCycle.from_dict(d.get("clock", {}))
	b.pot = str(d.get("pot", "nursery"))
	if not POTS.has(b.pot):
		b.pot = "nursery"
	b.turn = posmod(int(d.get("turn", 0)), 4)
	b.moisture = clampf(float(d.get("moisture", 0.6)), 0.0, 1.0)
	var s: Array = Array(d.get("soil", [FRESH_SOIL, FRESH_SOIL, FRESH_SOIL]))
	if s.size() == 3:
		b.soil = PackedFloat32Array([float(s[0]), float(s[1]), float(s[2])])
	b.root_fill = float(d.get("root_fill", 0.0))
	b.last_repot_day = int(d.get("last_repot_day", 0))
	b.repot_due = bool(d.get("repot_due", false))
	for m in d.get("milestones", []):
		if m is Dictionary:
			var line := {"day": int((m as Dictionary).get("day", 0)), "text": str((m as Dictionary).get("text", ""))}
			if (m as Dictionary).has("key"):
				line["key"] = str(m["key"])
			b.milestones.append(line)
	var acc: Array = d.get("accum", [0.0, 0.0, 0.0])
	b._growth_accum = float(acc[0])
	b._marker_accum = float(acc[1])
	b._leader_accum = float(acc[2])
	return b
