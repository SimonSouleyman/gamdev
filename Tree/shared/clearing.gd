class_name Clearing
extends RefCounted
## The living clearing (design doc section 17.6): as the crown grows, the ground below changes by
## itself. Where the crown shades the ground through the day, the sun meadow thins out and shade
## plants come up: wood anemones in the light shade, ferns and moss in the deep shade, and for a
## few days after rain (or a damp morning) mushrooms. What has come up so far is the journal's
## first collection. Pure data: TreeView's Understory draws the plan, the thinned grass and all.
##
## The shade is cast by the leaf clusters (living tips) of the hero tree: each one is projected
## onto the ground along the sun's rays at five times of the day (weighted by the sun's height,
## so the noon shadow counts most), splatted into a grid and blurred; a cell's shade is
## 1 - exp(-OPACITY * cover). The sun stands in the south (+Z) at noon, so the shade lies mostly
## north of the trunk (-Z), widened east and west by the morning and evening sun.
##
## Weather hook: the weather code calls after_rain(day) (GameState.after_rain()) when a shower
## has fallen; until then mushrooms also come up on damp mornings from a seeded chance.

## The kinds in collection order, the journal's names and the diary line on first appearance.
const KINDS: Array[String] = ["anemone", "fern", "moss", "mushroom"]
const NAMES := {
	"anemone": "Wood anemone",
	"fern": "Male fern",
	"moss": "Cushion moss",
	"mushroom": "Brown caps",
}
const NOTES := {
	"anemone": "white stars in the light shade, where the meadow grass gave way",
	"fern": "unrolled from a green curl in the deep shade",
	"moss": "soft cushions where the shade lies all day",
	"mushroom": "come up after rain or a damp night and are gone a few days later",
}
const FIRST_LINES := {
	"anemone": "Wood anemones flower in my shade.",
	"fern": "A fern unrolled in the deep shade.",
	"moss": "Moss creeps over the shaded ground.",
	"mushroom": "Mushrooms came up after the rain.",
}
## Days the ground has lain in the crown's shade before a kind comes up: the change takes time,
## so the collection fills over the tree's month (anemones first, moss last).
const UNLOCK := {"anemone": 1, "fern": 6, "moss": 11, "mushroom": 0}
## Days until the shade plants have spread over all the shade (fewer before).
const SPREAD_DAYS: float = 8.0
## Plants of a kind that must be up before it counts as having appeared.
const MIN_COUNT := {"anemone": 3, "fern": 2, "moss": 2, "mushroom": 1}

## The shade grid: HALF metres around the trunk each way, cells of CELL metres.
const HALF: float = 16.0
const CELL: float = 0.5
const N: int = 64
## Ground a leaf cluster covers (m²) and how much light the cover holds back.
const LEAF_AREA: float = 0.45
const OPACITY: float = 0.55
## A segment inside the crown shades as this share of a leaf cluster (the leaves along it).
const TWIG_SHARE: float = 0.35
## Bare earth right around the trunk (Meadow.BARE_RADIUS): nothing grows there.
const BARE_RADIUS: float = 0.9
## Below this shade the sun meadow stays.
const MIN_SHADE: float = 0.2
## Days mushrooms stand after a rain or a damp morning (that day included).
const MUSHROOM_DAYS: int = 3
## Chance of a damp morning (from the save seed) while the weather does not say otherwise.
const DAMP_CHANCE: float = 0.15
## No damp mornings before this day: the young tree casts no shade worth the name yet.
const FIRST_DAMP_DAY: int = 4

var seed: int = 1
## kind -> day it first came up.
var found: Dictionary = {}
## Days a shower fell (after_rain), the last few only.
var rain_days: Array[int] = []
## The first morning the crown shaded the meadow; -1 before.
var shade_since: int = -1


func _init(random_seed: int = 1) -> void:
	seed = random_seed


# --- weather ----------------------------------------------------------------------

## The weather hook: a shower fell on `day`. Mushrooms come up in the shade for a few days.
func after_rain(day: int) -> void:
	if not rain_days.has(day):
		rain_days.append(day)
	# Only the last days matter; older showers are forgotten.
	for d in rain_days.duplicate():
		if d <= day - 10:
			rain_days.erase(d)


## A damp, misty morning (seeded); mushrooms come up after it as after rain.
func is_damp_morning(day: int) -> bool:
	return day >= FIRST_DAMP_DAY and posmod(hash([seed, "damp", day]), 1000) < int(DAMP_CHANCE * 1000.0)


## The day of the rain or damp morning the mushrooms of `day` stand for; -1 when none stand.
func wet_day(day: int) -> int:
	for d in range(day, day - MUSHROOM_DAYS, -1):
		if rain_days.has(d) or is_damp_morning(d):
			return d
	return -1


func mushrooms_out(day: int) -> bool:
	return wet_day(day) >= 0


# --- shade ------------------------------------------------------------------------

## Sun directions over the day (toward the sun) with weights that sum to 1.
static func sun_samples(noon_elevation: float) -> Array:
	var out: Array = []
	var total := 0.0
	for f in [0.25, 0.375, 0.5, 0.625, 0.75]:
		var angle: float = PI * f
		var arc := sin(angle)
		out.append([Vector3(cos(angle), arc * sin(noon_elevation), arc * cos(noon_elevation)).normalized(), arc])
		total += arc
	for s in out:
		s[1] = s[1] / total
	return out


## The crown's shade on the ground, N x N cells (row-major, z rows), each 0 (full sun) to 1.
static func shade_map(sim: GrowthSim) -> PackedFloat32Array:
	var cover := PackedFloat32Array()
	cover.resize(N * N)
	var g := sim.graph
	var per := LEAF_AREA / (CELL * CELL)
	var samples := sun_samples(sim.clock.noon_elevation)
	for id in range(1, g.size()):
		if g.get_flag(id, "dead", false):
			continue
		var p := g.positions[id]
		# Only the crown casts shade worth the name; a leaf cluster (tip) more than a twig.
		var w := smoothstep(0.8, 2.5, p.y) * (1.0 if g.is_tip(id) else TWIG_SHARE)
		if w <= 0.0:
			continue
		for s in samples:
			var d: Vector3 = s[0]
			var gx := p.x - d.x * p.y / d.y
			var gz := p.z - d.z * p.y / d.y
			_splat(cover, gx, gz, per * w * float(s[1]))
	_blur(cover)
	_blur(cover)
	var out := PackedFloat32Array()
	out.resize(N * N)
	for i in range(N * N):
		out[i] = 1.0 - exp(-OPACITY * cover[i])
	return out


## Bilinear splat of `amount` at ground point (x, z).
static func _splat(grid: PackedFloat32Array, x: float, z: float, amount: float) -> void:
	var fx := (x + HALF) / CELL - 0.5
	var fz := (z + HALF) / CELL - 0.5
	var ix := floori(fx)
	var iz := floori(fz)
	var tx := fx - ix
	var tz := fz - iz
	for c in [[0, 0, (1.0 - tx) * (1.0 - tz)], [1, 0, tx * (1.0 - tz)], [0, 1, (1.0 - tx) * tz], [1, 1, tx * tz]]:
		var cx: int = ix + c[0]
		var cz: int = iz + c[1]
		if cx >= 0 and cz >= 0 and cx < N and cz < N:
			grid[cz * N + cx] += amount * float(c[2])


## One pass of a 3 x 3 box blur (soft shadow edges: the leaves are not points).
static func _blur(grid: PackedFloat32Array) -> void:
	var src := grid.duplicate()
	for z in range(N):
		for x in range(N):
			var sum := 0.0
			var n := 0
			for dz in range(-1, 2):
				for dx in range(-1, 2):
					var cx := x + dx
					var cz := z + dz
					if cx >= 0 and cz >= 0 and cx < N and cz < N:
						sum += src[cz * N + cx]
						n += 1
			grid[z * N + x] = sum / n


## Shade at a ground point, bilinear between cell centres; 0 outside the grid.
static func shade_at(map: PackedFloat32Array, x: float, z: float) -> float:
	if map.size() != N * N:
		return 0.0
	var fx := clampf((x + HALF) / CELL - 0.5, 0.0, N - 1.001)
	var fz := clampf((z + HALF) / CELL - 0.5, 0.0, N - 1.001)
	if absf(x) > HALF or absf(z) > HALF:
		return 0.0
	var ix := floori(fx)
	var iz := floori(fz)
	var tx := fx - ix
	var tz := fz - iz
	var a := lerpf(map[iz * N + ix], map[iz * N + ix + 1], tx)
	var b := lerpf(map[(iz + 1) * N + ix], map[(iz + 1) * N + ix + 1], tx)
	return lerpf(a, b, tz)


# --- what grows -------------------------------------------------------------------

## Where each shade plant stands on `day`: [{"kind", "pos": Vector2 (x, z), "size", "rot"}].
## Each cell keeps its own seeded plants, so they stay put as the shade around them changes;
## deeper shade holds more of them. The deepest cells are served first when `budget` runs out.
func plan(map: PackedFloat32Array, day: int, budget: int, mushroom_budget: int = 30) -> Array:
	var cells: Array = []
	for i in range(map.size()):
		if map[i] >= MIN_SHADE:
			cells.append(i)
	cells.sort_custom(func(a: int, b: int) -> bool: return map[a] > map[b] or (map[a] == map[b] and a < b))
	var out: Array = []
	var age := shade_age(day)
	if age < 0:
		return out
	var spread := clampf((age + 1.0) / SPREAD_DAYS, 0.15, 1.0)
	var off := Vector2(posmod(seed, 97) * 0.13, posmod(seed, 89) * 0.17)
	var wet := wet_day(day)
	var mushrooms := 0
	var plants := 0
	for i in cells:
		if plants >= budget:
			break
		var ix: int = i % N
		var iz: int = i / N
		var s: float = map[i]
		var rng := RandomNumberGenerator.new()
		rng.seed = hash([seed, "understory", ix, iz])
		for _k in range(3):
			var u := rng.randf()
			var pos := Vector2(-HALF + (ix + rng.randf()) * CELL, -HALF + (iz + rng.randf()) * CELL)
			var size := rng.randf_range(0.75, 1.25)
			var rot := rng.randf() * TAU
			var roll := rng.randf()
			var young := rng.randf()
			if s < MIN_SHADE + 0.4 * u or pos.length() < BARE_RADIUS + 0.15 or young > spread:
				continue
			var kind := _kind(pos, s, roll, off)
			# A kind whose time has not come yet leaves the spot to the one before it.
			while kind != "" and age < int(UNLOCK[kind]):
				kind = {"moss": "fern", "fern": "anemone", "anemone": ""}[kind]
			if kind != "" and plants < budget:
				out.append({"kind": kind, "pos": pos, "size": size, "rot": rot})
				plants += 1
		if wet >= 0 and s > 0.35 and mushrooms < mushroom_budget:
			var m := RandomNumberGenerator.new()
			m.seed = hash([seed, "mushroom", ix, iz, wet])
			if m.randf() < 0.07:
				var pos := Vector2(-HALF + (ix + m.randf()) * CELL, -HALF + (iz + m.randf()) * CELL)
				if pos.length() >= BARE_RADIUS + 0.15:
					out.append({"kind": "mushroom", "pos": pos, "size": m.randf_range(0.7, 1.2), "rot": m.randf() * TAU})
					mushrooms += 1
	return out


## Anemones carpet the light shade in drifts; ferns and moss take the deep shade in patches.
## "" leaves the spot to the leaf litter.
static func _kind(pos: Vector2, shade: float, roll: float, off: Vector2) -> String:
	var patch := sin(pos.x * 0.9 + off.x) * cos(pos.y * 0.8 + off.y) + (roll - 0.5) * 0.5
	if shade > 0.55 and patch > 0.3:
		return "fern"
	if shade > 0.5 and patch < -0.35:
		return "moss"
	if shade > 0.85 and roll < 0.4:
		return "moss"
	# Anemones spread in drifts, with bare leaf litter between them.
	var drift := sin(pos.x * 0.55 - off.y) * cos(pos.y * 0.6 + off.x) + (roll - 0.5) * 0.6
	return "anemone" if drift > -0.15 else ""


## Days the ground has lain in shade by `day` (0 on the first morning); -1 before.
func shade_age(day: int) -> int:
	return -1 if shade_since < 0 or day < shade_since else day - shade_since


## Some ground outside the bare earth lies in real shade.
static func has_shade(map: PackedFloat32Array) -> bool:
	for i in range(map.size()):
		if map[i] >= MIN_SHADE + 0.1:
			var p := Vector2(-HALF + (i % N + 0.5) * CELL, -HALF + (i / N + 0.5) * CELL)
			if p.length() > BARE_RADIUS + 0.3:
				return true
	return false


## Counts per kind in a plan.
static func counts(p: Array) -> Dictionary:
	var out := {}
	for k in KINDS:
		out[k] = 0
	for e in p:
		out[e["kind"]] = int(out[e["kind"]]) + 1
	return out


## Looks at what grows today and records the kinds that came up for the first time.
## Returns them (GameState writes the diary lines).
func update(sim: GrowthSim, day: int) -> Array[String]:
	var map := shade_map(sim)
	if shade_since < 0 and has_shade(map):
		shade_since = day
	var c := counts(plan(map, day, 4000))
	var out: Array[String] = []
	for k in KINDS:
		if not found.has(k) and int(c[k]) >= int(MIN_COUNT[k]):
			found[k] = day
			out.append(k)
	return out


func to_dict() -> Dictionary:
	return {"found": found, "rain_days": rain_days, "shade_since": shade_since}


static func from_dict(d: Dictionary, random_seed: int) -> Clearing:
	var c := Clearing.new(random_seed)
	var f: Dictionary = d.get("found", {})
	for k in f:
		if KINDS.has(str(k)):
			c.found[str(k)] = int(f[k])
	c.shade_since = int(d.get("shade_since", -1))
	for r in d.get("rain_days", []):
		c.rain_days.append(int(r))
	return c
