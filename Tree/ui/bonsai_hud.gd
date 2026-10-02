class_name BonsaiHud
extends CanvasLayer
## Bonsai mode's paper (design doc section 16; 0.7: the tools are real things on the sill,
## docs/notes/bonsai-tools-0.7.md). A small handwritten scrap with how the bonsai is (care day,
## the soil's water and N, P, K, the pot, a repotting due) and a line for the tool in hand
## (0.8.2.7: no "back to the bench" note any more, a tap below the sill goes back); small paper labels on the sill's things (until each was used once,
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
## The hint for the tool in hand (0.8.1, item 18): it names the tool first, so it is always clear
## which one is in hand, then what to touch and how to put it back.
## 0.8.2.2: the can, the tin and the trowel work on one tap and are never in hand.
const TOOL_HINTS := {
	"": "Tap the can to water, the tin for pellets. Pick up shears, tweezers or wire. Drag to look round; tap below the sill to go back.",
	"shears": "In hand: the shears. Touch a branch: the mark shows the cut. Lift to cut (a third at most).",
	"pinch": "In hand: the tweezers. Touch a fresh tip (ringed), lift to pinch it.",
	"wire": "In hand: the copper wire. Touch a branch and drag it into its new line; tap a wired one to free it.",
}
## The hint while the tin's slip is open.
const TIN_HINT := "The pellet tin: tap N, P or K on the slip and a spoon of it goes on the soil."
## The hint while the tree is out of its pot (repotting).
const REPOT_HINT := "Out of the pot: tap the roots to trim them, then tap a pot on the slip; fresh soil fills by itself."
## The word on the held tool's place on the sill: tap it to put the tool back.
const PUT_BACK := "put back"
const PELLETS: Array[String] = ["N", "P", "K"]
const PELLET_WORDS: Array[String] = ["leaves", "roots", "wood"]
## The repotting slip's lower edge sits this far above the screen's foot (canvas px).
const REPOT_SLIP_BOTTOM := 14.0

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
var _labels_layer: Control
var _labels: Dictionary = {}  # sill thing id -> PanelContainer
var _pellet_slip: PanelContainer
var _pellet_buttons: Array[Button] = []
## The kind the slip and the tin's label last showed (-1: not yet).
var _shown_kind: int = -1
var _repot_slip: PanelContainer
var _pot_buttons: Dictionary = {}  # pot id -> Button
var _pot_flow: HFlowContainer
var _pots_first: String = ""
var _trim_button: Button
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
			# 0.8.2.7 (Simon: "the tweezers' label was still there"): picking a tool up is
			# knowing it; its first-time label goes, as the others' do once used.
			_mark_used(id)
			first_page.call(id))
	v.tool_used.connect(_on_used)
	# A tap below the windowsill goes back to the bench (0.8.2.2; 0.8.2.7: the only way back,
	# the note in the top left corner is gone).
	v.back_requested.connect(func() -> void:
		if not v.busy and not _sheet.visible:
			back_pressed.emit())
	v.said.connect(func(text: String) -> void:
		_said = text
		_said_time = 4.0)


## The status as a small scrap in the top right corner.
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


## The pellet tin's slip: which pellets the next spoon gives. A narrow slip with the three kinds
## one under the other, each its mark, letter and word in one ink ring; the kind the tin gives
## now is ringed in red ink (0.8 review: the ring cut through the words, the orange word was
## unreadable, and the slip covered the crown and the tin).
func _build_pellet_slip() -> void:
	_pellet_slip = PanelContainer.new()
	_pellet_slip.add_theme_stylebox_override("panel", Paper.paper_box(200, 300, 183, "all", 12.0))
	_pellet_slip.rotation_degrees = 1.2
	_pellet_slip.visible = false
	_root.add_child(_pellet_slip)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 2)
	_pellet_slip.add_child(box)
	var head := Paper.ink_label("a spoon of:", 22, Paper.FAINT_INK)
	head.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(head)
	for k in range(3):
		var kind := k
		var b := Button.new()
		b.text = "%s  %s" % [PELLETS[k], PELLET_WORDS[k]]
		b.focus_mode = Control.FOCUS_NONE
		b.custom_minimum_size = Vector2(172, Paper.INK_TAP)
		b.add_theme_font_override("font", Paper.hand_font(true))
		b.add_theme_font_size_override("font_size", 24)
		for c in ["font_color", "font_hover_color", "font_pressed_color", "font_focus_color"]:
			b.add_theme_color_override(c, Paper.INK)
		for st in ["normal", "hover", "pressed", "focus"]:
			var pad := StyleBoxEmpty.new()
			pad.content_margin_left = 20
			pad.content_margin_right = 14
			b.add_theme_stylebox_override(st, pad)
		# The nutrient's mark before its letter (0.8), as on the dots underground.
		b.icon = NutrientMarks.icon(k + 1, 30)
		b.icon_alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.add_theme_constant_override("h_separation", 10)
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.pressed.connect(func() -> void: choose_pellets(kind))
		b.draw.connect(func() -> void: _draw_pellet_ring(b, kind))
		box.add_child(b)
		_pellet_buttons.append(b)


## The ink ring round one kind on the slip: round the mark and the word together, red and
## heavier for the kind the tin gives now.
func _draw_pellet_ring(b: Button, kind: int) -> void:
	var on := view != null and view.pellet_kind == kind
	var sb := StyleBoxFlat.new()
	sb.draw_center = on
	sb.bg_color = Color(0.6, 0.18, 0.12, 0.08)
	sb.border_color = Paper.RED_INK if on else (Paper.RED_INK if b.is_hovered() else Paper.FAINT_INK)
	sb.set_border_width_all(4 if on else 2)
	sb.set_corner_radius_all(30)
	sb.corner_detail = 6
	sb.expand_margin_left = 1
	b.draw_style_box(sb, Rect2(Vector2(6, 6), b.size - Vector2(12, 12)))


## A tap on a kind on the slip: a spoon of it goes on the soil at once (0 N, 1 P, 2 K; 0.8.2.2,
## Simon: "die Pellets sollten direkt nach Auswahl gestreut werden").
func choose_pellets(kind: int) -> void:
	if view == null:
		return
	_forget_said()
	view.pour_pellets(kind)
	_show_pellet_kind()


## The slip, the tin's label and the hint follow the kind the tin gives (the remembered one, or
## before any choice what the soil lacks most), also when the slip opens or after a restart.
func _show_pellet_kind() -> void:
	if view == null:
		return
	_shown_kind = view.pellet_kind
	for b in _pellet_buttons:
		b.queue_redraw()


## While the tree is out of its pot (0.8.2.2: repotting in at most three taps, trowel, trim, pot):
## "trim the roots" (or a tap on the roots themselves), then the pots, the current one first; a
## tap on a pot puts the tree into it and the fresh soil fills by itself.
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
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 4)
	_repot_slip.add_child(box)
	_trim_button = Paper.ink_button("1  trim the roots", 23)
	_trim_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_trim_button.pressed.connect(func() -> void:
		if view != null and not view.busy:
			view.repot_trim())
	box.add_child(_trim_button)
	var head := Paper.ink_label("2  a pot: fresh soil fills by itself", 21, Paper.FAINT_INK)
	head.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(head)
	_pot_flow = HFlowContainer.new()
	_pot_flow.alignment = FlowContainer.ALIGNMENT_CENTER
	_pot_flow.add_theme_constant_override("h_separation", 8)
	_pot_flow.add_theme_constant_override("v_separation", 6)
	box.add_child(_pot_flow)
	for pid in BonsaiSim.POT_ORDER:
		var id: String = pid
		# Full tap size (0.7 review: 56 px tall was under a finger's 9 mm).
		var b := Paper.ink_button(str(BonsaiSim.POTS[id]["name"]), 23)
		b.pressed.connect(func() -> void:
			if view != null:
				view.repot_into(id))
		_pot_flow.add_child(b)
		_pot_buttons[id] = b


## The current pot first on the slip (it is offered first), the others in their order after it.
func _order_pots() -> void:
	var first := view.sim().pot if view != null and view.sim() != null else ""
	if first == _pots_first or not _pot_buttons.has(first):
		return
	_pots_first = first
	_pot_flow.move_child(_pot_buttons[first], 0)
	var i := 1
	for id in BonsaiSim.POT_ORDER:
		if id != first:
			_pot_flow.move_child(_pot_buttons[id], i)
			i += 1
	var b := _pot_buttons[first] as Button
	b.text = "%s (as now)" % BonsaiSim.POTS[first]["name"]
	for id in _pot_buttons:
		if id != first:
			(_pot_buttons[id] as Button).text = str(BonsaiSim.POTS[id]["name"])


func show_hud(on: bool) -> void:
	visible = on
	if not on:
		close_sheet()
	else:
		# The scraps' words are written before their first frame (0.8.2.4: one showed blank).
		_process(0.0)


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
		"trowel":
			first_page.call("repot")
			return
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
		_hint.text = REPOT_HINT
	elif view.tin_open and _said_time <= 0.0:
		_hint.text = TIN_HINT
	_repot_slip.visible = lifted and not _sheet.visible
	if _repot_slip.visible:
		_order_pots()
		var trimmed := view.trim_share() >= 1.0
		_trim_button.text = "1  roots trimmed" if trimmed else "1  trim the roots"
		_trim_button.disabled = trimmed
		_place_repot_slip()
	else:
		_pots_first = ""
	_place_labels()
	_place_pellet_slip()


## The labels by the things on the sill: first time (until used once), with "clearer print",
## and on a PC when the pointer rests on one. Not on the tool in hand.
func _place_labels() -> void:
	var pts := view.object_screen_points()
	var room := _labels_layer.size
	# 0.8.2 (look review: at a low angle the arrow's "turn" lay on the trowel): a label goes
	# under its thing when that is clear, else above, lower, or to a side, whichever covers the
	# least of the other things on the sill and of the labels already placed.
	var hulls := {}
	for id in pts:
		var h: PackedVector2Array = view.tools.hull(view.camera, id)
		if h.size() >= 3:
			var inner := Geometry2D.offset_polygon(h, -LABEL_HULL_SHRINK)
			hulls[id] = inner[0] if not inner.is_empty() else h
	var placed: Array[Rect2] = []
	for id in _labels:
		var tag: PanelContainer = _labels[id]
		var held: bool = id == view.tool
		# The tool in hand: its place says "put back" (0.8.1, item 18), always.
		var word: String = PUT_BACK if held else (_label_word(id))
		var l := tag.get_child(0) as Label
		if l.text != word:
			l.text = word
		var on: bool = (held or pts.has(id)) and not _sheet.visible and (held or Paper.clear_print or is_new(id) or view.hover == id)
		# 0.8.2.7 (Simon): the tools carry no name label (they speak for themselves); a tool in
		# hand still shows "put back" at its place.
		if not held and id in UNNAMED:
			on = false
		tag.modulate.a = move_toward(tag.modulate.a, 1.0 if on else 0.0, 0.12)
		tag.visible = tag.modulate.a > 0.01
		if not pts.has(id) and not held:
			continue
		tag.size = tag.get_combined_minimum_size()
		tag.pivot_offset = tag.size * 0.5
		var at: Vector2 = view.tools.rest_point(view.camera, id) if held else pts[id]
		var lo := Vector2(6, 6)
		var hi := Vector2(maxf(room.x - tag.size.x - 6.0, 6.0), maxf(room.y - tag.size.y - 6.0, 6.0))
		var drop := 30.0 + float(BonsaiTools.LABEL_DROP.get(id, 0.0))
		var w := tag.size.x
		var h := tag.size.y
		var spots: Array[Vector2] = [
			at + Vector2(-w * 0.5, drop),
			at + Vector2(-w * 0.5, -26.0 - h),
			at + Vector2(-w * 0.5, drop + h + 6.0),
			at + Vector2(-w - 30.0, -h * 0.5),
			at + Vector2(30.0, -h * 0.5),
		]
		var best := 0
		var best_cost := INF
		if not held:
			var was: int = int(tag.get_meta("spot", 0))
			for k in range(spots.size()):
				var r := Rect2(spots[k].clamp(lo, hi), tag.size)
				var cost := _cover(r, id, hulls, placed) + 30.0 * k - (60.0 if k == was else 0.0)
				if cost < best_cost:
					best_cost = cost
					best = k
			tag.set_meta("spot", best)
		tag.position = spots[best].clamp(lo, hi)
		# 0.8.2.2 (review: from the side the labels piled up): a label that would still lie over
		# another label, wherever it goes, waits until the view turns back (not "put back", and
		# not the thing the pointer rests on).
		if on and not held and view.hover != id and _over_labels(Rect2(tag.position, tag.size), placed) > LABEL_CROWD * tag.size.x * tag.size.y:
			on = false
			tag.modulate.a = move_toward(tag.modulate.a, 0.0, 0.24)
			tag.visible = tag.modulate.a > 0.01
		if on:
			placed.append(Rect2(tag.position, tag.size))


## The sill's tools: no name label on them (0.8.2.7), only "put back" while one is in hand.
const UNNAMED: Array[String] = ["water", "fertiliser", "shears", "pinch", "wire", "trowel"]


## The share of a label that may lie over labels placed before it before it waits.
const LABEL_CROWD := 0.15


func _over_labels(r: Rect2, placed: Array[Rect2]) -> float:
	var area := 0.0
	for o in placed:
		area += r.intersection(o).get_area()
	return area


## Canvas pixels a thing's box is shrunk by before a label counts as lying on it (the hulls are
## grown by a finger's edge for tapping).
const LABEL_HULL_SHRINK := 12.0


## How much of the other things (and of the labels placed before) a label at `r` would cover,
## in square pixels; labels count double.
func _cover(r: Rect2, own: String, hulls: Dictionary, placed: Array[Rect2]) -> float:
	var box := PackedVector2Array([r.position, Vector2(r.end.x, r.position.y), r.end, Vector2(r.position.x, r.end.y)])
	var area := 0.0
	for id in hulls:
		if id == own:
			continue
		for poly in Geometry2D.intersect_polygons(box, hulls[id]):
			area += absf(_area(poly))
	for o in placed:
		var i := r.intersection(o)
		area += 2.0 * i.get_area()
	return area


static func _area(poly: PackedVector2Array) -> float:
	var a := 0.0
	for i in range(poly.size()):
		var p: Vector2 = poly[i]
		var q: Vector2 = poly[(i + 1) % poly.size()]
		a += p.x * q.y - q.x * p.y
	return a * 0.5


## A thing's label word.
func _label_word(id: String) -> String:
	return str(BonsaiTools.LABELS[id])


## The repotting slip lies low over the front row (nothing there is needed while the tree is out:
## a tap on the roots trims them), clear below the lifted root ball and the pot (0.8.2.2: it lay
## half over the can and the tin).
func _place_repot_slip() -> void:
	var room := _root.size
	var h := _repot_slip.get_combined_minimum_size().y
	var bottom := room.y - REPOT_SLIP_BOTTOM
	_repot_slip.offset_top = bottom - h - room.y
	_repot_slip.offset_bottom = bottom - room.y


## The pellet slip lies while the tin is open (also while it pours: a tap then gives the next
## spoon), at the screen's edge beside the tree, clear of the crown, the tin and the status
## scrap (0.8 review: it covered the crown and the tin).
func _place_pellet_slip() -> void:
	if view.pellet_kind != _shown_kind:
		_show_pellet_kind()
	var on := view.tin_open and not _sheet.visible and not view.is_lifted()
	if on and not _pellet_slip.visible:
		_show_pellet_kind()
	_pellet_slip.visible = on
	if not on:
		return
	var room := _root.size
	_pellet_slip.size = _pellet_slip.get_combined_minimum_size()
	_pellet_slip.position = pellet_slip_spot(room, _pellet_slip.size)


## Where the slip lies: the right or the left edge, below the notes at the top, whichever side
## covers least of the crown, the tin and the album card (screen rectangles of them).
func pellet_slip_spot(room: Vector2, size: Vector2) -> Vector2:
	var top := (_status.get_parent() as Control).get_global_rect().end.y + 12.0
	var avoid: Array[Rect2] = [view.crown_screen_rect()]
	var pts := view.object_screen_points()
	for id in ["fertiliser", "water", "album"]:
		if pts.has(id):
			avoid.append(Rect2(pts[id] - Vector2(70, 90), Vector2(140, 150)))
	var best := Vector2.ZERO
	var best_cost := INF
	for x in [room.x - size.x - 12.0, 12.0]:
		for y in [top, top + 60.0, top + 120.0]:
			var r := Rect2(Vector2(x, minf(y, room.y - size.y - 10.0)), size)
			var cost := 0.0
			for a in avoid:
				cost += r.intersection(a).get_area()
			if cost < best_cost - 1.0:
				best_cost = cost
				best = r.position
	return best


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


## The cuttings: each with its own ink doodle (0.8.2.2, Simon: "cuttings braucht für jede Option
## Kritzeleien"), the one on the sill named so. A tap on another asks first (0.8.2.1 review: one
## stray tap swapped the bonsai on the sill).
func _open_cuttings() -> void:
	var box := _open("Cuttings")
	_text(box, "Each finished tree on the clearing left a cutting. One bonsai stands on the sill in the light; the others rest on the shelf below and wait, just as they were.", 24)
	var choices := state.bonsai_choices(any_species.call())
	# Many cuttings (all species on the test switch): smaller doodles, so the page still holds them.
	var pic := 74 if choices.size() <= 4 else 60
	for sid in choices:
		var id: String = sid
		var sp := Species.from_id(id)
		var here := state.bonsai.species.id == id
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 10)
		box.add_child(row)
		row.add_child(_cutting_doodle(id, pic))
		if here:
			var l := _text(row, "%s  (on the sill)" % sp.display_name, 26)
			l.autowrap_mode = TextServer.AUTOWRAP_OFF
			l.size_flags_vertical = Control.SIZE_SHRINK_CENTER
			continue
		var b := Paper.ink_button(sp.display_name + ("  (resting)" if state.bonsai_resting.has(id) else "  (a new cutting)"), 25)
		b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		b.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		b.pressed.connect(func() -> void: _ask_cutting(id))
		row.add_child(b)


## The doodle of a cutting, fixed size, never taking a tap.
static func _cutting_doodle(id: String, px: int) -> TextureRect:
	var r := TextureRect.new()
	r.texture = InkSketch.texture(InkSketch.cutting_kind(id))
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	r.custom_minimum_size = Vector2(px, px)
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return r


## Asks before another cutting goes on the sill: what happens to the one there, yes or no.
func _ask_cutting(id: String) -> void:
	var sp := Species.from_id(id)
	var box := _open("The %s on the sill?" % sp.display_name)
	box.add_child(_cutting_doodle(id, 150))
	var now := state.bonsai.plant_name()
	var fresh := "from its resting place on the shelf" if state.bonsai_resting.has(id) else "as a new cutting"
	_text(box, "The %s comes to the sill %s. The %s goes to rest on the shelf, just as it is, and can come back any time." % [sp.display_name, fresh, now], 25)
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 30)
	box.add_child(row)
	var yes := Paper.ink_button("yes, swap them", 26)
	yes.add_theme_color_override("font_color", Paper.RED_INK)
	yes.pressed.connect(func() -> void:
		close_sheet()
		if state.swap_bonsai(id, any_species.call()):
			view.setup(state))
	row.add_child(yes)
	var no := Paper.ink_button("no, keep it", 26)
	no.pressed.connect(_open_cuttings)
	row.add_child(no)
