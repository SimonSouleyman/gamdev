class_name ShedMenu
extends CanvasLayer
## The 2D part of the garden shed: the small handwritten labels beside the things on the
## workbench (0.6: the things are the menu, no menu note any more), the options pinned to the
## board, the photo album, the seed bag, the tree's own page ("while you were away") and the
## loading page. All paper and ink (Paper), never plain windows.

signal continue_pressed
signal journal_pressed
signal new_tree_pressed
signal setting_changed(key: String, value: bool)
## A species was chosen from the seed bag: plant it as the next tree.
signal plant_pressed(species_id: String)
## A reset from the pinboard, confirmed (Simon, 0.6.x): "tree" plants a fresh seed of the same
## kind (album, grove, bonsai stay), "all" starts over like a fresh install.
signal reset_pressed(kind: String)

## The words on the labels of the things in the shed (Shed.ITEMS). (0.8.1, item 28: the flower
## pot left the bench; the tree's page is the journal's first page.)
const TAG_TEXTS := {"journal": "journal", "album": "photo album", "seeds": "seed bag",
	"gloves": "go outside", "options": "options", "bonsai": "my bonsai"}
## The switches on the options pinboard, in their order.
const OPTION_NAMES := {"sound": "sound", "no_ui": "no UI (pure scenery)", "battery_saver": "battery saver",
	"notifications": "a note each day", "vibration": "vibration", "clearer_print": "clearer print",
	"any_species": "every seed and the bonsai now (a new seed takes this tree's place)"}

var settings: Dictionary = {}
var _tags_layer: Control
var _tags: Dictionary = {}  # item -> PanelContainer
var _tree_name: String = "linden"
var _options: Control
var _album: Control
var _album_pages: Array[Control] = []
var _album_cards: Array[Control] = []
var _wallpaper_button: Button
var _album_left: TextureRect
var _album_right: TextureRect
var _album_cap_l: Label
var _album_cap_r: Label
var _album_title: Label
## The spread on show: an index into _spreads.
var _album_index: int = 0
var _photos: Array[String] = []
## The album's spreads in order: {"photo": first photo index} for two photos, {"flip": tree index}
## for a finished tree's double page with its month as a flip-book (TimeLapse).
var _spreads: Array = []
## The photos grouped by tree (TimeLapse.trees).
var _trees: Array = []
var _album_holders: Array[Control] = []
var _flip_box: Control
var _flip: FlipBook
var _flip_title: Label
var _flip_status: Label
var _video_button: Button
## The tree whose flip-book is open; -1 when two photos are on show.
var _flip_tree: int = -1
## The current tree is finished (main sets it): its flip-book page follows its last photo.
var tree_finished: bool = false
var _toggles: Dictionary = {}
## The switches' notes on the pinboard, in order (laid out over the board's height, _layout_notes).
var _notes: Array[Control] = []
var _loading: Control
var _seeds: Control
var _seeds_box: VBoxContainer
var _tree_page: Control
var _tree_box: VBoxContainer
var _sketch_graph: PlantGraph
## 0.8: "copy my tree" and "load a copy" on the pinboard (main wires them to the game).
var backup_notes: BackupNotes
## 0.8: the "send" words on the album's two cards and on the flip-book page, and the album's note.
var _send_buttons: Array[Button] = []
var _flip_send: Button
var _album_note: Label
## Tools switch this off: after sending on a PC the game's folder opens.
static var open_folders := true


func _ready() -> void:
	layer = 18
	_build_tags()
	_build_options()
	_build_album()
	_build_seeds()
	_build_tree_page()
	_build_loading()


# --- the labels beside the things on the workbench ------------------------------------

## A small torn strip of paper with a word in ink for each thing. They show on the first visits
## (until the thing was used once) and when the pointer rests on it, so a new player
## understands that the things on the bench are the menu.
func _build_tags() -> void:
	_tags_layer = Control.new()
	_tags_layer.set_anchors_preset(Control.PRESET_FULL_RECT)
	_tags_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_tags_layer)
	var i := 0
	for item in TAG_TEXTS:
		var tag := PanelContainer.new()
		tag.add_theme_stylebox_override("panel", Paper.paper_box(120, 44, 150 + i, "all", 9.0))
		tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var l := Paper.ink_label(TAG_TEXTS[item], 24)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		tag.add_child(l)
		tag.rotation_degrees = [-3.0, 2.0, -1.5, 2.5, -2.0, 1.5, -2.5][i % 7]
		tag.visible = false
		_tags_layer.add_child(tag)
		_tags[item] = tag
		i += 1


## Puts the labels by the things: `at` maps an item to its screen point, `below` to whether the
## label hangs under that point (else it sits on it), `shown` to whether it shows now.
## Labels fade in and out softly.
func place_tags(at: Dictionary, below: Dictionary, shown: Dictionary) -> void:
	for item in _tags:
		var tag: PanelContainer = _tags[item]
		var on: bool = bool(shown.get(item, false)) and at.has(item) and _tags_layer.visible
		var a := move_toward(tag.modulate.a, 1.0 if on else 0.0, 0.12)
		tag.modulate.a = a
		tag.visible = a > 0.01
		if at.has(item):
			tag.size = tag.get_combined_minimum_size()
			tag.pivot_offset = tag.size * 0.5
			var hang := tag.size.y * -0.1 if bool(below.get(item, false)) else tag.size.y
			var pos := (at[item] as Vector2) - Vector2(tag.size.x * 0.5, hang)
			# Always whole on the screen.
			var room := _tags_layer.size
			tag.position = pos.clamp(Vector2(8, 8), Vector2(maxf(room.x - tag.size.x - 8.0, 8.0), maxf(room.y - tag.size.y - 8.0, 8.0)))


func show_menu(on: bool) -> void:
	_tags_layer.visible = on
	if not on:
		_flip.stop()
		_options.visible = false
		_album.visible = false
		_seeds.visible = false


## The tree's name ("silver birch"), for its pages.
func set_tree_name(tree_name: String) -> void:
	_tree_name = tree_name


## Esc on the options board, in the album, at the seed bag or on the tree's page closes it.
func close_boards() -> void:
	_flip.stop()
	backup_notes.reset()
	_options.visible = false
	_album.visible = false
	_seeds.visible = false
	_tree_page.visible = false


func is_busy() -> bool:
	return _options.visible or _album.visible or _seeds.visible or _tree_page.visible


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
	board.offset_top = BOARD_TOP
	board.offset_bottom = -230
	_options.add_child(board)
	var names := OPTION_NAMES
	var i := 0
	for key in names:
		var note := PanelContainer.new()
		note.add_theme_stylebox_override("panel", Paper.paper_box(200, 90, 80 + i, "all", 18.0))
		# Two columns; the long test switch gets the last row to itself.
		var last := i == names.size() - 1
		note.position = Vector2(60 + (i % 2) * 300, 36 + (i / 2) * 128)
		note.custom_minimum_size = Vector2(560 if last else 260, 120)
		note.rotation_degrees = [-3.0, 2.0, 1.5, -2.0, 2.5, -1.0, 1.0][i]
		board.add_child(note)
		_notes.append(note)
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
	# The board reaches a little lower, so the resets are pinned on the cork, clear of its frame
	# and of "close" (0.6.3 review: in clearer print they ran over the frame).
	board.offset_bottom = BOARD_BOTTOM
	# "close", as the journal and the album say for leaving a page (0.8 review: one word).
	var back := Paper.scrap_button("close", 30, 90)
	back.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	back.grow_horizontal = Control.GROW_DIRECTION_BOTH
	back.offset_left = -70
	back.offset_right = 70
	back.offset_top = BACK_BOTTOM - Paper.INK_TAP
	back.offset_bottom = BACK_BOTTOM
	back.pressed.connect(func() -> void:
		backup_notes.reset()
		_options.visible = false)
	_options.add_child(back)
	# Two resets pinned below the notes; each asks once more before it acts. The same calm hand
	# as the notes above them.
	var resets := HBoxContainer.new()
	resets.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	resets.grow_horizontal = Control.GROW_DIRECTION_BOTH
	resets.offset_left = -320
	resets.offset_right = 320
	resets.offset_top = RESETS_BOTTOM - Paper.INK_TAP
	resets.offset_bottom = RESETS_BOTTOM
	resets.alignment = BoxContainer.ALIGNMENT_CENTER
	resets.add_theme_constant_override("separation", 30)
	_options.add_child(resets)
	for kind in ["tree", "all"]:
		var b := Paper.scrap_button("plant a new tree" if kind == "tree" else "start over", 27, 94 + resets.get_child_count())
		b.rotation_degrees = -1.2 if kind == "tree" else 1.0
		var label: String = b.text
		b.pressed.connect(func() -> void:
			if b.has_meta("armed"):
				b.remove_meta("armed")
				b.text = label
				_options.visible = false
				reset_pressed.emit(kind)
			else:
				b.set_meta("armed", true)
				b.text = "sure? tap again" if kind == "tree" else "erase all? tap again"
				get_tree().create_timer(4.0).timeout.connect(func() -> void:
					if is_instance_valid(b) and b.has_meta("armed"):
						b.remove_meta("armed")
						b.text = label))
		resets.add_child(b)
	# 0.8: the save backup, pinned between the switches and the resets (its note slip above it;
	# the switches' rows moved closer to make room). On the phone the live picture's scrap sits
	# between the backup and the resets, so the backup moves up by one row.
	backup_notes = BackupNotes.new()
	backup_notes.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	backup_notes.grow_horizontal = Control.GROW_DIRECTION_BOTH
	backup_notes.grow_vertical = Control.GROW_DIRECTION_BEGIN
	backup_notes.offset_left = -320
	backup_notes.offset_right = 320
	var backup_bottom := LIVE_BOTTOM - Paper.INK_TAP - ROW_GAP if show_live_note else RESETS_BOTTOM - Paper.INK_TAP - ROW_GAP
	backup_notes.offset_top = backup_bottom
	backup_notes.offset_bottom = backup_bottom
	_options.add_child(backup_notes)
	_add_live_note(board)
	_options.visible = false


## The pinboard's layout, in reference pixels from the screen's top (BOARD_TOP) and bottom (the
## rest). From the bottom up: "close" below the board, then on the cork the resets, the live
## picture's scrap (phone only), the backup notes with their slip growing upward; the switches
## fill the board from the top. Every tap target is at least Paper.INK_TAP tall; checked at
## 450x800 and 720x1280, normal and clearer print, with the phone path (tools/backup_shot.gd --phone).
const BOARD_TOP := 60
const BOARD_BOTTOM := -140
const BACK_BOTTOM := -40
const ROW_GAP := 12.0
const RESETS_BOTTOM := BOARD_BOTTOM - 15.0
const LIVE_BOTTOM := RESETS_BOTTOM - Paper.INK_TAP - ROW_GAP


## The live picture (specs/0.8.md section 6): a scrap pinned under the notes that says, in one line
## when tapped, how to put the tree on the phone's home screen or screen saver. Phone only (a PC has
## no live wallpaper); tools may show it.
const LIVE_NOTE := "the tree on my phone"
const LIVE_HOW := "Hold the home screen: Wallpapers, Tree. While charging: Settings, Display, Screen saver."
static var show_live_note: bool = Phone.is_available()
var live_note: Button


func _add_live_note(board: Control) -> void:
	live_note = Paper.scrap_button(LIVE_NOTE, 24, 97)
	live_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	live_note.visible = show_live_note
	_place_live_note(false)
	live_note.pressed.connect(func() -> void: _place_live_note(live_note.text == LIVE_NOTE))
	board.add_child(live_note)


## Closed: a small scrap in the row above the resets. Open: its line on a larger scrap in the
## free cork above the backup notes, in the notes' own hand size, while the backup notes stay
## where they are (0.8 review: the open note was small and took the backup's place); only the
## backup's note slip steps aside until it is closed. It never covers a switch. The live note is
## the board's child and the offsets are the screen's, so the board's own bottom offset is taken off.
func _place_live_note(open: bool) -> void:
	live_note.text = LIVE_HOW if open else LIVE_NOTE
	var size := 27 if open else 24
	live_note.add_theme_font_size_override("font_size", size)
	if live_note.has_meta("paper_sizes"):
		live_note.set_meta("paper_sizes", {"font_size": size})
	Paper.print_control(live_note)
	live_note.set_anchors_preset(Control.PRESET_CENTER_BOTTOM)
	live_note.grow_horizontal = Control.GROW_DIRECTION_BOTH
	live_note.grow_vertical = Control.GROW_DIRECTION_BEGIN
	live_note.offset_left = -290 if open else -180
	live_note.offset_right = 290 if open else 180
	# Open: its bottom a row above the backup notes' row (which ends a row above the live row).
	var bottom := LIVE_BOTTOM - (2.0 * Paper.INK_TAP + 2.0 * ROW_GAP if open else 0.0)
	live_note.offset_top = bottom - Paper.INK_TAP * (2.0 if open else 1.0) - BOARD_BOTTOM
	live_note.offset_bottom = bottom - BOARD_BOTTOM
	live_note.rotation_degrees = -0.6 if open else 0.8
	backup_notes.hide_slip(open)


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
	for k in _toggles:
		(_toggles[k] as CheckBox).set_pressed_no_signal(bool(settings.get(k, false)))
	backup_notes.reset()
	_place_live_note(false)
	_layout_notes()
	_options.visible = true


## The switches' rows spread over the cork down to the backup notes, keeping room above those
## for their note slip or the live picture's open note (0.8.1: on the phone's tall screen the
## rows sat at the top over a large empty board). The rows are never closer than they were.
func _layout_notes() -> void:
	var room := get_viewport().get_visible_rect().size if get_viewport() != null else Vector2(720, 1280)
	var backup_bottom := LIVE_BOTTOM - Paper.INK_TAP - ROW_GAP if show_live_note else RESETS_BOTTOM - Paper.INK_TAP - ROW_GAP
	# Board-local: the backup notes' row top, less two rows for the slip or the open live note.
	var free_bottom := room.y + backup_bottom - Paper.INK_TAP - 2.0 * (Paper.INK_TAP + ROW_GAP) - BOARD_TOP
	var rows := (_notes.size() + 1) / 2
	var last_h := 0.0
	for n in _notes:
		last_h = maxf(last_h, n.get_combined_minimum_size().y)
	var pitch := clampf((free_bottom - NOTES_TOP - last_h) / maxf(rows - 1, 1), ROW_PITCH, ROW_PITCH * 1.8)
	for i in range(_notes.size()):
		_notes[i].position.y = NOTES_TOP + (i / 2) * pitch


## The switches' first row (board-local) and their least row pitch.
const NOTES_TOP := 36.0
const ROW_PITCH := 128.0


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
	# Real bookbinding: worn cloth over boards, cream-brown pages that have been handled
	# (Simon, play test 4: everything like real paper and a real book).
	var leather := PaperLook.apply_leather(cover)
	leather.set_shader_parameter("leather_color", Color(0.2, 0.3, 0.19))
	cover.set_anchors_preset(Control.PRESET_FULL_RECT)
	cover.offset_left = 14
	cover.offset_right = -14
	cover.offset_top = 40
	cover.offset_bottom = -40
	_album.add_child(cover)
	var page := PanelContainer.new()
	PaperLook.apply(page, "book_page", 95, 30.0, {"paper_color": Color(0.8, 0.72, 0.6), "crumple": 0.22, "foxing": 0.6, "mottle": 0.6, "edge_age": 0.75})
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
	_album_title = Paper.ink_label("My trees", 40, Paper.INK, true)
	_album_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(_album_title)
	var close := Paper.ink_button("close", 26)
	close.pressed.connect(func() -> void:
		_flip.stop()
		_album.visible = false)
	head.add_child(close)
	for side in [0, 1]:
		var holder := CenterContainer.new()
		holder.size_flags_vertical = Control.SIZE_EXPAND_FILL
		box.add_child(holder)
		_album_holders.append(holder)
		var polaroid := PanelContainer.new()
		# Slightly yellowed photo card, a little bent; glued in askew (see _show_spread).
		PaperLook.apply(polaroid, "strip", 120 + side, 14.0, {"torn": Vector4.ZERO, "crumple": 0.12, "paper_color": Color(0.95, 0.94, 0.9), "foxing": 0.15, "edge_age": 0.35, "curl": 5.0})
		# Containers straighten their children, so the card sits in a plain Control that only
		# keeps its size; the card itself can then be turned freely.
		var slot := Control.new()
		slot.custom_minimum_size = Vector2(326, 360)
		slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
		holder.add_child(slot)
		slot.add_child(polaroid)
		polaroid.position = Vector2.ZERO
		_album_cards.append(polaroid)
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
		# Two strips of old tape over the corners, drawn over the photo.
		for k in [0, 1]:
			var tape := Panel.new()
			tape.custom_minimum_size = Vector2(96, 30)
			tape.size = Vector2(96, 30)
			tape.mouse_filter = Control.MOUSE_FILTER_IGNORE
			PaperLook.apply(tape, "tape", 140 + side * 2 + k, 0.0)
			tape.position = Vector2(-22.0, -16.0) if k == 0 else Vector2(246.0, -24.0)
			tape.rotation_degrees = -38.0 if k == 0 else 36.0
			# On the photo (a TextureRect does not lay out its children).
			tex.add_child(tape)
		# 0.8: "send", written on the page beside the card's lower corner.
		var send := Paper.ink_button("send", 24)
		send.position = Vector2(344, 262)
		send.rotation_degrees = -3.0
		send.pressed.connect(func() -> void: send_photo(_shown_photo_at(side)))
		slot.add_child(send)
		_send_buttons.append(send)
		if side == 0:
			_album_left = tex
			_album_cap_l = cap
		else:
			_album_right = tex
			_album_cap_r = cap
	_build_flip_page(box)
	var nav := HBoxContainer.new()
	nav.alignment = BoxContainer.ALIGNMENT_CENTER
	nav.add_theme_constant_override("separation", 36)
	box.add_child(nav)
	var prev := Paper.ink_button("< earlier", 24)
	prev.pressed.connect(func() -> void: _turn(-1))
	nav.add_child(prev)
	# The month so far as a flip-book, for the tree on this spread (the current one too).
	var flip := Paper.ink_button("flip through", 24)
	flip.pressed.connect(_flip_through)
	nav.add_child(flip)
	var next := Paper.ink_button("later >", 24)
	next.pressed.connect(func() -> void: _turn(1))
	nav.add_child(next)
	# On the phone: the upper photo of the spread becomes the home-screen wallpaper.
	_wallpaper_button = Paper.ink_button("as wallpaper", 24)
	_wallpaper_button.visible = Phone.is_available()
	_wallpaper_button.pressed.connect(func() -> void:
		var shown := _shown_photo()
		if shown >= 0 and Phone.set_wallpaper(_photos[shown]):
			_album_cap_l.text = "My wallpaper now.")
	nav.add_child(_wallpaper_button)
	_album_note = Paper.ink_label("", 22, Paper.FAINT_INK)
	_album_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_album_note.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_album_note)
	_album.visible = false


func open_album() -> void:
	_photos = Photos.list()
	_build_spreads()
	# The newest spread (a finished tree's flip-book comes after its last photos).
	_album_index = _spreads.size() - 1
	_album_note.text = ""
	_show_spread()
	_album.visible = true


## Two photos per spread, and after the last photo of each finished tree its flip-book page.
func _build_spreads() -> void:
	_trees = TimeLapse.trees(_photos)
	var ends := {}  # last photo index of a finished tree -> tree index
	var seen := 0
	for ti in range(_trees.size()):
		seen += (_trees[ti]["photos"] as Array).size()
		if ti < _trees.size() - 1 or tree_finished:
			ends[seen - 1] = ti
	_spreads = []
	var i := 0
	while i < maxi(_photos.size(), 1):
		_spreads.append({"photo": i})
		for k in [i, i + 1]:
			if ends.has(k):
				_spreads.append({"flip": ends[k]})
		i += 2


func _turn(step: int) -> void:
	var next := _album_index + step
	if next < 0 or next >= _spreads.size():
		return
	_album_index = next
	_album_note.text = ""
	_show_spread()


## The first photo on the spread on show; -1 on a flip-book page or in an empty album.
func _shown_photo() -> int:
	if _flip_tree >= 0 or _spreads.is_empty():
		return -1
	var i: int = _spreads[_album_index].get("photo", -1)
	return i if i < _photos.size() else -1


func _show_spread() -> void:
	var spread: Dictionary = _spreads[_album_index] if _album_index < _spreads.size() else {"photo": 0}
	if spread.has("flip"):
		_show_flip(int(spread["flip"]))
		return
	_flip_tree = -1
	_flip.stop()
	_flip_box.visible = false
	for h in _album_holders:
		h.visible = true
	for side in [0, 1]:
		var i: int = int(spread["photo"]) + int(side)
		var tex: TextureRect = _album_left if side == 0 else _album_right
		var cap: Label = _album_cap_l if side == 0 else _album_cap_r
		if i < _photos.size():
			tex.texture = Photos.load_texture(_photos[i])
			# Each photo was glued in by hand, never quite straight.
			var r := RandomNumberGenerator.new()
			r.seed = hash([_photos[i]])
			var card: Control = _album_cards[side]
			card.pivot_offset = card.size * 0.5
			card.rotation_degrees = r.randf_range(-6.0, 6.0)
			cap.text = Photos.caption(_photos[i])
			tex.get_parent().get_parent().visible = true
			_send_buttons[side].visible = can_send()
		else:
			tex.texture = null
			cap.text = "(no photo yet: every morning takes one, and the camera scrap outside takes more)" if _photos.is_empty() and side == 0 else ""
			tex.get_parent().get_parent().visible = side == 0 and _photos.is_empty()
			_send_buttons[side].visible = false


# --- the flip-book (month time-lapse) ------------------------------------------------

## A finished tree's double page: its mornings as a flip-book on one big photo card, the tree's
## name above, "save as video" below.
func _build_flip_page(box: VBoxContainer) -> void:
	_flip_box = VBoxContainer.new()
	_flip_box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_flip_box.add_theme_constant_override("separation", 10)
	box.add_child(_flip_box)
	_flip_title = Paper.ink_label("", 32, Paper.INK, true)
	_flip_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_flip_box.add_child(_flip_title)
	var holder := CenterContainer.new()
	holder.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_flip_box.add_child(holder)
	var card := PanelContainer.new()
	PaperLook.apply(card, "strip", 126, 14.0, {"torn": Vector4.ZERO, "crumple": 0.1, "paper_color": Color(0.95, 0.94, 0.9), "foxing": 0.15, "edge_age": 0.35, "curl": 3.0})
	holder.add_child(card)
	_flip = FlipBook.new()
	_flip.custom_minimum_size = Vector2(430, 700)
	card.add_child(_flip)
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 30)
	_flip_box.add_child(row)
	_video_button = Paper.ink_button("save as video", 24)
	_video_button.pressed.connect(func() -> void:
		if _flip_tree >= 0:
			save_video(_flip_tree))
	row.add_child(_video_button)
	# 0.8: the month's film through the share sheet.
	_flip_send = Paper.ink_button("send", 24)
	_flip_send.pressed.connect(func() -> void:
		if _flip_tree >= 0:
			send_video(_flip_tree))
	row.add_child(_flip_send)
	_flip_status = Paper.ink_label("", 22, Paper.FAINT_INK)
	_flip_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_flip_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_flip_box.add_child(_flip_status)
	_flip_box.visible = false


func _show_flip(tree_index: int) -> void:
	if tree_index < 0 or tree_index >= _trees.size():
		return
	_flip_tree = tree_index
	for h in _album_holders:
		h.visible = false
	_flip_box.visible = true
	var tree: Dictionary = _trees[tree_index]
	var pages := TimeLapse.pages(tree)
	var tree_name := Species.from_id(str(tree["species"])).display_name
	_flip_title.text = "The %s, %d morning%s" % [tree_name.to_lower(), pages.size(), "" if pages.size() == 1 else "s"]
	_flip_status.text = ""
	_video_button.disabled = false
	_flip_send.disabled = false
	_flip_send.visible = can_send()
	_flip.play(pages)


## "flip through": the flip-book of the tree on the spread on show (or the newest tree).
func _flip_through() -> void:
	if _trees.is_empty():
		return
	var shown := _shown_photo()
	_show_flip(TimeLapse.tree_of(_trees, shown) if shown >= 0 else (_flip_tree if _flip_tree >= 0 else _trees.size() - 1))


## Writes the tree's month as a short video (MjpegAvi) and hands it to the phone's gallery.
## One frame is made per rendered frame, so the album stays alive while the film develops.
## Returns the video's path ("" if it failed).
func save_video(tree_index: int) -> String:
	_video_button.disabled = true
	var path: String = await _develop(tree_index)
	if path == "":
		_video_button.disabled = false
		return ""
	if Phone.save_video_to_gallery(path, _on_video_saved.bind(path)):
		# The phone turns it into an MP4 on its own; the button waits for the answer.
		_flip_status.text = "saving to the phone's gallery..."
		return path
	_flip_status.text = "Saved as %s" % ProjectSettings.globalize_path(path)
	_video_button.disabled = false
	return path


## Writes the tree's month as a Motion-JPEG film (TimeLapse.video_path); "" if it failed.
func _develop(tree_index: int) -> String:
	var pages := TimeLapse.pages(_trees[tree_index])
	_flip_status.text = "developing the film..."
	var jpegs: Array[PackedByteArray] = []
	var size := Vector2i.ZERO
	for p in pages:
		await get_tree().process_frame
		var img := Image.load_from_file(ProjectSettings.globalize_path(p))
		if img == null or img.is_empty():
			continue
		if size == Vector2i.ZERO:
			size = TimeLapse.video_size(img.get_width(), img.get_height())
		jpegs.append(TimeLapse.encode_frame(img, size))
	for _i in range(TimeLapse.HOLD_LAST if not jpegs.is_empty() else 0):
		jpegs.append(jpegs[-1])
	var path := TimeLapse.video_path(_trees[tree_index])
	DirAccess.make_dir_recursive_absolute(TimeLapse.DIR)
	if jpegs.is_empty() or MjpegAvi.write(path, jpegs, size.x, size.y, TimeLapse.FPS) != OK:
		_flip_status.text = "The film could not be saved."
		return ""
	return path


## The phone's answer to save_video (Phone.save_video_to_gallery).
func _on_video_saved(ok: bool, path: String) -> void:
	if ok:
		_flip_status.text = "Saved to the phone's gallery (Movies/Tree)."
	else:
		_flip_status.text = "The gallery would not take it. Saved as %s" % ProjectSettings.globalize_path(path)
	_video_button.disabled = false


# --- sending (0.8): the share sheet on the phone, the game's folder on a PC ------------------

## "send" shows on the phone when its share sheet is there (hidden on a very old one), and on a PC.
static func can_send() -> bool:
	return PhoneFiles.can_share() or not OS.has_feature("mobile")


## The photo index on a side of the spread on show; -1 if none.
func _shown_photo_at(side: int) -> int:
	var first := _shown_photo()
	return first + side if first >= 0 and first + side < _photos.size() else -1


## The Polaroid of the photo (as in the album, with its caption) to the phone's share sheet; the
## player picks the app. On a PC it is written to the game's folder, and the folder opens.
func send_photo(index: int) -> String:
	if index < 0 or index >= _photos.size():
		return ""
	for b in _send_buttons:
		b.disabled = true
	_album_note.text = ""
	var path: String = await Polaroid.render(self, _photos[index])
	for b in _send_buttons:
		b.disabled = false
	if path == "":
		_album_note.text = "The picture could not be made."
		return ""
	if not PhoneFiles.share_image(path, _on_photo_shared):
		_album_note.text = "The picture is in the game's folder."
		print("sent picture: ", ProjectSettings.globalize_path(path))
		_open_folder(path.get_base_dir())
	return path


func _on_photo_shared(result: String) -> void:
	match result:
		"ok":
			_album_note.text = ""
		"no_app":
			_album_note.text = "No app on this phone takes a picture. It stays in the game's folder."
		_:
			_album_note.text = "The picture could not be sent. It stays in the game's folder."


## The month's film to the share sheet (the phone makes an MP4 first). On a PC: the game's folder.
func send_video(tree_index: int) -> String:
	_flip_send.disabled = true
	var path: String = await _develop(tree_index)
	if path == "":
		_flip_send.disabled = false
		return ""
	if PhoneFiles.share_video(path, _on_video_shared):
		_flip_status.text = "getting the film ready to send..."
		return path
	_flip_send.disabled = false
	_flip_status.text = "The film is in the game's folder."
	print("sent film: ", ProjectSettings.globalize_path(path))
	_open_folder(path.get_base_dir())
	return path


func _on_video_shared(result: String) -> void:
	_flip_send.disabled = false
	match result:
		"ok":
			_flip_status.text = ""
		"no_app":
			_flip_status.text = "No app on this phone takes a film. It stays in the game's folder."
		_:
			_flip_status.text = "The film could not be sent. It stays in the game's folder."


func _open_folder(dir: String) -> void:
	if open_folders and not OS.has_feature("mobile") and DisplayServer.get_name() != "headless":
		OS.shell_open(ProjectSettings.globalize_path(dir))


# --- the seed bag -------------------------------------------------------------------

func _build_seeds() -> void:
	_seeds = Control.new()
	_seeds.set_anchors_preset(Control.PRESET_FULL_RECT)
	_seeds.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_seeds)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.5)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_seeds.add_child(dim)
	var sheet := PanelContainer.new()
	sheet.add_theme_stylebox_override("panel", Paper.paper_box(300, 420, 77, "top", 30.0))
	sheet.set_anchors_preset(Control.PRESET_FULL_RECT)
	sheet.offset_left = 50
	sheet.offset_right = -50
	sheet.offset_top = 170
	sheet.offset_bottom = -540
	sheet.rotation_degrees = 1.2
	_seeds.add_child(sheet)
	_seeds_box = VBoxContainer.new()
	_seeds_box.add_theme_constant_override("separation", 12)
	sheet.add_child(_seeds_box)
	_seeds.visible = false


## Opens the seed bag. When this tree is finished (or the test switch is on) it offers the next
## seed: one button per unlocked species; otherwise it says what the bag holds.
func open_seeds(state: GameState, any_species: bool) -> void:
	for c in _seeds_box.get_children():
		c.queue_free()
	var head := HBoxContainer.new()
	_seeds_box.add_child(head)
	var title := Paper.ink_label("The seed bag", 40, Paper.INK, true)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	var close := Paper.ink_button("close", 26)
	close.pressed.connect(close_boards)
	head.add_child(close)
	var unlocked := state.unlocked_species(any_species)
	var names: Array[String] = []
	for sid in unlocked:
		names.append(Species.from_id(sid).display_name.to_lower())
	var text := ""
	if state.finished:
		text = "The %s has grown to its full size and dropped a seed. Which seed goes into the ground beside it?" % state.tree_name()
	elif any_species:
		text = "(Every seed is at hand now, from the pinboard. The %s is not finished yet.)" % state.tree_name()
	else:
		text = "Seeds saved for later: %s.

When this %s has grown to its full size (%d of %d segments now), one goes into the ground beside it and a new tree begins. Each finished tree brings a new kind of seed." % [
			", ".join(names), state.tree_name(), state.sim.graph.size(), state.sim.species.finish_nodes]
	var body := Paper.ink_label(text, 27)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_seeds_box.add_child(body)
	if state.can_plant_next(any_species):
		_seeds_box.add_child(Paper.ink_label("plant the next seed:", 30, Paper.INK, true))
		for sid in unlocked:
			var sp := Species.from_id(sid)
			var b := Paper.ink_button(sp.display_name, 30)
			b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
			b.pressed.connect(func() -> void:
				_seeds.visible = false
				plant_pressed.emit(sid))
			_seeds_box.add_child(b)
	_seeds.visible = true


# --- the tree's own page: "while you were away" (its status page is the journal's first) -----

func _build_tree_page() -> void:
	_tree_page = Control.new()
	_tree_page.set_anchors_preset(Control.PRESET_FULL_RECT)
	_tree_page.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_tree_page)
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.45)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_tree_page.add_child(dim)
	# A page torn out of the diary, a little askew.
	var sheet := PanelContainer.new()
	sheet.add_theme_stylebox_override("panel", Paper.paper_box(300, 440, 61, "all", 30.0))
	sheet.set_anchors_preset(Control.PRESET_FULL_RECT)
	sheet.offset_left = 44
	sheet.offset_right = -44
	sheet.offset_top = 130
	sheet.offset_bottom = -170
	sheet.rotation_degrees = -1.0
	_tree_page.add_child(sheet)
	_tree_box = VBoxContainer.new()
	_tree_box.add_theme_constant_override("separation", 12)
	sheet.add_child(_tree_box)
	_tree_page.visible = false


## The torn diary page about the tree. With a report (GameState.take_away_report): what
## happened while the game was closed, the growth in metres, the visitors that came and an ink
## sketch of the tree as it stands now. Without one: how the tree is (the journal's first page
## shows the same, Journal's "tree" ribbon).
func show_tree_page(state: GameState, report: Dictionary = {}) -> void:
	for c in _tree_box.get_children():
		c.queue_free()
	var head := HBoxContainer.new()
	_tree_box.add_child(head)
	var away := not report.is_empty()
	var title := Paper.ink_label("While you were away" if away else "My " + state.tree_name(), 40, Paper.INK, true)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	head.add_child(title)
	var close := Paper.ink_button("close", 26)
	close.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	close.pressed.connect(func() -> void: _tree_page.visible = false)
	head.add_child(close)
	# One label per paragraph (a long label with blank lines spreads out on the page).
	for line in (away_text(state.tree_name(), report) if away else status_text(state)):
		var body := Paper.ink_label(line, 27)
		body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		_tree_box.add_child(body)
	_sketch_graph = state.sim.graph
	var sketch := Control.new()
	sketch.size_flags_vertical = Control.SIZE_EXPAND_FILL
	sketch.custom_minimum_size = Vector2(0, 300)
	sketch.mouse_filter = Control.MOUSE_FILTER_IGNORE
	sketch.draw.connect(func() -> void: draw_graph_sketch(sketch, _sketch_graph))
	_tree_box.add_child(sketch)
	_tree_page.visible = true


func is_tree_page_open() -> bool:
	return _tree_page.visible


## "2 days and 3 hours" for an absence in seconds.
static func away_duration(seconds: float) -> String:
	var hours := int(seconds / 3600.0)
	var days := hours / 24
	hours = hours % 24
	var d := "%d day%s" % [days, "" if days == 1 else "s"]
	var h := "%d hour%s" % [hours, "" if hours == 1 else "s"]
	if days == 0:
		return h
	return d if hours == 0 else d + " and " + h


## The paragraphs of the "while you were away" page.
static func away_text(tree_name: String, report: Dictionary) -> Array[String]:
	var lines: Array[String] = ["I was away for %s." % away_duration(float(report.get("seconds", 0.0)))]
	var grown := float(report.get("grown", 0.0))
	var segments := int(report.get("segments", 0))
	if grown >= 0.05:
		lines.append("Meanwhile the %s grew %.1f m and is %.1f m tall now." % [tree_name, grown, float(report.get("height", 0.0))])
	elif segments > 0:
		lines.append("Meanwhile the %s grew a few new twigs (%d segments)." % [tree_name, segments])
	else:
		lines.append("The %s rested and waited for me." % tree_name)
	var visitors: Array = report.get("visitors", [])
	if visitors.is_empty():
		lines.append("No visitors came this time.")
	else:
		for id in visitors:
			lines.append(str(Visitors.LINES.get(id, id)))
	return lines


## The paragraphs of the tree's status page: how the tree is doing and who has come so far.
static func status_text(state: GameState) -> Array[String]:
	var sim := state.sim
	var lines: Array[String] = []
	if state.finished:
		lines.append("Day %d in the clearing. The %s has grown to its full size: %.1f m tall." % [state.day_number(), state.tree_name(), sim.height()])
	else:
		lines.append("Day %d in the clearing. The %s is %.1f m tall, %d of %d segments grown." % [
			state.day_number(), state.tree_name(), sim.height(), sim.living_nodes(), sim.species.finish_nodes])
	var came: Array[String] = []
	for id in Visitors.LINES:
		if Visitors.has_come(state, id):
			came.append(str(Visitors.LINES[id]))
	if came.is_empty():
		lines.append("No visitors yet.")
	lines.append_array(came)
	return lines


## The tree as it stands, drawn in ink from the plant graph: every living segment a stroke as
## thick as its wood, a little shaky like a quick drawing, leaves as pale green dabs at the tips.
static func draw_graph_sketch(c: Control, g: PlantGraph) -> void:
	if g == null or g.size() == 0:
		return
	# Seen from the south with a slight turn, so the crown has some depth.
	var pts := PackedVector2Array()
	pts.resize(g.size())
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for id in range(g.size()):
		var p := g.positions[id]
		var q := Vector2(p.x + p.z * 0.35, -p.y)
		pts[id] = q
		if not g.get_flag(id, "dead", false):
			lo = lo.min(q)
			hi = hi.max(q)
	var span := hi - lo
	var margin := 24.0
	var fit := minf((c.size.x - margin * 2.0) / maxf(span.x, 0.5), (c.size.y - margin * 2.0) / maxf(span.y, 0.5))
	fit = minf(fit, 140.0)
	var base := Vector2(c.size.x * 0.5, c.size.y - margin)
	var mid := Vector2((lo.x + hi.x) * 0.5, hi.y)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var ink := Color(Paper.INK, 0.85)
	c.draw_line(base + Vector2(-c.size.x * 0.3, 2), base + Vector2(c.size.x * 0.3, 2), Color(Paper.INK, 0.5), 2.0, true)
	var leaves := Color(0.32, 0.46, 0.22, 0.35)
	for id in range(1, g.size()):
		var parent := g.parents[id]
		if parent < 0 or g.get_flag(id, "dead", false):
			continue
		var a := base + (pts[parent] - mid) * fit + Vector2(rng.randf_range(-0.6, 0.6), rng.randf_range(-0.6, 0.6))
		var b := base + (pts[id] - mid) * fit
		c.draw_line(a, b, ink, clampf(g.radii[id] * fit * 1.6, 1.0, 10.0), true)
	for id in g.tips():
		if not g.get_flag(id, "dead", false):
			c.draw_circle(base + (pts[id] - mid) * fit, 3.0 + rng.randf() * 3.0, leaves)


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
