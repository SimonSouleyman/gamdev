class_name Compass
extends Control
## A small old hand compass that turns with the camera, so "grow east" and "the damp patch in
## the west" can be acted on, above and below ground. A brass case (rendered from a CC0 model,
## tools/render_icons.gd) with its dial and needle turned so north on the dial points to north
## in the world; the needle swings a little behind the dial, under a glass.

const CASE_TEX := preload("res://ui/icons/compass_case.png")
const DIAL_TEX := preload("res://ui/icons/compass_dial.png")
const NEEDLE_TEX := preload("res://ui/icons/compass_needle.png")
## The dial's centre and size as fractions of the case picture (printed by render_icons.gd).
const DIAL_CENTRE := Vector2(0.5, 0.593)
const DIAL_SIZE := 0.553
## The dial's own radius (inside the knurled rim) as a fraction of the dial picture.
const FACE := 0.96
const DIRS := {"N": Vector3(0, 0, -1), "E": Vector3(1, 0, 0), "S": Vector3(0, 0, 1), "W": Vector3(-1, 0, 0)}

var camera: Camera3D
## The needle's angle and swing (radians, radians per second); it follows the dial like a
## damped spring.
var _needle: float = 0.0
var _swing: float = 0.0
var _started: bool = false


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(96, 96)


func _process(delta: float) -> void:
	if camera == null or not camera.is_inside_tree():
		return
	var target := north_angle()
	if not _started:
		_needle = target
		_started = true
	var dt := minf(delta, 0.05)
	_swing += (wrapf(target - _needle, -PI, PI) * 60.0 - _swing * 7.0) * dt
	_needle += _swing * dt
	queue_redraw()


## Where north is on the screen, as an angle clockwise from straight up.
func north_angle() -> float:
	return north_angle_for(camera.global_basis)


static func north_angle_for(basis: Basis) -> float:
	var fwd := Vector3(-basis.z.x, 0.0, -basis.z.z)
	var right := Vector3(basis.x.x, 0.0, basis.x.z)
	if fwd.length_squared() < 1e-6:
		fwd = Vector3(basis.y.x, 0.0, basis.y.z)
	fwd = fwd.normalized()
	right = right.normalized()
	var nd: Vector3 = DIRS["N"]
	return atan2(nd.dot(right), nd.dot(fwd))


func _draw() -> void:
	if camera == null or not camera.is_inside_tree():
		return
	var side := minf(size.x, size.y)
	var box := Rect2((size - Vector2(side, side)) * 0.5, Vector2(side, side))
	draw_texture_rect(CASE_TEX, box, false)
	var c := box.position + box.size * DIAL_CENTRE
	var d := side * DIAL_SIZE
	var r := d * 0.5 * FACE
	var dial_rect := Rect2(-d * 0.5, -d * 0.5, d, d)
	# The dial turns with the world; the needle swings over it, its shadow a little below.
	draw_set_transform(c, north_angle(), Vector2.ONE)
	draw_texture_rect(DIAL_TEX, dial_rect, false)
	draw_set_transform(c + Vector2(1.5, 2.5), _needle, Vector2.ONE)
	draw_texture_rect(NEEDLE_TEX, dial_rect, false, Color(0.1, 0.06, 0.02, 0.35))
	draw_set_transform(c, _needle, Vector2.ONE)
	draw_texture_rect(NEEDLE_TEX, dial_rect, false)
	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)
	# The glass: a darker edge where the rim shades it, a soft sheen and a bright glint.
	draw_arc(c, r * 0.97, 0.0, TAU, 64, Color(0.08, 0.05, 0.02, 0.28), r * 0.07, true)
	for i in range(4):
		var t := float(i) / 3.0
		var col := Color(1.0, 0.98, 0.9, 0.07 + t * 0.05)
		draw_arc(c, r * (0.78 - t * 0.05), deg_to_rad(195 + t * 6), deg_to_rad(265 - t * 6), 24, col, r * (0.2 - t * 0.12), true)
	draw_circle(c + Vector2(-0.42, -0.5) * r, r * 0.07, Color(1, 1, 0.95, 0.45), true, -1.0, true)
