# Softer month (0.6.x, branch s-soft)

Simon's decision ("Softer"): a player who never steers to the dots still finishes the tree, just
later (about day 36 to 40), and every played day shows growth (at least about 20 segments).
Steering to dots stays clearly the fastest way. This note holds the numbers for the tuning table
(`docs/tuning.md` is owned by the design thread; copy them over from here).

## Changes

| Variable | Was | Now | Reason |
|---|---|---|---|
| Soft Liebig floor for the tree (`GrowthSim.liebig_floor`, passed to `Resources.growth_factor`) | 0.35 | 0.45 | A missing N, P or K slowed the non-steering styles to 5 to 15 segments a day. At 0.45 they grow 25+ a day. The bonsai keeps the 0.35 default of `Resources.growth_factor`. |
| Groundwater seep (`RootSystem.seep_per_metre`, new, at sunrise in `drink_tapped`) | none | 0.03 water per metre of main root per night | Water is a hard cap on growth. Straight-down roots sat at 4 water all month. The seep keeps any root network drinking, even one that found no deposit (soft failure). It is tied to steered main roots, so ending the run at once gains little from it. |
| Fine roots' share of the seep (`RootSystem.fine_seep_share`, new) | none | 0.3 | Fine roots from leftover life force are cheap metres. At full share (tried 0.01 per metre of the whole network) quitting early finished on day 33 or 34. |
| Boost and missing nutrients (`GrowthSim.boost_liebig`, new) | 0 (implicit) | 1.0 | With the higher floor and the seep, constant boosting finished linden on day 23 or 24 (20 % faster than steering). Now the boost's extra light only counts as far as N, P and K allow (the floor-free ratio). Without nitrogen a boost adds nothing. With the nutrients in hand it works as before, so steering to the dots the tree lacks is also what makes boosting pay. |
| Main-root cap (`Budgets.MAX_MAIN_ROOTS`) | 40 | 45 | The slow styles on oak finish on day 41 or 42. At 40 roots the last nights had no run and 200 to 430 life force piled up (broken list 3). |

`NIGHTLY_SHARE` (0.05), water factor (2), `REGROW_SHARE` and the leftover fine roots are unchanged.

## Seeded runs (tools/strategies.gd, seeds 3 / 14 / 27, finish day, "-" = not by day 45)

| Species, style | Before | After | Least segments in a day, after |
|---|---|---|---|
| linden dots | 29 / 30 / 31 | 29 / 27 / 29 | 26 |
| linden end_early | - / - / - | 37 / 36 / 37 | 25 |
| linden straight_down | - / - / - | 37 / 37 / 38 | 27 |
| linden random | - / - / - | 37 / 36 / 37 | 30 |
| linden boost_all | 30 / 31 / 32 | 28 / 27 / 28 | 25 |
| linden boost_quit | - / - / - | 37 / 35 / 37 | 26 |
| birch dots | 23 / 23 / 23 | 23 / 23 / 23 | 45 |
| birch passive styles | 38 to never | 31 to 33 | 34 |
| birch boost_all | 21 / 22 / 22 | 21 / 20 / 21 | 40 |
| oak dots | 35 / 35 / 38 | 34 / 31 / 37 | 26 |
| oak passive styles | 44 to never | 41 / 42 | 21 |
| oak boost_all | 32 / 33 / 30 | 33 / 30 / 34 | 26 |

Before: linden non-steering styles had days of 4 to 15 segments and left up to 1045 life force
unused (after the 40-root cap). After: no style leaves life force unused except one stuck night
(straight_down seed 3, 176). The longest night is 51 s.

`tools/month_report.gd --species=all` (seed 14, finish day before / after): linden 31 / 31,
birch 23 / 23, beech 30 / 28, sycamore 28 / 29, alder 27 / 26, oak 35 / 32. All are within 15 %
of their targets.

## Open points
- On birch the slow styles are 8 to 10 days behind steering (31 to 33 against 23). On oak they are
  about 6 days behind (41 or 42 against 31 to 37). The gap is a share of the month, so a slow
  species ends past day 40 for a player who never steers.
- boost_all is still 1 or 2 days faster than dots on linden and birch, within the 15 % limit.
