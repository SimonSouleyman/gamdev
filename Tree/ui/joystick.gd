class_name VirtualJoystick
extends Control
## A virtual thumb stick. `value`: x = right, y = up, each -1..1.
## Works with touch (multi-touch, so the dive button can be held at the same time) and the mouse.

signal pressed_changed(is_pressed: bool)

@export var radius: float = 110.0
@export var knob_radius: float = 46.0

var value: Vector2 = Vector2.ZERO
var _touch_index: int = -1
var _knob: Vector2 = Vector2.ZERO


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(radius, radius) * 2.0


func is_pressed() -> bool:
	return _touch_index != -1


func _center() -> Vector2:
	return get_global_rect().get_center()


func _input(event: InputEvent) -> void:
	if not is_visible_in_tree():
		return
	if event is InputEventScreenTouch:
		var t := event as InputEventScreenTouch
		if t.pressed and _touch_index == -1 and _hit(t.position):
			_begin(t.index, t.position)
		elif not t.pressed and t.index == _touch_index:
			_end()
	elif event is InputEventScreenDrag:
		var d := event as InputEventScreenDrag
		if d.index == _touch_index:
			_move(d.position)
	elif event is InputEventMouseButton and not DisplayServer.is_touchscreen_available():
		var m := event as InputEventMouseButton
		if m.button_index != MOUSE_BUTTON_LEFT:
			return
		if m.pressed and _touch_index == -1 and _hit(m.position):
			_begin(99, m.position)
		elif not m.pressed and _touch_index == 99:
			_end()
	elif event is InputEventMouseMotion and _touch_index == 99:
		_move((event as InputEventMouseMotion).position)


func _hit(p: Vector2) -> bool:
	return p.distance_to(_center()) <= radius * 1.3


func _begin(index: int, p: Vector2) -> void:
	_touch_index = index
	_move(p)
	get_viewport().set_input_as_handled()
	pressed_changed.emit(true)


func _end() -> void:
	_touch_index = -1
	_knob = Vector2.ZERO
	value = Vector2.ZERO
	queue_redraw()
	pressed_changed.emit(false)


func _move(p: Vector2) -> void:
	_knob = (p - _center()).limit_length(radius)
	value = Vector2(_knob.x, -_knob.y) / radius
	queue_redraw()


func _draw() -> void:
	var c := size * 0.5
	draw_circle(c, radius, Color(1, 1, 1, 0.08))
	draw_arc(c, radius, 0, TAU, 48, Color(1, 1, 1, 0.35), 3.0, true)
	draw_circle(c + _knob, knob_radius, Color(1, 1, 1, 0.3 if is_pressed() else 0.18))
	draw_arc(c + _knob, knob_radius, 0, TAU, 32, Color(1, 1, 1, 0.6), 2.0, true)
