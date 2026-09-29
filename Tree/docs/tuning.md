# Tuning table

Every number that shapes pacing or the life-force economy, with its value on `main` (tag
tree-v0.6; the 0.6.1 quick fixes on `iterate-0.6` change none of them), a range to tune in, and
the reason. [PLACEHOLDER] means the value has not been felt on the phone yet or is known to be
off. The build thread's 0.6.x streams (tree-062, s-roots, care) will change several of these;
each change updates this table in the same or the next pull request (design-practice.md).

Last checked against the code: 2026-09-29. File and constant in brackets.

## Clock and pacing

| Variable | Value | Range | Reason |
|---|---|---|---|
| Full day, real seconds (`DayCycle.seconds_per_day`) | 192 s | 150 to 300 | Play test 3: the day runs on its own and quickly. The design doc's 5 min day was shortened by Simon. |
| Daylight share (`daylight_fraction`) | 0.625 (120 s day, 72 s night clock) | 0.55 to 0.7 | Day is where the tree is watched; the night is held while the root runs, so its clock share matters little. |
| Night hold while the root runs (`NIGHT_HOLD_MARGIN`) | clock stops 0.5 s before dawn until the run ends | fixed | The run is never cut off by sunrise. This is why night length is set by the run, not the clock. |
| Night fast-forward after the run (`NIGHT_FAST_FORWARD`) | 30x | 10 to 60 | The rest of the night passes in about 2 s. |
| Empty night visit (`EMPTY_NIGHT_VISIT`) | 5 s | 3 to 10 | A night without life force is a short look, not a wait. |
| Real run length (from root speed and life force) | 1 to 112 s, typically 70 to 110 s by week 3 when steering | **target 40 to 60 s** | [PLACEHOLDER] Audit 2026-09-29: nights grow to 90 s and more. Being fixed in s-roots (0.6.4). |
| Dawn burst share (`GrowthSim.dawn_burst_share`) | 0.25 of affordable growth, max 30 nodes, over 10 s | 0.15 to 0.35 | The night's purchase shows at once, with the twinkle. |
| Morning line delay (`MORNING_DELAY`) | 11 s | fixed | Just after the dawn burst. |
| Days to finish, linden (`Species.finish_nodes` 1800, tuned by month_report) | target 30; seeded runs 16 to 43 (below) | 27 to 33 | [PLACEHOLDER] A month per tree is a pillar. Birch 25, oak 35. |
| Growth cap per second (`max_growth_per_second` x species pace) | 1 node/s x light (x3 boosted) | 0.7 to 1.5 | Calm growth is capped so the nutrients last the day; boost lifts the cap threefold. |
| Node cost (`cost_per_node` x (1 + nodes/380)) | 0.08 rising to about 0.46 at 1800 nodes | 0.05 to 0.12 base | A big tree needs more per segment, so growth spreads over the month. |

## Life force and boost

| Variable | Value | Range | Reason |
|---|---|---|---|
| Life force per leaf cluster (`life_force_per_tip`) | 0.02 per tip per second x light | 0.01 to 0.04 | Scaled to the 2 min day. |
| Self-shading (`effective_leaves`) | tips up to 50, then sqrt(50 x tips) | fixed | A big crown yields less per leaf. |
| Boost growth (`boost_multiplier`) | 3x light for growth | 2 to 3 | A tap should visibly speed growth. |
| Boost life force (`boost_life_force_factor`) | 0.4x while boosted | 0.2 to 0.6 | [PLACEHOLDER] The trade. Seeded runs show it costs too little in the end (constant boost finishes linden on day 14 to 37; see below). s-roots adds a life force cost for the night. |
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
| Root speed / dive speed (`speed`, `dive_speed`) | 0.9 / 1.3 m/s | 0.8 to 1.5 | [PLACEHOLDER] Sets real night length together with life force. |
| Turn rate (`turn_rate`) | 1.7 rad/s | 1.5 to 3 | [PLACEHOLDER] Audit: steering feels hard. s-roots makes it easier. |
| Auto sink (`sink_speed`, `dive_sink_rate`) | 0.07, 1.2 | small | The root sinks on its own; hold to dive. |
| Pickup radius (`collect_radius`) | 0.55 m | 0.5 to 1.0 | [PLACEHOLDER] Audit: dots are easy to miss. s-roots widens it. |
| Main root node cap (`ROOT_MAX_NODES_PER_MAIN_ROOT` x `step_length`) | 400 x 0.25 m = 100 m | fixed budget | Also caps a night at about 110 s. |
| Fine roots from leftover life force (`fine_nodes_per_life_force`) | 6 nodes per life force, reach 1.6 m, 150 to 600 per root | 1 to 6 | [PLACEHOLDER] **The main reason quitting early pays** (below). |
| First contact share (`Underground.FIRST_SHARE`) | 0.4 of a deposit | 0.3 to 0.6 | A well-steered night pays that night (QA round 2). |
| Deposit size (`DEPOSIT_SHARES`) | 4 first-contact shares | 3 to 6 | |
| Old roots' nightly draw (`NIGHTLY_SHARE`) | 0.2 of each reached deposit per night | 0.1 to 0.25 | [PLACEHOLDER] Reached deposits keep paying; that also rewards many cheap fine roots near the trunk. |
| Nutrient regrowth (`REGROW_SHARE`) | 0.05 of drunk dots per night | 0.02 to 0.1 | The soil is never exhausted for good. |
| Soft Liebig floor (`Resources.growth_factor`) | 0.35 | 0.25 to 0.5 | A missing N, P or K slows to about a third, never stops. |
| Water upkeep (`WATER_UPKEEP_PER_LEAF`) | 0.01 water per effective leaf at sunrise (beech 1.3x) | 0.005 to 0.02 | Leaves drink every day. No visible sign yet; care (0.6.3) adds one. |
| Shade dieback (`SHADE_DIEBACK_SHARE`) | 5 % of shaded tips per day (birch 10 %, beech 0) | 2 to 10 % | Soft failure from the growth model. |

## Care and pruning

| Variable | Value | Range | Reason |
|---|---|---|---|
| Largest cut (`tree/pruning.gd`) | a fifth of the living tree | fixed | Soft failure: never the trunk, never more than a fifth. |
| Pruning effect | none beyond removing wood (markers go to the rest of the crown) | to be set in 0.6.3 | [PLACEHOLDER] Audit: pruning has no felt effect. Spec in specs/care-and-pruning.md. |
| Care signals | none | to be set in 0.6.3 | [PLACEHOLDER] Spec in specs/care-and-pruning.md. |
| Bonsai largest cut (`bonsai_sim.gd`) | a third | fixed | Section 16. |

## Seeded runs, 2026-09-29 (what happened)

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
10a. A tree whose roots are never steered (every root ended at once) does not finish by about
   day 40 (Simon, 2026-09-29: "Softer"), or finishes as fast as a steered one (item 4).

**Care and pruning** (once 0.6.3 exists)
11. A care signal shows while nothing is lacking, or stays after the need was met for a full
    day and night.
12. The tree shows a need the player cannot act on that night (no dot of that kind in reach of
    any root start).
13. Pruning every day as much as allowed finishes a tree sooner than not pruning at all, or
    pruning has no effect the player can see by the next day.
14. A drooping or pale tree loses wood or height (soft failure means slower, never smaller,
    except shade dieback and cuts).
