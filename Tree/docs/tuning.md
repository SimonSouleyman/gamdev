# Tuning table

Every number that shapes pacing or the life-force economy, with its value, a range to tune in,
and the reason. [PLACEHOLDER] means the value has not been felt on the phone yet or is known to
be off. Each code change to such a number updates this table in the same or the next pull
request (design-practice.md).

Values below are the settled 0.6.3 numbers on `main` (tag work for v0.6.3, 2026-09-30: the new
tree, tree care, the roots, "Softer" and the balance fixes after the 0.6.3 play-through; notes in
docs/notes/sim-0.6.3.md, care-0.6.3.md, soft-0.6.x.md). 0.6.3 is on Simon's phone since
2026-09-30 00:25 UTC; none of these values has been felt there by Simon yet. Where a value changed
in 0.6.x, the tree-v0.6 value is in brackets.

Last checked against the code: 2026-09-30 00:35 UTC. File and constant in brackets.

## Clock and pacing

| Variable | Value | Range | Reason |
|---|---|---|---|
| Full day, real seconds (`DayCycle.seconds_per_day`) | 192 s | 150 to 300 | Play test 3: the day runs on its own and quickly. The design doc's 5 min day was shortened by Simon. |
| Daylight share (`daylight_fraction`) | 0.625 (120 s day, 72 s night clock) | 0.55 to 0.7 | Day is where the tree is watched; the night is held while the root runs, so its clock share matters little. |
| Night hold while the root runs (`NIGHT_HOLD_MARGIN`) | clock stops 0.5 s before dawn until the run ends | fixed | The run is never cut off by sunrise, so night length is set by the run. |
| Night fast-forward after the run (`NIGHT_FAST_FORWARD`) | 30x | 10 to 60 | The rest of the night passes in about 2 s. |
| Empty night visit (`EMPTY_NIGHT_VISIT`) | 5 s | 3 to 10 | A night without life force is a short look, not a wait. |
| Real run length (`RootSystem.run_seconds_for`) | 34 s x sqrt(life force / 150), between 20 and 44 s (was up to 112 s) | 20 to 60 s | [PLACEHOLDER] The night follows the tank, so a boosted day's night is clearly shorter; longest night in the sweeps 50 to 51 s. |
| Dawn burst (`GrowthSim.dawn_burst_share`) | 0.25 of affordable growth, max 30 nodes, over 10 s | 0.15 to 0.35 | The night's purchase shows at once, with the twinkle. |
| Morning line delay (`MORNING_DELAY`) | 11 s | fixed | Just after the dawn burst. |
| Finish size (`Species.finish_nodes`) | linden 2100 (1800), birch 1880 (1740), beech 2080, sycamore 1800 (2150), alder 1740, oak 1970 | per species | [PLACEHOLDER] Retuned for the new tree's growth curve so each species hits its month (below). |
| Days to finish, steered (meadow play, seeds 3/14/27) | linden 29/30/32, birch 23/23/25, beech 29/29/30, sycamore 27/27/28, alder 27/27/26, oak 34/32/34 | linden 26 to 34, birch 22 to 28, oak 31 to 39 | [PLACEHOLDER] A month per tree is a pillar. |
| Days to finish, never steered (end early, straight down, random) | linden 37 to 38, birch 30 to 33, beech 34 to 35, sycamore 32 to 33, alder 30 to 36, oak 40 to 43 | about 5 to 10 days after steered | [PLACEHOLDER] Simon, "Softer" (2026-09-29): a tree still finishes without steering, clearly later. |
| Least growth on any day | 24 segments in every style except boost+quit (19 to 20 on linden, beech, sycamore) | at least 20 | Every day must show visible growth (design doc section 7). Boost+quit gives up the night by design. |
| Finish height (`GrowthSim.FINISH_HEIGHT_SHARE`) | a finished tree also stands at least 0.4 of its species' height (12 m linden, 10 m birch) | 0.3 to 0.5 | New in 0.6.3: a tree pruned hard every day "finished" as a 2 m bush. |
| Seedling floor (`SEEDLING_FLOOR`) | growth never below 0.6 of full speed on days 1 to 3 | 0.5 to 0.7 | New in 0.6.3: unsteered oak and beech grew barely 21 segments on days 2 and 3. |
| Growth cap per second (`max_growth_per_second` x species pace) | 1 node/s x light | 0.7 to 1.5 | Calm growth is capped so the nutrients last the day. |
| Node cost (`cost_per_node` x (1 + nodes/380)) | 0.08 rising with size | 0.05 to 0.12 base | A big tree needs more per segment, so growth spreads over the month. |
| Young leaves (`young_leaf_bonus`) | up to 2.2x life force while the tree is small, easing off as it grows | 1 to 2.5 | [PLACEHOLDER] The new tree starts as a thin whip with few leaves; this keeps its first nights worth steering. |

## Life force and boost

| Variable | Value | Range | Reason |
|---|---|---|---|
| Life force per leaf cluster (`life_force_per_tip`) | 0.02 per tip per second x light | 0.01 to 0.04 | Scaled to the 2 min day. |
| Self-shading (`effective_leaves`) | tips up to 50, then sqrt(50 x tips) | fixed | A big crown yields less per leaf. |
| Boost growth (`boost_multiplier`) | 2x light for growth (3x) | 2 to 3 | [PLACEHOLDER] A tap should visibly speed growth; 3x made constant boosting finish far too early. |
| Boost life force (`boost_life_force_factor`) | 0.35x while boosted (0.4x, briefly 0.5x) | 0.3 to 0.5 | [PLACEHOLDER] The trade: a fully boosted day's root is now 35 to 41 % shorter in metres and 34 to 39 % in seconds (beech 60 % and 48 %). |
| Boost without nutrients (`boost_liebig`) | 1.0: the extra light counts only as far as N, P and K allow | 0 to 1 | A brighter sun cannot make up for a missing nutrient, so boosting a hungry tree gives nothing. |
| Constant boost vs steering | boost all day: linden 30, birch 22 to 23, oak 34 to 36; at most 2 days faster than steering (birch boost in the morning 21 against 23, -9 %) | within 15 % | [PLACEHOLDER] Inside the broken-list limit. Boost all day and end every root at once is now the slowest style (beech 40 to 44). |
| Boost length and stacking (`boost_hour`) | one game hour per tap, up to three ahead | fixed | Survey 1 and play test 3. |
| Seed life force (`SEED_LIFE_FORCE`) | 20 | 15 to 30 | Enough for the first root from the seed. |
| Offline growth (`apply_offline`) | one real day away = 20 s of mid-morning growth | 10 to 40 s | Coming back shows a little growth; never a reason to stay away. |
| Offline life force | +4 per real day away, max 8 | 2 to 10 | The player always comes back with enough for a short root. |
| Species life force factors (`species.gd`) | birch 0.9; beech calm 1.25, boosted 0.7; linden blossom 1.2 on days 18 to 22 | see section 15 | The quirks. |

## Roots and nutrients

| Variable | Value | Range | Reason |
|---|---|---|---|
| Root cost per metre (`RootSystem.base_cost_per_metre`) | 1.0 x (1 + 0.06 x distance + 0.12 x depth) | base 0.8 to 2 | Far and deep costs more (design doc section 5). |
| Birch topsoil cost, oak downward depth cost | 0.7x, 0.5x | per quirk | Section 15 quirks. |
| Calm-night pacing (`calm_life_force`, `cost_exponent`, `calm_run_seconds`, `calm_ref_life_force`) | above 50 life force each metre costs more (power 0.5); the run lasts 34 s x sqrt(tank / 150), 20 to 44 s | 30 to 45 s at a full calm tank | [PLACEHOLDER] Nights had grown to 80 to 110 s. |
| Pacing during the run (`_repace`, `MAX_REPACE_SCALE`, `REPACE_EASE`) | the tip's speed eases toward (life force left / local price) / time left, at most 3x base, over 1.5 s | fixed | Cheap metres near the trunk made early nights run 54 to 58 s. |
| Boxed-in start (`STUCK_RESTART_SECONDS`) | a start boxed in before its first segment restarts at the trunk after 1.2 s | 1 to 2 s | A 0 m, 4 s night with life force in hand. |
| Tip speed scale (`MIN_SPEED_SCALE`, `MAX_SPEED_SCALE`) | 0.65 to 1.8 of the base speed | fixed | A small tank still lasts about 20 s; a big one does not run long. |
| Root speed / dive speed (`speed`, `dive_speed`) | 0.9 / 1.3 m/s before scaling | 0.8 to 1.5 | Base speed; the pacing above scales it. |
| Turn rate (`turn_rate`), turn slowdown (`turn_slowdown`) | 1.7 rad/s, scaled with speed; a hard turn slows the tip by up to 0.4 | 0.2 to 0.5 slowdown | [PLACEHOLDER] The root no longer circles a deposit it is steered at. |
| Gentle magnetism (`magnet_radius`, `magnet_rate`) | a fresh deposit within 1.8 m ahead bends the heading at 1.3 rad/s, less while the stick is held hard | 1 to 2.5 m | [PLACEHOLDER] Easier steering without taking the choice away. |
| Pickup radius (`collect_radius`) | 0.7 m (0.55) | 0.5 to 1.0 | [PLACEHOLDER] Dots were easy to miss. |
| Main root node cap (`ROOT_MAX_NODES_PER_MAIN_ROOT` x `step_length`) | 400 x 0.25 m = 100 m | fixed budget | No longer reached with calm nights. |
| Main roots per tree (`MAX_MAIN_ROOTS`, `has_room_for_root`) | a root starts while one more full root (400 path + 600 fine nodes) fits a graph sized for 45; about 80 to 90 nights fit (was a hard stop at 40) | fixed | After night 45 no root could start and a pruned oak piled up 2780 life force; now at most 235 at dusk. |
| First contact share, tip (`FIRST_SHARE`, `tip_share`) | 0.2 of a deposit's capacity (0.4) | 0.15 to 0.4 | With smaller deposits (below) the tip still takes a clear share. |
| First contact share, fine root (`fine_share`) | 0.1 (was the same as the tip) | 0.05 to 0.15 | Steering to a deposit pays twice what a fine root gets: this is what made quitting early lose. |
| Deposit size (`DEPOSIT_SHARES`) | 1.15 (4) | 1 to 2 | [PLACEHOLDER] Smaller deposits, so a reached deposit does not pay for weeks. |
| Fine roots from leftover life force (`fine_nodes_per_life_force`, reach) | 6 nodes per life force; reach 1.6 m + 0.12 m per life force, at most +6 m | 1 to 6 nodes | [PLACEHOLDER] The leftover still helps, at the fine-root share. |
| Old roots' nightly draw (`nightly_share`, `nightly_water_factor`) | 0.05 of each reached deposit per night (0.2), water 2x | 0.03 to 0.1 | Reached deposits keep paying a little; the new root matters more. |
| Groundwater seepage (`seep_per_metre`, `fine_seep_share`) | 0.03 water per metre of main root per night; a metre of fine root 0.3 of that | 0.01 to 0.05 | [PLACEHOLDER] New with "Softer": a tree that is never steered still gets water, so it finishes (about day 36 to 40). |
| Nutrient regrowth (`REGROW_SHARE`) | 0.05 of drunk dots per night | 0.02 to 0.1 | The soil is never exhausted for good. |
| Soft Liebig floor (`GrowthSim.liebig_floor`) | 0.45 (0.35) | 0.35 to 0.5 | [PLACEHOLDER] A missing N, P or K slows growth to about half, never stops it; raised with "Softer". |
| Stock hold (`GrowthSim.hold_days`, `stock_room`) | the old roots' draw, the seep and alder's nodules fill each stock only up to 2 days of a calm day's need; the new root's own finds are never held back | 1.5 to 3 | [PLACEHOLDER] New in 0.6.3: stocks grew to 5 to 10 days, so a dry night never showed (B3). Water now covers more than 2 days on 0 to 10 of a month's days (oak and birch seed 27: 14 of 33, 10 of 24). A lossy water drain was tried and dropped: it slowed every style. |
| Water upkeep (`WATER_UPKEEP_PER_LEAF`) | 0.01 water per effective leaf at sunrise (beech 1.3x) | 0.005 to 0.02 | Leaves drink every day. Thirst shows on the 6th night when the roots stop finding water; in ordinary neglect N, P or K run short first, so thirst stays rare (a design call taken as "keep rare" while Simon is away). |
| Shade dieback (`SHADE_DIEBACK_SHARE`) | 5 % of shaded tips per day (birch 10 %, beech 0) | 2 to 10 % | Soft failure from the growth model. |

## Care and pruning (0.6.3)

| Variable | Value | Range | Reason |
|---|---|---|---|
| Largest cut (`tree/pruning.gd`) | a fifth of the living tree | fixed | Soft failure: never the trunk, never more than a fifth. |
| Pruning refund (`GrowthSim.PRUNE_REFUND`) | 0.3 of the cut segments, at the next sunrise | 0.2 to 0.4 | [PLACEHOLDER] Below 1, so pruning never speeds a tree up (broken 13). |
| Share near the cut (`PRUNE_NEAR_SHARE`), buds (`PRUNE_BUDS`), reach (`PRUNE_BUD_REACH`) | 0.7 of the refund; 2 buds (3 for a cut of 20 or more), at least 2 segments each, within 1.2 m below the cut | 0.4 to 0.8; 2 to 3; about 1 m | The crown visibly fills in at the cut by the next day. |
| Markers around a cut (`PRUNE_MARKERS_MIN`/`MAX`) | half the cut, 6 to 30, within 1.2 m | 4 to 40 | Draws some of the day's ordinary growth to the cut: felt, no extra growth. |
| Daily refund cap | 0.3 x a fifth of the living tree | fixed | The one-fifth rule caps several cuts a day. |
| Twin buds (sycamore) | the two fork segments are paid with nutrients and taken from the following growth; no refund on a forked cut | fixed | Cutting 20 tips a day finished a sycamore 3 days sooner; now 36 to 38 against 27 to 28 unpruned. |
| Care sign starts / full (`Care.NEED_START`, `NEED_FULL`) | the stock covers less than 0.75 / 0.25 of a calm day | 0.6 to 0.9 / 0.1 to 0.4 | [PLACEHOLDER] Shows only when the stock clearly will not last the day. |
| Care sign easing (`Care.EASE_SHARE`) | over the first 35 % of daylight | 20 to 50 % | Eased over the morning, gone by noon (broken 11). |
| Care reach check (`Care.expected_life_force`, `reachable`) | a sign shows only if tonight's estimated tank (now + a calm rest of the day x 0.8) reaches a dot of that kind | 0.6 to 1.0 | Never a need the player cannot act on (broken 12). |
| Care cues | thirst: tips drop up to 0.55 of a spray, a sixth fold away; N: new sprays fewer (x0.35) and smaller (x0.58), pale 0.6; P/K: up to 0.45 of leaf masses bare, tinge 0.3; no colour once the leaves turn | see care-0.6.3.md | [PLACEHOLDER] Shape first, so it reads in autumn. |
| Bonsai largest cut (`bonsai_sim.gd`) | a third | fixed | Section 16. |

## Build thread sweeps after the 0.6.3 balance fixes, 2026-09-30 (what happened)

From docs/notes/sim-0.6.3.md (headless, seeds 3/14/27, the build thread's `tools/strategies.gd`
and `tools/qa_care.gd`). Finish days are in the tables above. Pruning 20 tips a day never finishes a
tree sooner than leaving it unpruned (sycamore 36 to 38 against 27 to 28). Cutting a fifth every
noon now leaves a bush that does not finish by day 45. Oak runs past night 45 and spends its tank
each night. No style leaves life force unused after a night. Water: a tree whose roots stop
finding water shows thirst on the 6th night; in ordinary neglect it is short of N, P or K first.

**0.7 full check, 2026-09-30 01:50 UTC (what happened; build thread, iterate-0.7 at f2302f3,
seeds 3/14/27, 469 nights of start-pick play).** Finish days by style:

| Species | follow the glow from the newest tip | start pick (meadow) | end every root at once |
|---|---|---|---|
| linden | 29 / 29 / 30 | 30 / 27 / 30 | 36 / 36 / 36 |
| birch | 24 / 24 / 24 | 23 / 23 / 24 | 32 / 29 / 32 |
| beech | 29 / 30 / 30 | 28 / 29 / 29 | 35 / 35 / 35 |
| sycamore | 30 / 27 / 27 | 27 / 25 / 26 | 33 / 33 / 33 |
| alder | 28 / 26 / 27 | 26 / 29 / 26 | 31 / 30 / 31 |
| oak | 35 / 34 / 36 | 33 / 33 / 33 | 40 / 41 / 41 |

Steering from the tip now pays: following the glow reaches it on about 9 nights in 10 and beats
random steering by 6 to 8 days, and it is as good as the start pick, so the risk in the section
below is closed (0.7 broken item 1 and this table's item 5 pass). Nights ran 38 to 50 s (item 7
passes).

Nutrients: the kind the tree was shortest of was always in reach (B1 passes). Days at the soft
floor (growth about half): birch 4 %, linden 31, oak 34, sycamore 35, beech 43, alder 46, mostly
phosphorus (B2 broken). One kind held more than two days of stock: alder 100 % of days, birch 58,
oak 55, water or nitrogen (B3 broken; alder's nitrogen is its nodule quirk). 27 to 43 rich
patches lay in one night's reach against a limit of about 15 (B4 broken). A missed glow stayed at
0.45 beside the new one on 4 to 17 nights a month (0.7 item 3 broken; the spec now says the old
glow goes out when a new one shows). Bonsai: pellets take 3 taps (C1 broken), the repot slip
buttons are small (C1, D5), needs do not show on the tree or pot (C4). All go into 0.8 section 5
(build thread branch s08-balance, numbers in docs/notes/balance-0.8.md).

## Seeded runs on iterate-0.6, 2026-09-29 22:50 UTC (what happened, before the 0.6.3 fixes)

Same script and seeds (3 / 14 / 27, linden, finish day), on the settled 0.6.x numbers. Two new
styles use the game's own `RootBot.pick_start`, which starts the root where the meadow points to
the nutrient the tree is shortest of: **meadow** (then chases dots) and **meadow_boost**.

| Style | Finished on day | Longest night |
|---|---|---|
| meadow (start where the meadow points, chase dots) | 29 / 31 / 30 | 50 s |
| meadow_boost | 27 / 28 / 31 | 49 s |
| steer (start at the newest root tip, chase dots) | 35 / 35 / 37 | 49 s |
| boost (newest tip, chase dots) | 35 / 37 / 37 | 50 s |
| quit (end every root at once) | 37 / 36 / 37 | 2 s |
| boost_quit | 37 / 36 / 36 | 2 s |
| wander (random steering, newest tip) | 37 / - / - | 40 s |

Every day grew at least 25 segments; no night ran past 50 s. This agrees with the build thread's
numbers (steered 27 to 29, unsteered 35 to 38).

**What it means (interpretation):** the fixes work: quitting early no longer wins, nights are
calm, constant boosting is at most two days faster. But nearly all of the gain now comes from
**where the root starts**. Chasing dots from the newest tip does barely better than ending the
root at once (35 to 37 against 36 to 37). A player who never uses the start pick, or does not
connect the meadow with it, will feel that steering does nothing. The 0.7 glow, which marks
where to go, is the natural fix; the care page naming the missing nutrient and its meadow hint
helps too. Broken-list item 5 is only just met.

## 0.8 (planned, specs/0.8.md)

None of these touch the life-force economy; they set how often mood moments come.

| Variable | Value | Range | Reason |
|---|---|---|---|
| Wood for the hedgehog | 40 cut segments on the pile | 20 to 80 | [PLACEHOLDER] About a week of ordinary pruning. |
| Days until it moves in | 2 sunrises after the threshold | 1 to 4 | [PLACEHOLDER] A surprise, not a reward for the cut. |
| How often it shows | at most once a day, at dusk, on about half the days; none from 20 November to spring | 0.3 to 0.7 | [PLACEHOLDER] Rare enough to stay a moment. |
| Wren | after 80 cut segments, by day, at most once a day | 60 to 120 | [PLACEHOLDER] The later, smaller visitor. |
| Sticks drawn on the pile | at most 40, one merged mesh | 20 to 60 | Phone budget. |
| Automatic save copies | last 3 sunrises | 2 to 5 | Guards against a broken save at no cost to the player. |
| Dot shapes fade to round | beyond the fog's half distance | fixed | Tiny far dots cannot carry a shape. |

## Seeded runs on tag tree-v0.6, 2026-09-29 (what happened; before the 0.6.x fixes)

Headless Godot 4.7.2 on `main`, linden, seeds 3, 14 and 27, one visit per day with no dragging
of the sun, until finished or day 45. The script is a design check and is not in the repo; it
uses the game's own `RootBot` for steering. Styles: **steer** chases the nearest dot every
night until the life force is spent; **quit** ends every root after 1 m; **boost** keeps the
sun boosted all day and steers; **boost_quit** does both; **wander** steers at random.

| Style | Finished on day (seeds 3 / 14 / 27) | Real run length, last ten nights | Longest night |
|---|---|---|---|
| steer | 28 / 23 / 43 | 68 to 107 s | 112 s |
| quit | 18 / 18 / 22 | 1 s | 1 s |
| boost | 16 / 32 / 37 | 36 to 52 s | 68 s |
| boost_quit | 14 / 16 / 18 | 1 s | 1 s |
| wander | 31 / 29 / 34 | 80 to 87 s | 98 s |


The same three styles on the other five species (seed 14, finish day; target in brackets):

| Species | steer | quit | boost |
|---|---|---|---|
| birch (25) | 28 | 17 | 17 |
| beech (30) | 30 | 23 | not by day 45 (2 days with no growth) |
| sycamore (30) | 33 | 29 | not by day 45 (stalled at 1763 of 2150 segments) |
| black alder (30) | 31 | 21 | not by day 45 (stalled at 1180 of 1740 segments) |
| oak (35) | 38 | 26 | 32 |

Other observations: in the quit style the old roots' nightly draw brought in more nutrients
(1086 to 1288 units over the month) than in the steer style (522 to 801). On seed 27 the steer
style ended with 656 unused life force at dusk and one day grew only 7 segments.

**What it means (interpretation):** ending the root at once wins because the leftover life
force becomes fine roots right by the trunk, in the rich topsoil, and those reach many deposits
that the old roots then drink from every night. A long steered root spends the same life force
on distance and depth surcharges. Boosting all day does not cost enough: it wins whenever it is
combined with quitting. The chase bot is not a good player (random steering does about as
well), but a person chasing the glow behaves much like it. The month is not stable across
seeds (16 to 43 days for the same style). Constant boosting stalls three of the six species for good
(days with no growth at all while life force sits unused); the cause is not yet known, a guess is
that the tree reaches its height cap early and runs out of room to seed new growth.

## What "broken" looks like

For 0.7's three new mechanics, see also the list at the end of specs/0.7-candidates.md; for 0.8,
the list at the end of specs/0.8.md.

Checked before every full review and phone build, with seeded runs over at least the five styles
above and three seeds, on linden plus one fast and one slow species.

**Boost and life force**
1. Constant boosting finishes a tree more than 15 % sooner than calm steering on the same seed
   (today: up to 12 days sooner when combined with quitting).
2. Boosting never leaves the player short at night: the run length after a fully boosted day is
   not clearly shorter (at least a third) than after a calm day.
3. Life force piles up unused: more than two nights' worth left at dusk in any style.

**Root run**
4. Ending the root early grows a bigger tree or finishes sooner than chasing dots (today: it
   does, by 5 to 21 days).
5. Random steering does as well as aiming for the dots the meadow points to.
6. A run that touches no dot pays as much as one that touches several.

**Night length**
7. A night's root runs longer than 60 s of real time, or a night with life force in hand is
   over in less than 20 s without the player choosing to end it.
8. Night length keeps growing through the month instead of settling.

**Month and days**
9. A tree finishes outside its target by more than 15 % (linden 26 to 34 days) in the calm
   style, or the same style spreads by more than a week across seeds.
10. Any played day grows fewer than about 20 segments (no visible growth that day).
10a. A tree whose roots are never steered (every root ended at once) finishes more than about
   10 days after its steered days, or not by about day 42 for oak and day 40 for the rest (Simon,
   2026-09-29: "Softer"; ruled per species on 2026-09-30, since oak's month is longer), or
   finishes as fast as a steered one (item 4).

**Care and pruning** (once 0.6.3 exists)
11. A care signal shows while nothing is lacking, or stays after the need was met for a full
    day and night.
12. The tree shows a need the player cannot act on that night (no dot of that kind in reach of
    any root start).
13. Pruning every day as much as allowed finishes a tree sooner than not pruning at all, or
    pruning has no effect the player can see by the next day.
14. A drooping or pale tree loses wood or height (soft failure means slower, never smaller,
    except shade dieback and cuts).
