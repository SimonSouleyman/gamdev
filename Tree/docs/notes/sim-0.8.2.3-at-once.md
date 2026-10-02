# Stopping at once: too few roots? (design thread, 2026-10-02)

Simon (0.8.2.3 phone test): "wenn man sofort 'end root here' macht wachsen glaube ich zu wenige
wurzeln für die vorhandene life force. prüfe das."

Short run on main d1466a7, linden, seed 14, headless. "at_once" = every night ended before a start
was picked (GameState.finish_run_early before start_run, as the button does); a scratch copy of
strategies.gd, not committed.

## What happened

| Night style | Small roots grown a night (level 2 / level 3) | Drunk by them | Finished |
|---|---|---|---|
| dots (steering) | none (the tank goes into the root) | | day 30 |
| end_early (1 m root, then end) | 108 to 175 / 75 | several a night early on | day 36 |
| at_once (as built) | 22 to 61 / 39 to 75, from a 120 to 196 tank | 0 to 0.4 a night from day 5 on | **not by day 45** |
| at_once, side roots may start from older side roots | 98 to 146 / 75 | 0.4 to 5.9 a night | day 36 |
| at_once, reach 4 m instead of 2.5 m | 45 to 96 / 75 | about 0.2 to 0.7 | day 41 |
| at_once, both | 118 to 133 / 75 | up to 5.9 | day 37 |

A tank of 120 to 196 bought about 60 to 130 small-root nodes, against the 250 a night allows
(4 nodes per point would ask for 480 to 780). Most of it was lost.

## What it means

Simon is right. When a night ends at once there is no new root, so (1) the "rest" of the node
budget has no first-level tips to grow from and only a few root ends get it, and (2) the small
roots start only from main and fine roots, whose surroundings are drunk dry after a few nights.
So the network never moves outward, and the tree starves (not finished by day 45, against the
never-steered limit of about day 40).

**Fix (for the build thread, 0.8.2.4):** on a night ended at once, the small roots may also start
from the side roots of earlier nights (`side_start_level` 3 for that night only; nights with a
root keep 1, as agreed for 0.8.2.2), and every node of the budget is grown: when no fresh dot is
in reach, the rest spreads as short tips over the root ends of the last 7 nights, not a few
newest ends. Seen on seed 14 this makes about twice as many small roots, and the tree finishes
on day 36, the same as ending after 1 m and 6 days behind steering (limit: 4 or more).
