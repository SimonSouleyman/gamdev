extends SceneTree
## Screenshots of the garden shed (start menu): the workbench with its things and their labels,
## each thing hovered and mid-tap, the options pinboard (also in "clearer print"), the album,
## the journal, the seed bag, the flower pot's page, the "while you were away" page and the
## loading page.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6] [--species=linden]
## --flip=25: instead, grow a tree for 25 days with a morning photo each day (into a tool folder),
## then open the album on the finished tree's flip-book page, photograph it mid-turn and at the
## end, and save the month as a video (the path is printed).
## Then the bonsai (design doc section 16): the sill with the bonsai and its tools, bonsai mode
## with the tools' first-time labels (and in "clearer print"), each tool on the sill picked up
## with a real tap, held and used (0.7: can, pellet tin and its N P K slip, secateurs, tweezers,
## copper wire, the trowel on a day it does not ask, the carved arrows, the sketchbook and the
## album card), watering, fertiliser, a wire and a wire scar, repotting with the trowel, a
## juniper after 14 care days, a linden cutting, the style pages and the bonsai's album page, and
## the juniper and the sill at night.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6] [--species=linden] [--bonsai-only [--tools-only]]
## For the phone's look add `--rendering-method gl_compatibility` before `--` and `--phone` after it.
## --quick: only the bench by day and at night (the lantern's light), then quit.

var main: Node
var shots := ""
var days := 6
var species := "linden"
var frame := 0
var flip := 0
var _game: GameState
var _flip_day := 0
var _flip_frame := 0
var bonsai_only := false
var quick := false
## Only the sill's tools and repotting (with --bonsai-only), for a quick look.
var tools_only := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--flip="):
			flip = int(a.substr(7))
		elif a == "--bonsai-only":
			bonsai_only = true
		elif a == "--quick":
			quick = true
		elif a == "--tools-only":
			tools_only = true
	DirAccess.make_dir_recursive_absolute(shots)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> void:
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots.path_join(name + ".png"))


func _wait(n: int) -> void:
	for _i in range(n):
		await process_frame
## Grows the game one day on: tonight's root with the bot, then to mid-morning.
func _grow_day(g: GameState) -> void:
	if g.phase == GameState.Phase.DAY:
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()


func _flip_frames() -> bool:
	frame += 1
	if frame == 2:
		_game = GameState.new_game(42, species)
		_grow_day(_game)
		main.start(_game)
		main.enter_shed(false)
		Photos.DIR = "user://tool_flip_photos"
		TimeLapse.DIR = "user://tool_timelapse"
		Photos.clear()
		main.shed.camera.current = false
		main.tree_view.camera.make_current()
		main.shed_menu.show_menu(false)
		_flip_day = 1
		_flip_frame = frame
	if frame < 3:
		return false
	var tv: TreeView = main.tree_view
	if _flip_day <= flip:
		# Each day: grow (from day 2 on), let the view catch up, then the morning photo.
		if frame == _flip_frame + 1 and _flip_day > 1:
			_grow_day(_game)
			tv.refresh_clearing()
			tv._rebuild()
			tv._frame_camera(true)
		if frame == _flip_frame + 5:
			Photos.save_from(root.get_viewport(), _game.day_number(), "morning", species)
			_flip_day += 1
			_flip_frame = frame
		return false
	var menu: ShedMenu = main.shed_menu
	if _flip_frame > 0:
		# The album of the finished tree: its flip-book page comes last.
		_flip_frame = -frame
		main.shed.camera.make_current()
		menu.show_menu(true)
		menu.tree_finished = true
		menu.open_album()
		print("album spreads: ", menu._spreads)
	var since := frame + _flip_frame
	var book: FlipBook = menu._flip
	if since == 20:
		_shot("flip_playing")
		# Stop the pages and turn one by hand, half way, for a still of the turn.
		book.playing = false
		book._show(book.pages.size() / 2, 0.8)
	if since == 24:
		_shot("flip_turning")
		book._show(book.pages.size() - 1, 0.0)
	if since == 28:
		_shot("flip_grown")
		_save_video(menu)
	if since > 400:
		quit()
	return false


func _save_video(menu: ShedMenu) -> void:
	var path: String = await menu.save_video(menu._flip_tree)
	var data := FileAccess.get_file_as_bytes(path)
	print("video: %s (%d bytes, %d frames)" % [ProjectSettings.globalize_path(path), data.size(), MjpegAvi.frame_count(data)])
	_shot("flip_saved")
	quit()


func _process(_d: float) -> bool:
	if flip > 0:
		return _flip_frames()
	frame += 1
	if frame == 2:
		_run()
	return false


func _grown_game() -> GameState:
	# A tree grown for a few days, at mid-morning.
	var g := GameState.new_game(42, species)
	for _day in range(days):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if _day < days - 1:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()
	return g


func _run() -> void:
	main.start(_grown_game())
	main.enter_shed(false)
	if bonsai_only:
		await _bonsai_sequence()
		if tools_only:
			quit()
			return
		await _bonsai_rest()
		quit()
		return
	# First visit: every thing on the bench carries its label.
	await _wait(38)
	_shot("shed_menu")
	if quick:
		# On to the night: the window goes dark and the lantern lights the room.
		while main.state.phase == GameState.Phase.DAY:
			main.state.tick(0.5)
		await _wait(20)
		main.journal.clear_pages()
		await _wait(20)
		_shot("shed_night")
		quit()
		return
	# Two photos for the album, into a tool folder (never the player's album).
	Photos.DIR = "user://tool_photos"
	Photos.clear()
	main.shed.camera.current = false
	main.tree_view.camera.make_current()
	main.shed_menu.show_menu(false)
	await _wait(2)
	Photos.save_from(root.get_viewport(), 5, "morning")
	main.tree_view._yaw += 1.2
	await _wait(5)
	Photos.save_from(root.get_viewport(), 6, "camera")
	main.shed.camera.make_current()
	main.shed_menu.show_menu(true)
	# Each thing hovered (only its label) and caught mid-motion after a tap.
	for item in Shed.ITEMS:
		main.state.seen_pages["shed_used_" + item] = true
	for item in Shed.ITEMS + ["door"]:
		main._shed_hover = item
		await _wait(12)
		main.shed.tap(item)
		await _wait(9)
		_shot("shed_tap_" + item)
		await _wait(40)
	main._shed_hover = ""
	await _wait(12)
	_shot("shed_used")
	main.open_shed_item("options")
	await _wait(12)
	_shot("shed_options")
	main._apply_setting("clearer_print", true)
	main.journal.set_setting("clearer_print", true)
	main.shed_menu.open_options()
	await _wait(6)
	_shot("shed_options_clear")
	main.shed_menu.close_boards()
	main.open_shed_item("pot")
	await _wait(8)
	_shot("shed_pot_page_clear")
	main.shed_menu.close_boards()
	main.open_shed_item("journal")
	await _wait(15)
	_shot("shed_journal_clear")
	main.journal.close_diary()
	main._apply_setting("clearer_print", false)
	main.journal.set_setting("clearer_print", false)
	main.open_shed_item("album")
	await _wait(10)
	_shot("shed_album")
	main.shed_menu.close_boards()
	main.open_shed_item("journal")
	await _wait(15)
	_shot("shed_journal")
	main.journal.close_diary()
	main.open_shed_item("pot")
	await _wait(8)
	_shot("shed_pot_page")
	main.shed_menu.close_boards()
	# Closed for two days and five hours: the torn "while you were away" page.
	main.state.apply_offline(2.0 * 86400.0 + 5.0 * 3600.0)
	main._show_away_page()
	await _wait(8)
	_shot("shed_away")
	main.shed_menu.close_boards()
	# The seed bag of a finished tree: plant the next seed.
	main.state.finished = true
	main.state.grove.append({"species": main.state.sim.species.id, "days": days, "seed": 42})
	main.open_shed_item("seeds")
	await _wait(10)
	_shot("shed_seeds")
	main.shed_menu.close_boards()
	main.shed_menu.show_loading(true)
	await _wait(10)
	_shot("loading")
	main.shed_menu.show_loading(false)
	await _bonsai_sequence()
	await _bonsai_rest()
	quit()


func _seconds(t: float) -> void:
	await create_timer(t).timeout


## Care as a player gives it, day by day: water when pale, pellets when low, repot when asked,
## pinch a few fresh tips, and (`shape`) cut the longest branch now and then.
func _care(b: BonsaiSim, days: int, shape: bool) -> void:
	var end := b.day() + days
	while b.day() < end:
		var d := b.day()
		if b.moisture < 0.35:
			b.water()
		for k in range(3):
			if b.soil[k] < 0.3:
				b.fertilise(k)
		if b.repot_due:
			b.repot("rectangle" if b.species.conifer else "oval", 0.5)
		b.advance(6.0)
		if b.day() != d and shape:
			var n := 0
			for id in b.living_tips():
				if b.is_fresh_tip(id) and posmod(id * 7 + d, 5) == 0 and n < 6:
					b.pinch(id)
					n += 1
			# Keep the lower trunk clean (the classic first step), and cut back what runs out.
			if d == 2:
				for id in range(3, b.graph.size()):
					if id < b.graph.size() and b.graph.positions[id].y < 0.85 and not b.trunk_chain().has(id) and b.can_prune(id):
						b.prune(id)
			if d % 3 == 2:
				var best := -1
				var best_d := 0.0
				for id in range(3, b.graph.size()):
					var p := b.graph.positions[id]
					var r := Vector2(p.x, p.z).length() + maxf(0.0, p.y - 2.6)
					if b.can_prune(id) and b.subtree_leafy(id) >= 6 and b.subtree_leafy(id) < 40 and r > best_d:
						best_d = r
						best = id
				if best >= 0:
					b.prune(best)
	b.clock.time_of_day = main.state.sim.clock.time_of_day


## A side branch off the trunk that reaches toward the room (the camera's side), for the wire.
func _side_branch(b: BonsaiSim, min_len: int) -> int:
	var trunk := b.trunk_chain()
	var best := -1
	var best_score := -INF
	for id in range(2, b.graph.size()):
		if not trunk.has(id) and trunk.has(b.graph.parents[id]) and not b.is_jin(id):
			var n := b.subtree_leafy(id)
			var dir := b.graph.direction_of(id).rotated(Vector3.UP, b.turn * PI * 0.5)
			var score := -dir.z * 2.0 + b.graph.positions[id].y * 0.3 + minf(n, 30) * 0.02
			if n >= min_len and b.graph.positions[id].y > 0.6 and score > best_score:
				best_score = score
				best = id
	return best


## The camera beside a wired branch, a little below its pad (the wire shows under the foliage),
## far enough back that the whole coil and the branch it bends are in the picture.
func _look_at_wire(view: BonsaiView, b: BonsaiSim, id: int, dist: float) -> void:
	var dir := b.graph.direction_of(id).rotated(Vector3.UP, b.turn * PI * 0.5)
	var side := dir.cross(Vector3.UP)
	if side.z > 0.0:
		side = -side
	var v := (side + Vector3(dir.x, 0.0, dir.z) * 0.4).normalized()
	view.look_from(clampf(atan2(v.x, -v.z), -1.25, 1.25), -0.12, dist, _wire_focus(view, b, id))


## The middle of a wired branch, in the sill's frame.
func _wire_focus(view: BonsaiView, b: BonsaiSim, id: int) -> Vector3:
	var chain := b.wire_chain(id)
	return view.node_in_sill(chain[chain.size() / 2])


## Puts the tool in hand down with a tap on its place on the sill.
func _put_down(view: BonsaiView) -> void:
	if view.tool != "":
		await _tap(view.tools.rest_point(view.camera, view.tool))


## A real tap (press and release) at a canvas point.
func _tap(at: Vector2) -> void:
	_click(at, true)
	_click(at, false)
	await _wait(2)


## The pointer moves to a canvas point (the held tool follows it).
func _point(at: Vector2, pressed: bool = false) -> void:
	var motion := InputEventMouseMotion.new()
	motion.position = root.get_final_transform() * at
	motion.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0
	Input.parse_input_event(motion)
	await _wait(1)


## Taps the thing `id` on the sill where it is on screen.
func _tap_thing(view: BonsaiView, id: String) -> void:
	var pts := view.object_screen_points()
	if not pts.has(id):
		print("not on screen: ", id)
		return
	await _tap(pts[id])


## Taps a paper button of the bonsai's HUD by its text.
func _tap_button(text: String) -> void:
	for b in main.bonsai_hud.find_children("*", "Button", true, false):
		var btn := b as Button
		if btn.is_visible_in_tree() and btn.text.begins_with(text):
			await _tap(btn.get_global_rect().get_center())
			return
	print("no button: ", text)


func _click(at: Vector2, down: bool) -> void:
	var e := InputEventMouseButton.new()
	e.button_index = MOUSE_BUTTON_LEFT
	e.pressed = down
	# Input arrives in window pixels; the views work in the 720 x 1280 canvas.
	e.position = root.get_final_transform() * at
	Input.parse_input_event(e)


## The tools on the sill, each picked up with a real tap, shown in the hand, used and put down.
func _tools_sequence(view: BonsaiView, b: BonsaiSim) -> void:
	var hud: BonsaiHud = main.bonsai_hud
	var soil := view.plant_screen_position(0) + Vector2(0, 10)
	var pts := view.object_screen_points()
	print("sill things on screen: ", pts)
	# Watering: pick up the can (it lifts), move toward the pot (it follows), tap the soil.
	b.moisture = 0.02
	view.refresh(true)
	await _tap_thing(view, "water")
	await _seconds(0.4)
	_shot("tool_water_lifted")
	await _point(soil + Vector2(-120, -160))
	await _seconds(0.5)
	_shot("tool_water_held")
	var cn: Node3D = view.tools.items["water"]
	await _tap(soil)
	await _seconds(1.35)
	_shot("tool_water_use")
	await _seconds(1.2)
	main.journal.clear_pages()
	await _put_down(view)
	await _seconds(0.6)
	print("can put down: ", view.tool == "")
	# The pellet tin: its slip chooses N, P or K, then a tap on the soil spoons them.
	await _tap_thing(view, "fertiliser")
	await _point(soil + Vector2(110, -120))
	await _seconds(0.5)
	_shot("tool_fertiliser_slip")
	await _tap(soil)
	await _seconds(0.3)
	_shot("tool_fertiliser_choose_first")
	await _tap_button("K")
	await _seconds(0.2)
	_shot("tool_fertiliser_chosen")
	var k0 := b.soil[2]
	await _tap(soil)
	await _seconds(0.95)
	_shot("tool_fertiliser_use")
	await _seconds(1.2)
	print("pellets K: %.2f -> %.2f" % [k0, b.soil[2]])
	main.journal.clear_pages()
	await _put_down(view)
	await _seconds(0.5)
	# The secateurs: touch a branch, the cut shows; lift to cut.
	await _tap_thing(view, "shears")
	main.journal.clear_pages()
	var cut := _side_branch(b, 3)
	var cut_at := view.plant_screen_position(cut)
	await _point(cut_at)
	await _seconds(0.5)
	_shot("tool_shears_held")
	var before := b.leafy_count()
	_click(cut_at, true)
	await _wait(3)
	_shot("tool_shears_use")
	_click(cut_at, false)
	await _wait(3)
	print("cut with the secateurs: %d -> %d green" % [before, b.leafy_count()])
	await _seconds(0.8)
	await _put_down(view)
	# The tweezers: tap a fresh tip.
	await _tap_thing(view, "pinch")
	main.journal.clear_pages()
	var trunk := b.trunk_chain()
	var tip := -1
	for t in b.living_tips():
		if b.is_fresh_tip(t) and not trunk.has(t):
			tip = t
			break
	if tip >= 0:
		var p := view.plant_screen_position(tip)
		await _point(p)
		await _seconds(0.5)
		_shot("tool_pinch_held")
		await _tap(p)
		print("pinched with the tweezers: ", b.graph.get_flag(tip, "pinched", false))
	await _put_down(view)
	# The copper wire: drag a branch into a new line.
	await _tap_thing(view, "wire")
	main.journal.clear_pages()
	var wid := _side_branch(b, 3)
	var at := view.plant_screen_position(wid)
	await _point(at)
	await _seconds(0.4)
	_shot("tool_wire_held")
	_click(at, true)
	await _point(at + Vector2(0, 50), true)
	await _point(at + Vector2(0, 90), true)
	await _seconds(0.2)
	_shot("tool_wire_use")
	_click(at + Vector2(0, 90), false)
	await _wait(3)
	print("wired by drag: ", b.wired())
	await _seconds(0.4)
	_shot("tool_wire_done")
	await _put_down(view)
	# The trowel on a day the bonsai does not ask: a note says when.
	b.repot_due = false
	await _tap_thing(view, "trowel")
	main.journal.clear_pages()
	await _point(soil + Vector2(-60, -40))
	await _tap(soil)
	await _seconds(0.4)
	_shot("tool_trowel_not_yet")
	# The pot turned by a carved arrow, and by a sideways drag on its rim.
	await _put_down(view)
	var turn0 := b.turn
	await _tap_thing(view, "turn_right")
	await _seconds(0.3)
	_shot("tool_turn_arrow")
	await _seconds(0.6)
	print("turned by the arrow: %d -> %d" % [turn0, b.turn])
	var rim := view.plant_screen_position(0) + Vector2(-40, 60)
	_click(rim, true)
	await _point(rim + Vector2(40, 0), true)
	await _point(rim + Vector2(90, 0), true)
	_click(rim + Vector2(90, 0), false)
	await _seconds(0.9)
	print("and by a drag on the pot: -> %d" % b.turn)
	# The sketchbook, the album card and the box of cuttings open their pages.
	await _tap_thing(view, "styles")
	await _wait(4)
	_shot("tool_sketchbook_page")
	hud.close_sheet()
	await _tap_thing(view, "album")
	await _wait(4)
	_shot("tool_album_page")
	hud.close_sheet()
	await _wait(2)


## Repotting with the trowel: it asks, the trowel lifts it out, the secateurs trim the root ball,
## a pot is picked on the slip, the trowel puts it back in fresh soil.
func _repot_sequence(view: BonsaiView, b: BonsaiSim) -> void:
	b.repot_due = true
	view.look_from(0.0, BonsaiView.PITCH, BonsaiView.DIST)
	var soil := view.plant_screen_position(0) + Vector2(0, 10)
	await _tap_thing(view, "trowel")
	main.journal.clear_pages()
	await _point(soil + Vector2(-60, -40))
	await _seconds(0.4)
	_shot("repot_trowel_held")
	await _tap(soil)
	await _seconds(0.3)
	_shot("repot_trowel_dig")
	await _seconds(0.9)
	main.journal.clear_pages()
	await _wait(2)
	_shot("repot_lifted")
	await _tap_thing(view, "shears")
	main.journal.clear_pages()
	var ball := view.plant_screen_position(0) + Vector2(20, -20)
	await _point(ball)
	await _tap(ball)
	await _tap(ball)
	await _seconds(0.3)
	_shot("repot_trimmed")
	await _tap_button("grey rectangle")
	await _wait(3)
	_shot("repot_pot_picked")
	await _tap_thing(view, "trowel")
	await _point(soil + Vector2(-60, -40))
	await _tap(soil)
	await _seconds(1.0)
	main.journal.clear_pages()
	await _wait(3)
	_shot("repot_done")
	print("repotted: pot %s, due %s" % [b.pot, b.repot_due])
	await _put_down(view)
	await _seconds(0.5)


func _bonsai_sequence() -> void:
	var st: GameState = main.state
	main.shed_menu.close_boards()
	main.journal.clear_pages()
	# The first tree finished: the juniper stands on the sill.
	if st.grove.is_empty():
		st.grove.append({"species": st.sim.species.id, "days": days, "seed": 42})
	st.ensure_bonsai()
	for k in ["bonsai", "bonsai_water", "bonsai_fertiliser", "bonsai_wire", "bonsai_repot", "bonsai_pinch", "bonsai_shears", "bonsai_burn"]:
		st.seen_pages[k] = true
	var b := st.bonsai
	_care(b, 3, false)
	main.in_shed = false
	main.enter_shed(false)
	main.state.seen_pages.erase("shed_used_bonsai")
	await _wait(30)
	_shot("bonsai_sill")
	# Bonsai mode: the camera glides close to the pot (a real tap on the sill finds it).
	var sill: Vector2 = main.shed.camera.unproject_position(main.shed.bonsai_spot.global_position + Vector3(0, 0.15, 0))
	print("a tap on the sill finds: ", main.shed.item_at(sill))
	main.open_shed_item("bonsai")
	await _seconds(1.3)
	main.journal.clear_pages()
	await _wait(3)
	# First visit: every thing on the sill carries its label.
	_shot("bonsai_mode")
	var view: BonsaiView = main.bonsai_view
	main._apply_setting("clearer_print", true)
	await _wait(8)
	_shot("bonsai_mode_clearer_print")
	main._apply_setting("clearer_print", false)
	await _wait(3)
	await _tools_sequence(view, b)
	await _wait(12)
	_shot("bonsai_tools_used")
	if tools_only:
		await _repot_sequence(view, b)
		return
	# Watering: the can tilts over the pot, the soil darkens.
	b.moisture = 0.02
	view.refresh(true)
	await _wait(3)
	_shot("bonsai_dry")
	view.water()
	await _seconds(1.35)
	_shot("bonsai_watering")
	await _seconds(1.4)
	main.journal.clear_pages()
	await _wait(3)
	_shot("bonsai_watered")
	# Fertiliser: the tin tilts, pellets land on the soil.
	view.fertilise(0)
	await _seconds(0.95)
	_shot("bonsai_fertiliser")
	await _seconds(1.0)
	view.fertilise(2)
	await _seconds(1.9)
	main.journal.clear_pages()
	view.look_from(0.2, 0.75, 0.42)
	await _wait(4)
	_shot("bonsai_pellets")
	# Wire: a side branch coiled in copper and bent down.
	_care(b, 4, false)
	view.refresh(true)
	var id := _side_branch(b, 16)
	if id >= 0:
		var dir := b.graph.direction_of(id)
		b.wire(id, (dir + Vector3(0, -0.9, 0)).normalized())
	view.refresh(true)
	view.set_tool("wire")
	print("wire on %d: %d green, radius %.3f, at %s, chain %s, wired %s" % [id, b.subtree_leafy(id), b.graph.radii[id], b.graph.positions[id], b.wire_chain(id), b.wired()])
	_look_at_wire(view, b, id, 0.27)
	main.journal.clear_pages()
	await _seconds(0.5)
	_shot("bonsai_wire")
	# Left on too long: the wire bites; taken off, the scar stays.
	for _d in range(BonsaiSim.WIRE_BITE_DAYS + 3):
		b._new_day_passed()
	if id >= 0:
		b.unwire(id)
	b.repot_due = false
	view.refresh(true)
	_look_at_wire(view, b, id, 0.22)
	await _wait(4)
	_shot("bonsai_wire_scar")
	view.set_tool("")
	await _seconds(0.5)
	await _repot_sequence(view, b)


## The bonsai over longer care: a juniper after 14 days, its album and style pages, a linden
## cutting, back to the bench, and the night.
func _bonsai_rest() -> void:
	var st: GameState = main.state
	var view: BonsaiView = main.bonsai_view
	var b: BonsaiSim
	# A juniper after 14 care days of watering, pellets, pinching and a few cuts (a fresh one
	# from the nursery, so the days count from its first).
	b = BonsaiSim.starter(hash([42, "bonsai"]))
	b.clock.time_of_day = st.sim.clock.time_of_day
	st.bonsai = b
	view.setup(st)
	_care(b, 14, true)
	b.moisture = 0.55
	view.refresh(true)
	view.look_from(0.0, 0.22, BonsaiView.DIST)
	await _wait(4)
	_shot("bonsai_juniper_14days")
	view.look_from(-0.6, 0.3, BonsaiView.DIST)
	await _wait(4)
	_shot("bonsai_juniper_14days_side")
	print("juniper day %d: %d green, %d nodes, %.2f units tall, pot %s" % [b.day(), b.leafy_count(), b.graph.size(), b.height(), b.pot])
	main.bonsai_hud._open_album()
	await _wait(4)
	_shot("bonsai_album")
	main.bonsai_hud.close_sheet()
	for i in range(BonsaiHud.STYLES.size()):
		main.bonsai_hud._open_styles(i)
		await _wait(4)
		_shot("bonsai_style_%d_%s" % [i + 1, BonsaiHud.STYLES[i]])
	main.bonsai_hud.close_sheet()
	# A linden cutting from the finished tree, fresh and after ten days.
	st.swap_bonsai("linden")
	view.setup(st)
	b = st.bonsai
	b.clock.time_of_day = st.sim.clock.time_of_day
	view.refresh(true)
	main.bonsai_hud._open_cuttings()
	await _wait(4)
	_shot("bonsai_cuttings")
	main.bonsai_hud.close_sheet()
	view.look_from(0.0, 0.25, 0.55, Vector3(0, 0.12, 0))
	await _wait(4)
	_shot("bonsai_linden_cutting")
	_care(b, 10, true)
	view.refresh(true)
	view.look_from(0.0, 0.22, BonsaiView.DIST)
	await _wait(4)
	_shot("bonsai_linden_10days")
	print("linden day %d: %d green, %.2f units tall" % [b.day(), b.leafy_count(), b.height()])
	st.swap_bonsai("juniper")
	view.setup(st)
	main.leave_bonsai()
	await _seconds(1.0)
	_shot("bonsai_back_to_bench")
	# Night: the day runs out, the lantern lights the sill; then the juniper close up by night.
	while st.phase == GameState.Phase.DAY:
		st.tick(0.5)
	st.take_events()
	await _wait(10)
	main.journal.clear_pages()
	await _wait(20)
	_shot("bonsai_sill_night")
	main.open_shed_item("bonsai")
	await _seconds(1.3)
	main.journal.clear_pages()
	view.look_from(0.0, 0.22, BonsaiView.DIST)
	await _wait(8)
	_shot("bonsai_juniper_night")
