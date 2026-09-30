class_name MorningOffer
extends Control
## 0.8: the save could not be read at start. Instead of starting over in silence, a torn page
## offers the newest good sunrise save the game kept in its own storage (Backup.keep_morning).
## Two answers: take that morning back, or begin anew. Nothing else asks or waits.

## true: load the offered morning; false: keep the new game.
signal answered(take_morning: bool)

var _body: Label


func _init() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	var dim := ColorRect.new()
	dim.color = Color(0.05, 0.04, 0.02, 0.45)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(dim)
	var sheet := PanelContainer.new()
	sheet.add_theme_stylebox_override("panel", Paper.paper_box(300, 300, 63, "all", 30.0))
	sheet.set_anchors_preset(Control.PRESET_CENTER)
	sheet.grow_horizontal = Control.GROW_DIRECTION_BOTH
	sheet.grow_vertical = Control.GROW_DIRECTION_BOTH
	sheet.offset_left = -300
	sheet.offset_right = 300
	sheet.offset_top = -220
	sheet.offset_bottom = 60
	sheet.rotation_degrees = 0.8
	add_child(sheet)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 16)
	sheet.add_child(box)
	var title := Paper.ink_label("My save could not be read", 36, Paper.INK, true)
	title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(title)
	_body = Paper.ink_label("", 27)
	_body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_body)
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 30)
	box.add_child(row)
	var take := Paper.ink_button("take that morning", 27)
	take.pressed.connect(func() -> void: _answer(true))
	row.add_child(take)
	var anew := Paper.ink_button("begin anew", 27)
	anew.pressed.connect(func() -> void: _answer(false))
	row.add_child(anew)
	visible = false


## Shows the offer for the sunrise save made at `made_unix` (day `day` of the `tree_name`).
func offer(made_unix: float, tree_name: String, day: int) -> void:
	_body.text = "The game kept the %s as it was on the morning of %s (day %d). I can go on from there, or begin anew." % [
		tree_name, Backup.local_date(made_unix), day]
	visible = true


func _answer(take: bool) -> void:
	visible = false
	answered.emit(take)
