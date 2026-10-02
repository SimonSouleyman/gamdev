# 0.8.2 field extras: the far view, rock bands and soft veins, the first-time check

Written by the build stream s082-field, 2026-10-02, for the design thread to merge into
`tuning.md` (not edited here). Spec: `docs/specs/root-field-extras.md` items 1, 2 and 5 (the 0.8.3
items, the fungal network and the root drawing, are not built). Headless Godot 4.7.2; short run
only (Simon's prototyping rule): unit tests, `tools/autoplay.gd`, phone-look shots in
`GameDev/tree-qa/field-0.8.2/`, one strategies run (linden seed 14: dots, wish, end_early), and
`tools/qa_bands.gd` (soil generation only, 18 seeds).

## What changed and why

| Variable | Value | Reason |
|---|---|---|
| Far view: opens (`RootView.open_far_view`) | pinch out (two fingers, trackpad magnify or the wheel) past today's 30 m limit (`NORMAL_MAX_DISTANCE`) while choosing the start, on a quiet night, or after the run has settled; pinch in (ratio 0.8) or back closes it; never during the run or while tonight's root settles | Spec 1, broken 1. |
| Far camera (`FAR_PITCH`, `FAR_HFOV`, `FAR_FIELD_MARGIN`) | high three-quarter view, pitch 1.0 rad, the field's 30 m radius x 1.08 across 62 degrees of the screen's width (portrait widens the vertical angle, to 106 degrees at 450x1000), distance from that: 53.9 m on the phone's aspect | [PLACEHOLDER] In the spec's 40 to 60 m range. With the normal 62-degree vertical angle a portrait screen needs 118 m to fit the field's width; widening the angle keeps the camera at 54 m. The pull-back and the way down ease at 1.1/s (`CAM_RATE_FAR`, normal 3.0) and the angle eases over 1.6 s. |
| Recent nights brighter (`FieldLook.recent_nights`) | 7 (the newest among them, brightest) | [PLACEHOLDER] Spec value; range 5 to 10. Colours: older 0.62/0.5/0.36, recent 1.0/0.8/0.5, newest 1.5/1.12/0.62 (glow picks it up). Side and fine roots are left out. |
| A patch counts as known (`RootView.known_reach`, `Underground.known_patches`) | a root node within 8 m of the patch's edge, or the patch's own meadow sign over it (topsoil, under the clearing less 2.5 m) | [PLACEHOLDER] Range 5 to 12. The run's dots fade between 4 and 15 m from the camera, which sits about 2.4 m behind the tip, so 8 m from the root is about where a patch was clearly seen. In the wider field every topsoil patch has a meadow sign, but a far patch's sign is moved to the clearing's edge and only tells the direction: counting those would show every patch and exploring would no longer matter. |
| Far view drawing | one mesh of main-root lines (three-sided tubes, radius 0.0045 x the camera distance, about 0.24 m, a node every 2nd node; `Budgets.FAR_VIEW_SEGMENTS` 6000), one MultiMesh of patch clouds (kind colour, alpha by how full), the band mesh in flat dark, the vein mesh, the wish haze (its fade-out moved past 150 m), a trunk mark; the dots, boulders, finds and near roots hide; fog 0.004 and a lighter soil (0.105/0.09/0.08) so the dark bands read | Broken 1. gl_compatibility, 450x1000, linden s14 day 16: far view 5 draw calls, 7.6k primitives; the normal overview 32 draw calls, 73k primitives. |
| Tap in the far view | the nearest end of a main root within 70 px starts tonight's root there; the camera flies down with the run's own follow | Spec 1. |
| Bands (`Underground.band_count`, `band_length`, `band_thick`, `band_gap`, `band_bottom`) | 3 bands; centre line 8 to 14 m; 1 to 2 m thick; a gap with 2.0 m clear between the two rounded faces, off the straight line (1.2 m + half the gap away from it) and 1 m or more from the band's ends; from the meadow down to 3.5 to 4.5 m | [PLACEHOLDER] Spec 6 to 15 m: 8 m is the shortest that leaves room for a gap away from the straight line and from the ends. 4 m deep: below the rich patches (0.5 to 2 m), so diving under is a third, dearer way (depth cost). |
| Band placement | each across the straight line from the trunk to a different far patch (shuffled per seed), 2 to 4 m short of its edge, at least 9 m out, curved (radius 10 to 22 m, either way), clear of every rich patch by its radius + 0.8 m and of the other bands by 4 m (no pockets); a band is dropped when the bands together would block more than 40 % of the far patches' straight lines (`band_block_share`) | Broken 3: a band in the middle ring also shadows the far patch behind it; without the cap seeds 3 and 27 had 5 of 9 blocked. |
| Soft veins (`vein_count`, `vein_length`, `vein_width`, `vein_cost`, `vein_start_min`) | 2 veins, 8 to 14 m, 1 to 1.5 m wide, a metre inside costs 0.6x; each points at a far patch whose straight line is clear (axis within 26 degrees of the trunk-to-patch line, ending 0.6 to 1.4 m short of its edge, at its depth), starting 7 m or more from the trunk, clear of bands by 1 m and of each other by 4 m | [PLACEHOLDER] Spec 8 to 20 m: veins of 15 to 16 m starting 4 to 6 m out made a vein the cheaper way to 5 of 9 far patches on seeds 3 and 27 (broken 4); shorter ones that start in the middle ring's way help their own patch and seldom another. The spec's "about 2 veins" and "a third of the far patches" do not fit 9 far patches; 2 veins (22 %) kept. |
| Soil versions (`Underground.bands_version`, saved as `"bands"`) | 1 for a new game in the wider field; 0 for layouts 1 and 2 and for every layout-3 save from before 0.8.2 (no `"bands"` key) | Old saves keep their soil dot for dot. Bands and veins come from their own seeded generators after the soil is made; the dots that would lie in a band move to its face (ids unchanged), potassium gathers along both faces (a dot every 1.4 m of band, 0.4 to 3.5 m deep, as by a rock), finds move out too. |
| Walls (`RootSystem._move`, `Underground.band_push`) | a tip that meets a band slides along its face, or is pushed under it when that is shorter, as at a boulder; fine and side roots never grow into rock (boulders included: `_colonize` drops a node in rock and its children) | Broken 3. The side-root fan could reach into a band; the same now holds for boulders, which side roots could pass before. |
| Magnetism (`fresh_ahead(..., clear)`) | pulls only toward a fresh deposit whose straight line from the tip passes no rock or band (0.08 m margin) | Broken 3. |
| Vein cost (`RootSystem.cost_per_metre`, `soil`) | x `soil.soil_factor(p)`; the run's pace follows, so a root in a vein grows faster for the same tank | Spec 2. |
| Meadow stones over bands | a stone cluster every 1.5 m of band under the clearing (within its edge less 1 m) | Spec 2 ("bands under the clearing show a line of stones"). Most bands lie at 10 to 19 m, so a line shows where the band is in the clearing's 18 m. |

## Results

- `tools/qa_bands.gd`, seeds 1 to 16 and 27, 42 (generation only): 3 bands on 17 of 18 seeds (seed 7:
  2), 2 veins on all; straight lines blocked 3 or 4 of 9 far patches (33 to 44 %); every far patch
  has two routes (`FieldRoutes.routes`: a path through the topsoil at 1 m, 0.25 m clear of rock,
  and a second one at least 1.5 m from the first everywhere between trunk and patch).
- A vein is the cheaper way (than a clear straight line) to 1 to 3 of 9 far patches on seeds
  3/14/27.
- Strategies, linden seed 14 (R1 = roots-0.8.2 without bands): dots finished day 30 (R1 30), wish
  30 (29), end_early 38 (37). Calm nights 21 to 45 s, one wish night 47 s (R1: 5 % of nights 45 to
  52 s); roots 6 to 27 m, median 17. The bands do not break the month on this run.
- The far view on the phone's aspect: 15 of 24 patches known on day 16 (linden s14), the far
  ring's untouched patches hidden. Slowest strategies frame 9 ms (PC).

Shots (`--phone --rendering-method gl_compatibility`, 450x1000, linden s14 day 16,
`tools/grow_shot.gd -- --roots --far`): `far_view.png` (with its one-line hint), `far_view_bare.png`,
`field_overview.png` (the normal overview with a band), `field_band.png`, `field_vein.png`.
What they show: the far view reads as a map, warm root lines (the last nights brighter) around
the trunk, known patches as soft coloured clouds, the three bands as dark curved strokes with
their gaps, the veins as faint lighter strokes. Up close a vein reads as a translucent crumbly tube
pointing at its patch. A band up close reads as a rock wall, but as one lumpy slab rather than
piled stones (open point).

## First-time player check (spec 5, simulated)

Played as a new player would, from the code's flow (`main.gd`, `ui/pages.gd`, `RootView` hints),
fresh install, linden. What happened:

| New thing | When it is first taught | When it first matters | Lines | Verdict |
|---|---|---|---|---|
| Hold to fast-forward | "sapling" page, last line, with boost, the sun by hour, the pills, walk and zoom | the first day's waiting, right after that page | 1 (in a page of 4 topics) | One line, at the right moment, but buried (the 2026-09-29 gap 1 is still open). |
| Side roots | "first night" page: "End root here spends the rest on fine roots", before the first root grows | the first night ended early | 1, too early | Fixed (cheap): the settle line now says "The leftover grows side roots." when the leftover grew some. The first-night page still mentions it early (left for the page split). |
| The wider field | nowhere; far patches show their sign at the clearing's edge, the diary's far wish said "Wish: the clover in the north." as for a near one | the first far wish (from about day 5) | 0 | Fixed (cheap): a far wish's diary line says "far" ("Wish: the clover, far in the north."; "Still: ..., far in ..."), which tells it may take more than one night. |
| The far view | new: "Pinch out to see the whole field." under the pick line, from the 4th pick night until it was opened once; inside it, "Tap a root's end to start there. Pinch in to come back." | when the roots reach past the overview's frame (about night 4) | 1 | Fine. |
| Rock bands | new: "Rock: find its gap, go round, or dive under." for 5 s the first time a tip meets a band | the first band met (middle ring, usually week 2) | 1 | Fine; the meadow's stones (first_sunset page: "stones rock") also mark a band under the clearing. |
| Soft veins | new: "Soft soil: the root grows cheaper here." the first time a tip is in a vein | the first vein entered | 1 | Fine. |

Broken 9 holds for the four 0.8.2 things (far view, bands, veins, side roots): one line each, at
the moment. The pages that teach several things at once ("first night", "sapling", the 0.6 gaps)
are unchanged and stay the next step (onboarding-check.md, gaps 1 and 2).

## Tests

New `tests/test_field_extras.gd` (registered in `tests/run_tests.gd`): broken 1 (the far view
opens only outside the run, not while settling, after the run and on a quiet night; a few merged
meshes, fewer draw nodes than the normal view, within the segment budget; roots by night; a far
tap starts the run at an old tip), broken 2 (known patches only: hidden until a root came within
reach, one cloud per known patch, dots hidden), broken 3 (two routes for every far patch on seeds
3/14/27, a third to under half blocked, band sizes and gap, a tip driven at each band never
enters it, the magnetism never pulls through rock, old saves keep their soil dot for dot), broken
4 (vein cost and shape, it points at its patch, a vein is the cheaper way to at most 40 % of far
patches), broken 9 (one short line each, the far line not before it matters, the band line once).
Broken 5 (the month): the strategies run above; `test_side_roots`' calm night (12 to 18 m, 20 to
44 s) now plays on a soil with bands and passes.

Changed: `test_underground: surface hints match below` accepts a band under a line of stones;
`test_journal` also measures the far wish lines; `test_side_roots` preloads `Strategies`.
All 10157 checks pass; `tools/autoplay.gd` exits 0.

## For tuning.md (rows the design thread writes in, [PLACEHOLDER] until felt on the phone)

- Far view underground: built. Camera 53.9 m on 450x1000 (62 degrees across the screen, pitch
  1.0 rad); last 7 nights brighter; known = within 8 m of a root or under the patch's own sign in
  the clearing. Levers: `RootView.FAR_HFOV`, `FAR_PITCH`, `known_reach`, `FieldLook.recent_nights`.
- Rock bands and soft veins: built. 3 bands, 8 to 14 m, 1 to 2 m thick, gap 2.0 m clear, 3.5 to
  4.5 m deep, at most 40 % of far straight lines blocked (measured 33 to 44 %); 2 veins, 8 to 14 m
  (spec 8 to 20), 1 to 1.5 m wide, 0.6x, from 7 m out. New saves only (`bands` 1).
- Days to finish with bands (linden s14): dots 30, wish 30, end_early 38.

## Open points

- A band up close is one lumpy slab; Simon may want it to read as piled boulders (the far view's
  dark strokes read well).
- The far view's portrait camera uses a 106-degree vertical angle; on a phone the field fills the
  middle half of the screen and the top and bottom are empty soil.
- The normal pick overview still shows dots of far patches from 24 m (its fog follows the camera
  distance, as before 0.8.2), so "unknown" patches can be glimpsed there, though not in the far view.
- Not measured on the Fairphone: the far view's frame time (5 draw calls on the PC's
  gl_compatibility path).
- Spec's "a third of the far patches with a vein" is 2 of 9 with 2 veins.
