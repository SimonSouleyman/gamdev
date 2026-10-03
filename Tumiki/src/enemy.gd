## Enemies built from tumiki parts (port of enemy.d).
class_name Enemy

const PARTS_MAX_NUM := 8
const BOSS_MOVE_DEG := 0.04

static var total_num := 0

var exists := false
var spec: TData.EnemySpec
var parts: Array = []
var parts_num := 0
var fire_cnt := 0
var attack_form_idx := 0
var attack_ptn_idx := 0
var barrage_ptn_idx := 0
var pos := Vector2.ZERO
var cnt := 0
var is_boss := false
# Movement
var move_bullet = null
var pattern: TData.MovePattern
var move_point_idx := 0
var mdeg := 0.0
var on_route := false
var reach_first_point := false
var reach_first_point_first := false
var withdraw := false
var withdraw_pos := Vector2.ZERO


func _init() -> void:
	for i in PARTS_MAX_NUM:
		parts.append(EnemyPart.new())


func set_enemy(x: float, y: float, sp: TData.EnemySpec, mp: TData.MovePattern, boss: bool) -> void:
	pos = Vector2(x, y)
	spec = sp
	is_boss = boss
	parts_num = 0
	for eps in spec.parts:
		if parts_num >= PARTS_MAX_NUM:
			break
		parts[parts_num].set_part(eps)
		parts_num += 1
	for j in parts_num - 1:
		for i in range(j + 1, parts_num):
			parts[j].cover_parts.append(parts[i])
	fire_cnt = 0
	attack_form_idx = 0
	attack_ptn_idx = 0
	barrage_ptn_idx = 0
	pattern = mp
	if mp.is_bml:
		move_bullet = Game.I.bullets.add_move_bullet(mp.parser, mp.speed, x, y, mp.deg, Game.I.ship)
		if move_bullet == null:
			return
	else:
		move_bullet = null
		move_point_idx = 0
		mdeg = mp.deg
		on_route = false
		reach_first_point = false
		reach_first_point_first = false
		withdraw = false
		withdraw_pos = pos
	cnt = 0
	exists = true


func remove() -> void:
	for i in parts_num:
		parts[i].remove()
	if move_bullet != null:
		move_bullet.remove_forced()
	exists = false


func _break_into_fragments() -> void:
	for i in parts_num:
		if parts[i].shield > 0:
			parts[i].break_into_fragments(pos)


func _points_move() -> void:
	var ax: float
	var ay: float
	var spd: float
	var pt: Array
	if not withdraw:
		if pattern.point.has(barrage_ptn_idx):
			pt = pattern.point[barrage_ptn_idx]
			spd = pattern.speeds[barrage_ptn_idx]
		else:
			pt = pattern.point[-1]
			spd = pattern.speeds[-1]
		if not reach_first_point_first:
			spd *= 3
		elif not reach_first_point:
			spd *= 2
		if move_point_idx >= pt.size():
			move_point_idx = 0
		var aim: Vector2 = pt[move_point_idx]
		if cnt >= pattern.withdraw_cnt:
			withdraw = true
			on_route = false
		# The original scales both by the field length (21). The portrait field is longer, so x
		# follows it while y keeps the original scale to stay inside the field width.
		ax = aim.x * Game.I.field.size.x
		ay = aim.y * 21.0
	if withdraw:
		ax = withdraw_pos.x
		ay = withdraw_pos.y
		spd = pattern.speeds[-1] * 2
	var d := atan2(ax - pos.x, ay - pos.y)
	var od := d - mdeg
	if od > PI:
		od -= PI * 2
	elif od < -PI:
		od += PI * 2
	var aod := absf(od)
	if aod < BOSS_MOVE_DEG:
		mdeg = d
	elif od > 0:
		mdeg += BOSS_MOVE_DEG
		if mdeg >= PI * 2:
			mdeg -= PI * 2
	else:
		mdeg -= BOSS_MOVE_DEG
		if mdeg < 0:
			mdeg += PI * 2
	pos.x += sin(mdeg) * spd
	pos.y += cos(mdeg) * spd
	if not on_route:
		if aod < PI / 2:
			on_route = true
	elif aod > PI / 2:
		if withdraw:
			remove()
			return
		if is_boss and not reach_first_point_first:
			Game.I.boss_in_attack(pattern.withdraw_cnt)
		reach_first_point = true
		reach_first_point_first = true
		on_route = false
		move_point_idx += 1
		if move_point_idx >= pt.size():
			move_point_idx = 0


func _add_top_bullets() -> void:
	for i in parts_num:
		parts[i].add_top_bullets(barrage_ptn_idx)
	_set_top_bullets_pos()


func _remove_top_bullets() -> void:
	for i in parts_num:
		parts[i].remove_top_bullets()


func _set_top_bullets_pos() -> void:
	for i in parts_num:
		parts[i].set_top_bullets_pos(pos.x, pos.y)


func _check_wounded_parts() -> void:
	for i in parts_num:
		var ep: EnemyPart = parts[i]
		if ep.shield > 0:
			ep.wounded = false
			if ep.shield < ep.first_shield / 2:
				if (cnt & 15) < 3:
					ep.wounded = true
			elif ep.shield < ep.first_shield / 3:
				if (cnt & 7) < 3:
					ep.wounded = true
			elif ep.shield < ep.first_shield / 4:
				if (cnt & 3) < 3:
					ep.wounded = true


func move() -> void:
	var f: Field = Game.I.field
	if move_bullet != null:
		pos = move_bullet.pos
		if f.check_hit_box(pos, spec.size_xm * 2, spec.size_xp * 2, spec.size_ym * 2, spec.size_yp * 2):
			remove()
			return
	else:
		_points_move()
		if not exists:
			return
		if not reach_first_point:
			cnt += 1
			Enemy.total_num += 1
			return
	fire_cnt -= 1
	var af: TData.AttackForm = spec.attack_form[attack_form_idx]
	if fire_cnt < 0:
		fire_cnt = af.attack_period[attack_ptn_idx] + af.break_period[attack_ptn_idx]
		barrage_ptn_idx = af.barrage_ptn_start_idx + attack_ptn_idx
		_add_top_bullets()
		attack_ptn_idx += 1
		if attack_ptn_idx >= af.attack_period.size():
			attack_ptn_idx = 0
		if move_bullet == null and pattern.point.has(barrage_ptn_idx):
			move_point_idx = 0
			reach_first_point = false
	elif fire_cnt < af.break_period[attack_ptn_idx]:
		_remove_top_bullets()
	_set_top_bullets_pos()
	_check_wounded_parts()
	cnt += 1
	Enemy.total_num += 1


func draw(r: BlockRenderer) -> void:
	var z := -0.5
	for i in parts_num:
		var p: EnemyPart = parts[i]
		if p.shield > 0:
			p.draw(r, pos, z)
		if i == 0:
			z += 0.2
		else:
			z += 0.05


func _check_attack_form_change() -> void:
	var ai := attack_form_idx + 1
	if ai >= spec.attack_form.size():
		return
	if spec.attack_form[ai].shield < parts[0].shield:
		return
	attack_form_idx += 1
	_remove_top_bullets()
	for i in parts_num:
		var ep: EnemyPart = parts[i]
		if ep.shield > 0 and ep.spec.destroyed_form_idx >= 0 and attack_form_idx >= ep.spec.destroyed_form_idx:
			ep.break_into_fragments(pos)
			ep.remove()
	Game.I.particles.add(8, pos, 0, PI * 2, 0.3, 3, Particles.SMOKE)
	Game.I.sound.play_se(Sound.ENEMY_DESTROYED)
	fire_cnt = 100
	attack_ptn_idx = 0
	barrage_ptn_idx = 0


func check_hit(p: Vector2, damage: float) -> bool:
	var g := Game.I
	var dm := damage
	if move_bullet == null and not reach_first_point_first:
		dm = 0
	for i in parts_num:
		var ep: EnemyPart = parts[i]
		if ep.check_hit(p, dm, pos):
			if ep.shield <= 0:
				if ep.spec.damage_to_main_body > 0:
					parts[0].shield -= ep.spec.damage_to_main_body
					g.particles.add(5, pos, 0, PI * 2, 0.1, 2, Particles.SMOKE)
				g.add_score(ep.spec.tumiki_set.score, p)
				if ep.first_shield <= 1:
					g.sound.play_se(Sound.SMALL_ENEMY_DESTROYED)
				else:
					g.sound.play_se(Sound.ENEMY_DESTROYED)
				ep.remove()
				g.particles.add(8, p, 0, PI * 2, 1, 0.5, Particles.SPARK)
				var sp = g.splinters.get_instance()
				if sp != null:
					sp.set_from_enemy(pos.x + ep.spec.ofs.x, pos.y + ep.spec.ofs.y, ep.spec.tumiki_set,
						barrage_ptn_idx, is_boss and i == 0)
				if parts[0].shield <= 0:
					if i != 0:
						parts[0].spec.tumiki_set.break_into_fragments(pos.x, pos.y, 0)
					_break_into_fragments()
					if is_boss:
						var sc := g.boss_destroyed()
						g.add_score(sc, p)
					remove()
				else:
					for j in i:
						parts[j].activate_covered_top_bullets(barrage_ptn_idx)
			_set_top_bullets_pos()
			_check_attack_form_change()
			g.gauge.add(ep)
			return true
	return false


class EnemyPart:
	var top_bullets: Array = []
	var shield := 0.0
	var first_shield := 0.0
	var damaged := false
	var wounded := false
	var spec: TData.PartSpec
	var cover_parts: Array = []

	func set_part(s: TData.PartSpec) -> void:
		spec = s
		first_shield = s.shield
		shield = s.shield
		wounded = false
		damaged = false
		top_bullets = []
		cover_parts = []

	func remove() -> void:
		remove_top_bullets()
		shield = -1

	func add_top_bullets(idx: int) -> void:
		if shield <= 0:
			return
		top_bullets = spec.tumiki_set.add_top_bullets(idx, Game.I.ship, Bullets.ENEMY)

	func activate_covered_top_bullets(idx: int) -> void:
		if shield <= 0:
			return
		for etb in top_bullets:
			if etb.deactivated:
				etb.actor = etb.tumiki.add_top_bullet(idx, Game.I.ship, Bullets.ENEMY)
				etb.deactivated = false
				etb.cover_checked = false

	func remove_top_bullets() -> void:
		for etb in top_bullets:
			if etb.actor != null:
				etb.actor.remove_forced()
				etb.actor = null

	func set_top_bullets_pos(x: float, y: float) -> void:
		if shield <= 0:
			return
		for etb in top_bullets:
			if etb.actor != null:
				var ofsx: float = spec.ofs.x + etb.tumiki.ofs.x
				var ofsy: float = spec.ofs.y + etb.tumiki.ofs.y
				etb.actor.pos = Vector2(x + ofsx, y + ofsy)
				if not cover_parts.is_empty() and not etb.cover_checked:
					for cp in cover_parts:
						if cp.shield > 0 and cp.covers(ofsx, ofsy):
							etb.actor.remove_forced()
							etb.actor = null
							etb.deactivated = true
							break
					etb.cover_checked = true

	func covers(x: float, y: float) -> bool:
		var ox := x - spec.ofs.x
		var oy := y - spec.ofs.y
		var ts := spec.tumiki_set
		return ts.size_xm <= ox and ox <= ts.size_xp and ts.size_ym <= oy and oy <= ts.size_yp

	func check_hit(p: Vector2, damage: float, ppos: Vector2) -> bool:
		if shield <= 0:
			return false
		var f := spec.tumiki_set.check_hit(p, ppos.x + spec.ofs.x, ppos.y + spec.ofs.y)
		if f and damage > 0:
			shield -= damage
			damaged = true
			Game.I.sound.play_se(Sound.ENEMY_DAMAGED)
		return f

	func break_into_fragments(p: Vector2) -> void:
		spec.tumiki_set.break_into_fragments(p.x + spec.ofs.x, p.y + spec.ofs.y, 0)

	func draw(r: BlockRenderer, p: Vector2, z: float) -> void:
		spec.tumiki_set.draw_xy(r, p.x + spec.ofs.x, p.y + spec.ofs.y, z, damaged, wounded)
		damaged = false
