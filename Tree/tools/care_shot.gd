extends SceneTree
## Care shots (0.6.3): grows a tree with the root bot, then photographs it healthy, thirsty and
## short of nitrogen (summer and autumn), a cut before, just after and the next day, and the
## journal's care page. The needs come from the game itself (a night that brought none of that
## kind); a sign the game would not show (no deposit in reach) is reported, not forced.
## Run: godot --path . -s tools/care_shot.gd -- --shots=<folder> [--seed=42] [--species=linden] [--days=14]

var shots_dir := ""
var species := "linden"
var seed := 42
var days := 14
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
		elif a.begins_with("--days="):
			days = int(a.substr(7))
	DirAccess.make_dir_recursive_absolute(shots_dir)
	Almanac.weather_override = "clear"
	Almanac.season_override = "summer"
	g = GameState.new_game(seed, species)
	view = TreeView.new()
	root.add_child(view)
	_run.call_deferred()


func _run() -> void:
	await process_frame
	for d in range(days):
		_grow_one_day(d == days - 1)
	print("%s day %d: %d nodes, %.1f m" % [species, g.day_number(), g.sim.graph.size(), g.sim.height()])
	view.setup(g)
	view.set_hud_visible(false)
	var healthy := g.sim.resources.stock.duplicate()
	for season in ["summer", "autumn"]:
		_need(-1, healthy)
		await _shot("healthy_%s" % season, season)
		_need(Resources.Kind.WATER, healthy)
		await _shot("thirsty_%s" % season, season)
		_need(Resources.Kind.NITROGEN, healthy)
		await _shot("nitrogen_short_%s" % season, season)
		_need(Resources.Kind.POTASSIUM, healthy)
		await _shot("potassium_short_%s" % season, season)
	_need(-1, healthy)
	# The care page, with the tree short of water.
	_need(Resources.Kind.WATER, healthy)
	await _care_page("care_page")
	_need(-1, healthy)
	await _cut_shots()
	quit()


## Makes last night one that brought none of `kind` (-1: a night that brought plenty), and
## holds the sign at full strength (the morning's easing is over).
func _need(kind: int, healthy: PackedFloat32Array) -> void:
	for k in range(4):
		g.sim.resources.stock[k] = maxf(healthy[k], 400.0)
	if kind >= 0:
		g.sim.resources.stock[kind] = 0.0
	g.sim.assess_needs()
	g.sim.care_prev = g.sim.care_need.duplicate()
	var sig := g.care_signals()
	print("  need %s -> shown %s" % [Resources.KIND_NAMES[kind] if kind >= 0 else "none", sig])
	if kind >= 0 and sig[kind] <= 0.0:
		print("  (no %s deposit in reach tonight: the game would show nothing)" % Resources.KIND_NAMES[kind])


func _cut_shots() -> void:
	var sim := g.sim
	var cam_side := Vector3(view.camera.global_position.x, 0.0, view.camera.global_position.z).normalized()
	var best := -1
	var best_score := -INF
	for id in range(3, sim.graph.size()):
		if sim.graph.get_flag(id, "dead", false):
			continue
		var n := sim._subtree_size(id)
		var p := sim.graph.positions[id]
		if n < 20 or n > 60 or p.y < sim.height() * 0.35:
			continue
		var score := Vector3(p.x, 0.0, p.z).dot(cam_side) + n * 0.02
		if score > best_score:
			best_score = score
			best = id
	if best < 0:
		print("  no branch to cut")
		return
	var at := sim.graph.positions[best]
	# The whole tree from the cut's side, a little closer; the same camera for all three.
	view._yaw = atan2(at.x, at.z)
	view._pitch = 0.12
	view._zoom = 0.5
	view.pruning.preview(best)
	await _shot("cut_1_before", "summer", false)
	view.pruning.cut()
	await create_timer(3.5).timeout
	# The ring stays where the cut was, so the next shots show the place.
	var ring_at: Vector3 = view.pruning._marker.position
	view.pruning._marker.visible = true
	view.pruning._marker.position = ring_at
	await _shot("cut_2_just_after", "summer", false)
	print("  cut %d segments at %s; refund at dawn %d" % [int(sim.last_cut["nodes"]), at, sim.pending_refund()])
	_grow_one_day(true)
	view.pruning._marker.visible = true
	view.pruning._marker.position = ring_at
	await _shot("cut_3_next_day", "summer", false)
	print("  last cut: %s" % [sim.last_cut])
	await _care_page("care_page_after_cut")


## One night with the bot and the day after it; the last day stops at noon.
func _grow_one_day(stop_at_noon: bool) -> void:
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
	var until := 0.5 if stop_at_noon else 1.0
	while g.phase == GameState.Phase.DAY and g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * until:
		g.tick(0.5)
	g.take_events()


func _shot(name: String, season: String, reset_camera: bool = true) -> void:
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction * 0.5
	Almanac.season_override = season
	view._mood_day = -1
	if reset_camera:
		view._yaw = PI
		view._pitch = 0.12
		view._zoom = 1.0
	view._rebuild()
	view._frame_camera(true)
	for _i in range(12):
		await process_frame
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%s_%s.png" % [species, name]))


func _care_page(name: String) -> void:
	var journal := Journal.new()
	root.add_child(journal)
	journal.state = g
	await process_frame
	journal.open_care()
	for _i in range(20):
		await process_frame
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%s_%s.png" % [species, name]))
	for part in Care.page(g):
		print("  [%s] %s" % [part["title"], part["text"]])
	journal.queue_free()
	await process_frame
