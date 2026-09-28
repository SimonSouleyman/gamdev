extends SceneTree
## Look test: grows a tree for N days (like tools/grow_shot.gd), shows it in the real TreeView,
## then swaps in the look-test crown, light and grade, and photographs it at the given hours.
## Run: godot --path . -s lookdev/lookdev_shot.gd -- --days=20 --shots=<folder> [--hours=0.5,0.92] [--before]
## --before skips the swaps, for side-by-side comparison.

var shots_dir := ""
var days := 20
var seed := 42
var hours: Array[float] = [0.5, 0.93]
var before := false
var view: TreeView
var game: GameState
var frame := 0
var shot := 0
const YAWS := [0.0, PI * 0.5]
const NAMES := ["south", "east"]


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--hours="):
			hours.clear()
			for h in a.substr(8).split(","):
				hours.append(float(h))
		elif a == "--before":
			before = true
	DirAccess.make_dir_recursive_absolute(shots_dir)
	game = GameState.new_game(seed)
	for day in range(days):
		game.dive()
		var start := 0 if game.roots.graph.size() <= 1 else game.roots.graph.size() - 1
		game.start_run(start)
		var bot := RootBot.new()
		var guard := 0
		while game.steer(bot.stick_for(game.roots, game.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while game.phase == GameState.Phase.NIGHT:
			game.tick(0.25)
		if day < days - 1:
			while game.phase == GameState.Phase.DAY:
				game.tick(0.5)
	_advance_to(hours[0])
	game.take_events()
	view = TreeView.new()
	root.add_child(view)


func _advance_to(hour: float) -> void:
	while game.sim.clock.time_of_day < game.sim.clock.daylight_fraction * hour:
		game.tick(0.5)


func _process(_delta: float) -> bool:
	frame += 1
	if frame == 1:
		view.setup(game)
		view.set_hud_visible(false)
		if not before:
			LookDev.apply(view)
	# Each shot: set hour and angle, let a few frames settle (exposure, shadows), then save.
	var per := 12
	var total := hours.size() * YAWS.size()
	var i := (frame - 2) / per
	if frame < 2:
		return false
	if i >= total:
		quit()
		return false
	var hi := i / YAWS.size()
	var yi := i % YAWS.size()
	if (frame - 2) % per == 0:
		_advance_to(hours[hi])
		if not before:
			# Growth during the day rebuilds the old crown; put the look-test one back.
			view._rebuild()
			LookDev.apply(view)
		view._yaw = YAWS[yi]
		view._pitch = 0.1
	if (frame - 2) % per == per - 1:
		RenderingServer.force_draw(false)
		var tag := "before" if before else "after"
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%s_day%d_h%d_%s.png" % [tag, days, int(hours[hi] * 100), NAMES[yi]]))
	return false
