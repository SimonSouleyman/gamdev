# Care 0.6.3: notes for the design thread (stream s-care)

Written by the build stream s-care, 2026-09-29, so the design thread can merge it into
`design-doc.md`, `tuning.md` and `specs/care-and-pruning.md`. It narrows the spec
(`specs/care-and-pruning.md`) to what the owner chose ("tree care first": improvements to
existing care only, nothing new). Anything that would be a new mechanic is listed at the end as a
0.7 proposal.

## Spec, as built

### Reading what the tree lacks

**What the player feels.** Noticing a houseplant is thirsty: a glance at the crown says what
tonight's root should look for. Never alarm, never a loss.

**The need (sim, `GrowthSim.assess_needs`, at each sunrise after the night's intake and the
leaves' water).** For each of water, N, P, K: *coverage* = stock / what a full calm day of growth
would use (`day_capacity()` segments x `node_cost()` x the species' need for that kind; water
also after the leaves' upkeep). The day's capacity is the growth cap (`max_pace`) integrated over
today's daylight, i.e. what the tree would grow if nothing were short. A need is
`smoothstep(0.75, 0.25, coverage)`: nothing while the stock covers three quarters of the day,
full at a quarter. The stock is always used up by evening on purpose (the day's pace spreads it),
so the need is judged once, at dawn, from what the night brought, and held for the day.

**The signal (what shows).** `GameState.care_signals()`: the need eased from yesterday's value to
today's over the first 35 % of the daylight (mid-morning), so a need met by last night's root is
gone well before noon, and a new one grows in over the morning. Only the strongest need shows on
the tree (the spec: one sign at a time); the care page lists all of them. A need shows only if it
can be acted on tonight: at least one untapped deposit of that kind lies within reach of some
root node with the life force expected at dusk (`Care.reachable`). Nothing shows on the seed, on
the first day, or on a finished tree.

**Shape first, colour second** (autumn hides colour: seasons follow the real calendar).
- Water: the sprays hang: the leaf tips drop (up to 55 % of a spray's length) and swing in towards
  the stem, a sixth of the sprays fold away (the crown thins), a slightly greyer, duller green.
- Nitrogen: new shoots (grown since yesterday's dawn) carry fewer (down to 45 %) and smaller
  (down to 65 %) sprays; the older leaves pale a little towards yellow-green.
- Phosphorus: fewer leaf masses (up to a third of the masses stay bare, inner and lower first);
  a darker, dull purple tinge as the second cue.
- Potassium: fewer leaf masses, as phosphorus; a brown, scorched tinge as the second cue.
The droop and tints are shader uniforms on the one crown material (no new draw calls); the
shoot and mass cues are baked when the crown is rebuilt (it is rebuilt when the signal changes).

### The care page (journal, ribbon "care")

Handwritten, read from the crown and the roots, refreshed when the book opens:
- what the tree lacks now (each kind with a need, in words: "thirsty: the leaves hang"), or "It
  has what it needs today";
- for each need, which dots to steer for tonight and roughly where: the nearest untapped deposit
  of that kind within reach ("blue dots, north-east, about 4 m out, shallow"), and the meadow hint
  that marks it (rushes and the damp patch for water, clover and nettles for nitrogen);
- the crown's shape: lopsided towards a side (the crown's centre of leaves against the trunk),
  crowded inside (the share of shaded tips), shaded twigs dying back (the count last dawn);
- the last cut: when, how many segments, how many buds it woke and how much grew back at dawn.

### Pruning with a felt effect (`GrowthSim.prune`, `wake_buds_after_cuts`)

- A cut is recorded (where, which node it was cut from, how much). At the next sunrise a share of
  the cut wood comes back as vigour, `PRUNE_REFUND` = 0.3 of the cut segments.
- About 70 % of the refund wakes buds near the cut: 2 or 3 living nodes within 1.2 m below the cut
  on the remaining branch each put out a new shoot of at least two segments, growing outwards and
  towards the light, not back into the crowded inside. The rest joins the dawn burst for the whole
  crown (over the burst's own cap, still paid with nutrients like any growth).
- The light the cut lets in: at dawn 6 to 30 growth markers (half the cut) are seeded around the
  cut, so part of the day's ordinary growth goes there (the same growth, placed, not extra).
- The refund of one day is capped at 0.3 x a fifth of the living tree (several cuts add up, the
  fifth caps them). A finished tree gives no refund (pruning is then for the look).
- Thinning the inside works through shade dieback that already exists: fewer living nodes above a
  shaded tip means fewer shaded tips and fewer twigs dying back.
- The node budget: pruned wood (dead, not shed) is dropped from the graph at sunrise when the graph
  is 85 % full, and node costs keep counting it (so compaction changes no price). Without this,
  pruning hard every day filled the 3000-node budget with dead wood and "finished" the tree early
  (it was broken-list item 13 before this stream).

### Soft failure
A short tree grows slower (the soft Liebig floor 0.35 and water's hard cap, unchanged). The signals
take nothing away: no wood, no height (broken list 14). Thirst still drops nothing by itself.

## Tuning (to merge into tuning.md, section "Care and pruning")

| Variable | Value | Range | Reason |
|---|---|---|---|
| Need starts / full (`Care.NEED_START`, `NEED_FULL`, coverage of a calm day) | 0.75 / 0.25 | 0.6-0.9 / 0.1-0.4 | [PLACEHOLDER] Shows only when the stock clearly will not last the day; a quarter or less is a full sign. |
| Signal easing (`Care.EASE_SHARE`) | first 35 % of daylight | 20 to 50 % | The spec: eased over the morning, gone by noon (broken 11). |
| Tonight's life force estimate (`Care.expected_life_force`) | now + a calm rest of the day x 0.8 | 0.6 to 1.0 | Boosting spends some; the reach check stays on the safe side (broken 12). |
| Reach (`Care.reachable`) | distance from the nearest root node x cost per metre at the deposit <= the estimate | fixed | The cost rises with distance and depth, so the deposit's own price is the upper bound. |
| Droop (shader `thirst`) | tips drop up to 0.55 of a spray, a sixth of sprays fold away | 0.3 to 0.8 | Readable at the far camera in autumn colour. |
| N cue | new-shoot sprays x0.45 count, x0.65 size; pale 0.3 | 0.3-0.7 | Shape first; pale is the second cue. |
| P/K cue | up to a third of the leaf masses bare; tinge 0.3 | 0.2 to 0.45 | Fewer leaf masses, as the owner asked. |
| Pruning refund (`GrowthSim.PRUNE_REFUND`) | 0.3 of the cut segments, next sunrise | 0.2 to 0.4 | The spec's range. Below 1, so pruning never speeds a tree up (broken 13). |
| Share near the cut (`PRUNE_NEAR_SHARE`) | 0.7 of the refund, at least 2 segments per bud | 0.4 to 0.8 | The crown visibly fills in at the cut by the next day; one-segment shoots did not show. |
| Markers around a cut (`PRUNE_MARKERS_MIN`/`MAX`) | half the cut, 6 to 30, within 1.2 m | 4 to 40 | Draws part of the day's ordinary growth to the cut: felt, but no extra growth. |
| Buds woken per cut (`PRUNE_BUDS`) | 2, 3 for a cut of 20 segments or more | 2 to 3 | The spec. |
| Distance below the cut (`PRUNE_BUD_REACH`) | 1.2 m along the branch | about 1 m | The spec: within about a metre. |
| Daily refund cap | 0.3 x a fifth of the living tree | fixed | The one-fifth rule caps several cuts a day. |
| Dead-wood compaction (`COMPACT_AT`) | at 85 % of the node budget, at sunrise | 0.7 to 0.95 | Keeps the budget for living wood; costs unchanged. |

### Noon crown (look, same stream)
The owner found the 0.6.2 crown dark and blotchy at noon on the PC. In `hero_crown.gdshader`: the
outer, sunlit sprays are lifted (`sun_lift` 0.3 x (1 - occlusion)), the interior darkening is
softer (0.4 -> 0.32 of `interior_dark`, AO 0.6 -> 0.45 of the occlusion), the backlight a little
stronger (1.1 -> 1.35), and a daylight fill (`day_fill`, `TreeView.DAY_FILL` 0.09, off at night,
less in rain) keeps sprays in the crown's own shadow dark green instead of black. Mean brightness
of the crown area at noon from the north rises by about 5 %; no neon (sheet
`tree-qa/care/_noon_after.png`).

| Variable | Value | Range | Reason |
|---|---|---|---|
| Noon lift (`sun_lift`) | 0.3 | 0.15 to 0.4 | The sunlit side reads at noon; above 0.4 the rim turns lime. |
| Daylight fill (`TreeView.DAY_FILL`) | 0.09 | 0.05 to 0.12 | Removes the black blotches; more flattens the leaf masses. |

## Broken list, results (2026-09-30, headless Godot 4.7.2, tests in `tests/test_care.gd`)

- **11** (a signal while nothing lacks, or a day and night after the need was met): passes. With
  a full stock no signal shows; a night without water shows thirst by noon; after the next night
  brings water the sign is still easing out at dawn and is 0 by 37 % of the daylight
  (`test_signal_shows_only_while_lacking_and_clears_next_morning`).
- **12** (a need the player cannot act on): passes. A shown sign always has an untapped deposit of
  that kind within reach of a root node for the evening's estimated life force; with every deposit
  of the kind drunk, or with no life force, nothing shows
  (`test_signal_only_when_a_deposit_is_in_reach`).
- **13** (max pruning finishes sooner, or a cut shows nothing next day): passes. Cutting a fifth
  of the tree every day for ten days leaves it further from its finish than never cutting
  (`test_pruning_every_day_never_finishes_sooner`); over a whole month (seed 14, linden) never
  pruning finishes on day 29, pruning a fifth every noon does not finish by day 50. A cut of 10 to
  40 segments gives 0.3 back (never above 0.4), wakes 2 to 3 buds whose leafy shoots stand within
  2 m of the cut right after the next sunrise (`test_a_cut_answers_by_the_next_morning`).
  Before this stream, pruning hard every day would also have filled the 3000-node budget with
  dead wood and counted the tree as finished; compaction fixes that
  (`test_dead_wood_compaction_keeps_the_tree`).
- **14** (a drooping or pale tree loses wood or height): passes. Three days each without water and
  without nitrogen: grown wood and height never drop (shade dieback, the existing soft loss, is
  counted apart) (`test_a_short_tree_never_loses_wood_or_height`).

Also checked: `tools/month_report.gd --species=all` finish days are unchanged (a tree that is never
pruned grows exactly as before: linden 29, birch 23, beech 28, sycamore 28, alder 34, oak 31);
the full test suite and `tools/autoplay.gd` pass.

How often signs show with the chase bot (linden, seed 14): none in the first three weeks (the
roots bring more than a day needs); potassium from day 23, phosphorus from day 26. Water was never
short for the bot; a player who ignores the blue dots will see thirst. That is plausible, but
whether the signs come often enough to be felt should be judged in play on the phone.

Shots (`GameDev/tree-qa/care/`, made with `tools/care_shot.gd` and `tools/compare_shot.gd`):
`_signals.png` (healthy, thirsty, nitrogen- and potassium-short in summer and autumn),
`_cut.png` (before, just after, the next day), `after/linden_care_page*.png`, `_noon_after.png`.

## 0.7 proposals (not built: they would be new mechanics)
- Branches the tree marks for pruning (already a 0.7 candidate): the care page names crowded
  places in words only; a mark on the branch itself would be new.
- Cutting the leader lets the strongest side shoot below take over (a deliberate leader handover).
- Water-logging or over-feeding signs for the tree (the bonsai has them).
- A way to open the care page from the tree (for example tapping the drooping crown); today it is
  a ribbon in the journal and the HUD hint still names the missing dots.
