# Tree

A calm idle game about one realistic tree. Godot 4.7, GDScript, mobile-first (portrait).

## Open on the PC
1. Install Godot 4.7 stable (standard build, not .NET).
2. Godot project manager → Import → pick this folder's `project.godot`.
3. Press F5. You get the grey-box tree: a seedling grows toward the sun, hold the button to boost, the slider speeds time up.

## Run the tests (no editor needed)
```
godot --headless --path . --import   # once after a fresh checkout
godot --headless --path . -s tests/run_tests.gd
```
Exit code 0 means every test passed.

## What is here (Prototype 1, steps 1 to 4 and 9 of the build plan)
- `shared/` the simulation: plant graph with pipe-model thickness, space colonization, resources (water, N, P, K, life force), day/night clock with the real sun arc (east, south and high at noon, west) and boost as a trade, growth sim, save/load with offline catch-up.
- `tree/` a mesh builder (tapered tubes) and the grey-box scene.
- `tests/` 82 headless tests, including the design-doc acceptance criteria (morning boost leans east, noon south and up, evening west; boost grows more but yields less life force; five minutes day and three night; budgets enforced; same seed = same tree).
- `docs/` the design doc (v2.0), research notes, asset list, feel review with the play-test plan, and acceptance criteria.
- `CLAUDE.md` conventions for building with an agent.

Not here yet: root mode, switching, tutorial and diary, twinkle, sound, the day/night loop with dawn burst, read-the-meadow hints, the light/shadow model, real assets.
