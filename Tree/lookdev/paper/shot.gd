extends SceneTree
## Photographs the paper look-dev board (and each view) for judging the look.
## Run: godot --path . --rendering-driver vulkan --resolution 720x1280 -s lookdev/paper/shot.gd -- --shots=<folder> [--views=board,book,page]

var shots_dir := "user://paper_shots"
var views: PackedStringArray = PackedStringArray(["board", "book", "page"])
var frame := 0
var idx := 0
var scene: Control


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--views="):
			views = a.substr(8).split(",")
	DirAccess.make_dir_recursive_absolute(shots_dir)


func _process(_delta: float) -> bool:
	if scene == null:
		if idx >= views.size():
			quit()
			return false
		scene = load("res://lookdev/paper/paper_lookdev.tscn").instantiate()
		scene.view = views[idx]
		root.add_child(scene)
		frame = 0
		return false
	frame += 1
	if frame == 6:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("after_%s.png" % views[idx]))
		scene.queue_free()
		scene = null
		idx += 1
	return false
