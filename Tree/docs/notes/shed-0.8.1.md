# Shed and bonsai, 0.8.1 (items 18, 26, 28, 32)

Build notes for the 0.8.1 shed items of `docs/specs/0.8.md` (broken list 18, 26, 28, 32, checked
with 1 to 16 and the 0.7 list's C1, C2, D5). Simon (phone playtest): "die Bedienung von dem
Bonsai [ist] noch schwierig mit den einzelnen Gegenständen. Das [bitte] ausführlich prüfen."
Screens: the 6.4" 20:9 phone (1080 x 2400, and 720 x 1600 at a lower density) draws a
720 x 1600 canvas; the reference 720 x 1280 is a 450 x 800 window. On the 6.4" phone one canvas
pixel is about 0.094 mm, so 9 mm (48 dp) is about 96 canvas pixels.
Pictures: `GameDev/tree-qa/shed-0.8.1/item18_tools_before_after.png`, `item26_...`, `item28_...`,
`item32_...` (phone look, `--phone --rendering-method gl_compatibility`, from `tools/shed_shot.gd`).

## 18. Bonsai tool handling on the phone

**What happened (0.8 code, read and measured):**
- A finger that moved more than 10 px (about 1 mm) after touching down turned the view instead of
  tapping (12 px ended a tap). On a phone most taps wobble that much: taps were lost as tiny turns.
- A picked-up tool lifted where it lay; at the next touch it jumped to 70 px right and 40 px below
  the finger, under a right hand's finger and palm, and the big can then covered the crown.
- A tap acted at once on release: no sign of what it would water, pinch or cut beforehand
  (only the shears showed a mark). A tweezers tap had to land within 44 px of a tip, a
  fingertip's own radius.
- The front row (six things, 5 cm apart) did not fit the 20:9 screen: the trowel and the box of
  cuttings were cut at the edges.
- Nothing on the sill showed where the tool in hand went back.

**What changed:**
- Picking (`BonsaiView.pick_object`, `BonsaiTools.pick`): a thing answers inside its outline on
  screen (its box's hull, grown by 10 px) or within `TOOL_TAP` (52 px) of its mark; the nearest
  wins. A touch on the pot itself is the pot's (other things only right at their marks). With a
  tool in hand over something it works on, other things answer only within 0.6 x `TOOL_TAP`
  (31 px), so a touch on the soil next to the can waters. The tool's own place is a larger
  put-down target (1.4 x `TOOL_TAP`, 73 px), still nearest-wins against its neighbours.
- `TAP_SLOP` 28 px (about 2.6 mm) for every tap and for the start of a turn of the view (was 10
  and 12); `PICK_RADIUS` 56 px for tips and branches (was 44).
- Aim, then act: pressing with a tool over its target shows the aim and nothing happens; the act
  comes when the finger lifts, where the aim is then (sliding moves it, off the tree nothing).
  The soil gets a warm ring (can, tin, trowel; the root ball while repotting), a fresh tip a ring
  (tweezers), a branch is traced (wire), the shears keep their cut mark and outline. All drawn
  over everything, so neither the finger nor the tool hides them. On a PC the pointer resting
  shows the same.
- Holding (`BonsaiTools.HOLD`, `hold_pose`): the tool's working end (the can's rose, the tin's
  mouth, the blade tips, the trowel's blade, the wire's end) sits on the finger; its body reaches
  up and a little right (`HOLD_DIR`), clear of finger and palm. It is drawn at 0.55 of its distance
  from the eye and as much smaller (`HOLD_NEARER`): the same on screen, but in front of the crown.
  The can is 0.85 of its size in the hand.
- In hand, always clear: the status scrap's line starts "In hand: the watering can." and says
  what to touch; the tool's place on the sill shows a dashed ring and a "put back" label.
- Sill layout: the sketchbook moved off the front row into the window frame (opposite the album
  card), so the five things of the front row lie 6 cm apart (was 5). The can turned its spout
  toward the window (yaw -0.75). The close-up widens its field of view on a narrow screen until
  every thing on the sill is whole (`BonsaiView.base_fov`: 53 degrees at 720 x 1280, 64 on the
  phone; was 50 on both).
- A second finger cancels a press (no stray cut or pour after a pinch zoom).

**Test** (`tests/test_bonsai_touch.gd`): real touch events (mouse emulated from the touch, then
the touch, as the phone sends them) pushed in screen pixels into a 1080 x 2400, a 720 x 1600 and a
720 x 1280 screen. For each: every thing whole on screen, found by a tap, at least 9 mm across
(9.6 mm), at least 104 px apart, not under the paper; touches on the soil and the pot never find
a thing with any tool. Then each tool is picked up, aimed (the highlight is on, nothing has
happened), used on release and put down: can, pellet tin (and K on its slip), shears, tweezers
(touching 18 px beside the tip), wire (drag, then a tap frees it), trowel (not yet; then lift),
the shears swapped in to trim the root ball, a pot on the slip, the trowel puts it back; the
arrows (also with the can in hand), the sketchbook, the album card and the cuttings; every other
tool swaps in with one tap and nothing is watered; a wobbling (18 px) tap still picks up. Checks
on the tool in hand: its working end within 6 px of the finger, its top at least 54 px (a
fingertip) above the touch. Taps per action, all three screens: water 2, pellets 2, shears 2,
tweezers 2, wire 2 (pick up, one drag), trowel 2, turn 1, put down 1 (C1: at most 2).
The test found two real faults on the way, both fixed: the turned can's outline covered the left
of the pot (touches there picked up the can), and the sketchbook's new place is where a "wall"
tap went.

## 26. The shed on the phone's screen

**What happened:** the shed's camera kept a 70 degree vertical view from 0.7 m behind the bench;
on 20:9 (450 x 1000 and 720 x 1600) the view was 35 degrees wide and cut the sill with the bonsai
and the pinboard at the sides (pictures "before").

**What changed:** `fit_view()` widens the field of view until `must_see()` (the bonsai's pot and
crown with its label, the pinboard's corners and its label, the bench's things) is inside the
picture with a 5 % margin, never under 62 degrees, and tilts the eye (at (0, 1.62, -0.9)) so the
picture's top edge meets the front wall just under the rafters (`TOP_Y`): the ceiling is a thin
strip and the rest of the tall picture goes to the room and the bench. The span from the sill to
the pinboard sets the size of everything on the wall (the screen's width must hold it), so the
door narrowed from 0.8 to 0.7 m and the window and the pinboard moved 5 cm toward it; the
pinboard is a little smaller (scale 0.66 from 0.72). The middle rafter is gone (it ran from the
eye to the door and showed as a long lit wedge at the picture's top); the lantern hangs over the
bench with a smaller, warmer glass and still lights the bench at night. Every tap circle stays at
least 100 px on the phone (test `test_shed.gd: test_shed_view_fits_the_phone`, 720 x 1600,
720 x 1280, 1280 x 720).

The pinboard's switches now spread over the cork down to the backup notes (`_layout_notes`: row
pitch 128 to 230 px), keeping two rows free above the backup notes for their note slip or the
live picture's open note; on the 720 x 1280 screen nothing moves. Shots show the board plain, in
clearer print, with the backup's slip and with the live picture's note open.

## 28. The pot's page into the journal

The flower pot with the seedling left the bench (`Shed.ITEMS`). The tree's page (the day, its
height and segments, the day's wish in red ink, the visitors, the ink sketch of the tree as it
stands) is the journal's first ribbon, "tree", and the book opens there the first time, then
where it was left. It is always there (it is drawn from the game state, so a new tree has its own
page). "While you were away" still opens as its own torn page on return. Test
`test_shed.gd: test_tree_page_is_the_journals_first_page`.

## 32. The juniper's pads and trunk

**What happened:** each pad was one flattened dome (half height 0.48 of its radius) of cards on
its top and rim, with a dark core inside; from below or the side the core read as a dark plate
and the pad as a disc. The drawn trunk followed the pipe model's radius, a quarter of the tree's
height (day 14: radius 0.154 for 2.7 units).

**What changed:** pads are rounder (half height 0.62) and made of two or three overlapping lobes
at slightly different heights, needled all round (underside too); the core is smaller, rounder
and less dark. The drawn trunk is capped at height / 21 (`TRUNK_SLENDER`, with the builder's
radius scale a foot diameter of about a seventh of the height; classic 1:6 to 1:8), tapering as
before; branches stay under 0.62 of it. Only the look: the simulation's graph is untouched.

## Left as it was
- Busy tools still ignore taps while they move (watering, turning), except the extra spoon of
  pellets; a moving tool is short (under 2 s).
- The juniper's youngest apex pad can still sit small and alone on a young tree (growth, not look).
