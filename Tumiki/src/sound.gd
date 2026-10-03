## BGM and sound effects (port of soundmanager.d).
class_name Sound
extends Node

enum { SHIP_SHOT, STUCK, STUCK_BONUS, STUCK_DESTROYED, SHIP_DESTROYED,
	ENEMY_DAMAGED, SMALL_ENEMY_DESTROYED, ENEMY_DESTROYED, BOSS_DESTROYED,
	EXTEND, WARNING, PROPELLER, STUCK_BONUS_PUSHIN }
enum { BGM_STG1, BGM_STG2, BGM_STG3, BGM_BOSS, BGM_LAST_BOSS, BGM_ENDING }

const STAGE_BGM_NUM := 3
const BGM_FILES := ["we_are_tumiki_fighters.ogg", "just_over_the_horizon.ogg", "panic_on_meadow.ogg",
	"here_comes_a_gigantic_toy.ogg", "battle_over_the_junk_city.ogg", "return_to_home.ogg"]
const SE_FILES := ["ship_shot.wav", "stuck.wav", "stuck_bonus.wav", "stuck_destroyed.wav",
	"ship_destroyed.wav", "enemy_damaged.wav", "small_enemy_destroyed.wav", "enemy_destroyed.wav",
	"boss_destroyed.wav", "extend.wav", "warning.wav", "propeller.wav", "stuck_bonus_pushin.wav"]
const SE_CHANNEL := [0, 1, 2, 3, 2, 4, 5, 6, 6, 7, 7, 7, 2]
const FADE_TIME := 1.28

var bgm: Array = []
var se: Array = []
var channels: Array = []
var music: AudioStreamPlayer
var fade_tween: Tween
var se_volume_db := -4.0
var bgm_volume_db := -6.0


func _ready() -> void:
	for f in BGM_FILES:
		bgm.append(load("res://sounds/" + f))
	for f in SE_FILES:
		se.append(load("res://sounds/" + f))
	for i in 8:
		var p := AudioStreamPlayer.new()
		p.volume_db = se_volume_db
		add_child(p)
		channels.append(p)
	music = AudioStreamPlayer.new()
	music.volume_db = bgm_volume_db
	add_child(music)


func _state_ok(with_end: bool) -> bool:
	var s: int = Game.I.state
	return s == Game.IN_GAME or s == Game.START_GAME or (with_end and s == Game.END_GAME)


func play_bgm(n: int, loop: bool = true) -> void:
	if not _state_ok(true):
		return
	if fade_tween != null:
		fade_tween.kill()
	var st: AudioStreamOggVorbis = bgm[n]
	st.loop = loop
	music.stream = st
	music.volume_db = bgm_volume_db
	music.play()


func play_bgm_once(n: int) -> void:
	play_bgm(n, false)


func fade_music() -> void:
	if not music.playing:
		return
	if fade_tween != null:
		fade_tween.kill()
	fade_tween = create_tween()
	fade_tween.tween_property(music, "volume_db", -60.0, FADE_TIME)
	fade_tween.tween_callback(music.stop)


func halt_music() -> void:
	if fade_tween != null:
		fade_tween.kill()
	music.stop()


func play_se(n: int) -> void:
	if not _state_ok(false):
		return
	var p: AudioStreamPlayer = channels[SE_CHANNEL[n]]
	p.stream = se[n]
	p.play()
