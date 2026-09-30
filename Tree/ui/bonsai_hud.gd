class_name BonsaiHud
extends CanvasLayer
## Bonsai mode's paper (design doc section 16; 0.7: the tools are real things on the sill,
## docs/notes/bonsai-tools-0.7.md). A small handwritten scrap with how the bonsai is (care day,
## the soil's water and N, P, K, the pot, a repotting due) and a line for the tool in hand; a
## note "back to the bench"; small paper labels on the sill's things (until each was used once,
## and always with "clearer print"); the pellet tin's slip (N, P or K); the repotting slip while
## the tree is out of its pot; and the pages: the style pages (drawings of the classic styles,
## for inspiration only), the bonsai's album page (milestones and an ink sketch) and the
## cuttings on the shelf.

signal back_pressed

const STYLES: Array[String] = ["formal_upright", "informal_upright", "slanting", "cascade", "broom"]
const STYLE_TEXTS := {
	"formal_upright": ["Formal upright", "A straight trunk tapering to the top, the branches in tiers, the lowest the longest. Calm and old."],
	"informal_upright": ["Informal upright", "The trunk bends in soft curves, the apex back above the roots. The juniper in the reference grows like this."],
	"slanting": ["Slanting", "The whole tree leans, as if a wind had always blown from one side; a strong root holds it on the other."],
	"cascade": ["Cascade", "In a tall pot the trunk turns down over the rim and falls below it, like a tree on a cliff."],
	"broom": ["Broom", "A straight short trunk opens into many fine branches all at once: a dome, like a lone linden in a field."],
}
const TOOL_HINTS := {
	"": "Pick up a tool from the sill. Drag to look round, pinch to come closer.",
	"water": "Tap the soil to water. Tap the can again to put it down.",
	"fertiliser": "Choose N, P or K on the slip, then tap the soil.",
	"shears": "Touch a branch: the mark shows the cut, the outline what falls. Lift to cut (a third at most).",
	"pinch": "Tap a fresh tip (grown today or yesterday) with the tweezers: the buds behind it fill in.",
	"wire": "Touch a branch and drag it into its new line; the copper holds it. Tap a wired branch to take the wire off.",
	"trowel": "On a repot day, tap the pot: the tree comes out with its root ball.",
}
const PELLETS: Array[String] = ["N", "P", "K"]
const PELLET_WORDS: Array[String] = ["leaves", "roots", "wood"]
## The repotting slip's lower edge sits this far above the front row's tap points (canvas px).
const REPOT_SLIP_ABOVE_TOOLS := 20.0

var view: BonsaiView:
	set = _set_view
var state: GameState
## Whether the test switch "any species now" is on (all cuttings).
var any_species: Callable = func() -> bool: return false
## Set by main: shows the first-time journal page for a tool ("shears", "pinch", "wire", "repot").
var first_page: Callable = func(_id: String) -> void: pass

var _root: Control
var _status: Label
var _soil: Label
var _hint: Label
var _back: Control
var _labels_layer: Control
var _labels: Dictionary = {}  # sill thing id -> PanelContainer
var _pellet_slip: PanelContainer
var _pellet_buttons: Array[Button] = []
var _repot_slip: PanelContainer
var _pot_buttons: Dictionary = {}  # pot id -> Button
var _sheet: Control
var _sheet_box: VBoxContainer
var _style_index: int = 0
var _said: String = ""
var _said_time: float = 0.0


func _ready() -> void:
	layer = 17
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)
	_build_labels()
	_build_status()
	_build_back()
	_build_pellet_slip()
	_build_repot_slip()
	_build_sheet()
	visible = false


func _set_view(v: BonsaiView) -> void:
	view = v
	if v == null:
		return
	v.object_tapped.connect(_on_object)
	v.tool_picked.connect(func(id: String) -> void:
		_forget_said()
		if id in ["shears", "pinch", "wire"]:
			first_page.call(id)
		elif id == "trowel":
			first_page.call("repot"))
	v.tool_used.connect(_on_used)
	v.said.connect(func(text: String) -> void:
		_said = text
		_said_time = 4.0)


## The status as a small scrap in the top right corner, beside the note back to the bench.
func _build_status() -> void:
	var scrap := PanelContainer.new()
	PaperLook.apply(scrap, "strip", 171, 14.0)
	scrap.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	scrap.offset_left = -440
	scrap.offset_right = -18
	scrap.offset_top = 20
	scrap.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	scrap.rotation_degrees = 0.8
	scrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(scrap)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 0)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	scrap.add_child(box)
	_status = Paper.ink_label("", 25, Paper.INK, true)
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_status)
	_soil = Paper.ink_label("", 22)
	_soil.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_soil)
	_hint = Paper.ink_label("", 20, Paper.FAINT_INK)
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_hint)


## A small torn note in the top left corner: back to the workbench.
func _build_back() -> void:
	var note := PanelContainer.new()
	note.add_theme_stylebox_override("panel", Paper.paper_box(200, 90, 177, "all", 10.0))
	note.position = Vector2(16, 22)
	note.rotation_degrees = -2.5
	_root.add_child(note)
	var b := Paper.ink_button("back to\nthe bench", 24)
	b.custom_minimum_size = Vector2(170, 84)
	b.pressed.connect(func() -> void:
		if view != null and not view.busy:
			back_pressed.emit())
	note.add_child(b)
	_back = note


## A small paper label for each thing on the sill.
func _build_labels() -> void:
	_labels_layer = Control.new()
	_labels_layer.set_anchors_preset(Control.PRESET_FULL_RECT)
	_labels_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(_labels_layer)
	var i := 0
	for id in BonsaiTools.LABELS:
		var tag := PanelContainer.new()
		tag.add_theme_stylebox_override("panel", Paper.paper_box(110, 40, 180 + i, "all", 7.0))
		tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var l := Paper.ink_label(BonsaiTools.LABELS[id], 21)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		tag.add_child(l)
		tag.rotation_degrees = [-3.0, 2.0, -1.5, 2.5, -2.0, 1.5, -2.5][i % 7]
		tag.visible = false
		tag.modulate.a = 0.0
		_labels_layer.add_child(tag)
		_labels[id] = tag
		i += 1


## The pellet tin's slip: which pellets the next spoon gives.
func _build_pellet_slip() -> void:
	_pellet_slip = PanelContainer.new()
	_pellet_slip.add_theme_stylebox_override("panel", Paper.paper_box(300, 150, 183, "all", 12.0))
	_pellet_slip.rotation_degrees = 1.5
	_pellet_slip.visible = false
	_root.add_child(_pellet_slip)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 4)
	_pellet_slip.add_child(box)
	var head := Paper.ink_label("which pellets?", 22, Paper.FAINT_INK)
	head.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(head)
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 8)
	box.add_child(row)
	for k in range(3):
		var kind := k
		var b := Paper.ink_button("%s\n%s" % [PELLETS[k], PELLET_WORDS[k]], 23)
		b.custom_minimum_size = Vector2(96, 96)
		b.add_theme_color_override("font_color", Resources.KIND_COLORS[k + 1].darkened(0.45))
		# The nutrient's mark above its letter (0.8), as on the dots underground.
		b.icon = NutrientMarks.icon(k + 1, 30)
		b.icon_alignment = HORIZONTAL_ALIGNMENT_CENTER
		b.vertical_icon_alignment = VERTICAL_ALIGNMENT_TOP
		b.pressed.connect(func() -> void: choose_pellets(kind))
		row.add_child(b)
		_pellet_buttons.append(b)


## Chooses the pellets the tin gives next (0 N, 1 P, 2 K).
func choose_pellets(kind: int) -> void:
	if view == null:
		return
	view.pellet_kind = kind
	for k in range(3):
		var b := _pellet_buttons[k]
		var on := k == kind
		for s in ["normal", "hover", "pressed", "focus"]:
			var sb := b.get_theme_stylebox(s) as StyleBoxFlat
			if sb != null:
				sb = sb.duplicate() as StyleBoxFlat
				sb.border_color = Paper.RED_INK if on else Paper.INK
				sb.set_border_width_all(4 if on else 2)
				sb.bg_color = Color(0.6, 0.18, 0.12, 0.1) if on else Color(0.2, 0.15, 0.1, 0.0)
				b.add_theme_stylebox_override(s, sb)


## While the tree is out of its pot: the pots to choose from, low on the screen under the tools
## (the shears trim the root ball, the trowel puts it back; the status scrap says so).
func _build_repot_slip() -> void:
	_repot_slip = PanelContainer.new()
	_repot_slip.add_theme_stylebox_override("panel", Paper.paper_box(680, 170, 185, "all", 12.0))
	_repot_slip.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	_repot_slip.offset_left = 14
	_repot_slip.offset_right = -14
	_repot_slip.offset_top = -172
	_repot_slip.offset_bottom = -10
	_repot_slip.rotation_degrees = -0.5
	_repot_slip.visible = false
	_root.add_child(_repot_slip)
	var pots := HFlowContainer.new()
	pots.alignment = FlowContainer.ALIGNMENT_CENTER
	pots.add_theme_constant_override("h_separation", 8)
	pots.add_theme_constant_override("v_separation", 6)
	_repot_slip.add_child(pots)
	for pid in BonsaiSim.POT_ORDER:
		var id: String = pid
		# Full tap size (0.7 review: 56 px tall was under a finger's 9 mm).
		var b := Paper.ink_button(str(BonsaiSim.POTS[id]["name"]), 23)
		b.pressed.connect(func() -> void:
			if view != null:
				view.repot_pick(id))
		pots.add_child(b)
		_pot_buttons[id] = b
	var done := Paper.ink_button("fresh soil, and in", 23)
	done.add_theme_color_override("font_color", Paper.RED_INK)
	done.pressed.connect(func() -> void:
		if view != null:
			view.repot_finish())
	pots.add_child(done)


func show_hud(on: bool) -> void:
	visible = on
	if not on:
		close_sheet()


func is_busy() -> bool:
	return _sheet.visible


func _on_object(id: String) -> void:
	_forget_said()
	match id:
		"styles":
			_open_styles()
		"album":
			_open_album()
		"cuttings":
			_open_cuttings()
	_mark_used(id)


## A said line ("Not yet: ...") ends with the next thing done (0.7 review: it stayed on).
func _forget_said() -> void:
	_said = ""
	_said_time = 0.0


func _on_used(kind: String) -> void:
	_forget_said()
	match kind:
		"burn", "fertiliser":
			_mark_used("fertiliser")
		"unwire", "wire":
			_mark_used("wire")
		"turn":
			_mark_used("turn_left")
			_mark_used("turn_right")
		"repot":
			_mark_used("trowel")
		_:
			_mark_used(kind)


func _mark_used(id: String) -> void:
	if state != null:
		state.seen_pages["bonsai_tool_" + id] = true


## Whether the sill thing `id` still carries its first-time label.
func is_new(id: String) -> bool:
	return state != null and not state.seen_pages.has("bonsai_tool_" + id)


func _process(delta: float) -> void:
	if state == null or state.bonsai == null or view == null:
		return
	# The box of cuttings lies on the sill only while there is another cutting to choose.
	(view.tools.items["cuttings"] as Node3D).visible = state.bonsai_choices(any_species.call()).size() > 1
	if not visible:
		return
	var b := state.bonsai
	_status.text = "My %s bonsai, care day %d" % [b.plant_name(), b.day()]
	var water := "the soil is dry, the leaves droop" if b.droop() > 0.0 else ("the soil is wet, it grows slowly" if b.is_too_wet() else "the soil is damp")
	var words: Array[String] = []
	for k in range(3):
		var s := b.soil[k]
		words.append("%s %s" % [PELLETS[k], "low" if s < 0.2 else ("plenty" if s > BonsaiSim.BURN_LEVEL else "ok")])
	var pot := "In the %s" % b.pot_name()
	if b.repot_due:
		pot += ", asking to be repotted"
	_soil.text = "%s; %s. %s." % [water, ", ".join(words), pot]
	_said_time = maxf(0.0, _said_time - delta)
	var lifted := view.is_lifted()
	_hint.text = _said if _said_time > 0.0 else str(TOOL_HINTS.get(view.tool, ""))
	if _sheet.visible:
		# A page lies over the scrap: its tool line would peek out above the page.
		_hint.text = ""
	elif lifted and _said_time <= 0.0:
		_hint.text = "Out of the pot: snip the circling roots with the shears (%d%% so far), pick a pot below, then the trowel puts it back in fresh soil." % int(view.trim_share() * 100.0)
	_repot_slip.visible = lifted and not _sheet.visible
	if _repot_slip.visible:
		_place_repot_slip()
	if lifted:
		for id in _pot_buttons:
			(_pot_buttons[id] as Button).modulate = Color(1.0, 0.55, 0.35) if view.new_pot() == id else Color.WHITE
	_place_labels()
	_place_pellet_slip()


## The labels by the things on the sill: first time (until used once), with "clearer print",
## and on a PC when the pointer rests on one. Not on the tool in hand.
func _place_labels() -> void:
	var pts := view.object_screen_points()
	var room := _labels_layer.size
	for id in _labels:
		var tag: PanelContainer = _labels[id]
		var on: bool = pts.has(id) and not _sheet.visible and id != view.tool and (Paper.clear_print or is_new(id) or view.hover == id)
		tag.modulate.a = move_toward(tag.modulate.a, 1.0 if on else 0.0, 0.12)
		tag.visible = tag.modulate.a > 0.01
		if not pts.has(id):
			continue
		tag.size = tag.get_combined_minimum_size()
		tag.pivot_offset = tag.size * 0.5
		var pos := (pts[id] as Vector2) + Vector2(-tag.size.x * 0.5, 30.0 + float(BonsaiTools.LABEL_DROP.get(id, 0.0)))
		tag.position = pos.clamp(Vector2(6, 6), Vector2(maxf(room.x - tag.size.x - 6.0, 6.0), maxf(room.y - tag.size.y - 6.0, 6.0)))


## The repotting slip lies on the bench between the pot and the front row of tools, so the
## shears and the trowel stay free to tap (0.7 review: with full-size pot buttons it grew taller).
func _place_repot_slip() -> void:
	var room := _root.size
	var h := _repot_slip.get_combined_minimum_size().y
	var tools_top := room.y
	for id in ["trowel", "shears", "pinch", "wire"]:
		tools_top = minf(tools_top, view.tools.rest_point(view.camera, id).y)
	var bottom := clampf(tools_top - REPOT_SLIP_ABOVE_TOOLS, h + 280.0, room.y - 10.0)
	_repot_slip.offset_top = bottom - h - room.y
	_repot_slip.offset_bottom = bottom - room.y


## The pellet slip floats above the tin while it is in hand.
func _place_pellet_slip() -> void:
	var on := view.tool == "fertiliser" and not _sheet.visible and not view.busy
	_pellet_slip.visible = on
	if not on:
		return
	var room := _root.size
	_pellet_slip.size = _pellet_slip.get_combined_minimum_size()
	# Above the tin's place on the sill, so it stays put while the tin follows the finger.
	var at := view.tools.rest_point(view.camera, "fertiliser")
	var pos := at + Vector2(-_pellet_slip.size.x * 0.5, -_pellet_slip.size.y - 70.0)
	_pellet_slip.position = pos.clamp(Vector2(10, 260), Vector2(maxf(room.x - _pellet_slip.size.x - 10.0, 10.0), maxf(room.y - _pellet_slip.size.y - 10.0, 10.0)))


# --- sheets ------------------------------------------------------------------------------

func _build_sheet() -> void:
	_sheet = Control.new()
	_sheet.set_anchors_preset(Control.PRESET_FULL_RECT)
	_sheet.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_sheet)
	var page := PanelContainer.new()
	PaperLook.apply(page, "torn_page", 173, 34.0)
	page.set_anchors_preset(Control.PRESET_FULL_RECT)
	page.offset_left = 40
	page.offset_right = -40
	page.offset_top = 150
	page.offset_bottom = -300
	page.rotation_degrees = -0.8
	_sheet.add_child(page)
	_sheet_box = VBoxContainer.new()
	_sheet_box.add_theme_constant_override("separation", 12)
	page.add_child(_sheet_box)
	_sheet.visible = false


func close_sheet() -> void:
	_sheet.visible = false


func _open(title: String) -> VBoxContainer:
	for c in _sheet_box.get_children():
		c.queue_free()
	var head := HBoxContainer.new()
	_sheet_box.add_child(head)
	var t := Paper.ink_label(title, 38, Paper.INK, true)
	t.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	t.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	head.add_child(t)
	var close := Paper.ink_button("close", 25)
	close.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	close.pressed.connect(close_sheet)
	head.add_child(close)
	_sheet.visible = true
	return _sheet_box


func _text(parent: Control, words: String, size: int = 27) -> Label:
	var l := Paper.ink_label(words, size)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(l)
	return l


func _open_styles(index: int = -1) -> void:
	if index >= 0:
		_style_index = posmod(index, STYLES.size())
	var id := STYLES[_style_index]
	var box := _open(STYLE_TEXTS[id][0])
	var pic := TextureRect.new()
	pic.texture = load("res://ui/bonsai_styles/%s.png" % id)
	pic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	pic.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	pic.custom_minimum_size = Vector2(0, 430)
	pic.size_flags_vertical = Control.SIZE_EXPAND_FILL
	box.add_child(pic)
	_text(box, STYLE_TEXTS[id][1], 26)
	_text(box, "Only for inspiration: every bonsai may grow its own way.", 22).add_theme_color_override("font_color", Paper.FAINT_INK)
	var nav := HBoxContainer.new()
	nav.alignment = BoxContainer.ALIGNMENT_CENTER
	nav.add_theme_constant_override("separation", 40)
	box.add_child(nav)
	var prev := Paper.ink_button("< page", 24)
	prev.pressed.connect(func() -> void: _open_styles(_style_index - 1))
	nav.add_child(prev)
	nav.add_child(Paper.ink_label("%d / %d" % [_style_index + 1, STYLES.size()], 24))
	var next := Paper.ink_button("page >", 24)
	next.pressed.connect(func() -> void: _open_styles(_style_index + 1))
	nav.add_child(next)


## The bonsai's album page: it has no end, so the page grows by milestones.
func _open_album() -> void:
	var b := state.bonsai
	var box := _open("My %s bonsai" % b.plant_name())
	var lines := album_lines(b)
	var shown := lines.slice(maxi(0, lines.size() - 9))
	for line in shown:
		_text(box, line, 24)
	var sketch := Control.new()
	sketch.custom_minimum_size = Vector2(0, 220)
	sketch.size_flags_vertical = Control.SIZE_EXPAND_FILL
	sketch.mouse_filter = Control.MOUSE_FILTER_IGNORE
	sketch.draw.connect(func() -> void: _sketch(sketch, b))
	box.add_child(sketch)


static func album_lines(b: BonsaiSim) -> Array[String]:
	var out: Array[String] = []
	for m in b.milestones:
		out.append("Day %d: %s" % [int(m["day"]), str(m["text"])])
	return out


## An ink sketch of the bonsai as it stands (front view), with its pot.
func _sketch(c: Control, b: BonsaiSim) -> void:
	var g := b.graph
	var h := maxf(b.height(), 1.0)
	var sc := minf(c.size.y * 0.82 / h, c.size.x * 0.3 / BonsaiSim.MAX_RADIUS)
	var base := Vector2(c.size.x * 0.5, c.size.y * 0.86)
	for id in range(1, g.size()):
		if b.is_dead(id):
			continue
		var p := g.positions[id].rotated(Vector3.UP, b.turn * PI * 0.5)
		var q := g.positions[g.parents[id]].rotated(Vector3.UP, b.turn * PI * 0.5)
		var col := Paper.FAINT_INK if b.is_jin(id) else Paper.INK
		c.draw_line(base + Vector2(-q.x, -q.y) * sc, base + Vector2(-p.x, -p.y) * sc, col, clampf(g.radii[id] * sc * 2.4, 1.0, 9.0))
	var w := c.size.x * 0.34
	var pot := PackedVector2Array([base + Vector2(-w * 0.5, 0), base + Vector2(w * 0.5, 0), base + Vector2(w * 0.44, 24), base + Vector2(-w * 0.44, 24), base + Vector2(-w * 0.5, 0)])
	c.draw_polyline(pot, Paper.INK, 2.5)


func _open_cuttings() -> void:
	var box := _open("Cuttings")
	_text(box, "Each finished tree on the clearing left a cutting. One bonsai stands on the sill in the light; the others rest on the shelf below and wait, just as they were.")
	for sid in state.bonsai_choices(any_species.call()):
		var id: String = sid
		var sp := Species.from_id(id)
		var here := state.bonsai.species.id == id
		var line := "%s  (on the sill)" % sp.display_name if here else sp.display_name
		if here:
			_text(box, line, 28)
			continue
		var b := Paper.ink_button(line + ("  (resting)" if state.bonsai_resting.has(id) else "  (a new cutting)"), 26)
		b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		b.pressed.connect(func() -> void:
			close_sheet()
			if state.swap_bonsai(id, any_species.call()):
				view.setup(state))
		box.add_child(b)
