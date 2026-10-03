## Handles the appearance of enemies (port of stagemanager.d).
class_name StageManager

const STAGE_NUM := 5
const APPEARANCE_MAX := 8

var rank := 0.0
var speed_rank := 1.0
var stages: Array = []
var pattern: Array = []
var pattern_idx := 0
var next_app: TData.AppearancePattern = null
var cnt := 0
var appearance: Array = []
var ea_idx := 0
var rand := U.Rand.new()
var warning_cnt := 0
var boss_coming := false


class Appearance:
	var pattern: TData.AppearancePattern
	var cnt := -1
	var wait := false
	var is_boss := false


func _init() -> void:
	for i in APPEARANCE_MAX:
		appearance.append(Appearance.new())
	ea_idx = APPEARANCE_MAX
	for i in STAGE_NUM:
		stages.append(TData.StagePattern.new("stg%d.stg" % (i + 1)))


func start(sn: int) -> void:
	var sp: TData.StagePattern = stages[sn]
	pattern = sp.pattern
	pattern_idx = 0
	next_app = _next()
	rand.set_seed(sp.rand_seed)
	warning_cnt = sp.warning_cnt
	Game.I.bullets.bml_rand.set_seed(sp.rand_seed)
	cnt = 0
	boss_coming = false
	rank = 0
	speed_rank = 1
	for ea in appearance:
		ea.cnt = -1


func _next() -> TData.AppearancePattern:
	if pattern_idx >= pattern.size():
		return null
	pattern_idx += 1
	return pattern[pattern_idx - 1]


func _set_appearance(eap: TData.AppearancePattern, is_boss: bool) -> void:
	ea_idx -= 1
	if ea_idx < 0:
		ea_idx = APPEARANCE_MAX - 1
	var ea: Appearance = appearance[ea_idx]
	ea.pattern = eap
	ea.cnt = 0
	ea.wait = eap.wait_till_destroyed
	ea.is_boss = is_boss


func move() -> void:
	var g := Game.I
	while next_app != null and next_app.start_time <= cnt:
		_set_appearance(next_app, boss_coming)
		boss_coming = false
		next_app = _next()
	cnt += 1
	if cnt == warning_cnt:
		g.draw_warning()
		boss_coming = true
		g.sound.fade_music()
	if cnt >= warning_cnt and cnt <= warning_cnt + 200 and (cnt - warning_cnt) % 100 == 0:
		g.sound.play_se(Sound.WARNING)
	if cnt == warning_cnt + 240:
		if g.stage == STAGE_NUM - 1:
			g.sound.play_bgm(Sound.BGM_LAST_BOSS)
		else:
			g.sound.play_bgm(Sound.BGM_BOSS)
	for ea in appearance:
		if ea.cnt < 0:
			continue
		if ea.wait and Enemy.total_num <= 0:
			ea.wait = false
		var p: TData.AppearancePattern = ea.pattern
		if (ea.cnt % p.interval) == 0:
			var x: float
			var y: float
			if p.pos_type == 0:
				x = g.field.size.x - p.spec.size_xm * 1.1
				y = g.field.size.y * (p.pos + rand.next_signed_float(p.width))
			else:
				x = g.field.size.x * (p.pos + rand.next_signed_float(p.width))
				y = g.field.size.y - p.spec.size_ym * 1.1
			if not ea.wait:
				var en = g.enemies.get_instance()
				if en != null:
					en.set_enemy(x, y, p.spec, p.move, ea.is_boss)
		ea.cnt += 1
		if ea.cnt >= p.duration:
			ea.cnt = -1


func set_rank(r: float) -> void:
	rank = r * 0.24
