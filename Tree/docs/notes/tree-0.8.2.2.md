# 0.8.2.2 tree and roots: Simon's phone notes on 0.8.2.1

The spec is specs/0.8.md, section "0.8.2.2" (broken items 35 to 37 and 40 to 42). The bugs come from
the phone log (`tree-qa/session-0.8.2.1/perf.txt`, `logcat.txt`) and from the frame-by-frame review
of his recordings (`findings.md` there). The shed and bonsai items are on a separate branch.
Short test run only: the tests, `tools/autoplay.gd` (exit 0) and phone-path shots
(`--phone --rendering-method gl_compatibility`, 450x1000) in `GameDev/tree-qa/fix-0.8.2.2/tree/`.

## 1. Fast-forward at 8x (item 35)

`TreeView.FAST_FORWARD` is 8 (was 4). The sim still takes fixed steps, so holding changes no result.
`test_fast_forward.gd` checks 8x, eight game seconds per held second, and a day held from the
morning taking an eighth of the real time (same tree, tank and night).
Frame rate: 8 sim steps per frame cost 4.9 ms on average on the PC (11.5 ms at worst, day 16,
845 segments). The phone is about 3 to 5 times slower, so `main.FF_BUDGET_MS` (12 ms) caps the
sim time per fast-forwarded frame. Past the cap the day runs a little slower and the frame rate
holds. The steps are the same steps, so the tree is unchanged. Not yet measured on the Fairphone.

## 2. Back to the day: the hitch and the wrong camera

The phone log and the recordings point to three causes:

- **The album's morning photo** moved the player's camera to the album pose for one frame
  (`album_pose`). That was the low "whole forest and sky" frame. The read-back, the resize and the
  PNG and JPG encoding then ran on the main thread, which caused the 1.6 s freeze. The photo is now
  drawn off screen by a camera of its own in the same world (`TreeView.album_photo`, a
  SubViewport). It is taken behind the sunrise's black (`main._morning_behind_black`) and written
  on a worker thread (`Photos.save_image`). The screen's camera never moves: `tools/qa_switch.gd`
  checks this (0.000 m). The photo now shows the tree as it comes out of the night, before the
  dawn burst, with one photo per morning as before.
- **The live picture** (the phone's wallpaper layers) rendered four 1080x1920 layers at sunset
  and in the morning. `fix_alpha_edges` and a cubic resize of each layer ran on the main thread,
  which matches the 3 to 6 fps seconds at sunset in the log. It is now drawn behind the next black
  (the dive's, the sunrise's or the shed's). The image work moved to the worker thread.
- **The first frames after a switch** were long. The fade's tween advanced by their long time,
  so the black lifted while they ran. `main._hold_black` pauses the fade for 3 frames after every
  switch (dive, sunrise, into and out of the shed), and for as long as the photo or live picture
  takes. The tree's camera is placed before the first frame (`TreeView.snap_camera`).
- **Grey frames on the dive and the rise**: the dive's camera fell 1.6 m under the meadow while the
  picture still showed. It now stops 0.35 m above the ground (`DIVE_FLOOR`) and the black covers
  the rest.

Measured with `tools/qa_switch.gd` on the PC phone path: the switch frame (117 ms on the dive) and
the next ones are fully black, the fade-in frames run at 33 ms, and the tree camera is never under
the ground while the picture shows (`switch_log.txt`, shots in `switch/`). The upside-down top-down
frame when leaving the shed (findings 1) did not reproduce on the PC. My best guess is the live
picture's or the album photo's render on the phone's GL driver. Both are now drawn behind black.
Needs a look on the phone.

## 3. Corner pictures with the view (findings 5)

The shed, camera and shears pictures waited for the end of every transition. The journal button
did not. They now hide only during the first half of the dive (`_diving`) and are set at the switch
itself (`_update_corner`), so they come with the view, behind the black. The camera and shears
ignore taps during a transition.

## 4. Swipe up to dive, swipe down to come back (item 36)

`TreeView.is_swipe(from, to, up, seconds)` is shared by both views. At sunset a quick upward swipe
dives; the root view's way back is a downward swipe (`RootView.swipe_back`). The hints and the
sunset page say so ("Tap the ground or swipe up to dive to the roots", "Swipe down to wake the
tree"). Tested in `test_feedback_0822.gd`.

## 5. Dotted bright streaks underground

The cause is in `tree/bark.gdshader`. Old roots within 1.5 m of the camera are cut away with a hard
cut (`near_fade_band` 0). The dither hash `fract(sin(...) * 43758.5)` is exactly 0.0 for a share of
pixels, and `0.0 > 0.0` kept those pixels. Every cut-away root left scattered single pixels of
glowing bark along its shape, and the phone's lower precision makes it worse. A hard cut now
discards outright, and the soft fade discards on `>=`. The same `>=` change went into
`leaf.gdshader`. Shots: `roots/near_old_roots_*.png` show no specks at the camera spots that had
them (the investigation's before/after pairs are in the session scratch).

## 6. Roots go around old roots (item 37)

`RootSystem` now keeps two directions. `heading` is the held one: the stick and the dive steer
it, and rocks slide it as before. `travel` is where the tip really goes. `_avoid` works on a grid
of main-root segments, each from a node to its parent; fine and side roots are not in it. It looks
`AVOID_LOOK` 1 m ahead along the held (and pulled) heading. If that line comes closer than
`AVOID_CLEARANCE` 0.35 m to an old root, `_free_bend` tries bends in 10 degree steps. It tries the
way round that is square to both the heading and the blocking root first, on the side the tip is
already on (over or under a root across the way). Then it tries the plain sides, sideways first in
the topsoil. The last side used is tried first, so the tip does not shake. `travel` turns toward
the bend at 2.6 rad/s on top of the stick's rate and eases back onto the heading once the line is
clear. `AVOID_HARD` 0.2 m is a wall like a rock: a step that would come closer, or squeeze between
two roots, stays put, and the boxed-in rule (`_unstick`) takes over. The root a night starts from,
and anything within 1 m of the start, is ignored for the first metre. Tonight's last 1.5 m is never
an obstacle.
Tests: going round a root across the way in the topsoil and deeper, never closer than 0.2 m, held
heading unchanged, travel back on it, few curve reversals. Also: a fine root does not block, a
full-stick circle never crosses itself, and a cage of roots ends the run instead of slipping
through. In the QA run (`roots_log.txt`) a root aimed square at an old root passed it at 0.32 m and
ended with its heading unchanged.

## 7. Gentler pull (item 40, PR #28)

`magnet_radius` is 1.2 m (was 1.8) and `magnet_rate` is 0.6 rad/s (was 1.3), as in the spec.
The pull is now a bend on top of the held heading, at most `MAGNET_MAX_ANGLE` 30 degrees, and eases
off at the same rate once the deposit is passed or drunk, so the heading itself is never turned.
The old magnet test's deposit moved closer to the line: 0.8 m beside it instead of 40 degrees off.
Test: one deposit bends the tip by at most about 30 degrees (and does bend), the tip reaches it,
and it ends back on the unchanged heading.

## 8. End at once, small roots everywhere (items 41, 42)

"End root here" shows from the moment the night opens: in the pick mode, and while the tip still
waits for the stick. Ending before a start is picked (`RootSystem.end_at_once`, also
`GameState.finish_run_early` with no run) spends the whole tank on small roots. So does ending
before the root moved. There is no main root that night. The second level now grows toward the
fresh dots nearest to the network first (`nearest_fresh`), within the reach, until the node budget
is spent. The third level follows as before. Short tips around tonight's root (or the newest root
ends) take the nodes no dot needs.

Numbers changed, with reasons:
- `side_reach_max` is 2.5 m (was 3.0), as in the spec.
- `side_start_level` is 1: the small roots start from every night's main and fine roots, not from
  older side roots. Grown from side roots too, the small roots crept a reach further out each
  night. A month of ending every night at once then finished only 3 days behind steering (linden
  seed 14: day 33 against 30; the test and acceptance want more than 4), and beech seed 3 never
  steered finished on day 32 (the test wants 33 or later). With level 1: linden end_early day 36
  against dots day 30, boost_quit beech day 36 and linden day 37. Steering is unchanged at day 30
  (day 30 before this build too). Before this build end_early finished on day 38.

The design thread runs the full balance sim after this push.

## Tests and tools

- `tests/test_feedback_0822.gd` (new; registered in `run_tests.gd`, which now takes
  `-- --only=<words>` to run a few suites), `test_fast_forward.gd`, and `test_root_system.gd` (the
  magnet test).
- `tools/qa_switch.gd` films the dive, the sunrise and the shed trips frame by frame and takes an
  off-screen album photo. `tools/qa_roots_0822.gd` takes close shots of old roots, a root going
  round one, and a night ended at once.

Left open: the phone's frame rate at 8x, the top-down frame on leaving the shed and the hitch at
sunset all need a look on the Fairphone.
