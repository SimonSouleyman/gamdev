class_name AmbienceSynth
extends RefCounted
## Placeholder ambience made in code (design doc: ambience only, no music; CC0 recordings later).
## Loops are seamless: noise loops crossfade their tail into their head, tones use whole cycles.
## All randomness from a seeded generator.

const RATE: int = 22050


static func _to_wav(samples: PackedFloat32Array, loop: bool, rate: int = RATE) -> AudioStreamWAV:
	# A few extra frames past the end (a copy of the loop start for loops, silence otherwise),
	# so the resampler never reads beyond the buffer.
	var pad := 16
	var bytes := PackedByteArray()
	bytes.resize((samples.size() + pad) * 2)
	for i in range(samples.size() + pad):
		var v := samples[i] if i < samples.size() else (samples[i - samples.size()] if loop else 0.0)
		bytes.encode_s16(i * 2, int(clampf(v, -1.0, 1.0) * 32000.0))
	var w := AudioStreamWAV.new()
	w.format = AudioStreamWAV.FORMAT_16_BITS
	w.mix_rate = rate
	w.stereo = false
	w.data = bytes
	if loop:
		w.loop_mode = AudioStreamWAV.LOOP_FORWARD
		w.loop_begin = 0
		w.loop_end = samples.size()
	return w


## Makes a noise-based loop seamless: the extra `fade` samples after the end are blended into the start.
static func _seamless(raw: PackedFloat32Array, length: int) -> PackedFloat32Array:
	var fade := raw.size() - length
	var out := raw.slice(0, length)
	for i in range(fade):
		var k := float(i) / float(fade)
		out[i] = raw[i] * k + raw[length + i] * (1.0 - k)
	return out


## Wind in grass and leaves: low-passed noise with slow gusts.
static func wind(seconds: float = 8.0, seed: int = 1) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed
	var n := int(seconds * RATE)
	var fade := RATE / 2
	var raw := PackedFloat32Array()
	raw.resize(n + fade)
	var lp := 0.0
	var lp2 := 0.0
	for i in range(n + fade):
		var t := float(i % n) / float(n)
		# Gusts: whole cycles per loop, so the envelope loops too.
		var gust := 0.55 + 0.25 * sin(TAU * t * 1.0) + 0.2 * sin(TAU * t * 3.0 + 1.3)
		var cutoff := 0.02 + 0.03 * gust
		lp += (rng.randf_range(-1.0, 1.0) - lp) * cutoff
		lp2 += (lp - lp2) * cutoff * 1.5
		raw[i] = lp2 * 9.0 * gust
	return _to_wav(_seamless(raw, n), true)


## The root run's sound (0.8.1, broken list 27; Simon: "it can be just four low notes playing in
## turn"): a full, warm hum (a soft D major chord in low voices, each voice two slightly detuned
## copies that breathe slowly) and a slow melody of four low notes, one every NOTE_SECONDS, each
## a soft swell that dies away into the next. Mono at SONG_RATE (nothing in it above about 1.2 kHz,
## so it is never bright), a seamless loop of four notes: the tones are whole cycles per loop and
## each note's tail wraps into the loop's start.
const SONG_RATE: int = 11025
const NOTE_SECONDS := 5.0
## The four notes in turn (Hz): D3, F#3, E3, A2.
const SONG_NOTES: Array[float] = [146.85, 185.0, 164.8, 110.0]
## The hum's chord: [Hz, gain] (D2, A2, D3, F#3, A3), each played twice, 0.1 Hz apart.
const SONG_CHORD: Array = [[73.4, 0.6], [110.0, 0.55], [146.85, 0.42], [185.0, 0.26], [220.0, 0.16]]
const SONG_MELODY_GAIN := 0.95
## The loudest sample of the loop (the layer's level in Ambience sets how loud it is heard).
const SONG_PEAK := 0.8


static func _table(harmonics: Array) -> PackedFloat32Array:
	var tab := PackedFloat32Array()
	tab.resize(2048)
	for i in range(2048):
		var x := TAU * float(i) / 2048.0
		var v := 0.0
		for h in range(harmonics.size()):
			v += float(harmonics[h]) * sin(x * (h + 1))
		tab[i] = v
	return tab


## Rounds a frequency to whole cycles in `seconds`, so a held tone loops without a click.
static func _whole(hz: float, seconds: float) -> float:
	return roundf(hz * seconds) / seconds


static func night_song() -> AudioStreamWAV:
	var seconds := NOTE_SECONDS * SONG_NOTES.size()
	var n := int(seconds * SONG_RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	# The hum: a warm tone (soft harmonics, falling fast) per voice, each its own slow breath.
	var warm := _table([1.0, 0.4, 0.18, 0.08, 0.03])
	var sine := _table([1.0])
	var v := 0
	for c: Array in SONG_CHORD:
		for d in [-0.05, 0.05]:
			var f := _whole(float(c[0]) + d, seconds)
			var step := f / SONG_RATE * 2048.0
			var gain := float(c[1]) * 0.5
			# Its breath: 1 to 3 whole swells a loop, each voice at its own point of it.
			var bstep := (1.0 + float(v % 3)) * 2048.0 / n
			var bph := float(v * 617 % 2048)
			var ph := float(v * 211 % 2048)
			for i in range(n):
				out[i] += warm[int(ph) & 2047] * gain * (0.8 + 0.2 * sine[int(bph) & 2047])
				ph += step
				if ph >= 2048.0:
					ph -= 2048.0
				bph += bstep
			v += 1
	# The melody: a soft rounded tone, a slow swell (0.4 s) and a long fall, wrapping at the end.
	var soft := _table([1.0, 0.45, 0.16, 0.06])
	var note_len := int(NOTE_SECONDS * 1.6 * SONG_RATE)
	for k in range(SONG_NOTES.size()):
		var start := int(k * NOTE_SECONDS * SONG_RATE)
		var step := SONG_NOTES[k] / SONG_RATE * 2048.0
		var ph := 0.0
		for i in range(note_len):
			var t := float(i) / SONG_RATE
			var env := (0.5 - 0.5 * cos(PI * minf(t / 0.4, 1.0))) * exp(-t / 2.4)
			# The last half second fades to nothing, so the wrap never clicks.
			env *= clampf((float(note_len - i) / SONG_RATE) / 0.5, 0.0, 1.0)
			out[(start + i) % n] += soft[int(ph) & 2047] * env * SONG_MELODY_GAIN
			ph += step
			if ph >= 2048.0:
				ph -= 2048.0
	var peak := 0.0
	for i in range(n):
		peak = maxf(peak, absf(out[i]))
	var scale := SONG_PEAK / maxf(peak, 0.001)
	for i in range(n):
		out[i] *= scale
	return _to_wav(out, true, SONG_RATE)


## Faint insects: a high buzz with a fast flutter, fading in and out.
static func insects(seconds: float = 6.0, seed: int = 3) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed
	var n := int(seconds * RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	for i in range(n):
		var t := float(i) / RATE
		var env := 0.5 + 0.5 * sin(TAU * t * 2.0 / seconds)
		var flutter := 0.5 + 0.5 * sin(TAU * 32.0 * t)
		out[i] = sin(TAU * 4200.0 * t + 0.3 * rng.randf()) * flutter * env * 0.05
	return _to_wav(out, true)


## One short bird call: a few rising and falling whistled notes.
static func chirp(variant: int = 0) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = 100 + variant
	var notes := rng.randi_range(2, 5)
	var out := PackedFloat32Array()
	var phase := 0.0
	for _k in range(notes):
		var dur := rng.randf_range(0.06, 0.16)
		var f0 := rng.randf_range(2400.0, 3600.0)
		var f1 := f0 + rng.randf_range(-900.0, 1200.0)
		var m := int(dur * RATE)
		for i in range(m):
			var x := float(i) / m
			var f := lerpf(f0, f1, x) + 120.0 * sin(TAU * 28.0 * x * dur)
			phase += TAU * f / RATE
			var env := sin(PI * x)
			out.append(sin(phase) * env * env * 0.35)
		for _i in range(int(rng.randf_range(0.03, 0.09) * RATE)):
			out.append(0.0)
	return _to_wav(out, false)


## A short soft bell-like note for drinking a dot.
static func pling() -> AudioStreamWAV:
	var n := int(0.5 * RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	for i in range(n):
		var t := float(i) / RATE
		var env := minf(1.0, t * 200.0) * exp(-t * 7.0)
		out[i] = (sin(TAU * 880.0 * t) * 0.6 + sin(TAU * 1320.0 * t) * 0.25 + sin(TAU * 2200.0 * t) * 0.1 * exp(-t * 20.0)) * env * 0.4
	return _to_wav(out, false)


## A wren's song (0.8, the brush pile): about five seconds of loud, fast, high notes: a few
## clear notes, then trills and a rattle, each phrase at its own pitch, ending on a flourish.
static func wren_song(variant: int = 0) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = 700 + variant
	var out := PackedFloat32Array()
	var phase := 0.0
	# [notes, note seconds, gap seconds, start Hz, sweep Hz per note]
	var phrases := [
		[3, 0.07, 0.07, rng.randf_range(4200.0, 5200.0), -900.0],
		[9, 0.035, 0.018, rng.randf_range(5600.0, 6800.0), 1200.0],
		[14, 0.022, 0.012, rng.randf_range(3600.0, 4400.0), -700.0],
		[6, 0.05, 0.03, rng.randf_range(6000.0, 7200.0), -1800.0],
		[18, 0.016, 0.01, rng.randf_range(4400.0, 5000.0), 600.0],
		[4, 0.06, 0.04, rng.randf_range(5200.0, 6200.0), 1500.0],
	]
	for ph: Array in phrases:
		for k in range(int(ph[0])):
			var dur := float(ph[1]) * rng.randf_range(0.85, 1.15)
			var f0 := float(ph[3]) + rng.randf_range(-120.0, 120.0)
			var f1 := f0 + float(ph[4])
			var m := int(dur * RATE)
			for i in range(m):
				var x := float(i) / m
				var f := lerpf(f0, f1, x)
				phase += TAU * f / RATE
				var env := sin(PI * x)
				out.append((sin(phase) * 0.85 + sin(phase * 2.0) * 0.1) * env * 0.4)
			for _i in range(int(float(ph[2]) * RATE)):
				out.append(0.0)
		for _i in range(int(rng.randf_range(0.05, 0.12) * RATE)):
			out.append(0.0)
	return _to_wav(out, false)
