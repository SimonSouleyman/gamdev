extends RefCounted
## 0.8.2.7 (docs/notes/0.8.2.7.md section 4): root mode without stick or dive button. A tap on a
## root starts the run there (the root under the finger lights up on press); a press or drag on
## plain soil only turns the camera; during the run a finger dragged anywhere steers.
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


func _picking() -> RootView:
	var g := GameState.new_game(14)
	g.sim.resources.life_force = 40.0
	for night in range(3):
		_line(g.roots, 0, Vector3(cos(night * 2.1) * 4.0, -1.0, sin(night * 2.1) * 4.0), night)
	var rv := _view(g)
	rv.begin_pick()
	return rv


func _button(rv: RootView, pos: Vector2, down: bool) -> void:
	var e := InputEventMouseButton.new()
	e.button_index = MOUSE_BUTTON_LEFT
	e.pressed = down
	e.position = pos
	rv._unhandled_input(e)


func _move(rv: RootView, pos: Vector2, rel: Vector2) -> void:
	var e := InputEventMouseMotion.new()
	e.position = pos
	e.relative = rel
	rv._unhandled_input(e)


## A screen point on the old roots, and one well away from every root.
func _root_and_soil(rv: RootView) -> Array:
	var g := rv.roots.graph
	var on := rv.camera.unproject_position(g.positions[g.size() / 2])
	var vp := rv.get_viewport().get_visible_rect().size
	var soil := Vector2(-1, -1)
	for y in range(8, 0, -1):
		for x in range(1, 8):
			var p := Vector2(vp.x * x / 8.0, vp.y * y / 9.0)
			if rv.pick_node_at(p) < 0:
				soil = p
				break
		if soil.x >= 0.0:
			break
	return [on, soil]


func test_no_stick_and_no_dive_button() -> void:
	var rv := _picking()
	t.check(rv.get("joystick") == null and rv.get("dive_button") == null, "no stick, no dive button")
	var words: Array = []
	for b in rv.hud.find_children("*", "Button", true, false):
		words.append((b as Button).text)
	t.check_eq(words.size(), 1, "one button in the root HUD: %s" % str(words))
	t.check(rv.end_button != null, "let roots spread stays")
	t.check(rv.hud.find_children("*", "ThumbStick", true, false).is_empty(), "no stick drawn")
	_free(rv)


func test_a_tap_on_soil_does_not_start_and_a_drag_turns_the_camera() -> void:
	var rv := _picking()
	var pts := _root_and_soil(rv)
	var soil: Vector2 = pts[1]
	t.check(soil.x >= 0.0, "a spot of plain soil on screen")
	_button(rv, soil, true)
	t.check(not rv._hover.visible, "nothing lights up on soil")
	_button(rv, soil, false)
	t.check_eq(rv.mode, RootView.Mode.PICK, "a tap on soil starts nothing")
	var yaw := rv._orbit_yaw
	_button(rv, soil, true)
	_move(rv, soil + Vector2(80, 0), Vector2(80, 0))
	_button(rv, soil + Vector2(80, 0), false)
	t.check(absf(rv._orbit_yaw - yaw) > 0.1, "a drag turns the camera")
	t.check_eq(rv.mode, RootView.Mode.PICK, "and starts nothing")
	_free(rv)


func test_a_tap_on_a_root_lights_it_then_starts_there() -> void:
	var rv := _picking()
	var g := rv.roots.graph
	var id := g.size() / 2
	var on := rv.camera.unproject_position(g.positions[id])
	# A fingertip off the root still finds it (forgiving, about 9 mm).
	t.check(rv.finger_px() >= 34.0, "a finger-sized reach (%.0f px)" % rv.finger_px())
	var off := on + Vector2(0, rv.finger_px() * 0.6)
	_button(rv, off, true)
	t.check(rv._hover.visible, "the root under the finger lights up on press")
	var lit := rv._hover.position
	t.check_eq(rv.mode, RootView.Mode.PICK, "not started before the release")
	_button(rv, off, false)
	t.check_eq(rv.mode, RootView.Mode.RUN, "the release starts the run")
	t.check(g.positions[rv.roots.run_start_id].distance_to(lit) < 1e-4, "from the lit point")
	t.check(g.positions[rv.roots.run_start_id].distance_to(g.positions[id]) < 1.0, "on the tapped root (%.2f m off)" % g.positions[rv.roots.run_start_id].distance_to(g.positions[id]))
	_free(rv)


func test_a_drag_anywhere_steers_and_lifting_keeps_the_heading() -> void:
	var rv := _picking()
	var g := rv.roots.graph
	t.check(rv.start_at(g.size() - 1), "a run starts")
	for _i in range(10):
		rv._process(FRAME)
	t.check_eq(rv.roots.run_length, 0.0, "it waits for the finger")
	var from := Vector2(300, 900)
	_button(rv, from, true)
	t.check_eq(rv._stick(), Vector2.ZERO, "a finger down is the stick's centre")
	_move(rv, from + Vector2(55, 0), Vector2(55, 0))
	t.check_near(rv._stick().x, 55.0 / RootView.STEER_RADIUS, 1e-4, "drag right steers right")
	_move(rv, from + Vector2(400, -400), Vector2(345, -400))
	t.check_near(rv._stick().length(), 1.0, 1e-4, "full at the stick's edge")
	t.check(rv._stick().y > 0.0, "drag up steers up")
	var h0 := rv.roots.heading
	for _i in range(20):
		rv._process(FRAME)
	t.check(rv.roots.run_length > 0.0, "the root grows")
	t.check(rv.roots.heading.angle_to(h0) > 0.05, "and turns")
	_button(rv, from + Vector2(400, -400), false)
	t.check_eq(rv._stick(), Vector2.ZERO, "lifting lets go")
	t.check_eq(rv.mode, RootView.Mode.RUN, "the run goes on")
	_free(rv)
