extends SceneTree
## 0.8.2.2 QA: films the switches between the views frame by frame (phone, 0.8.2.1: a long hitch
## and a frame from a wrong camera on the way back to the day, after the root run and out of the
## shed; flat grey frames on the dive; the corner pictures a frame after the journal). Prints each
## frame's time, the fade, the drawing camera and its height above the meadow and the corner
## pictures, and saves the first frames that are not fully black after each switch. Then takes an
## album photo off screen and checks the screen's camera never moved.
## Run: godot --path . --rendering-method gl_compatibility --resolution 450x1000 -s tools/qa_switch.gd -- --shots=<folder> --phone

var main: Node
var shots := ""
var frame := 0
var failed := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
	if shots != "":
		DirAccess.make_dir_recursive_absolute(shots)
	Photos.DIR = "user://tool_switch_photos"
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _process(_d: float) -> bool:
	frame += 1
	if frame == 2:
		_run()
	return false


func _run() -> void:
	var g := GameState.new_game(42)
	_to_second_sunset(g)
	# 0.8.2.4: the sunset itself, filmed (the phone's dusk turned to night in one frame).
	main.start(g)
	await _frames(30)
	await _film_sunset()
	await _frames(30)
	print("== dive")
	main._on_ground_tapped()
	await _film("dive", 150)
	# Tonight ends at once (0.8.2.2): small roots everywhere, then the morning.
	var guard := 0
	while (main.journal.is_open() or not main.root_view.input_enabled) and guard < 300:
		if main.journal.is_open():
			main.journal.close_page()
			main.journal.close_diary()
		await process_frame
		guard += 1
	main.root_view.end_early()
	print("ended at once: side nodes %d" % main.root_view.roots.side_nodes_grown[0])
	guard = 0
	while main.state.phase != GameState.Phase.DAY and guard < 3000:
		if main.journal.is_open():
			main.journal.close_page()
			main.journal.close_diary()
		await process_frame
		guard += 1
	print("== rise (day %d)" % main.state.day_number())
	await _film("rise", 220)
	await _frames(30)
	print("== shed in")
	main.enter_shed(true)
	await _film("shed_in", 60)
	await _frames(20)
	# 0.8.2.4: the glide to the bonsai on the sill (the scraps came before the camera, one blank,
	# and the glide skimmed the workbench).
	print("== bonsai in")
	main.state.ensure_bonsai(true)
	main.bonsai_view.setup(main.state)
	main.bonsai_hud.state = main.state
	main.shed.bonsai_ready = true
	main.enter_bonsai()
	await _film_bonsai()
	# The milestone photo, off screen: the screen's camera stays, the screen is not read.
	var bcam: Camera3D = main.bonsai_view.camera
	var bpose := bcam.global_transform
	var bshot: Image = await Photos.shoot(bcam, Photos.screen_size(main))
	print("bonsai photo: %s, screen camera moved %.3f m" % [str(bshot.get_size()) if bshot != null else "none", bcam.global_transform.origin.distance_to(bpose.origin)])
	if bshot != null and shots != "":
		bshot.save_png(shots.path_join("bonsai_offscreen.png"))
	main.leave_bonsai()
	await _frames(40)
	print("== shed out")
	main.leave_shed()
	await _film("shed_out", 80)
	# The album photo, off screen: the screen's camera stays where it is.
	var cam: Camera3D = main.tree_view.camera
	var before := cam.global_transform
	var img: Image = await main.tree_view.album_photo()
	var moved := cam.global_transform.origin.distance_to(before.origin)
	print("album photo: %s, screen camera moved %.3f m" % [str(img.get_size()) if img != null else "none", moved])
	if moved > 0.01:
		failed = true
	if img != null and shots != "":
		img.save_png(shots.path_join("album_offscreen.png"))
	quit(1 if failed else 0)


## The first night with the root bot, then the next day to its sunset.
func _to_second_sunset(g: GameState) -> void:
	var bot := RootBot.new()
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(0)
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	# (0.8.2.4: stops a second of play before the sunset, which _film_sunset films.)
	while g.phase == GameState.Phase.DAY and g.sim.clock.time_of_day < g.sim.clock.daylight_fraction - 0.006:
		g.tick(0.25)
	g.take_events()


func _film(tag: String, n: int) -> void:
	var t0 := Time.get_ticks_usec()
	var saved := 0
	var was_black := false
	for i in range(n):
		await process_frame
		await RenderingServer.frame_post_draw
		var now := Time.get_ticks_usec()
		var fade: float = main._fade.color.a
		var cam := root.get_viewport().get_camera_3d()
		var above := cam.global_position.y - Terrain.height(cam.global_position.x, cam.global_position.z) if cam != null else 0.0
		var corner: bool = main._shed_button.visible
		var jb: bool = main.journal._open_button.visible
		print("  %s %3d %6.1f ms fade %.2f cam %s y%+.2f corner %s journal %s" % [tag, i, (now - t0) / 1000.0, fade, cam.get_parent().name if cam != null else "-", above, corner, jb])
		t0 = now
		if fade >= 0.99:
			was_black = true
		elif shots != "" and tag == "rise" and i in [30, 45, 70, 110]:
			# (0.8.2.4) the sunrise once the picture shows: above the grass, outside the crown.
			root.get_viewport().get_texture().get_image().save_png(shots.path_join("%s_%03d.png" % [tag, i]))
			t0 = Time.get_ticks_usec()
		elif was_black and saved < 4 and shots != "":
			var img := root.get_viewport().get_texture().get_image()
			img.save_png(shots.path_join("%s_%02d.png" % [tag, i]))
			saved += 1
			t0 = Time.get_ticks_usec()
		# Upside down (0.8.2.4): the camera's up must point up.
		if cam != null and cam.global_basis.y.y < 0.0:
			print("  !! %s frame %d: the camera is rolled over (up %s)" % [tag, i, str(cam.global_basis.y)])
			failed = true
		# The sunrise: above the grass and outside the crown once the picture shows (0.8.2.4).
		if tag == "rise" and fade < 0.98 and cam == main.tree_view.camera:
			var flat := Vector2(cam.global_position.x, cam.global_position.z).length()
			var in_crown: bool = flat < main.tree_view.crown_reach() and cam.global_position.y < main.state.sim.height()
			if above < 1.0 or in_crown:
				print("  !! rise frame %d: camera %.2f m above the meadow, %.2f m from the trunk (crown %.2f m)" % [i, above, flat, main.tree_view.crown_reach()])
				failed = true
		# The tree's camera under the meadow while the picture shows (the grey slab).
		if fade < 0.98 and cam == main.tree_view.camera and above < 0.0:
			print("  !! %s frame %d: the tree camera is under the ground with the fade at %.2f" % [tag, i, fade])
			failed = true


## The sunset, frame by frame: the sun's light and the haze must not jump (0.8.2.4).
func _film_sunset() -> void:
	print("== sunset")
	var tv: TreeView = main.tree_view
	var light: DirectionalLight3D = tv._sun_light
	var last_e := -1.0
	var worst := 0.0
	for i in range(90):
		await process_frame
		var e := light.light_energy
		if last_e > 0.0:
			worst = maxf(worst, absf(e - last_e) / maxf(last_e, 0.01))
		if i % 6 == 0 or (last_e > 0.0 and absf(e - last_e) / maxf(last_e, 0.01) > 0.05):
			print("  sunset %2d phase %d light %.3f fog %s" % [i, main.state.phase, e, str(tv._env.fog_light_color)])
		last_e = e
	print("sunset: largest light step in one frame %.0f %%" % (worst * 100.0))
	if worst > 0.15:
		print("  !! the light jumped at sunset")
		failed = true


## The glide to the sill: when the scraps show (with their words) and how high over the bench.
func _film_bonsai() -> void:
	var bench_y: float = main.shed.to_global(Vector3(0.0, Shed.BENCH_TOP, 0.0)).y
	var low := INF
	var hud_early := false
	var saved := 0
	for i in range(60):
		await process_frame
		await RenderingServer.frame_post_draw
		var cam: Camera3D = main.bonsai_view.camera
		var arrived: bool = main.bonsai_view.arrived()
		var hud: bool = main.bonsai_hud.visible
		low = minf(low, cam.global_position.y - bench_y)
		print("  bonsai %2d cam %.3f m over the bench, arrived %s, scraps %s '%s'" % [i, cam.global_position.y - bench_y, arrived, hud, main.bonsai_hud._status.text])
		if hud and not arrived:
			hud_early = true
		if hud and main.bonsai_hud._status.text == "":
			print("  !! a blank scrap")
			failed = true
		if cam.global_basis.y.y < 0.0:
			print("  !! bonsai frame %d: rolled over" % i)
			failed = true
		if shots != "" and (i % 8 == 0 or (hud and saved == 0)):
			root.get_viewport().get_texture().get_image().save_png(shots.path_join("bonsai_%02d.png" % i))
			if hud:
				saved += 1
	print("bonsai glide: lowest %.2f m over the bench, scraps before arrival %s" % [low, hud_early])
	if hud_early:
		failed = true


func _frames(n: int) -> void:
	for _i in range(n):
		await process_frame
