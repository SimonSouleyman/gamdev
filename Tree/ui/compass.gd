class_name Compass
extends Control
## A small compass that turns with the camera, so "grow east" and "the damp patch in the west"
## can be acted on, above and below ground.

var camera: Camera3D
const DIRS := {"N": Vector3(0, 0, -1), "E": Vector3(1, 0, 0), "S": Vector3(0, 0, 1), "W": Vector3(-1, 0, 0)}


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(96, 96)


func _process(_delta: float) -> void:
	queue_redraw()


func _draw() -> void:
	if camera == null or not camera.is_inside_tree():
		return
	var c := size * 0.5
	var r := minf(size.x, size.y) * 0.3
	# An ink compass on a round scrap of paper.
	draw_circle(c, r + 16.0, Color(0.2, 0.15, 0.1, 0.25))
	draw_circle(c, r + 14.0, Paper.PAPER)
	draw_arc(c, r + 10.0, 0.0, TAU, 40, Color(Paper.INK, 0.6), 1.5, true)
	var basis := camera.global_basis
	var fwd := Vector3(-basis.z.x, 0.0, -basis.z.z)
	var right := Vector3(basis.x.x, 0.0, basis.x.z)
	if fwd.length_squared() < 1e-6:
		fwd = Vector3(basis.y.x, 0.0, basis.y.z)
	fwd = fwd.normalized()
	right = right.normalized()
	var font := Paper.hand_font(true)
	for k in DIRS:
		var d: Vector3 = DIRS[k]
		var p := c + Vector2(d.dot(right), -d.dot(fwd)) * r
		var col := Paper.RED_INK if k == "N" else Paper.INK
		draw_string(font, p + Vector2(-8, 9), k, HORIZONTAL_ALIGNMENT_LEFT, -1, 26, col)
	# The needle: north in red ink.
	var nd: Vector3 = DIRS["N"]
	var np := Vector2(nd.dot(right), -nd.dot(fwd))
	draw_line(c - np * r * 0.45, c, Paper.INK, 2.0, true)
	draw_line(c, c + np * r * 0.45, Paper.RED_INK, 3.0, true)
