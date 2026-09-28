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
