class_name Hourglass
extends Control
## A small ink hourglass by the day scrap, shown only while the player holds the screen to
## fast-forward the day (specs/fast-forward.md, 0.8.2). No number, no word: the sand runs down
## once per game hour, calmly.

## The day's clock hour (TreeView sets it); the sand follows its fraction.
var hours: float = 6.0:
	set(v):
		if absf(v - hours) > 0.001:
			hours = v
			queue_redraw()


func _init() -> void:
	custom_minimum_size = Vector2(22, 32)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	size_flags_vertical = Control.SIZE_SHRINK_CENTER


func _draw() -> void:
	var w := size.x
	var h := size.y
	var ink := Paper.INK
	var cx := w * 0.5
	var top := 3.0
	var bot := h - 3.0
	var mid := h * 0.5
	var half := w * 0.36
	# The frame: two caps and the glass's waist.
	draw_line(Vector2(cx - half - 2, top), Vector2(cx + half + 2, top), ink, 2.0, true)
	draw_line(Vector2(cx - half - 2, bot), Vector2(cx + half + 2, bot), ink, 2.0, true)
	var left := PackedVector2Array([Vector2(cx - half, top), Vector2(cx - 1.5, mid), Vector2(cx - half, bot)])
	var right := PackedVector2Array([Vector2(cx + half, top), Vector2(cx + 1.5, mid), Vector2(cx + half, bot)])
	draw_polyline(left, ink, 1.6, true)
	draw_polyline(right, ink, 1.6, true)
	# The sand: what is left above shrinks, the heap below grows; a thin thread between.
	var f := fposmod(hours, 1.0)
	var sand := Color(Paper.FAINT_INK, 0.85)
	var up := (1.0 - f) * (mid - top - 3.0)
	if up > 0.5:
		var y0 := mid - 2.0 - up
		var k := (mid - y0) / (mid - top)
		draw_colored_polygon(PackedVector2Array([Vector2(cx - half * k + 1.0, y0), Vector2(cx + half * k - 1.0, y0), Vector2(cx, mid - 1.0)]), sand)
	var heap := f * (bot - mid - 3.0)
	if heap > 0.5:
		var k2 := heap / (bot - mid)
		draw_colored_polygon(PackedVector2Array([Vector2(cx - half * k2 * 1.6, bot - 1.0), Vector2(cx + half * k2 * 1.6, bot - 1.0), Vector2(cx, bot - 1.0 - heap)]), sand)
	draw_line(Vector2(cx, mid), Vector2(cx, bot - 1.0 - heap), sand, 1.0)
