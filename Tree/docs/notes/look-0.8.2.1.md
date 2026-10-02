# 0.8.2.1 look: phone and PC look review fixes

From the 0.8.2 review (`tree-qa/review-0.8.2/look`) and the phone test (`tree-qa/phone-0.8.2`).
Checked on the phone path (`--rendering-method gl_compatibility --phone`, 450x1000); before and
after montages in `GameDev/tree-qa/fix-0.8.2.1/` (the "before" from 0cbde7f). Logic bugs from the
same review (diary, settle, save, hold release, end-of-run rebuild) are a separate branch.

## 1. A new game after "start over" (phone)

Nothing of the old tree stayed: `tools/qa_newgame.gd` plays an old game, starts over and
photographs the new one; the new sapling, mesh, brush pile and clearing are its own. What looked
like the old tree:

- **The big orange crown in the morning photos and the morning view** was a forest tree in its
  autumn colours, straight behind the sapling. The album's morning photo framed the species'
  grown size from the clearing's edge, so a day-1 sapling was a speck at the bottom and that tree
  filled the photo. The album now frames in steps (`TreeView.album_stage`: twice the tree's
  height rounded up to 2.5, 5, 10, 20 m, at most the grown size) and the camera stands as far back
  as the step needs (not always the clearing's edge), from a little higher for a small step. The
  flip-book keeps one frame for days and steps back four times in a month.
- **The two red "droplets" at night** were falling autumn leaves (WeatherFx), 13 cm cards right in
  front of the close sapling camera, in full red at night. A crown under 2.5 m drops no leaves,
  leaves are sized to the tree (0.45 to 1 x) and darken with the night (`WeatherFx.night`).
- The "bushy tree" on day 1 is the sapling itself seen up close (unchanged).

Test: `test_look_0821.gd` (an old game then a new one in the same view: the sapling's wood, no
falling leaves, the morning photo shows the sapling at more than a sixth of the frame from a few
metres).

## 2. Into the shed without a white frame (phone)

The black fade now holds 0.12 s after the switch, so the room's first frames (the slow one, and
anything drawn for the first time) are under black. The bonsai is rebuilt only when it changed
(`refresh(false)`; a full rebuild on every visit was most of the 80 ms PC frame). Measured with
`tools/qa_shed_entry.gd`: the switch frame (78 ms first time, 34 ms after) is at fade 1.0 and the
next three frames too; the PC never showed a white frame, so the phone's white is assumed to be
that long first frame. Needs a look on the phone.

## 3. Tree mode at night

A faint moonlit fill on the crown (`NIGHT_FILL` 0.24, phone 0.32, through the crown's `day_fill`)
and the wood (`BARK_NIGHT_FILL` 0.3 as the bark's glow), so the crown is a grey-green shape
against the black forest; the sky, fog and grading stay night. The same glow (0.22) lifts a young
tree's near-black bark by day (fading out by 8 m). Night clouds were dark grey smudges on the lifted
blue sky: the ones that stay at night are thinner and moonlit pale.

## 4. Far view

- The camera frames what is known (roots, known patches, the trunk: `FieldLook.far_content`), not
  the whole field: `RootView._far_fit` solves the distance per point for the margins (sides 6 %,
  22 % under the HUD, 6 % at the bottom) and turns the view (24 headings) so the longer side runs
  up the screen. Pitch 1.2 (was 1.0). No slow auto-turn in the far view; a drag re-fits. Day 16:
  27 m instead of 54 m, the map about 95 % of the width.
- Rock bands are ink strokes on the map (`FieldLook.band_strokes_mesh`: a pale stone-coloured
  ribbon that swells and tapers, with hatching ticks), no longer the black rock walls from above.
- Root ends have tap dots (cream with a dark ring, 1.7 % of the distance, about 14 px).
- The life force reads in whole numbers ("life force 73") as in tree mode.
- Draw calls in the far view: 6 (`test_field_extras` checks the node count and now the fill).

## 5. Rock and soil underground

- Bands: a cellular noise breaks each face into blocks with cracks (darker cavity), the depth steps
  into ledges, the faces are flat-shaded, the piece ends are rounded noses, lighter stone and a
  finer grain. Same vertex count.
- Soil: a faint mottled soil around the camera (a 44 m sphere, unshaded, unfogged, world-space
  texture a little above `SOIL_COLOR`). A sky shader was tried: the phone renderer crushes a sky
  that dark to black. Hidden in the far view.
- Soft veins: stronger (alpha 0.26) with a paler seam along the top.

## 6. Run camera and root tubes

Old roots vanish within 1.5 m of the camera (`bark.gdshader near_fade`, a clean cut via
`near_fade_band` 0), tonight's within 0.6 m, so a run started at the trunk no longer looks out of a
tangle of planks. Roots thicker than 2.8 cm get two more sides (`BranchMeshBuilder.extra_sides`).
A few faint specks remain where the cut roots were (not traced).

## 7. Polaroid with black shed walls

`tools/backup_shot.gd` took its morning photos with the shed scene still shown (the room is only
built to be seen from inside). The game hides the shed outside, so its photos never had it; the
tool now hides it too.

## 8. Juniper and linden cutting

The pads' dark inner cores read as dark balls: smaller (0.78 / 0.6) and a mid green. Juniper
tufts are smaller (0.36 of the pad radius, 8 to 15 cm), so a pad reads as fine scale foliage.

## 9. Autumn forest

The phone's card forest (`forest_impostor.gdshader`) turns with the season: per card kind an
autumn colour (spruce and holly stay green), each tree at its own pace; late autumn browner. The
painted far wood browns too. `SeasonLook.forest_card_materials`.

## 10, 11. Tap targets and shed labels

`Paper.INK_TAP` 88 to 104 px (9 mm on the test phone: 1.55 x scale, about 18 px/mm); the circled
word's ring is at least 58 px tall and wider, so it reads as a button. The run's "end root here"
is a 106 px scrap on two lines (was 56 px); "hold to dive" moved up to make room. Shed labels that
would overlap are pushed apart sideways (`ShedMenu._spread_tags`).

## Minor

Done: hourglass 30 x 42 (was 22 x 32); "grey rectangular pot"; the sunset hint without the widow
word ("swipe down to the roots"); butterflies from 2 m trees on (the white ball by the day-1
sapling); hedgehog with a faint warm fill of its own (not photographed walking).
Not done: linden-cutting camera, day-20 framing, diary line repeats (diary.gd, other branch),
first-night page wording, rush clump, grey strip at 720, bonsai label crowding, the one-tap cutting
swap and the floating watering can.
