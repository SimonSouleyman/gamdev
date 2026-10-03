## Stage field: background colors and scrolling scenery (port of field.d).
## The original is a side view with mountains and a ground strip. In portrait the field is
## seen from above: the ground is a plane far below that scrolls down the screen, ground
## scenery lies on it and background aircraft fly between it and the play plane.
class_name Field

const FIELD_NUM := 5
## Half the visible field width (world y) on the play plane.
const VIEW_HALF_W := 15.0
## Depth of the ground plane below the play plane.
const GROUND_Z := -105.0

var size := Vector2(21, 16)
var eye_z := 20.0
var patterns: Array = []
var pattern: TData.FieldPattern
var rand := U.Rand.new()
var ground_scroll := 0.0
var ground_y := 0.0  # kept for the ship's take-off code; unused in the top-down view
var objs: U.Pool
var stage := 0


class FieldObj:
	var exists := false
	var pos := Vector2.ZERO
	var speed := 0.0
	var z := 0.0
	var fx := 0.0
	var tumiki_set: TData.TumikiSet

	func _setup(f: Field, ts: TData.TumikiSet, zz: float, s: float) -> void:
		tumiki_set = ts
		z = zz
		pos.x = f.size.x / f.eye_z * (f.eye_z - z) * 1.3
		fx = -pos.x
		speed = s
		exists = true

	## Ground scenery lies on the ground; taller rows (larger original depth) sit a bit lower.
	func set_ground(f: Field, ts: TData.TumikiSet, zz: float, s: float, rand: U.Rand) -> void:
		_setup(f, ts, -(40.0 - zz * 0.5), s)
		pos.y = rand.next_signed_float(f.half_width_at(z) * 1.1)

	## Background aircraft fly between the ground and the play plane.
	func set_sky(f: Field, ts: TData.TumikiSet, zz: float, s: float, rand: U.Rand) -> void:
		_setup(f, ts, zz * 0.5, s)
		pos.y = rand.next_signed_float(f.half_width_at(z) * 0.9)

	func move() -> void:
		pos.x -= speed
		if pos.x < fx:
			exists = false

	func draw(r: BlockRenderer) -> void:
		tumiki_set.draw_xy(r, pos.x, pos.y, z, false, false, 2)


func _init() -> void:
	for i in FIELD_NUM:
		patterns.append(TData.FieldPattern.new("fld%d.fld" % (i + 1)))
	objs = U.Pool.new(64, func(): return FieldObj.new())


## Sets the field length so that it fills a screen whose height is `aspect` times its width.
func set_aspect(aspect: float) -> void:
	size.x = maxf(21.0, VIEW_HALF_W * aspect + 1)


## Half the visible width (world y) at depth z.
func half_width_at(z: float) -> float:
	return VIEW_HALF_W / eye_z * (eye_z - z)


func start(sn: int) -> void:
	stage = sn
	pattern = patterns[sn]
	rand.set_seed(pattern.rand_seed)
	for fl in pattern.line:
		fl.cnt = fl.interval[rand.next_int(fl.interval.size())]
	objs.clear()
	ground_scroll = 0


func move() -> void:
	objs.move()
	for fl in pattern.line:
		fl.cnt -= 1
		if fl.cnt <= 0:
			var fo = objs.get_instance()
			if fo != null:
				var ts: TData.TumikiSet = fl.tumiki_set[rand.next_int(fl.tumiki_set.size())]
				if fl.on_ground:
					fo.set_ground(self, ts, fl.z, pattern.scroll_speed, rand)
				else:
					fo.set_sky(self, ts, fl.z, pattern.scroll_speed / 3 * 2, rand)
			fl.cnt = fl.interval[rand.next_int(fl.interval.size())]
	ground_scroll += pattern.scroll_speed


func draw(r: BlockRenderer) -> void:
	for o in objs.actor:
		if o.exists:
			o.draw(r)


## Draws the ground seen from above onto a canvas of size `s`: a hazy ground color with
## scrolling hills (mountain roots around green tops). Rows are generated from their index,
## so the pattern is stable while it scrolls.
func draw_ground(ci: CanvasItem, s: Vector2) -> void:
	var haze := pattern.back
	var base := pattern.ground.lerp(haze, 0.3)
	ci.draw_rect(Rect2(Vector2.ZERO, s), base)
	var k := s.x / 2 / half_width_at(GROUND_Z)  # pixels per world unit on the ground
	var c := s / 2
	var hw := half_width_at(GROUND_Z)
	var hh := c.y / k
	var row := hw * 0.36
	var root := pattern.ground.lerp(pattern.mount_top, 0.4).lerp(haze, 0.25)
	var top := pattern.mount_top.lerp(haze, 0.2)
	var gr := pattern.ground.lerp(haze, 0.12)
	var rng := RandomNumberGenerator.new()
	var i0 := floori((-hh + ground_scroll) / row) - 2
	var i1 := ceili((hh + ground_scroll) / row) + 2
	for i in range(i0, i1 + 1):
		rng.seed = hash(Vector2i(stage, i))
		var n := rng.randi_range(0, 2)
		for j in n:
			var wx := i * row + rng.randf_range(-0.3, 0.3) * row - ground_scroll
			var wy := rng.randf_range(-1.15, 1.15) * hw
			var rad := rng.randf_range(0.1, 0.24) * hw
			var kind := rng.randi_range(0, 2)
			var seg := 14
			var jit: Array = []
			for v in seg:
				jit.append(rng.randf_range(0.78, 1.12))
			var ctr := Vector2(c.x - wy * k, c.y - wx * k)
			if ctr.y + rad * k * 1.2 < 0 or ctr.y - rad * k * 1.2 > s.y:
				continue
			if kind == 0:
				# A flat patch of bare ground.
				ci.draw_colored_polygon(_blob(ctr, rad * k * 0.8, jit, 0.9), gr)
			else:
				# A hill: wide root, green top.
				ci.draw_colored_polygon(_blob(ctr, rad * k, jit, 1.25), root)
				ci.draw_colored_polygon(_blob(ctr + Vector2(rad * k * 0.08, -rad * k * 0.08), rad * k * 0.55, jit, 1.25), top)


func _blob(ctr: Vector2, rad: float, jit: Array, stretch: float) -> PackedVector2Array:
	var pts := PackedVector2Array()
	var seg := jit.size()
	for v in seg:
		var a := TAU * v / seg
		pts.append(ctr + Vector2(cos(a) * stretch, sin(a)) * rad * jit[v])
	return pts


func check_hit(p: Vector2) -> bool:
	return p.x < -size.x or p.x > size.x or p.y < -size.y or p.y > size.y


func check_hit_space(p: Vector2, space: float) -> bool:
	return p.x < -size.x + space or p.x > size.x - space or p.y < -size.y + space or p.y > size.y - space


func check_hit_box(p: Vector2, xm: float, xp: float, ym: float, yp: float) -> bool:
	return p.x < -size.x - xp or p.x > size.x - xm or p.y < -size.y - yp or p.y > size.y - ym
