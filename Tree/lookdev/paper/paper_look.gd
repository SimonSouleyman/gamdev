class_name PaperLook
extends RefCounted
## Look-dev for the journal's paper: turns any Panel / PanelContainer into a lit sheet of paper
## (crumples, folds, torn edges with fibres, age, cast shadow, curl) with one shader pass, gives
## Labels an ink shader and the book a leather one. See paper_lookdev.tscn for all of it at once.
##
## Adopting it in ui/ needs no rewrite: where a PanelContainer gets `Paper.paper_box(...)`, call
## `PaperLook.apply(panel, "<preset>", seed)` instead. Everything is seeded (same seed, same sheet).

const SHEET_SHADER := preload("res://lookdev/paper/paper_sheet.gdshader")
const INK_SHADER := preload("res://lookdev/paper/paper_ink.gdshader")
const LEATHER_SHADER := preload("res://lookdev/paper/leather_cover.gdshader")
const CRUMPLE_TEX := preload("res://lookdev/paper/textures/crumple_normal.png")
const DETAIL_TEX := preload("res://lookdev/paper/textures/paper_detail.png")
const LEATHER_TEX := preload("res://lookdev/paper/textures/leather_normal.png")
const LEAF_TEX := preload("res://lookdev/paper/textures/pressed_leaf.png")

## Uniform presets. Anything can be overridden per sheet.
const PRESETS: Dictionary = {
	# A bound page of the book: lies flat, only cockled a little by damp, curves into the gutter.
	"book_page": {"ruling": 0, "rule_step": 38.0, "rule_offset": Vector2(52.0, 150.0), "rule_color": Color(0.55, 0.62, 0.7),
		"crumple": 0.12, "crumple_scale": 0.6, "foxing": 0.35, "mottle": 0.4, "edge_age": 0.55, "curl": 0.0,
		"gutter": -1.0, "gutter_width": 34.0, "shadow_alpha": 0.25, "shadow_soft": 5.0, "shadow_offset": Vector2(2.0, 3.0), "pad": 16.0},
	# A page torn out of a squared notebook, folded to carry and crumpled.
	"torn_page": {"ruling": 1, "rule_step": 24.0, "rule_offset": Vector2(9.0, 5.0), "rule_color": Color(0.5, 0.64, 0.78),
		"torn": Vector4(0.0, 20.0, 0.0, 0.0), "tear_scale": 0.8, "crumple": 0.42, "crumple_scale": 0.85, "folds": 2, "fold_strength": 0.16,
		"foxing": 0.2, "mottle": 0.25, "edge_age": 0.45, "curl": 16.0, "paper_color": Color(0.955, 0.94, 0.9),
		"shadow_alpha": 0.5, "shadow_soft": 12.0, "shadow_offset": Vector2(5.0, 10.0), "pad": 40.0},
	# A scrap torn off for a HUD readout: small, rough on both ends, well handled.
	"scrap": {"ruling": 0, "torn": Vector4(9.0, 0.0, 9.0, 0.0), "tear_scale": 0.45, "crumple": 0.75, "crumple_scale": 1.4,
		"foxing": 0.45, "mottle": 0.4, "edge_age": 0.6, "stain": 0.0, "curl": 3.0,
		"shadow_alpha": 0.4, "shadow_soft": 5.0, "shadow_offset": Vector2(3.0, 5.0), "pad": 16.0},
	# A longer strip torn from the bottom of a page (the hint note).
	"strip": {"ruling": 0, "torn": Vector4(0.0, 8.0, 0.0, 10.0), "tear_scale": 0.6, "crumple": 0.4, "crumple_scale": 1.1,
		"foxing": 0.3, "mottle": 0.25, "edge_age": 0.45, "curl": 6.0,
		"shadow_alpha": 0.45, "shadow_soft": 7.0, "shadow_offset": Vector2(3.0, 5.0), "pad": 20.0},
	# Masking tape: yellowed, translucent, torn at both ends.
	"tape": {"ruling": 0, "torn": Vector4(4.0, 0.0, 4.0, 0.0), "tear_scale": 0.15, "crumple": 0.25, "crumple_scale": 2.0,
		"foxing": 0.0, "mottle": 0.6, "edge_age": 0.2, "opacity": 0.72, "paper_color": Color(0.9, 0.84, 0.66),
		"age_color": Color(0.8, 0.68, 0.45), "grain": 2.0, "shadow_alpha": 0.18, "shadow_soft": 2.0, "shadow_offset": Vector2(1.0, 1.5), "pad": 6.0},
}

static var _white: ImageTexture
static var _ink: Dictionary = {}


## Makes `c` (a Panel or PanelContainer) draw as a sheet of paper. `content_margin` is the
## writing margin inside the sheet; torn edges add their depth to it. Returns the material.
static func apply(c: Control, preset: String, seed: int, content_margin: float = 30.0, overrides: Dictionary = {}) -> ShaderMaterial:
	var params: Dictionary = (PRESETS[preset] as Dictionary).duplicate()
	params.merge(overrides, true)
	var mat := sheet_material(params, seed)
	var pad: float = params.get("pad", 24.0)
	var torn: Vector4 = params.get("torn", Vector4.ZERO)
	var sb := StyleBoxTexture.new()
	sb.texture = _white_texture()
	sb.set_expand_margin_all(pad)
	sb.content_margin_left = content_margin + torn.x * 0.6
	sb.content_margin_top = content_margin * 0.8 + torn.y * 0.6
	sb.content_margin_right = content_margin + torn.z * 0.6
	sb.content_margin_bottom = content_margin * 0.7 + torn.w * 0.6
	c.add_theme_stylebox_override("panel", sb)
	c.material = mat
	var fit := func() -> void: mat.set_shader_parameter("sheet_size", c.size)
	fit.call()
	if not c.resized.is_connected(fit):
		c.resized.connect(fit)
	return mat


## A sheet material from a parameter dictionary (see PRESETS for the keys).
static func sheet_material(params: Dictionary, seed: int) -> ShaderMaterial:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "paper_look"])
	var mat := ShaderMaterial.new()
	mat.shader = SHEET_SHADER
	mat.set_shader_parameter("crumple_tex", CRUMPLE_TEX)
	mat.set_shader_parameter("detail_tex", DETAIL_TEX)
	mat.set_shader_parameter("seed", rng.randf_range(0.0, 97.0))
	for k in params:
		if k in ["folds", "fold_strength"]:
			continue
		var v: Variant = params[k]
		if v is Color:
			v = Vector3((v as Color).r, (v as Color).g, (v as Color).b)
		mat.set_shader_parameter(k, v)
	# Folds: once across the middle (carried folded), sometimes once more down it.
	var folds: int = params.get("folds", 0)
	var fs: float = params.get("fold_strength", 0.25)
	if folds >= 1:
		var sgn := 1.0 if rng.randf() < 0.5 else -1.0
		mat.set_shader_parameter("fold_a", Vector4(0.5, rng.randf_range(0.42, 0.56), rng.randf_range(-0.05, 0.05), fs * sgn))
	if folds >= 2:
		mat.set_shader_parameter("fold_b", Vector4(rng.randf_range(0.45, 0.55), 0.5, PI * 0.5 + rng.randf_range(-0.04, 0.04), -fs * 0.8))
	# Now and then a tide-line from a wet cup or a raindrop.
	var stain_w: float = params.get("stain", 0.35)
	if stain_w > 0.0 and rng.randf() < 0.7:
		mat.set_shader_parameter("stain", Vector4(rng.randf_range(0.6, 1.05), rng.randf_range(0.55, 1.0), rng.randf_range(40.0, 75.0), stain_w))
	else:
		mat.set_shader_parameter("stain", Vector4.ZERO)
	return mat


## The ink shader for handwriting (Labels, RichTextLabels, Buttons, LineEdits). Shared per seed.
static func ink_material(seed: int = 0) -> ShaderMaterial:
	if _ink.has(seed):
		return _ink[seed]
	var mat := ShaderMaterial.new()
	mat.shader = INK_SHADER
	mat.set_shader_parameter("detail_tex", DETAIL_TEX)
	mat.set_shader_parameter("seed", float(seed % 1000) * 0.37)
	_ink[seed] = mat
	return mat


## Gives every text control under `node` the ink material (skips controls that have their own).
static func ink_all(node: Node, seed: int = 0) -> void:
	for ch in node.get_children():
		if (ch is Label or ch is RichTextLabel or ch is Button or ch is LineEdit) and (ch as CanvasItem).material == null:
			(ch as CanvasItem).material = ink_material(seed)
		ink_all(ch, seed)


## A leather cover on a Panel (the book).
static func apply_leather(c: Control, spine_x: float = -1.0) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = LEATHER_SHADER
	mat.set_shader_parameter("leather_tex", LEATHER_TEX)
	mat.set_shader_parameter("detail_tex", DETAIL_TEX)
	mat.set_shader_parameter("spine_x", spine_x)
	var sb := StyleBoxTexture.new()
	sb.texture = _white_texture()
	sb.set_expand_margin_all(24.0)
	c.add_theme_stylebox_override("panel", sb)
	c.material = mat
	var fit := func() -> void: mat.set_shader_parameter("sheet_size", c.size)
	fit.call()
	c.resized.connect(fit)
	return mat


## A pressed, dried leaf (CC0 scan, see CREDITS.md) with a thin shadow, for a book page.
static func pressed_leaf(size: float, angle_deg: float) -> Control:
	var holder := Control.new()
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.custom_minimum_size = Vector2(size, size)
	for i in range(2):
		var t := TextureRect.new()
		t.texture = LEAF_TEX
		t.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		t.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		t.size = Vector2(size, size)
		t.pivot_offset = Vector2(size, size) * 0.5
		t.rotation_degrees = angle_deg
		t.mouse_filter = Control.MOUSE_FILTER_IGNORE
		if i == 0:
			# Pressed flat: only a thin, close shadow.
			t.position = Vector2(1.5, 2.5)
			t.modulate = Color(0.12, 0.08, 0.04, 0.35)
		holder.add_child(t)
	return holder


static func _white_texture() -> ImageTexture:
	if _white == null:
		var img := Image.create(4, 4, false, Image.FORMAT_RGBA8)
		img.fill(Color.WHITE)
		_white = ImageTexture.create_from_image(img)
	return _white
