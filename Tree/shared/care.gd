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

## Colour of each kind's dots (0.8.1: plain coloured dots in play, so the words name the colour).
const DOT_WORDS: Array[String] = ["blue", "green", "orange", "violet"]
const HINT_WORDS: Array[String] = [
	"rushes mark water",
	"clover marks nitrogen",
	"nettles mark phosphorus, just under the grass",
	"potassium lies deep by the rocks",
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


const TIRED_LINE := "A tired branch (dull leaves, grey bark) dies back in shade; cut, it gives strength back."

const NEED_LINES: Array[String] = [
	"Thirsty: the leaves hang.",
	"Short of nitrogen (N): sparse shoots, pale leaves.",
	"Short of phosphorus (P): bare leaf masses, dark leaves.",
	"Short of potassium (K): bare leaf masses, brown edges.",
]
## The care page's doodle (0.8.2): the leaf of the strongest need, a healthy leaf without one.
const NEED_LEAVES: Array[String] = ["leaf_water", "leaf_n", "leaf_p", "leaf_k"]


## The journal's care page (0.6.3), in handwritten words: [{"title", "text"}, ...] for what the
## tree lacks and where tonight's root finds it, how the crown is shaped, and the last cut.
static func page(state: GameState) -> Array:
	var sim := state.sim
	var out: Array = []
	# What it lacks, and where to steer tonight.
	var lacks := ""
	var tonight := ""
	# The kinds named, so the page can show their marks (0.8).
	var kinds: Array = []
	if state.finished:
		lacks = "Grown: it needs nothing. The shears are for its look."
	elif state.is_seed() or state.day_number() <= 1:
		lacks = "Too young to read yet. Any dot helps."
	else:
		var shown := sim.care_shown()
		for k in range(4):
			if shown[k] < SHOW_MIN:
				continue
			lacks += ("\n" if lacks != "" else "") + NEED_LINES[k]
			kinds.append(k)
			var r := state.reach_for(k)
			if r.is_empty():
				tonight += ("\n" if tonight != "" else "") + "No %s dot in reach tonight." % DOT_WORDS[k]
			else:
				tonight += ("\n" if tonight != "" else "") + "Steer for the %s dots: %s (%s)." % [DOT_WORDS[k], where_words(r), HINT_WORDS[k]]
		if lacks == "":
			lacks = "It has what it needs today."
			tonight = "Any dots will do; water and nitrogen go fastest."
	out.append({"title": "What it lacks", "text": lacks})
	if tonight != "":
		out.append({"title": "Tonight's root", "text": tonight, "kinds": kinds if not kinds.is_empty() else [0, 1, 2, 3]})
	# The crown's shape.
	if not state.is_seed():
		var shape := crown_shape(sim)
		var words: Array[String] = []
		if float(shape["lean"]) > 0.25:
			words.append("It leans %s, where the sun fed it." % shape["lean_dir"])
		else:
			words.append("It stands evenly round the trunk.")
		if float(shape["crowded"]) > 0.15:
			words.append("%d twigs sit in shade: thin a branch above them." % int(shape["shaded"]))
		else:
			words.append("Open: light reaches most twigs.")
		# A twig the tree marks for pruning (0.7): one calm line, never a count.
		if not state.finished and not sim.tired_nodes().is_empty():
			words.append(TIRED_LINE)
		if state.last_dieback > 0:
			words.append("%d shaded twig%s died back at dawn." % [state.last_dieback, "" if state.last_dieback == 1 else "s"])
		out.append({"title": "The crown", "text": " ".join(words)})
	# The last cut.
	var cut := sim.last_cut
	var cut_text := "No cuts yet. Buds wake below a cut next morning."
	if not cut.is_empty():
		var head := "Day %d: %d segment%s cut." % [int(cut["day"]), int(cut["nodes"]), "" if int(cut["nodes"]) == 1 else "s"]
		if not bool(cut.get("woken", false)):
			cut_text = head + " At dawn buds will wake below it (about %d segments)." % sim.pending_refund()
		elif int(cut.get("regrown", 0)) == 0 and int(cut.get("buds", 0)) == 0:
			cut_text = head + " Grown: that cut was for its look."
		else:
			cut_text = head + " %d bud%s woke; %d segments grew back." % [int(cut["buds"]), "" if int(cut["buds"]) == 1 else "s", int(cut["regrown"])]
	out.append({"title": "The last cut", "text": cut_text})
	return out


## The care page's doodle (0.8.2, item 23): the leaf of the need the tree shows most, a healthy
## leaf when it lacks nothing.
static func doodle(state: GameState) -> String:
	if state.finished or state.is_seed() or state.day_number() <= 1:
		return "leaf_ok"
	var shown := state.sim.care_shown()
	var best := -1
	for k in range(4):
		if shown[k] >= SHOW_MIN and (best < 0 or shown[k] > shown[best]):
			best = k
	return NEED_LEAVES[best] if best >= 0 else "leaf_ok"
