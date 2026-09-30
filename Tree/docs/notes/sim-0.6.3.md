# Balance fixes after the 0.6.3 play-through (stream s-sim063)

Written by the build stream s-sim063, 2026-09-30, for the design thread to merge into `tuning.md`
(owned by the design thread; not edited here). It answers the should-fix balance findings of the
0.6.3 review (`GameDev/tree-qa/check-0.6.3/playthrough.md`). The review's driver is now in the
repo as `tools/qa_care.gd` (with `--set=sim.x=v / roots.x=v / clock.x=v` overrides);
`tools/strategies.gd` takes `--set=sim.x=v` too and prints the finish height (`hfin`).

All numbers: headless Godot 4.7.2, seeds 3 / 14 / 27, "before" = the review's logs on 5b5ecb1.

## Changes

| Variable | Was | Now | Reason |
|---|---|---|---|
| Twin buds (`GrowthSim.prune`, `_growth_debt`) | a cut shoot tip forks into 2 free segments, plus the 0.3 refund | the 2 fork segments are paid with nutrients and taken from the day's following growth; no refund on a forked cut | Cutting 20 tips a day finished a sycamore 3 days sooner (broken 13). |
| Finish height (`GrowthSim.FINISH_HEIGHT_SHARE`, new) | none | a finished tree also stands at least 0.4 of its species' `max_height` (12 m linden, 10 m birch) | A sycamore pruned hard every day "finished" as a 1.8 to 2.7 m bush. Unpruned trees finish at 17 to 33 m. |
| Root starts (`RootSystem.can_start_run`, `has_room_for_root`) | at most `MAX_MAIN_ROOTS` (45) runs | a run starts while one more full root (400 path + 600 fine nodes) fits the root graph (sized 45 x 1000 nodes); `MAX_MAIN_ROOTS` only sizes the graph | After night 45 no root could start: a pruned oak piled up 2780 life force with N, P and K at 0. Real roots use about half their budget, so about 80 to 90 nights fit. |
| Boost life force (`DayCycle.boost_life_force_factor`) | 0.5 | 0.35 | A fully boosted day's root was only 23 to 29 % shorter (broken 2). 0.4 left birch at 29 to 32 %. |
| Run time (`RootSystem.run_seconds_for`: `calm_run_seconds`, `calm_ref_life_force`, `MIN/MAX_RUN_SECONDS`) | about 30 s whatever the tank | 34 s x sqrt(life force / 150), between 20 and 44 s | The calm-night pacing evened every night to 30 s, so a boosted day's night was 0 % shorter in seconds. |
| Pacing during the run (`RootSystem._repace`, `MAX_REPACE_SCALE` 3, `REPACE_EASE` 1.5 s) | speed fixed at the start from a typical price per metre (2.2) | the tip's speed eases toward (life force left / local price per metre) / time left, at most 3x base | Near the trunk metres are cheap, so early nights ran 54 to 58 s (target at most 52). |
| Boxed-in start (`RootSystem.STUCK_RESTART_SECONDS`, new) | the run ended after 4 s stuck, 0 m, life force kept | a start boxed in before its first segment restarts at the trunk after 1.2 s | straight_down: a 0 m, 4 s night with 173 to 361 life force in hand (broken 7). |
| Stock hold (`GrowthSim.hold_days`, `stock_room`, new) | none: stocks grew to 5 to 10 days | the old roots' nightly draw, the groundwater seep and alder's nodules fill each stock only up to 2 days of a full calm day's need (water also the leaves' upkeep); the deposits keep the rest. The new root's own finds are never held back | Water covered more than 2 days at dusk on 20 to 35 of 22 to 37 days (B3), so a dry night never showed. Tried and dropped: a lossy water drain (0.15 to 0.35 of the stock each night) slowed every style, since the day's pace is set from the water. |
| Seedling floor (`GrowthSim.SEEDLING_FLOOR`, `SEEDLING_DAYS`, new; `growth_floor()`) | soft Liebig floor 0.45 from day 1 | 0.6 on days 1 to 3 | Unsteered oak and beech grew 21 to 24 segments on days 2 and 3 (A1). |

Unchanged: `seep_per_metre` 0.03, `fine_seep_share` 0.3, `liebig_floor` 0.45, `PRUNE_REFUND` 0.3,
`Care.NEED_START`/`NEED_FULL`. Lower seep (0.02, or fine share 0.05 to 0.15) did not make thirst
show in the standard styles either, and it pushed never-steered linden past day 40.

## Results

**Pruning (qa_care, finish day).** Sycamore, 20 tips a day: 24/25/24 before, now 36/38/38 (never
pruned 27/28/27). Sycamore, cutting a fifth again and again every noon: 33/32/33 at 1.8 to 2.7 m
before, now not finished by day 45 (a bush). Tips on the other species: linden 39/43/-, birch
30/29/28, beech 40/39/39, alder 35/36/33, oak not by 45: never sooner than unpruned.

**Nights after 45 (qa_care, one cut of 30 segments a day, 60 days).** Oak 59/56/58 with up to
2780 life force at dusk before; now 57/55/57 with at most 235 at dusk (each night's root spent it).
Linden 45/47/47, birch 35/39/35.

**Broken 2 (strategies, boost_all against dots, nights 2 on, three seeds).** Root metres shorter:
before 23 to 29 % (beech 46 %), now 35 to 41 % (beech 60 %). Seconds shorter: before -5 to 8 %,
now 34 to 39 % (beech 48 %). `test_a_boosted_day_leaves_a_clearly_shorter_night` (same tree, same
evening, linden day 9): 162 against 57 life force, 34.7 against 19.7 m, 39.0 against 23.7 s.

**Stocks (qa_care, meadow play "dots", days a stock at dusk covers more than 2 days).** Water:
before 20 to 35 days of 22 to 37; now 0 to 10 (oak seed 27: 14 of 33, birch seed 27: 10 of 24, the
only ones over a third). N: 0 to 14, except alder 21 to 26 (its nodules and the bot's N finds; the
run's own finds are not held back). P 0 to 10, K 0 to 6. A nutrient at full need (B2): 0 to 3 days.

**Water need.** A tree whose roots stop finding water now shows thirst: with N, P and K plentiful
and the water deposits dry, thirst shows on the 6th night (`test_neglected_roots_can_leave_the_tree_thirsty`);
before, the 5 to 10 days banked hid it for well over a week. In the standard styles (dots, quit)
water need is still 0 days: a neglected tree is short of N, P or K first, grows at the soft floor
and so drinks less than the seep and its fine roots bring. Thirst shows with hard pruning (need on
4 to 26 days with daily or maximum cuts). Whether that is enough is a design question (see Open).

**Finish days (strategies, seeds 3/14/27).**

| Species | dots (steered) | end_early | straight_down | random | boost_all | boost_morning | boost_quit |
|---|---|---|---|---|---|---|---|
| linden | 29/30/32 | 37/37/37 | 37/38/37 | 37/37/37 | 30/30/30 | 28/29/30 | 40/39/40 |
| birch | 23/23/25 | 32/32/32 | 33/33/33 | 31/30/32 | 22/22/23 | 21/23/21 | 34/30/32 |
| beech | 29/29/30 | 35/35/35 | 35/35/35 | 34/35/35 | 32/31/33 | 29/29/31 | 44/40/41 |
| sycamore | 27/27/28 | 33/33/32 | 33/33/33 | 32/32/33 | 29/28/29 | 26/27/30 | 34/38/34 |
| alder | 27/27/26 | 31/30/32 | 36/34/36 | 32/31/32 | 29/27/24 | 26/28/26 | 36/40/35 |
| oak | 34/32/34 | 42/40/41 | 43/42/42 | 41/40/41 | 36/34/35 | 34/31/35 | 42/41/41 |

`tools/month_report.gd --species=all`: linden 29/30/30, birch 25/23/24, beech 29/30/30, sycamore
26/28/27, alder 27/26/30, oak 33/36/36 (before 30/31/30, 23/23/25, 29/29/28, 27/29/29, 28/26/28,
35/32/35).

Longest night: 50 s in strategies (before 55), 51 s in qa_care max pruning (before 58). Least
segments in a played day: 24 (oak straight_down, end_early; before 21) except boost_quit 19 to 20
on linden, beech, sycamore (before 22). No life force left unused after a night in any style
(before 173 to 176 on straight_down). Broken 1: boost_all and boost_morning are at most 2 days
faster than dots (birch boost_morning 21 against 23, -9 %; before -17 %).

## Open points
- boost_quit (boosting all day and ending every root at once) is now the slowest style: beech
  40 to 44, alder 35 to 40, and its least day is 19 to 20 segments on three species. A player who
  boosts gives up the night by design; beech's boosted life force quirk (0.7) makes it harsh.
- Boosted nights under 20 s: 2 to 6 per three months on boost_all (a small boosted tank at the
  slowest tip speed, `MIN_SPEED_SCALE` 0.65). Before, 1 to 3.
- Steered linden seed 27 finishes on day 32 (before 29), within 26 to 34.
- Water need in ordinary neglect stays rare because N, P or K run short first (above).
