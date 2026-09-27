# Tree: Design Doc (v2.0, 2026-09-27)

Working title: **Tree**. UI language: English. Status: planning complete, Prototype 1 in progress. Engine: Godot 4.7, GDScript. Target: Android only for now (APK and F-Droid style stores, no Google services), minimum a Fairphone 6 class phone (2022 or newer, Vulkan). Developed and prototyped on PC.

This version consolidates every decision from the interviews (2026-09-26), the two surveys and the feel review (2026-09-27) into one text. Older drafts kept the decisions as dated tables; here each topic is stated once. Companion files: research-tree-growth.md (algorithm and sources), asset-research-2026-09-27.md (free assets and licences), game-feel-review-2026-09-27.md (feel review and play-test plan), review-2026-09-26.md (historical plan review), concepts/ (UI and style mockups).

## 1. Vision
A calm idle game about one realistic tree in a small forest clearing. It grows through sunlight and nutrients. Two linked worlds:
- **Tree (above ground, by day):** the tree grows procedurally, steered by the timing of the sun, and produces **life force**.
- **Roots (underground, by night):** the player steers a root through a space-like void to collect **nutrients**, paying with life force.

Each world feeds the other. One in-game day is one session of at most ten minutes. One tree takes about a month of casual play, then it moves to the grove and a new seed is planted beside it. The tree never dies. Nothing in the game rushes the player.

## 2. Look and feel
- **Visual style:** realistic 3D, photorealistic is the goal. One hero tree, a mostly fixed camera, portrait orientation. Four style mockups were compared (concepts/styles/); realism won.
- **Growth is always algorithmic:** the tree grows from a plant graph, never by swapping pre-made models. See research-tree-growth.md and section 4.
- **Journal UI:** every menu, card and hint is a page of one notebook. Torn-out pages (from a squared notebook) for small menus and hints; the big menus open the book itself, a leather-bound notebook with ribbon bookmarks (diary, pages to read again, settings; later grove and species). The HUD readouts (life force, nutrients, day) are handwritten on paper scraps in the same style. Handwritten text (fonts Caveat, Patrick Hand, Kalam, all OFL, self-hosted) with small drawings and scribbles; buttons are circled words or sketched boxes. The HUD in tree mode is a few small pills (day, life force, four resources), paper or glass, to be tried. A "no UI" toggle shows pure scenery for testing, screenshots and the wallpaper.
- **A living, cohesive world:** butterflies, wildflowers and bushes on the meadow; wind in leaves and grass, drifting clouds, occasional rain, snow in a later winter. All of it is mood and follows the same seed and clock. Gameplay effects of weather come later, only if cheap.
- **Growth feedback:** parts that are growing right now twinkle a little.
- **Sound:** very important from day one and peaceful. Ambience only, no music: wind, birds and insects above, a deep calm hum below, crossfaded on the dive.
- **Language:** English.

## 3. The day: tree mode
- **Scene:** a single tree in a small forest clearing. Trees and bushes close in all around, so the view never reaches far: the world stays small, and that small part is richly animated (wind, butterflies, pollen, fireflies, drifting clouds overhead). The trees around are grown by the same algorithm. Camera fixed on the tree, slow orbit by dragging, pinch to zoom. Looking at the tree from all sides is part of the day.
- **The sun** follows its real arc for Germany: it rises in the east, stands in the south and high at noon, sets in the west. Later, seasons tilt the arc.
- **Boost:** tap anywhere and the sun shines brighter for one game hour (tap again for more, up to three hours ahead); growth speeds up while the clock keeps running. Once pruning exists, a small tool switch chooses boost or prune.
- **Sun steering in three dimensions:** the timing of the boost shapes the tree. Morning grows the crown east, noon grows it south and taller, evening grows it west. A low sun grows sideways, a high sun grows up. The north side stays sparser by nature.
- **Boost is a trade, not a limit:** there is no cap on holding. Boosting grows faster but the leaves turn light into less life force, so a boosted day shapes a bigger tree and a calm day fills the night's tank. Nutrients cap growth either way.
- **Life force** comes from leaf area and light, accrues during the day and is spent underground at night.
- **Water upkeep:** leaves drink water every day. Too little and they droop and drop, so the roots must keep finding water.
- **Pruning:** tap a branch and confirm. Free; the cut wood is gone and its resources go to the rest of the crown. Dying or shaded branches are marked so the reason is visible. A journal page explains it the first time, later only a scissors mark.
- **Soft failure only:** drought makes leaves droop and fall, shaded branches die back slowly. Both come from the growth model. Nothing kills the tree.
- **Read the meadow:** the surface hints at what lies below, placed by the same seeded generator as the underground. Rushes and a damp patch over water, clover and nettles over nitrogen, a scatter of stones over rock, moss on the north side of the trunk. The player studies the meadow by day to plan the night's root run.
- **Day length:** up to five minutes. The sun can be dragged along its arc at any time to move the day on (during the skipped time the tree rests and the leaves still gather life force; the dawn burst always plays out first), so the player can pick the hour to boost, for example wait for the afternoon to grow the crown west. Once nutrients are spent the sun glows and the hint says so. Dragging elsewhere orbits the camera.
- **Dawn burst:** part of the growth bought by last night's nutrients is held back and released in the first ten seconds after sunrise, with the twinkle, while the camera rises. The daily total is unchanged.

## 4. Growth model (summary, details in research-tree-growth.md)
Palubicki's self-organizing tree model with space-colonization markers seeded on the sun's side, Borchert-Honda allocation of the nutrient budget (later), and the pipe model for thickness. Buds with too little light go dormant and their branches are shed; that is the dieback. Roots reuse the same space-colonization code with the nutrient dots as attractors. Each species is capped at its real size and shape. First species: linden (Tilia), broad dense crown, heart-shaped leaves, up to about 30 to 40 m, full size after about 30 in-game days.

## 5. The night: root mode
- **Scene:** a black void with glowing coloured nutrient dots. Nearby dots glow, distant ones fade like a soft fog. Third-person camera behind the root tip.
- **The dive:** only at sunset. The day ends, the player taps the ground, the camera dives down and the sound changes. Sunrise brings the camera back up.
- **One run per night:** pick any point on an existing root (not only a tip), then steer a new main root with a virtual joystick. The root sinks slowly on its own; hold to dive faster. The run ends when life force is used up, or earlier when the player ends it ("end root here"). Each night still spends everything: whatever life force is left goes into more and longer fine roots around the new root, in proportion to the leftover. There is no marker at the tip; the growing root itself shows where you are.
- **Cost:** life force per metre, rising with distance from the trunk and with depth.
- **Rocks:** hard walls, steer around them. Deeper down there are more rocks.
- **Result:** the steered path becomes a permanent main root. Fine side roots then sprout on their own along the path (space colonization within a short radius and a node budget per root) and collect the small dots nearby. Collected nutrients feed the tree the next day.
- **A night without life force:** a short visit underground without growing, a note in the diary, then morning.
- **Underground finds:** a fossil in a stone, an old root of an earlier tree, a humming water vein, a lost coin. Touched by the root, drawn into the journal.

## 6. Resources
Four underground resources, each a colour of dot.

| Resource | Colour | Real biology | Game effect |
|---|---|---|---|
| Water | Blue, most abundant | Every growth process, carries nutrients, keeps leaves working | Needed for all growth, and drunk daily by the leaves (drought if short) |
| Nitrogen (N) | Green | Leaves and chlorophyll | More and bigger leaves, so more life force |
| Phosphorus (P) | Warm orange | Energy transfer, roots, flowers and seeds | Cheaper, faster root growth; later flowering |
| Potassium (K) | Violet | Water regulation, wood strength, hardiness | Thicker, sturdier wood; later winter and drought hardiness |

- **Soft Liebig rule:** growth is limited by the scarcest resource relative to the species' needs, softened so a shortage slows the tree but never stops it.
- **Species profiles:** a need multiplier per resource. Linden wants much water and N. Birch is frugal. Willow wants very much water. Pine needs little N. Oak is in between.
- **Underground layout:** topsoil holds most N and P in rich patches with sparse ground between, and moderate water. Deeper there is less nutrient, more reliable water, K near the rocks, and more rock.

## 7. Pacing and the month
- **Clock:** one in-game day is one session. It runs only while the app is open. Starting values: up to 5 minutes of day, 3 minutes of night (480 s cycle, daylight fraction 0.625), to be tuned while playing.
- **Offline:** while the app is closed the tree keeps growing very slowly, and life force accrues at the same slow rate. Starting value: one real day away is about 20 s of game time. The player always returns with a little life force.
- **Every day must show visible growth**, for all 30 or so days.
- **Visitors as milestones:** butterflies at the first leaves, bees at the linden blossom (around day 20), a bird's nest in the crown, a fox in the shade, finally a bench under the tree. Each gets a diary line. Cosmetic.
- **A tree is finished** when it reaches its species size. It moves to the grove.
- **Seed hand-off:** the last diary page shows the finished tree dropping a seed. That seed is the next tree, planted beside the old one.
- **Grove:** the finished trees stand on the meadow around the new one and the player can walk along them in 3D (later milestone; a list is the fallback).
- **Species unlock:** finishing one tree unlocks one species. Order: linden, birch, willow, oak, pine. That is the whole prestige system; there is no forced end to playing.
- **Notification:** at most one per day, local only, one calm line about the tree ("your linden grew a new branch", "the roots are ready"). Can be switched off.

## 8. The journal
Pages: **diary** (the game writes one line per day: growth, visitors, finds, a night without life force; the player can add a note), **grove**, **species**, **settings**. Each morning the diary opens with an optional **wish** ("today, reach the damp patch in the west"), no reward or penalty. The tutorial lives in the diary too.

## 9. Start and tutorial
A seed is planted. The first night is the first root run, from the seed. The nutrients from that run grow the first sapling at dawn. No explanation outside the journal.

## 10. Settings from day one
Sound on/off, battery saver (lower frame rate), wallpaper mode, no-UI toggle, notification on/off.

## 11. Platform, device and later milestones
- Android only for now, iOS later if ever. APK direct or through F-Droid style stores. Nothing that depends on Google Play Services.
- Test phone: Fairphone 6 with /e/OS (Murena). Minimum: that class of phone, 2022 or newer, Vulkan, Godot Mobile renderer from day one.
- Realism budget: one hero tree, fixed camera, leaf-cluster cards in MultiMesh, instanced grass fading with distance, one directional shadow.
- Assets: CC0 textures and skies (Poly Haven, ambientCG) for bark, grass, leaf atlases and sky; paper textures and handwriting fonts for the journal; CC0 wind and hum sounds. Full list in asset-research-2026-09-27.md.
- **Live wallpaper (later):** the current tree as an Android live wallpaper via the TheOathMan/Godot-Android-Live-Wallpaper plugin, with a low-power mode (few frames per second, no shadows).
- **Seasons (later):** follow the real calendar at the player's location (default Germany) and tilt the sun's arc. Plan the data model for it now. Winter is parked: a linden is bare from November to April, and what the player does then is undecided.
- **Money:** free, no ads.
- **Saving:** local only for now; a backup option later.

## 12. Prototype 1 (PC): all basics in one loop
Goal: play the full loop from seed to roots to sapling to tree and back. Grey-box visuals, algorithmic growth. Each step is playable on its own and gets acceptance criteria and headless tests before it starts (docs/acceptance.md in the repo).

Status 2026-09-27: all eleven steps are built as a grey-box loop on PC; 213 headless tests and an end-to-end autoplay pass. Values are first guesses to tune while playing.
1. **Project setup:** Godot 4.7, Mobile renderer, GDScript, git. Folders `tree/`, `roots/`, `shared/`, `audio/`, `ui/`, `tests/`, `docs/`.
2. **Shared plant graph:** nodes with position, parent, radius and age; pipe-model radii. Used by tree and roots.
3. **Tree growth v0:** space colonization toward markers, tube mesh rebuilt on change.
4. **Sun:** the day/night clock, the real sun arc, hold-to-boost, markers seeded on the sun's side, boost as a trade, nutrients spent and life force produced.
5. **Root mode:** the void with dots and fog, pick a start point on a root, steer with joystick, auto-sink and hold-to-dive, rocks as walls, life force per metre, permanent root mesh, automatic fine roots, collection. The seeded underground generator also produces the surface hints and the finds.
6. **Switching:** tap the ground at sunset to dive; sunrise brings the camera up. Both states persist.
7. **Tutorial and diary:** seed, first run, sapling; the diary writes its first lines; the daily wish.
8. **Feel:** twinkle on growing tips, placeholder ambience per world, crossfade on the dive.
9. **Save/load:** plant graphs, resources, clock and timestamp; offline growth and life force applied on load.
10. **Day/night loop:** the clock drives the mode; dawn burst; dragging the sun to shorten the day; a night without life force as a short visit.
11. **Read the meadow:** surface hints rendered from the generator data; the orbit camera to study them.

Later milestones, in rough order: realistic assets (bark, leaf atlases, grass, sky) and the full light model (shadow grid, Borchert-Honda); mobile export and a performance pass on the Fairphone; the journal art; visitors and finds art; grove and seed hand-off; sound design; live wallpaper; seasons; more species.

## 13. Agentic build rules (also in the repo's CLAUDE.md)
- Simulation separate from rendering: `shared/` is plain GDScript with no scene dependency, tested headless with `godot --headless --path . -s tests/run_tests.gd` (small custom runner). After a fresh checkout run `godot --headless --path . --import` once.
- All randomness from one save seed: tree, underground, surface hints, finds.
- Hard budgets in code: tree nodes, fine roots per main root, nutrient dots loaded.
- One build step per branch and pull request, acceptance criteria first, tests green before every push, playable on PC at the end.
- Visual judgement (does it look natural) is Simon's, by screenshot in the thread. The play-test questions in game-feel-review-2026-09-27.md are asked after every prototype step.
- Values to tune while playing: day and night lengths, offline rates, root cost per metre, how much one night grows the tree, water upkeep, dots per run and fog distance.

## 14. Not in Prototype 1
Realistic assets, the full light model, seasons, live wallpaper, extra species, mobile export, sound beyond placeholders, visitor and find art, the 3D grove.

## Decision log
- 2026-09-27 menus (Simon): the start menu is the garden shed at the south edge of the clearing, looking out through the open door at the player's tree; journal, photo album and seed bag lie on the workbench, the options are notes on a pinboard, the menu is a handwritten note; in play a "shed" scrap pauses and returns there. The album gets a photo every morning plus camera photos; a drawn journal page covers loading. One tree after another, no save slots.
- 2026-09-27 play test 3 (Simon): the day runs on by itself and quickly (2 min of daylight); a tap boosts the sun for one game hour while time keeps running (no holding, no dragging the sun, no pause); underground dots are deposits with a set amount: a root draws a share on contact and the root network keeps drinking from reached deposits every night until they are empty; the clearing has uneven ground and undergrowth between the trees. Menus as whole scenes (garden shed, start menu, options, photo album) are next.
- 2026-09-27 play test 2 and QA rounds (Simon): the meadow is dense soft grass with herbs and wildflowers (no single blades); the clearing is closed in by a dense wall of mixed trees (oak, beech, birch, linden, spruce) and undergrowth; reference photos in docs/references/clearing. Moving the sun on rests the tree (life force still gathers) so the nutrients wait for the hour the player picks; a missing N, P or K slows growth to about a third instead of stopping it (soft Liebig floor 0.35); the simulation runs in fixed steps.
- 2026-09-27 play test 1 (Simon): the sun can be moved on at any time of the day; a root can end early and the leftover life force feeds more fine roots (replaces "leftover carries over"); no ball at the root tip; brighter start at dusk; the scene is a small forest clearing, not an open meadow; the journal is a book (big menus) and torn pages (small), with the HUD in the same handwritten paper style; CC0 photo textures (bark, leaves, ground, paper) and OFL handwriting fonts (Caveat, Patrick Hand) approved.
- 2026-09-26 interviews: vision, realism, algorithm, sun steering, roots, resources, pacing, soft failure, notifications, portrait, wallpaper wish, linden first.
- 2026-09-26 review: contradictions fixed (branch anywhere), soft failure, budgets, seed, agentic rules.
- 2026-09-27 survey 1: HUD, hold-to-boost, run ends when empty, journal explanation for pruning, camera, local save, free.
- 2026-09-27: journal UI, weather as mood, style mockups.
- 2026-09-27 survey 2: realistic 3D, dive only at sunset, night without life force, 5/3 split, no boost cap, offline life force, one run per night, auto-sink, rocks as walls, fog, water upkeep, finished at species size, species order, notification text, pruning free, ambience only, journal pages, Android only, minimum phone.
- 2026-09-27 feel review: all eight additions accepted, shorter day, orbit as part of the day, sun steering in three dimensions, boost as a trade.
