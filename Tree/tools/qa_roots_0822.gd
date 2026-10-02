extends SceneTree
## 0.8.2.2 QA for root mode (specs/0.8.md 0.8.2.2): plays eight bot nights, then
##  - photographs the old roots from close by (the phone's dotted bright streaks were cut-away old
##    roots near the camera leaving single pixels, tree/bark.gdshader),
##  - grows a root whose held heading runs straight at an older main root, with the stick at
##    rest, and photographs it going round (third person, then the settled overview); prints the
##    closest it came to that root and how far its travel ended off the held heading,
##  - ends the next night at once and photographs the small roots.
## Run: godot --path . --rendering-method gl_compatibility --resolution 450x1000 -s tools/qa_roots_0822.gd -- --shots=<folder> --phone

const FRAME := 1.0 / 30.0
var g: GameState
var rv: RootView
var out := ""
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			out = a.substr(8)
	DirAccess.make_dir_recursive_absolute(out)
	g = GameState.new_game(42, "linden")
	var bot := RootBot.new()
	for _day in range(8):
		g.dive()
		if g.can_start_run():
			g.start_run(bot.pick_start(g.roots, g.ground, g.sim.resources))
			var b := RootBot.new()
			var guard := 0
			while guard < 30000 and g.steer(b.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
				guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	print("root nodes %d, main roots %d" % [g.roots.graph.size(), g.roots.main_root_count])
	if Budgets.PHONE:
		root.scaling_3d_scale = 0.5
		root.msaa_3d = Viewport.MSAA_2X
	rv = load("res://roots/root_view.tscn").instantiate()
	root.add_child(rv)


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _run() -> void:
	g.dive()
	rv.setup(g.ground, g.roots, g.sim.resources)
	rv.can_start = func() -> bool: return g.can_start_run()
	rv.begin_pick()
	await _frames(5)
	# Close to the old roots, as the run's camera passes them.
	var gr := g.roots.graph
	var views := [[0.6, 0.3, 1.0, 5], [2.0, -0.2, 0.9, 40], [3.0, 0.5, 1.2, 200], [4.5, 0.1, 1.0, 600]]
	for i in range(views.size()):
		var v: Array = views[i]
		rv._orbit_yaw = v[0]
		rv._orbit_pitch = v[1]
		rv._orbit_distance = v[2]
		rv._look = gr.positions[int(v[3]) % gr.size()]
		rv._look_now = rv._look
		rv._snap_camera()
		await _frames(6)
		_shot("near_old_roots_%d" % i)
	# A start whose straight line meets another main root 2 to 4 m ahead, square on.
	var pick := _crossing_start()
	if pick.is_empty():
		print("no crossing found")
		quit(1)
		return
	var start: int = pick[0]
	var held: Vector3 = pick[1]
	var other: int = pick[2]
	rv.start_at(start)
	g.mark_run_started()
	g.roots.heading = held
	g.roots.travel = held
	g.roots._update_right()
	rv._waiting_for_input = false
	rv.scripted_stick = Vector2.ZERO
	var closest := INF
	var shots := 0
	var t := 0
	while g.roots.run_active and t < 900:
		await process_frame
		t += 1
		closest = minf(closest, _to_main(g.roots.tip_position, other))
		if t % 20 == 0 and shots < 6:
			_shot("around_%d" % shots)
			shots += 1
		if t > 30 and g.roots.run_length > 6.0:
			rv.end_early()
			break
	var off := rad_to_deg(g.roots.travel.angle_to(g.roots.heading))
	print("around an old root: closest %.2f m (hard %.2f), held heading turned %.1f deg, travel off it %.1f deg" % [closest, RootSystem.AVOID_HARD, rad_to_deg(g.roots.heading.angle_to(held)), off])
	await _frames(200)
	_shot("around_settled")
	# The next night ends at once.
	g.notify_run_done()
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	rv.setup(g.ground, g.roots, g.sim.resources)
	rv.begin_pick()
	await _frames(5)
	rv.end_early()
	print("ended at once: %d side nodes, life force %.1f" % [g.roots.side_nodes_grown[0] + g.roots.side_nodes_grown[1], g.sim.resources.life_force])
	await _frames(200)
	_shot("at_once_settled")
	quit(0)


## [start node, held heading, the other root's main index]: a node on a main root from which a
## flat line meets a different main root square on between 2 and 4 m ahead.
func _crossing_start() -> Array:
	var gr := g.roots.graph
	for id in range(gr.size() - 1, 0, -7):
		var m := int(gr.get_flag(id, "main", -1))
		if m < 0 or gr.get_flag(id, "fine", -1) >= 0:
			continue
		for other in range(1, gr.size(), 3):
			var m2 := int(gr.get_flag(other, "main", -1))
			if m2 < 0 or m2 == m or gr.get_flag(other, "fine", -1) >= 0:
				continue
			var to := gr.positions[other] - gr.positions[id]
			var d := to.length()
			if d < 2.0 or d > 4.0 or absf(to.y) > 0.6:
				continue
			var along := gr.direction_of(other)
			if absf(along.dot(to / d)) > 0.4:
				continue
			# Nothing else of the old roots in the way before it.
			return [id, Vector3(to.x, 0.0, to.z).normalized(), m2]
	return []


func _to_main(p: Vector3, main: int) -> float:
	var gr := g.roots.graph
	var best := INF
	for id in range(1, gr.size()):
		if int(gr.get_flag(id, "main", -1)) != main or gr.get_flag(id, "fine", -1) >= 0:
			continue
		best = minf(best, p.distance_to(Geometry3D.get_closest_point_to_segment(p, gr.positions[gr.parents[id]], gr.positions[id])))
	return best


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	var img := root.get_viewport().get_texture().get_image()
	img.save_png(out.path_join(name + ".png"))
	print("shot ", name)


func _frames(n: int) -> void:
	for _i in range(n):
		await process_frame
