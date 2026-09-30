class_name NutrientMarks
extends RefCounted
## Shapes on the nutrient dots (0.8, specs/0.8.md section 1): each nutrient keeps its colour and
## gets its own mark, so the dots read without colour too (red-green colour blindness, a
## greyscale screenshot). Water a drop, nitrogen a leaf, phosphorus a four-point spark,
## potassium a ring. The same marks are used underground (an atlas on the existing dot quads),
## on the HUD pills, the journal's care and meadow-hint pages and the bonsai's pellet slip.
## Drawn in code once and cached; numbers and reasons in docs/notes/shapes-brush-0.8.md.

const SHAPE_NAMES: Array[String] = ["drop", "leaf", "spark", "ring"]
## Colour and shape together, for text ("steer for the blue drops").
const WORDS: Array[String] = ["blue drop", "green leaf", "orange spark", "violet ring"]
## One atlas cell, in pixels (four cells side by side).
const CELL: int = 64
## Supersampling per pixel side when a mark is drawn.
const SAMPLES: int = 4
## Blur radius of the atlas's soft glow channel, in pixels of a cell.
const GLOW_BLUR: int = 4
## The leaf lies tilted like a real leaf, not upright like the drop.
const LEAF_TILT: float = -0.6

static var _atlas: ImageTexture = null
static var _icons: Dictionary = {}


## Whether the point `p` (the mark's square is -1..1 on both axes, y up) lies inside the mark.
static func inside(kind: int, p: Vector2) -> bool:
	match kind:
		0:
			return _in_drop(p)
		1:
			return _in_leaf(p)
		2:
			return _in_spark(p)
		3:
			var r := p.length()
			return r >= 0.46 and r <= 0.84
	return p.length() <= 0.8


## A drop: a round belly with a point on top (the belly's tangents meet at the tip).
static func _in_drop(p: Vector2) -> bool:
	var c := Vector2(0.0, -0.3)
	var r := 0.54
	if p.distance_to(c) <= r:
		return true
	var apex := Vector2(0.0, 0.9)
	var d := apex.distance_to(c)
	var u := (c - apex) / d
	var q := p - apex
	var along := q.dot(u)
	var half := asin(r / d)
	var reach := sqrt(d * d - r * r) * cos(half)
	return along >= 0.0 and along <= reach and absf(q.angle_to(u)) <= half


## A leaf: a pointed lens (two circle arcs) on a short stalk, tilted.
static func _in_leaf(p_in: Vector2) -> bool:
	var p := p_in.rotated(-LEAF_TILT)
	var w := 0.44
	var h := 0.8
	var c := (h * h - w * w) / (2.0 * w)
	var big := c + w
	var body := p.distance_to(Vector2(c, 0.08)) <= big and p.distance_to(Vector2(-c, 0.08)) <= big
	var stalk := absf(p.x) <= 0.06 and p.y <= -0.6 and p.y >= -0.98
	return body or stalk


## A four-point spark: a star with slightly concave sides.
static func _in_spark(p: Vector2) -> bool:
	var a := 0.95
	var e := 0.62
	return pow(absf(p.x) / a, e) + pow(absf(p.y) / a, e) <= 1.0


## The mark as coverage 0..1, `size` pixels square (anti-aliased by supersampling).
static func coverage(kind: int, size: int) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(size * size)
	var n := SAMPLES * SAMPLES
	for y in range(size):
		for x in range(size):
			var hits := 0
			for sy in range(SAMPLES):
				for sx in range(SAMPLES):
					var u := (x + (sx + 0.5) / SAMPLES) / size * 2.0 - 1.0
					var v := 1.0 - (y + (sy + 0.5) / SAMPLES) / size * 2.0
					if inside(kind, Vector2(u, v)):
						hits += 1
			out[y * size + x] = float(hits) / n
	return out


## The atlas for the underground dots: four cells (water, nitrogen, phosphorus, potassium).
## Red is the crisp mark, green a soft glow around it; mipmapped, so tiny far dots stay smooth.
static func atlas() -> ImageTexture:
	if _atlas == null:
		var img := Image.create(CELL * 4, CELL, false, Image.FORMAT_RGBA8)
		for k in range(4):
			var cov := coverage(k, CELL)
			var glow := _blur(cov, CELL, GLOW_BLUR)
			for y in range(CELL):
				for x in range(CELL):
					var i := y * CELL + x
					img.set_pixel(k * CELL + x, y, Color(cov[i], clampf(glow[i] * 1.6, 0.0, 1.0), 0.0, 1.0))
		img.generate_mipmaps()
		_atlas = ImageTexture.create_from_image(img)
	return _atlas


## Two box blurs of `radius` pixels (close to a gaussian).
static func _blur(src: PackedFloat32Array, size: int, radius: int) -> PackedFloat32Array:
	var a := src
	for _pass in range(2):
		var h := PackedFloat32Array()
		h.resize(size * size)
		for y in range(size):
			for x in range(size):
				var s := 0.0
				for d in range(-radius, radius + 1):
					s += a[y * size + clampi(x + d, 0, size - 1)]
				h[y * size + x] = s / (2 * radius + 1)
		var v := PackedFloat32Array()
		v.resize(size * size)
		for y in range(size):
			for x in range(size):
				var s := 0.0
				for d in range(-radius, radius + 1):
					s += h[clampi(y + d, 0, size - 1) * size + x]
				v[y * size + x] = s / (2 * radius + 1)
		a = v
	return a


## The mark in its nutrient's colour as ink on paper, `size` pixels square, for the HUD and
## the journal. Cached per kind and size.
static func icon(kind: int, size: int = 30) -> ImageTexture:
	var key := "%d_%d" % [kind, size]
	if not _icons.has(key):
		var col: Color = Resources.KIND_COLORS[kind].darkened(0.18)
		var cov := coverage(kind, size)
		var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
		for y in range(size):
			for x in range(size):
				img.set_pixel(x, y, Color(col, cov[y * size + x]))
		_icons[key] = ImageTexture.create_from_image(img)
	return _icons[key]


## A small picture of the mark for a Control: a TextureRect `size` pixels square.
static func icon_rect(kind: int, size: int = 30) -> TextureRect:
	var r := TextureRect.new()
	r.texture = icon(kind, size)
	r.custom_minimum_size = Vector2(size, size)
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	r.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	r.name = "mark_%s" % SHAPE_NAMES[kind]
	return r


## A handwritten key of the four marks with their names ("drop water", ...), for journal pages.
static func legend(font_size: int = 24) -> HFlowContainer:
	var row := HFlowContainer.new()
	row.add_theme_constant_override("h_separation", 18)
	row.add_theme_constant_override("v_separation", 4)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for k in range(4):
		var item := HBoxContainer.new()
		item.add_theme_constant_override("separation", 5)
		item.mouse_filter = Control.MOUSE_FILTER_IGNORE
		item.add_child(icon_rect(k, font_size + 6))
		item.add_child(Paper.ink_label(Resources.KIND_NAMES[k], font_size, Paper.INK))
		row.add_child(item)
	return row
