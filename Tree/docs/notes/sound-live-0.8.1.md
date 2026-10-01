# Root-run sound and a live picture that moves (0.8.1, broken list 27 and 33)

Build notes for two 0.8.1 items of `docs/specs/0.8.md`. Numbers not yet felt on the phone are
[PLACEHOLDER]. QA files: `GameDev/tree-qa/sound-0.8.1/`.

## 27. The root run's hum and four low notes

**Before:** `AmbienceSynth.hum()`: three pure sines (55, 82.5, 110 Hz) and a little rumble at
-6 dB: a thin drone, and almost all of it below what a phone speaker plays.

**Now:** `AmbienceSynth.night_song()`, the game's own synthesis (no file, no network), played in
Ambience's `hum` layer:
- **Hum:** a soft D major chord (D2, A2, D3, F#3, A3), each voice twice, 0.1 Hz apart (a slow
  chorus, warmer than one sine), with soft harmonics (1, 0.4, 0.18, 0.08, 0.03), each voice
  breathing on its own slow swell (1 to 3 per loop). The harmonics put it in the 150 to 700 Hz
  range a phone speaker plays; nothing above about 1.2 kHz.
- **Melody:** four low notes in turn, one every 5 s: D3, F#3, E3, A2 (Simon: "it can be just four
  low notes playing in turn"), a soft rounded tone that swells in 0.4 s and dies away over about
  2.4 s into the next. No switch.
- One seamless 20 s loop (tones in whole cycles, note tails wrap into the start), mono at
  11025 Hz. Made on a worker thread when the game starts (about 0.2 to 0.4 s on the PC; it is
  not needed before the first dive).
- **Level:** `Ambience.HUM_DB` = -17 dB [PLACEHOLDER].
- **Fades:** in over about 4 s from silence however fast the world switches (the dive, or a game
  loaded at night), out within 1 s when the night ends (`SONG_FADE_IN_S`, `SONG_FADE_OUT_S`).
  A layer that is silent and meant to be silent now rests (`stream_paused`), so the song is not
  even decoded in tree mode.

**Levels** (`tools/render_night_song.gd` renders 60 s through the game's own Ambience, its levels
and fades, with main.gd's dive and rise timings; `tools/sound_levels.py` measures them).
dBFS, RMS over 10 to 50 s; "A" is A-weighted (the ear), "phone" also cuts below 300 Hz (a small
speaker):

| 60 s render | RMS | A | phone | peak |
|---|---|---|---|---|
| root run (song + water) `root_run_60s.wav` | -31.0 | -43.3 | -47.6 | -17.5 |
| song alone `song_only_60s.wav` | -31.0 | -43.5 | -48.1 | -19.1 |
| tree mode by day `tree_day_60s.wav` | -27.7 | -39.3 | -40.8 | -14.9 |
| tree mode at dusk `tree_night_60s.wav` | -27.7 | -39.9 | -41.7 | -17.0 |

So the song is about 3 dB under the tree mode's ambience plain, 4 dB A-weighted and 7 dB on a
phone speaker. Spectral centroid 153 Hz (tree mode 177 Hz), energy above 1 kHz 0.002 %
(tree mode 1.9 %): warm, not bright. Fade: silent before the dive; about -53 dBFS 1.5 s in, at its level
after about 3 s (the fade is 15 dB a second); 0.5 s after the night ends -53 dBFS, silent from 1 s on.

**Tests** (`tests/test_night_song.gd`): four low notes over a low hum, a seamless loop, no
clipping, notes swell and die (not a flat drone), a low mean frequency; the song at its level is
quieter than the tree mode's wind layer alone; silent and resting in tree mode by day and night;
faint half a second into the dive and at its level after the fade; silent a second after the
night ends.

**Open:** the feel is Simon's call on the phone (headphones and the speaker). Levers: `HUM_DB`,
`SONG_NOTES`, `NOTE_SECONDS`, `SONG_CHORD`, `SONG_MELODY_GAIN`.

## 33. The live wallpaper and screen saver showed a still picture

**What happened (Fairphone 6, 2026-10-01, Tree wallpaper set, game 0.8):** logcat shows the
`:live` process alive with no errors; `dumpsys wallpaper` shows TreeWallpaperService bound; the
game's own layers are loaded (Simon's autumn tree, not the bundled sapling). Six screenshots in a
row show the frame loop *is* running: the picture changes between them. But the crown top of
his young tree moved at most 3 px (on a 1116 px wide screen), the grass 1 to 2 px, the clouds
under 1 px a second. Screenshots and a 10 s recording: `tree-qa/sound-0.8.1/phone/before*`.

**Cause:** not the frame loop, the visibility callback, the battery-saver path (off) or the
layers, but the numbers: the wind swayed the crown by 1.2 % of the *tree's* height
(`WIND` = 0.012), so a young tree a third of the screen tall moved about 4 px at most, slowly
(a 7 s gust), as one stiff piece; the 12x24 mesh was too coarse for anything like leaves to
move; the clouds crossed the screen in 20 to 35 minutes. Light and moon follow the clock but
change over minutes. Everything moved, nothing could be seen moving.

**Fix** (`shared/live_picture.gd` and its line-by-line copy `LiveScene.java`, parity checked with
`tools/live_parity.gd` and `Parity.java`, which now also prints the clouds):
- Sway 3 % of the tree's height (`WIND` 0.012 to 0.03), a small tree swaying as if it were 0.4
  of the layer tall (`WIND_MIN_TALL`), so Simon's young tree's crown top swings about 15 to 20 px each way.
- A wave through the branches (`0.35 k sin(1.6 t - 6 u + 4 v)`), so parts of the crown move
  apart instead of the whole tree leaning.
- Leaves shimmer: a quick 0.8 Hz wobble of 0.35 % of the width (`LEAF`, about 4 px), differing
  from cell to cell, only in the crown (above a third of the tree's height); the mesh is now
  24x48 (`LiveRenderer.MESH_W/H`, and `tools/live_preview.gd`).
- The near grass ripples in travelling waves (0.2 % to 0.5 % of the width).
- Clouds cross the screen in 4 to 7 minutes (about 3 to 5 px a second on the phone).
- Tests (`tests/test_live_picture.gd`, "wind is visible on the phone"): for a young, a grown and
  a tall tree the crown top sways at least 12 px and at most 90 px in 10 s on a 1116 px screen,
  neighbouring leaf cells move at least 2 px apart, the grass at least 3 px, the clouds 2 to 8 px
  a second. The foot, the picture's edges and the frame rate (12 fps) are as before.
- Desktop preview: `tools/live_shot.gd` frames in `tree-qa/sound-0.8.1/live/` (`wind_strip.png`,
  `wind.gif`).

**Battery** (14c): still 12 fps, drawing only while seen, a still frame a minute in battery
saver. The bigger mesh is 1225 vertex offsets a frame instead of 325 (a few sines each, well under
0.5 ms on the phone's CPU); the GPU work (one or two bitmap meshes, a gradient, three clouds) is
the same. The estimate in `live-icon-0.8.md` (0.2 to 0.5 % an hour) stands; to measure on the
phone: Settings > Battery > battery usage, "Tree" after an hour with the wallpaper on.

**How to check on the phone** (after installing 0.8.1; the wallpaper stays set across an update,
Android restarts the `:live` process): look at the home screen for ten seconds. The crown should
sway visibly and its leaves shimmer, the grass at the bottom ripple, a cloud drift a finger's
width in about ten seconds. The screen saver (Settings > Display > Screen saver > Tree > Start
now) shows the same. With adb: `adb shell screenrecord --time-limit 10 /sdcard/x.mp4`.

**Rebuilt:** `addons/tree_phone/bin/tree_live-release.aar` (`gradlew :treelive:copyAarToAddon`);
a debug APK exports with it (`tree-qa/sound-0.8.1/test.apk`, not installed).
