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
