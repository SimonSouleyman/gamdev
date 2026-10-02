# 0.8.2.3 perf: the root run's frames and the end of the night (PC thread, 2026-10-02)

The design thread found the nightly root sim about twice as dear as in 0.8.1 (sim-0.8.2.2.md).
Short test run only. Measured on the PC (Windows, Godot 4.7.2, `--phone`, Compatibility,
headless sim) with the new `tools/root_perf.gd` (linden, seed 14). The phone is about 3 to 5
times slower.

## Where the time went

- **The detour check** (`RootSystem._avoid`, 0.8.2.2) already had a grid of old main-root
  segments, but it was rebuilt from the whole network at every start (up to 3 to 4 ms in one
  frame by day 30, the "slowest run frame"), and every frame it collected every segment of
  4x4x4 cells (about 77 on average for straight_down) and tested all of them 4 times ahead, again
  in each step's push-out. That was most of the run's cost: straight_down spent 3.7 s in the
  detour and 2.7 s in the step over 38 nights (dots 0.9 s and 0.9 s).
- **The end of the night** (fine roots, the leftover's side roots, in the run's last frame):
  7 to 9 ms for dots and straight_down, 23 ms ending early, 14 ms "stop at once". Most of it is
  `nearest_fresh` (up to 11 ms alone) and the space colonization steps. That is a 30 to 100 ms
  hitch on a phone, on top of the settle's mesh in the next frame.

## What changed (no change in play)

- The obstacle grid is kept across nights and only grows (each frame adds the segments grown
  since); it is rebuilt only for another graph (a loaded game).
- The segments near the tip are looked up again only when the cells, the graph or the start rule
  change, or the tip moved 0.3 m, and kept only if they pass within 1.95 m of that point. Only
  segments within about 1.5 m of the tip can block, bend or push it, and the order is kept, so
  the bends are the same; whether the cells held any segment (which picks the bend's path) is kept
  apart.
- The end of the night can grow over several frames: `RootSystem.end_in_frames` (the root view
  sets it) lets the end's steps pause at `await _grow_on` once the frame's share is spent (2 ms in
  the run's last frame, then `RootView.END_FRAME_USEC` = 6 ms a frame, the camera holding still;
  `is_settling()` counts it). The steps and their order are the same, nothing else touches the
  roots or the stock meanwhile (the night's sim tick only ages the tree), and a save, a new run
  or `nearest_fresh` finishes it first. Tools, tests and `GameState.steer` grow everything at once
  as before.

## Before / after (PC, ms)

| | night avg | night max | slowest run frame | end of night, slowest frame |
|---|---|---|---|---|
| dots before | 90 | 173 | 4.16 | 8.72 |
| dots after | 69 | 116 | 0.65 | 6.18 (in frames; 9.8 at once) |
| straight_down before | 181 | 412 | 3.31 | 7.45 |
| straight_down after | 110 | 260 | 1.22 | 5.10 (7.6 at once) |
| end_early before | 22 | 27 | 2.93 | 22.89 |
| end_early after | 20 | 25 | 0.26 | 7.55, 4 more frames (22.4 at once) |
| at_once before | 11 | 14 | - | 14.24 |
| at_once after | 11 | 15 | - | 6.93, 3 more frames (15.2 at once) |

"In frames" is the view's path (`root_perf.gd --frames=6000`). With `--frames=300` (a small
share, as on a slow phone) no end frame was above 2.8 ms on the PC (dots up to 13 frames,
end_early up to 42), so the largest piece that cannot pause is about 1 ms on the PC (4 ms on a
phone); `graph.update_radii` is one of them. The run frames' average fell by 25 to 40 % (dots
0.074 to 0.055 ms, straight_down 0.151 to 0.090 ms).

## Same roots

`tools/strategies.gd -- --phone --species=linden --seed=14 --strats=dots,straight_down`: dots
finished day 30, straight_down day 38, the same root metres every night before and after.
`root_perf.gd` prints a fingerprint of every root node's position, the stock and the finish day:
equal before and after for dots, straight_down, end_early and at_once, at once and in frames
(6000 and 300 us).

## Tool fix: wishes reached

`strategies.gd` counted the diary's lines with a drawing, which are also the finds' sketches and
the glows reached again (73/8, 112/6). It now counts each wish deposit once, by its "reached" flag
(`Underground.mark_wish_reached`): dots 1/8, straight_down 0/5 for seed 14 (neither steers for
the wish).

## Not yet measured

The phone itself. Also seen on the way: `RootView._outside_roots` tests every root segment each
run frame (only the thick ones near the trunk count); cheap so far, a candidate if the phone shows
run frames creeping up late in the month.

Short test run: tests 10836 passed, `tools/autoplay.gd -- --phone` (Compatibility) exit 0.
