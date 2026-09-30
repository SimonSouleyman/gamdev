# Tree (Godot 4.7) — conventions for agents and humans

Design doc: `docs/design-doc.md` (the source of truth for gameplay decisions).
Design practice: `docs/design-practice.md` (spec a mechanic before building it, keep pacing and
economy numbers in `docs/tuning.md`, check the "broken" list there before each full review).

## Hard rules
- Godot **4.7 stable**, **Mobile renderer**, **GDScript** only (GDExtension/C++ only for proven hot loops, by explicit decision).
- Growth is always **algorithmic** (self-organizing tree model: space colonization + light + Borchert-Honda + pipe model). Never swap in pre-made tree models to fake growth.
- **No Google services** (no Play Games, Firebase, AdMob). Local notifications only.
- **Simulation is separate from rendering.** Everything in `shared/` is plain `RefCounted` code with no scene dependency and no `Node` access. Scenes and meshes only *read* the plant graph.
- **All randomness comes from the save seed.** Each system has its own `RandomNumberGenerator` seeded from `hash([seed, "<name>"])` (tree: `GrowthSim.rng`, underground, roots, wishes, meadow). Never the global `randf()`/`randi()`. RNG states are saved as strings (JSON numbers lose 64-bit precision).
- **Budgets** live in `shared/budgets.gd` and are enforced in code, not in comments.
- Portrait orientation (720×1280 reference). UI language: English.

## Layout
- `shared/` simulation core: plant graph, space colonization, resources, day cycle, save data.
- `tree/` tree mode scenes, mesh builder, sun.
- `shed/` the garden shed (the menu) and the bonsai on its windowsill (`bonsai_view.gd`; the simulation is `shared/bonsai_sim.gd`, its paper `ui/bonsai_hud.gd`).
- `roots/` root mode scenes, joystick, nutrient field.
- `audio/`, `ui/` as named.
- `tests/` headless tests. Run: `godot --headless --path . -s tests/run_tests.gd` (exit code 0 = pass). After a fresh checkout run `godot --headless --path . --import` once, or class_name lookups fail.
- `docs/` design doc and research notes.
- `tools/` dev tools: `autoplay.gd` plays the real scene end to end (seed, first root, sapling, tap boost, the day running on, second root) and takes screenshots; `grow_shot.gd` grows a tree for N days and photographs it; `month_report.gd -- --species=<id>|all` prints bot play day by day until the tree is finished, for tuning; `root_bot.gd` is the autopilot they share; `strategies.gd -- [--species=] [--seed=] [--strats=] [--nights] [--set=name=value]` plays whole months with different night and day styles (chase deposits, end early, straight down, random, always boost) for balancing the roots; `render_icons.gd` renders the HUD pictures and compass into `ui/icons` (needs a window); `hud_shot.gd` photographs the tree view with its real HUD. `shed_shot.gd` photographs the shed and then the bonsai (`--bonsai-only` for just the bonsai). `backup_shot.gd` photographs 0.8's backup notes on the pinboard, the album's "send" and the shared Polaroid.
- `tools/` dev tools: `autoplay.gd` plays the real scene end to end (seed, first root, sapling, tap boost, the day running on, second root) and takes screenshots; `grow_shot.gd` grows a tree for N days and photographs it; `month_report.gd -- --species=<id>|all` prints bot play day by day until the tree is finished, for tuning; `root_bot.gd` is the autopilot they share; `strategies.gd -- [--species=] [--seed=] [--strats=] [--nights] [--set=name=value]` plays whole months with different night and day styles (chase deposits, end early, straight down, random, always boost) for balancing the roots; `render_icons.gd` renders the HUD pictures and compass into `ui/icons` (needs a window); `hud_shot.gd` photographs the tree view with its real HUD. `shed_shot.gd` photographs the shed and then the bonsai (`--bonsai-only` for just the bonsai). `live_shot.gd` renders the live picture's layers and photographs the phone's animation through `live_preview.gd` (a desktop copy of the Java renderer); `live_parity.gd` checks that copy's maths against the Java one; `render_app_icon.gd` renders the app icon (both need a window).
- `main.tscn` is the game: it owns `GameState` (shared/) and switches `TreeView` (tree/) and `RootView` (roots/).

## Workflow
- One build step (see design doc "Prototype 1") per branch and pull request. Each PR ends playable on PC.
- Write acceptance criteria into `docs/acceptance.md` before starting a step; add a test for each.
- Run the tests before every push. A failing test is never skipped or deleted to get green.
- Visual judgement (does it look natural) is Simon's: post a screenshot in the project thread. After each step, ask the play-test questions from `docs/game-feel-review-2026-09-27.md`.
- Before handing a step over, run `godot --path . -s tools/autoplay.gd -- --shots=<folder>` and look at the screenshots.
- Sun steering is three-dimensional (east / south+up / west) and boosting trades life force for growth speed; keep both when touching `DayCycle` or `GrowthSim`.

## Versions
- Releases are tagged `tree-vX.Y(.Z)` and built into `GameDev/tree-releases`. From v0.5 on (Simon, 2026-09-28): fixes and improvements found in tests go out as 0.5.1, 0.5.2 and so on; only new features justify 0.6. From 0.6 on the same rule applies: fixes after the 0.6 test are 0.6.1, 0.6.2 and so on; the next set of new features starts 0.7. The plan for 0.6 is design doc section 17.
- Phone builds use the Gradle build (the TreePhone plugin): `godot --headless --path . --export-debug "Android" <apk>`. The Android build template in `android/` is gitignored; after reinstalling it, set `buildTools` to `'36.0.0'` in `android/build/config.gradle` (see `android_plugin/README.md`).

## Style
- `snake_case` files and functions, `PascalCase` classes via `class_name`.
- Static typing everywhere (`var x: float`, `-> void`).
- Short functions, no premature optimisation; measure with the profiler first.
