extends RefCounted
var t

const PATH := "user://test_save.json"


func test_round_trip_and_offline_catch_up() -> void:
	var s := GrowthSim.new(5)
	for k in range(4):
		s.resources.add(k, 30.0)
	s.clock.time_of_day = 0.2
	for _i in range(20):
		s.tick(0.5)
	s.resources.life_force = 3.5
	t.check_eq(SaveData.save(s, PATH), OK, "saved")

	var now := Time.get_unix_time_from_system()
	var loaded := SaveData.load(PATH, now)
	t.check(loaded != null, "loaded")
	if loaded == null:
		return
	t.check_eq(loaded.graph.size(), s.graph.size(), "graph size kept with no time passed")
	t.check_near(loaded.resources.life_force, 3.5, 1e-4, "life force kept")
	t.check_eq(loaded.species.id, "linden", "species kept")
	t.check_near(loaded.clock.time_of_day, s.clock.time_of_day, 1e-5, "clock kept")

	var later := SaveData.load(PATH, now + 86400.0)
	t.check(later.graph.size() > s.graph.size(), "grew while away")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(PATH))


func test_broken_saves_are_kept_aside_not_loaded() -> void:
	var path := "user://test_broken.json"
	for text in ['{"game":{"sim":{"graph":{}}}}', '{"game":5}', '{"game":{"sim":5}}', 'not json', '']:
		var f := FileAccess.open(path, FileAccess.WRITE)
		f.store_string(text)
		f.close()
		t.check(SaveData.load_game(path) == null, "broken save is not loaded: " + text)
		t.check(not FileAccess.file_exists(path), "and moved aside: " + text)
		t.check(FileAccess.file_exists(path + ".broken"), "kept as .broken: " + text)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path + ".broken"))
