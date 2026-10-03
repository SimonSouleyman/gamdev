## Splinters, particles, fragments, score signs and the damage gauge.
class_name Effects


class Splinter:
	const MOVE_DEG_DEFAULT := 0.05
	const MOVE_X_DEFAULT := 0.16
	const GRAVITY := 0.005
	const COLLISION_RATIO := 0.8
	static var sign_num := 0
	static var rand := U.Rand.new()

	var exists := false
	var tumiki_set: TData.TumikiSet
	var pos := Vector2.ZERO
	var vel := Vector2.ZERO
	var deg := 0.0
	var md := 0.0
	var barrage_ptn_idx := 0
	var col: Array = []
	var has_sign := false
	var cnt := 0
	var is_boss := false
	var flyin := false

	func _init() -> void:
		for i in 16:
			col.append(Vector2.ZERO)

	func set_from_enemy(x: float, y: float, ts: TData.TumikiSet, bpi: int, boss: bool) -> void:
		pos = Vector2(x, y)
		tumiki_set = ts
		barrage_ptn_idx = bpi
		deg = 0
		is_boss = boss
		if not boss:
			md = MOVE_DEG_DEFAULT
			vel = Vector2(-MOVE_X_DEFAULT, 0)
		else:
			md = MOVE_DEG_DEFAULT / 3
			vel = Vector2(-MOVE_X_DEFAULT / 2, -MOVE_X_DEFAULT / 3)
		if Splinter.sign_num > 0:
			has_sign = true
			Splinter.sign_num -= 1
		else:
			has_sign = false
		flyin = false
		cnt = 0
		exists = true

	func set_flyin(p: Vector2, mx: float, my: float, d: float, m: float, ts: TData.TumikiSet, bpi: int) -> void:
		pos = p
		tumiki_set = ts
		barrage_ptn_idx = bpi
		deg = d
		is_boss = false
		md = m
		vel = Vector2(mx, my)
		has_sign = false
		flyin = true
		cnt = 0
		exists = true

	func move() -> void:
		var g := Game.I
		pos += vel
		deg += md
		cnt += 1
		if not is_boss:
			vel.y -= GRAVITY
			if pos.y < -g.field.size.y - tumiki_set.size:
				exists = false
				return
			var sd := sin(deg) * COLLISION_RATIO
			var cd := cos(deg) * COLLISION_RATIO
			var ts := tumiki_set
			col[0] = Vector2(pos.x + ts.size_xm * cd, pos.y + ts.size_xm * sd)
			col[1] = Vector2(pos.x - ts.size_ym * sd, pos.y + ts.size_ym * cd)
			col[2] = Vector2(pos.x + ts.size_xp * cd, pos.y + ts.size_xp * sd)
			col[3] = Vector2(pos.x - ts.size_yp * sd, pos.y + ts.size_yp * cd)
			var di1 := 0
			var di2 := 1
			var idx := 4
			for i in 4:
				var o: Vector2 = (col[di2] - col[di1]) / 4
				var dp: Vector2 = col[di1]
				for j in 3:
					dp += o
					col[idx] = dp
					idx += 1
				di1 += 1
				di2 += 1
				if di2 > 3:
					di2 = 0
			var ship: Ship = g.ship
			if ship.cnt < -Ship.INVINCIBLE_CNT:
				return
			for cd2 in col:
				if ship.stuck.check_hit(cd2):
					var se = ship.stuck.get_instance()
					if se != null:
						var o := pos - ship.pos
						var sx := o.x * cos(-ship.deg) - o.y * sin(-ship.deg)
						var sy := o.x * sin(-ship.deg) + o.y * cos(-ship.deg)
						if not se.set_stuck(sx, sy, deg - ship.deg, tumiki_set, barrage_ptn_idx):
							continue
						if not flyin:
							g.add_score((tumiki_set.score / 2 / 10) * 10, pos)
						g.particles.add(3, pos, 0, PI * 2, 0.05, 0.3, Particles.SMOKE)
						g.sound.play_se(Sound.STUCK)
					exists = false
					return
		else:
			vel.x *= 0.99
			g.particles.add(1, pos, 0, PI * 2, 0.5 + rand.next_float(2), 0.5, Particles.SPARK)
			if rand.next_int(45) == 0:
				g.particles.add(3 + rand.next_int(4), pos, 0, PI * 2, 0.3, 0.7, Particles.SMOKE)
				g.sound.play_se(Sound.ENEMY_DESTROYED)
			if cnt > 180:
				g.particles.add(32, pos, 0, PI * 2, 2, 0.5, Particles.SPARK)
				g.particles.add(15, pos, 0, PI * 2, 0.5, 1.5, Particles.SMOKE)
				g.particles.add(15, pos, 0, PI * 2, 3, 1, Particles.SMOKE)
				g.sound.play_se(Sound.BOSS_DESTROYED)
				g.vibrate(300)
				exists = false

	func draw(r: BlockRenderer) -> void:
		tumiki_set.draw_rot(r, pos, -0.7, 1, deg)

	func check_hit(p: Vector2) -> bool:
		return U.in_quad(p, col)


class Fragment:
	const GRAVITY := 0.012
	static var rand := U.Rand.new()
	var exists := false
	var pos := Vector2.ZERO
	var vel := Vector2.ZERO
	var size := Vector2.ZERO
	var deg := 0.0
	var md := 0.0
	var shape := 0
	var color := 0
	var cnt := 0

	func set_frag(sh: int, cl: int, x: float, y: float, s: Vector2) -> void:
		shape = sh
		color = cl
		pos = Vector2(x, y)
		size = s
		vel = Vector2(rand.next_signed_float(0.2), rand.next_signed_float(0.1))
		deg = 0
		md = rand.next_signed_float(8)
		cnt = 32 + rand.next_int(48)
		exists = true

	func move() -> void:
		cnt -= 1
		if cnt < 0:
			exists = false
			return
		pos += vel
		vel.y -= GRAVITY
		deg += md

	func draw(r: BlockRenderer) -> void:
		if cnt < 16:
			if (cnt & 1) == 1:
				return
		elif cnt < 32:
			if (cnt % 3) == 2:
				return
		elif (cnt % 4) == 3:
			return
		var sh := shape if shape < TData.PROPELLER_SHAPE else 9
		r.block(sh, color, 1, pos.x, pos.y, -1, deg_to_rad(deg), size.x, size.y,
			(size.x + size.y) / 2, BlockRenderer.Layer.OVER)


class ScoreSign:
	const FIELD_X := 14.5
	var exists := false
	var pos := Vector2.ZERO
	var my := 0.0
	var size := 0.0
	var num := 0
	var cnt := 0

	func set_sign(p: Vector2, n: int, s: float) -> void:
		pos = p
		if pos.x > FIELD_X - s * 2:
			pos.x = FIELD_X - s * 2
		num = n
		size = s
		my = 0.3
		cnt = 60
		exists = true

	func move() -> void:
		cnt -= 1
		if cnt < 0:
			exists = false
			return
		pos.y += my
		my *= 0.92


class DamageGauge:
	var cnt := 0
	var items: Array = [[null, 0], [null, 0], [null, 0]]

	func init() -> void:
		cnt = 0
		for it in items:
			it[0] = null

	func add(ep) -> void:
		var mc := 0x7fffffff
		var si = null
		for it in items:
			if it[0] != null:
				if it[0] == ep:
					it[1] = cnt
					return
				if mc > it[1]:
					si = it
					mc = it[1]
			elif mc >= 0:
				si = it
				mc = -0x7fffffff
		si[0] = ep
		si[1] = cnt

	func move() -> void:
		for it in items:
			if it[0] != null and it[0].shield <= 0:
				it[0] = null
		cnt += 1

	func draw(r: BlockRenderer) -> void:
		var x := 18.0
		var y := -13.0
		for it in items:
			if it[0] != null:
				var ts: TData.TumikiSet = it[0].spec.tumiki_set
				var s := 1.0 / ts.size * 3
				ts.draw_xy(r, x / s, y / s, 0.9, false, false, 0, BlockRenderer.Layer.OVER, s)
				var sl: float = it[0].shield
				var i := 0
				while sl > 0:
					var sx2 := 11.0
					var slb := minf(sl, 100)
					var sx1 := sx2 - slb / 10
					r.block(0, (3 + i) % 12, 3, sx1 + sx2 / 2, y - 0.4, 1 + i * 0.1, 0, sx2 - sx1, 0.4, 1,
						BlockRenderer.Layer.OVER)
					i += 1
					sl -= 100
			y += 1.8
