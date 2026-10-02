extends RefCounted
## 0.8.2.7 (docs/notes/0.8.2.7.md): after the run the camera frames tonight's root, eased there
## calmly from the run camera, following the settle's growth; the player's own turn or zoom
## takes the camera back.
var t

const FRAME := 1.0 / 30.0


func _view(g: GameState) -> RootView:
	var rv := RootView.new()
	var holder := Node.new()
	t.root.add_child(holder)
	holder.add_child(rv)
	rv.setup(g.ground, g.roots, g.sim.resources)
	return rv


func _free(rv: RootView) -> void:
	rv.get_parent().queue_free()


## A main root of night `night` from node `from` in a straight line to `to`.
func _line(r: RootSystem, from: int, to: Vector3, night: int) -> int:
	var a := r.graph.positions[from]
	var n := maxi(1, ceili(a.distance_to(to) / 0.25))
	var id := from
	for i in range(1, n + 1):
		id = r.graph.add_node(id, a.lerp(to, float(i) / n))
		r.graph.set_flag(id, "main", night)
	r.main_root_count = maxi(r.main_root_count, night + 1)
	r.graph.update_radii()
	return id


## Old roots, then tonight's run from the newest end, steered in a curve; `end_after` frames of
## run then ended early (0: the whole tank). Stops at the settle's start.
func _run(seed: int, end_after: int) -> RootView:
	var g := GameState.new_game(seed)
	g.sim.resources.life_force = 40.0
	for night in range(4):
		_line(g.roots, 0, Vector3(cos(night * 1.7) * 5.0, -1.2, sin(night * 1.7) * 5.0), night)
	var rv := _view(g)
	t.check(rv.start_at(g.roots.graph.size() - 1), "a run starts")
	rv.scripted_stick = Vector2(0.35, 0.2)
	var guard := 0
	while rv.mode == RootView.Mode.RUN and guard < 6000:
		if end_after > 0 and guard == end_after:
			rv.end_early()
		rv._process(FRAME)
		guard += 1
	return rv


## Share of tonight's framed nodes the camera shows on screen (in front of it, inside the view).
func _on_screen(rv: RootView) -> float:
	var vp := rv.get_viewport().get_visible_rect().size
	var inside := 0
	for id in rv._focus_ids:
		var p := rv.roots.graph.positions[id]
		if rv.camera.is_position_behind(p):
			continue
		var s := rv.camera.unproject_position(p)
		if s.x >= 0.0 and s.x <= vp.x and s.y >= vp.y * 0.12 and s.y <= vp.y:
			inside += 1
	return float(inside) / maxf(rv._focus_ids.size(), 1)


func _settle_calmly(rv: RootView, label: String) -> void:
	var guard := 0
	while not rv.focus_tonight and rv.is_settling() and guard < 600:
		rv._process(FRAME)
		guard += 1
	t.check(rv.focus_tonight, "%s: the camera frames tonight's root" % label)
	t.check(rv._focus_ids.size() > 0, "%s: tonight's nodes are known (%d)" % [label, rv._focus_ids.size()])
	var pos := rv.camera.global_position
	var fwd := -rv.camera.global_basis.z
	var max_step := 0.0
	var max_turn := 0.0
	var finished := [false]
	rv.run_finished.connect(func(_x: PackedFloat32Array) -> void: finished[0] = true)
	guard = 0
	while rv.is_settling() and guard < 600:
		rv._process(FRAME)
		var p := rv.camera.global_position
		var f := -rv.camera.global_basis.z
		max_step = maxf(max_step, p.distance_to(pos))
		max_turn = maxf(max_turn, rad_to_deg(f.angle_to(fwd)))
		pos = p
		fwd = f
		guard += 1
	t.check(finished[0], "%s: the night moves on" % label)
	t.check(max_turn < 4.0, "%s: no snap, the view turns at most %.2f deg a frame" % [label, max_turn])
	t.check(max_step < 0.25 * maxf(rv._orbit_distance, 4.0), "%s: no jump, at most %.2f m a frame" % [label, max_step])
	var share := _on_screen(rv)
	t.check(share >= 0.97, "%s: tonight's root on screen at the end (%.0f %%)" % [label, share * 100.0])


func test_a_short_root_is_framed_calmly() -> void:
	var rv := _run(14, 90)
	_settle_calmly(rv, "short")
	t.check(rv._orbit_distance < 20.0, "a short root is seen close (%.1f m)" % rv._orbit_distance)
	_free(rv)


func test_a_long_root_is_framed_calmly() -> void:
	var rv := _run(21, 0)
	t.check(rv.roots.run_length > 5.0, "a long root (%.1f m)" % rv.roots.run_length)
	_settle_calmly(rv, "long")
	_free(rv)


func test_the_fine_roots_at_old_ends_stay_out_of_the_frame() -> void:
	var rv := _run(14, 0)
	var g := rv.roots.graph
	for id in rv._focus_ids:
		var fine: bool = int(g.get_flag(id, "fine", -1)) != -1
		t.check(not fine or rv._focus_ids.has(g.parents[id]), "node %d grew from tonight's root" % id)
		if fine and not rv._focus_ids.has(g.parents[id]):
			break
	_free(rv)


func test_the_player_takes_the_camera_back() -> void:
	var rv := _run(14, 90)
	for _i in range(10):
		rv._process(FRAME)
	t.check(rv.focus_tonight, "framing")
	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.position = Vector2(200, 500)
	rv._unhandled_input(press)
	var move := InputEventMouseMotion.new()
	move.position = Vector2(260, 500)
	move.relative = Vector2(60, 0)
	rv._unhandled_input(move)
	t.check(not rv.focus_tonight, "a turn of the player's own ends the framing")
	var d := rv._orbit_distance
	var yaw := rv._orbit_yaw
	for _i in range(30):
		rv._process(FRAME)
	t.check_near(rv._orbit_distance, d, 1e-4, "the distance is the player's")
	t.check_near(rv._orbit_yaw, yaw, 1e-4, "and the turn too (finger still down)")
	var rv2 := _run(15, 90)
	var wheel := InputEventMouseButton.new()
	wheel.button_index = MOUSE_BUTTON_WHEEL_UP
	wheel.pressed = true
	rv2._unhandled_input(wheel)
	t.check(not rv2.focus_tonight, "a zoom ends it too")
	_free(rv)
	_free(rv2)
