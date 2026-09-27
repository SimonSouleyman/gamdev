# Tree

A calm idle game about one realistic tree. Godot 4.7, GDScript, mobile-first (portrait).

## Open on the PC
1. Install Godot 4.7 stable (standard build, not .NET).
2. Godot project manager → Import → pick this folder's `project.godot`.
3. Press F5. A seed is planted at sunset; the journal pages explain the rest.
   On the PC: mouse = finger. Hold to boost the sun, drag to walk around the tree, scroll to zoom.
   Underground: WASD or arrow keys steer, space dives. Dev keys: T time speed 1x/5x/20x, H hides the UI, J opens the diary, F9 starts a new game.
   The save lives in the Godot user folder (`user://tree_game.json`); F9 or deleting it starts over.

## Run the tests (no editor needed)
```
godot --headless --path . --import   # once after a fresh checkout
godot --headless --path . -s tests/run_tests.gd
```
Exit code 0 means every test passed.

## Play it without hands
```
godot --path . -s tools/autoplay.gd -- --shots=C:/temp/tree-shots   # plays two days, saves screenshots
godot --path . -s tools/grow_shot.gd -- --days=15 --shots=C:/temp/tree-shots
godot --headless --path . -s tools/month_report.gd                    # 30 days of numbers
```

## What is here (Prototype 1, all eleven build steps as a grey box)
- `shared/` the simulation: plant graph, space colonization, resources, day/night clock with the real sun arc and boost as a trade, growth sim with dawn burst, the seeded underground (dots, rocks, finds, surface hints), the root system, the diary, the game state that runs the loop, save/load with offline catch-up.
- `tree/` tree mode: the meadow with its hints, the tree mesh and leaf clusters, twinkle, orbit camera, the sun and its arc.
- `roots/` root mode: glowing dots in a fog, rocks, the steered root, fine roots.
- `ui/` the journal (pages, diary, settings), the thumb stick, the sun arc.
- `audio/` placeholder ambience made in code.
- `tests/` headless tests; `tools/` autoplay and tuning tools.
- `docs/` the design doc (v2.0), research notes, asset list, feel review with the play-test plan, and acceptance criteria.

Not here yet (later milestones): realistic assets and leaf cards, the light/shadow model and pruning UI, water upkeep and drought, visitors, the grove, mobile export.
