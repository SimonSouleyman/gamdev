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

## Step 6: switching — open
- Tapping the ground dives into root mode and back; both states persist.

## Step 7: tutorial — open
- A new game starts with a seed, then one root run, then the sapling appears.

## Step 10: day/night loop — open
- The dive happens only at sunset; sunrise brings the camera back up.
- A night without life force is a short visit plus a diary line.
- Dawn burst: part of the night's growth is released in the first ten seconds after sunrise.
- Once nutrients are spent, dragging the sun shortens the day; night length is unchanged.

## Step 11: read the meadow — open
- Surface hints (rushes, clover, stones, moss) come from the underground generator and match what is below at that spot (same seed).
- (visual) The player can orbit and see the hints from every side.
