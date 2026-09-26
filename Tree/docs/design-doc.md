# Tree: Design Doc (draft v0.9, 2026-09-26)

Working title: **Tree**. UI language: English. Status: planning. Engine: Godot 4. Target: mobile (Android first?), developed and prototyped on PC.

## Vision
A calm idle game about one realistic tree on a peaceful meadow. It grows through sunlight and nutrients. Two linked modes:
- **Tree (upper world):** the tree grows procedurally and produces **life force**.
- **Roots (underground):** the player steers a root through a space-like void to collect **nutrients**, which cost life force.

The two modes feed each other. The player can switch between them at any time.

## Decisions so far
| Topic | Decision |
|---|---|
| Visual style | 3D, as realistic as possible |
| Growth | Procedural growth, no swapping between models. Self-organizing tree model: space-colonization markers + light + Borchert-Honda nutrient allocation + pipe model for thickness (see research-tree-growth.md) |
| Offline growth | Keeps growing, but much slower. First guess: 1 real day closed = about 20 s of active game time (to be tuned) |
| Sun | Natural sunrise to sunset cycle. The player "activates" the sun: it shines brighter and boosts growth (to be prototyped) |
| Long-term goal | Prestige unlocks new tree species. No forced end: you can stop whenever you want |
| Realism of size | Each species is capped at its real-life size and shape |
| First species | Linden (Tilia, "Linde"): broad, dense crown, heart-shaped leaves, golden-yellow in autumn, up to about 30–40 m |
| Sun steering | Timing is the steering: the sun follows its natural east to west path, and boosting it in the morning grows the tree east, in the evening west |
| Game loop | Short back and forth. Root runs are short because the life force from the tree phase always limits them |
| Persistence | One tree keeps growing over many seasons and years (a long-term companion, not short rounds) |
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

## Tree mode
- **Scene:** a single tree on a meadow with rolling hills and a sky (see references/meadow.png, references/tree-models.png).
- **Growth tick:** the tree spends nutrients, and light exposure decides which buds grow.
- **Steering:** the sun moves on its natural path from east to west. Boosting it in the morning grows the tree toward the east, boosting it in the evening grows it toward the west (decided).
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
- Assets: CC0 textures and skies from Poly Haven (bark, grass, HDRI skies). Leaf-cluster textures per species.

## Target device
The test phone is a Fairphone 6 running /e/OS (Murena, Android without Google). It is a mid-range phone with Vulkan support.
- Distribute the APK directly or through F-Droid-style stores, and avoid anything that depends on Google Play Services (Play Games login, Firebase, AdMob).
- A live wallpaper renders all the time and costs battery. It needs a low-power mode: few frames per second and no shadows.

## Prototype 1 (PC): all basics in one loop
Goal: play the full loop once, from seed to roots to sapling to tree and back to roots. Grey-box visuals are fine, but the growth must already be algorithmic.

Build order, with each step playable on its own:
1. **Project setup:** Godot 4 (latest stable), Mobile renderer, GDScript, git. Folders: `tree/`, `roots/`, `shared/` (plant graph, SCA), `audio/`, `ui/`.
2. **Shared plant graph:** nodes with position, parent, radius and age, plus the pipe-model radius update. The tree and the roots both use it.
3. **Tree growth v0:** space colonization toward markers, with a simple tube mesh rebuilt each tick. No light model yet.
4. **Sun:** a day/night cycle (fast in the prototype) and a boost button. Markers are seeded on the side the sun is on, and boosting speeds up growth. The resources are nutrients (spent to grow) and life force (produced by leaves and light).
5. **Root mode:** a black void with glowing nutrient dots. Pick any point on an existing root and fly a new main root in third person. Life force drains per metre, the path becomes a permanent root mesh, fine side roots sprout automatically, and nutrients are collected.
6. **Switching:** tap the ground to dive the camera down, and tap a button to go back up. Both scenes stay loaded, or their state is kept.
7. **Tutorial flow:** a seed, then the first root run, then the sapling grows.
8. **Feel:** a twinkle shader or particles on growing tips, a placeholder ambient sound per mode, and a crossfade on switching.
9. **Save/load:** the plant graphs, the resources and the timestamp. Offline growth is applied on load.
10. **Day/night loop:** the in-game day drives the mode: daytime is tree mode, night is root mode, whole cycle under 10 minutes. Cycle count per tree is the month pacing knob.

Later milestones: the full light model (shadow grid, Borchert-Honda), realistic assets (bark, leaves, grass, sky), mobile export and a performance pass on the Fairphone, seasons, the live wallpaper, prestige and species.

## Agentic build: rules for the repo
- A `CLAUDE.md` in the repo carries the conventions: Godot version, GDScript style, folder layout, node budgets, no Google services, growth is always algorithmic.
- Simulation and rendering are separate. Plant graph, growth tick, resources and offline catch-up are plain GDScript classes with no scene dependency, tested headless (`godot --headless`, GUT). Scenes and meshes only read the graph.
- All randomness comes from one save seed (tree and underground), so every test and bug can be replayed.
- Hard budgets from day one: tree internodes, fine roots per main root, nutrient dots loaded at once.
- Each build step gets acceptance criteria before it starts, its own branch and pull request, and ends playable on the PC.
- Visual judgement (does it look natural) stays with Simon, via screenshots in the thread.

## Not in Prototype 1
Realistic assets, the full light model, seasons, live wallpaper, prestige and extra species, mobile export, sound beyond placeholders, monetization.

## Open questions
- Root mode: exact cost curve, to be tuned in the prototype.
- Winter: a real-calendar linden is bare from about November to April. What does the player do then? (Parked by Simon for later)
- Idle timing detail: does the in-game clock only run while the app is open (plus slow offline growth), or does it keep running in real time?
- Does life force accrue while the app is closed? (Proposal: yes, slowly.)
- Camera: fixed with slow orbit and pinch zoom?
- Save data: local only, or a backup?
- Settings from day one: sound on/off, battery saver (lower frame rate), wallpaper mode.
- Weather?
- Monetization (if any)?
- Target devices and minimum spec.
