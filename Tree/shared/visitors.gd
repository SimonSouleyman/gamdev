class_name Visitors
extends RefCounted
## Visitors as milestones (design doc, game feel): the tree earns company as it grows. Each
## visitor comes once, gets a diary line, and the nest stays visible. Cosmetic.
## Checked each morning; remembered in the save with the seen pages ("visitor_<id>").

const LINES := {
	"butterflies": "The first butterflies came to the leaves.",
	"nest": "Blackbirds are nesting in the crown.",
	"fox": "A fox slept in my shade this morning.",
}


## The visitors due this morning that have not come yet; marks them and writes their lines.
static func arrive(state: GameState) -> Array[String]:
	var due: Array[String] = []
	var h := state.sim.height()
	var day := state.day_number()
	if state.sim.tip_count() > 3:
		due.append("butterflies")
	if h >= 8.0:
		due.append("nest")
	if day >= 12 and h >= 10.0:
		due.append("fox")
	var out: Array[String] = []
	for id in due:
		if state.first_time("visitor_" + id):
			state.diary.add(day, LINES[id], "tree", id, "visitor")
			out.append(id)
	return out


static func has_come(state: GameState, id: String) -> bool:
	return state.seen_pages.has("visitor_" + id)
