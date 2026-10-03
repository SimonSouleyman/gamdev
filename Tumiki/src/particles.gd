## Additive smoke and spark particles.
class_name Particles

enum { SMOKE, SPARK }
var rand := U.Rand.new()
var n := 0
# Struct of arrays for speed
var px := PackedFloat32Array()
var py := PackedFloat32Array()
var vx := PackedFloat32Array()
var vy := PackedFloat32Array()
var alpha := PackedFloat32Array()
var size := PackedFloat32Array()
var type := PackedInt32Array()
var cnt := PackedInt32Array()
var idx := 0

func _init(num: int) -> void:
	n = num
	px.resize(num)
	py.resize(num)
	vx.resize(num)
	vy.resize(num)
	alpha.resize(num)
	size.resize(num)
	type.resize(num)
	cnt.resize(num)
	cnt.fill(-1)

func add(count: int, p: Vector2, deg: float, od: float, speed: float, s: float, t: int) -> void:
	for k in count:
		idx -= 1
		if idx < 0:
			idx = n - 1
		var i := idx
		px[i] = p.x
		py[i] = p.y
		var sb := rand.next_float(0.5) + 0.75
		var d := deg + rand.next_signed_float(od)
		vx[i] = -sin(d) * speed * sb
		vy[i] = -cos(d) * speed * sb
		cnt[i] = 16 + rand.next_int(16)
		alpha[i] = 0.8 + rand.next_float(0.2)
		size[i] = s * (rand.next_float(0.5) + 0.75)
		type[i] = t

func move() -> void:
	for i in n:
		if cnt[i] < 0:
			continue
		cnt[i] -= 1
		if cnt[i] < 0:
			continue
		px[i] += vx[i]
		py[i] += vy[i]
		vx[i] *= 0.9
		vy[i] *= 0.9
		alpha[i] *= 0.9
		size[i] *= 1.025 if type[i] == SMOKE else 1.01

func draw(r: BlockRenderer) -> void:
	for i in n:
		if cnt[i] < 0:
			continue
		var c: Color
		if type[i] == SMOKE:
			c = Color(0.8, 0.8, 0.8, alpha[i])
		elif (cnt[i] & 1) == 0:
			c = Color(1, 0.4, 0.2, alpha[i])
		else:
			c = Color(1, 1, 0.1, alpha[i])
		r.particle(px[i], py[i], size[i], c)

func clear() -> void:
	cnt.fill(-1)
