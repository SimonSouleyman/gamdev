extends SceneTree
## Screenshots of the garden shed (start menu): the workbench with its things and their labels,
## each thing hovered and mid-tap, the options pinboard (also in "clearer print"), the album,
## the journal, the seed bag, the flower pot's page, the "while you were away" page and the
## loading page.
## Then the bonsai (design doc section 16): the sill with the bonsai, bonsai mode, watering,
## fertiliser, a wire and a wire scar, repotting, a juniper after 14 care days, a linden cutting,
## the style pages and the bonsai's album page.
## Run: godot --path . -s tools/shed_shot.gd -- --shots=C:/some/folder [--days=6] [--species=linden] [--bonsai-only]

var main: Node
var shots := ""
var days := 6
var species := "linden"
var frame := 0
var bonsai_only := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a == "--bonsai-only":
			bonsai_only = true
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


func _process(_d: float) -> bool:
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
		quit()
		return
	# First visit: every thing on the bench carries its label.
	await _wait(38)
	_shot("shed_menu")
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


## The camera beside a wired branch, a little below its pad (the wire shows under the foliage).
func _look_at_wire(view: BonsaiView, b: BonsaiSim, id: int, dist: float) -> void:
	var dir := b.graph.direction_of(id).rotated(Vector3.UP, b.turn * PI * 0.5)
	var side := dir.cross(Vector3.UP)
	if side.z > 0.0:
		side = -side
	var v := (side + Vector3(dir.x, 0.0, dir.z) * 0.4).normalized()
	view.look_from(clampf(atan2(v.x, -v.z), -1.25, 1.25), -0.28, dist, _wire_focus(view, b, id))


## The middle of a wired branch, in the sill's frame.
func _wire_focus(view: BonsaiView, b: BonsaiSim, id: int) -> Vector3:
	var chain := b.wire_chain(id)
	return view.node_in_sill(chain[chain.size() / 2])


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
	# Bonsai mode: the camera glides close to the pot.
	main.open_shed_item("bonsai")
	await _seconds(1.3)
	main.journal.clear_pages()
	await _wait(3)
	_shot("bonsai_mode")
	var view: BonsaiView = main.bonsai_view
	# Watering: the can tilts over the pot, the soil darkens.
	b.moisture = 0.12
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
	_look_at_wire(view, b, id, 0.2)
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
	_look_at_wire(view, b, id, 0.17)
	await _wait(4)
	_shot("bonsai_wire_scar")
	view.set_tool("")
	# Repotting: lift out, trim the root ball, pick the pot, fresh soil.
	b.repot_due = true
	view.look_from(0.0, 0.2, BonsaiView.DIST)
	view.repot_lift()
	await _seconds(0.9)
	_shot("bonsai_repot_lift")
	view.repot_trim()
	view.repot_trim()
	await _wait(3)
	_shot("bonsai_repot_trimmed")
	view.repot_pick("rectangle")
	main.bonsai_hud._open_repot()
	await _wait(4)
	_shot("bonsai_repot_pot")
	main.bonsai_hud.close_sheet()
	view.repot_finish()
	await _seconds(0.9)
	main.journal.clear_pages()
	await _wait(3)
	_shot("bonsai_repotted")
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
