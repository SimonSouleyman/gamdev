extends SceneTree

func _init() -> void:
	var root3 := Node3D.new()
	get_root().add_child(root3)
	var r := BlockRenderer.new()
	root3.add_child(r)
	var cam := Camera3D.new()
	cam.position = Vector3(0, 0, 20)
	root3.add_child(cam)
	await process_frame
	for frame in 3:
		r.begin_frame()
		for c in 8:
			r.block(0, c, 3, -14 + c * 4, 0, 0, 0, 1.5, 1.5, 1)
		r.end_frame()
		await process_frame
	await RenderingServer.frame_post_draw
	var img := get_root().get_texture().get_image()
	var w := img.get_width()
	var h := img.get_height()
	for c in 8:
		var x := int(w / 2 + (-14 + c * 4) / 20.0 * w / 2)
		print(c, " ", img.get_pixel(x, h / 2), " expected ", BlockRenderer.COLORS[c] * 0.9)
	quit()
