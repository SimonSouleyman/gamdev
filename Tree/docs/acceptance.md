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
