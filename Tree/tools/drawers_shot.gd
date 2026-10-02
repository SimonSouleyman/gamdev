extends SceneTree
## 0.8.2.6 shots (notes/drawers-0.8.2.6.md): the finds in the bench's drawers and a find's hint
## at night. A linden of six nights whose roots reached every kind of find; the left drawer open
## (the view leaning over it), a find's note, the right drawer, the room again; the next night's
## pick and far view with the map scrap's mark, and a shard's gap mark in the far view.
## Run (phone look): godot --path . --rendering-method gl_compatibility -s tools/drawers_shot.gd -- --phone --shots=C:/some/folder [--size=450x1000]

var main: Node
var shots := ""
var win := Vector2i(450, 1000)
var bot := RootBot.new()


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			win = Vector2i(int(p[0]), int(p[1]))
	DirAccess.make_dir_recursive_absolute(shots)
	DisplayServer.window_set_size(win)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(shots.path_join(name + ".png"))
	print("shot ", name, " ", img.get_size())


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _quiet() -> void:
	main.journal.close_diary()
	main.journal.clear_pages()


## The soil's find of this kind, touched by the roots (as RootSystem would).
func _touch(g: GameState, kind: String) -> void:
	for f in g.ground.finds:
		if f["kind"] == kind and not f["found"]:
			f["found"] = true
			g.notify_find(f)
			return


func _game() -> GameState:
	var g := GameState.new_game(42, "linden")
	var kinds := ["coin", "fossil", "old_root", "water_vein", "", "map_scrap"]
	for day in range(6):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		# Each night's root reached one more kind (the last night a map scrap: tomorrow's hint).
		_touch(g, kinds[day])
		if day == 4:
			# A shard from the first tree (finds stay from tree to tree; no hint now).
			g.finds.add("shard", 9, "linden", 1)
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if day < 5:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	for k in Pages.TEXTS:
		g.seen_pages[k] = true
	for item in Shed.ITEMS:
		g.seen_pages["shed_used_" + item] = true
	g.take_events()
	print("finds: ", g.finds.list(), " hint: ", g.finds.hint)
	return g


func _run() -> void:
	await _wait(2)
	main.start(_game())
	await _wait(30)
	_quiet()
	main.enter_shed(false)
	await _wait(30)
	_quiet()
	var shed: Shed = main.shed
	shed.toggle_drawer("drawer01")
	await _wait(60)
	_shot("01_drawer_left_finds")
	for i in range(shed.find_count()):
		if shed._finds_shown[i]["item"]["kind"] == "fossil":
			print("fossil at ", shed.find_screen(i), " -> find_at ", shed.find_at(shed.find_screen(i)))
			shed.show_note(shed.find_at(shed.find_screen(i)))
	await _wait(40)
	_shot("02_find_note_fossil")
	shed.hide_note()
	shed.toggle_drawer("drawer02")
	await _wait(60)
	_shot("03_drawer_right_finds")
	for i in range(shed.find_count()):
		if shed._finds_shown[i]["item"]["kind"] == "map_scrap":
			shed.show_note(i)
	await _wait(40)
	_shot("04_find_note_map")
	shed.toggle_drawer("drawer02")
	await _wait(50)
	_shot("05_room_again")
	main.leave_shed()
	await _wait(60)
	# The next night: the map scrap's mark over a far patch.
	while main.state.phase == GameState.Phase.DAY:
		main.state.tick(0.5)
	main.state.take_events()
	await _wait(10)
	main._on_ground_tapped()
	await _wait(200)
	_quiet()
	await _wait(20)
	print("night %d hint %s" % [main.state.day_number(), main.root_view.find_hint])
	_shot("06_night_pick_map_mark")
	var rv: RootView = main.root_view
	rv.open_far_view()
	await _wait(150)
	_quiet()
	_shot("07_far_view_map_mark")
	# A shard's night: the gap of the nearest rock band.
	var gap := Finds.gap_hint(main.state.ground, Vector3(8, -1, 8))
	gap["kind"] = "gap"
	rv.set_find_hint(gap)
	rv.leave_far_view()
	await _wait(80)
	rv.open_far_view()
	await _wait(150)
	_quiet()
	_shot("08_far_view_gap_mark")
	rv.leave_far_view()
	await _wait(100)
	_shot("09_night_gap_mark")
	quit()
