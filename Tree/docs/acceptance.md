# Acceptance criteria per build step

Each criterion has a test in `tests/` unless marked (visual), which Simon judges from a screenshot.

## Step 2: shared plant graph — done
- Adding nodes links parent and children; budgets refuse extra nodes.
- Pipe model: a fork is thicker than a tip, and the base is at least as thick as the trunk.
- A graph survives a to_dict/from_dict round trip.

## Step 3: tree growth v0 — done
- Tips grow toward markers and consume them; markers out of reach are ignored.
- Same seed gives the same tree.
- The node budget is never exceeded.
- (visual) The grey-box tree branches and does not look like a straight pole.

## Step 4: sun — done
- A full in-game day is at most 10 real minutes; the start values are five minutes of daylight and three of night (survey 2, to tune).
- The sun rises in the east and sets in the west; no growth at night.
- Boost in the morning leans the crown east; at noon south and upward; in the evening west (sun steering in three dimensions).
- Boosting grows more but yields less life force (the boost trade).
- Growth spends nutrients and produces life force; an empty stock gives no growth.

## Step 9: save/load — done
- A save reloads with the same graph, resources, species and clock.
- Time away applies slow offline growth (about 20 s of game time per real day).

## Step 5: root mode — open
- Steering a root drains life force per metre; the run stops when it is empty.
- The path becomes permanent nodes of a root plant graph.
- Fine roots sprout within a radius and respect the per-root budget.
- Dots inside the kill distance are collected into the matching resource.
- (visual) The void with coloured dots reads as "flying in space".

## Step 6: switching — done
- Tapping the ground at sunset dives into root mode; sunrise brings the camera back up (autoplay tool).
- Time holds at sunset until the player dives.
- Both worlds persist: tree, roots, collected dots, diary, phase and clock survive a save and load.
- The clock does not run while the app is closed; offline growth and life force still accrue.

## Step 7: tutorial — done
- A new game starts with a seed at sunset, then one root run from the seed, then the sapling appears at dawn.
- The tutorial is journal pages only, each shown once; the diary writes its first lines.
- Each morning brings a wish, deterministic from the seed.

## Step 8: feel — done
- (visual) Growing tips twinkle; the dawn burst twinkles while the camera rises.
- Placeholder ambience: wind, birds and insects above, a deep hum below, crossfaded on the dive (made in code, loops seamless).

## Step 10: day/night loop — done
- The dive happens only at sunset; sunrise brings the camera back up.
- The night lasts until tonight's single run is spent, then passes quickly to sunrise.
- A night without life force is a short visit plus a diary line.
- Dawn burst: part of the night's growth is released in the first ten seconds after sunrise; the daily total stays about the same.
- Once nutrients are spent, dragging the sun (on its arc chart or in the sky) moves the day on, never past sunset; the night keeps its pace.
- Thirty consecutive days of bot play each show growth (at least five new segments a day).

## Step 11: read the meadow — done
- Surface hints (rushes and damp patch, clover or nettles, stones, moss) come from the underground generator and match what is below at that spot (same seed).
- (visual) The player can orbit and see the hints from every side.

## Species (design doc section 15) — done
- Six profiles (linden, birch, beech, sycamore maple, black alder, pedunculate oak) with the table's needs and sizes; the species is saved with the game.
- Unlock order: the linden always; each species once the one before it has finished; the options pinboard's "any species now" offers all six (saved with the settings).
- Each quirk is one hook switched by a species field: linden blossom week (days 18 to 22, +20 % life force, a diary line with the bees); birch topsoil roots -30 %, shade dieback x2, life force x0.9; beech no shade dieback, calm hours +25 %, boost less, water upkeep x1.3, slow first ten days; sycamore a cut shoot tip forks into two; alder root nodules make nitrogen per metre of root, water deposits drain 1.5x; oak downward roots pay half the depth surcharge, slow, wide and crooked.
- A tree is finished at its species' finish size (or the node budget): a diary line and a page, it joins the grove, and the seed bag in the shed offers "plant the next seed" with the unlocked species. A new tree keeps the grove, the pages read and the album.
- Each species has a journal page (look and quirk), shown once when it is planted, kept in the pages tab.
- (visual) The hero tree's bark and leaf tint follow the species (white birch, dark oak); the forest ring is unchanged.
- tools/month_report.gd --species=<id>|all: growth every day, finished near 30 days (birch 25, oak 35).

## 0.6 part 2: HUD pictures and the hand compass
- The HUD buttons at the middle right are pictures of the real things, top to bottom: a leather journal, a small wooden hut (the shed), an old camera (takes a photo for the album) and hand pruning shears; no words. Each picture's tap area is at least as large as the old paper scraps (150 x 96 canvas units), and they stay clear of the compass and the sun's arc.
- The pictures are rendered by `tools/render_icons.gd` (CC0 Poly Haven models in `tools/icon_models`, the hut and the shears built there) and can be regenerated.
- The compass is an old brass hand compass: the case stays, the dial turns so its N points to north in the world, the needle swings after it under a glass; still turning with the camera above and below ground.
- While the shears are out their picture glows; on a PC the pointer is a small secateurs picture with its hot spot at the blade's point.
- (visual) `tools/hud_shot.gd` shows the real HUD (docs/screenshots/0.6-hud).
## 0.6 part 3: the shed as the menu (design doc section 17)
- The workbench stands in the middle of the shed view, the tree visible through the open door above it; there is no menu note any more.
- Every menu entry is a thing: the journal opens the diary, the photo album the album, the seed bag its page (and the next seed), the flower pot the tree's own page, the garden gloves (or the open door) lead outside, the pinboard on the wall holds the options.
- Each thing has a small handwritten label that shows until it was used once and whenever the pointer rests on it.
- A tap plays a real CC0 sound and a small motion (a book lifts and opens a little, the bag rustles, the pot wobbles, the gloves lift, the notes flutter) before the page opens.
- A small window with an empty sill beside the workbench lets light in; the Node3D `bonsai_spot` marks the place for the bonsai.
- The pinboard has "vibration" (on by default) and "clearer print"; both are saved with the other settings. Clearer print switches every handwritten text to the calm hand, a size larger, and back exactly (test).
- With vibration on, a cut, a dive and a finished tree buzz the phone (Input.vibrate_handheld; VIBRATE permission added by the TreePhone export plugin); off, nothing buzzes (test).
- After the game was closed for an hour or more, a torn "while you were away" page shows once: how long, the growth in metres, the visitors that came meanwhile and an ink sketch of the tree drawn from its plant graph (tests: report once, none for a short absence, absences add up, the page's words).
- (visual) tools/shed_shot.gd photographs the shed, each thing hovered and mid-tap, the pinboard (also in clearer print), the pot's page and the away page.
## 0.6 part 1: a simpler forest on the phone
- On the phone path (Budgets.PHONE; on a PC `-- --phone`) the forest ring and the shrub belt are baked cards (ForestImpostors): one draw call per set, trees ordered from the clearing outwards, octagon cards cut to the plant's outline, hard alpha cut (no dither, no near fade, no shadow lookups).
- Every forest tree kind and shrub kind has baked cells (colour and normals, two sides) inside the shader's cell array; each card shows its own plant's kind.
- A phone keeps only Budgets.FOREST_REAL_TREES real 3D trees, the innermost tree of each sector of the ring; a PC keeps all its 3D trees and its look is unchanged.
- tools/grow_shot.gd `--stats` prints draw calls and primitives with and without the forest; on the phone path at day 10 the forest costs about 12k primitives in 4 to 8 draw calls (before: about 117k in 18 to 21).
- (visual) The ring still reads as a closed wall of mixed broadleaves with the painted deep wood behind; the player's tree stands out at noon and in the evening (days 5 and 20).
- (phone) The day tree view reaches 30 fps on the Fairphone 6.
## 0.6 living clearing (design doc 17.6) — done
- The crown's shade on the ground comes from the hero tree's crown (Clearing.shade_map: leaf clusters projected along the sun's rays over the day); a seedling casts none, a grown crown shades the north side of the trunk most.
- Shade plants come up by themselves, in time: wood anemones first, ferns after about a week of shade, moss last; mushrooms for three days after rain (`GameState.after_rain()` / `Clearing.after_rain(day)`, the weather hook) or a seeded damp morning. Nothing grows on the bare earth around the trunk; the same seed and shade give the same plants; the budget caps them.
- Each kind's first appearance writes one diary line and joins the journal's "clearing" page (the first collection), which is saved and carried to the next tree.
- (visual) Under the crown the meadow thins and darkens, the ground turns to leaf litter with painted anemones, ferns, moss cushions and mushrooms (tools/grow_shot.gd --pitch=0.6 --zoom=0.35 --rain).

## 0.6 month time-lapse (design doc 17.7) — done
- The album's photos group by tree; the flip-book plays a tree's morning photos in order at six pages a second; a finished tree's flip-book page follows its last photos, "flip through" plays any tree's month (the current one too).
- "save as video" writes a Motion-JPEG AVI (valid RIFF with one frame chunk and index entry per photo) and calls `Phone.save_video_to_gallery(path)`, a no-op on PC (plugin work in android_plugin/README.md).
- The flip-book loads small copies of the photos (user://photos/thumbs), made when a photo is taken or on first use; clearing the album clears them.
- (visual) Pages turn toward the binding, showing the next morning under them (tools/shed_shot.gd --flip=25).
## 0.6 sky, seasons and weather (design doc section 17, parts 2, 4, 5)
- The moon's phase comes from the real date (mean synodic month from the new moon of 2000-01-06): new moon at the 2024-04-08 and 2025-09-21 solar eclipses, full moon at the 2025-09-07 lunar eclipse; a waxing moon is lit on the right (tests/test_almanac.gd).
- At night the moon stands where it would during the night: a full moon high in the south, a young crescent low in the west, an old one low in the east, none around new moon.
- The season follows the German calendar: spring from 20 March, summer from 1 June, autumn from 1 September, and from 20 November the late autumn look holds until 19 March (winter parked). The look eases from day to day (no jumps within the growing year).
- `--season=`, `--weather=`, `--moon=` and `--date=` on the command line (after `--`) force a look for screenshots and PC tests; no UI.
- Weather per game day is deterministic from the save seed, the game day and the real date; showers now and then (not every day), misty mornings common in autumn, thunder rare and never in the parked winter. A notable morning (mist, dew) and evening (shower, thunder) writes one diary line.
- (visual) After sunset the sky deepens over half a minute from dusk to a deep blue starry night with the moon and a weak bluish moonlight; few clouds at night; the tree and meadow stay readable (tree view, dive, shed at night).
- (visual) Spring light green, summer deep green, autumn yellow/orange/red with leaves drifting from the crown, late autumn browner and thinner; forest, shrubs, herbs and meadow tinted to match. Growth and the sun's arc are unchanged.
- (visual) A shower: rain streaks, a grey sky, wet sheen, rain sound, birds quiet. Mist: the haze comes into the clearing and lifts by mid-morning. Dew: glints on the grass in the first sun. Thunder: a distant roll, sound only.
- Phone: fewer stars (500), rain drops (450) and falling leaves (36); no moon shadow.
## 0.6 part 8: bonsai mode (design doc sections 16 and 17 item 8)
- Unlock (H): the windowsill stays empty until the first clearing tree is finished; then a young juniper in a clay nursery pot stands there (diary line). The pinboard's "any species now" also opens it (test).
- Where (A): the bonsai stands on the sill beside the workbench and is a thing in the shed with its label "my bonsai"; a tap glides the camera close to the pot (bonsai mode); "back" (or Esc / the back gesture) glides back to the workbench.
- Same clock: `BonsaiSim` follows the tree's clock exactly (same hour, its own count of care days); it grows by day in the window light, not at night, while the player plays the tree or looks at the bonsai; offline growth like the tree (tests).
- Independent (G): no shared life force or resources; its care costs the tree nothing (test).
- Window light and turning: markers are seeded on the window side, so that side grows; "< turn" / "turn >" turn the pot a quarter and the growth moves round (test). Nothing grows through the glass.
- Water: soil moisture dries over the day (faster in the sun); the soil is darker when wet; too dry droops the leaves and slows growth, too wet slows it; never fatal (tests).
- Fertiliser: nitrogen, phosphorus or potassium pellets from the tin lie on the soil; the soft Liebig rule (floor 0.35); too much of one burns a few tips brown, which rest three days and grow on (tests).
- Pot cap: each pot (clay nursery 560, grey rectangle 600, blue oval 500, green round 450, cream cascade 420 green segments) caps the living segments; cut wood leaves the graph, so pruning makes the crown denser, not bigger (tests). Finer segments (1.7 cm) than the tree.
- Shears: tree/pruning.gd with its preview, at most a third of the tree, never the trunk base (test). The juniper keeps a cut branch as a silver jin; a cut at the trunk strips a line of shari below it (test).
- Pinching: a tip grown today or yesterday stops when tapped; the buds behind it get its markers (test).
- Wire: touch a branch in wire mode and drag it to its new line; a copper coil appears and the branch bends half way at once, the rest over four days, then it is set; left on after six days the wire bites and leaves a spiral scar that stays; a tap removes the wire (early: the branch springs back part of the way) (tests). While wiring or cutting the foliage thins so the wood shows.
- Repotting (B): about every seventh day the bonsai asks to be repotted; lift it out with its root ball, snip the circling roots, pick a pot, fresh soil; until then it grows a little slower (root bound), never stops (tests).
- Nothing dies, no end (D): the album page ("album page") lists the milestones and sketches the bonsai; each milestone in view adds a photo to the photo album ("Juniper bonsai, care day N").
- Species (E): the juniper (the only conifer, scale-like foliage pads from a painted atlas) plus a cutting of every finished clearing tree ("cuttings"); one stands on the sill and grows, the others rest on the shelf unchanged (test).
- Style (F): five style pages as ink drawings (formal upright, informal upright, slanting, cascade, broom), for inspiration only.
- Save: the bonsai on the sill and the resting ones are in the same save file (graph, pot, turn, wires, scars, moisture, soil, milestones) and survive a new tree (tests).
- (visual) `tools/shed_shot.gd [--bonsai-only]` photographs the sill, bonsai mode, watering, pellets, a wire and a wire scar, repotting, a juniper after 14 care days, a linden cutting, the style pages and the album page.
## 0.6.2 the tree rebuilt (fresh review: crown shading and gaps, calmer green, real growth curve, camera pulls back)
- Growth curve (every game tree, `GrowthSim.natural_form`; the forest's bare simulation is unchanged): about a metre a day at first, a thin whip with one upright leader, never taller than the species' curve for its day (`target_height`), near the species height at the end of its month; the pace ramps up from a few shoots a day to many (tests/test_tree_growth_curve.gd).
- The crown lifts: side branches below the crown base are shed a few a night (never a big limb at once), and shed wood counts toward the finished tree; a crown at full height keeps widening a little, so it can always finish (constant boost no longer stalls for good: `tools/month_report.gd --boost`).
- Month pacing: `tools/month_report.gd --species=all` finishes every species near its target day, every day shows growth.
- Leaf masses: the crown's sprays are gathered at the twig ends into masses (budget 1800 on a phone, 3600 on a PC), shaded dark inside and light at the sunny rim, with sky between them; a clean alpha cut, no dither speckle; an olive, not neon, green (tests).
- Camera: the frame follows the tree's real height (a sapling small in its clearing, a grown tree from further back); the album's morning photos all use one camera framed for the species' grown size, so the flip-book shows the tree growing (test).
- (visual) `tools/compare_shot.gd` sheets of linden, oak and birch on days 2, 8 and 20 at dawn, noon and golden hour, summer and autumn, phone path.
## 0.6.4 roots: touched deposits worth more, calm nights, easier steering, boost costs (QA r1)
- The tip draws at least twice what a fine root draws from a deposit (tests/test_root_system.gd).
- Leftover life force widens the fine roots' reach only up to a cap (test).
- A night's root on 30, 120 or 300 life force takes 15-60 s real time, and more life force still grows a longer root (test); a big tank pays more per metre and grows faster (test).
- A deposit just off the heading is reached with the stick at rest (magnetism); without the pull the root passes it (test).
- A hard turn grows slower, so the tightest circle is under a metre across (test).
- The night's pace survives a save (test).
- The RootBot skips tapped deposits and ones behind the tip (test).
- Steering to deposits finishes at least 4 days before ending early (linden, seed 14; 0.8.1: was "leads after 12 days", which the wider field's unchanged first weeks no longer show), ending early still finishes by day 40, and no night's root takes more than a minute (tests/test_game_state.gd).
- A boost grows faster but gathers at most 60 % of the life force (test).
- Measured with tools/strategies.gd over seeds 3, 14, 27 and linden, birch, oak: chasing deposits finishes first (or level with always boosting), ending early 8-15 days later; month_report keeps every species within a week of its target.
## 0.8 live picture and app icon (specs/0.8.md sections 6 and 7)
- The live picture shows only the tree of the last save with its grass line and a sky; no HUD, forest, shed or text (LiveExport draws in a world of its own; visual: `tools/live_shot.gd`).
- The light follows the real time: German sunrise and sunset by date, golden light at a low sun, night an hour after sunset, the moon's real phase (tests/test_live_picture.gd); the phone's copy of the maths gives identical numbers (`tools/live_parity.gd`).
- The crown sways softly, the trunk's foot and the picture's edges stay; 12 frames a second (test).
- The crown stays below the lock screen's clock on phone screens and the view never pans (test).
- The meta file round trip; a newer, broken or foreign picture is refused and the last good one stays; no picture shows the bundled sapling (tests; LiveData.java).
- The app icon: a linden from the game's growth on a pale-blue sky and a grass line, crown inside the adaptive icon's safe circle, readable at 48 px (visual: `tools/render_app_icon.gd -- --qa=`).
## 0.8.2 roots: the dearer metre, far wishes per seed, side roots and thicker roots (specs/side-roots.md, specs/0.8.md item 29; notes/roots-0.8.2.md)
- The wider field's metre is dearer (layout 3; old soils keep 1.0) and a calm night's root (meadow start, chasing deposits) is 12 to 18 m long (median), still 20 to 44 s (tests/test_side_roots.gd).
- About half of the underground-wish mornings point far on every seed (running share, saved with the diary) (test).
- Cutting marked twigs never finishes a tree sooner than leaving them (tests/test_marks.gd; measured with `tools/strategies.gd --strats=cut_marks,cut_marks_tip,dots`).
- The leftover life force grows a second and a third level of side roots, flagged by level, at most 250 nodes a night on top of the first level's 150; no second-level node lies more than its reach (3 m) from its start, no third-level one more than 0.8 m; where no dot is in reach short tips still sprout; with less than a tenth of the tank left no side roots grow (tests).
- The share of the tank spent on the root sets its thickness once, 1.0x to 1.5x, kept through a save, 1.0x for old saves; a thicker root seeps up to 1.25x per metre (tests).
- The root view draws main roots by their thickness, the second level finer, the third half as thick and dimmer, all in one merged mesh (test; visual: `tools/grow_shot.gd --roots --run --side|--full --phone`).
## 0.8.2.4 fast-forward 16x, the sunset picture, bonsai lamp, drawers, ending at once (notes/ff-0.8.2.4.md)
- Holding runs the day at 16x, eased in, same tree as a watched day; the hourglass turns about once a second (tests/test_fast_forward.gd, tests/test_ff_0824.gd).
- The sunset picture runs the rest of the day at the fast-forward's speed, eases into the sunset, stops at the sunset hold, never dives; a tap stops it without a boost; not offered at dusk, at night or in the day's last half hour; the same tree as a watched day (tests/test_ff_0824.gd).
- A small lamp over the bonsai is on by night only, aimed at the bonsai, its reach short of the bench (test; visual: `tools/ff_shot.gd`).
- Every drawer of the bench that shows on a 450x1000 screen opens and closes with a tap on its front, toward the room, with an empty place for its things (test).
- A night ended at once grows nearly its whole small-root budget (200 or more of 250 from a tank of 60 or more), and a linden seed 14 ended at once every night finishes by day 40 and at least 4 days behind steering (specs/0.8.md item 44; test).
