class_name Ambience
extends Node
## Ambience per world (design doc: wind, birds and insects above, a deep calm hum below,
## crossfaded on the dive). Placeholder sounds from AmbienceSynth.

const ABOVE_DB := -8.0
const BELOW_DB := -6.0
const SILENT_DB := -60.0

var _wind: AudioStreamPlayer
var _insects: AudioStreamPlayer
var _hum: AudioStreamPlayer
var _bird: AudioStreamPlayer
var _chirps: Array[AudioStreamWAV] = []
var _rng := RandomNumberGenerator.new()
var _bird_timer: float = 3.0
var _above: bool = true
## Birds sing by day only; the tree view sets this.
var daylight: bool = true
var _tween: Tween
var _collect: AudioStreamPlayer
var _streak: int = 0
var _streak_timer: float = 0.0


func _ready() -> void:
	_rng.seed = 7
	_wind = _player(AmbienceSynth.wind(), ABOVE_DB)
	_insects = _player(AmbienceSynth.insects(), ABOVE_DB - 10.0)
	_hum = _player(AmbienceSynth.hum(), SILENT_DB)
	for v in range(4):
		_chirps.append(AmbienceSynth.chirp(v))
	_bird = AudioStreamPlayer.new()
	_bird.volume_db = ABOVE_DB - 4.0
	add_child(_bird)
	_collect = AudioStreamPlayer.new()
	_collect.stream = AmbienceSynth.pling()
	_collect.volume_db = -14.0
	_collect.max_polyphony = 4
	add_child(_collect)
	for p in [_wind, _insects, _hum]:
		(p as AudioStreamPlayer).play()


func _player(stream: AudioStream, db: float) -> AudioStreamPlayer:
	var p := AudioStreamPlayer.new()
	p.stream = stream
	p.volume_db = db
	add_child(p)
	return p


## Crossfade between the meadow and the underground.
func set_world(above: bool, seconds: float = 2.0) -> void:
	_above = above
	if _tween:
		_tween.kill()
	_tween = create_tween().set_parallel(true)
	_tween.tween_property(_wind, "volume_db", ABOVE_DB if above else SILENT_DB, seconds)
	_tween.tween_property(_insects, "volume_db", (ABOVE_DB - 10.0) if above else SILENT_DB, seconds)
	_tween.tween_property(_hum, "volume_db", SILENT_DB if above else BELOW_DB, seconds)


func is_above() -> bool:
	return _above


## A soft glassy note when the root drinks dots; a little higher for each in a row.
func play_collect(count: int) -> void:
	if _collect == null:
		return
	_collect.pitch_scale = clampf(0.9 + 0.08 * _streak + _rng.randf_range(-0.03, 0.03), 0.8, 1.8)
	_streak = mini(_streak + count, 8)
	_streak_timer = 1.2
	_collect.play()


func set_enabled(on: bool) -> void:
	AudioServer.set_bus_mute(AudioServer.get_bus_index("Master"), not on)


func _process(delta: float) -> void:
	_streak_timer -= delta
	if _streak_timer <= 0.0:
		_streak = 0
	if not _above or not daylight:
		return
	_bird_timer -= delta
	if _bird_timer <= 0.0:
		_bird_timer = _rng.randf_range(2.5, 9.0)
		_bird.stream = _chirps[_rng.randi_range(0, _chirps.size() - 1)]
		_bird.pitch_scale = _rng.randf_range(0.9, 1.15)
		_bird.play()
