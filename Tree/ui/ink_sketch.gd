class_name InkSketch
extends RefCounted
## Small ink drawings for the diary (0.7): a tuft of rushes for a reached damp patch, a clover
## for a reached nitrogen patch; from 0.8 a nettle for phosphorus and comfrey for potassium (0.8
## review: stones only ever mean rock). From 0.8.2 one doodle for every journal page (its tool,
## visitor, find or the leaf of a need) and a seed or leaf per species for the seed bag. Drawn in
## code with soft pen strokes, once, then cached.

const SIZE: int = 128
static var _cache: Dictionary = {}


## The drawing as shown in the journal: the pen a little bolder, so it still reads at the small
## sizes of the day pages and the seed bag (0.8.2; drawn at SIZE, shown at 56 to 104 px).
static func texture(kind: String) -> Texture2D:
	if not _cache.has(kind):
		var img: Image = null
		if _warming.has(kind):
			# Drawn (or being drawn) on a worker thread by warm(): wait for it.
			WorkerThreadPool.wait_for_task_completion(int(_warming[kind]))
			_warming.erase(kind)
			_mutex.lock()
			img = _warmed.get(kind)
			_warmed.erase(kind)
			_mutex.unlock()
		if img == null:
			img = _journal_image(kind)
		_cache[kind] = ImageTexture.create_from_image(img)
	return _cache[kind]


## Draws these kinds' doodles ahead on worker threads (0.8.2.4: the journal's first open drew
## them all on the main thread, 0.49 s on the phone); texture() picks them up.
static var _warming: Dictionary = {}
static var _warmed: Dictionary = {}
static var _mutex := Mutex.new()


static func warm(kinds: Array) -> void:
	for kind in kinds:
		var k := str(kind)
		if k == "" or _cache.has(k) or _warming.has(k) or not has(k):
			continue
		_warming[k] = WorkerThreadPool.add_task(_warm_one.bind(k), false, "ink sketch")


static func _warm_one(kind: String) -> void:
	var img := _journal_image(kind)
	_mutex.lock()
	_warmed[kind] = img
	_mutex.unlock()


static func _journal_image(kind: String) -> Image:
	var img := _bolder(image(kind))
	img.generate_mipmaps()
	return img


## Each pixel takes the strongest ink of its neighbours (a max over 3 x 3, done as a row pass
## and a column pass on the alpha bytes): strokes one pixel wider on each side.
static func _bolder(src: Image) -> Image:
	var w := src.get_width()
	var h := src.get_height()
	var data := src.get_data()
	var a := PackedByteArray()
	a.resize(w * h)
	for i in range(w * h):
		a[i] = data[i * 4 + 3]
	var rows := a.duplicate()
	for y in range(h):
		var o := y * w
		for x in range(1, w - 1):
			rows[o + x] = maxi(a[o + x], maxi(a[o + x - 1], a[o + x + 1]))
	var c := Color(Paper.INK)
	var r8 := c.r8
	var g8 := c.g8
	var b8 := c.b8
	for y in range(h):
		for x in range(w):
			var i := y * w + x
			var m := rows[i]
			if y > 0:
				m = maxi(m, rows[i - w])
			if y < h - 1:
				m = maxi(m, rows[i + w])
			data[i * 4] = r8
			data[i * 4 + 1] = g8
			data[i * 4 + 2] = b8
			data[i * 4 + 3] = m
	return Image.create_from_data(w, h, false, Image.FORMAT_RGBA8, data)


static func image(kind: String) -> Image:
	var img := Image.create(SIZE, SIZE, false, Image.FORMAT_RGBA8)
	img.fill(Color(Paper.INK, 0.0))
	match kind:
		"rushes":
			_rushes(img)
		"clover":
			_clover(img)
		"hedgehog":
			_hedgehog(img)
		"nettles":
			_nettles(img)
		"comfrey":
			_comfrey(img)
		"seed_linden": _seed_linden(img)
		"seed_birch": _seed_birch(img)
		"seed_beech": _seed_beech(img)
		"seed_sycamore": _seed_sycamore(img)
		"seed_alder": _seed_alder(img)
		"seed_oak": _seed_oak(img)
		"seed_juniper": _seed_juniper(img)
		"seed": _seed(img)
		"root": _root(img)
		"fine_roots": _fine_roots(img)
		"sapling": _sapling(img)
		"leaf_sun": _leaf_sun(img)
		"sunset": _sunset(img)
		"root_fork": _root_fork(img)
		"shears": _shears(img)
		"grown_tree": _grown_tree(img)
		"bonsai": _bonsai(img)
		"can": _can(img)
		"tin": _tin(img)
		"tweezers": _tweezers(img)
		"wire": _wire(img)
		"trowel": _trowel(img)
		"moon": _moon(img)
		"feather": _feather(img)
		"leaf_ok", "leaf_water", "leaf_n", "leaf_p", "leaf_k", "leaf_burnt": _leaf_need(img, kind)
		"butterflies": _butterfly(img)
		"nest": _nest(img)
		"fox": _fox(img)
		"wren": _wren(img)
		"fossil": _fossil(img)
		"old_root": _old_root(img)
		"water_vein": _water_vein(img)
		"coin": _coin(img)
		"blossom": _blossom(img)
		"anemone": _anemone(img)
		"fern": _fern(img)
		"moss": _moss(img)
		"mushroom": _mushroom(img)
		"pile": _pile(img)
		"rain": _rain(img)
		"mist": _mist(img)
		"sun": _sun_only(img)
		"book": _book(img)
		"cutting_juniper", "cutting_linden", "cutting_birch", "cutting_beech", "cutting_sycamore", "cutting_alder", "cutting_oak":
			_cutting(img, kind.substr(8))
	return img


## Every drawing there is (0.8.2: one per page; tests check each is drawn and every page has one).
const KINDS: Array[String] = ["rushes", "clover", "hedgehog", "nettles", "comfrey",
	"seed_linden", "seed_birch", "seed_beech", "seed_sycamore", "seed_alder", "seed_oak", "seed_juniper",
	"seed", "root", "fine_roots", "sapling", "leaf_sun", "sunset", "root_fork", "shears", "grown_tree",
	"bonsai", "can", "tin", "tweezers", "wire", "trowel", "moon", "feather",
	"leaf_ok", "leaf_water", "leaf_n", "leaf_p", "leaf_k", "leaf_burnt",
	"butterflies", "nest", "fox", "wren", "fossil", "old_root", "water_vein", "coin",
	"blossom", "anemone", "fern", "moss", "mushroom", "pile", "rain", "mist", "sun", "book",
	"cutting_juniper", "cutting_linden", "cutting_birch", "cutting_beech", "cutting_sycamore", "cutting_alder", "cutting_oak"]


static func has(kind: String) -> bool:
	return KINDS.has(kind)


## The seed or leaf of a species (0.8.2, item 19): the seed bag's list and the species' page.
static func species_kind(species_id: String) -> String:
	return "seed_" + species_id


## A cutting of a species in its little pot (0.8.2.2: the bonsai's cuttings page, one each).
static func cutting_kind(species_id: String) -> String:
	return "cutting_" + species_id


## The hedgehog at the brush pile (0.8): side on, snout to the left, spines swept back over a
## round back, a few sticks of the pile behind it.
static func _hedgehog(img: Image) -> void:
	# The ground and two sticks of the pile behind, to the right.
	_curve(img, [Vector2(8, 112), Vector2(60, 110), Vector2(122, 113)], 2.0)
	_stroke(img, Vector2(84, 108), Vector2(124, 78), 2.2, 1.4)
	_stroke(img, Vector2(96, 110), Vector2(126, 96), 1.8, 1.2)
	# The belly line and the face: a pointed snout with a dark nose.
	_curve(img, [Vector2(96, 100), Vector2(66, 106), Vector2(38, 102), Vector2(22, 92), Vector2(12, 86)], 2.0)
	_curve(img, [Vector2(12, 86), Vector2(20, 80), Vector2(34, 74)], 2.0)
	_blob(img, Vector2(11, 86), Vector2(3.2, 2.8))
	_blob(img, Vector2(27, 80), Vector2(1.8, 1.8))
	# The back: an arc of spines, each a short stroke swept toward the rump.
	var c := Vector2(66, 100)
	for k in range(30):
		var a := PI + 0.35 + (PI - 0.55) * k / 29.0
		var root := c + Vector2(cos(a) * 34.0, sin(a) * 30.0)
		var dir := Vector2(cos(a), sin(a)) * 0.7 + Vector2(0.75, 0.0)
		_stroke(img, root, root + dir.normalized() * 10.0, 1.6, 0.8)
	_curve(img, [Vector2(34, 76), Vector2(52, 68), Vector2(78, 68), Vector2(96, 80), Vector2(100, 98)], 1.6)
	# Small feet.
	for x in [44.0, 80.0]:
		_stroke(img, Vector2(x, 104), Vector2(x - 3, 110), 1.8)


static func _nettles(img: Image) -> void:
	# An upright stem with pairs of pointed, toothed leaves, and a drooping flower tassel.
	_curve(img, [Vector2(18, 116), Vector2(64, 112), Vector2(112, 116)], 2.2)
	_curve(img, [Vector2(62, 113), Vector2(60, 70), Vector2(63, 16)], 2.6, 1.2)
	for i in range(3):
		var y := 92.0 - i * 26.0
		var size := 26.0 - i * 5.0
		for side in [-1.0, 1.0]:
			var base := Vector2(61.5, y)
			var tip := base + Vector2(side * size, -size * 0.55)
			var mid := base.lerp(tip, 0.5)
			var n := Vector2(tip.y - base.y, base.x - tip.x).normalized() * size * 0.28
			_curve(img, [base, mid + n, tip], 1.8, 1.0)
			_curve(img, [base, mid - n, tip], 1.8, 1.0)
			# Teeth: short ticks along the outer edge.
			for t in [0.35, 0.6, 0.82]:
				var e: Vector2 = base.lerp(tip, t) + n * (1.0 - absf(t - 0.5)) * 0.9
				_stroke(img, e, e + (tip - base).normalized() * 3.5 + n.normalized() * 2.0, 1.2)
	_curve(img, [Vector2(63, 60), Vector2(80, 64), Vector2(90, 78)], 1.4, 0.8)
	for k in range(5):
		_blob(img, Vector2(70.0 + k * 4.5, 62.0 + k * k * 0.7), Vector2(1.6, 1.6))


static func _comfrey(img: Image) -> void:
	# Comfrey: big, rough, pointed leaves arching out of one clump, and a leafy stem with a curled
	# spray of hanging bell flowers.
	_curve(img, [Vector2(10, 116), Vector2(64, 112), Vector2(118, 116)], 2.2)
	var base := Vector2(62, 112)
	for leaf: Array in [[Vector2(14, 84), 0.9], [Vector2(30, 66), 1.0], [Vector2(96, 70), 1.0], [Vector2(114, 90), 0.85]]:
		var tip: Vector2 = leaf[0]
		var w: float = 11.0 * float(leaf[1])
		var mid := base.lerp(tip, 0.5) + Vector2(0, -8)
		var n := Vector2(tip.y - base.y, base.x - tip.x).normalized() * w
		_curve(img, [base, mid + n, tip], 2.0, 1.0)
		_curve(img, [base, mid - n * 0.8, tip], 2.0, 1.0)
		# The midrib.
		_curve(img, [base, mid, tip], 1.2, 0.6)
	# The flowering stem with two small leaves, curling over at the top.
	_curve(img, [Vector2(62, 112), Vector2(60, 70), Vector2(64, 40), Vector2(78, 26), Vector2(90, 30)], 2.4, 1.2)
	for side in [-1.0, 1.0]:
		var at := Vector2(61, 66)
		_curve(img, [at, at + Vector2(side * 12.0, -10.0), at + Vector2(side * 20.0, -6.0)], 1.6, 0.8)
	# Hanging bells along the curl, each on a short stalk: narrow at the top, flared at the mouth.
	for p in [Vector2(72, 30), Vector2(81, 28), Vector2(89, 32), Vector2(95, 39)]:
		var top: Vector2 = p + Vector2(0.5, 4.0)
		_stroke(img, p, top, 1.2)
		_curve(img, [top, top + Vector2(-2.6, 5.0), top + Vector2(-4.2, 11.0)], 1.6, 1.2)
		_curve(img, [top, top + Vector2(2.6, 5.0), top + Vector2(4.2, 11.0)], 1.6, 1.2)
		_curve(img, [top + Vector2(-4.2, 11.0), top + Vector2(0.0, 12.6), top + Vector2(4.2, 11.0)], 1.4)


static func _rushes(img: Image) -> void:
	# Damp ground: a wavering line with a few short ticks of water below it.
	_curve(img, [Vector2(10, 112), Vector2(40, 108), Vector2(78, 113), Vector2(118, 109)], 2.4)
	for x in [28.0, 58.0, 92.0]:
		_stroke(img, Vector2(x, 119), Vector2(x + 9, 119), 1.6)
	# Blades fanning out from one tuft, each a gentle curve.
	var base := Vector2(62, 110)
	var tips := [Vector2(20, 30), Vector2(40, 14), Vector2(64, 8), Vector2(88, 18), Vector2(110, 40), Vector2(52, 26)]
	for tip in tips:
		var mid: Vector2 = base.lerp(tip, 0.5) + Vector2((tip.x - base.x) * 0.12, 6.0)
		_curve(img, [base, mid, tip], 2.2, 1.0)
	# Two seed heads: small dark ovals near the top of two blades.
	_blob(img, Vector2(44, 30), Vector2(3.2, 7.0))
	_blob(img, Vector2(85, 28), Vector2(3.2, 7.0))


static func _clover(img: Image) -> void:
	# A stem from the ground up to three round leaflets, and a small flower head beside it.
	_curve(img, [Vector2(20, 116), Vector2(64, 112), Vector2(110, 116)], 2.2)
	_curve(img, [Vector2(58, 113), Vector2(54, 88), Vector2(58, 62)], 2.4, 1.0)
	var c := Vector2(58, 50)
	for k in range(3):
		var a := -PI * 0.5 + k * TAU / 3.0
		var dir := Vector2(cos(a), sin(a))
		var side := Vector2(-dir.y, dir.x)
		# Each leaflet is two small lobes, like a heart pointing inward.
		_ring(img, c + dir * 17.0 + side * 7.0, 9.5, 2.0)
		_ring(img, c + dir * 17.0 - side * 7.0, 9.5, 2.0)
		_stroke(img, c, c + dir * 20.0, 1.4)
	# The flower head on its own stalk: a ring of small florets.
	_curve(img, [Vector2(62, 110), Vector2(84, 90), Vector2(92, 64)], 2.0, 1.0)
	var f := Vector2(94, 52)
	_ring(img, f, 10.0, 1.8)
	for k in range(7):
		var a := k * TAU / 7.0
		_blob(img, f + Vector2(cos(a), sin(a)) * 5.5, Vector2(1.8, 1.8))


## A pen line from a to b, `width` pixels wide, anti-aliased.
static func _stroke(img: Image, a: Vector2, b: Vector2, width: float, width_b: float = -1.0) -> void:
	if width_b < 0.0:
		width_b = width
	var steps := int(a.distance_to(b)) + 1
	for s in range(steps + 1):
		var k := float(s) / steps
		_dab(img, a.lerp(b, k), lerpf(width, width_b, k) * 0.5)


## A smooth curve through `points` (Catmull-Rom), tapering to `end_width` if given.
static func _curve(img: Image, points: Array, width: float, end_width: float = -1.0) -> void:
	if end_width < 0.0:
		end_width = width
	var pts: Array = [points[0]] + points + [points[-1]]
	var segments := points.size() - 1
	var prev: Vector2 = points[0]
	for i in range(segments):
		for s in range(1, 13):
			var tt := float(s) / 12.0
			var p := _catmull(pts[i], pts[i + 1], pts[i + 2], pts[i + 3], tt)
			var k := (i + tt) / segments
			var k0 := (i + (s - 1) / 12.0) / segments
			_stroke(img, prev, p, lerpf(width, end_width, k0), lerpf(width, end_width, k))
			prev = p


static func _catmull(p0: Vector2, p1: Vector2, p2: Vector2, p3: Vector2, tt: float) -> Vector2:
	var t2 := tt * tt
	var t3 := t2 * tt
	return 0.5 * ((2.0 * p1) + (-p0 + p2) * tt + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3)


static func _ring(img: Image, c: Vector2, r: float, width: float) -> void:
	var steps := int(TAU * r) + 4
	for s in range(steps):
		var a := s * TAU / steps
		_dab(img, c + Vector2(cos(a), sin(a)) * r, width * 0.5)


static func _blob(img: Image, c: Vector2, r: Vector2) -> void:
	for y in range(int(c.y - r.y) - 1, int(c.y + r.y) + 2):
		for x in range(int(c.x - r.x) - 1, int(c.x + r.x) + 2):
			var q := Vector2(x + 0.5 - c.x, y + 0.5 - c.y)
			var d := sqrt(pow(q.x / r.x, 2.0) + pow(q.y / r.y, 2.0))
			_ink(img, x, y, clampf((1.0 - d) * 3.0, 0.0, 1.0))


## A round pen dab of radius `r` at `p`.
static func _dab(img: Image, p: Vector2, r: float) -> void:
	for y in range(int(p.y - r) - 1, int(p.y + r) + 2):
		for x in range(int(p.x - r) - 1, int(p.x + r) + 2):
			var d := Vector2(x + 0.5, y + 0.5).distance_to(p)
			_ink(img, x, y, clampf(r + 0.5 - d, 0.0, 1.0))


static func _ink(img: Image, x: int, y: int, a: float) -> void:
	if a <= 0.0 or x < 0 or y < 0 or x >= img.get_width() or y >= img.get_height():
		return
	var old := img.get_pixel(x, y)
	if a > old.a:
		img.set_pixel(x, y, Color(Paper.INK, a))


# --- 0.8.2: a doodle for every page, a seed or leaf for every species --------------------

## A leaf from `base` to `tip`, `w` wide at its middle: two edges, a midrib and `veins` pairs of
## side veins. `teeth` adds small ticks along the edges; `heart` gives it a notched heart base.
static func _leaf(img: Image, base: Vector2, tip: Vector2, w: float, veins: int = 3, teeth: bool = false, heart: bool = false, pen: float = 1.8) -> void:
	var along := tip - base
	var n := Vector2(-along.y, along.x).normalized()
	var mid := base.lerp(tip, 0.45)
	for side in [-1.0, 1.0]:
		var pts: Array = [base]
		if heart:
			pts.append(base + n * side * w * 0.55 - along * 0.06)
		pts.append(mid + n * side * w)
		pts.append(base.lerp(tip, 0.8) + n * side * w * 0.45)
		pts.append(tip)
		_curve(img, pts, pen, pen * 0.6)
		if teeth:
			for k in range(1, 7):
				var t := k / 7.0
				var e: Vector2 = base.lerp(tip, t) + n * side * w * sin(t * PI) * (0.95 if t < 0.6 else 0.9)
				_stroke(img, e, e + (along.normalized() * 2.5 + n * side * 2.0), 1.1)
	_curve(img, [base - along * 0.12, base, tip], pen * 0.75, 0.6)
	for k in range(veins):
		var t := (k + 1.0) / (veins + 1.0)
		var at := base.lerp(tip, t * 0.85)
		for side in [-1.0, 1.0]:
			var out: Vector2 = at + along * 0.12 + n * side * w * sin((t * 0.85 + 0.1) * PI) * 0.75
			_stroke(img, at, out, 1.0, 0.6)


## Light hatching inside a circle-ish area (shade, a dark leaf, a cup).
static func _hatch(img: Image, c: Vector2, r: Vector2, gap: float = 5.0, pen: float = 1.0) -> void:
	var y := -r.y + gap * 0.5
	while y < r.y:
		var half := r.x * sqrt(maxf(0.0, 1.0 - pow(y / r.y, 2.0)))
		_stroke(img, c + Vector2(-half + y * 0.3, y), c + Vector2(half * 0.85 + y * 0.3, y - half * 0.3), pen)
		y += gap


static func _ground(img: Image, y: float = 112.0) -> void:
	_curve(img, [Vector2(10, y + 2), Vector2(50, y - 1), Vector2(90, y + 1), Vector2(118, y - 1)], 2.0)


static func _sun(img: Image, c: Vector2, r: float, rays: int = 9, from: float = 0.0, to: float = TAU) -> void:
	_ring(img, c, r, 2.0)
	for k in range(rays):
		var a := from + (to - from) * (k + 0.5) / rays
		var d := Vector2(cos(a), sin(a))
		_stroke(img, c + d * (r + 4.0), c + d * (r + 11.0), 1.8, 1.0)


# The species: each seed or leaf as on the seed bag's list and its journal page.

static func _seed_linden(img: Image) -> void:
	# A heart-shaped leaf, and the bract with its hanging cluster of round nutlets.
	_leaf(img, Vector2(40, 104), Vector2(28, 30), 22.0, 3, true, true)
	_curve(img, [Vector2(78, 20), Vector2(92, 46), Vector2(98, 84)], 1.8)
	_curve(img, [Vector2(78, 20), Vector2(104, 40), Vector2(108, 80), Vector2(98, 84)], 1.8)
	_curve(img, [Vector2(90, 50), Vector2(80, 76), Vector2(76, 92)], 1.4)
	for p in [Vector2(70, 100), Vector2(80, 104), Vector2(75, 112)]:
		_ring(img, p, 4.6, 1.8)
		_stroke(img, Vector2(76, 92), p + Vector2(0, -4.0), 1.0)


static func _seed_birch(img: Image) -> void:
	# A small toothed, pointed leaf, and a catkin hanging from its twig.
	_curve(img, [Vector2(10, 22), Vector2(60, 18), Vector2(118, 28)], 2.0, 1.2)
	_leaf(img, Vector2(36, 22), Vector2(28, 96), 18.0, 3, true)
	_curve(img, [Vector2(86, 24), Vector2(90, 40)], 1.4)
	for k in range(9):
		var c := Vector2(90.0 + sin(k * 0.5) * 2.0, 44.0 + k * 7.0)
		_ring(img, c, 4.8 - k * 0.18, 1.5)


static func _seed_beech(img: Image) -> void:
	# A glossy oval leaf with straight parallel veins, and the opened, bristly husk with its
	# three-sided nut.
	_leaf(img, Vector2(28, 108), Vector2(46, 26), 17.0, 6)
	var c := Vector2(88, 86)
	for side in [-1.0, 1.0]:
		var tip := c + Vector2(side * 26.0, -10.0)
		_curve(img, [c + Vector2(0, 18), c + Vector2(side * 18.0, 12.0), tip], 1.8)
		for k in range(5):
			var p: Vector2 = (c + Vector2(0, 18)).lerp(tip, 0.2 + k * 0.17) + Vector2(side * 2.0, 2.0)
			_stroke(img, p, p + Vector2(side * 4.0, 3.5), 1.0)
	_curve(img, [c + Vector2(-10, 8), c + Vector2(0, -34), c + Vector2(10, 8)], 2.0)
	_stroke(img, c + Vector2(0, -32), c + Vector2(0, 6), 1.0)


static func _seed_sycamore(img: Image) -> void:
	# The paired winged fruit: two seeds joined at a stalk, their wings spread in a narrow V.
	var j := Vector2(64, 92)
	for side in [-1.0, 1.0]:
		var seed := j + Vector2(side * 9.0, -2.0)
		_blob(img, seed, Vector2(6.0, 5.0))
		var tip := j + Vector2(side * 44.0, -66.0)
		_curve(img, [seed + Vector2(side * 4.0, -3.0), j + Vector2(side * 18.0, -40.0), tip], 2.0, 1.4)
		_curve(img, [seed + Vector2(-side * 2.0, -5.0), j + Vector2(side * 4.0, -46.0), tip + Vector2(-side * 10.0, -6.0), tip], 1.8, 1.2)
		for k in range(4):
			var a: Vector2 = seed.lerp(tip, 0.3 + k * 0.15)
			_stroke(img, a, a + Vector2(-side * 6.0, -6.0), 0.9)
	_curve(img, [j + Vector2(0, 2), Vector2(66, 108), Vector2(62, 120)], 1.6)


static func _seed_alder(img: Image) -> void:
	# A round leaf with a notched tip, and two small woody cones on their stalks.
	_leaf(img, Vector2(36, 110), Vector2(42, 40), 24.0, 4, true)
	_stroke(img, Vector2(37, 42), Vector2(42, 48), 2.0)
	_stroke(img, Vector2(47, 42), Vector2(42, 48), 2.0)
	_curve(img, [Vector2(118, 14), Vector2(98, 30), Vector2(88, 52)], 1.6)
	for c in [Vector2(80, 70), Vector2(102, 62)]:
		_stroke(img, Vector2(96, 34), c + Vector2(0, -10), 1.2)
		_ring(img, c, 9.0, 1.8)
		for k in range(3):
			_stroke(img, c + Vector2(-6, -4 + k * 5), c + Vector2(6, -6 + k * 5), 1.0)


static func _seed_oak(img: Image) -> void:
	# An acorn in its scaly cup on a long stalk, and a wavy lobed leaf behind it.
	var base := Vector2(36, 112)
	var tip := Vector2(38, 26)
	for side in [-1.0, 1.0]:
		var pts: Array = [base]
		for k in range(1, 6):
			var t := k / 6.0
			var w := (11.0 if k % 2 == 1 else 5.0) + 7.0 * sin(t * PI)
			pts.append(base.lerp(tip, t) + Vector2(side * w, 0))
		pts.append(tip)
		_curve(img, pts, 1.8, 1.0)
	_curve(img, [base + Vector2(0, 6), base, tip], 1.3, 0.6)
	var c := Vector2(86, 82)
	_curve(img, [c + Vector2(-13, 0), c + Vector2(-12, 22), c + Vector2(0, 32), c + Vector2(12, 22), c + Vector2(13, 0)], 2.0)
	_curve(img, [c + Vector2(-16, 0), c + Vector2(0, -6), c + Vector2(16, 0)], 2.0)
	_curve(img, [c + Vector2(-16, 0), c + Vector2(0, 7), c + Vector2(16, 0)], 2.0)
	_hatch(img, c + Vector2(0, 0.5), Vector2(14.0, 4.0), 3.0, 0.9)
	_curve(img, [c + Vector2(0, -6), Vector2(88, 50), Vector2(100, 26), Vector2(112, 12)], 1.6)
	_blob(img, c + Vector2(0, 33), Vector2(1.6, 1.6))


static func _seed_juniper(img: Image) -> void:
	# A sprig of tiny scale leaves, and three round berries with their bloom.
	_curve(img, [Vector2(16, 116), Vector2(46, 80), Vector2(70, 40), Vector2(84, 14)], 2.0, 1.2)
	for branch in [[Vector2(36, 92), Vector2(14, 70)], [Vector2(54, 68), Vector2(92, 52)], [Vector2(64, 50), Vector2(42, 26)]]:
		var a: Vector2 = branch[0]
		var b: Vector2 = branch[1]
		_stroke(img, a, b, 1.6, 1.0)
		for k in range(6):
			var p: Vector2 = a.lerp(b, (k + 0.5) / 6.0)
			var d := (b - a).normalized()
			_stroke(img, p, p + d * 4.0 + Vector2(-d.y, d.x) * 3.0, 1.0)
			_stroke(img, p, p + d * 4.0 - Vector2(-d.y, d.x) * 3.0, 1.0)
	for p in [Vector2(88, 84), Vector2(102, 92), Vector2(92, 100)]:
		_ring(img, p, 6.5, 1.8)
		_blob(img, p + Vector2(-2, -2), Vector2(1.4, 1.4))


# The tutorial and bonsai pages.

static func _seed(img: Image) -> void:
	# A seed in the soil with a first root down and a pale sprout curling up.
	_ground(img, 60.0)
	for x in [24.0, 50.0, 96.0]:
		_stroke(img, Vector2(x, 70), Vector2(x + 6, 70), 1.2)
	_ring(img, Vector2(64, 84), 9.0, 2.0)
	_blob(img, Vector2(64, 84), Vector2(3.0, 5.0))
	_curve(img, [Vector2(66, 94), Vector2(62, 106), Vector2(68, 120)], 1.8, 0.8)
	_curve(img, [Vector2(62, 76), Vector2(58, 56), Vector2(64, 36), Vector2(74, 34)], 1.8, 1.2)
	_leaf(img, Vector2(64, 38), Vector2(84, 26), 6.0, 0)


static func _root(img: Image) -> void:
	# A root reaching down from the trunk's foot, the glowing dots around it.
	_ground(img, 18.0)
	_curve(img, [Vector2(60, 18), Vector2(58, 50), Vector2(70, 80), Vector2(64, 116)], 3.0, 1.0)
	for p in [Vector2(30, 52), Vector2(96, 44), Vector2(92, 90), Vector2(36, 98)]:
		_ring(img, p, 6.0, 1.6)
		_blob(img, p, Vector2(2.4, 2.4))


static func _fine_roots(img: Image) -> void:
	_ground(img, 18.0)
	_curve(img, [Vector2(60, 18), Vector2(58, 50), Vector2(70, 80), Vector2(64, 116)], 3.0, 1.0)
	for k in range(9):
		var y := 34.0 + k * 9.0
		var x := 60.0 + sin(y * 0.05) * 6.0
		var side := -1.0 if k % 2 == 0 else 1.0
		_curve(img, [Vector2(x, y), Vector2(x + side * 12.0, y + 4.0), Vector2(x + side * 22.0, y + 12.0)], 1.2, 0.6)


static func _sapling(img: Image) -> void:
	_ground(img)
	_curve(img, [Vector2(60, 112), Vector2(62, 80), Vector2(58, 50)], 2.4, 1.4)
	_leaf(img, Vector2(61, 86), Vector2(36, 72), 8.0, 1)
	_leaf(img, Vector2(61, 70), Vector2(86, 56), 8.0, 1)
	_leaf(img, Vector2(58, 52), Vector2(48, 30), 7.0, 1)
	_sun(img, Vector2(102, 26), 10.0, 8)


static func _leaf_sun(img: Image) -> void:
	_leaf(img, Vector2(28, 112), Vector2(74, 44), 18.0, 4)
	_sun(img, Vector2(98, 28), 12.0, 9)


static func _sunset(img: Image) -> void:
	# The sun half down behind the meadow, and two birds going home.
	_ground(img, 84.0)
	_curve(img, [Vector2(40, 84), Vector2(42, 70), Vector2(64, 60), Vector2(86, 70), Vector2(88, 84)], 2.0)
	for k in range(7):
		var a := PI + PI * (k + 0.5) / 7.0
		var d := Vector2(cos(a), sin(a))
		_stroke(img, Vector2(64, 84) + d * 30.0, Vector2(64, 84) + d * 40.0, 1.8, 1.0)
	for p in [Vector2(28, 30), Vector2(44, 22)]:
		_curve(img, [p + Vector2(-7, 2), p + Vector2(-3, -2), p], 1.4)
		_curve(img, [p, p + Vector2(3, -2), p + Vector2(7, 2)], 1.4)
	for x in [20.0, 70.0, 104.0]:
		_stroke(img, Vector2(x, 100), Vector2(x + 10, 100), 1.2)


static func _root_fork(img: Image) -> void:
	# An old root with a ring where the new one starts, and the new root setting off.
	_curve(img, [Vector2(8, 40), Vector2(50, 46), Vector2(90, 40), Vector2(122, 50)], 3.0, 2.0)
	_ring(img, Vector2(62, 45), 9.0, 1.6)
	_curve(img, [Vector2(62, 52), Vector2(66, 80), Vector2(84, 112)], 2.2, 0.8)
	for d in [Vector2(-1, -1), Vector2(1, -1), Vector2(-1, 1), Vector2(1, 1)]:
		_stroke(img, Vector2(62, 45) + d * 10.0, Vector2(62, 45) + d * 15.0, 1.2)


static func _shears(img: Image) -> void:
	# Garden shears, open: two crossed curved blades and their ring handles.
	var pivot := Vector2(64, 60)
	_curve(img, [Vector2(18, 108), Vector2(48, 78), pivot, Vector2(92, 26), Vector2(112, 12)], 2.2, 1.2)
	_curve(img, [Vector2(108, 108), Vector2(80, 78), pivot, Vector2(36, 28), Vector2(20, 14)], 2.2, 1.2)
	_curve(img, [pivot, Vector2(84, 36), Vector2(104, 18)], 1.4, 0.8)
	_ring(img, Vector2(14, 112), 9.0, 2.0)
	_ring(img, Vector2(114, 112), 9.0, 2.0)
	_blob(img, pivot, Vector2(3.0, 3.0))


static func _grown_tree(img: Image) -> void:
	_ground(img, 114.0)
	_curve(img, [Vector2(58, 114), Vector2(60, 86), Vector2(56, 64)], 3.4, 2.4)
	_curve(img, [Vector2(60, 84), Vector2(78, 66)], 2.0, 1.2)
	var c := Vector2(60, 46)
	var pts: Array = []
	for k in range(13):
		var a := k * TAU / 12.0
		var r := 34.0 + (6.0 if k % 2 == 0 else 0.0)
		pts.append(c + Vector2(cos(a) * r * 1.2, sin(a) * r * 0.85))
	_curve(img, pts, 1.8)
	# A winged seed spinning down beside it.
	_curve(img, [Vector2(104, 92), Vector2(112, 82), Vector2(118, 86)], 1.4)
	_blob(img, Vector2(104, 93), Vector2(2.4, 2.0))


static func _bonsai(img: Image) -> void:
	# A shallow pot, a curved trunk and two cloud pads.
	_curve(img, [Vector2(30, 98), Vector2(98, 98)], 2.2)
	_curve(img, [Vector2(34, 98), Vector2(38, 116), Vector2(90, 116), Vector2(94, 98)], 2.0)
	_curve(img, [Vector2(60, 98), Vector2(54, 80), Vector2(66, 64), Vector2(58, 46)], 3.0, 1.8)
	_curve(img, [Vector2(64, 68), Vector2(84, 58)], 1.8, 1.2)
	for pad in [[Vector2(58, 36), 24.0], [Vector2(90, 52), 18.0], [Vector2(36, 62), 15.0]]:
		var c: Vector2 = pad[0]
		var w: float = pad[1]
		_curve(img, [c + Vector2(-w, 4), c + Vector2(-w * 0.6, -8), c + Vector2(0, -11), c + Vector2(w * 0.6, -8), c + Vector2(w, 4), c + Vector2(-w, 4)], 1.6)


static func _can(img: Image) -> void:
	# A watering can, its rose sprinkling a few drops.
	_curve(img, [Vector2(30, 60), Vector2(30, 108), Vector2(78, 108), Vector2(78, 60)], 2.2)
	_curve(img, [Vector2(30, 60), Vector2(54, 54), Vector2(78, 60)], 2.0)
	_curve(img, [Vector2(40, 58), Vector2(54, 30), Vector2(70, 58)], 2.0)
	_curve(img, [Vector2(78, 92), Vector2(98, 72), Vector2(108, 54)], 2.0)
	_ring(img, Vector2(110, 50), 5.0, 1.8)
	for p in [Vector2(116, 62), Vector2(120, 72), Vector2(112, 76)]:
		_blob(img, p, Vector2(1.6, 2.6))


static func _tin(img: Image) -> void:
	# A pellet tin and a spoon with three pellets on it.
	_curve(img, [Vector2(20, 54), Vector2(44, 46), Vector2(68, 54), Vector2(44, 62), Vector2(20, 54)], 2.0)
	_curve(img, [Vector2(20, 54), Vector2(20, 104), Vector2(44, 112), Vector2(68, 104), Vector2(68, 54)], 2.0)
	_stroke(img, Vector2(28, 76), Vector2(60, 76), 1.2)
	_curve(img, [Vector2(76, 104), Vector2(96, 80), Vector2(110, 62)], 2.0)
	_ring(img, Vector2(110, 54), 10.0, 1.8)
	for p in [Vector2(106, 52), Vector2(114, 52), Vector2(110, 58)]:
		_blob(img, p, Vector2(2.4, 2.4))


static func _tweezers(img: Image) -> void:
	# Long tweezers pinching a fresh tip off a twig.
	_curve(img, [Vector2(20, 118), Vector2(60, 70), Vector2(84, 40)], 2.0, 1.4)
	_curve(img, [Vector2(30, 122), Vector2(66, 76), Vector2(88, 44)], 2.0, 1.4)
	_stroke(img, Vector2(25, 120), Vector2(36, 106), 1.4)
	_curve(img, [Vector2(68, 20), Vector2(86, 40), Vector2(118, 56)], 2.0)
	_leaf(img, Vector2(92, 44), Vector2(104, 24), 5.0, 0)


static func _wire(img: Image) -> void:
	# A branch with copper wire coiled round it, bending it into a new line.
	_curve(img, [Vector2(10, 100), Vector2(50, 84), Vector2(84, 60), Vector2(118, 48)], 3.0, 1.6)
	for k in range(7):
		var t := 0.12 + k * 0.11
		var p := Vector2(10, 100).lerp(Vector2(118, 48), t) + Vector2(0, -4.0 * sin(t * PI))
		_curve(img, [p + Vector2(-4, 7), p + Vector2(3, 0), p + Vector2(4, -7)], 1.4)


static func _trowel(img: Image) -> void:
	_curve(img, [Vector2(60, 58), Vector2(44, 76), Vector2(40, 100), Vector2(48, 120), Vector2(64, 100), Vector2(72, 72), Vector2(60, 58)], 2.0)
	_stroke(img, Vector2(56, 64), Vector2(54, 104), 1.0)
	_stroke(img, Vector2(64, 58), Vector2(86, 22), 2.2)
	_curve(img, [Vector2(82, 30), Vector2(96, 8), Vector2(104, 14), Vector2(90, 36), Vector2(82, 30)], 1.8)


static func _moon(img: Image) -> void:
	_curve(img, [Vector2(70, 18), Vector2(42, 34), Vector2(40, 72), Vector2(70, 94), Vector2(94, 84)], 2.2)
	_curve(img, [Vector2(70, 18), Vector2(60, 40), Vector2(64, 66), Vector2(94, 84)], 2.0)
	for p in [Vector2(100, 30), Vector2(24, 100), Vector2(108, 108)]:
		_stroke(img, p + Vector2(-5, 0), p + Vector2(5, 0), 1.4)
		_stroke(img, p + Vector2(0, -5), p + Vector2(0, 5), 1.4)


static func _feather(img: Image) -> void:
	# A quill: the pages to read again.
	_curve(img, [Vector2(22, 118), Vector2(60, 70), Vector2(108, 12)], 1.8, 1.0)
	for k in range(10):
		var t := 0.3 + k * 0.065
		var p := Vector2(22, 118).lerp(Vector2(108, 12), t)
		_stroke(img, p, p + Vector2(-16, -4) * (1.0 - absf(t - 0.6)), 1.0)
		_stroke(img, p, p + Vector2(8, 14) * (1.0 - absf(t - 0.6)), 1.0)


# The leaf of a need (the care page and a care line).

static func _leaf_need(img: Image, kind: String) -> void:
	match kind:
		"leaf_water":
			# A limp, hanging leaf and a drop.
			_curve(img, [Vector2(30, 20), Vector2(56, 28), Vector2(66, 44)], 2.0)
			_leaf(img, Vector2(66, 44), Vector2(60, 110), 15.0, 3)
			_curve(img, [Vector2(100, 70), Vector2(94, 86), Vector2(100, 92), Vector2(106, 86), Vector2(100, 70)], 1.6)
		"leaf_n":
			# A pale leaf: a thin outline, few veins, small and sparse.
			_leaf(img, Vector2(34, 110), Vector2(84, 34), 15.0, 1, false, false, 1.2)
			_leaf(img, Vector2(70, 112), Vector2(104, 70), 8.0, 0, false, false, 1.0)
		"leaf_p":
			# A dark leaf: hatched all over.
			_leaf(img, Vector2(30, 112), Vector2(90, 26), 20.0, 3)
			_hatch(img, Vector2(58, 70), Vector2(15.0, 26.0), 5.0)
		"leaf_k", "leaf_burnt":
			# Brown, scorched edges: a ragged margin and hatching just inside it.
			_leaf(img, Vector2(30, 112), Vector2(90, 26), 20.0, 3)
			for k in range(9):
				var t := 0.15 + k * 0.09
				var c := Vector2(30, 112).lerp(Vector2(90, 26), t)
				for side in [-1.0, 1.0]:
					var e: Vector2 = c + Vector2(43, 30).normalized() * side * 20.0 * sin((t * 0.9 + 0.05) * PI) * 0.85
					_stroke(img, e, e - Vector2(43, 30).normalized() * side * 5.0, 1.4)
		_:
			# A healthy leaf.
			_leaf(img, Vector2(30, 112), Vector2(90, 26), 20.0, 4)


# Visitors, finds and the clearing.

static func _butterfly(img: Image) -> void:
	var c := Vector2(64, 64)
	_stroke(img, c + Vector2(0, -16), c + Vector2(0, 18), 2.4, 1.6)
	for side in [-1.0, 1.0]:
		_curve(img, [c + Vector2(0, -6), c + Vector2(side * 22, -34), c + Vector2(side * 40, -24), c + Vector2(side * 30, 0), c + Vector2(0, 0)], 1.8)
		_curve(img, [c + Vector2(0, 2), c + Vector2(side * 28, 12), c + Vector2(side * 22, 32), c + Vector2(0, 8)], 1.8)
		_ring(img, c + Vector2(side * 22, -16), 4.0, 1.2)
		_curve(img, [c + Vector2(0, -16), c + Vector2(side * 6, -28), c + Vector2(side * 12, -32)], 1.0)


static func _nest(img: Image) -> void:
	_curve(img, [Vector2(14, 60), Vector2(30, 92), Vector2(64, 100), Vector2(98, 92), Vector2(114, 60)], 2.2)
	for k in range(8):
		var x := 20.0 + k * 12.0
		_stroke(img, Vector2(x, 64 + sin(k) * 4.0), Vector2(x + 18, 80 + cos(k) * 6.0), 1.0)
		_stroke(img, Vector2(x + 14, 62), Vector2(x - 4, 84), 1.0)
	for p in [Vector2(48, 56), Vector2(66, 52), Vector2(82, 58)]:
		_ring(img, p, 7.5, 1.8)
	_curve(img, [Vector2(14, 60), Vector2(64, 66), Vector2(114, 60)], 1.6)


static func _fox(img: Image) -> void:
	# A fox asleep, curled up with its brush over its nose.
	_curve(img, [Vector2(20, 96), Vector2(26, 64), Vector2(56, 50), Vector2(90, 58), Vector2(106, 84), Vector2(96, 100), Vector2(20, 100), Vector2(20, 96)], 2.0)
	_curve(img, [Vector2(30, 98), Vector2(46, 82), Vector2(72, 80), Vector2(100, 92)], 1.8)
	_hatch(img, Vector2(70, 92), Vector2(18.0, 5.0), 3.0, 0.9)
	_curve(img, [Vector2(30, 70), Vector2(26, 52), Vector2(38, 62)], 1.8)
	_curve(img, [Vector2(40, 62), Vector2(42, 48), Vector2(50, 60)], 1.8)
	_stroke(img, Vector2(32, 78), Vector2(38, 80), 1.4)
	_ground(img, 106.0)


static func _wren(img: Image) -> void:
	# A tiny round bird with its tail cocked up, singing.
	_curve(img, [Vector2(30, 66), Vector2(44, 50), Vector2(70, 52), Vector2(84, 70), Vector2(70, 90), Vector2(44, 90), Vector2(30, 66)], 2.0)
	_curve(img, [Vector2(80, 64), Vector2(96, 40), Vector2(102, 32)], 2.0, 1.4)
	_curve(img, [Vector2(84, 70), Vector2(100, 46), Vector2(108, 38)], 1.6, 1.2)
	_stroke(img, Vector2(32, 62), Vector2(18, 58), 1.6)
	_stroke(img, Vector2(32, 64), Vector2(20, 66), 1.6)
	_blob(img, Vector2(40, 60), Vector2(2.0, 2.0))
	for x in [52.0, 62.0]:
		_stroke(img, Vector2(x, 90), Vector2(x - 2, 106), 1.4)
	_curve(img, [Vector2(30, 106), Vector2(70, 104), Vector2(110, 108)], 2.0)
	for k in range(3):
		_curve(img, [Vector2(14, 44 - k * 9), Vector2(10, 40 - k * 9), Vector2(14, 36 - k * 9)], 1.2)


static func _fossil(img: Image) -> void:
	_curve(img, [Vector2(14, 70), Vector2(30, 30), Vector2(80, 22), Vector2(116, 52), Vector2(104, 100), Vector2(50, 108), Vector2(14, 70)], 2.0)
	var c := Vector2(64, 66)
	var prev := c
	for k in range(60):
		var a := k * 0.28
		var p := c + Vector2(cos(a), sin(a)) * (1.0 + k * 0.45)
		_stroke(img, prev, p, 1.6)
		prev = p
	for k in range(8):
		var a := k * 0.8
		_stroke(img, c + Vector2(cos(a), sin(a)) * 8.0, c + Vector2(cos(a), sin(a)) * 24.0, 0.9)


static func _old_root(img: Image) -> void:
	_curve(img, [Vector2(10, 40), Vector2(44, 52), Vector2(70, 48), Vector2(96, 70), Vector2(118, 104)], 4.0, 1.4)
	_curve(img, [Vector2(44, 52), Vector2(36, 80), Vector2(44, 110)], 2.6, 1.0)
	_curve(img, [Vector2(76, 52), Vector2(96, 36), Vector2(116, 34)], 2.0, 0.8)
	for p in [Vector2(56, 48), Vector2(84, 58)]:
		_ring(img, p, 3.0, 1.0)


static func _water_vein(img: Image) -> void:
	for k in range(4):
		var y := 34.0 + k * 18.0
		_curve(img, [Vector2(10, y), Vector2(40, y - 8), Vector2(70, y + 6), Vector2(100, y - 6), Vector2(120, y + 2)], 1.8 if k % 2 == 0 else 1.2)


static func _coin(img: Image) -> void:
	_ring(img, Vector2(62, 64), 34.0, 2.2)
	_ring(img, Vector2(62, 64), 26.0, 1.2)
	_leaf(img, Vector2(56, 84), Vector2(70, 44), 9.0, 2)
	for p in [Vector2(104, 30), Vector2(110, 96)]:
		_stroke(img, p + Vector2(-4, 0), p + Vector2(4, 0), 1.2)
		_stroke(img, p + Vector2(0, -4), p + Vector2(0, 4), 1.2)


static func _blossom(img: Image) -> void:
	# A drooping cluster of linden flowers under its bract, and a bee.
	_curve(img, [Vector2(30, 14), Vector2(44, 40), Vector2(48, 80)], 1.8)
	_curve(img, [Vector2(30, 14), Vector2(56, 34), Vector2(58, 76), Vector2(48, 80)], 1.8)
	_curve(img, [Vector2(44, 44), Vector2(40, 72), Vector2(36, 86)], 1.4)
	for p in [Vector2(28, 96), Vector2(40, 100), Vector2(34, 110)]:
		for k in range(5):
			var a := k * TAU / 5.0
			_ring(img, p + Vector2(cos(a), sin(a)) * 3.6, 2.2, 1.0)
		_stroke(img, Vector2(36, 86), p, 0.9)
	var b := Vector2(92, 56)
	_ring(img, b, 8.0, 1.8)
	_stroke(img, b + Vector2(-3, -7), b + Vector2(-3, 7), 1.4)
	_stroke(img, b + Vector2(3, -7), b + Vector2(3, 7), 1.4)
	_curve(img, [b + Vector2(-2, -8), b + Vector2(-10, -22), b + Vector2(2, -16)], 1.2)
	_curve(img, [b + Vector2(2, -8), b + Vector2(12, -22), b + Vector2(8, -10)], 1.2)
	_curve(img, [b + Vector2(-10, 10), Vector2(70, 84), Vector2(84, 96), Vector2(110, 90)], 1.0)


static func _anemone(img: Image) -> void:
	_ground(img)
	_curve(img, [Vector2(64, 112), Vector2(62, 80), Vector2(64, 56)], 1.8)
	var c := Vector2(64, 44)
	for k in range(6):
		var a := k * TAU / 6.0
		var d := Vector2(cos(a), sin(a))
		_curve(img, [c + d * 4.0 + Vector2(-d.y, d.x) * 5.0, c + d * 18.0, c + d * 4.0 - Vector2(-d.y, d.x) * 5.0], 1.6)
	_blob(img, c, Vector2(3.0, 3.0))
	_leaf(img, Vector2(63, 84), Vector2(34, 70), 7.0, 1, true)
	_leaf(img, Vector2(63, 84), Vector2(94, 72), 7.0, 1, true)


static func _fern(img: Image) -> void:
	_ground(img)
	_curve(img, [Vector2(50, 112), Vector2(46, 70), Vector2(60, 30), Vector2(84, 22)], 2.0, 1.2)
	for k in range(8):
		var t := 0.15 + k * 0.1
		var p := Vector2(50, 112).lerp(Vector2(64, 28), t) + Vector2(-6.0 * sin(t * PI), 0)
		var l := 22.0 * (1.0 - t * 0.8)
		_curve(img, [p, p + Vector2(-l * 0.6, -l * 0.25), p + Vector2(-l, -l * 0.1)], 1.2, 0.6)
		_curve(img, [p, p + Vector2(l * 0.6, -l * 0.4), p + Vector2(l, -l * 0.3)], 1.2, 0.6)
	# A fiddlehead unrolling beside it.
	var c := Vector2(94, 70)
	var prev := c
	for k in range(36):
		var a := k * 0.3
		var p := c + Vector2(cos(a), sin(a)) * (1.0 + k * 0.28)
		_stroke(img, prev, p, 1.6)
		prev = p
	_curve(img, [prev, Vector2(100, 96), Vector2(96, 112)], 1.8)


static func _moss(img: Image) -> void:
	_ground(img)
	for hump in [[Vector2(36, 112), 24.0], [Vector2(74, 112), 30.0], [Vector2(106, 112), 16.0]]:
		var c: Vector2 = hump[0]
		var r: float = hump[1]
		_curve(img, [c + Vector2(-r, 0), c + Vector2(-r * 0.7, -r * 0.7), c + Vector2(0, -r), c + Vector2(r * 0.7, -r * 0.7), c + Vector2(r, 0)], 1.8)
		for k in range(int(r / 4.0)):
			var x := c.x - r * 0.7 + k * 5.0
			_stroke(img, Vector2(x, c.y - r * 0.5), Vector2(x + 1.5, c.y - r * 0.5 - 4.0), 1.0)
	_curve(img, [Vector2(74, 82), Vector2(76, 66), Vector2(80, 60)], 1.2)
	_blob(img, Vector2(80, 58), Vector2(1.8, 3.0))


static func _mushroom(img: Image) -> void:
	_ground(img)
	for m in [[Vector2(52, 112), 30.0, 50.0], [Vector2(92, 112), 18.0, 28.0]]:
		var b: Vector2 = m[0]
		var r: float = m[1]
		var h: float = m[2]
		_curve(img, [b + Vector2(-r * 0.25, 0), b + Vector2(-r * 0.2, -h * 0.5), b + Vector2(-r * 0.22, -h)], 1.8)
		_curve(img, [b + Vector2(r * 0.25, 0), b + Vector2(r * 0.2, -h * 0.5), b + Vector2(r * 0.22, -h)], 1.8)
		_curve(img, [b + Vector2(-r, -h), b + Vector2(-r * 0.8, -h - r * 0.6), b + Vector2(0, -h - r * 0.9), b + Vector2(r * 0.8, -h - r * 0.6), b + Vector2(r, -h)], 2.0)
		_curve(img, [b + Vector2(-r, -h), b + Vector2(0, -h + 4.0), b + Vector2(r, -h)], 1.6)
		_ring(img, b + Vector2(-r * 0.3, -h - r * 0.5), r * 0.12, 1.0)


static func _pile(img: Image) -> void:
	_ground(img)
	for s in [[Vector2(14, 110), Vector2(104, 64)], [Vector2(24, 76), Vector2(116, 108)], [Vector2(40, 110), Vector2(70, 50)], [Vector2(60, 56), Vector2(96, 110)], [Vector2(20, 96), Vector2(110, 90)]]:
		_stroke(img, s[0], s[1], 2.2, 1.4)
	_curve(img, [Vector2(70, 50), Vector2(80, 40), Vector2(86, 42)], 1.2)


static func _rain(img: Image) -> void:
	_curve(img, [Vector2(20, 60), Vector2(22, 44), Vector2(40, 40), Vector2(48, 24), Vector2(72, 22), Vector2(84, 36), Vector2(104, 36), Vector2(110, 56), Vector2(20, 60)], 2.0)
	for k in range(7):
		var x := 28.0 + k * 12.0
		var y := 72.0 + (k % 3) * 10.0
		_stroke(img, Vector2(x, y), Vector2(x - 4, y + 12), 1.6, 1.0)


static func _mist(img: Image) -> void:
	_sun(img, Vector2(88, 36), 12.0, 7)
	for k in range(4):
		var y := 64.0 + k * 12.0
		_curve(img, [Vector2(10 + k * 6, y), Vector2(40, y - 5), Vector2(70, y + 3), Vector2(116 - k * 4, y - 3)], 1.4)


static func _sun_only(img: Image) -> void:
	_sun(img, Vector2(64, 60), 20.0, 11)
	_ground(img)


static func _book(img: Image) -> void:
	# An open notebook (a copy of the tree loaded back).
	_curve(img, [Vector2(64, 40), Vector2(40, 32), Vector2(14, 38), Vector2(14, 98), Vector2(40, 92), Vector2(64, 100)], 2.0)
	_curve(img, [Vector2(64, 40), Vector2(88, 32), Vector2(114, 38), Vector2(114, 98), Vector2(88, 92), Vector2(64, 100)], 2.0)
	_stroke(img, Vector2(64, 40), Vector2(64, 100), 1.6)
	for k in range(4):
		var y := 50.0 + k * 10.0
		_stroke(img, Vector2(22, y), Vector2(56, y - 2), 1.0)
		_stroke(img, Vector2(72, y - 2), Vector2(106, y), 1.0)


# 0.8.2.2: the cuttings (the bonsai's cuttings page). Each a slip of its tree in a small pot, its
# leaves as on the seed bag's pictures, so the page reads at a glance.

static func _cutting(img: Image, sid: String) -> void:
	# A small clay pot with its rim and the soil in it, a few crumbs of grit.
	_curve(img, [Vector2(30, 92), Vector2(98, 92)], 2.2)
	_curve(img, [Vector2(28, 86), Vector2(100, 86), Vector2(100, 92)], 2.0)
	_stroke(img, Vector2(28, 86), Vector2(28, 92), 2.0)
	_curve(img, [Vector2(34, 92), Vector2(40, 122), Vector2(88, 122), Vector2(94, 92)], 2.0)
	for p in [Vector2(46, 82), Vector2(58, 84), Vector2(84, 83)]:
		_blob(img, p, Vector2(1.6, 1.2))
	match sid:
		"juniper":
			# A sprig of scale leaves, upright with three side sprays.
			_curve(img, [Vector2(62, 86), Vector2(60, 56), Vector2(66, 18)], 2.2, 1.2)
			for br in [[Vector2(60, 66), Vector2(34, 46)], [Vector2(61, 52), Vector2(92, 36)], [Vector2(63, 34), Vector2(44, 16)]]:
				var a: Vector2 = br[0]
				var b: Vector2 = br[1]
				_stroke(img, a, b, 1.5, 1.0)
				var d := (b - a).normalized()
				var n := Vector2(-d.y, d.x)
				for k in range(5):
					var p: Vector2 = a.lerp(b, (k + 0.6) / 5.0)
					_stroke(img, p, p + d * 4.0 + n * 3.0, 1.0)
					_stroke(img, p, p + d * 4.0 - n * 3.0, 1.0)
		"linden":
			# Two heart-shaped toothed leaves on a slanting slip.
			_curve(img, [Vector2(64, 86), Vector2(62, 60), Vector2(70, 40)], 2.2, 1.4)
			_leaf(img, Vector2(62, 58), Vector2(30, 30), 15.0, 3, true, true, 1.6)
			_leaf(img, Vector2(70, 40), Vector2(96, 12), 14.0, 3, true, true, 1.6)
		"birch":
			# A thin slip with three small toothed, pointed leaves.
			_curve(img, [Vector2(64, 86), Vector2(66, 50), Vector2(60, 14)], 1.8, 1.0)
			_leaf(img, Vector2(65, 66), Vector2(40, 54), 7.0, 2, true, false, 1.4)
			_leaf(img, Vector2(66, 46), Vector2(92, 36), 7.0, 2, true, false, 1.4)
			_leaf(img, Vector2(62, 28), Vector2(40, 14), 6.0, 2, true, false, 1.4)
		"beech":
			# Two glossy oval leaves with straight parallel veins, one up, one aside.
			_curve(img, [Vector2(64, 86), Vector2(64, 50)], 2.2, 1.4)
			_leaf(img, Vector2(64, 52), Vector2(66, 8), 12.0, 6, false, false, 1.6)
			_leaf(img, Vector2(64, 64), Vector2(104, 50), 10.0, 5, false, false, 1.6)
		"sycamore":
			# One big hand-shaped leaf of five lobes on its long stalk.
			_curve(img, [Vector2(64, 86), Vector2(66, 66), Vector2(64, 50)], 2.0, 1.4)
			var c := Vector2(64, 48)
			for a in [-1.2, -0.6, 0.0, 0.6, 1.2]:
				var tip: Vector2 = c + Vector2(sin(a), -cos(a)) * (36.0 - absf(a) * 9.0)
				_leaf(img, c, tip, 8.5 - absf(a) * 1.5, 1, true, false, 1.5)
		"alder":
			# Two round leaves with a notched tip, and a tiny cone.
			_curve(img, [Vector2(64, 86), Vector2(60, 60), Vector2(66, 36)], 2.2, 1.4)
			_leaf(img, Vector2(60, 62), Vector2(26, 50), 14.0, 3, true, false, 1.6)
			_leaf(img, Vector2(66, 38), Vector2(82, 6), 15.0, 3, true, false, 1.6)
			_ring(img, Vector2(94, 52), 6.0, 1.6)
			_stroke(img, Vector2(66, 44), Vector2(90, 48), 1.2)
		"oak":
			# Two wavy lobed leaves.
			_curve(img, [Vector2(64, 86), Vector2(64, 56)], 2.2, 1.4)
			for lf in [[Vector2(64, 58), Vector2(40, 10)], [Vector2(64, 68), Vector2(108, 44)]]:
				var base: Vector2 = lf[0]
				var tip: Vector2 = lf[1]
				var along := tip - base
				var n := Vector2(-along.y, along.x).normalized()
				for side in [-1.0, 1.0]:
					var pts: Array = [base]
					for k in range(1, 6):
						var t := k / 6.0
						var w := (7.0 if k % 2 == 1 else 3.0) + 5.0 * sin(t * PI)
						pts.append(base.lerp(tip, t) + n * side * w)
					pts.append(tip)
					_curve(img, pts, 1.6, 1.0)
				_curve(img, [base, tip], 1.1, 0.6)
