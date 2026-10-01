extends RefCounted
## The root run's sound (0.8.1, broken list 27): a warm hum with four slow low notes, quieter than
## the tree mode's ambience, fading in on the dive and out at the end of the night, never heard in
## tree mode. Levels of the full mix: tools/render_night_song.gd and tools/sound_levels.py.
var t


func _samples(w: AudioStreamWAV) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	var n := w.data.size() / 2
	out.resize(n)
	for i in range(n):
		out[i] = w.data.decode_s16(i * 2) / 32000.0
	return out


func _rms(x: PackedFloat32Array, a: int, b: int) -> float:
	var s := 0.0
	for i in range(a, b):
		s += x[i] * x[i]
	return sqrt(s / maxf(b - a, 1))


func test_the_song_is_four_low_notes_over_a_warm_hum() -> void:
	var w := AmbienceSynth.night_song()
	var x := _samples(w)
	var n := int(w.loop_end)
	t.check(w.loop_mode == AudioStreamWAV.LOOP_FORWARD and w.loop_begin == 0, "it loops")
	t.check_near(float(n) / w.mix_rate, AmbienceSynth.NOTE_SECONDS * 4.0, 0.01, "four notes a loop")
	t.check(AmbienceSynth.SONG_NOTES.size() == 4, "four notes in turn (Simon)")
	for f in AmbienceSynth.SONG_NOTES:
		t.check(f >= 80.0 and f <= 200.0, "a low note (%.0f Hz)" % f)
	for c: Array in AmbienceSynth.SONG_CHORD:
		t.check(float(c[0]) <= 230.0, "the hum is low (%.0f Hz)" % float(c[0]))
	# Seamless: the loop's end runs into its start without a step.
	t.check(absf(x[n - 1] - x[0]) < 0.02, "no click at the loop (%.4f)" % absf(x[n - 1] - x[0]))
	var peak := 0.0
	for i in range(n):
		peak = maxf(peak, absf(x[i]))
	t.check(peak <= AmbienceSynth.SONG_PEAK + 0.01, "never clips")
	# Not the old flat drone: each note swells, so the level moves within a note's five seconds.
	var r := w.mix_rate
	var early := _rms(x, int(0.4 * r), int(0.9 * r))
	var late := _rms(x, int(4.2 * r), int(4.7 * r))
	t.check(early > late * 1.15, "a note swells and dies away (%.3f, %.3f)" % [early, late])
	# Warm, never bright: its mean frequency (from how fast it changes, exact for a sine) is low.
	var d := 0.0
	for i in range(1, n):
		d += (x[i] - x[i - 1]) * (x[i] - x[i - 1])
	var f_mean := sqrt(d / (n - 1)) / _rms(x, 0, n) * r / TAU
	t.check(f_mean < 400.0, "warm, not bright (mean frequency %.0f Hz)" % f_mean)


func test_the_song_is_quieter_than_the_tree_mode() -> void:
	# The tree mode's quietest steady layer, the synthesized wind, is already louder than the song
	# at its level; the forest and the birds or crickets come on top.
	var song := _samples(AmbienceSynth.night_song())
	var wind := _samples(AmbienceSynth.wind())
	var song_db := linear_to_db(_rms(song, 0, song.size())) + Ambience.HUM_DB
	var wind_db := linear_to_db(_rms(wind, 0, wind.size())) + float(Ambience.LEVELS["wind"])
	t.check(song_db < wind_db - 2.0, "the song (%.1f dB) under the tree mode's wind alone (%.1f dB)" % [song_db, wind_db])


func test_the_song_fades_in_on_the_dive_and_is_never_heard_in_tree_mode() -> void:
	var amb := Ambience.new()
	t.root.add_child(amb)
	t.check(amb.song_ready(true), "the song is made")
	var hum: AudioStreamPlayer = amb._layers["hum"]
	t.check(hum.stream is AudioStreamWAV, "the song plays in the hum layer")
	# Tree mode, by day and at night: silent, resting.
	for daylight in [true, false]:
		amb.daylight = daylight
		amb.set_world(true, 0.01)
		for _i in range(300):
			amb._process(0.1)
			t.check(hum.volume_db <= Ambience.SILENT_DB, "silent in tree mode")
		t.check(hum.stream_paused, "and not even playing")
	# The dive (or a game loaded at night, which switches at once): a fade, not a jump.
	amb.set_world(false, 0.01)
	amb._process(0.5)
	t.check(hum.volume_db < Ambience.HUM_DB - 25.0, "half a second in it is still faint (%.1f dB)" % hum.volume_db)
	t.check(not hum.stream_paused, "it has started")
	for _i in range(50):
		amb._process(0.1)
	t.check_near(hum.volume_db, Ambience.HUM_DB, 0.01, "at its level after the fade-in")
	t.check(Ambience.SONG_FADE_IN_S >= 2.0, "a slow fade-in")
	# The night ends (main.gd: set_world(true, 2.5) at the black): gone within a second.
	amb.set_world(true, 2.5)
	amb._process(0.5)
	t.check(hum.volume_db < Ambience.HUM_DB - 20.0, "fading out (%.1f dB)" % hum.volume_db)
	amb._process(0.5)
	amb._process(0.05)
	t.check(hum.volume_db <= Ambience.SILENT_DB and hum.stream_paused, "silent a second after the night ends")
	amb.queue_free()
