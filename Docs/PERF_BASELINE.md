# Drift — Performance Baseline and Budget (Phase 0, 2026-09-20)

Measured in the live Editor (Unity 6000.6.2f1, URP, quality level *PC*, `PC_RPAsset`) with
`Assets/_Drift/Scripts/Editor/PerfBaseline.cs` driven from `unity command eval`, never in Play Mode.
Every number below is **desktop** unless it says *phone estimate*. Re-run with
`return Drift.EditorTools.PerfBaseline.Systems();` (also `PerIsland()`, `Meshes()`, `Materials()`,
`Memory()`, `RenderStats()`, `BuildBigIsland(600)`, `RestoreWorld()`).

## Method

- CPU: `Stopwatch` around each public step method, 60 iterations at `dt = 1/60` after 3 warm-up calls,
  reported as average / min / max per call. Averages therefore *include* the amortised mesh rebuilds
  (herds 15 Hz, vegetation on dirty, plates 30 Hz, fish 15 Hz, flocks every frame). Private rebuilds
  were called through delegates so their cost is also listed on their own.
- Managed allocation: neither `GC.GetTotalMemory` nor `GC.GetAllocatedBytesForCurrentThread` moves in
  this Mono, so the profiler counter *GC Allocated In Frame* was recorded per editor frame and the
  frame of a 2000-call loop compared against an empty loop. Validated: 2000 × `new byte[1024]` reads
  2,136 KB (1,068 B each). Frame noise is ±30 KB, i.e. the resolution is **~15 bytes per call**.
- Rendering: renderers were walked through the transform hierarchy (DontSave children included),
  tested against the main camera frustum; `UnityEditor.UnityStats` was read after focusing the Game
  view and forcing a repaint (with the Game tab hidden behind the Scene view the stats never update,
  and `Camera.main.Render()` from eval only produces URP render-pass errors).
- Worlds: (a) the start world (seed 12346, player at (0,0), area 24, 32 islands in 9 chunks, 12 of them
  within the 100 u simulation distance); (b) a **stress island** built by merging the 13 largest loaded
  AI islands into the player (`MergeFrom` + `FinishUplift`, guests parked on a golden-angle spiral),
  area 611, then `IslandLifeSystem.Simulate(600)` and 4 s of herd steps. Because guests were placed by
  bounding radius the result is a sprawling 99 × 105 u body; a real drive-in merge chain would be
  more compact, so its terrain figure is an upper bound.

## (a) Start world — per-frame CPU (desktop)

islands=32, near(<100 u)=12, herds=139, animals=907, plants=2336, birds=42, fish ~90.

| Call | avg ms | min ms | max ms | alloc/call |
|---|---:|---:|---:|---:|
| IslandHerdSystem.Step (all islands) | 0.145 | 0.054 | 1.160 | 0 |
| IslandHerdSystem.Step (player) | 0.010 | 0.002 | 0.062 | 0 |
| IslandHerdSystem.RebuildMesh (player, 23 animals, 1.4k verts) | 0.026 | 0.025 | 0.069 | 0 |
| IslandLifeSystem.Step (all islands) | 0.104 | 0.014 | 0.824 | growth only¹ |
| IslandLifeSystem.Step (player) | 0.008 | 0.001 | 0.091 | 0 |
| IslandLifeSystem.Tick (player, ldt 1.2) | 0.002 | 0.001 | 0.007 | 0 |
| IslandLifeSystem.RebuildVegetationMesh (player, 95 plants, 3.3k verts) | 0.040 | 0.039 | 0.083 | 0 |
| IslandLifeSystem.ApplyTint (player, 676 terrain verts) | 0.049 | 0.049 | 0.072 | 0 |
| FlockSystem.Step (42 birds, rebuilds every frame) | 0.111 | 0.107 | 0.164 | 0 |
| FlockSystem.RebuildMesh | 0.108 | 0.106 | 0.149 | 0 |
| FishSystem.Step | 0.010 | 0.001 | 0.075 | 0 |
| FishSystem.Rebuild | 0.031 | 0.031 | 0.068 | 0 |
| WaterFeedback.Step | 0.003 | 0.002 | 0.024 | 0 |
| CloudShadows.Step | 0.000 | 0.000 | 0.007 | 0 |
| PlateSystem.Step (borders at 30 Hz) | 0.129 | 0.035 | 1.304 | 0 |
| PlateSystem.ComputeBorders | 0.086 | 0.086 | 0.100 | 0 |
| PlateSystem.RebuildBorderMesh | 0.039 | 0.038 | 0.082 | 0 |
| StormSystem.Step | 0.000 | 0.000 | 0.001 | 0 |
| IslandWorld.Step (collisions, 32 islands) | 0.017 | 0.017 | 0.024 | 0 |
| WorldStreamer.StreamAround(false) | 0.002 | 0.002 | 0.020 | 0 |
| Island.Tick (all islands) | 0.041 | 0.040 | 0.075 | 0 |
| Island.Tick (player) | 0.001 | 0.001 | 0.017 | 0 |
| Island.AdvanceSink (player, mesh refresh every 0.5 s) | 0.002 | 0.000 | 0.097 | 0 |
| Island.RebuildMesh (player) | 0.035 | 0.034 | 0.060 | 0 |
| Island.RecomputeStats (player) | 0.007 | 0.007 | 0.009 | 0 |
| **Sum of per-frame steps** | **≈ 0.56** | | | |

¹ `IslandLifeSystem.Step (all)` allocated ≈ 120–160 KB over 2000 calls (33 s of real time, 200
life-seconds): that is `new Plant` for plants that grew in (≈ 70 B each), a discrete simulation
event, not per-frame garbage. Everything else is 0 within the 15 B/call resolution.

Per-island (start world, the near ones): a 130-area island (18 herds, 120 animals, 358 plants) costs
Herd.RebuildMesh 0.129 ms (7.5k verts), Life.RebuildVeg 0.170 ms (9.0k verts), Life.Tick 0.009 ms.
Islands beyond `simDistance` cost 0.000–0.001 ms per step. Full table: run `PerIsland()`.

One-off costs: `ResetWorld()` + `StreamAround(true)` for 31 islands 15.5 ms (+9 MB managed);
flock reseed 1.5 ms.

## (b) Stress island (area 611) — per-frame CPU (desktop)

islands=19, near=3, player: 102 herds / 663 animals (40.8k herd verts), 774–1,261 plants
(58.7k vegetation verts), terrain 41,790 verts (99 × 105 u rectangle, 94 % of it sea floor),
bounding radius 57.8 so the chase camera sits 111 u up at zoom 1.

| Call | avg ms | min ms | max ms | alloc/call |
|---|---:|---:|---:|---:|
| IslandHerdSystem.Step (all islands) | 0.267 | 0.140 | 1.201 | 0 |
| IslandHerdSystem.Step (player) | 0.247 | 0.129 | 1.006 | 0 |
| IslandHerdSystem.RebuildMesh (player, 663 animals) | **0.694** | 0.677 | 0.944 | 0 |
| IslandLifeSystem.Step (all islands) | 0.127 | 0.010 | **3.041** | 0 |
| IslandLifeSystem.Step (player) | 0.121 | 0.015 | 3.064 | 0 |
| IslandLifeSystem.Tick (player, ldt 1.2, ~430 land cells) | 0.047 | 0.040 | 0.092 | 0 |
| IslandLifeSystem.RebuildVegetationMesh (player) | **0.971** | 0.947 | 1.393 | 0 |
| IslandLifeSystem.ApplyTint (player, 41.8k terrain verts, every 0.5 s) | **3.020** | 2.973 | 3.998 | 0 |
| FlockSystem.Step | 0.110 | 0.107 | 0.172 | 0 |
| FishSystem.Step | 0.010 | 0.001 | 0.080 | 0 |
| WaterFeedback.Step / CloudShadows.Step | 0.003 / 0.000 | | | 0 |
| PlateSystem.Step | 0.100 | 0.019 | 0.900 | 0 |
| StormSystem.Step | 0.000 | | | 0 |
| IslandWorld.Step | 0.017 | 0.017 | 0.028 | 0 |
| WorldStreamer.StreamAround(false) | 0.002 | | | 0 |
| Island.Tick (all islands) | 0.025 | 0.024 | 0.070 | 0 |
| Island.AdvanceSink (player) | 0.081 | 0.000 | **2.494** | 0 |
| Island.RebuildMesh (player, 41.8k verts, every 0.5 s while sinking) | **2.020** | 1.369 | 2.121 | 0 |
| Island.RecomputeStats (player) | 0.314 | 0.296 | 0.405 | 0 |
| **Sum of per-frame steps** | **≈ 0.74** | | worst frame ≈ 6.5 | |

Event costs on this island: `MergeFrom` + `FinishUplift` ≈ 8 ms per merge (105 ms for 13, growing
with area); `Simulate(600)` 36 ms, so the 1800 life-second offline catch-up cap is ≈ 110 ms.

Mesh rebuild costs scale linearly with vertices: ≈ 17 ns/vertex for herds and vegetation
(`TemplateBatch`), ≈ 48 ns/vertex for the island terrain (`FillMesh` + `SetColors`),
≈ 72 ns/vertex for `ApplyTint` (bilinear `TintAt` per terrain vertex plus `SetColors`).

## Rendering

UnityStats of the Game view (1537 × 805 editor Game view, Forward+ on the PC renderer; on the Mobile
renderer it is plain Forward with render scale 0.8):

| World / zoom | draw calls | SRP-batched | standard | null-geometry (blits) | SetPass | triangles | vertices | renderers in frustum |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| start, zoom 1 | **16** | 6 | 7 | 3 | 11 | 6,889 | 18,861 | 7 |
| start, zoom 3 (max) | — | | | | | | | 13 |
| start, zoom 0.32 (min) | — | | | | | | | 7 |
| stress island, zoom 1 (camera 111 u up) | **42** | 37 | 2 | 3 | 11 | 52,426 | 153,077 | 38 |
| stress island, zoom 3 | — | | | | | | | 42 |
| stress island, zoom 0.32 | — | | | | | | | 10 |

Each renderer is exactly one draw call: all four shaders (`Drift/IslandTerrain`, `Drift/VertexColor`,
`Drift/Water`, `Drift/Fish`) have a single `UniversalForward` pass, no `ShadowCaster` and no
`DepthOnly` pass. Consequences: nothing casts shadows (`shadowCasters = 0`) although both RP assets
enable main-light shadows (Mobile: 1 cascade, 50 u, shadow map allocated and cleared every frame for
nothing); the required depth texture is produced by URP's copy-depth pass after opaques (one extra
full-screen pass on phones). The 7 "standard" draws in the start world are skybox, the water plane
(its `MaterialPropertyBlock` takes it out of the SRP batcher, harmless for one renderer) and editor/UI
overlays; the 3 null-geometry draws are URP blits.

Scene mesh inventory (all loaded islands, start world): 100 renderers, 163.9k verts / 58.4k tris:

| Mesh type | renderers | total verts | total tris | largest single mesh | shader |
|---|---:|---:|---:|---|---|
| island terrain | 32 | 25,338 | 11,774 | 2,809 verts (area 130) | Drift/IslandTerrain |
| vegetation | 32 | 73,209 | 24,403 | 9,021 verts (358 plants) | Drift/VertexColor |
| herds | 32 | 55,824 | 18,608 | 7,506 verts (120 animals) | Drift/VertexColor |
| flocks | 1 | 7,560 | 2,520 | 7,560 (42 birds, 180 verts each) | Drift/VertexColor |
| fish | 1 | 651 | 279 | 651 (7 verts per fish) | Drift/Fish |
| plate borders | 1 | 1,224 | 612 | 1,224 | Drift/VertexColor |
| water | 1 | 121 | 200 | 121 | Drift/Water |

Stress island single meshes: terrain 41,790 verts, vegetation 53.8–58.7k verts (46 verts per plant at
old growth), herds 40,806 verts (61.5 verts per animal). Per-template sizes: hare/sheep 60 verts,
goat/ox 78, tree 40–60, bird 180.

Materials: every material is SRP-batcher compatible; **GPU instancing is used nowhere**
(`enableInstancing` false, no `multi_compile_instancing`), which is fine while every system is one
dynamic mesh. Mobile RP asset: SRP batcher on, dynamic batching off, MSAA off, render scale 0.8,
**HDR on** (a bandwidth cost worth reconsidering on budget phones).

## Memory

| Item | start world | stress island |
|---|---:|---:|
| Managed heap in use (`Profiler.GetMonoUsedSizeLong`, editor-inflated) | 92.7 MB | 93.2 MB |
| World mesh memory (`GetRuntimeMemorySizeLong` over loaded meshes) | 7.2–12.6 MB² | 7.5–13.9 MB² |
| of which herds / terrain / vegetation | 2.5–4.2 / 1.2–2.1 / 3.1–5.5 MB | 2.0–3.8 / 2.0–3.9 / 2.9–5.3 MB |
| Live `Mesh` objects (`Resources.FindObjectsOfTypeAll`) | 13,877 | 13,887 |

² The runtime size of a dynamic mesh doubles right after an upload (CPU copy still resident), hence
the range. Live Mesh objects are almost entirely editor noise: 4,203 "Vegetation", 3,591 "Herds" and
4,540 "IslandTerrain" meshes (1.95 GB) were already alive before this session (left behind by earlier
Play Mode sessions, where `Destroy` is deferred and frames never advance under automation). The
reload path itself is leak-free: `ResetWorld()` removes exactly 31 meshes per type and
`StreamAround(true)` adds them back, and the counts were identical at the end of the session.
Managed growth from the start world to the stress island is 0.5 MB.

## Measured on this desktop vs. estimated for a budget phone

**Measured here:** every CPU time and allocation above, vertex/triangle/renderer counts, draw-call
counts of the editor Game view, mesh memory. **Estimated:** all phone times. Assumption: a budget phone
(Mali-G52/Adreno 610 class, ~2 GHz A53/A55) runs this single-threaded C# **8–10× slower** than this
machine, so multiply desktop ms by ~9; GPU figures cannot be derived from the desktop at all (fill
rate, bandwidth for HDR + depth copy, render scale) and the real frame time, thermal throttling and
memory footprint **need the owner's device** with a development build and the Profiler attached.

Phone estimates: start world ≈ 4.5–5.6 ms of simulation per frame; stress island ≈ 6–7.5 ms average
but a **50–65 ms worst frame** every 0.5 s when `ApplyTint` (27–30 ms), the herd rebuild (6–7 ms) and
the sinking island's `RebuildMesh` (18–20 ms) coincide, i.e. a dropped frame at 30 fps. Flocks alone
are ~1 ms per frame on a phone because the bird mesh is rebuilt every frame. A merge costs ~70–80 ms
(hidden by the slow-motion beat), the offline catch-up up to ~1 s at load.

## Budget (30 fps on a budget phone, 33.3 ms frame)

Reserve ≈ 12 ms for URP rendering and driver, ≈ 4 ms for engine overhead, UI and audio
(`AudioDirector` ≈ 1–2 % of a core), ≈ 4 ms headroom, leaving **≤ 12 ms for simulation including
all dynamic mesh rebuilds**. Desktop equivalents assume the 9× factor and are what `PerfBaseline`
is checked against; "worst" is the most expensive single frame, so periodic work must be amortised
or spread, never bunched.

| System (all loaded islands) | phone avg | phone worst frame | desktop avg / worst |
|---|---:|---:|---:|
| Herds: `IslandHerdSystem.Step` incl. mesh | 2.5 ms | 4.0 ms | 0.28 / 0.45 |
| Plants: `IslandLifeSystem.Step` incl. tick, tint, mesh | 2.0 ms | 4.0 ms | 0.22 / 0.45 |
| Flocks incl. mesh | 1.0 ms | 1.5 ms | 0.11 / 0.17 |
| Fish incl. mesh | 0.3 ms | 0.5 ms | 0.03 / 0.06 |
| Plates + storms + borders | 1.0 ms | 1.5 ms | 0.11 / 0.17 |
| Islands: `Tick`, `IslandWorld.Step`, sink/emerge refresh, streamer | 1.5 ms | 4.0 ms | 0.17 / 0.45 |
| Water, clouds, day/night, impact feedback | 0.3 ms | 0.5 ms | 0.03 / 0.06 |
| New creatures (Phase 4, all together; measured 2026-09-20: `IslandCrittersSystem.Step` over 32 islands 0.019 ms avg / 0.12 worst, +2 seabird flocks inside the flock row, `PerfBaseline.Critters()`; fireflies on every island since 2026-09-20: 45 islands 0.016 ms by day, 0.023 ms at night with 63 wandering fireflies on 4 near islands and 399 baked halos, 0 B/frame, near-tier glow re-bake 0.006 ms at 12 Hz, mid/far tier bake once; +1 additive draw call per visible island at night, +1 per settled island for the window halos) | 1.5 ms | 2.5 ms | 0.17 / 0.28 |
| Life sounds (Phase 6, on top of the synth; measured 2026-09-20 in the Editor: `LifeSynth` 0.36 ms per second of audio idle, 3.4 ms day, 5.0 ms night, 5.7 ms with every voice on = 0.57 % of a core, i.e. ≈ 0.095 ms per 60 Hz frame on the audio thread; scout 0.5 Hz on the main thread) | 0.5 ms | 0.5 ms | 0.06 / 0.06 |
| **Total simulation** | **≤ 10 ms** | **≤ 14 ms** | 1.1 / 1.6 |
| Any single call in steady state | | ≤ 9 ms | ≤ 1.0 |
| Events: merge / world load / offline catch-up | ≤ 100 ms / ≤ 300 ms / ≤ 1.5 s | | 11 / 33 / 170 |

- **Draw calls < 100** total (UnityStats `drawCalls`, blits included), ≤ 60 scene renderers in the
  frustum at maximum zoom, SetPass ≤ 20. Today: 16 / 42.
- **Zero steady-state GC:** 0 bytes per frame from every stepped system (resolution 15 B/call); heap
  allocation is allowed only for discrete events (a plant or animal spawning, a merge, a load) and
  managed growth over a 10-minute session must stay under 2 MB.
- **Vertices:** ≤ 250k vertices in the frustum per frame, ≤ 300k resident across loaded islands.
  Per mesh: island terrain ≤ 45k, vegetation ≤ 60k per island, herds ≤ 10k per island, flocks ≤ 8k,
  fish ≤ 1k, plate borders ≤ 2k. Any new creature type: one mesh per system, ≤ 8k vertices.
  Animal detail LOD (2026-09-20): the herd mesh of an island with animals within 12 u of the camera may carry up to
  40 detailed animals / 10.4k detail vertices on top (`IslandHerdSystem.maxDetailed`, `maxDetailVertices`), i.e.
  ≤ ~16k for that island (measured: 39 detailed of 120, 15.4k verts, rebuild 0.38 ms desktop ≈ 3.5 ms phone estimate);
  every other island keeps the ≤ 10k cap.
- **Caps:** per island 40 herds / **120 animals also after merges** (today a merged island carries
  every guest herd: 102 herds / 663 animals / 40.8k verts), `maxPlants` 3000 but the vegetation mesh
  must stay ≤ 60k vertices (≈ 1,300 old-growth plants: cheaper far templates or a plant LOD are
  needed before the cap is real); world-wide ≤ 1,000 simulated animals (start world: 907), ≤ 300 in
  the near tier with full behaviour, ≤ 54 birds, ≤ 120 fish.
- **Mesh rebuild rates:** herds ≤ 15 Hz near / 1 Hz hidden, vegetation ≤ 7 Hz, flocks ≤ 15 Hz (today
  every frame), fish ≤ 15 Hz, plate borders ≤ 30 Hz, island terrain only on real shape changes and
  never a full 2 ms desktop rebuild for a sink step.
- **Memory:** world mesh memory ≤ 16 MB, total app footprint on the phone < 300 MB (owner's device).

Items that already break the budget and belong to Phase 1: `ApplyTint` on big islands (3.0 ms desktop
≈ 27 ms phone; tint per life cell instead of per terrain vertex, or move the blend to the shader),
`Island.RebuildMesh` + `RecomputeStats` every 0.5 s while sinking (2.3 ms ≈ 21 ms phone; touch only
the shoreline band or amortise), herd/vegetation meshes on merged islands (enforce the caps, then move
animation to the vertex shader so rebuilds only happen on position changes), flocks rebuilt every
frame, and the 94 %-sea terrain rectangle of a sprawling merge.
