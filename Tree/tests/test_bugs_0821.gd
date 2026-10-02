extends RefCounted
## 0.8.2.1: the bugs of the 0.8.2 bug hunt (docs/notes/bugs-0.8.2.1.md), one test per item.
var t

const FRAME := 1.0 / 30.0
const SIM_STEP := 1.0 / 30.0
const OLD_SAVE := "res://tests/fixtures/tree-v0.8.1_midrun.json"


func _old_game() -> GameState:
	var json := JSON.new()
	t.check_eq(json.parse(FileAccess.get_file_as_string(OLD_SAVE)), OK, "the 0.8.1 save reads")
	return GameState.from_dict((json.data as Dictionary)["game"])


func _view(g: GameState) -> RootView:
	var rv := RootView.new()
	# Under a holder, not the root: on its own a RootView sets up a soil of its own (F6 play).
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


## A game at its first night, the run steered for `seconds`.
func _mid_run(seed: int, seconds: float) -> GameState:
	var g := GameState.new_game(seed, "linden")
	g.dive()
	g.start_run(0)
	var bot := RootBot.new()
	for _i in range(int(seconds / SIM_STEP)):
		if not g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP):
			break
	return g


func _round_trip(g: GameState) -> GameState:
	return GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict(), "", false, true)))


# --- 1: old saves show a sensible day page -------------------------------------------------

## Lines from 0.8.1 and earlier have no topic: their wording gives it, routine lines never show,
## and the doodle matches what shows.
func test_old_lines_get_a_topic_and_a_doodle() -> void:
	var d := Diary.new()
	d.add(2, "The old roots drew water 2.1, nitrogen 1.5, phosphorus 0.3, potassium 0.5 from the soil overnight.")
	d.add(2, "Dew sparkled on the meadow in the first sun.")
	d.add(2, "The linden is 1.4 m tall with 13 leaf clusters.")
	d.add(2, "Night 3: the new root grew 34 m and drank water 2.8, nitrogen 3.2, phosphorus 1.2, potassium 1.6.")
	d.add(2, "Night 3: the root found what feeds the nettles in the north, the one I wished for.", "tree", "nettles")
	var page := d.page(2)
	t.check_eq(page.size(), 1, "day 2 shows the reached wish only (the dew waits behind a find)")
	t.check(str(page[0]["text"]).contains("the one I wished for"), "the reached wish shows: " + str(page[0]["text"]))
	t.check_eq(d.doodle(2), "nettles", "with its nettles")
	d.add(5, "The old roots drew water 2.9, nitrogen 0.0, phosphorus 0.0, potassium 1.5 from the soil overnight.")
	d.add(5, "Wood anemones are flowering in the shade of my crown, where the meadow grass has thinned.")
	d.add(5, "Mist lay over the clearing this morning.")
	var five := d.page(5)
	t.check_eq(five.size(), 1, "day 5: one line")
	t.check(str(five[0]["text"]).begins_with("Wood anemones"), "the find, not the routine line")
	t.check_eq(d.doodle(5), "anemone", "an anemone doodle")
	d.add(4, "The old roots drew water 0.5, nitrogen 0.0, phosphorus 0.0, potassium 1.5 from the soil overnight.")
	d.add(4, "The linden is 3.3 m tall with 36 leaf clusters.")
	d.add(4, "A light shower passed over the clearing in the afternoon.")
	t.check_eq(str(d.page(4)[0]["text"]), "A light shower passed over the clearing in the afternoon.", "a quiet day: the weather")
	t.check_eq(d.doodle(4), "rain", "with rain")
	d.add(0, "I planted a linden seed in the clearing as the sun went down.")
	t.check_eq(d.doodle(0), "seed", "the planting: a seed")
	d.add(8, "At dusk a hedgehog snuffled out of the brush pile at the edge of the clearing and back in again. It has moved into my cuttings.", "tree", "hedgehog")
	t.check_eq(Diary.topic_of(d.page(8)[0]), "visitor", "the hedgehog is a visitor")
	# Every doodle an old line gets is drawn.
	for row in Diary.LEGACY_LINES:
		t.check(str(row[2]) == "" or InkSketch.has(str(row[2])), "drawn: " + str(row[2]))


## Simon's phone save is from before 0.8.2: a real 0.8.1 save shows no routine line on any day.
func test_a_0_8_1_save_shows_no_routine_line() -> void:
	var g := _old_game()
	var shown := 0
	for day in g.diary.days():
		for e in g.diary.page(day):
			var text := str(e["text"])
			t.check(not text.contains("drank water") and not text.contains("drew water"), "day %d: no routine line: %s" % [day, text])
			t.check(not text.contains(" m tall") or g.diary.page(day).size() == 1, "day %d: the height only on a day without anything else" % day)
			shown += 1
		t.check(InkSketch.has(g.diary.doodle(day)), "day %d has a doodle" % day)
	t.check(shown >= g.diary.days().size(), "every every old day shows a line (%d on %d days)" % [shown, g.diary.days().size()])


# --- 2: a reached wish shows on its day page -----------------------------------------------

func test_a_reached_wish_beats_the_mornings_finds() -> void:
	var d := Diary.new()
	d.add(15, "Wish: the clover in the north.", "tree", "clover", "wish")
	d.add(15, "Moss creeps over the shaded ground.", "tree", "moss", "find")
	d.add(15, "Night 16: wish found, the clover.", "tree", "clover", "find")
	var page := d.page(15)
	t.check_eq(str(page[page.size() - 1]["text"]), "Night 16: wish found, the clover.", "the reached wish shows")
	t.check_eq(d.doodle(15), "clover", "and its doodle")
	# A find written after it does not hide it either.
	d.add(15, "Found a lost coin, green with age.", "tree", "coin", "find")
	t.check(d.page(15).any(func(e: Dictionary) -> bool: return Diary.is_reached_line(e)), "still the reached wish")


# --- 3: a far wish chosen again keeps up to three mornings ----------------------------------

func test_a_far_wish_chosen_again_keeps_three_mornings() -> void:
	var u := Underground.new(14)
	var roots := RootSystem.new(14)
	roots.fit_soil(u)
	var pid := u.add_wish_deposit(2, Resources.Kind.NITROGEN, Vector3(16, -0.8, 0), 1.2, 8, true)
	t.check(pid >= 0 and Diary.is_far(u, pid), "a far deposit placed on day 2")
	var d := Diary.new()
	# Missed, and on day 10 the wish points at it again (missed_ahead).
	d.wish_patch = pid
	d.wish_since = 10
	for day in [11, 12]:
		d.new_wish(u, day, 14, roots)
		t.check_eq(d.wish_patch, pid, "day %d: still the far wish chosen on day 10" % day)
	d.new_wish(u, 13, 14, roots)
	t.check(d.wish_patch != pid or d.wish_since == 13, "day 13: three mornings are up")
	# The day it was chosen survives a save.
	d.wish_since = 10
	t.check_eq(Diary.from_dict(JSON.parse_string(JSON.stringify(d.to_dict()))).wish_since, 10, "saved")
	# A new wish records its morning.
	var e := Diary.new()
	e.new_wish(u, 7, 14, roots)
	t.check_eq(e.wish_since, 7, "a new wish starts its mornings today")


# --- 4: no far wish before day 5 -------------------------------------------------------------

func test_far_wishes_start_on_day_five() -> void:
	var keep := Diary.far_share
	Diary.far_share = 1.0
	var far_after := 0
	for seed in [3, 14, 27, 42]:
		var roots := RootSystem.new(seed)
		for day in range(1, 16):
			var u := Underground.new(seed)
			roots.fit_soil(u)
			# far_want 1: the running share asks for a far wish every morning.
			var plan := Diary.plan_wish(u, day, seed, roots, null, 1.0)
			var far := bool(plan.get("far", false))
			if day < Diary.FAR_FROM_DAY:
				t.check(not far, "seed %d day %d: no far wish yet" % [seed, day])
				t.check(not bool(Diary.plan_wish(u, day, seed).get("far", false)), "seed %d day %d: nor by the bare coin" % [seed, day])
			elif far:
				far_after += 1
	Diary.far_share = keep
	t.check(far_after >= 8, "from day 5 they come (%d)" % far_after)
	# Before day 5 the running share does not count, so day 5 brings no burst of far wishes.
	var d := Diary.new()
	var u2 := Underground.new(14)
	var r2 := RootSystem.new(14)
	r2.fit_soil(u2)
	for day in range(1, Diary.FAR_FROM_DAY):
		d.new_wish(u2, day, 14, r2)
	t.check_eq(d.wish_days, 0, "no underground morning counted before day 5")


# --- 5: a new game or a load resets the settle --------------------------------------------

func test_setup_ends_the_old_games_settle() -> void:
	var g := GameState.new_game(14)
	var rv := _view(g)
	g.sim.resources.life_force = 60.0
	_line(g.roots, 0, Vector3(3, -1, -4), 0)
	rv.setup(g.ground, g.roots, g.sim.resources)
	t.check(rv.start_at(g.roots.graph.size() - 1), "a run starts")
	g.roots.finish_early(g.ground, g.sim.resources)
	rv._settle()
	t.check(rv.is_settling(), "tonight's root settles")
	var finished := [0]
	rv.run_finished.connect(func(_tot: PackedFloat32Array) -> void: finished[0] += 1)
	# Start over (or load a copy) during the settle.
	var g2 := GameState.new_game(5)
	rv.setup(g2.ground, g2.roots, g2.sim.resources)
	t.check(not rv.is_settling(), "the new game does not settle")
	rv.begin_pick()
	t.check(rv.can_far_view(), "the far view may open")
	for _i in range(12):
		rv._process(0.5)
	t.check_eq(finished[0], 0, "no stray run_finished")
	t.check(not rv.is_settling(), "still not settling")
	_free(rv)


# --- 6: the life bar after loading mid-run -------------------------------------------------

func test_the_life_bar_resumes_from_the_saved_tank() -> void:
	var g := _mid_run(11, 3.0)
	t.check(g.roots.run_active, "mid-run")
	var tank := g.roots.run_tank
	var loaded := _round_trip(g)
	var rv := _view(loaded)
	rv.resume_run()
	t.check_near(rv._life_at_start, tank, 0.01, "the bar starts from the night's tank")
	_free(rv)
	# A 0.8.1 save has no tank: the old estimate, never below what is left.
	var old := _old_game()
	var rv2 := _view(old)
	rv2.resume_run()
	t.check(rv2._life_at_start >= old.sim.resources.life_force, "an old save's bar is not over full")
	_free(rv2)


# --- 7: the cap on the night's intake is saved -----------------------------------------------

func test_the_nights_room_survives_a_save() -> void:
	var g := _mid_run(11, 2.0)
	t.check(not g.roots.run_room.is_empty(), "the night has a room")
	var loaded := _round_trip(g)
	t.check_eq(loaded.roots.run_room.size(), 4, "four kinds")
	for k in range(4):
		t.check_near(float(loaded.roots.run_room[k]), float(g.roots.run_room[k]), 0.001, "kind %d kept" % k)
	# A save from before has none: the room the tree has now.
	var old := _old_game()
	t.check(old.roots.run_active, "the 0.8.1 save is mid-run")
	t.check_eq(old.roots.run_room.size(), 4, "an old mid-run save gets a room")


# --- 8: a lost release ends the hold ---------------------------------------------------------

func _tree_view() -> TreeView:
	var tv := TreeView.new()
	t.root.add_child(tv)
	var g := GameState.new_game(7)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(0)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.2:
		g.tick(0.5)
	g.take_events()
	tv.setup(g)
	return tv


func _hold(tv: TreeView) -> void:
	tv._begin_press(Vector2(200, 600))
	for _i in range(90):
		tv._process(1.0 / 60.0)


func test_focus_out_or_a_cancelled_touch_ends_the_hold() -> void:
	var tv := _tree_view()
	_hold(tv)
	t.check(tv.fast_forwarding(), "held: fast-forward")
	var main: Node = load("res://main.gd").new()
	main.tree_view = tv
	main._notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
	t.check(not tv.fast_forwarding(), "focus out ends it")
	tv._process(1.0 / 60.0)
	t.check_eq(tv.time_speed(), 1.0, "the normal clock")
	t.check_eq(tv.state.sim.clock.boost_remaining, 0.0, "nothing boosted")
	main.tree_view = null
	main.free()
	_hold(tv)
	var cancel := InputEventScreenTouch.new()
	cancel.index = 0
	cancel.pressed = false
	cancel.canceled = true
	tv._unhandled_input(cancel)
	t.check(not tv.fast_forwarding(), "a cancelled touch ends it")
	tv.free()


# --- 9: the end of a run costs no long frame ------------------------------------------------

func test_pieces_make_the_same_mesh_as_one_build() -> void:
	var g := GameState.new_game(14)
	for night in range(6):
		_line(g.roots, 0 if night == 0 else g.roots.graph.size() - 1 - night * 3, Vector3(cos(night) * 6.0, -1.0 - night * 0.2, sin(night) * 6.0), night)
	var b := BranchMeshBuilder.new()
	var whole := b.build(g.roots.graph)
	var acc := b.new_arrays()
	var n := g.roots.graph.size()
	var from := 1
	while from < n:
		b.append(g.roots.graph, from, mini(from + 7, n), acc)
		from += 7
	var pieces := b.mesh_from(acc)
	var a: Array = whole.surface_get_arrays(0)
	var c: Array = pieces.surface_get_arrays(0)
	t.check_eq((c[Mesh.ARRAY_VERTEX] as PackedVector3Array).size(), (a[Mesh.ARRAY_VERTEX] as PackedVector3Array).size(), "the same vertices")
	t.check(c[Mesh.ARRAY_VERTEX] == a[Mesh.ARRAY_VERTEX], "at the same places")
	t.check(c[Mesh.ARRAY_INDEX] == a[Mesh.ARRAY_INDEX], "the same triangles")


func test_the_settle_builds_the_old_roots_over_frames() -> void:
	var g := GameState.new_game(14)
	g.sim.resources.life_force = 60.0
	for night in range(4):
		_line(g.roots, 0, Vector3(cos(night * 1.7) * 5.0, -1.2, sin(night * 1.7) * 5.0), night)
	var rv := _view(g)
	var old_mesh := rv._static_roots.mesh
	t.check(rv.start_at(g.roots.graph.size() - 1), "a run starts")
	var bot := RootBot.new()
	for _i in range(60):
		if not g.roots.advance(bot.stick_for(g.roots, g.ground), false, FRAME, g.ground, g.sim.resources):
			break
	g.roots.finish_early(g.ground, g.sim.resources)
	rv._settle()
	t.check(rv._static_roots.mesh == old_mesh, "the old roots' mesh is not rebuilt when the run ends")
	t.check(not rv._static_job.is_empty(), "the morning's mesh is built in pieces")
	var finished := [0]
	rv.run_finished.connect(func(_tot: PackedFloat32Array) -> void: finished[0] += 1)
	var guard := 0
	while rv.is_settling() and guard < 1000:
		rv._process(FRAME)
		guard += 1
	t.check_eq(finished[0], 1, "the night moves on once")
	t.check(rv._static_job.is_empty(), "the pieces are done")
	var want := rv._builder.build(g.roots.graph, 1, g.roots.graph.size())
	var got: Array = rv._static_roots.mesh.surface_get_arrays(0)
	t.check(got[Mesh.ARRAY_VERTEX] == want.surface_get_arrays(0)[Mesh.ARRAY_VERTEX], "the old roots now include tonight's, as one build would make them")
	t.check(rv._live_roots.mesh == null, "no warm root left")
	_free(rv)


func test_the_far_tips_are_cached_until_the_roots_change() -> void:
	var g := GameState.new_game(14)
	_line(g.roots, 0, Vector3(4, -1, 0), 0)
	var rv := _view(g)
	t.check(rv.far_tips() == FieldLook.main_tips(g.roots), "the main roots' ends")
	var key := rv._tips_key.duplicate()
	rv.far_tips()
	t.check(rv._tips_key == key, "the same key: no recount")
	_line(g.roots, 0, Vector3(-4, -1, 0), 1)
	t.check(rv.far_tips() == FieldLook.main_tips(g.roots), "a new root: counted again")
	t.check_eq(rv.far_tips().size(), 2, "two ends")
	_free(rv)
