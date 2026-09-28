class_name PaperNote
extends PanelContainer
## A short hint written on a torn strip of paper. Hidden while it has nothing to say.

var label: Label

var text: String = "":
	set(value):
		if value == text:
			return
		text = value
		if label:
			label.text = value
		visible = value != ""


func _init(font_size: int = 28, seed: int = 60) -> void:
	PaperLook.apply(self, "strip", seed, 18.0)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	label = Paper.ink_label("", font_size, Paper.INK, true)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(label)
	visible = false
	rotation_degrees = -0.8
