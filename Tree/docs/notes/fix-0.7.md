# 0.7 review fixes: look and UI (stream s07-fix)

Written by the build stream s07-fix, 2026-09-30, for the design thread. Look, UI and render
code only; no pacing or economy number in `tuning.md` changed. Shots (phone look,
`--phone --rendering-method gl_compatibility`, 450x800 and 720x1280) and before/after montages:
`GameDev/tree-qa/fix-0.7/` (`before/`, `after/`, `*_before_after.png`).

## 1. Marked branches (supersedes the look rows of notes/marks-0.7.md)

The sign now reads as a small branch going dry, not as shade or thirst:
- **No droop.** Hanging was thirst's cue; `HeroCrown.MARK_HANG` 0.6 -> 0.
- **Sparse and small.** `MARK_THIN` 0.45 -> 0.5 (summer), `MARK_THIN_AUTUMN` 0.7 -> 0.85,
  `MARK_SMALL` 0.2 -> 0.3. `MARK_MIN_SHARE` 0.75 -> 1.0: the whole leaf mass the marked twig sits
  in shows the sign (a 3-segment twig inside a mass was 2 or 3 sprays, lost at phone size; the
  whole mass is a clump about 2 m across on a grown linden, still one small branch's worth).
  The sprays spread 0.9 of the mass radius around the twig (was 0.6), so it reads as a thin
  branch rather than a tight ball.
- **Dull, dry colour in the shader** (`hero_crown.gdshader`, strength carried in the fraction of
  INSTANCE_CUSTOM.x): luminance times (1.4, 1.2, 0.78) at 90 %: a washed-out yellow-grey going
  khaki-brown. Not the bright yellow-green of nitrogen (1.3, 1.36, 0.42), not thirst's grey-green
  (1.0, 1.02, 0.78), not potassium's scorched edges. The old per-instance multiply
  (1.45, 1.2, 1.8, pale bluish) is gone.
- **Out of the crown's inner shade:** a marked spray takes 55 % less of the crown's occlusion,
  so its few dry leaves do not sink into the dark inside (the reason the summer sign looked like
  ordinary inner shade).
- **Autumn:** the marked twig goes bare first (up to 75 % of its remaining sprays drop with the
  turning), and what stays is dry brown (luminance times 0.95, 0.72, 0.5), not the season's
  yellow or orange.
- **Grey bark:** `MARK_BARK` 0.7 -> 0.9.
- **Where:** `GrowthSim.MARK_OUT_MIN` 0.4: among the candidates, tips at least 0.4 crown radii
  out from the trunk (horizontally) are marked first, then the soonest, then the furthest out.
  A forecast only, as before: which tips are marked changes, the dieback and growth do not
  (tests/test_marks.gd unchanged and passing).

Still natural signs only: no icon, outline or count. Judged on the shots: the dry clump is easy
to find when enlarged and visible at a glance at 450x800 when it faces the camera; a marked twig
behind the trunk is still hidden until you orbit (on purpose: the eye learns to look).

## 2. Wish glow

- Far off: the haze grows up to 3x (`far_grow`) by 45 m, keeps 90 % of its strength beyond the
  fog edge (`far_floor` 0.22 -> 0.9), held to 45 m and gone by 60 m (were 30 / 38), and ignores the
  depth buffer (`depth_test_disabled`): old roots and stones no longer hide it; being soft and
  additive, drawn over them it reads as light behind them.
- The reach: the haze swells to 1.3x (was 1.7x) over 1.2 s and fades over 3.5 s; the deposit's
  dots lose their warmth over 4 s instead of snapping back to their own colour.
- All dots fade out within 1-3 m of the camera (`dot_glow.gdshader near_hide` 1.0): right at the
  camera they filled the view as big neon blobs after the tip reached the deposit.

## 3.-5. Paper

- Ink ovals round two-line words ("back to / the bench", "N / leaves" and the other pellets) now
  ring both lines (`Paper._draw_ring` measures the lines; two-line buttons get 4 px more padding
  top and bottom so the ring fits in clearer print too).
- Repot pot buttons are full tap size (Paper.INK_TAP, 88), font 21 -> 23; the slip lies on the
  bench between the pot and the front row of tools, its lower edge 20 px above their tap points
  (`BonsaiHud.REPOT_SLIP_ABOVE_TOOLS`), so the shears and trowel stay free.
- One name per tool: **shears** everywhere (the tree view, the journal and the bonsai album
  already said shears); the sill label and the repot hint said secateurs.

## 6. Crown colour on the phone renderer

The compatibility renderer showed the crown lime at noon and the autumn orange loud. On that
renderer only (`TreeView._compat`): the summer green is multiplied by (0.93, 0.97, 1.0)
(`CROWN_GRADE_PHONE`, a little less red), the crown's saturation is 0.76 (`CROWN_SATURATION_PHONE`,
applies to autumn too), the sunlit lift 0.3 -> 0.2 (`CROWN_SUN_LIFT_PHONE`), and the daylight
fill 0.09 -> 0.13 (`DAY_FILL_PHONE`) so the calmer green does not sink into black blotches.
A first try with (0.84, 0.96, 1.0) and saturation 0.8 went blue-green and blotchy inside.
The PC look is unchanged.

## Minor

- The bonsai scrap's said line ("Not yet: ...") ends with the next thing done (a tool picked,
  used, or a sill thing tapped), and the tool line is hidden while a page lies over the scrap.
- The sun arc's words sit inside the arc beside its ends ("sunset" hid behind the book at 720).
- Back while the tree is out of its pot puts it back into its own pot unchanged (it still asks
  to be repotted); a second back leaves. `BonsaiView.repot_cancel`, test
  `test_back_puts_a_lifted_tree_back`.
- Diary: the reached wish's ink sketch sits under its line; "A wish: today, find ..." in lower
  case after the colon.
- Sill tools 5 cm apart (were 4.6 cm, just under 9 mm on a 720-wide phone); the cheese box's lid,
  leaning edge-on at the screen's right rim, read as a stray unlabelled stick and is left off.
