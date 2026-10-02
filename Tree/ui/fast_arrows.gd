class_name FastArrows
extends Control
## A small ink double arrow (») by the day scrap, shown only while the day runs fast (held, or the
## sunset picture's run; specs/fast-forward.md). No number, no word. It pulses gently, once per
## PULSE_SECONDS of real time. 0.8.2.5: it replaces the small hourglass, since the sunset picture
## is now a walnut hourglass and the two read alike.
const PULSE_SECONDS := 1.1

## Real seconds since it last showed (the pulse's phase).
var _t := 0.0


func _init() -> void:
	custom_minimum_size = Vector2(40, 42)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	size_flags_vertical = Control.SIZE_SHRINK_CENTER
	visibility_changed.connect(func() -> void: _t = 0.0)


func _process(delta: float) -> void:
	if not visible:
		return
	_t += delta
	queue_redraw()


## The pulse, 0 to 1 and back once per PULSE_SECONDS.
func pulse() -> float:
	return 0.5 - 0.5 * cos(TAU * _t / PULSE_SECONDS)


func _draw() -> void:
	var p := pulse()
	var ink := Color(Paper.INK, lerpf(0.55, 1.0, p))
	var h := size.y
	var mid := h * 0.5
	var arm := h * 0.26 * lerpf(0.94, 1.06, p)
	var lean := arm * 0.95
	# Two chevrons, the second a little ahead; hand-drawn: each a slightly bowed stroke.
	var x0 := size.x * 0.5 - lean - 3.0 + p * 1.5
	for i in range(2):
		var x := x0 + i * (lean * 0.9 + 1.5)
		var tip := Vector2(x + lean, mid + 0.4 * (i - 0.5))
		var a := Vector2(x, mid - arm)
		var b := Vector2(x + 0.6, mid + arm)
		draw_polyline(PackedVector2Array([a, (a + tip) * 0.5 + Vector2(0.8, -0.6), tip]), ink, 2.4, true)
		draw_polyline(PackedVector2Array([tip, (tip + b) * 0.5 + Vector2(0.6, 0.7), b]), ink, 2.4, true)
