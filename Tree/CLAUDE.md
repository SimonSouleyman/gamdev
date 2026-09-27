# Tree (Godot 4.5) — conventions for agents and humans

Design doc: `docs/design-doc.md` (the source of truth for gameplay decisions).

## Hard rules
- Godot **4.5 stable**, **Mobile renderer**, **GDScript** only (GDExtension/C++ only for proven hot loops, by explicit decision).
- Growth is always **algorithmic** (self-organizing tree model: space colonization + light + Borchert-Honda + pipe model). Never swap in pre-made tree models to fake growth.
- **No Google services** (no Play Games, Firebase, AdMob). Local notifications only.
- **Simulation is separate from rendering.** Everything in `shared/` is plain `RefCounted` code with no scene dependency and no `Node` access. Scenes and meshes only *read* the plant graph.
- **All randomness comes from one seed** (`GrowthSim.rng`). No `randf()`/`randi()` outside `shared/rng.gd`-fed generators.
- **Budgets** live in `shared/budgets.gd` and are enforced in code, not in comments.
- Portrait orientation (720×1280 reference). UI language: English.

## Layout
- `shared/` simulation core: plant graph, space colonization, resources, day cycle, save data.
- `tree/` tree mode scenes, mesh builder, sun.
- `roots/` root mode scenes, joystick, nutrient field.
- `audio/`, `ui/` as named.
- `tests/` headless tests. Run: `godot --headless --path . -s tests/run_tests.gd` (exit code 0 = pass). After a fresh checkout run `godot --headless --path . --import` once, or class_name lookups fail.
- `docs/` design doc and research notes.

## Workflow
- One build step (see design doc "Prototype 1") per branch and pull request. Each PR ends playable on PC.
- Write acceptance criteria into `docs/acceptance.md` before starting a step; add a test for each.
- Run the tests before every push. A failing test is never skipped or deleted to get green.
- Visual judgement (does it look natural) is Simon's: post a screenshot in the project thread. After each step, ask the play-test questions from `docs/game-feel-review-2026-09-27.md`.
- Sun steering is three-dimensional (east / south+up / west) and boosting trades life force for growth speed; keep both when touching `DayCycle` or `GrowthSim`.

## Style
- `snake_case` files and functions, `PascalCase` classes via `class_name`.
- Static typing everywhere (`var x: float`, `-> void`).
- Short functions, no premature optimisation; measure with the profiler first.
