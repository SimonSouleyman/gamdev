# Changelog

## Progress (for resuming work)

Kept current during the 0.6.x iteration (Simon, 2026-09-29: "reiterate and improve, visuals mostly,
also gameplay"). Every finished step is committed and pushed; a fresh session reads this list.
- Done: 0.6 (tag tree-v0.6), on Simon's phone.
- In progress: fresh review of every scene and a critical play-through on PC (branch iterate-0.6).
- Next: the plan from that review, as 0.6.1, 0.6.2 ...; new mechanics are asked first (0.7).
- Open: the daytime tree-view frame rate on the phone is still unmeasured.

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
