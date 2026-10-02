# 0.8.2.4: 16x, the sunset picture, the bonsai lamp, drawers, ending at once (PC thread, 2026-10-02)

Simon (phone test of 0.8.2.3): the fast-forward twice as fast again; a button to run the rest of
the day quickly, a sunset picture in the style of the others; a small lamp over the bonsai so it
can be tended at night; the bench's drawers should open if they show. Plus (coordinator):
specs/0.8.md item 44 (a night ended at once grew too few small roots) and the button's new name.
Short test run only (Simon's rule). Phone-look shots (`--phone`, Compatibility, 450x1000) in
`GameDev/tree-qa/fix-0.8.2.4/` (made with the new `tools/ff_shot.gd`).

## 1. Hold to fast-forward: 16x

- `TreeView.FAST_FORWARD` 8 -> 16, the ease-in (0.5 s) and everything else unchanged. The sim
  still steps in fixed game time, so the tree is the same (tests/test_fast_forward.gd, now at 16x).
- Frame rate: main.gd's per-frame budget for fast-forward steps (`FF_BUDGET_MS`, 12 ms) is kept;
  past it the day runs a little slower instead of the frame rate dropping. 16x at 60 fps is 8 sim
  steps a frame: on the PC (`--phone`, day 12 of a linden) a step averages 0.37 ms, so about 3 ms
  a frame (worst single step 7.6 ms, the morning's burst); about 10 to 15 ms on the phone, i.e.
  right at the budget, so on the phone the day may run a little under 16x late in the month.
- The hourglass: at 16x a game hour is about half a real second, so its sand flickered; it now
  runs down once per two game hours (`Hourglass.HOURS_PER_TURN`), about once a second as at 8x.

## 2. The sunset picture

- A new picture under the shears in the right-hand column (`main._sunset_button`, icon
  `ui/icons/sunset.png`, rendered by `tools/render_icons.gd --only=sunset` like the others: a
  glowing sun half behind a grassy hill with a small tree, ink edge and shadow).
- One tap: `TreeView.run_to_sunset()` runs the day with the fast-forward's machinery (same eased
  speed, same fixed steps, same budget); the speed eases down over the last game hour
  (`SUNSET_EASE_OUT`), and it stops at the sunset hold. It never dives: the night's root run still
  waits for the player's tap or swipe. The picture glows while it runs, the hourglass shows.
- Stopping: a tap anywhere on the view (that tap is only a stop, no boost), the picture again, a
  page or the shed opening, or the shears coming out.
- Offered only by day with at least half a game hour left (`SUNSET_RUN_MIN_HOURS`); hidden at the
  sunset hold, at night, underground and while diving (like the camera).
- Tests (tests/test_ff_0824.gd): runs to the hold and never dives, eases in, tap stops without a
  boost, not offered in the last half hour or at dusk/night, the same tree as a watched day.

## 3. The bonsai lamp

- `Shed._build_bonsai_lamp`: an iron wall bracket above the window, a dark green enamel shade
  (cream inside, brass cap) with a small bulb, hanging about 0.6 m over the sill. A warm spot
  (`bonsai_lamp`, range 1.1 m, 38 degrees, no shadows) aimed at the bonsai; on by night only
  (follows `shed.daylight`), the bulb glows with it. Energy 1.0 (Mobile) / 1.4 (Compatibility,
  which draws dimmer): a warm pool on the bonsai and the sill, the bench outside its reach.
- Shots: `shed_night_bonsai_lamp.png` (the shed at night, the shade above the window, the sill
  lit), `bonsai_night_lamp.png` (bonsai mode at night; `base/ab_bonsai_night_nolamp.png` is the
  same view without it: the tree and sill were near black).

## 4. The drawers

- The workbench model's four drawers (`WoodenTable_03_drawer01..04`; all show on the phone, the
  top right one cut by the screen's edge) open with a tap on their front and close with another
  (`Shed.drawer_at`, `toggle_drawer`): a small tug, then they slide 0.24 m out with a soft
  overshoot (0.38 s), back in with a little bump; the door's creak, pitched up, as the sound.
  The things on the bench keep their taps (a drawer only answers where no thing is hit).
- Inside: a kraft paper liner and an empty `Contents` node on the floor. **Hook:**
  `Shed.drawer_contents("drawer01")` returns that node (drawer frame: x across, z back to front,
  0.37 or 0.78 m wide, 0.44 m deep); what goes in them is being decided with Simon.
- Shot: `shed_drawers_open.png` (top left and middle drawers open).

## 5. Ending a night at once grows its whole budget (specs/0.8.md item 44)

As tested in notes/sim-0.8.2.3-at-once.md, now built (`RootSystem`):
- On a night ended at once only, the small roots may also start from older side roots
  (`AT_ONCE_START_LEVEL` 3 for that night; nights with a main root keep `side_start_level` 1).
- The whole node budget is grown: when no fresh dot is in reach, the rest spreads as short tips
  over the root ends of the last 7 nights (`AT_ONCE_SPREAD_NIGHTS`; `night_starts`, saved, marks
  where each night's growth began; old saves use every root end), in up to 4 rounds
  (`_fill_tips`); the third level fills the same way.
- `tools/strategies.gd` has an `at_once` strategy. Linden seed 14 (`--phone`): at_once grows
  175 + 75 = 250 nodes every night from night 4 (tank 63 and more; was 22 to 61 + 39 to 75) and
  finishes on **day 36** (was: not by day 45); end_early day 36 and dots day 30, unchanged. So
  ending at once is 6 days behind steering (item 44 wants 4 or more).
- Tests: at least 200 of 250 nodes (or all allowed) on every at-once night with a tank of 60 or
  more, `night_starts` saved; the month ended at once finishes between day 34 and 40.

## 6. "End root here" is now "let roots spread"

Simon's pick. The name lives in one place, `Pages.END_ROOT_NAME` / `END_ROOT_LABEL` (the run's
button and the first night's page use it); the design doc, specs/0.8.md and the onboarding check
say the new name, older notes keep the old one.

## Short test run

Tests pass, `tools/autoplay.gd` exits 0, shots looked at (above plus `hud_sunset_icon.png`,
`hud_sunset_running.png`).
