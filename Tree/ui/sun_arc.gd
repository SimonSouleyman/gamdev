class_name SunArc
extends Control
## The sun's arc as a small sky chart at the top of the screen, shown once nutrients are spent:
## sunrise on the left, sunset on the right, the glowing sun at the time of day.
## Dragging the sun along the arc moves the day on (design doc "Day length"). The real sun in
## the 3D sky can be dragged as well, but it is often above the portrait frame at noon.

signal dragged(fraction_of_day: float)

## 0..1 through the daylight part of the day.
var progress: float = 0.0
var _dragging: bool = false
var _time: float = 0.0


func _ready() -> void:
	# Only a press on the sun itself is taken; anything else passes on (hold to boost, orbit).
	mouse_filter = Control.MOUSE_FILTER_PASS


func _process(delta: float) -> void:
	_time += delta
	queue_redraw()


func _arc_point(p: float) -> Vector2:
	var w := size.x
	var h := size.y
	var a := PI * (1.0 - clampf(p, 0.0, 1.0))
	return Vector2(w * 0.5 + cos(a) * w * 0.42, h * 0.92 - sin(a) * h * 0.78)


## A page opened mid-drag: drop the drag, or it would keep moving time while paused.
func cancel_drag() -> void:
	_dragging = false


func is_dragging() -> bool:
	return _dragging


func knob_position() -> Vector2:
	return _arc_point(progress)


func _draw() -> void:
	var steps := 40
	for i in range(steps):
		if i % 2 == 0:
			draw_line(_arc_point(float(i) / steps), _arc_point(float(i + 1) / steps), Color(1, 1, 0.9, 0.7), 3.0, true)
	var k := knob_position()
	var pulse := 1.0 + 0.15 * sin(_time * 3.0)
	draw_circle(k, 34.0 * pulse, Color(1.0, 0.85, 0.4, 0.25))
	draw_circle(k, 22.0, Color(1.0, 0.92, 0.6))
	var font := Paper.hand_font(true)
	draw_string(font, _arc_point(0.0) + Vector2(-30, 34), "sunrise", HORIZONTAL_ALIGNMENT_LEFT, -1, 26, Paper.PAPER)
	draw_string(font, _arc_point(1.0) + Vector2(-40, 34), "sunset", HORIZONTAL_ALIGNMENT_LEFT, -1, 26, Paper.PAPER)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
		var m := event as InputEventMouseButton
		var was := _dragging
		_dragging = m.pressed and m.position.distance_to(knob_position()) < 70.0
		if _dragging or was:
			accept_event()
	elif event is InputEventMouseMotion and _dragging:
		var pos := (event as InputEventMouseMotion).position
		# The nearest point of the arc to the finger, but only forward in time.
		var best := progress
		var best_d := INF
		for i in range(101):
			var p := float(i) / 100.0
			var d := _arc_point(p).distance_to(pos)
			if d < best_d:
				best_d = d
				best = p
		if best > progress:
			var step := best - progress
			# Several drag events can arrive in one frame (phones sample at 120-240 Hz): count
			# each step once, or a short drag would jump far past where the finger stopped.
			progress = best
			dragged.emit(step)
		accept_event()
