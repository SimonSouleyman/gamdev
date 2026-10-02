extends RefCounted
## 0.8.2.1 look and phone fixes (docs/notes/look-0.8.2.1.md).
var t

const SIM_STEP := 1.0 / 30.0


## The first night with the root bot, then to the next morning.
func _play_day(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3:
		g.tick(0.5)
	g.take_events()


## Phone test 0.8.2: after "start over" the morning photo showed a big autumn tree behind a speck
## of a sapling (the album framed the grown tree from the clearing's edge, and the forest tree
## behind filled the photo), and hand-sized red leaves fell beside the day-1 sapling at night.
## The new game is clean: its own small tree, framed so the photo shows it, no falling leaves.
func test_a_new_game_after_start_over_is_clean() -> void:
	var tv := TreeView.new()
	t.root.add_child(tv)
	var old := GameState.new_game(42)
	for _d in range(6):
		_play_day(old)
	tv.setup(old)
	var fresh := GameState.new_game(43)
	_play_day(fresh)
	tv.setup(fresh)
	var box := tv._tree_mesh.get_aabb()
	t.check(box.size.y < maxf(fresh.sim.height() * 1.5, 1.5), "the new tree's wood is the sapling's, not the old tree's (%.1f m)" % box.size.y)
	t.check(not tv.weather_fx._leaves.visible, "a sapling drops no autumn leaves")
	# The album's morning photo: the sapling stands large enough to see.
	tv.album_pose(true)
	var cam := tv.camera
	var vp := tv.get_viewport().get_visible_rect().size
	var foot := cam.unproject_position(Vector3.ZERO)
	var top := cam.unproject_position(Vector3(0, fresh.sim.height(), 0))
	var share := (foot.y - top.y) / vp.y
	t.check(share > 0.15, "the sapling fills more than a sixth of the morning photo (%.2f)" % share)
	t.check(cam.global_position.length() < 12.0, "the photo is taken from a few metres, not the clearing's edge (%.1f m)" % cam.global_position.length())
	tv.album_pose(false)
	t.check(TreeView.album_stage(30.0, 22.0) == 22.0, "a grown tree is framed at its grown size")
	t.check(TreeView.album_stage(1.0, 22.0) <= TreeView.album_stage(4.0, 22.0), "the album steps back as the tree grows")
	tv.free()


## Tap targets about 9 mm on the test phone (Paper.INK_TAP), and the run's "let roots spread" as tall.
func test_tap_targets_are_large_enough() -> void:
	var b := Paper.ink_button("close", 26)
	t.check(b.get_combined_minimum_size().y >= 104.0, "an ink word's tap area is at least 104 px tall")
	b.free()
	var rv := RootView.new()
	t.root.add_child(rv)
	var h := rv.end_button.offset_bottom - rv.end_button.offset_top
	t.check(h >= 100.0, "the run's end button is a full tap target (%d px)" % h)
	rv.free()


## The shed's labels never overlap ("photo album" and "seed bag" did).
func test_shed_labels_do_not_overlap() -> void:
	var menu := ShedMenu.new()
	t.root.add_child(menu)
	menu.show_menu(true)
	# Two labels whose things stand side by side (the album and the seed bag on the bench).
	var two := {}
	var keys := menu._tags.keys()
	two[keys[0]] = Vector2(300, 400)
	two[keys[1]] = Vector2(330, 400)
	var two_shown := {keys[0]: true, keys[1]: true}
	for item in menu._tags:
		(menu._tags[item] as Control).modulate.a = 0.0
	for _i in range(12):
		menu.place_tags(two, {}, two_shown)
	var a: Control = menu._tags[keys[0]]
	var b: Control = menu._tags[keys[1]]
	t.check(not Rect2(a.position, a.size).intersects(Rect2(b.position, b.size)), "two labels side by side are pushed apart")
	menu.free()


## The night lifts the crown a little (a moonlit fill), the card forest turns in autumn.
func test_night_crown_and_autumn_forest() -> void:
	t.check(TreeView.NIGHT_FILL > 0.0 and TreeView.NIGHT_FILL_PHONE < TreeView.DAY_FILL_PHONE * 3.0, "a faint moonlit fill on the crown at night")
	var mat := ForestImpostors.material("trees")
	var fall: PackedVector4Array = mat.get_shader_parameter("cell_fall")
	var turns := 0
	for f in fall:
		if f.w > 0.0:
			turns += 1
	t.check(turns > 0, "the forest cards have autumn colours")
	t.check("autumn" in (mat.shader as Shader).code, "the card shader takes the season")
