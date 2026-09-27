class_name Journal
extends CanvasLayer
## The journal (design doc sections 2, 8, 9): every hint is a page of one notebook.
## Torn-out pages pop up for one-time explanations (the tutorial lives here), and the
## diary page lists what the game wrote, today's wish, a note field and the settings.
## Grey-box: paper colours and the default font; the handwriting fonts come later.

signal page_closed(page_id: String)
signal opened_changed(is_open: bool)
signal setting_changed(key: String, value: bool)

const PAPER := Color(0.96, 0.93, 0.84)
const INK := Color(0.28, 0.2, 0.13)
const FAINT_INK := Color(0.45, 0.36, 0.27)

var state: GameState
var settings: Dictionary = {"sound": true, "no_ui": false, "battery_saver": false, "notifications": true}

var _queue: Array = []  # [{id, title, body}]
var _page: Control
var _page_title: Label
var _page_body: Label
var _page_id: String = ""
var _diary: Control
var _diary_text: RichTextLabel
var _note: LineEdit
var _toggles: Dictionary = {}
var _open_button: Button


func _ready() -> void:
	layer = 20
	_build_open_button()
	_build_page()
	_build_diary()


func is_open() -> bool:
	return _page.visible or _diary.visible


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
	_page.visible = true
	_page.modulate.a = 0.0
	create_tween().tween_property(_page, "modulate:a", 1.0, 0.35)
	opened_changed.emit(true)


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
		if e is InputEventMouseButton and not e.pressed and (e as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
			close_page())
	add_child(_page)
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.35)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(dim)
	var paper := _paper_panel()
	paper.set_anchors_preset(Control.PRESET_CENTER)
	paper.custom_minimum_size = Vector2(600, 0)
	paper.offset_left = -300
	paper.offset_right = 300
	paper.offset_top = -260
	paper.grow_vertical = Control.GROW_DIRECTION_BOTH
	paper.rotation_degrees = -1.2
	paper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(paper)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 14)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	paper.add_child(box)
	_page_title = _ink_label(34)
	box.add_child(_page_title)
	_page_body = _ink_label(26)
	_page_body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_page_body.custom_minimum_size = Vector2(540, 0)
	box.add_child(_page_body)
	var footer := _ink_label(20)
	footer.text = "(tap to turn the page)"
	footer.add_theme_color_override("font_color", FAINT_INK)
	footer.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	box.add_child(footer)
	_page.visible = false


func _paper_panel() -> PanelContainer:
	var p := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = PAPER
	sb.set_corner_radius_all(6)
	sb.shadow_color = Color(0, 0, 0, 0.35)
	sb.shadow_size = 10
	sb.content_margin_left = 30
	sb.content_margin_right = 30
	sb.content_margin_top = 26
	sb.content_margin_bottom = 22
	sb.border_color = Color(0.85, 0.8, 0.68)
	sb.set_border_width_all(2)
	p.add_theme_stylebox_override("panel", sb)
	return p


func _ink_label(size: int) -> Label:
	var l := Label.new()
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", INK)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


# --- the diary ------------------------------------------------------------------

func _build_open_button() -> void:
	_open_button = Button.new()
	_open_button.text = "journal"
	_open_button.focus_mode = Control.FOCUS_NONE
	_open_button.add_theme_font_size_override("font_size", 24)
	_open_button.add_theme_color_override("font_color", INK)
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(PAPER, 0.9)
	sb.set_corner_radius_all(18)
	sb.content_margin_left = 16
	sb.content_margin_right = 16
	sb.content_margin_top = 6
	sb.content_margin_bottom = 6
	for k in ["normal", "hover", "pressed"]:
		_open_button.add_theme_stylebox_override(k, sb)
	_open_button.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	_open_button.offset_left = -150
	_open_button.offset_right = -20
	_open_button.offset_top = 22
	_open_button.offset_bottom = 66
	_open_button.pressed.connect(open_diary)
	add_child(_open_button)


func set_button_visible(on: bool) -> void:
	_open_button.visible = on


func _build_diary() -> void:
	_diary = Control.new()
	_diary.set_anchors_preset(Control.PRESET_FULL_RECT)
	_diary.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_diary)
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.4)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_diary.add_child(dim)
	var paper := _paper_panel()
	paper.set_anchors_preset(Control.PRESET_FULL_RECT)
	paper.offset_left = 30
	paper.offset_right = -30
	paper.offset_top = 60
	paper.offset_bottom = -60
	_diary.add_child(paper)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	paper.add_child(box)
	var head := HBoxContainer.new()
	box.add_child(head)
	var title := _ink_label(38)
	title.text = "Diary"
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	var close := _ink_button("close")
	close.pressed.connect(close_diary)
	head.add_child(close)

	_diary_text = RichTextLabel.new()
	_diary_text.bbcode_enabled = true
	_diary_text.scroll_following = true
	_diary_text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_diary_text.add_theme_color_override("default_color", INK)
	_diary_text.add_theme_font_size_override("normal_font_size", 23)
	_diary_text.add_theme_font_size_override("bold_font_size", 24)
	_diary_text.add_theme_font_size_override("italics_font_size", 23)
	box.add_child(_diary_text)

	var note_row := HBoxContainer.new()
	box.add_child(note_row)
	_note = LineEdit.new()
	_note.placeholder_text = "write a note for today"
	_note.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_note.add_theme_font_size_override("font_size", 22)
	_note.text_submitted.connect(func(_t: String) -> void: _add_note())
	note_row.add_child(_note)
	var add := _ink_button("write")
	add.pressed.connect(_add_note)
	note_row.add_child(add)

	var settings_title := _ink_label(26)
	settings_title.text = "Settings"
	box.add_child(settings_title)
	var grid := GridContainer.new()
	grid.columns = 2
	box.add_child(grid)
	var names := {"sound": "sound", "no_ui": "no UI (pure scenery)", "battery_saver": "battery saver", "notifications": "daily note (later)"}
	for key in names:
		var c := CheckBox.new()
		c.text = names[key]
		c.focus_mode = Control.FOCUS_NONE
		c.add_theme_color_override("font_color", INK)
		c.add_theme_color_override("font_pressed_color", INK)
		c.add_theme_color_override("font_hover_color", INK)
		c.add_theme_font_size_override("font_size", 22)
		c.button_pressed = settings[key]
		c.toggled.connect(func(on: bool) -> void:
			settings[key] = on
			setting_changed.emit(key, on))
		grid.add_child(c)
		_toggles[key] = c
	_diary.visible = false


func _ink_button(text: String) -> Button:
	var b := Button.new()
	b.text = text
	b.focus_mode = Control.FOCUS_NONE
	b.add_theme_font_size_override("font_size", 24)
	b.add_theme_color_override("font_color", INK)
	b.add_theme_color_override("font_hover_color", INK)
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0, 0, 0, 0)
	sb.border_color = INK
	sb.set_border_width_all(2)
	sb.set_corner_radius_all(20)
	sb.content_margin_left = 14
	sb.content_margin_right = 14
	for k in ["normal", "hover", "pressed"]:
		b.add_theme_stylebox_override(k, sb)
	return b


func open_diary() -> void:
	_refresh_diary()
	_diary.visible = true
	opened_changed.emit(true)


func close_diary() -> void:
	_diary.visible = false
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
	var out := ""
	if state.diary.wish != "":
		out += "[i]Wish for today: %s[/i]\n\n" % state.diary.wish
	var last_day := -1
	for e in state.diary.entries:
		var day := int(e["day"])
		if day != last_day:
			out += "\n[b]Day %d[/b]\n" % day
			last_day = day
		out += ("[i]%s[/i]\n" if e["by"] == "player" else "%s\n") % str(e["text"]).replace("[", "[lb]")
	_diary_text.text = out


func set_setting(key: String, on: bool) -> void:
	settings[key] = on
	if _toggles.has(key):
		(_toggles[key] as CheckBox).set_pressed_no_signal(on)
