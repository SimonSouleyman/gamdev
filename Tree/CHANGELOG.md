# Changelog

## Progress (for resuming work)

Simon (2026-09-29, away): finish 0.6.x, then 0.7 (his three picks), then 0.8 (specs/0.8.md: fixes
from the 0.7 check first, then dot shapes, backup, sharing, brush pile, app icon, live picture).
Install each fully checked version on his phone (save backed up first); APKs in GameDev/tree-releases.
- 0.6.3 tagged (tree-v0.6.3), on main and on Simon's phone.
- 0.7 tagged on iterate-0.7 (wish glow, bonsai tools on the sill, marked branches, look/UI fixes
  from the 0.7 check). Balance findings of the 0.7 check go into 0.8 (Simon: "direkt in 0.8").
- 0.8 streams: s08-shapes (dot shapes, brush pile with hedgehog), s08-backup (copy/load, share),
  s08-live (live picture wallpaper + screen saver, new app icon) built; s08-balance (section 5
  fixes) in progress. Then merge into iterate-0.8, full check, phone install.
- Choices taken as "recommended" while Simon was away:
  - Scope after 0.7: new features too (Simon tapped it), 0.8 per the design thread's spec.
  - Live picture: wallpaper and screen saver, one animation (default until he answers).
  - Shed at night: dark room with a lantern pool, a little moonlight so the tree reads outside.
  - Thirst stays rare: a neglected tree runs short of N/P/K first; the seep stays 0.03 (lower
    pushed unsteered linden past day 40). Design call left to the design thread.
  - Boost life force 0.5 -> 0.35 and night length follows the tank (20-44 s), so a boosted day
    costs a third of the night (tuning item 2).
  - A tree counts as finished only at 0.4 of its species' height (no "finished" bush).
  - The seed sack keeps "seeds" printed on it; its label says "seed bag".
  - Marked branches: natural signs only (dull yellow-grey sparse leaves, grey bark), no droop so
    they differ from thirst; still angle-dependent (stronger would need outlines). Simon's call.
  - One tool name: "shears" everywhere (also the bonsai's).
  - Missed wish: stays a plain deposit, its glow goes when the next wish shows (design thread).
  - Loading a copy first keeps the current game as a safety copy, undoable for a day.
  - Live picture keeps the album's side but frames the tree from low eye height.
- Note: Simon's phone has the newest installed version with his save; backups in GameDev/tree-shots/savebackup.

## v0.7: the wish underground, bonsai tools, marked branches
- Branches the tree marks for pruning (natural signs only): a shaded twig that will die back in a
  day or two shows it first, with thin, limp, washed-out leaves and greying bark; at most three at
  a time, never on beech or the bonsai; the care page says in one line that a branch looks tired.
  Cut it or leave it: left alone it dies back as before (and now stays a bare grey twig). A dying
  tip gives nothing back when cut. Notes and numbers: docs/notes/marks-0.7.md.
- 0.7 review, look and UI (docs/notes/fix-0.7.md): a marked branch now reads as a small branch
  going dry (sparse, small, dull yellow-grey to brownish leaves, clearly grey bark, no droop, bare
  first in autumn; twigs clear of the trunk marked first); the wish glow reads as a warm "over
  there" from the overview and is never hidden behind old roots, and reaching it is a soft warm
  swell that fades (no big green blobs at the camera); the crown is a calmer mid green on the
  phone (autumn less loud); ink ovals ring two-line words; full-size repot pot buttons; "shears"
  everywhere; back puts a lifted bonsai back in its pot; stale bonsai hints cleared; the "sunset"
  word no longer hides behind the book; the diary's wish sketch sits under its line; sill tools
  a little further apart and the stray lid gone.

## v0.8 (in progress)
- Shapes on the nutrient dots (specs/0.8.md section 1): water glows as a drop, nitrogen a leaf,
  phosphorus a four-point spark, potassium a ring, so the dots read without colour (red-green
  colour blindness, a grey screenshot). Same quads and draw call underground (an atlas in the
  dot shader); far dots in the fog fade to one round glow. The marks also show on the HUD pills,
  the care page, the pages that name the dots, and the bonsai's pellet slip; hints say
  "the blue drop dots". Always on. Notes: docs/notes/shapes-brush-0.8.md.
- Brush pile with a hedgehog (section 4): cut branches lie on a pile of sticks at the clearing
  edge away from the shed from the next sunrise, growing with each cut (one merged mesh, at most
  40 sticks). After 40 cut segments a hedgehog moves in two sunrises later and snuffles out at
  dusk on about half the evenings (less from 25 October, never from 20 November until spring),
  with a diary line and an ink sketch the first time; after 80 a wren may sing from the pile by
  day. Rain darkens the sticks. Mood only; fresh for each tree; the bonsai's cuttings stay out.
- Tools: grow_shot.gd `--roots` (the underground, also in grey), `--cut=`, `--hedgehog=`,
  `--wren=`, `--pile_close`.

## v0.6.3: tree care, softer nights, review fixes
- The tree shows what it lacks, shape first: thirsty leaves hang, new shoots short of nitrogen
  stay sparse and small, short of phosphorus or potassium some leaf masses stay bare; colour only
  as a second cue, so it reads in autumn too. A sign shows only while the need exists, only if
  tonight's root can reach a dot of that kind, and eases out by mid-morning once met.
- A care page in the journal (blue ribbon): what the tree lacks, which dots to steer for tonight
  and roughly where, how the crown is shaped, what the last cut did.
- Pruning answers: 0.3 of a cut comes back at the next sunrise, as two or three buds below the
  cut and growth for the crown; pruning never finishes a tree sooner.
- The crown a little lighter at noon (no black blotches in its own shadow).
- Notes and tuning numbers for the design thread: docs/notes/care-0.6.3.md.
- Look review fixes: ink buttons and pinboard scraps tap at least 9 mm (the ring stays small), the
  resets on the cork in the notes' calm hand, journal ribbons readable and clear of the screen edge,
  calm test-switch wording; care signs carried by shape (stronger), no colour cue once the leaves
  turn, the HUD line says what the crown shows; night in the shed (lantern pool, dark walls, dark
  door); ferns and upright tufts lit and calm on the phone; clearer sunrise/sunset words, a blue
  pruning outline, "Tree care" page title.

## v0.6.2 (in progress): the tree rebuilt
- The crown as leaf masses at the twig ends: dark inside, light at the sunny rim, sky between
  the masses, olive leaves with a clean edge (no speckle).
- A real growth curve: a thin whip with one leader for the first days, then height after the
  species' curve, the crown lifting as lower branches are shed; each species still finishes
  near its month, and constant boosting no longer stalls a tree for good.
- The camera frames the tree's real height (a sapling small in its clearing); the album's
  morning photos share one camera framed for the grown tree, so the flip-book shows growth.
## 0.6.4 roots (branch s-roots)

QA r1 found that ending each root after 2 m grew the biggest tree, chasing dots the smallest,
nights grew to 80-110 s, the tip circled the dots and boosting all day halved the month.
- Worth: the tip draws 0.2 of a deposit, a fine root 0.1; deposits hold less (1.15 shares);
  the old roots draw 0.05 a night (water twice that); the leftover's fine roots as before in
  reach, but they draw half what the tip draws (they drew the same). Steering to deposits now
  grows the biggest tree.
- Calm nights: above 50 life force each metre costs more (square root of the tank) and the tip
  grows faster (up to 1.8x, turning with it), so a root takes 20-60 s at any size.
- Steering: a hard turn slows the tip by up to 40 % (tighter curve), a fresh deposit up to 1.8 m
  ahead pulls the tip onto it, collect radius 0.7 m. RootBot aims at fresh deposits ahead
  (skips tapped ones), prefers what the tree lacks and picks a start near a rich patch.
- Boost: growth 2x (was 3x), life force 0.5 (was 0.4): always boosting now finishes within
  about a day of calm play for linden and birch, 4 days sooner for oak (was up to 15 sooner).
- tools/strategies.gd measures it all.

## v0.6 (2026-09-28)

The whole plan of design doc section 17 in one version, then an extensive PC test (a 12-day
scripted play-through of every menu, phase and reload, and a look review of every hour, season
and weather). From here on: fixes as 0.6.1, 0.6.2 ...; new features start 0.7.
- Phone: the forest ring as camera-facing painted cards (half the triangles, 4-8 draw calls).
- HUD: pictures instead of paper scraps (journal, hut, camera, secateurs) at the middle right;
  a brass hand compass; the secateurs as the mouse pointer while pruning.
- Night: stars, a moon in its real phase, soft moonlight, a deep blue night sky.
- The shed: the workbench in the middle with real objects as the menu (journal, album, seed bag,
  pot, gloves, pinboard), each with a sound and a small motion; a windowsill; "clearer print" and
  "vibration" switches; a "while you were away" page with an ink sketch.
- Seasons after the real calendar (look only), weather moods (rain, mist, dew, thunder).
- The living clearing: shade plants under the crown (anemones in spring, ferns, moss, mushrooms
  after rain) and a journal page that collects them.
- The album's month as a flip-book, saved as a video to the phone's gallery.
- Bonsai mode on the windowsill (design doc section 16).
- No bench under the tree.

## v0.6 (in progress)

- The living clearing: as the crown grows, its shade changes the ground below by itself. The sun
  meadow thins to leaf litter, wood anemones flower, then ferns unroll and moss spreads; for a
  few days after rain or a damp morning mushrooms come up. The journal's new "clearing" ribbon
  lists what has come up so far (the first collection), with a diary line for each first.
- The month time-lapse: in the album a finished tree's page plays its morning photos as a
  flip-book ("flip through" does it for any tree), and "save as video" saves the month as a
  short film (into the phone's gallery once the phone plugin can; on a PC into the game folder).
## v0.6 (in progress): bonsai mode
- After the first finished tree a young juniper stands on the shed's windowsill. Tap it: the
  camera comes close to the pot. Water it, give it pellets (N, P or K), turn the pot toward the
  window, shape it with the shears (a third at most), pinch fresh tips and wire branches into new
  lines. About every seventh day it asks to be repotted. It follows the same days as the tree,
  costs the tree nothing and never dies; its album page grows by milestones.
- Each finished clearing tree leaves a cutting for the sill; style pages show the classic shapes.

## v0.5.2 (2026-09-28)

From Simon's feedback on 0.5.1.
- A small patch of bare earth around the trunk; nothing grows there.
- The journal, shed, photo and shears scraps sit in the middle of the right edge, clear of the
  compass and the sun's arc.
- No more flickering yellow points in the crown (new-growth sparkles are few, soft and steady;
  a few calm pollen motes); softer grass edges and 2x anti-aliasing on phones.
- With the shears out the camera rides the trunk: drag beside the tree to move up and down along
  it and around it (mouse wheel on a PC); the shears glow while out.
- No bench under the tree; visitors come anew to each tree.

## v0.5.1 (2026-09-28)

From Simon's recorded phone test of v0.5 (video, frame-rate log and save state).
- The tree view on the phone runs faster (about 12-16 to about 19-23 frames a second with a
  26 m linden): a smaller clearing on phones (30 m), fewer and larger leaf sprays on a big tree,
  a lighter meadow, and a leaner forest ring (the forest cost the phone the most).
- The sky on the phone is bright again and the far wood is hazed; no black band on the horizon.
- Calmer grass and a lighter dawn and evening haze on the phone.
- Underground, the camera no longer ends up inside thick roots.

## v0.5 (2026-09-28)

Simon's play test 4 and the whole open list, then one test loop with screenshots.
From here on: fixes go out as 0.5.1, 0.5.2 ...; only new features start 0.6.

### Play test 4
- Drag the sun along the arc to let the day pass; a tap anywhere else still boosts (a boost
  already bought waits for after the skip).
- After a root run, a short pause frames tonight's root while its fine roots spread out.
- The dive falls into the ground (down, a little turn and zoom); swipe down at sunset to dive,
  swipe up after the night's root to wake the tree.
- The tree stands out: lighter, warmer leaves with a soft rim; darker forest; calmer meadow.
- The meadow: fine grass, blue-green sedge, clover patches and drifts of meadow flowers.
- Pruning: a "shears" scrap; the cut point and the part that would fall show under the finger
  or mouse; the cut branch tips over and sinks into the grass. Never the trunk, never more than
  a fifth of the tree. Pruned wood no longer counts for height, life force or finishing.
- The shed in real weathered wood, a worn workbench, leather books, a folded seed bag, a seedling.
- The album like a real book: cloth binding, handled pages, photos glued in askew with old tape.

### Also on the list
- Real nature sounds (CC0): forest with birds by day, crickets at dusk, water trickling below.
- Visitors: butterflies, a blackbird nest in the crown, a fox in the shade, a bench under the tree.
- Phone: "as wallpaper" in the album sets the home-screen wallpaper; "a note each day" on the
  pinboard is a daily reminder at 9:00 (plain Android, no Google services). Photos in wallpaper size.
- The HUD names a missing nutrient and its dot colour; no offline life force in the middle of a
  night's root; the day-1 camera shows the sapling, not the forest wall; dawn is never black; the
  sky is out of the haze; scraps clear of the compass and the hints; journal ribbons readable.

### Six tree species
- Linden, silver birch, beech, sycamore maple, black alder and pedunculate oak (design doc section 15),
  each with its own needs, size, growth shape, bark and leaf tint, and one quirk on an existing mechanic.
- New small mechanics the quirks needed: shade dieback of crowded inner twigs, and the leaves' daily water upkeep.
- A tree is finished at its species' size; the seed bag in the shed then plants the next unlocked species.
  The options pinboard has a test switch "any species now". Each species has a journal page.
- tools/month_report.gd runs until the tree is finished, per species (--species=<id>|all).

## v0.4 (2026-09-28)

The new look from the visuals thread, and the game running well on Simon's Fairphone 6.
One test loop with screenshots on PC and phone (Simon's call for this version).

### Look
- The linden's crown is made of painted leaf sprays that shade as one volume; grey-brown bark.
- Forest trees and shrubs use the same sprays; painted meadow grass; fractured, mossy boulders
  underground and stones in the meadow.
- Golden hour: warm low sun, real light and shade by day, softer glare; more contrast.
- Paper UI: crumpled torn pages, a bound book page with a leather cover, paper notes and ink
  that sits in the paper.

### Phone
- Fixed a crash in the sound after a few seconds on Android.
- From 4 to about 20 frames per second on the Fairphone: a simpler renderer (which also removed
  flickering lines), half-resolution 3D, fewer forest trees, shrubs, grass and flowers, cheaper
  leaves, a 30 fps cap to save battery.
- The back gesture closes the journal or a board, goes to the shed, and in the shed leaves the game.

### Fixes from the test loop
- Handwriting no longer thin and grey; pages and the book fade in properly.

## v0.3 (2026-09-28)

From Simon's play test 3 and his menu wishes, then three rounds of independent review
(each with a play/robustness tester and a look/realism tester).

### Gameplay (play test 3)
- The day runs by itself (about three minutes). A tap boosts the sun for one game hour while the
  clock keeps running; taps stack up to three hours; a boost ends with the day and is saved.
- Resources are deposits: the new root's first contact takes 40 %, and every root that reached a
  deposit keeps drinking a share of it each night until it is empty. Reached deposits are dimmed
  underground, so fresh ones stand out. Nitrogen comes back faster (clover and nettles).
- Growth is steady over the whole month: visible every day, the tree reaches its size near day 30.
- When one nutrient runs out, a page says which one and what colour its dots are.

### Menus (Simon's answers 1A 2A 3A 4A)
- The start menu is the garden shed: the tree seen through the open door, journal, album and seed
  bag on the bench, a handwritten note as the menu. A "shed" scrap pauses the game and goes there.
- Options on a cork pinboard, a photo album with a photo every morning plus a camera scrap,
  a drawn journal page while loading. One tree at a time, no save slots.

### Look
- Uneven natural ground (it is now actually drawn), gentle swells, rising toward the forest.
- The clearing edge in three layers: herbs, ferns and flowers in front, a belt of hazel, hawthorn,
  elder, holly and blackthorn, then the trees. The shed is only seen from inside.
- The clearing grows with the tree (18 m up to 42 m), so the camera can step back and show a grown
  linden whole.
- The crown starts above a clear trunk, fuller leaves, fuller darker forest crowns, depth fog that
  keeps the tree crisp, less glare in the evening.

### Fixes
- No softlock from Esc during a dive or sunrise; no HUD over the shed or scraps over loading.
- Unread tutorial pages survive entering the shed and closing the game.
- Album in the order taken, opens on the newest photo; one photo at a time, never through a fade.

### Phone
- Android export (arm64, portrait, debug-signed, no Google services needed), app icon from an
  in-game shot of a half-grown linden.

## v0.2 (2026-09-27)

Everything since v0.1, from Simon's play tests and seven rounds of independent review
(three QA rounds with three testers each, plus a realism-only round against the reference photos).

### Gameplay (play test 1)
- The sun can be moved on at any time: drag it along the arc at the top. The tree rests meanwhile
  (leaves still gather life force), so the nutrients wait for the hour you pick, e.g. wait for the
  afternoon, then boost to grow west. The dawn burst always plays out first.
- A root can be ended early ("end root here" or E). The rest of the night's life force goes into
  more and longer fine roots around it. Ending before the root grew keeps the life force.
- No marker ball at the root tip; tonight's root glows warmer than the old ones.
- The soil slowly refills each night, so the month never runs dry.
- A missing nitrogen, phosphorus or potassium slows growth to about a third instead of stopping it;
  the day only counts as "spent" when water runs out or the tree is full.
- Growth is paced so a calm day lasts until sunset and the node budget lasts the whole month;
  the linden stops growing taller at its species height.
- The simulation runs in fixed steps, so the tree grows the same at any frame rate.

### Look (play tests 1 and 2, reference photos)
- A small forest clearing instead of an open meadow: a closed wall of oak, beech, birch, linden and
  spruce (grown by the same growth model, each with its own shape, bark and leaf colour), bushes at
  the edge, painted deep wood behind; the camera stays inside the clearing.
- The meadow is dense soft grass clumps with herbs and wildflowers, dry and lush patches, shade at
  the forest edge; rushes, clover, nettles, stones and damp patches still hint at what lies below.
- The player's tree: smooth branches with photo bark, a single trunk and a crown, photo leaf sprays
  with a shaded interior; it stands out from the darker, cooler forest by a light rim and haze.
- Light: physical sky, drifting clouds, warm light and golden haze at a low sun, a bluer noon,
  soft shadows, MSAA and FXAA; branches and leaves sway in the wind.
- Life: butterflies, a bird flock now and then, pollen in the sun, fireflies at dusk.
- Underground: smooth, textured roots; soft glow puffs and a note when a root drinks a dot.

### Journal (play test 1)
- The big menu is a leather notebook with cloth ribbon bookmarks (diary, pages to read again,
  settings) and page turns; hints are torn squared-notebook pages; every readout (life force,
  nutrients, day, hints, compass, buttons, stick) is handwritten on paper scraps.
- Handwriting fonts Caveat and Patrick Hand (OFL); CC0 photo textures for bark, leaves, ground and
  paper (credits in assets/CREDITS.md).

### Fixes from the reviews (selection)
- The sky was mirrored (the sun rose in the west); south is now +Z and the camera looks south.
- No stuck boost, stick or dive button after a page; a pinch or sun drag interrupted by a page is
  dropped; a boost held through sunset no longer dives; re-reading a page never starts a run.
- A root can no longer tunnel through overlapping rocks, get wedged, or drain life force while stuck.
- Saves: exact RNG state and growth carry-overs, atomic writes, broken saves kept aside, the
  morning and dawn burst survive a reload, time in the background is grown on resume.
- Performance: compressed photo textures with mipmaps, forest meshes cached per seed, grass fading
  with distance, range mesh builds, fixed-step simulation.

### Tools
- tools/autoplay.gd (two days with screenshots), tools/grow_shot.gd (--days, --hour),
  tools/month_report.gd; 255 headless tests.

### Known limits
- Grey-box underground rocks, meadow hints as separate meshes (draw calls), no forest LOD yet,
  the whole tree mesh is rebuilt while it grows (a phone will need a thread or incremental builds).
- The crown is still sparser than a real linden; the full light model (shadow grid) is a later step.

## v0.1 (2026-09-27)
Prototype 1: the whole loop as a grey box (seed, root runs, sapling, sun steering, dawn burst,
diary, meadow hints, placeholder ambience), 227 tests.
