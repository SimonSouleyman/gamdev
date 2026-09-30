class_name BrushPile
extends RefCounted
## The brush pile (0.8, specs/0.8.md section 4): the branches the player cuts no longer sink
## away for good. At the next sunrise they lie on a low pile of sticks at the clearing edge, on
## the side away from the shed. Once enough wood has gathered, a hedgehog moves in a few days
## later and is seen at dusk, snuffling out of the pile and back; later a wren may sing from it
## by day. Mood only: nothing here touches growth, life force or nutrients. Fresh for each tree
## (the visitors count per tree). Pure data; tree/brush_pile_view.gd draws it.
## Numbers and reasons: docs/notes/shapes-brush-0.8.md.

## Cut segments on the pile before the hedgehog takes to it (about a week of ordinary pruning;
## a cut a day is some 25 segments).
const HEDGEHOG_WOOD: int = 40
## Sunrises after that before it has moved in (a surprise, not a reward for the cut).
const MOVE_IN_DAYS: int = 2
## Share of the evenings it shows once it lives there.
const HEDGEHOG_CHANCE: float = 0.5
## From 25 October it comes out less (getting ready to hibernate) ...
const LATE_CHANCE: float = 0.2
const LATE_FROM: Array[int] = [10, 25]
## ... and not at all in the late autumn season (20 November until spring, Almanac).
## It comes out in the last part of the daylight (about 19:00 on the game's clock) and the dusk
## hold after it.
const DUSK_FROM: float = 0.93
## The wren: the later, smaller visitor, singing from the pile by day.
const WREN_WOOD: int = 80
const WREN_CHANCE: float = 0.35
const WREN_FROM: float = 0.25
const WREN_TO: float = 0.6
## Sticks drawn (one merged mesh): a few for the first cut, one more for every few segments.
const MAX_STICKS: int = 40
const FIRST_STICKS: int = 4
const SEGMENTS_PER_STICK: int = 3
## Where the pile lies (0.8 review): on the far clearing edge the game's camera looks at (it
## starts north of the tree, looking south, where the hidden shed stands at +z), 16 degrees
## west of the shed, so it sits inside the phone's narrow default framing beside the tree and
## still clear of the shed; this far inside the edge (the camera's orbit stays further in).
const BEARING: Vector2 = Vector2(-0.276, 0.961)
const EDGE_INSET: float = 1.6
## The pile's reach from its centre when full, along the edge and across it (metres).
const HALF_LENGTH: float = 1.5
const HALF_DEPTH: float = 0.65
## The edge's herbs, flowers and shrubs keep this far from the pile's centre (Scenery).
const CLEAR_RADIUS: float = 1.9
## The hedgehog's run (0.8 review): from the pile across the meadow to the tree's foot (a
## hedgehog forages for beetles under a tree), ending beside the trunk on the pile's side, this
## far out to the side, so the trunk never hides it from the camera; at most RUN_MAX long; this
## wide each side. Tall plants keep off it and the grass on it is trodden short, so the hedgehog
## is seen on its evening walk where the camera looks. The pile lies 25 to 30 m from the camera,
## where a 30 cm hedgehog is a few pixels; at the tree's foot it is as big as a true size allows.
const RUN_END_BESIDE_TRUNK: float = 2.5
const RUN_MAX: float = 24.0
const RUN_HALF_WIDTH: float = 0.9

## Segments cut since the last sunrise (lying in the grass until then).
var fallen: int = 0
## Segments on the pile.
var wood: int = 0
## The day the pile first held enough wood for the hedgehog; -1 until then.
var enough_day: int = -1
## The last day the hedgehog came out and the wren sang; -1 for never.
var hedgehog_day: int = -1
var wren_day: int = -1


## A cut branch fell into the grass (the tree's own cuts only, never the bonsai's).
func add_cut(segments: int) -> void:
	fallen += maxi(0, segments)


## Sunrise: yesterday's cuttings go onto the pile. True the first morning the pile is there.
func sunrise(day: int) -> bool:
	if fallen <= 0:
		return false
	var first := wood == 0
	wood += fallen
	fallen = 0
	if enough_day < 0 and wood >= HEDGEHOG_WOOD:
		enough_day = day
	return first


func stick_count() -> int:
	if wood <= 0:
		return 0
	return mini(MAX_STICKS, FIRST_STICKS + wood / SEGMENTS_PER_STICK)


## The pile's size 0..1 (its share of the most sticks drawn).
func fullness() -> float:
	return float(stick_count()) / MAX_STICKS


func hedgehog_home(day: int) -> bool:
	return enough_day >= 0 and day >= enough_day + MOVE_IN_DAYS


## How likely the hedgehog is out on an evening of this date ({"month", "day"}).
static func hedgehog_chance(date: Dictionary) -> float:
	var month := int(date.get("month", 7))
	var d := int(date.get("day", 1))
	if Almanac.season_for(month, d) == Almanac.Season.LATE_AUTUMN:
		return 0.0
	if Almanac.day_of_year(month, d) >= Almanac.day_of_year(LATE_FROM[0], LATE_FROM[1]):
		return LATE_CHANCE
	return HEDGEHOG_CHANCE


## The hedgehog comes out now: it lives in the pile, it is dusk (`share` of the daylight gone,
## 1 at the sunset hold), it has not been out today, and today's roll says so.
func hedgehog_due(day: int, share: float, date: Dictionary, seed: int) -> bool:
	if not hedgehog_home(day) or hedgehog_day == day or share < DUSK_FROM:
		return false
	return roll(seed, day, "hedgehog") < hedgehog_chance(date)


## The wren sings now: enough wood, by day, not yet today, and today's roll says so.
func wren_due(day: int, share: float, seed: int) -> bool:
	if wood < WREN_WOOD or wren_day == day or share < WREN_FROM or share > WREN_TO:
		return false
	return roll(seed, day, "wren") < WREN_CHANCE


## The day's chance for a visitor, 0..1, from the save seed (the same day always rolls the same).
static func roll(seed: int, day: int, what: String) -> float:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "brush", what, day])
	return rng.randf()


## The pile's centre on the ground plane for a clearing of `radius` metres (y = 0).
static func position(radius: float) -> Vector3:
	var b := BEARING.normalized() * (radius - EDGE_INSET)
	return Vector3(b.x, 0.0, b.y)


## Where the run ends: beside the trunk on the pile's side (y = the ground's z).
static func run_end(radius: float) -> Vector2:
	var c := position(radius)
	var to := Vector2(signf(c.x) * RUN_END_BESIDE_TRUNK, 0.0) - Vector2(c.x, c.z)
	return Vector2(c.x, c.z) + to.limit_length(RUN_MAX)


## The run's direction (unit, on the ground plane) and length from the pile's middle.
static func run_dir(radius: float) -> Vector2:
	var c := position(radius)
	return (run_end(radius) - Vector2(c.x, c.z)).normalized()


static func run_length(radius: float) -> float:
	var c := position(radius)
	return run_end(radius).distance_to(Vector2(c.x, c.z))


## How much a ground point lies on the hedgehog's run for a clearing of `radius` metres: 1 on
## it, fading to 0 over half a metre beside it and at its inner end. Includes the pile's spot.
static func on_run(p: Vector2, radius: float) -> float:
	var c3 := position(radius)
	var c := Vector2(c3.x, c3.z)
	var inward := run_dir(radius)
	var rel := p - c
	var along := rel.dot(inward)
	var across := absf(rel.dot(Vector2(-inward.y, inward.x)))
	if along < 0.0:
		# Behind the pile's middle: only the pile's own spot.
		return 1.0 - smoothstep(CLEAR_RADIUS - 0.5, CLEAR_RADIUS, rel.length())
	var side := 1.0 - smoothstep(RUN_HALF_WIDTH, RUN_HALF_WIDTH + 0.5, across)
	var length := run_length(radius)
	var end := 1.0 - smoothstep(length - 0.5, length, along)
	return maxf(side * end, 1.0 - smoothstep(CLEAR_RADIUS - 0.5, CLEAR_RADIUS, rel.length()))


func to_dict() -> Dictionary:
	return {"fallen": fallen, "wood": wood, "enough_day": enough_day, "hedgehog_day": hedgehog_day, "wren_day": wren_day}


static func from_dict(d: Dictionary) -> BrushPile:
	var b := BrushPile.new()
	b.fallen = int(d.get("fallen", 0))
	b.wood = int(d.get("wood", 0))
	b.enough_day = int(d.get("enough_day", -1))
	b.hedgehog_day = int(d.get("hedgehog_day", -1))
	b.wren_day = int(d.get("wren_day", -1))
	return b
