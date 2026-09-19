# Drift — Architecture & Build Plan

This is the durable, version-controlled copy of the architecture plan agreed on 2026-09-19.
It gets updated as milestones land or decisions change — treat it as the source of truth over
any local/session-only planning notes.

## Context

Drift is a cozy, no-lose, session-interruptible (train/bus) mobile game (Android + iOS): you
steer a small island across a literal 3D planet, absorbing other landmasses agar.io-style to
grow from a tiny island into a continent. Two pillars: (1) the growth/merge loop, (2) a "deep"
ecosystem simulation of flora/fauna that keeps evolving even while the app is closed. Tectonic
plates double as gameplay (movement/terrain). Visual style is pixel-art-on-3D.

Controls: WASD on desktop (racing-game style — W/S throttle forward/back, A/D turn), a virtual
thumbstick on mobile (Input System ships `OnScreenStick` for this — no extra package needed).
Camera is a top-down chase camera (high pitch, behind-and-above the island), not a free orbit.

**Current phase: flat-ground prototype (2026-09-19).** The sphere planet is parked (the `Planet`
GameObject is deactivated, `PlanetSphere`/`PlanetTopologyBaker` untouched) in favor of a flat ocean
plane (200x200, +-100) so island physics, collisions and the plate system can be iterated in
isolation. `Island` no longer has a sphere mode — bringing the sphere back means re-deriving the
island/plate/life math on a sphere, not flipping a flag.

**Timescales.** Geology is fast, life is faster: `PlateSystem.timeScale` scales plate waves and
plate-driven drift; `IslandLifeSystem.timeScale` (default 6, doubled from 3) scales the vegetation
cycle, so a plains -> woods -> fire -> regrowth loop plays out in roughly 20-60 real seconds.
Animation (sway) uses real time.

**Endless world.** There is no world edge. `WorldStreamer` keeps a (2r+1)^2 block of 110-unit chunks
around the player: each chunk deterministically plans 2-4 islands from a hash of its coordinates,
spawns them one per frame, and destroys them again when the player is more than `loadRadius +
unloadMargin` chunks away (absorbed islands are remembered so they do not respawn). The ocean
plane follows the camera (`WaterFollower`). Plates are an infinite jittered grid generated lazily
per cell (see plates below), so nothing is stored per world size.

## Key architectural decisions

1. **World = two decoupled systems, no runtime mesh booleans.**
   - `PlanetSphere` (`Drift.World`): a baked-at-edit-time icosphere tile graph
     (`PlanetTopologyAsset`), ~20 chunked meshes (one per icosahedron face) so only touched
     chunks rebuild. Data substrate for plates/biomes/ecosystem capacity — not what moves or
     collides. Elevation is real (not clamped at sea level) so the terrain shader — which derives
     color purely from world-position elevation, no textures yet — reads correctly; clamping
     ocean to a flat radius previously caused a speckled dithering bug at the ocean/sand boundary.
   - `Island` (`Drift.Islands`): one GameObject per island (player + AI). Each island owns an
     `IslandShape` — a heightfield grid (0.5-unit cells, local XZ, values above 0 are land, `Sea`
     = -0.8 is water) generated from a seeded noisy blob and meshed by `FillMesh` (plateau, beach
     slope, then below the water plane so the shoreline is just the mesh intersecting the ocean).
     Islands *float*: they sit at y=0 with a small bob and are carried by the plates. State is a
     planar position `_pos`, a heading `Forward` (`Normal` is always up) and `_carry`/`_selfVel`
     velocities; `ToWorld/ToLocal` map island-local XZ to world XZ. Implements `IIslandSurface`
     (Core: bounds, area, version, `SampleHeight`) so `Drift.Life` never depends on `Drift.Islands`,
     and `IPlateRider` so `Drift.Tectonics` never depends on it either.
   - **Heavy islands** (`Island.Tick`): velocity is state, not input. Thrust accelerates it,
     drag decays it, both scaled by `Agility = (agilityArea / (agilityArea + area))^0.6`, so a
     big island accelerates and turns far more slowly (and coasts on its momentum) and has a lower
     top speed. Turning has its own angular inertia. Measured: area 24 needs ~4-8 s to reach 7
     u/s; area 680 only reaches ~4 u/s after 8 s and turns at a few degrees per second.
   - **Collision = merge + energy-driven mountain belt** (`IslandWorld.Step`, `Island.MergeFrom`).
     Broad phase is bounding-radius overlap, narrow phase samples one island's land cells against
     the other's heightfield. The player always hosts a merge (no-lose: touching a bigger island
     is a merge, not a defeat); between two AI islands the larger hosts. Momentum is conserved
     (the merged island keeps the mass-weighted velocity, so it visibly slows), and the collision
     energy `E = 0.5 * mu * vClose^2` (reduced mass, area units) decides the mountain height
     `H = 0.22 * sqrt(E) * (1 + 0.25 * plateConvergence)`, capped at `1 + 0.35 * minR` (a typical hit peaks around 3.5 units, half of before).
     The ridge is a Gaussian across the collision normal, tapered along the suture, with ridged
     noise and flanks, on top of a 35% crust thickening in the overlap. Contact is detected as soon as
     shorelines touch (land over the other's shallows), and the join gets a smooth union plus a land
     bridge so the two masses fuse into one broad island instead of touching at a neck. It rises slowly over
     `upliftDuration + H` seconds (about 10 s for a typical hit) and then bakes in. The impact also
     shakes the camera (`Island.Impact` event) and shoves the plate under it. After a merge the
     pivot is re-centered on the land centroid and the guest's vegetation and cell states are
     transferred.
2. **Tectonic plates = waves (`Drift.Tectonics.PlateSystem`).** An infinite jittered grid of
   plates (one per 90-unit cell, created lazily from a hash of the cell coordinates). Each
   plate's position is `home + dir * amp * sin(omega*t + phase) + offset`, so plates surge back
   and forth like waves and boundaries alternate between convergent (red), divergent (cyan) and
   transform (yellow), classified from relative velocity along the seed-to-seed normal as in
   the reference diagrams; arrows show each plate's motion. Borders are computed only for a window
   around the focus (player/camera) by half-plane clipping with edges tagged by neighbour plate,
   and drawn as a vertex-colored mesh. **One system with the islands:** islands are carried by
   the velocity of the plate they float on, riders push their plate back (momentum scaled by
   `Mass / (Mass + plateMass) * riderPush`, springing back), collisions add an impulse, and
   `ConvergenceAt` feeds mountain height. Stochastic terrain events are still to do.

3. **Camera**: current implementation (`IslandChaseCamera`, M2) is a hand-rolled top-down chase
   camera — position = island position + `Normal * height + (-Forward) * distanceBehind`,
   exponential-smoothed follow/look. Gets replaced by Cinemachine (already added as a package)
   with mass-driven zoom in M7, transitioning from this close-up framing out to a full-planet view
   as the player grows. In the flat prototype the camera starts 9 up / 7 back and its framing scales
   with the player island's bounding radius (`referenceRadius`, `zoomExponent`), plus a
   collision shake.

4. **Vegetation succession** (`Drift.Life.IslandLifeSystem`; animals removed for now, the
   meshes stay in `LifeMeshes` for later). Each island has a coarse cell grid (1.4 units) holding a
   succession `stage` per land cell: 0 bare -> plains (grass, flowers) -> shrub (bushes) -> woods
   (trees) -> old growth. Stage grows with fertility (from elevation: beach, lowland, tree line,
   alpine) and clamps to a per-cell maximum, so uplift kills trees on rising mountains. Cycle
   events: lightning and stage-driven ignition start fires that spread to neighbours for ~20 life
   seconds, leaving ash (dark ground tint, reset stage) that regrows with a fertility boost;
   old-growth stands collapse back to shrub. Plants are *data*, not GameObjects: counts per cell
   follow the stage, and every plant of an island is baked into ONE dynamic mesh
   (`Vegetation` child, rebuilt at ~7 Hz only when dirty and only near the camera; far islands
   simulate at 1/8 rate and hide beyond `hideDistance`), with wind sway done in the vertex
   shader. The ground colour follows the stage through per-vertex colours written to the island
   mesh (`IIslandSurface.ApplyGroundTint`).
   Not built yet: animals/predators, real Tier-B offline catch-up (plan below), saving.
   Tier B plan: closed-form logistic growth `P(t) = K·P0·e^(rt) / (K + P0·(e^(rt)-1))` evaluated
   once per species per region — never a per-elapsed-second loop.

5. **Save/load + offline catch-up (`Drift.SaveSystem.SaveManager`, built).** Saves on
   `OnApplicationPause(true)`, `OnApplicationQuit` and every 20 s, as JSON in
   `persistentDataPath/drift_save.json`, written to a temp file and swapped in (`File.Replace`).
   Persisted: the player island (heightfield with mountains baked in, pose, momentum), its
   vegetation grid (stage / ash / fire per cell — plants are re-derived from that), plate time
   and pushed-plate offsets, the streamer's start position and the set of absorbed AI islands.
   AI islands are not saved: they regenerate deterministically per chunk (an AI-AI merge is lost
   on reload — acceptable for now). On load the elapsed real time is turned into life seconds
   (`elapsed * timeScale`, capped at `maxOfflineLifeSeconds` = 1800) and the vegetation is
   fast-forwarded in 10-second steps (`IslandLifeSystem.CatchUp`), so the cost is bounded no matter
   how long the app was closed. Verified: capture -> wipe -> load restores area, mountain height,
   position, stage counts and plate offsets exactly (13 KB file). Editor menu:
   `Drift/Save/Delete Save File` — do this when you want a fresh start, because Play resumes the
   save by default (`SaveManager.loadOnStart`).

6. **Mobile performance** (ongoing): agent-count budget (~300-500) across three simulation LOD
   tiers by camera focus; GPU instancing for flora/fauna billboards; one texture atlas for
   terrain; SRP Batcher + pooling everywhere; dirty-chunk-only mesh rebuilds. Target <100 draw
   calls steady-state — M1's planet alone is 27 draw calls, comfortably inside budget.

## Project structure

Assembly-per-system under `Assets/_Drift/Scripts/`: `Drift.Core` (generation + shared
interfaces such as `IIslandSurface`, no Unity-specific deps beyond UnityEngine), `Drift.World`
(sphere planet, parked), `Drift.Tectonics` (plates; knows only `IPlateRider`), `Drift.Life`
(procedural flora/fauna; knows only `IIslandSurface`), `Drift.Islands` (islands, collisions, camera;
references Core/Tectonics/Life), `Drift.Editor` (editor-only tools), `Drift.SaveSystem`, `Drift.UI`
and `Drift.Tests` (not created yet). Cross-system links go through interfaces so no assembly
cycles form.

Folders: `Assets/_Drift/{Scripts,Prefabs,ScriptableObjects,Materials,Textures/PixelArt,Shaders,
Scenes,Data}`. Baked data assets (e.g. `PlanetTopology.asset`) live in `Assets/_Drift/Data`.
Reference/mood-board images live in `Reference/` at the project root (not under `Assets`, so
Unity never imports them as textures).

## Build order

- **M0** — stock URP template. *Done.*
- **M1** — baked `PlanetTopologyAsset`, chunked textured icosphere, orbit camera. *Done* (orbit
  camera later replaced — see M2).
- **M2** — controllable island: WASD great-circle movement, starter landmass with real area (not
  a point), top-down chase camera. *Done*, pending the user's own interactive playtest (see
  Known Issues — Play Mode below).
- **M3** — AI islands + collisions. *Done in flat mode:* heightfield islands, merge on contact,
  collision mountain belts, plate-carried AI islands. Not yet: AI wander/steering, erosion.
- **M4 (early)** — procedural geometric life on islands, moving and cycling. *First pass done*
  (see ecosystem above); depth/offline catch-up still to do.
- **M6 (early)** — tectonic plates as waves with borders and island coupling. *First pass done.*
- **M4** — (full) `SpeciesDef`s (flora + 1 herbivore), real-time-only ecosystem tick, no save yet.
- **M5** — save/load + offline catch-up. *First pass done* (stepped fast-forward instead of the
  closed-form solution; AI islands not persisted).
- **M6** — tectonic plates: boundary classification, current nudges, stochastic terrain events.
- **M7** — Cinemachine mass-driven zoom + ecosystem/rendering LOD tiers + billboard clamp.
- **M8** — texture atlasing, GPU instancing, pooling audit, on-device profiling build.

## Known issues / environment quirks

- **Play Mode does not advance frames under headless/unfocused automation.** When Claude drives
  the Editor via the CLI in this environment, `Time.frameCount` can get stuck at 1 indefinitely
  even with `unity command set_autotick` enabled and `unity command editor_focus` called — the
  Player Loop appears to need real OS-level window focus that a remote/headless session doesn't
  have. Workaround used so far: call `unity command eval` to directly invoke gameplay methods
  (e.g. `island.Tick(input, dt)` in a loop) and manually compute/apply camera framing, rather than
  waiting on real-time simulation. This is a tool limitation, not a bug in Drift's code — real
  interactive Play Mode works normally in the user's own focused Editor window, which is also the
  right place to test feel/input (WASD, touch) that an automated session can't meaningfully judge.
- Mesh winding matters for the island blob (and any custom mesh): a front face must be wound so
  its normal points the intended direction or it back-face-culls invisible from that side. Bit us
  once on the island blob (fixed) — worth double-checking on any new procedural mesh.
- Ocean tiles must get *real* negative elevation in the mesh (not clamped to 0) — a shader that
  derives color from world-position elevation will misread a flattened ocean surface as
  elevation-0 and dither between ocean/sand colors. Bit us once on the planet terrain (fixed).
- **Track procedurally-rebuilt children by the actual transform hierarchy, not a plain C# list.**
  A private `List<GameObject>` field (no `[SerializeField]`) does not survive a domain reload
  (every `recompile`), so a rebuild routine that only destroys what's in that list forgets about
  the previous batch after any recompile and doubles up. `PlanetSphere.BuildChunks()` hit exactly
  this (40 chunks instead of 20) — fixed by destroying `transform`'s actual children instead of
  trusting the list. Apply the same pattern to any future rebuild-my-children component.
- **`HideFlags.DontSaveInEditor` children created in Edit Mode do not carry into Play Mode** the
  way you'd expect from "the same in-memory scene continues." `AIIslandSpawner`'s children (built
  at edit time so they're visible without pressing Play, per the ExecuteAlways/OnEnable
  convention) were still edit-time objects when Play Mode started, and confirmed present via
  `unity command eval` immediately after `editor_play` — but a `FindObjectsByType<T>()` query
  from `eval` found only objects that existed at Play Mode's *start*, not ones a spawner's
  `OnEnable` would only recreate if it re-ran (which it doesn't, since the spawner GameObject
  itself wasn't disabled/re-enabled going into Play). Net effect: don't rely on `FindObjectsByType`
  for edit-time-spawned content right after entering Play from `eval`; walk the known parent's
  `transform` children directly instead (as the M3 absorption verification does). Also remember
  `Destroy()` is deferred to end-of-frame — calling it from `eval` leaves the object non-null
  until a real frame actually completes, which (see above) may not happen in this automation.
- **Anything that rebuilds its own children must find them via the hierarchy** (marker component
  or child name), never via a plain list field — lists reset on every recompile. Applies to
  `PlanetSphere`, `PlateSystem` borders, `AIIslandSpawner`, `IslandLifeSystem`.
- **Serialized values do not follow C# default changes.** Changing a field default in code does
  nothing for components already in the scene; push new tuning with `set_component_properties`.
  Also, setting a field through the CLI does not re-run `OnEnable` — toggle the GameObject
  inactive/active to rebuild (this bit the plate border material and the spawner material).
- `Destroy()` is a Play-Mode-only call (deferred); edit-time code must use `DestroyImmediate`.
- The Editor can be closed under us (`unity status` shows no instance): `unity open <project>`
  relaunches it (it opens the last/startup scene — re-open `Planet.unity`).
