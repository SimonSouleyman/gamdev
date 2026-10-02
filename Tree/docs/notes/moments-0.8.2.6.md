# 0.8.2.6: a day worth watching (moments) and the morning that shows the night

Built from specs/journal-drawers-loop.md section 3, G2 and G3 (broken items 57 and 58). The journal
(J1 to J4) and the drawers (D1, D2) are on their own branches; here the diary only changes its one
morning line. Short test run only. Shots and logs: `GameDev/tree-qa/moments-0.8.2.6/`.

## 1. Four moments at set times (G2)

`shared/moments.gd` (Moments, pure): the hours on the clock face, the weather turn, the glow's path.

| moment  | hour  | what shows |
|---------|-------|------------|
| morning | 7:18  | the wish plant opens its flowers (green buds since sunrise, then pink/white heads, brown tufts, violet bells over 2 s, a little brightening; the butterflies and the shimmer come with them). Just after the morning event (the wish scrap, 7:17). The night's result itself shows at the first view (section 2). |
| visitor | 9:30  | the day's visitor: `Visitors.arrive` moved here from the morning (still all due ones at once, as before; in practice one), its line on the scrap for 4.5 s; or the wren on the brush pile (BrushPile.WREN_FROM is 9:30, so it already came then). A day with nobody has no visitor moment and no slow-down. |
| weather | 14:00 | a turn of the mood for about 7.5 s (1 s in, 4 s held, 2.5 s out, real seconds): a breeze (grass and crown wind x2.8), a passing cloud (sun light -50 %, softer shadows), or a short shower (rain particles and the grey sky at 0.7; also the rain sound). Picked from the seed and day; no extra shower on a rainy day. Look only: `weather_today()`, the mushrooms and the sim are untouched. |
| sunset  | 20:00 | the ink ring round the wish place (0.8.2.5), then the dive. The clock holds here anyway, so fast-forward ends at it. |

- `GameState` emits `moment:<name>` when a day step crosses the hour (`Moments.crossed`).
  `main._moment` hands it to `TreeView.moment`.
- **Fast-forward slow-down:** at a moment `TreeView` runs at 1x for `Moments.SLOW_SECONDS` (2 s),
  then eases in again over `FAST_EASE` as at the start. Both the held finger and the sunset
  picture. `main._process` ends a fast-forwarded frame's steps at the step that crossed the
  moment, so the moment is shown on its hour (measured in moments_shot.log: 7.30, 9.50, 14.00 h at
  1.0x, back to speed 2.0 s later at 7.54, 9.74, 14.24 h). The steps are the same fixed steps, so
  the tree is the same (test).

## 2. The morning shows the night (G3)

- **Glow** (`tree/root_glow.gd`, RootGlow): at sunrise `GameState.night_roots` keeps last night's
  new root ids (any night whose roots grew: a run, or one ended at once). As the black of the rise
  lifts (`main._rise`, 0.7 s into the fade) `TreeView.morning_reveal` lays one flat ribbon mesh on
  the ground over them and a warm light runs out along it for 2.4 s: first from the trunk along
  the older roots to where the night began (30 % of the run), then along the night's growth in the
  order it grew, a fainter light left behind; deeper roots fainter, the main root wider, widening
  further out so a far root still reads. Only within the clearing (never over the forest).
  Drawn additive and without a depth test, like the ink ring: at ground level the meadow's grass
  hid it completely on the phone shots; through the grass it reads as light in the soil. It fades
  out within 1.8 to 3.5 m of the camera and for a camera under 1.2 m, so it never fills the
  picture. Cost: one mesh built once a morning (here 131 ribbons), one unshaded material, 2.4 s.
- **Facing the night:** behind the rise's black, the camera turns (only if needed) until the new
  roots' middle lies within 30 degrees of straight ahead beyond the tree
  (`TreeView.face_night_roots`). Without it, the day-5 shot had 0 of 84 segments on screen.
- **Burst:** the dawn burst's new twigs (since `sim.dawn_size`) sparkle again from the reveal, every
  second one instead of every fourth and 1.4x larger for 2.4 s. The burst itself is unchanged
  (sim). Twinkles closer than 1 m to the camera are skipped (a rising camera passing a sapling).
- **Diary:** the "drank" line now names the best thing the night reached
  (`GameState.night_best`, saved; `Diary.morning_line`): "The roots reached the wish." /
  "The roots found rich soil by the clover." (rushes, clover, nettles, comfrey) / "The roots
  found a lost coin." (a find's first three words, so the drawers branch's new finds work too).
  Each fits one phone line in both hands (tested; "Last night ..." did not). A wish beats a rich
  patch, a rich patch beats a find (the spec's order). Same topic "drank", one line, the morning
  after; nothing on a night that reached nothing. An old save with only the drank flag names
  "a rich patch". `Diary.DRANK_LINE` is no longer written.

## 3. Smaller things

- The wish plant's shimmer drew the fog's colour over its whole quad: a pale rectangle in the
  sky behind the stand (also in the 0.8.2.5 shots). Fog is off for it now.
- `run_reached_patch()` (the deposit id) next to `run_reached_deposit()`.

## Short check (2026-10-02)

- Tests: new `tests/test_moments_0826.gd` (the hours; the visitor moment; fast-forward slowing at
  each moment and coming back, held and with the sunset picture; the same tree; the weather turn
  look-only; the flowers opening; the glow after a root night and none without, about 2 s; the
  morning line and its ranking, saved). `test_wish_0825` (the stand's material, the line by topic)
  and `test_game_state` visitors (they come at the day's moment now) updated.
- `tools/autoplay.gd` exit 0.
- New `tools/moments_shot.gd` (phone path: `--rendering-method gl_compatibility`, 450x1000,
  `--phone`): the real sunrise after a night (`morning_glow_a/b`), the weather turns forced
  (`weather_calm/cloud/shower/breeze`), then the day run with the sunset picture from 6:54 with
  each moment as it slows down (`moment_before_morning_buds`, `moment_morning`, `moment_visitor`,
  `moment_weather`, `moment_sunset`). Looked at: the glow reads as a warm line running from the
  trunk out under the grass; the clover stand goes from green buds to pink and white heads; the
  visitor's line shows on the scrap; the cloud visibly darkens the clearing; the shower is a grey
  turn (its streaks hardly show in a still); the breeze only shows moving.
- For Simon on the phone: is the glow bright enough (and not too much), is the camera's turn
  toward the night's roots at sunrise welcome, and does 2 s at each moment feel right at 16x?
