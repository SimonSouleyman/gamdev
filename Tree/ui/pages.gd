class_name Pages
extends RefCounted
## Journal page texts. The tutorial lives here: no explanation outside the journal (design doc section 9).

const TEXTS: Dictionary = {
	"planted": ["Day 0", "I planted a linden seed in the clearing as the sun went down.\n\nA seed carries a little life force, enough for one first root. Tap the ground to follow it down."],
	"first_night": ["Below the meadow", "Down here it is dark, and the soil glows: blue is water, green nitrogen, orange phosphorus, violet potassium.\n\nSteer the root with the stick (on a keyboard: WASD or the arrow keys). It sinks slowly on its own; hold \"dive\" (or space) to go down faster. Rocks are hard, steer around them.\n\nEvery metre costs life force, more so far from the trunk and deep down. The root waits for your first move, then grows until the life force is spent. You can also end it early (\"end root here\" or E): the rest of the life force then goes into more fine roots around it."],
	"first_run_done": ["The first root", "The root drank what it touched, and fine roots reached for what lay close by.\n\nAt sunrise the seed will use it all to grow."],
	"sapling": ["A sapling", "The seed sprouted with the night's water and nutrients.\n\nTap anywhere and the sun shines brighter for an hour (tap again for more): the tree grows faster, but the leaves turn the light into less life force for tonight's root. The day runs on by itself.\n\nThe sun steers the crown: boosting in the morning grows it east, at noon south and taller, in the evening west. The small compass shows where east is. The arc at the top shows where the sun is: tap in the afternoon to grow west.\n\nThe pills at the top: the life force the leaves gather for tonight's root, and the water, nitrogen, phosphorus and potassium the roots brought.\n\nDrag to walk around the tree, pinch or scroll to zoom."],
	"spent": ["Nothing left to grow with", "The tree has used up what the roots brought. The leaves still gather life force for tonight's root while the day runs out."],
	"first_sunset": ["Sunset", "The day is over. Tap the ground to dive down to the roots.\n\nThe meadow hints at what lies below: rushes and a damp patch over water, clover and nettles over nitrogen, stones over rock."],
	"pick": ["A new root", "Tonight a root can start anywhere on the old roots, not only at a tip. Tap a point on a root to begin; drag to look around."],
	"empty_night": ["A quiet night", "There was no life force left for a root tonight, so I only looked around below.\n\nCalm days, without boosting, fill the tank for the night."],
}


static func title(id: String) -> String:
	return TEXTS[id][0]


static func body(id: String) -> String:
	return TEXTS[id][1]
