extends SceneTree
## Prints how the shed's view frames the room on a few screen shapes (0.8.2.2, the tighter view):
## the field of view, the tilt, and the share of the screen below the workbench's front foot
## (floor). Run: godot --headless --path . -s tools/shed_frame.gd


func _initialize() -> void:
	var shed := Shed.new()
	root.add_child(shed)
	await process_frame
	var bench := shed.get_node("Workbench") as Node3D
	var box := _box(bench)
	print("bench box (shed frame): ", box)
	for aspect: float in [450.0 / 1000.0, 450.0 / 800.0, 1080.0 / 2400.0]:
		var vp_h := 1000.0
		var vp_w: float = vp_h * aspect
		shed._fit_aspect = -1.0
		shed._fit_for(aspect)
		var cam := shed.camera
		var front_y := _screen_y(cam, Vector3(0.0, 0.1, box.position.z), vp_w, vp_h)
		var top_y := _screen_y(cam, Vector3(0.0, Shed.BENCH_TOP, box.position.z), vp_w, vp_h)
		print("aspect %.3f: fov %.1f pitch %.1f, bench top edge at %.0f%%, bench foot at %.0f%% (floor below: %.0f%%)" % [aspect, cam.fov, rad_to_deg(shed._fit_pitch), top_y * 100.0, front_y * 100.0, (1.0 - front_y) * 100.0])
		var inv := Transform3D(cam.basis, cam.position).affine_inverse()
		var worst := ""
		for q in shed.must_see():
			var c := inv * q
			worst += " %.2f/%.2f" % [absf(c.x) / -c.z / aspect, absf(c.y) / -c.z]
		print("   need (x / y tan) per point:", worst)
	quit()


## Screen height share (0 top .. 1 bottom) of a shed-frame point for this camera.
func _screen_y(cam: Camera3D, p: Vector3, w: float, h: float) -> float:
	var inv := Transform3D(cam.basis, cam.position).affine_inverse()
	var c := inv * p
	var t := tan(deg_to_rad(cam.fov * 0.5))
	return 0.5 + (c.y / c.z) / t * 0.5


func _box(n: Node3D) -> AABB:
	var acc: Array = [null]
	_gather(n, Transform3D(Basis(), n.position), acc)
	return acc[0]


func _gather(n: Node, xf: Transform3D, acc: Array) -> void:
	for c in n.get_children():
		var t := xf
		if c is Node3D:
			t = xf * (c as Node3D).transform
		if c is MeshInstance3D and (c as MeshInstance3D).mesh != null:
			var a := t * (c as MeshInstance3D).get_aabb()
			acc[0] = a if acc[0] == null else (acc[0] as AABB).merge(a)
		_gather(c, t, acc)
