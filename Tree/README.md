# Tree

A calm idle game about one realistic tree. Godot 4.5, GDScript, mobile-first (portrait).

## Open on the PC
1. Install Godot 4.5 stable (standard build, not .NET).
2. Godot project manager → Import → pick this folder's `project.godot`.
3. Press F5. You get the grey-box tree: a seedling grows toward the sun, hold the button to boost, the slider speeds time up.

## Run the tests (no editor needed)
```
godot --headless --path . -s tests/run_tests.gd
```
Exit code 0 means every test passed.

## What is here (Prototype 1, steps 1–4 and 9 of the build plan)
- `shared/` the simulation: plant graph with pipe-model thickness, space colonization, resources (water, N, P, K, life force), day/night clock with sun boost, growth sim, save/load with offline catch-up.
- `tree/` a mesh builder (tapered tubes) and the grey-box scene.
- `tests/` 71 headless tests, including the design-doc acceptance criteria (morning boost leans east, evening boost leans west, one in-game day under 10 minutes, budgets enforced, same seed = same tree).
- `docs/` the design doc and the research notes.
- `CLAUDE.md` conventions for building with an agent.

Not here yet: root mode, mode switching, the tutorial, twinkle, sound, the light/shadow model, real assets.
