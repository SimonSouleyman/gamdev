class_name ShedMenu
extends CanvasLayer
## The 2D part of the garden shed: the handwritten menu note, the options pinned to the board,
## the photo album and the loading page. All paper and ink (Paper), never plain windows.

signal continue_pressed
signal journal_pressed
signal new_tree_pressed
signal setting_changed(key: String, value: bool)

var settings: Dictionary = {}
var _note: PanelContainer
var _options: Control
var _album: Control
var _album_pages: Array[Control] = []
var _album_left: TextureRect
var _album_right: TextureRect
var _album_cap_l: Label
var _album_cap_r: Label
var _album_title: Label
var _album_index: int = 0
var _photos: Array[String] = []
var _toggles: Dictionary = {}
var _loading: Control


func _ready() -> void:
	layer = 18
	_build_note()
	_build_options()
	_build_album()
	_build_loading()


# --- the menu note pinned inside the door frame -----------------------------------

func _build_note() -> void:
	_note = PanelContainer.new()
	_note.add_theme_stylebox_override("panel", Paper.paper_box(256, 320, 71, "top", 26.0))
	_note.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	# Pinned low on the left, beside the doorway.
	_note.offset_left = -300
	_note.offset_top = -440
	_note.offset_right = -22
	_note.offset_bottom = -30
	_note.rotation_degrees = -2.5
	add_child(_note)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	_note.add_child(box)
	box.add_child(Paper.ink_label("Tree", 52, Paper.INK, true))
	box.add_child(Paper.ink_label("my linden", 26, Paper.FAINT_INK))
	for pair in [["go outside", "continue"], ["read the journal", "journal"], ["look at the photos", "album"], ["options", "options"]]:
		var b := Paper.ink_button(pair[0], 28)
		b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		var key: String = pair[1]
		b.pressed.connect(func() -> void: _on_menu(key))
		box.add_child(b)


func _on_menu(key: String) -> void:
	match key:
		"continue":
			continue_pressed.emit()
		"journal":
			journal_pressed.emit()
		"album":
			open_album()
		"options":
			open_options()


func show_menu(on: bool) -> void:
	_note.visible = on
	if not on:
		_options.visible = false
		_album.visible = false


func is_busy() -> bool:
	return _options.visible or _album.visible


# --- options, pinned to the board -------------------------------------------------

func _build_options() -> void:
	_options = Control.new()
	_options.set_anchors_preset(Control.PRESET_FULL_RECT)
	_options.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_options)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.45)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_options.add_child(dim)
	# A cork board with notes pinned on it.
	var board := Panel.new()
	board.add_theme_stylebox_override("panel", _cork_box())
	board.set_anchors_preset(Control.PRESET_FULL_RECT)
	board.offset_left = 30
	board.offset_right = -30
	board.offset_top = 150
	board.offset_bottom = -560
	_options.add_child(board)
	var names := {"sound": "sound", "no_ui": "no UI (pure scenery)", "battery_saver": "battery saver", "notifications": "a note each day"}
	var i := 0
	for key in names:
		var note := PanelContainer.new()
		note.add_theme_stylebox_override("panel", Paper.paper_box(200, 90, 80 + i, "all", 18.0))
		note.position = Vector2(60 + (i % 2) * 300, 70 + (i / 2) * 240)
		note.custom_minimum_size = Vector2(260, 150)
		note.rotation_degrees = [-3.0, 2.0, 1.5, -2.0][i]
		board.add_child(note)
		var c := CheckBox.new()
		c.text = names[key]
		c.focus_mode = Control.FOCUS_NONE
		c.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		c.add_theme_font_override("font", Paper.hand_font())
		c.add_theme_font_size_override("font_size", 27)
		for fc in ["font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color"]:
			c.add_theme_color_override(fc, Paper.INK)
		var icons := Paper.check_icons()
		c.add_theme_icon_override("unchecked", icons[0])
		c.add_theme_icon_override("checked", icons[1])
		c.toggled.connect(func(on: bool) -> void:
			settings[key] = on
			setting_changed.emit(key, on))
		note.add_child(c)
		_toggles[key] = c
		# A red pin.
		var pin := Control.new()
		pin.mouse_filter = Control.MOUSE_FILTER_IGNORE
		pin.draw.connect(func() -> void:
			pin.draw_circle(Vector2(130, 4), 9.0, Color(0.7, 0.15, 0.12))
			pin.draw_circle(Vector2(127, 1), 3.0, Color(1, 0.7, 0.65)))
		note.add_child(pin)
		i += 1
	var back := Paper.ink_button("back", 30)
	back.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	back.offset_left = -60
	back.offset_right = 60
	back.offset_top = -540
	back.offset_bottom = -490
	back.add_theme_stylebox_override("normal", Paper.paper_box(96, 48, 90, "all", 14.0))
	back.pressed.connect(func() -> void:
		_options.visible = false
		_note.visible = true)
	_options.add_child(back)
	_options.visible = false


## Cork in a wooden frame: speckled noise, generated once.
func _cork_box() -> StyleBoxTexture:
	var img := Image.create(128, 128, false, Image.FORMAT_RGBA8)
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	for y in range(128):
		for x in range(128):
			var frame := x < 10 or y < 10 or x > 117 or y > 117
			var c := Color(0.36, 0.25, 0.16) if frame else Color(0.66, 0.5, 0.33).darkened(rng.randf() * 0.25).lightened(rng.randf() * 0.08)
			img.set_pixel(x, y, c)
	var sb := StyleBoxTexture.new()
	sb.texture = ImageTexture.create_from_image(img)
	sb.texture_margin_left = 12
	sb.texture_margin_right = 12
	sb.texture_margin_top = 12
	sb.texture_margin_bottom = 12
	sb.axis_stretch_horizontal = StyleBoxTexture.AXIS_STRETCH_MODE_TILE_FIT
	sb.axis_stretch_vertical = StyleBoxTexture.AXIS_STRETCH_MODE_TILE_FIT
	return sb


func open_options() -> void:
	_note.visible = false
	for k in _toggles:
		(_toggles[k] as CheckBox).set_pressed_no_signal(bool(settings.get(k, false)))
	_options.visible = true


# --- the photo album -------------------------------------------------------------

func _build_album() -> void:
	_album = Control.new()
	_album.set_anchors_preset(Control.PRESET_FULL_RECT)
	_album.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_album)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.7)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_album.add_child(dim)
	# Green cloth cover, cream pages, two photos per spread (portrait: one above the other).
	var cover := Panel.new()
	var cloth := StyleBoxFlat.new()
	cloth.bg_color = Color(0.2, 0.32, 0.2)
	cloth.border_color = Color(0.12, 0.2, 0.12)
	cloth.set_border_width_all(6)
	cloth.set_corner_radius_all(12)
	cloth.shadow_color = Color(0, 0, 0, 0.5)
	cloth.shadow_size = 16
	cover.add_theme_stylebox_override("panel", cloth)
	cover.set_anchors_preset(Control.PRESET_FULL_RECT)
	cover.offset_left = 14
	cover.offset_right = -14
	cover.offset_top = 40
	cover.offset_bottom = -40
	_album.add_child(cover)
	var page := PanelContainer.new()
	page.add_theme_stylebox_override("panel", Paper.paper_box(360, 640, 95, "", 30.0, Color(0.8, 0.72, 0.6), "beige"))
	page.set_anchors_preset(Control.PRESET_FULL_RECT)
	page.offset_left = 34
	page.offset_right = -34
	page.offset_top = 60
	page.offset_bottom = -60
	_album.add_child(page)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	page.add_child(box)
	var head := HBoxContainer.new()
	box.add_child(head)
	_album_title = Paper.ink_label("My linden", 40, Paper.INK, true)
	_album_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(_album_title)
	var close := Paper.ink_button("close", 26)
	close.pressed.connect(func() -> void:
		_album.visible = false
		_note.visible = true)
	head.add_child(close)
	for side in [0, 1]:
		var holder := CenterContainer.new()
		holder.size_flags_vertical = Control.SIZE_EXPAND_FILL
		box.add_child(holder)
		var polaroid := PanelContainer.new()
		var white := StyleBoxFlat.new()
		white.bg_color = Color(0.97, 0.96, 0.93)
		white.content_margin_left = 12
		white.content_margin_right = 12
		white.content_margin_top = 12
		white.content_margin_bottom = 8
		white.shadow_color = Color(0, 0, 0, 0.3)
		white.shadow_size = 6
		polaroid.add_theme_stylebox_override("panel", white)
		polaroid.rotation_degrees = -5.0 if side == 0 else 4.0
		# A strip of tape over the top edge.
		var tape := ColorRect.new()
		tape.color = Color(0.95, 0.92, 0.8, 0.75)
		tape.custom_minimum_size = Vector2(90, 24)
		tape.mouse_filter = Control.MOUSE_FILTER_IGNORE
		tape.position = Vector2(110, -10)
		tape.rotation_degrees = 3.0
		polaroid.add_child(tape)
		tape.top_level = false
		holder.add_child(polaroid)
		var v := VBoxContainer.new()
		polaroid.add_child(v)
		var tex := TextureRect.new()
		tex.custom_minimum_size = Vector2(300, 300)
		tex.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		tex.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
		v.add_child(tex)
		var cap := Paper.ink_label("", 26)
		cap.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		cap.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		cap.custom_minimum_size = Vector2(250, 0)
		v.add_child(cap)
		if side == 0:
			_album_left = tex
			_album_cap_l = cap
		else:
			_album_right = tex
			_album_cap_r = cap
	var nav := HBoxContainer.new()
	nav.alignment = BoxContainer.ALIGNMENT_CENTER
	nav.add_theme_constant_override("separation", 60)
	box.add_child(nav)
	var prev := Paper.ink_button("< earlier", 24)
	prev.pressed.connect(func() -> void: _turn(-2))
	nav.add_child(prev)
	var next := Paper.ink_button("later >", 24)
	next.pressed.connect(func() -> void: _turn(2))
	nav.add_child(next)
	_album.visible = false


func open_album() -> void:
	_note.visible = false
	_photos = Photos.list()
	_album_index = maxi(0, _photos.size() - 2)
	if _album_index % 2 == 1:
		_album_index -= 1
	_show_spread()
	_album.visible = true


func _turn(step: int) -> void:
	var next := _album_index + step
	if next < 0 or next >= maxi(_photos.size(), 1):
		return
	_album_index = next
	_show_spread()


func _show_spread() -> void:
	for side in [0, 1]:
		var i: int = _album_index + int(side)
		var tex: TextureRect = _album_left if side == 0 else _album_right
		var cap: Label = _album_cap_l if side == 0 else _album_cap_r
		if i < _photos.size():
			tex.texture = Photos.load_texture(_photos[i])
			cap.text = Photos.caption(_photos[i])
			tex.get_parent().get_parent().visible = true
		else:
			tex.texture = null
			cap.text = "(no photo yet: every morning takes one, and the camera scrap outside takes more)" if _photos.is_empty() and side == 0 else ""
			tex.get_parent().get_parent().visible = side == 0 and _photos.is_empty()


# --- loading page -------------------------------------------------------------------

func _build_loading() -> void:
	_loading = PanelContainer.new()
	_loading.add_theme_stylebox_override("panel", Paper.paper_box(360, 640, 99, "", 40.0, Paper.PAPER, "cream"))
	_loading.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_loading)
	var box := VBoxContainer.new()
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	_loading.add_child(box)
	var sketch := Control.new()
	sketch.custom_minimum_size = Vector2(0, 420)
	sketch.draw.connect(func() -> void: _draw_tree_sketch(sketch))
	box.add_child(sketch)
	var t := Paper.ink_label("Tree", 64, Paper.INK, true)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(t)
	var sub := Paper.ink_label("the forest is growing...", 30, Paper.FAINT_INK)
	sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(sub)
	_loading.visible = false


## A small ink drawing of a tree for the loading page.
func _draw_tree_sketch(c: Control) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 3
	var base := Vector2(c.size.x * 0.5, c.size.y - 20)
	c.draw_line(base + Vector2(-90, 0), base + Vector2(90, 0), Color(Paper.INK, 0.6), 2.0, true)
	_branch(c, rng, base, -PI * 0.5, 120.0, 7.0, 0)


func _branch(c: Control, rng: RandomNumberGenerator, p: Vector2, ang: float, length: float, width: float, depth: int) -> void:
	var q := p + Vector2(cos(ang), sin(ang)) * length
	c.draw_line(p, q, Color(Paper.INK, 0.85), width, true)
	if depth >= 6:
		c.draw_circle(q, 6.0 + rng.randf() * 4.0, Color(0.3, 0.45, 0.2, 0.5))
		return
	for k in [-1, 1]:
		_branch(c, rng, q, ang + k * rng.randf_range(0.25, 0.55), length * rng.randf_range(0.62, 0.78), maxf(1.0, width * 0.68), depth + 1)


func show_loading(on: bool) -> void:
	_loading.visible = on
