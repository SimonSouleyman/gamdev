## Stage field: background colors, mountains and scrolling scenery (port of field.d).
class_name Field

const GROUND_LEVEL := -17.0
const FIELD_NUM := 5
const GROUND_Y := 280.0

var size := Vector2(21, 16)
var eye_z := 20.0
var patterns: Array = []
var pattern: TData.FieldPattern
var rand := U.Rand.new()
var mnx := 0.0
var ground_y := 0.0
var objs: U.Pool
var back_mount_pos: Array = []


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

	func set_ground(f: Field, ts: TData.TumikiSet, zz: float, s: float) -> void:
		_setup(f, ts, zz, s)
		pos.y = Field.GROUND_LEVEL - ts.size_ym

	func set_sky(f: Field, ts: TData.TumikiSet, zz: float, s: float, rand: U.Rand) -> void:
		_setup(f, ts, zz, s)
		pos.y = rand.next_float(f.size.y / f.eye_z * (f.eye_z - z) * 0.8)

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
	for i in 17:
		back_mount_pos.append(Vector2.ZERO)


func start(sn: int) -> void:
	pattern = patterns[sn]
	rand.set_seed(pattern.rand_seed)
	for fl in pattern.line:
		fl.cnt = fl.interval[rand.next_int(fl.interval.size())]
	objs.clear()
	var x := 0.0
	for i in 4:
		var tx := i * 160 + 80 + rand.next_signed_float(30)
		var ty := GROUND_Y - 5 - rand.next_float(25)
		var nx := 160 + i * 160 + rand.next_signed_float(30)
		back_mount_pos[i * 2] = Vector2(x, GROUND_Y)
		back_mount_pos[i * 2 + 1] = Vector2(tx, ty)
		x = nx
	for i in 8:
		back_mount_pos[8 + i] = Vector2(back_mount_pos[i].x + 640, back_mount_pos[i].y)
	back_mount_pos[16] = Vector2(1280, GROUND_Y)
	mnx = 0


func move() -> void:
	objs.move()
	for fl in pattern.line:
		fl.cnt -= 1
		if fl.cnt <= 0:
			var fo = objs.get_instance()
			if fo != null:
				var ts: TData.TumikiSet = fl.tumiki_set[rand.next_int(fl.tumiki_set.size())]
				if fl.on_ground:
					fo.set_ground(self, ts, fl.z, pattern.scroll_speed)
				else:
					fo.set_sky(self, ts, fl.z, pattern.scroll_speed / 3 * 2, rand)
			fl.cnt = fl.interval[rand.next_int(fl.interval.size())]
	mnx += pattern.scroll_speed
	if mnx >= 640:
		mnx -= 640


func draw(r: BlockRenderer) -> void:
	for o in objs.actor:
		if o.exists:
			o.draw(r)


## Draws the background in the original 640x480 screen space. `xf` maps such a point
## to the target canvas.
func draw_back(ci: CanvasItem, xf: Transform2D) -> void:
	var g := pattern.ground
	var mr := pattern.mount_root
	var mt := pattern.mount_top
	var gy1 := 400 - ground_y
	var gy2 := GROUND_Y - ground_y
	ci.draw_colored_polygon(xf * PackedVector2Array([Vector2(0, 480), Vector2(640, 480), Vector2(640, gy1), Vector2(0, gy1)]), g)
	ci.draw_polygon(xf * PackedVector2Array([Vector2(0, gy1), Vector2(640, gy1), Vector2(640, gy2), Vector2(0, gy2)]),
		PackedColorArray([g, g, mr, mr]))
	var idx := 0
	for i in back_mount_pos.size() / 2:
		var x1: float = back_mount_pos[idx].x - mnx
		var x2: float = back_mount_pos[idx + 1].x - mnx
		var x3: float = back_mount_pos[idx + 2].x - mnx
		if x1 >= 640:
			break
		if x3 >= 0:
			ci.draw_polygon(xf * PackedVector2Array([
				Vector2(x1, back_mount_pos[idx].y - ground_y),
				Vector2(x2, back_mount_pos[idx + 1].y - ground_y),
				Vector2(x3, back_mount_pos[idx + 2].y - ground_y)]),
				PackedColorArray([mr, mt, mr]))
		idx += 2


func check_hit(p: Vector2) -> bool:
	return p.x < -size.x or p.x > size.x or p.y < -size.y or p.y > size.y


func check_hit_space(p: Vector2, space: float) -> bool:
	return p.x < -size.x + space or p.x > size.x - space or p.y < -size.y + space or p.y > size.y - space


func check_hit_box(p: Vector2, xm: float, xp: float, ym: float, yp: float) -> bool:
	return p.x < -size.x - xp or p.x > size.x - xm or p.y < -size.y - yp or p.y > size.y - ym
