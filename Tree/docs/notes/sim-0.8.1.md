# 0.8.1 simulation: no-roots finish, a wider root field, far wishes

Written by the build stream s08-sim, 2026-10-01, for the design thread to merge into `tuning.md`
(not edited here). Items: (a) the tree played without roots finishes again (tuning.md "0.8 balance
fixes", check-0.8 item 1), (b) specs/0.8.md item 29 "A wider root field", (c) item 34 "Wish
distance". Headless Godot 4.7.2, seeds 3 / 14 / 27, `tools/strategies.gd` (all nine styles, six
species), `tools/qa_field.gd` (new). "Before" = the 0.8 check (`tree-qa/check-0.8/playthrough.md`).

**Status: not fully verified.** The final sweep below ran on the code as committed. The last test
run, `month_report`, `qa_nutri`, `qa_glow`, `autoplay` and the underground shots could not be run
in this session (see "Open points"). Treat the numbers as the build stream's sweep, not a check.

## What changed and why

| Variable | Was (0.8) | Now | Reason |
|---|---|---|---|
| WIP of 2026-09-30 (`MAX_REPACE_SCALE` 4, `MIN_SPEED_SCALE` 0.18, `fine_seep_share` 0.5) | | back to 3.0 / 0.3 / 0.3 | The WIP made boost_quit finish but made ending early nearly as good as steering (linden end_early 32 to 34 against dots 29 to 30, alder 31 against 28): the fine roots' seep fed every quitter. |
| Boost price for a hungry tree (`GrowthSim.boost_cost_liebig`, new) | the boost always cuts life force to 0.35 | while boosted, the cut eases by (1 - growth factor without floor) x the morning's hunger (`care_need`, judged at sunrise) | (a). A tree that wakes hungry and has a nutrient missing gains nothing from the boost (`boost_liebig`), so it now also keeps its life force. A tree the night fed pays the full price, also after its own boost used the stock up, so broken item 2 holds: boost_all nights are 0.67 of dots nights on average (0.65 without this rule; 0.74 to 0.86 when the rule did not look at the morning). |
| `boost_hunger_gain` (new) | | 1.0 | Tool lever (strategies `--set=sim.boost_hunger_gain=`); 2 and 3 were being tried when the session stopped. |
| Soil layout (`Underground.LAYOUT`, `game_layout`) | 2 | 3 for new games; saves keep 1 or 2, dot for dot, with their 14 m field and `distance_cost` 0.06 | (b), and old saves keep their soil. |
| Field radius (`Underground.extent`, `field_extent`) | 14 m (`EXTENT`, still used by layouts 1 and 2) | 30 m | Item 29. The root's wall (`RootSystem._move`) and the wish placement read the soil's own extent. |
| Rich patches (`Underground.rings`, `gap3`) | 6 in 2.2 to 14 m, 3.2 m apart | 14: near ring 5 to 12 m (P, N, W, P, N), middle 12 to 20 m (W, P, N, W, P), far 20 to 30 m (N, W, P, N); 7 m apart; sizes per kind as layout 2 (`mix`) | Item 29. Every patch its own drive; the near ring gives W, N and P. |
| Scattered dots (`scatter3`) | 700 in the 14 m field | 1200 in the 30 m field (a third of the density) | Item 29. Dots in all 2375 to 2430 per seed; 20 wish deposits of ~70 dots still fit the 4000 budget. |
| Rocks (`rocks3`) | 5 shallow, 26 deep | 9 shallow, 60 deep (density 0.4 of before) | The bigger volume; potassium sits around the deep rocks (8 dots each). |
| Deep water veins (`deep_water3`) | 1 | 2 | The bigger volume. |
| Starter patch | 12 / 16 / 14 / 10 at (0, -0.8, -1.2) | unchanged | "The first week stays as it is." |
| Price of distance (`RootSystem.distance_cost`, `fit_soil`) | 0.06 | 0.03 in layout 3 (`distance_cost_wide`), 0.06 kept for older soils | Item 29: a 25 m drive would cost 2.5x a metre by the trunk. |
| Far meadow signs (`Underground.surface_hints(edge)`, `EDGE_INSET`, `EDGE_SIGN_SCALE`; `Meadow` passes `Terrain.edge`) | every sign over its patch | a patch beyond the clearing (`edge` - 2.5 m) shows its sign at that radius in its direction, 0.8 the size; one that would land on the shed turns 4 m along the edge | Item 29. `tree/meadow.gd` is the only render file touched. |
| Far wishes (`Diary.far_share`, `FAR_AHEAD_MIN/MAX`, `FAR_REACH_NIGHTS`, `FAR_DAYS`, `place_far`, `is_far`; the deposit's saved "far" flag) | every underground wish 5.5 to 8.5 m beyond the newest tip | in layout 3, half the new underground wishes (`far_share` 0.5, own RNG stream) go 9 to 17 m beyond the newest tip and 12 m or more from the trunk, within 2.5 calm reaches (REACH_SHARE of a calm tank each) of a straight root from that tip; an unreached far wish stays up to 3 mornings | Item 34. Old saves: the near wish as before (no far draw in layouts 1 and 2). |
| `tools/strategies.gd` | | prints run ms (average, longest night) and the slowest frame | "Budgets: the night's sim cost." |

## Results (final sweep, strategies.gd, seeds 3/14/27, "-" = not by day 45)

| Species (target) | wish | tip | dots | end_early | straight_down | random | boost_all | boost_morning | boost_quit |
|---|---|---|---|---|---|---|---|---|---|
| linden (30) | 29/29/29 | 31/30/34 | 30/29/30 | 36/37/36 | 38/38/40 | 37/37/38 | 30/30/30 | 27/29/29 | 41/39/41 |
| birch (25) | 24/24/24 | 25/25/25 | 24/24/24 | 26/28/30 | 31/30/30 | 29/30/30 | 23/23/23 | 21/21/22 | 31/32/33 |
| beech (30) | 28/28/28 | 29/30/28 | 29/29/28 | 35/36/35 | 36/35/37 | 35/35/35 | 30/30/30 | 27/29/27 | 37/35/36 |
| sycamore (30) | 28/28/28 | 31/29/31 | 30/28/29 | 36/36/34 | 38/36/37 | 36/36/35 | 27/27/28 | 25/27/26 | 37/36/33 |
| alder (30) | 28/28/28 | 30/28/31 | 28/28/28 | 37/35/37 | 38/37/40 | 36/35/34 | 30/30/28 | 26/27/26 | 36/42/37 |
| oak (35) | 32/33/32 | 35/34/37 | 32/33/32 | 39/41/41 | 43/43/43 | 40/41/41 | 29/32/31 | 29/32/30 | 34/40/41 |

- (a) boost_quit finishes on all 18 runs (0.8: 3 never). The three failing runs: linden s3 41,
  beech s3 37, alder s14 42. Linden 39 to 41 and alder s14 42 sit just past "about day 40" and
  10 to 14 days after dots; the sweeps between candidate settings moved single runs by 2 to 3
  days, so this is at the edge, not inside it.
- Oak boost_morning s3: 29 against dots 32, 9 % sooner (0.8: 15.2 %). Sycamore boost_morning s3:
  25 against dots 30, 17 % (dots s3 was 29 in earlier sweeps; boost_all and boost_morning are
  otherwise 0 to 13 % sooner).
- Optional: oak straight_down 43/43/43 (not inside 42); alder end_early s3 37, 9 days after dots.
- Steered months in range (wish 24 to 33, dots 24 to 33, tip 25 to 37). Never-steered: end_early
  2 to 9 days after dots (birch s3 only 2), straight_down and random 5 to 12 (alder straight_down
  s27 40, 12 days; oak 43).
- Nights: 19 to 52 s (19 is a boost_all night 1 or 2), from night 3 on 20 to 52 s. boost_all
  nights 0.67 of dots nights on average, metres 0.65 (item 2).
- Sim cost: a night's run 50 to 183 ms of wall time on average (longest 512 ms, end_early with
  its fine roots), slowest single frame 22 ms (the end of a run). 0.8's numbers were not printed.

**The wider field (qa_field.gd, 18 runs per style).**

| Measure | "dots" (meadow start, chase deposits) | "wish" (follow the glow from the newest tip) |
|---|---|---|
| Rich patches touched by a night's root | median 0 to 1, max 2 to 3; 3 to 24 nights a month touch none | median 0, max 1 to 3 |
| Rich patches within tonight's straight drive (start to tip) | median 1 to 4, max 7 to 12 | median 1 to 3, max 5 to 12 |
| Rich patches a straight root on tonight's whole tank could reach from the start | median 7 to 12, max 10 to 14 | median 9 to 12, max 12 to 14 |
| Straight distance a night's root gets from its start | median 11 to 16 m, max 23 to 37 m | median 9 to 15 m, max 17 to 35 m |
| First week: every needed kind within one calm night | 7 of 7 nights, all runs | the same |
| B1: the scarcest kind within one calm night / two | 100 % / 100 % | 100 % / 100 % |
| Underground-wish days pointing at a far wish | 43 to 84 % (missed far wishes wait up to 3 mornings) | 25 to 70 %, about 47 % overall |
| Far wishes reached | rarely (this bot ignores the glow) | all, each within one night |

What it means: the field is wide in the sense of the drive (a night gets 10 to 15 m from its
start and touches one or two rich patches), but **not** in the sense of a calm tank's reach: a
straight root on tonight's tank could reach nearly every fresh patch (7 to 14), because a night
still buys 30 to 55 m of root. "2 to 4 patches in reach" holds only for where the root actually
goes. Making the tank's reach 2 to 4 would need a night to buy about a third of today's metres
(`cost_exponent`, `calm_life_force`) or a field of 50 m and more, both outside the spec's ranges;
the design thread should decide. Far wishes are reached in one night by a glow-following player
(nights buy enough), so "two or three nights" is an upper bound that holds, not the typical case.

## For tuning.md (rows the design thread writes in, [PLACEHOLDER] until felt on the phone)

- Days to finish, steered and never steered: the table above (replaces the 0.8 rows).
- Boost price for a hungry tree: new row, `boost_cost_liebig` 1.0 x morning hunger, range 0.5 to 1.
- Constant boost vs steering: boost all day 0 to 12 % sooner, boost in the morning up to 17 %
  (sycamore s3), boost all day and end every root at once 31 to 42 (all finish).
- Wider root field: `field_extent` 30, rings 5-12 / 12-20 / 20-30 m with 5 / 5 / 4 patches,
  `gap3` 7, `scatter3` 1200, `rocks3` 9 / 60, `deep_water3` 2, layout 3.
- Root cost per metre: 1.0 x (1 + 0.03 x distance + 0.12 x depth) in the wider field (0.06 kept
  for saves with layout 1 or 2).
- Wish distance: `far_share` 0.5, 9 to 17 m beyond the newest tip, 12 m or more out, within 2.5
  calm reaches, waits up to 3 mornings.
- Meadow signs: far patches at the clearing's edge (2.5 m inside it, 0.8 the size).
- Patch spacing / scattered dots rows: mark layout 2 values as "0.8 saves".

## Open points

- **Not run** (the session's permission to start Godot ended after it failed to stop five of its
  own stuck headless runs, see the report): the full test suite on the final code,
  `month_report --species=all`, `qa_nutri`, `qa_glow`, `autoplay`, the underground shots.
- Tests known to fail on the last run before the final changes (to be rechecked):
  `test_game_state` "steering leads after 12 days" (dots 507 against end_early 468 segments, the
  test asks 1.1x: in the wider field steering pays later in the month); "a root at least a third
  shorter" after a boosted day (seed 14: metres 0.70 of calm also without the boost change; the
  night's seconds 0.64); `test_care` thirst within six nights (the seep covers 0.75 of the smaller
  day-20 tree's water in the wider field against 0.66 before, so thirst does not show). The
  root wall, marks and B4 tests were adapted (field edge, a marked tip that is still a tip, the
  0.8 month on layout 2); `test_field.gd` is new and has not passed yet.
- boost_quit linden and alder at 41 to 42; oak straight_down 43.
- `tools/grow_shot.gd --roots` now also photographs the whole field from far out
  (`roots_field`, `roots_field_top`); not yet run.
