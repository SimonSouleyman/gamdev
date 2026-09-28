class_name Paper
extends RefCounted
## The journal look, shared by every page, scrap and button: aged paper with fibres and a
## torn edge, handwriting, ink colours, a leather book cover. Everything is generated (seeded)
## so the look needs no downloads; CC0 paper textures and bundled OFL handwriting fonts
## (Caveat, Patrick Hand) can replace the stand-ins later without touching the callers.

const PAPER := Color(0.95, 0.91, 0.8)
const PAPER_SHADE := Color(0.86, 0.8, 0.66)
const INK := Color(0.2, 0.16, 0.12)
const FAINT_INK := Color(0.42, 0.34, 0.25)
const RED_INK := Color(0.6, 0.18, 0.12)
const LEATHER := Color(0.33, 0.2, 0.12)
## Tap area of a picture button on the HUD (about 110 px tall on a 1080x2400 phone).
const PICTURE_TAP := Vector2(150, 96)

static var _cache: Dictionary = {}


## Handwriting: the bundled font once it exists, else a handwriting font Windows ships.
static func hand_font(bold: bool = false) -> Font:
	var key := "font_%s" % bold
	if _cache.has(key):
		return _cache[key]
	var f: Font
	# Caveat for headings (lively), Patrick Hand for reading (calm, legible on a phone).
	var paths := ["res://ui/fonts/Caveat-Regular.ttf", "res://ui/fonts/PatrickHand-Regular.ttf"] if bold 		else ["res://ui/fonts/PatrickHand-Regular.ttf", "res://ui/fonts/Caveat-Regular.ttf"]
	for path in paths:
		if ResourceLoader.exists(path):
			f = load(path)
			break
	if f == null:
		var sf := SystemFont.new()
		sf.font_names = PackedStringArray(["Segoe Print", "Ink Free", "Bradley Hand", "Comic Sans MS"])
		sf.font_weight = 700 if bold else 400
		sf.antialiasing = TextServer.FONT_ANTIALIASING_GRAY
		f = sf
	_cache[key] = f
	return f


## A sheet of paper with fibres and faint stains; `torn` edges are ragged ("top", "bottom", "all").
static func paper_texture(w: int, h: int, seed: int, torn: String = "", tint: Color = PAPER, kind: String = "cream") -> ImageTexture:
	var key := "paper_%d_%d_%d_%s_%s_%s" % [w, h, seed, torn, tint.to_html(), kind]
	if _cache.has(key):
		return _cache[key]
	var noise := FastNoiseLite.new()
	noise.seed = seed
	noise.frequency = 0.03
	noise.fractal_octaves = 3
	var fibre := FastNoiseLite.new()
	fibre.seed = seed + 1
	fibre.frequency = 0.25
	var rng := RandomNumberGenerator.new()
	rng.seed = seed
	var top_edge := _torn_edge(w, rng, 7.0) if torn in ["top", "all"] else PackedFloat32Array()
	var bottom_edge := _torn_edge(w, rng, 7.0) if torn in ["bottom", "all"] else PackedFloat32Array()
	var img := Image.create(w, h, false, Image.FORMAT_RGBA8)
	# A scanned paper (CC0, OpenGameArt) under the stains, if it is there.
	var photo: Image = null
	var photo_path := "res://assets/paper/paper_%s.png" % kind
	if ResourceLoader.exists(photo_path):
		# Read once from the file (not back from the GPU) and kept for every page.
		if not _cache.has(photo_path):
			# The raw file only exists in the editor; an exported game (the phone) reads the import.
			var raw: Image = Image.load_from_file(ProjectSettings.globalize_path(photo_path)) if OS.has_feature("editor") else null
			if raw == null or raw.is_empty():
				raw = (load(photo_path) as Texture2D).get_image()
			if raw.is_compressed():
				raw.decompress()
			_cache[photo_path] = raw
		photo = _cache[photo_path]
	for y in range(h):
		for x in range(w):
			var n := noise.get_noise_2d(x, y) * 0.5 + 0.5
			var f := fibre.get_noise_2d(x * 3.0, y * 0.4) * 0.5 + 0.5
			var c := tint.lerp(PAPER_SHADE, clampf(n * 0.55 + f * 0.12, 0.0, 1.0))
			if photo != null:
				var pc := photo.get_pixel((x + seed * 37) % photo.get_width(), (y + seed * 53) % photo.get_height())
				c = Color(pc.r * c.r / PAPER.r, pc.g * c.g / PAPER.g, pc.b * c.b / PAPER.b)
			# A darker rim, like a page handled for a while.
			var rim := minf(minf(x, w - 1 - x), minf(y, h - 1 - y)) / 18.0
			c = c.lerp(PAPER_SHADE.darkened(0.12), clampf(1.0 - rim, 0.0, 1.0) * 0.35)
			var a := 1.0
			if not top_edge.is_empty() and y < top_edge[x]:
				a = 0.0
			if not bottom_edge.is_empty() and y > h - 1 - bottom_edge[x]:
				a = 0.0
			# The fibrous white of a fresh tear.
			if a > 0.0 and ((not top_edge.is_empty() and y < top_edge[x] + 2.5) or (not bottom_edge.is_empty() and y > h - 3.5 - bottom_edge[x])):
				c = c.lightened(0.35)
			img.set_pixel(x, y, Color(c, a))
	var tex := ImageTexture.create_from_image(img)
	_cache[key] = tex
	return tex


static func _torn_edge(w: int, rng: RandomNumberGenerator, depth: float) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(w)
	var v := depth * 0.5
	for x in range(w):
		v = clampf(v + rng.randf_range(-1.6, 1.6), 1.0, depth)
		out[x] = v
	return out


## A StyleBox from a paper texture, stretched by 9-patch so the rim and tears keep their size.
static func paper_box(w: int, h: int, seed: int, torn: String = "", margin: float = 26.0, tint: Color = PAPER, kind: String = "cream") -> StyleBoxTexture:
	var sb := StyleBoxTexture.new()
	sb.texture = paper_texture(w, h, seed, torn, tint, kind)
	sb.texture_margin_left = 20
	sb.texture_margin_right = 20
	sb.texture_margin_top = 20
	sb.texture_margin_bottom = 20
	# Tile the middle instead of stretching it, so a big page keeps the paper's grain.
	sb.axis_stretch_horizontal = StyleBoxTexture.AXIS_STRETCH_MODE_TILE_FIT
	sb.axis_stretch_vertical = StyleBoxTexture.AXIS_STRETCH_MODE_TILE_FIT
	sb.content_margin_left = margin
	sb.content_margin_right = margin
	sb.content_margin_top = margin * 0.8 + (8.0 if torn in ["top", "all"] else 0.0)
	sb.content_margin_bottom = margin * 0.7 + (8.0 if torn in ["bottom", "all"] else 0.0)
	return sb


## The leather cover the book pages lie on: grained leather with a stitched border and worn corners.
static func cover_box() -> StyleBoxTexture:
	var sb := StyleBoxTexture.new()
	sb.texture = leather_texture(160, 220)
	sb.texture_margin_left = 24
	sb.texture_margin_right = 24
	sb.texture_margin_top = 24
	sb.texture_margin_bottom = 24
	return sb


static func leather_texture(w: int, h: int) -> ImageTexture:
	var key := "leather_%d_%d" % [w, h]
	if _cache.has(key):
		return _cache[key]
	var grain := FastNoiseLite.new()
	grain.seed = 9
	grain.frequency = 0.35
	var mottle := FastNoiseLite.new()
	mottle.seed = 10
	mottle.frequency = 0.04
	var img := Image.create(w, h, false, Image.FORMAT_RGBA8)
	for y in range(h):
		for x in range(w):
			var g := grain.get_noise_2d(x, y) * 0.5 + 0.5
			var m := mottle.get_noise_2d(x, y) * 0.5 + 0.5
			var c := LEATHER.darkened(0.25).lerp(LEATHER.lightened(0.12), m) * (0.88 + 0.22 * g)
			# Rounded, worn corners.
			var cx := minf(x, w - 1 - x)
			var cy := minf(y, h - 1 - y)
			var a := 1.0
			if cx < 12 and cy < 12 and Vector2(12 - cx, 12 - cy).length() > 12.0:
				a = 0.0
			if cx < 20 and cy < 20:
				c = c.lightened(0.08 * (1.0 - Vector2(cx, cy).length() / 28.0))
			# Stitches along the border.
			if (cx == 9 or cy == 9) and cx >= 9 and cy >= 9 and int(x + y) % 8 < 4:
				c = Color(0.82, 0.72, 0.5)
			img.set_pixel(x, y, Color(c, a))
	var tex := ImageTexture.create_from_image(img)
	_cache[key] = tex
	return tex


## A hand-drawn check box: [empty, ticked], ink on transparent.
static func check_icons(size: int = 34) -> Array[ImageTexture]:
	if _cache.has("checks"):
		return _cache["checks"]
	var out: Array[ImageTexture] = []
	for ticked in [false, true]:
		var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
		img.fill(Color(0, 0, 0, 0))
		var rng := RandomNumberGenerator.new()
		rng.seed = 3
		# A slightly wobbly square.
		var corners := [Vector2(4, 5), Vector2(size - 5, 4), Vector2(size - 4, size - 5), Vector2(5, size - 4)]
		for i in range(4):
			_ink_line(img, corners[i], corners[(i + 1) % 4], INK, 2.0)
		if ticked:
			_ink_line(img, Vector2(8, size * 0.5), Vector2(size * 0.42, size - 8), RED_INK, 3.0)
			_ink_line(img, Vector2(size * 0.42, size - 8), Vector2(size - 3, 3), RED_INK, 3.0)
		out.append(ImageTexture.create_from_image(img))
	_cache["checks"] = out
	return out


static func _ink_line(img: Image, a: Vector2, b: Vector2, col: Color, width: float) -> void:
	var steps := int(a.distance_to(b) * 2.0) + 1
	for s in range(steps + 1):
		var p := a.lerp(b, float(s) / steps)
		for dy in range(-2, 3):
			for dx in range(-2, 3):
				var q := p + Vector2(dx, dy)
				var d := q.distance_to(p)
				if d <= width * 0.5 and q.x >= 0 and q.y >= 0 and q.x < img.get_width() and q.y < img.get_height():
					img.set_pixelv(Vector2i(q), col)


static func ink_label(text: String, size: int, color: Color = INK, bold: bool = false) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", hand_font(bold))
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Ink that sits in the paper: pressure, skips and a slight bleed (visuals thread).
	l.material = PaperLook.ink_material()
	return l


## A word circled in ink, the journal's button.
static func ink_button(text: String, size: int = 26) -> Button:
	var b := Button.new()
	b.text = text
	b.focus_mode = Control.FOCUS_NONE
	b.add_theme_font_override("font", hand_font(true))
	b.add_theme_font_size_override("font_size", size)
	for k in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color"]:
		b.add_theme_color_override(k, INK)
	for k in ["normal", "hover", "pressed", "focus"]:
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(0.2, 0.15, 0.1, 0.08 if k == "pressed" else 0.0)
		sb.draw_center = true
		sb.border_color = INK if k != "hover" else RED_INK
		sb.set_border_width_all(2)
		sb.set_corner_radius_all(24)
		sb.corner_detail = 5
		sb.content_margin_left = 16
		sb.content_margin_right = 16
		sb.content_margin_top = 2
		sb.content_margin_bottom = 4
		# Slightly uneven, like a circle drawn by hand.
		sb.expand_margin_left = 2
		sb.expand_margin_top = 1
		b.add_theme_stylebox_override(k, sb)
	return b


## A HUD button that is a picture of the real thing (ui/icons, rendered by tools/render_icons.gd),
## at the middle of the right edge: `top` is its top edge relative to the screen's middle. The
## tap area is wider than the picture (at least the old paper scraps' size); it dips when pressed.
static func picture_button(tex: Texture2D, top: float, tilt: float = 0.0) -> TextureButton:
	var b := TextureButton.new()
	b.texture_normal = tex
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	b.focus_mode = Control.FOCUS_NONE
	b.set_anchors_preset(Control.PRESET_CENTER_RIGHT)
	b.offset_left = -PICTURE_TAP.x - 14
	b.offset_right = -14
	b.offset_top = top
	b.offset_bottom = top + PICTURE_TAP.y
	b.pivot_offset = PICTURE_TAP * 0.5
	b.rotation_degrees = tilt
	b.button_down.connect(func() -> void: b.scale = Vector2.ONE * 0.9)
	b.button_up.connect(func() -> void: b.scale = Vector2.ONE)
	b.mouse_entered.connect(func() -> void: b.self_modulate = Color(1.12, 1.08, 1.0))
	b.mouse_exited.connect(func() -> void: b.self_modulate = Color.WHITE)
	return b
