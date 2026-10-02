# 0.8.2.4 transitions: Simon's 0.8.2.3 phone recording

The findings and frames are in `tree-qa/session-0.8.2.3/findings/`. Short test run only: the tests,
`tools/autoplay.gd` (exit 0) and `tools/qa_switch.gd` on the phone path
(`--phone --rendering-method gl_compatibility`, 450x1000). Log and shots are in
`GameDev/tree-qa/trans-0.8.2.4/`. The fast-forward, sunset button, lamp and drawers are on a
separate branch. This one only touches the camera, the transitions, the journal and the photos.

## 1. Upside-down frame in bonsai mode (c4_72_79), and the 0.95 s freeze before it

The frame is flipped top to bottom, not rolled: left and right stay put and "NPK" reads mirrored
upward. It has no paper scraps. It came 1.2 s after the glide to the sill: the bonsai's milestone
photo (`main._bonsai_photo`). That code hid the HUD and read the screen back
(`get_viewport().get_texture().get_image()`). Then it resized the image and encoded the PNG and the
JPG thumbnail on the main thread, which was the 0.95 s freeze. On the phone's GL renderer the screen
was drawn flipped for the frame it was read. The camera scrap's photo used the same read-back, and
so did the album's morning photo before 0.8.2.2. That fits the same frame Simon saw on leaving the
shed.

- `Photos.shoot(cam, size)` draws a copy of a camera's view off screen (a SubViewport in the same
  world, as `TreeView.album_photo` does). `Photos.save_image` encodes it on a worker thread. The
  bonsai photo and the camera scrap use these. The screen is never read and the HUD no longer
  blinks out.
- Camera roll, checked anyway: `TreeView._frame_camera` looked straight down with `FORWARD` as up.
  Seen from the south (yaw near pi), that rolls the picture half round for a frame. The up vector is
  now the camera's own heading. `RootView._look_at_safely` keeps the camera's current up there.
- `qa_switch` now fails on any filmed frame whose camera up points down (dive, rise, shed, glide):
  none.
- `BonsaiView.enter` no longer forces a full rebuild (`refresh(false)`). The shed's visit already
  brought it up to date, and a forced rebuild made the glide's first frame long.

Measured: the off-screen bonsai photo is upright and without scraps (`switch/bonsai_offscreen.png`),
and the screen's camera moved 0.000 m. One read-back frame stays (the GPU read of the off-screen
image). The resize and encoding are off the main thread.

## 2. Journal at 15 fps, and the 0.49 s open

- Once the book is open (after its 0.2 s fade), the dim around it darkens to solid over 0.25 s. Then
  `disable_3d` stops the world being drawn under it. Closing it (or `clear_pages`) turns the world
  back on. The book's look is unchanged; only the 22 % of the world that showed through the margin
  dim is gone.
- The open's stall was the day pages' ink doodles, each drawn the first time in GDScript
  (`InkSketch._bolder`, per pixel). `Journal._process` now hands them to `InkSketch.warm` every 3 s
  while the book is shut, which draws them on worker threads. `texture()` picks them up, or waits for
  one still in progress. On the PC (12 days): the first `_refresh_diary` took 88.6 ms before and
  17.8 ms after. The rest is the first-time widget and font set-up; later opens take 3 ms.

## 3. Sunset in one frame (c1_85_12-85_17)

When the sun touched the horizon, `_update_sun` switched the sun's energy (1.6 to 0.55), the haze
colour, the golden fog and the fill light from their day values to the evening's. `_day_weight`
now eases a 0..1 day weight over `SUNSET_EASE` (0.8 s, smoothstepped), and every one of those
values blends with it. Behind a black (`snap_camera`, `setup`) it is set at once. `qa_switch`
films the sunset: the largest light step in one frame is now 7 % (before, about 65 % in one frame).

## 4. Glide to the sill (c4_70_77-71_12)

`main.enter_bonsai` waits until `BonsaiView.arrived()` (the glide is over) before it shows the
scraps, and blocks input as a transition until then. `BonsaiHud.show_hud` writes the scraps' words
before their first frame. The glide (and the way back) arcs up over the straight line by
`FLY_LIFT` 0.22 m at its middle. In `qa_switch` the lowest point is now 0.81 m above the bench
top, the scraps never show before arrival, and none is blank.

## 5. Morning starts in the grass (c1_37_17)

The sunrise rose from `DIVE_FLOOR` (0.35 m, in the grass) and closed in to 0.6 of the orbit
distance, so it passed under and through the crown. With `TreeView.rising` (set by `main._rise`
until the rise ends), the camera rises from `RISE_FLOOR` 1.3 m. It keeps the full orbit distance,
and at least `crown_reach() + RISE_CLEAR` (1.2 m) out while it is low, within the clearing. The
dive is unchanged. `qa_switch` fails on a rise frame with the picture showing and the camera less
than 1 m above the meadow or inside the crown: none (shots `switch/rise_0xx.png`).

## 6. Smaller things

- Dive start: the HUD, the corner pictures and the journal button fade out together over 0.35 s
  (`DIVE_UI_FADE`), then hide. The journal button now hides with the others during the dive
  (`_update_corner`). The brightening step was the night's exposure lift: `state.dive()` sets
  NIGHT, and `night_amount` jumped from its dusk value to 1. It now eases over `NIGHT_EASE` 0.9 s
  while the clearing shows (at once when hidden or after a snap).
- The thin white zigzag at a root tip (c1_106_50) is next to a K deposit, on a frame after
  "end root here": a fine root a few pixels wide reaching a dot, aliased (the phone has no MSAA)
  and brightened by the root glow and bloom. I found no separate spark effect in the code, and
  the bark shader's dither specks were fixed in 0.8.2.2. I left it as it is; it needs a look on
  the phone.
- Root mode: the camera stuttered through a row of dots. Each drunk dot lets the pull go or turns
  it to the next dot, so the tip's travel changes its turn at once. The camera looked 1.2 m ahead
  along it, unsmoothed, so its turn rate stepped on each collect. The look-ahead now eases
  (`RUN_LOOK_RATE` 5/s). Measured with the root bot on the PC phone path (seeds 7 and 11): in the
  frame after a collect the camera's turn went from 0.03 to 0.24-0.81 deg/frame in one step before.
  After, it ramps (0.03, 0.05, 0.09 ...). The frame time on collect frames is unchanged (no cost
  spike; 16.7 ms at vsync).

## Perf log

The `perf:` line's "worst frame" now uses the wall clock between frames. Godot caps a frame's
delta (about 150 ms on the phone: physics steps per frame), so the 0.95 s freezes showed as 150 ms.
The tree view processes in every mode, so the line also covers the shed, the bonsai and the switch
frames. Gaps over 10 s (the app in the background) are not counted.

Left open: phone checks of the bonsai photo's remaining read-back frame, the journal's frame rate
with the world off, and the rise for a grown tree.
