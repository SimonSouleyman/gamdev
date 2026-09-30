class_name InkSketch
extends RefCounted
## Small ink drawings for the diary (0.7): a tuft of rushes for a reached damp patch, a clover
## for a reached nitrogen patch; from 0.8 a nettle for phosphorus and comfrey for potassium (0.8
## review: stones only ever mean rock). Drawn in code with soft pen strokes, once, then cached.

const SIZE: int = 128
static var _cache: Dictionary = {}


static func texture(kind: String) -> Texture2D:
	if not _cache.has(kind):
		_cache[kind] = ImageTexture.create_from_image(image(kind))
	return _cache[kind]


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
	return img


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
