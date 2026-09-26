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
- A full in-game day is at most 10 real minutes.
- The sun rises in the east and sets in the west; no growth at night.
- Boost in the morning leans the crown east; boost in the evening leans it west.
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
