# 0.8.1 look: brighter nights, plain dots, framing, noon crown

Broken items 17, 20, 25 and 31 of specs/0.8.md. Simon (phone): "es ist noch sehr dunkel, sowohl
nachts im Baummodus und bei den Wurzeln. Man sieht die Wurzeln nicht." Checked on the phone path
(`--rendering-method gl_compatibility`, `--phone`) at 450x800 and 720x1280; before/after montages
in `GameDev/tree-qa/look-0.8.1/` (shots from `tools/grow_shot.gd`, `hud_shot.gd`, `wish_shot.gd`,
`autoplay.gd`; the "before" ones from the 0.8 merge, f828714).

## 17. Nights readable

**Root run** (`roots/root_view.gd`, `tree/bark.gdshader`):

| | 0.8 | 0.8.1 | why |
|---|---|---|---|
| old roots' own glow (x their colour) | 0.35, 0.3, 0.22 | 0.75, 0.66, 0.5 | the old and fine roots were dark brown on black; now a pale warm wood that reads at normal brightness, still far dimmer than the dots |
| old roots' rim (`glow_rim`, new) | none | 0.3, 0.27, 0.21 | a soft light along the outline: a 2 to 3 px fine root still reads from the overview |
| tonight's root glow / rim | 0.95, 0.72, 0.35 / none | 1.15, 0.85, 0.45 / 0.55, 0.4, 0.2 | stays the brightest wood, so the player's root stands out among the old ones |
| fine roots' smallest radius | 0.02 m | 0.026 m | a little thicker on screen; still clearly finer than a run root |
| soil (background and fog colour) | 0.01, 0.01, 0.02 | 0.045, 0.038, 0.036 | a very dark warm grey instead of black: the field has depth, but it is still night below |
| ambient colour, energy | 0.25, 0.28, 0.4 x 0.6 | 0.34, 0.33, 0.4 x 0.95 | rocks and roots' shaded sides do not sink to black |

The dots and the wish haze are additive and unshaded, so they are untouched by these lifts and
keep standing out (montage `17_roots_night`, `20_wish_dots`).

**Tree mode at night** (`tree/tree_view.gd`, `tree/night_sky.gd`):

| | 0.8 | 0.8.1 | why |
|---|---|---|---|
| moonlight | 0.14 + 0.3 x lit disc | 0.3 + 0.4 x lit disc | a moonless night was nearly black on the phone |
| night exposure (`NIGHT_EXPOSURE_LIFT`) | none | x (1 + 0.45 n) | the eye adapts to the dark: the tree and meadow read |
| night ambient (`NIGHT_AMBIENT_LIFT`) | none | x (1 + 0.6 n) | a cool moonlit fill under the crown |

`n` is the night amount (0 dusk to 1 night). The sky stays a deep blue, colours stay muted
(saturation 0.6 at night as before), so it reads as night, not as day (montage `17_night_tree`).
Not applied inside the shed, whose night the lantern lights.

## 20. Plain coloured dots in play

The dots underground are a plain round glow in their kind's colour again (`roots/dot_glow.gdshader`
without the shape atlas or per-dot kind data; the wish's dots are the same dots, warmer). The HUD
pills and the run's "tonight:" catch show a small coloured ink dot (`TreeView.ink_dot`). The words
name colours only ("steer toward the blue dots", the first night's page). The shapes stay only in
the journal's key (care and meadow-hint pages) and on the bonsai pellet tins
(`NutrientMarks.icon`/`legend`); the atlas and its blur are gone. Tests: `test_nutrient_marks.gd`
now checks the shapes only where they are still shown, and plain dots everywhere in play.

## 25. Framing follows the tree

`TreeView.play_frame` (tree mode only; the album, the live picture and pruning keep their own):

- The tree's foot sits at 0.86 of the screen height and its top at 0.17 (`FRAME_BASE`,
  `FRAME_TOP`): below the HUD's pills, above the hint note. The frame height is the tree's height
  plus 0.8 m of meadow for a young tree (`FRAME_PAD`, gone by 6 m), instead of 0.6.2's plus 3 m
  with the camera at least 3 m out, which left a day-1 sapling as a speck low in the grass.
- The crown's width is measured as the camera sees it: its reach to the left and right of the
  trunk from 24 directions (`crown_across`), with the leaf masses at the tips and the perspective
  of branches reaching toward the camera. The frame's middle moves up to 2.5 m toward a
  one-sided crown (`FRAME_SHIFT_MAX`), and 5 % of the width stays free on each side.
- The camera comes as close as 2.4 m with a lens down to 34 degrees (`FRAME_MIN_DISTANCE`,
  `FRAME_MIN_FOV`), so a seedling is large without the camera standing in the grass. The pinch
  now only moves the camera (a real zoom); the lens follows the frame.
- A grown linden is broader than tall (30 m across at 25 m on this seed): the camera may step back
  to 1 m inside the clearing's edge (it is high up then) and widen to 96 degrees vertical (about
  64 across on 9:16) (`FRAME_EDGE`, `FRAME_MAX_FOV`, were 3 m and 78). The clearing itself is not
  changed (shed and brush pile placement).
- Re-framed when the tree grew 12 % in height or reach (was 35 % in height only), so a growing
  crown does not push past the top during the day.

Measured on screen (living wood, 450x800, from the north, top/bottom/left/right):
day 1 (0.8 m) 0.52 to 0.82, was 0.73 to 0.88; day 5 (4.4 m) 0.18 to 0.84, was 0.31 to 0.89;
day 15 (14.6 m, 15 m crown) 0.26 to 0.77, was 0.23 to 0.72; day 30 (25 m, 30 m crown) 0.31 to 0.71
and 0.09 to 0.87 across, was cut at the left (-0.08). A broad crown on a portrait screen leaves
sky and meadow above and below; that is the price of seeing it whole.

## 31. Noon crown

The blotches were the crown's sprays shadowing each other through the 1024 px phone shadow map:
small hard dark patches inside every leaf mass. The crown no longer receives shadows
(`shadows_disabled` in `hero_crown.gdshader`); it still casts its shadow on the meadow, and the
soft shade inside comes from what was already there: each mass's bent normal (lit and shaded
side), its occlusion (`interior_dark`, AO) and the daylight fill. The phone's green grade loses a
little more red (0.84 to 0.8) so the brighter sunlit side is green, not the cream-lime of the 0.8
review. Montage `31_noon_crown`.

## Cost

No new nodes, passes or draw calls. The dot shader is simpler (no atlas sample); the crown skips
its shadow-map lookups (cheaper); the roots add a few instructions of emission. Tree-mode framing
reads 24 cached sectors per frame (rebuilt only when the tree changes). The 30 fps check on the
Fairphone (item 30) stays with the device test.
