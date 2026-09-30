# Build notes: save backup and sharing a photo (0.8, specs 2 and 3)

Build thread notes for specs/0.8.md sections 2 (save backup) and 3 (share a photo), built on
branch s08-backup. Checked against items 4 to 10 and 15 to 16 of the spec's broken list.

## What was built

**Save backup (shared/backup.gd, ui/backup_notes.gd, ui/morning_offer.gd)**
- Two notes on the options pinboard, between the switches and the resets: "copy my tree" and
  "load a copy". A small paper slip above them says what happened; it only shows after a tap.
  The switches' rows sit a little closer (170 -> 145 px apart) to make room, so the slip clears
  the long bottom note in "clearer print" too.
- "copy my tree" writes one zip, `tree-<tree>-<date>.zip` (local date): `tree_backup.json`
  (kind, format 1, save version, tree, day, time), `tree_game.json` (the same save the game
  writes: tree, roots, clock, species, bonsai, diary, pages read, grove, clearing) and
  `photos/*.png` (the album, bonsai photos included). The photos are stored, not deflated
  (they are PNGs). The flip-book's small copies are left out; the album makes them again.
- Phone: the zip is made in the game's own storage, then Android's "save as" picker
  (ACTION_CREATE_DOCUMENT) lets the player pick where it goes; the local zip is removed after.
  PC: the zip stays in the game's folder (`user://copies`) and the folder opens.
- "load a copy" opens Android's "open" picker (ACTION_OPEN_DOCUMENT; zip, x-zip and
  octet-stream, since file managers name zips differently). The chosen file is copied into the
  game's storage and read completely before anything happens. A good copy arms the note:
  "sure? tap again" with "The linden on day 12, copied 2026-09-30. It replaces this game." for
  8 seconds (the resets wait 4; after a picker a little longer). The second tap loads it, writes
  the diary line "I loaded a copy of my tree, made on <date>." and saves. PC: the system's file
  dialog opens in the game's folder (or the newest copy there is taken when there is none).
- Refused, with a handwritten note, and the game untouched: not a zip or no manifest ("That file
  is not a copy of my tree."), damaged zip or save ("This copy is damaged."), a higher copy format
  or save version ("This copy is from a newer Tree."), no room ("There is not enough room on the
  phone."). All photos and the save are written beside the current ones first and only swapped
  in when everything is written, so a full phone mid-load also leaves the game as it was.
- Undo: before a copy is loaded, the game as it is (save and album) is written as
  `user://copies/before-load.zip`; if that cannot be written, nothing is loaded. For a day after a
  load the slip offers "take back the game before", which arms "sure? tap again" for it like any
  copy (and keeps the wrong copy as the new "before", so it can be flipped back).
- Only plain photo names are taken out of a zip (no folders, no ".."), so a copy cannot write
  outside the album.
- The last three sunrise saves are kept in `user://mornings` after each sunrise's save (only a
  save that reads as a game). Never a note, never a reminder. If the save cannot be read at start
  (it is kept aside as `.broken`, as before), a torn page offers the newest good one: "take that
  morning" or "begin anew". Without a good one the new game simply begins, as before.

**Sharing (ui/polaroid.gd, ShedMenu.send_photo / send_video)**
- A "send" word beside each Polaroid in the album, and beside "save as video" on a finished
  tree's flip-book page.
- The Polaroid is drawn again on its own (the album's card: the photo cropped square, the white
  card, the handwritten caption), straight, on the album's page paper, three times the album's
  size (about 1190x1240 px). No HUD, no tape, no text beyond the caption, no link, no game name.
- Phone: Android's share sheet (ACTION_SEND through the system chooser, no title), the player
  picks the app. The film: the phone turns the AVI into an MP4 (the same encoder as "save as
  video") and shares that. PC: the file is written to the game's folder (`user://sent`, the film
  stays in `user://timelapse`) and the folder opens.
- No app takes it: "No app on this phone takes a picture. It stays in the game's folder."
- "send" is hidden on a phone whose plugin has no share sheet (the "very old Android" case; the
  plugin's minimum is Android 7).

**TreePhone plugin (additive)**
- New classes `Documents` (the two pickers), `Sharer` and `ShareProvider` (a small read-only
  content provider for the one shared file in the cache folder `share`, instead of androidx
  FileProvider, so the plugin still needs no library). New plugin methods `createDocument`,
  `openDocument`, `shareFile`, `shareVideo`, signals `document_saved`, `document_opened`,
  `shared` (a result word), `onMainActivityResult`; `VideoSaver.toMp4`. Manifest: the provider
  and a `<queries>` block for ACTION_SEND (package visibility on Android 11+). No permission.
- GDScript wrapper: `shared/phone_files.gd` (`PhoneFiles`), a no-op on a PC.

## Choices taken (recommended option each time)
- "On a PC it goes into the game folder": the game's user folder (`user://copies`,
  `user://sent`), which is where a PC game keeps its files; the folder opens after copying or
  sending. The install folder may not be writable.
- A loaded copy comes back exactly as copied: its clock is stamped with the time of loading, so
  there is no catch-up growth for the days between copy and load. The sunrise saves, in contrast,
  grow on as usual (they are the same game, a little earlier).
- The settings (sound, clearer print ...) are not in the copy: they belong to the phone, not the
  tree. The time-lapse videos are not in it either (they can be made again from the album).
- A copy made during a night run saves exactly what the normal save saves (tested).
- "The first photo of a tree (no caption yet)": every photo has a caption from its name; if one
  ever comes out empty, the date is used.
- The shared Polaroid lies on a little of the album's page paper instead of a transparent
  background (messaging apps turn transparency black or white at random).

## Tests (tests/test_backup.gd)
Round trip with the album (tree, roots, clock, species, bonsai, diary, pages read and every
photo byte for byte), a copy made mid-run, eleven kinds of broken, foreign and newer files that
never change the game or the album, photo names that try to leave the album, the three rolling
sunrise saves (a broken save is never kept, a damaged newest is skipped and never put back), an
old save without version, bonsai, clearing or grove loading and copying.

## Open
- Not tried on the phone yet (the pickers and the share sheet under /e/OS, and the size of a
  month's copy: about 2 to 3 MB per album photo, so 60 to 120 MB for a month).
- The shared picture has a thin dark line at its bottom edge on some runs (the page paper's
  edge); cosmetic.
