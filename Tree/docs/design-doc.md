# Tree: Design Doc (draft v1.3, 2026-09-27)

Working title: **Tree**. UI language: English. Status: planning. Engine: Godot 4. Target: Android only for now (APK, F-Droid style stores), minimum a Fairphone 6 class phone (2022 or newer, Vulkan). Developed and prototyped on PC.

## Vision
A calm idle game about one realistic tree on a peaceful meadow. It grows through sunlight and nutrients. Two linked modes:
- **Tree (upper world):** the tree grows procedurally and produces **life force**.
- **Roots (underground):** the player steers a root through a space-like void to collect **nutrients**, which cost life force.

The two modes feed each other. The in-game day drives them: daytime is tree mode, night is root mode (see Pacing). The dive happens only at sunset; sunrise brings the player back up.

## Decisions so far
| Topic | Decision |
|---|---|
| Visual style | Realistic 3D, photorealistic is the goal (confirmed in survey 2 after seeing four style mockups in concepts/styles/). The journal UI sits on top as paper and handwriting. Assets: CC0 textures and skies, see asset-research-2026-09-27.md |
| Growth | Procedural growth, no swapping between models. Self-organizing tree model: space-colonization markers + light + Borchert-Honda nutrient allocation + pipe model for thickness (see research-tree-growth.md) |
| Offline growth | Keeps growing, but much slower. First guess: 1 real day closed = about 20 s of active game time (to be tuned) |
| Sun | Natural sunrise to sunset cycle. The player "activates" the sun: it shines brighter and boosts growth (to be prototyped) |
| Long-term goal | Prestige unlocks new tree species. No forced end: you can stop whenever you want |
| Realism of size | Each species is capped at its real-life size and shape |
| First species | Linden (Tilia, "Linde"): broad, dense crown, heart-shaped leaves, golden-yellow in autumn, up to about 30–40 m |
| Sun steering | Timing is the steering, in all three dimensions (decided 2026-09-27). The sun follows a real arc: it rises in the east, stands in the south at noon (Germany), sets in the west. Boosting in the morning grows the crown east, at noon south and upward, in the evening west. A low sun grows the tree sideways, a high sun grows it taller. The north side gets less light by nature, as with real trees. Later, the seasons tilt the arc (low in winter, high in summer) |
| Game loop | Short back and forth. Root runs are short because the life force from the tree phase always limits them |
| Persistence | Superseded on 2026-09-26: a tree lives about one month (about 30 cycles), then moves to the gallery. The early idea of one tree over years is dropped |
| Mode switch | Tap the ground and the camera dives down into the dark root world (and back up) |
| Language | GDScript. Heavy math may move to GDExtension (C++) later if needed |
| Wallpaper | Wanted (confirmed): the current tree as a live Android wallpaper. Feasible via the TheOathMan/Godot-Android-Live-Wallpaper plugin. Later milestone |
| Start / tutorial | A seed is planted. The first root run happens from the seed, and the nutrients from that run grow the first sapling |
| Feedback on growth | Parts that are currently growing twinkle a little |
| Sound | Very important from the start. Peaceful overall: wind and birds above, a deep calm hum below |
| Pacing | About one month of casual play per tree. One cycle is one in-game day: daytime is tree mode, night is root mode. Day plus night take at most 10 minutes of real play, about one cycle per real day. So a linden reaches full size in roughly 30 cycles, and each day must show visible growth |
| End of a tree | The finished tree goes to a gallery, and the next game starts with a new seed |
| Notifications | Yes, at most one per day (for example: a root run is affordable, or the tree has grown). Local notifications only, no push service |
| Tree mode actions | Timing the sun, and pruning. Watering is out: water only comes from the roots |
| Pruning | Cut a branch and the tree redirects its resources to the rest. Realistic use: remove a shaded or dying branch, or shape the crown. Adds a light choice without breaking the calm |
| Soft failure | The tree never dies, but it reacts: too little water makes leaves droop and drop, branches with too little light die back (both come from the growth model). Pruning dead branches is the fix |
| Orientation | Portrait. The tree fills a tall screen, and it doubles as the phone wallpaper |
| Seasons | Later, not in the first prototype. When added, they follow the real calendar at the player's location (default: Germany). Plan the data model for it now |

## UI and interaction decisions (survey, 2026-09-27)
| Topic | Decision |
|---|---|
| HUD in tree mode | Small pills: day, life force, four resources (as in the mockups) |
| Boost | Hold anywhere on the screen. While boosting is the only tree action that works. Once pruning exists, a small tool switch sets the mode (boost or prune) |
| Root run end | A run ends only when life force is used up. No stop button. Each night spends everything |
| Pruning UI | A journal page with a handwritten explanation the first time; later just a scissors mark on the branch |
| Look | The whole UI is a stylized journal (decided 2026-09-27): torn-out pages for smaller menus, handwritten notes with drawings and scribbles. Menus and HUD should feel like one notebook, animated where it helps. Handwriting fonts: Caveat, Patrick Hand, Kalam (all OFL, self-hosted). Keep the "no UI" toggle (pure scenery) for testing and screenshots |
| Grove | Walk along your old trees in 3D. The finished trees stand on the meadow around the new one (later milestone; a simple list is the fallback) |
| Clock | Runs only while the app is open, plus slow offline growth. One session is one in-game day |
| Camera | Tree mode: fixed with slow orbit and pinch zoom. Root mode: third person behind the root tip |
| Saving | Local only for now. Backup later |
| Weather | Mood first, decided 2026-09-27: the world must feel alive and cohesive. Butterflies, wildflowers and bushes on the meadow; wind, clouds, rain and snow as atmosphere. No gameplay effect in Prototype 1; small effects (rain adds a little water) only later and only if cheap |
| Money | Free, no ads |
| Root cost | Rises with distance from the trunk and with depth |

## Journal UI (decided 2026-09-27)
- Every menu, card and hint is a page of one notebook: torn-out pages for small menus, full pages for the grove, species and settings.
- Text is handwritten, with small drawings and scribbles (a sketched branch, a crossed-out word, an arrow). Buttons look like circled words or sketched boxes.
- The HUD pills in tree mode stay small; they can be paper labels or stay glass, to be tried in the chosen style.
- Contents of the journal, and whether the game writes a diary line per day, are still open (survey 2).
- Assets: CC0 paper textures and free Godot canvas shaders for wobbly lines exist (see asset-research-2026-09-27.md); the drawings are ours.

## Weather and life on the meadow (decided 2026-09-27)
- Cosmetic in Prototype 1 and after: butterflies, wildflowers, bushes, wind in the leaves and grass, drifting clouds, occasional rain, snow in a later winter.
- Everything follows the same seed and clock so it feels like one living place, not effects layered on top.
- Gameplay effects are optional and later, only if cheap.

## Tree mode
- **Scene:** a single tree on a meadow with rolling hills and a sky (see references/meadow.png, references/tree-models.png).
- **Growth tick:** the tree spends nutrients, and light exposure decides which buds grow.
- **Steering:** the sun moves on its real arc, east to south to west. Boosting in the morning grows the tree east, at noon south and up, in the evening west (decided). Boosting trades life force for growth speed.
- **Output:** life force grows with leaf area and light.
- **Pruning:** tap a branch and confirm to cut it. Its resources go to the remaining branches. Dead or dying branches are marked so the player sees why pruning helps.
- **Stress signals:** drought makes leaves droop and fall, and shaded branches die back slowly. Nothing kills the tree.

## Root mode
- **Scene:** a black void with glowing, colored nutrient dots (see references/roots-space-*.png).
- **Controls:** pick any point on an existing root (or a tip), then steer a new main root in third person. Growing drains life force.
- **Pacing:** runs are short. Each run pushes one root a bit further in one direction, then it's back to the tree.
- **Result:** the path you steered becomes a permanent root. The nutrients you collected go to the tree.
- **Algorithm:** the same space-colonization code as the tree, with the nutrient dots as attractors, for any automatic side-rootlets.

## Resources (biology-based, kept simple)
Four underground resources. All are collected as glowing dots in root mode, and each has its own colour.

| Resource | Colour (proposal) | Real biology | Game effect |
|---|---|---|---|
| Water | Blue, most abundant | Needed for every growth process, carries nutrients, keeps leaves working | Base requirement for any growth |
| Nitrogen (N) | Green | Builds leaves and chlorophyll | More and bigger leaves, so more life force |
| Phosphorus (P) | Warm orange | Energy transfer, root development, flowers and seeds | Cheaper and faster root growth, later flowering |
| Potassium (K) | Violet | Water regulation, wood strength, frost and drought resistance | Thicker, sturdier wood. Later: winter and drought hardiness |

- **Rule (Liebig's law of the minimum):** growth is limited by the scarcest resource relative to the species' needs. Use a soft version so shortages slow the tree down but never block it completely.
- **Species profiles:** each species has a need multiplier per resource. Examples: linden needs moist, nutrient-rich soil, so it wants a lot of water and N. Birch is a frugal pioneer with low needs overall. Willow wants very much water. Pine does well on poor soil and needs little N.

## Underground layout
- **Topsoil (shallow):** most nutrients (N, P) in rich patches with sparse areas between them. Water is moderate.
- **Deeper:** fewer nutrients, more reliable water, and K from weathered minerals near the rocks. Rocks are obstacles to steer around.
- **Branching:** a new run can start anywhere along an existing root, not only at its tips.
- **Controls:** a virtual joystick for now.
- **Main roots vs fine roots:** the player steers only main roots. Fine side roots then sprout on their own around the finished path, using space colonization with nearby small dots as attractors. They are limited to a short radius and a node budget per root. Biologically, fine roots do most of the absorbing, so they collect small dots near the path automatically.
- **Root cost (to tune):** it should go up with distance from the trunk and with depth. The pipe model makes roots near the trunk thicken as more root hangs off them, and deeper soil is harder to grow through.

## Realism on mobile: approach
- A single hero tree and a mostly fixed camera. This is what makes realism affordable on a phone.
- Develop on PC, but use Godot's **Mobile renderer** from day one so nothing is built that cannot be ported.
- The main costs are leaves (alpha overdraw), the grass, and shadows. Budgets: leaf-cluster cards drawn with MultiMesh, instanced grass that fades with distance, one directional-light shadow.
- Assets: CC0 textures and skies from Poly Haven and ambientCG (bark, grass, leaf atlases, HDRI skies); stylized props from Quaternius, KayKit and Kenney if the stylized look wins. Full list with licences in asset-research-2026-09-27.md.

## Target device
The test phone is a Fairphone 6 running /e/OS (Murena, Android without Google). It is a mid-range phone with Vulkan support.
- Distribute the APK directly or through F-Droid-style stores, and avoid anything that depends on Google Play Services (Play Games login, Firebase, AdMob).
- A live wallpaper renders all the time and costs battery. It needs a low-power mode: few frames per second and no shadows.

## Prototype 1 (PC): all basics in one loop
Goal: play the full loop once, from seed to roots to sapling to tree and back to roots. Grey-box visuals are fine, but the growth must already be algorithmic.

Build order, with each step playable on its own. Status 2026-09-27: steps 1 to 4 and 9 are built and tested (71 headless tests); 5 to 8 and 10 are open.
1. **Project setup:** Godot 4 (latest stable), Mobile renderer, GDScript, git. Folders: `tree/`, `roots/`, `shared/` (plant graph, SCA), `audio/`, `ui/`.
2. **Shared plant graph:** nodes with position, parent, radius and age, plus the pipe-model radius update. The tree and the roots both use it.
3. **Tree growth v0:** space colonization toward markers, with a simple tube mesh rebuilt each tick. No light model yet.
4. **Sun:** a day/night cycle (fast in the prototype) and a boost button. Markers are seeded on the side the sun is on, and boosting speeds up growth. The resources are nutrients (spent to grow) and life force (produced by leaves and light).
5. **Root mode:** a black void with glowing nutrient dots. Pick any point on an existing root and fly a new main root in third person. Life force drains per metre, the path becomes a permanent root mesh, fine side roots sprout automatically, and nutrients are collected.
6. **Switching:** tap the ground to dive the camera down, and tap a button to go back up. Both scenes stay loaded, or their state is kept.
7. **Tutorial flow:** a seed, then the first root run, then the sapling grows.
8. **Feel:** a twinkle shader or particles on growing tips, a placeholder ambient sound per mode, and a crossfade on switching.
9. **Save/load:** the plant graphs, the resources and the timestamp. Offline growth is applied on load.
10. **Day/night loop:** the in-game day drives the mode: daytime is tree mode, night is root mode, whole cycle under 10 minutes. Dive only at sunset. Dawn burst, and dragging the sun to shorten the day once nutrients are spent. Cycle count per tree is the month pacing knob.
11. **Read the meadow:** surface hints (rushes, clover, stones, moss) placed from the underground generator, plus the slow orbit camera so the player can study them.

Later milestones: the full light model (shadow grid, Borchert-Honda), realistic assets (bark, leaves, grass, sky), mobile export and a performance pass on the Fairphone, seasons, the live wallpaper, prestige and species.

## Agentic build: rules for the repo
- A `CLAUDE.md` in the repo carries the conventions: Godot version, GDScript style, folder layout, node budgets, no Google services, growth is always algorithmic.
- Simulation and rendering are separate. Plant graph, growth tick, resources and offline catch-up are plain GDScript classes with no scene dependency, tested headless (`godot --headless -s tests/run_tests.gd`, a small custom runner; no GUT). Scenes and meshes only read the graph.
- All randomness comes from one save seed (tree and underground), so every test and bug can be replayed.
- Hard budgets from day one: tree internodes, fine roots per main root, nutrient dots loaded at once.
- Each build step gets acceptance criteria before it starts, its own branch and pull request, and ends playable on the PC.
- Visual judgement (does it look natural) stays with Simon, via screenshots in the thread.

## Not in Prototype 1
Realistic assets, the full light model, seasons, live wallpaper, prestige and extra species, mobile export, sound beyond placeholders, monetization.

## Decided in survey 2 (2026-09-27)
| Topic | Decision |
|---|---|
| Visual style | Realistic 3D, photorealistic is the goal |
| Diving | Only at sunset. The day ends, the player taps the ground, the night begins. Sunrise brings the camera back up |
| Night without life force | A short visit underground without growing, plus a note in the journal. Then morning |
| Day and night split | Up to five minutes of day and three minutes of night as the starting values (480 s cycle, daylight fraction 0.625). The day can be shorter: once the nutrients are spent, the player drags the sun along its arc to move time on. The day includes looking at the tree from all sides with the slow orbit. Simon will tune this while playing |
| Boost limit | None. Holding the screen all day only burns nutrients faster; nutrients cap growth |
| Offline life force | Accrues slowly, at the same rate as offline growth, so the player always returns with a little for the roots |
| Runs per night | One. Pick a start point on an existing root, steer until life force is empty, then morning |
| Depth control | The root sinks slowly on its own. Tap (hold) to dive faster; the joystick steers sideways and forward |
| Rocks | Hard walls, steer around them |
| Visibility in the void | Nearby dots glow, distant ones are faint, like a soft fog |
| Water upkeep | Leaves drink water every day. Too little and they droop, so the roots must keep finding water |
| Tree finished | When it reaches its species size (about a month for the linden). Then it moves to the gallery |
| Species unlock | Finishing one tree unlocks one species. Order: linden, birch, willow, oak, pine |
| Notification | One calm line per day about the tree ("your linden grew a new branch", "the roots are ready") |
| Pruning cost | Free. The cut wood is gone, its resources go to the rest of the crown |
| Sound | Ambience only: wind, birds and insects above, a deep hum below. No music |
| Journal contents | Diary (the game writes one line per day, the player can add a note), grove, species, settings |
| Platforms | Android only for now. iOS later, if ever |
| Minimum phone | Fairphone 6 class, 2022 and newer, Vulkan mid-range, Mobile renderer |

## Feel additions (decided 2026-09-27, from game-feel-review-2026-09-27.md)
All eight suggestions of the feel review are in. The play-test plan in that file is the plan for every prototype step.
| Addition | What it is | Where it lands |
|---|---|---|
| Read the meadow | Surface plants hint at what lies below, placed by the same seeded generator: rushes and a damp patch over water, clover and nettles over nitrogen, a scatter of stones over rock, moss on the north side. The player studies the meadow by day (orbit camera) to plan the night's root run | Prototype 1, step 5 (underground generator) and a new step 11 |
| Dawn burst | Part of the growth bought by the night's nutrients is held back and released in the first ten seconds after sunrise, with the twinkle, while the camera rises. Daily total unchanged | Step 10 (day/night loop) |
| Boost as a trade | Boosting grows faster but converts nutrients into less life force. Holding all day shapes a bigger tree, a calm day fills the night's tank. No limit, no UI | Step 4 tuning, in the growth sim |
| Let the day pass | Once nutrients are spent the sun glows softly; dragging the sun along its arc moves time on, so a day is two to five minutes as the player likes. Dragging elsewhere orbits the camera. Night keeps its length | Step 10 |
| Visitors as milestones | Butterflies at the first leaves, bees at the linden blossom (about day 20), a bird's nest, a fox in the shade, finally a bench under the tree. Each gets a diary line. Cosmetic | Later milestone, with the realistic assets |
| Underground finds | A fossil in a stone, an old root of an earlier tree, a humming water vein, a lost coin. Touched by the root, drawn into the journal | Step 5 (data) and later (art) |
| Seed hand-off | The finished tree's last diary page shows it dropping a seed; that seed is the next tree, planted beside the old one in the grove | With the gallery/grove milestone |
| One wish per day | The diary opens each morning with an optional line of direction ("today, reach the damp patch in the west"). No reward or penalty. Tutorial steps live here too | Step 7 (tutorial) |


## Open
- Parked by Simon: winter (a real-calendar linden is bare from November to April; what does the player do then).
- Tuning values, to be set while playing: day/night split, offline rates, root cost per metre, nutrient prices.
- Settings from day one (decided, not built): sound on/off, battery saver (lower frame rate), wallpaper mode, no-UI toggle, notification on/off.
