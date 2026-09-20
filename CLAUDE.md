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

**Known limitation — Play Mode doesn't advance frames under this automation.** `editor_play`
enters Play Mode but `Time.frameCount` can stay stuck at 1 indefinitely (confirmed with
`set_autotick` and `editor_focus`, no effect) — the Player Loop seems to need real OS window
focus this headless session doesn't have. Don't waste time sleeping-and-recapturing waiting for
animation/physics to progress. Instead use `unity command eval` to directly call gameplay
methods and assert on the result (e.g. call `island.Tick(input, dt)` in a loop to simulate
movement, or manually compute+apply a camera's target transform for a one-shot verification
screenshot). Real interactive testing — especially anything about *feel* (input response, camera
smoothing, touch controls) — has to happen in the user's own focused Editor window; say so
explicitly rather than claiming a feel-based change is verified from this session.

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

- `eval` has a ~5 s main-thread budget — long benches (e.g. `PerfBaseline.PerIsland()` over 32
  islands) time out; bench subsets or fewer iterations instead.

## Workflow preferences

- Only commit when explicitly asked — changes are staged/left as working-tree edits otherwise.
- Prefer the live-Editor CLI workflow above over asking the user to click through the Editor
  manually, except for anything about *feel* (see the Play Mode limitation above) — that always
  needs the user's own hands-on test.

## Testing gameplay headlessly

Because Play Mode frames do not advance under this automation, gameplay systems expose public
step methods you can drive from `unity command eval`: `PlateSystem.Step(dt)`, `Island.Tick(input,
dt)`, `Island.AdvanceUplift/FinishUplift()`, `IslandWorld.Step()`, `IslandLifeSystem.Step(dt)` /
`Simulate(lifeSeconds)` / `GetStageCounts`, `WorldStreamer.StreamAround(true)`. Careful: any script
edit on disk triggers an automatic recompile, and a domain reload regenerates every island (a merge
you just staged disappears) — recompile first, then stage the scene, then screenshot.
Force a collision by `SetPlanarPosition` on an AI island next to the player and calling
`IslandWorld.Step()`; fast-forward ecology with a loop of `IslandLifeSystem.Step`. `eval` runs in
the current mode — entering Play first (`editor_play`) is needed for anything that calls
`Destroy`. Remember the values baked into the scene: after changing a field default in code, push
it into the scene objects with `set_component_properties`.
