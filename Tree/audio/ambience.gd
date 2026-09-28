class_name Ambience
extends Node
## Ambience per world (design doc: ambience only, no music). Above: a recorded forest with birds
## by day, a second bird recording on top, crickets as the sun goes down, a soft wind from
## AmbienceSynth. Below: a deep calm hum and water trickling somewhere in the soil. Crossfaded on
## the dive and with the time of day. Recordings are CC0 (assets/CREDITS.md; Simon, play test 4).

const ABOVE_DB := -8.0
const BELOW_DB := -6.0
const SILENT_DB := -60.0
## How loud each layer is at full presence.
const LEVELS := {"forest": -6.0, "birds": -14.0, "crickets": -12.0, "wind": -18.0, "hum": -6.0, "water": -20.0, "rain": -9.0}
## How fast layers follow their targets (dB per second).
const FADE_DB_PER_S := 30.0

var _layers: Dictionary = {}  # name -> AudioStreamPlayer
var _above: bool = true
## Birds sing by day only; the tree view sets this.
var daylight: bool = true
## A shower above, 0..1 (the tree view's weather, design doc section 17); birds fall quiet in it.
var rain: float = 0.0
var _thunder: AudioStreamPlayer
var _collect: AudioStreamPlayer
var _streak: int = 0
var _streak_timer: float = 0.0
var _rng := RandomNumberGenerator.new()
## Seconds a set_world crossfade takes.
var _fade_speed: float = FADE_DB_PER_S


func _ready() -> void:
	_rng.seed = 7
	_layer("forest", _loop(load("res://assets/sounds/forest_ambience.mp3")))
	_layer("birds", _loop(load("res://assets/sounds/birds.ogg")))
	_layer("crickets", _loop(load("res://assets/sounds/crickets.mp3")))
	_layer("wind", AmbienceSynth.wind())
	_layer("hum", AmbienceSynth.hum())
	_layer("water", _loop(load("res://assets/sounds/water_flowing.ogg")))
	_layer("rain", _loop(load("res://assets/sounds/rain.ogg")))
	_thunder = AudioStreamPlayer.new()
	_thunder.stream = load("res://assets/sounds/thunder.ogg")
	_thunder.volume_db = -13.0
	add_child(_thunder)
	_collect = AudioStreamPlayer.new()
	_collect.stream = AmbienceSynth.pling()
	_collect.volume_db = -14.0
	_collect.max_polyphony = 4
	add_child(_collect)


func _loop(stream: AudioStream) -> AudioStream:
	if stream is AudioStreamMP3:
		(stream as AudioStreamMP3).loop = true
	elif stream is AudioStreamOggVorbis:
		(stream as AudioStreamOggVorbis).loop = true
	return stream


func _layer(name: String, stream: AudioStream) -> void:
	var p := AudioStreamPlayer.new()
	p.stream = stream
	p.volume_db = SILENT_DB
	add_child(p)
	# Each layer starts at its own point, so the loops do not line up.
	p.play(_rng.randf() * 5.0)
	_layers[name] = p


## Crossfade between the meadow and the underground.
func set_world(above: bool, seconds: float = 2.0) -> void:
	_above = above
	_fade_speed = 60.0 / maxf(seconds, 0.05)


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


## Distant thunder: a deep, soft roll far away (sound only). Not heard underground.
func play_thunder() -> void:
	if _thunder == null or not _above:
		return
	_thunder.pitch_scale = _rng.randf_range(0.55, 0.72)
	_thunder.volume_db = _rng.randf_range(-16.0, -11.0)
	_thunder.play()


func set_enabled(on: bool) -> void:
	AudioServer.set_bus_mute(AudioServer.get_bus_index("Master"), not on)


func _target(name: String) -> float:
	var on := false
	match name:
		"forest", "birds":
			on = _above and daylight
		"crickets":
			on = _above and not daylight
		"wind":
			on = _above
		"hum", "water":
			on = not _above
		"rain":
			on = _above and rain > 0.01
			if on:
				return float(LEVELS[name]) + linear_to_db(clampf(rain, 0.05, 1.0))
	if name == "birds" and rain > 0.3:
		on = false
	return float(LEVELS[name]) if on else SILENT_DB


func _process(delta: float) -> void:
	_streak_timer -= delta
	if _streak_timer <= 0.0:
		_streak = 0
	for name in _layers:
		var p: AudioStreamPlayer = _layers[name]
		p.volume_db = move_toward(p.volume_db, _target(name), _fade_speed * delta)
