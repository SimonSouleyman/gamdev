# Branches the tree marks for pruning (0.7): notes for the design thread (stream s07-marks)

Written by the build stream s07-marks, 2026-09-30, for the design thread to merge into
`tuning.md` and `design-doc.md` (not edited here). Spec: `specs/0.7-candidates.md` section 2,
Simon's choice "natural signs only" (thin, dull leaves and grey bark; no icon, marker or count).

## Spec, as built

**Purpose.** Give the shears a reason every few days. Shade dieback already takes 5 % of the
shaded tips a day (birch 10 %, beech none); until now nothing showed it coming.

**What the player feels.** A gardener's eye: "that little branch by the trunk looks tired, I'll
take it off." Never alarm: ignoring it costs only what it cost before.

**Input.** None new: the shears as today.

**How a mark is chosen (sim, `GrowthSim.update_marks`, at each sunrise).** The dieback is
already seeded per tip and day (`hash([seed, "shade", tip, day])`), so the sim can read it
ahead. A mark is a *forecast only*: the dieback itself is unchanged (`_dies_back` is the same
roll `shade_dieback` uses), so a tree that is never pruned grows exactly as before. At sunrise,
after the dieback, the crown's lift and the dead-wood compaction:
- keep yesterday's marks whose tip is alive, still shaded and not yet due;
- add shaded tips whose roll takes them at one of the next two sunrises, soonest first, then
  the ones furthest out of the crown (easiest to see), up to three in all;
- only tips at least 0.5 m above the crown base (the crown lifts 0.3 to 0.8 m a day in the
  second week and would otherwise shed a marked twig before it dies);
- none on beech (its dieback rate is 0), none on the bonsai (its own sim, no marks).
A mark ends by the dieback on the day it foretold, by a cut, or when light reaches the twig
(a cut above it; the tip is no longer shaded and lives).

**Output (look, `HeroCrown`, `bark.gdshader`).** The marked twig is the tip and its segments back
to the fork (at most 6); a tip right at a fork reaches over it into the little branch it grows
on (at least 3 segments, if that branch holds 12 or fewer), so the sign is a clump, not a leaf.
- Its share of its leaf mass (at least three quarters, so a short twig still shows) thins by
  45 % at full sign (70 % in autumn), the sprays hang (60 % towards the ground) and are 20 %
  smaller: shape first, so it reads in autumn.
- Its leaves go dull: paler, greyer, washed out (colour multiply 1.45 / 1.2 / 1.8); in autumn the
  season recolours them and the sparse, hanging shape carries the sign.
- Its bark greys to a weathered silver (vertex alpha on the branch mesh, 70 % at full sign).
- The sign eases in over the morning like the care signals: 0.7 on the first of two days'
  warning, full on the day before the dieback.
- After the dieback the twig that ended in the dead tip stays bare and grey (flag "withered"),
  as it already was in the sim (a node with a child is no leaf cluster); before, the renderer
  kept drawing leaves on it.

**Cutting.** As any cut, with one change: a marked tip was dying anyway and gives nothing back.
The refund (0.3 of the cut segments) counts only the living wood that would have stayed; a cut of
the marked tip alone records no cut (no buds, no markers). The care page's "last cut" line still
names the whole cut.

**The care page.** While a twig shows the sign, the crown section adds one line, no count:
"A branch looks tired: thin, dull leaves and greying bark. Left in the shade it dies back within
a day or two; cut, it gives some of its strength back."

**Soft failure.** Ignored, the twig dies back exactly as before. Nothing else changes.

**Edge cases.** Beech: no marks. Bonsai: not included. Finished tree: marks still show (the look),
the page line does not (the cut gives nothing back then). Autumn: sparse and hanging first. A
save keeps the marks (`GrowthSim.to_dict` "marks"); compaction moves their ids.

## Tuning (to merge into tuning.md, section "Care and pruning")

| Variable | Value | Range | Reason |
|---|---|---|---|
| Marks at once (`GrowthSim.MARK_MAX`) | 3 | 2 to 3 | The spec's most. A grown linden loses about 2 shaded twigs a day (0 to 6); three lets a new one show while yesterday's are still up. Never more than 3 in 9 seeded months (tests, stats below). |
| Days of warning (`MARK_WARN_DAYS`) | 1 to 2 | 1 to 2 | The spec. Read from the seeded dieback roll, so it is exact: every mark that ended by dieback died on its foretold day, 1 or 2 days after it first showed. |
| Sign on the first of two days / the last day (`MARK_FIRST_DAY`, `MARK_LAST_DAY`) | 0.7 / 1.0, eased over the first 35 % of the daylight | 0.5 to 1 | Grows towards the dieback; the easing is the care signals' (`Care.EASE_SHARE`). |
| Above the crown base (`MARK_ABOVE_BASE`) | 0.5 m | 0 to 1 m | The crown lifts 0.3 to 0.8 m a day in week two; at 1.0 m marks got rare (3 of 24 days in the test run), at 0 some marked twigs were shed before they died. [PLACEHOLDER] |
| Twig length (`MARK_TWIG`, `MARK_TWIG_MIN`, `MARK_BRANCH_MAX`) | at most 6, at least 3 over a fork if that branch holds 12 or fewer | 3 to 8 | Enough to read as a small branch; a cut there takes at most a small branch. |
| Thinning (`HeroCrown.MARK_THIN`, `_AUTUMN`) | 45 % / 70 % of the twig's sprays | 30 to 80 % | Sparse but still there to show the dull colour; in autumn the shape has to carry it. [PLACEHOLDER] |
| Twig's share of its mass (`MARK_MIN_SHARE`) | at least 0.75 | 0.5 to 1 | A one-leaf share was invisible from the normal camera. |
| Hang, size (`MARK_HANG`, `MARK_SMALL`) | 0.6 towards the ground, 20 % smaller | 0.3 to 0.8 | Limp leaves read at arm's length where colour alone did not. |
| Dull (`MARK_DULL`, `MARK_DULL_TINT`) | 1.0 x (1.45, 1.2, 1.8) | | Paler and greyer than the crown's greens, not yellow (nitrogen's pale) and not brown (potassium's scorch). A darker olive tried first disappeared in the crown's shaded inside. [PLACEHOLDER] |
| Bark grey (`MARK_BARK`, `WITHERED_BARK`) | 0.7 marked, 0.85 died back | 0.4 to 1 | Silver-grey dead wood; the thin twigs are small on screen, so it is the third cue. |
| A dying tip's refund | 0 | fixed | It was dying anyway; see broken 9. |

How often (headless, grow_shot's play: root bot from the newest tip, calm days, until finished or
day 45): linden seeds 3/14/27: a mark shows at noon on 22/20/21 of 35/35/34 days, 18/17/19 marks,
at most 2/3/3 at once, and 17/15/18 of 49/47/40 died-back twigs were foretold (the rest were
lone tips inside a full day's shade, or over the cap). Birch: 23 to 26 days of 27 to 31, 38 to 46
marks, a third of 93 to 102 died-back twigs foretold. Oak: 26 to 31 days of 37 to 39.

## Broken list (specs/0.7-candidates.md), results (headless Godot 4.7.2, `tests/test_marks.gd`)

- **6** (more than three at once, less than a day or more than two of warning): passes. Linden
  seed 14, 24 days: never more than 3; every mark that ended by dieback died on its foretold
  day, 1 or 2 days after it first showed; a mark that ended otherwise had light reach it
  (`test_marks_warn_one_to_two_days_ahead`). The sign is fully up by noon on every marked day.
- **7** (signs on beech or on the bonsai): passes. Beech, 18 days, shaded twigs present: no mark,
  no tired node; the bonsai has no marks, nothing on it withers, and its view reads neither
  (`test_no_marks_on_beech_or_the_bonsai`).
- **9** (cutting marked branches every day finishes sooner than never cutting): passes in the
  controlled test. Three copies of one linden (day 10), fed alike every night without a root,
  ten days: cutting every marked twig at noon at its fork, or only the marked tip, leaves the
  tree no nearer its finish than never cutting (`test_cutting_marked_twigs_never_finishes_sooner`).
  Whole months (`tools/strategies.gd --strats=dots,cut_marks,cut_marks_tip`, linden, 13 seeds
  1-11, 14, 27): finish day on average 29.0 never cutting, 29.1 cutting at the fork, 29.0 cutting
  the tip only. Single seeds differ by up to 2 days either way (tip only: 28 against 30 on seed
  3, 30 against 29 on seed 7), which is the month's own noise: the same "dots" play with life
  force per leaf changed by 0.01 % moves single seeds by -2 to +4 days. A dying tip gives no
  refund, so cutting only marked tips can never add growth on its own (with the 0.3 refund it
  would have added a few segments a week).
- **8** (the sign cannot be told from autumn colour or drought droop at the normal camera):
  judged on the shots below, for Simon: the marked clump is local (one clump, not the crown),
  paler and greyer than the crown in summer; in autumn it is sparse and hangs. At full phone
  size it is quiet; it reads best when you look for it (the "learn to spot it" choice).

Never-pruning play is unchanged: `tools/strategies.gd` linden seeds 3/14/27, all six styles,
finish days identical before and after (dots 30/27/30, end_early 36/36/36, straight_down
37/38/37, random 37/37/37, boost_all 29/30/29, boost_quit 39/38/42). The full test suite (2148
checks) and `tools/autoplay.gd` pass.

Shots (`GameDev/tree-qa/marks/`, `tools/grow_shot.gd --phone --marks`, gl_compatibility,
450x800, linden seed 3 day 15, two marked twigs by the trunk at 5.8 and 7.3 m, camera on their
side): `linden_s3_day15_{summer,autumn}_marks_before.png` and `..._after.png` (each marked twig cut
at its fork), `linden_s3_day15_{summer,autumn}_sheet.png` (before, after, and both enlarged 2.8x
around the marks).

## Open points
- Readability. Shaded twigs sit inside the crown by definition; from the normal camera the sign
  is a quiet, paler, limp clump near the trunk, clear when enlarged and easy to miss at a glance.
  Stronger is easy (the table's levers) but gets less natural. Simon's call on the phone.
- A cut makes the clump fuller for a moment: the sign covers at least three quarters of the leaf
  mass it sits in, and after the cut the rest of that mass shows green again.
- About a third of died-back twigs are foretold (lone tips that fall into shade and die the next
  dawn, and days over the cap of three); the rest still die unseen, as before.
- The spec's broken item 8 needs a look review with autumn colour and a thirsty tree side by side.
