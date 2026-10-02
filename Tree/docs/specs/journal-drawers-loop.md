# Spec: a three-tab journal, finds in the shed drawers, a day worth watching (0.8.2.6)

Written 2026-10-02 from Simon's 0.8.2.3 notes (rework the journal, openable drawers, look at the
game loop) and the ten survey cards in notes/survey-2026-10-02.md. He tapped the recommended
option on all ten and confirmed in his own words (15:52 UTC): "ja alle so". It comes after
specs/wish-compass-vial.md (0.8.2.5), which this spec builds on (the wish place, the vial).

## 1. The journal: three tabs (J1 to J4)

**Purpose.** The journal answers "what does my tree need today?" at a glance and keeps what is
worth keeping. Everything else leaves.

**What the player should feel.** "One look and I know what to do tonight."

**Structure (J1 "Drei Reiter").** The leather notebook keeps its look. Three ribbons instead of six:
- **Today:** the page for the day (below).
- **Diary:** the days, newest first, with doodles and the player's own notes.
- **Collection:** the shade plants under the crown ("Under my crown"), the finds (with a link to
  the drawer they lie in), the visitors seen once each, and the finished trees' species.
The settings ribbon goes; the switches live only on the pinboard. The hint pages go to the last
pages of the book, reachable from a small "notes" corner, not a ribbon.

**Today (J2 "Nur Spielrelevantes").** At most three lines, each with its coloured dot where it fits:
1. The wish: "Wish: the clover." (spec wish-compass-vial.md).
2. What the tree lacks most, in words ("thirsty", "hungry for nitrogen") or "nothing missing".
3. Where tonight's root finds it: "near the comfrey" or "far away, toward the wish".
Plus the small ink sketch of the tree. No segment counts, no height numbers, no visitor list.

**Diary (J3 "Nur Besonderes").** One line a day, written only when something happened: a visitor
seen for the first time, a find, a wish reached, the first blossom, a finished tree, a night with
no life force. Each with its doodle. A quiet day stays empty (the date shows, with nothing under
it). The player's own notes are always kept. `Diary.PAGE_MAX` becomes 1 for the game's lines;
routine and mood lines are no longer written.

**Hints (J4 "Kurz im Bild").** A first-time hint is one sentence in ink at the edge of the screen,
gone on the next tap. Torn-out hint pages no longer flutter in. The full text is in the back of the
book. The "while you were away" page and the morning page stay (they are not hints), each at most
three lines.

## 2. The shed drawers: finds (D1, D2)

**Purpose.** Give the roots' finds a home you can open and a small use, so digging far has a
reason beyond nutrients.

**What the player should feel.** Rummaging through a drawer of treasures from their own soil.

**Output (D1 "Fundstücke").** The workbench drawers (layout 08: the workbench at the bottom of the
screen) open with a tap, slide out with a wooden sound, and close on a second tap. Each find the
roots reach lies in a drawer as a small real object (fossil shell, old root, coin; a stone with a
water vein; new: a map scrap, a shard). A tap on a find shows its one line and the night it was
found. Finds stay forever, across trees (they belong to the garden, like the album).

**Use (D2 "Kleiner Hinweis").** At most one find a week has a use, and only a hint, never growth:
- a **map scrap** shows a far nutrient patch as a faint mark in the next night's root view,
- a **shard of a drain pipe** shows a rock gap (the cheap way through a rock band) for one night.
Nothing else: finds never add life force, nutrients or speed.

## 3. The game loop: a day worth watching (G1 to G4)

**Session length (G1 "3 bis 5 Minuten").** The target for one visit: a short look by day (the wish
place, the crown, a boost or two, a cut), then the night's root. Fast-forward and the sunset button
stay, so the day can be skipped without loss.

**Moments at set times (G2 "Momente").** The day gets four moments that happen at fixed times of
day, so watching shows something and skipping (fast-forward) still shows each one briefly:
- **Morning:** the night's result (below) and the wish plant opening its flowers.
- **Late morning:** the day's visitor arrives (one at a time, as now).
- **Afternoon:** the weather mood turns (a breeze, a passing cloud shadow, a short shower), look only.
- **Sunset:** the ink ring around the wish place, then the dive.
While fast-forwarding, the clock slows to normal for about two seconds at each moment, then
carries on. Missing a moment costs nothing; the visitor still lands in the diary.

**The morning shows the night (G3 "Morgens zeigen").** On the first view each morning: a short glow
runs along last night's new roots under the grass (seen from the tree view as a faint line in the
soil, about two seconds), the crown grows a visible burst from what the roots drank, and the diary
names the best thing the night reached (a wish, a rich patch, a find). This replaces the plain line
"the roots drank well" from wish-compass-vial.md section 3.

**After the six species (G4 "Sammeln").** The long goal is collecting, not a bigger number: the
Collection tab fills (plants, finds, visitors, species), the drawers fill, trees keep coming. Rarer
quirks for later trees and a full field guide are later work (design doc roadmap), not this version.

## What working looks like (broken list, short)

51. The journal shows more than three ribbons, or a settings ribbon.
52. The Today page has a number on it, or more than three lines.
53. A diary line on a day where nothing happened, or two game lines on one day.
54. A torn-out hint page fluttering in during play.
55. A drawer that does not open, or a reached find missing from the drawers.
56. A find that changes growth, life force or nutrients.
57. A moment (morning, visitor, weather, sunset) skipped without being shown while fast-forwarding.
58. A morning after a root run with no glow along the new roots.
