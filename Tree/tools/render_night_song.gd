extends SceneTree
## Renders the root run's sound (0.8.1, broken list 27) and the tree mode's ambience headless, through
## the game's own Ambience (its layers, levels and fades), into WAV files, and prints their levels:
##   godot --headless --path . -s tools/render_night_song.gd -- <folder>
## Writes root_run_60s.wav (a 60 s night: the dive at 0.5 s, the night ending at 56 s, as main.gd
## times them), song_only_60s.wav (the song layer alone at its level), tree_day_60s.wav and
## tree_night_60s.wav (the tree mode by day and at dusk, before the dive). Levels in dBFS:
## RMS over the steady part, the loudest sample, and the RMS of the song once the night is over.

const SECONDS := 60.0
const BLOCK := 512


var _done := false


func _process(_delta: float) -> bool:
	if not _done:
		_done = true
		_run()
	return false


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var dir := args[0] if args.size() > 0 else "user://night_song"
	DirAccess.make_dir_recursive_absolute(dir)
	var amb := Ambience.new()
	root.add_child(amb)
	var t0 := Time.get_ticks_msec()
	amb.song_ready(true)
	print("night_song made in %d ms" % (Time.get_ticks_msec() - t0))
	var rate := AudioServer.get_mix_rate()

	var run := _render(amb, rate, "run", [])
	_save(run, rate, dir.path_join("root_run_60s.wav"))
	var song := _render(amb, rate, "run", ["hum"])
	_save(song, rate, dir.path_join("song_only_60s.wav"))
	var day := _render(amb, rate, "day", [])
	_save(day, rate, dir.path_join("tree_day_60s.wav"))
	var dusk := _render(amb, rate, "dusk", [])
	_save(dusk, rate, dir.path_join("tree_night_60s.wav"))

	var steady := [int(10.0 * rate), int(50.0 * rate)]
	print("level (dBFS)            RMS 10-50 s   peak")
	for row in [["root run (song+water)", run], ["song alone", song], ["tree mode, day", day], ["tree mode, dusk", dusk]]:
		var buf: PackedFloat32Array = row[1]
		print("%-22s  %8.1f   %8.1f" % [row[0], _db(_rms(buf, steady[0], steady[1])), _db(_peak(buf, 0, buf.size()))])
	print("song in the first 0.5 s (before the dive): %.1f dBFS" % _db(_rms(song, 0, int(0.5 * rate))))
	for s in [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]:
		print("song %.0f s into the dive: %.1f dBFS" % [s - 0.5, _db(_rms(song, int((s - 0.25) * rate), int((s + 0.25) * rate)))])
	print("song 0.5 s after the night ends: %.1f dBFS" % _db(_rms(song, int(56.9 * rate), int(57.1 * rate))))
	print("song 1.5 s after the night ends (tree mode): %.1f dBFS" % _db(_rms(song, int(57.6 * rate), int(60.0 * rate))))
	amb.queue_free()
	quit()


## A 60 s render. "run": tree mode at dusk, the dive at 0.5 s, the night ending at 56 s; "day" and
## "dusk": the tree mode only. `only`: the layers to hear (all when empty).
func _render(amb: Ambience, rate: float, kind: String, only: Array) -> PackedFloat32Array:
	var layers: Dictionary = amb._layers
	var pbs := {}
	for name in layers:
		var p: AudioStreamPlayer = layers[name]
		p.volume_db = Ambience.SILENT_DB
		p.stream_paused = true
		var pb: AudioStreamPlayback = p.stream.instantiate_playback()
		pb.start(0.0)
		pbs[name] = pb
	amb.daylight = kind == "day"
	amb.set_world(true, 0.01)
	# Settle the tree mode's layers first (they are at their level when the dive begins).
	for _i in range(60):
		amb._process(0.1)
	var out := PackedFloat32Array()
	var frames := int(SECONDS * rate)
	out.resize(frames)
	var events := []
	if kind == "run":
		# main.gd: _dive sets the world to below over 2.2 s, then 0.8 s at the black (1.6 s on);
		# _rise sets it above over 2.5 s after 0.6 s of fading to black.
		events = [[0.5, false, 2.2], [2.1, false, 0.8], [56.6, true, 2.5]]
	var pos := 0
	while pos < frames:
		var now := pos / rate
		while not events.is_empty() and now >= float(events[0][0]):
			amb.set_world(bool(events[0][1]), float(events[0][2]))
			events.pop_front()
		var n := mini(BLOCK, frames - pos)
		amb._process(n / rate)
		for name in layers:
			if not only.is_empty() and not only.has(name):
				continue
			var p: AudioStreamPlayer = layers[name]
			if p.stream_paused:
				continue
			var gain := db_to_linear(p.volume_db)
			var mixed: PackedVector2Array = (pbs[name] as AudioStreamPlayback).mix_audio(1.0, n)
			for i in range(mixed.size()):
				out[pos + i] += (mixed[i].x + mixed[i].y) * 0.5 * gain
		pos += n
	return out


func _rms(buf: PackedFloat32Array, a: int, b: int) -> float:
	var s := 0.0
	for i in range(a, b):
		s += buf[i] * buf[i]
	return sqrt(s / maxf(b - a, 1))


func _peak(buf: PackedFloat32Array, a: int, b: int) -> float:
	var m := 0.0
	for i in range(a, b):
		m = maxf(m, absf(buf[i]))
	return m


func _db(x: float) -> float:
	return linear_to_db(maxf(x, 1e-6))


func _save(buf: PackedFloat32Array, rate: float, path: String) -> void:
	var bytes := PackedByteArray()
	bytes.resize(buf.size() * 2)
	for i in range(buf.size()):
		bytes.encode_s16(i * 2, int(clampf(buf[i], -1.0, 1.0) * 32767.0))
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS
	w.mix_rate = int(rate)
	w.stereo = false
	w.data = bytes
	w.save_to_wav(path)
