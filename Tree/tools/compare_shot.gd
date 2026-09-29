extends SceneTree
## Look comparison sheet for the hero tree: grows one tree with the root bot and photographs it
## on the given days (noon, summer, the game's own camera), and on the last day also at dawn,
## at golden hour, in autumn and from the sunny side. For before/after reviews of the crown.
## Run: godot --path . [--rendering-method gl_compatibility] -s tools/compare_shot.gd --
##   --shots=<folder> [--species=linden] [--days=2,8,20] [--seed=42] [--tag=before] [--phone]
##   [--album] (also the album's morning photo framing on each day)

var shots_dir := ""
var species := "linden"
var seed := 42
var days: Array[int] = [2, 8, 20]
var tag := ""
var album := false
var view: TreeView
var g: GameState


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--tag="):
			tag = a.substr(6) + "_"
		elif a == "--album":
			album = true
		elif a.begins_with("--days="):
			days.clear()
			for s in a.substr(7).split(","):
				days.append(int(s))
	DirAccess.make_dir_recursive_absolute(shots_dir)
	Almanac.weather_override = "clear"
	Almanac.season_override = "summer"
	g = GameState.new_game(seed, species)
	view = TreeView.new()
	root.add_child(view)
	_run.call_deferred()


func _run() -> void:
	await process_frame
	view.set_hud_visible(false)
	var grown := 0
	for target in days:
		while grown < target:
			_grow_one_day(grown == target - 1)
			grown += 1
		print("%s day %d: %d nodes, %.1f m, %d tips" % [species, g.day_number(), g.sim.graph.size(), g.sim.height(), g.sim.tip_count()])
		view.setup(g)
		view.set_hud_visible(false)
		print("  crown: %d sprays in %d leaf masses, %d leafy nodes" % [view._leaves.multimesh.instance_count, HeroCrown.last_masses, HeroCrown.last_leafy])
		await _shot("d%d_noon" % target, 0.5, "summer", PI)
		if album and view.has_method("album_pose"):
			await _album_shot("d%d_album" % target)
		if target == days[days.size() - 1]:
			await _shot("d%d_dawn" % target, 0.06, "summer", PI)
			await _shot("d%d_golden" % target, 0.9, "summer", PI)
			await _shot("d%d_noon_sunside" % target, 0.5, "summer", 0.5)
			await _shot("d%d_autumn_noon" % target, 0.5, "autumn", PI)
			await _shot("d%d_autumn_golden" % target, 0.9, "autumn", PI)
		# Back to where the growth loop left off: midday of the target day.
		g.sim.clock.time_of_day = g.sim.clock.daylight_fraction * 0.5
	quit()


## One night with the bot and the day after it; the last day stops at noon.
func _grow_one_day(stop_at_noon: bool) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
	g.start_run(start)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)
	var until := 0.5 if stop_at_noon else 1.0
	while g.phase == GameState.Phase.DAY and g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * until:
		g.tick(0.5)
	g.take_events()


func _shot(name: String, hour: float, season: String, yaw: float) -> void:
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction * hour
	Almanac.season_override = season
	view._mood_day = -1
	view._yaw = yaw
	view._pitch = 0.12
	view._zoom = 1.0
	view._frame_camera(true)
	for _i in range(12):
		await process_frame
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%s%s_%s.png" % [tag, species, name]))


func _album_shot(name: String) -> void:
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction * 0.12
	Almanac.season_override = "summer"
	view._mood_day = -1
	view.call("album_pose", true)
	for _i in range(12):
		await process_frame
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%s%s_%s.png" % [tag, species, name]))
	view.call("album_pose", false)
