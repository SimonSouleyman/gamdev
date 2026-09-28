extends SceneTree
## Grows a tree for N in-game days with the root bot and calm days, then takes screenshots of
## the tree view at noon from three sides. For judging the look of the growth.
## Run: godot --path . -s tools/grow_shot.gd -- --days=10 --shots=C:/some/folder [--seed=42] [--species=oak] [--boost] [--dive]
## --dive: instead, five frames of the fall into the ground (dive_amount 0 to 1) from the south.
## Mood (section 17): --season=spring|summer|autumn|late_autumn, --weather=rain|mist|dew|clear,
## --moon=<phase 0..1>, --date=YYYY-MM-DD (Almanac.read_cmdline); --night=<0..1> photographs the
## sunset hold that far into the night instead of noon; --yaw=<radians> and --pitch= one view only;
## --settle=<frames> waits before the first photo; --face_moon turns the view toward the moon; --look_up=<radians> tilts the camera toward the sky; --tag=<name> prefixes the file names.

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
var night := -1.0
var only_yaw := -100.0
var pitch := 0.12
var tag := ""
var look_up := 0.0
var face_moon := false
var settle := 0


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
		elif a == "--prune":
			prune = true
		elif a == "--dive":
			dive = true
		elif a == "--boost":
			boost = true
		elif a.begins_with("--night="):
			night = float(a.substr(8))
		elif a.begins_with("--yaw="):
			only_yaw = float(a.substr(6))
		elif a.begins_with("--pitch="):
			pitch = float(a.substr(8))
		elif a.begins_with("--settle="):
			settle = int(a.substr(9))
		elif a == "--face_moon":
			face_moon = true
		elif a.begins_with("--look_up="):
			look_up = float(a.substr(10))
		elif a.begins_with("--tag="):
			tag = a.substr(6) + "_"
	Almanac.read_cmdline()
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
	# Stop at noon of the last day (or at sunset for --night).
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * hour and g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	while night >= 0.0 and g.phase == GameState.Phase.DAY:
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
		view.night_override = night
		view.look_up = look_up
		if face_moon:
			# Stand opposite the moon, so it hangs above the tree.
			var d := view._night_sky.moon_direction()
			only_yaw = atan2(-d.x, -d.z)
	if dive:
		return _dive_frames()
	if prune:
		return _prune_frames()
	var names := ["from_north", "from_east", "from_south"]
	var yaws := [PI, PI * 0.5, 0.0]
	if only_yaw > -99.0:
		names = ["view"]
		yaws = [only_yaw]
	# --settle: let rain and falling leaves fill the air before the first photo.
	var f := frame - settle
	if f < 1:
		view._yaw = yaws[0]
		view._pitch = pitch
		return false
	var i := f / 20
	if f % 20 == 0 and i - 1 < names.size():
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("%stree_day%d_h%d_%s.png" % [tag, days, int(hour * 100), names[i - 1]]))
	if i < names.size():
		view._yaw = yaws[i]
		view._pitch = pitch
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
