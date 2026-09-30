class_name AmbienceSynth
extends RefCounted
## Placeholder ambience made in code (design doc: ambience only, no music; CC0 recordings later).
## Loops are seamless: noise loops crossfade their tail into their head, tones use whole cycles.
## All randomness from a seeded generator.

const RATE: int = 22050


static func _to_wav(samples: PackedFloat32Array, loop: bool) -> AudioStreamWAV:
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
	w.mix_rate = RATE
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


## A deep calm hum for underground: low tones in whole cycles, slowly beating, with soft rumble.
static func hum(seconds: float = 8.0, seed: int = 2) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed
	var n := int(seconds * RATE)
	var fade := RATE / 2
	var raw := PackedFloat32Array()
	raw.resize(n + fade)
	var lp := 0.0
	for i in range(n + fade):
		var t := float(i) / RATE
		var swell := 0.75 + 0.25 * sin(TAU * t / seconds)
		var tone := 0.5 * sin(TAU * 55.0 * t) + 0.3 * sin(TAU * 82.5 * t + 0.4) + 0.18 * sin(TAU * 110.25 * t + 1.1)
		lp += (rng.randf_range(-1.0, 1.0) - lp) * 0.01
		raw[i] = (tone * swell * 0.35 + lp * 2.5) * 0.9
	return _to_wav(_seamless(raw, n), true)


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
