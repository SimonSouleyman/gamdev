class_name Backup
extends RefCounted
## Save backup (0.8, specs/0.8.md section 2). Two parts:
## - A copy the player keeps ("copy my tree" / "load a copy" on the pinboard): one zip with the
##   save (tree, roots, clock, species, bonsai, diary and the pages read) and the album photos.
##   A copy is read and checked completely before anything is written, so a broken, foreign or
##   newer file never touches the current game.
## - The last MORNING_COPIES sunrise saves in the game's own storage, kept without asking or
##   telling (never a reminder); if the save cannot be read at start the newest good one is offered.
## No network, no permission: the phone's own pickers do the rest (PhoneFiles).

## The copy's own format. A copy with a higher number (or a save version above
## GameState.SAVE_VERSION) comes from a newer Tree and is refused.
const FORMAT := 1
const KIND := "tree-backup"
const MANIFEST := "tree_backup.json"
const GAME_ENTRY := "tree_game.json"
const PHOTO_PREFIX := "photos/"
## Sunrise saves kept in the game's own storage (tuning: 2 to 5).
const MORNING_COPIES := 3
## A copy bigger than this is not ours (the game reads it into memory).
const MAX_BYTES := 512 * 1024 * 1024

## Tools and tests point these elsewhere, so they never touch the player's files.
## COPIES_DIR: on a PC "copy my tree" writes here (the game's folder); on the phone the zip is
## made here first and then handed to the "save as" picker, and a picked copy lands here.
static var COPIES_DIR := "user://copies"
static var MORNINGS_DIR := "user://mornings"

## Why a copy was refused (Backup.read / apply "why"), as a handwritten note.
const NOTES := {
	"broken": "This copy is damaged. My tree stays as it is.",
	"foreign": "That file is not a copy of my tree. My tree stays as it is.",
	"newer": "This copy is from a newer Tree. My tree stays as it is.",
	"no_room": "There is not enough room on the phone. My tree stays as it is.",
}


# --- the copy the player keeps ---------------------------------------------------------

## "tree-linden-2026-09-30.zip": the tree and the (local) date.
static func file_name(state: GameState, now_unix: float = -1.0) -> String:
	var tree := state.tree_name().to_lower().replace(" ", "-")
	return "tree-%s-%s.zip" % [tree, local_date(now_unix)]


static func local_date(unix: float = -1.0) -> String:
	if unix < 0.0:
		unix = Time.get_unix_time_from_system()
	var bias := int(Time.get_time_zone_from_system().get("bias", 0)) * 60
	return Time.get_date_string_from_unix_time(int(unix) + bias)


## Writes the copy of `state` and the album in `photos_dir` to `out_path` (a zip). The save is the
## same as the normal save (a copy made during a night run keeps the run's start, as the save does).
static func make(state: GameState, out_path: String, photos_dir: String = Photos.DIR, now_unix: float = -1.0) -> Error:
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	DirAccess.make_dir_recursive_absolute(out_path.get_base_dir())
	var part := out_path + ".part"
	var zip := ZIPPacker.new()
	var err := zip.open(part)
	if err != OK:
		return err
	# The photos are PNGs already: packing them again only costs time.
	zip.compression_level = ZIPPacker.COMPRESSION_NONE
	var photos := album_files(photos_dir)
	var manifest := {
		"kind": KIND,
		"format": FORMAT,
		"save_version": GameState.SAVE_VERSION,
		"tree": state.sim.species.id,
		"day": state.day_number(),
		"made_at_unix": now_unix,
		"photos": photos.size(),
	}
	var game := {"saved_at_unix": now_unix, "game": state.to_dict()}
	err = _add(zip, MANIFEST, JSON.stringify(manifest, "\t").to_utf8_buffer())
	if err == OK:
		err = _add(zip, GAME_ENTRY, JSON.stringify(game, "", false, true).to_utf8_buffer())
	for name in photos:
		if err != OK:
			break
		var bytes := FileAccess.get_file_as_bytes(photos_dir.path_join(name))
		err = _add(zip, PHOTO_PREFIX + name, bytes) if not bytes.is_empty() else ERR_FILE_CANT_READ
	var closed := zip.close()
	if err == OK:
		err = closed
	if err != OK:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(part))
		return err
	if FileAccess.file_exists(out_path):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(out_path))
	return DirAccess.rename_absolute(ProjectSettings.globalize_path(part), ProjectSettings.globalize_path(out_path))


static func _add(zip: ZIPPacker, name: String, bytes: PackedByteArray) -> Error:
	var err := zip.start_file(name)
	if err != OK:
		return err
	err = zip.write_file(bytes)
	var closed := zip.close_file()
	return err if err != OK else closed


## The album's photos (file names; the flip-book's small copies are made again when needed).
static func album_files(photos_dir: String = Photos.DIR) -> Array[String]:
	var out: Array[String] = []
	var d := DirAccess.open(photos_dir)
	if d == null:
		return out
	for f in d.get_files():
		if f.ends_with(".png"):
			out.append(f)
	out.sort()
	return out


## Reads and checks a copy without changing anything. Returns {"ok": true, "manifest", "game"
## (the parsed save), "photos" (names)} or {"ok": false, "why": "broken" | "foreign" | "newer"}.
static func read(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {"ok": false, "why": "foreign"}
	var f := FileAccess.open(path, FileAccess.READ)
	var size := f.get_length() if f != null else 0
	var magic := f.get_buffer(4) if f != null and size >= 4 else PackedByteArray()
	f = null
	# A zip starts with "PK\x03\x04"; anything else is not a copy of a tree.
	if size > MAX_BYTES or magic != PackedByteArray([0x50, 0x4B, 0x03, 0x04]):
		return {"ok": false, "why": "foreign"}
	var zip := ZIPReader.new()
	if zip.open(path) != OK:
		return {"ok": false, "why": "broken"}
	var files := zip.get_files()
	if not files.has(MANIFEST):
		zip.close()
		return {"ok": false, "why": "foreign"}
	var manifest: Variant = _parse(zip.read_file(MANIFEST))
	if not (manifest is Dictionary) or str((manifest as Dictionary).get("kind", "")) != KIND:
		zip.close()
		return {"ok": false, "why": "foreign"}
	if int(manifest.get("format", 0)) > FORMAT or int(manifest.get("save_version", 0)) > GameState.SAVE_VERSION:
		zip.close()
		return {"ok": false, "why": "newer"}
	if not files.has(GAME_ENTRY):
		zip.close()
		return {"ok": false, "why": "broken"}
	var game: Variant = _parse(zip.read_file(GAME_ENTRY))
	if not SaveData._looks_like_a_game(game):
		zip.close()
		return {"ok": false, "why": "broken"}
	if int((game["game"] as Dictionary).get("version", 0)) > GameState.SAVE_VERSION:
		zip.close()
		return {"ok": false, "why": "newer"}
	var photos: Array[String] = []
	for name in files:
		if name.begins_with(PHOTO_PREFIX) and _safe_photo_name(name.substr(PHOTO_PREFIX.length())):
			photos.append(name.substr(PHOTO_PREFIX.length()))
	zip.close()
	return {"ok": true, "manifest": manifest, "game": game, "photos": photos}


## Only plain photo names come out of a copy (no folders, no "..").
static func _safe_photo_name(name: String) -> bool:
	return name.ends_with(".png") and name.is_valid_filename() and not name.contains("/") and not name.contains("\\") and not name.begins_with(".")


static func _parse(bytes: PackedByteArray) -> Variant:
	if bytes.is_empty():
		return null
	var json := JSON.new()
	return json.data if json.parse(bytes.get_string_from_utf8()) == OK else null


## Loads a copy in place of the current game: the save and the album. Everything is read, checked
## and written beside the current files first; only when all of it is written are they swapped,
## so a broken copy or a full phone leaves the current game exactly as it was.
## The copy's clock is stamped `now`, so the tree comes back as it was copied (no catch-up growth
## for the time between copy and load). Returns read()'s answer ("why": "no_room" on a write error).
static func apply(path: String, game_path: String = SaveData.GAME_PATH, photos_dir: String = Photos.DIR, now_unix: float = -1.0) -> Dictionary:
	var got := read(path)
	if not got["ok"]:
		return got
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	var incoming := photos_dir.trim_suffix("/") + "_incoming"
	_remove_dir(incoming)
	DirAccess.make_dir_recursive_absolute(incoming)
	var zip := ZIPReader.new()
	var err := zip.open(path)
	for name in got["photos"]:
		if err != OK:
			break
		var bytes := zip.read_file(PHOTO_PREFIX + name)
		var out := FileAccess.open(incoming.path_join(name), FileAccess.WRITE)
		if out == null or bytes.is_empty():
			err = ERR_FILE_CANT_WRITE
			break
		out.store_buffer(bytes)
		out.close()
		if out.get_error() != OK:
			err = ERR_FILE_CANT_WRITE
	zip.close()
	var game: Dictionary = got["game"]
	game["saved_at_unix"] = now_unix
	var tmp := game_path + ".incoming"
	if err == OK:
		var gf := FileAccess.open(tmp, FileAccess.WRITE)
		if gf == null:
			err = ERR_FILE_CANT_WRITE
		else:
			gf.store_string(JSON.stringify(game, "", false, true))
			gf.close()
	if err != OK:
		_remove_dir(incoming)
		DirAccess.remove_absolute(ProjectSettings.globalize_path(tmp))
		return {"ok": false, "why": "no_room"}
	# Everything is in place beside the current game: swap.
	DirAccess.rename_absolute(ProjectSettings.globalize_path(tmp), ProjectSettings.globalize_path(game_path))
	_clear_album(photos_dir)
	DirAccess.make_dir_recursive_absolute(photos_dir)
	for name in got["photos"]:
		DirAccess.rename_absolute(ProjectSettings.globalize_path(incoming.path_join(name)), ProjectSettings.globalize_path(photos_dir.path_join(name)))
	_remove_dir(incoming)
	got["saved_at_unix"] = now_unix
	return got


## The game as it was before the last "load a copy" (save and album), kept so a wrong copy can
## be undone: the pinboard offers it for a day after a load ("take back the game before").
const BEFORE_LOAD := "before-load.zip"
## How long the pinboard keeps offering it (seconds).
const UNDO_SECONDS := 86400.0


static func before_load_path(dir: String = COPIES_DIR) -> String:
	return dir.path_join(BEFORE_LOAD)


## True while the game before the last load can still be taken back from the pinboard.
static func can_undo_load(dir: String = COPIES_DIR, now_unix: float = -1.0) -> bool:
	var p := before_load_path(dir)
	if not FileAccess.file_exists(p):
		return false
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	return now_unix - float(FileAccess.get_modified_time(p)) < UNDO_SECONDS


## "load a copy" with a safety copy: the current game and album are first written as a copy
## (before_load_path), then the copy at `path` is loaded (apply). If the safety copy cannot be
## written, nothing is loaded ("why": "no_room"). Loading the safety copy itself works too (it
## is read before it is replaced by the game it undoes).
static func load_with_safety(path: String, current: GameState, game_path: String = SaveData.GAME_PATH, photos_dir: String = Photos.DIR, dir: String = COPIES_DIR, now_unix: float = -1.0) -> Dictionary:
	var safety := before_load_path(dir)
	var fresh := safety + ".new"
	if make(current, fresh, photos_dir, now_unix) != OK:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(fresh))
		return {"ok": false, "why": "no_room"}
	var got := apply(path, game_path, photos_dir, now_unix)
	if not got["ok"]:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(fresh))
		return got
	DirAccess.remove_absolute(ProjectSettings.globalize_path(safety))
	DirAccess.rename_absolute(ProjectSettings.globalize_path(fresh), ProjectSettings.globalize_path(safety))
	return got


## The diary line after a copy was loaded.
static func diary_line(manifest: Dictionary) -> String:
	var made := float(manifest.get("made_at_unix", 0.0))
	return "I loaded a copy made on %s." % local_date(made) if made > 0.0 else "I loaded a copy of my tree."


## "The linden on day 12, copied 2026-09-30." for the note before loading.
static func describe(manifest: Dictionary) -> String:
	var tree := Species.from_id(str(manifest.get("tree", "linden"))).display_name.to_lower()
	return "The %s on day %d, copied %s." % [tree, int(manifest.get("day", 0)), local_date(float(manifest.get("made_at_unix", 0.0)))]


static func _clear_album(photos_dir: String) -> void:
	for f in album_files(photos_dir):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(photos_dir.path_join(f)))
	_remove_dir(photos_dir.path_join("thumbs"))


static func _remove_dir(dir: String) -> void:
	var d := DirAccess.open(dir)
	if d == null:
		return
	for f in d.get_files():
		d.remove(f)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(dir))


## The newest copy in COPIES_DIR (a PC without a file dialog loads that one), or "".
static func newest_copy(dir: String = COPIES_DIR) -> String:
	var best := ""
	var best_time := -1
	var d := DirAccess.open(dir)
	if d == null:
		return ""
	for f in d.get_files():
		if f.ends_with(".zip") and f.begins_with("tree-"):
			var t := FileAccess.get_modified_time(dir.path_join(f))
			if t > best_time:
				best_time = t
				best = dir.path_join(f)
	return best


# --- the sunrise saves in the game's own storage ------------------------------------------

## After each sunrise's save: keeps a copy of the save file and drops the oldest beyond `keep`.
## Only a save that reads as a game is kept. Returns the copy's path ("" if none was made).
static func keep_morning(game_path: String = SaveData.GAME_PATH, dir: String = MORNINGS_DIR, keep: int = MORNING_COPIES, now_unix: float = -1.0) -> String:
	if not FileAccess.file_exists(game_path) or not is_good_save(game_path):
		return ""
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	DirAccess.make_dir_recursive_absolute(dir)
	var path := dir.path_join("morning_%d.json" % int(now_unix * 1000.0))
	if DirAccess.copy_absolute(ProjectSettings.globalize_path(game_path), ProjectSettings.globalize_path(path)) != OK:
		return ""
	var all := mornings(dir)
	for i in range(maxi(keep, 1), all.size()):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(all[i]))
	return path


## The kept sunrise saves, newest first.
static func mornings(dir: String = MORNINGS_DIR) -> Array[String]:
	var out: Array[String] = []
	var d := DirAccess.open(dir)
	if d == null:
		return out
	for f in d.get_files():
		if f.begins_with("morning_") and f.ends_with(".json"):
			out.append(dir.path_join(f))
	out.sort_custom(func(a: String, b: String) -> bool: return _stamp(a) > _stamp(b))
	return out


static func _stamp(path: String) -> int:
	return int(path.get_file().get_basename().trim_prefix("morning_"))


static func is_good_save(path: String) -> bool:
	var json := JSON.new()
	return json.parse(FileAccess.get_file_as_string(path)) == OK and SaveData._looks_like_a_game(json.data)


## The newest sunrise save that reads as a game, or "".
static func newest_good_morning(dir: String = MORNINGS_DIR) -> String:
	for p in mornings(dir):
		if is_good_save(p):
			return p
	return ""


## When a kept morning save was made (unix seconds).
static func morning_time(path: String) -> float:
	return _stamp(path) / 1000.0


## The tree's name and day in a kept sunrise save, for the offer ({} if it does not read).
static func peek(path: String) -> Dictionary:
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK or not SaveData._looks_like_a_game(json.data):
		return {}
	var g := GameState.from_dict(json.data["game"])
	return {"tree": g.tree_name(), "day": g.day_number()}


## Puts a kept sunrise save back as the game's save (the time since then grows as usual on load).
static func restore_morning(path: String, game_path: String = SaveData.GAME_PATH) -> Error:
	if not is_good_save(path):
		return ERR_FILE_CORRUPT
	return DirAccess.copy_absolute(ProjectSettings.globalize_path(path), ProjectSettings.globalize_path(game_path))
