extends SceneTree
## Grows a tree for N in-game days with the root bot and calm days, then takes screenshots of
## the tree view at noon from three sides. For judging the look of the growth.
## Run: godot --path . -s tools/grow_shot.gd -- --days=10 --shots=C:/some/folder [--seed=42] [--species=oak] [--boost] [--dive]
## --dive: instead, five frames of the fall into the ground (dive_amount 0 to 1) from the south.
## --pitch=0.5 --zoom=0.8: look down more / step closer (the ground under the crown).
## --rain: a shower on the last day (Clearing.after_rain), so the mushrooms are up.

var shots_dir := ""
var days := 10
var seed := 42
var species := "linden"
var boost := false
var hour := 0.5  # fraction of the daylight, 0.5 = noon
var view: TreeView
var frame := 0
var dive := false
var prune := false
var pitch := 0.12
var zoom := 1.0
var rain := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--hour="):
			hour = float(a.substr(7))
		elif a.begins_with("--pitch="):
			pitch = float(a.substr(8))
		elif a.begins_with("--zoom="):
			zoom = float(a.substr(7))
		elif a == "--rain":
			rain = true
		elif a == "--prune":
			prune = true
		elif a == "--dive":
			dive = true
		elif a == "--boost":
			boost = true
	DirAccess.make_dir_recursive_absolute(shots_dir)
	var g := GameState.new_game(seed, species)
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
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * hour:
		g.tick(0.5)
	if rain:
		g.after_rain()
	g.take_events()
	print("clearing: found %s, plan %s" % [g.clearing.found, Clearing.counts(g.clearing.plan(Clearing.shade_map(g.sim), g.day_number(), Budgets.UNDERSTORY_PLANTS))])
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
	if dive:
		return _dive_frames()
	if prune:
		return _prune_frames()
	var names := ["from_north", "from_east", "from_south"]
	var yaws := [PI, PI * 0.5, 0.0]
	var i := frame / 20
	if frame % 20 == 0 and i - 1 < names.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("tree_day%d_h%d_%s.png" % [days, int(hour * 100), names[i - 1]]))
	if i < names.size():
		view._yaw = yaws[i]
		view._pitch = pitch
		view._zoom = zoom
	else:
		quit()
	return false


func _dive_frames() -> bool:
	var steps := [0.0, 0.3, 0.55, 0.75, 0.9]
	var i := frame / 15
	view._yaw = 0.0
	view._pitch = 0.12
	if i < steps.size():
		view.dive_amount = steps[i]
	if frame % 15 == 14 and i < steps.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("dive_%d.png" % int(steps[i] * 100)))
	if i >= steps.size():
		quit()
	return false


## --prune: preview a cut on a side branch, cut it, and photograph the fall.
func _prune_frames() -> bool:
	view._yaw = 0.0
	view._pitch = 0.12
	if frame == 20:
		# A side branch around mid height.
		var g := view.state.sim.graph
		var best := -1
		for id in range(3, g.size()):
			if g.children[id].size() > 0 and absf(g.positions[id].y - view.state.sim.height() * 0.5) < 1.0 and Vector2(g.positions[id].x, g.positions[id].z).length() > 1.0:
				best = id
				break
		view.pruning.preview(best)
	for k in [[30, "prune_preview"], [36, "prune_fall1"], [48, "prune_fall2"], [80, "prune_after"]]:
		if frame == k[0]:
			RenderingServer.force_draw(false)
			root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join(k[1] + ".png"))
	if frame == 31:
		print("cut ", view.pruning.cut(), " segments")
		view._rebuild()
	if frame > 82:
		quit()
	return false
