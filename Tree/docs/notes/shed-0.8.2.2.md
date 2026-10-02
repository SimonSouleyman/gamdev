# Shed and bonsai, 0.8.2.2 (Simon's 0.8.2.1 phone notes)

Build notes for the shed and bonsai part of `docs/specs/0.8.md`, "0.8.2.2: Simon's 0.8.2.1 phone
notes" (broken list 38, 39, 43), plus the bugs from his phone session
(`GameDev/tree-qa/session-0.8.2.1/findings.md`, items 3, 6, 7, 8, 9) and three cheap points from
the 0.8.2.1 review. Short test run only (Simon's rule while prototyping): the unit tests,
`tools/autoplay.gd`, and before/after pictures at 450 x 1000 and 450 x 800 (`--phone
--rendering-method gl_compatibility`), in `GameDev/tree-qa/fix-0.8.2.2/shed/before` and `after`.

## The shed's empty floor: a tighter view (item 43, Simon chose "engerer Blick")

- The eye stands 0.4 m further back (by the back wall) and 8 cm toward the window
  (`Shed.EYE`); the workbench stands 0.32 m nearer the eye (`BENCH_Z` 0.4 to 0.08), so it fills
  the picture's foot where the floor was. The door is 4 cm narrower (0.66 m), the window 2 cm
  nearer the door, the things on the bench 3 to 5 cm closer together.
- On a 450 x 1000 screen the field of view the front wall needs drops from 92 to 76 degrees; the
  door, the pinboard and the sill come out the same size or a little larger, the bench's things
  larger. Floor under the bench: 21 % of the height before, about 6 % now (a woven rag rug lies
  there); at 450 x 800 none. `tools/shed_frame.gd` prints these numbers for any change.
- The lantern hangs nearer the eye (it showed half cut at the picture's top edge).
- Test: `test_shed.gd` `test_the_shed_shows_little_floor` (the bench's foot at 90 % of the
  height or lower at 450 x 1000, 720 x 1600 and 450 x 800; the door frame whole); the existing
  view tests keep every thing, label and switch on screen and finger size.

## Simpler bonsai handling (item 38)

- **The can:** one tap waters (it goes over the pot, pours, goes back to its place); nothing is
  picked up. With the shears (or another tool) in hand, a tap on the can waters too and the tool
  stays in hand.
- **The pellets:** a tap on the tin opens its slip ("a spoon of:" N, P, K); a tap on a kind pours
  a spoon of it at once (Simon: "direkt nach Auswahl"). One tap on the tin, one on the kind; more
  kinds are one tap each, taps while it pours queue up. A second tap on the tin closes the slip.
  The kind poured last stays ringed (kept in the save as before).
- **Repotting in three taps:** the trowel (one tap where it lies: the tree comes out), the roots
  (a tap or a swipe on them, or "1 trim the roots" on the slip: trimmed evenly all round, was
  three snips with the shears), a pot (one tap: in it goes, the fresh soil fills by itself; the
  current pot first, marked "as now"). Back while it is out still puts it back as it was.
- **Held tools** are only the shears, the tweezers and the wire (they need a place on the tree);
  they work as in 0.8.1.
- **Back to the bench:** a tap anywhere below the windowsill's front edge on screen goes back
  (a drag there still turns the view). The scrap's first line says so.
- Test: `test_bonsai_touch.gd` (real touches at 1080 x 2400, 720 x 1600, 720 x 1280): watering 1
  tap, pellets 2 (one on the tin), repotting 3, held tools 2; the tap below the sill goes back,
  the drag does not. `test_bonsai.gd` for the flows without touches.

## Cuttings (item 39)

- Every option on the cuttings page has its own ink doodle: a slip of that tree in a small pot,
  its leaves as on the seed bag's pictures (`InkSketch` kinds `cutting_<species>`, all seven).
- A tap on another cutting asks first ("The Linden on the sill?", what happens to the one there,
  "yes, swap them" / "no, keep it"); one stray tap no longer swaps the bonsai (0.8.2.1 review).

## The can and the tin out of the wall; no water below the sill (item 39)

- They stood half inside the window's casing and frame. Both now stand forward on the board,
  in front of `BonsaiTools.WALL_Z` (the casing's face), the can with its spout toward the room
  and a little smaller (0.35 of the model, was 0.38) so the close-up keeps its 50 degree view
  and the front row its finger spacing.
- Each drop of the water lives only as long as its fall from the rose to the soil
  (`BonsaiView.drop_time`): none falls past the pot. The can pours from higher, clear of the
  tallest pot's rim (Simon's video: the spout sank into the pot).

## Repotting's graphics errors (session findings 8)

- The glazed pots were drawn inside out: their walls were wound the wrong way, so the front wall
  was culled and the inside of the back wall, the soil, the pellets and the root ball showed
  through (the "old roots around the new cream pot", the purple dots through its side). Wound
  the right way now; `test_glazed_pots_face_outward`.
- While the tree is out, the pot no longer keeps its full soil with grit and pellets under the
  root ball (two soils at once): only a little old soil low in the pot; no rootbound strands.
- The root ball takes the shape of the pot it goes into.
- The slip lies low over the front row (nothing there is needed while the tree is out), clear of
  the root ball (before, it covered the can and the tin).

## The stalls (session findings 3 and 9)

- 1.7 s of black at the first spoon of pellets, 1.0 to 1.2 s while repotting: the phone built
  the shaders of things drawn for the first time (the pellets, a glazed pot, the root ball and
  its roots). Every such material is now drawn once, tiny and inside the pot, for a few frames
  when the sill is first in view (`BonsaiView._build_warm_up`), so the shaders are built while
  the shed loads. Not measurable on this PC; to watch on the phone.

## Labels from the side (0.8.2.1 review)

- A sill label that would still lie over another label wherever it goes waits (fades) until the
  view turns back (not "put back", not the thing under a PC's pointer). Pictures:
  `labels_side_left/right`.

## Pictures

`after/sill/<size>/shed_menu*.png` (the tighter view, clearer print, the night),
`after/bonsai/<size>/`: `bonsai_mode`, `tool_water_over/use/done`, `tool_fertiliser_slip/use`,
`repot_due/trowel_dig/lifted/trimmed/going_in/done`, `tool_cuttings_page/ask`,
`labels_side_*`, `back_below_sill`.
