## Bouncing letters hanging from the top of the screen (port of mobileletter.d).
## Coordinates are in the original 640x480 screen space.
class_name Letters

const LETTER_WIDTH := 2.1
const LETTER_HEIGHT := 3.0
const COLOR_NUM := 6
const GRAVITY := 0.2
const ROOT_Y := 16.0

var pool: U.Pool
var rand := U.Rand.new()
static var mrand := U.Rand.new()


class MobileLetter:
	var exists := false
	var pos := Vector2.ZERO
	var deg := 0.0
	var md := 0.0
	var vel := Vector2.ZERO
	var root := Vector2.ZERO
	var length := 0.0
	var ch := ""
	var color := 0
	var size := 0.0
	var cnt := 0

	func set_letter(x: float, y: float, l: float, c: String, cl: int, s: float, cn: int) -> void:
		pos = Vector2(x, y)
		root = pos
		length = l
		vel = Vector2(Letters.mrand.next_signed_float(2.5), Letters.mrand.next_signed_float(1))
		ch = c
		color = cl
		size = s
		cnt = cn
		deg = 0
		md = Letters.mrand.next_signed_float(10)
		exists = true

	func move() -> void:
		cnt -= 1
		if cnt < 0:
			pos.x += (root.x - pos.x) * 0.97
			deg *= 0.95
			pos.y -= 3
			if pos.y < root.y - size * Letters.LETTER_HEIGHT:
				exists = false
			return
		pos += vel
		vel.y += Letters.GRAVITY
		deg += md
		deg *= 0.95
		if U.dist(pos, root) > length:
			vel *= -0.57
			md *= -0.4
			pos += vel
			pos.x += (root.x - pos.x) * 0.5
		deg *= 0.99


func _init(n: int) -> void:
	pool = U.Pool.new(n, func(): return MobileLetter.new())


func add(s: String, x: float, lgt: float, size: float, cnt: int, col: int) -> void:
	var color := col
	if col < 0:
		rand.set_seed(-col)
	for c in s:
		var ml = pool.get_instance()
		if ml == null:
			return
		if c != " ":
			if col < 0:
				color = rand.next_int(COLOR_NUM)
			ml.set_letter(x, ROOT_Y, lgt, c, color, size, cnt)
		x += LETTER_WIDTH * size


func move() -> void:
	pool.move()


func clear() -> void:
	pool.clear()
