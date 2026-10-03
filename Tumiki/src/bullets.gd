## Bullets driven by BulletML (port of BulletActor / BulletActorPool / BulletInst).
class_name Bullets

enum { ENEMY, SHIP, MOVE }

const VEL_SS_SDM_RATIO := 62.0 / 10
const VEL_SDM_SS_RATIO := 10.0 / 62
const FIELD_SPACE := 0.5
const SHIP_HIT_WIDTH := 0.4

var pool: U.Pool
var cnt := 0
var bml_rand := U.Rand.new()
static var total_bullets_speed := 0.0


func _init(n: int) -> void:
	pool = U.Pool.new(n, func(): return BulletActor.new(self))


class BulletActor:
	var bullets: Bullets
	var exists := false
	var pos := Vector2.ZERO
	var acc := Vector2.ZERO
	var ppos := Vector2.ZERO
	var deg := 0.0
	var speed := 0.0
	var rank_num := 0.0
	var runner: BulletML.Runner
	var parser: Array = []
	var ranks: Array = []
	var speeds: Array = []
	var morph_num := 0
	var morph_idx := 0
	var shape := 0
	var color := 0
	var bullet_size := 0.0
	var x_reverse := 1.0
	var y_reverse := 1.0
	var target  # object with get_target_pos()
	var type := 0
	var deactivated := false
	var speed_rank_num := 1.0
	var is_simple := false
	var is_top := false
	var is_visible := true
	var cnt := 0
	var should_be_removed := false
	var is_wait := false
	var post_wait := 0
	var wait_cnt := 0
	var is_morph_seed := false

	func _init(b: Bullets) -> void:
		bullets = b

	func _start(sr: float, sh: int, cl: int, sz: float, xr: float, yr: float, tg, tp: int) -> void:
		exists = true
		is_top = false
		is_wait = false
		is_visible = true
		is_morph_seed = false
		ppos = pos
		speed_rank_num = sr
		shape = sh
		color = cl
		bullet_size = sz
		x_reverse = xr
		y_reverse = yr
		target = tg
		type = tp
		deactivated = false
		cnt = 0
		should_be_removed = false

	func set_full(r: BulletML.Runner, x: float, y: float, d: float, s: float, rank: float, sr: float,
			sh: int, cl: int, sz: float, xr: float, yr: float, tg, tp: int,
			p: Array, rs: Array, ss: Array, mn: int, mi: int) -> void:
		pos = Vector2(x, y)
		acc = Vector2.ZERO
		deg = d
		speed = s
		rank_num = rank
		runner = r
		parser = p
		ranks = rs
		speeds = ss
		morph_num = mn
		morph_idx = mi
		is_simple = false
		_start(sr, sh, cl, sz, xr, yr, tg, tp)

	func set_simple(x: float, y: float, d: float, s: float, rank: float, sr: float,
			sh: int, cl: int, sz: float, xr: float, yr: float, tg, tp: int) -> void:
		pos = Vector2(x, y)
		acc = Vector2.ZERO
		deg = d
		speed = s
		rank_num = rank
		runner = null
		morph_num = 0
		morph_idx = 0
		is_simple = true
		_start(sr, sh, cl, sz, xr, yr, tg, tp)

	func set_top() -> void:
		is_top = true
		is_visible = false

	func rewind() -> void:
		runner = BulletML.Runner.from_doc(parser[0])
		morph_idx = 0

	func remove() -> void:
		should_be_removed = true

	func remove_forced() -> void:
		runner = null
		exists = false

	func remove_forced_visible() -> void:
		if is_visible:
			Game.I.particles.add(1, pos, deg, 0, speed * get_speed_rank(), 0.4, Particles.SPARK)
			remove_forced()

	func remove_forced_visible_enemy() -> void:
		if is_visible and type == Bullets.ENEMY:
			Game.I.particles.add(1, pos, deg, 0, speed * get_speed_rank(), 0.4, Particles.SPARK)
			remove_forced()

	func get_speed_rank() -> float:
		if type == Bullets.ENEMY:
			return speed_rank_num * Game.I.stage_manager.speed_rank
		return speed_rank_num

	# --- BulletML callbacks ---
	func get_rank() -> float:
		if type == Bullets.ENEMY:
			var sr: float = Game.I.stage_manager.rank / (1 + morph_num * 0.33)
			return minf(rank_num + (1 - rank_num) * sr, 1.0)
		return rank_num

	func get_rand() -> float:
		return bullets.bml_rand.next_float(1)

	func get_turn() -> int:
		return bullets.cnt

	func get_bullet_direction() -> float:
		return U.rtod(deg)

	func get_aim_direction() -> float:
		var t: Vector2 = target.get_target_pos()
		return U.rtod((atan2(t.x - pos.x, t.y - pos.y) * x_reverse + PI / 2) * y_reverse - PI / 2)

	func get_bullet_speed() -> float:
		return speed * Bullets.VEL_SS_SDM_RATIO

	func get_bullet_speed_x() -> float:
		return acc.x

	func get_bullet_speed_y() -> float:
		return acc.y

	func create_simple_bullet(d: float, s: float) -> void:
		bullets.add_bullet(self, U.dtor(d), s * Bullets.VEL_SDM_SS_RATIO)

	func create_bullet(state: BulletML.State, d: float, s: float) -> void:
		bullets.add_bullet_state(self, state, U.dtor(d), s * Bullets.VEL_SDM_SS_RATIO)

	func do_vanish() -> void:
		remove()

	func do_change_direction(d: float) -> void:
		deg = U.dtor(d)

	func do_change_speed(s: float) -> void:
		speed = s * Bullets.VEL_SDM_SS_RATIO

	func do_accel_x(sx: float) -> void:
		acc.x = sx * Bullets.VEL_SDM_SS_RATIO

	func do_accel_y(sy: float) -> void:
		acc.y = sy * Bullets.VEL_SDM_SS_RATIO

	# --- Actor ---
	func _check_ship_hit() -> void:
		var ship: Ship = Game.I.ship
		var bm := ppos - pos
		var inaa := bm.length_squared()
		if inaa > 0.00001:
			var so := ship.pos - pos
			var inab := bm.dot(so)
			if inab >= 0 and inab <= inaa:
				var hd := so.length_squared() - inab * inab / inaa
				if hd >= 0 and hd <= Bullets.SHIP_HIT_WIDTH:
					ship.destroyed()

	func move() -> void:
		var tpos: Vector2 = target.get_target_pos()
		ppos = pos
		if is_top:
			deg = (atan2(tpos.x - pos.x, tpos.y - pos.y) * x_reverse + PI / 2) * y_reverse - PI / 2
		if is_wait and wait_cnt > 0:
			wait_cnt -= 1
			if should_be_removed:
				remove_forced()
			return
		if not is_simple and runner != null:
			runner.run(self)
			if runner != null and runner.is_end():
				if is_top:
					rewind()
					if is_wait:
						wait_cnt = post_wait
						return
				elif is_morph_seed:
					remove_forced()
					return
		if should_be_removed:
			remove_forced()
			return
		var sr := get_speed_rank()
		pos.x += (sin(deg) * speed + acc.x) * sr * x_reverse
		pos.y += (cos(deg) * speed - acc.y) * sr * y_reverse
		if is_visible:
			var g := Game.I
			match type:
				Bullets.ENEMY:
					Bullets.total_bullets_speed += speed * sr
					if g.splinters_check_hit(pos):
						remove_forced_visible()
					else:
						var hse = g.ship.stuck.check_hit_without_my_ship(pos)
						if hse != null:
							g.particles.add(3, pos, deg, 0.1, speed * sr / 2, 0.6, Particles.SMOKE)
							g.particles.add(20, pos, 0, PI * 2, 3, 0.4, Particles.SPARK)
							g.sound.play_se(Sound.STUCK_DESTROYED)
							g.ship.stuck.remove_stuck_enemy(hse)
							remove_forced()
						else:
							_check_ship_hit()
				Bullets.SHIP:
					if g.enemies_check_hit(pos, 1):
						g.particles.add(3, pos, deg, 0.1, speed * sr / 2, 0.5, Particles.SMOKE)
						g.particles.add(3, pos, deg + PI, 1, speed * sr, 0.3, Particles.SPARK)
						remove_forced()
			if exists and g.field.check_hit_space(pos, Bullets.FIELD_SPACE):
				remove_forced()
		cnt += 1

	func draw(r: BlockRenderer) -> void:
		if not is_visible:
			return
		var d := (-deg * x_reverse + PI / 2) * y_reverse - PI / 2
		var layer := BlockRenderer.Layer.TOP if type == Bullets.ENEMY else BlockRenderer.Layer.OVER
		var cl := 8 + color
		match shape:
			0:
				r.block(0, cl, 3, pos.x, pos.y, 0, d, bullet_size * 0.2, bullet_size * 0.5, 0.3, layer)
			1:
				r.block(5, cl, 3, pos.x, pos.y, 0, d, bullet_size * 0.2, bullet_size * 0.5, 0.3, layer)
			2:
				r.block(0, cl, 3, pos.x, pos.y, 0, deg_to_rad(cnt * 11.0), bullet_size * 0.4, bullet_size * 0.4, 0.3, layer)


func add_bullet(now: BulletActor, d: float, s: float) -> void:
	var ba: BulletActor = pool.get_instance()
	if ba == null or now.deactivated:
		return
	var nmi := now.morph_idx + 1
	if nmi < now.morph_num:
		var runner := BulletML.Runner.from_doc(now.parser[nmi])
		ba.set_full(runner, now.pos.x, now.pos.y, d, s, now.ranks[nmi], now.speeds[nmi],
			now.shape, now.color, now.bullet_size, now.x_reverse, now.y_reverse, now.target, now.type,
			now.parser, now.ranks, now.speeds, now.morph_num, nmi)
		ba.is_morph_seed = true
	else:
		nmi -= 1
		ba.set_simple(now.pos.x, now.pos.y, d, s, now.ranks[nmi], now.speeds[nmi],
			now.shape, now.color, now.bullet_size, now.x_reverse, now.y_reverse, now.target, now.type)


func add_bullet_state(now: BulletActor, state: BulletML.State, d: float, s: float) -> void:
	var ba: BulletActor = pool.get_instance()
	if ba == null or now.deactivated:
		return
	var runner := BulletML.Runner.from_state(state)
	var mi := now.morph_idx
	ba.set_full(runner, now.pos.x, now.pos.y, d, s, now.ranks[mi], now.speeds[mi],
		now.shape, now.color, now.bullet_size, now.x_reverse, now.y_reverse, now.target, now.type,
		now.parser, now.ranks, now.speeds, now.morph_num, mi)


func add_top_bullet(parser: Array, ranks: Array, speeds: Array, x: float, y: float, d: float, s: float,
		shape: int, color: int, size: float, xr: float, yr: float, target, type: int,
		prev_wait: int, post_wait: int) -> BulletActor:
	var ba: BulletActor = pool.get_instance()
	if ba == null:
		return null
	var runner := BulletML.Runner.from_doc(parser[0])
	ba.set_full(runner, x, y, d, s, ranks[0], speeds[0], shape, color, size, xr, yr, target, type,
		parser, ranks, speeds, parser.size(), 0)
	ba.is_wait = true
	ba.wait_cnt = prev_wait
	ba.post_wait = post_wait
	ba.set_top()
	return ba


func add_move_bullet(parser: BulletML, s: float, x: float, y: float, d: float, target) -> BulletActor:
	var ba: BulletActor = pool.get_instance()
	if ba == null:
		return null
	var runner := BulletML.Runner.from_doc(parser)
	ba.set_full(runner, x, y, d, 0, 0, s, 0, 0, 0, 1, 1, target, MOVE, [], [], [], 0, 0)
	ba.is_visible = false
	return ba


func move() -> void:
	pool.move()
	cnt += 1


func draw(r: BlockRenderer) -> void:
	for a in pool.actor:
		if a.exists:
			a.draw(r)


func clear() -> void:
	for a in pool.actor:
		if a.exists:
			a.remove_forced()


func clear_visible() -> void:
	for a in pool.actor:
		if a.exists:
			a.remove_forced_visible()


func clear_visible_enemy() -> void:
	for a in pool.actor:
		if a.exists:
			a.remove_forced_visible_enemy()


func clear_stuck_enemy_hit(se) -> void:
	se.set_wide_collision()
	for a in pool.actor:
		if a.exists and se.check_hit(a.pos):
			a.remove_forced_visible()
