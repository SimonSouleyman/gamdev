class_name Diary
extends RefCounted
## The journal's diary: the game writes one or more lines per day, the player can add notes,
## and each morning brings an optional wish (design doc section 8). Pure data.
## From 0.7 most wishes point at a wish deposit the generator places ahead of the newest root
## tip (Underground.wish_deposits): it glows at night, and reaching it writes a line with a small ink drawing (docs/notes/wish-0.7.md).

## Each: {"day": int, "text": String, "by": "tree" | "player"}, plus "drawing": String (an ink
## sketch, InkSketch kind) and from 0.8.2 "topic": String (TOPICS; a line without one is a plain
## note, shown only when nothing else fills the day's third place).
var entries: Array = []
var wish: String = ""
## The wish deposit (index into Underground.patches) today's wish points at, or -1.
var wish_patch: int = -1
## Yesterday's wish deposit, missed: it still glows faintly tonight when no new wish glows, then
## fades; a new wish's glow puts it out at once (0.8: one glow at a time). -1 if none.
var last_patch: int = -1
## Today's wish deposit was reached (the diary line is written once).
var wish_reached: bool = false

## Share of the days whose wish points at a wish deposit underground (the rest are day wishes).
## A static var so tools can compare with and without (strategies.gd --set=wish_share=0).
static var underground_share: float = 0.6
## A wish deposit counts as reachable when the straight line to it from some root node is free
## of rock and costs at most this share of a full calm tank (RootSystem.calm_life_force).
const REACH_SHARE: float = 0.6
## How far beyond the newest root tip the wish deposit goes: past where the leftover's fine
## roots reach from a stub, so continuing the root is what finds it.
const AHEAD_MIN: float = 5.5
const AHEAD_MAX: float = 8.5
const PLACE_TRIES: int = 24
## With this many wish deposits missed and untouched in the soil, one of them of the kind the
## tree lacks, a new underground wish points at it again (when it lies ahead) or becomes a day
## wish (0.8, B4 of the 0.7 check).
const MISSED_MAX: int = 4
const UNTOUCHED_SHARE: float = 0.5
## Far wishes (0.8.1, item 34): in the wider root field (layout 3) this share of the new
## underground wishes places its deposit in the middle or far ring (Underground.FAR_RING or more
## from the trunk), FAR_AHEAD_MIN to FAR_AHEAD_MAX beyond the newest root tip, where a straight
## root from that tip costs at most FAR_REACH_NIGHTS calm reaches (REACH_SHARE of a calm tank
## each), so continuing the root reaches it within two or three nights. The wish then stays for up
## to FAR_DAYS mornings until a root reaches it. A static var so tools can try other shares.
static var far_share: float = 0.5
const FAR_AHEAD_MIN: float = 9.0
const FAR_AHEAD_MAX: float = 17.0
const FAR_REACH_NIGHTS: float = 2.5
const FAR_DAYS: int = 3
## The root reached the patch when a main-root node lies within its radius plus this.
const REACH_MARGIN: float = 0.35
## Glow strength of today's wish deposit and of yesterday's missed one.
const GLOW_TODAY: float = 1.0
const GLOW_YESTERDAY: float = 0.45

const DAY_WISHES: Array[String] = [
	"Today, grow the crown toward the morning sun: boost early, when it stands in the east.",
	"Today, look at the tree from every side before the sun sets.",
	"Today, let the sun shine calmly and save life force for a long root.",
]


## A shorter journal (0.8.2, specs/0.8.md items 21 and 22): a day page holds at most
## PAGE_MAX of the game's lines, each about eight words: the day's wish, a need the tree shows,
## and one find, visitor or milestone (a mood line, then a plain note, only when there is none).
## Routine lines (numbers, "the tree grew") are no longer written. The player's own notes are
## theirs and always show, below.
const PAGE_MAX: int = 3
## A game line stays at or under this many words (one line on the phone; tests check the width).
const MAX_WORDS: int = 9
const TOPICS: Array[String] = ["wish", "care", "find", "visitor", "milestone", "mood"]
## The third place of a day page, in order of preference.
const THIRD: Array[String] = ["find", "visitor", "milestone", "mood", ""]


func add(day: int, text: String, by: String = "tree", drawing: String = "", topic: String = "") -> void:
	var e := {"day": day, "text": text, "by": by}
	if drawing != "":
		e["drawing"] = drawing
	if topic != "":
		e["topic"] = topic
	entries.append(e)


## The game's lines shown on a day's page (at most PAGE_MAX): the day's wish, then a need, then
## the best of the rest (THIRD). A topic written twice keeps its last line (a reached wish after
## the morning's wish is a find, not a repeat).
func page(day: int) -> Array:
	var by_topic := {}
	for e in entries:
		if int(e["day"]) != day or str(e.get("by", "tree")) == "player":
			continue
		var t := str(e.get("topic", ""))
		if t == "wish" or t == "care":
			by_topic[t] = e
		elif not by_topic.has(t):
			by_topic[t] = e
	var out: Array = []
	for t in ["wish", "care"]:
		if by_topic.has(t):
			out.append(by_topic[t])
	for t in THIRD:
		if by_topic.has(t):
			out.append(by_topic[t])
			break
	return out.slice(0, PAGE_MAX)


## The player's own notes of a day.
func notes(day: int) -> Array:
	return entries.filter(func(e: Dictionary) -> bool: return int(e["day"]) == day and str(e.get("by", "tree")) == "player")


## Days that have a page, in order.
func days() -> Array[int]:
	var out: Array[int] = []
	for e in entries:
		var d := int(e["day"])
		if not out.has(d):
			out.append(d)
	out.sort()
	return out


## The one doodle of a day's page (0.8.2, item 23): the drawing of its find, visitor or
## milestone, else of the wish (its plant), else of the need (its leaf); a sun when none has one.
func doodle(day: int) -> String:
	var lines := page(day)
	for i in [2, 0, 1]:
		if i < lines.size() and lines[i].has("drawing"):
			return str(lines[i]["drawing"])
	for e in lines:
		if e.has("drawing"):
			return str(e["drawing"])
	return "sun"


func lines_for_day(day: int) -> Array:
	var out: Array = []
	for e in entries:
		if int(e["day"]) == day:
			out.append(e)
	return out


func last(n: int) -> Array:
	return entries.slice(maxi(0, entries.size() - n))


## An optional wish for the morning, pointing at something the meadow shows.
## Deterministic from the save seed, the day, the roots grown so far and what the tree lacks.
static func make_wish(ground: Underground, day: int, seed: int, roots: RootSystem = null, res: Resources = null) -> String:
	return str(plan_wish(ground, day, seed, roots, res)["text"])


## The morning's wish, not yet placed: {"text", "kind" (-1 for a day wish), and for a wish
## underground "center", "radius", "count", "from" (the newest root tip it continues from)}.
## The deposit goes AHEAD_MIN to AHEAD_MAX beyond the newest root tip, outward from the trunk
## where it can, never into rock, and only where a straight root from that tip costs at most
## REACH_SHARE of a calm tank. Its kind is what the tree lacks most (0.8: any of the four).
static func plan_wish(ground: Underground, day: int, seed: int, roots: RootSystem = null, res: Resources = null) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "wish", day])
	var underground := rng.randf() < underground_share
	var pick := rng.randi()
	var coin := rng.randi() % 2
	if underground:
		var sys := roots if roots != null else RootSystem.new()
		var kind := wish_kind(sys, res, coin)
		var size := Underground.wish_size(kind, rng, ground.layout)
		var tip := newest_tip(sys)
		# An earlier wish deposit of the same kind, missed and still untouched, lies ahead: the
		# wish points at it again instead of adding another (0.8, B4: missed deposits piled up
		# to 20 fresh patches in reach by the month's last week).
		var again := missed_ahead(ground, sys, kind, sys.graph.positions[tip])
		if again >= 0:
			var p: Dictionary = ground.patches[again]
			return {"text": wish_text_at(kind, p["center"]), "kind": kind, "patch": again,
				"center": p["center"], "radius": p["radius"], "from": tip}
		# Enough missed deposits lie waiting already, one of them of this kind: a day wish
		# instead of one more (a kind the tree lacks and no missed deposit holds still gets one).
		if untouched_wishes(ground, sys) >= MISSED_MAX and untouched_wishes(ground, sys, kind) > 0:
			return {"text": DAY_WISHES[pick % DAY_WISHES.size()], "kind": -1}
		var center := Vector3.INF
		if ground.layout >= 3:
			# A separate draw, so the near wishes of the wider field stay as they would be.
			var far_rng := RandomNumberGenerator.new()
			far_rng.seed = hash([seed, "far_wish", day])
			if far_rng.randf() < far_share:
				center = place_far(ground, sys, sys.graph.positions[tip], float(size["radius"]), far_rng)
		var far := center != Vector3.INF
		if center == Vector3.INF:
			center = place_ahead(ground, sys, sys.graph.positions[tip], float(size["radius"]), rng)
		if center != Vector3.INF:
			return {"text": wish_text_at(kind, center), "kind": kind, "center": center,
				"radius": size["radius"], "count": size["count"], "from": tip, "far": far}
	return {"text": DAY_WISHES[pick % DAY_WISHES.size()], "kind": -1}


## The kind the tree lacks most: the lowest stock for its need, of the kinds the species needs
## (0.7 chose only between water and nitrogen, so the glow never led to the phosphorus the trees
## were really short of). Without resources, water or nitrogen by `coin`.
static func wish_kind(sys: RootSystem, res: Resources, coin: int) -> int:
	if res == null:
		return Resources.Kind.WATER if coin == 0 else Resources.Kind.NITROGEN
	var best := Resources.Kind.WATER
	var lowest := INF
	for k in range(4):
		var need: float = sys.species.needs[k]
		if need <= 0.0:
			continue
		var ratio := res.stock[k] / need
		if ratio < lowest:
			lowest = ratio
			best = k
	return best


## The node where the newest main root ended (the trunk before the first root).
static func newest_tip(sys: RootSystem) -> int:
	var main := sys.main_root_count - 1
	if main < 0:
		return 0
	for id in range(sys.graph.size() - 1, 0, -1):
		if sys.graph.get_flag(id, "main", -1) == main:
			return id
	return 0


## Wish deposits no root has found yet (most of their dots fresh).
static func untouched_wishes(ground: Underground, sys: RootSystem, kind: int = -1) -> int:
	var n := 0
	for pid in ground.wish_patch_ids():
		if kind >= 0 and int(ground.patches[pid]["kind"]) != kind:
			continue
		if _untouched(ground, sys, pid):
			n += 1
	return n


## Most of the patch (UNTOUCHED_SHARE of its dots) still waits fresh: a root that only grazed
## its edge did not find it.
static func _untouched(ground: Underground, sys: RootSystem, pid: int) -> bool:
	if bool(ground.patches[pid].get("reached", false)):
		return false
	var dots := ground.patch_dots(pid)
	var fresh := 0
	for i in dots:
		if sys.is_fresh(i) and ground.dot_collected[i] == 0:
			fresh += 1
	return fresh >= UNTOUCHED_SHARE * dots.size()


## The nearest earlier wish deposit of `kind` that no root has found yet, AHEAD_MIN - 2 to
## AHEAD_MAX + 2 m from `from` and within REACH_SHARE of a calm tank in a straight line; -1 if none.
static func missed_ahead(ground: Underground, sys: RootSystem, kind: int, from: Vector3) -> int:
	var best := -1
	var best_d := INF
	var tank := sys.calm_life_force * REACH_SHARE
	for pid in ground.wish_patch_ids():
		var p: Dictionary = ground.patches[pid]
		if int(p["kind"]) != kind:
			continue
		var c: Vector3 = p["center"]
		var d := Vector2(c.x - from.x, c.z - from.z).length()
		if d < AHEAD_MIN - 2.0 or d > AHEAD_MAX + 2.0 or d >= best_d:
			continue
		if not _untouched(ground, sys, pid):
			continue
		var goal := c - (c - from).normalized() * float(p["radius"]) * 0.6
		if line_cost(ground, sys, from, goal) <= tank:
			best = pid
			best_d = d
	return best


## A place AHEAD_MIN..AHEAD_MAX beyond `from`, outward from the trunk (turning further aside with
## each try), free of rock, in the topsoil, inside the world, and within a calm tank's reach.
## Vector3.INF if none was found.
static func place_ahead(ground: Underground, sys: RootSystem, from: Vector3, radius: float, rng: RandomNumberGenerator) -> Vector3:
	var flat := Vector2(from.x, from.z)
	var base := flat.angle() if flat.length() > 0.8 else rng.randf() * TAU
	var tank := sys.calm_life_force * REACH_SHARE
	for i in range(PLACE_TRIES):
		var spread := lerpf(0.5, PI, float(i) / (PLACE_TRIES - 1))
		var a := base + rng.randf_range(-spread, spread)
		var d := rng.randf_range(AHEAD_MIN, AHEAD_MAX)
		var depth := clampf(-from.y + rng.randf_range(-0.3, 0.5), Underground.WISH_MIN_DEPTH, Underground.WISH_MAX_DEPTH)
		var c := Vector3(from.x + cos(a) * d, -depth, from.z + sin(a) * d)
		var r := Vector2(c.x, c.z).length()
		if r > ground.extent - radius - 0.5 or r < 2.5:
			continue
		if ground.rock_at(c, radius + 0.3) >= 0:
			continue
		var goal := c - (c - from).normalized() * radius * 0.6
		if line_cost(ground, sys, from, goal) <= tank:
			return c
	return Vector3.INF


## A far wish's place (0.8.1, item 34): in the middle or far ring, FAR_AHEAD_MIN..FAR_AHEAD_MAX
## beyond `from` (outward from the trunk first, turning aside with each try), free of rock, in the
## topsoil, inside the field, and within FAR_REACH_NIGHTS calm reaches of a straight root from
## `from`. Vector3.INF if none was found (the wish is then a near one).
static func place_far(ground: Underground, sys: RootSystem, from: Vector3, radius: float, rng: RandomNumberGenerator) -> Vector3:
	var flat := Vector2(from.x, from.z)
	var base := flat.angle() if flat.length() > 0.8 else rng.randf() * TAU
	var tank := sys.calm_life_force * REACH_SHARE * FAR_REACH_NIGHTS
	for i in range(PLACE_TRIES):
		var spread := lerpf(0.4, PI, float(i) / (PLACE_TRIES - 1))
		var a := base + rng.randf_range(-spread, spread)
		var d := rng.randf_range(FAR_AHEAD_MIN, FAR_AHEAD_MAX)
		var depth := rng.randf_range(Underground.WISH_MIN_DEPTH, Underground.WISH_MAX_DEPTH)
		var c := Vector3(from.x + cos(a) * d, -depth, from.z + sin(a) * d)
		var r := Vector2(c.x, c.z).length()
		if r > ground.extent - radius - 0.5 or r < Underground.FAR_RING:
			continue
		if ground.rock_at(c, radius + 0.3) >= 0:
			continue
		var goal := c - (c - from).normalized() * radius * 0.6
		if line_cost(ground, sys, from, goal) <= tank:
			return c
	return Vector3.INF


## A far wish's deposit (item 34): placed by place_far, in the middle or far ring and a long drive
## beyond the newest tip.
static func is_far(ground: Underground, patch_id: int) -> bool:
	if patch_id < 0 or patch_id >= ground.patches.size():
		return false
	return bool(ground.patches[patch_id].get("far", false))


static func wish_text(ground: Underground, patch_id: int) -> String:
	var patch: Dictionary = ground.patches[patch_id]
	return wish_text_at(int(patch["kind"]), patch["center"])


static func wish_text_at(kind: int, center: Vector3) -> String:
	var where := Underground.compass(center)
	match kind:
		Resources.Kind.WATER:
			return "Today, reach the damp patch with the rushes in the %s." % where
		Resources.Kind.PHOSPHORUS:
			return "Today, find what feeds the nettles in the %s." % where
		Resources.Kind.POTASSIUM:
			return "Today, reach the deep soil under the comfrey in the %s." % where
	return "Today, find what feeds the clover in the %s." % where


## The plants over the deposits, for the day page's short lines.
const PLANTS: Array[String] = ["rushes", "clover", "nettles", "comfrey"]


## The day page's wish line (0.8.2): short, with where it points. A day wish keeps its gist.
static func wish_entry(kind: int, center: Vector3, kept: bool = false) -> String:
	if kind < 0:
		return ""
	var where := Underground.compass(center)
	return ("Still: the %s in the %s." if kept else "Wish: the %s in the %s.") % [PLANTS[kind], where]


const DAY_WISH_LINES: Array[String] = [
	"Wish: boost early, grow eastward.",
	"Wish: see the tree from every side.",
	"Wish: a calm sun, a long root tonight.",
]


## The line the diary gets when a root reaches a wish deposit.
static func reached_text(ground: Underground, patch_id: int, night: int) -> String:
	return "Night %d: wish found, the %s." % [night, PLANTS[int(ground.patches[patch_id]["kind"])]]


## The ink sketch beside a reached wish's line: what grows (or lies) above the deposit.
const DRAWINGS: Array[String] = ["rushes", "clover", "nettles", "comfrey"]


static func drawing_for(ground: Underground, patch_id: int) -> String:
	return DRAWINGS[int(ground.patches[patch_id]["kind"])]


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


## Called at sunrise: the new wish, whose deposit the generator places now. A missed wish
## deposit glows faintly one more night only when the new wish does not glow: one glow at a time
## (0.7 broken item 3; 0.8 section 5). Either way the missed deposit stays as a plain deposit.
func new_wish(ground: Underground, day: int, seed: int, roots: RootSystem, res: Resources = null) -> void:
	# A far wish not reached yet stays for up to FAR_DAYS mornings: it takes a root continued over
	# two or three nights (0.8.1, item 34).
	if wish_patch >= 0 and not wish_reached and is_far(ground, wish_patch):
		var placed := int(ground.patches[wish_patch].get("day", day))
		if day - placed < FAR_DAYS and _untouched(ground, roots, wish_patch):
			last_patch = -1
			var p: Dictionary = ground.patches[wish_patch]
			add(day, wish_entry(int(p["kind"]), p["center"], true), "tree", DRAWINGS[int(p["kind"])], "wish")
			return
	last_patch = wish_patch if wish_patch >= 0 and not wish_reached else -1
	wish_reached = false
	wish_patch = -1
	var w := plan_wish(ground, day, seed, roots, res)
	if w.has("patch"):
		wish_patch = int(w["patch"])
	elif int(w["kind"]) >= 0:
		wish_patch = ground.add_wish_deposit(day, int(w["kind"]), w["center"], float(w["radius"]), int(w["count"]), bool(w.get("far", false)))
	wish = str(w["text"]) if wish_patch >= 0 else DAY_WISHES[day % DAY_WISHES.size()]
	if wish_patch >= 0:
		last_patch = -1
		var p: Dictionary = ground.patches[wish_patch]
		add(day, wish_entry(int(p["kind"]), p["center"]), "tree", DRAWINGS[int(p["kind"])], "wish")
	else:
		add(day, DAY_WISH_LINES[maxi(0, DAY_WISHES.find(wish))], "tree", "sun", "wish")


## The deposits that glow tonight: [{"patch", "center", "radius", "strength"}].
func glows(ground: Underground) -> Array:
	var out: Array = []
	if wish_patch >= 0 and wish_patch < ground.patches.size() and not wish_reached:
		out.append(_glow(ground, wish_patch, GLOW_TODAY))
	elif last_patch >= 0 and last_patch < ground.patches.size() and wish_patch < 0:
		# Yesterday's missed glow only on a night without a new one (an old save may hold both).
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
		add(day, reached_text(ground, p, night), "tree", drawing_for(ground, p), "find")
		if p == wish_patch:
			wish_reached = true
		ground.mark_wish_reached(p)
		if p == last_patch:
			last_patch = -1
		return p
	return -1


func to_dict() -> Dictionary:
	return {"entries": entries, "wish": wish, "wish_patch": wish_patch, "last_patch": last_patch, "wish_reached": wish_reached}


static func from_dict(d: Dictionary) -> Diary:
	var diary := Diary.new()
	for e in d.get("entries", []):
		diary.add(int(e.get("day", 0)), str(e.get("text", "")), str(e.get("by", "tree")), str(e.get("drawing", "")), str(e.get("topic", "")))
	diary.wish = str(d.get("wish", ""))
	diary.wish_patch = int(d.get("wish_patch", -1))
	diary.last_patch = int(d.get("last_patch", -1))
	diary.wish_reached = bool(d.get("wish_reached", false))
	return diary
