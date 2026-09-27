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
	var r := minf(size.x, size.y) * 0.36
	draw_circle(c, r + 14.0, Color(0.98, 0.96, 0.9, 0.55))
	var basis := camera.global_basis
	var fwd := Vector3(-basis.z.x, 0.0, -basis.z.z)
	var right := Vector3(basis.x.x, 0.0, basis.x.z)
	if fwd.length_squared() < 1e-6:
		fwd = Vector3(basis.y.x, 0.0, basis.y.z)
	fwd = fwd.normalized()
	right = right.normalized()
	var font := get_theme_default_font()
	for k in DIRS:
		var d: Vector3 = DIRS[k]
		var p := c + Vector2(d.dot(right), -d.dot(fwd)) * r
		var col := Color(0.7, 0.15, 0.1) if k == "N" else Color(0.25, 0.2, 0.15)
		draw_string(font, p + Vector2(-8, 8), k, HORIZONTAL_ALIGNMENT_LEFT, -1, 22, col)
