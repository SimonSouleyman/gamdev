# Drift

A cozy mobile island game (German UI). You steer a small island across the sea, bump into other islands
and absorb them agar.io-style until your continent is complete – the Pangäa – while plants, herds, birds,
fish and a fishing village keep living on it, even while the app is closed.

Two modes:

- **Gemütlich** – no time pressure. Grow your island, reach milestones (Leuchtturm, Hafen, Dorf …), watch
  and photograph the animals, fill the species journal over several worlds, then fly over the finished
  Pangäa map-style. Every world has a seed ("Welt-Nummer"); "Weiter" continues the saved one.
- **Abenteuer** – a race on an endless ring. The island drives itself and sinks faster the further you get
  (+50 % per km, no ceiling); you dodge islands and storms, collect flotsam for buoyancy, ride plate
  boundaries and whales for boosts and build momentum ("Schwung"). Score = distance; runs are logged in the
  adventure journal with a record.

Tilda, a small volcano, is the guide: she grumbles (procedural synth), briefs both tutorials and comments on
milestones. All audio is synthesised in code – there are no audio assets.

## Tech

- Unity **6000.6.2f1**, URP, Input System package only (no legacy `Input`). Target: Android phones,
  portrait; tuned for a Fairphone 6 (Adreno 810) at a steady 60 fps with an automatic render scale.
- Everything procedural: islands are heightfields (`Islands/`), the sea is one shader with a coast distance
  field, wake, foam flecks and a cloud-shadow texture (`Shaders/Water.shader`, `Visuals/`), vegetation, herds
  and critters are template-batched meshes (`Life/`), the UI is built in code (`Bridge/`, `UI/`).
- Assemblies per system: `Drift.Core`, `Drift.World`, `Drift.Tectonics`, `Drift.Islands`, `Drift.Life`,
  `Drift.Visuals`, `Drift.Audio`, `Drift.SaveSystem`, `Drift.UI`, `Drift.Bridge` (scene glue), tests in
  `Drift.Tests*`, editor tooling in `Drift.Editor`.

## Layout

```
Assets/_Drift/Scripts/       game code (one folder per assembly, see above)
Assets/_Drift/Shaders/       water, terrain, clouds, storm, animals, fish, sky (+ DriftWater/DriftClouds/DriftMotion .hlsl)
Assets/_Drift/Scenes/Planet.unity   the single scene (title, both modes, all UI)
Assets/_Drift/Scripts/Editor/Playtest/   bot play-test scenarios (menu Drift/Playtest/*) and the QuickTests runner
Assets/Settings/             URP assets: Mobile_RPAsset/Mobile_Renderer (phone), PC_RPAsset (editor)
Docs/ARCHITECTURE.md         design, systems, decisions – per-version "additions" sections at the end
Docs/CHANGES_<date>.md       what changed per version, in German (owner-facing)
Docs/PERF_BASELINE.md        performance numbers and caps
Playtests/                   play-test results (JSON) and summarize.py
CLAUDE.md                    conventions for working on the project with the Unity CLI and agents
```

## Build and install

- Android APK: `unity command build --target Android --outputPath Builds/Android/Drift-<version>.apk --confirm true`
  (add `--options '["Development"]'` for a development build – it logs `Drift-FPS`/`Drift-RES` frame stats and
  contains the GPU probe, started by a `perfprobe` file in the app's `files/` folder).
- Bump the version through `PlayerSettings.bundleVersion` / `Android.bundleVersionCode` in the running Editor,
  not by editing `ProjectSettings.asset` (the Editor overwrites it when a build starts).
- Install over USB: `adb install -r Drift-<version>.apk` (package `com.drift.game`).

## Tests and play tests

- EditMode tests (NUnit, ~1,100): run from the Test Runner, or class by class from `unity command eval` with
  `Drift.EditorTools.Playtest.QuickTests.Run("^ClassNameTests$")`.
- Bot play tests: menu `Drift/Playtest/*` (cozy observation, adventure good/poor bot, soak, tutorials) write
  `Playtests/<scenario>_<time>.json`; `python Playtests/summarize.py` prints gaps, animal sizes, decisions per
  10 s, memory and frame times.

## Versions

Tags `v0.1` … `v0.6.7.3` in this project (mirrored as `drift-v*` in the gamdev monorepo); each version has a
`Docs/CHANGES_*.md` entry. Snapshots of every version also live outside the repo in `DriftVersions/`.
