# Tree: Design Doc (v2.2, 2026-09-29)

Working title: **Tree**. UI language: English. Status: v0.5.1 is on the test phone, 0.5.2 (fixes) is being built, 0.6 is planned (section 17). Engine: Godot 4.7, GDScript. Target: Android only for now (APK and F-Droid style stores, no Google services), minimum a Fairphone 6 class phone (2022 or newer, Vulkan). Developed and prototyped on PC.

Version 2.0 consolidated every decision from the interviews (2026-09-26), the two surveys and the feel review (2026-09-27) into one text. Older drafts kept the decisions as dated tables; here each topic is stated once. Version 2.1 (2026-09-28) brings the text in line with what was built up to 0.5.2 and adds the roadmap (section 17). Version 2.2 (2026-09-29) adds the core loop (section 18) and the design practice: design-practice.md (how design work is done), tuning.md (every pacing and economy number, and the "broken" list) and specs/ (one spec per changing mechanic). Companion files: research-tree-growth.md (algorithm and sources), asset-research-2026-09-27.md (free assets and licences), game-feel-review-2026-09-27.md (feel review and play-test plan), review-2026-09-26.md (historical plan review), concepts/ (UI and style mockups).

## 1. Vision
A calm idle game about one realistic tree in a small forest clearing. It grows through sunlight and nutrients. Two linked worlds:
- **Tree (above ground, by day):** the tree grows procedurally, steered by the timing of the sun, and produces **life force**.
- **Roots (underground, by night):** the player steers a root through a space-like void to collect **nutrients**, paying with life force.

Each world feeds the other. One in-game day is one session of at most ten minutes. One tree takes about a month of casual play, then it moves to the photo album (a double page per finished tree) and a new seed from the seed bag is planted. The tree never dies. Nothing in the game rushes the player.

## 2. Look and feel
- **Visual style:** realistic 3D, photorealistic is the goal. One hero tree, a mostly fixed camera, portrait orientation. Four style mockups were compared (concepts/styles/); realism won.
- **Growth is always algorithmic:** the tree grows from a plant graph, never by swapping pre-made models. See research-tree-growth.md and section 4.
- **Journal UI:** every menu, card and hint is a page of one notebook. Torn-out pages (from a squared notebook) for small menus and hints; the big menus open the book itself, a leather-bound notebook with ribbon bookmarks (diary, pages to read again, species). The HUD readouts (life force, nutrients, day) are handwritten on paper scraps in the same style. Handwritten text (fonts Caveat, Patrick Hand, Kalam, all OFL, self-hosted) with small drawings and scribbles; buttons are circled words or sketched boxes. In tree mode the readouts sit on small paper scraps; from 0.6 the buttons (journal, shed, shears, album) are pictures of the real objects (a book, a small wooden hut, hand pruning shears, a closed photo album) at the middle right, so compass and sun arc stay free, and the compass is an old hand compass. A "no UI" toggle shows pure scenery for testing, screenshots and the wallpaper.
- **A living, cohesive world:** butterflies, wildflowers and bushes on the meadow; wind in leaves and grass, drifting clouds; from 0.6 rain showers, misty mornings, dew and distant thunder, the moon in its real phase and a starry sky at night, and the seasons as a look (section 17); snow only with a later winter. All of it is mood and follows the same seed and clock. Gameplay effects of weather come later, only if cheap.
- **Growth feedback:** parts that are growing right now twinkle a little (kept soft since 0.5.2 so it does not flicker on the phone).
- **Sound:** very important from day one and peaceful. Ambience only, no music, with one exception decided by Simon (2026-09-30): wind, birds and insects above; below, a full, warm, friendly hum with a simple, slow melody, quiet and ambient, crossfaded on the dive.
- **Language:** English.

## 3. The day: tree mode
- **Scene:** a single tree in a small forest clearing. Trees and bushes close in all around, so the view never reaches far: the world stays small, and that small part is richly animated (wind, butterflies, pollen, fireflies, drifting clouds overhead). The trees around are grown by the same algorithm. Camera fixed on the tree, slow orbit by dragging, pinch to zoom. Looking at the tree from all sides is part of the day.
- **The sun** follows its real arc for Germany: it rises in the east, stands in the south and high at noon, sets in the west. Later, seasons tilt the arc.
- **Boost:** tap anywhere and the sun shines brighter for one game hour (tap again for more, up to three hours ahead); growth speeds up while the clock keeps running. The shears button switches to cut mode and back.
- **Sun steering in three dimensions:** the timing of the boost shapes the tree. Morning grows the crown east, noon grows it south and taller, evening grows it west. A low sun grows sideways, a high sun grows up. The north side stays sparser by nature.
- **Boost is a trade, not a limit:** there is no cap on holding. Boosting grows faster but the leaves turn light into less life force, so a boosted day shapes a bigger tree and a calm day fills the night's tank. Nutrients cap growth either way.
- **Life force** comes from leaf area and light, accrues during the day and is spent underground at night.
- **Water upkeep:** leaves drink water every day. Too little and they droop and drop, so the roots must keep finding water.
- **Pruning:** in cut mode the camera is anchored to the trunk (drag up and down slides along it, left and right orbits); touching a branch previews the cut point and highlights the part that would fall, releasing cuts, and the branch tips over into the grass. Never the trunk, never more than a fifth of the tree at once. On PC the shears are the mouse cursor, on the phone the shears button glows. Free; pruned wood no longer counts for height, life force or finishing. A journal page explains it the first time.
- **Soft failure only:** drought makes leaves droop and fall, shaded branches die back slowly. Both come from the growth model. Nothing kills the tree.
- **Read the meadow:** the surface hints at what lies below, placed by the same seeded generator as the underground. Rushes and a damp patch over water, clover and nettles over nitrogen, a scatter of stones over rock, moss on the north side of the trunk. The player studies the meadow by day to plan the night's root run.
- **Day length:** up to five minutes. The sun can be dragged along its arc at any time to move the day on (during the skipped time the tree rests and the leaves still gather life force; the dawn burst always plays out first), so the player can pick the hour to boost, for example wait for the afternoon to grow the crown west. Once nutrients are spent the sun glows and the hint says so. Dragging elsewhere orbits the camera.
- **Dawn burst:** part of the growth bought by last night's nutrients is held back and released in the first ten seconds after sunrise, with the twinkle, while the camera rises. The daily total is unchanged.

## 4. Growth model (summary, details in research-tree-growth.md)
Palubicki's self-organizing tree model with space-colonization markers seeded on the sun's side, Borchert-Honda allocation of the nutrient budget (later), and the pipe model for thickness. Buds with too little light go dormant and their branches are shed; that is the dieback. Roots reuse the same space-colonization code with the nutrient dots as attractors. Each species is capped at its real size and shape. First species: linden (Tilia), broad dense crown, heart-shaped leaves, up to about 30 to 40 m, full size after about 30 in-game days.

## 5. The night: root mode
- **Scene:** a black void with glowing coloured nutrient dots. Nearby dots glow, distant ones fade like a soft fog. Third-person camera behind the root tip.
- **The dive:** only at sunset. The player swipes down, the camera falls into the ground with a slight turn and zoom, and the sound changes. After the night's root a short pause shows the fine roots spreading; a swipe up (or sunrise) brings the camera back to the tree.
- **One run per night:** pick any point on an existing root (not only a tip), then steer a new main root with a virtual joystick. The root sinks slowly on its own; hold to dive faster. The run ends when life force is used up, or earlier when the player ends it ("end root here"). Each night still spends everything: whatever life force is left goes into more and longer fine roots around the new root, in proportion to the leftover. There is no marker at the tip; the growing root itself shows where you are.
- **Cost:** life force per metre, rising with distance from the trunk and with depth. A calm night (0.6.4): a big tank of life force makes each metre dearer and the root grow faster, so a night's root takes about 20 to 60 s at any tree size.
- **Steering help (0.6.4):** a hard turn slows the tip for a tighter curve, and a deposit just ahead gently pulls the tip onto it (less while the stick is held hard).
- **Worth (0.6.4):** the tip draws twice what a fine root draws from a deposit, the old roots draw a little each night (water twice as much), and the leftover's fine roots reach only so far: steering to the deposits grows the biggest tree; ending early grows a smaller one.
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
- **Species profiles:** a need multiplier per resource, per species (the six and their needs: section 15).
- **Underground layout:** topsoil holds most N and P in rich patches with sparse ground between, and moderate water. Deeper there is less nutrient, more reliable water, K near the rocks, and more rock.

## 7. Pacing and the month
- **Clock:** one in-game day is one session. It runs only while the app is open. Starting values: up to 5 minutes of day, 3 minutes of night (480 s cycle, daylight fraction 0.625), to be tuned while playing.
- **Offline:** while the app is closed the tree keeps growing very slowly, and life force accrues at the same slow rate. Starting value: one real day away is about 20 s of game time. The player always returns with a little life force.
- **Every day must show visible growth**, for all 30 or so days.
- **Visitors as milestones:** butterflies at the first leaves, bees at the linden blossom (around day 20), a bird's nest in the crown, a fox in the shade, later more (section 17). Each gets a diary line. Cosmetic. The bench under the tree is removed in 0.6 (Simon did not like it); nothing man-made stands by the tree.
- **A tree is finished** when it reaches its species size. It gets a double page in the photo album; from 0.6 the album plays the month's daily photos as a time-lapse.
- **Seed hand-off:** the last diary page shows the finished tree dropping a seed. That seed is the next tree, planted beside the old one.
- **Grove:** a 3D grove of finished trees around the clearing is a later candidate (too costly on the phone for now); the photo album is the grove today.
- **Species unlock:** finishing one tree unlocks one species. Order: linden, birch, beech, sycamore maple, black alder, pedunculate oak (section 15). That is the whole prestige system; there is no forced end to playing.
- **Notification:** at most one per day, local only, one calm line about the tree ("your linden grew a new branch", "the roots are ready"). Built as "a note each day" on the pinboard, at 9:00, off by default (Android asks for permission once).

## 8. The journal
Pages: **diary** (the game writes one line per day: growth, visitors, finds, a night without life force; the player can add a note; from 0.6 a "while you were away" page after time away), **species**, and the first-time pages (pruning, later bonsai care). The album and the options pinboard live in the shed as their own objects. Each morning the diary opens with an optional **wish** ("today, reach the damp patch in the west"), no reward or penalty. The tutorial lives in the diary too.

## 9. Start and tutorial
A seed is planted. The first night is the first root run, from the seed. The nutrients from that run grow the first sapling at dawn. No explanation outside the journal.

## 10. Settings
Handwritten notes and switches on the pinboard in the shed: sound on/off, battery saver (lower frame rate), no-UI toggle, "a note each day", the test switch "any species now"; from 0.6 a readable print hand and haptics on/off. Two reset notes (decided 2026-09-29): "plant a new tree" starts a new tree and keeps the album, the finished trees, the unlocked species and the bonsai; "start over" wipes everything like a fresh install. Each asks "sure? tap again" before it acts. The still wallpaper is set from the album ("as wallpaper").

## 11. Platform, device and later milestones
- Android only for now, iOS later if ever. APK direct or through F-Droid style stores. Nothing that depends on Google Play Services.
- Test phone: Fairphone 6 with /e/OS (Murena). Minimum: that class of phone, 2022 or newer, Vulkan, Godot Mobile renderer from day one.
- Realism budget: one hero tree, fixed camera, leaf-cluster cards in MultiMesh, instanced grass fading with distance, one directional shadow.
- Assets: CC0 textures and skies (Poly Haven, ambientCG) for bark, grass, leaf atlases and sky; paper textures and handwriting fonts for the journal; CC0 wind and hum sounds. Full list in asset-research-2026-09-27.md.
- **Live wallpaper (0.8, specs/0.8.md section 6):** the current tree as an Android live wallpaper via the TheOathMan/Godot-Android-Live-Wallpaper plugin, with a low-power mode (few frames per second, no shadows).
- **Seasons (look in 0.6):** spring, summer and autumn follow the real calendar at the player's location (default Germany) as a look only; growth rules and the sun's arc stay as they are. Tilting the arc with the season is a later option. Winter is parked: a linden is bare from November to April, and what the player does then is undecided; until then the late autumn look holds.
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

Later milestones as planned in 2026-09 (historical; the current plan is section 17), in rough order: realistic assets (bark, leaf atlases, grass, sky) and the full light model (shadow grid, Borchert-Honda); mobile export and a performance pass on the Fairphone; the journal art; visitors and finds art; grove and seed hand-off; sound design; live wallpaper; seasons; more species.

## 13. Agentic build rules (also in the repo's CLAUDE.md)
- Simulation separate from rendering: `shared/` is plain GDScript with no scene dependency, tested headless with `godot --headless --path . -s tests/run_tests.gd` (small custom runner). After a fresh checkout run `godot --headless --path . --import` once.
- All randomness from one save seed: tree, underground, surface hints, finds.
- Hard budgets in code: tree nodes, fine roots per main root, nutrient dots loaded.
- One build step per branch and pull request, acceptance criteria first, tests green before every push, playable on PC at the end.
- Visual judgement (does it look natural) is Simon's, by screenshot in the thread. The play-test questions in game-feel-review-2026-09-27.md are asked after every prototype step.
- Values to tune while playing: day and night lengths, offline rates, root cost per metre, how much one night grows the tree, water upkeep, dots per run and fog distance.

## 14. Not in Prototype 1
Realistic assets, the full light model, seasons, live wallpaper, extra species, mobile export, sound beyond placeholders, visitor and find art, the 3D grove.

## 15. Tree species (decided 2026-09-27)
Six broadleaf trees from the most common species in German forests and towns (Bundeswaldinventur 2022: beech and oak lead the broadleaves, then birch, ash, maple, alder, hornbeam; linden is the classic village and avenue tree). No conifers for now. Ash is left out on purpose: ash dieback is killing it across Germany, which is a sad fit for a calm game.

**Rule for gameplay differences:** the loop stays identical for every species (day with sun and boost, night with one root run, about a month per tree). A species differs in three small ways only:
1. **Needs:** its own multiplier per resource (water, N, P, K), so the player hunts different dots underground and reads the clearing differently.
2. **Shape:** its own growth parameters in `shared/species.gd` (apical dominance, phototropism, gravitropism, size cap, branching), so it looks right and reacts to the sun a little differently.
3. **One quirk:** a single tweak to one mechanic the game already has (boost, pruning, root cost, deposits, dieback, life force). No new buttons, no new modes. The journal's species page explains the quirk in one handwritten line.

### The roster (the six, in unlock order)

| # | Species | Look | Size | Needs W / N / P / K | Growth shape | Quirk (one mechanic) |
|---|---|---|---|---|---|---|
| 1 | **Linden** (Tilia cordata/platyphyllos) | Broad dense dome, heart-shaped leaves, grey bark fissuring with age, pale yellow fragrant blossom with winged bracts | 30 m, crown 12 m | 1.0 / 0.8 / 0.3 / 0.4 | Balanced, low apical dominance, broadens into a dome | **Blossom week:** around days 18 to 22 bees arrive and the leaves give about 20 % more life force. The forgiving teacher tree. |
| 2 | **Silver birch** (Betula pendula) | Slender and airy, white bark with black diamonds, hanging twig tips, small serrated triangular leaves, catkins | 25 m, crown 6 m | 0.7 / 0.4 / 0.2 / 0.3 | Fast start, light and open crown, tips droop (negative gravitropism on fine twigs) | **Pioneer:** roots in the topsoil cost about 30 % less, but shaded branches die back twice as fast and the thin crown gives a little less life force. Quick to finish (about 25 days). |
| 3 | **European beech** (Fagus sylvatica) | Smooth silver-grey "elephant skin" bark, dense layered crown with near-horizontal branches, glossy oval leaves, beechnuts | 35 m, crown 12 m | 0.9 / 0.7 / 0.4 / 0.6 | Slow for the first ten days, then strong; low phototropism, branches spread in flat layers | **Patient:** shaded inner branches do not die back, and calm (unboosted) hours give about 25 % more life force while boost gives less; drought bites earlier (water upkeep x1.3). |
| 4 | **Sycamore maple** (Acer pseudoplatanus) | Big five-lobed leaves, opposite branching, rounded crown, bark flaking in plates, helicopter seeds | 30 m, crown 10 m | 1.0 / 1.0 / 0.4 / 0.6 | Fast youth growth, paired side shoots, strong response to the sun | **Twin buds:** pruning a shoot tip makes it fork into two, so the scissors become a shaping tool for a denser crown. Hungry for nitrogen. |
| 5 | **Black alder** (Alnus glutinosa) | Narrow cone with one straight leader, round dark leaves with a notched tip, dark fissured bark, small woody cones | 25 m, crown 6 m | 1.4 / 0.1 / 0.5 / 0.3 | High apical dominance, conical, grows straight up | **Nitrogen maker:** root nodules make a little nitrogen every night (per metre of root), so it barely needs green dots; it is the thirstiest tree and drains water deposits faster. Real biology: Frankia bacteria, and they need phosphorus. |
| 6 | **Pedunculate oak** (Quercus robur) | Massive short trunk, gnarled zigzag branches, wide irregular crown, lobed leaves, acorns, deeply furrowed bark | 30 m, crown 14 m | 0.8 / 0.6 / 0.5 / 0.7 | Slowest, lowest apical dominance, crooked branching (extra kink per segment), very wide | **Taproot:** root runs pointing downward cost about half the depth surcharge, so deep water and potassium near the rocks are in reach; drought-hardy; the most visitors (jay, stag beetle, woodpecker). The long, grand finale (about 35 days). |

Values are first guesses to tune while playing, like everything in section 13. Linden and birch already exist in `shared/species.gd`; the others are new profiles plus one small hook per quirk.

### Alternatives if Simon wants to swap one
- **Hornbeam** (Carpinus betulus): muscly grey fluted trunk, very common in oak-hornbeam forest. Quirk idea: "hedge tree", every pruning cut regrows extra-dense.
- **White willow** (Salix alba), from the older plan: silver leaves, grows by water, very fast. Quirk idea: very cheap roots through water, very thirsty. Close to alder.
- **Wild cherry** (Prunus avium): white spring blossom, shiny red-banded bark. Quirk idea: a blossom week like linden but early, with birds eating the cherries.
- **Horse chestnut** (Aesculus hippocastanum): huge candles of blossom, conkers; very common in towns but not native.

### Decided by Simon (2026-09-27 survey)
1. The six: linden, birch, beech, maple, alder, oak.
2. Unlock order as in the table: easy first, oak as the grand finale.
3. Each species differs by needs, shape and one quirk.
4. About a month per tree; birch a little shorter (about 25 days), oak a little longer (about 35 days).
5. All six quirks kept as listed in the table.

## 16. Bonsai mode (decided 2026-09-28, planned for 0.6)
A second, smaller game in the garden shed: one bonsai in a pot that the player grows and shapes over many weeks, next to the tree on the clearing. Reference: a juniper in informal upright style on a wooden bench (docs/references/bonsai/juniper-reference.png). Nothing here is built yet; the letters in brackets point to the survey answers at the end of this section.

**Where it lives.** The bonsai stands on the windowsill beside the workbench (A), so it is always visible in the shed menu and grows while the player looks at it. Tapping it moves the camera close to the pot: that is bonsai mode. The shed "back" gesture returns to the workbench. The window is its sun: light comes from one fixed side, which gives the bonsai its own version of sun steering (turning the pot, below).

**The loop, mirrored from the tree.**

| | Tree on the clearing | Bonsai in the shed |
|---|---|---|
| Clock | In-game day and night, about one cycle per real day | The same clock; the bonsai lives through the same days (no own clock) |
| Sun | Real arc, boost by tap, drag the sun | Fixed window light; **turn the pot** a quarter at a time; the side facing the window grows, the back stays sparse |
| Water | Roots find blue deposits at night | **Watering can**: the soil dries over the day, the pot surface darkens when wet; too dry means drooping, too wet slows growth (soft failure only) |
| N, P, K | Root run to deposits | **Fertiliser**: a spoon of pellets on the soil, choose which one; same soft Liebig rule; too much burns leaf tips a little |
| Night | Root run, one per night | **No root run.** About every seventh day the bonsai asks to be **repotted**: lift it out, trim the root ball with the shears, pick the pot, fresh soil. The only underground moment (B) |
| Shaping | Shears, no more than a fifth at once | Shears plus **pinching** (tap a fresh tip to stop it) plus **wire** (C) |
| Size | Grows to its species size | **The pot caps it**: pot volume sets the node budget; pruning moves vigour back into the inner buds, so the tree grows denser, not bigger |
| End | Finished at species size, goes to the album | **No end**: the bonsai is a lifelong companion; its album page grows by milestones (D) |

**Wire.** Tap a branch in wire mode, drag it into a new direction; a copper coil appears along it. Over the next days the branch sets in the new angle. If the wire stays on too long it bites in and leaves a visible scar in the bark (soft failure, never fatal). Removing it is a tap. In the simulation this is a per-node target angle that the pipe-model mesh bends toward a little each day.

**Species (E).** Bonsai species come from the roster: each finished clearing tree gives a cutting for the bonsai, so linden, birch, beech, sycamore maple, black alder and oak all appear as bonsai (all six are real bonsai trees in Europe). Juniper, like in the reference, would be the one conifer and the starter, with deadwood (silver jin and shari) as its quirk. The species' needs and shape parameters from section 15 are reused, scaled down; the quirks carry over where they make sense (maple twin buds, alder's own nitrogen, beech's patience).

**Style (F).** No score. The journal gets a few pages of classic styles as drawings (formal upright, informal upright, slanting, cascade, broom), as inspiration only.

**Relation to the tree (G).** The two games are independent: no shared life force, no shared resources, no penalty for ignoring one of them. The bonsai costs nothing to care for. The link is the unlock (H) and the cutting.

**Time scale.** A real bonsai takes years. In the game one care day shows small visible change (a new flush, a thickening wire branch), and a recognisable shape after about two weeks of care. It keeps refining without end. Once seasons exist (section 11), the bonsai is where they show first: a pot is a small, cheap place for spring flush, autumn colour and bare winter branches.

**What it needs from the existing systems.**
- Plant graph, space colonization and pipe model (section 4) with a small node budget (about 400 to 600) and a finer segment length; markers seeded on the window side.
- `shared/species.gd` profiles plus a bonsai scale; a new juniper profile if chosen.
- The shears and the cut preview from 0.5 (the one-fifth rule relaxed to a third for a bonsai), the 0.5.2 trunk-anchored cut camera, orbiting around the pot.
- The day clock and save file (a second plant graph, pot, wires, soil moisture, fertiliser in the soil); offline growth as for the tree.
- The 0.6 shed with the workbench in the centre and table objects as menu; the windowsill beside it.
- Album and journal pages (bonsai double page, style pages, first-time pages for watering, wire and repotting).
- New: pot models (a few shapes and glazes), watering can, fertiliser tin, wire coil mesh, root-ball mesh for repotting, moss and fine gravel on the soil.
- Performance: the shed already holds 30 fps on the Fairphone; one small tree close up is cheaper than the clearing.

**Decided by Simon (2026-09-28 survey: every recommendation taken).**
- (A) Where it stands: windowsill beside the workbench.
- (B) Night: no root run, repotting every seventh day.
- (C) Wire: yes, with wire bite as the soft failure.
- (D) End: one lifelong bonsai.
- (E) Species: roster species plus juniper as the only conifer and starter.
- (F) Style: free with style pages as inspiration.
- (G) Link: independent, no shared life force.
- (H) Unlock: after the first finished clearing tree.

## 17. Roadmap after 0.5.2 (decided 2026-09-28)
Planned from a candidate list drawn from relaxed and cosy games (Animal Crossing, Neko Atsume, Alba, Terra Nil, Townscaper, Forest, Viridi and others), filtered through the pillars below. The full list with effort and phone risk per idea is in docs/roadmap-ideas-2026-09-28.md. Before each version starts, open details are asked again.

**Pillars used as the filter.** Calm with no pressure (soft failure only, at most one notification a day); one realistic tree in a small living clearing; every menu a real object or a handwritten page; a daily ritual of about one day and night per real day; free and self-contained (no ads, no purchases, no Google services, local only); runs on the Fairphone at 30 fps.

**0.6: the whole plan in one version.** Simon's call (2026-09-28): everything below goes into 0.6. Fixes and adjustments found when testing 0.6 go out as 0.6.1, 0.6.2 and so on; the next set of new features starts 0.7. Suggested build order, each part tested on PC before the next, one phone build and test at the end:
1. **Phone performance:** a simpler forest on the phone for 30 fps (first, because everything else adds cost).
2. **HUD and sky:** picture icons (book, small wooden hut, hand pruning shears, closed album) at the middle right; the compass as an old hand compass; the bench visitor removed; moon in its real phase and a starry sky with few clouds at night.
3. **The shed:** the workbench in the centre with real models of journal, seed bag and flower pot as the menu (every menu item is a table object); the objects answer a tap with a real sound and a small motion; a pinboard switch for a clearer, larger print hand; haptics on a cut, a dive and a finished tree (with a switch); the "while you were away" diary page (growth, visitors, a sketch).
4. **Seasons as a look:** spring green, summer and autumn colour with falling leaves follow the real calendar (Germany default). Mood only: growth rules and the sun's arc are unchanged. Winter stays parked; the late autumn look holds until spring.
5. **Weather moods:** now and then a light rain shower, a misty morning, dew on the grass, distant thunder. Mood only (section 2); a lighter version on the phone.
6. **Living clearing:** as the crown grows, the ground below changes by itself: the sun meadow gives way to shade plants (wood anemone, fern, moss), mushrooms appear after rain. The first collection direction (chosen over the field guide and the curiosity shelf).
7. **Month time-lapse:** the album's daily photos play as a flip-book on the finished tree's double page and can be saved as a short video to the phone's gallery.
8. **Bonsai mode** as in section 16, on the new windowsill beside the workbench.

0.6 is by far the biggest version so far, and seasons, weather and the living clearing all add cost on the phone; the 30 fps goal is checked on the Fairphone at the end, and anything too expensive gets a lighter phone version in 0.6.x.

**0.7 and 0.8 (decided 2026-09-29).** 0.7: the day's wish glowing underground, branches that show they are dying, bonsai tools on the windowsill (specs/0.7-candidates.md). 0.8: fixes from the 0.7 balance check, shapes on the nutrient dots, save backup, share a photo, brush pile with a hedgehog, a new app icon, and the live picture of the tree as wallpaper or screen saver (specs/0.8.md).

**Later candidates (not decided).** Field guide (photograph visitors to fill sketch pages, more visitors); a curiosity shelf in the shed with the old gardener's things; 3D grove; winter; night sky events; naming the tree; more species; placed habitat objects (only if Simon wants man-made things near the tree).

**Never (they break a pillar).** Daily streaks, login rewards and "come back or it withers"; a shop, currency or ads; online leaderboards and cloud save; background music (ambience only, decided; the one exception is the quiet hum-and-melody of the root run, Simon 2026-09-30); quest lists; conifers in the clearing.

## 18. Core loop (2026-09-29)
The loop on three time scales. The fun hypothesis, the one thing that must feel good for the game to work: **"I shaped this tree"**. Every day the player sees growth that followed their choices (when the sun was boosted, where the root went, what was cut), and after a month the tree is visibly theirs.

### Moment to moment (seconds)
| | By day (tree mode) | By night (root mode) |
|---|---|---|
| Action | Tap to boost the sun an hour; drag the sun on; orbit and look; cut a branch in cut mode | Steer the root tip with the stick toward glowing dots; hold to dive; end the root early |
| Feedback | Growing tips twinkle, the crown leans toward the boosted sun, the life force scrap fills (slower while boosted), a cut branch tips into the grass | The root draws a line through the dark, a touched dot flares and dims, the cost per metre shows as the life force drains |
| Reward | Visible growth where the sun was; calm, beautiful light | Nutrients for tomorrow, a find now and then, a new permanent root |

### One visit (a day and its night, about 3 to 6 minutes)
- **Goal:** grow the tree the way I want today, and fill the roots' store for tomorrow.
- **Decision by day:** boost now for fast growth in this direction, or stay calm and bank life force for a longer root tonight. Read the meadow (rushes, clover, stones) to plan where the root goes.
- **Decision by night:** where to start the root, which dots to chase, how far and deep to pay for, and when to stop.
- **Tension:** life force is one pot for two uses (growth speed by day, reach by night). Depth and distance cost more per metre. From 0.6.3 (being built) the tree also shows what it lacks (drooping for water, pale for nitrogen and so on), which points the next root.
- **Resolution:** the dawn burst shows what the night bought; the diary writes the day's line; the morning photo goes into the album. Nothing is lost if the player plays badly: the tree only grows slower, droops or sheds a branch.

### Long term (a month per tree, then the next)
- **Progression:** a tree takes about a month of one visit per real day (birch about 25 days, oak about 35) and is finished at its species size. It gets an album double page with the month as a flip-book, drops a seed, and unlocks the next of the six species, each with its own needs, shape and one quirk. After the first finished tree the bonsai on the shed's windowsill is a second, lifelong companion that grows a cutting from every finished tree.
- **What brings the player back** (only the game itself, never pressure): the tree grew a little while they were away and the "while you were away" page says so; the daily wish; the visitors and finds that come with size; the living clearing changing under the crown; the seasons; the album filling up; the next species. At most one calm notification a day, off by default.
- **Never:** streaks, login rewards, withering as a threat, shop, ads, leaderboards (section 17, design-practice.md).

## Decision log
- 2026-10-01 (Simon, 11:44 UTC, "Ja, Meter teurer"; card from the 0.8.1 sim, tuning "Dearer metre in the wide field"): **a root metre costs more in the wide field** (0.8.2), so a calm night buys 12 to 18 m instead of 30 to 55 m and far patches need a root continued over two or three nights; the night lasts as long as before, deposits pay a little more so a steered tree still takes about a month. Not chosen: a 50 m field, or leaving it.
- 2026-10-01 (Simon, 08:01 UTC, "alle gut" to the coordinator's five ideas; specs/root-field-extras.md): 0.8.2 adds zooming far out underground to see the whole field and past roots, rock bands and soft soil veins for route choices, and a first-time player check; 0.8.3 adds a rare fungal network that briefly taps a distant patch, and an ink drawing of the finished tree's whole root network beside its album photo.
- 2026-09-30 (Simon, 18:31 UTC, for 0.8.1): fix the noon crown (too dark, blotchy) and the juniper bonsai (disc-like pads, thick trunk); the live wallpaper and screen saver must actually animate (they showed a still picture); the day's wish points at a far patch on about half its underground days. Dying branches stay as they are.
- 2026-09-30 (Simon, 18:29 UTC, answering the coordinator's review of the 0.8.1 list): **split again**: fixes are 0.8.1, the new features (seed pictures, side roots, fast-forward, the shorter journal) are 0.8.2. **A much wider root field** in 0.8.1: patches far apart in a bigger volume, so reaching one can take a root continued over nights. **Frame rate** measured on the phone underground and by day in the 0.8.1 check. **Plain dots stay**, colour-blind help later. **The root melody stays** without a switch; four low notes in turn are enough.
- 2026-09-30 (Simon, card at 18:17 UTC, "Ins Tagebuch", for 0.8.1): the pot on the shed bench goes; its "My <tree>" page becomes the journal's first page.
- 2026-09-30 (Simon, 18:15 UTC, for 0.8.1): **a third level of roots**: smaller roots branch off the second-level side roots, fed by about 30 % of the leftover life force, shorter, finer and dimmer, with a node cap for the phone (specs/side-roots.md).
- 2026-09-30 (Simon, 18:06 UTC, for 0.8.1): **no shapes underground either**: nutrient dots are plain coloured everywhere in play; shapes stay only as a key on the journal pages and on the pellet tins. This drops the colour-blind reason for the shapes; the colours must differ in brightness as well as hue. **Root-run sound**: "der Ton im Wurzelmodus ist unangenehm. Satteres, freundlicheres Brummen mit einfacher, langsamer Melodie." This bends the "ambience only, no music" pillar for the root run only: a fuller, warmer hum with a simple, slow melody, kept quiet and ambient; tree mode stays without music.
- 2026-09-30 (Simon, 17:58 UTC): **everything into 0.8.1**: "Alle genannten Punkte (Verbesserungen und neue Features) direkt in 0.8.1 machen". Side roots, hold to fast-forward and the shorter journal move from 0.8.2 into 0.8.1 with the fixes (dark nights, bonsai tools, seed pictures, plain dots in tree mode, tree-mode camera framing, shed windowsill and pinboard on the phone); one broken list in specs/0.8.md.
- 2026-09-30 (Simon, 17:57 UTC): **a shorter journal** (0.8.2, specs/0.8.md): at most three one-line entries a day, explanation pages cut to what the player acts on, a topical ink doodle on every page, and a more handwritten free (OFL) body font, Kalam by default; Caveat stays for headings, "clearer print" keeps Patrick Hand.
- 2026-09-30 (Simon, 17:52 UTC): **hold to fast-forward** (specs/fast-forward.md, 0.8.2): in tree mode a tap still boosts; holding the screen runs the day 4x faster with the boosts already set firing at their times, until release or the sunset hold. It changes no result, only the waiting.
- 2026-09-30 (Simon, 17:45 UTC): **versioning**: "Ich will Version 0.9 nicht so schnell erreichen." New features can land as sub-versions (x.y.z) when Simon says so; the side roots come as 0.8.2 after the 0.8.1 fixes. 0.9 comes later, when Simon calls it.
- 2026-09-30 (Simon, 0.8 phone notes, 17:43 UTC): **side roots and thicker roots** (specs/side-roots.md, 0.8.2): the fine roots along a new root get a second level that grows by itself toward nearby water and nutrients, a short way for now; leftover life force at the end of the night goes into it, and a root driven until the tank is dry grows thicker instead. This replaces "leftover life force makes longer fine roots". Reading of Simon's dictated sentence to be confirmed on his return. Fixes for 0.8.1: brighter nights and visible roots, bonsai tools easier to handle, a picture for each species in the seed pouch.
- 2026-09-30 (design thread, under Simon's away rule): after the 0.7 full check, **the "never steered" limit is per species**: a tree whose roots are never steered finishes about 5 to 10 days after its steered days, by about day 42 for oak (steered 33 to 36, never steered 40 to 41) and day 40 for the others. **One glow at a time**: a missed wish deposit stays as an ordinary deposit and its glow goes out when the next wish shows.
- 2026-09-29 (Simon, 22:46 UTC): two more items for 0.8 (specs/0.8.md sections 6 and 7). **A live picture of the tree**: only the current tree from the save, with sky and wind, light and season following the real time, no interaction. It is pulled forward from "later" (section 11). Simon chose both on a card (22:47 UTC): the live wallpaper and the Android screen saver share one animation. **A new app icon**: only a grown linden against a clear sky, at most a grass line or a few bushes, as an adaptive icon rendered from the game.
- 2026-09-29 (Simon, 22:32 and 22:38 UTC): while he is away the build thread finishes 0.6.x, builds 0.7, then goes on to **0.8** with new features, taking the recommended option on every question. 0.8 is the four undecided candidates, specced in specs/0.8.md with these choices: **shapes on the nutrient dots** (drop, leaf, spark, ring), always on, wherever a nutrient is shown by colour; **save backup** as one zip with save and album through Android's own "save as" picker, "load a copy" behind "sure? tap again", plus automatic copies of the last three sunrises, never a reminder; **share a photo** as the whole captioned Polaroid (and the month's time-lapse) through Android's share sheet, with nothing added; **brush pile** of the player's cut branches at the clearing edge away from the shed, with a hedgehog at dusk first and a wren later, mood only, fresh per tree. The "Later or maybe" ideas stay later; the Never list stands.
- 2026-09-29 (Simon, on project-chat cards): **unsteered roots, "Softer"** (chosen over "Keep strict"): a tree whose roots are never steered still finishes, at about day 36 to 40, and every day shows growth; steering stays clearly better (tuning.md broken list). **Reset, "Both"**: two pinboard notes, "plant a new tree" (keeps album, finished trees, species and bonsai) and "start over" (like a fresh install), each asking "sure? tap again" (section 10). **0.7 candidates, the recommended option for all three** (specs/0.7-candidates.md): the day's wish glows softly underground and marks a real, somewhat bigger deposit; branches about to die back show it by natural signs only (thin, dull leaves, grey bark), no glow; the bonsai tools lie on the windowsill as real objects instead of the paper menu. 0.7 is built after the 0.6.x phone test.
- 2026-09-29 design practice (Simon's game designer prompt, adapted): mechanics are specced before they are built (specs/), every pacing and economy number lives in tuning.md with a "broken" list checked before each full review, economy changes are checked over seeded play styles before building; the core loop is written down (section 18). Streaks, rankings, pressure tactics, shop economics, social pressure and completion-rate metrics are left out on purpose (design-practice.md).
- 2026-09-28 roadmap after 0.5.2 (Simon, section 17): survey on cards, then two changes the same hour. Final: everything planned goes into 0.6 (look and shed, small additions, bonsai mode, seasons as a calendar look with winter parked, weather moods, the living clearing as the first collection direction, the month time-lapse); fixes after the 0.6 test are 0.6.1, 0.6.2 and so on, new features start 0.7. The field guide, curiosity shelf and other ideas stay later candidates; streaks, shop, leaderboards, music and quest lists are ruled out. Design doc brought in line with 0.5.2 (pruning, dive, HUD, settings, album instead of grove, bench removed) as v2.1.
- 2026-09-28 bonsai mode (Simon, section 16): a later game mode in the garden shed with a loop like the tree; every survey recommendation taken: windowsill beside the workbench, repotting every seventh day instead of a root run, wire with wire bite, one lifelong bonsai, roster cuttings plus juniper as the only conifer and starter, free styling, independent of the clearing tree, unlocked after the first finished tree.
- 2026-09-28 species build (Simon: all six, quirks as in section 15; the seed bag offers the next unlocked species after a finished tree; an options-pinboard test switch "any species now" makes all six plantable). Numbers picked in code (shared/species.gd, shared/growth_sim.gd), first guesses to tune: a tree is **finished** when it carries its species' finish size in segments (linden 1800, birch 1740, beech 2080, sycamore 2150, alder 1740, oak 1970; or the 3000-node budget), tuned with tools/month_report.gd to about 30 days (birch 25, oak 35). Growth pace factors: beech 0.6 for ten days then 1.1, birch 1.15 and sycamore 1.1 for eight days, oak 0.85. Shade dieback (new, smallest version): once a day at sunrise 5 % of the tips with at least 12 living nodes above them in their 1 m column die back (birch 10 %, beech none). Water upkeep (new, smallest version): each leaf cluster drinks 0.01 water at sunrise (beech x1.3). Linden blossom days 18 to 22. Birch twig droop 0.5 of a segment out in the crown, life force x0.9. Sycamore: a shoot tip is an unbranched end of up to three segments. Alder: nodules make 0.01 nitrogen per metre of root per night; water deposits give 1.5x per contact. Oak: "downward" means a root heading below -0.5 (about 30 degrees down); extra jitter 0.22 per segment. A new tree keeps the photo album (captions name the tree), the grove and the pages already read; the diary starts fresh.
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
- 2026-09-28: the garden shed is only seen in the shed scene; the clearing edge has three layers (herbs and flowers, mixed shrubs of five species, then trees).
- 2026-09-28 (Simon, visuals thread): the clearing grows with the tree (18 m for a young tree, up to 42 m in 6 m steps, widened at night) so the camera can step back about 35 m and see a grown linden whole.
- 2026-09-28 review rounds: steady growth over 30 days (calm pace capped, the new root's first contact takes 40 % of a deposit), reached deposits dimmed, nitrogen regrows faster, unread tutorial pages survive a save.
- 2026-09-28 (Simon): the look test from the visuals thread is adopted (spray crown and forest, grass, rocks, golden hour, paper UI). On phones the game uses the Compatibility renderer, half-resolution 3D, reduced scenery budgets and a 30 fps cap. For v0.4 one test loop with screenshots replaces the three review rounds.
- 2026-09-28 play test 4 (Simon): drag the sun along the arc to let the day pass (a tap elsewhere still boosts); after a root run a short pause shows what grew while the fine roots spread visibly; the dive feels like the camera falling into the ground (downward move, a little turn and zoom), and swiping down/up switches between tree and roots; the tree must stand out more from ground and forest; the meadow gets two more grass kinds, small meadow flowers, herbs and clover.
