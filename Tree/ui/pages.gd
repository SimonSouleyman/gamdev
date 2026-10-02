class_name Pages
extends RefCounted
## Journal page texts. The tutorial lives here: no explanation outside the journal (design doc section 9).

## 0.8.2 (specs/0.8.md, items 21 to 24): each explanation page says only what the player needs
## to act, about a third of its 0.8 length, and carries one ink doodle about its topic (DOODLES).

## The night's "end the root" button: its name in the page texts ({end}) and its two-line label on
## the scrap (roots/root_view.gd). One place, so a new name is a one-line change (0.8.2.4, Simon:
## renamed from "End root here").
const END_ROOT_NAME := "Let roots spread"
const END_ROOT_LABEL := "let roots\nspread"

const TEXTS: Dictionary = {
	"planted": ["Day 0", "A seed went in at sunset. Tap the ground to grow its first root."],
	"first_night": ["Below the meadow", "Glowing dots feed the tree: blue dots are water, green nitrogen, orange phosphorus, violet potassium.\n\nSteer with the stick (WASD, arrows); \"dive\" (space) sinks faster. Each metre costs life force. \"{end}\" (E) spends the rest on fine roots."],
	"first_run_done": ["The first root", "The root drank what it touched; the seed grows at sunrise."],
	"sapling": ["A sapling", "Tap: an hour of brighter sun. Faster growth, less life force for tonight.\n\nMorning boosts grow it east, noon tall, evening west; the arc shows the sun. The pills: life force and nutrients.\n\nHold a still finger: the day runs at 4x. Drag to walk, pinch to zoom."],
	"spent": ["Nothing left to grow with", "The roots' haul is used up. The leaves still gather life force."],
	"first_sunset": ["Sunset", "Tap the ground or swipe up to dive to the roots.\n\nThe meadow hints below: rushes water, clover nitrogen, nettles phosphorus, stones rock, comfrey a potassium wish."],
	"pick": ["A new root", "Tonight a root can start anywhere on the old roots: tap one."],
	"shears": ["The shears", "Touch a branch to outline what would fall; slide to choose, lift to cut.\n\nCutting is free; the tree grows into your shape. Tap the shears to put them away."],
	"finished": ["A grown tree", "Fully grown; it dropped a seed. The shed's seed bag holds the next kind. No hurry."],
	"bonsai": ["A bonsai on the windowsill", "A juniper to keep as long as I like; forgetting it harms nothing.\n\nThe window side grows: turn the pot now and then. Water when the soil is pale, pellets when it hungers, a new pot weekly. Shape it with shears, tweezers and wire."],
	"bonsai_water": ["Watering", "Water when the soil looks pale. Dry, it droops; it never dies."],
	"bonsai_fertiliser": ["Pellets", "The scarcest of N, P and K sets the pace. Too much browns tips."],
	"bonsai_shears": ["Shaping", "Cuts make it denser, not bigger: a third at most. Cut branches stay as silver jin or shari."],
	"bonsai_pinch": ["Pinching", "Pinch a tip from today or yesterday: the pad fills in."],
	"bonsai_wire": ["Wire", "Wire sets a branch's new line in four days; then take it off, or it scars."],
	"bonsai_repot": ["Repotting", "A week fills the pot. Snip circling roots, pick a pot (bigger carries more), fresh soil."],
	"bonsai_burn": ["Burnt tips", "Too many pellets browned a few tips. They grow on."],
	"empty_night": ["A quiet night", "No life force was left tonight. Calm days fill the tank."],
}

## The doodle on each page (InkSketch kinds); a species page shows its seed or leaf.
const DOODLES: Dictionary = {
	"planted": "seed", "first_night": "root", "first_run_done": "fine_roots", "sapling": "sapling",
	"spent": "leaf_sun", "first_sunset": "sunset", "pick": "root_fork", "shears": "shears",
	"finished": "grown_tree", "bonsai": "bonsai", "bonsai_water": "can", "bonsai_fertiliser": "tin",
	"bonsai_shears": "shears", "bonsai_pinch": "tweezers", "bonsai_wire": "wire",
	"bonsai_repot": "trowel", "bonsai_burn": "leaf_burnt", "empty_night": "moon",
}


## Each species has a page too ("species_<id>"): its look and its quirk (Species).
const SPECIES_PREFIX := "species_"


static func has(id: String) -> bool:
	return TEXTS.has(id) or (id.begins_with(SPECIES_PREFIX) and Species.ORDER.has(id.substr(SPECIES_PREFIX.length())))


## Every page id, the tutorial pages first, then the six species pages.
static func ids() -> Array[String]:
	var out: Array[String] = []
	for id in TEXTS:
		out.append(str(id))
	for sid in Species.ORDER:
		out.append(SPECIES_PREFIX + sid)
	return out


static func species_page(species_id: String) -> String:
	return SPECIES_PREFIX + species_id


static func title(id: String) -> String:
	if id.begins_with(SPECIES_PREFIX):
		return Species.from_id(id.substr(SPECIES_PREFIX.length())).page_title()
	return TEXTS[id][0]


static func body(id: String) -> String:
	if id.begins_with(SPECIES_PREFIX):
		return Species.from_id(id.substr(SPECIES_PREFIX.length())).page_body()
	return (TEXTS[id][1] as String).replace("{end}", END_ROOT_NAME)


## The ink doodle of a page (0.8.2, item 23).
static func doodle(id: String) -> String:
	if id.begins_with(SPECIES_PREFIX):
		return InkSketch.species_kind(id.substr(SPECIES_PREFIX.length()))
	return str(DOODLES.get(id, ""))
