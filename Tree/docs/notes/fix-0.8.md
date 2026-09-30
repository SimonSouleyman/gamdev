# 0.8 review fixes: look and UI (stream s08-fix)

Written by the build stream s08-fix, 2026-09-30, for the design thread. Look, UI and render code
plus the small logic named below; no pacing or economy number in `tuning.md` changed (the brush
pile's wood, days and chances are untouched). Shots (phone look,
`--phone --rendering-method gl_compatibility`, 450x800 and 720x1280) and before/after montages:
`GameDev/tree-qa/fix-0.8/` (`after/`, `*_before_after.png`; the "before" side is the review's own
shots in `tree-qa/check-0.8/look/`).

## 1. Brush pile and hedgehog (supersedes the "Where" of notes/shapes-brush-0.8.md)

What happened: the game's camera starts north of the tree looking south (TreeView._yaw = PI, the
album's side too), so the pile "opposite the shed" lay behind it; only the south view showed it,
as a 35 px heap at the frame's edge with tall reeds in front, and the hedgehog never left the
reeds. What it means: the pile has to be on the side the camera looks at, which is the shed's side.

| Number | Value | Reason |
|---|---|---|
| `BrushPile.BEARING` | (-0.276, 0.961): 16 degrees west of the shed, on the far edge | The phone's default frame (FRAME_FOV 50 at 9:16) is about 29 degrees wide; the pile's middle sits about 11 degrees off the view's middle and its far end inside the frame with the camera 0.45 of the clearing's radius out. At 16 degrees the full pile (1.5 m each way) keeps 1.4 m from the (hidden) shed's side wall and is never in front of its door. The spec's "away from the shed" is replaced by "beside the shed, in view"; the shed itself is only drawn in the shed scene and the album. |
| `EDGE_INSET` | 1.6 m (unchanged) | Still outside the camera's orbit (test). |
| Run: `RUN_END_BESIDE_TRUNK`, `RUN_MAX`, `RUN_HALF_WIDTH` | ends 2.5 m beside the trunk on the pile's side, at most 24 m, 0.9 m each side | From the pile the camera is 25 to 30 m away, where a 30 cm hedgehog is a few pixels whatever it does. At the tree's foot it is 18 m away, about 9 x 10 px at 450 wide and 15 px at 720: as big as a true size allows. Beside the trunk, not behind it, so the trunk never hides it. |
| On the run | edge herbs, flowers, shrubs and shade plants taken out (as at the pile's spot); the meadow map gets the run as a trodden line (grass short and lying over, no sedge or flowers), tall grass patches kept short | Nothing tall in front of the pile or on the hedgehog's path. A visible trodden line to a brush pile is what a hedgehog run looks like. |
| `HOG_SPEED` | 0.5 m/s (was 0.16) | A hedgehog's brisk trundle (they walk about 0.3 to 0.6 m/s); at 0.16 the walk to the tree would take minutes. |
| The walk | out along the run, a snuffle half way (2 to 3 s), 4 to 6 s at the tree's foot, a few steps aside (2.5 to 4 s), home; ends when the walk is done (at most `HOG_SECONDS` 120) or at nightfall | It reaches the tree 27 s (first clearing) to 45 s (24 m clearing) after it comes out at 0.93 of the daylight, i.e. in the sunset hold. |
| `HOG_SCALE` | 1.1 (about 30 cm) | A big adult, still a real size. |
| Coat | skin (0.3, 0.23, 0.16), spine tips (0.9, 0.85, 0.72) to (0.66, 0.57, 0.45), rim 0.5 | A real hedgehog reads grizzled light brown from a distance; the old dark coat vanished on the dusk meadow. The rim is the low light on the spine tips, no spotlight. |
| Pile | dome up to 0.9 m (was 0.7), sticks a little paler (silver-grey wood lying out) | Reads as a heap from across the clearing. |
| Wren (`WREN_SCALE`) | 1.25 (12.5 cm), rim 0.4 | Still a wren's size; it sits on the pile's top, now in view. |

Tests: `test_brush_pile` checks the new place (in the default view, clear of the shed and its
door, outside the orbit, at the edge) instead of "the far side from the shed"; the draw call,
mood-only and timing tests are unchanged and pass. `grow_shot --hedgehog=` prints where the
hedgehog is on screen.

## 2. One sign per thing: comfrey over a potassium wish

Stones meant shallow rock and a potassium wish. Comfrey (Symphytum officinale) is the gardener's
potash plant: its deep roots bring potassium up, and its leaves are made into potash feed. So a
wish for potassium now grows comfrey above the deposit; stones mean rock only. Changed: 
`Underground.surface_hints` ("comfrey"), `Diary.DRAWINGS`, the wish and reached lines ("reach the
deep soil under the comfrey in the ..."), the ink sketch (`InkSketch "comfrey"`: arching pointed
leaves and a curled spray of hanging bells; the stones sketch is gone), the first-sunset page
("... nettles over phosphorus, stones over rock. Where a wish points to potassium, comfrey
grows."), and the tests. As before, only a wished potassium deposit shows a sign (logic unchanged).

## 3. Nettles and comfrey readable at phone size

Both are now plant shapes built in code, one merged mesh and one material per patch (was 14
separate cones per nettle patch):
- Nettles: 40 stems 0.6 to 1.1 m (real stands are 0.5 to 1.5 m, dense), six pairs of pointed leaves 6 to 16 cm,
  drooping and crossing up each stem, hanging tassels; leaf colour (0.15, 0.28, 0.09), a deep
  green a little warmer than the meadow's. They read as a dark, spiky, taller block.
- Comfrey: 8 clumps about 0.7 m, 7 to 10 broad arching basal leaves 34 to 48 cm, 2 or 3 leafy
  flowering stems with curled sprays of 8 violet bells (0.46, 0.2, 0.5), some paler: broad mounds
  with violet dots, the common purple comfrey's colours.

## 4. and 5. The pellet slip

- What happened with "pellets K: 0.46 -> 0.46": the first tap on the soil spooned the tin's kind
  and the tin was busy pouring for about two seconds; the slip hid while it poured (so the tool's
  tap on "K" found no button) and the next tap on the soil was dropped silently. Fixed: the slip
  stays while the tin pours (a choice then is for the next spoon), and a tap on the soil while
  it pours gives one more spoon when the tin is back up (`BonsaiView._spoon_waiting`).
- The ring for the chosen kind never showed: `choose_pellets` restyled the button's empty
  styleboxes, not the ring. Now each kind is one ink ring round its mark, letter and word; the
  kind the tin gives (remembered, or before any choice what the soil lacks most) is ringed in
  red ink, 4 px; the slip, the tin's paper label ("pellets K") and the ring follow it every
  frame, so they are right when the slip opens and after a restart.
- Layout: a narrow slip (three rows, one ring each, words in plain ink, the colour only in the
  mark), at the screen's edge below the notes, on the side that covers least of the crown's screen
  rectangle, the tin and the album card (`BonsaiHud.pellet_slip_spot`). Rows are INK_TAP (88)
  tall. Hint: "Tap the soil for the marked pellets; the slip changes the kind."

## 6. The noon crown on the phone

What happened: the sunlit south side went pale cream-lime at noon on the phone renderer. What it
means: the ACES tone mapper whitens a bright, lifted green. On the phone only:
`CROWN_GRADE_PHONE` (0.93, 0.97, 1.0) -> (0.84, 0.92, 0.94), `CROWN_SATURATION_PHONE` 0.76 -> 0.8,
`CROWN_SUN_LIFT_PHONE` 0.2 -> 0.06 (0 and a (0.8, 0.89, 0.92) grade went too dark inside), and a new `sheen` uniform (the leaves' specular) 0.18 -> 0.08
(`CROWN_SHEEN_PHONE`). The lit side stays a natural lit green; the shaded side is still lifted by
the day fill (unchanged). The PC look is unchanged.

## Minor

- "take back the game before": the slip always carries a line ("A copy was loaded; the game
  before it is kept.") when the board is reopened.
- The live picture's open note: font 27 (was 24, the notes' size), a scrap in the free cork above
  the backup notes, which stay in place (only their note slip waits while it is open).
- Journal ribbons: all as wide as the longest word, their ends in one line at the page's edge (the
  page gives way by the ribbon width), the chosen one 10 px further out; font 24 (was 26).
- One word for leaving a page: "close" (the journal and the album say it); the pinboard's "back"
  is now "close". "back to the bench" stays: it leaves a mode, not a page.
- The shared Polaroid is cut out along the card's edge (no album page around it).
- The night's "tonight:" line shows each kind's mark before its amount.
- Bonsai hunger: N pales toward (1.28, 1.18, 0.42) at 0.8 (was (1.2, 1.12, 0.45) at 0.65), its
  shade core 0.75 (was 0.6); K browns 55 % of the tips (was 45 %) at 0.9 toward (1.08, 0.66, 0.32).
- Live picture: `Image.fix_alpha_edges()` before the layers are resized (the pale horizon seam was
  the clear colour mixed into the edge by the cubic filter); clover and flower clumps keep
  5.5 m further from the camera than the grass (`FLOWER_CLEAR`), so none is cut by the bottom edge.
- App icon: the linden of day 22 (was 16; since the 0.8 balance changes the day-16 crown of seed
  2026 is sparse, and the day-22 one is the fullest of 16, 19 and 22 at 48 px), and the small gaps
  inside the crown are filled with the crown's own green a shade darker (two box blurs of radius 4
  at the 432 px layer, from 0.5 coverage), behind the leaves; the outline and the safe circle rule
  are unchanged (tree scale 0.612, crown inside the circle).
