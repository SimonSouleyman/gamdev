class_name BonsaiHud
extends CanvasLayer
## Bonsai mode's paper (design doc section 16): a torn scrap with how the bonsai is (care day,
## the soil's water and N, P, K, the pot, a repotting due), the tools as words circled in ink
## (watering can, fertiliser, turning the pot, shears, pinching, wire), and the pages: the
## repotting steps, the style pages (drawings of the classic styles, for inspiration only), the
## bonsai's album page (milestones and an ink sketch) and the cuttings on the shelf.

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
	"": "Drag to look round the pot, scroll or pinch to come closer.",
	"shears": "Touch a branch: the mark shows the cut, the outline what falls. Lift to cut (a third at most).",
	"pinch": "Tap a fresh tip (grown today or yesterday) to pinch it: the buds behind it fill in.",
	"wire": "Touch a branch and drag it into its new line; the copper holds it. Tap a wired branch to take the wire off.",
}

var view: BonsaiView
var state: GameState
## Whether the test switch "any species now" is on (all cuttings).
var any_species: Callable = func() -> bool: return false

var _root: Control
var _status: Label
var _soil: Label
var _hint: Label
var _tools: Dictionary = {}
var _repot_button: Button
var _cuttings_button: Button
var _sheet: Control
var _sheet_box: VBoxContainer
var _style_index: int = 0


func _ready() -> void:
	layer = 17
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_root)
	_build_status()
	_build_tools()
	_build_sheet()
	visible = false


func _build_status() -> void:
	var scrap := PanelContainer.new()
	PaperLook.apply(scrap, "strip", 171, 18.0)
	scrap.set_anchors_preset(Control.PRESET_TOP_WIDE)
	scrap.offset_left = 26
	scrap.offset_right = -26
	scrap.offset_top = 26
	scrap.rotation_degrees = -0.6
	scrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(scrap)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 2)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	scrap.add_child(box)
	_status = Paper.ink_label("", 30, Paper.INK, true)
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_status)
	_soil = Paper.ink_label("", 25)
	_soil.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_soil)
	_hint = Paper.ink_label("", 22, Paper.FAINT_INK)
	_hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_hint)


func _build_tools() -> void:
	var strip := PanelContainer.new()
	PaperLook.apply(strip, "strip", 172, 16.0)
	strip.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	strip.offset_left = 18
	strip.offset_right = -18
	strip.offset_top = -270
	strip.offset_bottom = -24
	strip.rotation_degrees = 0.4
	_root.add_child(strip)
	var rows := VBoxContainer.new()
	rows.add_theme_constant_override("separation", 10)
	strip.add_child(rows)
	var r1 := _row(rows)
	_button(r1, "water", func() -> void: view.water())
	_button(r1, "fertiliser", _open_fertiliser)
	_button(r1, "< turn", func() -> void: view.turn_pot(-1))
	_button(r1, "turn >", func() -> void: view.turn_pot(1))
	var r2 := _row(rows)
	for t in ["shears", "pinch", "wire"]:
		var tool_name: String = t
		_tools[tool_name] = _button(r2, tool_name, func() -> void: _toggle_tool(tool_name))
	_repot_button = _button(r2, "repot", _open_repot)
	var r3 := _row(rows)
	_button(r3, "styles", _open_styles)
	_button(r3, "album page", _open_album)
	_cuttings_button = _button(r3, "cuttings", _open_cuttings)
	_button(r3, "back", func() -> void: back_pressed.emit())


func _row(parent: Control) -> HBoxContainer:
	var r := HBoxContainer.new()
	r.alignment = BoxContainer.ALIGNMENT_CENTER
	r.add_theme_constant_override("separation", 10)
	parent.add_child(r)
	return r


func _button(parent: Control, text: String, action: Callable) -> Button:
	# The strip holds three rows: a little under the full tap size (Paper.INK_TAP) to fit.
	var b := Paper.ink_button(text, 27, 60.0)
	b.pressed.connect(func() -> void:
		if view != null and not view.busy:
			action.call())
	parent.add_child(b)
	return b


func _toggle_tool(t: String) -> void:
	view.set_tool("" if view.tool == t else t)
	if view.tool != "":
		first_page.call(view.tool)


## Set by main: shows the first-time journal page for a tool ("shears", "pinch", "wire").
var first_page: Callable = func(_id: String) -> void: pass


func show_hud(on: bool) -> void:
	visible = on
	if not on:
		close_sheet()


func is_busy() -> bool:
	return _sheet.visible


func _process(_d: float) -> void:
	if not visible or state == null or state.bonsai == null:
		return
	var b := state.bonsai
	_status.text = "My %s bonsai, care day %d" % [b.plant_name(), b.day()]
	var water := "the soil is dry, the leaves droop" if b.droop() > 0.0 else ("the soil is wet, it grows slowly" if b.is_too_wet() else "the soil is damp")
	var words: Array[String] = []
	var names: Array[String] = ["N", "P", "K"]
	for k in range(3):
		var s := b.soil[k]
		words.append("%s %s" % [names[k], "low" if s < 0.2 else ("plenty" if s > BonsaiSim.BURN_LEVEL else "ok")])
	var pot := "in the %s" % b.pot_name()
	if b.repot_due:
		pot += ", asking to be repotted"
	_soil.text = "%s; %s. %s." % [water, ", ".join(words), pot]
	_hint.text = TOOL_HINTS.get(view.tool, "")
	for t in _tools:
		var on: bool = view.tool == t
		(_tools[t] as Button).modulate = Color(1.0, 0.55, 0.35) if on else Color.WHITE
	_repot_button.visible = b.repot_due or view.is_lifted()
	_cuttings_button.visible = state.bonsai_choices(any_species.call()).size() > 1


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


func _open_fertiliser() -> void:
	var box := _open("Fertiliser")
	_text(box, "A spoon of pellets on the soil. Which one? Too much of one burns a few leaf tips.")
	var names: Array[String] = ["nitrogen (leaves, shoots)", "phosphorus (roots, buds)", "potassium (wood, health)"]
	for k in range(3):
		var kind := k
		var b := Paper.ink_button(names[k], 28)
		b.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		b.add_theme_color_override("font_color", Resources.KIND_COLORS[k + 1].darkened(0.45))
		b.pressed.connect(func() -> void:
			close_sheet()
			view.fertilise(kind))
		box.add_child(b)


## Repotting in four steps: lift it out, trim the roots, pick the pot, fresh soil.
func _open_repot() -> void:
	var box := _open("Repotting")
	if not view.is_lifted():
		_text(box, "The roots fill the pot. First lift the tree out with its root ball.")
		var lift := Paper.ink_button("lift it out", 28)
		lift.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		lift.pressed.connect(func() -> void:
			view.repot_lift()
			first_page.call("repot")
			_open_repot())
		box.add_child(lift)
		return
	_text(box, "Trim the long circling roots with the shears (%d%% cut so far), pick a pot, then fresh soil." % int(view.trim_share() * 100.0))
	var trim := Paper.ink_button("snip the roots", 28)
	trim.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	trim.pressed.connect(func() -> void:
		view.repot_trim()
		_open_repot())
	box.add_child(trim)
	_text(box, "The pot:", 26)
	var pots := HFlowContainer.new()
	pots.add_theme_constant_override("h_separation", 10)
	pots.add_theme_constant_override("v_separation", 8)
	box.add_child(pots)
	for pid in BonsaiSim.POT_ORDER:
		var id: String = pid
		var b := Paper.ink_button("%s (%d)" % [BonsaiSim.POTS[id]["name"], int(BonsaiSim.POTS[id]["nodes"])], 23)
		b.pressed.connect(func() -> void:
			view.repot_pick(id)
			_open_repot())
		pots.add_child(b)
	var done := Paper.ink_button("fresh soil, and in", 30)
	done.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	done.pressed.connect(func() -> void:
		close_sheet()
		view.repot_finish())
	box.add_child(done)
	_text(box, "(The number is how many green twigs the pot can carry.)", 22)


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
