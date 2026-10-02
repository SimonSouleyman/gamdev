# Journal 0.8.2.6: three ribbons (specs/journal-drawers-loop.md, part 1, J1 to J4)

Built on branch s0826-journal from main 0.8.2.5.

## What changed
- **Ribbons (J1, broken 51):** today, diary, collection. The settings page is gone (the switches
  live on the shed's pinboard; `Journal.set_setting` only keeps the dictionary in step). The care
  and pages ribbons are gone too.
- **Notes at the back:** the explanation pages sit behind a small "notes" corner at the page's
  foot; a tap opens one in full as the old torn page (`Journal.read_page`), over the book only.
- **Today (J2, broken 52):** `Journal.today_lines(state)`, at most three lines, no numbers, each
  with its nutrient's mark: the wish ("Wish: the clover.", "Still: ...", "Wish found: ..."), what
  the tree lacks most ("Thirsty.", "Hungry for nitrogen.", "Nothing missing."), and where tonight
  ("Tonight: near the comfrey.", "Tonight: toward the wish.", "Tonight: far away, toward the
  wish.", "Tonight: none within reach."; left out once tonight's root has run). Plus the ink sketch.
  The crown and last-cut paragraphs of the old care page are gone ("everything else leaves").
- **Diary (J3, broken 53):** `Diary.PAGE_MAX = 1`; `Diary.page` shows only `Diary.SPECIAL`
  (a reached wish, then find, visitor, milestone, drank). Days newest first; a quiet day shows its
  date only, no doodle. Player notes always show. The care line (`_care_line`) and the weather
  lines (`_weather_note`) are no longer written. The morning's wish entry is still written (topic
  "wish") as the day's record and for old tests/tools, but never shown in the Diary.
- **Hints (J4, broken 54):** `Journal.show_page` now shows one ink sentence (`Pages.hint`,
  `Pages.HINTS`) on a paper slip at the screen's lower edge; the next tap (after 0.6 s) clears it
  and still emits `page_closed`, so the game's flow (first root after "first_night" etc.) is
  unchanged. The tap is caught, not passed to the game; the game pauses while it shows, as before.
  The nutrient key shows under the hints that name the dots.

## Hooks for the parallel branches
- **Drawers (wt0826-drawers):** set `journal.finds_source = func() -> Array: return [...]` with
  `{"kind", "text", "day"}` per find; the Collection lists them with "in the workbench drawer".
  Optional `"where"` replaces "in the workbench drawer". Unset, the finds come from this tree's
  diary (topic find, kinds in `Underground.FIND_TEXTS`). Wiring on merge with s0826-drawers
  (`state.finds.list()`): map each item to `{"kind": kind, "text": line, "day": night,
  "where": "in the " + drawer name}` in main.gd after `journal.state = state`.
- **Moments (wt0826-moments):** `Diary.DRANK_LINE` and its sunrise line are untouched; the topic
  "drank" is the lowest of `Diary.SPECIAL`, so their new "best thing reached" line shows on a day
  with nothing better. Merge note: `game_state._sunrise` lost the `_care_line()` and
  `_weather_note("morning")` calls next to their edit.

## Old saves
No save format change. Old lines are still read by wording (`Diary.LEGACY_LINES`); routine, mood,
height and plain lines simply no longer show. An old day wish (no wish place) shows on Today.

## Short test run
Tests (journal, shed, nutrient marks, bugs 0821, wish 0825, almanac updated to the new
structure), autoplay, phone-look shots in `GameDev/tree-qa/journal-0.8.2.6/` (today, diary,
collection, notes, notes_read, hint_*, each also `_clear`).
