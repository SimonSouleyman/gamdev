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
## Mornings whose wish pointed underground, and of them those at a far wish (0.8.2): the far draw
## keeps a running share of far_share instead of an independent coin per day, so every seed gets
## about half (the 0.8.1 check: seed 3 got 25 to 35 %).
var wish_days: int = 0
var far_days: int = 0
## The morning today's wish was chosen, or chosen again (a missed deposit pointed at anew): a far
## wish keeps for FAR_DAYS mornings from it (0.8.2.1, bug 3: counted from the day the deposit was
## placed, a far one chosen again was dropped the next morning). -1: not known (an old save).
var wish_since: int = -1

## Share of the days whose wish points at a wish deposit underground. 0.8.2.5 (specs/
## wish-compass-vial.md, item 1): 1.0, every morning has a wish place; the day wishes are gone.
## A static var so tools can compare with and without (strategies.gd --set=wish_share=0); below
## 1.0 a morning without a place has no wish at all.
static var underground_share: float = 1.0
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
## Far wishes start on this morning (0.8.2.1, bug 4): the first days teach the near wish, and
## the 0.8.2 first-time check found the far ones mattered from about day 5. Before it the running
## far share does not count.
const FAR_FROM_DAY: int = 5
## The root reached the patch when a main-root node lies within its radius plus this.
const REACH_MARGIN: float = 0.35
## Glow strength of today's wish deposit and of yesterday's missed one.
const GLOW_TODAY: float = 1.0
const GLOW_YESTERDAY: float = 0.45

## A shorter journal (0.8.2, specs/0.8.md items 21 and 22). 0.8.2.6 (specs/journal-drawers-loop.md,
## J3 "Nur Besonderes"): the Diary ribbon shows one game line a day, and only on a day something
## happened (SPECIAL): a reached wish first, then a find, a visitor, a milestone, what the night's
## root reached. The wish and the need moved to the Today page (Journal.today_lines); mood and
## care lines are no longer written. A quiet day shows its date and nothing under it. The player's
## own notes are theirs and always show, below.
const PAGE_MAX: int = 1
## A game line stays at or under this many words (one line on the phone; tests check the width).
const MAX_WORDS: int = 9
## "drank" (0.8.2.5, specs/wish-compass-vial.md item 3): the morning after a night whose root
## reached a deposit, DRANK_LINE.
const TOPICS: Array[String] = ["wish", "care", "find", "visitor", "milestone", "drank", "mood"]
## The topics worth a diary line, in order of preference (0.8.2.6). "wish" (the morning's wish
## line, still kept as the day's record), "care" and "mood" (written before 0.8.2.6) never show.
const SPECIAL: Array[String] = ["find", "visitor", "milestone", "drank"]
const DRANK_LINE := "The roots drank well."


func add(day: int, text: String, by: String = "tree", drawing: String = "", topic: String = "") -> void:
	var e := {"day": day, "text": text, "by": by}
	if drawing != "":
		e["drawing"] = drawing
	if topic != "":
		e["topic"] = topic
	entries.append(e)


## The game's line shown on a day's page (at most PAGE_MAX, 0.8.2.6: one, and only on a day
## something happened): the best of SPECIAL. A topic written twice keeps its last line, and a
## reached wish beats every other find (0.8.2.1, bug 2: the clearing's lines, written at sunrise,
## hid the night's "wish found").
## Lines written before 0.8.2 have no topic: topic_of reads it from their wording, and drops the
## routine ones (bug 1: every old day showed "The old roots drew ...", and a sun).
func page(day: int) -> Array:
	var by_topic := {}
	for e in entries:
		if int(e["day"]) != day or str(e.get("by", "tree")) == "player":
			continue
		var t := topic_of(e)
		if not SPECIAL.has(t):
			continue
		if t == "find" and by_topic.has(t) and is_reached_line(by_topic[t]) and not is_reached_line(e):
			continue
		by_topic[t] = e
	var out: Array = []
	for t in SPECIAL:
		if by_topic.has(t):
			out.append(by_topic[t])
	return out.slice(0, PAGE_MAX)


## The topic of a line that has none (written before 0.8.2), read from its wording: one of
## TOPICS, "" for a plain note (shown only when nothing else fills the day), or ROUTINE for the
## night's numbers 0.8.2 no longer writes (never shown on a page).
const ROUTINE := "routine"
## [words in the line (lower case), topic, drawing]; the first match wins.
const LEGACY_LINES: Array = [
	["the old roots drew", ROUTINE, ""],
	["the new root grew", ROUTINE, ""],
	["went into fine roots", ROUTINE, ""],
	["root nodules made", ROUTINE, ""],
	# The day's height: shown only on an old day with nothing else (a plain note).
	[" m tall with ", "", "sapling"],
	["died back in the crown", ROUTINE, ""],
	["the one i wished for", "find", ""],
	["wish found", "find", ""],
	["today, ", "wish", ""],
	["wish: ", "wish", ""],
	["fossil", "find", "fossil"],
	["old root of a tree", "find", "old_root"],
	["water vein", "find", "water_vein"],
	["lost coin", "find", "coin"],
	["found ", "find", ""],
	["anemone", "find", "anemone"],
	["fern ", "find", "fern"],
	["moss ", "find", "moss"],
	["mushroom", "find", "mushroom"],
	["hedgehog", "visitor", "hedgehog"],
	["wren", "visitor", "wren"],
	["butterflies", "visitor", "butterflies"],
	["blackbird", "visitor", "nest"],
	["a fox", "visitor", "fox"],
	["planted", "milestone", "seed"],
	["sprouted", "milestone", "sapling"],
	["blossom", "milestone", "blossom"],
	["full size", "milestone", "grown_tree"],
	["fully grown", "milestone", "grown_tree"],
	["juniper", "milestone", "bonsai"],
	["cutting", "milestone", "bonsai"],
	["brush pile", "milestone", "pile"],
	["pile of sticks", "milestone", "pile"],
	["fill the soil", "milestone", "moon"],
	["no life force", "milestone", "moon"],
	["mist", "mood", "mist"],
	["dew ", "mood", "mist"],
	["shower", "mood", "rain"],
	["thunder", "mood", "rain"],
	["rain", "mood", "rain"],
]


static func topic_of(e: Dictionary) -> String:
	if e.has("topic"):
		return str(e["topic"])
	return str(_legacy(e)[1])


## The drawing of a line: its own, or for a line from before 0.8.2 one read from its wording
## (a reached wish: the plant it names).
static func drawing_of(e: Dictionary) -> String:
	if e.has("drawing"):
		return str(e["drawing"])
	if e.has("topic"):
		return ""
	var d := str(_legacy(e)[2])
	if d == "":
		d = _plant_in(str(e.get("text", "")))
	return d


## A root reached the day's wish ("wish found", or before 0.8.2 "the one I wished for").
static func is_reached_line(e: Dictionary) -> bool:
	var text := str(e.get("text", "")).to_lower()
	return text.contains("wish found") or text.contains("the one i wished for")


static func _legacy(e: Dictionary) -> Array:
	var text := str(e.get("text", "")).to_lower() + " "
	for row in LEGACY_LINES:
		if text.contains(str(row[0])):
			return row
	return ["", "", ""]


static func _plant_in(text: String) -> String:
	for k in range(PLANTS.size()):
		if text.contains(PLANTS[k]):
			return DRAWINGS[k]
	return ""


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


## The one doodle of a day's page (0.8.2, item 23): the drawing of its line; a sun when it has
## none (0.8.2.6: a quiet day shows no doodle in the book, Journal).
func doodle(day: int) -> String:
	for e in page(day):
		if drawing_of(e) != "":
			return drawing_of(e)
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


## The morning's wish, not yet placed: {"text", "kind" (-1: no wish place, only when
## underground_share is below 1 or the soil holds no deposit at all), "center", "radius",
## "count", "from" (the newest root tip it continues from), or "patch" for a deposit wished for again}.
## The deposit goes AHEAD_MIN to AHEAD_MAX beyond the newest root tip, outward from the trunk
## where it can, never into rock, and only where a straight root from that tip costs at most
## REACH_SHARE of a calm tank. Its kind is what the tree lacks most (0.8: any of the four).
## `far_want` (0.8.2, from new_wish): how many far mornings the running share still owes
## (far_share x the underground mornings so far, this one included, minus the far ones); a far
## wish is tried when it is at least a seeded threshold between 0.25 and 0.75. Below 0 (a bare
## plan without a diary): the independent coin of 0.8.1.
static func plan_wish(ground: Underground, day: int, seed: int, roots: RootSystem = null, res: Resources = null, far_want: float = -1.0) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "wish", day])
	var underground := rng.randf() < underground_share
	# (Drawn so the stream stays as it was: it picked the day wish before 0.8.2.5.)
	rng.randi()
	var coin := rng.randi() % 2
	if underground:
		var sys := roots if roots != null else RootSystem.new()
		var kind := wish_kind(sys, res, coin)
		var size := Underground.wish_size(kind, rng, ground.layout)
		var tip := newest_tip(sys)
		# The far draw (layout 3): a separate stream, so the near wishes of the wider field stay
		# as they would be. 0.8.2: a running share when the diary passes one (far_want).
		var go_far := false
		var far_rng := RandomNumberGenerator.new()
		if ground.layout >= 3 and day >= FAR_FROM_DAY:
			far_rng.seed = hash([seed, "far_wish", day])
			var roll := far_rng.randf()
			go_far = roll < far_share if far_want < 0.0 else far_want >= lerpf(0.25, 0.75, roll) and far_share > 0.0
		# An earlier wish deposit of the same kind, missed and still untouched, lies ahead: the
		# wish points at it again instead of adding another (0.8, B4: missed deposits piled up
		# to 20 fresh patches in reach by the month's last week). A missed far one only when the
		# far draw allows it (0.8.2, so a far wish ignored for days does not tip the share).
		var again := missed_ahead(ground, sys, kind, sys.graph.positions[tip], go_far or far_want < 0.0)
		if again >= 0:
			var p: Dictionary = ground.patches[again]
			return {"text": wish_text_at(kind, p["center"]), "kind": kind, "patch": again,
				"center": p["center"], "radius": p["radius"], "from": tip}
		# Enough missed deposits lie waiting already, one of them of this kind: the wish points at
		# the nearest of them instead of adding one more (0.8.2.5: was a day wish).
		if untouched_wishes(ground, sys) >= MISSED_MAX and untouched_wishes(ground, sys, kind) > 0:
			var waiting := fallback_patch(ground, sys, kind)
			if waiting >= 0:
				return _again(ground, waiting, tip, true)
		var center := Vector3.INF
		if go_far:
			center = place_far(ground, sys, sys.graph.positions[tip], float(size["radius"]), far_rng)
		var far := center != Vector3.INF
		if center == Vector3.INF:
			center = place_ahead(ground, sys, sys.graph.positions[tip], float(size["radius"]), rng)
		if center != Vector3.INF:
			return {"text": wish_text_at(kind, center), "kind": kind, "center": center,
				"radius": size["radius"], "count": size["count"], "from": tip, "far": far}
		# No new place in reach (rock all round, the field's rim): a deposit already in the soil.
		var other := fallback_patch(ground, sys, kind)
		if other >= 0:
			return _again(ground, other, tip, true)
	return {"text": "", "kind": -1}


## A wish for a deposit already in the soil. `fallback`: chosen because no new one could be
## placed (it does not count in the running far share, which is about new wishes).
static func _again(ground: Underground, pid: int, tip: int, fallback: bool = false) -> Dictionary:
	var p: Dictionary = ground.patches[pid]
	return {"text": wish_text_at(int(p["kind"])), "kind": int(p["kind"]), "patch": pid,
		"center": p["center"], "radius": p["radius"], "from": tip, "fallback": fallback}


## A deposit to wish for when no new one can be placed (0.8.2.5: every morning has a wish place):
## the nearest wish deposit of `kind` no root found yet, else of any kind, else the nearest
## deposit of `kind` with most of its dots still fresh, else of any kind; never the starter patch
## at the trunk, never deeper than the meadow shows. -1 only when the soil holds none.
static func fallback_patch(ground: Underground, sys: RootSystem, kind: int) -> int:
	var from := sys.graph.positions[newest_tip(sys)]
	var wishes := ground.wish_patch_ids()
	for want in [kind, -1]:
		var best := _nearest(ground, from, func(pid: int) -> bool:
			return wishes.has(pid) and (want < 0 or int(ground.patches[pid]["kind"]) == want) and _untouched(ground, sys, pid))
		if best >= 0:
			return best
	for want in [kind, -1]:
		var best := _nearest(ground, from, func(pid: int) -> bool:
			return (want < 0 or int(ground.patches[pid]["kind"]) == want) and _fresh(ground, sys, pid))
		if best >= 0:
			return best
	return -1


static func _nearest(ground: Underground, from: Vector3, ok: Callable) -> int:
	var best := -1
	var best_d := INF
	for pid in range(ground.patches.size()):
		var c: Vector3 = ground.patches[pid]["center"]
		if Vector2(c.x, c.z).length() < 2.0 or -c.y > Underground.HINT_MAX_DEPTH:
			continue
		var d := from.distance_to(c)
		if d < best_d and ok.call(pid):
			best = pid
			best_d = d
	return best


## Most of a deposit's dots wait fresh (any deposit, not only a wish deposit).
static func _fresh(ground: Underground, sys: RootSystem, pid: int) -> bool:
	var dots := ground.patch_dots(pid)
	var fresh := 0
	for i in dots:
		if sys.is_fresh(i) and ground.dot_collected[i] == 0:
			fresh += 1
	return dots.size() > 0 and fresh >= UNTOUCHED_SHARE * dots.size()


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
static func missed_ahead(ground: Underground, sys: RootSystem, kind: int, from: Vector3, allow_far: bool = true) -> int:
	var best := -1
	var best_d := INF
	var tank := calm_reach(sys)
	for pid in ground.wish_patch_ids():
		var p: Dictionary = ground.patches[pid]
		if int(p["kind"]) != kind or not allow_far and bool(p.get("far", false)):
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
	var tank := calm_reach(sys)
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
	var tank := calm_reach(sys) * FAR_REACH_NIGHTS
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


## 0.8.2.5: the plant only, no direction; the meadow shows the place, the compass points to it.
static func wish_text_at(kind: int, _center: Vector3 = Vector3.ZERO) -> String:
	match kind:
		Resources.Kind.WATER:
			return "Today, reach the damp patch under the rushes."
		Resources.Kind.PHOSPHORUS:
			return "Today, find what feeds the nettles."
		Resources.Kind.POTASSIUM:
			return "Today, reach the deep soil under the comfrey."
	return "Today, find what feeds the clover."


## The plants over the deposits, for the day page's short lines.
const PLANTS: Array[String] = ["rushes", "clover", "nettles", "comfrey"]


## The day page's wish line (0.8.2): short. `far`: a far wish says so (0.8.2 first-time check:
## nothing told the player that this one may take a root continued over two nights). 0.8.2.5: no
## direction ("Wish: the clover."); the meadow shows the place, the compass points to it.
static func wish_entry(kind: int, _center: Vector3 = Vector3.ZERO, kept: bool = false, far: bool = false) -> String:
	if kind < 0:
		return ""
	var line := ("Still: the %s, far away." if kept else "Wish: the %s, far away.") if far else ("Still: the %s." if kept else "Wish: the %s.")
	return line % PLANTS[kind]


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
	var tank := calm_reach(sys)
	for start in nearest_starts(sys, c, 6):
		var to := c - start
		if to.length() <= r:
			return true
		var goal := c - to.normalized() * r * 0.6
		if line_cost(ground, sys, start, goal) <= tank:
			return true
	return false


## The line cost a wish may ask of one calm night: REACH_SHARE of a calm tank, in the old soil's
## metres (0.8.2: the wider field's dearer metre, RootSystem.base_cost_wide, would otherwise
## shrink it to about 8 m, below the near wish's 5.5 to 8.5 m from a deep tip, and most near
## wishes became day wishes; a calm night still drives about 10 m straight from its start).
static func calm_reach(sys: RootSystem) -> float:
	return sys.calm_life_force * REACH_SHARE * sys.base_cost_per_metre / RootSystem.BASE_COST_OLD


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
		var since := wish_since if wish_since >= 0 else int(ground.patches[wish_patch].get("day", day))
		if day - since < FAR_DAYS and _untouched(ground, roots, wish_patch):
			last_patch = -1
			var p: Dictionary = ground.patches[wish_patch]
			add(day, wish_entry(int(p["kind"]), p["center"], true, true), "tree", DRAWINGS[int(p["kind"])], "wish")
			wish_days += 1
			far_days += 1
			return
	last_patch = wish_patch if wish_patch >= 0 and not wish_reached else -1
	wish_reached = false
	wish_patch = -1
	var w := plan_wish(ground, day, seed, roots, res, far_share * (wish_days + 1) - far_days)
	wish_since = day
	var fallback := bool(w.get("fallback", false))
	if w.has("patch"):
		wish_patch = int(w["patch"])
	elif int(w["kind"]) >= 0:
		wish_patch = ground.add_wish_deposit(day, int(w["kind"]), w["center"], float(w["radius"]), int(w["count"]), bool(w.get("far", false)))
		# The soil's dot budget is full: a deposit already there (0.8.2.5: never a day wish).
		if wish_patch < 0:
			wish_patch = fallback_patch(ground, roots, int(w["kind"]))
			fallback = true
	wish = wish_text(ground, wish_patch) if wish_patch >= 0 else ""
	if wish_patch >= 0:
		last_patch = -1
		var p: Dictionary = ground.patches[wish_patch]
		add(day, wish_entry(int(p["kind"]), p["center"], false, is_far(ground, wish_patch)), "tree", DRAWINGS[int(p["kind"])], "wish")
		# The running far share counts from FAR_FROM_DAY (no far wish before it to balance), and
		# not a fallback (0.8.2.5: those mornings were day wishes before).
		if fallback:
			return
		if day >= FAR_FROM_DAY or ground.layout < 3:
			wish_days += 1
		if is_far(ground, wish_patch):
			far_days += 1


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
	return {"entries": entries, "wish": wish, "wish_patch": wish_patch, "last_patch": last_patch, "wish_reached": wish_reached,
		"wish_days": wish_days, "far_days": far_days, "wish_since": wish_since}


static func from_dict(d: Dictionary) -> Diary:
	var diary := Diary.new()
	for e in d.get("entries", []):
		diary.add(int(e.get("day", 0)), str(e.get("text", "")), str(e.get("by", "tree")), str(e.get("drawing", "")), str(e.get("topic", "")))
	diary.wish = str(d.get("wish", ""))
	diary.wish_patch = int(d.get("wish_patch", -1))
	diary.last_patch = int(d.get("last_patch", -1))
	diary.wish_reached = bool(d.get("wish_reached", false))
	diary.wish_days = int(d.get("wish_days", 0))
	diary.far_days = int(d.get("far_days", 0))
	diary.wish_since = int(d.get("wish_since", -1))
	return diary
