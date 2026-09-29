class_name Care
extends RefCounted
## Reading the tree (0.6.3, docs/notes/care-0.6.3.md): what it lacks, where tonight's root can
## find it, and how the crown is shaped. Only reads the game; changes nothing. Pure data.

## A kind's stock covering this share of a full calm day or more: no need. At NEED_FULL or less
## the need is full.
const NEED_START := 0.75
const NEED_FULL := 0.25
## Yesterday's signal eases into today's over this share of the daylight (gone by mid-morning).
const EASE_SHARE := 0.35
## Tonight's life force is estimated as what the leaves hold now plus this share of a calm rest
## of the day (boosting spends some; the reach check stays on the safe side).
const LIFE_FORCE_MARGIN := 0.8
## A signal weaker than this does not show.
const SHOW_MIN := 0.05
## A deposit with less than this share of its capacity left does not count.
const DEPOSIT_MIN := 0.1

const DOT_WORDS: Array[String] = ["blue", "green", "orange", "violet"]
const HINT_WORDS: Array[String] = [
	"rushes and a damp patch on the meadow mark water",
	"clover and nettles on the meadow mark nitrogen",
	"phosphorus lies in the topsoil, close under the grass",
	"potassium sits deep down beside the rocks",
]


## Need 0..1 from the share of a calm day's growth the stock covers.
static func need_from(coverage: float) -> float:
	return smoothstep(NEED_START, NEED_FULL, coverage)


## Life force the player will likely have for tonight's root (a calm rest of the day).
static func expected_life_force(state: GameState) -> float:
	var sim := state.sim
	var lf := sim.resources.life_force
	match state.phase:
		GameState.Phase.DAY:
			var clock := sim.clock
			var day_seconds := clock.seconds_per_day * clock.daylight_fraction
			# The sun's arc from now to sunset: the integral of sin over the rest of the day.
			var t := clampf(clock.time_of_day / clock.daylight_fraction, 0.0, 1.0)
			var light_seconds := day_seconds / PI * (1.0 + cos(PI * t))
			var gain := sim.effective_leaves() * sim.life_force_per_tip * light_seconds \
				* sim.species.life_force_factor(clock.day_count, false) * sim.young_leaf_bonus()
			return lf + gain * LIFE_FORCE_MARGIN
		GameState.Phase.SUNSET:
			return lf
		_:
			return 0.0 if state.run_used else lf


## The nearest untapped deposit of `kind` a root can reach tonight: {"dot", "position",
## "distance" (from the nearest root), "from_trunk", "depth", "direction" (compass word)}, or {}
## when none is in reach (or no root can start tonight).
static func reachable(state: GameState, kind: int, life_force: float = -1.0) -> Dictionary:
	if not state.roots.can_start_run():
		return {}
	if life_force < 0.0:
		life_force = expected_life_force(state)
	if life_force <= 0.0:
		return {}
	var ground := state.ground
	var roots := state.roots
	# Root starts, one per cell of 1.5 m (a root can start anywhere on the old roots).
	var starts := {}
	for id in range(roots.graph.size()):
		var p := roots.graph.positions[id]
		var key := Vector3i((p / 1.5).floor())
		if not starts.has(key):
			starts[key] = p
	var points: Array = starts.values()
	var best := {}
	var best_d := INF
	for i in range(ground.dot_count()):
		if ground.dot_kinds[i] != kind or ground.dot_collected[i] != 0 or roots.tapped.has(i):
			continue
		if ground.fullness(i) < DEPOSIT_MIN:
			continue
		var at := ground.dot_positions[i]
		var d := INF
		for p in points:
			d = minf(d, at.distance_to(p))
		# The price per metre rises with distance and depth: the deposit's own is the upper bound.
		if d * roots.cost_per_metre(at) > life_force or d >= best_d:
			continue
		best_d = d
		best = {"dot": i, "position": at, "distance": d, "from_trunk": Vector2(at.x, at.z).length(),
			"depth": -at.y, "direction": Underground.compass(at)}
	return best


## Words for where a deposit lies, for the care page ("north-east, about 4 m out, shallow").
static func where_words(r: Dictionary) -> String:
	var depth: float = r["depth"]
	var how_deep := "shallow" if depth < 1.2 else ("a little deeper" if depth < Underground.TOPSOIL else "deep down")
	var out: float = r["from_trunk"]
	if out < 1.5:
		return "right under the trunk, %s" % how_deep
	return "%s, about %.0f m out, %s" % [r["direction"], out, how_deep]


## How the crown is shaped, from the living tips: {"lean" (0..1: the leaves' centre off the
## trunk, in crown radii), "lean_dir" (compass word), "crowded" (share of shaded tips),
## "shaded" (count)}.
static func crown_shape(sim: GrowthSim) -> Dictionary:
	var g := sim.graph
	var c := Vector3.ZERO
	var n := 0
	for id in g.tips():
		if g.get_flag(id, "dead", false):
			continue
		c += g.positions[id]
		n += 1
	var tips := maxi(n, 1)
	c /= float(tips)
	var r := maxf(sim.crown_radius(sim.height()), 0.3)
	var shaded := sim.shaded_tips().size()
	return {"lean": Vector2(c.x, c.z).length() / r, "lean_dir": Underground.compass(Vector3(c.x, 0.0, c.z)),
		"crowded": float(shaded) / tips, "shaded": shaded}
