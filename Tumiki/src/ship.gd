## The player's ship and the enemy pieces stuck to it (port of ship.d / stuckenemy.d).
class_name Ship

const SIZE := 0.3
const RESTART_CNT := 300
const INVINCIBLE_CNT := 228
const RESPAWN_CNT := 250
const RESPAWN_MOVE := 0.8
const NOBULLET_CNT := 150
const TARGET_DISTANCE := 20.0
const BASE_SPEED := 0.4
const SLOW_SPEED := 0.33
const BANK_BASE := 1.5
const FIELD_SPACE := 1.5
const FIRE_INTERVAL := 2
## Touch control: the ship follows the finger, but never faster than this per frame.
const TOUCH_MAX_SPEED := 1.1

var pos := Vector2.ZERO
var deg := 0.0
var restart := false
var cnt := 0
var stuck: StuckPool
var rand := U.Rand.new()
var speed := BASE_SPEED
var vel := Vector2.ZERO
var fire_cnt := 0
var field_limit := Vector2.ZERO
var tumiki_set: TData.TumikiSet
var target := VirtualTarget.new()
var ground_y := 0.0
var start_cnt := 0
var end_cnt := 0
var smx := 0.0
var smy := 0.0
var friend_pos: Array = []
var btn_prsd := false


class VirtualTarget:
	var pos := Vector2.ZERO

	func get_target_pos() -> Vector2:
		return pos


func _init() -> void:
	var f: Field = Game.I.field
	field_limit = Vector2(f.size.x - FIELD_SPACE, f.size.y - FIELD_SPACE)
	tumiki_set = TData.tumiki_set("myship/ship.tmk")
	for i in 8:
		friend_pos.append(Vector2.ZERO)
	stuck = StuckPool.new(128, self)
	stuck.get_instance().set_as_my_ship(tumiki_set)


func get_target_pos() -> Vector2:
	return pos


func start() -> void:
	var f: Field = Game.I.field
	pos = Vector2(-f.size.x / 2, 0)
	vel = Vector2.ZERO
	speed = BASE_SPEED
	restart = true
	cnt = -INVINCIBLE_CNT
	fire_cnt = 0
	deg = 0


func start_stage() -> void:
	var f: Field = Game.I.field
	start()
	pos = Vector2(-f.size.x / 3 * 2, -f.size.y / 5 * 4)
	deg = 0.2
	ground_y = 120
	f.ground_y = ground_y
	start_cnt = 0
	cnt = 0
	smx = 0
	smy = 0
	rand.set_seed(0)
	for i in friend_pos.size():
		friend_pos[i] = Vector2(-rand.next_float(f.size.x * 3) + f.size.x * 1.5, pos.y + rand.next_float(f.size.y))


func back_to_home() -> void:
	var f: Field = Game.I.field
	end_cnt = 0
	smx = -0.05
	smy = 0
	rand.set_seed(0)
	for i in friend_pos.size():
		friend_pos[i] = Vector2(-rand.next_float(f.size.x * 3) + f.size.x * 1.5, f.size.y + rand.next_float(f.size.y / 2))


static var invincible := "--invincible" in OS.get_cmdline_user_args()


func destroyed() -> void:
	if cnt <= 0 or invincible:
		return
	var g := Game.I
	stuck.flyin_all_enemies()
	g.sound.play_se(Sound.SHIP_DESTROYED)
	g.ship_destroyed()
	g.particles.add(15, pos, 0, PI * 2, 2, 1, Particles.SMOKE)
	g.particles.add(20, pos, 0, PI * 2, 1, 0.6, Particles.SPARK)
	g.particles.add(8, pos, 0, PI * 2, 4, 0.3, Particles.SPARK)
	g.vibrate(120)
	start()
	pos.x = -g.field.size.x
	cnt = -RESTART_CNT


## Called once per frame in game. `input` is the movement requested this frame (world units),
## `fire` and `slow` are the button states.
func move(input: Vector2, fire: bool, slow: bool) -> void:
	var g := Game.I
	cnt += 1
	if cnt < -NOBULLET_CNT:
		g.bullets.clear_visible_enemy()
	if cnt < -INVINCIBLE_CNT:
		if cnt > -RESPAWN_CNT:
			pos.x += RESPAWN_MOVE
		return
	if cnt == 0:
		restart = false
	# Movement: the requested displacement, capped per axis.
	var lim := TOUCH_MAX_SPEED * speed / BASE_SPEED
	var mv := Vector2(clampf(input.x, -lim, lim), clampf(input.y, -lim, lim))
	# Velocity used for banking is scaled to the original keyboard speed.
	vel = Vector2(clampf(mv.x, -speed, speed), clampf(mv.y, -speed, speed))
	pos += mv
	pos.x = clampf(pos.x, -field_limit.x, field_limit.x)
	pos.y = clampf(pos.y, -field_limit.y, field_limit.y)
	target.pos = pos + Vector2(cos(deg), sin(deg)) * TARGET_DISTANCE
	if fire and fire_cnt <= 0:
		fire_cnt = FIRE_INTERVAL + 1
		var tbs := tumiki_set.add_top_bullets(0, target, Bullets.SHIP)
		if not tbs.is_empty():
			var a = tbs[0].actor
			a.is_top = false
			a.is_morph_seed = true
			a.pos = pos
			a.deg = -deg - PI / 2
			g.sound.play_se(Sound.SHIP_SHOT)
	if slow:
		speed += (SLOW_SPEED - speed) * 0.2
		stuck.pull_in()
	else:
		speed += (BASE_SPEED - speed) * 0.2
		deg += (vel.y * BANK_BASE - deg) * 0.05
		stuck.push_out()
	if fire_cnt > 0:
		fire_cnt -= 1
	stuck.move()


func start_move() -> void:
	var g := Game.I
	if start_cnt < 120:
		if start_cnt < 80:
			smx += 0.003
		else:
			smx -= 0.006
		pos.x += smx
		if start_cnt < 60:
			g.particles.add(1, pos, PI / 2 + 0.2, 0.4, start_cnt * 0.02, 0.5, Particles.SMOKE)
	if start_cnt > 60 and start_cnt < 180:
		if start_cnt == 61:
			g.sound.play_se(Sound.PROPELLER)
		if start_cnt < 140:
			smy += 0.003
		else:
			smy -= 0.006
		pos.y += smy
		ground_y -= 1
		g.field.ground_y = ground_y
		pos.x -= 0.01
	if start_cnt > 180:
		pos.x -= 0.1
		deg -= 0.0026
		if start_cnt > 256:
			g.set_in_game()
	start_cnt += 1
	for i in friend_pos.size():
		friend_pos[i].y += 0.15


func end_move() -> void:
	pos.x += BASE_SPEED * 2
	deg *= 0.95


func back_to_home_move(button: bool) -> void:
	var g := Game.I
	var ec := end_cnt % 200
	if ec < 100:
		smx += 0.001
	else:
		smx -= 0.001
	if ec < 50 or ec > 150:
		smy += 0.001
	else:
		smy -= 0.001
	pos.x += smx
	pos.y += smy
	deg *= 0.95
	if end_cnt <= 60:
		btn_prsd = true
	else:
		if button:
			if not btn_prsd:
				g.start_gameover()
				return
		else:
			btn_prsd = false
	if end_cnt > 700:
		g.start_gameover()
		return
	elif end_cnt > 620:
		end_move()
		for i in friend_pos.size():
			friend_pos[i].x += BASE_SPEED * 2
	end_cnt += 1
	if end_cnt < 220:
		for i in friend_pos.size():
			friend_pos[i].y -= 0.05
	elif end_cnt < 330:
		for i in friend_pos.size():
			friend_pos[i].y -= 0.02


func draw(r: BlockRenderer) -> void:
	stuck.draw(r)
	if cnt < -RESPAWN_CNT or (cnt < 0 and (-cnt % 32) < 16):
		return
	tumiki_set.draw_rot(r, pos, 0, 0, deg)


func draw_friendly(r: BlockRenderer, back: bool) -> void:
	var z := -3.0
	for fp in friend_pos:
		tumiki_set.draw_rot(r, fp, z, 1, 0.0 if back else 0.2)
		z -= 1


func draw_left(r: BlockRenderer, x: float, y: float, z: float, sc: float) -> void:
	tumiki_set.draw_xy(r, x, y, z, false, false, 0, BlockRenderer.Layer.WORLD, sc)


# --------------------------------------------------------------------------

class StuckEnemy:
	const COLLISION_RATIO := 0.8
	const COLLISION_RATIO_WIDE := 3.3
	const SPLINTER_FLYIN_RATIO_X := 0.03
	const SPLINTER_FLYIN_RATIO_Y := 0.01
	const SPLINTER_FLYIN_DEG_RATIO := -0.03
	const SPLINTER_FLYIN_MOVE_Y := 0.36
	const SPLINTER_FLYIN_MOVE_DEG_MAX := 0.2
	const CONNECTED_ENEMY_MAX := 16

	var exists := false
	var is_connected := false
	var pool: StuckPool
	var ship: Ship
	var tumiki_set: TData.TumikiSet
	var ofs := Vector2.ZERO
	var lofs := Vector2.ZERO
	var st_deg := 0.0
	var pos := Vector2.ZERO
	var deg := 0.0
	var col: Array = [Vector2.ZERO, Vector2.ZERO, Vector2.ZERO, Vector2.ZERO]
	var is_my_ship := false
	var top_bullets: Array = []
	var barrage_ptn_idx := 0
	var target := VirtualTarget.new()
	var cnt := 0
	var col_size := 0.0
	var connected: Array = []

	func _init(p: StuckPool, s: Ship) -> void:
		pool = p
		ship = s

	func set_stuck(x: float, y: float, d: float, ts: TData.TumikiSet, bpi: int) -> bool:
		ofs = Vector2(x, y)
		st_deg = d
		tumiki_set = ts
		barrage_ptn_idx = bpi
		is_my_ship = false
		cnt = 0
		connected = []
		_set_col_size()
		if not pool.check_connected(self):
			return false
		top_bullets = tumiki_set.add_top_bullets(barrage_ptn_idx, target, Bullets.SHIP)
		exists = true
		return true

	func _set_col_size() -> void:
		col_size = maxf(maxf(-tumiki_set.size_xm, tumiki_set.size_xp), maxf(-tumiki_set.size_ym, tumiki_set.size_yp))
		col_size *= COLLISION_RATIO

	func remove() -> void:
		for tb in top_bullets:
			if tb.actor != null:
				tb.actor.remove_forced()
		exists = false

	func set_as_my_ship(ts: TData.TumikiSet) -> void:
		ofs = Vector2.ZERO
		st_deg = 0
		tumiki_set = ts
		is_my_ship = true
		connected = []
		top_bullets = []
		_set_col_size()
		exists = true

	func break_into_fragments() -> void:
		if pool.pull_in_ratio < 1:
			return
		tumiki_set.break_into_fragments(pos.x, pos.y, deg)
		Game.I.bullets.clear_stuck_enemy_hit(self)

	func break_into_splinter() -> void:
		var sp = Game.I.splinters.get_instance()
		if sp == null:
			return
		var md := clampf(lofs.x * SPLINTER_FLYIN_DEG_RATIO, -SPLINTER_FLYIN_MOVE_DEG_MAX, SPLINTER_FLYIN_MOVE_DEG_MAX)
		sp.set_flyin(pos, lofs.x * SPLINTER_FLYIN_RATIO_X,
			lofs.y * SPLINTER_FLYIN_RATIO_Y + SPLINTER_FLYIN_MOVE_Y, deg, md, tumiki_set, barrage_ptn_idx)

	func set_normal_collision() -> void:
		_set_collision(sin(deg) * COLLISION_RATIO, cos(deg) * COLLISION_RATIO)

	func set_wide_collision() -> void:
		_set_collision(sin(deg) * COLLISION_RATIO_WIDE, cos(deg) * COLLISION_RATIO_WIDE)

	func _set_collision(sd: float, cd: float) -> void:
		col[0] = Vector2(pos.x + tumiki_set.size_xm * cd, pos.y + tumiki_set.size_xm * sd)
		col[1] = Vector2(pos.x - tumiki_set.size_ym * sd, pos.y + tumiki_set.size_ym * cd)
		col[2] = Vector2(pos.x + tumiki_set.size_xp * cd, pos.y + tumiki_set.size_xp * sd)
		col[3] = Vector2(pos.x - tumiki_set.size_yp * sd, pos.y + tumiki_set.size_yp * cd)

	func move() -> void:
		var g := Game.I
		deg = st_deg + ship.deg
		var osd := sin(ship.deg)
		var ocd := cos(ship.deg)
		lofs = Vector2(ofs.x * ocd - ofs.y * osd, ofs.x * osd + ofs.y * ocd)
		pos = ship.pos + lofs * pool.pull_in_ratio
		set_normal_collision()
		target.pos = pos - Vector2(cos(deg), sin(deg)) * Ship.TARGET_DISTANCE
		for tb in top_bullets:
			if tb.actor != null:
				var t: TData.Tumiki = tb.tumiki
				var so := Vector2(t.ofs.x * cos(deg) - t.ofs.y * sin(deg), t.ofs.x * sin(deg) + t.ofs.y * cos(deg))
				tb.actor.pos = pos + so
				tb.actor.deg = deg - PI / 2
		cnt += 1
		var mp := 1
		var sen := Sound.STUCK_BONUS
		if pool.pull_in_ratio >= 1:
			mp = 5
		else:
			sen = Sound.STUCK_BONUS_PUSHIN
		if tumiki_set.fire_score_interval > 0 and (cnt % tumiki_set.fire_score_interval) == 0 \
				and not g.field.check_hit(pos):
			g.add_score(tumiki_set.fire_score * mp, pos)
			g.sound.play_se(sen)
		if not is_my_ship:
			pool.total_size += tumiki_set.size
		else:
			var i := 0
			while i < connected.size():
				if not connected[i].exists:
					connected.remove_at(i)
				else:
					i += 1

	func draw(r: BlockRenderer) -> void:
		if is_my_ship:
			return
		if pool.pull_in_ratio < 1:
			tumiki_set.draw_rot(r, pos, 0.2, 1, deg, pool.pull_in_ratio)
		else:
			tumiki_set.draw_rot(r, pos, 0.2, 0, deg)

	func check_hit(p: Vector2) -> bool:
		return U.in_quad(p, col)

	func check_connected(se: StuckEnemy) -> bool:
		if U.dist(se.ofs, ofs) > se.col_size + col_size:
			return false
		se.add_connected(self)
		add_connected(se)
		return true

	func add_connected(se: StuckEnemy) -> void:
		if connected.size() < CONNECTED_ENEMY_MAX:
			connected.append(se)

	func scan_connected() -> void:
		is_connected = true
		for c in connected:
			if c.exists and not c.is_connected:
				c.scan_connected()

	func set_top_bullets_deactivated(v: bool) -> void:
		for tb in top_bullets:
			if tb.actor != null:
				tb.actor.deactivated = v


class StuckPool:
	const PULLIN_CNT_MAX := 16
	var pool: U.Pool
	var pull_in_ratio := 1.0
	var total_size := 0.0
	var pull_in_cnt := 0

	func _init(n: int, ship: Ship) -> void:
		pool = U.Pool.new(n, func(): return StuckEnemy.new(self, ship))

	func get_instance():
		return pool.get_instance()

	func _init_pull_in() -> void:
		pull_in_cnt = 0
		pull_in_ratio = 1

	func check_hit(p: Vector2) -> bool:
		if pull_in_cnt > 0:
			return false
		for se in pool.actor:
			if se.exists and se.check_hit(p):
				return true
		return false

	func check_hit_without_my_ship(p: Vector2):
		if pull_in_cnt > 0:
			return null
		for se in pool.actor:
			if se.exists and not se.is_my_ship and se.check_hit(p):
				return se
		return null

	func remove_all_enemies() -> void:
		for se in pool.actor:
			if se.exists and not se.is_my_ship:
				se.break_into_fragments()
				se.remove()
		_init_pull_in()

	func flyin_all_enemies() -> void:
		for se in pool.actor:
			if se.exists and not se.is_my_ship:
				se.break_into_splinter()
				se.remove()
		_init_pull_in()

	func check_connected(nse: StuckEnemy) -> bool:
		var c := false
		for se in pool.actor:
			if se.exists and se.check_connected(nse):
				c = true
		return c

	func remove_stuck_enemy(hse: StuckEnemy) -> void:
		hse.break_into_fragments()
		hse.remove()
		var my_ship: StuckEnemy = null
		for se in pool.actor:
			if se.exists:
				if se.is_my_ship:
					my_ship = se
				se.is_connected = false
		if my_ship != null:
			my_ship.scan_connected()
		for se in pool.actor:
			if se.exists and not se.is_my_ship and not se.is_connected:
				se.break_into_fragments()
				se.remove()

	func pull_in() -> void:
		if pull_in_cnt == 0:
			for se in pool.actor:
				if se.exists and not se.is_my_ship:
					se.set_top_bullets_deactivated(true)
		if pull_in_cnt < PULLIN_CNT_MAX:
			pull_in_cnt += 1
		pull_in_ratio = 1 - float(pull_in_cnt) / PULLIN_CNT_MAX

	func push_out() -> void:
		if pull_in_cnt > 0:
			pull_in_cnt -= 1
		if pull_in_cnt == 0:
			for se in pool.actor:
				if se.exists and not se.is_my_ship:
					se.set_top_bullets_deactivated(false)
		pull_in_ratio = 1 - float(pull_in_cnt) / PULLIN_CNT_MAX

	func move() -> void:
		total_size = 0
		for se in pool.actor:
			if se.exists:
				se.move()
		Game.I.set_rank(total_size * 0.02)

	func draw(r: BlockRenderer) -> void:
		for se in pool.actor:
			if se.exists:
				se.draw(r)

	func count() -> int:
		var n := 0
		for se in pool.actor:
			if se.exists and not se.is_my_ship:
				n += 1
		return n
