## Game state and actor pools (port of gamemanager.d). Runs at a fixed 60 frames per second.
class_name Game
extends Node3D

enum { START_GAME, IN_GAME, GAMEOVER, PAUSE, TITLE, END_GAME }

const STAGE_END_CNT := 360
const BOSSTIMER_FREEZED := 999999999
const LEFT_NUM := 2
const LEFT_BONUS := 30000
const FIRST_EXTEND := 200000
const EVERY_EXTEND := 500000
const CREDIT_NUM := 2
const STAGE_MESSAGE := ["WE ARE TUMIKI FIGHTERS!", "JUST OVER THE HORIZON", "PANIC ON MEADOW",
	"COASTLINE UNDER FIRE", "JUNK CITY CENTRAL"]
const SAVE_PATH := "user://save.cfg"

static var I: Game
## Screen height / width, set by Main before the game is created.
static var view_aspect := 16.0 / 9.0
## World units at the top of the field covered by the HUD text, set by Main.
var hud_top := 4.0

var state := TITLE
var stage := 0
var cnt := 0
var pause_cnt := 0
var score := 0
var hi_score := 0
var left := 0
var extend_score := 0
var boss_timer := BOSSTIMER_FREEZED
var boss_dst_cnt := -1
var credit := 0

var r: BlockRenderer
var camera: Camera3D
var sound: Sound
var field: Field
var ship: Ship
var particles: Particles
var fragments: U.Pool
var bullets: Bullets
var enemies: U.Pool
var splinters: U.Pool
var signs: U.Pool
var gauge: Effects.DamageGauge
var letters: Letters
var stage_manager: StageManager

# Input for the current frame, filled in by Main.
var in_move := Vector2.ZERO
var in_fire := false
var in_slow := false
var in_button := false
var _btn_prsd := true
var _is_continue := false


func _ready() -> void:
	I = self
	r = BlockRenderer.new()
	add_child(r)
	camera = Camera3D.new()
	camera.keep_aspect = Camera3D.KEEP_WIDTH
	camera.near = 0.1
	camera.far = 1000
	add_child(camera)
	sound = Sound.new()
	add_child(sound)
	field = Field.new()
	field.set_aspect(view_aspect)
	camera.fov = rad_to_deg(2 * atan(Field.VIEW_HALF_W / field.eye_z))
	particles = Particles.new(128)
	fragments = U.Pool.new(128, func(): return Effects.Fragment.new())
	bullets = Bullets.new(512)
	ship = Ship.new()
	splinters = U.Pool.new(144, func(): return Effects.Splinter.new())
	gauge = Effects.DamageGauge.new()
	enemies = U.Pool.new(64, func(): return Enemy.new())
	signs = U.Pool.new(32, func(): return Effects.ScoreSign.new())
	letters = Letters.new(32)
	stage_manager = StageManager.new()
	_load()
	_set_camera()
	stage = 0
	start_title()


func _set_camera() -> void:
	# The original looks at the field from z = eyeZ. The camera is rolled by -90 degrees so that
	# the original "forward" (+x) points up on a portrait screen. The field is seen from above,
	# with its full width (world y) filling the screen width.
	camera.transform = Transform3D(Basis(Vector3(0, 0, 1), -PI / 2), Vector3(0, 0, field.eye_z))


func _load() -> void:
	var cf := ConfigFile.new()
	if cf.load(SAVE_PATH) == OK:
		hi_score = cf.get_value("score", "hi", 0)


func _save() -> void:
	var cf := ConfigFile.new()
	cf.set_value("score", "hi", hi_score)
	cf.save(SAVE_PATH)


func vibrate(ms: int) -> void:
	if OS.has_feature("mobile"):
		Input.vibrate_handheld(ms)


# --- State changes ---

func start_title() -> void:
	state = TITLE
	sound.halt_music()
	if stage >= StageManager.STAGE_NUM:
		stage = StageManager.STAGE_NUM - 1
	field.start(stage)
	field.ground_y = 0
	letters.clear()
	signs.clear()
	cnt = 0
	_btn_prsd = true


func _start_in_game() -> void:
	state = IN_GAME
	score = 0
	left = LEFT_NUM
	extend_score = FIRST_EXTEND
	_start_stage()
	field.ground_y = 0
	Effects.Splinter.sign_num = 0
	signs.clear()


func start_in_game_first() -> void:
	stage = 0
	_start_in_game()
	state = START_GAME
	ship.start_stage()
	Effects.Splinter.sign_num = 2
	credit = CREDIT_NUM


func start_ending() -> void:
	state = END_GAME
	ship.back_to_home()
	add_score(left * LEFT_BONUS, ship.pos)
	left = 0
	credit = 0
	letters.add("MISSION COMPLETED!", 110, 400, 12, 500, 1)
	sound.play_bgm_once(Sound.BGM_ENDING)


func draw_warning() -> void:
	letters.add("WARNING", 132, 210, 32, 250, 0)
	letters.add("HERE COMES A GIGANTIC TOY", 80, 280, 10, 250, -3)


func set_in_game() -> void:
	state = IN_GAME


func _start_stage() -> void:
	fragments.clear()
	particles.clear()
	letters.clear()
	enemies.clear()
	bullets.clear()
	splinters.clear()
	ship.start()
	stage_manager.start(stage)
	field.start(stage)
	gauge.init()
	boss_timer = BOSSTIMER_FREEZED
	boss_dst_cnt = -1
	letters.add("STAGE %d" % (stage + 1), 180, 150, 24, 240, -4)
	var msg: String = STAGE_MESSAGE[stage]
	letters.add(msg, 320 - msg.length() * 10, 270, 10, 240, -2)
	var si := stage % (Sound.STAGE_BGM_NUM * 2 - 1)
	if si >= Sound.STAGE_BGM_NUM:
		si = Sound.STAGE_BGM_NUM * 2 - si - 2
	sound.play_bgm(Sound.BGM_STG1 + si)


func start_gameover() -> void:
	state = GAMEOVER
	letters.clear()
	cnt = 0
	if score > hi_score and not Ship.invincible:
		hi_score = score
		_save()
	sound.fade_music()


func toggle_pause() -> void:
	if state == IN_GAME:
		state = PAUSE
		pause_cnt = 0
	elif state == PAUSE:
		state = IN_GAME


func add_score(sc: int, p: Vector2) -> void:
	if sc <= 0:
		return
	score += sc
	var ss = signs.get_instance_forced()
	ss.set_sign(p, sc, minf(0.3 + float(sc) / 3000, 1.2))
	if score > extend_score:
		sound.play_se(Sound.EXTEND)
		left += 1
		if extend_score <= FIRST_EXTEND:
			extend_score = EVERY_EXTEND
		else:
			extend_score += EVERY_EXTEND


func ship_destroyed() -> void:
	bullets.clear_visible()
	left -= 1
	if left < 0:
		start_gameover()


func boss_destroyed() -> int:
	sound.fade_music()
	bullets.clear_visible()
	ship.stuck.remove_all_enemies()
	bullets.clear()
	if boss_dst_cnt < 0:
		boss_dst_cnt = 0
	var bs := clampi(int(boss_timer / 60) * 10 + 10000, 10000, 20000)
	return bs * 3


func boss_in_attack(tm: int) -> void:
	boss_timer = int(tm * 16.66667)


func set_rank(rk: float) -> void:
	stage_manager.set_rank(rk)


func splinters_check_hit(p: Vector2) -> bool:
	for sp in splinters.actor:
		if sp.exists and sp.check_hit(p):
			return true
	return false


func enemies_check_hit(p: Vector2, damage: float) -> bool:
	for en in enemies.actor:
		if en.exists and en.check_hit(p, damage):
			return true
	return false


# --- Frame update ---

func _physics_process(_delta: float) -> void:
	step()
	draw_frame()


func step() -> void:
	match state:
		START_GAME, IN_GAME, END_GAME:
			_move_in_game()
		GAMEOVER:
			_move_gameover()
		PAUSE:
			pause_cnt += 1
		TITLE:
			_move_title()
	cnt += 1


func _button_pressed() -> bool:
	# Edge detection like the original btnPrsd logic.
	if in_button:
		if not _btn_prsd:
			_btn_prsd = true
			return true
	else:
		_btn_prsd = false
	return false


func _move_title() -> void:
	if cnt <= 16:
		_btn_prsd = true
	elif _button_pressed():
		start_in_game_first()
		return
	field.move()
	TData.propeller_cnt += 1


func _move_in_game() -> void:
	if boss_dst_cnt > STAGE_END_CNT - 120 and stage >= StageManager.STAGE_NUM - 1:
		stage += 1
		boss_dst_cnt = -1
		boss_timer = BOSSTIMER_FREEZED
		start_ending()
		return
	if boss_dst_cnt > STAGE_END_CNT:
		stage += 1
		_start_stage()
		return
	elif boss_dst_cnt == STAGE_END_CNT - 119:
		sound.play_se(Sound.PROPELLER)
	if state == IN_GAME:
		stage_manager.move()
	field.move()
	splinters.move()
	if boss_dst_cnt > STAGE_END_CNT - 120:
		bullets.clear_visible()
		bullets.clear()
		ship.end_move()
	elif state == IN_GAME:
		ship.move(in_move, in_fire, in_slow)
	elif state == START_GAME:
		ship.start_move()
	else:
		ship.back_to_home_move(in_button)
	Bullets.total_bullets_speed = 0
	bullets.move()
	Enemy.total_num = 0
	enemies.move()
	particles.move()
	fragments.move()
	signs.move()
	gauge.move()
	letters.move()
	TData.propeller_cnt += 1
	if boss_dst_cnt >= 0:
		boss_dst_cnt += 1
		ship.stuck.remove_all_enemies()
	elif boss_timer < BOSSTIMER_FREEZED:
		boss_timer -= 17
		if boss_timer < 0:
			bullets.clear_visible()
			bullets.clear()
			ship.stuck.remove_all_enemies()
			boss_timer = 0
			boss_dst_cnt = 0
			sound.fade_music()


func _move_gameover() -> void:
	var goto_next := false
	if cnt == 48:
		letters.add("GAME OVER", 100, 230, 28, 400, 4)
	if cnt <= 64:
		_btn_prsd = true
	elif _button_pressed() and credit <= 0:
		goto_next = true
	if (cnt > 64 and goto_next) or cnt > 700:
		if credit > 0 and cnt <= 700 and _is_continue:
			credit -= 1
			_start_in_game()
		else:
			start_title()
		return
	field.move()
	bullets.move()
	enemies.move()
	particles.move()
	fragments.move()
	letters.move()
	TData.propeller_cnt += 1


## Called by the HUD when the player picks YES/NO on the continue screen.
func choose_continue(yes: bool) -> void:
	_is_continue = yes
	if state == GAMEOVER and cnt > 64:
		if yes and credit > 0:
			credit -= 1
			_start_in_game()
		else:
			start_title()


func draw_frame() -> void:
	r.begin_frame()
	match state:
		START_GAME, IN_GAME, PAUSE, END_GAME:
			field.draw(r)
			enemies_draw()
			for sp in splinters.actor:
				if sp.exists:
					sp.draw(r)
			if state == START_GAME:
				ship.draw_friendly(r, false)
			elif state == END_GAME:
				ship.draw_friendly(r, true)
			ship.draw(r)
			particles.draw(r)
			for f in fragments.actor:
				if f.exists:
					f.draw(r)
			bullets.draw(r)
			gauge.draw(r)
			_draw_left()
		GAMEOVER:
			field.draw(r)
			enemies_draw()
			particles.draw(r)
			for f in fragments.actor:
				if f.exists:
					f.draw(r)
			bullets.draw(r)
		TITLE:
			field.draw(r)
	r.end_frame()


func enemies_draw() -> void:
	for en in enemies.actor:
		if en.exists:
			en.draw(r)


func _draw_left() -> void:
	var x := -field.size.x * 0.85
	var sz := 0.4
	for i in left:
		ship.draw_left(r, x / sz, -field.size.y * 0.82 / sz, 1 / sz, sz)
		x += 2
