# Tuning table

Every number that shapes pacing or the life-force economy, with its value, a range to tune in,
and the reason. [PLACEHOLDER] means the value has not been felt on the phone yet or is known to
be off. Each code change to such a number updates this table in the same or the next pull
request (design-practice.md).

Values below are the 0.8 numbers on `main` (0.8 is on Simon's phone since 2026-09-30 06:29 UTC;
the balance fixes from the 0.7 check are in docs/notes/balance-0.8.md, earlier ones in
sim-0.6.3.md, care-0.6.3.md, soft-0.6.x.md). None of these values has been felt on the phone by
Simon yet. Where 0.8 changed a value, the 0.6.3 value is in brackets as "(0.6.3: ...)"; other
brackets hold the tree-v0.6 value.

Last checked against the code: 2026-09-30 06:40 UTC (main at b4fbd37). File and constant in brackets.

## Clock and pacing

| Variable | Value | Range | Reason |
|---|---|---|---|
| Full day, real seconds (`DayCycle.seconds_per_day`) | 192 s | 150 to 300 | Play test 3: the day runs on its own and quickly. The design doc's 5 min day was shortened by Simon. |
| Daylight share (`daylight_fraction`) | 0.625 (120 s day, 72 s night clock) | 0.55 to 0.7 | Day is where the tree is watched; the night is held while the root runs, so its clock share matters little. |
| Night hold while the root runs (`NIGHT_HOLD_MARGIN`) | clock stops 0.5 s before dawn until the run ends | fixed | The run is never cut off by sunrise, so night length is set by the run. |
| Night fast-forward after the run (`NIGHT_FAST_FORWARD`) | 30x | 10 to 60 | The rest of the night passes in about 2 s. |
| Hold to fast-forward (0.8.2; specs/fast-forward.md; `TreeView.HOLD_START`, `FAST_FORWARD`, `FAST_EASE`) | holding still for 0.6 s by day runs the clock at 8x (0.8.2.1: 4x) until release, easing in over 0.5 s; stops at once on release and at the sunset hold | 6x to 10x; hold 0.4 to 0.8 s; ease 0 to 1 s | [PLACEHOLDER] Simon, 2026-09-30: tap still boosts, a hold fast-forwards with the set boosts at their times; 2026-10-02: twice as fast. Game-time based (more fixed sim steps per frame), so it changes no result (tests/test_fast_forward.gd). |
| Empty night visit (`EMPTY_NIGHT_VISIT`) | 5 s | 3 to 10 | A night without life force is a short look, not a wait. |
| Real run length (`RootSystem.run_seconds_for`) | 34 s x sqrt(life force / 150), between 20 and 44 s (was up to 112 s) | 20 to 60 s | [PLACEHOLDER] The night follows the tank, so a boosted day's night is clearly shorter; longest night in the sweeps 50 to 51 s. |
| Dawn burst (`GrowthSim.dawn_burst_share`) | 0.25 of affordable growth, max 30 nodes, over 10 s | 0.15 to 0.35 | The night's purchase shows at once, with the twinkle. |
| Morning line delay (`MORNING_DELAY`) | 11 s | fixed | Just after the dawn burst. |
| Finish size (`Species.finish_nodes`) | linden 2100 (1800), birch 1880 (1740), beech 2080, sycamore 1800 (2150), alder 1740, oak 1970 | per species | [PLACEHOLDER] Retuned for the new tree's growth curve so each species hits its month (below). |
| Days to finish, steered (meadow play, seeds 3/14/27; 0.8.1 wider field) | wish / tip / dots: linden 29/29/29, 30/32/32, 29/30/30; birch 24/24/24, 25/27/25, 24/24/24; beech 28/28/28, 29/30/29, 28/30/28; sycamore 28/28/28, 31/29/31, 29/30/29; alder 28/28/28, 29/28/29, 28/28/28; oak 32/32/32, 33/33/37, 33/33/32 (month_report: linden 29, birch 24, beech 28, sycamore 28, alder 28, oak 33) | linden 26 to 34, birch 22 to 28, oak 31 to 39 | [PLACEHOLDER] A month per tree is a pillar. Held through the wider field (0.8: linden 29, birch 24, oak 32 to 33). |
| Days to finish, never steered (end early, straight down, random; 0.8.1) | linden 38/37/36, 38/38/40, 37/37/38; birch 26/28/31, 31/31/30, 29/31/30; beech 38/36/35, 36/37/37, 35/36/35; sycamore 37/35/35, 39/36/37, 36/36/36; alder 37/35/37, 38/38/40, 36/35/35; oak 39/39/39, 43/43/44, 41/41/41 | about 5 to 10 days after steered; by about day 42 for oak, day 40 for the rest | [PLACEHOLDER] Simon, "Softer" (2026-09-29): a tree still finishes without steering, clearly later. Oak straight down 43 to 44 sits just past its limit (open). |
| Least growth on any day | 26 segments or more in every style; 16 to 22 only on days with a cut, since the cut counts as loss | at least 20 | Every day must show visible growth (design doc section 7). |
| Finish height (`GrowthSim.FINISH_HEIGHT_SHARE`) | a tree counts as finished only once it also stands at least 0.4 of its species' height (12 m linden, 10 m birch) | 0.3 to 0.5 | New in 0.6.3 (build thread's choice while Simon is away): a tree pruned hard every day "finished" as a 2 m bush. |
| Seedling floor (`SEEDLING_FLOOR`) | growth never below 0.6 of full speed on days 1 to 3 | 0.5 to 0.7 | New in 0.6.3: unsteered oak and beech grew barely 21 segments on days 2 and 3. |
| Growth cap per second (`max_growth_per_second` x species pace) | 1 node/s x light | 0.7 to 1.5 | Calm growth is capped so the nutrients last the day. |
| Species pace (`Species.pace`) | linden 0.92, beech 0.97, sycamore 0.8, alder 0.82, oak 0.8, birch 1.0 (0.6.3: 1.0, 1.1, 1.0, 1.0, 0.85, 1.0) | 0.7 to 1.1 | [PLACEHOLDER] New in 0.8: well fed, steered trees finished on day 23 (alder) to 29 (oak). |
| Node cost (`cost_per_node` x (1 + nodes/380)) | 0.08 rising with size | 0.05 to 0.12 base | A big tree needs more per segment, so growth spreads over the month. |
| Young leaves (`young_leaf_bonus`) | up to 2.2x life force while the tree is small, easing off as it grows | 1 to 2.5 | [PLACEHOLDER] The new tree starts as a thin whip with few leaves; this keeps its first nights worth steering. |

## Life force and boost

| Variable | Value | Range | Reason |
|---|---|---|---|
| Life force per leaf cluster (`life_force_per_tip`) | 0.02 per tip per second x light | 0.01 to 0.04 | Scaled to the 2 min day. |
| Self-shading (`effective_leaves`) | tips up to 50, then sqrt(50 x tips) | fixed | A big crown yields less per leaf. |
| Boost growth (`boost_multiplier`) | 2x light for growth (3x) | 2 to 3 | [PLACEHOLDER] A tap should visibly speed growth; 3x made constant boosting finish far too early. |
| Boost life force (`boost_life_force_factor`) | 0.3x while boosted (0.8: 0.35x; earlier 0.4x, briefly 0.5x) | 0.3 to 0.5 | [PLACEHOLDER] The trade: a fully boosted day costs about a third of the night or more. 0.8.1: in the wider field a calm root goes further and deeper, where metres cost more, so 0.35 left the boosted night at 0.60 to 0.78 of a calm one; at 0.3 it is 0.53 to 0.67 in metres, 0.60 to 0.66 in seconds, and still lasts 21 to 23 s. Now at the bottom of its range. |
| Boost without nutrients (`boost_liebig`) | 1.0: the extra light counts only as far as N, P and K allow | 0 to 1 | A brighter sun cannot make up for a missing nutrient, so boosting a hungry tree gives nothing. |
| Boost price for a hungry tree (`GrowthSim.boost_cost_liebig`, `boost_hunger_gain`; 0.8.1) | while boosted, the life force cut eases by (1 - growth factor without floor) x the morning's hunger (`care_need` at sunrise), `boost_cost_liebig` 1.0, `boost_hunger_gain` 1.0 | 0.5 to 1 | [PLACEHOLDER] A tree that wakes hungry gains nothing from the boost (row above), so now it also keeps its life force. A tree the night fed pays the full price, so constant boosting still loses (boost_all nights 0.67 of dots nights). |
| Constant boost vs steering (0.8.1) | boost all day 0 to 9 % sooner than steering to dots; boost in the morning up to 14 %, except sycamore seed 14 at 16.7 %; boost all day and end every root at once finishes on all 18 runs, day 29 to 42 | within 15 %; quitting by about day 40 | [PLACEHOLDER] The 0.8 stall (3 of 18 runs never finished) is fixed. Open: sycamore seed 14 just past 15 % (its low P and K needs leave little for the boost price to hold back; needs a growth-side lever), linden seed 14 and alder seed 3 quitting at day 42. |
| Boost length and stacking (`boost_hour`) | one game hour per tap, up to three ahead | fixed | Survey 1 and play test 3. |
| Seed life force (`SEED_LIFE_FORCE`) | 20 | 15 to 30 | Enough for the first root from the seed. |
| Offline growth (`apply_offline`) | one real day away = 20 s of mid-morning growth | 10 to 40 s | Coming back shows a little growth; never a reason to stay away. |
| Offline life force | +4 per real day away, max 8 | 2 to 10 | The player always comes back with enough for a short root. |
| Species life force factors (`species.gd`) | birch 0.9; beech calm 1.25, boosted 0.7; linden blossom 1.2 on days 18 to 22 | see section 15 | The quirks. |

## Roots and nutrients

| Variable | Value | Range | Reason |
|---|---|---|---|
| Root cost per metre (`RootSystem.base_cost_per_metre`) | 1.0 x (1 + 0.03 x distance + 0.12 x depth) in the wide field (layout 3); 0.8 saves keep 0.06 x distance | base 0.8 to 2 | Far and deep costs more (design doc section 5). Open (0.8.1 sim): a dearer metre, see "0.8.1 sim" below. |
| Dearer metre in the wide field (0.8.2, planned; Simon "Ja, Meter teurer", 2026-10-01) | layout 3 only: `base_cost_per_metre` about 2.5 (or `cost_exponent` about 0.8 instead), so a calm night from the best start buys 12 to 18 m of straight root (now 30 to 55 m) and lasts as long as now (20 to 44 s); deposit size (`DEPOSIT_SHARES`) or `tip_share` up so steered months stay in "Days to finish"; old saves (layouts 1 and 2) keep 1.0 | base 2 to 3, or exponent 0.7 to 0.9; 12 to 18 m | [PLACEHOLDER] The 0.8.1 sim: a calm night could buy 7 to 14 rich patches in a straight line, so no far patch needed two nights. The build thread picks the lever and the deposit retune with the sim; far wish reach follows the line cost by itself. |
| Birch topsoil cost, oak downward depth cost | 0.7x, 0.5x | per quirk | Section 15 quirks. |
| Calm-night pacing (`calm_life_force`, `cost_exponent`, `calm_run_seconds`, `calm_ref_life_force`) | above 50 life force each metre costs more (power 0.5); the run lasts 34 s x sqrt(tank / 150), 20 to 44 s | 30 to 45 s at a full calm tank | [PLACEHOLDER] Nights had grown to 80 to 110 s. |
| Pacing during the run (`_repace`, `MAX_REPACE_SCALE`, `REPACE_EASE`) | the tip's speed eases toward (life force left / local price) / time left, at most 3x base, over 1.5 s | fixed | Cheap metres near the trunk made early nights run 54 to 58 s. |
| Boxed-in start (`STUCK_RESTART_SECONDS`) | a start boxed in before its first segment restarts at the trunk after 1.2 s | 1 to 2 s | A 0 m, 4 s night with life force in hand. |
| Tip speed scale (`MIN_SPEED_SCALE`, `MAX_SPEED_SCALE`) | 0.3 to 1.8 of the base speed (0.6.3: 0.65 to 1.8) | fixed | A small tank still lasts about 20 s; 0.8 lowered the slowest speed because the night after a boosted day ran 9 to 15 s (now 13 s or more on night 2, 18 s or more after). |
| Root speed / dive speed (`speed`, `dive_speed`) | 0.9 / 1.3 m/s before scaling | 0.8 to 1.5 | Base speed; the pacing above scales it. |
| Turn rate (`turn_rate`), turn slowdown (`turn_slowdown`) | 1.7 rad/s, scaled with speed; a hard turn slows the tip by up to 0.4 | 0.2 to 0.5 slowdown | [PLACEHOLDER] The root no longer circles a deposit it is steered at. |
| Detour around old roots (0.8.2.2; specs/0.8.md "Roots go around old roots"; `RootSystem.AVOID_*`) | the tip keeps 0.35 m (`AVOID_CLEARANCE`) from older main roots and tonight's own root (its last 1.5 m excepted), looking 1 m ahead (`AVOID_LOOK`); it bends in 10 degree steps up to 90 degrees, square to the old root on the side it is already on (over or under a root across the way), then the plain sides (sideways first in the topsoil), turning at 2.6 rad/s on top of the stick's rate, and eases back onto the held heading; never closer than 0.2 m (`AVOID_HARD`, a wall like a rock); the root a night starts from (and anything within 1 m of the start) is ignored for the first metre; no way round within 90 degrees: it stops there and the boxed-in rule applies | clearance 0.25 to 0.5 m; look-ahead 0.6 to 1.5 m | [PLACEHOLDER] Simon, 2026-10-02: no driving into existing roots, go around without changing the direction. |
| Gentle magnetism (`magnet_radius`, `magnet_rate`) | a fresh deposit within 1.2 m ahead bends the heading at about 0.6 rad/s, at most about 30 degrees per deposit, then the heading eases back to the held direction (0.8.2.1: 1.8 m, 1.3 rad/s, no limit; 0.8.2.2 planned) | 1 to 1.5 m; 0.4 to 0.8 rad/s; 20 to 40 degrees | [PLACEHOLDER] Simon, 2026-10-02: the auto-steering is good but too strong, keep the original direction more. |
| Pickup radius (`collect_radius`) | 0.7 m (0.55) | 0.5 to 1.0 | [PLACEHOLDER] Dots were easy to miss. |
| Main root node cap (`ROOT_MAX_NODES_PER_MAIN_ROOT` x `step_length`) | 400 x 0.25 m = 100 m | fixed budget | No longer reached with calm nights. |
| Main roots per tree (`MAX_MAIN_ROOTS`, `has_room_for_root`) | a root starts while one more full root (400 path + 600 fine nodes) fits a graph sized for 45; about 80 to 90 nights fit (was a hard stop at 40) | fixed | After night 45 no root could start and a pruned oak piled up 2780 life force; now at most 235 at dusk. |
| First contact share, tip (`FIRST_SHARE`, `tip_share`) | 0.2 of a deposit's capacity (0.4) | 0.15 to 0.4 | With smaller deposits (below) the tip still takes a clear share. |
| First contact share, fine root (`fine_share`) | 0.1 (was the same as the tip) | 0.05 to 0.15 | Steering to a deposit pays twice what a fine root gets: this is what made quitting early lose. |
| Deposit size (`DEPOSIT_SHARES`) | 1.15 (4) | 1 to 2 | [PLACEHOLDER] Smaller deposits, so a reached deposit does not pay for weeks. |
| Fine roots from leftover life force (`fine_nodes_per_life_force`, reach) | 6 nodes per life force; reach 1.6 m + 0.12 m per life force, at most +6 m | 1 to 6 nodes | [PLACEHOLDER] The leftover still helps, at the fine-root share. **To be replaced in 0.8.2** by the two rows below (specs/side-roots.md): the first level then stays at 150 nodes and 1.6 m. |
| Second-level side roots (0.8.2, planned; 0.8.2.2 built: `side_reach_max` 2.5, was 3.0; `side_start_level` 1) | leftover life force buys about 4 nodes per point, growing toward fresh dots within 1.0 m + 0.05 m per point, at most 2.5 m; 0.8.2.2: from every night's main and fine roots (not older side roots), the nearest fresh dots first, and the whole tank when the night is ended at once (no main root that night); none when less than about 10 % of the tank is left; drink at the fine share 0.1 | 2 to 6 nodes; 1.5 to 3 m | [PLACEHOLDER] Simon, 0.8 phone notes: the leftover should show as a fan of small roots. Short reach so ending early does not win again (item 4). |
| Third-level side roots (0.8.2, planned) | about 30 % of the leftover's nodes (second level 70 %); from second-level roots at least 0.5 m long, toward dots within 0.3 m + 0.02 m per point, at most 0.8 m; half as thick, drink 0.05; second and third level together at most about 250 nodes a night | 20 to 40 %; 0.5 to 1 m; 150 to 300 nodes | [PLACEHOLDER] Simon, 18:15 UTC: smaller roots branching off the side roots. The cap keeps the phone at 30 fps underground. |
| Root thickness from a full run (0.8.2, planned) | share of the tank spent on the player's root sets its thickness, 1.0x to about 1.5x; seep per metre up to 1.25x | 1.3 to 1.8x; 1.1 to 1.4x | [PLACEHOLDER] Simon: whoever drives to the end of the life force gets a thicker root instead of side roots. |
| Old roots' nightly draw (`nightly_share`, `nightly_water_factor`) | 0.08 of each reached deposit per night (0.6.3: 0.05; tree-v0.6: 0.2), water 2x | 0.03 to 0.1 | Young trees sat at the floor for their first week or two; the stock caps below still limit it. |
| Groundwater seepage (`seep_per_metre`, `fine_seep_share`, `seep_day_cover`) | 0.03 water per metre of main root per night; a metre of fine root 0.3 of that; in all at most 0.65 of a calm day's water (0.8.1, new cap) | 0.01 to 0.05; cap 0.55 to 0.7 | [PLACEHOLDER] New with "Softer": a tree that is never steered still gets water, so it finishes (about day 36 to 40). The cap: in the wider field a steered tree's roots reach 680 to 740 m by day 20 and their seep alone covered 0.65 to 0.76 of its water, so thirst could never show (it starts at 0.75). |
| Nutrient regrowth (`REGROW_SHARE`) | 0.05 of drunk dots per night | 0.02 to 0.1 | The soil is never exhausted for good. |
| Soft Liebig floor (`GrowthSim.liebig_floor`) | 0.55 (0.6.3: 0.45; tree-v0.6: 0.35) | 0.45 to 0.6 | [PLACEHOLDER] A missing N, P or K slows growth to about half, never stops it; raised in 0.8 so never-steered trees stay near day 40 with the slower paces. |
| Stock hold, old roots (`GrowthSim.hold_days`, `stock_room`) | the old roots' draw, the seep and alder's nodules fill each stock only up to 1.2 days of a calm day's need (0.6.3: 2) | 1.2 to 2 | [PLACEHOLDER] At 2, a boosted tree banked enough to finish 16 to 19 % sooner than a calm one once phosphorus was plentiful; 1.5 still gave 16 % on oak. |
| Stock hold, the night's finds (`GrowthSim.find_hold_days`, `RootSystem.run_room`) | the night's root fills the stock up to 1.2 days as well; a deposit reached while full is tapped all the same, for the old roots | 1 to 2 | New in 0.8 (B3): one kind held over two days of stock on up to 100 % of days; now 0 %. |
| Water upkeep (`WATER_UPKEEP_PER_LEAF`) | 0.01 water per effective leaf at sunrise (beech 1.3x) | 0.005 to 0.02 | Leaves drink every day. Thirst shows on the 6th night when the roots stop finding water; in ordinary neglect N, P or K run short first, so thirst stays rare (a design call taken as "keep rare" while Simon is away). |
| Soil layout (`Underground.LAYOUT`, `game_layout`) | layout 3 (the wide field) for new games from 0.8.1; saves keep layout 1 or 2 dot for dot, with their 14 m field and `distance_cost` 0.06 | fixed | Dot ids must not move under an old save. The three rows below are layout 2 (0.8 saves). |
| Rich patches (`Underground.mix`) | 8 in all: water 2 x 36 to 46 dots (0.8 a dot), nitrogen 2 x 40 to 52 (1.45), phosphorus 2 x 40 to 52 (1.1), 1.2 to 1.8 m wide; one deep water vein of 55 to 75 (0.6.3: 27 patches of 16 to 36 dots, 4 veins) | 8 to 15 in one night's reach | [PLACEHOLDER] B4: fewer, larger patches, each its own choice with a meadow sign; phosphorus is now 0.7 of nitrogen's total (was 0.45), which fixed B2. |
| Patch spacing (`patch_gap`, `patch_near`, `PATCH_MAX_DEPTH`), 0.8 saves | 3.2 m apart, 2.2 m or more from the trunk, 0.5 to 2.0 m deep | fixed | A young tree reaches one, and every patch shows on the meadow. Layout 3: row "Wider root field". |
| Scattered dots (`scatter`), 0.8 saves | 700 (0.6.3: 1400) | 700 to 1100 | B4: no longer a dot every few centimetres. Layout 3: 1200. |
| Wider root field (0.8.1, layout 3; specs/0.8.md "A wider root field") | soil radius 30 m (`field_extent`); 14 rich patches in rings 5 to 12 m (P, N, W, P, N), 12 to 20 m (W, P, N, W, P), 20 to 30 m (N, W, P, N), at least 7 m apart (`gap3`), sizes as layout 2; 1200 scattered dots (`scatter3`); rocks 9 shallow, 60 deep (`rocks3`); 2 deep water veins (`deep_water3`); `distance_cost` 0.03; starter patch unchanged | 25 to 40 m; 4 to 6 m; 6 to 9 m; 0.02 to 0.04 | [PLACEHOLDER] Simon, 18:29 UTC: patches much further apart, a bigger volume, sometimes a root continued over nights to reach one. Built; for what a night reaches see "0.8.1 sim" below. |
| Wish distance (0.8.1; `Diary.far_share`, `FAR_AHEAD_MIN/MAX`, `FAR_REACH_NIGHTS`) | layout 3 only: half the new underground wishes (`far_share` 0.5) go 9 to 17 m beyond the newest tip and 12 m or more from the trunk, within 2.5 calm reaches of a straight root from that tip; an unreached far wish waits up to 3 mornings; a deposit whose glow a root reached never counts as missed again; the rest as before, 5.5 to 8.5 m beyond the newest tip | 0.3 to 0.6 far | [PLACEHOLDER] Coordinator's suggestion, Simon agreed (18:31 UTC). Measured: about 47 % of underground-wish days point far; a glow follower reaches every far wish within one night (see "0.8.1 sim"). |
| Far view underground (0.8.2, planned; specs/root-field-extras.md 1) | pinch out up to the whole 30 m field outside the running root; the last 7 nights' roots brighter; a patch counts as known within the fog's reach of a root or under a meadow sign | camera 40 to 60 m; 5 to 10 nights | [PLACEHOLDER] Plan journeys over several nights. |
| Rock bands and soft veins (0.8.2, planned; root-field-extras.md 2) | about 3 bands, 6 to 15 m long, 1 to 2 m thick, each with a gap; about 2 veins, 1 to 1.5 m wide, 8 to 20 m long, root cost 0.6x inside | 2 to 4 bands; veins 0.5 to 0.8x | [PLACEHOLDER] A small route choice to far patches. |
| Fungal network (0.8.3, planned; root-field-extras.md 3) | about 3 hidden mycelium patches in the middle and far rings, seen within 1 m; a touch links the nearest unreached rich patch within 15 m for 3 nights at the fine share 0.1; once per patch per tree | 2 to 4; 2 to 4 nights | [PLACEHOLDER] A rare find, about once a week at most. |
| Starter patch (`starter`) | water 12, N 16, P 14, K 10 dots (0.6.3: 10 / 8 / 7 / 7) | fixed | A young tree's first week, before its roots reach a rich patch. |
| Meadow signs | clover over nitrogen, nettles over phosphorus, comfrey over a potassium wish, stones over rock only (0.6.3: nothing over phosphorus, stones also meant potassium); a patch beyond the clearing shows its sign 2.5 m inside the clearing's edge in its direction, 0.8 the size (0.8.1) | fixed | One sign per thing (build thread's choice while Simon is away; comfrey is the gardener's potassium plant). |
| Wish kind (`Diary.wish_kind`) | the kind with the lowest stock against need, any of the four (0.6.3: water or nitrogen only) | fixed | The glow never led to the missing phosphorus. |
| Underground wishes (`Diary.underground_share`) | 0.6 of wishes (0.6.3: 0.75) | 0.5 to 0.75 | A wish deposit of the right kind alone made steered trees 3 to 4 days faster. |
| Missed wish deposits (`MISSED_MAX`, `missed_ahead`) | at most 4 wait; a later wish points at an untouched one of its kind 3.5 to 10.5 m ahead again, or is a day wish | fixed | B4: up to 11 missed deposits lay fresh by the last week. |
| Glows at once (`Diary.glows`) | one; yesterday's glows faintly only on a night with a day wish | fixed | 0.7 broken item 3. |
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
| Bonsai pellets (`BonsaiSim.pellet_kind`, `tin_kind`) | a tap on a tin pours at once (1 tap; 0.8: tin, then soil, 2 taps; specs/0.8.md 0.8.2.2); the last kind is kept in the save, before any choice the tin gives what the soil lacks most | fixed | C1: it took 3 taps and forgot the kind each visit. |
| Bonsai needs shown (`BonsaiSim.hunger`, `BonsaiView.hungry_color`, `K_TIP_SHARE`, `ROOTBOUND_LIFT`, `bonsai_soil.gdshader`) | dry soil pale tan and cracked, soaked soil dark and glossy; hunger from a little before growth slows: N pales to yellow-green, P dulls to bronze, K browns up to 45 % of tips; on repot day the soil rises 7 mm with 6 roots at the rim and 5 under the foot | see balance-0.8.md | [PLACEHOLDER] C4: natural signs only, read at 450 x 800. |

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

## 0.8.1 sim, 2026-10-01 (what happened; build thread, docs/notes/sim-0.8.1.md on iterate-0.8.1)

Headless, seeds 3/14/27, `tools/strategies.gd` (nine styles) and the new `tools/qa_field.gd`, on
layout 3. Steered months: linden 29, birch 24, beech 28, sycamore 28, alder 28, oak 32 to 33, as
in 0.8. Never steered: 35 to 40 (birch 26 to 31), oak 39 to 44. Boost all day and end every root at once finishes
on all 18 runs (29 to 42). In the field a night's root touches 0 to 3 rich patches and its straight
drive passes 1 to 4; but a straight root on the whole tank could reach 7 to 14, because a calm
night buys 30 to 55 m (median straight distance from the start 11 to 16 m). The scarcest kind lay
within one and within two nights on 100 % of days; in the first week every kind within one night.
Four sim findings changed the game: the boost life force (0.3), the boost price for a hungry tree,
the seep cap and the "reached" flag on wish deposits (rows above). One test changed instead of the
game: "steering leads after 12 days" (both trees still sit on the starter patch then) now reads
"steering to deposits finishes at least 4 days before ending early, and ending early by day 40"
(acceptance.md).

**What it means:** where the root actually goes, item 29's "2 to 4 rich patches" holds; what the
tank could buy in a straight line does not, so no far patch truly needs two nights yet. Still open:
the tank's reach (the build thread recommends a dearer metre, `base_cost_per_metre` about 2.5 or
`cost_exponent` about 0.8, so a calm night buys 12 to 18 m with its seconds unchanged, then deposit
size or `tip_share` retuned so steered months stay 26 to 34; the other way, a field of 50 m or more
or a dearer distance, leaves the spec's ranges); sycamore boost in the morning at 16.7 % on seed 14;
quitting at day 42 on two runs; oak straight down 43 to 44; roots too thin to read in the field
overview (for the 0.8.2 far view).

## 0.8 balance fixes, 2026-09-30 (what happened; build thread, docs/notes/balance-0.8.md)

Headless, seeds 3/14/27, `tools/qa_nutri.gd` and `tools/qa_glow.gd` (both now in the repo),
against the targets in specs/0.8.md section 5. Days at the soft floor: at most 17 % on every
species and seed (was 31 to 46 %). Stock over two days: 0 % (was up to 100 %). Rich patches in
calm reach: 11 to 16 counting any patch with one fresh dot, at most 11 with a quarter fresh (was
27 to 43). The scarcest kind was always in reach and late-month nights still touch fresh
deposits. Two glows at once: never (was 4 to 17 nights). Longest night 52 s. The kind behind the
few floor days is phosphorus 38 to 100 % per species, but that is one to five days a month, so
the "no nutrient behind more than 60 %" target is read as met.

**What it means:** B2 to B4 and 0.7 item 3 pass; the fix also showed that the 0.6.3 months were
set by starvation, so the species paces were slowed instead. Still open: playing entirely without
roots (boost all day, end every root at once) no longer finishes on 3 of 18 runs, which the build
thread fixes as 0.8.1; oak straight-down (43) and alder end-early on seed 3 sit just past item 10a.

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

## 0.8 mood moments (specs/0.8.md)

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
10a. A tree whose roots are never steered (every root ended at once, straight down or random)
   finishes more than about 10 days after its steered days, or not by about day 42 for oak and day 40 for the rest (Simon,
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
