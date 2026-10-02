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
	main.start(g)
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
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
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
		elif was_black and saved < 4 and shots != "":
			var img := root.get_viewport().get_texture().get_image()
			img.save_png(shots.path_join("%s_%02d.png" % [tag, i]))
			saved += 1
			t0 = Time.get_ticks_usec()
		# The tree's camera under the meadow while the picture shows (the grey slab).
		if fade < 0.98 and cam == main.tree_view.camera and above < 0.0:
			print("  !! %s frame %d: the tree camera is under the ground with the fade at %.2f" % [tag, i, fade])
			failed = true


func _frames(n: int) -> void:
	for _i in range(n):
		await process_frame
