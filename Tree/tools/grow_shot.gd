extends SceneTree
## Grows a tree for N in-game days with the root bot and calm days, then takes screenshots of
## the tree view at noon from three sides. For judging the look of the growth.
## Run: godot --path . -s tools/grow_shot.gd -- --days=10 --shots=C:/some/folder [--seed=42] [--boost]

var shots_dir := ""
var days := 10
var seed := 42
var boost := false
var view: TreeView
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a == "--boost":
			boost = true
	DirAccess.make_dir_recursive_absolute(shots_dir)
	var g := GameState.new_game(seed)
	for day in range(days):
		g.dive()
		var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
		g.start_run(start)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if day < days - 1:
			while g.phase == GameState.Phase.DAY:
				# Optional: boost through the mornings, so the crown should lean east.
				g.sim.clock.boost_active = boost and g.sim.clock.time_of_day < 0.2
				g.tick(0.5)
	# Stop at noon of the last day.
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.5:
		g.tick(0.5)
	g.take_events()
	print("day %d: %d nodes, %.1f m, %d tips, crown centre %s" % [g.day_number(), g.sim.graph.size(), g.sim.height(), g.sim.tip_count(), g.sim.centroid()])
	view = TreeView.new()
	root.add_child(view)
	set_meta("game", g)


func _process(_delta: float) -> bool:
	frame += 1
	if frame == 1:
		# After the view's own _ready, which runs once the tree starts.
		view.setup(get_meta("game"))
		view.set_hud_visible(false)
	var names := ["from_north", "from_east", "from_south"]
	var yaws := [PI, PI * 0.5, 0.0]
	var i := frame / 20
	if frame % 20 == 0 and i - 1 < names.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("tree_day%d_%s.png" % [days, names[i - 1]]))
	if i < names.size():
		view._yaw = yaws[i]
		view._pitch = 0.12
	else:
		quit()
	return false
