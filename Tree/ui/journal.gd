class_name Journal
extends CanvasLayer
## The journal (design doc sections 2, 8, 9). 0.8.2.6 (specs/journal-drawers-loop.md, J1 to J4):
## a leather-bound notebook with three ribbons: Today (the wish, what the tree lacks, where
## tonight's root finds it, and an ink sketch of the tree), Diary (one line on the days something
## happened, and the player's notes) and Collection (the plants under the crown, the finds, the
## visitors, the trees grown). The explanation pages sit at the back of the book behind a small
## "notes" corner; a first-time hint is one ink sentence at the screen's edge, gone on the next
## tap. The switches live on the shed's pinboard only. Handwriting and paper come from Paper.

signal page_closed(page_id: String)
signal opened_changed(is_open: bool)
signal setting_changed(key: String, value: bool)

## A hint can only be tapped away after a short moment, so a tap meant for the game does not
## throw away a sentence nobody has read.
const MIN_PAGE_SECONDS := 0.6

var state: GameState
## "any_species" is the test switch on the shed's pinboard: every species plantable now.
## "vibration" (haptics) and "clearer print" (a larger, legible hand) are pinboard switches from 0.6.
var settings: Dictionary = {"sound": true, "no_ui": false, "battery_saver": false, "notifications": true, "any_species": false,
	"vibration": true, "clearer_print": false}

## The finds for the Collection page (0.8.2.6): a hook the shed's drawers can set, returning
## [{"kind": String (InkSketch kind), "text": String, "day": int, optional "where": String (its
## drawer, e.g. "in the left drawer")}]. Unset, the finds come from this tree's diary.
var finds_source: Callable

var _queue: Array = []  # [{id, title, body, doodle}]
## Hints put aside while the player is in the shed; they come back outside.
var _held: Array = []
## The hint on screen: one ink sentence on a slip at the screen's lower edge (0.8.2.6, J4).
var _page: Control
var _page_sheet: PanelContainer
var _page_text: Label
var _page_doodle: TextureRect
var _cur: Dictionary = {}
## Size of a hint's doodle, a page's and a day's, in pixels.
const HINT_DOODLE_SIZE: int = 64
const PAGE_DOODLE_SIZE: int = 104
const DAY_DOODLE_SIZE: int = 72
## The hint's slip sits this far above the screen's lower edge.
const HINT_BOTTOM := 36.0
var _page_id: String = ""
var _page_shown_at: float = 0.0
var _pages_torn: int = 0
## A page read again from the back of the book: the full torn-out page over the book.
var _sheet: Control
var _sheet_paper: PanelContainer
var _sheet_title: Label
var _sheet_body: Label
var _sheet_doodle: TextureRect
var _sheet_marks: Control
var _sheet_id: String = ""

var _book: Control
## The dim around the open book; solid once it is open (the world under it then stops drawing).
var _book_dim: ColorRect
const BOOK_DIM := 0.78
var _book_page: PanelContainer
var _tabs: Dictionary = {}  # name -> Control (content)
var _tab_buttons: Dictionary = {}
## The three ribbons (J1), and the back pages behind the "notes" corner.
const RIBBONS: Array[String] = ["today", "diary", "collection"]
const NOTES_TAB := "notes"
## The book opens at Today, then where it was left.
var _current_tab: String = "today"
var _diary_text: RichTextLabel
var _note: LineEdit
var _notes_corner: Button
var _open_button: TextureButton
var _pages_list: VBoxContainer
var _pages_empty: Label
var _book_title: Label
## The Collection page's box (rebuilt on each open).
var _collection_box: VBoxContainer
## The Today page: its lines and the ink sketch of the tree as it stands.
var _today_box: VBoxContainer
var _tree_sketch: Control
## Hints whose topic names the nutrients by their dots show the key of the four marks (0.8).
const MARK_PAGES: Array[String] = ["first_night", "first_sunset", "sapling"]
var _page_marks: Control


func _ready() -> void:
	layer = 20
	_build_open_button()
	_build_book()
	_build_page()
	_build_sheet()


## 0.8.2.4 (phone: opening the book froze for 0.49 s): the day pages' ink doodles were drawn on
## the first open. They are drawn ahead on worker threads now, a few seconds apart, while the
## book is shut (InkSketch.warm).
var _warm_t: float = 0.0


func _process(delta: float) -> void:
	_warm_t -= delta
	if _warm_t > 0.0 or state == null or _book.visible:
		return
	_warm_t = 3.0
	var kinds: Array = []
	for day in state.diary.days():
		kinds.append(state.diary.doodle(day))
	InkSketch.warm(kinds)


func is_book_open() -> bool:
	return _book.visible


func is_open() -> bool:
	return _page.visible or _book.visible or _sheet.visible


# --- first-time hints (0.8.2.6, J4) ----------------------------------------------------

## Queues a first-time hint. It shows as one ink sentence (Pages.hint) at the screen's edge, one
## after another; each goes on the next tap. The full page stays at the back of the book.
## `doodle`: an InkSketch kind, else the page's own (Pages.doodle).
func show_page(id: String, title: String, body: String, doodle: String = "") -> void:
	if doodle == "":
		doodle = Pages.doodle(id)
	_queue.append({"id": id, "title": title, "body": body, "doodle": doodle})
	if not _page.visible:
		_next_page()


func _next_page() -> void:
	if _queue.is_empty():
		_page.visible = false
		_page_id = ""
		_cur = {}
		opened_changed.emit(is_open())
		return
	_cur = _queue.pop_front()
	_page_id = _cur["id"]
	_page_text.text = Pages.hint(_page_id, str(_cur["body"]))
	var kind := str(_cur.get("doodle", ""))
	_page_doodle.texture = InkSketch.texture(kind) if InkSketch.has(kind) else null
	_page_doodle.visible = _page_doodle.texture != null
	_page_marks.visible = MARK_PAGES.has(_page_id)
	_pages_torn += 1
	PaperLook.apply(_page_sheet, "torn_page", 40 + _pages_torn % 5, 18.0)
	_page.visible = true
	move_child(_page, -1)
	_page_shown_at = Time.get_ticks_msec() / 1000.0
	# The slip fades in.
	_page.modulate.a = 0.0
	create_tween().tween_property(_page, "modulate:a", 1.0, 0.25)
	opened_changed.emit(true)


## The hint on screen: its sentence ("" when none shows).
func hint_text() -> String:
	return _page_text.text if _page.visible else ""


## Drops every open or queued hint (a new game or a load starts clean).
func clear_pages() -> void:
	_queue.clear()
	_held.clear()
	_page.visible = false
	_page_id = ""
	_cur = {}
	_sheet.visible = false
	_book.visible = false
	_world_back()


## Puts the open and queued hints aside without marking them read (entering the shed).
func hold_pages() -> void:
	if _page.visible:
		_held.append(_cur)
		_page.visible = false
		_page_id = ""
		_cur = {}
	_held.append_array(_queue)
	_queue.clear()
	opened_changed.emit(is_open())


## Brings the hints put aside back (leaving the shed).
func release_pages() -> void:
	_queue = _held + _queue
	_held.clear()
	if not _page.visible and not _queue.is_empty():
		_next_page()


## Ids of the hint on screen, the queued and the held ones, in order (saved with the game).
func pending_ids() -> Array[String]:
	var out: Array[String] = []
	if _page.visible and _page_id != "":
		out.append(_page_id)
	for p in _held + _queue:
		out.append(str(p["id"]))
	return out


## The hint on screen, or the page read again from the back of the book.
func current_page() -> String:
	if _sheet.visible:
		return _sheet_id
	return _page_id if _page.visible else ""


## Closes the page read again, else the hint on screen (the next queued one follows).
func close_page() -> void:
	if _sheet.visible:
		_sheet.visible = false
		_sheet_id = ""
		opened_changed.emit(is_open())
		return
	if not _page.visible:
		return
	var id := _page_id
	_page.visible = false
	page_closed.emit(id)
	_next_page()


func _build_page() -> void:
	# A full-screen catcher: the tap that clears the hint does not also act on the game.
	_page = Control.new()
	_page.set_anchors_preset(Control.PRESET_FULL_RECT)
	_page.mouse_filter = Control.MOUSE_FILTER_STOP
	_page.gui_input.connect(func(e: InputEvent) -> void:
		if e is InputEventMouseButton and not e.pressed and (e as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT \
				and Time.get_ticks_msec() / 1000.0 - _page_shown_at >= MIN_PAGE_SECONDS:
			close_page())
	add_child(_page)
	_page_sheet = PanelContainer.new()
	_page_sheet.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_page_sheet.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_page_sheet.offset_left = 22
	_page_sheet.offset_right = -22
	_page_sheet.offset_top = -HINT_BOTTOM
	_page_sheet.offset_bottom = -HINT_BOTTOM
	_page_sheet.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(_page_sheet)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page_sheet.add_child(row)
	_page_doodle = _doodle_rect(HINT_DOODLE_SIZE)
	row.add_child(_page_doodle)
	var words := VBoxContainer.new()
	words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	words.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(words)
	_page_text = Paper.ink_label("", 28)
	_page_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_page_text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	words.add_child(_page_text)
	# The key of the dots' marks (0.8), under the hints that name the nutrients by colour.
	_page_marks = NutrientMarks.legend(20)
	_page_marks.visible = false
	words.add_child(_page_marks)
	_page.visible = false


## The full torn-out page, for a page read again from the back of the book (never in play).
func _build_sheet() -> void:
	_sheet = Control.new()
	_sheet.set_anchors_preset(Control.PRESET_FULL_RECT)
	_sheet.mouse_filter = Control.MOUSE_FILTER_STOP
	_sheet.gui_input.connect(func(e: InputEvent) -> void:
		if e is InputEventMouseButton and not e.pressed and (e as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
			close_page())
	add_child(_sheet)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.4)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sheet.add_child(dim)
	var holder := CenterContainer.new()
	holder.set_anchors_preset(Control.PRESET_FULL_RECT)
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sheet.add_child(holder)
	_sheet_paper = PanelContainer.new()
	_sheet_paper.custom_minimum_size = Vector2(620, 0)
	_sheet_paper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(_sheet_paper)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sheet_paper.add_child(box)
	var head := HBoxContainer.new()
	head.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_child(head)
	_sheet_title = Paper.ink_label("", 44, Paper.INK, true)
	_sheet_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_sheet_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	head.add_child(_sheet_title)
	_sheet_doodle = _doodle_rect(PAGE_DOODLE_SIZE)
	head.add_child(_sheet_doodle)
	box.add_child(_rule())
	_sheet_body = Paper.ink_label("", 29)
	_sheet_body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_sheet_body.custom_minimum_size = Vector2(540, 0)
	box.add_child(_sheet_body)
	_sheet_marks = NutrientMarks.legend(24)
	_sheet_marks.visible = false
	box.add_child(_sheet_marks)
	var footer := Paper.ink_label("tap to turn back", 22, Paper.FAINT_INK)
	footer.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	box.add_child(footer)
	_sheet.visible = false


## Opens a page from the back of the book, in full, over the book.
func read_page(id: String) -> void:
	if not Pages.has(id):
		return
	_sheet_id = id
	_sheet_title.text = Pages.title(id)
	_sheet_body.text = Pages.body(id)
	var kind := Pages.doodle(id)
	_sheet_doodle.texture = InkSketch.texture(kind) if InkSketch.has(kind) else null
	_sheet_doodle.visible = _sheet_doodle.texture != null
	_sheet_marks.visible = MARK_PAGES.has(id)
	_pages_torn += 1
	PaperLook.apply(_sheet_paper, "torn_page", 40 + _pages_torn % 5, 34.0)
	_sheet.visible = true
	move_child(_sheet, -1)
	opened_changed.emit(true)


## A small ink doodle (InkSketch), fixed size, never taking a tap.
static func _doodle_rect(px: int, kind: String = "") -> TextureRect:
	var r := TextureRect.new()
	r.custom_minimum_size = Vector2(px, px)
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	r.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	r.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	if kind != "":
		r.texture = InkSketch.texture(kind)
	return r


## A heading with its doodle at the right (the book's tabs, 0.8.2).
static func _heading(text: String, kind: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	var l := Paper.ink_label(text, 32, Paper.INK, true)
	l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(l)
	row.add_child(_doodle_rect(80, kind))
	return row


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


## The button itself (main.gd fades it out with the HUD at the dive's start).
func open_button() -> Control:
	return _open_button


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
	dim.color = Color(0.05, 0.04, 0.02, BOOK_DIM)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_book.add_child(dim)
	_book_dim = dim

	# The leather cover, a little larger than the page.
	var cover := Panel.new()
	cover.add_theme_stylebox_override("panel", Paper.cover_box())
	PaperLook.apply_leather(cover)
	cover.set_anchors_preset(Control.PRESET_FULL_RECT)
	cover.offset_left = 8
	cover.offset_right = -60
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
		edge.offset_right = -86 + i * 3
		edge.offset_top = 58
		edge.offset_bottom = -52 + i * 3
		edge.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_book.add_child(edge)

	_book_page = PanelContainer.new()
	# The book's pages: smooth cream paper.
	PaperLook.apply(_book_page, "book_page", 11, 34.0)
	_book_page.set_anchors_preset(Control.PRESET_FULL_RECT)
	_book_page.offset_left = 40
	# Narrow enough that the ribbons (below) have room to carry their words (0.6.3 review).
	_book_page.offset_right = -92
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
	_tabs["today"] = _build_today_tab()
	_tabs["diary"] = _build_diary_tab()
	_tabs["collection"] = _build_collection_tab()
	_tabs[NOTES_TAB] = _build_notes_tab()
	for k in _tabs:
		var c: Control = _tabs[k]
		c.set_anchors_preset(Control.PRESET_FULL_RECT)
		content.add_child(c)
	# The back of the book (J1): a small "notes" corner at the page's foot, not a ribbon.
	var foot := HBoxContainer.new()
	foot.alignment = BoxContainer.ALIGNMENT_END
	stack.add_child(foot)
	_notes_corner = Paper.ink_button("notes", 22)
	_notes_corner.add_theme_color_override("font_color", Paper.FAINT_INK)
	_notes_corner.pressed.connect(func() -> void: _show_tab(NOTES_TAB))
	foot.add_child(_notes_corner)

	# Cloth ribbon bookmarks sticking out of the right edge of the book, with forked ends.
	var ribbons := {"today": Color(0.42, 0.3, 0.18), "diary": Color(0.62, 0.2, 0.16), "collection": Color(0.52, 0.42, 0.16)}
	var y := 110
	for k in RIBBONS:
		var b := Button.new()
		b.text = k
		b.focus_mode = Control.FOCUS_NONE
		# The calm reading hand, large enough to read on a phone (0.6.3 review: 8 px in Caveat).
		b.add_theme_font_override("font", Paper.hand_font(false))
		b.add_theme_font_size_override("font_size", 24)
		for fc in ["font_color", "font_hover_color", "font_pressed_color"]:
			b.add_theme_color_override(fc, Color(0.98, 0.95, 0.88))
		# The text sits clear of the V cut in the ribbon's end.
		var pad := StyleBoxEmpty.new()
		pad.content_margin_left = 14
		pad.content_margin_right = 22
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
		# All ribbons as wide as the longest word, their page ends in one line at the page's edge
		# (0.8 review: "clearing" reached over the page and the ends were ragged); _layout_ribbons.
		b.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		b.offset_top = y
		b.offset_bottom = y + 110
		b.rotation_degrees = 0
		b.pressed.connect(func() -> void: _show_tab(k))
		_book.add_child(b)
		_tab_buttons[k] = b
		y += 118
	_show_tab("today", false)
	_book.visible = false


# --- Today (J2): at most three lines, no numbers ------------------------------------

func _build_today_tab() -> Control:
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 14)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.add_child(box)
	_today_box = VBoxContainer.new()
	_today_box.add_theme_constant_override("separation", 14)
	box.add_child(_today_box)
	_tree_sketch = Control.new()
	_tree_sketch.custom_minimum_size = Vector2(0, 420)
	_tree_sketch.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_tree_sketch.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_tree_sketch.draw.connect(func() -> void:
		if state != null:
			ShedMenu.draw_graph_sketch(_tree_sketch, state.sim.graph))
	box.add_child(_tree_sketch)
	return scroll


## The Today page: each line with the coloured mark of its nutrient where it names one.
func _refresh_today() -> void:
	for c in _today_box.get_children():
		c.queue_free()
	for line in today_lines(state):
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		var kind := int(line["kind"])
		if kind >= 0:
			var mark := NutrientMarks.icon_rect(kind, 34)
			mark.size_flags_vertical = Control.SIZE_SHRINK_CENTER
			row.add_child(mark)
		var l := Paper.ink_label(str(line["text"]), 28, Paper.RED_INK if line["topic"] == "wish" else Paper.INK)
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(l)
		_today_box.add_child(row)
	_tree_sketch.queue_redraw()


## What the tree lacks, in words (J2).
const LACK_WORDS: Array[String] = ["Thirsty.", "Hungry for nitrogen.", "Hungry for phosphorus.", "Hungry for potassium."]
const NOTHING_MISSING := "Nothing missing."


## The Today page's lines (J2): at most three, no numbers. Each {"topic": "wish" | "lacks" |
## "tonight", "text", "kind" (the nutrient's mark, -1 for none)}.
static func today_lines(s: GameState) -> Array:
	var out: Array = []
	var d := s.diary
	# 1. The wish.
	var wish_kind := -1
	var wish_open := false
	if d.wish_patch >= 0 and d.wish_patch < s.ground.patches.size():
		var p: Dictionary = s.ground.patches[d.wish_patch]
		wish_kind = int(p["kind"])
		var plant := Diary.PLANTS[wish_kind]
		if d.wish_reached:
			out.append({"topic": "wish", "text": "Wish found: the %s." % plant, "kind": wish_kind})
		else:
			wish_open = true
			var kept := d.wish_since >= 0 and d.wish_since < s.day_number()
			out.append({"topic": "wish", "text": Diary.wish_entry(wish_kind, p["center"], kept, Diary.is_far(s.ground, d.wish_patch)), "kind": wish_kind})
	elif d.wish != "":
		# An old save's day wish (before 0.8.2.5).
		out.append({"topic": "wish", "text": "Wish: " + wish_line(d.wish), "kind": -1})
	# 2. What the tree lacks most.
	var need := main_need(s)
	out.append({"topic": "lacks", "text": LACK_WORDS[need] if need >= 0 else NOTHING_MISSING, "kind": need})
	# 3. Where tonight's root finds it (only while tonight's root is still to come).
	if s.finished or s.is_seed() or not s.roots.can_start_run():
		return out
	var look := need if need >= 0 else (wish_kind if wish_open else -1)
	if look < 0:
		return out
	var text := ""
	if wish_open and look == wish_kind:
		text = "Tonight: far away, toward the wish." if Diary.is_far(s.ground, d.wish_patch) else "Tonight: toward the wish."
	elif not s.reach_for(look).is_empty():
		text = "Tonight: near the %s." % Diary.PLANTS[look]
	elif wish_open:
		text = "Tonight: far away, toward the wish."
	else:
		text = "Tonight: none within reach."
	out.append({"topic": "tonight", "text": text, "kind": look})
	return out


## The nutrient the tree lacks most (the leaves show it, Care), or -1: none, or too young or grown
## to read.
static func main_need(s: GameState) -> int:
	if s.finished or s.is_seed() or s.day_number() <= 1:
		return -1
	var shown := s.sim.care_shown()
	var best := -1
	for k in range(4):
		if shown[k] >= Care.SHOW_MIN and (best < 0 or shown[k] > shown[best]):
			best = k
	return best


## Opens the book at Today (the care page's place before 0.8.2.6).
func open_care() -> void:
	open_diary()
	_show_tab("today", false)


## Which ribbon's page is open ("today" first; "notes" for the back pages).
func current_tab() -> String:
	return _current_tab


# --- Diary (J3): one line on the days something happened ---------------------------

func _build_diary_tab() -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	_diary_text = RichTextLabel.new()
	_diary_text.bbcode_enabled = true
	# The day pages' doodles are drawn smaller than they were made (InkSketch.texture's mipmaps).
	_diary_text.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
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
	var empty := StyleBoxEmpty.new()
	_note.add_theme_stylebox_override("normal", empty)
	_note.add_theme_stylebox_override("focus", empty)
	note_row.add_child(_note)
	var add := Paper.ink_button("write", 24)
	add.pressed.connect(_add_note)
	note_row.add_child(add)
	_note.text_submitted.connect(func(_t: String) -> void: _add_note())
	return box


## The days that show in the Diary, newest first: only those with a line (Diary.page) or a note
## of the player's own (0.8.2.7, Simon: "days on which nothing was written should not show at
## all"; a quiet day used to show its date alone).
static func shown_days(diary: Diary) -> Array[int]:
	var out: Array[int] = []
	for day in diary.days():
		if not diary.page(day).is_empty() or not diary.notes(day).is_empty():
			out.append(day)
	out.reverse()
	return out


## The days, newest first: the date, then the day's one line with its doodle when something
## happened (Diary.page), then the player's own notes in red. Quiet days are left out.
func _refresh_diary_text() -> void:
	_diary_text.clear()
	var days := shown_days(state.diary)
	if days.is_empty():
		_diary_text.append_text("[color=#%s]Nothing to write down yet.[/color]
" % Paper.FAINT_INK.to_html(false))
	for day in days:
		var lines := state.diary.page(day)
		_diary_text.append_text("[b]Day %d[/b]" % day)
		if not lines.is_empty():
			_diary_text.append_text("  ")
			_diary_text.add_image(InkSketch.texture(state.diary.doodle(day)), DAY_DOODLE_SIZE, DAY_DOODLE_SIZE, Color.WHITE, INLINE_ALIGNMENT_CENTER)
		_diary_text.append_text("\n")
		for e in lines:
			_diary_text.append_text("%s\n" % str(e["text"]).replace("[", "[lb]"))
		for e in state.diary.notes(day):
			_diary_text.append_text("[i][color=#8a2c1e]%s[/color][/i]\n" % str(e["text"]).replace("[", "[lb]"))
		_diary_text.append_text("\n")
	_diary_text.scroll_to_line(0)


# --- Collection (J1) ----------------------------------------------------------------

func _build_collection_tab() -> Control:
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_collection_box = VBoxContainer.new()
	_collection_box.add_theme_constant_override("separation", 12)
	_collection_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_collection_box)
	return scroll


## The finds for the Collection page: finds_source's when set (the shed's drawers), else the
## finds this tree's diary records, each kind once.
func collection_finds() -> Array:
	if finds_source.is_valid():
		return finds_source.call()
	var out: Array = []
	var seen := {}
	for e in state.diary.entries:
		var kind := Diary.drawing_of(e)
		if Diary.topic_of(e) == "find" and Underground.FIND_TEXTS.has(kind) and not seen.has(kind):
			seen[kind] = true
			out.append({"kind": kind, "text": str(e["text"]), "day": int(e["day"])})
	return out


## The visitors this tree has had, each once: [{"kind", "text"}].
static func collection_visitors(s: GameState) -> Array:
	var out: Array = []
	for id in Visitors.LINES:
		if Visitors.has_come(s, id):
			out.append({"kind": id, "text": str(Visitors.LINES[id])})
	if s.seen_pages.has("visitor_hedgehog"):
		out.append({"kind": "hedgehog", "text": GameState.HEDGEHOG_LINE})
	if s.seen_pages.has("visitor_wren"):
		out.append({"kind": "wren", "text": GameState.WREN_LINE})
	return out


func _refresh_collection() -> void:
	for c in _collection_box.get_children():
		c.queue_free()
	# Under my crown: the shade plants and mushrooms (Clearing.found), with their painted drawings.
	_collection_box.add_child(_heading("Under my crown", "fern"))
	var found: Dictionary = state.clearing.found
	var atlas := load(Understory.ATLAS) as Texture2D
	for k in Clearing.KINDS:
		if not found.has(k):
			continue
		var pic := TextureRect.new()
		var at := AtlasTexture.new()
		at.atlas = atlas
		var cell: int = Understory.CELL[k]
		var half := atlas.get_size() * 0.5
		at.region = Rect2(Vector2(cell % 2, cell / 2) * half, half)
		pic.texture = at
		pic.custom_minimum_size = Vector2(80, 80)
		pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		pic.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		_collection_box.add_child(_collection_row(pic, Clearing.NAMES[k], Clearing.NOTES[k]))
	if found.is_empty():
		_collection_box.add_child(_faint("Nothing yet: the shade is still small."))
	# The finds, and the drawer they lie in.
	_collection_box.add_child(_heading("Finds", "coin"))
	var finds := collection_finds()
	for f in finds:
		var kind := str(f.get("kind", ""))
		_collection_box.add_child(_collection_row(_doodle_rect(64, kind if InkSketch.has(kind) else ""), str(f.get("text", "")), str(f.get("where", "in the workbench drawer"))))
	if finds.is_empty():
		_collection_box.add_child(_faint("Nothing yet: the roots find things far out."))
	# The visitors, each once.
	_collection_box.add_child(_heading("Visitors", "butterflies"))
	var visitors := collection_visitors(state)
	for v in visitors:
		_collection_box.add_child(_collection_row(_doodle_rect(64, str(v["kind"])), str(v["text"]), ""))
	if visitors.is_empty():
		_collection_box.add_child(_faint("Nobody yet."))
	# The trees grown, each species once.
	_collection_box.add_child(_heading("Trees grown", "grown_tree"))
	var species := {}
	for t in state.grove:
		species[str(t.get("species", ""))] = true
	for sid in Species.ORDER:
		if species.has(sid):
			var kind := InkSketch.species_kind(sid)
			_collection_box.add_child(_collection_row(_doodle_rect(64, kind), Species.from_id(sid).display_name, ""))
	if species.is_empty():
		_collection_box.add_child(_faint("None yet: this is the first."))


func _collection_row(pic: Control, words_text: String, note: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	row.add_child(pic)
	var words := VBoxContainer.new()
	words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var n := Paper.ink_label(words_text, 25)
	n.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	words.add_child(n)
	if note != "":
		var l := Paper.ink_label(note, 22, Paper.FAINT_INK)
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		words.add_child(l)
	row.add_child(words)
	return row


static func _faint(text: String) -> Label:
	var l := Paper.ink_label(text, 23, Paper.FAINT_INK)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return l


# --- the back of the book: the explanation pages (J1, J4) ----------------------------

func _build_notes_tab() -> Control:
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 12)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(box)
	box.add_child(_heading("Notes", "feather"))
	_pages_empty = _faint("Nothing to read again yet.")
	box.add_child(_pages_empty)
	_pages_list = VBoxContainer.new()
	_pages_list.add_theme_constant_override("separation", 10)
	box.add_child(_pages_list)
	return scroll


func _refresh_notes() -> void:
	for c in _pages_list.get_children():
		c.queue_free()
	var any := false
	for id in Pages.ids():
		if state.seen_pages.has(id):
			var b := Paper.ink_button(Pages.title(id), 26)
			b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
			b.pressed.connect(func() -> void: read_page(id))
			_pages_list.add_child(b)
			any = true
	_pages_empty.visible = not any


func _show_tab(name: String, animate: bool = true) -> void:
	if not _tabs.has(name):
		name = "today"
	_current_tab = name
	for k in _tabs:
		(_tabs[k] as Control).visible = k == name
	_notes_corner.visible = name != NOTES_TAB
	_layout_ribbons(name)
	if animate:
		_turn_page()


## The ribbons: all as wide as the longest word needs, starting in one line at the page's right
## edge (the page gives way for them), the chosen one sticking out a little further.
const RIBBON_EDGE := 18.0
const RIBBON_CHOSEN := 10.0


func _layout_ribbons(chosen: String) -> void:
	var w := 0.0
	for k in _tab_buttons:
		var b := _tab_buttons[k] as Button
		b.custom_minimum_size = Vector2.ZERO
		w = maxf(w, b.get_combined_minimum_size().x)
	w = ceilf(w)
	for k in _tab_buttons:
		var b := _tab_buttons[k] as Button
		var out := RIBBON_CHOSEN if k == chosen else 0.0
		b.offset_right = -RIBBON_EDGE + out
		b.offset_left = -RIBBON_EDGE - w
		b.custom_minimum_size = Vector2(w + out, 0)
	_book_page.offset_right = -RIBBON_EDGE - w


## A page turn: the page squeezes toward the spine and opens again.
func _turn_page() -> void:
	_book_page.pivot_offset = Vector2(0, _book_page.size.y * 0.5)
	var tw := create_tween()
	tw.tween_property(_book_page, "scale:x", 0.08, 0.12).set_ease(Tween.EASE_IN)
	tw.tween_property(_book_page, "scale:x", 1.0, 0.18).set_ease(Tween.EASE_OUT)


func open_diary() -> void:
	_refresh_diary()
	# The ribbons fit their words again (clearer print may have changed their size).
	_layout_ribbons(_current_tab)
	_book.visible = true
	_book.modulate.a = 0.0
	_book_page.scale = Vector2(0.9, 0.96)
	_book_page.pivot_offset = Vector2(0, _book_page.size.y * 0.5)
	_book_dim.color.a = BOOK_DIM
	var tw := create_tween().set_parallel(true)
	tw.tween_property(_book, "modulate:a", 1.0, 0.2)
	tw.tween_property(_book_page, "scale", Vector2.ONE, 0.3).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	# 0.8.2.4 (phone: about 15 fps the whole time the book was open, the world drawn under it):
	# once open, the dim around the book closes to solid and the world under it stops drawing.
	tw.chain().tween_property(_book_dim, "color:a", 1.0, 0.25)
	tw.chain().tween_callback(func() -> void:
		if _book.visible:
			get_viewport().disable_3d = true)
	opened_changed.emit(true)


func close_diary() -> void:
	_book.visible = false
	_sheet.visible = false
	_sheet_id = ""
	_world_back()
	opened_changed.emit(is_open())


## The world draws again under the closing book.
func _world_back() -> void:
	if is_inside_tree():
		get_viewport().disable_3d = false
	_book_dim.color.a = BOOK_DIM


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
	_refresh_today()
	_refresh_diary_text()
	_refresh_collection()
	_refresh_notes()


## The day's wish after "Wish: ", mid-sentence (0.7 review: "A wish: Today, ..." had a stray
## capital).
static func wish_line(wish: String) -> String:
	return wish.substr(0, 1).to_lower() + wish.substr(1) if wish != "" else ""


## The pinboard's switches (0.8.2.6: the book has no settings page; the shed's pinboard does).
func set_setting(key: String, on: bool) -> void:
	settings[key] = on
