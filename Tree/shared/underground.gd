class_name Underground
extends RefCounted
## The seeded underground: nutrient dots, rocks and finds, plus the surface hints that
## reveal them on the meadow. One generator, one seed (design doc sections 5, 6, 3).
## Coordinates: +X east, +Z south, -Z north, y < 0 below the meadow. Pure data.

## Horizontal radius of the generated volume, metres around the trunk, in the soils before 0.8.1
## (layouts 1 and 2). An instance's own radius is `extent`.
const EXTENT: float = 14.0
## Layout 3 (0.8.1, item 29): the wider root field. A static var so tools can try another radius.
static var field_extent: float = 30.0
## Deepest point of the volume.
const DEPTH: float = 10.0
## Topsoil holds most N and P in rich patches.
const TOPSOIL: float = 2.5
## Cell size of the spatial hash for dot queries.
const CELL: float = 1.0
## Touch distance for a find.
const FIND_RADIUS: float = 0.7
## A surface hint is placed only above patches shallower than this.
const HINT_MAX_DEPTH: float = 2.2

const FIND_TEXTS: Dictionary = {
	"fossil": "a fossil shell pressed into a stone",
	"old_root": "an old root of a tree that stood here long before",
	"water_vein": "a water vein that hums very quietly",
	"coin": "a lost coin, green with age",
}

var seed: int = 1
var dot_positions: PackedVector3Array = PackedVector3Array()
var dot_kinds: PackedInt32Array = PackedInt32Array()
## Each dot is a deposit (Simon, play test 3): `dot_capacity` is what it held at the start,
## `dot_amounts` what is left. A root draws one share per contact; roots that reached a dot
## keep drawing from it night after night (RootSystem.drink_tapped) until it is empty.
var dot_amounts: PackedFloat32Array = PackedFloat32Array()
var dot_capacity: PackedFloat32Array = PackedFloat32Array()
## How many first-contact shares a deposit holds.
const DEPOSIT_SHARES: float = 1.15
## 0.8.2 (the dearer metre): in the wider field (layout 3) every dot holds this much instead, so
## the fewer deposits a night's dearer root reaches still keep a steered tree in its month (a
## steered linden finished on day 32 to 35 at 1.15). Older soils keep DEPOSIT_SHARES; a 0.8.1 save
## in the wider field gets the bigger deposits on load (their capacity is not saved).
static var deposit_shares_wide: float = 1.5
var deposit_shares: float = DEPOSIT_SHARES
## The new root's first contact takes this much, so a well-steered night pays off that night
## (QA round 2: at 1/4 the old roots drank more than the new one).
const FIRST_SHARE: float = 0.2
var dot_collected: PackedByteArray = PackedByteArray()
var rock_centers: PackedVector3Array = PackedVector3Array()
var rock_radii: PackedFloat32Array = PackedFloat32Array()
## Each: {"kind": String, "position": Vector3, "found": bool}
var finds: Array = []
## Rich patches, kept for the surface hints and the daily wish.
## Each: {"kind": int, "center": Vector3, "radius": float, "first": int, "end": int (dot ids
## first..end-1), "wish": bool (a wish deposit)}
var patches: Array = []

## Wish deposits (0.7): on a day whose wish points underground, the generator places one rich
## topsoil patch about WISH_SIZE times a normal one, a few metres beyond the newest root tip, so
## continuing tonight's root from there reaches it (Diary.plan_wish). Seeded from the save seed
## and the day; its dots come after all older ids, so saves keep their deposit state.
## Each placed: {"day": int, "kind": int, "center": Vector3, "radius": float, "count": int, "far": bool
## (0.8.1: a far wish, Diary.place_far), "reached": bool (0.8.1: a root reached its glow)}.
var wish_deposits: Array = []
const WISH_SIZE: float = 1.5
const WISH_RADIUS: float = 1.15
const WISH_MIN_DEPTH: float = 0.6
const WISH_MAX_DEPTH: float = 1.8

## The soil's layout. 1: the 0.1 to 0.7 soil (27 rich patches, 1400 scattered dots). 2 (0.8,
## docs/notes/balance-0.8.md): fewer but larger topsoil patches set apart from each other, more
## phosphorus, and nettles on the meadow over phosphorus. A save keeps the layout it was made
## with, so its dot ids stay the same.
## 3 (0.8.1, docs/notes/sim-0.8.1.md): the wider root field, 30 m around the trunk, its rich
## patches in a near, a middle and a far ring, far apart, so a patch takes a long drive.
const LAYOUT: int = 3
## The layout a new game gets (GameState.new_game): LAYOUT; tools may compare an older one.
static var game_layout: int = LAYOUT
var layout: int = LAYOUT
## This soil's horizontal radius: EXTENT for layouts 1 and 2, field_extent for layout 3.
var extent: float = EXTENT

## Layout 2, the rich topsoil patches per kind: [patches, min dots, max dots, min radius,
## max radius, amount per dot]. A static var so tools can try other mixes; the game never
## changes it.
static var mix: Dictionary = {
	Resources.Kind.WATER: [2, 36, 46, 1.3, 1.8, 0.8],
	Resources.Kind.NITROGEN: [2, 40, 52, 1.2, 1.7, 1.45],
	Resources.Kind.PHOSPHORUS: [2, 40, 52, 1.2, 1.7, 1.1],
	Resources.Kind.POTASSIUM: [0, 30, 40, 1.1, 1.5, 1.2],
}
## Layout 2: deep water veins [patches, min dots, max dots], scattered single dots, and the
## least distance between two rich topsoil patches' centres (horizontal).
static var deep_water: Array = [1, 55, 75]
## Layout 2: the dots of each kind in the small mixed starter patch where the first roots head
## (layout 1: 10, 8, 7, 7). Richer, so the young tree is not short in its first week, when its
## roots cannot reach the rich patches yet (they lie 3 m or more out).
static var starter: Array = [12, 16, 14, 10]
static var scatter: int = 700
static var patch_gap: float = 3.2
## Layout 2: the nearest a rich topsoil patch lies to the trunk (layout 1: 3 m), so a young tree's
## short roots reach one in its first week.
static var patch_near: float = 2.2
## Layout 2 topsoil patches lie shallower than HINT_MAX_DEPTH, so each one shows on the meadow.
const PATCH_MAX_DEPTH: float = 2.0

## Layout 3 (0.8.1, item 29): the rich topsoil patches lie in rings around the trunk, each ring
## [inner radius, outer radius, the kinds of its patches in placing order]. The near ring gives
## every kind a young tree needs within one night; the middle and far rings need a long drive or
## a root continued over nights. The same sizes per kind as layout 2 (`mix`).
static var rings: Array = [
	[5.0, 12.0, [Resources.Kind.PHOSPHORUS, Resources.Kind.NITROGEN, Resources.Kind.WATER, Resources.Kind.PHOSPHORUS, Resources.Kind.NITROGEN]],
	[12.0, 20.0, [Resources.Kind.WATER, Resources.Kind.PHOSPHORUS, Resources.Kind.NITROGEN, Resources.Kind.WATER, Resources.Kind.PHOSPHORUS]],
	[20.0, 30.0, [Resources.Kind.NITROGEN, Resources.Kind.WATER, Resources.Kind.PHOSPHORUS, Resources.Kind.NITROGEN]],
]
## Layout 3: the least distance between two rich patches' centres (layout 2: patch_gap), the
## scattered single dots in the whole field, deep water veins, and rocks [shallow, deep] (layout 2:
## 5 and 26 in a field a fifth the size).
static var gap3: float = 7.0
static var scatter3: int = 1200
static var deep_water3: Array = [2, 55, 75]
static var rocks3: Array = [9, 60]
## Where the middle ring starts: a patch this far from the trunk or further counts as far for the
## wish (item 34).
const FAR_RING: float = 12.0

## 0.8.2 (specs/root-field-extras.md 2): rock bands and soft soil veins in the wider field, so the
## straight line to a far patch is not always the way. `bands_version` 0: none (layouts 1 and 2,
## and every layout-3 save from before 0.8.2, which keeps its soil dot for dot); 1: the 0.8.2 bands
## and veins. Saved with the soil. They are placed after the rest of the soil from their own
## seeded generators, so the soil around them is the same as without them (dots that would lie in
## a band are moved to its face).
const BANDS_VERSION: int = 1
static var game_bands: int = BANDS_VERSION
var bands_version: int = 0
## Each band: {"points": PackedVector2Array (its centre line, x/z, every BAND_STEP), "open":
## PackedByteArray (per segment, 1 = the gap), "half": half its thickness, "bottom": its depth (it
## rises to the meadow), "lo"/"hi": Vector2 bounds with the half thickness, "target": the far patch
## whose straight line from the trunk it was placed across}.
var bands: Array = []
## Each vein: {"points": PackedVector3Array (centre line), "radius", "lo"/"hi": Vector3 bounds,
## "target": the far patch it points at}.
var veins: Array = []
## Tuning (docs/notes/field-0.8.2.md). Bands: how many, centre-line length, thickness, the clear
## opening of the gap, depth. Veins: how many, length, width, and the cost of a metre inside.
static var band_count: int = 3
static var band_length: Vector2 = Vector2(8.0, 14.0)
static var band_thick: Vector2 = Vector2(1.0, 2.0)
static var band_gap: float = 2.0
static var band_bottom: float = 4.0
## The most far patches whose straight line the bands may block together, as a share.
static var band_block_share: float = 0.4
static var vein_count: int = 2
static var vein_length: Vector2 = Vector2(8.0, 14.0)
## A vein starts at least this far from the trunk (in the middle ring's way), so it helps the
## patch it points at and not every patch in its direction.
static var vein_start_min: float = 7.0
static var vein_width: Vector2 = Vector2(1.0, 1.5)
static var vein_cost: float = 0.6
const BAND_STEP: float = 0.5

## A normal rich topsoil patch in layout 1 (the wish deposit is WISH_SIZE times one): [dots,
## radius, amount per dot]. Water 20 to 32 dots in 0.9 to 1.5 m, nitrogen 22 to 36 in 0.8 to 1.4 m,
## phosphorus 16 to 26 in 0.7 to 1.2 m; potassium sits by the rocks, 8 dots a rock.
const NORMAL_V1 := {
	Resources.Kind.WATER: [26.0, 1.2, 1.0], Resources.Kind.NITROGEN: [29.0, 1.1, 1.2],
	Resources.Kind.PHOSPHORUS: [21.0, 0.95, 1.2], Resources.Kind.POTASSIUM: [16.0, 0.9, 1.2],
}

var _rng := RandomNumberGenerator.new()
var _grid: Dictionary = {}


## `bands`: the bands' version (BANDS_VERSION); -1 = what a new game gets (game_bands in the
## wider field, none in the older soils).
func _init(random_seed: int = 1, layout_version: int = LAYOUT, bands_ver: int = -1) -> void:
	seed = random_seed
	layout = layout_version
	deposit_shares = deposit_shares_wide if layout >= 3 else DEPOSIT_SHARES
	_rng.seed = hash([random_seed, "underground"])
	extent = field_extent if layout >= 3 else EXTENT
	if layout <= 1:
		_generate()
	elif layout == 2:
		_generate_v2()
	else:
		_generate_v3()
	if layout >= 3:
		bands_version = game_bands if bands_ver < 0 else bands_ver
	if bands_version >= 1:
		_generate_bands()
		_generate_veins()
	_build_grid()


func dot_count() -> int:
	return dot_positions.size()


func remaining_dots() -> int:
	var n := 0
	for c in dot_collected:
		if c == 0:
			n += 1
	return n


# --- generation -------------------------------------------------------------

func _generate() -> void:
	_generate_rocks()
	# A small mixed starter patch right where the first root heads (down and north from the seed),
	# so the very first run finds all four nutrients even if the player barely steers.
	var starter := Vector3(0.0, -0.8, -1.2)
	for k in range(4):
		_add_patch(k, starter, 0.9, [10, 8, 7, 7][k], 1.0)
	# Topsoil: rich N and P patches, moderate water.
	for _i in range(8):
		_add_patch(Resources.Kind.NITROGEN, _random_point(0.4, TOPSOIL, 3.0), _rng.randf_range(0.8, 1.4), _rng.randi_range(22, 36), 1.2)
	for _i in range(5):
		_add_patch(Resources.Kind.PHOSPHORUS, _random_point(0.4, TOPSOIL, 3.0), _rng.randf_range(0.7, 1.2), _rng.randi_range(16, 26), 1.2)
	for _i in range(6):
		_add_patch(Resources.Kind.WATER, _random_point(0.5, TOPSOIL, 3.0), _rng.randf_range(0.9, 1.5), _rng.randi_range(20, 32), 1.0)
	# Deeper: fewer nutrients, reliable water veins, potassium near rocks.
	for _i in range(4):
		_add_patch(Resources.Kind.WATER, _random_point(4.0, DEPTH - 1.0, 2.0), _rng.randf_range(1.4, 2.2), _rng.randi_range(40, 60), 1.4)
	for r in range(rock_centers.size()):
		if -rock_centers[r].y > 3.0:
			_add_potassium_around_rock(r)
	# Sparse scattered dots between the patches.
	for _i in range(1400):
		var p := _random_point(0.2, DEPTH, 0.8)
		var deep := -p.y > TOPSOIL
		_add_dot(p, _pick_kind(deep), 0.5)
	_generate_finds()


## Layout 2 (0.8): fewer, larger rich patches, set apart so each is its own choice on the
## meadow; phosphorus as rich as nitrogen; half the scattered dots (B4 of the 0.7 check).
func _generate_v2() -> void:
	_generate_rocks()
	var start := Vector3(0.0, -0.8, -1.2)
	for k in range(4):
		_add_patch(k, start, 0.9, int(starter[k]), 1.0)
	# The kinds take turns, so no kind always gets the first (best spaced) places.
	var order: Array[int] = []
	var most := 0
	for k in mix:
		most = maxi(most, int(mix[k][0]))
	for i in range(most):
		for k in [Resources.Kind.PHOSPHORUS, Resources.Kind.NITROGEN, Resources.Kind.WATER, Resources.Kind.POTASSIUM]:
			if i < int(mix[k][0]):
				order.append(k)
	var centres: Array[Vector3] = []
	for k in order:
		var m: Array = mix[k]
		var c := _spaced_point(centres, 0.5, PATCH_MAX_DEPTH, patch_near)
		centres.append(c)
		_add_patch(k, c, _rng.randf_range(float(m[3]), float(m[4])), _rng.randi_range(int(m[1]), int(m[2])), float(m[5]))
	for _i in range(int(deep_water[0])):
		_add_patch(Resources.Kind.WATER, _random_point(4.0, DEPTH - 1.0, 2.0), _rng.randf_range(1.6, 2.4), _rng.randi_range(int(deep_water[1]), int(deep_water[2])), 1.4)
	for r in range(rock_centers.size()):
		if -rock_centers[r].y > 3.0:
			_add_potassium_around_rock(r)
	for _i in range(scatter):
		var p := _random_point(0.2, DEPTH, 0.8)
		_add_dot(p, _pick_kind(-p.y > TOPSOIL), 0.5)
	_generate_finds()


## Layout 3 (0.8.1, item 29): the wider field. The starter patch as in layout 2, the rich topsoil
## patches ring by ring, far apart; scattered dots thinner over the bigger volume.
func _generate_v3() -> void:
	_generate_rocks(int(rocks3[0]), int(rocks3[1]))
	var start := Vector3(0.0, -0.8, -1.2)
	for k in range(4):
		_add_patch(k, start, 0.9, int(starter[k]), 1.0)
	var centres: Array[Vector3] = []
	for ring in rings:
		for k in ring[2]:
			var m: Array = mix[int(k)]
			var c := _spaced_point(centres, 0.5, PATCH_MAX_DEPTH, float(ring[0]), gap3, float(ring[1]))
			centres.append(c)
			_add_patch(int(k), c, _rng.randf_range(float(m[3]), float(m[4])), _rng.randi_range(int(m[1]), int(m[2])), float(m[5]))
	for _i in range(int(deep_water3[0])):
		_add_patch(Resources.Kind.WATER, _random_point(4.0, DEPTH - 1.0, 2.0), _rng.randf_range(1.6, 2.4), _rng.randi_range(int(deep_water3[1]), int(deep_water3[2])), 1.4)
	for r in range(rock_centers.size()):
		if -rock_centers[r].y > 3.0:
			_add_potassium_around_rock(r)
	for _i in range(scatter3):
		var p := _random_point(0.2, DEPTH, 0.8)
		_add_dot(p, _pick_kind(-p.y > TOPSOIL), 0.5)
	_generate_finds()


## A topsoil point at least `gap` (horizontally; patch_gap when negative) from every point in
## `taken`, `min_dist` to `max_dist` from the trunk (extent when negative); after 30 tries the one
## farthest from the others.
func _spaced_point(taken: Array[Vector3], min_depth: float, max_depth: float, min_dist: float, gap: float = -1.0, max_dist: float = -1.0) -> Vector3:
	if gap < 0.0:
		gap = patch_gap
	var best := Vector3.ZERO
	var best_gap := -1.0
	for _t in range(30):
		var p := _random_point(min_depth, max_depth, min_dist, max_dist)
		var g := INF
		for q in taken:
			g = minf(g, Vector2(p.x - q.x, p.z - q.z).length())
		if g >= gap:
			return p
		if g > best_gap:
			best_gap = g
			best = p
	return best


## For the balancing tools: applies one command-line argument that tries another layout-2 mix
## (--mix=kind:field=value, --deep=patches,min,max, --scatter=n, --gap=metres). Returns false
## for any other argument. The game never calls it.
static func tool_arg(a: String) -> bool:
	if a.begins_with("--mix="):
		var kv := a.substr(6).split("=")
		var kf := kv[0].split(":")
		mix[int(kf[0])][int(kf[1])] = float(kv[1])
	elif a.begins_with("--deep="):
		var dv := a.substr(7).split(",")
		deep_water = [int(dv[0]), int(dv[1]), int(dv[2])]
	elif a.begins_with("--starter="):
		var sv := a.substr(10).split(",")
		starter = [int(sv[0]), int(sv[1]), int(sv[2]), int(sv[3])]
	elif a.begins_with("--scatter="):
		scatter = int(a.substr(10))
	elif a.begins_with("--near="):
		patch_near = float(a.substr(7))
	elif a.begins_with("--gap="):
		patch_gap = float(a.substr(6))
	elif a.begins_with("--layout="):
		game_layout = int(a.substr(9))
	elif a.begins_with("--gap3="):
		gap3 = float(a.substr(7))
	elif a.begins_with("--scatter3="):
		scatter3 = int(a.substr(11))
	elif a.begins_with("--extent="):
		field_extent = float(a.substr(9))
	elif a.begins_with("--rocks3="):
		var rv := a.substr(9).split(",")
		rocks3 = [int(rv[0]), int(rv[1])]
	elif a.begins_with("--deep3="):
		var d3 := a.substr(8).split(",")
		deep_water3 = [int(d3[0]), int(d3[1]), int(d3[2])]
	elif a.begins_with("--bands="):
		game_bands = int(a.substr(8))
	elif a.begins_with("--band_count="):
		band_count = int(a.substr(13))
	elif a.begins_with("--band_gap="):
		band_gap = float(a.substr(11))
	elif a.begins_with("--vein_count="):
		vein_count = int(a.substr(13))
	elif a.begins_with("--vein_cost="):
		vein_cost = float(a.substr(12))
	elif a.begins_with("--ring="):
		# --ring=index:kinds, the kinds as digits by Resources.Kind (--ring=0:21021).
		var rk := a.substr(7).split(":")
		var kinds: Array = []
		for ch in rk[1]:
			kinds.append(int(ch))
		rings[int(rk[0])][2] = kinds
	else:
		return false
	return true


## A normal rich topsoil patch of `kind` in `layout_version`: [dots, radius, amount per dot].
static func normal_patch(kind: int, layout_version: int = LAYOUT) -> Array:
	if layout_version <= 1:
		return NORMAL_V1[kind]
	var m: Array = mix[kind]
	return [(float(m[1]) + float(m[2])) * 0.5, (float(m[3]) + float(m[4])) * 0.5, float(m[5])]


## A wish deposit's size: WISH_SIZE times a normal rich patch of `kind`, drawn from `rng`.
static func wish_size(kind: int, rng: RandomNumberGenerator, layout_version: int = LAYOUT) -> Dictionary:
	var n := normal_patch(kind, layout_version)
	return {
		"radius": float(n[1]) * WISH_RADIUS * rng.randf_range(0.92, 1.08),
		"count": int(round(float(n[0]) * WISH_SIZE * rng.randf_range(0.92, 1.08))),
	}


## Places a wish deposit (from Diary.plan_wish). Returns its index in `patches`, or -1 when the
## dot budget is full.
func add_wish_deposit(day: int, kind: int, center: Vector3, radius: float, count: int, far: bool = false) -> int:
	if dot_count() + count > Budgets.NUTRIENT_DOTS_LOADED:
		return -1
	wish_deposits.append({"day": day, "kind": kind, "center": center, "radius": radius, "count": count, "far": far})
	return _place_wish_deposit(wish_deposits[-1])


func _place_wish_deposit(w: Dictionary) -> int:
	var saved_seed := _rng.seed
	var saved := _rng.state
	_rng.seed = hash([seed, "wish_dots", int(w["day"])])
	var kind := int(w["kind"])
	var first := dot_count()
	_add_patch(kind, w["center"], float(w["radius"]), int(w["count"]), float(normal_patch(kind, layout)[2]))
	patches[-1]["wish"] = true
	patches[-1]["day"] = int(w["day"])
	patches[-1]["far"] = bool(w.get("far", false))
	patches[-1]["reached"] = bool(w.get("reached", false))
	# Always the newest entry of wish_deposits (add_wish_deposit, from_dict).
	patches[-1]["wish_index"] = wish_deposits.size() - 1
	_rng.seed = saved_seed
	_rng.state = saved
	for i in range(first, dot_count()):
		var c := _cell(dot_positions[i])
		if not _grid.has(c):
			_grid[c] = PackedInt32Array()
		var arr: PackedInt32Array = _grid[c]
		arr.append(i)
		_grid[c] = arr
	return patches.size() - 1


## A root reached this wish deposit's glow (Diary.check_reached): it is never a missed deposit
## again, even when the root only grazed its edge (0.8.1: a wish pointed a second time at a far
## deposit reached five nights before). Saved with the deposit.
func mark_wish_reached(patch_id: int) -> void:
	if patch_id < 0 or patch_id >= patches.size() or not bool(patches[patch_id].get("wish", false)):
		return
	patches[patch_id]["reached"] = true
	var wi := int(patches[patch_id].get("wish_index", -1))
	if wi >= 0 and wi < wish_deposits.size():
		wish_deposits[wi]["reached"] = true


## Indices into `patches` of the wish deposits.
func wish_patch_ids() -> PackedInt32Array:
	var out := PackedInt32Array()
	for i in range(patches.size()):
		if bool(patches[i].get("wish", false)):
			out.append(i)
	return out


## Dot ids of a patch.
func patch_dots(patch_id: int) -> PackedInt32Array:
	var out := PackedInt32Array()
	if patch_id < 0 or patch_id >= patches.size():
		return out
	for i in range(int(patches[patch_id]["first"]), int(patches[patch_id]["end"])):
		out.append(i)
	return out


## What a patch holds now, and held at the start (sum over its dots).
func patch_amount(patch_id: int, capacity: bool = false) -> float:
	var total := 0.0
	for i in patch_dots(patch_id):
		total += dot_capacity[i] if capacity else dot_amounts[i]
	return total


func _generate_rocks(shallow: int = 5, deeper: int = 26) -> void:
	# A few shallow rocks (they show as stones on the meadow), more and bigger ones deeper.
	for _i in range(shallow):
		_add_rock(_random_point(0.7, 1.6, 2.5), _rng.randf_range(0.45, 0.8))
	for _i in range(deeper):
		var depth := 1.5 + (DEPTH - 1.5) * sqrt(_rng.randf())
		var p := _random_point(depth, depth, 2.5)
		_add_rock(p, _rng.randf_range(0.5, 1.2 + depth * 0.08))


func _add_rock(center: Vector3, radius: float) -> void:
	rock_centers.append(center)
	rock_radii.append(radius)


func _add_potassium_around_rock(r: int) -> void:
	for _i in range(8):
		var dir := _random_unit()
		_add_dot(rock_centers[r] + dir * (rock_radii[r] + _rng.randf_range(0.15, 0.5)), Resources.Kind.POTASSIUM, 1.2)


func _add_patch(kind: int, center: Vector3, radius: float, count: int, amount: float) -> void:
	var first := dot_positions.size()
	for _i in range(count):
		var offset := _random_unit() * radius * sqrt(_rng.randf())
		_add_dot(center + offset, kind, amount)
	patches.append({"kind": kind, "center": center, "radius": radius, "first": first, "end": dot_positions.size(), "wish": false})


func _add_dot(p: Vector3, kind: int, amount: float) -> void:
	if dot_positions.size() >= Budgets.NUTRIENT_DOTS_LOADED:
		return
	p.y = clampf(p.y, -DEPTH, -0.15)
	if is_inside_rock(p, 0.1):
		return
	dot_positions.append(p)
	dot_kinds.append(kind)
	dot_amounts.append(amount * deposit_shares)
	dot_capacity.append(amount * deposit_shares)
	dot_collected.append(0)


func _pick_kind(deep: bool) -> int:
	var r := _rng.randf()
	if deep:
		return Resources.Kind.WATER if r < 0.6 else (Resources.Kind.POTASSIUM if r < 0.9 else (Resources.Kind.NITROGEN if r < 0.95 else Resources.Kind.PHOSPHORUS))
	return Resources.Kind.WATER if r < 0.4 else (Resources.Kind.NITROGEN if r < 0.7 else (Resources.Kind.PHOSPHORUS if r < 0.9 else Resources.Kind.POTASSIUM))


func _generate_finds() -> void:
	# Fossil on a rock, old root and coin in the topsoil, water vein in the first deep water patch.
	var deep_rock := 5
	finds.append({"kind": "fossil", "position": rock_centers[deep_rock] + Vector3.UP * (rock_radii[deep_rock] + 0.2), "found": false})
	finds.append({"kind": "old_root", "position": _random_point(0.6, 2.0, 4.0), "found": false})
	finds.append({"kind": "coin", "position": _random_point(0.3, 1.0, 2.5), "found": false})
	for patch in patches:
		if patch["kind"] == Resources.Kind.WATER and -(patch["center"] as Vector3).y > 4.0:
			finds.append({"kind": "water_vein", "position": patch["center"], "found": false})
			break


# --- rock bands and soft veins (0.8.2) ---------------------------------------------

## The rich topsoil patches of the middle and far rings (not wishes, not the deep water veins).
func far_patch_ids() -> PackedInt32Array:
	var out := PackedInt32Array()
	for i in range(patches.size()):
		var p: Dictionary = patches[i]
		var c: Vector3 = p["center"]
		if bool(p.get("wish", false)) or -c.y > PATCH_MAX_DEPTH or Vector2(c.x, c.z).length() < FAR_RING:
			continue
		out.append(i)
	return out


func _shuffled(ids: PackedInt32Array, rng: RandomNumberGenerator) -> PackedInt32Array:
	var a := ids.duplicate()
	for i in range(a.size() - 1, 0, -1):
		var j := rng.randi_range(0, i)
		var tmp := a[i]
		a[i] = a[j]
		a[j] = tmp
	return a


## About band_count curved walls, each across the straight line from the trunk to a different far
## patch (a third of them), 2 to 4 m short of its edge, with one gap off that line; clear of every
## rich patch and of each other, so no patch is ever closed in. Dots and finds that would lie in a
## band move to its face; potassium gathers along both faces, as by a rock.
func _generate_bands() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "bands"])
	var far := far_patch_ids()
	# A band also shadows patches behind it; at most this many straight lines end up blocked
	# (broken 3: never more than about half).
	var most := ceili(far.size() * band_block_share)
	for pid in _shuffled(far, rng):
		if bands.size() >= band_count:
			break
		for _try in range(12):
			var b := _try_band(pid, rng)
			if b.is_empty():
				continue
			bands.append(b)
			var blocked := 0
			for q in far:
				if straight_blocked(q):
					blocked += 1
			if blocked <= most:
				break
			bands.pop_back()
	if bands.is_empty():
		return
	for i in range(dot_positions.size()):
		dot_positions[i] = _out_of_bands(dot_positions[i])
	for f in finds:
		f["position"] = _out_of_bands(f["position"])
	for b in bands:
		var pts: PackedVector2Array = b["points"]
		var open: PackedByteArray = b["open"]
		var n := int(_band_length(b) / 1.4)
		for k in range(n):
			var s := rng.randi_range(0, open.size() - 1)
			if open[s] != 0:
				continue
			var at := pts[s].lerp(pts[s + 1], rng.randf())
			var t := (pts[s + 1] - pts[s]).normalized()
			var side := 1.0 if k % 2 == 0 else -1.0
			var off := Vector2(-t.y, t.x) * side * (float(b["half"]) + rng.randf_range(0.15, 0.5))
			var depth := rng.randf_range(0.4, minf(float(b["bottom"]) - 0.3, 3.5))
			_add_dot(Vector3(at.x + off.x, -depth, at.y + off.y), Resources.Kind.POTASSIUM, 1.2)


func _try_band(pid: int, rng: RandomNumberGenerator) -> Dictionary:
	var c: Vector3 = patches[pid]["center"]
	var flat := Vector2(c.x, c.z)
	var dist := flat.length()
	var dir := flat / dist
	var cross := dist - float(patches[pid]["radius"]) - rng.randf_range(2.0, 4.0)
	var length := rng.randf_range(band_length.x, band_length.y)
	var half := rng.randf_range(band_thick.x, band_thick.y) * 0.5
	# Signed radius of curvature: the wall bends toward the trunk or away from it.
	var bend := rng.randf_range(10.0, 22.0) * (1.0 if rng.randf() < 0.5 else -1.0)
	# Where along the band the straight line crosses, and the gap off to one side of it (its clear
	# opening band_gap between the two rounded faces).
	var s0 := rng.randf_range(-0.2, 0.2) * length
	var gap_u := band_gap + 2.0 * half
	var keep := 1.2 + gap_u * 0.5
	var lo_l := -length * 0.5 + gap_u * 0.5 + 1.0
	var hi_l := s0 - keep
	var lo_r := s0 + keep
	var hi_r := length * 0.5 - gap_u * 0.5 - 1.0
	var g := 0.0
	if hi_l - lo_l >= hi_r - lo_r:
		if hi_l < lo_l:
			return {}
		g = rng.randf_range(lo_l, hi_l)
	else:
		if hi_r < lo_r:
			return {}
		g = rng.randf_range(lo_r, hi_r)
	if cross < 9.0:
		return {}
	var x := dir * cross
	var centre := x - dir * bend
	var steps := ceili(length / BAND_STEP)
	var pts := PackedVector2Array()
	for i in range(steps + 1):
		var u := -length * 0.5 + length * float(i) / steps
		pts.append(centre + dir.rotated((u - s0) / bend) * bend)
	var open := PackedByteArray()
	# A segment that overlaps the gap is open, so the clear opening is never less than band_gap.
	for i in range(steps):
		var ua := -length * 0.5 + length * float(i) / steps
		var ub := -length * 0.5 + length * float(i + 1) / steps
		open.append(1 if ub > g - gap_u * 0.5 and ua < g + gap_u * 0.5 else 0)
	# Clear of the trunk, the field's edge, every rich patch and the other bands.
	for p in pts:
		var r := p.length()
		if r < 6.0 or r > extent - 1.0 - half:
			return {}
		for q in patches:
			var qc: Vector3 = q["center"]
			if -qc.y <= PATCH_MAX_DEPTH + 0.5 and p.distance_to(Vector2(qc.x, qc.z)) < float(q["radius"]) + half + 0.8:
				return {}
		for ob in bands:
			for op in (ob["points"] as PackedVector2Array):
				if p.distance_to(op) < half + float(ob["half"]) + 4.0:
					return {}
	var lo := pts[0]
	var hi := pts[0]
	for p in pts:
		lo = lo.min(p)
		hi = hi.max(p)
	return {"points": pts, "open": open, "half": half, "bottom": band_bottom + rng.randf_range(-0.5, 0.5),
		"lo": lo - Vector2.ONE * half, "hi": hi + Vector2.ONE * half, "target": pid}


## Length of a band's centre line, gap included.
func _band_length(b: Dictionary) -> float:
	var pts: PackedVector2Array = b["points"]
	var total := 0.0
	for i in range(pts.size() - 1):
		total += pts[i].distance_to(pts[i + 1])
	return total


## About vein_count tubes of soft soil, each pointing at a far patch whose straight line no band
## blocks, ending 0.6 to 1.4 m short of its edge, from vein_start_min or more from the trunk; clear
## of the bands and of each other.
func _generate_veins() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "veins"])
	var targets := {}
	for b in bands:
		targets[int(b["target"])] = true
	for pid in _shuffled(far_patch_ids(), rng):
		if veins.size() >= vein_count:
			break
		if targets.has(pid):
			continue
		var c: Vector3 = patches[pid]["center"]
		if line_blocked(Vector3(0, c.y, 0), c):
			continue
		for _try in range(12):
			var v := _try_vein(pid, rng)
			if not v.is_empty():
				veins.append(v)
				break


func _try_vein(pid: int, rng: RandomNumberGenerator) -> Dictionary:
	var c: Vector3 = patches[pid]["center"]
	var flat := Vector2(c.x, c.z)
	var vdir := (flat / flat.length()).rotated(rng.randf_range(-0.45, 0.45))
	var length := rng.randf_range(vein_length.x, vein_length.y)
	var radius := rng.randf_range(vein_width.x, vein_width.y) * 0.5
	var end := flat - vdir * (float(patches[pid]["radius"]) + rng.randf_range(0.6, 1.4))
	var start := end - vdir * length
	var d_end := clampf(-c.y, 0.7, 1.8)
	var d_start := clampf(d_end + rng.randf_range(-0.4, 0.6), 0.6, 2.0)
	var bow := rng.randf_range(-1.5, 1.5)
	var side := Vector2(-vdir.y, vdir.x)
	var steps := ceili(length / BAND_STEP)
	var pts := PackedVector3Array()
	for i in range(steps + 1):
		var k := float(i) / steps
		var p2 := start.lerp(end, k) + side * bow * sin(PI * k)
		pts.append(Vector3(p2.x, -lerpf(d_start, d_end, k), p2.y))
	for p in pts:
		var r := Vector2(p.x, p.z).length()
		if r < vein_start_min or r > extent - 1.0 - radius:
			return {}
		if band_at(p, radius + 1.0) >= 0:
			return {}
		for ov in veins:
			for op in (ov["points"] as PackedVector3Array):
				if p.distance_to(op) < radius + float(ov["radius"]) + 4.0:
					return {}
	var lo := pts[0]
	var hi := pts[0]
	for p in pts:
		lo = lo.min(p)
		hi = hi.max(p)
	return {"points": pts, "radius": radius, "lo": lo - Vector3.ONE * radius, "hi": hi + Vector3.ONE * radius, "target": pid}


## The straight line from the trunk to a patch (at its depth, up to its edge) passes a band.
func straight_blocked(pid: int) -> bool:
	if bands.is_empty():
		return false
	var c: Vector3 = patches[pid]["center"]
	var flat := Vector2(c.x, c.z)
	var end := flat.length() - float(patches[pid]["radius"])
	var n := ceili(end / 0.2)
	for i in range(n + 1):
		var p2 := flat.normalized() * end * float(i) / n
		if band_at(Vector3(p2.x, c.y, p2.y)) >= 0:
			return true
	return false


## The nearest point of a band's rock (its closed segments) to `p2`, horizontally:
## [distance, closest point, segment] or [INF, ...] when the band is all gap.
func _band_closest(b: Dictionary, p2: Vector2) -> Array:
	var pts: PackedVector2Array = b["points"]
	var open: PackedByteArray = b["open"]
	var best := INF
	var best_q := Vector2.ZERO
	var best_s := -1
	for s in range(open.size()):
		if open[s] != 0:
			continue
		var q := Geometry2D.get_closest_point_to_segment(p2, pts[s], pts[s + 1])
		var d := p2.distance_to(q)
		if d < best:
			best = d
			best_q = q
			best_s = s
	return [best, best_q, best_s]


## The band `p` lies in (within `margin` of its rock), or -1.
func band_at(p: Vector3, margin: float = 0.0) -> int:
	var p2 := Vector2(p.x, p.z)
	for i in range(bands.size()):
		var b: Dictionary = bands[i]
		var lo: Vector2 = b["lo"]
		var hi: Vector2 = b["hi"]
		if p2.x < lo.x - margin or p2.y < lo.y - margin or p2.x > hi.x + margin or p2.y > hi.y + margin:
			continue
		if -p.y > float(b["bottom"]) + margin:
			continue
		if float(_band_closest(b, p2)[0]) < float(b["half"]) + margin:
			return i
	return -1


## `p` moved out of every band by `margin`: sideways to the nearer face, or down below the band
## when that is shorter. Unchanged outside the bands.
func band_push(p: Vector3, margin: float = 0.0) -> Vector3:
	for b in bands:
		var p2 := Vector2(p.x, p.z)
		var lo: Vector2 = b["lo"]
		var hi: Vector2 = b["hi"]
		if p2.x < lo.x - margin or p2.y < lo.y - margin or p2.x > hi.x + margin or p2.y > hi.y + margin:
			continue
		var bottom := float(b["bottom"])
		if -p.y > bottom + margin:
			continue
		var cl := _band_closest(b, p2)
		var d := float(cl[0])
		var want := float(b["half"]) + margin
		if d >= want:
			continue
		var down := bottom + margin + p.y
		if down < want - d:
			p.y = -(bottom + margin)
			continue
		var q: Vector2 = cl[1]
		var n := (p2 - q) / d if d > 1e-4 else _segment_normal(b, int(cl[2]), p2)
		var out := q + n * want
		p.x = out.x
		p.z = out.y
	return p


func _segment_normal(b: Dictionary, s: int, p2: Vector2) -> Vector2:
	var pts: PackedVector2Array = b["points"]
	var t := (pts[s + 1] - pts[s]).normalized()
	var n := Vector2(-t.y, t.x)
	# Toward the trunk's side, so a pushed dot or root stays on the near side.
	return n if n.dot(-pts[s]) >= 0.0 else -n


func _out_of_bands(p: Vector3) -> Vector3:
	if band_at(p, 0.1) < 0:
		return p
	return band_push(p, 0.2)


## The soft vein `p` lies in, or -1.
func vein_at(p: Vector3, margin: float = 0.0) -> int:
	for i in range(veins.size()):
		var v: Dictionary = veins[i]
		var lo: Vector3 = v["lo"]
		var hi: Vector3 = v["hi"]
		if p.x < lo.x - margin or p.y < lo.y - margin or p.z < lo.z - margin or p.x > hi.x + margin or p.y > hi.y + margin or p.z > hi.z + margin:
			continue
		var pts: PackedVector3Array = v["points"]
		var r := float(v["radius"]) + margin
		for s in range(pts.size() - 1):
			if p.distance_squared_to(Geometry3D.get_closest_point_to_segment(p, pts[s], pts[s + 1])) < r * r:
				return i
	return -1


## What a metre of root costs here relative to the plain soil: vein_cost in a soft vein.
func soil_factor(p: Vector3) -> float:
	if veins.is_empty():
		return 1.0
	return vein_cost if vein_at(p) >= 0 else 1.0


## A straight line from `a` to `b` passes through a rock or a band (sampled every `step`).
func line_blocked(a: Vector3, b: Vector3, step: float = 0.2, margin: float = 0.0) -> bool:
	var n := maxi(1, ceili(a.distance_to(b) / step))
	for i in range(n + 1):
		if is_inside_rock(a.lerp(b, float(i) / n), margin):
			return true
	return false


## Rich patches the player has come near (0.8.2 far view, specs/root-field-extras.md 1): a root
## node within `reach` of the patch's edge, or the patch's own meadow sign over it (a topsoil patch
## under the clearing, `edge` its radius; a far patch's sign moved to the clearing's edge only
## tells the way, not the place). The deep veins count only by reach.
func known_patches(root_positions: PackedVector3Array, reach: float, edge: float = INF) -> PackedInt32Array:
	var out := PackedInt32Array()
	const CELL_K := 2.0
	var cells := {}
	for p in root_positions:
		var key := Vector3i(floori(p.x / CELL_K), floori(p.y / CELL_K), floori(p.z / CELL_K))
		if not cells.has(key):
			cells[key] = PackedVector3Array()
		var arr: PackedVector3Array = cells[key]
		arr.append(p)
		cells[key] = arr
	var rim := edge - EDGE_INSET
	for i in range(patches.size()):
		var c: Vector3 = patches[i]["center"]
		var r := float(patches[i]["radius"])
		if -c.y <= HINT_MAX_DEPTH and Vector2(c.x, c.z).length() <= rim:
			out.append(i)
			continue
		var want := reach + r
		var lo := Vector3i(floori((c.x - want) / CELL_K), floori((c.y - want) / CELL_K), floori((c.z - want) / CELL_K))
		var hi := Vector3i(floori((c.x + want) / CELL_K), floori((c.y + want) / CELL_K), floori((c.z + want) / CELL_K))
		var hit := false
		for x in range(lo.x, hi.x + 1):
			for y in range(lo.y, hi.y + 1):
				for z in range(lo.z, hi.z + 1):
					var key := Vector3i(x, y, z)
					if not cells.has(key):
						continue
					for q in (cells[key] as PackedVector3Array):
						if q.distance_squared_to(c) <= want * want:
							hit = true
							break
					if hit:
						break
				if hit:
					break
			if hit:
				break
		if hit:
			out.append(i)
	return out


## Random point with depth in [min_depth, max_depth] and horizontal distance
## in [min_dist, max_dist] from the trunk (the soil's extent when negative). Layout 3 spreads it
## evenly over the ring's area; layouts 1 and 2 keep their old draw, dot for dot.
func _random_point(min_depth: float, max_depth: float, min_dist: float, max_dist: float = -1.0) -> Vector3:
	var a := _rng.randf() * TAU
	var hi := extent if max_dist < 0.0 else max_dist
	var d := sqrt(lerpf(min_dist * min_dist, hi * hi, _rng.randf())) if layout >= 3 else lerpf(min_dist, hi, sqrt(_rng.randf()))
	return Vector3(cos(a) * d, -_rng.randf_range(min_depth, max_depth), sin(a) * d)


func _random_unit() -> Vector3:
	var v := Vector3(_rng.randf_range(-1, 1), _rng.randf_range(-1, 1), _rng.randf_range(-1, 1))
	while v.length_squared() > 1.0 or v.length_squared() < 1e-4:
		v = Vector3(_rng.randf_range(-1, 1), _rng.randf_range(-1, 1), _rng.randf_range(-1, 1))
	return v.normalized()


# --- queries ----------------------------------------------------------------

func _cell(p: Vector3) -> Vector3i:
	return Vector3i(floori(p.x / CELL), floori(p.y / CELL), floori(p.z / CELL))


func _build_grid() -> void:
	_grid.clear()
	for i in range(dot_positions.size()):
		var c := _cell(dot_positions[i])
		if not _grid.has(c):
			_grid[c] = PackedInt32Array()
		var arr: PackedInt32Array = _grid[c]
		arr.append(i)
		_grid[c] = arr


## Ids of uncollected dots within `radius` of `p`.
func dots_near(p: Vector3, radius: float) -> PackedInt32Array:
	var out := PackedInt32Array()
	var r2 := radius * radius
	var lo := _cell(p - Vector3.ONE * radius)
	var hi := _cell(p + Vector3.ONE * radius)
	for x in range(lo.x, hi.x + 1):
		for y in range(lo.y, hi.y + 1):
			for z in range(lo.z, hi.z + 1):
				var key := Vector3i(x, y, z)
				if not _grid.has(key):
					continue
				for i in (_grid[key] as PackedInt32Array):
					if dot_collected[i] == 0 and dot_positions[i].distance_squared_to(p) <= r2:
						out.append(i)
	return out


## Marks dots collected and adds them to the matching resource. Returns the ids collected.
## Draws `share` of each deposit's capacity (at most what is left) into the matching resource.
## Returns the ids drawn from; `last_drawn` holds the amount each gave, in the same order.
var last_drawn: PackedFloat32Array = PackedFloat32Array()


## `water_share` multiplies the share drawn from water deposits (alder drains them faster).
## `room` (4 floats by Resources.Kind, empty = no limit) caps what goes into the tree and is
## drawn down as it fills (0.8: the night's finds fill the stock only up to the tree's room;
## the rest stays in the deposit for the old roots).
func collect(ids: PackedInt32Array, into: Resources, share: float = FIRST_SHARE, water_share: float = 1.0, room: Array = []) -> PackedInt32Array:
	var out := PackedInt32Array()
	last_drawn = PackedFloat32Array()
	for i in ids:
		if dot_collected[i] != 0:
			continue
		var k := share * (water_share if dot_kinds[i] == Resources.Kind.WATER else 1.0)
		var take := minf(dot_amounts[i], dot_capacity[i] * k)
		if not room.is_empty():
			take = minf(take, maxf(float(room[dot_kinds[i]]), 0.0))
			room[dot_kinds[i]] = float(room[dot_kinds[i]]) - take
		if take <= 0.0:
			continue
		dot_amounts[i] -= take
		if dot_amounts[i] <= 0.01:
			dot_amounts[i] = 0.0
			dot_collected[i] = 1
		into.add(dot_kinds[i], take)
		out.append(i)
		last_drawn.append(take)
	return out


## How full a deposit still is, 0..1 (for the glow's size).
func fullness(i: int) -> float:
	return 0.0 if dot_capacity[i] <= 0.0 else dot_amounts[i] / dot_capacity[i]


## The soil slowly refills (rain, rotting leaves, water seeping in): each night a share of the
## drunk dots comes back, so the month does not run dry. Deterministic from the seed and day.
## Returns how many came back.
func regrow(share: float, day: int) -> int:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "regrow", day])
	var n := 0
	for i in range(dot_collected.size()):
		# Clover and nettles keep feeding nitrogen back into the soil, so it comes back faster
		# (QA: without this nitrogen ran out from day 4 and held the tree back for a week).
		var chance := share * (6.0 if dot_kinds[i] == Resources.Kind.NITROGEN else 2.0)
		if dot_amounts[i] < dot_capacity[i] and rng.randf() < chance:
			dot_amounts[i] = minf(dot_capacity[i], dot_amounts[i] + dot_capacity[i] * 0.5)
			if dot_collected[i] != 0:
				dot_collected[i] = 0
				n += 1
	return n


func is_inside_rock(p: Vector3, margin: float = 0.0) -> bool:
	return rock_at(p, margin) >= 0


## The rock `p` lies in: a boulder's index, or rock_centers.size() + a band's index (0.8.2); -1.
func rock_at(p: Vector3, margin: float = 0.0) -> int:
	for r in range(rock_centers.size()):
		var rr := rock_radii[r] + margin
		if p.distance_squared_to(rock_centers[r]) < rr * rr:
			return r
	if not bands.is_empty():
		var b := band_at(p, margin)
		if b >= 0:
			return rock_centers.size() + b
	return -1


## Finds not yet found within touch distance of `p`; marks them found and returns them.
func touch_finds(p: Vector3) -> Array:
	var out: Array = []
	for f in finds:
		if not f["found"] and (f["position"] as Vector3).distance_to(p) < FIND_RADIUS:
			f["found"] = true
			out.append(f)
	return out


## Surface hints for the meadow: what grows above what lies below (design doc "Read the meadow").
## Each: {"kind": "rushes"|"damp"|"clover"|"nettles"|"comfrey"|"stones"|"moss", "position": Vector3 (y = 0), "radius": float}
## `edge` is the clearing's radius (0.8.1, item 29): a patch beyond it shows its sign at the
## clearing's edge in its direction (EDGE_INSET inside, a little smaller), so a far patch can still
## be read from the day. INF: every sign over its patch.
const EDGE_INSET: float = 2.5
const EDGE_SIGN_SCALE: float = 0.8


func surface_hints(edge: float = INF) -> Array:
	var out: Array = []
	var clover_turn := true
	var rim := edge - EDGE_INSET
	for patch in patches:
		var c: Vector3 = patch["center"]
		# Not the starter patch right under the seed: the meadow at the trunk stays plain.
		if -c.y > HINT_MAX_DEPTH or Vector2(c.x, c.z).length() < 2.0:
			continue
		var ground := Vector3(c.x, 0.0, c.z)
		var r: float = patch["radius"]
		var flat := Vector2(c.x, c.z)
		if flat.length() > rim:
			var at := flat.normalized() * maxf(rim, 2.0)
			ground = Vector3(at.x, 0.0, at.y)
			r *= EDGE_SIGN_SCALE
		match int(patch["kind"]):
			Resources.Kind.WATER:
				out.append({"kind": "damp", "position": ground, "radius": r})
				out.append({"kind": "rushes", "position": ground, "radius": r * 0.7})
			Resources.Kind.NITROGEN:
				# A wish deposit's wish names the clover, so clover grows above it. Layout 2:
				# clover over nitrogen, nettles over phosphorus (nettles love phosphate).
				if bool(patch.get("wish", false)) or layout >= 2:
					out.append({"kind": "clover", "position": ground, "radius": r})
					continue
				out.append({"kind": "clover" if clover_turn else "nettles", "position": ground, "radius": r})
				clover_turn = not clover_turn
			Resources.Kind.PHOSPHORUS:
				if layout >= 2 or bool(patch.get("wish", false)):
					out.append({"kind": "nettles", "position": ground, "radius": r})
			Resources.Kind.POTASSIUM:
				# A wish for potassium shows comfrey above it: its deep roots bring potash up
				# (gardeners make potash feed of it). Stones mean shallow rock only (0.8 review).
				if bool(patch.get("wish", false)):
					out.append({"kind": "comfrey", "position": ground, "radius": r})
	for i in range(rock_centers.size()):
		var top := -rock_centers[i].y - rock_radii[i]
		if top < 1.2:
			out.append({"kind": "stones", "position": Vector3(rock_centers[i].x, 0.0, rock_centers[i].z), "radius": rock_radii[i]})
	# 0.8.2: a rock band under the clearing shows as a line of stones along it.
	for b in bands:
		var pts: PackedVector2Array = b["points"]
		var open: PackedByteArray = b["open"]
		for s in range(0, open.size(), 3):
			if open[s] != 0 or pts[s].length() > edge - 1.0:
				continue
			out.append({"kind": "stones", "position": Vector3(pts[s].x, 0.0, pts[s].y), "radius": float(b["half"]) * 0.8})
	# Moss grows on the shady north side (-Z) of the trunk.
	out.append({"kind": "moss", "position": Vector3(0, 0, -0.12), "radius": 0.15})
	return out


## Direction word ("north", "south-east", ...) from the trunk to a ground point.
static func compass(p: Vector3) -> String:
	var ns := "north" if p.z < 0.0 else "south"
	var ew := "east" if p.x > 0.0 else "west"
	if absf(p.z) > 2.0 * absf(p.x):
		return ns
	if absf(p.x) > 2.0 * absf(p.z):
		return ew
	return ns + "-" + ew


func to_dict() -> Dictionary:
	var found: Array = []
	for f in finds:
		found.append(f["found"])
	var wishes: Array = []
	for w in wish_deposits:
		var c: Vector3 = w["center"]
		var row: Array = [w["day"], w["kind"], c.x, c.y, c.z, w["radius"], w["count"]]
		if bool(w.get("far", false)) or bool(w.get("reached", false)):
			row.append(1 if bool(w.get("far", false)) else 0)
		if bool(w.get("reached", false)):
			row.append(1)
		wishes.append(row)
	return {"seed": seed, "layout": layout, "collected": Marshalls.raw_to_base64(dot_collected),
		"amounts": Marshalls.raw_to_base64(dot_amounts.to_byte_array()), "finds_found": found,
		"wish_deposits": wishes, "bands": bands_version}


static func from_dict(d: Dictionary) -> Underground:
	# A save from before 0.8 has no layout: its soil is layout 1.
	# A save from before 0.8.2 has no bands entry: its soil keeps no bands or veins.
	var u := Underground.new(int(d.get("seed", 1)), int(d.get("layout", 1)), int(d.get("bands", 0)))
	# The wish deposits placed so far, in the same order, so their dot ids come out the same.
	for w in d.get("wish_deposits", []):
		if w is Array and (w as Array).size() >= 7:
			u.wish_deposits.append({"day": int(w[0]), "kind": int(w[1]), "center": Vector3(w[2], w[3], w[4]), "radius": float(w[5]), "count": int(w[6]),
				"far": (w as Array).size() >= 8 and int(w[7]) != 0, "reached": (w as Array).size() >= 9 and int(w[8]) != 0})
			u._place_wish_deposit(u.wish_deposits[-1])
	var raw := Marshalls.base64_to_raw(str(d.get("collected", "")))
	if raw.size() <= u.dot_collected.size():
		for i in range(raw.size()):
			u.dot_collected[i] = raw[i]
	var am := Marshalls.base64_to_raw(str(d.get("amounts", ""))).to_float32_array()
	if am.size() > 0 and am.size() <= u.dot_amounts.size() and raw.size() == am.size():
		# Saves from before the 0.6.4 rebalance held bigger deposits.
		for i in range(am.size()):
			u.dot_amounts[i] = minf(am[i], u.dot_capacity[i])
	else:
		# An older save: dots were either full or gone.
		for i in range(u.dot_count()):
			if u.dot_collected[i] != 0:
				u.dot_amounts[i] = 0.0
	var found: Array = d.get("finds_found", [])
	for i in range(mini(found.size(), u.finds.size())):
		u.finds[i]["found"] = bool(found[i])
	return u
