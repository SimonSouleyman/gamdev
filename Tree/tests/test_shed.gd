extends RefCounted
## 0.6 shed: the "while you were away" page, haptics, clearer print and the pinboard switches.
var t


func _grown(seed: int) -> GameState:
	var g := GameState.new_game(seed)
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction * 0.3
	g.phase = GameState.Phase.DAY
	for k in range(4):
		g.sim.resources.add(k, 40.0)
	g.sim.resources.life_force = 40.0
	for _i in range(200):
		g.sim.tick(0.5)
	return g


func test_away_report_after_a_long_absence_once() -> void:
	var g := _grown(4)
	var nodes := g.sim.living_nodes()
	g.apply_offline(2.0 * 86400.0)
	var r := g.take_away_report()
	t.check(not r.is_empty(), "two days away leave a report")
	t.check_near(float(r.get("seconds", 0.0)), 2.0 * 86400.0, 1.0, "the time away")
	t.check_near(float(r.get("height", -1.0)), g.sim.height(), 1e-4, "the height now")
	t.check(float(r.get("grown", -1.0)) >= 0.0, "growth in metres is never negative")
	t.check_eq(int(r.get("segments", -1)), maxi(g.sim.living_nodes() - nodes, 0), "new segments counted")
	t.check(r.get("visitors") is Array, "visitors listed")
	t.check(g.take_away_report().is_empty(), "the page is shown once")


func test_no_report_for_a_short_absence() -> void:
	var g := _grown(5)
	g.apply_offline(600.0)
	t.check(g.take_away_report().is_empty(), "ten minutes away: no page")


func test_two_absences_before_reading_add_up() -> void:
	var g := _grown(6)
	g.apply_offline(3.0 * 3600.0)
	g.apply_offline(5.0 * 3600.0)
	t.check_near(float(g.take_away_report().get("seconds", 0.0)), 8.0 * 3600.0, 1.0, "both absences")


func test_away_page_words() -> void:
	t.check_eq(ShedMenu.away_duration(3600.0), "1 hour", "one hour")
	t.check_eq(ShedMenu.away_duration(86400.0), "1 day", "one day")
	t.check_eq(ShedMenu.away_duration(2.0 * 86400.0 + 5.0 * 3600.0), "2 days and 5 hours", "days and hours")
	var lines := ShedMenu.away_text("linden", {"seconds": 90000.0, "grown": 0.42, "height": 6.3, "segments": 40, "visitors": ["nest"]})
	t.check(lines[0].contains("1 day and 1 hour"), "how long: " + lines[0])
	t.check(lines[1].contains("0.4 m") and lines[1].contains("6.3 m"), "growth in metres: " + lines[1])
	t.check(lines.has(Visitors.LINES["nest"]), "the visitor that came")
	var quiet := ShedMenu.away_text("oak", {"seconds": 7200.0, "grown": 0.0, "segments": 0, "visitors": []})
	t.check(quiet.has("No visitors came this time."), "no visitors")
	t.check(quiet[1].contains("rested"), "no growth: " + quiet[1])


func test_pot_page_words() -> void:
	var g := _grown(7)
	var lines := ShedMenu.status_text(g)
	t.check(lines[0].contains("%.1f m" % g.sim.height()), "the height: " + lines[0])
	t.check(lines.has("No visitors yet."), "no visitors yet")


func test_haptics_switch() -> void:
	var was := Haptics.enabled
	Haptics.enabled = true
	t.check(Haptics.buzz("cut") and Haptics.buzz("dive") and Haptics.buzz("finished"), "cut, dive and finished buzz")
	t.check(not Haptics.buzz("nothing"), "an unknown moment does not")
	Haptics.enabled = false
	t.check(not Haptics.buzz("cut"), "switched off")
	Haptics.enabled = was


func test_clearer_print_switches_font_and_size_and_back() -> void:
	var box := VBoxContainer.new()
	var heading := Paper.ink_label("Tree", 40, Paper.INK, true)
	var body := Paper.ink_label("words", 30)
	var plain := Label.new()
	box.add_child(heading)
	box.add_child(body)
	box.add_child(plain)
	Paper.set_clear_print(true, box)
	t.check_eq(heading.get_theme_font("font"), Paper.clear_font(), "headings in the clear hand")
	t.check_eq(heading.get_theme_font_size("font_size"), 48, "a size larger")
	t.check_eq(body.get_theme_font_size("font_size"), 36, "body a size larger")
	t.check(not plain.has_theme_font_size_override("font_size"), "non-handwritten text untouched")
	Paper.set_clear_print(true, box)
	t.check_eq(body.get_theme_font_size("font_size"), 36, "switching twice does not grow it again")
	Paper.set_clear_print(false, box)
	t.check_eq(heading.get_theme_font("font"), Paper.hand_font(true), "the lively hand back")
	t.check_eq(heading.get_theme_font_size("font_size"), 40, "the size back")
	t.check_eq(body.get_theme_font_size("font_size"), 30, "body size back")
	box.free()


func test_pinboard_has_the_new_switches() -> void:
	var j := Journal.new()
	t.check_eq(j.settings.get("vibration"), true, "vibration on by default")
	t.check_eq(j.settings.get("clearer_print"), false, "clearer print off by default")
	j.free()
	t.check(ShedMenu.OPTION_NAMES.has("vibration") and ShedMenu.OPTION_NAMES.has("clearer_print"), "both on the pinboard")
	for item in Shed.ITEMS:
		t.check(ShedMenu.TAG_TEXTS.has(item), "every thing on the bench has a label: " + item)


## 0.8.1, item 28: the flower pot left the bench; the tree's page (how it is, the wish, an ink
## sketch) is the journal's first page, and the book opens there; "while you were away" still
## comes as its own page.
func test_tree_page_is_the_journals_first_page() -> void:
	t.check(not Shed.ITEMS.has("pot") and not ShedMenu.TAG_TEXTS.has("pot"), "no flower pot on the bench")
	var g := _grown(12)
	g.diary.wish = "Today, the tree would like some water."
	var j := Journal.new()
	t.root.add_child(j)
	j.state = g
	j.open_diary()
	t.check_eq(j.current_tab(), "tree", "the book opens at the tree's page")
	t.check_eq(j._tab_buttons.keys()[0], "tree", "its ribbon is the first")
	var lines := Journal.tree_page_lines(g)
	t.check(lines[0].contains("%.1f m" % g.sim.height()), "how tall it is: " + lines[0])
	t.check(lines[1].begins_with("A wish: today"), "the day's wish: " + lines[1])
	var words: Array[String] = []
	for c in j._tree_box.get_children():
		if not c.is_queued_for_deletion():
			words.append((c as Label).text)
	t.check_eq(words, lines, "the page shows them")
	t.check(j._tree_sketch.custom_minimum_size.y >= 300.0, "with room for the ink sketch")
	# Another ribbon, closed and opened again: where it was left.
	j._show_tab("diary", false)
	j.close_diary()
	j.open_diary()
	t.check_eq(j.current_tab(), "diary", "then it opens where it was left")
	j.free()
	# The page is never lost: a new tree's page is that tree's.
	var h := GameState.new_tree(5, "linden", g)
	t.check(Journal.tree_page_lines(h)[0].contains("linden"), "a new tree has its own page: " + Journal.tree_page_lines(h)[0])
	# "While you were away" still opens as its own page.
	var menu := ShedMenu.new()
	t.root.add_child(menu)
	g.apply_offline(2.0 * 86400.0)
	var report := g.take_away_report()
	menu.show_tree_page(g, report)
	t.check(menu.is_tree_page_open(), "the away page opens on return")
	menu.free()


## 0.8.1, item 26: on the phone's tall screen (and the reference one) the shed's view holds the
## sill with the bonsai and the pinboard whole, and every thing answers a tap where it is.
func test_shed_view_fits_the_phone() -> void:
	for canvas in [Vector2i(720, 1600), Vector2i(720, 1280), Vector2i(1280, 720)]:
		var vp := SubViewport.new()
		vp.size = canvas
		vp.disable_3d = false
		t.root.add_child(vp)
		var shed := Shed.new()
		vp.add_child(shed)
		shed.bonsai_ready = true
		shed.camera.current = true
		shed.fit_view()
		var where := "%dx%d" % [canvas.x, canvas.y]
		for p in Shed.must_see():
			var s := shed.camera.unproject_position(shed.to_global(p))
			t.check(not shed.camera.is_position_behind(shed.to_global(p)) and s.x >= 0.0 and s.x <= canvas.x and s.y >= 0.0 and s.y <= canvas.y, "%s: %s on screen (%s)" % [where, p, s])
		for item in Shed.ITEMS:
			var c: Node3D = shed._picks[item][0]
			var s := shed.camera.unproject_position(c.global_position)
			t.check(s.x > 20.0 and s.x < canvas.x - 20.0 and s.y > 20.0 and s.y < canvas.y - 20.0, "%s: %s inside the screen (%s)" % [where, item, s])
			t.check_eq(shed.item_at(s), item, "%s: a tap on the %s finds it" % [where, item])
			# Finger size on a phone (portrait): its tap circle at least 9 mm (100 canvas px).
			t.check(canvas.x > canvas.y or shed._screen_radius(c.global_position, float(shed._picks[item][1])) * 2.0 >= 100.0, "%s: %s at least finger size" % [where, item])
		vp.free()


## 0.8.2 (look review, sillzoom): on a narrow phone screen the sill's tools keep a margin from
## the left edge, and the bonsai's label hangs under the sill's edge, below the tools.
func test_the_sill_keeps_its_tools_clear() -> void:
	for canvas in [Vector2i(450, 1000), Vector2i(450, 800), Vector2i(720, 1600)]:
		var vp := SubViewport.new()
		vp.size = canvas
		vp.disable_3d = false
		t.root.add_child(vp)
		var shed := Shed.new()
		vp.add_child(shed)
		shed.bonsai_ready = true
		shed.camera.current = true
		shed.fit_view()
		var where := "%dx%d" % [canvas.x, canvas.y]
		var spot := shed.bonsai_spot.position
		var lowest := 0.0
		for id in BonsaiTools.RESTS:
			var r: Array = BonsaiTools.RESTS[id]
			var p: Vector3 = spot + (r[0] as Vector3)
			var s := shed.camera.unproject_position(shed.to_global(p))
			t.check(s.x >= canvas.x * 0.05 and s.x <= canvas.x * 0.95, "%s: the %s keeps off the edge (%.0f px)" % [where, id, s.x])
			if (r[0] as Vector3).y < 0.01:
				lowest = maxf(lowest, s.y)
		var edge := shed.camera.unproject_position(shed.to_global(spot + Vector3(BonsaiTools.RESTS["water"][0].x + 0.06, 0.12, 0.05)))
		t.check(edge.x >= 24.0, "%s: the can's outer side %.0f px from the edge" % [where, edge.x])
		var tag := shed.item_tag_position("bonsai")
		t.check(shed.item_tag_below("bonsai") and tag.y > lowest, "%s: the label hangs below the tools (%.0f > %.0f)" % [where, tag.y, lowest])
		vp.free()


## 0.8.2.2 (Simon: "im Schuppen sieht man noch viel Boden"; specs/0.8.md item 43): a tighter view.
## The workbench stands nearer, so its foot meets the picture's foot and only a strip of floor
## (with a rug) is left; the door, its frame, the pinboard and the sill stay whole on screen.
func test_the_shed_shows_little_floor() -> void:
	for canvas in [Vector2i(450, 1000), Vector2i(720, 1600), Vector2i(450, 800)]:
		var vp := SubViewport.new()
		vp.size = canvas
		vp.disable_3d = false
		t.root.add_child(vp)
		var shed := Shed.new()
		vp.add_child(shed)
		shed.bonsai_ready = true
		shed.camera.current = true
		shed.fit_view()
		var where := "%dx%d" % [canvas.x, canvas.y]
		var foot := shed.camera.unproject_position(shed.to_global(Vector3(0.0, 0.1, Shed.BENCH_Z - 0.32)))
		t.check(foot.y >= canvas.y * 0.9, "%s: the bench's foot at %.0f%% of the height (floor below it: %.0f%%)" % [where, foot.y / canvas.y * 100.0, 100.0 - foot.y / canvas.y * 100.0])
		var hd := Shed.DEPTH * 0.5
		for p in [Vector3(Shed.DOOR_W * 0.5 + 0.07, Shed.DOOR_H + 0.08, hd), Vector3(-Shed.DOOR_W * 0.5 - 0.07, Shed.DOOR_H + 0.08, hd)]:
			var s := shed.camera.unproject_position(shed.to_global(p))
			t.check(s.x >= 0.0 and s.x <= canvas.x and s.y >= 0.0, "%s: the door frame whole (%s)" % [where, s])
		vp.free()
