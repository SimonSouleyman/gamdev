# Dot shapes and the brush pile (0.8)

Build notes for specs/0.8.md sections 1 and 4 (branch s08-shapes). The numbers below are the
ones chosen in code, with the reason; tuning.md's "0.8, planned" table is left for the design
thread to update. Shots: `GameDev/tree-qa/shapes-brush/` (before/ and after/).

## 1. Shapes on the nutrient dots

Each nutrient keeps its colour and gets a mark (`ui/nutrient_marks.gd`, drawn in code once and
cached): water a **drop** (round belly, point on top), nitrogen a **leaf** (pointed lens on a
short stalk, tilted), phosphorus a four-point **spark**, potassium a **ring**. Always on, no
switch (spec's choice).

Where they show:
- Underground: the same dot quads and MultiMesh; the kind rides in the instance's custom data
  and the dot shader samples a 256 x 64 atlas (one cell per kind; red the crisp mark, green its
  soft glow; mipmapped). No new node, material or draw call. The collect flash glows in the
  dot's shape too. Dimmed (reached) dots and the wish's warm dots keep their shape.
- HUD pills (the ink dot became the mark), the journal's care page (the marks of the dots
  "Tonight's root" asks for, and the words "blue drop", "green leaf"...), the pages that name the
  dots by colour ("Below the meadow", "Sunset" with the meadow hints, "A sapling") carry a key of
  the four marks under the text, and the bonsai's pellet slip shows N, P and K with their marks.
- Hints and the "spent" page name colour and shape ("the blue drop dots").

| Number | Value | Reason |
|---|---|---|
| Shape full up to (`shape_near`) | 7.5 m | Shapes at full strength where a dot is still 8 px or more on the phone look. |
| Round from (`shape_far`) | 9.5 m | The fog's half distance (fog 4 to 15 m), as tuning.md planned. The smallest dot there is about 6 px wide; beyond it every kind fades to the same round glow. |
| Mark span in the quad (`shape_span`) | 0.72 of the half width | The mark fills most of the glow; a faint round halo stays around it so it still glows. |
| Mark glow | crisp 0.85, soft 0.75, halo 0.12, white core 0.18 | Keeps the colour saturated (a whiter core washed the blue drops out on the phone). |
| Atlas cell | 64 px, 4x supersampled, glow blur 4 px | Enough for a close dot (about 60 px); mipmaps keep far ones smooth. |

Test (`tests/test_nutrient_marks.gd`): the four marks differ by more than 30 % of their area at
the smallest phone size (6 px), at 12 and 32 px; the ring keeps its hole at 6 px. The marks are
drawn in one tone, so a grey screenshot keeps exactly these shapes
(`after/roots_*_grey.png`). Draw calls: one node draws every dot, as before.

## 4. The brush pile with a hedgehog

`shared/brush_pile.gd` (data and rules, saved with the game as "brush"), `tree/brush_pile_view.gd`
(drawing). Cut branches still tip over into the grass; at the next sunrise their segments go onto
the pile. The first morning writes one diary line. Mood only: nothing reads the pile but the view.

Where: on the clearing edge opposite the shed (the shed stands at +z), a little toward the west:
bearing (-0.34, -0.94), 1.6 m inside the edge. The camera's orbit stays within
`max(clearing, 18) - 2.5` m of the tree, so the pile's inner edge (1.6 + 0.65 m inside the edge)
stays 0.25 m outside it and the camera is several metres above it; it can never stand between the
camera and the tree, or near the shed and its camera path. The edge's herbs, flowers and shrubs
keep 1.9 m from the pile's centre (Scenery scales those few to nothing) so it is not buried.

| Number | Value | Range | Reason |
|---|---|---|---|
| Wood for the hedgehog (`HEDGEHOG_WOOD`) | 40 cut segments | 20 to 80 | As planned. A cut a day is about 25 segments (qa_care `--prune=some`: 552 in 20 days), so steady pruning reaches it on the second morning. |
| Days until it moves in (`MOVE_IN_DAYS`) | 2 sunrises | 1 to 4 | As planned: a surprise, not a reward for the cut. |
| How often it shows (`HEDGEHOG_CHANCE`) | half the evenings | 0.3 to 0.7 | As planned. With the two numbers above and a cut a day it came 4 to 6 days after the first cut on seeds 3, 14, 27, 42 (test: within 7). |
| Late autumn (`LATE_CHANCE`, from 25 October) | 0.2 of the evenings | 0.1 to 0.3 | Getting ready to hibernate; from 20 November until spring (Almanac's late-autumn season) never. |
| When (`DUSK_FROM`) | from 0.93 of the daylight (about 19:00) and through the dusk hold | fixed | Dusk only; the state marks the day, so at most once a day. |
| Its walk (`HOG_SECONDS`, speed, reach) | 48 s real time, 0.16 m/s, up to 2.2 m out, three snuffling stops of 2 to 4.5 s | | Long enough to be seen while the sunset hold begins; a slow snuffle, not a run. |
| Wren (`WREN_WOOD`, `WREN_CHANCE`) | 80 segments; 0.35 of the days, between 0.25 and 0.6 of the daylight (about 9:30 to 14:30) | 60 to 120 | The later, smaller visitor; sits on the top of the pile for 36 s and sings three times. |
| Sticks drawn | 4 + one per 3 segments, at most 40 (one merged mesh) | 20 to 60 | The first cut shows at once (12 segments: 8 sticks); full at about 108 segments. |
| Wet sticks (`WET_DARKEN`) | 45 % darker while it rains | | Weather mood. |

Cost: the pile, the hedgehog and the wren are each one mesh with one material and no shadow
(one draw call each while visible; the wren and the hedgehog are hidden unless out). A full pile
is about 3200 triangles, the hedgehog about 1100, the wren 240.

Models: all built in code, no downloads. The hedgehog is an ellipsoid body under about 300 thin
three-sided spines (dark root, pale tip, swept back), a pointed brown snout with a dark nose, bead
eyes, small ears and feet, about 28 cm long. The wren is a 10 cm brown body with a cocked tail and
a fine beak; its song is synthesised (`AmbienceSynth.wren_song`: about five seconds of fast high
notes and trills) and plays from the pile, heard across the clearing. The first hedgehog writes
a diary line with an ink sketch (`InkSketch "hedgehog"`), the first wren a line; both count as
this tree's visitors (`visitor_hedgehog`, `visitor_wren`). A new tree starts a fresh pile; the
bonsai's cuttings never go to it.

## Open

- From the game's camera the pile is small: at the edge it is 25 to 30 m from the camera on the
  far side, about 30 to 40 px wide on the phone look, and the hedgehog only a few pixels at dusk.
  The diary line and sketch carry the moment. A closer spot would put the pile inside the
  camera's orbit. Simon's call on the phone.
- The wren's song is synthesised; a CC0 recording would sound more real (none downloaded).
