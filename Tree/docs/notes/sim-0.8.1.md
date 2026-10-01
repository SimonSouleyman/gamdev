# 0.8.1 simulation: no-roots finish, a wider root field, far wishes

Written by the build stream s08-sim, 2026-10-01, for the design thread to merge into `tuning.md`
(not edited here). Items: (a) the tree played without roots finishes again (tuning.md "0.8 balance
fixes", check-0.8 item 1), (b) specs/0.8.md item 29 "A wider root field", (c) item 34 "Wish
distance". Headless Godot 4.7.2, seeds 3 / 14 / 27, `tools/strategies.gd` (all nine styles, six
species), `tools/qa_field.gd` (new). "Before" = the 0.8 check (`tree-qa/check-0.8/playthrough.md`).

**Status: verified (second session, 2026-10-01).** The four tests that failed on the first
session's code were settled (section "The four failing tests"); on the committed code all tests
pass (5633), `autoplay` exits 0, and `strategies.gd` (nine styles, six species, seeds 3/14/27),
`month_report --species=all`, `qa_nutri`, `qa_glow` and `qa_field` ran; the results table below is
that final sweep. Underground overview shots (phone path, gl_compatibility, 450x800, seed 14 day
20) are in `GameDev/tree-qa/sim-0.8.1/` (`roots_overview`, `roots_field`, `roots_field_top`).

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
| Boost life force (`DayCycle.boost_life_force_factor`), second session | 0.35 | 0.3 (bottom of its range 0.3 to 0.5) | Broken item 2. In the wider field a calm root drives further out and deeper, where a metre costs more, so at 0.35 the boosted night's root was 0.60 to 0.78 of the calm one in metres (seeds 27, 14, 3; the test's seed 14: 0.70). At 0.3: 0.53 to 0.67 in metres, 0.60 to 0.66 in seconds, the boosted night still 21 to 23 s. Boost styles moved by 0 to 2 days. |
| Seep cap (`RootSystem.seep_day_cover`, new; `GameState._sunrise` passes the cap), second session | no cap | groundwater brings at most 0.65 of a calm day's water | Care, tuning "Water upkeep": a steered tree's main roots reach 680 to 740 m by day 20 in the wider field (most of it beyond 14 m), and their seep alone covered 0.65 to 0.76 of its water, so a tree whose water deposits ran dry never showed thirst (`Care.NEED_START` 0.75). Below that, neglect shows (thirst on the first night of the test). Finish days moved by 0 to 2 days. |
| A reached wish deposit (`Underground.mark_wish_reached`, a "reached" flag on the patch and the saved deposit; `Diary._untouched`), second session | a root that reached the glow but grazed only its edge left the deposit "missed" | a deposit whose glow a root reached is never a missed deposit again; saved as the deposit row's 9th field | Item 34: a wish pointed a second time, five nights later, at a far deposit the bot had reached on its first night (`missed_ahead`), so that far wish counted as "reached in 6 nights". |

## Results (final sweep, second session, strategies.gd, seeds 3/14/27, "-" = not by day 45)

| Species (target) | wish | tip | dots | end_early | straight_down | random | boost_all | boost_morning | boost_quit |
|---|---|---|---|---|---|---|---|---|---|
| linden (30) | 29/29/29 | 30/32/32 | 29/30/30 | 38/37/36 | 38/38/40 | 37/37/38 | 29/30/28 | 27/27/28 | 40/42/35 |
| birch (25) | 24/24/24 | 25/27/25 | 24/24/24 | 26/28/31 | 31/31/30 | 29/31/30 | 24/23/24 | 21/21/22 | 34/29/29 |
| beech (30) | 28/28/28 | 29/30/29 | 28/30/28 | 38/36/35 | 36/37/37 | 35/36/35 | 30/30/30 | 27/29/27 | 37/38/35 |
| sycamore (30) | 28/28/28 | 31/29/31 | 29/30/29 | 37/35/35 | 39/36/37 | 36/36/36 | 28/28/28 | 25/25/26 | 36/36/35 |
| alder (30) | 28/28/28 | 29/28/29 | 28/28/28 | 37/35/37 | 38/38/40 | 36/35/35 | 30/32/28 | 26/28/26 | 42/39/41 |
| oak (35) | 32/32/32 | 33/33/37 | 33/33/32 | 39/39/39 | 43/43/44 | 41/41/41 | 30/31/31 | 31/30/29 | 40/38/39 |

- Steered months in range (wish 24 to 32, dots 24 to 33, tip 25 to 37); `month_report`: linden 29,
  birch 24, beech 28, sycamore 28, alder 28, oak 33.
- Never steered: end_early 2 to 9 days after dots (birch s3 only 2, as in the first session),
  straight_down and random 5 to 12 days, all by day 40 except oak straight_down 43/43/44 (limit
  about 42; was 43/43/43).
- Constant boost: boost all day 0 to 9 % sooner than dots (oak s3 30 against 33); boost in the
  morning 0 to 14 % except **sycamore s14, 25 against 30 (16.7 %)** (s3 now 13.8 %; the first
  session had s3 at 17 %). Not cheap to fix: neither the boost's price (0.35 to 0.3) nor
  `boost_hunger_gain` 2 moved it; the gain comes from growth (sycamore's low P and K needs leave
  little for `boost_liebig` to hold back, and the soft floor lets the afternoon grow on), so it
  needs a growth-side lever (`boost_multiplier` is at the bottom of its range). Left open.
- boost_quit finishes on all 18 runs, 29 to 42: linden s14 42 and alder s3 42 sit just past
  "about day 40" (`boost_hunger_gain` 2 moved runs by up to 5 days either way, noise, so it
  stays 1.0).
- Boosted nights (the test's seed 14, ninth evening): 0.59 of a calm night's metres, 0.60 of its
  seconds.

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

## The four failing tests (second session)

| Test | Verdict | What changed |
|---|---|---|
| `test_game_state`: steering leads after 12 days (507 vs 468 segments, wants 1.1x) | **The test**, as the spec's wording changed with the wider field: specs/0.8.md "A wider root field" keeps the starter patch and the first week as they were ("the first days are not harder") and asks that "ending early still clearly loses" through items 4 and 10a, which are finish days. On day 12 both trees follow the growth curve on the starter patch and the near ring (dots 507 to 515 on all three seeds, end_early 382 to 468; moving only `fine_reach_max_extra` swings end_early from 337 to 581, so no game lever makes a day-12 count robust); steering's lead shows from week 3 on. | The test now asks that steering to deposits finishes at least 4 days before ending early (seed 14: day 30 against 37), and that ending early still finishes by day 40. `acceptance.md` updated. |
| `test_game_state`: a boosted day leaves a root a third shorter (33.5 vs 47.8 m, 0.70) | **The game** (broken item 2). | `boost_life_force_factor` 0.35 to 0.3 (row above). |
| `test_care`: thirst within six nights without new water | **The game** (tuning "Water upkeep": thirst shows when the roots stop finding water). | The seep cap (row above). |
| `test_field`: far wishes reached within three nights (6) | **The game**: every far wish was reached on its first night; the 6 was a wish pointing again at a deposit already reached. | The "reached" flag (row above). |

Side effect: `test_marks` took its tree at noon on day 14 and expected a marked twig; the seep cap
moved this seed's seeded marks (day 14 none, day 15 one). The three tests now take the first noon
from day 14 on with a mark (at most four days on) and check the same things.

## Open design point: a calm tank still reaches 7 to 14 rich patches (for the design thread)

Item 29 asks that "a calm night from the best start reaches about 2 to 4" rich patches. A night's
root as played touches 0 to 3 and its straight drive passes 1 to 4, which fits; but a straight root
on tonight's whole tank could reach 7 to 14 of them (`qa_field` linden s14: median 8, max 14),
because a calm night still buys 30 to 55 m of root (a 25 m drive costs only 1.75x a metre by the
trunk at `distance_cost` 0.03). Two ways to bring that to 2 to 4:

1. **Shorter nights in metres (recommended).** Raise the price of a metre so a calm night buys
   about 12 to 18 m: `base_cost_per_metre` about 1.0 to 2.5, or `cost_exponent` 0.5 to about 0.8
   (a big tank buys fewer extra metres). The night's seconds stay, since the run is paced to the
   tank's time (`run_seconds_for`); only the tip slows. It keeps the 30 m field, the loaded-dot
   budget (4000), the Fairphone frame budget and the meadow signs, and the far ring then really
   needs a root continued over two or three nights, which is what Simon asked for. To retune with
   it: deposit size or `tip_share` so steered months stay 26 to 34, the far wish's reach
   (`FAR_REACH_NIGHTS`, `REACH_SHARE`; they follow `line_cost`, so mostly automatic), the first
   week (starter patch 1.2 m out, unchanged) and B1 within two nights.
2. **A bigger field or dearer distance.** A field of 50 m or more (about three times the area
   again, so thinner dots or past the dot budget, a wider overview, far signs further beyond the
   clearing), or `distance_cost` back toward 0.06 to 0.1, which the spec halved so that far
   patches pay at all. Both are outside the spec's ranges and make the far ring a worse trade
   rather than a longer journey.

Not changed now; for the design thread to decide.

## For tuning.md (rows the design thread writes in, [PLACEHOLDER] until felt on the phone)

- Days to finish, steered and never steered: the table above (replaces the 0.8 rows).
- Boost price for a hungry tree: new row, `boost_cost_liebig` 1.0 x morning hunger, range 0.5 to 1.
- Constant boost vs steering: boost all day 0 to 9 % sooner, boost in the morning up to 14 %
  except sycamore s14 (16.7 %), boost all day and end every root at once 29 to 42 (all finish).
- Boost life force: 0.3 (was 0.35); a boosted night 0.53 to 0.67 of a calm one in metres.
- Groundwater seepage: new cap, at most 0.65 of a calm day's water (`seep_day_cover`, range 0.55
  to 0.7; at 0.75 or more thirst cannot show).
- Wider root field: `field_extent` 30, rings 5-12 / 12-20 / 20-30 m with 5 / 5 / 4 patches,
  `gap3` 7, `scatter3` 1200, `rocks3` 9 / 60, `deep_water3` 2, layout 3.
- Root cost per metre: 1.0 x (1 + 0.03 x distance + 0.12 x depth) in the wider field (0.06 kept
  for saves with layout 1 or 2).
- Wish distance: `far_share` 0.5, 9 to 17 m beyond the newest tip, 12 m or more out, within 2.5
  calm reaches, waits up to 3 mornings.
- Meadow signs: far patches at the clearing's edge (2.5 m inside it, 0.8 the size).
- Patch spacing / scattered dots rows: mark layout 2 values as "0.8 saves".

## Open points

- Sycamore boost_morning s14 16.7 % sooner than dots (limit 15 %); see the results.
- boost_quit linden s14 and alder s3 at 42; oak straight_down 43 to 44 (limit about 42).
- The tank's reach (7 to 14 rich patches): the design point above.
- `roots_field` and `roots_field_top` show the patches and dots across the field, but the roots
  are too thin to read from that far out; worth a look with the 0.8.2 "zooming far out" item.
