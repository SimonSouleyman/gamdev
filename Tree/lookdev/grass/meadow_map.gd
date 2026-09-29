class_name MeadowMap
extends RefCounted
## The meadow is not one even carpet (0.6.1 review): lusher and drier stretches, patches of
## taller grass, a slow drift of colour and a few paths of trodden, flattened grass (one from the
## shed door toward the tree, a game trail or two across the clearing). One small picture holds it
## all, seeded from the save: the grass clumps read it when they are planted (GrassLook) and the
## ground shader draws the same patches under them, so both agree. Look only.
##
## Channels: r lush (0 dry .. 1 lush), g tall grass, b path (1 on the trodden line), a colour drift.

## Metres from the trunk to the map's edge (each way); beyond it the ground has no patches.
const HALF := 48.0
## Pixels per side (about a third of a metre each).
const SIZE := 288

var image: Image
var texture: ImageTexture


## Builds the map for this save and clearing (the shed path starts at Shed.origin).
static func build(seed: int, clearing: float) -> MeadowMap:
	var m := MeadowMap.new()
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "meadow map"])
	var lush := FastNoiseLite.new()
	lush.seed = rng.randi()
	lush.frequency = 0.045
	lush.fractal_octaves = 3
	var tall := FastNoiseLite.new()
	tall.seed = rng.randi()
	tall.frequency = 0.11
	tall.fractal_octaves = 2
	var drift := FastNoiseLite.new()
	drift.seed = rng.randi()
	drift.frequency = 0.02
	drift.fractal_octaves = 1
	var data := PackedByteArray()
	data.resize(SIZE * SIZE * 4)
	var step := 2.0 * HALF / SIZE
	for y in range(SIZE):
		var z := -HALF + (y + 0.5) * step
		for x in range(SIZE):
			var wx := -HALF + (x + 0.5) * step
			var i := (y * SIZE + x) * 4
			data[i] = int(clampf(0.5 + lush.get_noise_2d(wx, z) * 1.3, 0.0, 1.0) * 255.0)
			# Tall grass stands in clumped patches, not everywhere.
			data[i + 1] = int(smoothstep(0.08, 0.38, tall.get_noise_2d(wx, z)) * 255.0)
			data[i + 3] = int(clampf(0.5 + drift.get_noise_2d(wx, z) * 1.2, 0.0, 1.0) * 255.0)
	m.image = Image.create_from_data(SIZE, SIZE, false, Image.FORMAT_RGBA8, data)
	for path in _paths(rng, clearing):
		m._stamp(path, 0.55)
	m.texture = ImageTexture.create_from_image(m.image)
	return m


## The trodden lines: from the shed door toward the tree (ending short of the trunk, where the
## player stands to look), and one or two game trails crossing the clearing at a slant.
static func _paths(rng: RandomNumberGenerator, clearing: float) -> Array:
	var out: Array = []
	var door := Vector2(Shed.origin.x, Shed.origin.z - 1.6)
	out.append(_wander(rng, door, Vector2(rng.randf_range(-2.0, 2.0), 2.8), 1.5))
	for k in range(2 if clearing > 20.0 else 1):
		var a := rng.randf() * TAU
		var b := a + PI + rng.randf_range(-0.9, 0.9)
		var r := clearing + 2.0
		var from := Vector2(cos(a), sin(a)) * r
		var to := Vector2(cos(b), sin(b)) * r
		# Animals keep to the edge of the open ground, not past the trunk.
		var mid := (from + to) * 0.5
		if mid.length() < 7.0:
			var side := mid.normalized() if mid.length() > 0.1 else Vector2(cos(a + PI * 0.5), sin(a + PI * 0.5))
			from += side * (7.0 - mid.length())
			to += side * (7.0 - mid.length())
		out.append(_wander(rng, from, to, 1.6))
	return out


## Points every 0.25 m from `from` to `to`, wandering sideways by up to `sway` metres.
static func _wander(rng: RandomNumberGenerator, from: Vector2, to: Vector2, sway: float) -> PackedVector2Array:
	var pts := PackedVector2Array()
	var d := to - from
	var n := maxi(2, int(d.length() / 0.25))
	var side := Vector2(-d.y, d.x).normalized()
	var p1 := rng.randf() * TAU
	var p2 := rng.randf() * TAU
	for i in range(n + 1):
		var t := float(i) / n
		# Zero at both ends, so the path starts and ends where it should.
		var s := sin(t * PI) * (sin(t * 5.1 + p1) * 0.7 + sin(t * 11.3 + p2) * 0.3) * sway
		pts.append(from + d * t + side * s)
	return pts


## Writes the path into the blue channel: 1 on the line, fading out at `width` metres.
func _stamp(pts: PackedVector2Array, width: float) -> void:
	var px := SIZE / (2.0 * HALF)
	var reach := int(ceil(width * px)) + 1
	for p in pts:
		var c := (p + Vector2(HALF, HALF)) * px
		for y in range(int(c.y) - reach, int(c.y) + reach + 1):
			for x in range(int(c.x) - reach, int(c.x) + reach + 1):
				if x < 0 or y < 0 or x >= SIZE or y >= SIZE:
					continue
				var d := Vector2(x + 0.5, y + 0.5).distance_to(c) / px
				var v := 1.0 - smoothstep(width * 0.35, width, d)
				if v <= 0.0:
					continue
				var col := image.get_pixel(x, y)
				if v > col.b:
					col.b = v
					image.set_pixel(x, y, col)


## The map at a point of the ground (bilinear), outside it: even, no path.
func sample(x: float, z: float) -> Color:
	var u := (x + HALF) / (2.0 * HALF) * SIZE - 0.5
	var v := (z + HALF) / (2.0 * HALF) * SIZE - 0.5
	if u < 0.0 or v < 0.0 or u >= SIZE - 1 or v >= SIZE - 1:
		return Color(0.5, 0.0, 0.0, 0.5)
	var x0 := int(u)
	var y0 := int(v)
	var fx := u - x0
	var fy := v - y0
	var top := image.get_pixel(x0, y0).lerp(image.get_pixel(x0 + 1, y0), fx)
	var bottom := image.get_pixel(x0, y0 + 1).lerp(image.get_pixel(x0 + 1, y0 + 1), fx)
	return top.lerp(bottom, fy)
