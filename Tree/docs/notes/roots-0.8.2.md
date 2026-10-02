# 0.8.2 roots: the dearer metre, far wishes per seed, cut marks, side roots and thicker roots

Written by the build stream s082-roots, 2026-10-01, for the design thread to merge into
`tuning.md` (not edited here). Items: (1) the dearer metre (tuning "Dearer metre in the wide
field", specs/0.8.md item 29), (2) far wishes balanced per seed (item 34; the 0.8.1 check's
minor 2), (3) cutting marked twigs never sooner (0.8.1 check minor 5), (4) side roots in two more
levels and thicker roots (specs/side-roots.md). Headless Godot 4.7.2, seeds 3 / 14 / 27, six
species: `tools/strategies.gd` (all eleven styles), `month_report --species=all`, `qa_nutri`,
`qa_glow`, `qa_field` (dots and wish), `qa_care`; phone-look shots in
`GameDev/tree-qa/roots-0.8.2/`. Raw runs: `tree-qa/roots-0.8.2/exp` (R1 = this branch).

## What changed and why

| Variable | Was (0.8.1) | Now | Reason |
|---|---|---|---|
| Root cost per metre (`RootSystem.base_cost_wide`, set by `fit_soil`) | 1.0 | 2.5 in the wider field (layout 3); layouts 1 and 2 keep 1.0 | (1) Simon "Ja, Meter teurer". A calm night's root (dots and wish play, nights 3 on) is now 13 to 22 m long, median 17 (was 30 to 55 m); nights still 20 to 44 s (paced to the tank). `cost_exponent` stays 0.5: 0.7 on top made the continued-root ("tip") style 1 to 2 days slower again. |
| Root sinking (`RootSystem.advance`, drift) | 0.07 m/s, whatever the pace | x min(1, run_speed_scale): it sinks per metre as at full speed | Side effect of (1): the tip is paced slower (12 to 18 m in the same seconds), so it sank three times as steeply and a root continued from its newest tip ended on the floor at 10 m within a week (tip style never finished linden or oak). Old soils pace at 1x or more, unchanged. |
| Deposit size (`Underground.deposit_shares_wide`) | 1.15 (`DEPOSIT_SHARES`) | 1.5 in layout 3; older soils 1.15 | (1) A dearer root touches fewer deposits; at 1.15 steered linden needed 32 to 35 days. `tip_share` 0.28 was tried instead and did not help (the night's room, `run_room`, caps what a first contact brings; the gain is in the old roots' nightly draw, which follows the deposit's size). A 0.8.1 save in layout 3 gets the bigger deposits on load (capacity is not saved, amounts are kept). |
| Seep per metre (`seep_per_metre_wide`, `fine_seep_share_wide`) | 0.03; fine roots 0.3 | 0.075 (= 0.03 x 2.5); fine and second-level roots 1.0, third level 0.5; layouts 1 and 2 unchanged | (1) and (4): a never-steered tree lives on the seep (sim-0.8.1 "Softer"). With 2.5x shorter roots and side roots of 250 nodes within 3 m instead of 600 fine-root nodes within 7.6 m, linden ending early never finished (water stock gone by noon). The cap `seep_day_cover` 0.65 still bounds it. |
| Soft Liebig floor (`GrowthSim.liebig_floor`) | 0.55 | 0.6 (top of its range) | (4): the short side roots find little N, P and K, so a tree never steered leans on the floor; at 0.55 oak ended early on day 43 to 44. |
| Wish reach (`Diary.calm_reach`) | `calm_life_force` x `REACH_SHARE` (30 life force) | the same in the old soil's metres (x base cost / 1.0) | (1): at 2.5x the near wish's 5.5 to 8.5 m from a deep tip failed the reach check and most underground wishes became day wishes (3 to 7 a month in wish play). A calm night still drives about 8 to 11 m straight from its start, so a near wish stays one night, a far one (9 to 17 m beyond the tip) one or two. `qa_glow` measures against it (far wishes against `FAR_REACH_NIGHTS`). |
| Far wish draw (`Diary.wish_days`, `far_days`, `plan_wish(far_want)`) | an independent coin per day (`hash([seed, "far_wish", day])`) | a running share: a far wish is tried when `far_share` x (underground mornings + 1) - far mornings is at least a seeded threshold of 0.25 to 0.75; waiting days count; a missed far deposit is pointed at again only when the share allows; saved with the diary (old saves start at 0) | (2): seed 3 got 25 to 35 % in 0.8.1. Now wish play: 44 to 59 % on every seed and species (table below). |
| First-level fine roots (`fine_nodes_per_life_force`, `fine_reach_*`) | 150 + 6 nodes per leftover point (max 600), reach 1.6 m + 0.12 m per point (max 7.6 m) | 150 nodes, 1.6 m, fixed (removed) | (4) specs/side-roots.md "What changes". |
| Second level (`side_nodes_per_life_force`, `side_reach_base/per_life_force/max`, `side_level3_share`, `side_nodes_max`) | | 4 nodes per point of leftover, 70 % to the second level; from the first level's tips (and points along the new root while it has fewer than 4 tips) toward fresh dots within 1.0 m + 0.05 m per point, at most 3.0 m (spec 2.5, range 1.5 to 3); where a start has no dot in reach, 2 to 5 short tips (more with more leftover); drink at `fine_share` 0.1; no node more than its reach from its start | (4). 3.0 m: at 2.5 a never-steered oak finished a day later (43). The short tips are what shows in the sparse field (most nights a stub finds no dot within 3 m). |
| Third level (`side3_reach_*`, `SIDE3_MIN_LENGTH`, `side3_share`) | | 30 % of the nodes; from along (every third node from 0.25 m) and at the tips of second-level roots at least 0.5 m long, toward dots within 0.3 m + 0.02 m per point, at most 0.8 m, or short tips; drink 0.05 | (4) as specced. |
| Node cap (`Budgets.SIDE_ROOTS_PER_MAIN_ROOT`, `side_nodes_max`) | | second and third level at most 250 a night (hard budget 300), first level 150, all within the 600 per main root; the graph size is unchanged | (4) edge cases. |
| Near-empty tank (`side_min_left_share`) | | less than 0.1 of tonight's tank left: no side roots | (4). |
| Thickness (`thick_gain`, `RootSystem.thickness`, saved per main root) | | 1.0 + 0.5 x the share of the tank spent on the root (1.0x ended at once, 1.5x run dry), set once at the end of the run; old saves 1.0 | (4). |
| Seep of a thick root (`seep_thick_gain`) | | a main root seeps 1 + 0.25 x (thickness - 1) / 0.5 per metre (1.25x at 1.5x) | (4). |
| The look (`RootView`, `BranchMeshBuilder.radius_mul`, `bark.gdshader`) | every root at the fine-root floor (0.026 m) | main roots 1.4x the floor times their thickness; second level 0.8x, third 0.4x and dimmer (vertex colour 0.62, which now also dims the glow rim); tonight's root and its fan keep the run's warm glow while they settle; one merged mesh as before | (4) broken 5 and 8: at the floor the thick root and the fan read the same as the old fine roots. |
| `tools/strategies.gd --nights` | | also prints what the run drank, the leftover, side nodes and thickness | Tool. |
| `tools/grow_shot.gd --roots --run` | | `--tank=`, `--side` (end early, photograph the fan at the end-of-run camera), `--full` (spend the tank, photograph the thick root) | Tool. |

## Results (strategies.gd, seeds 3/14/27, R1; "-" = not by day 45)

| Species (target) | dots | wish | tip | end_early | straight_down | random | boost_all | boost_morning | boost_quit | cut_marks | cut_marks_tip |
|---|---|---|---|---|---|---|---|---|---|---|---|
| linden (30) | 30/30/31 | 29/29/30 | 35/35/35 | 38/37/37 | 37/37/38 | 37/37/36 | 29/31/32 | 29/28/27 | 36/36/36 | 30/31/30 | 31/30/31 |
| birch (25) | 25/25/24 | 25/24/24 | 26/28/28 | 30/32/31 | 29/31/31 | 30/31/30 | 24/23/24 | 24/22/22 | 29/31/30 | 26/26/26 | 25/25/26 |
| beech (30) | 30/28/29 | 29/29/29 | 32/32/34 | 37/36/36 | 37/36/36 | 34/35/35 | 28/30/31 | 27/29/27 | 36/36/35 | 30/28/29 | 30/28/29 |
| sycamore (30) | 29/30/29 | 29/28/28 | 35/34/35 | 36/38/35 | 36/37/37 | 35/35/35 | 28/30/27 | 28/28/27 | 35/35/36 | 30/30/30 | 29/30/30 |
| alder (30) | 28/30/30 | 28/29/28 | 32/35/35 | 37/36/36 | 36/36/36 | 35/35/35 | 28/26/28 | 26/27/27 | 37/35/35 | 28/30/29 | 28/28/29 |
| oak (35) | 32/33/34 | 32/33/32 | 40/36/39 | 40/41/39 | 40/42/41 | 38/41/40 | 31/32/32 | 30/32/30 | 40/41/40 | 34/33/33 | 32/34/34 |

- Steered months in range: dots and wish linden 29 to 31, birch 24 to 25, oak 32 to 34;
  `month_report`: linden 31, birch 24, beech 28, sycamore 32, alder 28, oak 33.
- Never steered inside 10a on all 54 runs: 4 to 9 days after dots, all by day 38 (birch 32),
  oak by day 42 (oak straight_down 40/42/41; was 43/43/44).
- Boost: boost all day at most 13.3 % sooner than dots (alder s14 26 vs 30; 0.8.1: 9 %); boost in
  the morning at most 12.9 % (linden
  s27 27 vs 31; sycamore s14 now 28 vs 30, 6.7 %, was 16.7 %). Boost all day and end every root
  at once finishes on all 18 runs, day 29 to 41 (linden 36, oak 40 to 41; was up to 42).
- The continued-root style "tip" is the one steered style past its range: linden 35 (target 26
  to 34), oak s3 40 (31 to 39). The bot continues from the newest tip, by mid-month at the
  field's edge (25 to 30 m out) where a metre costs most and the far ring is sparse; following
  the glow from the same tip ("wish") is 29 to 30. `distance_cost` 0.02 brought tip to 34 to 35 /
  36 to 38 but nights to median 19 m (R2); left at 0.03. Open, small.
- Least growth on a day: 22 or more except cut days (oak cut_marks s14 19, the cut counts).
- Nights: 13 to 52 s; 5 % of the non-quitting nights from night 3 on run 45 to 52 s (0.8.1:
  7.5 %, max 53). A boosted night is 0.58 to 0.76 of a calm one in seconds and 0.48 to 0.77 in
  metres (0.8.1: 0.57 to 0.71 / 0.40 to 0.69): item 2 borderline as in 0.8.1; the test's seed 14
  case passes.

**The wider field with the dearer metre (qa_field, 18 runs per style).**

| Measure | dots (meadow start) | wish (glow from the newest tip) |
|---|---|---|
| Rich patches touched by a night's root | median 0 to 1, max 1 to 2 | median 0, max 0 to 2 |
| Rich patches within tonight's straight drive | median 0 to 2, max 2 to 6 | median 0 to 2, max 2 to 7 |
| Rich patches a straight root on tonight's whole tank reaches from the start | median 2 to 6, max 4 to 12 (0.8.1: 7 to 12 / 10 to 14) | median 2 to 5, max 6 to 12 |
| Straight distance a night gets from its start | median 7 to 11 m, max 13 to 19 m (0.8.1: 11 to 16 / 23 to 37) | median 8 to 10 m, max 14 to 17 m |
| Underground-wish days pointing far | 60 to 80 % (the bot ignores the glow; a far wish waits 3 mornings) | 44 to 59 % on every seed (linden 50/53/55, sycamore 47/50/53, oak 50/50/50, birch 47/50/50, alder 44/50/56, beech 50/50/59) |
| Far wishes reached | rarely (ignored) | all, in 1 night (two thirds) or 2 nights (one third) |
| First week, every needed kind within one calm night | 7 of 7, all runs | the same |
| B1 scarcest kind within one / two calm nights | 100 % / 100 % | 100 % / 100 % |

What it means: the tank no longer reaches the whole ring; the median night reaches 2 to 6 rich
patches in a straight line (item 29's 2 to 4 on the median, more late in the month when the tank
is 180 to 230), and a far wish now takes one or two nights of a continued root instead of always
one. qa_nutri: B1 100 %, B2 floor days 0 to 7 of 23 to 33 (oak s27 7), B3 0 %, B4 17 to 25
patches by its own measure (from any root node, any fresh dot; 0.8.1 20 to 24). qa_glow: never
two glows, never a glow out of reach. qa_care (linden s14): no signal without a need or out of
reach, nothing shrinks; pruning daily or at the maximum never finishes.

## Side roots, as played (linden s14, a quitting night every other night)

A night ended at once with 100 to 180 left grows 100 to 175 second-level and 75 third-level
nodes; a run to the end grows none and a 1.5x root. The end of a run with side roots costs one
frame of 6 to 16 ms on the PC (space colonization of the two levels, `tools/strategies.gd`
"slowest frame" includes it: up to 155 ms with 14 runs in parallel); the root graph's budget is
unchanged (the 400 + 600 per main root it was sized for). Drawing: no node per root, the same
two merged meshes (old roots, tonight's root).

Phone-look shots (gl_compatibility, 450x1000, `--phone`, linden s14 day 16):
`roots_side.png` (ended at 1.2 m with 109 left: 123 + 75 side nodes) shows the fan as many fine
hanging roots with small forks at their tips around the stub, warm against the pale old roots;
`roots_full.png` (12.3 m, tank dry, 1.5x) shows tonight's root as the thickest warm line;
`roots_run.png`, `roots_overview.png`, `roots_close.png`, `roots_medium.png`, `dive_*.png`.

## Item 3: cutting marked twigs

On R1, 4 of 36 cut runs finished a day or two before dots (linden cut_marks s27 30 vs 31, alder
cut_marks s27 29 vs 30, alder cut_marks_tip s14 and s27 28 and 29 vs 30, oak cut_marks s27 33 vs
34). These runs are measuring the nights, not the cut: `dots` itself, with `cost_per_node`
changed by a millionth (`--set=sim.cost_per_node=0.0800001`), finished alder s14 and s27 on day 28
instead of 30 and linden s27 on 29 instead of 31; the same three runs. A day's growth that
differs by one leaf changes tonight's tank and the bot's choice of kind, and a different deposit
found on night 19 moves the finish by up to three days (alder s14: phosphorus found on night 19
in one run, not in the other). Reseeding the tree's RNG every sunrise did not remove it.

So the game rule was checked where the nights cannot move it: the new test
`test_marks.gd: test_cutting_marked_twigs_never_finishes_a_month_sooner` plays linden, sycamore
and alder (seed 14) from day 10 to the finish in three copies fed alike every night: cutting
every marked twig at noon at its fork or just the tip finishes on the same day as never cutting
(linden 29/29/29, sycamore 27/27/27, alder 27/27/27; oak 31/31/31 tried too). The cut itself
gives back at most 0.3 of the living wood that would have stayed, and nothing for the dying tip
(0.7 rule, unchanged). No game number was changed for item 3; a single-run sweep comparison
cannot show "never sooner" while one run's nights move it by up to three days.

## Tests changed (and why)

- `test_care: neglected roots can leave the tree thirsty`: in the wider field the seep reaches its
  cap (0.65 of a calm day) on a steered network, and a day-20 tree on its growth curve uses only
  about 0.7 of a calm day's water, so what is left at dusk plus the seep covers it: it is not
  short of water, and a thirst sign would be broken 11. The test now checks what the cap is for:
  the seep alone brings at most 0.65 of a calm day, the cap is below the sign's start, and a tree
  that has used its water up wakes thirsty within the six nights. The game was tried
  first: the seep topping the stock up to 0.65 instead of adding 0.65 made every tree never
  steered miss day 45; `seep_day_cover` 0.55 pushed linden ending early to day 41 to 42.
- `test_care: crown shape cues`: the bare-crown share's threshold follows its own comment's
  measured range (0.32 to 0.63): 0.3, was 0.4; this seed's day-10 crown keeps 0.37 now.
- `test_root_system: leftover reaches only so far`: the first level no longer widens; checks the
  side roots' reach and node cap instead.
- `test_field` / `test_wish`: the far wish's reach check uses `Diary.calm_reach`.
- New: `tests/test_side_roots.gd` (items 1, 2, 4: a calm night 12 to 18 m and 20 to 44 s; far
  wishes 40 to 60 % on seeds 3/14/27; two levels, reach caps, node caps, short tips without dots,
  near-empty tank, thickness set once and saved, old saves 1.0x, thick seep, the look per level
  in one mesh) and the month test in `test_marks.gd` (item 3).

## For tuning.md (rows the design thread writes in, [PLACEHOLDER] until felt on the phone)

- Dearer metre in the wide field: `base_cost_wide` 2.5 (layout 3; old soils 1.0), `cost_exponent`
  0.5 unchanged; a calm night's root 13 to 22 m (median 17), 20 to 44 s.
- Root sinking: per metre (x min(1, pace)), new row or a note under "Root speed".
- Deposit size: 1.5 in layout 3 (`deposit_shares_wide`), 1.15 for older soils.
- Groundwater seepage: 0.075 per metre of main root in layout 3, fine and second-level roots 1.0
  of that, third level 0.5, a thick root up to 1.25x; cap unchanged (0.65, additive).
- Soft Liebig floor: 0.6 (was 0.55), now at the top of its range.
- Wish distance: the reach check in the old soil's metres (`Diary.calm_reach`); the far draw a
  running share (44 to 59 % per seed in wish play).
- Fine roots from leftover: replaced by the two side-root rows (values in the table above; second
  level reach max 3.0, inside its range 1.5 to 3).
- Root thickness: 1.0 to 1.5x as specced, seep 1.25x.
- Days to finish, steered and never steered: the table above (replaces the 0.8.1 rows).
- Constant boost vs steering: boost all day at most 13.3 %, in the morning at most 12.9 % (the sycamore
  s14 16.7 % is gone); boost all day and end every root at once day 29 to 41.
- 0.8.1 sim's "open design point" (a calm tank reaches 7 to 14 patches): now 2 to 6 on the median.

## Open points

- The "tip" style (continue from the newest tip, chase dots): linden 35, oak s3 40, a day past
  their ranges; glow-following from the same tip is in range.
- Boosted nights 0.58 to 0.76 of a calm one in seconds (item 2 borderline, as in 0.8.1).
- Item 3 in single-run sweeps is within the nights' noise (above); the fed-alike month test holds.
- A run's end with side roots is one 6 to 16 ms frame on the PC; measure on the Fairphone with the
  0.8.2 build (item 30).
- The fan hangs mostly downward (second-level short tips lean down, the colonizer's soil bias);
  Simon may want it to spread more sideways.
