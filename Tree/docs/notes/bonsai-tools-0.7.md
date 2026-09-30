# Bonsai tools on the windowsill (0.7 candidate 3)

Build notes for `docs/specs/0.7-candidates.md` section 3 (decided by Simon on 2026-09-29: real
tools on the sill). Design doc section 16 stays the source of truth for what bonsai care does;
this note only changes how the player reaches it. Written before building (design practice 2).

## Purpose and feel
The shed's pillar is "every menu is a real object". Bonsai mode still had a 12-button paper
panel. Now the tools lie on the sill beside the pot, and caring for the bonsai feels like picking
up a real tool at a real window.

## What lies on the sill
The sill board is deeper toward the room (a worn board wide enough for a row of small tools).

| Object | Where | Tap it | Then |
|---|---|---|---|
| Watering can (Poly Haven) | left of the pot, spout toward the window | pick up | tap tree or soil: it pours |
| Pellet tin "N P K" | right of the pot | pick up; a small paper slip shows N, P, K | tap a letter to choose (remembered, circled); tap the soil: a spoon of that one |
| Trowel (Poly Haven) | front row | pick up | repot day: tap the pot to lift the tree out; other days a note says when it asks; while lifted a tap puts it back in fresh soil |
| Secateurs | front row | pick up | as the old "shears": touch a branch, lift to cut; while repotting, tap the root ball to trim |
| Tweezers | front row | pick up | as the old "pinch": tap a fresh tip |
| Copper wire coil | front row | pick up | as the old "wire": drag a branch, tap a wired one to take it off |
| Sketchbook | front row | opens the style pages | |
| Box of cuttings (Poly Haven "Cheese Box 01") | front row, right end (only with more than one cutting) | opens the cuttings page | |
| Two arrows carved in the board | round the pot's front | turn the pot a quarter that way | |
| Pot | the pot | a sideways drag on it turns it a quarter | |
| Album card | tucked into the window frame | opens the bonsai's album page | |

The can and the tin stand clear of the widest pot (the grey rectangle), so no pot runs into a
tool after repotting.

Picking up: the tool lifts a little and then follows the finger or cursor, held a little below
and beside the pointer so it never hides what it points at. Tap its empty place on the sill, or
the tool itself where it floats off the tree, to put it down (over the tree a tap uses it). A
tool on its way back answers no tap. Tapping another tool swaps. Esc / the back gesture puts a held tool down
first, then closes a page, then goes back to the bench. A small paper note "back to the bench"
sits in the top corner. The status stays as a small handwritten scrap (care day, water, N P K,
pot, and a one-line hint for the tool in hand).

While repotting (lifted), a small slip low on the screen, under the tools, lists the pots; the
secateurs trim the root ball and the trowel (or "fresh soil, and in" on the slip) finishes.

## Labels
- First time: each object carries a small paper label until it has been used once (saved in
  `seen_pages` as `bonsai_tool_<id>`).
- "Clearer print" on: the labels always show.
- First use of each tool still opens its journal page (water, fertiliser, shears, pinch, wire,
  repot).

## Soft failure and edge cases
- Nothing new can harm the bonsai (section 16). A tap with the wrong tool does nothing.
- The trowel lies there always but only works when the bonsai asks (every seventh day).
- Leaving bonsai mode puts the tool down. While the tree is lifted out, back does not leave.
- Small phone: every object has a tap area at least finger size (about 9 mm, 100 canvas pixels
  across in the 720 x 1600 canvas of a 1080 x 2400 phone) in the default close-up, and the tap
  areas do not overlap.
- Orbiting: the objects are real, so they move with the view; their tap areas follow.

## Tuning levers
Tool positions and scale on the sill, the tap radius (`BonsaiView.TOOL_TAP`), the held offset
from the pointer, whether first-time labels show.

## Camera
The default close-up looks a little further down onto the sill than before (pitch 0.45, 0.8 m,
focus low and toward the room), so pot, crown and the row of tools share the tall screen.

## Acceptance
- Every bonsai action of the old paper panel works from the sill objects (tests call each one).
- One tap picks a tool up; a tap on it or its place puts it down (tests).
- The pellet slip chooses N, P or K and the soil tap spoons that one (test).
- The trowel lifts only when repotting is due (test).
- In a 720 x 1600 view the tap areas of all sill objects are on screen, at least finger size and
  apart (test).
- (visual) `tools/shed_shot.gd --bonsai-only` photographs the sill with its tools, each tool
  picked up and in use, repotting with the trowel, the pellet slip, and the phone renderer.
