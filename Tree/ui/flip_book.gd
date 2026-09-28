class_name FlipBook
extends Control
## The album's flip-book (design doc 17.7): a tree's morning photos, one page after another at
## TimeLapse.FPS, each page turning away from the right edge toward the binding on the left to
## show the next one under it. Pages load as small copies (Photos.load_thumb), a few ahead of
## the one on show, so a month of photos never stalls the album.

## Pages are drawn inset by this much, with the binding strip on the left.
const MARGIN := 6.0
## Share of a page's time spent turning.
const TURN_SHARE := 0.55

var pages: Array[String] = []
var loop: bool = true
var playing: bool = false
var _time: float = 0.0
var _shown: int = -1
var _cache: Dictionary = {}  # path -> Texture2D
var _under: TextureRect
var _page: TextureRect
var _shadow: ColorRect
var _day: Label


func _ready() -> void:
	clip_contents = true
	_under = _rect()
	_page = _rect()
	# The turning page darkens as it lifts away from the light.
	_shadow = ColorRect.new()
	_shadow.color = Color(0.1, 0.07, 0.04, 0.0)
	_shadow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(_shadow)
	_shadow.set_anchors_preset(Control.PRESET_FULL_RECT)
	_day = Paper.ink_label("", 26, Color(0.98, 0.96, 0.9))
	_day.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.7))
	_day.add_theme_constant_override("shadow_offset_x", 2)
	_day.add_theme_constant_override("shadow_offset_y", 2)
	_day.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_day.offset_left = -150
	_day.offset_top = -48
	_day.offset_right = -14
	_day.offset_bottom = -8
	_day.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(_day)


func _rect() -> TextureRect:
	var r := TextureRect.new()
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	r.set_anchors_preset(Control.PRESET_FULL_RECT)
	r.offset_left = MARGIN
	r.offset_top = MARGIN
	r.offset_right = -MARGIN
	r.offset_bottom = -MARGIN
	add_child(r)
	return r


## Plays `paths` from the first page.
func play(paths: Array[String], p_loop: bool = true) -> void:
	pages = paths
	loop = p_loop
	_time = 0.0
	_shown = -1
	playing = not pages.is_empty()
	_show(0, 0.0)


func stop() -> void:
	playing = false
	_cache.clear()


func _texture(i: int) -> Texture2D:
	if i < 0 or i >= pages.size():
		return null
	var p := pages[i]
	if not _cache.has(p):
		_cache[p] = Photos.load_thumb(p)
	return _cache[p]


func _process(delta: float) -> void:
	if not playing or not is_visible_in_tree():
		return
	# A page only turns once the next one is loaded (the first round loads them).
	var next := TimeLapse.frame_at(_time + delta, pages.size(), loop)
	if next != _shown and not _cache.has(pages[next]):
		_texture(next)
		return
	_time += delta
	var i := TimeLapse.frame_at(_time, pages.size(), loop)
	var into := fposmod(_time * TimeLapse.FPS, 1.0)
	_show(i, into)
	# Load one page ahead per frame, so the next turn never waits.
	for k in range(1, 3):
		var j := posmod(i + k, pages.size())
		if not _cache.has(pages[j]):
			_texture(j)
			break


## Page `i` lies open; `into` (0..1) is how far its time has run: in the last part it turns
## away toward the binding, showing page i + 1 below it.
func _show(i: int, into: float) -> void:
	if pages.is_empty():
		_page.texture = null
		_under.texture = null
		_day.text = ""
		return
	if i != _shown:
		_shown = i
		_page.texture = _texture(i)
		_day.text = "day %d" % Photos.day_of(pages[i])
	var last := not loop and i == pages.size() - 1
	var turn := 0.0 if last else smoothstep(1.0 - TURN_SHARE, 1.0, into)
	_under.texture = _texture(posmod(i + 1, pages.size())) if turn > 0.0 else null
	_under.visible = turn > 0.0
	_page.pivot_offset = Vector2(0.0, _page.size.y * 0.5)
	_page.scale = Vector2(maxf(1.0 - turn, 0.001), 1.0)
	# The lifted edge rises a little and the page tips as it would between thumb and finger.
	_page.rotation = -turn * 0.06
	_shadow.color.a = turn * 0.45
