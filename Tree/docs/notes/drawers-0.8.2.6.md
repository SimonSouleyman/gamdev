# 0.8.2.6: finds in the workbench drawers (specs/journal-drawers-loop.md, part 2: D1, D2)

Built on 0.8.2.4's openable drawers (notes/ff-0.8.2.4.md, the hook `Shed.drawer_contents`) and
0.8.2.5's layout 08 (only the bench's upper row shows: `drawer01` left, `drawer02` right). Broken
list items 55 and 56. Short test run only (Simon's rule while prototyping). Phone-look shots
(`--phone`, Compatibility, 450x1000) in `GameDev/tree-qa/drawers-0.8.2.6/` (`tools/drawers_shot.gd`).

## The finds belong to the garden (`shared/finds.gd`, class `Finds`)

- `GameState.finds`: every find the roots reached, oldest first, each with its kind, the night it
  was found, the tree's species and number. Carried to the next tree (`new_tree`), like the grove
  and the clearing's collection. Saved as `"finds"` in the game save.
- **For the journal's Collection:** `state.finds.list()` gives each find as
  `{kind, day, species, tree, drawer, drawer_name, note}`; `Finds.note(kind)` is its one line,
  `Finds.when_line(item)` "Found on night 9, under the linden.", `Finds.DRAWER_NAMES` the
  drawer's name ("the bench's left drawer").
- An old save (no `"finds"`) puts what its soil marks as found into the drawers, each with the
  night of its diary line (`Finds.from_old_save`).
- Two new kinds in every soil: **2 map scraps and 2 drain-pipe shards** (`Underground.MAP_SCRAPS`,
  `SHARDS`), 5 to 16 m from the trunk in the topsoil, from a generator of their own
  (`hash([seed, "finds_0826"])`), appended after the older finds: dots, rocks and the older finds
  of a save stay where they were; an old save gets them unfound on load. Diary lines (with two new ink doodles, `map_scrap` and `shard`) and the find
  page say where it went ("It goes in the bench's drawer.").

## The drawers (`shed/shed.gd`, `shed/find_models.gd`)

- Left drawer: the soil's own things (fossil shell in a stone, old root, coin, stone with a water
  vein), two by two. Right drawer: the people's things (map scrap, shard). A second or third find
  of a kind (later trees) lies beside the first, a little turned; more stay listed in the journal.
- The things are small real objects built in code (`FindModels`): a lumpy pebble with a ribbed
  shell on it, a gnarled tapering root (bark lighter on top), a verdigris coin with rim and worn
  head, a dark pebble crossed by a pale quartz vein, a torn scrap of yellowed map (ink path, pencil
  ring), a curved terracotta pipe shard with darker silt inside. A few hundred triangles each, one
  plain material per part (vertex colours), no textures but the map's 96x72 one; each with a soft
  contact shadow.
- Opening a drawer now **leans the view over it** (0.55 s; the drawer slides 0.32 m, was 0.24):
  on a 450x1000 phone the finds are about 30 (the coin) to 100 px across. One drawer out at a time. A tap on a find
  lifts it a little (a light wooden tick) and shows its note on a paper strip at the top: its line
  and the night it was found. The next tap elsewhere hides the note, the one after closes the
  drawer and straightens the view; the phone's back closes it too. Leaving the shed slides every
  drawer back. The labels of the bench's things hide while leaning.
- `main.enter_shed` fills the drawers from `state.finds.list()` (rebuilt only when the list changed).

## A hint, never growth (D2)

- A map scrap or a shard may give one hint for the **next night** (`Finds.give_hint`), at most
  one in 7 days of a tree (`HINT_EVERY_DAYS`; a new tree may have one at once):
  - map scrap: the nearest far patch (middle or far ring) the roots have not come near yet and
    that still holds most of what it had;
  - shard: the gap of the nearest rock band (in a soil without bands, none).
- It shows as a faint dashed pencil ring (`roots/hint_mark.gdshader`, one quad, additive, not hidden
  by roots or stones): cream over a patch, grey-blue over a gap, in the run and the pick view, and
  in the far view (which frames it). Only on that night (`Finds.hint_for(night)`).
- A find changes nothing else: no life force, nutrients, deposits, roots or growth
  (`test_a_find_changes_no_growth_life_force_or_nutrients` plays the next day against a twin).

## Tests (`tests/test_drawers_0826.gd`)

New soils hold the new finds in all layouts; an old soil loads with its found flags and the same
dots; every reached kind is listed with its drawer and laid into it; finds saved, through the save
file, kept by the next tree; an old save's found finds come into the drawers; no find changes
growth (56); the map scrap's hint is for the next night only and one a week; a shard's mark lies in
a band's gap; the root view shows the mark and the far view frames it; on 450x1000, 720x1600 and
720x1280 each open drawer leans, shows its finds on screen at 6 % of the width or more, a tap finds
each and shows its line and night, closing straightens the view (55).

## Shots

`01_drawer_left_finds`, `02_find_note_fossil`, `03_drawer_right_finds`, `04_find_note_map`,
`05_room_again`, `06_night_pick_map_mark`, `07_far_view_map_mark`, `08_far_view_gap_mark`,
`09_night_gap_mark`; `base/` holds 0.8.2.5's shots for comparison.
