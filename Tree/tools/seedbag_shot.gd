extends SceneTree
## Close-ups of one thing on the shed's workbench from four sides (0.8.2.7: the seed bag).
## Run: godot --path . --resolution 600x600 -s tools/seedbag_shot.gd -- --shots=C:/some/folder [--thing=seeds]

var shots := ""
var thing := "seeds"


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--thing="):
			thing = a.substr(8)
	DirAccess.make_dir_recursive_absolute(shots)
	_run.call_deferred()


func _run() -> void:
	var shed := Shed.new()
	root.add_child(shed)
	# A soft room ambient (the game's own world is not loaded here), so the far sides are not black.
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.2, 0.17, 0.13)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.85, 0.75, 0.62)
	env.ambient_light_energy = 0.45
	var we := WorldEnvironment.new()
	we.environment = env
	root.add_child(we)
	for i in range(5):
		await process_frame
	var item: Node3D = shed._items[thing]
	var centre := item.global_position + Vector3(0, 0.08, 0)
	var cam := Camera3D.new()
	cam.fov = 40.0
	root.add_child(cam)
	cam.current = true
	# Front (the room's side), both sides and from behind, a little from above.
	var front := -shed.global_transform.basis.z
	var names := ["front", "left", "right", "back"]
	var turns := [0.0, PI * 0.5, -PI * 0.5, PI]
	for k in range(4):
		var dir: Vector3 = front.rotated(Vector3.UP, turns[k])
		cam.global_position = centre + dir * 0.42 + Vector3(0, 0.2, 0)
		cam.look_at(centre, Vector3.UP)
		for i in range(8):
			await process_frame
		var img := root.get_texture().get_image()
		img.save_png(shots.path_join("%s_%s.png" % [thing, names[k]]))
	quit()
