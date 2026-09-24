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
thumbstick on mobile (the custom `Drift.UI.TouchControls`: floating stick + pinch zoom, no `OnScreenStick`).
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

**Bounded world with connected edges.** The world is a torus of `worldChunks` x `worldChunks`
chunks (6 x 110 = 660 units per side): sail off one edge and you re-enter on the opposite one. Nothing
is actually wrapped in coordinates — the player keeps growing unbounded coordinates and content is
generated from the *wrapped* chunk id, so islands repeat every 660 units and plates too
(`PlateSystem.gridPeriod`, keep `cellSize * gridPeriod == worldChunks * chunkSize`). `WorldStreamer`
keeps a 3x3 block of chunks loaded, spawns one island per frame, unloads far ones, and remembers
absorbed islands (by wrapped id, so an absorbed island is gone from every copy of the world). What a chunk
contains is described under "Island archetypes and the world plan". The
finite island supply is the goal: the player wins the map by absorbing everything (`Progress` /
`WorldSlots` give remaining area and positions for a HUD/minimap). The ocean plane follows the camera.

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
   - **Collision = drive-in, then merge + energy-driven mountain belt** (`IslandWorld.Step`,
     `Island.MergeFrom`). Broad phase is bounding-radius overlap, narrow phase samples one island's
     land cells against the other's heightfield and reports both shelf contact and land-over-land
     (`deepCells`). First touch does *not* merge: `IslandWorld` keeps a per-pair contact record and
     runs a **drive-in phase** — the host gets extra drag (`impactDrag`) while it shoves the guest
     along the contact normal (`pushRatio`), so the two masses visibly grind into each other. The
     merge fires once `deepCells` covers `mergeOverlap` (10 %) of the smaller island, or after
     `maxContactTime` (2 s) for a glancing blow. A 4 u/s ram takes ~0.8 s and leaves ~0.3 u/s;
     a slow drift-in takes the full 2 s. The impact speed is recorded at *first* contact and passed
     to `MergeFrom`, otherwise the late merge would read a near-zero closing speed and flatten the
     mountains. The player always hosts a merge (no-lose); between two AI islands the larger hosts.
     Momentum is mass-weighted and then scaled by `impactRetention` (0.8), so the merged island is
     markedly slower. The collision energy `E = 0.5 * mu * vClose^2` decides the mountain height
     `H = 0.22 * sqrt(E) * (1 + 0.25 * plateConvergence)`, capped at `1 + 0.35 * minR` (a typical hit
     peaks around 3.4 units). The ridge is a Gaussian across the collision normal with ridged noise,
     on top of a 35 % crust thickening and a land bridge at the join, and rises over
     `upliftDuration + H` seconds before baking in. The impact also shakes the camera and shoves the
     plate underneath.
2. **Tectonic plates = waves (`Drift.Tectonics.PlateSystem`).** An infinite jittered grid of
   plates (one per 90-unit cell, created lazily from a hash of the cell coordinates). Each
   plate's position is `home + dir * amp * sin(omega*t + phase) + offset`, so plates surge back
   and forth like waves and boundaries alternate between convergent, divergent and transform,
   classified from relative velocity along the seed-to-seed normal as in the reference diagrams.
   Borders are computed only for a window around the focus (player/camera) by half-plane clipping
   with edges tagged by neighbour plate. **They are drawn as water, not as lines** (`Drift/PlateSeam`
   on `Materials/PlateBorder.mat`, queue Transparent+1, premultiplied alpha, ZTest LEqual so islands
   occlude it): one soft ribbon per boundary (`seamWidth` 8 u mesh, ~5.5 u visible, `borderHeight`
   above the sea), split along the centre line into a plate-A and a plate-B strip. **The ribbon is a
   meandering fault line, not the straight Voronoi edge (2026-09-20, `PlateSystem.Seams.cs` — the one
   source of truth for the curve).** `Border.p0 -> p1` stays the straight chord (that is what "which
   plate carries me" uses: `NearestPlate` is untouched); the visible seam is
   `SeamPoint(b, t) = lerp(p0, p1, t) + b.normal * amplitude * taper(t) * Meander(key, s)` with `s` the
   along coordinate relative to the midpoint of the two plates (the bends stay put on the pair while a
   junction wanders). `Meander` = 78 % a lazy meander (lattice 26–38 u per border, bends alternate sides
   at every lattice point, so a border is never straight) + 22 % a free wobble (lattice ≈ 14–20 u); in
   plate time only the *depth* of each bend breathes (0.3–1 over 90 s, wobble 45 s ≈ ≤ 0.1 u/s), nothing
   slides along the seam. `amplitude = min(seamMeanderMax 6 u, seamMeander 0.12 × chord length)`; the
   cap is "small-island radius + visible half width": an island that is visibly clear of the ribbon is
   never carried by the plate on the other side of it. The taper (smoothstep over ≤ 15 u, zero value
   and slope at both ends) keeps the three borders of a triple junction meeting in exactly one point;
   ends cut by the border window (`open0/open1`) are not tapered. Normal, direction of `t` and the noise
   key come from the wrapped core ids of the pair, so the curve is identical in every copy of the torus
   and independent of which plate is `a`. API: `SeamPoint`, `SeamTangent` (always p0 → p1),
   `SeamOffset`, `SeamLength`, `SeamAdvance` (walk an arc length), `ClosestOnSeam` (false past an end),
   `NearestSeam`, `MakeBorder`, static `Meander`. Events (`RollEvents`), hence storms, and
   `VolcanoSpawner` (seam search, the permanent hold, island arcs) all sit on that curve. The mesh is a
   continuous strip per side with miter joins (≈ one cross-section per `seamSegment` 6 u, ≤ 24 per
   border, stretched automatically to stay ≤ `MaxSeamVertices` 1808; the inside of a tight bend is
   pinched so it cannot fold), UV0.y = true arc length offset to the plate-midpoint coordinate of end 0,
   half width varied ±`seamWidthVariation` 25 % by noise along the seam, a world-space noise ragging the
   soft outline in the shader; both ends still overshoot by the half width and the shader rounds them
   into a capsule that fades to 0 alpha. Borders entirely beyond the distance fade are skipped. Looks blend continuously on `closing / transformThreshold`: *convergent* = foam fronts
   rolling in on the centre from both sides + broken crest + dark troughs, *divergent* = light
   turquoise upwelling band with rings welling up out of noise maxima and a faint warm glow,
   *transform* = long shear streaks (storm whitecaps from `StormSystem.IntensityAt`, sampled along
   the seam only while a storm is alive). The foam of each half moves with its plate relative to the
   seam via closed-form scrolls computed on the CPU (no shader time, no accumulated state): inward =
   half the change of the plate distance, along = half the arc the pair has turned through relative
   to its rest direction (`seamFlowGain` 1.5); that water displacement vector is projected on the local
   tangent / normal of the curve at every cross-section and interpolated along the strip. The old arrow meshes are gone; each half carries
   scattered soft foam chevrons anchored to its plate (they travel with it and point the way it
   moves, fading at standstill). Event cues are soft expanding rings in the same mesh (`side == 0`).
   Vertex layout (UV0-3) is documented at the top of `PlateSeam.shader`; the ribbon fades out
   towards `seamFadeDistance` from the focus and a little when the camera is very close;
   `_SeamTimeOffset` (global float) offsets the shader clock for edit-mode checks. **One system with the islands:** islands are carried by
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

4. **Vegetation succession** (`Drift.Life.IslandLifeSystem`). Each island has a coarse cell grid (1.4 units) holding a
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
   mesh (`IIslandSurface.ApplyGroundTint(cellColors, cellsX, cellsZ, gridOrigin, gridCell)`: the life
   grid is handed over and bilinearly filtered per terrain vertex inside `Island`; it used to be a
   per-vertex callback, 3 ms per tint on a big island).
   **Animals (peaceful only):** `IslandHerdSystem` keeps herds per island — hares (many, small,
   fast hops), sheep, mountain goats (only on islands with highland) and oxen (few, large, slow) —
   unlocked by island area, with the herd count growing with area. A herd has a leader that wanders
   and grazes; members follow in formation, and the whole herd flees burning cells. All animals of an
   island are one dynamic mesh (`TemplateBatch`). `FlockSystem` runs bird flocks (4-9 birds) around the
   player that fly island to island, orbit each one for a while, then pick the next; wing flapping is
   a vertex-shader effect (`_Flap`). Herds transfer on merges like vegetation.
   Tier B plan: closed-form logistic growth `P(t) = K·P0·e^(rt) / (K + P0·(e^(rt)-1))` evaluated
   once per species per region — never a per-elapsed-second loop.

5. **Save/load + offline catch-up (`Drift.SaveSystem.SaveManager`, v2).** Saves on
   `OnApplicationPause(true)`, `OnApplicationQuit` and every 20 s (only while Playing/Paused —
   `GameSession.SavingAllowed`), as JSON in `persistentDataPath/drift_save.json` (temp file +
   `File.Replace`). Since 2026-09-21 only the capture runs on the main thread; `JsonUtility.ToJson` and
   the file write run on a worker (`SaveManager.Save`), except on pause/quit (`SaveBlocking`). `Load`,
   `DeleteSave` and `HasValidSave` wait for a write in flight. Rule for anything added to the save: its
   `Capture()` must hand out fresh arrays/objects, and nothing may mutate captured data afterwards. Persisted: the player island (heightfield, pose, momentum, sink), its
   vegetation grid, plate time and pushed-plate offsets, the streamer start position and absorbed
   slot keys, **plus every streamed AI island that differs from its chunk plan and every live
   volcano**. `SaveGame.worldGenVersion` stores `WorldStreamer.WorldGenVersion` (currently 2, bumped whenever
   `Plan()` lays the world out differently for the same seed): consumed slot keys and overrides only make sense
   in the layout that produced them, so `Readable`/`Peek`/`HasValidSave` and `Load` reject any other value —
   a pre-archetype save reads 0 (the field has deliberately no initialiser, JsonUtility would keep it for a
   missing key), the title shows no "Weiter" and the file is simply overwritten by the next run. `WorldStreamer` records each slot's planned area/peak/version at spawn and treats an
   island as *dirty* when it is uplifting, its shape changed (area > 20 % or peak > 0.1) or it sits
   > 2 units from its slot / carries self-velocity. Dirty islands are saved as per-slot overrides
   keyed by the wrapped slot key (`IslandSaveData.slotKey`): pose-only (~150 B) or full heightfield
   + vegetation grid; overrides are captured on save and on chunk unload and applied when the slot
   streams back in, re-based onto whichever copy of the torus is loading. Volcanoes save heightfield,
   sink and `emergeRemaining` (a half-risen one resumes via `Island.BeginEmergence(duration,
   startSink)`) plus the spawner cooldown. Files of version < 2 are ignored. Load order: plates →
   streamer overrides → player (+ life catch-up capped at 1800 life-seconds) → volcanoes →
   `StreamAround(true)`. Typical file 16–36 KB. Herds are persisted inside `LifeSaveData.herds`; storms are transient. **Save v3** adds `worldSeed`/`legacySeeds`; v2 files are still read (`MinReadableVersion`) and keep the scene's per-system seeds (`legacySeeds`). `SaveManager.Load` applies the seed via `GameSession.ApplyWorldSeed` *before* plates/streamer restore. `HasValidSave` (cached on file mtime/size) is what "Weiter" uses. A clean slot whose life has lived > 30 s is also stored as a pose override so visited islands keep their herds and succession.
   Title screen decides: `SaveManager.loadOnStart` is false, "Weiter" calls `Load()`.

6. **Mobile performance** (ongoing): agent-count budget (~300-500) across three simulation LOD
   tiers by camera focus; GPU instancing for flora/fauna billboards; one texture atlas for
   terrain; SRP Batcher + pooling everywhere; dirty-chunk-only mesh rebuilds. Target <100 draw
   calls steady-state — M1's planet alone is 27 draw calls, comfortably inside budget.
   **Per-frame budget (2026-09-20 pass).** All simulation systems are allocation-free in steady
   state; measured with 32 islands the stepped systems cost ~0.7 ms/frame on desktop (plates 0.12,
   life 0.18, herds 0.14, flocks 0.11, collisions 0.02; was ~1.5 ms and ~92 KB GC/frame). Plate
   borders are re-clipped and re-meshed at `borderRefreshInterval` (30 Hz; plates move ≤4 u/s) with
   pooled buffers (curved seam ribbons: one interleaved `SeamVertex[]` + `ushort[]` of fixed size uploaded with
   `SetVertexBufferData`, 4 verts per cross-section, ~860–920 verts for the 31-boundary window, hard cap 1808 +
   event rings; `RebuildBorderMesh` 0.075 ms (0.086 ms with a storm sampled per cross-section), `ComputeBorders`
   0.09 ms, `Step` ≈ 0.085 ms avg at 60 fps with one island, 0 B; the straight ribbons were 0.02 / 0.055 ms). `WorldStreamer.WorldSlots()` is cached and invalidated by
   `Consume()`/`RestoreState`/`ResetWorld` — route every `_consumed` change through `Consume`.
   Vegetation, herds and flocks all bake through `TemplateBatch` (arrays + precomputed rotation
   basis, ~25 ns/vertex); vegetation ground tint precomputes one colour per life cell and refreshes
   at `tintInterval` (0.5 s), and both tint and mesh intervals double for islands beyond
   `detailDistance` (45 u). `WorldHud` only reformats text when a displayed value changes. Still
   open: GPU instancing for animals/birds needs `multi_compile_instancing` in `VertexColor.shader`.
   **Phase 0 baseline (2026-09-20, `Docs/PERF_BASELINE.md`).** Measured with `Drift.EditorTools.PerfBaseline`
   from `eval`: the start world costs ≈ 0.56 ms/frame of simulation on desktop (herds 0.15, plates 0.13,
   flocks 0.11, life 0.10), 16 draw calls, zero steady-state GC. A 611-area merged island averages
   0.74 ms but spikes to ≈ 6.5 ms every 0.5 s (`ApplyTint` 3.0 ms over 41.8k terrain vertices, sink
   `RebuildMesh` 2.0 ms, herd rebuild 0.7 ms for 663 animals — merges bypass the 120-animal cap) at 42
   draw calls / 153k vertices in view. Budget for a budget phone at 30 fps (assume 9× slower): ≤ 12 ms
   simulation incl. mesh rebuilds, < 100 draw calls, zero per-frame GC, per-mesh vertex caps (terrain
   45k, vegetation 60k, herds 10k, flocks 8k) and caps enforced after merges. Shaders have only a
   `UniversalForward` pass, so nothing casts shadows although both RP assets enable them.
   **Phase 1 (2026-09-20): shader animation and simulation tiers.** Animals are baked by
   `TemplateBatch.AddAnimal` with per-vertex animation data (UV0 = pitch lever/phase, UV1 = hop
   amplitude/moving) and drawn by `Shaders/Animal.shader` (VertexColor look, cloud shadows), so hop and
   grazing head movement run in the vertex shader and the herd mesh is only rebuilt when an animal
   moved, turned, changed gait or drowned — an idle herd costs nothing. `Drift.Core.LifeLod` gives every
   life system a planar camera distance (`DistanceProvider`, installed by `IslandChaseCamera`;
   `Camera.main`, then 0 = near, as fallback) and three tiers: **near** (< `detailDistance` 45 u) full
   behaviour, mesh ≤ 15 Hz; **mid** (< `simDistance`) herds step at 10 Hz and rebuild at 7.5 Hz, plants
   double tint/mesh intervals; **far** herds only drift their centres every 0.5 s (`DriftHerds`, no
   rebuild unless visible) and plants tick succession at 1/8 rate with no mesh or tint work.
   `FlockSystem` rebuilds at 15 Hz within 60 u, 5 Hz otherwise. `IslandHerdSystem.EnforceCaps` holds
   40 herds / 120 animals after `AbsorbFrom` and `Restore` (surplus taken from the most numerous
   species), so a merged 611-area island stays at ~7.8k herd vertices. Life seeds are the serialized
   `seed` only, `Capture()` returns null without a grid, `Simulate` guards non-positive steps, and
   `IslandShape.IsLand/IsShelf` (`LandBounds()`/`ShelfBounds()`) name the two land thresholds the merge
   uses. Gotcha: an Editor-only assembly (`Drift.Tests`) cannot `AddComponent` its MonoBehaviours;
   runtime test fakes live in `Drift.Tests.Fakes` (`UNITY_INCLUDE_TESTS`). Measured: start world herds
   0.073 → 0.047 ms, flocks 0.110 → 0.012 ms, stress herd rebuild 0.68 → 0.16 ms; 83/83 EditMode tests.
   Eval-benchmark quirk: the player sits exactly on the chunk (0,0) corner, so any `Island.Tick` in
   an eval loop nudges it across the border and `StreamAround` loads 16 chunks instead of 9 —
   restore positions or call `ResetWorld()`+`StreamAround(true)` afterwards.
   **Phase 2 (2026-09-20): animal behaviour.** Every animal runs an `AnimalState` machine (Graze, Look,
   Walk, Rest, Sleep, Play) inside `IslandHerdSystem.MoveHerd`: the herd's wander target still decides
   walking (stops now last a species-specific 4–40 s), and while the herd stands each member cycles
   graze → look up (alert; 30 % stretch, small turn) → graze, or lies down when the herd will stay long
   enough (`restChance`). Timings per species (`Species` table): hares graze 2–5 s, look often, rest rarely,
   travel in hop bursts of 0.8–1.5 s at 2.6× speed with 0.4–1.1 s pauses (a hare five bodies behind cuts
   its pause); sheep amble and rest 15–40 s; goats look up often; oxen graze 10–22 s and rest long. Night
   comes through `Drift.Core.LifeEnvironment.NightProvider` (installed by `DayNightCycle` in OnEnable, 0
   without one): above `sleepThreshold` 0.6 a herd never starts a wander, non-watchers go rest → sleep with
   a geometric stagger, watchers (member 0 of a herd ≥ 6, the middle one ≥ 12) stay awake; below
   `wakeThreshold` 0.45 each sleeper wakes after its own 5..`dawnStaggerMax` (60 s) with a stretch, and the
   herd waits until nobody sleeps. Play: at most one pair per herd (`playRate` × species rate, ×3 at dusk
   and dawn) chases in a loop at formation radius + 2.5 bodies for 4–8 s, then both look up and walk back.
   Startle, fire, shore and storm huddle override everything (sleepers stand up: Walk while moving, Look
   while bunched) and the herd settles again afterwards. Poses live in the mesh as three `float4` UV
   channels (`TemplateBatch.AddAnimal(…, AnimalPose)`: lever/up/phase/hop, moving/alert/sleep/t0,
   pitchFrom/pitchTo/restFrom/restTo) and `Shaders/Animal.shader` blends pitch and rest over
   `_TransitionTime` (0.8 s) after `t0` on the global `_LifeClock` (the most advanced island's accumulated
   step time, set by `IslandHerdSystem.Step`; 0 = show targets), lowers resting bodies by `_RestLower`
   (38 %) of their height, adds breathing (slower asleep) and an alert rear twitch — so a state change is
   one rebuild and an idle or sleeping herd rebuilds nothing. Tiers: near steps every frame, mid at 10 Hz
   with the accumulated dt, far only drifts centres (no wander at night); when an island leaves the far tier
   or a save is restored, `SeedStates` puts every animal in a plausible pose for the current night (asleep
   or grazing), so per-animal state is never saved and v2–v4 files load unchanged. Measured on the start
   world: herd step (all islands) 0.072 → 0.040 ms, herd mesh rebuilds in 10 s 977 → 182 (near islands 264
   → 90) because herds now stand more than they walk; 99/99 EditMode tests.
   **Phase 3 (2026-09-20): life cycles.** The old herd growth ("+1 adult every `growthInterval` with
   `growthChance`") is now a birth: `IslandHerdSystem.TryBirth` fires on the same 45 s timer, but only by day
   (`NightAmount` < `wakeThreshold`), on a calm herd (not startled, fleeing, climbing a shore or huddling), on
   mature ground (`StageAt` > 0.95, the old condition), with at least two members, below the species `maxSize`
   and the island `maxAnimals`, and never while a young under `youngIndependence` (0.7) is in the herd. New
   herds still only come with new land (`_peakArea`); births are the growth path inside a herd, so a sinking
   island never refills. A young (`Animal.growth` 0..1, `age`, `parent`) starts at `youngScale` 0.45 of the adult
   and grows linearly over the species' `grow` time in herd seconds (hare 240, sheep and goat 480, ox 720); it
   inherits the parent's colour variant, sits `YoungOffset` (0.5–0.75 bodies) beside the parent's formation slot
   (`Slot`: the slot, not the parent's position, so a playing parent does not tow it), grazes in 0.6× bouts and
   is picked first for play pairs (`StartPlay`; `youngPlayRate` ×3 while an awake young is in the herd). At
   growth 1 `Promote` gives it a formation slot; `Orphans`/`Adopt` re-parent a young whose parent drowned or was
   trimmed by `EnforceCaps`. Size and a paler tint (`youngTint`, `youngTintAmount` × (1 − growth)) are baked by
   `TemplateBatch.AddAnimal(…, tint, amount)` — positions, hop amplitude and the lever/up channels scale with the
   animal, so `Animal.shader` is unchanged — and `bakedGrowth` limits growth rebuilds to every `growthRebuildStep`
   (4 %: a hare every ~10 s, an ox every ~30 s), also while asleep. `HerdSaveData` gained `growth`, `age` and
   `parent` arrays; JsonUtility reads missing ones as empty, which `Restore` treats as all adults (age = grow
   time, parent −1), so v2–v5 files load unchanged. Offline: `IslandLifeSystem.Simulate` ends with
   `IslandHerdSystem.CatchUp(lifeSeconds / timeScale)`, which ages young (and promotes them, one rebuild) but
   bears nothing — the 1800 life-second cap is 300 herd seconds, enough for a hare to grow up. Caps count young
   as animals (a radius-8 test island fills 120 on the spot and therefore never breeds — the cap working).
   **Phase 4 (2026-09-20): new creatures.** `Life/IslandCrittersSystem` (per island, added next to the herd
   system by `WorldStreamer.Spawn`/`VolcanoSpawner.Create`) owns four small species in one "Critters" mesh drawn
   with `Shaders/Critter.shader`: UV0 = (wing-tip offset in world xz, phase, glow), UV1 = (bob) baked by
   `TemplateBatch.AddCritter`, so wings fold up about the body axis (visible from above), everything bobs and
   glow vertices are unlit with a slow blink; cull is off because wings, legs and claws are single triangles
   (`ShapeBuilder.Fin`). Residents come from the island seed (`Repopulate`, no-op per `Version`) and persist in
   `LifeSaveData.critters` (`CritterSaveData`: kind, variant, state, x/z/yaw/scale/timer/target; files without
   the block repopulate): **crabs** (24 verts, red/orange, `crabScale` 0.06) live on the shore band `shoreMin`
   0.02–`shoreMax` 0.25, one per `crabShoreSpacing` 4 u of estimated shoreline (2·√(π·area), a disc), ≤ `maxCrabs`
   12, and cycle Idle 1–4 s → Move (a target along the local shore tangent, `TryShoreTarget`, at 0.35 u/s with
   the body across the direction of travel) or Dig 2–4 s (drawn lowered); above `diveSpeed` 1.5 u/s of
   `IIslandSurface.Speed` (new interface member: `Island` returns the self-propelled `_selfVel`, not the plate
   carry) each crab dives after its own 0–0.6 s reaction and stays Hidden (not drawn) until 3–6 s after the island
   slowed. **Turtles** (24 verts, dark green, 0.09) only on islands above `turtleMinArea` 20 (1 + one per 40
   area, ≤ 3): Rest 20–60 s, then by day (`NightAmount` < `nightThreshold`) crawl at 0.08 u/s from the shore band
   up to `turtleInland` 0.55 or back down. Shape changes (`Version`) relocate residents whose ground left their
   band within 3 u or drop them; new land past the peak area adds residents; `IslandLifeSystem.ShiftLocal`/
   `AbsorbFrom` forward to the critters so `Island.MergeFrom` needs no change, and `AbsorbFrom` re-applies the
   caps. Transients are never saved and exist only in the **near** tier: **butterflies** (6 verts, two wing
   triangles, 0.07) while `NightAmount` < `dayThreshold` 0.5, one per `butterflyFlowers` 3 living flowers up to
   16, spawn on a flower (`IslandLifeSystem.TryRandomPlant`, reservoir sampling without allocation), sit 1–4 s and
   fly to another flower within `butterflyRange` 3 u at 0.5 u/s, `butterflyHeight` 0.12 above ground plus a
   baked wobble; **fireflies** (6-vert diamond, glow 1, 0.03) while `NightAmount` > 0.6, one per `areaPerFirefly`
   8 up to 24, spawn by preference on meadow-to-wood cells (stage 0.3–0.95, otherwise any stage ≥ 0.3, because an
   old-growth island would have none) and wander 0.25 u/s within 1.5 u at 0.35 height. Both fade in/out over
   `fadeTime` 1.5 s (baked scale), at most `spawnsPerInterval` 2 per `spawnInterval` 1.5 s; the mid tier steps
   residents every 0.2 s and freezes transients, the far tier drops transients and steps nothing. The mesh
   (≤ ~900 verts worst case) rebuilds at `meshInterval` 1/12 s only when something moved or faded. `FlockSystem`
   gained `seabirdFlocks` 2 flocks of 2–3 `LifeKind.Seabird` (48 verts instead of the 180-vert bird: white/grey
   body, long flat wings, no flap alpha, a slow soar and bank) that prefer cliff islands (`IsCliff`: Barren,
   Volcanic or `MaxHeight` > `cliffHeight` 1.7, cached per island Version), circle them at BoundingRadius + 1.5
   for 20–40 s at peak + `seabirdAltitude` 2.5, keep a lone cliff island as their target, never descend or perch,
   and fall back to any island when no cliff is in range. Measured on the start world: critters step over all
   32 islands 0.019 ms (137 crabs, 22 turtles, 6 butterflies by day / 17 fireflies at night, 3.9k verts in
   total), per near island 0.003–0.005 ms step and 0.007–0.014 ms rebuild, ≈ 20 B/call at the profiler's noise
   floor (transient spawns), flocks 0.033 ms with 47 birds; 123/123 EditMode tests (`Tests/LifePhase4Tests.cs`).
   Still open: the scene's player `/Island` needs the component added by hand (streamed islands get it in code),
   and `IslandSaveUtil.ApplySaved`/`Island.NotifyShapeGenerated` do not call `IslandCrittersSystem.Repopulate`
   (its own `OnEnable` and the `Version` check in `Step` cover both paths).
   **Fireflies on every island (2026-09-20, owner: „auf allen Inseln nachts Glühwürmchen, die die Insel leicht
   erleuchten").** The paragraph above is superseded for fireflies: they no longer live in the "Critters" mesh
   and are no longer near-tier only. Per island and shape `Version` (at most every 2 s, lazily on the first
   visible night) `BuildHomes` seeds **homes** from its own random stream: `DesiredFireflies` = one per
   `areaPerFirefly` 2.5 of land, at least `minFireflies` 12 (a rock of a few square units: 1.5 per area), at most
   `maxFireflies` 48, scattered within `fireflyClusterRadius` 1.7 round 1–`maxGlowBlobs` 8 **cluster centres**
   (one per `areaPerGlowBlob` 14; meadow-to-wood ground first, then any grown ground, then any dry ground, so
   rock, lava and sandbanks twinkle too). `FireflyAmount` = dusk ramp `fireflyDusk` 0.35 → `nightThreshold` 0.6
   of `NightAmount` × (1 − `fireflyStormCut` 0.7 × the island's `StormIntensity`); home i is out while its rank
   0.9·i/n is below the amount, so the swarm grows at dusk, thins in a storm and goes at dawn one firefly at a
   time. **Near tier:** one wandering `LifeKind.Firefly` critter per home that is out (tappable as before;
   targets within `fireflyRange` 1.5 of its home, short pauses, fades over `fadeTime`); coming in from the mid
   tier they start full bright at their homes. **Mid tier:** no firefly objects at all - the homes are baked
   once as halos that drift (`fireflyDrift` 0.45), bob and blink in the vertex shader. **Far tier:** the first
   `farFireflies` 8 homes and two ground blobs; beyond `hideDistance` nothing (homes are not even computed).
   **Rendering:** a second child "FireflyGlow" (one draw call per visible island, only at night) with
   `IslandCrittersSystem.SharedGlowMaterial` = `Drift/Critter` with `_GlowMode` 1, `Blend One One`, no depth
   write, depth offset −1, queue Transparent+20 (after water, seams, fish). `GlowBatch` (same file) writes two
   shapes: **halos** - four vertices at the firefly's position, expanded camera-facing in the vertex shader after
   the curve bend, radius `fireflyHalo` 0.11 u but never below `fireflyMinAngle` 0.0065 (tan of the half angle ≈ 4
   px radius at 720p; an enlarged halo is dimmed to 70 %), hovering at `fireflyHeight` 0.35 + `fireflyHeightSpread`
   1.1 × f² (f fixed per home: most stay low, some rise to the treetops, or a closed canopy would hide the swarm
   from above), a radial falloff plus a hot core (`_HaloStrength`,
   `_CoreStrength`, `_CoreRadius`), blink between `_GlowMin` and 1 - emissive, unlit, no cloud shadow, only the
   horizon haze dims it; and **ground glow blobs** - a 25-vertex disc (`glowBlobRadius` 1.9) under every cluster,
   every vertex sampled onto the terrain + `glowBlobLift` 0.05, falloff in the colour alpha (squared in the
   shader), `groundGlowColor` a faint warm green that pulses slowly. The amount reaches the GPU as one float per
   renderer (`_FireflyAmount`, `MaterialPropertyBlock`, only when it moved by 0.01). ≤ 48·4 + 8·25 = 392 glow
   verts per island; the near tier re-bakes only the halos at `meshInterval` while fireflies move (the blobs stay in
   the batch: `GlowBatch.Mark`/`Rewind`; no allocation), the "Critters" mesh no longer rebuilds for them. The
   firefly state is `[NonSerialized]`: the Editor keeps plain private fields across a domain reload but not the
   critter objects, which once left homes whose slots pointed nowhere. Measured on the start world (45 islands):
   step 0.016 ms by day, 0.023 ms at night (63 wandering fireflies on 4 near islands, 399 baked halos), 0 B. `FirefliesShown` = what a viewer sees in any
   tier (audio). 5 EditMode tests in `Tests/FireflyTests.cs`.
   **Phase 5 (2026-09-20): flora and ecosystem processes.** Two plant kinds, `LifeKind.Palm` (leaning trunk plus six
   flat fronds, 30 verts) and `LifeKind.Reed` (four stalks and a seed head, 45 verts), map to plant slots 4 and 5
   (`LifeMeshes.PlantSlot`; the per-cell counters are `PlantKinds` 6 wide). A **shore cell** is a land cell with
   water in its 8-neighbourhood (`IslandLifeSystem.ComputeShore`, `IsShoreCell`); islands are plateaus with steep
   rims, so the 0.12–0.35 height band alone held 3–6 cells per island. Below every third shore cell (cell hash) a
   palm stands where a downhill walk from the cell centre (`TryDownhill`, ≤ 1.5 cells, sideways scatter) first
   meets ground between `palmMinHeight` 0.05 and `palmMaxHeight` 0.35, below every second one a reed cluster at the
   waterline (≤ `reedMaxHeight` 0.12, drawn at y ≥ 0); `maxPalms` 24 / `maxReeds` 40 only limit spawns, a merge
   fades the guest's surplus, and both otherwise die only by fire or drowning. **Wind sway** lives in
   `Shaders/VertexColor.shader`: `TemplateBatch.AddPlant` bakes UV0 = (template height weight × per-kind factor,
   phase, vertex height in world units) and the vertex shader bends by weight² × height × `_WindBend` (0.45) along
   the global `_LifeWind` — xy = `Drift.Core.LifeEnvironment.Wind` × (1 + 1.5 × `LifeEnvironment.Storm`), z = storm;
   `WaterFeedback` installs `WindProvider`/`StormProvider` in `OnEnable` (fallback breeze (0.6, 0.2), no storm) and
   `IslandLifeSystem.PushWind` sets the global once per frame from whichever island steps first — with a slow gust
   (0.6 rad/s over position) and a fast flutter (3.2 + 3 × storm rad/s, amplitude `_WindFlutter` 0.18 + 0.5 × storm)
   plus a small dip of the tip. The legacy alpha sway is 0 on the vegetation material; a mesh without UV0 (flocks,
   plate borders, the bolt) reads zero and stays rigid, herds use `Drift/Animal`. `TemplateBatch` now uploads only
   the UV channels a batch wrote (plants 1, animals/critters 3), and the sway/colour work happens in the single
   `AddTransformed` pass. **Flowers**: `LifeMeshes.FlowerVariants` = 6 colours × 3 shapes (single, cluster of three,
   tall with a bud; ≤ 60 verts), variant = shape × 6 + colour from two slices of the cell hash, so a cell regrows
   the same colour; `TryRandomPlant(LifeKind.Flower, …)` is unchanged for the butterflies. **Bloom**: every cell has a
   noise phase (`BloomPhase`, Perlin at 0.09/u so the wave travels); the factor 0.5 + 0.5 sin(2π age / (`bloomPeriod`
   240 s × timeScale) + phase) is evaluated every `bloomInterval` 1 s (2 s mid tier, and at the end of `Simulate`),
   stored per cell (`_bloomBaked`, `BloomAt`) and the mesh is only marked dirty when a cell holding flowers moved
   more than `bloomRebuildStep` 0.08 — the rebuild bakes flower scale 0.5–1.2 and brightness 0.8–1.15. **Fire
   scars**: `_burn` fades at `burnFadeRate` 0.00125 per life-second (0.9 → 0 in 720 life-s = 2 real minutes,
   was 0.025/s), `CellColor` lerps to charcoal (0.16, 0.13, 0.11) with alpha → the terrain shader's ash; grass that
   spawns on a cell with burn > 0.25 is flagged `shoot` and drawn at 0.6× and towards (0.8, 1.3, 0.65) by the cell's
   baked burn, so the shoots normalise as the scar fades; `IgniteAt`, `BurnAt`, `ShootCount` are the test/debug
   hooks. **Tree growth**: trees and palms spawned after the initial population start with `maturity` 0 (drawn at
   `saplingScale` 0.3 × target), advance by ldt / `treeGrowTime` 240 life-seconds in `SyncPlants`, and re-bake every
   `growthRebuildStep` 5 % (`bakedMaturity`, no per-frame lerp); old growth (stage > 1.0) adds up to 8 % canopy.
   **Seasons**: `_season` 0..1 advances by ldt / (`seasonPeriod` 1200 s × timeScale) in `Tick`, so `Simulate` and the
   offline catch-up move it; four key multipliers (spring fresh, summer deep, autumn olive/orange for trees and golden
   grass, late autumn) are smoothstepped (`SeasonColour`, every channel within 0.76–1.22 at `seasonAmplitude` 1) and
   applied in `CellColor` (ground, every tint interval) and at bake time to trees, bushes, grass and half to reeds;
   the mesh re-bakes when the season moved `seasonRebuildStep` 0.02 (once per 24 s). It is saved as
   `LifeSaveData.season` (old files read 0) and `Repopulate` copies the static `SeasonReference` (the last ticked
   island), so streamed neighbours share the player's season. **Vertex budget**: `maxVegetationVerts` 60000 with
   `fullCellVerts` 190 per old-growth cell gives a density factor; below 1 the grass/bush/tree counts round
   stochastically per cell (`Thin`, even thinning instead of bare rows), `Spawn` refuses anything past the budget,
   and `AbsorbFrom` fades the guest's grass/flowers, then bushes/reeds, then trees until the estimate fits. Measured
   on the start world: 109 palms, 134 reeds, 156 flowers, 78.9k resident vegetation verts (was 73.2k), life step over
   32 islands 0.118 ms (baseline 0.104), player rebuild 0.068 ms for 2.5k verts and an 84-area island 0.209 ms for
   7.8k (≈ 27 ns/vertex, was 17–25: the UV0 channel and colour scale), `ApplyTint` unchanged at 0.049 ms,
   `EvaluateBloom` 0.001 ms. The 611-area stress island (414 land cells, density factor 0.76) holds 1,098 plants at
   **53.4k verts** (was 58.7k) with palms and reeds at their caps, rebuild 1.01 ms (was 0.97), `ApplyTint` 3.0 ms
   (unchanged, still the open spike); 133/133 EditMode tests (`Tests/LifePhase5Tests.cs`).
   **Phase 6 (2026-09-20): life sounds.** `Audio/LifeSynth` (see "Audio") is the third `ISynthSource` on its own
   runtime "Life" AudioSource, so it shares the `OnAudioFilterRead` path and costs the audio thread, not the frame:
   measured with a Stopwatch over 5 s of 48 kHz stereo in the Editor, idle 0.36 ms per second of audio (0.04 % of a
   core), a full day mix 3.4 ms (0.34 %), a full night mix 5.0 ms (0.50 %), everything at once 5.7 ms (0.57 %; budget
   0.6 % ≈ 0.5 ms/frame on a phone) against `MusicSynth` 8.2 ms (0.83 %) and `SfxSynth` 2.8 ms (0.28 %). The
   main-thread scout runs every 0.5 s over the ≤ 32 loaded islands (only those within 40 u are read: a
   `GetHudStats` grid walk, three `CountOf` plant loops and a herd loop each) and allocates nothing; the densities
   are plain float writes. Analysis over 6 s: crickets alone put 100 % of their energy in 3–6 kHz, birds 67 % in 2–6
   kHz with 19 chirp onsets, rustle 71 % in 0.6–3 kHz, lapping 92 % below 2 kHz, zero density is exactly silent, and
   no mix exceeds a peak of 0.17 before the director's `lifeVolume`. 145/145 EditMode tests
   (`Tests/AudioPhase6Tests.cs`, 12 new).
   Measured on the start world after `RestoreWorld()`: herd step (all islands) 0.08–0.14 ms with no young,
   0.20 ms with 94 young and 1100 instead of 899 animals; player rebuild 0.035 → 0.046 ms for 1260 → 1740
   vertices (26 ns/vertex either way); rebuilds in 10 s 530–880 without and 850–990 with young, i.e. inside the
   spread between runs (the count depends on how many herds happen to be walking). 109/109 EditMode tests
   (`Tests/LifePhase3Tests.cs`).
   **Animal detail LOD and behaviours (2026-09-20, owner: "beim Ranzoomen detaillierter", "interessante Dinge tun").**
   `Life/AnimalModels.cs` builds a detail template per species, colour variant and age (`LifeMeshes.GetDetailTemplate`;
   hare 252, sheep 279, goat 258, ox 303 verts against 60–78 for the simple boxes; young = the same with the head
   scaled 1.32 about the neck): four legs, ears, tail, snout, eyes, woolly lumps and a dark face for sheep, horns and
   beard for goats, horns, hump and a light muzzle for oxen, long ears, white tail and hind feet for hares, built from
   the new additive `ShapeBuilder.Tube` (n-sided tapered tube), `Lump` (8-face gem), `Patch` and `VertexAt/SetVertex`.
   They drive the *unchanged* `Drift/Animal` channels with per-vertex values (`PlantTemplate.lever/up/leg`) instead
   of plain z / y: feet have lever 0 and up 0 (planted), the body's `up` is legLength / `_RestLower` (0.38,
   `AnimalModels.RestLower` must match the material) + a little squash, so resting lowers the body by exactly the leg
   length and the legs fold away; the head shares one lever (capped at 0.9) scaled so the snout just reaches the ground
   at the deepest graze swing, and its `up` puts it on the ground when asleep; neck vertices blend body → head. The one
   shader addition is a walking-leg lift: `TemplateBatch.AddAnimal` writes a signed world-space amplitude into the
   colour alpha of leg vertices (diagonal pairs ±, 0 on simple templates and hares), and the vertex shader lifts them
   alternately in step with the walk bob. **Selection** is per animal in `IslandHerdSystem.UpdateDetail`:
   `LifeEnvironment.ViewDistance` (= the planar `LifeLod.Distance` unless a `ViewDistanceProvider` with the true 3D
   camera distance is installed) below `detailNear` 9 u makes an animal detailed, above `detailFar` 12 u simple again;
   at most `maxDetailed` 40 and `maxDetailVertices` 10.4k per island mesh, nearest first, a detailed animal keeping its
   place against a newcomer less than 3 u closer; evaluated every `detailCheckInterval` 0.25 s (one comparison for an
   island out of range) and the mesh is only marked dirty when the set changed, so a moving camera costs at most 4
   rebuilds/s and an unchanged set none. Measured on a 120-animal test island: 39 detailed, 15.4k verts, rebuild
   0.38 ms (simple only: 7.4k verts, 0.18–0.25 ms; 24 ns/vertex either way), step 0.020 ms avg, 0 B; a camera
   sweeping across the sleeping island in 10 s changed the set 19 times. Budget note: the herd mesh of the one or two
   islands within 12 u of the camera may now reach ~16k vertices (was ≤ 10k); `maxDetailed` is the knob for weak phones.
   **Behaviours** sit on top of the Phase 2 state machine without new `AnimalState` values (WatchTools indexes moods
   by it): `AnimalActivity` (Drink, Wade, Wallow, Lookout, Spar, Race, Binky, Dig, Burrowed, Line, Shade, Watch, plus
   the derived Huddle and Flee), `ActivityOf`, `MoodOf`/`MoodText` (German), `IsHidden`. A member with a *goal* walks
   there instead of to its formation slot (`CanStepMember` relaxes the ground rule for shore visits); a herd *errand*
   (`Think`, once per `thinkInterval` 1 s for a calm herd by day; every rate × `behaviourRate`, 0 = off) first walks
   the herd centre to a spot that is still valid ground — so the rising-shore rule never fires — and then hands out
   goals. **All:** at dawn (night falling through `wakeThreshold` after a night) 70 % of the herds look for the nearest
   shore in eight directions (`TryFindShore`, ≤ `shoreSearch` 7 u, the waterline found in 2 cm steps) and drink side by
   side on the wet sand (height 0.045–0.13, head-down pitch 13° alternating with a look-up) for 12–20 s; with a
   `LifeEnvironment.PointOfInterest` within BoundingRadius + `watchRange` 15 u (polled every 2 s) a standing herd lines
   up across the direction and watches for 8–14 s (cooldown 40–60 s). **Sheep** walk in single file behind member 0
   (`lineSpacing` 0.9 bodies; each joins once the line has passed its place, young beside their parent) and gather
   tightly (gather 0.55) under a tree within `shadeRange` 6 u (`IslandLifeSystem.TryRandomPlant`, no new interface
   needed) while it rains (`Agitation` between `rainThreshold` 0.1 and the huddle threshold) or at noon
   (`LifeEnvironment.TimeOfDay` within ±`noonWindow` 0.07 of 0.5; −1 without a provider). **Goats** post one lookout
   per standing herd on the highest ground a half-unit hill climb finds within `lookoutRange` 5 u (≥ `lookoutMinRise`
   0.12 above the herd), alert, turning now and then, until the herd moves on; their play is head-butt sparring (back
   off 1.5 bodies, charge head-down to 0.5, 3–5 rounds). **Oxen** wade at noon (25 %/s, rarely otherwise): adults stand
   in the band −0.045…0.02 and drink for 25–45 s, young stay ashore; the `shore` flag exempts them from drowning down
   to `WadeFloor` −0.06 and lets them walk back over low ground; a resting ox wallows (40 % of rests; 3.5–5.5 s roll of
   ±75°, baked as a roll about the lying body's axis, rebuilt only while the animal is detailed). **Hares** dig
   burrows (a 42-vert mound with a dark entrance in the herd mesh, ≤ `maxBurrows` 6 per island, dug when none lies
   within 0.7 × `burrowReach` 5 u, celebrated with a binky), dive into the nearest one when startled or in a storm
   huddle (hidden = not drawn, not tappable via `IsHidden`) and pop out 4–10 s after the danger with a stretch; binkies
   (three big twisting hops) happen in the near tier only. **Young** race as a pack (≥ 2 awake young, up to four,
   chasing each other round the herd). Startle, fire, shore, storm and night cancel every errand; far-tier drifting and
   `SeedStates` reset the layer. Burrows are saved without a schema change as x/z pairs behind the first herd's
   `HerdSaveData.m` block (every reader takes min(variant.Length, m.Length / 6) members, so old and new files load
   both ways). `AddHerd(kind, centre, size)` / `ClearHerds()` are public for tests and settlements. Core hooks for the
   coordinator: `LifeEnvironment.TimeOfDayProvider`, `PointOfInterest`, `ViewDistanceProvider`.
   `Tests/AnimalDetailTests.cs` (17 tests); the three older vertex assertions moved from 10k to 17k.

## Biomes and collectible species

(2026-09-20, owner: "Vegetation und Tiere sollen sich auf den Inseln unterscheiden
– so kann man neue Arten sammeln", "mindestens 3 weitere Bewegungsfolgen".) Every island has a `Drift.Core.LifeBiome`
(`IIslandSurface.Biome`; `Island.BiomeForSeed(shapeSeed)`, the player's start island is Temperate) and `Life/BiomeSpec.cs`
(`Biomes.Of(biome)`) says what it brings forth. The succession grid is unchanged: a biome only decides **which species
fills each succession role** (ground cover / flower / shrub / tree / shore palm / reed = the six per-cell counters),
how dense the roles are (`groundDensity`, `shrubDensity`, `treeDensity`, `canopyClears`, `flowerEvery`,
`flowerMaxStage`; non-1 densities round stochastically per cell like the vertex-budget thinning), and the ground
palette (`BiomeSpec.GroundColour(stage)`; `Biomes.Ground(r,g,b)` turns the colour the ground should *show* into the
linear multiplier on the terrain's `_Grass` that `ApplyGroundTint` carries — the terrain multiplies in linear space,
so pale or ochre ground needs factors of 4–15, which the island mesh's Float32 vertex colours hold). Temperate: grass,
meadow flowers, bushes, trees, shore palms, reeds; hare, sheep, goat, ox. Tropical: fern, hibiscus, banana plant,
palm inland (a `Palm` with `Plant.slot` = tree) + jungle tree + bamboo; capybara, flamingo, giant tortoise. Nordic:
reindeer lichen + fly agaric, heather, juniper, snow-capped spruce + birch, no shore palms, snow above 1.2 u and in
noise drifts over 30 % of the ground; penguin, reindeer, arctic fox. Savanna: dry grass that stays under the trees,
aloe, thorn bush + termite mound, sparse umbrella acacias + baobabs on ochre ground; meerkat, zebra, giraffe. Trees
are picked by a coarse noise (stands form), the rest by the cell hash. `LifeKind` grew from 16 to 42 values, appended
only (herd animals 16–24, plants 25–41; saves store the number); `Plant.slot` replaced `PlantSlot(kind)` as the counter
index, `LifeMeshes.PlantSlot` now answers the role of any kind, and `CountOf` / `TryRandomPlant` /
`NearestPlantDistance` treat the four temperate kinds as *roles* (`CountOf(Tree)` = everything that grows as a tree),
so butterflies, shade-seeking sheep, browsing giraffes and the sound scout work on every island; `CountOfKind` is
exact. Plant templates live in `Life/PlantModels.cs` (18–72 verts, baobab 120), herd species carry `biome`,
`coastal` (targets next to shore cells, `IslandLifeSystem.NearShore`, margin 0.06 instead of `shoreMargin`),
`grazePitch` and `standsToRest`; `PickSpecies` only offers the biome of the ground a herd is founded on.

**The biome belongs to the land, not to the island** (2026-09-20, owner: "Die Insel-Biome sollen nach der
Kollision erhalten bleiben"). `IslandLifeSystem` carries a `byte[] _cellBiome` next to `_stage`/`_burn`/`_land`,
same indexing. A fresh grid writes `IIslandSurface.Biome` into every land cell; a re-grid carries each cell's
biome over exactly like its stage, a merge's imported guest cells bring the **guest's** biome with them
(`Import.biome`, only into cells the host had no land on), and land that appears with no predecessor (a new rim,
a land bridge, refloated shore) takes the biome of the nearest cell that has one (`FillUnknownBiomes`, two
sweeps per round), so borders stay where they were instead of snapping back to the host. Everything that used
to ask the island now asks the cell: `DesiredCounts`, `TryKindFor`, the palm/reed/shore rules, `CellColor` with
its season amplitude and its snow line, and `RebuildVegetationMesh`'s per-plant season tint. The tint blends
across a biome border — a cell with a neighbour of another biome averages its colour with its four neighbours'
(own weight 2), inside the existing `tintInterval` and allocation-free, and `TintAt`'s bilinear filter spreads
the rest, so a one-biome island is bit for bit what it was. The island-level API answers for the *dominant*
biome (`DominantBiome` = the biome with the most land cells; `Biome`, `BiomeName`, `BiomeSpec` follow it) and
the rest is readable through `BiomesPresent` (bitmask), `CellsOfBiome(LifeBiome)`, `BiomeAt(Vector2)` (one byte
array read; a beach point whose cell centre is already water answers from the land next to it) and
`BiomeAtCell(i, j)`. `IslandHerdSystem` mirrors `Biome`/`BiomesPresent`/`BiomeAt`; on an island with more than
one biome `Rebalance` draws the spot **first** and then picks the species from that spot's biome (`TryFound`,
a species that keeps landing on ground it cannot use drops out after 6 misses, as `Excluded` did before), so a
merged island goes on producing reindeer on its nordic part and zebras on its savanna part. A one-biome island
still takes the old path (`PickSpecies` then `TryRandomPos`) and therefore the same draws from `_rnd`, i.e. the
same herds it always had. "Foreign" now means *native to none of the biomes this island carries*
(`NativeHere`, `IsForeign`, `BiomesPresent`) — a species whose ground came along is simply at home, and the
foreign machinery below is what is left for species whose biome is not (or no longer) on the island.
`LifeSaveData.biomeRuns` stores the grid run-length encoded (`count << 4 | biome`, water cells carry the
previous land cell so a rim of sea does not cut every run in two): a one-biome island is one int, a
three-biome 1089-cell island 34 ints (~138 characters of JSON against ~2.2 kB for a raw `byte[]`, 1/16 of the
size and 0.3 % of the whole file). Files from before have no block and load with every cell at the island's
own seed biome, exactly the world they were saved in. Volcanic and streamed islands are untouched.

**Collecting = merging.** `AbsorbFrom` carries the guest's plants and herds over as before, and then: *nothing
foreign ever spawns from nothing* (`Rebalance` and the succession only produce native species), a foreign herd
breeds true (births keep `herd.spec`) inside the unchanged caps — `SurplusHerd` now spares a species that is down to
its last herd while another still has several, so a merge never costs a collected species —, and foreign plants (1)
are exempt from the canopy quota (only natives make way), (2) claim a free place of their role in their own or a
neighbouring cell with `foreignReseedChance` 0.5 (`TryKindFor`, a byte per cell and role, only filled while the
island holds foreign plants), (3) seed one suitable neighbour cell every `foreignSpreadInterval` 60 life-seconds
(`SpreadForeign`: a cell whose stage wants that role; into a gap, or replacing a native plant while the species
counts fewer than `foreignStand` 12), and (4) keep their last `foreignStand` plants when the merge's vertex budget
fades the guest's surplus. They still die by fire or drowning, so a species can be lost again. Native plants are
regrown from the stage grid on load as always; foreign ones are saved in `LifeSaveData.foreign` (two ints each:
kind | variant << 8 | slot << 16 and the position in 2 cm steps from the grid origin, at most `maxSavedForeign` 600,
trees first) and put back *before* `SyncPlants` so they hold their places. Files without the block and herd kinds a
build does not know load as before.

**Animals and choreographies.** `Life/AnimalModels.Biomes.cs` (partial class) adds a simple box template (60–126
verts) and a detail rig (228–363 verts, young = head × 1.32, young reindeer without antlers) per new species on the
unchanged lever/up/leg channels, plus *special poses* baked as templates of their own because the shader only knows
pitch and rest (`AnimalModels.PoseSpecial`, chosen per animal in `IslandHerdSystem.PoseOf`): flamingo on one leg
with the head tucked (its Rest/Sleep — `standsToRest` keeps `restTo` 0), tortoise drawn into its shell, meerkat
upright (every look-around and the sentry), penguin flat on its belly (`Rig.bakePitch` + `headPitch`). Simple
templates tip the box over with the new baked `pitch` of `TemplateBatch.AddAnimal`. `AnimalActivity` gained Slide,
Parade, OneLeg, Browse, Stampede, Soak, Tuck, Circle, Sentry, Pounce, HopChain, Shake (appended; German `MoodText`
for each; `ActivityStep(herd, member)` reports the step, `StartChoreography(herd, activity)` stages one for tests and
screenshots). They run on the existing errand/goal layer, so startle, fire, rising shore, storm huddle and night
cancel them like every errand, the far tier resets them, and mid/far tiers need no extra work: **penguins**
(`Errand.Slide`) queue on the bank, flop, glide down their lane at 4.5 × speed, dive (not drawn, not tappable),
surface, shake; **flamingos** (`Errand.Parade`, herd-level `ParadeStep`) gather in a row in the shallows, turn their
heads together, march along the water line and back in step (one shared phase), then stand on one leg; **giraffes**
(`Errand.Browse`) ring an acacia, stretch the neck (pitch −24°), chew (−24° ↔ −17°), look about and start over;
**zebras and reindeer** stampede along a circle that is checked to lie on their own ground (3.2 × speed, 3 × hop,
tight formation) and settle with every head up, reindeer also walk in single file and spar like goats;
**capybaras** (`Errand.Soak`) lie in the wade band with only head and back out and shake themselves dry once they
are back up; **tortoises** answer a startle by tucking in instead of fleeing and peek out before they go on;
**oxen** with a young close a ring around it from `duskThreshold` 0.3 night on, facing outwards, and sleep like that
(the one errand that lasts through the night); **arctic foxes** stalk, leap in an arc and nose-dive into the ground;
**sheep** jump at the same spot of the path one after the other while they walk in line. Journal hooks, all without
allocation: `IslandHerdSystem.SpeciesPresent` / `IslandLifeSystem.PlantsPresent` (ulong, bit = `(int)LifeKind`),
`HasSpecies`, `AnimalsOf`, `HerdsOf`, `HasPlant`, `CountOfKind`, `IsForeign`, `Biome`, `BiomeName`,
`LifeNames.Of/Plural/OfBiome`, `Biomes.Collectibles/Animals/Plants(biome)`, `Biomes.TryHomeOf`. The collection album
that reads them (`DiscoveryJournal`, `CollectionCatalog`, `JournalPanel`, popup names) is described under "Watch
tools". Measured on
temporary real islands from `eval` (desktop): area 95, camera 5 u, 40 detailed — herd step 0.089–0.110 ms in all
four biomes (temperate 0.105), life step 0.008–0.011 ms; area 600 at 30 u — 120 animals in 7.6–9.1k herd vertices,
herd step 0.068–0.079 ms, vegetation 52–60k vertices, life step 0.040–0.063 ms; 0 B/frame everywhere.
`Tests/BiomeTests.cs` (26 tests). The per-cell biome costs nothing measurable: on a 740-land-cell island
(desktop, `eval`) `Tick` is 0.112 ms with one biome and 0.104 ms with three, the vegetation rebuild 1.40 vs
1.44 ms, `ApplyTint` 0.032 vs 0.067 ms (the border blend, twice per second), 0 B/frame in both.

## Project structure

Assembly-per-system under `Assets/_Drift/Scripts/`: `Drift.Core` (generation + shared
interfaces such as `IIslandSurface`, no Unity-specific deps beyond UnityEngine), `Drift.World`
(sphere planet, parked), `Drift.Tectonics` (plates; knows only `IPlateRider`), `Drift.Life`
(procedural flora/fauna; knows only `IIslandSurface`), `Drift.Islands` (islands, collisions, camera;
references Core/Tectonics/Life), `Drift.Editor` (editor-only tools), `Drift.SaveSystem` (save +
`GameSession`), `Drift.Audio` (procedural synth), `Drift.Visuals` (water feedback, impact, day/night),
`Drift.UI` (touch controls, sprites; plus an unwired TMP HUD framework from another chat),
`Drift.Bridge` (uGUI HUD + session screens, references everything) and `Drift.Tests`. Cross-system links go through interfaces so no assembly
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
- **M4** — (full) `SpeciesDef`s (superseded by the succession grid + herds; predators dropped by decision) (flora + 1 herbivore), real-time-only ecosystem tick, no save yet.
- **M5** — save/load + offline catch-up. *First pass done* (stepped fast-forward instead of the
  closed-form solution; save v2 now persists dirty AI islands and volcanoes).
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

## Session, screens, touch (`Drift.SaveSystem.GameSession`, `Drift.Bridge.SessionScreens`, `Drift.UI.TouchControls`)

`GameSession` is a state machine Title → Playing → Paused → GameOver over a pure-C# `SessionModel`
(transitions, restart countdown, `SessionStats`: time survived, islands/volcanoes absorbed via
`Island.Merged`, peak land mass). Outside Playing (and while the tutorial's `SinkingSuspended` or photo mode's `PhotoSinkHold` is set) it sets
`player.sinkEnabled = false`; outside Playing also `Island.InputLocked`, `Time.timeScale` (0 while paused) and disables autosave. `SessionScreens` draws title
(Start / Weiter if a save exists), pause (Esc, Android back, top-right button) and game-over
(stats, countdown, "Neue Insel") as one procedural uGUI canvas (sorting 10, above `WorldHud`), and
creates an EventSystem if none exists. `TouchControls` (Drift.UI) is a floating thumbstick on the
left half (x → turn, y → throttle) feeding `Island.InputProvider`, plus two-finger pinch →
`IslandChaseCamera.ZoomBy`; shown only when `Touchscreen.current != null` or `forceShowTouch`.
Screens are overlays in the one gameplay scene — the world keeps drifting behind the title.
**World seed.** `GameSession.WorldSeed` is the master seed; `WorldSeeds.Derive(master, salt)` hashes it
into `WorldStreamer`, `PlateSystem`, `FlockSystem.Reseed`, `VolcanoSpawner.Reseed` and the player's
shape/life/herd seeds. Title screen: seed field + "Zufall" (`PreviewSeed` regenerates the world live),
Start → `StartNewGame(seed)`. On the first title frame a readable save is loaded behind the title (the
preview shows the island "Weiter" continues; no second load) or a random seed is applied
(`randomSeedOnFirstTitle`). `Bridge.IslandPreview` renders the player island top-down into a 512² RT
with a throttled second Base camera (enabled only on render frames), shown on the title (4 fps) and
game over (once). Pause has "Home" (back to the title, saves; labelled "Zum Titel" before v0.6.3) and "Beenden" only off-mobile.
`IslandChaseCamera.SnapToTarget()` jumps the camera after loads/resets.
Backgrounding auto-pauses: `GameSession.OnApplicationPause(true)` (device) and `OnApplicationFocus(false)`
(Editor/desktop only) save via `SaveManager.Save()` and then `SessionModel.EnterBackground()` (= `Pause()`,
so only Playing changes); nothing auto-resumes — the pause menu is up on return. `SessionStats` persist
in the save (`SaveGame.stats`, v4); "Weiter" continues the run's stats via `SaveManager.LoadedStats` →
`SessionModel.BeginPlaying(stats)`, a new game or "Neue Insel" resets them, game over deletes the save.
`Save()` is deduplicated per frame (`Time.frameCount`) so the background save + `OnApplicationPause` /
`OnApplicationQuit` write once. v2/v3 files still load (`MinReadableVersion` 2) with zeroed stats.
**Save v5 (Phase 7)** adds `SaveGame.journal` (`JournalSaveData`: seen species ids, first-seen run times,
`youngBorn`, `firesSeen`). `SaveManager.Journal` (a pure `DiscoveryJournal`) is captured on every save,
restored on `Load` only for `version >= 5` (older files → empty journal) and reset by `DeleteSave`, so it
belongs to the run like the stats: a new game or a game over starts a fresh log. `Readable`/`Peek`/
`HasValidSave` do not look at the block. Gotcha: `JsonUtility` never yields null for a missing class field,
it builds a default instance — `DiscoveryJournal.Restore` treats null and empty the same.
**Save v6 (collection album, 2026-09-20)** keeps the block's name and adds `ids` (`CollectEntry.id`: `(int)LifeKind`
for island life, 100 + `(int)SeaKind` for the sea, 200 for the fish schools — enum numbers, so the catalog may be
reordered), `states` (1 seen, 2 collected), `seenAt`, `collectedAt`, `best` and `bestStage`; the count "right now" is
not saved, the first scan after loading brings it back. A v5 block (`species` / `firstSeen` of the old 11-entry
`JournalSpecies` table, no `ids`) is read as "seen" entries with their times (`DiscoveryJournal.LegacyIndex`), and what
of it lives on the island turns "collected" with that first scan — silently, because the first
`ReportPresence` after a `Reset`/`Restore` is the baseline (`Baselined`). `MinReadableVersion`, `Readable`/`Peek`/
`HasValidSave` and the `worldGenVersion` gate are unchanged. Across runs only the *Lebensbuch* survives:
`SaveSystem.LifeBook`, one PlayerPrefs string `drift_lifebook` (hex of a ulong, bit = `(int)LifeKind` of every kind
ever collected), loaded lazily by `WatchTools.LifeBook`, written only in Play Mode and only when a bit is new
(`WatchTools.lifeBook` switches it off); `LifeBook.Line` / `Count` / `Total` are there for a title or album line.

**UI style system (2026-09-20, `Drift.UI.UiStyle` + `UiSprites`; soft "candy" pass the same day).** Every screen is
built through one static style class, so a restyle touches one file. *Font:* `UiStyle.Font` is the only font source
(static, cached per domain; `FontSource` names what was picked). Order: (a) `Resources.Load<Font>("Fonts/DriftRounded")`
= `Assets/_Drift/Resources/Fonts/DriftRounded.ttf`, currently **Fredoka** (SIL OFL, licence text beside it; importer
Dynamic, font data included); (b) a development-only OS font, the first installed of `Arial Rounded MT Bold, Varela
Round, Nunito, Quicksand, Comic Sans MS, Segoe Print, Noto Sans Rounded, Rubik, Segoe UI Variable Display, Roboto`
(filtered against `Font.GetOSInstalledFontNames()`, created once via `Font.CreateDynamicFontFromOSFont`; on this PC
that is Comic Sans MS — system fonts must never be bundled); (c) builtin `LegacyRuntime.ttf`. Both (a) and (b) get
a per-glyph fallback list appended to `Font.fontNames` (`Segoe UI Symbol, Segoe UI, Arial, Noto Sans Symbols, Noto
Sans, Roboto, DejaVu Sans, …`), which is where ▸ comes from; ä ö ü ß „ “ · – … are Fredoka's own. **Swapping the
font = replacing that one .ttf** (keep the name, keep it Dynamic). The Fredoka file is the *variable* font and
Unity's legacy text renders its default instance, Light 300: `UiStyle.FontIsLight` therefore makes `Label` embolden
every text (`Weight(bold)`), and bold labels from 44 units up get `Thicken` (an `Outline` in the text's own colour,
0.016 × size) so headlines and button captions are chunky. A static Medium/SemiBold instance of the font would make
both tricks unnecessary — set `FontIsLight` false then. `Lines(spacing)` rescales line spacing for fonts with a
taller line box than 1.19 em (Comic Sans 1.375), and `FitWidth(Text)` keeps a single-line label inside its rect by
scaling it around its pivot whenever text, size or rect change (dirty-vertices callback, nothing per frame); all
button, chip, key-cap, HUD, popup and journal-row labels use it. *Palette* (no pure white/black, softer and lighter
than before): `Glass` dusk blue (0.11, 0.23, 0.35) at 0.76 for HUD panels and chips, `GlassDense` 0.9 for modal
menus, `Veil` for cards inside a panel, `Track` a dark inset (bar tracks, toggle wells, picture backing), `Ghost` /
`Line` cream at 0.14 / 0.13, text `Cream` with `CreamSoft` 0.84 / `Muted` 0.62 / `Faint` 0.34, candy colours with
their lip tone: `Mint`/`MintDeep` (primary, `Ink` text), `Lagoon`/`LagoonDeep` (secondary and round icon buttons,
cream text), `Rose`/`RoseDeep` (warning buttons, cream text), `Cream`/`CreamDeep` (key caps, toggle knob, the seed
field — warm cream wells with ink text where the player reads or types), accents `Sand`, `Coral`, `Sky`,
`Dim`/`DimLight` scrims, `Shadow`, `Sheen`. Cream text on a panel is part of the contract of `Panel`, `Card`, `Chip`,
`SecondaryButton` and `IconButton` — those surfaces stay dark. *Type scale* for the 1080 × 1920 reference: `Display`
156, `Title` 104, `Heading` 60, `Subheading` 44, `Body` 36, `Caption` 29, `ButtonText` 52 / `ButtonTextSmall` 40
(buttons under 120 high), line spacing 1.22, sentence case everywhere. *Spacing* `Margin`/`Pad` 40, `Gap` 24,
`GapSmall` 12; *radii* panels 64 (`RoundedLarge`), cards/pictures 36 (`Rounded`), key caps 18 (`RoundedSmall`),
buttons, bars and chips under 80 units are pills (`RoundedFor(size)` decides). *Sprites* are white SDF textures at
100 px per unit (one texel = one canvas unit), cached per domain: `RoundedOf(radius)` / `PillOf(height)` /
`PillHighlightOf(height)` / `PillShadowOf(height)`, `SoftShadow` (blurred rounded rect, body inset by `ShadowBlur`
36, so a shadow image overhangs its panel by that much and drops `ShadowDrop` 12), `SoftCircle`, `Circle`,
`CircleRing`, `TopHighlight`, `Ring`, `PanelRing` (the gentle line 7 units inside a panel), `InnerShadow`, the
non-sliced superellipse family `Squircle` / `SquircleShadow` / `SquircleRing` / `SquircleInner` for square frames,
`Arrow`, and `Icon(UiIcon)` — Pause, Book, Camera, Album, Close, Back, Home, Dice drawn from soft SDF shapes with
cut-out holes, so one tint colours them. Gotcha: a sliced Image shrinks oversized borders *per axis*, so one
wide-radius pill on a 20-unit bar ends in lens-shaped points — pills are generated per height with radius =
height / 2, and `UiBar.Set` never makes the fill narrower than it is high. *Builders:* `Canvas` (scaler 1080 × 1920
Expand + `SafeArea`), `Scrim`, `Panel` (root without graphic → `Shadow`, `Body`, `Sheen`, `Border` children, content
goes on the root; `dense`, `blocksInput`), `SquirclePanel`, `Card`, `Chip`, `Divider`, `Bar` → `UiBar` (inset track,
glossy pill fill moved by `anchorMax.x`, `Animate(unscaledTime)` lets a glint wander along the fill — `WorldHud`
calls it for the buoyancy bar), `Picture` (rounded or, with `squircle: true`, superellipse `Mask` + inner shadow +
rim; minimap, island preview, photos), `PrimaryButton`, `SecondaryButton(warning, glass)` (glass = with drop shadow,
for buttons floating over the world), `IconButton(size)` / `IconButton(size, UiIcon)`, `ButtonIcon(button, UiIcon)`
(icon in front of a caption), `SetInteractable` (dims lip and glyph too), `Toggle` → `UiToggle.Set(on)` (pill switch
with a sliding knob), `PageDots` → `UiPageDots.Set(index, count)` (soft circles, the current one a wider mint pill),
`Icon`, `Pill`, `Dot`, `Label`, `KeyCap`, `OnResize(rect, action)` (`UiResizeWatcher`, for breakpoints: the HUD
hint chip wraps onto two lines when it no longer fits beside the map), placement extensions (`TopCenter`, `TopLeft`,
`Center`, `BottomCenter`, `Place`, `Stretch`) and `DestroyChildrenNamed`. A button is root → `Shadow`, `Lip`, `Body`
(the target graphic), `Sheen`, `Glint`, `Label`: look graphics up via `Button.targetGraphic` / `UiStyle.LabelOf`, not
`GetComponent<Image>()` on the root. A caller may tint the body; a translucent tint (the quiet „Überspringen" on
Tilda's paper bubble) switches lip, sheen and glint off, so the button becomes a flat ghost pill instead of showing
its lip through. *Motion* (unscaled time, so it plays at `Time.timeScale == 0`; none of it is `[ExecuteAlways]`, and
every component returns at once while at rest): `UiPressFeedback` is a damped spring — held, the control squashes to
103 % × 93 % and sinks into its lip, released it bounces once past its rest shape; `UiFadeIn` fades a screen in over
0.26 s while its panel rises 20 units and pops from 94 % with a back-ease; `UiToggle` slides its knob.
`WorldHud` (top panel 820 wide so the round 120-unit buttons on the right stay clear in portrait, chunkier bars,
separate „Auftrieb" / „Form" labels, bilinear minimap in a squircle frame, hint chip; canvas sorting order 1 because
the fish shader's queue Transparent+5 drew over an order-0 canvas), `SessionScreens` (cream seed field, dice button
`RandomSeed`, pause button icon, `VoiceToggle` as a switch), `WatchTools` (book / camera icon buttons, icons on the
photo-mode buttons), `PhotoAlbum` (page dots up to 8 pages, else „Seite x von y"), `TouchControls` (translucent
discs with a rim on `SoftCircle` shadows; an overlay canvas, so `capture_game_view` cannot show it), `TutorialGuide`,
`HelpScreen` and `TildaBubble` all construct through it; object names such as `TopPanel`, `MapFrame`, `HintPanel`,
`TitleScreen`, `PauseButton`, `RandomSeed`, `VoiceToggle`, `AnimalPopup`, `JournalScreen`, `StickBase` are unchanged.
`capture_game_view --width 540 --height 960` renders a true portrait layout in Edit Mode (values an `Update`
computed under the landscape Game view, e.g. the tutorial arrow or the presenter Tilda, stay landscape; rect-driven
breakpoints such as the hint chip do follow).

**Anleitung (`Bridge.HelpScreen`).** A plain builder/controller class that `SessionScreens` puts into its own canvas
(no scene wiring): button „Anleitung" on the title (it slides into the slot of „Weiter" when there is no save) and in
the pause menu, `OpenHelp(page)` / `CloseHelp()` / `HelpOpen`, Escape closes it first. Five pages with a title, big
page dots, „Zurück" / „Weiter" („Fertig" on the last), a round close button and Tilda (shared 3D portrait, 270 units)
in the corner with her tip in a speech bubble (`TildaBubble`, see below; 640 wide, growing upwards from just above the
page dots, tail at her mouth; the tips are two lines long so the bubble clears the page's cards):
Dein Ziel · Steuerung (key caps and finger glyphs drawn with the style helpers; the card matching
`Touchscreen.current != null` comes first) · Auftrieb & Form (sample bars, round vs. elongated) · Platten, Stürme,
Vulkane · Leben beobachten. The footer button „Tutorial erneut spielen" calls `TutorialGuide.RequestReplay()`.
`SessionScreens.EditorPreview.Help` + `editorHelpPage` / `editorHelpTouchFirst` preview it in Edit Mode.
The tip is typed at 40 chars/s while she grumbles it (mood per page: Cheerful / Warm / Giggly on the volcano page);
closing the screen quiets her. On wide canvases `SessionScreens` hands the big presenter Tilda over
(`HelpScreen.UsePresenter(view)`): the corner Tilda hides, the bubble grows to the full 880 units and its tail reaches
out of the panel towards her mouth, and she shows page 1's Wave / the volcano page's Cheer for 1.8 s and then
`Present` for as long as the tip types. In portrait the corner Tilda stays, and a remark that starts while the
Anleitung is open there („Mh-hm! Da bin ich wieder." after switching the grumble on) borrows the tip bubble. The
footer holds two buttons: „Tutorial erneut spielen" (470) and „Tildas Grummeln: an / aus" (390, `TildaVoice.Toggle()`;
the same toggle is the last row of the pause menu).

**Tutorial with Tilda (`Bridge.TutorialGuide`, pure `SaveSystem.TutorialModel`).** `TutorialGuide` sits on
`SessionUI` with its own canvas (`TutorialCanvas`, sorting 12: over the session screens, under watch tools and
stick). The model holds the steps Greeting → Move → Zoom → Ram → Buoyancy → Form → Watch → Farewell → Done and
advances only on reported events: `Continue()` („Weiter"; not offered for Move and Ram), `ReportTravel` (> 6 u of
self-propelled travel, per-frame delta capped at 2 u so a teleport does not count), `ReportZoom` (`ZoomTarget` moved
> 15 % from the value at step start), `ReportMerge` (`Island.Merged` with the player as host; a merge before its step
is remembered and the step is skipped), `ReportWatched` (animal popup or journal, same rule), `Skip()`, and `Tick`
(idle ≥ 20 s → `Sleepy`, a cheer for 1.6 s after every solved step, the farewell ends itself after 10 s).
`SinkingSuspended` is true exactly in steps 1–4; the guide copies it into the new
`GameSession.SinkingSuspended` every frame (the hook only gates `player.sinkEnabled = playing && !suspended`) and
clears it in `OnDisable` — that is the tutorial's only write to the simulation. It starts on a *new* game (a
transition into Playing with fresh `SessionStats`) while PlayerPrefs `drift_tutorial_done` is unset or a replay was
requested; a restart from step 5 on keeps the step, „Weiter" (continue a save) never starts it. Finish and skip set
the key, `RequestReplay` deletes it and, in a running game, begins again at once. Per-play state is reset from
`Update` via a `RuntimeInitializeOnLoadMethod` session counter, since Play Mode starts without a domain reload. The
speech bubble is a `TildaBubble` (below) beside Tilda's 3D portrait (a `TildaView`; `PoseFor`: greeting → Wave,
typing → Talk, a solved step or the farewell → Cheer, idle ≥ 20 s → Sleepy, else Idle), with the step counter, the
step text typed at `charsPerSecond` 45 on unscaled time, a short hint and „Überspringen" / „Weiter" in its footer.
Portrait (canvas narrower than `wideLayoutWidth` 1850): the pair sits above the minimap, the 330-unit bust on the
left, the bubble from x = 312 to 20 units before the right edge. Wide: Tilda stands full-body (680 units, shrinking to
480 on nearly square windows) at the bubble's left end, the pair centred but never over the minimap, and raised above
the HUD's hint chip where the 1000-unit bubble would meet it; pose `Present` while the text types. Input goes to the
two buttons and to taps on the part of the bubble right of the screen's middle, so the floating stick still starts
underneath. Steps the player can click through turn their pages with „Weiter ▸" (an arrow sprite, not a glyph) and
end with „Weiter" / „Fertig"; steps that wait for the player (Move, Ram) turn their pages by themselves and come
round again in silence. Step 4 shows a mint arrow over the nearest island (edge distance, refreshed every 0.5 s) or
clamped to the screen edge pointing at it; steps 5 and 6 pulse a `Ring` around `WorldHud.BuoyancyRect` / `FormRect`
(world corners → tutorial canvas; both canvases share camera and plane distance). Hidden outside Playing and during
photo mode or the journal; the current step returns afterwards. `editorPreview` (Greeting / Arrow / Buoyancy / Form
/ Sleepy) shows it in Edit Mode. 18 model tests in `Tests/TutorialTests.cs`.
*Grumble (2026-09-20, replaces the spoken lines):* the bubble grumbles every page it types (`TildaVoice.Murmur`,
mood Cheerful for greeting and farewell, Giggly right after a solved step, else Warm), so text, sound and mouth share
one clock; while a page types the guide reports activity, a dozing Tilda snore-mumbles once (`SleepyMumble`, 7
chars/s), and hiding the bubble quiets her. Outside the tutorial the same bubble shows the remarks she makes during
play (`TildaVoice.LineStarted` while Playing with no tutorial running, e.g. „So, auf ein Neues!"): no counter, no
buttons, pages turn by themselves.

**Tilda, the 3D volcano (`Bridge.TildaModel` / `TildaAnimator` / `TildaPortrait` / `TildaView`, 2026-09-20).** Tilda
is a friendly little island volcano, built procedurally at runtime (no assets; the old `Resources/UI/turtle_*`
sprites are unused). *Model:* 27 DontSave meshes, 6 981 verts, all on one `Drift/VertexColor` material with
`_LightAmount` 0 — her shading is **baked into the vertex colours** from a fixed key light (`TildaModel.Lit`,
half-Lambert 0.52–1.1), so day/night, storms and cloud shadows never darken her; lava, crater wall, face and sparks
skip the bake and read as glowing. An 18-segment lathe (sand rim, wobbly green skirt with five conifers, two bushes and
six flowers, rock bands from light tan to dark chocolate, crater rim, a round rosy nose; 2 052 verts + nose), lava
dome + five dribbles, two stubby rock arms with thumbs, four smoke puffs, three „Z" glyphs, seven lava sparks, and
her **face (reworked 2026-09-20: a little old volcano lady, 38 renderers / 14.8k verts in all)**. *Eyes:* each eye
node is scaled to the eye's radii (her left one 7 % bigger and 1.2 cm lower), so everything inside is built on a
unit sphere and turns like the real thing: a warm white ball with two fixed highlights, a `Ball` child carrying a
lava-amber iris (three colour rings) and pupil that rotates to look (±22° / ±15°, plus a fixed convergence because
each eye sits square on her round flank), and an upper and a lower lid — sphere shells that rotate about the eye's X
axis (upper: −78° shut … +80° open with a dark lash line, tilted 7° down towards the outside for a kind look;
lower: pushed up by a big smile, which turns the eye into a ∩ crescent). The layers (iris 1.03, pupil 1.06, shine
1.09, lids 1.13 / 1.17 eye radii) are millimetres apart, hence the portrait's **24-bit depth buffer**. *Brows:* six
lichen-green tufts each, thick at the inner end, own transforms (lift and tilt). *Glasses:* round gold wire frames
(two 20-segment rings, bridge, temples into her flanks) low on her nose, one transform pivoting at the bridge
(bounce on a hop, slip down when she dozes), and a streak of light per lens that rests small at the rim and sweeps
across every 7.4 s (position and length are transforms; its length follows the lens chord). *Around it:* rosy
two-tone cheeks with three freckles each, crow's feet, dimples that widen with the mouth but do not stretch, a wide
smile whose inside glows like the crater and shows her **one tooth**, a frangipani behind her „ear" on the
viewer's left of the crater rim and a lava dribble that ends in a curl over her forehead on the right (both wobble).
Unlit details are built flat and moved onto the face by `Settle`; lit parts built in a node's frame pass that frame to
`Lit` so the baked key light stays in body space. She faces −Z; face
parts are placed by `Surface(angle, y)` on the ideal lathe with local +Z = surface normal. *Poses* (`TildaPose`, all
transforms, no re-meshing, unscaled clock): Idle (bob, breathing squash, slow yaw, puffs loop in 3.4 s, blink every
3–6 s), Talk (mouth + small nods; also a flag that overlays Wave), Wave (right arm swings, mouth a little open), Cheer
(both arms up, hopping with squash, mouth wide, puffs in 1 s, sparks, lava dome swells), Sleepy (slow deep breathing,
arms droop, rising Zs instead of smoke). *Expressions:* `TildaFace.For(pose, side)` is the face of a pose as plain
numbers (brow lift / tilt per side, upper lids per eye, lower lid, gaze, mouth width, smile curvature, glasses slip)
and the animator eases towards it (9 /s): **Idle** half-lidded kind eyes, the left brow a touch higher, a glance to
the side every ~5 s; **Talk** lids up, brows stress what she says and her glances go to the text; **Wave** eyes wide,
brows high, big open smile; **Cheer** squeezed-shut laughing eyes (lower lids up), brows up, wide mouth with tooth;
**Sleepy** lids shut (the lash line hangs as a ⌣), brows low, a small round mouth breathing in and out, glasses
slipped down her nose; **Present** gaze and the brow on that side go to what she presents; **Comfort** brows steep
with the inner ends up, soft lids, a smaller smile, eyes a little lowered. The blink closes the upper lid for 0.14 s. *Portrait rig:* `TildaPortrait` is one shared
DontSave root object at y = −3000 (three main-camera far-clip lengths below the sea) on the unnamed layer 31; its
plain Base camera (no `UniversalAdditionalCameraData` → no post, no stacking; depth −90, HDR off, FOV 24°, clip 2–20,
culling mask = layer 31 only) renders into a 384² ARGB32 target with **alpha 0 background** (clear colour = glass teal
at alpha 0, so the straight-alpha `RawImage` shows no dark fringe) and 4× MSAA (`Msaa` const; 0.25 → 0.30 ms, visibly
smoother eyes and outline). `TildaPortrait.CreateImage(parent, size)` returns a `RawImage` with a `TildaView`; a view
registers in `OnEnable` and releases in `OnDisable`, the rig is built on first use (2.6 ms) and **destroyed with the
last view** (meshes, material, RT found through the hierarchy, so an orphan after a domain reload cleans up the same
way), i.e. a hidden Tilda costs nothing and leaves no objects. While shown, `Update` animates and enables the camera at
most 20 times per second (`MaxFps`; ≈ 0.27 ms CPU per render in the Editor, animation 0.003 ms) like `IslandPreview`;
in Edit Mode it renders a still on `SetPose` / `RenderNow()` instead. The pose comes from the most recent view
(`SetPose(pose, talking)`, `Play(pose, seconds, talkSeconds)` for a gesture that falls back to the base pose). Users:
the tutorial bubble, the Anleitung corner (Wave on page 1, a short Cheer on the volcano page, otherwise Talk for the
length of the tip, then Idle) and the presenter beside the menus (below).
*Presenter poses:* `Present` (arm on `PresentSide` held out at about +14° with a slow beat while
talking, body yawed 17° that way — she faces −Z, so a negative yaw looks to the viewer's right — eased) and `Comfort`
(both arms a little open, head tilted, slow nod; game over); `TildaAnimator.VoiceLevel` ≥ 0 replaces the sine talk
cycle by the loudness the grumble synth really produced (Cheer keeps the mouth at least 0.55 open, asleep it only
widens her snoring mouth); `TildaPortrait` feeds it from `TildaVoice.Grumbling / Level` for whichever view is
current, so any visible Tilda moves her mouth with the sound. With the grumble switched off the talk cycle runs, but
only while a bubble is still typing (`TildaBubble.TypingNow`), not for the whole time a remark stays up. `TildaView.Play(pose, seconds, talk, then, thenSeconds)` chains a second pose (wave, then present).
`TildaView.SetResolution` (the rig's RT follows the largest enabled request: 384² small uses, 768² `LargeSize` once
she is more than 400 screen pixels big; still ≤ 20 fps, 4× MSAA, alpha-0 background, rebinds all views) and
`SetFullBody` (bust: FOV 21°, target y 1.38 — tighter than before so her face reads at bubble size, her base runs out
of the lower edge; full body: FOV 26°,
target y 1.3 — base rim at 5 % above the lower edge, base centre 15 %, body within ±0.37 of the width, crater rim at
65 %; the rig switches framing per current view). `TildaPortrait.HopHeight` tells the presenter how high a cheer
hop is, and `MouthIn(fullBody)` where her mouth is in the picture (bust 0.5 / 0.252, full body 0.5 / 0.321 from the
lower left; a test compares them with `MeasureMouth()`), which is what the speech bubbles aim their tails at. Cost:
animation 0.005 ms without allocations, a render 0.35 ms in the Editor.

**Tilda's grumble (`Audio.TildaGrumbleSynth`, `Bridge.TildaVoice` + `TildaVoiceLines`, 2026-09-20; replaces the
rendered TTS voice, which sounded like a computer).** She has no words any more, only a warm, elderly, slightly rumbly
mumble that follows the text of her speech bubble; zero audio assets. *Shared clock:* `GrumbleTiming` (pure) says when
character *i* of a text is on screen — 1 / charsPerSecond per character, + 0.34 s after a sentence, + 0.12 s after a
comma, nothing after the last word — and both the bubble's typewriter (`CharsAt`) and the plan use it. *Plan:*
`GrumblePlan.Build(text, mood, cps)` (pure, no allocation, ≤ 160 entries) walks the text: every vowel group is a
syllable and becomes a grunt at the moment the typewriter reaches it, unless the last grunt started less than the
mood's spacing ago (Warm 0.118 s) — then the earlier grunt is drawn out and its formants glide to the second vowel,
which is what makes it a mumble rather than a beep per letter (at 45 chars/s about half the syllables get their own
grunt, ≈ 4.5 sounds/s; typed slowly, all do). Formants F1–F3 come from the actual vowel (a e i o u ä ö ü) and are
pulled 35–80 % towards a neutral mouth; pitch = mood base × melody (the sentence starts ~1.5 semitones up and drifts
to the mood's end point, + 1.1 on the first syllable of a word, a question lifts its last third by up to 9, ± 0.8
hashed), clamped 142–232 Hz; the consonant before the vowel picks a soft noise tick (plosive 2.3 kHz / 9 ms, sibilant
4.3 kHz / 24 ms, h 1.5 kHz / 30 ms, nasals and liquids none); a grunt ends 28 ms before the next. After the last
sentence, and after 38 % of the others, comes a closed-mouth „mh-hm" (two hums, low then 4.5 semitones up) in the
breath the clock leaves there. *Moods* (`GrumbleMood`): Warm (170 Hz base, falls 2.5 st), Cheerful (168 Hz, rises
3.5 st: greeting, farewell, new island), Giggly (169 Hz, grunts of 0.1 s every 0.09 s alternating +2.2 / −1.2 st,
three rising hums: solved steps), Soft (153 Hz, slow, quiet, falling, one sighing hum: game over), Sleepy (151 Hz,
one long falling „mmh" every 0.34 s or more, no ticks: dozing). *Synth* (`ISynthSource`, audio thread, no
allocations, 32-sample control blocks): polyBLEP saw tilted by a 2.6 kHz one-pole → three Chamberlin band-passes
(Q ≈ 6 / 8 / 9, gains 1 / 0.85 / 0.3; F1 opens from 62 % over the first 35 ms like a mouth) + a 420 Hz body path,
× (1 − 8 % subharmonic growl at f0 / 2), + a little f0 / 2 sine and 95 Hz rumble noise, envelope
sin(π·u^0.62) (quick to open, slow to close), 5.3 + 2.1 Hz vibrato (±1.1 %), 2 % jitter, 5 % tremor at 6.1 Hz, 28 Hz
pitch portamento between grunts, 4.2 kHz output low-pass, soft clip. The main thread hands a plan over through three
preallocated slots (`Speak` picks the one that is neither active nor pending), a new text fades the old one out over
20 ms, `Stop()` likewise; `Level` is a follower of the output (attack 60 Hz, release 7 Hz, × 3.4) published once per
buffer — her mouth. Measured offline from rendered WAVs (48 kHz, scratch folder, numpy autocorrelation; nobody has
*heard* it yet): median F0 Warm 180 Hz (5–95 %: 156–212, falling 182 → 168), Cheerful 204, Giggly 214, Soft 162
(173 → 141), Sleepy 148; peak 0.41–0.66, RMS 0.06–0.12, largest sample step 0.085; spectrum −3 dB in 120–1000 Hz,
−13 dB in 1–3 kHz, −30 dB above 3 kHz, < −16 dB below 120 Hz; 4.5 s of audio render in 21 ms. *Runtime:*
`TildaVoice` (auto-created DontSave object under `SessionUI`) owns the synth and its output — a DontSave child
„TildaGrumble" with `AudioSource` + `SynthAudioOutput`, the same mechanism as AudioDirector's Music / Sfx / Life
(filter mode, or streamed clip when the director already fell back or no filter call arrives within 1.5 s),
`ignoreListenerPause`, `volume` 0.8. `Murmur(plainText, mood, cps)` is what a `TildaBubble` calls for the page it
starts typing; `Quiet()` stops it. *Remarks:* `Say(key, VoicePriority)` queues one of the nine short texts in
`TildaVoiceLines` (`greeting`, `pause`, `gameover`, `new_island`, `voice_on`, `idle_1..4`; rich text, mood per key)
through the pure `TildaVoiceQueue` (Interrupt fades the current one over 0.15 s and drops waiting lines, Queue waits
(≤ 2, no duplicates), IfSilent is dropped unless she is silent, `Hush(prefixes…)` ends only matching keys). When a
line starts, `LineStarted(TildaLine)` asks whoever shows Tilda to put it into a bubble and set `shown` — the
presenter beside a menu, the Anleitung's tip bubble in portrait, the tutorial's bubble during play; a line nobody
shows is dropped. It stays up `LengthOf(key)` = typing + reading time of its pages (`TildaBubble.SecondsFor`, 4–8 s),
ends early when the bubble is tapped through or its Tilda leaves, and `LineEnded(key)` closes the bubble. `Say`
works with the grumble switched off (PlayerPrefs `drift_tilda_voice`, the old key; „Tildas Grummeln: an / aus"):
the bubbles still appear, only the synth is silent; switching it on answers „Mh-hm! Da bin ich wieder.". *Ducking:*
`AudioDirector.VoiceDuck` (static 0..1, 1 while the synth sounds, reset on disable) is smoothed over
`voiceDuckSeconds` 0.25 on **unscaled** time and multiplies music, sfx and life by `1 − grumbleDuckDepth` (0.15;
the field was renamed from `voiceDuckDepth` so the 0.35 serialised in scenes for the spoken voice is not kept);
`Drift.Bridge` references `Drift.Audio`. Who remarks: title greeting once per app start (`Queue`), up to four idle
remarks after `idleRemarkSeconds` 28 on the title (`IfSilent`, pose Present), `pause` once per app start, `gameover`
on game over and `new_island` queued when the new island starts. The old pipeline is unused and waits for deletion:
`Assets/_Drift/Resources/TildaVoice/` (29 WAVs, 16 MB), `Tools/TildaVoice/`, `Editor/TildaVoiceExport.cs` (an empty
stub). 30 tests in `Tests/TildaVoiceTests.cs` (shared clock, syllable count follows the text, grunts stay in range and
never overlap, a flavour per mood, no NaN / peak < 0.9 / silent tail per mood, silence when switched off, the envelope
opens the mouth once per grunt and shuts it in pauses, take-over without a click, other sample rates and mono, a face
per pose, mouth constants vs. the rig, typed rich text keeps every character, paging, tail geometry, remark layout,
queue rules, presenter layout), invoked by reflection from `eval`.

**Tilda's speech bubble (`Bridge.TildaBubble`, 2026-09-20).** One component for the tutorial, the Anleitung tips and
the remarks beside the menus, built from `UiStyle` helpers and `UiStyle.Font` only (the rounded UI font reaches it by
itself; the comic tail is the one sprite it generates). Look: cream paper `Paper` at 0.98 opacity, dark warm ink type
(`InkText`), a 7-unit ink outline on a soft dark halo plus a 20-unit drop shadow — readable over bright sea, dark
night and the glass panels alike —, her name as a lava-red chip (`Subheading` 44, bold) straddling the top edge, body
type 40 on the 1080 × 1920 reference, padding 46 / 64 / 42, the step counter top right; with a footer (tutorial):
hint, „Überspringen" as ink-on-paper secondary button and the mint „Weiter" with an arrow sprite while more pages
follow. Pure text helpers: `Key(word)` marks a key word (bold, lava ink `#B23F14`; one or two per text),
`Plain`, `Pages(rich, maxChars)` (splits at sentence ends, else at a space, never inside a tag pair;
`MaxCharsFor(width)` ≈ four lines), `Typed(rich, n)` (the untyped rest stays in the string as transparent text so
lines never re-wrap; open tags are closed and reopened around the cut, colour tags inside the hidden rest are
dropped), `SecondsFor`. `Show(rich, mood, cps, autoAdvance, onFinished)` types page by page on unscaled time,
grumbles each page (`TildaVoice.Murmur`), pops every new page in from 0.9 to 1 with a little overshoot in 0.2 s and
sets its own height from the text's preferred height (so a wider font grows the bubble instead of clipping);
`Next()` finishes the typing, turns the page, then runs the final action; `ShowStill` is the Edit Mode preview.
`PointTailAt(world, gap)` slides the tail's base along the edge that faces the point (clear of the rounded corners),
turns it towards the point and clamps its length to 34–150; outline tail, outline, body, tail fill are layered so
the join is seamless; nothing is touched while target and size are unchanged.

**Tilda as presenter (`Bridge.TildaPresenter`, 2026-09-20).** One presenter per `SessionScreens` canvas; the screen
that is up calls `Present(scrim, canvasSize, panelSize, fraction, allowPeek)` every frame and the presenter moves into
that scrim *behind* the panel (sibling 0), so it fades in with the screen. `Compute` is the pure layout rule, decided
from the free strip beside the centred panel: **Side** when `(canvas.x − panel.x) / 2 − Margin ≥ 440` (landscape,
tablets): her square picture is `fraction × canvas height` (title 0.6, Anleitung 0.56, game over 0.5, pause 0.42;
`presenter*` fields), capped so that her body (±0.37 of the picture) fits between the HUD minimap (Margin + 340 +
Gap) and the panel minus a Gap — 1106 units = 57.6 % of the height on 16:9 —, standing left of the panel, base rim
`Margin + 0.05 × size` above the bottom edge, on a `SoftCircle` ellipse (0.92 × 0.36 of her size, centred on her base,
alpha 0.85, shrinking while she hops). **Peek** otherwise (portrait phones), when at least 90 units are free above
the panel: size `(free − 30) / 0.35` clamped 250…540, 190 units right of the middle over the straight part of the
panel edge, cut by a `RectMask2D` exactly at the panel's top edge just below her mouth (the glass panel is
translucent, her body would shine through), no shadow. **Hidden** if neither fits or `allowPeek` is false (the
Anleitung in portrait keeps its corner Tilda). Entrance on unscaled time, 0.85 s: three decaying hops in from beyond
the left screen edge, or rising from behind the panel with a little overshoot; Edit Mode rests at the end. Poses:
title Wave (as long as the greeting, max 4.5 s) then Idle, Present during idle remarks; pause Idle; game over Comfort
for the length of the line, Cheer in the last second of the countdown; Anleitung see above. The journal, album and
photo mode dismiss her. *Remarks:* the presenter listens to `TildaVoice.LineStarted` and shows the line in a
`TildaBubble` parented to the same scrim as last sibling (over the panel); `Remark(layout, canvas, panel, height)`
is the pure rule: beside a menu the bubble (420–600 wide) stands up and to the left of her head, inside the left
margin and clear of her face, its tail pointing down at her mouth; peeking, it lies left of her, resting on the
panel's top edge and sliding down over the panel only as far as the screen's top edge forces it. Her remark leaves
with her (`Hush`), so the next queued line is not held up. `PreviewRemark(rich)` shows one without the queue.
`TildaPresenter.EditorCanvas` (static, Edit Mode only) lays out as if the canvas had that
size: `capture_game_view --width 540 --height 960` renders a true portrait canvas but runs no `Update`, so a
portrait preview sets `(1080, 1920)` first (and back to zero afterwards); `TutorialGuide.PlaceBubble` honours it too.

**Watch tools (Phase 7, reworked 2026-09-20 after the owner's play test; `Drift.Bridge.WatchTools`, light interaction
only).** One `[ExecuteAlways]` component next to `SessionScreens` on `SessionUI` with its own procedural canvas
(`WatchToolsCanvas`, ScreenSpaceOverlay like every game canvas since 2026-09-21, sorting 15: above the session screens, below the touch stick) built from the
shared `UiStyle` builders, one small `Build*` method per panel. It reads the simulation and writes only the camera and
the session's watch holds (`GameSession.PhotoInputHold/FollowInputHold`, `PhotoSinkHold/FollowSinkHold`; it never
writes `Island.InputLocked` itself). `editorPreview` (None / Popup / Journal /
Photo / Album / AlbumPhoto) shows each panel with sample data in Edit Mode (Popup: a foreign flamingo with its origin
line and a collection toast); with preview None a popup, a followed
herd or photo mode staged from `eval` (`Tap`, `FollowHerd`, `EnterPhotoMode`) stays up and live, so real state can be
captured without Play Mode (the camera then only moves when `eval` calls `StepCamera(input, dt)`; leaving either mode
in Edit Mode snaps the chase camera back).
`Drift.Bridge` references `Drift.Visuals` (fish schools); `Drift.Tests` references `Drift.SaveSystem` and `Drift.Bridge`.
*Tap:* mouse **and** touchscreen are read side by side every frame (a touch laptop reports `Touchscreen.current`
while the player uses the mouse — the old `if (touchscreen) … return;` never reached the mouse branch, which is why
clicking animals did nothing). Each device has its own `Press` (position, time, `TapPress` flags); the touch path takes
`primaryTouch.startPosition` and also catches a tap that began and ended between two frames (phase already `Ended`,
unseen `touchId`). On release the pure `TapPicker.Gate(playing, press, tapHoldSeconds 0.6, tapSlop 30 u)` answers
with a `TapReject` (None / NotPlaying / OverUi / MultiTouch / Pinching / Steered / HeldTooLong / MovedTooFar;
`NothingNear` when the pick misses): `overUi` comes from `WatchTools.UiBlocks(screenPos)`, which walks
`GraphicRegistry.GetRaycastableGraphicsForCanvas` of every enabled canvas with a `GraphicRaycaster`
(`RectangleContainsScreenPoint` + `Graphic.Raycast`) instead of `EventSystem.IsPointerOverGameObject`, whose answer
belongs to the input module's pointer of the last UI update; `steered` is set only while
`TouchControls.IsPointerOnStick(id) && StickDeflected` (new accessors) — a press that merely *starts* in the
thumbstick half is still a tap, the mouse never counts as the stick. `Release(press, pos)` is public, so the whole
path runs from `eval`/tests without devices; `debugTaps` logs every press (what was picked at how many px, or why it
was rejected and which graphic lay under it). `Tap(screenPos)` searches every island whose edge is within `tapRange`
60 u of the camera or of its focus and whose herd/critter system is not in the Far tier: every animal (young too,
projected at body height) and every visible critter (crab, turtle, butterfly, firefly; herd animals win a near tie)
goes through `TapPicker.Consider`, the closest within `pickRadius` 64 canvas units wins. Units become pixels via
`TapPicker.PixelsPerUnit` = max(canvas scale, shorter screen side / 1080): the 1080 × 1920 *Expand* scaler drops to
0.56 in a landscape window, which had shrunk the old 40-unit radius to 22 px; now it is 64 px on any 1080p screen.
(The tap fields were renamed — `pickRadius`, `tapSlop`, `tapHoldSeconds`, `followZoomLevel` — so the stale values
serialized in `Planet.unity` cannot override the new defaults.) Feedback: a `TapRipple` ring expands and fades at the
tap (sand on a hit, cream on a miss) and a pulsing `PickMarker` ring lies on the ground under the picked creature as
long as the popup is up. The popup (species name from `LifeNames.Of` — `WatchTools.NameOf/PluralOf` are thin wrappers, so
every biome species reads German in the popup and the follow chip —, „· Jungtier" for `IsYoung`, herd size, mood from
`MoodOf`; for a species that is foreign to the island it stands on (`IslandHerdSystem.IsForeign`) a fourth, sky-blue
line „Fremde Art von tropischen Inseln" (`WatchTools.OriginLine` ← `CollectionCatalog.OriginOf` ← `Biomes.TryHomeOf`)
that makes the card 40 units higher and slides the follow button down (`LayoutPopup`, `PopupOrigin`);
critters: „Lebt am Strand" + what it is doing, no follow button, shorter card) re-places itself above the
creature every frame (below it while following, so it stays clear of the return button), clamps to the safe area,
refreshes its text only when state/size/young changed, fades over `popupFadeSeconds` after `popupShowSeconds` 7 (4 before v0.6.3) and is
validated each frame. Only its „Herde folgen" button takes raycasts, so an animal half under the card can be tapped; a
miss closes it.
*Orbit camera (2026-09-20, owner: "the photo camera pulls away from the island; let me rotate around the herd with
WASD"):* photo mode and herd following share one pure orbit model, `Bridge.PhotoRig` + the device-free `OrbitInput`
struct (pointer deltas in pixels, key axes −1..1, zoom factor, reset, centre). The rig **stores no world position**:
the pivot is the *subject* — handed in every frame: the followed herd's centre on the terrain, else the top of the
player island's centre — plus a planar offset in world axes (so island drift moves the camera along and the body
rotation is irrelevant) at an eased ground height under the pivot. (The old rig captured `pivot` once in world space
at `EnterPhotoMode`; the island kept drifting on momentum, plates and storms while the pivot stood still — that was
the "camera pulls away" bug.) Input moves targets, `Step(input, dt)` eases yaw / pitch 8–85° / distance / offset
towards them on unscaled time (`smoothing` 10/s); distance limits come from the subject (`DistanceLimits(radius,
floor 1.5)` → max `7·r + 15`; herd: `max(herd radius, island radius / 2)`), the pan offset is clamped to
`panRadiusFactor` 1.5 × island bounding radius and is off (forced to 0) outside photo mode. `FromPose` takes the
camera over exactly where it stands (targets clamped, a 0.5 s rotation blend turns the view onto the subject), `R` /
`GoHome` returns the short way to the home orbit (photo: the entry orbit; follow: the chase framing at
`followZoomLevel` 0.3 behind the heading), „Zentrieren" / `Center` zeroes the offset. `WatchTools` gathers mouse
(left drag orbit, wheel zoom, right/middle drag pan), touch (one finger orbit, pinch zoom, two-finger drag pan) and
keyboard (A/D or ←/→ around the subject — D moves the camera to its right —, W/S or ↑/↓ tilt, Q/E or numpad ± zoom,
R reset) into one `OrbitInput` in `Update` and poses `Camera.main` in `LateUpdate` via the public `StepCamera(input,
dt)` while `IslandChaseCamera.Suspended`; the pose is kept above every island under it with the chase camera's own
`RequiredHeight` (three sight-line samples close up), clearance and near clip ease with `PhotoRig.CloseBlend(distance)`
(`IslandChaseCamera.SuspendedClose`). Handing back calls `IslandChaseCamera.ResumeEased(returnEaseSeconds 0.6)`: the
pose blends (smoothstep, unscaled, carried with the focus) from where the camera stands into the chase pose;
`SnapToTarget` cancels it. An `OrbitHint` chip is up in both modes: „A/D drehen · W/S neigen · Q/E Zoom · R zurück"
(photo adds „Rechte Maustaste: verschieben") or, with `InputMode.TouchPreferred`, „Ziehen: drehen · Zwei Finger: Zoom".
*Follow:* „Herde folgen" → `FollowHerd`: the orbit camera takes over around the herd centre and eases to the home
orbit; the framing radius (herd radius + 1, ≥ 1.5, eased) only sets the zoom limits. **The island is not steered and
does not sink meanwhile** (`FollowInputHold` + `FollowSinkHold`; WASD belongs to the camera), taps on animals keep
working, and a finger that lands in the thumbstick half orbits (the stick is released every frame). The chase zoom is
saved and restored. „Zurück zur Insel" and a `FollowChip` („Schafe · 7 Tiere · grast": plural name, size, majority mood,
refreshed every 0.4 s) are up the whole time. `ReturnToIsland` also fires when the herd is gone (if an earlier herd
died out and the indices shifted, the herd of the same kind nearest the last centre is picked up again), its island
sank or lost its land, or the session leaves Playing/Paused.
*Journal = collection album (2026-09-20, owner: "so kann man neue Arten auf seiner Insel sammeln").* Model, pure C# in
`Drift.SaveSystem`: `CollectionCatalog` builds the entry list once from `Biomes.Collectibles` — every herd animal and
plant under the *first* biome that brings it forth (`TryHomeOf`; palm and reed therefore count for the temperate
islands), Gemäßigt 10 / Tropisch 8 / Nordisch 9 / Savanne 9 — plus „Meer & Himmel" with 20: the four critters
(collectible, they live on the island), Vogel, Seevogel, Fisch (the `FishSystem` schools), Fischschwarm (bait ball),
Fliegender Fisch, Delfin, Meeresschildkröte, Qualle, Rochen, Wal, Walkalb, Walbulle and the four boats (seen only).
56 entries, 40 of them collectible; masks (`CollectibleMask`, `LifeMask`, `AnimalMask`, `PlantMask`, `CritterMask`,
bit = `(int)LifeKind`), `IndexOf(LifeKind|SeaKind)`, `EntriesOf(section)`, `HintOf(entry)` („lebt auf tropischen
Inseln", „wächst auf …", „lebt am Strand", „schwimmt im offenen Meer" …). `DiscoveryJournal` holds per entry the
state **unknown → seen → collected** (never back; `MarkSeen`, `MarkCollected`, only collectibles can be collected and
collecting something unseen counts as seeing it), `SeenAt` / `CollectedAt` (run time), `BestOf` (record count) and
`CurrentOf`, plus `YoungBorn`, `FiresSeen`, `BestStage`, `SeenIn/CollectedIn(section)`, `UnseenLifeMask` and
`Version`. `ReportPresence(mask, time)` takes what lives on the player's island as one ulong: an unchanged mask
costs one compare, new bits are collected and returned, lost bits only clear the presence (`PresentNow`, count 0) —
the entry stays „gesammelt" and the card says „zurzeit nicht auf deiner Insel". `CollectionToasts` (pure) queues the
news: one toast at a time, news that arrives meanwhile or in the same scan is combined — „Neu auf deiner Insel:
Flamingo!", „… Zebra und Giraffe!", from three on „5 neue Arten gesammelt!"; sightings are a quieter second class
(„Zum ersten Mal gesehen: Delfin", „3 neue Arten gesehen") that waits for collections and drops an entry that is
collected in the same breath.
*Scan:* every `discoverInterval` 1 s `WatchTools.Scan(journal, focus, time)` (public, index loops and
`TryGetComponent`, 0 B and 0.007 ms per scan in the start scene with 45 islands) first reads the player island —
`SpeciesPresent | PlantsPresent | critter bits` → `ReportPresence`; herd and critter counts every scan, plant counts
one species per scan round-robin because `CountOfKind` walks every plant (all of them on the first scan and when the
journal opens), `ReportStage(settlement.Stage)`, Lebensbuch — and then marks as seen what is within `discoverRange`
25 u of the focus (player or followed herd) on *other* islands: herd centres and visible critters of near-tier
systems, and the island's whole `PlantsPresent` mask; every one of those loops is skipped by a mask test once nothing
unseen could be in it. Sea and sky (skipped once everything is seen): flocks (`IsSeabird`), fish schools
(`IsBaitBall` → Fischschwarm, else Fisch), `SeaLifeSystem` groups (`GroupKind/GroupPosition`; a lone `Whale` group is
the Walbulle, a pod the Wal plus Walkalb with `GroupHasCalf`, whales in dive state 0 do not count, resting gulls are
Seevögel, seaweed and flotsam are not in the album), `FlyingFishActive`, and `ShipSystem` slots. `Births` and
`IgnitionCount` of the islands in range are tracked as per-component deltas as before. It reads only; the first scan
of a run (new or loaded) clears the toast queue, so a run never opens with „8 neue Arten". `seaLife` / `ships` are
new lazily resolved references (no wiring needed).
*Panel (`Bridge.JournalPanel`, a builder/controller like `PhotoAlbum`, glyphs in `Bridge.JournalGlyphs`):* title,
„Gesammelt 9/40 · Gesehen 17/56" with a mint bar, the Lebensbuch line, six tabs — Gemäßigt, Tropisch, Nordisch,
Savanne, Meer & Himmel, Insulaner — each with its own little count (the active tab is the mint one), a section header
with „5/8 gesammelt · 6/8 gesehen" and a bar per biome, and a pool of 15 soft cards refilled per tab and page: colour
swatch with a white SDF silhouette (13 glyphs by kind: four-legged animal, wader, tree, sprout, crab, turtle,
butterfly, firefly, bird, fish, whale, jellyfish, boat; a mint check badge when collected — drawn, not a font glyph),
German name or „???", a state pill (gesammelt mint / gesehen sand / unbekannt), „seit 7:11" / „bei 6:42", and the
detail line („7 auf deiner Insel · Rekord 18", „zurzeit nicht auf deiner Insel", „noch nicht auf deiner Insel", or
the biome hint for unknown entries). The Insulaner tab shows two read-only cards: settlement (stage now, highest
stage of the run, folk, villages, buildings by German name) and the old island statistics (animals, herds, plants,
trees, foreign species, young born, lightning fires, islands absorbed). Two layouts switched by the screen rect
(`UiStyle.OnResize`, so an Edit Mode capture at 540 × 960 lays out right): portrait 960 × 1500 with 2 × 3 tabs and
2 × 5 cards; from 2100 canvas units of width a landscape two-pane 1980 × 1080 (header, tabs and close on the left,
3 × 5 cards on the right). A `Fit` rect between scrim and panel scales the panel into the room it has (≤ 1.5), since
a landscape canvas is only 1920 units high; the fade-in animates the panel's own scale, hence the extra level.
Sections with more entries than cards get page dots and arrow buttons (Meer & Himmel: two pages).
`WatchTools.ShowJournalTab(tab, page)` switches from `eval`; `editorPreview = Journal` fills the panel from a sample
run (natives, a lost goat herd, a tropical merge, sightings at sea). Refreshed every `journalRefresh` 0.5 s or when
`DiscoveryJournal.Version` moved. Opened from the pause menu it lies over it; opened from the HUD book icon it pauses
the session itself and resumes on close.
*Toast:* a `CollectionNews` chip (book icon, sand text for collections, smaller cream text for sightings, fades in)
under the HUD panel, or under the follow controls while a herd is followed. It waits while photo mode, the journal or
the album is up and is cleared on the title and at game over. Tilda does not comment yet: `TildaVoice.Say` only takes
keys of `TildaVoiceLines` (integration request: lines such as `collect_first`, `collect_many`).
Tests: `Tests/CollectionTests.cs` (20: catalog, state transitions, counts, save round-trip, v5 migration, toast batching,
Lebensbuch, card texts, paging), name and origin-line tests in `Tests/WatchToolsTests.cs`.
*Photo mode:* entered from the round camera button in the HUD (`PhotoButton`, stacked under pause and book, top
right) or „Fotomodus" in the pause menu (which resumes the session). Time runs on, but the island **does not sink**:
`GameSession.PhotoSinkHold` and `FollowSinkHold` are flags beside the tutorial's `SinkingSuspended`
(`sinkEnabled = SinkAllowed(playing, tutorialHold, photoHold, followHold)`), so no owner can release another's hold.
Input works the same way: `Island.InputLocked` is a single static bool that `GameSession.ApplyState` rewrites on every
state change, so the watch tools hold input through `PhotoInputHold` / `FollowInputHold` and the session composes
`IslandInputLocked(playing, photoHold, followHold)`. Photo mode sets the static `WatchTools.HudHidden` (read by
`WorldHud`) and puts the camera on the orbit rig above (panning enabled, „Zentrieren" button), kept
`photoGroundClearance` above every island under it and `photoMinHeight` above the sea. Entered while a herd is
followed, the herd stays the subject and the orbit carries over; „Zurück" then returns to following (pan offset eases
back to 0), otherwise the chase camera resumes eased. If the herd vanishes under a running photo mode the rig is
re-based onto the island without moving the camera. `SessionScreens` hides the
pause button, disables the thumbstick and pinch-zoom and routes Escape / Android back to `AlbumBack`, `ExitPhotoMode`
or `CloseJournal`. „Foto speichern" runs a coroutine: every enabled root canvas is switched off, one frame passes,
`WaitForEndOfFrame`, `ScreenCapture.CaptureScreenshotAsTexture`, `PhotoLibrary.Save` (PNG
`persistentDataPath/Photos/drift_<yyyyMMdd_HHmmss>.png`, `_2`… within the same second, plus a 256-px
`…_thumb.jpg` made from the written file — a blit from the capture texture comes out too bright in a linear
project), canvases back on, a cream shutter flash (0.45 s) and the toast „Foto gespeichert". „Album" opens the album
over photo mode, „Zurück" (or any state change away from Playing) releases photo mode's two holds and hands the
camera back (eased, or to the followed herd).
*Fotoalbum (`Bridge.PhotoAlbum`, pure file side `Bridge.PhotoLibrary`):* a builder/controller like `HelpScreen`,
placed into the WatchTools canvas (no scene wiring); opened from photo mode, the pause menu and the title
(`WatchTools.OpenAlbum/CloseAlbum/AlbumBack/AlbumOpen`). Grid: „Fotoalbum", „12 Fotos", 3 × 4 rounded thumbnails per
page (centre crop via `uvRect`), newest first (`PhotoLibrary.List`: date from the file name, write time as fallback),
arrow pager „Seite 1 von 2", a friendly empty state. One thumbnail is loaded per `Tick` for the visible page only
(a photo without thumbnail gets one made and stored once) and all textures are destroyed on page change and close.
A tap opens the full view (picture fitted between date line „20. September 2026 · 12:03 Uhr" and the buttons,
„Foto 3 von 12"), „Löschen" → „Dieses Foto wirklich löschen?" with „Ja, löschen" / „Behalten" (deletes PNG +
thumbnail), „Zurück". Escape steps confirm → picture → grid → closed.

**Movement feel (2026-09-20).** Steering builds up linearly over `turnEaseIn` (0.35 s) and winds down
at `2·turnResponse·√Agility`; a keel term (`velocityAlign`, 2.5/s) swings self-velocity toward the
heading with speed preserved, so turns redirect the island instead of sliding it sideways (slip
plateaus ~22° in a full-rate turn). **Heading and body are separate (2026-09-20, owner: "W must always
drive straight ahead").** `Island.Forward`/`Yaw` is the heading: steering, thrust, keel and the chase
camera; only player input turns it (AI islands keep their spawn heading). The heightfield/transform is
yawed by `BodyYaw` relative to it (`transform.rotation = LookRotation(BodyForward)`), and local space
is body space everywhere: `ToLocal/ToWorld` use the cached body basis and equal the transform's XZ
frame, so `DetectContact`, `MergeFrom`, flocks, fish and the life systems (which live under the
transform) follow a turning body without knowing about it. The ambient yaw drift (at most
`driftRotation·Agility`, seeded slow double sine, ±~14° for the start island) turns the body only.
**The body turns on a merge (against "one long formation"), dosed since 2026-09-20 (owner: "weniger
stark bei mehreren Treffern kurz hintereinander, Richtung je nach Treffpunkt"):** `MergeFrom` queues a body
turn; how far and which way is decided here, *how it moves in time* is a damped angular spring (see "Time
response" below).
*Direction* is the torque of the off-centre hit in **heading space** (`Island.TorqueSign`, pure and tested):
`sign(cross(heading, contactDir))` with the contact taken relative to the host's centre *before* the merge
(seen from the merged centroid an equal-sized guest always lies dead centre) — a hit right of the nose turns
the body clockwise seen from above, left of it counter-clockwise, so the struck side swings to the rear the
short way; rear hits follow the same sign. Within `DeadCentre` (|sin| < 0.1, about ±6°) the nose turns
towards the side of the heading axis with less land (`LandSides`, cell count about the merged centroid;
balanced within 2 % → seed parity). *Magnitude*: in that direction only, the candidate in [90°, 180°]
(5° steps) whose facing direction has the smallest `1.5·reach(front) + reach(rear)`, where reach is
`IslandShape.LandReach` — the ray distance from the centroid to the last land, smoothed over ±15°; for an
oval that is the minor principal axis, for a cluster the notch between the bodies; ties go to the smaller
turn, so a round island turns exactly 90°. *Dosing*: `w = clamp01(timeSinceLastMerge / bodyTurnCooldown 8 s)`
on the island's own `Tick` clock, angle = `lerp(bodyTurnRapidAngle 25°, full, w)`, and while a turn is still
in flight the queued total is clamped to ±`bodyTurnMaxQueued` 120° (an isolated merge may still use the
full 180°). `LastBodyTurn` exposes the signed angle of the last merge. AI hosts follow the same rule with
their spawn heading. Measured (radius-3 host and guest): guest 3 / 1.5 u right of the nose +90°, left −90°,
rear-right +120°, dead centre ±90° by land balance; a second merge 0.5 / 1 / 2 / 4 / 8 s after the first
turns 34° / 44° / 41° / 57° / 90°; five merges 0.5 s apart queue 90 → 114 → 117 → 119 → 120°, never more.
Heading, velocity and camera are untouched.
*Time response (2026-09-20, owner: "Die Rotation bei Aufprall soll gedämpft beschleunigen"):* the constant-duration
smoothstep (its angular acceleration jumped to the maximum in the first frame) is replaced by a small spring-damper
state in `Island`: `_turnFeed` (impact angle not yet handed to the spring), `_turnError` (angle the spring still
has to turn) and `_turnRate` (yaw rate, public `BodyTurnRate`). The impact only adds to `_turnFeed`; a first-order
lag feeds it into a critically damped spring with the same pole, i.e. three equal real poles ω:
`done(t) = 1 − e^(−ωt)·(1 + ωt + (ωt)²/2)`. Yaw rate *and* angular acceleration start at zero, the rate swells to
its peak at `t = 2/ω` and then decays onto the target; overshoot or oscillation is impossible. ω follows the size:
`ω = 6.296 / BodyTurnSettleTime` (the 95 % point), settle time `lerp(bodyTurnTime 2.5 s, bodyTurnTimeHuge 5 s,
BodyTurnSluggishness)`, rate cap `BodyTurnRateCap = lerp(bodyTurnMaxRate 70°/s, bodyTurnMaxRateHuge 35°/s, …)`; the
sluggishness is 0 for a start-sized island (`bodyTurnRefArea` 24), 1 from `bodyTurnHugeArea` 2000 on and follows
`√Agility` in between (area 100 → 3.3 s, 300 → 4.0 s, 800 → 4.6 s). Each `Tick` integrates in 20 ms substeps
(at most 16, longer ones beyond 0.32 s) with the closed-form solution of the spring, so any `dt` is stable; the cap only ever lowers the rate,
and a huge substep that would carry the capped rate past the target lands on it. Below 0.02° and 0.1°/s the state
snaps onto the target (`IsBodyTurning` ends). Successive merges add to the feed, so the rate stays continuous and
the targets add up; the dosing rules above (cooldown weight, 120° cap, torque direction) are untouched because
they only read `BodyTurnRemaining` (= feed + error). `SetBodyYaw`, `Restore` and `ResetToStart` clear the state
(snap); `Capture`/`CapturePose` still save a turn in flight as finished; `bodyTurnTime ≤ 0` still snaps.
Measured (20 ms ticks): host 42 area, 90°: 50 / 90 / 95 / 99 % after 1.18 / 2.34 / 2.78 / 3.70 s, peak 55°/s,
14°/s after 0.2 s, peak acceleration 107°/s² reached after ≈ 0.25 s; 120° rear hit: 95 % after 2.8 s at the
66°/s cap; 262 area, 115°: 1.68 / 3.32 / 3.94 / 5.26 s, peak 50°/s (cap); 1261 area, 90°: 2.04 / 4.06 / 4.80 /
6.42 s, peak 32°/s; overshoot 0.000° everywhere, also with `dt` 0.25 / 1.5 / 10 s.
Measured with three guests rammed dead ahead (shallow overlap, no currents): Compactness 0.63/0.54/0.46
without the turn, 0.63/0.69/0.71 with it; bounding radius 8.6 → 6.7. `IslandSaveData.bodyYaw`
persists it for the player, overrides (pose-only too) and volcanoes; `yaw` stays the heading, old
saves read 0, a turn in flight is saved as finished. **Compactness incentive:**
`IslandShape.Compactness()` (4πA/P², 1 = disc) is recomputed with the stats; the player's sink rate
is multiplied by `lerp(elongatedSinkMultiplier 1.6, 1, Compactness)` and the HUD shows "Form xx %"
(warning colour below 50 %). `impactDrag` is 0.5 (no dead stop). Collision feedback is subtle: camera
shake 0.12, no slow-mo, water ring at half strength, uplift rises in ~1.7 s with an ease-out. **Merges conserve
volume and never invent land** (user decision): where the bodies overlap (≈10 % of the smaller island
from the drive-in) the overlapped land stacks (`max + min`), there is no bridge fill, and the ridge
(`H = 0.2·√E·(1+0.25·conv)` capped at `0.8 + 0.25·minR`, broad Gaussian `w = 0.7·minR+1`,
`L = 1.6·minR+1.8`, ridged noise 20 %) takes its volume from the whole island: `upVolume/landArea` is
shaved off every land cell inside the uplift array, so the shore creeps inward — higher = less wide.
Measured: 23.8 + 24.0 area → 41.0 (−14 %), volume 29.6 → 31.7, peak 1.5.
Merge re-basing: only the guest's shelf/land gets the host's sink offset; its flat sea floor stays the
`Sea` sentinel (otherwise a deep-sunk host turned the guest's whole grid into a rectangular plateau).

**Input mode (`Drift.UI.InputMode`).** A PC with a touch panel reports `Touchscreen.current`, so
"has a touchscreen" is not "plays by touch": `InputMode.TouchPreferred` follows the last used device
(touch press → true; key, mouse button or wheel → false; phones start true) and drives the HUD hint,
the Anleitung's control order, the tutorial texts and `TouchControls.Available`.

## Sinking = the way to lose

The player's island slowly sinks (`IslandShape.sink`, subtracted from all heights): the waterline
climbs, so land area shrinks, vegetation and herds relocate or die, and when `LandArea < sunkArea` the
island is `Sunk` (game over). The loop is: sink, hunt the next island, merge, refloat. AI islands never sink.

**Sunk means sunk (2026-09-20, owner: "man soll erst verlieren, wenn wirklich kein Teil der Insel über Wasser
ist").** Two things used to end a run early. `sunkArea` was 3 area units, i.e. 12 % of the start island, and
`Buoyancy^sinkLandExponent` falls off a cliff near zero, so the last ~13 % of the land went under between two
0.5 s mesh refreshes: the player saw a whole island and then the game-over screen. Now `sunkArea` is 0.2 (below
one 0.25-area cell, so the run ends only when no cell is above water) and `maxLandLossPerSecond` (0.05) caps how
fast the waterline may swallow land — the depth is `DepthAt(max(shareFromTimer, lastShare - rate·dt))`, which
also keeps the island sinking *after* the timer is empty until the last hilltop is gone. Measured on the start
island: the bar empties at t = 84.5 s with 12 % of the land left, the last land goes under at t = 87.0 s, and
the loss fires at area 0.00. The bar showing empty for the final seconds is intended — it is the "you are going
under now" moment, not a stuck bar.
`GameSession` deletes the save on game over and restarts a fresh world after `restartDelay`. There is
deliberately no offline sinking: closing the app never costs you the island; the tutorial, photo and follow
holds (`sinkEnabled`) work as before.

**Size makes it hard (2026-09-20, owner: "deutlich schneller sinken, wenn sie größer ist … nicht leicht, eine
große Insel lange zu halten").** The old rule (`0.012 · (area/30)^0.35` depth units per second, refloat
`0.04 · guestArea` depth units) made big islands *easier*: merged islands are tall (measured peak 2.2 u at 39
area, 5.4 u at 204, 8 u at 442, against 0.88 u for the start island), so a 440-area island needed ≈ 200 s to go
under and any 50-area guest refloated it completely. Now the state is the **buoyancy** (`Island.Buoyancy`, 1 =
afloat, 0 = sunk; for the player it is the share of the sink time that is left, so the HUD bar is a timer that
drains linearly; other islands still report their land share, which is also available as `LandFraction`):
- *Designed curve* `Island.SinkSecondsForArea(area)` = `SinkBalance.SecondsForArea` =
  `sinkSecondsHuge + (sinkSecondsSmall − sinkSecondsHuge) · sinkHalfArea / (sinkHalfArea + area)` with 95 s / 10 s /
  250: seconds from full buoyancy to sunk for a round island in calm water, evaluated on the full-buoyancy area
  (`FullArea`, so it does not slow down while the land shrinks). 25 → 87 s, 100 → 71 s, 300 → 49 s, 800 → 30 s,
  2000 → 19 s, ∞ → 10 s. The form and storm multipliers stay (`SinkMultiplier` =
  `lerp(1, stormSinkMultiplier 1.6, storm) · lerp(elongatedSinkMultiplier 1.6, 1, Compactness)`); with the typical
  merged form of 65–75 % (×1.15–1.2) the player feels ≈ 75 / 60 / 41 / 25 / 16 s. The start island (form 87 %)
  takes 80.5 s (before: 78.5 s). Land lost per second grows faster than the area (0.3 → 1.4 → 6 → 27 → 100 area/s).
- *Depth from the terrain:* `IslandHypsometry` (256 height classes over the raw land, built only when the raw
  heights change: generate, restore, merge, each uplift frame — 0.8 ms for a 77k-cell grid — never while sinking)
  gives the depth at which a given share of the land is still above water, and its exact inverse.
  `AdvanceSink`: `buoyancy −= dt · SinkMultiplier / SinkSecondsForArea(FullArea)`, `sink = DepthAt(buoyancy ^
  sinkLandExponent 0.4)`. So the timing holds whatever the terrain (a volcano cone or a merge ridge buys no extra
  time any more) and the exponent keeps the old look: the shore goes first, most land stays until late (start
  island: land 95 / 89 / 83 / 77 / 71 / 62 / 47 / 16 % after 10 … 80 s while the bar reads 88 … 1 %). The saved
  state is still `sink`; after `Restore` the buoyancy is read back from it (`LandFractionAt(sink) ^ (1/0.4)`), so
  old saves load unchanged.
- *Refloat is relative to the host:* a guest gives back `RefloatFactor · (guestArea / hostFullArea) ^
  refloatExponent 0.55` of the full buoyancy (`SinkBalance.RefloatShare`, `Island.RefloatShareFor`,
  `LastRefloat`), clamped at full: 1 % of your size → 8 %, 3 % → 15 %, 10 % → 28 %, 30 % → 52 %, 50 % → 68 %,
  equal or bigger → 100 %; volcanic guests ×1.6 (10 % → 45 %), barren ×0.7. A 13-area islet gives the start
  island 71 %, a 100-area island 33 %, a 1000-area continent 9 %. `MergeFrom` adds the share to the buoyancy,
  takes the new depth from the host's curve (the guest is re-based onto it, as before) and then keeps the
  *buoyancy* through the merge and its uplift while the depth follows the changing terrain
  (`RebuildHypsometry(true)`; a fixed depth silently lost up to 0.2 buoyancy to the ridge erosion).
- `SinkSecondsLeft` (estimate at the current rate; ∞ while sinking is held and for AI islands), `SinkSecondsFull`
  and `SinkMultiplier` are there for the HUD.
- *Scripted runs* (`Tests/IslandSinkBalanceTests.cs`, `SinkRunSimulator`: a pure-number player on the real world
  plan — 166 islands, 5332 area, median 13 — travelling at 50–80 % of `MaxSpeed` with 2.5–7.5 s overhead per leg,
  form ×1.2, 12 % area lost per merge). Old rule: 14–51 min, peak 700–3500 area, up to 29 min above 1000. New
  rule: always the nearest island, however tiny: 2.2–2.6 min, peak 165–232; best refloat per second: 2.9–3.2 min,
  peak 365–624; cautious (the smallest island that keeps the bar above 75 %): 6.9–8.6 min, peak 298–584; nobody
  reaches 1000. Growth is what ends a run, so eating small to stay small is a real strategy. A softer setting if
  wanted: `sinkSecondsHuge` 18, `sinkHalfArea` 500 (felt 80 / 72 / 58 / 41 / 29 s) → peaks up to ≈ 770, runs 3–12 min.
The chase camera zooms with mouse wheel / Q / E (0.08x to 3x, exponential), for watching herds up close;
pinch comes with the Android controls. Above `CloseZoomStart` 0.4 the framing is the classic one; below
it `IslandChaseCamera.CloseBlend` (smoothstep to 1 at 0.08) fades the island-size scale to 1 (a close-up
is equally close on a continent), halves height and distance (0.77 u from the focus at a 26° pitch, a
0.1 u animal ≈ 11 % of the screen height), raises the focus onto the ground under the island centre,
lowers the terrain clearance from `groundClearance` 0.9 to `closeGroundClearance` 0.22 with two extra
sight-line samples, eases `Camera.nearClipPlane` from 0.3 to 0.05 (back to 0.3 when zoomed out or
`Suspended`) and carries the camera along with the focus, because the follow lag (speed / `followLerp`
≈ 1.2 u at full speed) would otherwise exceed the camera distance while driving. The camera always
sits behind the heading, never behind the turning body.
`Drift.Bridge.WorldHud` is a plain uGUI HUD (land mass, world progress, buoyancy bar with sinking
warning, wrap-aware minimap; hidden outside Playing/Paused). Title, pause and game-over screens are
`SessionScreens` (see Session below). The TMP HUD framework in `Drift.UI` is not wired into the scene.

## Life persistence, lightning fires (2026-09-20)

Plants and herds persist. Succession only grows for bushes and trees (they die only by fire or
drowning); grass and flowers are generated *and* removed with the stage — grass peaks in plains/shrub
and is gone by mid-woods, flowers by shrub — fading out over ~10 s (`CanopyFadeRate`) as the canopy
replaces them. Fires start only from lightning: while `IIslandSurface.StormIntensity > 0`,
`IslandLifeSystem` rolls strikes at `lightningRate` (0.035/s at full storm, ∝ area/50) that show a
0.3 s bolt marker in the vegetation mesh and ignite burnable cells.
Herds: `round(area/7)` per island (min 1 above area 4, cap 40 herds / 120 animals), species by weighted
roll (hares always, sheep ≥15, goats ≥30, oxen ≥50; caps 16/14/10/8); herds spawn and wander ≥
`herdSpacing` (1.6) apart and members sit in a constant-density cluster (member k at ~2.2·body·√k), so
a herd reads as one group. Herds are only added when the island grows past its previous peak area,
members only disappear when their ground drowns (they steer uphill first, `shoreMargin`), and grow
slowly on old growth. `Repopulate()` is a no-op for an unchanged `Version`; sink bumps refresh the grid
in place, merges re-grid with plants/herds carried over. Plants draw at `plantScale` 0.55 (`cellSize`
1.2), animals at `animalScale` 0.175 with 60–78-vert body+head templates (~7.5k herd verts on a
170-area island); walking speeds 0.175–0.5 u/s.
Phase 5 flora: palms (≤ 24, below every third shore cell on the 0.05–0.35 beach) and reeds (≤ 40, at the
waterline) follow the tree rule; flowers have 6 colours × 3 shapes per cell hash and bloom in 4-min waves;
fire scars stay charcoal for 2 real minutes while small bright shoots come up; trees grow from 0.3 to full size
over 240 life-seconds; a 20-min colour season tints ground and plants and is saved as `LifeSaveData.season`
(old files → 0, new islands copy the running season). Per-plant growth, bloom and shoot state is not saved (a
restore rebuilds mature plants from the cell grid, as before). Vegetation stays under 60k verts per island by
thinning density on big islands rather than removing plants. Wind sway is in the vertex shader (`_LifeWind`).
Critters (Phase 4, `IslandCrittersSystem`, one "Critters" mesh per island, `Drift/Critter`): crabs one per 4 u
of shoreline (≤ 12, shore band 0.02–0.25, 0.35 u/s sideways, dive above island `Speed` 1.5 u/s), turtles
only above area 20 (≤ 3, 0.08 u/s between the shore and height 0.55, rest 20–60 s), both saved in
`LifeSaveData.critters`; butterflies (≤ 16, one per 3 flowers, by day, flower to flower within 3 u)
are transient and near-tier only. **Fireflies** glow on every island at night, in every tier (see „Fireflies on
every island" under Phase 4): ≤ 48 per island, one per 2.5 area, at least 12, never saved.
Seabirds: `FlockSystem.seabirdFlocks` 2 flocks of 2–3 gliding white/grey birds circling cliff islands (Barren,
Volcanic or peak > 1.7) without ever landing.

## Settlements (`Drift.Life.IslandSettlementSystem`, `SettlementMeshes`, 2026-09-20)

Island folk ("Insulaner") settle big, mature islands and build up step by step. Pure decoration: nothing here
touches island physics, sinking or the player's success, and nobody ever dies (folk whose homes are gone walk off).
One `[ExecuteAlways]` component per island next to `IslandLifeSystem` (same pattern as the critters: finds
`IIslandSurface` / the life system on its GameObject, `Repopulate()` idempotent per shape `Version`, time in **real
seconds**). **Wiring:** `WorldStreamer.Spawn` / `VolcanoSpawner.Create` have to `AddComponent<IslandSettlementSystem>().seed
= shapeSeed`, the scene's player `/Island` needs the component with `prehistory = false`; `Island.NotifyShapeGenerated` and
`IslandSaveUtil.ApplySaved` should call `Repopulate()` like they do for the critters.

- **Stages** (`SettlementStage`, German names via `StageName`): none „Unbesiedelt" → **Lager** (area ≥ 35 and life stage ≥
  woods on ≥ 30 % of the land cells for `foundingDelay` 90 s: campfire, 2 tents, 2–4 folk) → **Weiler** (area ≥ 45, 240 s as a
  camp: tents are rebuilt as 5 thatched huts, well, 2 fenced gardens, jetty with rowing boat) → **Dorf** (≥ 70, 480 s: + 6
  half-timbered houses, hall with flag, windmill, lighthouse on the highest firm shore ground, third garden) → **Stadt**
  (≥ 110, 720 s: huts rebuilt as stone houses, 8 houses, market square, second jetty, 3 terraced fields turned uphill, shrine
  on the highest ground within 7 u). A stage needs 70 % of the current plan finished and is never lost. Plans are cumulative
  build orders (`Plans`); 1 / 2 / 2 / 3 sites at once; build times 30–300 s per building (`BuildingSpec.buildTime` ×
  `buildTimeScale` 0.8), so camp → town is about 50 real minutes. Caps per island, also after merges: 32 roofed structures,
  6 plots, 3 villages, 24 folk (4 / 8 / 16 / 24 per stage, 2 + the capacity of the finished homes). Since the small houses
  (below) the village plan has 7 huts and 8 houses, the town 12 houses, 7 stone houses and a second well (29 structures).
- **Scale (2026-09-20, owner: „Häuser höchstens halb so groß, eher ein Drittel"):** `buildingScale` 1.5 → **0.55** (hut 0.14 u
  tall, house 0.18, hall 0.23, windmill 0.25 under 1 u trees), `settlerScale` 1.3 → **0.75** (0.049 u = door height, a good
  quarter of a house). `ScaleOf(kind)` is what a kind is drawn, spaced and blocked with: the lighthouse × `lighthouseScale`
  1.4 (0.54 u, still the landmark), the jetty and its boat × `dockScale` 1.25 (deck 0.41 u long, 0.05 above the water).
  Everything that was an absolute distance tuned at 1.5 follows through `Unit` = scale / 1.5 (free-spot margin, garden gap,
  door and site jitters, arrival distance, fire and party rings) or was re-tuned: `settlerSpeed` 0.12 → 0.06,
  `beachMargin` 0.5 → 0.35, village radius + 1 (was + 2), woods point 0.4 … radius + 1, shrine 1.2–5 u from the hearth,
  lighthouse 0.25–0.75 behind the shore, jetties 2 u apart; `siteGap` 0.06 of free ground round every footprint and
  `villageSpread` 1.35 on the search reach give lanes between the small houses, so a town reads as a town and not as
  one blob from the chase camera (a full town spans ≈ 3 u). `Blocks` = 0.8 × radius × `ScaleOf` (≈ 0.1 u for a hut);
  `TryGetDockWorld` keeps its contract (deck end, deck height, sea direction). Windows are modelled 1.45 × oversized
  (`SettlementMeshes.WindowGrow`). A save carries `SettlementSaveData.scale`; a file without it (scale 1.5) is drawn
  together round each hearth on load (`Rescale`: positions × ratio where the ground carries them, shore buildings stay).
- **Night halos:** a third child "SettlementGlow" (the fireflies' additive `SharedGlowMaterial`, one draw call, only once a
  settled island has seen a night) is baked with the static mesh: one halo per lit building at window height
  (`windowHalo` 0.85 × footprint, never smaller on screen than `windowMinAngle` 0.0026), the lighthouse lamp
  (`lampMinAngle` 0.0055), the campfire plus a warm ground blob. No per-frame cost.
- **Ground rules:** height 0.3–1.5, slope ≤ 0.35 across the footprint, a ring of 8 probes at 0.5 u all above 0.25 (keeps
  houses off the beach and the rim), no overlap (tents and huts reserve the room of the stone house they become), nearest
  free spot to the hearth with a slight uphill preference; windmill = highest candidate, docks = nearest gentle shore in 16
  marched directions with deep water ahead (placed at water level, local +z out to sea, `TryGetDockWorld` for ships),
  lighthouse = highest firm ground 0.3–0.9 u behind the shore.
- **Construction is visible:** foundation slab scales in (progress 0–0.08) → scaffold frame rises out of the ground (0.12–0.5) →
  the finished body rises inside it (0.5–0.92) → the scaffold shrinks away; an upgrade sinks the old body while the new one
  rises. Plots, campfire, jetty and market just grow in. The static mesh is re-baked per 2 % progress.
- **Hazards, cozy:** ground below `drownHeight` 0.05 after a shape change → the building tilts and sinks away over 5 s and
  the plan rebuilds it elsewhere; a village whose hearth drowned recentres on its highest building or fresh valid ground,
  else it goes **dormant** (keeps its stage, no folk) until a later shape change offers a site (given up below area 8). A
  wooden building whose life cell burns (`BurnAt` = 1) burns for 6 s, stands as a charred frame for `rebuildDelay` 120 s and
  is then a building site again (stone: well, lighthouse, stone house, shrine, plots never burn). Storms (`StormIntensity` >
  0.35) send everybody indoors.
- **Folk** (near tier every frame, mid tier every 0.25 s, far tier not at all): carry logs from the woods or the jetty to a
  site and hammer there, chop by the trees outside the clearing, farm in gardens and fields, fish from the jetty end (one per
  jetty, walking out over its shore end), stroll between doors; above `gatherNight` 0.5 they sit round the fire (warm tint),
  above `sleepNight` 0.85 they go home and disappear, 60 % of the windows go dark; they wave when `LifeLod.Distance` of the
  settler is < 3 u; after `AbsorbFrom` everybody runs towards the point between the villages and cheers for 30 s.
- **Rendering:** two `DontSave` children with `LifeMeshes.Material` (Drift/VertexColor, so cloud shadows and the curve come
  for free), both baked with `TemplateBatch`: **"Settlement"** = buildings (rebuilt only on a construction step, a shape
  change, a fire/sink step or a night step of 0.08; windows and lamps are a separate glow template blended towards an HDR
  warm colour with `NightAmount`; the hall flag sways through the wind channel) and **"SettlementFolk"** = settlers (33–39
  verts), logs, rods, windmill sails (rolled by `LifeEnvironment.Wind`), bobbing boats, flame and smoke, the lighthouse beam
  (≤ 10 Hz near, 3.3 Hz mid). In the far tier the static mesh carries sails and boats at rest and the folk mesh is empty; it
  refreshes at most every 10 s. Unsettled islands create no child objects. Templates: 39–186 verts per building, a town is
  ≈ 4.2k + 1.1k verts (+ ≈ 120 glow verts at night; budget 12k).
- **Life hooks** (the only lines in `IslandLifeSystem`): `LifeSaveData.settlement`, the `Settlement` property the component
  sets on itself, the forwards in `ShiftLocal` / `AbsorbFrom` / `Capture` / `Restore` / `Simulate` (offline catch-up advances
  construction), and the **clearing**: a life cell under or next to a building (`Clears`: cell centre within half a cell +
  radius × scale × `clearReach` 1 + `clearMargin` 0.12 of it) wants no bushes or trees (they fade like under a canopy and
  come back as saplings when the building is gone), keeps one grass tuft and takes the village-green ground tint. With
  the small houses that is on average less than one 1.2 u cell per building. Docks, lighthouses and shrines clear nothing.
- **Save:** `SettlementSaveData` (villages: stage, centre, ages; buildings: kind, variant, state, village, upgrade source,
  x, z, yaw, progress, timer, burn; folk count). Folk are derived from the homes on load. A missing or empty block (every
  older file) loads as an **unsettled** island — no history is invented for a saved island.
- **Prehistory:** a streamed island (`prehistory` true, never a volcano) above the founding area rolls a seeded age of up to
  `prehistoryMax` 5400 s (20 % stay unsettled) and fast-forwards it in `Repopulate` with the fertile height band standing in for
  the mature-ground test, so the world already has camps, hamlets, villages and the odd town. Cost ≤ 1.5 ms for a town.
- **Cost (desktop):** near step 0.003 (camp) – 0.029 ms (29-structure town, incl. the 10 Hz folk mesh), mid 0.002, far
  0.0001 ms; static rebuild 0.10 ms incl. the night halos, folk rebuild 0.032 ms for a town; town prehistory 1.6 ms; 0 B
  per step. 16 EditMode tests in `Tests/SettlementTests.cs`.
- Not done: path tint between buildings, herds walking round houses (`Blocks(local)` is there for `IslandHerdSystem`),
  journal/tap entries, chimney smoke.

## Island character, storms, animal reactions

`Island.kind` (`IslandKind`: Regular / Volcanic / Ancient / Barren) is derived in `GenerateShape`:
volcanoes → Volcanic, the player → Regular, otherwise `Island.KindForSeed` (~15 % Ancient, ~15 %
Barren). `IIslandSurface.Character` mirrors it into `Drift.Life` without a dependency. Effects:
Volcanic refloats ×1.6 (`RefloatFactor`), sparse-but-fertile vegetation, dark rock tint; Ancient
starts as old growth with denser/larger herds; Barren is generated flatter and 0.8× smaller
(`barrenRadiusScale/HeightScale`), fertility ×0.4, pale tint, refloat ×0.7.
`Drift.Tectonics.StormSystem` turns `Slip` plate events into storms (radius 35, ~25 s, max 3,
~90 s cooldown): `IntensityAt`, `GustAt`; islands ride the gust (added to the carry), the player
sinks 1.6× faster inside (`stormSinkMultiplier`), herds huddle (`IslandHerdSystem.Agitation`),
flocks steer away and abort landings. Herds `Startle` from the contact point for ~3 s on a merge
(guest herds too); flocks have Cruise/Orbit/Descend/Perched/TakeOff states and land on islands
with area > 60 (wing flap alpha 0 while perched). Herd meshes rebuild only when animals moved or changed state, ≤ 15 Hz near / 7.5 Hz mid / not at all far (see Phase 1 and Phase 2 under "Mobile performance"); the idle states (graze, look, rest, sleep, play) are described there.

## Audio (`Drift.Audio`, fully procedural — there are no audio assets)

`MusicSynth`: generative C-major-pentatonic music (bass drone, 8 pad voices, 8 pluck voices,
C–Am–Dsus–G, stereo feedback delay). `Tension` 0..1 drives BPM 56→112, density, brightness and
volume; above `BuildThreshold` (0.72 ≈ 2.2 s to contact) a rising 16th-note arpeggio; `Impact()`
plays a resolving low chord (blooms over ≈ 0.25 s so the rock speaks first, level 0.34) + a short, quiet
noise swell and eases back over 4 s. `SfxSynth`: wind (gusting filtered noise, rises with speed), water wash
(level from speed), the rock impact (see "Impact = crunching mountains" below), the one-shots
`ShipBeached` / `BigSplash` / `WhaleBlow`, sinking warning pulse below 0.35 buoyancy. `TensionTracker.Compute` estimates
time-to-contact with the nearest island ahead (gap / closing speed, 8 s horizon). `AudioDirector`
("Audio" GameObject) owns three hidden AudioSources ("Music", "Sfx", "Life", created at runtime) rendered
via `OnAudioFilterRead` (falls back to a streamed `AudioClip.Create` if the filter never runs); music
~0.8–1.7 % of a core, zero GC. Public knobs: `musicVolume`, `sfxVolume`, `lifeVolume` (0.5), `musicEnabled`,
`sfxEnabled`, `lifeEnabled`. Main Camera has the AudioListener.
**Life layer (Phase 6, `LifeSynth`).** The habitat under the music: songbirds (3 singers, 2–5-note motifs over
a pentatonic-ish degree table with Markov-ish steps, exponential per-note glides of ±3 semitones, a half-sine
note window and a 30–120 Hz FM trill on 40 % of the motifs; rests 8 s → 2 s with density, singer k joins above
density k/3), seabird cries (0.5–0.9 s: a short rise, then an exponential fall to 45 % of 1.5–2.1 kHz with 2nd/3rd
harmonics and a 28 Hz rasp, every 6–16 s), crickets (two layers, 4200 / 4340 Hz carriers under 31 / 27 Hz squared
half-sine pulse trains, gated in 0.25–0.75 s chirps with density-shortened gaps, 0.08 Hz level LFO; layer 2 above
density 0.4), leaf rustle (two independent white noises through a two-pole low-pass at 1.4–2.5 kHz that opens
with the gust and an 800 Hz high-pass; slow gust from two sines, random flurries with a 0.3 s rise / 1.2 s fall),
shore lapping (noise through a 1.1 kHz two-pole low-pass and 250 Hz high-pass under a smoothstep-rise /
0.45 s-decay envelope every 1.4–2.6 s), frog bloops at dusk (90–160 ms exponential drops from 520–780 Hz to ≈ 45 %,
35 % doubled, every 1.5–6 s) and rare sheep (0.5–0.9 s, 230–330 Hz, 8–11 Hz tremolo, 850–1050 Hz formant, every
15–40 s) and ox calls (1.0–1.6 s, 95–125 Hz falling 12 %, 330–410 Hz formant, every 30–70 s) sharing one voice.
Slow parameters (glides of the long calls, envelopes, filter cutoffs) are evaluated once per 64-sample block and
ramped per sample; idle voices cost nothing; the sum is soft-clipped. The audio thread only reads eight density
floats (`BirdDensity`, `SeabirdAmount`, `CricketDensity`, `RustleAmount`, `LapAmount`, `FrogAmount`, `SheepAmount`,
`OxAmount`, each smoothed with τ 0.8 s) plus `Duck`. The main thread fills them every `lifeUpdateInterval` (0.5 s):
`LifeSoundScout.Evaluate` walks `Island.All` for islands whose edge is within `lifeRange` 40 u of the player (the
player's own island at distance 0) and reads `IslandLifeSystem.GetHudStats` (woods + old growth → birds, plains +
shrub → crickets), `CountOf(Tree/Palm)` (rustle), `CountOf(Reed)` (frogs), `IslandHerdSystem.HerdKind` (sheep/ox)
and `SleepingFraction` (calls only while awake), `IslandCrittersSystem.FireflyCount`, and the `FlockSystem` flocks by
position (`BirdCountOf`, `IsSeabird`), each weighted by `LifeSoundMix.Proximity`; `LifeSoundMix.Apply` maps the
counts through the saturating `Density(count, full)` = 1 − e^(−2·count/full) and gates by `LifeEnvironment`:
birds and seabirds by day (night < 0.55, fading over 0.3), crickets above night 0.35, frogs in the dusk window
0.2–0.8, rustle by wind speed and storm, lapping by 1 − 1.4·speed, birds and crickets −70 % in a storm. The layer
ducks (`DuckDepth` 0.5, attack 0.25 s, release 1.2 s) for `lifeDuckImpactSeconds` 3 s after an impact and above
`lifeDuckTension` 0.7. `Drift.Audio` now references `Drift.Life`; `Drift.Tests` references `Drift.Audio`.
**Impact = crunching mountains (2026-09-20, owner: "mehr ein Crunchen der Gebirge als gerade").** The old impact was
a 45→28 Hz sine sweep — a straight tone. `SfxSynth.Impact(intensity, duration)` now builds a rock collision from five
layers on one "bus" that costs nothing while silent (`BusActive`): **(a) body hit** — a 170–230 Hz noise burst
(τ 50–90 ms) plus a fixed 48–60 Hz sub bump (τ 60–95 ms, no sweep), level 0.15; **(b) grind** — the main character:
a pool of 32 two-pole resonator grains, each rung by a 0.3–1.5 ms noise burst, centre 330–3500 Hz (log-uniform, the top
follows the brightness envelope), 3–28 cycles to −60 dB (Q 1.4–13, clamped 3–60 ms: low grains thud, high ones tick,
nothing rings like glass), Pareto amplitudes (0.11·u^−0.75, ≈ 5 % reach full level and read as cracks), random pan
pushed towards the edges, density (100 + 400·I)/s times a stick-slip value (re-drawn every 30–150 ms, 0.1–1.9) so it
surges instead of hissing, plus a quiet decorrelated stereo friction bed (noise band around 500–1400 Hz); envelope:
0.3 → 1 over 0.4 s, holds for `duration`, then e^(−t/0.5 s) (gone after ≈ 1.5 s); **(c) cracks** — 3 + 5·I (3–8)
snaps in the first second (clustered early): 800–2500 Hz, 20–60 ms, gliding down 8–18 %, each with two inharmonic
partials (×1.53, ×2.31) and a 3 ms click so they read as rock, not as a ping; **(d) rumble** — brown-ish noise
through 45–120 Hz (two high-pass poles, so nothing piles up below 40 Hz) with a 2.6/4.1 Hz tremolo, swelling over
0.55·duration, and a 150–300 Hz body band for phone speakers, both mono/centred; **(e) debris** — pebble ticks
(1.2–3.3 kHz, 3–9 ms, 25 → 0 /s) and 1–5 water plops (260–540 Hz chirping up ×1.6–2.2) over 2 + I seconds.
Intensity scales density, crack count, rumble, level and length (`DefaultImpactSeconds` = 1 + 1.2·I when no duration
is given); every event draws fresh randoms (unseeded synths seed from the clock). The bus is soft-clipped on its own
(< 0.85) before the final soft-clip, so impact + wind + water stays ≤ 0.9. `GrindAmount` (0..1, held) runs the grind
and rumble continuously: `AudioDirector.UpdateGrind` polls at 10 Hz — only while `IslandWorld.ActiveContacts > 0` —
whether the player's island touches a neighbour (`DetectContact`) and drives it from the closing speed, so the
drive-in phase (≤ 2 s before the merge) already grinds. `AudioDirector` parks the intensity from `Island.Impact`,
then acts on `Island.Merged`: merges the player is not part of fade with distance (`impactHearingRange` 70 u, below
0.02 they are skipped, music included) and the grind lasts `EstimateMergeSeconds` = `upliftDuration` + 0.2·ridge +
0.8·I. One-shots for other systems: `TriggerShipBeached(intensity[, planarPos])` (two wooden stick-slip creaks — a
pulse train with dropped cycles through a 760/950 Hz formant — a 0.45–0.75 s sand scrape of 330 tiny grains/s and a
small splash), `TriggerBigSplash(intensity[, planarPos])` (body 850→420 Hz + spray 2.4–2.9 kHz noise, sub thud above
0.45, plops, droplets), `TriggerWhaleBlow([planarPos])` (airy 1.9→1.15 kHz hiss with a 520 Hz breath, then droplets),
`SetGrind(amount)`, `TriggerImpact(intensity, duration)`. Rumble and body band run at half the sample rate, the bed is a
difference of two one-poles per ear. Cost on top of wind + water (0.26–0.3 % of a core): +0.39 % in the Editor during
the densest grind of a continental impact (+0.24 % under the same Mono outside the Editor, +0.07 % under .NET 8),
+0.26 % in its tail, 0 when idle, zero allocations; `Tests/ImpactAudioTests.cs` holds the numeric checks and the
analysis helpers (FFT, 1/3-octave shares, onset counter, WAV writer).

## Water, fish, wind & clouds (`Drift.Visuals`, 2026-09-20)

`Drift/Water` is one transparent pass: five sharpened sines plus a wind-aligned storm chop give the
normal, three short sines add ripple detail, specular is broad and sun-based, fresnel tints toward
`_SkyColor`. Shallow turquoise, shore foam (surging, noise-broken) and caustic sparkle come from the
camera depth texture (works in the Game view: `_DepthRange` 1.3 follows the real beach outline); the
analytic distance to the 8 nearest islands (`_IslandData`, bounding radius, pushed by `WaterFeedback`)
only feeds the surf foam, never the tint — as a tint fallback it produced a saturated turquoise disc
3× the island because the bounding radius is far wider than the beach. `WaterFeedback` also pushes
`_Storm` (smoothed `StormSystem.IntensityAt(player)`), `_WindDir` (rotating global wind + 0.6·plate
current + 0.4·gust) and `_SplashPos`. Wind streaks are wide, soft, low-contrast noise stretched along
the wind (`currentStreakStrength` 0.35: barely visible calm, clear in storms); storms multiply a grey
tint (composes with the day/night colours), raise wave amplitude/frequency and add whitecaps.
`CloudShadows` sets global `_CloudCover/_CloudOffset/_CloudScale/_CloudShadowStrength`;
`Shaders/DriftClouds.hlsl` is included by the water, island terrain, vertex-colour and fish shaders so
one drifting cloud field darkens sea, land and life together; cover rises with storms and drifts with
the same wind vector as the streaks. `DayNightCycle` multiplies a storm dim/desaturation onto its
time-of-day result. `FishSystem` runs 2–6 schools of 8–20 fish within 30 u (seeded from a wrapped
12 u grid so they reappear in place), hugging island shelves via `SampleHeight`, scattering from the
moving player, jumping with a splash ring; all fish are one 15 Hz mesh (`Drift/Fish`, drawn after
the water with underwater tint), ~0.01 ms/step, 0 GC. The URP `ScreenSpaceAmbientOcclusion` feature
on `PC_Renderer` is disabled (dark halo under islands, expensive on phones). `IslandTerrain.mat`
`_RockStart` is 2.0 so the flatter merge ridges stay green up to the tree line.

## The sea (`Drift.Visuals.SeaLifeSystem`, `ShipSystem`, `SeaMesh`, `Drift.Core.SeaMath`, 2026-09-20)

Owner: "Das Meer ist insgesamt zu leer." Everything below is procedural low-poly vertex colour, lives only
around the player, is capped, costs 0 B/frame and adds **three draw calls**: `SeaLifeUnder` (`Drift/Fish`,
alpha-blended after the water with the underwater tint), `SeaLifeAbove` and `SeaShips` (`Drift/VertexColor`,
two-sided). The water material is opaque (`_MinAlpha` 1), so an opaque mesh below y = 0 only shows up as a
turquoise depth patch — submerged bodies therefore always go into the `Drift/Fish` mesh and only what breaks
the surface into the opaque one (where the depth foam draws a free waterline around hulls and jumping dolphins).
All three systems share the pattern of `FishSystem`: a wrapped world grid (`SeaMath.CellSeed`, cell sizes that
divide the 660 u torus: 30 u sea life, 60 u ships, 22 u flotsam) decides what a cell holds, so the same cell gives
the same animals on every copy of the world; a fixed pool is filled when a cell's point comes within
`spawnRadius + player radius` (90 / 100 / 70 u), things scale in over `fadeBand` (`SeaMath.EdgeFade`) and are
recycled beyond `recycleRadius` (108 / 120 / 84 u). A cell whose inhabitants wandered off stays empty until the
player has left it or `respawnSeconds` (90 s) passed, and after the first scan new arrivals only appear beyond
half the range, so nothing pops up next to the island. `SeaObstacles` collects `Island.All` near the player as
circles (slot 0 = the player) and `SeaMath.SteerAvoid` bends every heading around them; `SeaObstacles.Shallow`
samples the real shelf (`Island.SampleHeight(ToLocal)`) where the bounding circle is too coarse.
`SeaShape` builds flat-shaded templates once (loft through elliptical rings, hull, prism, sheets), `SeaBatch`
copies them into preallocated arrays (hard vertex caps 2000 + 1600 + 2400 = 6000; overflow is dropped) and both
systems rebuild at 15 Hz. "Emissive" is a vertex colour far above 1 with the night light's hue divided back out,
because both shaders multiply by the main light.
**FishSystem** now runs up to 10 schools within 45 u in three species (silver sardines, gold reef fish, 3–7
large blue mackerel swimming deeper) and at most one **bait ball** (44 sardines swirling in a 1.7 u ball in open
water, `TryGetBaitBall`, `IsBaitBall`, `SchoolSpecies`). **SeaLifeSystem** (`Kind`): dolphin pods of 3–6
(`DolphinCount`; roam → bow-ride when the player moves within 40 u: they sprint to a point `radius + 5` ahead and
leap in sequence every 2–4 s for ~20 s, then peel away for 25 s; circle a resting player curiously; jumps are an
arc with pitch, a splash ring and spray on re-entry), whales (`WhaleVisible`: deep silhouette → three
breaths with a rising back and a spout of drifting white octahedra → dive with the fluke raised and slapped down,
big ring + spray; pods and solitary bulls, see below), sea turtles swimming between islands and lingering offshore,
jellyfish swarms (6–10, pulsing; at night they glow with a soft halo disc, in storms they sink and dim), rays
gliding in arcs and fleeing the moving player, rafts of resting gulls that paddle away flapping, seaweed patches,
flying-fish bursts by day in calm weather, five gulls circling and plunge-diving over the bait ball, and warm
pools of light under lit ship lanterns. Accessors for a journal / tap layer: `PositionsOf(kind, buffer)`,
`GroupKind/Position/State`, counts, `SeaNames.German(SeaKind)`. **ShipSystem**: sailing boats (mast, main + jib
that swing to leeward and heel with `WaterFeedback.Wind`), fishing boats (cabin, gaff sail; sail to the nearest
fish school, preferring the bait ball, and drift there 25–45 s), rowing boats hugging the true shelf of their
home island (oars animate, moored with a lantern at night or in storms), and one rare trading cog that only
spawns ≥ 45 u out, crosses on a fixed course and keeps its distance. Destinations are islands inside the visible
range; a ship moors at `Drift.Core.IDockProvider.TryGetDockWorld` when the island GameObject has one (bow to the
jetty), otherwise it anchors where the shelf rises above `anchorDepth`; moored boats ride with their island.
`SeaMath.SailsDown` (storm > 0.35, up again < 0.2, from `StormSystem.IntensityAt` at the ship) furls the sails
and cuts speed to 30 %, `SeaMath.LanternOn` (night > 0.45 / < 0.35) lights the lantern. Hulls heave, pitch and roll
on `SeaMath.Swell` (the water shader's two main waves) and leave two tapering foam ribbons. Ships never block
the player: they steer away at `radius + playerMargin` and never sail into ground above `hullDraft` (−0.5, i.e.
where the terrain becomes visible) on their own; what happens when an island reaches them is described below. **Flotsam**
(`ShipSystem.Flotsam.cs`, same mesh): driftwood, barrels, message bottles, palm logs with a coconut (only near
islands) and ≤ 3 flagged buoys on the bank of the nearest island; all of it floats and is pushed aside the same
way. Rock stacks were deliberately left out: a static rock cannot be kept from clipping through a growing player
island or a drifting AI island without a collider or a vanishing act.

**Boats and islands (`ShipSystem.Beaching.cs`; owner: "Die Boote interagieren noch nicht mit den Inseln, die sollen
bei Aufprall auch auf Ufer laufen").** Contact = the ground under a hull's middle, bow or stern rises above
`hullDraft` (every island in `SeaObstacles`, the player above all). The *closing speed* is the relative velocity
along the shore normal (`SeaMath.ClosingSpeed`; normal = downhill gradient blended with the radial). At or above
`SeaMath.BeachClosingSpeed` — rowing boat 0.6, fishing boat 1.3, sailing boat 1.7, trading cog 3.4 u/s — or after
1.5 s pinned over the shelf at ≥ 35 % of it (`ShouldBeach`), the boat is **beached**: `FindBeachSpot` walks up the
slope to the strip 0.02–0.12 above the waterline (slope ≤ `BeachSlopeLimit` 2.0 — real shores measure 0.6–1.8, rock
stacks ~3 — free of other hulls, ≥ 2.5 u from the jetty, not inside `Drift.Core.IShoreBlocker.BlocksShore`; up to
nine candidates along the shore), the hull slides there in 0.7–2.4 s, tips 18–35° to the downhill side
(`BeachRoll`; cog 18–24°, a steep bank tips it as far as it goes) with a slight pitch, sails go slack (30 %), the
lantern is swung and a signal flag waved for 10 s and then every 14 s, the wake stops; splash ring
(`WaterFeedback.Splash`), spray (`SeaLifeSystem.SprayAt`) and the static event
`ShipSystem.ShipBeached(Vector3 pos, float intensity)` (+ `ShipRefloated`, `BeachedTotal`) for audio. Below the
threshold the hull is shoved out along the normal at `SeaMath.EvadeSpeed` (always above the threshold, so a boat that
is not beached is never overrun); the cog is shouldered sideways along the island's bow. ≤ 4 hulls per island
(`MaxBeachedPerIsland`; refloating ones count); no valid spot or cap reached → the old shove out to sea. Squeezed
between two islands the boat goes to the one that closes faster (`FasterHost`). The old bow-wave shove scaled with
the player's speed, which is why nothing was ever touched; it is now limited to 60 % of the hull's evade speed.
**Riding:** a beached or moored ship stores its pose in the host's *body space* (`SeaMath.ToBodyLocal`, the same
frame as `Island.ToLocal`, so body turns from merges carry it round) and derives the world pose every step;
`Island.Merged` re-bases the local pose (the merged body is re-centred on its centroid) and re-hosts hulls of the
absorbed island. Hulls on the *player* are drawn into a second mesh, `SeaShipsRiding` (island-local vertices, the
GameObject copies the player's transform every frame), because a 15 Hz world-space rebuild would stutter on an
island doing 7 u/s; it shares the 2400-vertex budget (`maxRideVerts` 1100, `SeaBatch.limit`) and is one more draw
call only while something rides. **Refloat** (`ShouldRefloat`): after 20–60 s, when the island sank under the hull
(spot < −0.02) or the host is gone — it slides downhill to deep water, rights itself as the ground lets go
(the tilt follows how far the beach lifts the hull out of the swell), sets sail with 6 s of grace. A spot that is
built over, uplifted > 0.35, turned into a cliff or reshaped by a running merge (`IsUplifting`) makes the hull slide
to the nearest free shore spot, or refloat when there is none. **Tied boats:** the resting player island is a
destination while it has a jetty (`playerDockMaxSpeed` 0.4); moored boats stay tied while their island does more
than 0.6 u/s (`StaysTied`, at most 40 s overdue) and cast off once it rests. **Gentle:** moving islands are steered
around where they will be in 1.5 s (sailing boats look 8 u further ahead and turn away ~16 u out), hulls within 16 u
of a moving island bob up to 2.8× harder with a short chop, fishing boats haul in their net floats (2.2 s) and leave.

**Whales** are two kinds: `Kind.WhalePod` (2–4 animals in a loose trailing formation of about a body length,
`SeaMath.WhalePodOffset`; 60 % bring a **calf** at 0.5 scale tucked in at its mother's flank that breathes faster and
comes up alone between the pod's surfacings) and `Kind.Whale`, the solitary **bull** (1.25 scale). Deep 22–40 s →
three breaths, one animal after the other (`WhaleBreathLag`) → fluke dive, 40 % of them synchronized, otherwise
0.9 s apart. Bulls **breach** every 80–180 s (first after 20–55 s) when the storm is below 0.3, the player's shore is
25–80 u away and the water ahead is clear (`BreachAllowed`): a 2.8 s full-body leap with a twist drawn opaque
(`_tWhaleBreach`, pale throat, long pectoral fins), launch ring, landing ring + 18 spray + 10 big droplets. Caps:
≤ 2 pods + ≤ 2 loners in range (`WhaleSpawnAllowed`, hard-clamped in `SeaMath`), cell rolls 6 % loner / 8 % pod.
They steer 12 u (+ 70 % of the pod's reach) clear of islands and ships, drift back when more than `whaleHomeRange`
55 u from the player, and when something still gets inside 80 % of the clearance of any member they dive to −2.1
and let it pass overhead. Measured over 10 simulated minutes: 6 surfacings + 2 breaches within 60 u of a resting
player, 11 + 3 for one travelling at 3 u/s, a whale within 60 u 50–65 % of the time. `WhaleCount` (all animals),
`WhalePodCount`, `WhaleLonerCount`, `WhaleCalfCount`, `WhaleBreaching`, `PositionsOf(Kind.Whale | Kind.WhalePod)`,
`MemberSeaKind` (→ `SeaKind.WhaleCalf` "Walkalb", `WhaleBull` "Walbulle").

Cost (desktop, Editor, 2026-09-20 evening): Ships 0.011–0.023 ms per frame (3–6 ships + 12 flotsam, ≤ 1.9k verts;
0.018 ms with four hulls riding the moving player), SeaLife 0.015 ms in the start world and 0.021 ms with 2 pods of
4 + 2 bulls surfacing (under 1.7k of 2000 verts: whales use a 47-vertex silhouette beyond 32 u and for calves);
fish 0.008 → 0.015 ms (156 fish); 0 B/step. Pure parts are tested in `Tests/SeaTests.cs` (32 tests; they live in
`Drift.Core.SeaMath` because `Drift.Tests` does not reference `Drift.Visuals`). Not yet: only whales avoid ships,
flotsam is still just shoved aside, `IShoreBlocker` has no implementer yet (hulls rest below 0.12, buildings need
≥ 0.3, so they cannot overlap today), and nothing is tappable or in the journal.

## Curved world (`Drift.Visuals.CurvedWorld`, `Shaders/DriftCurve.hlsl`, 2026-09-20)

The simulation stays a flat 660 x 660 torus (a torus cannot be mapped onto a sphere without tearing); the planet is
a **vertex bend**. `DriftCurve.hlsl` wraps every world-space vertex onto a sphere of radius R that touches the sea at
`_CurveFocus`: planar distance d becomes the arc angle d / R, height stays along the sphere normal
(`DriftCurveWS` / `DriftCurveHClip`). Near the focus that is the classic drop d^2 / (2R) plus a pull-in of
d^3 / (6R^2), and it is the identity at the focus, so gameplay, tap picking and UI projection around the player are
untouched (R = 900: 0.06 / 0.22 / 0.89 u drop at 10 / 20 / 40 u; R = 260: 0.19 / 0.77 / 3.07 u). All world shaders
include it (water, island terrain, vertex colour, animal, critter, fish, plate seam); **fragment code keeps the unbent
`positionWS`** (height colouring, the terrain clip plane, cloud shadows, noise). Normals stay unbent. Anything below
y = -1000 is never bent (Tilda's portrait stage). New world shaders: see the usage block at the top of the include.

`CurvedWorld` (on "Visuals", order 300) pushes the globals **per camera** in
`RenderPipelineManager.beginCameraRendering`: only `Camera.main`, cameras passed to `CurvedWorld.Register` and, with
`bendSceneView` (off: gizmos are not bent), the Scene view (same focus/R as the game, no haze) are bent; the island preview and Tilda's camera render flat. Focus =
the ground point the camera looks at (`WaterFollower.ViewFocus`, clamped to 1.5 x camera height + 5 so a horizontal
photo camera keeps it nearby) — for the chase camera that is the island / followed herd. R = lerp(`maxRadius` 900,
`minRadius` 260, smoothstep of the camera-to-focus distance on a log scale between `viewNear` 12 and `viewFar` 120),
times 1 / `strength`; `flatten` switches it off. Closer than `viewNear` R keeps growing by
(viewNear / distance)^`closeFlatten` 1.5 (a camera 1 u above the sea would otherwise have its horizon 40 u away). A
fresh island at zoom 1 (14 u, R 892) is practically flat, the same island fully zoomed out has R 587, a radius-30
continent R 310 at zoom 1 and 260 zoomed out: the limb crosses the upper third of the screen. `CurvedWorld.Bend(world)` / `DropAt(xz)` mirror the shader for C# callers
(labels, picking far from the focus).

What closes the horizon: (1) `WaterFollower` replaces the scene Plane (100 u cells) at runtime with a DontSave nested
grid — 3 u cells out to 72 u, doubling per level to 1152 u, level seams stitched with fan triangles (no T-junction
cracks once bent), 9 505 vertices / 18 816 triangles, one draw call; the object follows the view focus snapped to
12 u so the vertices of the three inner levels stay on fixed world positions (the bent surface does not swim). The
scene keeps the Plane reference (restored on disable), the transform scale is compensated in the vertices. (2) Horizon
haze: `DriftFog` fades solid things by `haze` 0.8 and the sea completely into `_CurveFogColor` between 35 % and 85 %
(`hazeStart`, `hazeFull`) of the limb distance (a circle around the point under the camera in unbent coordinates); a
low camera (limb less than `lowLimbAngle` 4 degrees under eye level, blending out until `highLimbAngle` 20) sees
everything beyond a third of that distance squeezed into half a degree, so there the fade runs from 10 % to 55 %
(`lowHazeStart`, `lowHazeFull`) and stays a soft band on screen. (3) `Drift/CurvedSky`, a
dome drawn around every bent camera (`_CurveSkyDraw`: the island preview and portrait cameras keep their clear
colour), last of the opaques with depth write (so the skybox, whose
horizon sits at eye level up to 45 degrees above the limb, never shows its ground half): horizon colour exactly on
the limb all round the planet; what is above it is the sky described under "Sky" below. The water shader also fades
its wave normals, height shading and glints between 40 and 120 u (`farLod`): from the distances the curved world
shows, the flattened waves still lined up into a grid. (4) Renderer bounds are unbent, so `CurvedWorld` widens
`Camera.cullingMatrix` for the bent camera each frame (frustum edge rays traced to the planet or the limb, unbent
again, plus 8 %) and resets it after the camera rendered; without it islands near the limb pop out while on screen.

## Impact feedback, day/night (`Drift.Visuals`)

`WaterFeedback` pushes `_PlayerPos/_PlayerVel/_PlayerRadius`, up to two impact rings and the local
plate current (`_CurrentDir/_CurrentSpeed/_CurrentFoam`) to the Ground renderer via a
MaterialPropertyBlock (the .mat never changes). The water shader draws bow foam and a churned wake
(relative to the current by default, `wakeRelativeToCurrent`) **along the island's real outline**: `WaterFeedback`
reads `Island.LandReach` into a 32-entry body-space radial profile (only when `Island.Version` changes, at most every
0.1 s, else every 0.5 s; blurred 0.2/0.6/0.2, no allocations) and pushes `_PlayerReach[32]` (reach, d reach / d angle),
`_PlayerBodyYaw`, the two widest outline points across the velocity (`_PlayerWakeEdge`, found in body space, smoothed
at 8 /s) and `_PlayerWake` (wake length, V length, cutoff range; all zero below `wakeMinSpeed`, which makes the
shader skip the wake with one branch). Per pixel: one polynomial atan2, two interpolated array entries give the
outline radius and its normal; bow foam where the normal faces the velocity, turbulent foam in the silhouette's
shadow behind the line through the widest points (fading with the distance to the trailing outline, so it follows
notches and the stern width; one extra stretched noise lookup), a V line from each widest point. Also expanding foam rings (6 u/s, 2.5 s)
and thin foam streaks scrolling with the current (none below 0.3 u/s). `ImpactFeedback` listens to
`Island.Impact/Merged`, places the ring at the contact midpoint and does a 0.22 s slow-motion
beat (timeScale 0.35, skipped when paused). `DayNightCycle` rotates the Directional Light on a
tilted axis over `dayLength` (360 s), blends sun colour/intensity (night floor 0.5) and hands out the sky palette
(flat ambient, water sky/deep tints, see "Sky"); the light is never below 14°. Exposes `TimeOfDay`, `NightAmount`.

## Sky (`Drift.Core.SkyPalette` / `SkyMath`, `Shaders/DriftSky.hlsl`, 2026-09-20)

Owner: "Skybox für Tag und Nacht, der Übergang soll fließend verlaufen." One **palette** drives everything that has a
sky colour: `SkyPalette` is a serialized array of `SkyKey` rows on a cyclic time axis (13 default keys: night, first
light 0.215, sunrise 0.25, golden morning 0.29, day 0.36 / noon / 0.64, golden evening 0.71, sunset 0.75, dusk 0.785,
twilight 0.83, night 0.88), each with zenith, horizon, sun-side glow and anti-sun (Belt of Venus) colour + strength,
sun disc, lit / shaded cloud colour, flat ambient and the water's deep tint, authored in sRGB. `Evaluate(t)` blends
the two neighbouring keys (wrapping 1 -> 0, half smoothstep `easing`), so nothing can pop. `DayNightCycle.Apply()`
evaluates it once per frame, applies the storm (`SkyMath.ApplyStorm`: grey + darker, less so for colours that are dark
already, so a night storm stays indigo) and publishes `Sky`; `AmbientColor`, `WaterSky` (= `Sky.horizon`) and
`WaterDeep` are fields of that one value, and `CurvedWorld` pushes the same `Sky.horizon` as `_CurveFogColor`. The sky's
limb colour, the haze and the water's far colour / reflection tint are therefore one value at every time of day
(colours go to the shaders linear: `SetGlobalVector(c.linear)`, the water's copy through `MaterialPropertyBlock.SetColor`).
`SkyMath` (pure, tested in `Tests/SkyTests.cs`; it lives in `Drift.Core` because `Drift.Tests` does not reference
`Drift.Visuals`) holds the sun / moon orbits, star visibility (0 until the sun is 5 degrees down, 1 at 20), sun disc
visibility / swelling, the light's colour turn (`LightNight`, later than `NightAmount` so the last sunlight stays warm),
the moon-light blend and the shooting star. The **moon** runs opposite the sun on a flatter orbit (`moonTilt` 66: it
culminates at 24 degrees, where the chase camera can see it; `moonLead`: already up at sunset), its phase (`moonPhase`,
one cycle per `moonCycleDays` 8) is simple sphere shading. With `moonLight` the directional light swings (slerp over the
top, at most 8 degrees per second) from the sun's to the moon's side while it is dimmest (sun height 0 ... -0.4), so the
water's glitter path lies under the moon.

`DriftSky.hlsl` is the sky itself, shared by the dome (`Drift/CurvedSky`) and the `RenderSettings.skybox` fallback
(`Drift/Skybox`, set by `CurvedWorld` in Play Mode only - a runtime material would be saved into the scene as "none" -
for cameras that clear to the skybox but are not bent; solid-colour cameras such as Tilda's portrait are untouched):
gradient from `_CurveFogColor` at the limb to the zenith colour (`skyGradient`), warm tint towards the sun and violet
belt opposite it (both start just above the limb, which keeps the haze colour all round), sun disc + rational-falloff
halo, moon disc with phase, maria and halo, a hashed star field (one 3D cell hash per pixel: `starDensity` 64 cells
per unit direction, about 6 000 stars over the visible half, size / colour / twinkle from the hash; the field turns
with the time of day around the sun's orbital axis), a Milky Way band (two noise octaves, only evaluated inside the
band, fades in with the square of the star visibility), one analytic shooting-star streak (`SkyMath.ShootingStar`: time
slots of `shootingStarPeriod` 7 s, `shootingStarChance` 0.6, 0.9 s long) and the **cloud layer**: the very noise field,
offset, scale and cover of `CloudShadows` (`DriftClouds.hlsl` globals) seen on a layer `cloudHeight` 150 u above the
camera, so shadows on the sea belong to clouds in the sky and storms close the cover; the noise's analytic gradient
shades the flank facing the light (sun by day, moon at night) for free, thick parts get a grey belly, clouds hide the
stars and catch the sun / moon halo as a rim. Beyond a limb angle of 36-48 degrees (`cloudFadeLimbAngle`: the camera
looks at the planet from outside) the cloud layer fades out. Sun and moon are drawn per camera where that camera sees
them (`SkyMath.Apparent`: same azimuth, elevation counted from the limb in that azimuth), so the sun sets into the
planet's edge at every zoom and can never hang above it at night; below the limb the shader returns the haze colour
after one dot product (the transparent sea is drawn later, so those pixels are shaded too). Cost above the limb: two
value-noise lookups by day, plus one hash, one sin and (in the band) two lookups at night; no textures. No aurora (too
costly for what it adds). Build note: `Drift/CurvedSky` and `Drift/Skybox` are found with `Shader.Find`; assign
`Materials/CurvedSky.mat` / `Materials/DriftSkybox.mat` to `CurvedWorld.skyMaterial` / `skyboxMaterial` (or add both
shaders to "Always Included Shaders") or a player build strips them.

## Volcanic islands

New land comes from the sea: `VolcanoSpawner` listens to `PlateSystem`'s `Rift` events (fired on
divergent boundaries by `PlateSystem.Events.cs`, `riftRate` 0.03, was 0.02: ~18 → ~27 rifts per minute in
the border window, so a finished cooldown waits only a few seconds for a usable rift) and turns a qualifying one into a volcanic island. Filters (2026-09-20, owner: "mehr
Vulkane an den Plattengrenzen"): `minPlayerDistance` 30 – `maxPlayerDistance` 200 units from the player
(was 40–150), at least `minIslandDistance` 25 clear of every existing island, at most `maxLive` 10 alive
(was 5) and a `cooldown` of 25 s (was 70) that runs on the spawner's own clock (`Step(dt)`, so it can be
driven headlessly; `TryHandleRift(e)` is the filter + spawn without the Play Mode gate). The cooldown still
sets the pace: measured over 330 simulated seconds from the start position with the start window's 44
islands loaded, spawn events came 26–40 s apart and the cap of 10 was reached after 221 s (one cone per 22 s;
with `riftRate` still 0.02: 25–60 s apart, 10 cones after 290 s) and then held. **Island arcs:** with `chainChance` 0.35 the
event grows a chain of up to `chainMax` 3 cones along the same seam — further cones alternate sides of the
first at `(r1 + r2)·1.6 + 6..12` u, are 0.5–0.85× its radius (never below 0.7·`minRadius`), must pass the
same player/island filters (chain siblings excepted) and surface `chainStagger` 5 s one after the other.
Radii are `minRadius` 2.2 – `maxRadius` 5.5 by rift strength (was 2.5–4.5). **On the seam:** plates carry
their riders away from a spreading boundary at several units per second, so a cone used to surface 10–20 u
off the rift that made it (measured 14 u only 8 s after surfacing). The spawner therefore holds every cone on
the seam of its two plates (`HoldOnSeam`: the border with the same plate-cell pair; a snap onto the closest point
of the *curved* seam PlateSystem draws - `PlateSystem.ClosestOnSeam`, arcs step along it with `SeamAdvance`, the
seam search is `NearestSeam` - within `seamSnapDistance` 4 u, a return at `seamReturnSpeed` 6 u/s from further away, e.g. after
the cone was outside the border window; no clamping to the segment ends, which would shove the cones of a
shrinking border into its triple junction) — while it rises and, with `holdAfterEmergence` (default on), for
good, so the cones keep marking the boundary. Along the seam a held cone is moved with the mean velocity of
both plates instead of whichever plate is nearer, otherwise the cones of one arc shear together on a transform
component (21 u → 1 u in 27 s before the fix, 21 → 16 after). Measured 330 s in: all ten cones within 0.3 u of
their seam except one riding past a segment end (6 u). Anchors are not saved; `Restore` re-anchors every cone
to the nearest border within 8 u.
`IslandShape.CreateVolcano` builds a steep cone (peak ≈ 0.95 × radius + 1.6 minus the crater, so ~3.7 units
for a radius-3.3 island and ~5.5 for the largest) with a crater dip and a noisy rim; the terrain shader
turns the flanks to rock and the top to snow by itself.

The island starts fully submerged and rises over 12 s using the existing `IslandShape.sink` offset
(`Island.BeginEmergence` / `AdvanceEmergence`, the sibling of the player's sinking). Vegetation and
herds hang off `LandArea` and `Version`, so they populate the cone on their own as it surfaces.
Emerging islands are skipped by collision detection until they are up. Volcanic islands do **not**
count towards `WorldStreamer.WorldSlots()`, so the HUD's world progress stays the share of the
planned islands and volcanoes are bonus material; saved together with the player (§5).

## Island archetypes and the world plan (2026-09-20, owner: "mehr Variation in Größe und Form")

`Island.archetype` (`IslandArchetype`, saved as `IslandSaveData.archetype`) picks the generator:
`Classic` = the original `IslandShape.CreateBlob` (player, tests, anything without a plan), everything else
goes through `IslandArchetypes.Create(type, radius, seed, cell, heightScale)` — a pure function, so a slot
looks the same in every copy of the torus and after every reload. `radius` is the nominal half extent the
streamer spaces by; land reaches at most 1.4 radii from the centroid (tested) and the land area is
`IslandArchetypes.AreaFactor(type) · π · radius²`, which is what `WorldSlot.EstimatedArea` and the HUD
progress use (checked on the 44 islands of the start window: estimate 1394 vs. 1403 real). The families:
**Blob** (0.85; stretch 1–1.4, coast wobble 0.12–0.32 over three seeded harmonics, from radius ~5 up ridged
inland hills to ~2.6 — rocky tops, goats), **Ridge** (0.49; 2.7 : 1 bent spine with a crest up to ~2.5), **Crescent** (0.50; a
ring that is thickest opposite its opening, open 90–190° or, every third, an atoll with a 25–45° pass; the
lagoon ends at the line between the horns), **TwinPeak** (0.59; two lobes joined by a soft-min saddle, hills
to ~2.7), **Sandbank** (0.44; long, bent, below 0.45 high on a wide shallow shelf), **Mesa** (0.89;
rounded-square plateau at 1.1–1.55 with a cliff and a narrow beach skirt), **Archipelago** (0.40; 2–4
islets on one heightfield over a wobbly shallow bank) and **Stack** (0.59; one rock column 1.6–3.1 high plus
up to two needles). Shallow floors sit at `Shallow` −0.4: above the terrain clip (−0.45), so the water shader
tints them turquoise, and below `DetectContact`'s shelf threshold (−0.3), so sailing across a lagoon is not
a collision. Nothing exceeds `MaxPeak` 3.2 (snow starts at 3.4 and belongs to volcano cones), every shape has
a beach band, the grid is cropped to the shelf and re-centred on the land centroid (bodies turn about
their origin; a crescent's centroid lies in the ring, not the lagoon). Islands under radius 3 use 0.25-u
cells (`IslandArchetypes.CellSize`), otherwise a stack would be four vertices wide; merges resample the guest
onto the host grid anyway. `IslandKind` is unchanged (Barren still scales radius ×0.8 and heights ×0.55).

`WorldStreamer.Plan` rolls, per chunk, `minPerChunk`–`maxPerChunk` (2–4) **main islands** — class shares
`largeShare` 0.05 (radius 9 + 5·t², so mostly 9–11 and rarely 14), `bigShare` 0.12 (6–9), `mediumShare` 0.43
(3–6), rest small (2–3), archetype by a weight table per class (no crescents under 3, stacks only small,
sandbanks/mesas not large) — sorts them largest first and places them with the old `(r1+r2)·1.8 + 6`
spacing, then `minIslets`–`maxIslets` (1–2) **islets** of radius 1.2–2 (blob 35 / stack 30 / sandbank 20 /
mini archipelago 15), half of them (`satelliteChance`) as satellites `(R + r)·1.4 + 5..13` u off a main
island, with `(r1+r2)·1.4 + 5` spacing. The chunk margin is `max(10, 1.4·r + 2)`, so independently planned
neighbours never overlap (tested across chunk and world edges), and the start exclusion is
`startExclusion + 1.4·r`. Seed 777: **166 slots** (was 113), radius < 2: 56, 2–3: 38, 3–6: 54,
6–9: 15, ≥ 9: 3 (largest 13.5); estimated world area **5332** (was 3481, +53 %: the few big and large islands
carry most of it); archetypes Blob 46, Ridge 25, Sandbank 22, Archipelago 21, Stack 17, Crescent 14, Mesa 12,
TwinPeak 9. Seven other seeds: 147–165 slots, 52–61 islets, 3–8 large, area 4450–5940. Per-frame cost with the
45 islands of the start window: life 0.05 ms, herds 0.07 ms, critters 0.01 ms, collisions 0.07 ms.
Spawning the 44 islands of the start window takes 50–70 ms in the Editor (26k terrain vertices). The layout
change is why `WorldGenVersion` went to 2 (§5).

## Water

`Drift/Water` (transparent URP shader, no assets needed): analytic multi-wave normals, depth-based
shallow/deep colour and foam from the camera depth texture (URP assets have *Depth Texture* enabled),
fresnel sky tint, specular glint and sparkles. Islands write depth in their normal pass, and their
submerged part is `clip`ped below y = -0.45 so no stepped shelf shows through the water. Known
rough edge: a faint darker halo under islands (depth-based shallow tint) that still needs a look
in the real Game view; Store water assets can replace this later.

## Work by parallel chats

Other chats have added `Drift.UI` (TMP HUD framework: `HudPresenter`, `IHudDataSource`,
`IMoveInputSource`/`MobileMoveInput`, `CanvasScaleSetup` — all unwired dead code today; the live UI is
`WorldHud`/`SessionScreens`/`TouchControls`), `Drift.Tests` (EditMode tests) and `PlateSystem.Events.cs`
(tectonic terrain events on boundaries; PlateSystem is a `partial` class). `Drift.Bridge.IslandHudData`
is likewise unwired. When several chats share the project, check `git status` and the console for
foreign changes before editing shared files.

`EditorSettings.asset` has `m_EnterPlayModeOptions: 3` — domain **and** scene reload are disabled on
entering Play Mode. Statics (`Island.All`, `Island.InputProvider/InputLocked`, `PlateSystem.Instance`,
`StormSystem.Instance`, static events) therefore survive between Play sessions and must be reset in
`OnDisable`/`OnEnable`; `[ExecuteAlways]` `OnEnable` does not re-run on Play entry, so per-play wiring
(`SessionScreens`) self-heals from `Update`.

## Performance pass for 60 fps on mid-range phones (2026-09-21)

Measured in real Play Mode with a scripted driver (see `Docs/PERF_BASELINE.md`, section 2026-09-21).
Structural rules that new content has to follow:

- **Frame rate:** `Drift.Core.PlatformSetup` sets `Application.targetFrameRate = 60` on phones (the platform
  default is 30). Android uses Optimized Frame Pacing. The Mobile URP asset has HDR, main-light shadows (nothing
  casts any), light cookies/layers, lens flares, reflection-probe blending/box projection, terrain holes and LOD
  cross-fade off. A new effect that needs one of them has to switch it back on deliberately.
- **Streaming:** a chunk's island heightfields are generated on worker threads as soon as the chunk is planned
  (`Island.PrebuiltShape`, pure math + `Mathf.PerlinNoise`); `WorldStreamer.Spawn` takes the finished shape and
  waits for it otherwise. A fresh island's life components start **disabled** and are switched on one per frame
  (`WorldStreamer.StageNext`, order life → herds → critters → settlement). A new per-island system therefore has to
  (a) be added to `NextDisabledLife` in dependency order, (b) populate itself from `OnEnable`, and (c) cope with
  its siblings still being disabled for a few frames. Saved islands (overrides) still spawn in one frame.
- **Terrain mesh:** height-only changes go through `IslandShape.RefreshHeights` (positions, and the index buffer
  only when a vertex crossed the culling depth; colours are kept). Sinking skips the normals, the merge ridge
  recomputes them and is redrawn at 30 Hz with the hypsometry at 10 Hz. `FillMesh` stays the full rebuild.
- **Garbage:** per-frame code allocates nothing (structs for per-bird data, no `foreach` over interfaces in hot
  paths, strings only when a label changes). Allocation is fine for discrete events (spawn, merge, save capture).
- **UI:** every game canvas is `ScreenSpaceOverlay` (`UiStyle.Canvas`), drawn after the camera, so a future
  full-screen effect never touches text; pass `null` as the camera to `RectTransformUtility`.

## Surfing, watching, herd patterns (2026-09-21, second pass)

- **Surf:** `PlateSystem.SurfVelocity(pos, heading, radius, drive)` gives the player island extra velocity along the
  nearest seam (`NearestSeam`), scaled by alignment, distance to the seam and boundary kind; `Island` keeps it as
  `_surf` (smoothed by `surfResponse`), part of `PlanarVelocity`. `Island.WaterVelocity` (= self + surf) is what the
  wake is drawn from - never "planar minus current", which is wrong for a second wherever the current jumps.
  Rider push is capped by `maxRiderPlateSpeed`. Plate amplitude/speed are per-plate factors on the live
  `waveAmplitude`/`waveSpeed`; the pendulum phase is the integrated `_waveClock` (saved as `PlateSaveData.waveClock`).
- **Watching:** `Bridge/WatchSubjects.Find(entry, …)` turns a journal entry into a `WatchSubject` (a herd, or a
  focus delegate asked every frame). `WatchTools.Watch(catalogIndex)` follows it with the herd-follow camera;
  `Following` covers both. A new journal entry type needs a case in `WatchSubjects`.
- **Herd patterns:** errands `Stroll`, `Visit`, `Spread` (AnimalActivity appended, moods appended in order) for every
  species, chosen in `Think` after the species errands with `strollRate`/`visitRate`/`spreadRate`. On a surface
  version change only errands that the new shape spoils are cancelled (`SurvivesReshape`); shore errands always are.


## Rising water, visible storms (2026-09-21, third pass)

- `IIslandSurface.SinkDepth` (Island: `_shape.sink`). Life systems treat a growing value as "the water is rising"
  (herds: 6 s window) - only then do herds flee (`IslandHerdSystem.Refuge`, refuge map = grid BFS components of dry
  land, each herd heads for a spot on its component's top; sliders `refugeStart`/`refugeSpeed`). Walking to the
  shore to drink/wade never triggers it.
- `Drown` no longer removes a wet animal outright: `TryAshore` moves it to dry ground within 2 units (`Scrambled`);
  only an animal with no dry land in reach is lost (`Drowned`) - i.e. when its detached part has fully sunk.
  Critters: `Relocate` falls back to `TryDryNear`.
- Settlements: on rising water a low village moves its centre to the highest reachable site with room
  (`MoveUp`, dry path, `moveUpRange`), folk gather there, `regroupRest` s pause (no building), `Village.keep` holds
  the folk count while homes are missing. Buildings/plants never move; they sink.
- `LifeEnvironment.LightningStruck(world, strength)` is the one lightning event: island strikes
  (`IslandLifeSystem.Strike`, play mode) and sea strikes (`StormVisuals`, Poisson at `lightningRate` per storm)
  both report it; `StormVisuals` draws bolts + global `_LightningFlash` (DayNightCycle adds it to sun/ambient),
  `AudioDirector` plays `SfxSynth.Thunder` delayed by distance / `thunderSpeed`.
- `StormVisuals` (`Shaders/Storm.shader`, one dynamic mesh: spiral cloud layers turned in the vertex shader, rain
  lanes, additive bolts in `StormBolt.mat`) pushes `_DriftStorms[4]` for the water shader (rough sea inside storms)
  and `_StormEye` (clear hole above the player). `StormVisuals.StormCount/StormAt` feed the minimap
  (`WorldHud.DrawMap`, blended disc under the islands).
- Player island scene values: `IslandHerdSystem.maxAnimals` 200, `maxHerds` 60.

## Materials, mountains, clouds, signature moves (2026-09-22, parallel-agent round)

- **Terrain** (`Shaders/IslandTerrain.shader`): two Texture2DArrays (7 layers: sand, grass, forest floor, dry, tundra, rock,
  snow; `Textures/Terrain`), top-down projection in island-local space, layer picked from the existing tint colour + height
  + slope, only the two strongest layers sampled (4 reads/pixel), textures normalised by their mean so tints keep working,
  distance fade to flat colour.
- **Vegetation** (`Shaders/Vegetation.shader`, `Textures/Plants/Resources/DriftVegetation.mat`, loaded via Resources):
  per-vertex surface id in UV0.w (`PlantTemplate.part`, written by `TemplateBatch.AddPlant`), per-face planar projection.
- **Mountains** (`Island.MergeFrom`, `IslandShape` slope relaxation): wide uplift bump, slope cap `maxSlope` 30° /
  `volcanoMaxSlope` 45° applied at merge time, rise over `upliftDuration` 7 s with eased blend, Version bumps on the 0.5 s
  sink cadence. `AudioDirector.EstimateMergeSeconds` caps the uplift at 2.5 s for the impact sound.
- **Clouds** (`CloudShadows`, `CloudField` C# twin, `DriftClouds.hlsl`, `DriftCloudPuff.hlsl`, `Clouds.shader/.mat`): jittered
  clump grid (18 u), one static puff mesh placed in the shader; shadows use the same field. Storm clumps in `StormVisuals`
  (14 per storm, orbiting at different speeds, rain shafts under 70 %). Water: sky reflection and specular use a calmed
  normal (the full normal drew white bands). Tests: `Scripts/Tests/Visuals` (own asmdef).
- **Herds**: refuge = nearest safe ground (`SafeHeight` = 0.03 + `refugeStart` + sink pace × `refugeLead`) with a spread
  penalty (`refugeSpread`); signature moves in `IslandHerdSystem.Signature.cs` (one per species, `signatureRate`, run as
  errands, cancelled by startle/fire/water/storm/night). **Critters**: states Wave/Nest/Spiral, firefly `_SyncWave`,
  flock dive + `Murmur`. **Markings** (`Life/Markings.cs`, `Shaders/DriftMarkings.hlsl`): marking id + body coords in UV3,
  procedural patterns, no texture reads; after editing the .hlsl reimport Animal.shader and Critter.shader.

## Game modes, Pangäa, ring world (2026-09-22, second parallel round)

- `Drift.Core.GameModes` (Cozy/Adventure) is the single mode switch; `GameSession.StartNewGame(mode)` sets it and the
  per-mode save file (`GameModes.SaveFile`: cozy keeps `drift_save.json`). Leaving Play Mode resets it to Cozy.
  Adventure never saves (`SaveManager.Save` returns early), never auto-restarts after sinking.
- Cozy: `State.RunComplete` via `GameSession.CompleteRun()` (called by `Bridge/PangaeaFinale` once
  `WorldStreamer.Progress` reports 0 islands left for 1.5 s); the finale drives `CurvedWorld`/camera/sky through public
  fields and restores them on the next state change, then `RunJournal.Add` (run_journal.json + RunJournal/*.png,
  `RunJournalPanel` UI, sortingOrder 40 > finale 20). Cozy sinking = size regulator on current area with a floor that
  rises with `RunProgress` (Island "Sinken (Gemütlich)"); world = 4×90 u chunks, 20 planned islands (layout v3; v2 saves
  keep their big world). PlateSystem gridPeriod 4 × 90 must match.
- Cross-run books in persistentDataPath: `drift_journal.json` (JournalBook: all-time discoveries; the save keeps only the
  run layer), `drift_phototasks.json` + PhotoTasks/ (19 signature-move photo tasks, `Bridge/PhotoSubjects` judges
  photos), `drift_best_times.json` (BestTimes), PlayerPrefs `drift_adventure_tutorial_done`.
- Adventure: `Islands/RingWorld` (+ `RingIslandSpawner`, `Visuals/RingRims`) disables the WorldStreamer, sets
  `Island.PositionConstraint`, lays plates along the band; `DriftCurve.hlsl` ring bend (upward along z, walls fold the
  sea beyond the band), `CurvedWorld` culling box. `IslandChaseCamera` in the ring: `ringPitchUp`, `ringAlongBias`,
  `ringWallMargin` keep the view along the track and inside the band. Island merge boost `Boosting/BoostFactor`,
  `SpeedBoost`, `AddBuoyancy` (flotsam rewards via `Visuals/Encounters.ApplyReward`).
- Travel: `Visuals/Encounters` (pacer + dolphins/whale/route flotsam), `Visuals/CurrentField` (64² current texture for
  the water's drifting foam comets), `Bridge/IslandHints` (cozy destination hints). Adventure tutorial:
  `AdventureTutorialModel/Guide`, Tilda `TildaAccessory.SportShades`.

## Feel, milestones, events, ring rebuild, tilt (2026-09-22, third round)

- **Direct-direction steering**: `Island.DirectionProvider` (world XZ, length 0..1) + `Island.DirectionSteering`.
  `Tick(input, driveDir, dt)` drives along `_driveDir` without turning; the heading only follows when
  `directionHeadingFollow > 0` (default 0, so the chase camera stands still). `UI/TiltSteering` + `Core/TiltMath`
  (calibrated neutral, dead zone, smoothing, drift-follow, `editorFakeTilt` for the Editor), settings screen in
  `UI/TiltSettingsScreen`, PlayerPrefs `drift_tilt_*`. Keyboard can use the same scheme (`KeysToDirection`).
- **Speed feel**: `Islands/SpeedFeel` (pure curves) drives `IslandChaseCamera` (FOV/pull-back/shake/roll/merge kick),
  `WaterFeedback` + `Water.shader` (`_SpeedFeel`, speed lines, spray ring), `AudioDirector`/`SfxSynth`
  (flow + surf voices), `Visuals/DriveFeel` (surf kick + flow push via `Island.SpeedBoost`, cozy only).
  `Island.leanIntoTurns` rolls only the transform, never the planar body basis.
- **Milestones** (cozy): `SaveSystem/Milestones` (3/6/10/15, derived from `Stats.islandsAbsorbed`, mask in the save),
  `Bridge/MilestoneToasts` (toast + Tilda line `milestone_*`), landmarks through
  `IslandSettlementSystem.TryBuildLandmark` (landmark village), `FlockSystem.SetSeabirdHome`, evening `Festival`
  lanterns. `Milestones.MapRange` gates `WorldHud.DrawMap`; the HUD shows `Milestones.NextText`.
- **World events** (cozy): `Visuals/WorldEvents` (scheduler + director), `RainbowArc` + `Shaders/Rainbow.shader`,
  aurora/meteors as guarded branches in `DriftSky.hlsl` (globals are 0 when idle), whale migration in `SeaLifeSystem`.
- **Adventure rebuild**: no merging (`IslandWorld` adventure branch → `Island.Bump`, `Island.Bumped` event,
  buoyancy loss), flotsam is the only refloat (`Encounters.LayTrack` along the ring), plate lanes run along the track
  and move (`PlateSystem` ring params), difficulty over `RingWorld.RunSeconds`/`Level`. Waterfall edges replace the
  rims (`Visuals/RingFalls` + `Shaders/RingFall.shader`, ring bend in `DriftCurve.hlsl`); the player is unclamped and
  can fall (`Island.LostOverEdge`, `EdgeWarning`), obstacle islands stay clamped.
- **Merge tint**: `Island.MergeFrom` calls `IslandLifeSystem.RefreshAfterMerge()` (a `LateUpdate` net covers other
  paths) so the merged mesh is never drawn with white vertex colours.

## Steering, coast field, two-step Pangäa (2026-09-22, fourth round)

- Direct-direction steering is the default: `SessionScreens` installs `Island.DirectionProvider` and sets
  `Island.DirectionSteering` from `UI/SteerSettings` (PlayerPrefs `drift_steer_direct`, pause menu "Steuerung").
  `IslandChaseCamera.fixedOrientation` + `viewYawDeg` keep a north-fixed view (`ViewIsFixed`/`ViewForward`, used by
  `WatchTools.SetFollowHome` too); the shake is an offset on the smoothed pose and everything freezes at `dt <= 0`
  (that loop was the "camera drifts away in the menu" bug). Deflection scales the speed cap
  (`Island.directionMinSpeedShare`), while cruise and boost keep the unscaled cap.
- `Visuals/CoastField` builds a signed-distance field of the player island's coastline in body space (rebuilt on
  `Island.Version`) and pushes `_CoastField`/`_CoastParams`/`_CoastRot`; bow foam, wake churn, speed lines and the
  fleck emphasis read it, so foam follows notches. `_IslandData` and `_PlayerReach`-based shore distance are gone.
- Cozy end: `SessionModel.PangaeaReached` (free look, sinking held, banner in `PangaeaFinale`) and only its "Weiter"
  calls `CompleteRun()`. `SaveSystem/IslandNames` gives every finished Pangäa a generated name (seeded by the world
  seed, never a real island name verbatim); `RunRecord.name`, shown in the finale and the run journal.
- One watch mode: `WatchTools.BeginWatch(WatchSubject)` for animal tap, discovery toast, journal card and photo cue;
  the cue tap is answered by WatchTools' own press pipeline (`Press.onCue`, `CueContains`) with a >= 90 px hit rect.

## Play-test tool, moments, life director, sea visitors (2026-09-23)

- `Core/Moments.cs`: `Moments.Report(MomentKind, worldPos)` whenever something a watcher would notice starts (herd
  errands/plays/signature moves/sleep stirs, bird murmurs/landings/dives, butterflies, critter moves, firefly waves,
  festivals, fish jumps, dolphin/whale/ray/turtle/seal/flying-fish/gull/jellyfish moments). One null check without
  listeners. `Moments.Noticed(cam, world, size)` is the shared "noticed" rule: on screen and the subject
  (`SubjectSize(kind)` world units) at least `NoticePx` (20) pixels tall.
- `Bridge/LifeDirector.cs` (on SessionUI, cozy only): keeps the owner's target "something interesting near the
  camera at least every 10 s". `LifePacer` (pure, tested) tracks the gap since the last noticed moment; after
  5.5–7.5 s it starts something ON SCREEN that is big enough: a herd signature/play/species move (the followed herd
  first in watch mode), a flock special move, critters, a fish jump, dolphins, a surfacing whale or
  `SeaLifeSystem.TryShowNear`. Weighted, no immediate repeat, per-kind cooldowns. By night herds are never woken:
  firefly waves at the watched herd (`IslandCrittersSystem.TriggerFireflyWaveAt`), sleep stirs
  (`IslandHerdSystem.TryStirInSleep`) and the sea take over.
- Sea (`SeaLifeSystem.Show.cs`, `Core/SeaShow.cs`): cozy coast visitors (a seal that pops up, looks at the island and
  may haul out onto the beach; a turtle paddling along the shore), ray leaps, flying-fish bursts across the view, a
  dolphin pass; coast fish schools jump more often in view. All cozy only; `TryShowNear(center, radius, roll)`.
- `WatchTools` sets the global keyword `DRIFT_NEAR_FADE` while following/photographing: `Vegetation.shader` dithers
  out plants nearer to the camera than the watched subject (`_DriftNearFade`), so trees stop filling the orbit view.
- `CurrentField` refreshes spread over frames (`Begin`/`Step`, `WaterFeedback.currentFieldRowsPerFrame`) and collects
  plate sites with `PlateSystem.PlatesInRect` (cell lookups): the old 2.6 ms refresh four times a second was the
  main CPU hitch.
- Play-test tool (`Scripts/Editor/Playtest`, editor only): menu `Drift/Playtest/*` or
  `PlaytestRunner.Run(scenario, options)` from eval. Scenarios on a fixed seed: `cozy_observe`, `watch`,
  `adventure_good`/`adventure_poor`, `tutorial_cozy`/`tutorial_adventure`, `clips`, `soak_cozy`. `HumanBot` steers
  through `SessionScreens.ScreenOverride`/`MoveOverride` (screen directions like stick and tilt, with reaction delay,
  noise and attention lapses). `PlaytestRecorder` writes `Playtests/<scenario>_<time>.json` (noticed-event gaps per
  phase, frame cost, optic flow, race levels: threats, pickups, near misses, boost/surf share, memory);
  screenshots/clips go to `Playtests/shots/` (gitignored). `python Playtests/summarize.py` prints the newest file
  per scenario. `PlaytestProfiler` gives per-system CPU ms, GC and the worst frames from the Editor profiler;
  `PlaytestCapture` sets the phone portrait Game view and builds filmstrips/contact sheets.

## Proposal round after the play test (2026-09-23 evening, 6 + 1 agents)

- Race camera (`IslandChaseCamera`): ring pose 1.6 / 11, `ringPitchUp` 7; at speed it dollies in (`ringSpeedDolly`) and
  widens FOV (`ringSpeedFovGain`); boost adds FOV (`boostFovGain`, `boostReference` 1.65) plus a swell per pickup
  (`boostFovKick`); `RingWorld.Dodged` gives a swell and a sideways sway. `Visuals/SpeedStreaks` (on the main camera,
  `Materials/SpeedStreaks.mat`) draws edge streaks while boosting. FOV stays below ~76°.
- Cozy life zoom: above `framingKneeRadius` the chase distance grows with sqrt(radius); after `idleDelay` s without
  steering the camera eases to `idleCloseIn` of the distance (default zoom only); zoomMax grows by the same factor so
  the full overview stays reachable.
- Watch: flocks within `smoothDistance` of the camera live in their own mesh `FlocksNear`, rebuilt every LateUpdate
  with a CPU wing pose (24 poses per variant) — the 15 Hz rebuild plus the world-position flap phase was the "birds
  jitter while watching". The pick marker is an outline ring only. `WatchFraming` (PhotoRig.cs) gives the follow
  pose: aim at the animals' middle, `watchPitch` 55°, distance by `watchFill`/`watchMinBodyPixels`, screen lift below
  the HUD, a one-off yaw pick with the fewest plants in the sight line.
- Adventure pacing: ring plate rows 160 long (`ringPlateLength`, `ringRowDrift` 0.12) so a lane boundary lasts 6–8 s;
  `RingWorld.extraIslandsPerLevel` and ring volcanoes (`RingIslandSpawner.TryRaiseVolcano`, rising 95–150 u ahead,
  a free gap of >= 14 u always kept, `VolcanoRising` event); `lifeHideDistance` 90 on ring islands. Escort animals give
  `Island.EscortFactor` (steady pace) instead of a permanent boost; flotsam spacing 72–120, boost x1.65. Hit grace:
  `Island.hitGrace` 2.2 s glide-through plus `hitSidestep`.
- HUD: adventure = one slim strip (152 of 1920) with timer/status/best time and an "Auftrieb" bar + level; cozy panel
  232 high with labelled bars and a tap-to-explain card (PlayerPrefs `drift_hud_explained`). New-world confirmation
  in SessionScreens whenever a cozy save exists. `SeaKind.Seal` (id 120) with catalog entry, glyph, tap and album scan.
- Readability (ring): `CloudShadows` keeps a cloud-free corridor ahead of the player; flotsam beacons
  (`ShipSystem.Beacons.cs`, `FlotsamBeacon.shader`); storms get a moonlit rim at night; `RingRim.shader` foam is
  continuous in world z (the lane gaps aliased into horizontal stripes). Storm billboards 216 on mobile, no GC.
- GPU: water pixels in full haze return early, current flecks and cloud shadows exit early, cloud shadow per vertex
  for terrain/plants/animals/critters/fish, terrain in the AlphaTest queue (its clip), CoastField rebuilds for
  sink/mountain bumps at most every `coastRebuildInterval` (2 s). `CurvedWorld.extendFarClip` raises the far plane to the
  limb (a long Pangäa put the camera past 1000 u = "pale blue screen"); haze starts after the camera distance
  (`hazeClearView`); `DayNightCycle.dimWaterAtNight` darkens shallow water/foam at night (`_WaterLight`).

## Phone build and first device test (2026-09-23, v0.6.2)

- Android: `com.drift.game`, portrait only, IL2CPP ARM64, Vulkan/GLES3, the Mobile quality level. The only build scene
  is `Planet.unity`. The icon comes from `Scripts/Editor/AppIconMaker.cs` (menu "Drift/App-Symbol …"), which renders
  the start island. The APKs go to `Builds/Android/` and a copy to `../Drift-APK/`. The active build target is Android.
- Shaders that code finds with `Shader.Find` are only built when a material references them:
  `Resources/ShaderRefs/Ref_*.mat` holds one per Drift shader, and `ShaderBuildTests` guards it.
- Adventure score = distance: `RingWorld.RunDistance` counts new ground along the track while the race runs;
  `SessionStats.distance`; `BestDistances` (file `drift_best_times.json`, new key); levels by `levelDistance`
  (340 m, each later level longer by its pace gain, so the timing matches the old 45 s steps).
  `GameSession.StartHold`/`RingWorld.StartHeld` pin the island and the obstacles while Tilda's briefing runs.
- Race camera: `IslandChaseCamera.ringLateralFollow` keeps the camera close across the track (portrait width).
  The ring sea strip is refined near the focus (`WaterFollower.ringNearRows`/`ringNearSubdiv`/`ringColumnSpacing`).
- Cozy plates: `PlateSystem.cellSize` 102, `gridPeriod` 3 (3x3 plates in the 306 u world). Islands feel
  `CarryVelocity` = `interiorCurrentShare` of the plate motion, capped at `interiorCurrentCap` x their top speed;
  boundaries get `cozySurfBoost`. The ring keeps the full carry.


## v0.6.3 additions (2026-09-24)

- **Screen stays awake**: `PlatformSetup` sets `Screen.sleepTimeout = NeverSleep` on mobile (tilt/watching never touch the screen).
- **Chase camera hand-over**: `PangaeaFinale.HandOffChase/HandBackChase` record and restore the chase camera's enabled state;
  `GameSession.SnapCamera()` re-enables a disabled chase camera outside the finale. (The finale used to leave it off, so every
  later run had a frozen camera.)
- **Tilt keeps the view still**: `IslandChaseCamera.HoldCourse` (set by `SessionScreens` while the tilt steers) suspends the cozy
  course swing, so a physical tilt always names the same screen direction (`StepYaws`).
- **Pangäa free flight**: `FlyOverCamera` moves the camera itself (stick/tilt/WASD fly, drag turns the view in place, pinch/Q-E
  height 3..max(60, 2.5 r), reach 1.25 r + 30); settings live in `PangaeaFinale.freeFlight`.
- **Milestones are watchable**: `MilestoneToasts` toasts are tappable while their landmark exists (lighthouse, harbour jetty,
  seabird flock, festival ground/village) and open `WatchTools` with a still-subject framing (`stillPitch`, `stillFill`, slow orbit).
- **Notices +3 s**: news 7 s, first sightings / "weitergezogen" 5.6 s, animal card 7 s, milestone toast 8.5 s.
- **TapSparkles** (`Bridge/TapSparkles.cs`, on SessionUI): every ~3.5 s a screen-space star glint on one tappable subject in view
  (per-subject cooldown 12 s); shares the tap candidate list with `WatchSubjects`, which now also covers flocks, dolphins,
  turtles, rays, surfaced whales and nearby lighthouses/docks.
- **Adventure**: every ring boundary always surfs at full strength (`ringCalmSurf` removed; push = surfSpeed * ringSurfBoost +
  surfPlateGain * ringSlideFull); `RingWorld.sinkSpeed` 1.15 on top of the level scaling; flotsam lines use
  `trackLineStep` 7 and `trackScatter` 2.5 (lateral and ±30 % along-line jitter).


## v0.6.4 additions (2026-09-24)

- **Merge body turn tuned down**: `Island.bodyTurnAmount` (1/3) scales the chosen 90..180 deg; times 5/10 s, rate caps
  35/17.5 deg/s, `bodyTurnMaxQueued` 40. Tests pin the algorithm at full scale and the defaults separately.
- **Small islands**: `WorldStreamer.smallIslandAreaScale` (2) multiplies small/islet radii by its square root.
- **Species pool per run** (`Life/SpeciesPool.cs`): drawn from the world seed + album (unfound species weigh more),
  rarity tiers Common/Occasional/Rare drive both pool membership and herd spawn weights (3/1.5/0.8); cozy only
  (Adventure/Edit Mode/tests: no pool = all species). Persisted in the run save (`speciesPool`). Start island's first
  herd = the start species (Hare or Sheep). Critters outside the pool don't spawn. Median album completion 8 runs.
- **Herd meetings** (`Life/IslandHerdSystem.Meetings.cs`, `Errand.Meet`): Greet, Tag, Shove, Trek, RingDance; natural
  trigger `meetRate`/`meetRange`, cooldowns, max 2 per island, day only; `MomentKind.Meeting`,
  `LifeNudge.HerdMeeting` in the director.
- **Watch turns with the island** (`WatchTools.watchTurnWithIsland`, `watchSubjectEase`); merge re-centering shifts the
  stored herd focus. Campfires are tap/sparkle targets (`TapTargetKind.Campfire`) with a village still-subject framing.
- **Finale fly-over ignores tilt** (`SessionScreens.ReadFlyScreen`), stick always shown.
- **Ambient fish layer** (`Visuals/FishSystem.Ambient.cs`, `FishShapes.cs`): cozy-only camera-following schools
  (`cozyAmbientMin` 8 in view), 7 new kinds, one mesh/material with the existing fish, view-distance scale up to 2.4.
- **Adventure momentum** ("Schwung", `RingWorld.momentum*`): 0..1, +0.13 per boost pickup (+0.26 whale), surf feed,
  half-life 10 s, hit halves; top speed/cruise/steer/thrust/surf scale up to x2. HUD bar under Auftrieb.
- **Whale boost**: 4 s x2.5 + ghost through obstacles (`Island.GhostPassing`, extended while overlapping), whale swims
  at the island's flank then dives (`SeaLifeSystem.whaleBoost*`, `Encounters.whaleBoost*`, event `WhaleBoostStarted`).
- **Audio**: surf cue is band-passed noise (`SfxSynth.SurfNoiseLevel`) instead of a pitched tone (was heard as a motor);
  `Audio/AdventureMusicSynth.cs` (tropical, 112 BPM, +4 %/1000 m to +30 %, stage changes on the 4-bar line with a
  fill), crossfaded by `AudioDirector` while `AdventureRunning` (set by `SeaAudioBridge`).


## v0.6.5 additions (2026-09-25)

- **Whale boost** x2 / 3 s (`Encounters.whaleBoost*`); `SeaLifeSystem` rebuilds every frame while an escort swims
  fast (`EscortEveryFrame`), places the boost whale from the island's same-frame position, switches sides every third
  of the boost with a dive under the island (`whaleBoostCrossSeconds`), tucks behind the stern at the band edge.
- **`TapSparkles.SparkleAt(world, scale, anchor, seconds)`**: the tap glint anywhere in any mode (pool of 8); the
  island sparkles during the ghost ride (`whaleSparkle*`). Positioned in LateUpdate (order 900) after the camera.
- **Idle gestures** (`Life/IslandHerdSystem.Idle.cs`, `IdleAction`): per-animal timers on their own random stream,
  near LOD tier only; body tilts pivot about a pair of feet; `AnimalShownPose` includes them. `idleRate` 0 = off.
- **Gait-steady pose changes**: `AnimalPose.gaitSteady` encodes UV1.x as moving+2 when the change at t0 kept the gait,
  so `Animal.shader` skips its 0.25 s walk blend (hares used to hop on every head turn).
- **Start island = one herd** until its first merge (`DesiredHerds`).
- **Steady animal card** (`WatchTools.popupSteady`, dead zone 110x80, 0.35 s follow, pixel snapped).
- **Journal**: no "Ansehen" icon; 100 px photo-task slot with its own tap target and a task detail overlay.
- **`SaveSystem/JournalReset.cs`**: `Run(scope, targets)`; scopes species+tasks or everything (photos, chronicle).
  One-time full reset before the first scene load, PlayerPrefs `drift_reset_v065`; the best-distance file is kept.
- **New world per run** (`WorldSeeds`): the title proposes a fresh seed after every run; typed seeds are exact.
- **Harbour** (`Life/SettlementMeshes.Harbour.cs`): fisherman's hut on stilts + props next to the jetty's land end,
  reserved circle, re-derived from the terrain; `IslandSettlementSystem.TryGetHarbourView` feeds the watch framing.
