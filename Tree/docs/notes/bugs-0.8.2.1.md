# 0.8.2.1: fixes from the 0.8.2 bug hunt

Written by the build stream s0821-bugs, 2026-10-02. Source: the bug hunt report in
`GameDev/tree-qa/review-0.8.2/bugs/report.md` (items 1 to 9, probes and old saves there). Logic
only (shared/, the logic of roots/root_view.gd, main.gd's input handling) and tests; no visual
change. Tests: `tests/test_bugs_0821.gd`, one or more per item. Short run (Simon's prototyping
rule): all tests, `tools/autoplay.gd` headless (exit 0), the report's probes on the old saves.

## What changed

| # | Bug | Fix |
|---|---|---|
| 1 | Old saves (0.8.1 and earlier, Simon's phone save) showed the routine "The old roots drew ..." line on every old day, hid the reached wish and finds, doodle always a sun. | `Diary.topic_of` reads a topic from the wording of a line without one (`LEGACY_LINES`: reached wish, finds, clearing plants, visitors, milestones, weather); the night's numbers ("drew/drank water", "new root grew", fine roots, nodules, twigs died back) are `ROUTINE` and never show; the day's height ("is 6.1 m tall with ...") is a plain note, shown only on an old day with nothing else. `Diary.drawing_of` gives old lines the matching doodle (a reached wish: its plant). Nothing is rewritten in the save: the inference runs when a page is drawn, so the lines stay as they were written. Old days never had a wish line (0.8.1 kept the wish outside the diary), so none shows. |
| 2 | A reached wish was hidden on its own page: `page()` kept the first line of a topic, the clearing's "find" lines written at sunrise beat the night's "wish found". | A topic keeps its last line, as the comment said, and a reached-wish line (`is_reached_line`) beats every other find of its day. The doodle now follows the comment too: the third place first (also when it stands second on a day without a need), then the wish, then the need. |
| 3 | A far wish pointed at again (missed_ahead) kept 1 morning, not up to 3: counted from the day the deposit was placed. | `Diary.wish_since`, the morning the wish was chosen (or chosen again), saved; a far wish keeps while `day - wish_since < FAR_DAYS`. Old saves (no key): the patch's day, as before. |
| 4 | A far wish could come on day 1 (about half the seeds). | **Choice:** far wishes start on day 5 (`Diary.FAR_FROM_DAY`, the 0.8.2 first-time check's "from about day 5"), both with the running share and the bare coin. The running far share counts underground mornings from that day only, so day 5 brings no burst of far wishes to catch up. Days 1 to 4 teach the near wish. [PLACEHOLDER, range 3 to 7] |
| 5 | `RootView.setup` kept `_settle_t`: start over or load during the 4.6 s settle, and the next night counted as settling (far view refused, swipe-up blocked, wrong hint, stray `run_finished`). | `_reset_run_state()` in `setup`: settle timer and pieces, the morning-mesh job, the tip cache, waiting/quiet flags, the run's notes, touches and presses, the life bar's start, the look cache; stick and dive let go. Probe `probe_settle2.gd` now: "new game first night: settling false", hint "Tap a point on a root ...". |
| 6 | Life bar too full after loading mid-run (start rebuilt from length x base cost). | `resume_run` starts the bar from the saved `roots.run_tank` (never below what is left); saves without it (before 0.8.2) keep the old estimate. |
| 7 | `roots.run_room` (cap on the night's intake) was not saved. | Saved and loaded in `RootSystem.to_dict/from_dict`. A mid-run save from before has none: `GameState.from_dict` gives it the room the tree has now (`stock_room(find_hold_days)`; the night's finds are in the stock already). |
| 8 | A hold could outlive a lost release (alt-tab with the button held: 4x until the next click). | `TreeView.cancel_press()` ends the press (a hold just stops, nothing boosts) and forgets the fingers; `main.gd` calls it (and `RootView.release_controls`) on application/window focus out, pause and close; a cancelled touch or mouse release (`canceled`) calls it too. |
| 9 | Day 30 (7071 root nodes): `_settle` rebuilt the whole old-root mesh in one frame, then the settle's end did it again; `pick_far_tip_at` recounted the root ends per tap. | `_settle` keeps the old-root mesh (it already ends where tonight's root starts; rebuilt only if it does not). The morning's mesh, tonight's root included, is built in pieces of 300 nodes per frame during the settle (`BranchMeshBuilder.append` into one set of arrays, `mesh_from` at the end: one surface, the same vertices and triangles as one build, tested) and swapped in at the settle's end; frames left over finish at once. `RootView.far_tips()` caches the root ends until the roots change (instance, node count, main roots). |

## Measured on the PC (headless, `long_30.json`, day 30, 7071 root nodes, end early with side roots)

| | Before | After |
|---|---|---|
| `_settle` | 69 to 75 ms | 4 to 6 ms |
| end early (side roots + settle) | 92 ms | 29 to 30 ms (the rest is `finish_early`'s side roots, 13 ms, unchanged: simulation) |
| settle's end (`run_finished`) | 94 ms frame | no long frame; the pieces: 25 x 3.7 ms over the settle's first frames |
| longest frame from the run's end to the morning | 94 ms | 18 ms |
| `pick_far_tip_at` per tap | 8 to 13 ms | 0.9 to 1.4 ms (the first tap after a change still counts: 8 ms) |

On the Fairphone expect about five times these: a piece about 15 to 20 ms, no single frame of a
few hundred ms. `finish_early`'s 13 ms (side roots) is simulation and was left as it is.

## Tests changed

`test_field.test_a_far_wish_waits_two_or_three_mornings` looks for the first far wish from day 5
(before, from day 1: with no roots grown, the four near wishes of days 1 to 4 lie missed, and
missed near deposits then take the wishes, so no far one came in 30 days of that bare setup).
`test_side_roots.test_far_wishes_are_balanced_on_every_seed` counts the mornings from day 5, as
the diary's running share now does (seeds 3, 14, 27 stay within 40 to 60 % far).

`test_care.test_neglected_roots_can_leave_the_tree_thirsty` measured "the seep alone" after
zeroing every water dot, but the sunrise regrows some of them and the roots still had them
tapped; with far wishes from day 5 the network differs and 16 regrown tapped dots added 2.6 of
water (23.8 of a 21.2 cap). The test now also drops those dots from `tapped` (run dry for good), so
it measures what it says. The seep cap itself is unchanged and holds.
