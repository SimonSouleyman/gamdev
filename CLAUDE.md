# Drift

A cozy, no-lose mobile game: steer a small island across a literal 3D planet, absorbing other
landmasses agar.io-style to grow from a tiny island into a continent, while its flora/fauna
ecosystem keeps evolving even while the app is closed. See [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md)
for the full design/architecture and build-order plan (M0-M8) — read it before starting new
systems work, and update it when a milestone lands or a decision changes.

## Environment

- Unity **6000.6.2f1** (the only editor actually installed — don't target another version
  without installing it first via `unity install-modules`/`unity editors`).
- URP. Packages beyond the stock template: Cinemachine, `com.unity.mathematics`,
  `com.unity.pipeline` (live-Editor connection), plus the template defaults (Input System,
  AI Navigation, Timeline, Test Framework).
- Build modules installed: Android, iOS, WebGL. iOS module is installed for asset/package
  compatibility, but an actual `.ipa` still needs Xcode on a Mac — this Windows machine can't
  produce the final iOS binary.
- Active Input Handling is **Input System package only** — `UnityEngine.Input` (the old class)
  does not work. Use `Keyboard.current` / `Touchscreen.current` / Input Actions.

## Working with the live Editor

The Unity CLI (`unity`) talks to a running Editor instance over `unity command <name> ...`
(add `--project-path` when more than one Editor might be running). Prefer this over hand-editing
`.unity`/`.prefab`/`.asset` YAML — always check `unity status` first.

Typical loop for a code change:
1. Write/edit `.cs` files directly (normal file edits — Unity picks them up).
2. `unity command recompile`, then poll `unity command recompile_status` until
   `compilationFailed: false` and `status: completed`.
3. Wire scene changes via `create_gameobject` / `attach_script` / `add_component` /
   `set_component_properties` / `set_transform` / `set_parent` — not by hand-editing the scene
   file. `set_component_properties`/`set_serialized_field` take **asset references as their
   project path** (e.g. `"Assets/_Drift/Data/PlanetTopology.asset"`) and **scene object
   references as a hierarchy path** (e.g. `"/Planet"`); array-typed CLI flags like
   `--rotation`/`--position` need a JSON array string (`"[50,-30,0]"`), not comma-separated
   values.
4. Verify with `unity command capture_game_view --save_path "Temp/x.png"` (path must be inside
   the project; `Temp/` is convenient and gitignored) — read the PNG back to actually look at it,
   don't assume. Check `unity command console --level error` for runtime/shader errors.
5. Delete debug/verification assets afterward (`delete_asset`) so they don't pollute the repo.

**Play Mode frames advance once `Application.runInBackground = true` is set** (from `eval`, right
after `editor_play`; it is a runtime flag and is not saved). Without it the unfocused Editor never
ticks the Player Loop and `Time.frameCount` stays at 1. With it the game really runs: drive it by
replacing `Island.InputProvider` with a steering lambda (index the `Island.All` list, a `foreach`
over the interface allocates), sample state from `eval`, take screenshots *including the overlay
UI* with `ScreenCapture.CaptureScreenshot(path)` (`capture_game_view` renders the camera only), and
profile with `ProfilerDriver` + `HierarchyFrameDataView` (enable `ProfilerDriver.enabled`, sleep,
read the frames back in chunks - `eval` has a 5 s budget). Anything about *feel* (input response,
camera smoothing, touch) still needs the owner's own hands-on test.

## Code conventions established so far

- **Assembly-per-system**, matching `Docs/ARCHITECTURE.md`'s project structure section
  (`Drift.Core`, `Drift.World`, `Drift.Islands`, …). New systems get their own `asmdef`.
- **Editor-visible without Play Mode.** Components that build/place procedural content
  (`PlanetSphere`, `Island`) use `[ExecuteAlways]` + `OnEnable()` (not `Awake()`, which only
  fires in Play Mode) so the result is visible immediately in the Scene/Game view. Guard any
  per-frame gameplay logic in `Update()`/`LateUpdate()` with `if (!Application.isPlaying) return;`
  so it doesn't run in the Editor.
- Procedurally generated GameObjects (mesh chunks, blobs) get
  `hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild` so rebuilding them never
  pollutes the saved scene or accumulates duplicates across domain reloads.
- **Island state is the source of truth, `Transform` is output.** Islands keep a planar position,
  a heading and a heightfield (`IslandShape`); `Transform.position/rotation` are derived each
  update. The old sphere-surface convention (unit `Normal` + tangent `Forward`, parallel
  transport) still describes the parked `Planet` work — see ARCHITECTURE.md.
- **Systems talk through interfaces, not assemblies**: `IIslandSurface` (Core), `IPlateRider`
  (Tectonics). Add a new interface instead of a new assembly reference when two systems need to
  influence each other (islands <-> plates <-> life).
- **Rebuild-my-children routines find their children through the hierarchy** (marker component or
  name), never a plain `List` field — it is empty after every recompile and you get duplicates.
- Edit-time code uses `DestroyImmediate`; `Destroy` only in Play Mode.
- Watch mesh **triangle winding** on any new procedural mesh — a face wound the wrong way
  back-face-culls invisible instead of erroring, which is a confusing silent failure.
- Terrain/elevation-driven shaders must get **real elevation in the geometry**, not clamped to
  sea level — see the ARCHITECTURE.md "Known issues" entry.
- No code comments unless they capture a genuinely non-obvious constraint or a past bug (the two
  points above are exactly that kind of thing) — don't narrate what the code already says.

## Working with several agents on this project

**Default workflow (owner's standing request, 2026-09-21): parallelize as much as possible.**
1. Split every request into independent tasks, grouped by disjoint file ownership (one agent per
   system/file set; shared files like `IIslandSurface`, `LifeEnvironment` or the scene stay with the
   coordinator). Put any cross-agent API the tasks need in place *before* dispatch.
2. Dispatch all independent tasks at once as parallel agents. Each agent: writes code, verifies with
   `eval`/its own EditMode test class (`QuickTests.Run("^MyTests$")` from eval — see the run_tests
   note under CLI gotchas; never Play Mode), and reports changed files, results, "Scene wiring
   needed" and "Integration requests".
3. Only the coordinator integrates: scene wiring, Play Mode checks, screenshots, the full EditMode
   suite, docs (`Docs/CHANGES_*.md`, ARCHITECTURE.md), save backup/restore — then reports to the owner.
   Tasks that genuinely depend on each other run sequentially; small one-file fixes need no agent.
4. No background shells may outlive an agent: polling loops get a hard iteration limit and a timeout, never a bare
   `until <condition>` (a condition that never matches kept recompiling the Editor for 40 minutes once).
5. Only one Editor is running, so recompiles from parallel agents collide: agents wait for
   `recompile_status` to be `completed` and re-run their check if another agent's edit triggered a
   reload in between.
6. Scale: ~3–5 agents normally, more for big requests — but every agent always owns its own
   independent system; never two agents on the same system.
7. Spare the Editor: do everything that can happen before dispatch up front (shared APIs, interfaces,
   scene objects, materials, one recompile), and have agents do the Editor-free parts of their work
   (reading, planning, writing code and tests) first, then compile/verify in as few rounds as possible.
8. Commits only when the owner asks — also after an integration round.

Five parallel agents built audio/visuals/UI/gameplay/persistence in one pass; it worked because each
agent owned a disjoint set of files and never touched the scene or Play Mode — the coordinator did
all `attach_script`/`set_component_properties`/`save_scene`/screenshots afterwards from the agents'
"Scene wiring needed" sections. Keep that split: agents write code + `eval` verification + a wiring
list; one session integrates. Cross-file needs go through public API additions made *before*
dispatch (e.g. `Island.InputProvider`, `Island.Merged`, `IslandKind`) or "Integration requests" in
the report. Note `FindObjectsByType` in `eval` can miss `DontSave` children — walk the transform.

## CLI gotchas learned the hard way

- `editor_play` / `editor_stop` take no arguments (`--action` makes the call fail silently — the
  Editor then simply isn't playing).
- Under Git Bash, hierarchy paths like `/Visuals` get rewritten to `C:/Program Files/Git/Visuals`;
  pass `//Visuals`, set `MSYS_NO_PATHCONV=1`, or set fields through `eval` with
  `Undo.RecordObject` + `EditorUtility.SetDirty`.
- UI built by `[ExecuteAlways]` components (`SessionScreens`, `WorldHud`) only updates on an editor
  tick; before an edit-mode `capture_game_view`, invoke their private `Update` via reflection from
  `eval` (and `SessionScreens.Preview.RenderNow(player)` for the island picture).
- Exiting Play Mode (no domain/scene reload) leaves seeds/streamed islands changed; restore with
  `WorldStreamer.ResetWorld()` + `StreamAround(true)` and `IslandChaseCamera.SnapToTarget()`.
- `run_tests` can leave an untitled scene open; reopen `Assets/_Drift/Scenes/Planet.unity` before `editor_play`.
- Playing a run overwrites the owner's `drift_save.json` (persistentDataPath): back it up first and copy it
  back after `editor_stop`.
- The Mobile quality level is hidden in the Editor while the build target is Standalone; to look at the
  phone pipeline set `QualitySettings.renderPipeline` to `Assets/Settings/Mobile_RPAsset.asset` at runtime.

- `unity command run_tests` hung the Editor's command pipeline twice (every later command timed out; only killing
  the Editor, deleting `Temp/UnityLockfile` and `unity open` helped). For plain EditMode tests use
  `Drift.EditorTools.Playtest.QuickTests.Run("^(ClassA|ClassB)$")` from `eval` (reflection runner, no coroutines);
  run the full suite in a few class-pattern chunks.
- Unity wipes `Temp/` when the Editor starts: keep anything worth keeping (play-test screenshots) elsewhere
  (`Playtests/shots/`, gitignored).
- Play tests: the `Drift/Playtest/*` scenarios (`Scripts/Editor/Playtest`, see ARCHITECTURE.md "Play-test tool") drive
  the game with a human-like bot on a fixed seed and write `Playtests/*.json`; `python Playtests/summarize.py`.
  They play the real save slot: back up `LocalLow/DefaultCompany/Drift` + PlayerPrefs first, restore afterwards.
- `eval` has a ~5 s main-thread budget — long benches (e.g. `PerfBaseline.PerIsland()` over 32
  islands) time out; bench subsets or fewer iterations instead.

## Workflow preferences

- Only commit when explicitly asked — changes are staged/left as working-tree edits otherwise.
- Prefer the live-Editor CLI workflow above over asking the user to click through the Editor
  manually, except for anything about *feel* (see the Play Mode note above) — that always
  needs the user's own hands-on test.

## Testing gameplay headlessly

Besides real Play Mode (see above), gameplay systems expose public step methods you can drive
deterministically from `unity command eval`: `PlateSystem.Step(dt)`, `Island.Tick(input,
dt)`, `Island.AdvanceUplift/FinishUplift()`, `IslandWorld.Step()`, `IslandLifeSystem.Step(dt)` /
`Simulate(lifeSeconds)` / `GetStageCounts`, `WorldStreamer.StreamAround(true)`. Careful: any script
edit on disk triggers an automatic recompile, and a domain reload regenerates every island (a merge
you just staged disappears) — recompile first, then stage the scene, then screenshot.
Force a collision by `SetPlanarPosition` on an AI island next to the player and calling
`IslandWorld.Step()`; fast-forward ecology with a loop of `IslandLifeSystem.Step`. `eval` runs in
the current mode — entering Play first (`editor_play`) is needed for anything that calls
`Destroy`. Remember the values baked into the scene: after changing a field default in code, push
it into the scene objects with `set_component_properties`.
