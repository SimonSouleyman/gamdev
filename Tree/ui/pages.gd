class_name Pages
extends RefCounted
## Journal page texts. The tutorial lives here: no explanation outside the journal (design doc section 9).

const TEXTS: Dictionary = {
	"planted": ["Day 0", "I planted a seed in the clearing as the sun went down.\n\nA seed carries a little life force, enough for one first root. Tap the ground to follow it down."],
	"first_night": ["Below the meadow", "Down here it is dark, and the soil glows: blue is water, green nitrogen, orange phosphorus, violet potassium.\n\nSteer the root with the stick (on a keyboard: WASD or the arrow keys). It sinks slowly on its own; hold \"dive\" (or space) to go down faster. Rocks are hard, steer around them.\n\nEvery metre costs life force, more so far from the trunk and deep down. The root waits for your first move, then grows until the life force is spent. You can also end it early (\"end root here\" or E): the rest of the life force then goes into more fine roots around it."],
	"first_run_done": ["The first root", "The root drank what it touched, and fine roots reached for what lay close by.\n\nAt sunrise the seed will use it all to grow."],
	"sapling": ["A sapling", "The seed sprouted with the night's water and nutrients.\n\nTap anywhere and the sun shines brighter for an hour (tap again for more): the tree grows faster, but the leaves turn the light into less life force for tonight's root. The day runs on by itself.\n\nThe sun steers the crown: boosting in the morning grows it east, at noon south and taller, in the evening west. The small compass shows where east is. The arc at the top shows where the sun is: tap in the afternoon to grow west.\n\nThe pills at the top: the life force the leaves gather for tonight's root, and the water, nitrogen, phosphorus and potassium the roots brought.\n\nDrag to walk around the tree, pinch or scroll to zoom."],
	"spent": ["Nothing left to grow with", "The tree has used up what the roots brought. The leaves still gather life force for tonight's root while the day runs out."],
	"first_sunset": ["Sunset", "The day is over. Tap the ground to dive down to the roots.\n\nThe meadow hints at what lies below: rushes and a damp patch over water, clover over nitrogen, nettles over phosphorus, stones over rock."],
	"pick": ["A new root", "Tonight a root can start anywhere on the old roots, not only at a tip. Tap a point on a root to begin; drag to look around."],
	"shears": ["The shears", "With the shears on, touch a branch (or point at it with the mouse): a mark shows where it would be cut, and the part that would fall is outlined. Slide along the tree to choose, lift the finger (or click) to cut. Off the tree, nothing is cut.

Cutting is free. The tree puts its strength into the branches that are left, so the crown takes the shape you give it.

Tap the shears again to put them away; then a tap boosts the sun again."],
	"finished": ["A grown tree", "The tree has grown to its full size and dropped a seed.

In the garden shed, the seed bag on the workbench now offers the next seed: plant it and a new tree begins beside this one. Each finished tree brings a new kind of seed.

There is no hurry. This tree stays as long as you like."],
	"bonsai": ["A bonsai on the windowsill", "A young juniper in a clay nursery pot, for as long as I like. It lives through the same days as the tree outside but needs nothing from it, and nothing bad happens if I forget it for a while.

The window is its sun: the side facing the glass grows, the back stays sparse. Turn the pot a quarter now and then.

Water when the soil looks pale (wet soil is dark; too much only slows it), a spoon of pellets when it hungers. Every seventh day or so it asks to be repotted.

Shape it as I like with the shears, pinching and wire. The style pages show the classic shapes, only for inspiration."],
	"bonsai_water": ["Watering", "The soil dries over the day, faster in the sun. Dry, the leaves droop and it grows slowly; soaked, it sulks a little. It never dies of it."],
	"bonsai_fertiliser": ["Pellets", "Nitrogen, phosphorus and potassium, like the tree outside: the scarcest one sets the pace. A spoon too many burns a few leaf tips brown; they grow on after a few days."],
	"bonsai_shears": ["Shaping", "The pot keeps the bonsai small: it carries only so many twigs. Cutting one branch gives its strength to the buds inside, so the tree grows denser, not bigger. A third of it at most at once.

A juniper keeps a cut branch as silver deadwood (jin); a cut close to the trunk strips a line of bark below it (shari)."],
	"bonsai_pinch": ["Pinching", "A fresh tip, grown today or yesterday, can be pinched with the tweezers: it stops, and the buds behind it take its share. The pads grow dense and close."],
	"bonsai_wire": ["Wire", "Copper coiled round a branch holds it in a new line. Over about four days the branch sets and keeps it.

Take it off after that: left on too long, the wire bites into the bark, and the scar stays."],
	"bonsai_repot": ["Repotting", "The roots fill the pot in about a week. Lift the tree out, snip the long roots circling the root ball, choose a pot (a bigger one carries more twigs), and give it fresh soil."],
	"bonsai_burn": ["Burnt tips", "Too many pellets of one kind: a few leaf tips turned brown. They rest a few days, then grow on."],
	"empty_night": ["A quiet night", "There was no life force left for a root tonight, so I only looked around below.\n\nCalm days, without boosting, fill the tank for the night."],
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
	return TEXTS[id][1]
