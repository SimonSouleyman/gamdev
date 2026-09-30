extends RefCounted
## Save backup (0.8, specs/0.8.md section 2; broken list 4 to 6): the copy round trip with the
## album, refused files that never touch the game, the three sunrise saves, old saves.
var t

const ROOT := "user://test_backup"
const GAME := ROOT + "/game.json"
const PHOTOS := ROOT + "/photos"
const COPY := ROOT + "/copies/tree-copy.zip"
const OTHER_GAME := ROOT + "/other_game.json"
const OTHER_PHOTOS := ROOT + "/other_photos"
const MORNINGS := ROOT + "/mornings"


func _clean() -> void:
	_rm(ROOT)


func _rm(dir: String) -> void:
	var d := DirAccess.open(dir)
	if d == null:
		return
	for sub in d.get_directories():
		_rm(dir.path_join(sub))
	for f in d.get_files():
		d.remove(f)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(dir))


## A game with a few days behind it, a root run, diary lines, pages read and the bonsai.
func _grown_game(seed: int = 11) -> GameState:
	var g := GameState.new_game(seed)
	for _night in range(2):
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(Vector2(0.3, 0.05), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		for _i in range(60):
			g.tick(0.5)
	g.seen_pages["sapling"] = true
	g.seen_pages["shears"] = true
	g.diary.add(g.day_number(), "A line of my own.")
	g.ensure_bonsai(true)
	return g


## A few small photos in an album folder: name -> bytes.
func _album(dir: String, names: Array) -> Dictionary:
	DirAccess.make_dir_recursive_absolute(dir)
	var out := {}
	var i := 0
	for n in names:
		var img := Image.create(8, 12, false, Image.FORMAT_RGB8)
		img.fill(Color(0.1 * i, 0.5, 0.2))
		img.save_png(dir.path_join(n))
		out[n] = FileAccess.get_file_as_bytes(dir.path_join(n))
		i += 1
	return out


func _json(g: GameState) -> String:
	return JSON.stringify(g.to_dict(), "", true, true)


## The same game through the normal save and load (what the copy must equal).
func _through_save(g: GameState, path: String, now: float) -> GameState:
	SaveData.save_game(g, path)
	var f := FileAccess.open(path, FileAccess.READ)
	var data: Dictionary = JSON.parse_string(f.get_as_text())
	f = null
	data["saved_at_unix"] = now
	var w := FileAccess.open(path, FileAccess.WRITE)
	w.store_string(JSON.stringify(data, "", false, true))
	w = null
	return SaveData.load_game(path, now)


func test_copy_round_trip_restores_game_and_album() -> void:
	_clean()
	var g := _grown_game()
	var album := _album(PHOTOS, ["linden_day001_morning_100.png", "linden_day002_camera_200.png", "linden_bonsai_day001_bonsai_300.png"])
	var now := 1_900_000_000.0
	t.check_eq(Backup.make(g, COPY, PHOTOS, now), OK, "the copy is written")
	t.check(FileAccess.file_exists(COPY) and not FileAccess.file_exists(COPY + ".part"), "one finished file")
	# Another game and album in its place (a reinstall with a new game).
	var other := GameState.new_game(99, "birch")
	SaveData.save_game(other, OTHER_GAME)
	_album(OTHER_PHOTOS, ["birch_day001_morning_900.png"])
	var got := Backup.apply(COPY, OTHER_GAME, OTHER_PHOTOS, now + 5000.0)
	t.check(got["ok"], "the copy loads: %s" % str(got.get("why", "")))
	var loaded := SaveData.load_game(OTHER_GAME, now + 5000.0)
	t.check(loaded != null, "the loaded save reads")
	if loaded == null:
		return
	var expected := _through_save(g, ROOT + "/expected.json", now)
	t.check_eq(_json(loaded), _json(expected), "tree, roots, clock, species, bonsai, diary and pages come back exactly")
	t.check_eq(loaded.sim.graph.size(), g.sim.graph.size(), "tree")
	t.check_eq(loaded.roots.graph.size(), g.roots.graph.size(), "roots")
	t.check_near(loaded.sim.clock.time_of_day, g.sim.clock.time_of_day, 1e-5, "clock (no catch-up growth between copy and load)")
	t.check_eq(loaded.day_number(), g.day_number(), "day")
	t.check_eq(loaded.sim.species.id, "linden", "species")
	t.check(loaded.bonsai != null and loaded.bonsai.graph.size() == g.bonsai.graph.size(), "bonsai")
	t.check_eq(loaded.diary.entries.size(), g.diary.entries.size(), "diary")
	t.check(loaded.seen_pages.has("shears"), "pages read")
	var names := Backup.album_files(OTHER_PHOTOS)
	t.check_eq(names.size(), album.size(), "album: the copy's photos replace the other album")
	for n in album:
		t.check_eq(FileAccess.get_file_as_bytes(OTHER_PHOTOS.path_join(n)), album[n], "photo comes back byte for byte: " + n)
	t.check(not DirAccess.dir_exists_absolute(OTHER_PHOTOS + "_incoming"), "nothing left over")
	t.check(Backup.diary_line(got["manifest"]).begins_with("I loaded a copy"), "a diary line for the load")
	t.check(Backup.file_name(g, now).begins_with("tree-linden-") and Backup.file_name(g, now).ends_with(".zip"), "named with the tree and the date")
	_clean()


func test_copy_during_a_night_run_saves_the_runs_start() -> void:
	_clean()
	var g := _grown_game(12)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(g.roots.graph.size() - 1)
	for _i in range(30):
		g.steer(Vector2(0.2, 0.0), false, 1.0 / 30.0)
	var now := 1_900_000_000.0
	t.check_eq(Backup.make(g, COPY, PHOTOS, now), OK, "copy mid-run")
	t.check(Backup.apply(COPY, OTHER_GAME, OTHER_PHOTOS, now)["ok"], "loads")
	var loaded := SaveData.load_game(OTHER_GAME, now)
	var expected := _through_save(g, ROOT + "/expected.json", now)
	t.check(loaded != null and expected != null, "both read")
	if loaded != null and expected != null:
		t.check_eq(_json(loaded), _json(expected), "the copy holds what the normal save holds mid-run")
	_clean()


func _write(path: String, bytes: PackedByteArray) -> void:
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_buffer(bytes)
	f = null


func _zip(path: String, entries: Dictionary) -> void:
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var z := ZIPPacker.new()
	z.open(path)
	for k in entries:
		z.start_file(k)
		z.write_file(str(entries[k]).to_utf8_buffer())
		z.close_file()
	z.close()


func test_broken_foreign_and_newer_files_never_touch_the_game() -> void:
	_clean()
	var g := _grown_game(13)
	DirAccess.make_dir_recursive_absolute(ROOT)
	var now := 1_900_000_000.0
	SaveData.save_game(g, GAME)
	_album(PHOTOS, ["linden_day001_morning_100.png"])
	var before_game := FileAccess.get_file_as_bytes(GAME)
	var before_photo := FileAccess.get_file_as_bytes(PHOTOS.path_join("linden_day001_morning_100.png"))
	Backup.make(g, COPY, PHOTOS, now)
	var good := FileAccess.get_file_as_bytes(COPY)
	var game_text := JSON.stringify({"saved_at_unix": now, "game": g.to_dict()})
	var manifest := {"kind": Backup.KIND, "format": Backup.FORMAT, "save_version": GameState.SAVE_VERSION}
	var cases := {}
	cases["text"] = ["foreign", "hello, not a tree".to_utf8_buffer()]
	cases["empty"] = ["foreign", PackedByteArray()]
	cases["truncated"] = ["broken", good.slice(0, good.size() / 2)]
	cases["no_manifest"] = ["foreign", null, {"readme.txt": "photos of my holiday"}]
	cases["other_kind"] = ["foreign", null, {Backup.MANIFEST: JSON.stringify({"kind": "something-else"}), Backup.GAME_ENTRY: game_text}]
	var newer := manifest.duplicate()
	newer["format"] = Backup.FORMAT + 1
	cases["newer_format"] = ["newer", null, {Backup.MANIFEST: JSON.stringify(newer), Backup.GAME_ENTRY: game_text}]
	var newer_save := manifest.duplicate()
	newer_save["save_version"] = GameState.SAVE_VERSION + 1
	cases["newer_save"] = ["newer", null, {Backup.MANIFEST: JSON.stringify(newer_save), Backup.GAME_ENTRY: game_text}]
	var d := g.to_dict()
	d["version"] = GameState.SAVE_VERSION + 1
	cases["newer_game"] = ["newer", null, {Backup.MANIFEST: JSON.stringify(manifest), Backup.GAME_ENTRY: JSON.stringify({"game": d})}]
	cases["no_game"] = ["broken", null, {Backup.MANIFEST: JSON.stringify(manifest)}]
	cases["bad_game"] = ["broken", null, {Backup.MANIFEST: JSON.stringify(manifest), Backup.GAME_ENTRY: '{"game":{"sim":5}}'}]
	cases["not_json"] = ["broken", null, {Backup.MANIFEST: JSON.stringify(manifest), Backup.GAME_ENTRY: "{{{"}]
	for name in cases:
		var c: Array = cases[name]
		var path := ROOT + "/bad_%s.zip" % name
		if c.size() > 2:
			_zip(path, c[2])
		else:
			_write(path, c[1])
		var got := Backup.apply(path, GAME, PHOTOS, now)
		t.check(not got["ok"], "refused: " + name)
		t.check_eq(str(got.get("why", "")), c[0], "why: " + name)
		t.check(Backup.NOTES.has(str(got.get("why", ""))), "a handwritten note for it: " + name)
		t.check_eq(FileAccess.get_file_as_bytes(GAME), before_game, "the game is untouched: " + name)
		t.check_eq(FileAccess.get_file_as_bytes(PHOTOS.path_join("linden_day001_morning_100.png")), before_photo, "the album is untouched: " + name)
		t.check_eq(Backup.album_files(PHOTOS).size(), 1, "no photo added: " + name)
	t.check(not Backup.read(ROOT + "/missing.zip")["ok"], "a missing file is refused")
	_clean()


func test_a_copy_cannot_write_outside_the_album() -> void:
	_clean()
	var g := _grown_game(14)
	var manifest := {"kind": Backup.KIND, "format": Backup.FORMAT, "save_version": GameState.SAVE_VERSION}
	_zip(COPY, {Backup.MANIFEST: JSON.stringify(manifest), Backup.GAME_ENTRY: JSON.stringify({"game": g.to_dict()}),
		"photos/../../evil.png": "x", "photos/sub/deep.png": "x", "photos/ok_day001_morning_1.png": "x", "other/file.png": "x"})
	var got := Backup.read(COPY)
	t.check(got["ok"], "the copy itself reads")
	t.check_eq(got.get("photos", []), ["ok_day001_morning_1.png"], "only plain photo names are taken")
	_clean()


func test_three_sunrise_saves_are_kept() -> void:
	_clean()
	var g := _grown_game(15)
	DirAccess.make_dir_recursive_absolute(ROOT)
	t.check_eq(Backup.keep_morning(GAME, MORNINGS, 3, 1000.0), "", "no save yet, no copy")
	var stamps := []
	for day in range(5):
		g.diary.add(day, "day %d" % day)
		SaveData.save_game(g, GAME)
		var p := Backup.keep_morning(GAME, MORNINGS, Backup.MORNING_COPIES, 1000.0 + day * 86400.0)
		t.check(p != "", "sunrise %d kept" % day)
		stamps.append(p)
	var kept := Backup.mornings(MORNINGS)
	t.check_eq(kept.size(), 3, "the last three sunrises stay")
	t.check_eq(kept, [stamps[4], stamps[3], stamps[2]], "newest first, the oldest dropped")
	# A broken save is never kept as a morning copy.
	_write(GAME, "{broken".to_utf8_buffer())
	t.check_eq(Backup.keep_morning(GAME, MORNINGS, 3, 1000.0 + 6 * 86400.0), "", "a broken save is not kept")
	t.check_eq(Backup.mornings(MORNINGS).size(), 3, "and the good ones stay")
	# The newest good one is offered when the save cannot be read; a damaged newest is skipped.
	_write(kept[0], "{damaged".to_utf8_buffer())
	t.check_eq(Backup.newest_good_morning(MORNINGS), kept[1], "the newest good one")
	t.check_eq(Backup.restore_morning(kept[1], GAME), OK, "put back as the save")
	var back := SaveData.load_game(GAME, Backup.morning_time(kept[1]))
	t.check(back != null and back.sim.graph.size() == g.sim.graph.size(), "and it loads")
	t.check(Backup.restore_morning(kept[0], GAME) != OK, "a damaged one is never put back")
	_clean()


func test_old_saves_still_load_and_copy() -> void:
	_clean()
	# A save from before the bonsai, the clearing, the grove and the version number (0.4 era).
	var g := _grown_game(16)
	g.bonsai = null
	var d := g.to_dict()
	for k in ["version", "bonsai", "bonsai_resting", "clearing", "grove", "pending_pages", "finished", "spent_announced"]:
		d.erase(k)
	_write(GAME, JSON.stringify({"saved_at_unix": 1000.0, "game": d}, "", false, true).to_utf8_buffer())
	var old := SaveData.load_game(GAME, 1000.0)
	t.check(old != null, "an old save loads")
	if old == null:
		return
	t.check_eq(old.sim.graph.size(), g.sim.graph.size(), "with its tree")
	t.check_eq(Backup.make(old, COPY, PHOTOS, 2000.0), OK, "and can be copied")
	t.check(Backup.apply(COPY, OTHER_GAME, OTHER_PHOTOS, 2000.0)["ok"], "and the copy loads")
	# A copy's manifest without the numbers (hand-made): read as format 0, which is not newer.
	var loose := {"kind": Backup.KIND}
	_zip(ROOT + "/loose.zip", {Backup.MANIFEST: JSON.stringify(loose), Backup.GAME_ENTRY: JSON.stringify({"game": d})})
	t.check(Backup.read(ROOT + "/loose.zip")["ok"], "a copy without version numbers still reads")
	_clean()
