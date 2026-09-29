class_name Diary
extends RefCounted
## The journal's diary: the game writes one or more lines per day, the player can add notes,
## and each morning brings an optional wish (design doc section 8). Pure data.
## From 0.7 most wishes point at a wish deposit underground (Underground.WISH_DEPOSITS): it
## glows at night, and reaching it writes a line with a small ink drawing (docs/notes/wish-0.7.md).

## Each: {"day": int, "text": String, "by": "tree" | "player"}, plus "drawing": String (an ink
## sketch the journal draws beside the line: "rushes" or "clover") on a reached wish.
var entries: Array = []
var wish: String = ""
## The wish deposit (index into Underground.patches) today's wish points at, or -1.
var wish_patch: int = -1
## Yesterday's wish deposit, missed: it still glows faintly tonight, then fades. -1 if none.
var last_patch: int = -1
## Today's wish deposit was reached (the diary line is written once).
var wish_reached: bool = false

## Share of the days whose wish points at a wish deposit underground (the rest are day wishes).
const UNDERGROUND_SHARE: float = 0.75
## A wish deposit counts as reachable when the straight line to it from some root node is free
## of rock and costs at most this share of a full calm tank (RootSystem.calm_life_force).
const REACH_SHARE: float = 0.6
## The root reached the patch when a main-root node lies within its radius plus this.
const REACH_MARGIN: float = 0.35
## A patch with fewer of its dots left (or more of them tapped) than this share is not picked.
const MIN_LEFT_SHARE: float = 0.5
## Glow strength of today's wish deposit and of yesterday's missed one.
const GLOW_TODAY: float = 1.0
const GLOW_YESTERDAY: float = 0.45

const DAY_WISHES: Array[String] = [
	"Today, grow the crown toward the morning sun: boost early, when it stands in the east.",
	"Today, look at the tree from every side before the sun sets.",
	"Today, let the sun shine calmly and save life force for a long root.",
]


func add(day: int, text: String, by: String = "tree", drawing: String = "") -> void:
	var e := {"day": day, "text": text, "by": by}
	if drawing != "":
		e["drawing"] = drawing
	entries.append(e)


func lines_for_day(day: int) -> Array:
	var out: Array = []
	for e in entries:
		if int(e["day"]) == day:
			out.append(e)
	return out


func last(n: int) -> Array:
	return entries.slice(maxi(0, entries.size() - n))


## An optional wish for the morning, pointing at something the meadow shows.
## Deterministic from the save seed, the day and the roots grown so far.
static func make_wish(ground: Underground, day: int, seed: int, roots: RootSystem = null) -> String:
	return str(make_wish_target(ground, day, seed, roots)["text"])


## The morning's wish: {"text": String, "patch": int (a wish deposit in ground.patches, or -1)}.
## Only a wish deposit that tonight's root can reach is picked (see reachable).
static func make_wish_target(ground: Underground, day: int, seed: int, roots: RootSystem = null) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "wish", day])
	var underground := rng.randf() < UNDERGROUND_SHARE
	var pick := rng.randi()
	if underground:
		var options := PackedInt32Array()
		for p in ground.wish_patch_ids():
			if is_fresh(ground, p, roots) and reachable(ground, p, roots):
				options.append(p)
		if not options.is_empty():
			var p := options[pick % options.size()]
			return {"text": wish_text(ground, p), "patch": p}
	return {"text": DAY_WISHES[pick % DAY_WISHES.size()], "patch": -1}


static func wish_text(ground: Underground, patch_id: int) -> String:
	var patch: Dictionary = ground.patches[patch_id]
	var where := Underground.compass(patch["center"])
	if int(patch["kind"]) == Resources.Kind.WATER:
		return "Today, reach the damp patch with the rushes in the %s." % where
	return "Today, find what feeds the clover in the %s." % where


## The line the diary gets when a root reaches a wish deposit.
static func reached_text(ground: Underground, patch_id: int, night: int) -> String:
	var patch: Dictionary = ground.patches[patch_id]
	var where := Underground.compass(patch["center"])
	if int(patch["kind"]) == Resources.Kind.WATER:
		return "Night %d: the root found the damp patch under the rushes in the %s, the one I wished for." % [night, where]
	return "Night %d: the root found what feeds the clover in the %s, the one I wished for." % [night, where]


static func drawing_for(ground: Underground, patch_id: int) -> String:
	return "rushes" if int(ground.patches[patch_id]["kind"]) == Resources.Kind.WATER else "clover"


## Enough of the patch is left and not yet drunk by the roots.
static func is_fresh(ground: Underground, patch_id: int, roots: RootSystem = null) -> bool:
	var ids := ground.patch_dots(patch_id)
	if ids.is_empty():
		return false
	var left := 0
	for i in ids:
		if ground.dot_collected[i] == 0 and ground.fullness(i) > 0.5 and (roots == null or not roots.tapped.has(i)):
			left += 1
	return left >= ids.size() * MIN_LEFT_SHARE


## A full calm tank reaches the patch from some root node (or the trunk): the straight line to
## its near edge is free of rock and costs at most REACH_SHARE of the tank.
static func reachable(ground: Underground, patch_id: int, roots: RootSystem = null) -> bool:
	var patch: Dictionary = ground.patches[patch_id]
	var c: Vector3 = patch["center"]
	var r := float(patch["radius"])
	var sys := roots if roots != null else RootSystem.new()
	var tank := sys.calm_life_force * REACH_SHARE
	for start in nearest_starts(sys, c, 6):
		var to := c - start
		if to.length() <= r:
			return true
		var goal := c - to.normalized() * r * 0.6
		if line_cost(ground, sys, start, goal) <= tank:
			return true
	return false


## Life force for a straight root from `a` to `b`, or INF if it passes through rock.
static func line_cost(ground: Underground, sys: RootSystem, a: Vector3, b: Vector3) -> float:
	var length := a.distance_to(b)
	var steps := maxi(1, ceili(length / 0.25))
	var dir := (b - a) / length if length > 1e-4 else Vector3.DOWN
	var cost := 0.0
	for s in range(steps):
		var p := a.lerp(b, (s + 0.5) / steps)
		if ground.is_inside_rock(p, 0.1):
			return INF
		cost += sys.cost_per_metre(p, dir) * length / steps
	return cost


## Positions of up to `n` root nodes nearest to `p` (the trunk when there are no roots yet).
static func nearest_starts(sys: RootSystem, p: Vector3, n: int) -> Array[Vector3]:
	return _positions(sys, nearest_start_ids(sys, p, n))


## Ids of up to `n` root nodes nearest to `p`.
static func nearest_start_ids(sys: RootSystem, p: Vector3, n: int) -> PackedInt32Array:
	var g := sys.graph
	var best: Array = []
	var stride := maxi(1, g.size() / 600)
	for id in range(0, g.size(), stride):
		best.append([g.positions[id].distance_squared_to(p), id])
	best.sort_custom(func(a: Array, b: Array) -> bool: return a[0] < b[0])
	var out := PackedInt32Array()
	for k in range(mini(n, best.size())):
		out.append(int(best[k][1]))
	return out


static func _positions(sys: RootSystem, ids: PackedInt32Array) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for id in ids:
		out.append(sys.graph.positions[id])
	if out.is_empty():
		out.append(Vector3.ZERO)
	return out


## Called at sunrise: a missed wish deposit glows faintly one more night; then the new wish.
func new_wish(ground: Underground, day: int, seed: int, roots: RootSystem) -> void:
	last_patch = wish_patch if wish_patch >= 0 and not wish_reached else -1
	var w := make_wish_target(ground, day, seed, roots)
	wish = str(w["text"])
	wish_patch = int(w["patch"])
	wish_reached = false
	if last_patch == wish_patch:
		last_patch = -1


## The deposits that glow tonight: [{"patch", "center", "radius", "strength"}].
func glows(ground: Underground) -> Array:
	var out: Array = []
	if wish_patch >= 0 and wish_patch < ground.patches.size() and not wish_reached:
		out.append(_glow(ground, wish_patch, GLOW_TODAY))
	if last_patch >= 0 and last_patch < ground.patches.size():
		out.append(_glow(ground, last_patch, GLOW_YESTERDAY))
	return out


func _glow(ground: Underground, patch_id: int, strength: float) -> Dictionary:
	var p: Dictionary = ground.patches[patch_id]
	return {"patch": patch_id, "center": p["center"], "radius": p["radius"], "strength": strength}


## A point lies within reach of a glowing deposit: its patch id, or -1.
func glow_at(ground: Underground, p: Vector3) -> int:
	for glow in glows(ground):
		if p.distance_to(glow["center"]) <= float(glow["radius"]) + REACH_MARGIN:
			return int(glow["patch"])
	return -1


## After a run: the glowing deposit the new main root reached (-1 if none). The diary line with
## its drawing is written here, and the deposit stops glowing.
func check_reached(ground: Underground, roots: RootSystem, day: int, night: int) -> int:
	var g := roots.graph
	var main := roots.main_root_count - 1
	for id in range(maxi(1, roots.run_first_new_id), g.size()):
		if g.get_flag(id, "main", -1) != main:
			continue
		var p := glow_at(ground, g.positions[id])
		if p < 0:
			continue
		add(day, reached_text(ground, p, night), "tree", drawing_for(ground, p))
		if p == wish_patch:
			wish_reached = true
		if p == last_patch:
			last_patch = -1
		return p
	return -1


func to_dict() -> Dictionary:
	return {"entries": entries, "wish": wish, "wish_patch": wish_patch, "last_patch": last_patch, "wish_reached": wish_reached}


static func from_dict(d: Dictionary) -> Diary:
	var diary := Diary.new()
	for e in d.get("entries", []):
		diary.add(int(e.get("day", 0)), str(e.get("text", "")), str(e.get("by", "tree")), str(e.get("drawing", "")))
	diary.wish = str(d.get("wish", ""))
	diary.wish_patch = int(d.get("wish_patch", -1))
	diary.last_patch = int(d.get("last_patch", -1))
	diary.wish_reached = bool(d.get("wish_reached", false))
	return diary
