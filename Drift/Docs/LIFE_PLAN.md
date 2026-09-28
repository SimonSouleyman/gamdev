# Drift — Life Improvement Plan

Decided with the project owner on 2026-09-20. Goal: make the life on the islands **more interesting to
watch**, with **behaviour** as the main focus, while staying fast enough for **budget phones**.
Read `Docs/ARCHITECTURE.md` and `CLAUDE.md` first. This plan extends them; update ARCHITECTURE.md when a
phase lands.

## Decisions

| Topic | Decision |
|---|---|
| Main focus | Animal behaviour and animation first; content and processes second |
| Target hardware | **Budget phones.** Every phase must stay inside the performance budget set in Phase 0 |
| Order | **Optimize first**, then build new behaviour and content on top |
| Interaction | Light only (tap, camera, log, photo). Nothing the player does may affect gameplay |
| Cozy rule | Animals still die only by drowning or fire. No predators (dropped by design) |
| Work split | **One chat, one topic (phase) at a time.** Most life work touches the same files |

Out of scope (not chosen): drinking at the shore, leader/follower formations, deer, mushrooms and moss,
weather shelter behaviour, nests and eggs, real interaction such as feeding or planting.

## Working rules for the chat doing this

- Follow every convention in `CLAUDE.md` (assembly per system, no comments except non-obvious
  constraints, `[ExecuteAlways]` + `OnEnable`, children found through the hierarchy, `DestroyImmediate`
  at edit time, watch triangle winding).
- Systems talk through interfaces. Life must not reference `Drift.Islands`; extend `IIslandSurface`
  (Core) or add a new interface instead.
- Play Mode does not advance frames under automation. Verify with `unity command eval` and the public
  step methods (`IslandHerdSystem.Step`, `IslandLifeSystem.Step/Simulate`). Anything about *feel* or
  *look in motion* needs the owner's own test. Say so instead of claiming it is verified.
- Make a backup zip before each phase and commit only when the owner asks.
- After each phase: recompile, check `unity command console --level error`, run the EditMode tests,
  update ARCHITECTURE.md, and report what still needs owner playtesting.

## Where things live today

| Area | Files (`Assets/_Drift/Scripts/`) |
|---|---|
| Plants and succession grid | `Life/IslandLifeSystem.cs`, `Life/TemplateBatch.cs`, `Life/ShapeBuilder.cs` |
| Animal species and meshes | `Life/LifeMeshes.cs` (`LifeKind`: Grass, Flower, Bush, Tree, Hare, Sheep, Goat, Ox, Bird), `Life/IslandHerdSystem.cs` |
| Birds (and seabirds since Phase 4) | `Islands/FlockSystem.cs` |
| Crabs, turtles, butterflies, fireflies (Phase 4) | `Life/IslandCrittersSystem.cs`, `Shaders/Critter.shader` |
| Fish, water, clouds, day/night | `Visuals/FishSystem.cs`, `Visuals/DayNightCycle.cs` (`NightAmount`), `Visuals/CloudShadows.cs` |
| Audio (procedural) | `Audio/AudioDirector.cs`, `SfxSynth.cs`, `MusicSynth.cs` |
| UI | `Bridge/WorldHud.cs`, `Bridge/SessionScreens.cs`, `UI/TouchControls.cs` (`Drift.UI` TMP framework is unwired) |
| Save | `SaveSystem/*`, life data in `LifeSaveData` and `HerdSaveData` |

Current numbers to be aware of: plant grid `cellSize` 1.2, `maxPlants` 3000, `maxAnimals` 120,
`maxHerds` 40, herd meshes rebuilt at 15 Hz (1 Hz off-screen).

## Phases

Each phase is one chat's worth of work. Do them in this order.

### Phase 0 — Baseline and budget
*Ideas 12.* Measure before changing anything.
- [x] Record draw calls, vertex counts, per-frame cost of the herd/plant/flock/fish updates, and GC
      allocations on a typical island and on the biggest island the game reaches.
- [x] Set a written budget (ARCHITECTURE.md already targets under 100 draw calls steady state and zero
      GC in steady state). Later phases are checked against it.
- [x] Note that real on-phone numbers need the owner's device; say what was estimated versus measured.
- Done 2026-09-20: numbers, budget and the measured-vs-estimated note are in `Docs/PERF_BASELINE.md`
  (re-run with `Assets/_Drift/Scripts/Editor/PerfBaseline.cs` from `unity command eval`).

### Phase 1 — Cheaper animation and simulation tiers, plus cleanup
*Ideas 13, 14, 15.* Makes room for everything after it.
- [x] Move animal animation (walk bob, hop, head movement) out of the 15 Hz CPU mesh rebuild and into a
      vertex shader driven by per-animal data, keeping the vertex-colour look and cloud shadows.
- [x] Simulation tiers by distance from the camera (the M7 plan): near = full behaviour, mid = cheap,
      far/hidden = statistical only. Off-screen islands must cost close to nothing.
- [x] Fix the issues the test pass found:
  - `IslandLifeSystem.Repopulate` seeds from `seed + gameObject.name.GetHashCode()`, so renaming an
    island changes its life and the hash is not stable across runtimes.
  - `IslandLifeSystem.Capture` throws if the grid was never built.
  - `IslandLifeSystem.Simulate(x, 0)` loops forever.
  - `LandBounds` and `LandCentroid` use different definitions of "land".
- [x] Add tests for the offline catch-up path and for the fixes above.
- Done when: the Phase 0 numbers are the same or better with more animals possible, and tests pass.
- Done 2026-09-20: `Shaders/Animal.shader` + `TemplateBatch.AddAnimal` (UV0 = pitch lever/phase, UV1 =
  hop amplitude/moving) animate the herds on the GPU, the herd mesh is only rebuilt when an animal moved;
  `Drift.Core.LifeLod` (near < `detailDistance`, mid < `simDistance`, far) drives herds, plants and the
  flock mesh rate; `IslandHerdSystem.EnforceCaps` keeps 40 herds / 120 animals after merges (surplus taken
  from the most numerous species). Start world: herds 0.073 → 0.047 ms, flocks 0.110 → 0.012 ms; the
  611-area stress island: 100 herds / 657 animals / 40k verts → 19 / 120 / 7.8k, herd rebuild 0.68 → 0.16
  ms. 83 EditMode tests pass (`Tests/LifePhase1Tests.cs`; the fake surface moved to the runtime
  `Drift.Tests.Fakes` assembly because an Editor-only assembly cannot `AddComponent` a MonoBehaviour).
  Still open from the baseline: `ApplyTint` and the sink `RebuildMesh` on big islands.

### Phase 2 — Animal behaviour
*Ideas 6, 7, 4. The main event.*
- [x] Per-animal state machine: **graze** (head down), **look up** (alert, tail flick, stretch),
      **walk**, **rest** (lying down), **sleep**. Species-specific timing and gait (hares hop in bursts).
- [x] **Night:** animals rest and sleep as `DayNightCycle.NightAmount` rises, and wake at dawn. Reach it
      through an interface, not an assembly reference.
- [x] **Play:** young animals chase each other (needs Phase 3 for real young; use small adults until then
      or build the two together).
- [x] Keep existing reactions (huddle in storms, startle on merges, flee, steer uphill from drowning
      ground). New states must not break them.
- Done when: a herd visibly mixes grazing, looking, resting and walking, and sleeps at night.
- Done 2026-09-20 (code and tests; the *look in motion* still needs the owner's Play Mode check):
  `IslandHerdSystem` runs `AnimalState` Graze / Look / Walk / Rest / Sleep / Play per animal with species
  timings and a hop-burst gait for hares; `Drift.Core.LifeEnvironment.NightProvider` (installed by
  `DayNightCycle`) settles herds above 0.6 night, non-watchers sleep, dawn wakes them over ≤ 60 s; at most
  one chasing pair per herd; startle/huddle/shore/fire override the idle states. Poses are baked as three
  `float4` UV channels and blended in `Shaders/Animal.shader` against `_LifeClock`, so a state change is one
  rebuild. Start world: herd step 0.072 → 0.040 ms, rebuilds in 10 s 977 → 182 (near islands 264 → 90);
  99/99 EditMode tests (`Tests/LifePhase2Tests.cs`). Old saves load; states are re-seeded from the night.

### Phase 3 — Life cycles
*Idea 3, babies grow up.*
- [x] Herds produce young over time; young are smaller, follow the herd, grow to adult scale slowly.
- [x] Respect the herd and animal caps and the "only grow past previous peak" logic in `IslandHerdSystem`.
- [x] Save and restore young and their growth in `HerdSaveData`. Old saves must still load.
- Done 2026-09-20 (code and tests; the look of the small pale young in motion needs the owner's Play Mode
  check): the old "+1 adult every 45 s" growth is now a birth — by day (`NightAmount` < `wakeThreshold`), on a
  calm herd (no startle, flee, shore climb or storm huddle) standing on mature ground, one young per herd until
  it is `youngIndependence` (0.7) grown, inside `maxSize`/`maxAnimals`. Young start at `youngScale` 0.45 of
  the adult, paler by `youngTintAmount`, and grow linearly over 4 min (hare), 8 min (sheep, goat) or 12 min (ox)
  of herd time; they keep a slot half a body beside their parent's formation slot, graze in shorter bouts and
  take play pairs first (`youngPlayRate` ×3 while one is awake). Growth is baked into the mesh (`AddAnimal`
  scale + tint; no shader change) and re-baked only per `growthRebuildStep` 4 % (a hare every ~10 s, an ox
  every ~30 s). `HerdSaveData.growth/age/parent` round-trip through JSON; files without them load as adults.
  `IslandLifeSystem.Simulate` hands the fast-forward to `IslandHerdSystem.CatchUp` (life-seconds ÷ `timeScale`),
  which ages young but bears nothing offline. Caps count young; `EnforceCaps` re-parents orphans. 109/109
  EditMode tests (`Tests/LifePhase3Tests.cs`). Start world: herd step 0.14 ms → 0.20 ms with 94 young and 22 %
  more animals, player rebuild 26 ns/vertex unchanged, rebuild counts inside run-to-run noise.

### Phase 4 — New creatures
*Idea 1: crabs, butterflies, fireflies, turtles, seabirds.*
- [x] **Beach crabs:** scuttle along the shoreline, dive into holes when the island moves fast.
- [x] **Butterflies** over flower meadows by day, **fireflies** at night. Cheap: point-like, shader
      animated, capped counts.
- [x] **Turtles** on beaches, **seabirds** on cliffs and rocky islands (extend `FlockSystem` or a sibling).
- [x] Each new creature gets a species spec, area/habitat rules, save data and a hard cap, and stays
      within the budget.
- Done 2026-09-20 (code and tests; the look in motion needs the owner's Play Mode check): one new per-island
  component `Life/IslandCrittersSystem.cs` (added by `WorldStreamer.Spawn` / `VolcanoSpawner.Create`; **the
  scene's player `/Island` still needs it added by hand**) draws crabs, turtles, butterflies and fireflies in
  one "Critters" mesh with the new `Shaders/Critter.shader` (wing flap, bob and firefly blink in the vertex
  shader, 6–24 verts per creature). Crabs: one per 4 u of estimated shoreline (disc perimeter of the land
  area), ≤ 12, on the 0.02–0.25 shore band, scuttle sideways along the water line, pause, dig, and dive into
  their holes for 3–6 s when `IIslandSurface.Speed` (new; the island's own drive) exceeds 1.5 u/s. Turtles: only
  above area 20, ≤ 3, crawl 0.08 u/s from the shore to 0.55 height and back by day, rest 20–60 s. Butterflies
  (≤ 16, one per 3 flowers, `NightAmount` < 0.5) fly flower to flower (`IslandLifeSystem.TryRandomPlant`);
  fireflies (≤ 24, one per 8 area, `NightAmount` > 0.6) prefer meadow/wood-edge cells. Both fade over 1.5 s and
  only exist in the near tier (mid freezes them, far drops them); crabs/turtles persist in
  `LifeSaveData.critters`. `FlockSystem` gained 2 seabird flocks (`LifeKind.Seabird`, 48 verts, white/grey,
  gliding without flap) that circle Barren/Volcanic islands or peaks above 1.7 u and never land. Start world:
  32 islands, 137 crabs, 22 turtles, 17 fireflies at night, all critters 0.019 ms/frame desktop (budget 0.17),
  0 B/frame in steady state; 123/123 EditMode tests (`Tests/LifePhase4Tests.cs`, 14 new).

### Phase 5 — Flora and ecosystem processes
*Ideas 2, 8.*
- [x] **Beach palms and reeds** that sway in the wind (wind vector already exists in `WaterFeedback`).
- [x] **More flower colours and shapes**, which also feed the butterflies.
- [x] **Fire scars and regrowth:** burnt ground turns dark, then fresh green shoots come back.
- [x] **Flower blooming waves** that spread and fade across meadows.
- [x] **Visible tree growth** from sapling to full size.
- [x] **Colour seasons:** a slow island-wide tint shift (for example towards autumn), through the existing
      tint path (`TintAt`), not new textures.
- Note the existing rule: succession only grows, a plant dies only by fire or drowning.
- Done 2026-09-20 (code and tests; sway, bloom waves, sapling growth and the season drift *in motion* need the
  owner's Play Mode check): `LifeKind.Palm`/`Reed` (30 / 45 verts) grow below every third / second **shore cell**
  (a land cell with water in its 8-neighbourhood — islands are plateaus with steep rims, so a height band alone
  found 3–6 cells per island) on the beach (0.05–0.35) / at the waterline (≤ 0.12), found by a short downhill walk,
  caps 24 / 40, die only by fire or drowning. `Shaders/VertexColor.shader` bends vertices along the global
  `_LifeWind` (xy = `LifeEnvironment.Wind` × (1 + 1.5 storm), z = storm; providers installed by `WaterFeedback`,
  pushed once per frame by `IslandLifeSystem.PushWind`) by UV0 = (height weight², phase, height) with a slow gust
  and a storm-scaled flutter; `TemplateBatch.AddPlant` bakes the channel, everything else stays rigid. Flowers come
  in 6 colours × 3 shapes (colour and shape from the cell hash), bloom per cell on a 4-min cycle offset by noise
  (scale 0.5–1.2, brightness 0.8–1.15, evaluated every `bloomInterval` 1 s, baked when a flower cell moved > 0.08),
  scars fade over 720 life-seconds (2 real minutes) while grass on them is drawn as small saturated shoots, trees and
  palms spawn at 0.3 and reach full size after 240 life-seconds in 5 % steps, and a 20-min colour season (spring →
  summer → autumn → back, ≤ 25 % per channel) tints ground, trees, bushes and grass, is saved (`LifeSaveData.season`,
  old files → 0) and copied to newly streamed islands from `SeasonReference`. A vertex budget (`maxVegetationVerts`
  60k) thins big islands' grass/bush/tree density evenly and fades a merge's cheapest surplus first. Start world:
  109 palms, 134 reeds, life step over 32 islands 0.118 ms (was 0.104), vegetation rebuild ≈ 27 ns/vertex (was
  17–25); the 611-area stress island now sits at 53.4k vegetation verts (was 58.7k) with `ApplyTint` unchanged;
  133/133 EditMode tests (`Tests/LifePhase5Tests.cs`, 10 new).

### Phase 6 — Life sounds
*Idea 10.* Fully procedural, no audio assets.
- [x] Habitat sounds in `Drift.Audio`: bird calls by day, crickets at night, rustling and waves, chosen by
      the nearest island's life and `NightAmount`.
- [x] Reuse the existing zero-GC synth approach and stay inside its CPU budget.
- Done 2026-09-20 (code, tests and eval analysis; *how it sounds* needs the owner's ears in Play Mode):
  `Audio/LifeSynth.cs` is a third `ISynthSource` rendered by a runtime "Life" child of the Audio object
  (`AudioDirector.lifeVolume` 0.5, `lifeEnabled`), block-synthesised with no assets and no allocation: three
  songbird singers (2–5-note pentatonic motifs with per-note glides and FM trill, Markov-ish steps, rests 8 → 2 s
  with density), a seabird "kee-aw" (rise then a descending harmonic stack with a 28 Hz rasp), two detuned cricket
  layers (4.2 / 4.34 kHz carriers, 31 / 27 Hz pulse trains, chirp gating, slow LFO), leaf rustle (white noise through
  a 12 dB/oct 1.4–2.5 kHz low-pass and an 800 Hz high-pass with gust and flurry envelopes), shore lapping (filtered
  noise pulses every 1.4–2.6 s), frog bloops at dusk, and rare sheep/ox calls (harmonic stack, tremolo, one formant).
  `LifeSoundScout` reads the player's and the AI islands' life components within `lifeRange` 40 u (woods/old-growth
  and meadow cells, trees and palms, reeds, sheep/ox herds and their sleeping fraction, fireflies, flock positions and
  sizes) once per `lifeUpdateInterval` 0.5 s; `LifeSoundMix.Apply` maps the counts through a saturating `Density`
  and gates by `LifeEnvironment.NightAmount`, wind, storm and player speed. The layer ducks by 50 % on impacts (3 s)
  and above tension 0.7. Measured in the Editor: idle 0.04 %, day 0.34 %, night 0.50 %, everything at once 0.57 % of
  a core (budget 0.6 %; music is 0.83 %, sfx 0.28 %); 145/145 EditMode tests (`Tests/AudioPhase6Tests.cs`, 12 new).
  The Tests asmdef now references `Drift.Audio`, `Drift.Audio` references `Drift.Life`, and `FlockSystem` gained
  the one-line accessor `BirdCountOf(int)`.

### Phase 7 — Watch tools
*Ideas 9, 11. Light interaction only.*
- [x] **Tap an animal** for a small popup (species, herd size, mood).
- [x] **Follow-a-herd camera** with an easy way back to the player island.
- [x] **Discovery log:** species seen collect in a journal with a completion count. This also covers the
      ecosystem stats idea.
- [x] **Photo mode:** hide the HUD, free camera, save an image.
- [x] Build on `WorldHud`/`SessionScreens`/`TouchControls`. Touch first, since the target is phones.
      Nothing here may change simulation state.
- Done 2026-09-20 (code, tests and edit-mode previews; taps, drags, pinches, the follow camera's feel and a
  real saved photo need the owner's Play Mode / device check): new component `Bridge/WatchTools.cs` (own canvas,
  sorting 15, **has to be added to the scene's `SessionUI` object and the scene saved** — streamed nothing, no
  other wiring) with the pure helpers `Bridge/TapPicker.cs` and `Bridge/PhotoRig.cs`. A tap (≤ 0.35 s, ≤ 24
  canvas units of travel, not over UI, not in the thumbstick half on touch screens) picks the nearest animal of
  a near-tier herd within 40 canvas units and shows a popup that follows it for 4 s (species, „Jungtier", herd
  size, mood from `AnimalState`); „Herde folgen" hands `IslandChaseCamera.SetFollowOverride` the herd centre
  (zoom eases to ≤ 0.6, back to the old zoom on return) with a big „Zurück zur Insel" button and an automatic
  return when the herd or island is gone or the run ends. `SaveSystem/DiscoveryJournal.cs` (11 species: 4 herd
  animals, bird, seabird, crab, turtle, butterfly, firefly, fish) is filled by a 1 Hz scan of everything within
  25 u of the camera focus, counts young born and lightning fires on those islands, and is saved as
  `SaveGame.journal` (**save v5**; v2–v4 files load with an empty journal). Journal panel from the pause menu
  („Tagebuch") or the HUD book icon (pauses, resumes on close); photo mode from the pause menu („Foto"): game
  resumes with `Island.InputLocked`, HUD hidden, orbit/pan/zoom camera, „Foto speichern" →
  `persistentDataPath/Photos/drift_<timestamp>.png` without UI, „Zurück" snaps the chase camera back.
  160/160 EditMode tests (`Tests/WatchToolsTests.cs`, 15 new). Nothing in here writes simulation state.

## Definition of done for the whole plan

- The owner has played it and confirms the life is more interesting to watch (this cannot be checked
  headlessly).
- Performance stays inside the Phase 0 budget on a budget phone.
- Saves from before the change still load.
- ARCHITECTURE.md and CLAUDE.md describe the new systems and any new gotchas.
