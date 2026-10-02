class_name Moments
extends RefCounted
## A day worth watching (0.8.2.6, specs/journal-drawers-loop.md G2): four moments at set times of
## day. Watching shows each one; fast-forward (the held finger or the sunset picture) slows to
## normal speed for SLOW_SECONDS at each, then carries on (TreeView.moment). Missing one costs
## nothing. Pure data and maths: GameState emits "moment:<name>" when the clock crosses one,
## the scenes show it.
## - morning: the night's result (the reveal at the first view, TreeView.morning_reveal) and the
##   wish plant opening its flowers (WishPlant.open_flowers).
## - visitor: the day's visitor arrives (a first-time one, Visitors.arrive, or the wren).
## - weather: the mood turns for a few seconds (a breeze, a passing cloud, a short shower); look
##   only, the sim and today's weather (Almanac) are untouched.
## - sunset: the ink ring round the wish place, then the dive (the "sunset" event; the clock holds
##   there anyway, so fast-forward ends at it).

const MORNING := "morning"
const VISITOR := "visitor"
const WEATHER := "weather"
const SUNSET := "sunset"
## Game hours on the clock face (6:00 sunrise, 20:00 sunset). Morning just after the morning event
## (GameState.MORNING_DELAY, about 7:17, the wish scrap); the visitor at BrushPile.WREN_FROM (9:30),
## when the wren may start singing.
const HOURS := {"morning": 7.3, "visitor": 9.5, "weather": 14.0, "sunset": 20.0}
## Real seconds fast-forward runs at normal speed at a moment, before it eases in again.
const SLOW_SECONDS: float = 2.0
## The weather turn, real seconds: rising, holding, fading (about the slow-down and a little more).
const TURN_IN: float = 1.0
const TURN_HOLD: float = 4.0
const TURN_OUT: float = 2.5
const TURNS: Array[String] = ["breeze", "cloud", "shower"]
## The morning glow along last night's new roots, real seconds.
const GLOW_SECONDS: float = 2.4


## The moments whose hour lies in (before, after] (game hours), in order. The sunset is not
## among them: it is the clock's own hold (GameState "sunset").
static func crossed(before: float, after: float) -> Array[String]:
	var out: Array[String] = []
	for m: String in [MORNING, VISITOR, WEATHER]:
		var h := float(HOURS[m])
		if before < h and after >= h:
			out.append(m)
	return out


## Today's weather turn: one of TURNS from the save seed and the day (a shower is only a short
## one on a dry day; on a rainy day it is a breeze or a cloud).
static func turn_kind(seed: int, day: int, rainy: bool) -> String:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, day, "weather_turn"])
	var r := rng.randf()
	if rainy:
		return "breeze" if r < 0.5 else "cloud"
	return "breeze" if r < 0.4 else ("cloud" if r < 0.75 else "shower")


## The turn's strength 0..1 `t` real seconds after it began (0 before, and after it is over).
static func turn_amount(t: float) -> float:
	if t < 0.0:
		return 0.0
	if t < TURN_IN:
		return smoothstep(0.0, 1.0, t / TURN_IN)
	if t < TURN_IN + TURN_HOLD:
		return 1.0
	return 1.0 - smoothstep(0.0, 1.0, (t - TURN_IN - TURN_HOLD) / TURN_OUT)


## The glow's share of its run spent on the way out from the trunk along the older roots.
const PATH_SHARE: float = 0.3


## Last night's new root segments (ids from..to-1 of the root graph, each with its parent), after
## the way to them from the trunk along the older roots ("old"), so the light runs out from the
## tree: [{"a": Vector3, "b": Vector3, "u": 0..1 when the light reaches it, "main": bool, "old": bool}].
static func night_segments(roots: RootSystem, from: int, to: int) -> Array:
	var out: Array = []
	var g := roots.graph
	to = mini(to, g.size())
	if from < 1 or to <= from:
		return out
	var path: Array[int] = []
	var id := g.parents[from]
	while id > 0 and path.size() < 400:
		path.append(id)
		id = g.parents[id]
	path.reverse()
	var head := PATH_SHARE if not path.is_empty() else 0.0
	for i in range(path.size()):
		var n := path[i]
		out.append({"a": g.positions[g.parents[n]], "b": g.positions[n], "u": head * (i + 1) / path.size(), "main": true, "old": true})
	var span := float(maxi(1, to - from))
	for n in range(from, to):
		var p := g.parents[n]
		if p < 0:
			continue
		out.append({"a": g.positions[p], "b": g.positions[n], "u": head + (1.0 - head) * (n - from) / span, "main": int(g.get_flag(n, "main", -1)) >= 0, "old": false})
	return out


## Where last night's new roots lie from the trunk, on the ground (their middle), or ZERO.
static func night_middle(roots: RootSystem, from: int, to: int) -> Vector2:
	var g := roots.graph
	to = mini(to, g.size())
	if from < 1 or to <= from:
		return Vector2.ZERO
	var sum := Vector2.ZERO
	for n in range(from, to):
		sum += Vector2(g.positions[n].x, g.positions[n].z)
	return sum / float(to - from)
