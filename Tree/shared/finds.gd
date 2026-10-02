class_name Finds
extends RefCounted
## The garden's finds (0.8.2.6, specs/journal-drawers-loop.md D1, D2): every thing the roots
## reached, kept for good in the workbench's drawers, across trees (they belong to the garden,
## like the album). A map scrap or a drain-pipe shard may give one hint for the next night
## (a far patch, a gap in a rock band), at most one a week; a find never adds growth, life force
## or nutrients. Pure data; the shed lays them out (Shed.fill_drawers), the journal lists them.

## The kinds, in the order they lie in the drawers.
const KINDS: Array[String] = ["fossil", "old_root", "coin", "water_vein", "map_scrap", "shard"]
## Which drawer of the bench each kind lies in (layout 08 shows the upper row: drawer01 left,
## drawer02 right): the soil's own things on the left, the people's things on the right.
const DRAWER := {"fossil": "drawer01", "old_root": "drawer01", "coin": "drawer01", "water_vein": "drawer01",
	"map_scrap": "drawer02", "shard": "drawer02"}
const DRAWER_NAMES := {"drawer01": "the bench's left drawer", "drawer02": "the bench's right drawer"}
## A find's one line (the note shown when it is tapped in its drawer).
const NOTES := {
	"fossil": "A fossil shell pressed into a stone.",
	"old_root": "An old root of a tree that stood here long before.",
	"coin": "A lost coin, green with age.",
	"water_vein": "A stone with a water vein; it hums very quietly.",
	"map_scrap": "A scrap of an old garden map, a patch marked in pencil.",
	"shard": "A shard of a clay drain pipe; water once found its way here.",
}
## Kinds that may give a hint for the next night, and what it shows.
const HINTS := {"map_scrap": "patch", "shard": "gap"}
## The find page's last sentence when a find gave a hint.
const HINT_PAGE := {"patch": "Tomorrow night I may see the patch it marks.",
	"gap": "Tomorrow night I may see the way through the stone."}
## At most one hint in this many days (the same tree; a new tree may have one at once).
const HINT_EVERY_DAYS := 7
## A patch counts as known (no map needed) when a root came this near it (RootView.known_reach).
const KNOWN_REACH := 8.0

## Each: {"kind": String, "day": int (the night it was found), "species": String (the tree it was
## found under), "tree": int (that tree's number, 1 for the first), "hint": bool (it gave a hint)}.
var items: Array = []
## The next night's hint: {} or {"kind": "patch"|"gap", "night": int (the day number whose night
## shows it), "pos": Vector3, "radius": float, "from": String (the find's kind)}.
var hint: Dictionary = {}
## The last hint given: the tree's number and the day.
var last_hint_tree: int = -1
var last_hint_day: int = -1000


## Every find, oldest first, for the journal's Collection and the drawers: each
## {"kind", "day", "species", "tree", "drawer" (key), "drawer_name", "note"}.
func list() -> Array:
	var out: Array = []
	for f in items:
		var kind := str(f["kind"])
		var d := drawer_for(kind)
		out.append({"kind": kind, "day": int(f["day"]), "species": str(f["species"]), "tree": int(f["tree"]),
			"drawer": d, "drawer_name": str(DRAWER_NAMES.get(d, "the bench's drawer")), "note": note(kind)})
	return out


func count() -> int:
	return items.size()


static func drawer_for(kind: String) -> String:
	return str(DRAWER.get(kind, "drawer01"))


static func note(kind: String) -> String:
	return str(NOTES.get(kind, "Something the roots found."))


## The second line of a find's note: when and under which tree.
static func when_line(f: Dictionary) -> String:
	var sp := Species.from_id(str(f.get("species", "linden")))
	var tree := sp.display_name.to_lower() if sp != null else str(f.get("species", ""))
	if int(f.get("day", 0)) <= 0:
		return "Found under the %s." % tree
	return "Found on night %d, under the %s." % [int(f["day"]), tree]


## Keeps a new find. `tree` is the tree's number (grove size + 1).
func add(kind: String, day: int, species: String, tree: int) -> Dictionary:
	var f := {"kind": kind, "day": day, "species": species, "tree": tree, "hint": false}
	items.append(f)
	return f


## May a find on this day give a hint (none in the last HINT_EVERY_DAYS days of this tree)?
func hint_allowed(tree: int, day: int) -> bool:
	return tree != last_hint_tree or day - last_hint_day >= HINT_EVERY_DAYS


## The hint for tonight's root view ({} when none): only on the night it was given for.
func hint_for(night: int) -> Dictionary:
	if hint.is_empty() or int(hint.get("night", -1)) != night:
		return {}
	return hint


## A map scrap or shard (`f`, the soil's find; `item`, its entry in items) found on `day` under
## tree `tree`: the next night's hint, if one is allowed
## this week and the soil has something to show (a far patch not known yet; a rock band's gap).
## Returns the hint ({} if none). Changes nothing in the soil or the tree.
func give_hint(f: Dictionary, item: Dictionary, ground: Underground, root_positions: PackedVector3Array, known: PackedInt32Array, tree: int, day: int) -> Dictionary:
	var what := str(HINTS.get(str(f["kind"]), ""))
	if what == "" or not hint_allowed(tree, day):
		return {}
	var at: Vector3 = f.get("position", Vector3.ZERO)
	var h := {}
	if what == "patch":
		h = far_patch_hint(ground, known, at)
	else:
		h = gap_hint(ground, at)
	if h.is_empty():
		return {}
	h["kind"] = what
	h["night"] = day + 1
	h["from"] = str(f["kind"])
	hint = h
	last_hint_tree = tree
	last_hint_day = day
	item["hint"] = true
	return h


## The nearest far patch (middle or far ring) to `at` that the far view does not know yet and that
## still holds most of what it had; failing that, the nearest untouched one.
static func far_patch_hint(ground: Underground, known: PackedInt32Array, at: Vector3) -> Dictionary:
	var best := -1
	var best_d := INF
	for pass_i in range(2):
		for pid in ground.far_patch_ids():
			if pass_i == 0 and known.has(pid):
				continue
			if ground.patch_amount(pid) < ground.patch_amount(pid, true) * 0.6:
				continue
			var c: Vector3 = ground.patches[pid]["center"]
			var d := Vector2(c.x - at.x, c.z - at.z).length()
			if d < best_d:
				best_d = d
				best = pid
		if best >= 0:
			break
	if best < 0:
		return {}
	var p: Dictionary = ground.patches[best]
	return {"pos": p["center"], "radius": float(p["radius"]), "patch": best}


## The gap of the rock band nearest to `at`: the middle of its open segments, at half the band's
## depth. {} in a soil without bands.
static func gap_hint(ground: Underground, at: Vector3) -> Dictionary:
	var best := {}
	var best_d := INF
	for bi in range(ground.bands.size()):
		var b: Dictionary = ground.bands[bi]
		var pts: PackedVector2Array = b["points"]
		var open: PackedByteArray = b["open"]
		var sum := Vector2.ZERO
		var n := 0
		for s in range(open.size()):
			if open[s] != 0:
				sum += (pts[s] + pts[s + 1]) * 0.5
				n += 1
		if n == 0:
			continue
		var g := sum / n
		var d := g.distance_to(Vector2(at.x, at.z))
		if d < best_d:
			best_d = d
			var depth := clampf(float(b["bottom"]) * 0.35, 0.6, 2.0)
			best = {"pos": Vector3(g.x, -depth, g.y), "radius": float(b["half"]) + 1.2, "band": bi}
	return best


# --- save -------------------------------------------------------------------

func to_dict() -> Dictionary:
	var rows: Array = []
	for f in items:
		rows.append([f["kind"], f["day"], f["species"], f["tree"], 1 if f.get("hint", false) else 0])
	var d := {"items": rows, "hint_tree": last_hint_tree, "hint_day": last_hint_day}
	if not hint.is_empty():
		var p: Vector3 = hint["pos"]
		d["hint"] = [hint["kind"], hint["night"], p.x, p.y, p.z, hint["radius"], hint.get("from", "")]
	return d


static func from_dict(d: Dictionary) -> Finds:
	var out := Finds.new()
	for r in d.get("items", []):
		if r is Array and (r as Array).size() >= 4 and KINDS.has(str(r[0])):
			out.items.append({"kind": str(r[0]), "day": int(r[1]), "species": str(r[2]), "tree": int(r[3]),
				"hint": (r as Array).size() >= 5 and int(r[4]) != 0})
	out.last_hint_tree = int(d.get("hint_tree", -1))
	out.last_hint_day = int(d.get("hint_day", -1000))
	var h: Variant = d.get("hint")
	if h is Array and (h as Array).size() >= 6:
		out.hint = {"kind": str(h[0]), "night": int(h[1]), "pos": Vector3(h[2], h[3], h[4]), "radius": float(h[5]),
			"from": str(h[6]) if (h as Array).size() >= 7 else ""}
	return out


## A save from before 0.8.2.6 has no finds of its own: the ones its soil marks as found go into
## the drawers, each with the night its diary line names (0 when there is none).
static func from_old_save(ground: Underground, diary: Diary, species: String, tree: int) -> Finds:
	var out := Finds.new()
	for f in ground.finds:
		if not bool(f["found"]):
			continue
		var kind := str(f["kind"])
		var day := 0
		for e in diary.entries:
			if str(e.get("drawing", "")) == kind or str(e.get("text", "")).to_lower().contains(kind.replace("_", " ")):
				day = int(e.get("day", 0))
				break
		out.add(kind, day, species, tree)
	return out
