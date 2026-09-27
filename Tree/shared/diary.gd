class_name Diary
extends RefCounted
## The journal's diary: the game writes one or more lines per day, the player can add notes,
## and each morning brings an optional wish (design doc section 8). Pure data.

## Each: {"day": int, "text": String, "by": "tree" | "player"}
var entries: Array = []
var wish: String = ""


func add(day: int, text: String, by: String = "tree") -> void:
	entries.append({"day": day, "text": text, "by": by})


func lines_for_day(day: int) -> Array:
	var out: Array = []
	for e in entries:
		if int(e["day"]) == day:
			out.append(e)
	return out


func last(n: int) -> Array:
	return entries.slice(maxi(0, entries.size() - n))


## An optional wish for the morning, pointing at something the meadow shows.
## Deterministic from the save seed and the day.
static func make_wish(ground: Underground, day: int, seed: int) -> String:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "wish", day])
	var options: Array[String] = []
	for patch in ground.patches:
		var c: Vector3 = patch["center"]
		if -c.y > Underground.HINT_MAX_DEPTH or Vector2(c.x, c.z).length() < 2.0:
			continue
		if _patch_left(ground, patch) < 4:
			continue
		var where := Underground.compass(c)
		match int(patch["kind"]):
			Resources.Kind.WATER:
				options.append("Today, reach the damp patch with the rushes in the %s." % where)
			Resources.Kind.NITROGEN:
				options.append("Today, find what feeds the clover in the %s." % where)
	options.append("Today, grow the crown toward the morning sun: boost early, when it stands in the east.")
	options.append("Today, look at the tree from every side before the sun sets.")
	options.append("Today, let the sun shine calmly and save life force for a long root.")
	return options[rng.randi_range(0, options.size() - 1)]


static func _patch_left(ground: Underground, patch: Dictionary) -> int:
	return ground.dots_near(patch["center"], float(patch["radius"]) + 0.2).size()


func to_dict() -> Dictionary:
	return {"entries": entries, "wish": wish}


static func from_dict(d: Dictionary) -> Diary:
	var diary := Diary.new()
	for e in d.get("entries", []):
		diary.entries.append({"day": int(e.get("day", 0)), "text": str(e.get("text", "")), "by": str(e.get("by", "tree"))})
	diary.wish = str(d.get("wish", ""))
	return diary
