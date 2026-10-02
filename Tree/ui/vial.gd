class_name Vial
extends Control
## Life force as a round glass vial with green liquid (0.8.2.5, specs/wish-compass-vial.md item 4,
## Simon: "wie eine Manaphiole aus Rollenspielen wie Diablo"). No number anywhere: by day the
## liquid rises as the leaves gather life force, slower while a boost runs, and a pencil mark on
## the glass shows how high a calm day would have filled it by now (the gap is the boost's cost).
## By night it falls as the root grows; what is left is the leftover for small roots.
##
## The glass, liquid, cork and pencil mark are painted by ui/vial.gdshader (one pass, so the
## Compatibility renderer draws it); the paper tag on its twine is drawn here. The liquid follows
## its level on a damped spring, so it sloshes a little when it moves and then settles.

const SHADER := preload("res://ui/vial.gdshader")
## The painted rect's height per width (ui/vial.gdshader's units).
const ASPECT := 1.3
## Below this gap (share of the vial) the pencil mark stays hidden: a calm day has none.
const MARK_MIN_GAP := 0.012

## The vial's full mark for the night: the calm day's projected fill at sunset (set by the tree
## view's vial, read by the root view's). 0 when unknown (a game loaded at night).
static var day_capacity: float = 0.0

## 0..1 targets; the shown values follow them.
var level: float = 0.0
var mark: float = 0.0
var mark_visible: bool = false
## Underground: the liquid glows a little more.
var night: bool = false

var _glass: ColorRect
var _mat: ShaderMaterial
var _tag: Control
var _shown: float = -1.0
var _speed: float = 0.0
var _tilt: float = 0.0
var _tilt_speed: float = 0.0
var _mark_shown: float = -1.0
var _mark_alpha: float = 0.0
var _time: float = 0.0

# Day tracking (track_day): what a calm day would have gathered by now.
var _calm: float = 0.0
var _last_lf: float = -1.0
var _last_t: float = -1.0
var _last_day: int = -1
var _last_boost: bool = false
var _leaf_rate: float = 0.0
var _leaf_rate_age: float = 99.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_mat = ShaderMaterial.new()
	_mat.shader = SHADER
	_glass = ColorRect.new()
	_glass.color = Color.WHITE
	_glass.material = _mat
	_glass.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_glass)
	_tag = Control.new()
	_tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_tag.draw.connect(_draw_tag)
	add_child(_tag)
	resized.connect(_layout)
	_layout()


func _layout() -> void:
	# The painted vial keeps its shape, centred on the left of the box; the tag hangs to the right.
	var w := minf(size.x / 1.1, size.y / ASPECT)
	_glass.position = Vector2(0.0, (size.y - w * ASPECT) * 0.5)
	_glass.size = Vector2(w, w * ASPECT)
	_mat.set_shader_parameter("aspect", ASPECT)
	_tag.position = Vector2.ZERO
	_tag.size = size
	_tag.queue_redraw()


func _process(delta: float) -> void:
	var dt := minf(delta, 0.05)
	_time += dt
	if _shown < 0.0:
		_shown = level
		_mark_shown = mark
	# The level on a soft spring: it lags, overshoots a hair and settles.
	var prev_speed := _speed
	_speed += ((level - _shown) * 26.0 - _speed * 8.5) * dt
	_shown = clampf(_shown + _speed * dt, 0.0, 1.0)
	# The surface tilts against the push and swings back to level; a faint breath keeps it alive.
	var push := (_speed - prev_speed) / maxf(dt, 1e-3)
	_tilt_speed += (-_tilt * 30.0 - _tilt_speed * 3.2 - clampf(push, -3.0, 3.0) * 0.6) * dt
	_tilt = clampf(_tilt + _tilt_speed * dt, -0.12, 0.12)
	_mark_shown = lerpf(_mark_shown, mark, 1.0 - exp(-dt * 4.0))
	_mark_alpha = move_toward(_mark_alpha, 1.0 if mark_visible else 0.0, dt * 1.5)
	_mat.set_shader_parameter("level", _shown)
	_mat.set_shader_parameter("mark", _mark_shown)
	_mat.set_shader_parameter("mark_alpha", _mark_alpha)
	_mat.set_shader_parameter("tilt", _tilt + 0.006 * sin(_time * 0.9))
	_mat.set_shader_parameter("wave", 0.0035 + minf(absf(_speed) * 0.05 + absf(_tilt_speed) * 0.02, 0.012))
	_mat.set_shader_parameter("time", _time)
	_mat.set_shader_parameter("night", 1.0 if night else 0.0)


## Jumps to the current level without sloshing (a new view, a load).
func settle() -> void:
	_shown = level
	_speed = 0.0
	_tilt = 0.0
	_tilt_speed = 0.0
	_mark_shown = mark


## The level the liquid shows now (for tests and shots).
func shown_level() -> float:
	return maxf(_shown, 0.0)


## By day: the vial fills toward a calm day's sunset; the mark shows a calm day's fill so far.
func track_day(state: GameState) -> void:
	var sim := state.sim
	var clock := sim.clock
	var lf := sim.resources.life_force
	var t := minf(clock.time_of_day, clock.daylight_fraction)
	_leaf_rate_age += get_process_delta_time()
	if _leaf_rate_age > 0.5 or _last_day != clock.day_count:
		_leaf_rate_age = 0.0
		_leaf_rate = calm_rate(sim)
	if _last_day != clock.day_count or _last_lf < 0.0 or t < _last_t or lf < _last_lf - 1e-4:
		# A new day, a load or a jump back: the mark starts where the liquid is.
		_calm = lf
	elif clock.boost_active or _last_boost:
		_calm += _leaf_rate * light_seconds(clock, _last_t, t)
	else:
		_calm += lf - _last_lf
	_calm = maxf(_calm, lf)
	_last_day = clock.day_count
	_last_boost = clock.boost_active
	_last_lf = lf
	_last_t = t
	var rest := _leaf_rate * light_seconds(clock, t, clock.daylight_fraction) if state.phase == GameState.Phase.DAY else 0.0
	var cap := maxf(_calm + rest, 1.0)
	day_capacity = cap
	night = false
	level = clampf(lf / cap, 0.0, 1.0)
	mark = clampf(_calm / cap, 0.0, 1.0)
	mark_visible = mark - level > MARK_MIN_GAP


## By night: the liquid falls from tonight's start as the root spends it.
func track_night(life_force: float, at_start: float) -> void:
	var full := maxf(at_start, 0.001)
	if day_capacity >= at_start:
		full = day_capacity
	night = true
	level = clampf(life_force / full, 0.0, 1.0)
	mark_visible = false


## Life force per second of full sun on a calm hour (the sim's own sum without the boost).
static func calm_rate(sim: GrowthSim) -> float:
	return sim.effective_leaves() * sim.life_force_per_tip * sim.species.life_force_factor(sim.clock.day_count, false) * sim.young_leaf_bonus()


## Seconds of full sun between two times of day (the sun's arc is a sine over the daylight).
static func light_seconds(clock: DayCycle, t0: float, t1: float) -> float:
	var df := clock.daylight_fraction
	var a := clampf(t0, 0.0, df)
	var b := clampf(t1, 0.0, df)
	if b <= a:
		return 0.0
	return clock.seconds_per_day * df / PI * (cos(PI * a / df) - cos(PI * b / df))


# --- the tag ---------------------------------------------------------------------------------

## A small paper tag tied to the neck with the twine, a leaf drawn on it in ink (no number).
func _draw_tag() -> void:
	var w := _glass.size.x
	var o := _glass.position
	var knot := o + Vector2(0.58, 0.335) * w
	var tag_c := o + Vector2(0.90, 0.47) * w
	var tw := w * 0.30
	var th := w * 0.20
	var rot := deg_to_rad(16.0)
	var x := Transform2D(rot, tag_c)
	# The twine from the knot to the tag's hole.
	var hole := x * Vector2(-tw * 0.36, 0.0)
	var mid := (knot + hole) * 0.5 + Vector2(0.0, w * 0.05)
	_tag.draw_polyline(PackedVector2Array([knot, mid, hole]), Color(0.42, 0.31, 0.19), maxf(1.2, w * 0.016), true)
	# The tag: a pointed paper label with a soft shadow.
	var pts := PackedVector2Array([
		Vector2(-tw * 0.5, 0.0), Vector2(-tw * 0.3, -th * 0.5), Vector2(tw * 0.5, -th * 0.5),
		Vector2(tw * 0.5, th * 0.5), Vector2(-tw * 0.3, th * 0.5)])
	var shadow := PackedVector2Array()
	var paper := PackedVector2Array()
	for pt in pts:
		shadow.append(x * pt + Vector2(w * 0.015, w * 0.022))
		paper.append(x * pt)
	_tag.draw_colored_polygon(shadow, Color(0.05, 0.03, 0.02, 0.28))
	_tag.draw_colored_polygon(paper, Paper.PAPER.darkened(0.04))
	var edge := paper.duplicate()
	edge.append(paper[0])
	_tag.draw_polyline(edge, Color(Paper.INK, 0.55), maxf(1.0, w * 0.01), true)
	_tag.draw_circle(hole, w * 0.017, Color(Paper.INK, 0.7))
	# A small leaf in ink: life.
	var leaf := PackedVector2Array()
	var lw := tw * 0.3
	var lh := th * 0.28
	for i in range(13):
		var a := float(i) / 12.0 * TAU
		var pt := Vector2(cos(a) * lw, sin(a) * lh * (1.0 - 0.35 * cos(a)))
		leaf.append(x * (Vector2(tw * 0.1, 0.0) + pt.rotated(-0.35)))
	_tag.draw_colored_polygon(leaf, Color(0.22, 0.42, 0.16, 0.85))
	_tag.draw_polyline(leaf, Color(Paper.INK, 0.8), maxf(1.0, w * 0.009), true)
	var vein_a := x * (Vector2(tw * 0.1, 0.0) + Vector2(-lw * 1.15, 0.0).rotated(-0.35))
	var vein_b := x * (Vector2(tw * 0.1, 0.0) + Vector2(lw * 0.8, 0.0).rotated(-0.35))
	_tag.draw_line(vein_a, vein_b, Color(Paper.INK, 0.8), maxf(1.0, w * 0.009), true)
