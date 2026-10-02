extends SceneTree
## 0.8.2.1 QA: an old game is played for some days, then "start over" (main._reset("all")); the
## new game's morning photo (album pose) and its first night are photographed, to see that
## nothing of the old tree stays. Run: godot --path . -s tools/qa_newgame.gd -- --shots=<folder>
## [--days=7] [--fresh] (fresh: no old game, for comparison).

var main: Node
var shots := ""
var frame := 0
var days := 7
var fresh := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a == "--fresh":
			fresh = true
	DirAccess.make_dir_recursive_absolute(shots)
	Photos.DIR = "user://tool_newgame_photos"
	TimeLapse.DIR = "user://tool_newgame_timelapse"
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))
	print("shot ", name)


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame


func _grow_day(g: GameState) -> void:
	if g.phase == GameState.Phase.DAY:
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()


func _album(tv: TreeView, name: String) -> void:
	var hud_was: bool = tv.hud.visible
	tv.hud.visible = false
	tv.album_pose(true)
	await _wait(4)
	_shot(name)
	tv.album_pose(false)
	tv.hud.visible = hud_was


func _outside() -> void:
	await _wait(2)
	if main.in_shed:
		main.leave_shed()
		await _wait(60)
	await _wait(20)


func _run() -> void:
	var tv: TreeView = main.tree_view
	if not fresh:
		var g := GameState.new_game(42)
		for _d in range(days):
			_grow_day(g)
		main.start(g)
		await _outside()
		await _album(tv, "a_old_album")
		main._reset("all")
	else:
		main.start(GameState.new_game(43))
		await _outside()
	await _wait(20)
	var n: GameState = main.state
	_grow_day(n)
	main.start(n)
	await _outside()
	_shot("b_new_day1")
	await _album(tv, "c_new_album_day1")
	while n.phase == GameState.Phase.DAY:
		n.tick(0.5)
	await _wait(30)
	_shot("d_new_night1")
	quit()
