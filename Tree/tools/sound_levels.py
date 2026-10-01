"""Levels of the WAVs written by tools/render_night_song.gd (0.8.1, broken list 27).

    python tools/sound_levels.py <folder>

For each file: RMS over 10..50 s and the peak in dBFS, plain, A-weighted (what an ear hears)
and "phone" (A-weighted and cut below 300 Hz, roughly what a phone's small speaker plays).
Needs numpy only.
"""
import sys, wave, os
import numpy as np


def read(path):
    with wave.open(path) as w:
        rate = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32767.0
        if w.getnchannels() == 2:
            x = x.reshape(-1, 2).mean(1)
    return x, rate


def a_weight(f):
    f2 = f * f
    ra = (12194.0**2 * f2 * f2) / ((f2 + 20.6**2) * np.sqrt((f2 + 107.7**2) * (f2 + 737.9**2)) * (f2 + 12194.0**2))
    return ra * 10 ** (2.0 / 20)


def weighted(x, rate, phone):
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / rate)
    g = a_weight(np.maximum(f, 1e-3))
    if phone:
        g = g / np.sqrt(1.0 + (300.0 / np.maximum(f, 1e-3)) ** 8)
    return np.fft.irfft(spec * g, len(x))


def db(v):
    return 20 * np.log10(max(v, 1e-6))


def main(folder):
    print("%-22s %8s %8s %8s %8s" % ("file", "RMS", "A", "phone", "peak"))
    for name in ["root_run_60s.wav", "song_only_60s.wav", "tree_day_60s.wav", "tree_night_60s.wav"]:
        x, rate = read(os.path.join(folder, name))
        s = x[int(10 * rate):int(50 * rate)]
        rms = lambda y: np.sqrt(np.mean(y * y))
        print("%-22s %8.1f %8.1f %8.1f %8.1f" % (name, db(rms(s)), db(rms(weighted(s, rate, False))),
              db(rms(weighted(s, rate, True))), db(np.abs(x).max())))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
