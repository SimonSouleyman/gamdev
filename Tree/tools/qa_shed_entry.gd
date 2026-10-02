extends SceneTree
## 0.8.2.1 QA: films the trip into the shed frame by frame (phone test: a white frame and a
## 108-138 ms hitch when the house icon was tapped). Prints each frame's time and brightness and
## saves the brightest frames. Run: godot --path . -s tools/qa_shed_entry.gd -- --shots=<folder>

var main: Node
var shots := ""
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
	DirAccess.make_dir_recursive_absolute(shots)
	Photos.DIR = "user://tool_shed_photos"
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
	main.start(g)
	await _frames(10)
	main.leave_shed()
	await _frames(90)
	for trip in range(2):
		print("trip ", trip)
		main.enter_shed(true)
		var t0 := Time.get_ticks_usec()
		for i in range(50):
			await process_frame
			await RenderingServer.frame_post_draw
			var img := root.get_viewport().get_texture().get_image()
			var lum := _mean(img)
			var now := Time.get_ticks_usec()
			print("  frame %2d  %6.1f ms  fade %.2f  lum %.3f" % [i, (now - t0) / 1000.0, main._fade.color.a, lum])
			t0 = now
			if lum > 0.6:
				img.save_png(shots.path_join("white_%d_%02d.png" % [trip, i]))
		main.leave_shed()
		await _frames(60)
	quit()


func _frames(n: int) -> void:
	for _i in range(n):
		await process_frame


func _mean(img: Image) -> float:
	var s := 0.0
	var n := 0
	for y in range(0, img.get_height(), 40):
		for x in range(0, img.get_width(), 40):
			s += img.get_pixel(x, y).get_luminance()
			n += 1
	return s / maxf(n, 1)
