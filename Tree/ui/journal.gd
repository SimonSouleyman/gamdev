class_name Journal
extends CanvasLayer
## The journal (design doc sections 2, 8, 9): every hint is a page of one notebook.
## Small things are torn-out pages that flutter in (the tutorial lives there); the big menus
## open the book itself: a leather-bound notebook with ribbon tabs for the diary, the pages to
## read again and the settings. Handwriting and paper come from Paper.

signal page_closed(page_id: String)
signal opened_changed(is_open: bool)
signal setting_changed(key: String, value: bool)

## A page can only be turned after a short moment, so a tap meant for the game does not
## throw away a page nobody has read.
const MIN_PAGE_SECONDS := 0.6

var state: GameState
## "any_species" is the test switch on the shed's pinboard: every species plantable now.
## "vibration" (haptics) and "clearer print" (a larger, legible hand) are pinboard switches from 0.6.
var settings: Dictionary = {"sound": true, "no_ui": false, "battery_saver": false, "notifications": true, "any_species": false,
	"vibration": true, "clearer_print": false}

var _queue: Array = []  # [{id, title, body}]
## Pages put aside while the player is in the shed; they come back outside.
var _held: Array = []
var _page: Control
var _page_sheet: PanelContainer
var _page_title: Label
var _page_body: Label
var _page_id: String = ""
var _page_shown_at: float = 0.0
var _pages_torn: int = 0

var _book: Control
var _book_page: PanelContainer
var _tabs: Dictionary = {}  # name -> Control (content)
var _tab_buttons: Dictionary = {}
var _current_tab: String = "diary"
var _diary_text: RichTextLabel
var _wish_label: Label
var _note: LineEdit
var _toggles: Dictionary = {}
var _open_button: TextureButton
var _pages_list: VBoxContainer
var _pages_empty: Label
var _book_title: Label
## The clearing's collection: what came up in the crown's shade (Clearing.found).
var _clearing_list: VBoxContainer
var _clearing_more: Label
## The care page (0.6.3): the crown and the roots read in words (Care.page).
var _care_box: VBoxContainer


func _ready() -> void:
	layer = 20
	_build_open_button()
	_build_page()
	_build_book()


func is_book_open() -> bool:
	return _book.visible


func is_open() -> bool:
	return _page.visible or _book.visible


# --- torn-out pages -----------------------------------------------------------

## Queues a one-time page. Pages show one after another; each closes with a tap.
func show_page(id: String, title: String, body: String) -> void:
	_queue.append({"id": id, "title": title, "body": body})
	if not _page.visible:
		_next_page()


func _next_page() -> void:
	if _queue.is_empty():
		_page.visible = false
		_page_id = ""
		opened_changed.emit(is_open())
		return
	var p: Dictionary = _queue.pop_front()
	_page_id = p["id"]
	_page_title.text = p["title"]
	_page_body.text = p["body"]
	# Each page is torn a little differently and lies a little askew.
	_pages_torn += 1
	# Torn from a squared notebook.
	# Crumpled, torn paper lit like a real sheet (visuals thread).
	PaperLook.apply(_page_sheet, "torn_page", 40 + _pages_torn % 5, 34.0)
	_page.visible = true
	# Above the book, when a page is opened from its "pages" tab.
	move_child(_page, -1)
	_page_shown_at = Time.get_ticks_msec() / 1000.0
	# The page flutters in: drops a little, turns into place.
	_page.modulate.a = 0.0
	_page_sheet.rotation_degrees = -6.0 + (_pages_torn % 3) * 2.0
	_page_sheet.pivot_offset = _page_sheet.size * 0.5
	var tw := create_tween().set_parallel(true)
	tw.tween_property(_page, "modulate:a", 1.0, 0.3)
	tw.tween_property(_page_sheet, "rotation_degrees", -1.5 + (_pages_torn % 3) * 1.2, 0.45).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	opened_changed.emit(true)


## Drops every open or queued page (a new game or a load starts clean).
func clear_pages() -> void:
	_queue.clear()
	_held.clear()
	_page.visible = false
	_page_id = ""
	_book.visible = false


## Puts the open and queued pages aside without marking them read (entering the shed).
func hold_pages() -> void:
	if _page.visible:
		_held.append({"id": _page_id, "title": _page_title.text, "body": _page_body.text})
		_page.visible = false
		_page_id = ""
	_held.append_array(_queue)
	_queue.clear()
	opened_changed.emit(is_open())


## Brings the pages put aside back (leaving the shed).
func release_pages() -> void:
	_queue = _held + _queue
	_held.clear()
	if not _page.visible and not _queue.is_empty():
		_next_page()


## Ids of the page on screen, the queued and the held ones, in order (saved with the game).
func pending_ids() -> Array[String]:
	var out: Array[String] = []
	if _page.visible and _page_id != "":
		out.append(_page_id)
	for p in _held + _queue:
		out.append(str(p["id"]))
	return out


func current_page() -> String:
	return _page_id if _page.visible else ""


func close_page() -> void:
	if not _page.visible:
		return
	var id := _page_id
	_page.visible = false
	page_closed.emit(id)
	_next_page()


func _build_page() -> void:
	_page = Control.new()
	_page.set_anchors_preset(Control.PRESET_FULL_RECT)
	_page.mouse_filter = Control.MOUSE_FILTER_STOP
	_page.gui_input.connect(func(e: InputEvent) -> void:
		if e is InputEventMouseButton and not e.pressed and (e as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT \
				and Time.get_ticks_msec() / 1000.0 - _page_shown_at >= MIN_PAGE_SECONDS:
			close_page())
	add_child(_page)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.4)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(dim)
	var holder := CenterContainer.new()
	holder.set_anchors_preset(Control.PRESET_FULL_RECT)
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(holder)
	_page_sheet = PanelContainer.new()
	_page_sheet.custom_minimum_size = Vector2(620, 0)
	_page_sheet.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(_page_sheet)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page_sheet.add_child(box)
	_page_title = Paper.ink_label("", 44, Paper.INK, true)
	box.add_child(_page_title)
	box.add_child(_rule())
	_page_body = Paper.ink_label("", 29)
	_page_body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_page_body.custom_minimum_size = Vector2(540, 0)
	box.add_child(_page_body)
	var footer := Paper.ink_label("tap to turn the page", 22, Paper.FAINT_INK)
	footer.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	box.add_child(footer)
	_page.visible = false


## A hand-drawn line under a heading.
func _rule() -> Control:
	var c := Control.new()
	c.custom_minimum_size = Vector2(0, 8)
	c.mouse_filter = Control.MOUSE_FILTER_IGNORE
	c.draw.connect(func() -> void:
		var w := c.size.x
		var pts := PackedVector2Array()
		for i in range(21):
			var x := w * 0.62 * i / 20.0
			pts.append(Vector2(x, 3.0 + sin(i * 1.7) * 1.2))
		c.draw_polyline(pts, Color(Paper.INK, 0.7), 2.0, true))
	return c


# --- the button that opens the book ------------------------------------------------

func _build_open_button() -> void:
	# A small leather notebook, the top of the column of pictures at the middle right, clear of
	# the compass and the sun's arc (Simon, 0.5.1 and 0.6).
	_open_button = Paper.picture_button(preload("res://ui/icons/journal.png"), -252.0, 2.0)
	_open_button.pressed.connect(open_diary)
	add_child(_open_button)


func set_button_visible(on: bool) -> void:
	_open_button.visible = on


func set_button_enabled(on: bool) -> void:
	_open_button.disabled = not on


## In "no UI" mode the button stays, faint, so the setting can always be switched back.
func set_button_faint(on: bool) -> void:
	_open_button.modulate.a = 0.3 if on else 1.0


# --- the book ---------------------------------------------------------------------

func _build_book() -> void:
	_book = Control.new()
	_book.set_anchors_preset(Control.PRESET_FULL_RECT)
	_book.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_book)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.78)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_book.add_child(dim)

	# The leather cover, a little larger than the page.
	var cover := Panel.new()
	cover.add_theme_stylebox_override("panel", Paper.cover_box())
	PaperLook.apply_leather(cover)
	cover.set_anchors_preset(Control.PRESET_FULL_RECT)
	cover.offset_left = 8
	cover.offset_right = -40
	cover.offset_top = 22
	cover.offset_bottom = -18
	_book.add_child(cover)
	# The stack of pages under the open page (edges showing at the right and bottom).
	for i in range(3):
		var edge := Panel.new()
		var sb := StyleBoxFlat.new()
		sb.bg_color = Paper.PAPER_SHADE.darkened(0.04 * (3 - i))
		sb.set_corner_radius_all(4)
		edge.add_theme_stylebox_override("panel", sb)
		edge.set_anchors_preset(Control.PRESET_FULL_RECT)
		edge.offset_left = 40
		edge.offset_right = -64 + i * 3
		edge.offset_top = 58
		edge.offset_bottom = -52 + i * 3
		edge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_book.add_child(edge)

	_book_page = PanelContainer.new()
	# The book's pages: smooth cream paper.
	PaperLook.apply(_book_page, "book_page", 11, 34.0)
	_book_page.set_anchors_preset(Control.PRESET_FULL_RECT)
	_book_page.offset_left = 40
	_book_page.offset_right = -70
	_book_page.offset_top = 56
	_book_page.offset_bottom = -58
	_book.add_child(_book_page)
	# The spine: a soft shadow down the left edge where the pages bind.
	var spine := TextureRect.new()
	var grad := GradientTexture2D.new()
	var g := Gradient.new()
	g.set_color(0, Color(0.2, 0.12, 0.05, 0.45))
	g.set_color(1, Color(0.2, 0.12, 0.05, 0.0))
	grad.gradient = g
	grad.width = 32
	grad.height = 4
	spine.texture = grad
	spine.stretch_mode = TextureRect.STRETCH_SCALE
	spine.set_anchors_preset(Control.PRESET_LEFT_WIDE)
	spine.offset_left = 40
	spine.offset_right = 80
	spine.offset_top = 56
	spine.offset_bottom = -58
	spine.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_book.add_child(spine)

	var stack := VBoxContainer.new()
	stack.add_theme_constant_override("separation", 12)
	_book_page.add_child(stack)
	var head := HBoxContainer.new()
	stack.add_child(head)
	var title := Paper.ink_label("My linden", 46, Paper.INK, true)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	_book_title = title
	var close := Paper.ink_button("close", 26)
	close.pressed.connect(close_diary)
	head.add_child(close)
	stack.add_child(_rule())

	var content := Control.new()
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	stack.add_child(content)
	_tabs["diary"] = _build_diary_tab()
	_tabs["pages"] = _build_pages_tab()
	_tabs["settings"] = _build_settings_tab()
	_tabs["clearing"] = _build_clearing_tab()
	_tabs["care"] = _build_care_tab()
	for k in _tabs:
		var c: Control = _tabs[k]
		c.set_anchors_preset(Control.PRESET_FULL_RECT)
		content.add_child(c)

	# Cloth ribbon bookmarks sticking out of the right edge of the book, with forked ends.
	var ribbons := {"diary": Color(0.62, 0.2, 0.16), "care": Color(0.2, 0.36, 0.5), "pages": Color(0.25, 0.38, 0.22), "clearing": Color(0.52, 0.42, 0.16)}
	var y := 120
	for k in ribbons:
		var b := Button.new()
		b.text = k
		b.focus_mode = Control.FOCUS_NONE
		b.add_theme_font_override("font", Paper.hand_font(true))
		b.add_theme_font_size_override("font_size", 22)
		for fc in ["font_color", "font_hover_color", "font_pressed_color"]:
			b.add_theme_color_override(fc, Color(0.98, 0.95, 0.88))
		# The text sits clear of the V cut in the ribbon's end.
		var pad := StyleBoxEmpty.new()
		pad.content_margin_left = 10
		pad.content_margin_right = 20
		for s in ["normal", "hover", "pressed", "focus"]:
			b.add_theme_stylebox_override(s, pad)
		var cloth := Control.new()
		cloth.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cloth.set_anchors_preset(Control.PRESET_FULL_RECT)
		cloth.show_behind_parent = true
		var col: Color = ribbons[k]
		cloth.draw.connect(func() -> void:
			var w := cloth.size.x
			var h := cloth.size.y
			# A strip of cloth with a V cut into its free end.
			cloth.draw_colored_polygon(PackedVector2Array([Vector2(0, 0), Vector2(w, 0), Vector2(w - 16, h * 0.5), Vector2(w, h), Vector2(0, h)]), col)
			cloth.draw_colored_polygon(PackedVector2Array([Vector2(0, 0), Vector2(8, 0), Vector2(8, h), Vector2(0, h)]), col.darkened(0.25))
			for i in range(0, int(h), 10):
				cloth.draw_line(Vector2(12, i + 2), Vector2(12, i + 6), col.lightened(0.35), 1.0))
		b.add_child(cloth)
		b.set_anchors_preset(Control.PRESET_TOP_RIGHT)
		b.offset_left = -92
		b.offset_right = -2
		b.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		b.offset_top = y
		b.offset_bottom = y + 110
		b.rotation_degrees = 0
		b.pressed.connect(func() -> void: _show_tab(k))
		_book.add_child(b)
		_tab_buttons[k] = b
		y += 124
	_show_tab("diary", false)
	_book.visible = false


func _build_diary_tab() -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	_wish_label = Paper.ink_label("", 26, Paper.RED_INK)
	_wish_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_wish_label)
	_diary_text = RichTextLabel.new()
	_diary_text.bbcode_enabled = true
	_diary_text.scroll_following = true
	_diary_text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_diary_text.add_theme_color_override("default_color", Paper.INK)
	for k in ["normal_font", "bold_font", "italics_font"]:
		_diary_text.add_theme_font_override(k, Paper.hand_font(k == "bold_font"))
	_diary_text.add_theme_font_size_override("normal_font_size", 25)
	_diary_text.add_theme_font_size_override("bold_font_size", 30)
	_diary_text.add_theme_font_size_override("italics_font_size", 25)
	box.add_child(_diary_text)
	var note_row := HBoxContainer.new()
	box.add_child(note_row)
	_note = LineEdit.new()
	_note.placeholder_text = "write a note for today..."
	_note.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_note.add_theme_font_override("font", Paper.hand_font())
	_note.add_theme_font_size_override("font_size", 25)
	_note.add_theme_color_override("font_color", Paper.INK)
	_note.add_theme_color_override("font_placeholder_color", Color(Paper.FAINT_INK, 0.7))
	_note.add_theme_color_override("caret_color", Paper.INK)
	var line := StyleBoxLine.new()
	line.color = Color(Paper.INK, 0.5)
	line.thickness = 2
	line.grow_begin = 0
	line.vertical = false
	var empty := StyleBoxEmpty.new()
	_note.add_theme_stylebox_override("normal", empty)
	_note.add_theme_stylebox_override("focus", empty)
	note_row.add_child(_note)
	var add := Paper.ink_button("write", 24)
	add.pressed.connect(_add_note)
	note_row.add_child(add)
	_note.text_submitted.connect(func(_t: String) -> void: _add_note())
	return box


func _build_pages_tab() -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	box.add_child(Paper.ink_label("Pages to read again", 32, Paper.INK, true))
	_pages_empty = Paper.ink_label("Nothing to read again yet. Pages the journal shows you will be kept here.", 25, Paper.FAINT_INK)
	_pages_empty.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_pages_empty)
	_pages_list = VBoxContainer.new()
	_pages_list.add_theme_constant_override("separation", 10)
	box.add_child(_pages_list)
	return box


## The first collection (design doc 17.6): a handwritten list of the shade plants and mushrooms
## that have come up under the crown, each with a small painted drawing and the day it came.
func _build_clearing_tab() -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	box.add_child(Paper.ink_label("Under my crown", 32, Paper.INK, true))
	var intro := Paper.ink_label("Where the crown's shade falls, the sun meadow gives way. What has come up there so far:", 25, Paper.FAINT_INK)
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(intro)
	_clearing_list = VBoxContainer.new()
	_clearing_list.add_theme_constant_override("separation", 14)
	box.add_child(_clearing_list)
	_clearing_more = Paper.ink_label("", 24, Paper.FAINT_INK)
	_clearing_more.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_clearing_more)
	return box


## The care page (0.6.3): what the tree lacks and where tonight's root finds it, how the
## crown is shaped, and what the last cut did, read from the crown and the roots.
func _build_care_tab() -> Control:
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_care_box = VBoxContainer.new()
	_care_box.add_theme_constant_override("separation", 10)
	_care_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_care_box)
	return scroll


func _refresh_care() -> void:
	for c in _care_box.get_children():
		c.queue_free()
	_care_box.add_child(Paper.ink_label("Reading my tree", 32, Paper.INK, true))
	for part in Care.page(state):
		_care_box.add_child(Paper.ink_label(str(part["title"]), 27, Paper.RED_INK, true))
		var text := Paper.ink_label(str(part["text"]), 25)
		text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_care_box.add_child(text)


## Opens the book at the care page.
func open_care() -> void:
	open_diary()
	_show_tab("care", false)


func _refresh_clearing() -> void:
	for c in _clearing_list.get_children():
		c.queue_free()
	var found: Dictionary = state.clearing.found
	var atlas := load(Understory.ATLAS) as Texture2D
	for k in Clearing.KINDS:
		if not found.has(k):
			continue
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 14)
		# A small drawing of it, glued in beside the name.
		var pic := TextureRect.new()
		var at := AtlasTexture.new()
		at.atlas = atlas
		var cell: int = Understory.CELL[k]
		var half := atlas.get_size() * 0.5
		at.region = Rect2(Vector2(cell % 2, cell / 2) * half, half)
		pic.texture = at
		pic.custom_minimum_size = Vector2(96, 96)
		pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		pic.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		row.add_child(pic)
		var words := VBoxContainer.new()
		words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		words.add_child(Paper.ink_label(Clearing.NAMES[k], 30, Paper.INK, true))
		var note := Paper.ink_label("first seen on day %d: %s" % [int(found[k]), Clearing.NOTES[k]], 23, Paper.FAINT_INK)
		note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		words.add_child(note)
		row.add_child(words)
		_clearing_list.add_child(row)
	if found.is_empty():
		_clearing_more.text = "Nothing yet. The young tree's shade is still too small; the ground takes a few days to change."
	elif found.size() < Clearing.KINDS.size():
		_clearing_more.text = "More may come as the crown grows, and after rain."
	else:
		_clearing_more.text = ""


func _build_settings_tab() -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 16)
	box.add_child(Paper.ink_label("Settings", 32, Paper.INK, true))
	var names := {"sound": "sound", "no_ui": "no UI (pure scenery)", "battery_saver": "battery saver", "notifications": "a daily note (later)"}
	for key in names:
		var c := CheckBox.new()
		c.text = names[key]
		c.focus_mode = Control.FOCUS_NONE
		c.add_theme_font_override("font", Paper.hand_font())
		c.add_theme_font_size_override("font_size", 28)
		for fc in ["font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color"]:
			c.add_theme_color_override(fc, Paper.INK)
		var icons := Paper.check_icons()
		c.add_theme_icon_override("unchecked", icons[0])
		c.add_theme_icon_override("checked", icons[1])
		c.add_theme_icon_override("unchecked_disabled", icons[0])
		c.add_theme_icon_override("checked_disabled", icons[1])
		c.button_pressed = settings[key]
		c.toggled.connect(func(on: bool) -> void:
			settings[key] = on
			setting_changed.emit(key, on))
		box.add_child(c)
		_toggles[key] = c
	return box


func _show_tab(name: String, animate: bool = true) -> void:
	_current_tab = name
	for k in _tabs:
		(_tabs[k] as Control).visible = k == name
	for k in _tab_buttons:
		# The chosen ribbon sticks out a little further.
		(_tab_buttons[k] as Button).offset_left = -86 if k == name else -70
	if animate:
		_turn_page()


## A page turn: the page squeezes toward the spine and opens again.
func _turn_page() -> void:
	_book_page.pivot_offset = Vector2(0, _book_page.size.y * 0.5)
	var tw := create_tween()
	tw.tween_property(_book_page, "scale:x", 0.08, 0.12).set_ease(Tween.EASE_IN)
	tw.tween_property(_book_page, "scale:x", 1.0, 0.18).set_ease(Tween.EASE_OUT)


func open_diary() -> void:
	_refresh_diary()
	_book.visible = true
	_book.modulate.a = 0.0
	_book_page.scale = Vector2(0.9, 0.96)
	_book_page.pivot_offset = Vector2(0, _book_page.size.y * 0.5)
	var tw := create_tween().set_parallel(true)
	tw.tween_property(_book, "modulate:a", 1.0, 0.2)
	tw.tween_property(_book_page, "scale", Vector2.ONE, 0.3).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	opened_changed.emit(true)


func close_diary() -> void:
	_book.visible = false
	opened_changed.emit(is_open())


func _add_note() -> void:
	var text := _note.text.strip_edges()
	if text.is_empty() or state == null:
		return
	state.diary.add(state.day_number(), text, "player")
	_note.text = ""
	_refresh_diary()


func _refresh_diary() -> void:
	if state == null:
		return
	_book_title.text = "My " + state.tree_name()
	_wish_label.text = ("A wish: " + state.diary.wish) if state.diary.wish != "" else ""
	_wish_label.visible = state.diary.wish != ""
	var out := ""
	var last_day := -1
	for e in state.diary.entries:
		var day := int(e["day"])
		if day != last_day:
			out += "\n[b]Day %d[/b]\n" % day
			last_day = day
		out += ("[i][color=#8a2c1e]%s[/color][/i]\n" if e["by"] == "player" else "%s\n") % str(e["text"]).replace("[", "[lb]")
	_diary_text.text = out
	for c in _pages_list.get_children():
		c.queue_free()
	var any := false
	for id in Pages.ids():
		if state.seen_pages.has(id):
			var b := Paper.ink_button(Pages.title(id), 26)
			b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
			b.pressed.connect(func() -> void: show_page(id, Pages.title(id), Pages.body(id)))
			_pages_list.add_child(b)
			any = true
	_pages_empty.visible = not any
	_refresh_clearing()
	_refresh_care()


func set_setting(key: String, on: bool) -> void:
	settings[key] = on
	if _toggles.has(key):
		(_toggles[key] as CheckBox).set_pressed_no_signal(on)
