extends Control
## Look-dev board for the paper UI: an open journal (double page on leather), a page torn out
## of a squared notebook, and HUD scraps, all with PaperLook. Built in code so it stays in step
## with paper_look.gd. `view` picks what to show: "board" (all of it), "book" (the portrait
## journal page as the game shows it), "page" (the torn-out page at full size).

@export var view: String = "board"

const INK := Color(0.17, 0.13, 0.1)
const FAINT := Color(0.38, 0.3, 0.22)
const RED := Color(0.55, 0.16, 0.1)


func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	_backdrop()
	match view:
		"empty":
			pass
		"book":
			_single_book()
		"page":
			_torn_page(Vector2(50, 360), Vector2(620, 460), -1.5, 44)
			_hint_strip(Vector2(50, 1050), 620.0)
		_:
			_double_page(Rect2(14, 24, 692, 560))
			_torn_page(Vector2(46, 640), Vector2(600, 360), -2.2, 40)
			_scrap(Vector2(34, 1080), "life force 20", Color(0.83, 0.74, 0.45), 3, 1.5)
			_scrap(Vector2(300, 1100), "water 0.4", Color(0.28, 0.5, 0.85), 4, -2.0)
			_scrap(Vector2(492, 1074), "N 1.2", Color(0.33, 0.66, 0.35), 5, 0.8)
	PaperLook.ink_all(self)


## A blurred, darkened meadow, as the game dims the scene behind the journal.
func _backdrop() -> void:
	var bg := TextureRect.new()
	var gt := GradientTexture2D.new()
	var g := Gradient.new()
	g.set_color(0, Color(0.16, 0.2, 0.12))
	g.set_color(1, Color(0.2, 0.26, 0.1))
	g.add_point(0.45, Color(0.12, 0.14, 0.09))
	gt.gradient = g
	gt.fill_from = Vector2(0, 0)
	gt.fill_to = Vector2(0, 1)
	bg.texture = gt
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.stretch_mode = TextureRect.STRETCH_SCALE
	add_child(bg)


func _label(text: String, size: int, color: Color = INK, bold: bool = false) -> Label:
	var l := Label.new()
	l.text = text
	var f: Font = load("res://ui/fonts/Caveat-Regular.ttf" if bold else "res://ui/fonts/PatrickHand-Regular.ttf")
	l.add_theme_font_override("font", f)
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return l


func _panel(pos: Vector2, size: Vector2, angle: float) -> PanelContainer:
	var p := PanelContainer.new()
	p.position = pos
	p.size = size
	p.custom_minimum_size = size
	p.pivot_offset = size * 0.5
	p.rotation_degrees = angle
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(p)
	return p


func _double_page(r: Rect2) -> void:
	var cover := Panel.new()
	cover.position = r.position
	cover.size = r.size
	add_child(cover)
	PaperLook.apply_leather(cover, r.size.x * 0.5)
	var half := (r.size.x - 36.0) * 0.5
	var top := r.position.y + 16.0
	var h := r.size.y - 32.0
	# Page block edges peeking out under the open pages.
	for i in range(3):
		var e := ColorRect.new()
		e.color = Color(0.82, 0.76, 0.62).darkened(0.07 * i)
		e.position = Vector2(r.position.x + 16.0 - 2.0 * i, top + 3.0 + 2.0 * i)
		e.size = Vector2(r.size.x - 32.0 + 4.0 * i, h - 2.0)
		e.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(e)
		move_child(e, cover.get_index() + 1)
	var left := _panel(Vector2(r.position.x + 18.0, top), Vector2(half, h), 0.0)
	PaperLook.apply(left, "book_page", 11, 26.0, {"gutter": 1.0, "rule_offset": Vector2(-100.0, 96.0), "rule_step": 30.0})
	var right := _panel(Vector2(r.position.x + 18.0 + half, top), Vector2(half, h), 0.0)
	PaperLook.apply(right, "book_page", 12, 26.0, {"gutter": -1.0, "rule_offset": Vector2(-100.0, 96.0), "rule_step": 30.0})

	var lb := VBoxContainer.new()
	lb.add_theme_constant_override("separation", 0)
	left.add_child(lb)
	lb.add_child(_label("My linden", 44, INK, true))
	lb.add_child(_label("Day 0", 26, INK, true))
	var body := _label("I planted a linden seed in the clearing as the sun went down.", 21)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lb.add_child(body)
	lb.add_child(_label("Day 1", 26, INK, true))
	var b2 := _label("The first root found water under the rushes.", 21)
	b2.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lb.add_child(b2)
	var mine := _label("it smelled of rain all night", 22, RED)
	mine.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lb.add_child(mine)

	# Right page: a pressed leaf taped in, with a caption.
	var rb := Control.new()
	rb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	right.add_child(rb)
	var leaf := PaperLook.pressed_leaf(220.0, -18.0)
	leaf.position = Vector2(40, 40)
	rb.add_child(leaf)
	for t in [[Vector2(78, 70), -38.0, 21], [Vector2(170, 252), 22.0, 22]]:
		var tape := PanelContainer.new()
		tape.size = Vector2(78, 26)
		tape.position = t[0]
		tape.pivot_offset = tape.size * 0.5
		tape.rotation_degrees = t[1]
		tape.mouse_filter = Control.MOUSE_FILTER_IGNORE
		rb.add_child(tape)
		PaperLook.apply(tape, "tape", t[2], 0.0)
	var cap := _label("Tilia cordata,\nfrom the old tree", 24, FAINT, true)
	cap.position = Vector2(70, 330)
	cap.rotation_degrees = -3.0
	rb.add_child(cap)

	# The ribbon bookmark lying across the pages.
	var ribbon := Control.new()
	ribbon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ribbon.position = Vector2(r.position.x + r.size.x * 0.5 + 40.0, top - 22.0)
	ribbon.size = Vector2(26, h + 50.0)
	ribbon.draw.connect(func() -> void: _draw_ribbon(ribbon))
	add_child(ribbon)


func _draw_ribbon(c: Control) -> void:
	var col := Color(0.55, 0.14, 0.12)
	var w := 20.0
	var h := c.size.y
	var pts := PackedVector2Array()
	var pts_r := PackedVector2Array()
	for i in range(21):
		var y := h * i / 20.0
		var x := sin(i * 0.35) * 5.0 + i * 0.8
		pts.append(Vector2(x, y))
		pts_r.append(Vector2(x + w, y))
	# Shadow first.
	var shadow := PackedVector2Array()
	for p in pts:
		shadow.append(p + Vector2(3, 5))
	for i in range(pts_r.size() - 1, -1, -1):
		shadow.append(pts_r[i] + Vector2(3, 5))
	c.draw_colored_polygon(shadow, Color(0, 0, 0, 0.3))
	for i in range(20):
		var quad := PackedVector2Array([pts[i], pts_r[i], pts_r[i + 1], pts[i + 1]])
		var shade := 0.85 + 0.2 * sin(i * 0.35 + 1.2)
		c.draw_colored_polygon(quad, Color(col.r * shade, col.g * shade, col.b * shade))
	# Woven cloth: faint lengthwise threads, and a frayed V cut at the end.
	for k in range(1, 6):
		c.draw_polyline(_offset(pts, Vector2(w * k / 6.0, 0)), Color(1, 0.8, 0.7, 0.08), 1.0)


func _offset(pts: PackedVector2Array, o: Vector2) -> PackedVector2Array:
	var out := PackedVector2Array()
	for p in pts:
		out.append(p + o)
	return out


func _single_book() -> void:
	var cover := Panel.new()
	cover.position = Vector2(8, 22)
	cover.size = Vector2(672, 1240)
	add_child(cover)
	PaperLook.apply_leather(cover)
	var page := _panel(Vector2(40, 56), Vector2(610, 1166), 0.0)
	PaperLook.apply(page, "book_page", 11, 34.0, {"gutter": -1.0})
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 6)
	page.add_child(box)
	box.add_child(_label("My linden", 50, INK, true))
	box.add_child(_label("Day 0", 30, INK, true))
	var body := _label("I planted a linden seed in the clearing as the sun went down.", 25)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(body)
	box.add_child(_label("Day 1", 30, INK, true))
	var b2 := _label("The first root drank from a damp patch under the rushes. At sunrise the seed used it all to grow.", 25)
	b2.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(b2)
	box.add_child(_label("it smelled of rain all night", 25, RED))
	var leaf := PaperLook.pressed_leaf(260.0, 14.0)
	leaf.position = Vector2(340, 720)
	add_child(leaf)


func _torn_page(pos: Vector2, size: Vector2, angle: float, seed: int) -> void:
	var p := _panel(pos, size, angle)
	PaperLook.apply(p, "torn_page", seed, 34.0)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 8)
	p.add_child(box)
	box.add_child(_label("Below the meadow", 44, INK, true))
	var body := _label("Down here it is dark, and the soil glows: blue is water, green nitrogen, orange phosphorus, violet potassium.", 27)
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(body)
	var f := _label("tap to turn the page", 22, FAINT)
	f.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	box.add_child(f)
	# A paper clip holding it by the top edge.
	var clip := Control.new()
	clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	clip.position = pos + Vector2(size.x - 150.0, -14.0)
	clip.size = Vector2(40, 100)
	clip.rotation_degrees = angle + 4.0
	clip.draw.connect(func() -> void: _draw_clip(clip))
	add_child(clip)


## A steel paper clip: a wire of three nested turns, with a shadow and a highlight.
func _draw_clip(c: Control) -> void:
	var pts := PackedVector2Array()
	var loops := [[Vector2(20, 18), 14.0, 78.0], [Vector2(20, 22), 9.0, 58.0], [Vector2(20, 14), 5.0, 44.0]]
	# Outer U up, middle U down, inner U up: sample each as a stadium half.
	pts.append(Vector2(34, 18 + 70))
	pts.append(Vector2(34, 18))
	for i in range(13):
		var a := PI * i / 12.0
		pts.append(Vector2(20, 18) + Vector2(cos(a) * 14.0, -sin(a) * 14.0))
	pts.append(Vector2(6, 88))
	for i in range(13):
		var a := PI + PI * i / 12.0
		pts.append(Vector2(15, 88) + Vector2(cos(a) * 9.0, -sin(a) * 9.0))
	pts.append(Vector2(24, 30))
	for i in range(13):
		var a := PI * i / 12.0
		pts.append(Vector2(19, 30) + Vector2(cos(a) * 5.0, -sin(a) * 5.0))
	pts.append(Vector2(14, 70))
	var shadow := PackedVector2Array()
	for p in pts:
		shadow.append(p + Vector2(2.5, 4.0))
	c.draw_polyline(shadow, Color(0, 0, 0, 0.28), 3.5, true)
	c.draw_polyline(pts, Color(0.42, 0.44, 0.46), 3.0, true)
	c.draw_polyline(_offset(pts, Vector2(-0.6, -0.6)), Color(0.86, 0.88, 0.9, 0.8), 1.0, true)


func _scrap(pos: Vector2, text: String, dot: Color, seed: int, angle: float) -> void:
	var p := _panel(pos, Vector2(10, 10), angle)
	p.custom_minimum_size = Vector2.ZERO
	PaperLook.apply(p, "scrap", seed, 12.0)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	p.add_child(row)
	var d := Control.new()
	d.custom_minimum_size = Vector2(18, 18)
	d.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	d.draw.connect(func() -> void:
		# A dab of watercolour: darker rim where the pigment dried.
		d.draw_circle(Vector2(9, 9), 8.0, Color(dot, 0.75))
		d.draw_arc(Vector2(9, 9), 7.5, 0.0, TAU, 20, Color(dot.darkened(0.3), 0.6), 1.5, true))
	row.add_child(d)
	row.add_child(_label(text, 28, INK, true))


func _hint_strip(pos: Vector2, width: float) -> void:
	var p := _panel(pos, Vector2(width, 10), -0.8)
	p.custom_minimum_size = Vector2(width, 0)
	PaperLook.apply(p, "strip", 61, 22.0)
	var l := _label("The sun has set. Tap the ground to follow the roots down.", 26, INK, true)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	p.add_child(l)
