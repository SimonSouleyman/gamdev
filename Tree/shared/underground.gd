class_name Underground
extends RefCounted
## The seeded underground: nutrient dots, rocks and finds, plus the surface hints that
## reveal them on the meadow. One generator, one seed (design doc sections 5, 6, 3).
## Coordinates: +X east, +Z south, -Z north, y < 0 below the meadow. Pure data.

## Horizontal radius of the generated volume, metres around the trunk.
const EXTENT: float = 14.0
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

## Wish deposits (0.7): a few rich topsoil patches about WISH_SIZE times a normal one, one per
## compass direction, that the daily wish can point at (Diary.make_wish_target). Generated after
## everything else from their own RNG, so older saves keep their dot ids.
const WISH_DEPOSITS: int = 8
const WISH_SIZE: float = 1.5
const WISH_RADIUS: float = 1.15
const WISH_MIN_DIST: float = 3.5
const WISH_MAX_DIST: float = 8.0
const WISH_MIN_DEPTH: float = 0.6
const WISH_MAX_DEPTH: float = 1.8

var _rng := RandomNumberGenerator.new()
var _grid: Dictionary = {}


func _init(random_seed: int = 1) -> void:
	seed = random_seed
	_rng.seed = hash([random_seed, "underground"])
	_generate()
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
	_generate_wish_deposits()


## Normal rich topsoil patches hold nitrogen 22 to 36 dots (radius 0.8 to 1.4) and water 20 to 32
## (0.9 to 1.5); a wish deposit holds WISH_SIZE times the mean and is WISH_RADIUS times as wide.
func _generate_wish_deposits() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "wish_deposits"])
	var saved := _rng.state
	var saved_seed := _rng.seed
	_rng.seed = hash([seed, "wish_deposit_dots"])
	for i in range(WISH_DEPOSITS):
		var kind := Resources.Kind.WATER if i % 2 == 0 else Resources.Kind.NITROGEN
		var mean_count := 26.0 if kind == Resources.Kind.WATER else 29.0
		var mean_radius := 1.2 if kind == Resources.Kind.WATER else 1.1
		var radius := mean_radius * WISH_RADIUS * rng.randf_range(0.92, 1.08)
		var count := int(round(mean_count * WISH_SIZE * rng.randf_range(0.92, 1.08)))
		var center := Vector3.ZERO
		for _try in range(12):
			var a := (float(i) + rng.randf_range(-0.3, 0.3)) * TAU / WISH_DEPOSITS
			var d := rng.randf_range(WISH_MIN_DIST, WISH_MAX_DIST)
			center = Vector3(cos(a) * d, -rng.randf_range(WISH_MIN_DEPTH, WISH_MAX_DEPTH), sin(a) * d)
			if rock_at(center, radius + 0.3) < 0:
				break
		_add_patch(kind, center, radius, count, 1.0 if kind == Resources.Kind.WATER else 1.2)
		patches[-1]["wish"] = true
	_rng.seed = saved_seed
	_rng.state = saved


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


func _generate_rocks() -> void:
	# A few shallow rocks (they show as stones on the meadow), more and bigger ones deeper.
	for _i in range(5):
		_add_rock(_random_point(0.7, 1.6, 2.5), _rng.randf_range(0.45, 0.8))
	for _i in range(26):
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
	dot_amounts.append(amount * DEPOSIT_SHARES)
	dot_capacity.append(amount * DEPOSIT_SHARES)
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


## Random point with depth in [min_depth, max_depth] and horizontal distance
## in [min_dist, EXTENT] from the trunk.
func _random_point(min_depth: float, max_depth: float, min_dist: float) -> Vector3:
	var a := _rng.randf() * TAU
	var d := lerpf(min_dist, EXTENT, sqrt(_rng.randf()))
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
func collect(ids: PackedInt32Array, into: Resources, share: float = FIRST_SHARE, water_share: float = 1.0) -> PackedInt32Array:
	var out := PackedInt32Array()
	last_drawn = PackedFloat32Array()
	for i in ids:
		if dot_collected[i] != 0:
			continue
		var k := share * (water_share if dot_kinds[i] == Resources.Kind.WATER else 1.0)
		var take := minf(dot_amounts[i], dot_capacity[i] * k)
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


func rock_at(p: Vector3, margin: float = 0.0) -> int:
	for r in range(rock_centers.size()):
		var rr := rock_radii[r] + margin
		if p.distance_squared_to(rock_centers[r]) < rr * rr:
			return r
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
## Each: {"kind": "rushes"|"damp"|"clover"|"nettles"|"stones"|"moss", "position": Vector3 (y = 0), "radius": float}
func surface_hints() -> Array:
	var out: Array = []
	var clover_turn := true
	for patch in patches:
		var c: Vector3 = patch["center"]
		# Not the starter patch right under the seed: the meadow at the trunk stays plain.
		if -c.y > HINT_MAX_DEPTH or Vector2(c.x, c.z).length() < 2.0:
			continue
		var ground := Vector3(c.x, 0.0, c.z)
		var r: float = patch["radius"]
		match int(patch["kind"]):
			Resources.Kind.WATER:
				out.append({"kind": "damp", "position": ground, "radius": r})
				out.append({"kind": "rushes", "position": ground, "radius": r * 0.7})
			Resources.Kind.NITROGEN:
				# A wish deposit's wish names the clover, so clover grows above it.
				if bool(patch.get("wish", false)):
					out.append({"kind": "clover", "position": ground, "radius": r})
					continue
				out.append({"kind": "clover" if clover_turn else "nettles", "position": ground, "radius": r})
				clover_turn = not clover_turn
	for i in range(rock_centers.size()):
		var top := -rock_centers[i].y - rock_radii[i]
		if top < 1.2:
			out.append({"kind": "stones", "position": Vector3(rock_centers[i].x, 0.0, rock_centers[i].z), "radius": rock_radii[i]})
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
	return {"seed": seed, "collected": Marshalls.raw_to_base64(dot_collected),
		"amounts": Marshalls.raw_to_base64(dot_amounts.to_byte_array()), "finds_found": found}


static func from_dict(d: Dictionary) -> Underground:
	var u := Underground.new(int(d.get("seed", 1)))
	# A save from before the wish deposits (0.7) holds fewer dots: the new ones come after the
	# old ids, so the saved state still lines up and the new deposits start full.
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
