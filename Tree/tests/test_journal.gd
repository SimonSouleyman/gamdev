extends RefCounted
## The shorter journal with doodles and a handwritten font (0.8.2, specs/0.8.md items 19, 21 to
## 24): a day page holds at most three short lines, each fits one line on the phone in both
## hands; every page carries an ink doodle about its topic; every species has its seed or leaf in
## the seed bag; the bundled fonts are OFL and show every character the game writes.

var t

## The diary's text width on the phone (Journal shots: 472 to 482 px at 450x1000 and 720x1600,
## the width never changes in portrait), less room for the scroll bar.
const LINE_WIDTH := 450.0
## The diary's sizes: 25 px in the normal hand, 30 px in clearer print (Paper.CLEAR_PRINT_SCALE).
const BODY_SIZE := 25
## Characters a player may type into a note or that dates and names can bring.
const EXTRA_CHARS := "äöüÄÖÜß°–—…‘’“”'\"€·×"
## The book's day doodle and the pages' doodle sizes leave room for the line; the "●" of the HUD
## readouts uses the default font, not a hand.
const NOT_HANDWRITTEN := "●"


func _fonts() -> Array[Font]:
	return [load(Paper.BODY_FONT), load(Paper.CLEAR_FONT), load(Paper.HEADING_FONT)]


func _width(text: String, font: Font, size: int) -> float:
	return font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, size).x


## Every line the game can write into the diary, at its longest (long tree name, two-word
## compass points).
func _all_game_lines() -> Array[String]:
	var out: Array[String] = []
	var ground := Underground.new()
	for kind in range(4):
		for where in [Vector3(5, -1, -5), Vector3(-5, -1, 5), Vector3(0, -1, -6)]:
			out.append(Diary.wish_entry(kind, where))
			out.append(Diary.wish_entry(kind, where, true))
			out.append(Diary.wish_entry(kind, where, false, true))
			out.append(Diary.wish_entry(kind, where, true, true))
	for kind in range(4):
		ground.patches.append({"kind": kind, "center": Vector3(5, -1, 5), "radius": 1.0})
		out.append(Diary.reached_text(ground, ground.patches.size() - 1, 28))
	# The Today page's words (0.8.2.6, J2) and the reached wish.
	out.append_array(Journal.LACK_WORDS)
	out.append_array([Journal.NOTHING_MISSING, "Tonight: far away, toward the wish.", "Tonight: toward the wish.", "Tonight: none within reach."])
	for plant in Diary.PLANTS:
		out.append("Tonight: near the %s." % plant)
		out.append("Wish found: the %s." % plant)
	for k in GameState.FIND_LINES:
		out.append(str(GameState.FIND_LINES[k]))
	for k in Visitors.LINES:
		out.append(str(Visitors.LINES[k]))
	for k in Clearing.FIRST_LINES:
		out.append(str(Clearing.FIRST_LINES[k]))
	out.append_array([GameState.HEDGEHOG_LINE, GameState.WREN_LINE, GameState.PILE_LINE])
	out.append(Backup.diary_line({"made_at_unix": 1790000000.0}))
	# The lines that name the tree, with the longest name.
	var longest := ""
	for sid in Species.ORDER:
		var n := Species.from_id(sid).display_name.to_lower()
		if n.length() > longest.length():
			longest = n
	var g := GameState.new_game(5, "oak")
	out.append(str(g.diary.entries[0]["text"]))
	out.append("I took a cutting of the %s." % longest)
	out.append_array(["In blossom: the bees have come.", "Fully grown; a new seed waits.", "A juniper waits on the windowsill.", "The seed sprouted at dawn.",
		"Night 28: no life force left for a root.", "Night 28: the roots fill the soil now."])
	return out


func test_every_game_line_is_short_and_fits_one_phone_line() -> void:
	var lines := _all_game_lines()
	t.check(lines.size() > 40, "the lines are all there (%d)" % lines.size())
	var body: Font = load(Paper.BODY_FONT)
	var clear: Font = load(Paper.CLEAR_FONT)
	for line in lines:
		t.check(line != "", "no empty line")
		t.check(line.split(" ", false).size() <= Diary.MAX_WORDS, "at most %d words: %s" % [Diary.MAX_WORDS, line])
		var w := maxf(_width(line, body, BODY_SIZE), _width(line, clear, roundi(BODY_SIZE * Paper.CLEAR_PRINT_SCALE)))
		t.check(w <= LINE_WIDTH, "one line on the phone, either hand (%.0f px): %s" % [w, line])


## 0.8.2.6 (J3, broken 53): one game line a day, only when something happened; the wish, the
## need and the weather never fill a day page.
func test_a_day_page_holds_one_line_only_when_something_happened() -> void:
	t.check_eq(Diary.PAGE_MAX, 1, "one game line a day")
	var d := Diary.new()
	d.add(3, "Wish: the clover.", "tree", "clover", "wish")
	d.add(3, "Mist over the clearing this morning.", "tree", "mist", "mood")
	d.add(3, "Pale leaves: short of nitrogen.", "tree", "leaf_n", "care")
	d.add(3, "The first butterflies came to the leaves.", "tree", "butterflies", "visitor")
	d.add(3, "Found a lost coin, green with age.", "tree", "coin", "find")
	d.add(3, "my own note", "player")
	var page := d.page(3)
	t.check_eq(page.size(), 1, "one line")
	t.check_eq(str(page[0]["topic"]), "find", "the find before the visitor")
	t.check_eq(d.notes(3).size(), 1, "the player's note is kept apart")
	t.check_eq(d.doodle(3), "coin", "the page's doodle: its find")
	# A quiet day: the wish, the need, the weather and a plain old line show nothing.
	var q := Diary.new()
	q.add(1, "Wish: the rushes.", "tree", "rushes", "wish")
	q.add(1, "Thirsty: the leaves hang.", "tree", "leaf_water", "care")
	q.add(1, "Dew sparkled in the first sun.", "tree", "mist", "mood")
	q.add(1, "a plain note")
	t.check(q.page(1).is_empty(), "a quiet day stays empty")
	t.check(q.days().has(1), "but its date is there")
	t.check_eq(Diary.new().doodle(9), "sun", "a day without lines still has a doodle")
	# The night's root reached something: a line when nothing better came.
	q.add(1, Diary.DRANK_LINE, "tree", "fine_roots", "drank")
	t.check_eq(str(q.page(1)[0]["topic"]), "drank", "what the night reached")
	q.add(1, "In blossom: the bees have come.", "tree", "blossom", "milestone")
	t.check_eq(str(q.page(1)[0]["topic"]), "milestone", "a milestone before it")
	# Topics survive a save.
	var back := Diary.from_dict(JSON.parse_string(JSON.stringify(d.to_dict())))
	t.check_eq(str(back.page(3)[0]["topic"]), "find", "topics are saved")


func test_a_played_month_writes_short_day_pages() -> void:
	var g := GameState.new_game(11, "linden")
	var bot := RootBot.new()
	for _day in range(8):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	var body: Font = load(Paper.BODY_FONT)
	var wishes := 0
	for day in g.diary.days():
		var page := g.diary.page(day)
		t.check(page.size() <= Diary.PAGE_MAX, "day %d: at most one line (%d)" % [day, page.size()])
		t.check(InkSketch.has(g.diary.doodle(day)), "day %d: a doodle (%s)" % [day, g.diary.doodle(day)])
		for e in page:
			var text := str(e["text"])
			t.check(_width(text, body, BODY_SIZE) <= LINE_WIDTH, "day %d: one line: %s" % [day, text])
			t.check(Diary.SPECIAL.has(Diary.topic_of(e)), "day %d: only what happened: %s" % [day, text])
		for e in g.diary.lines_for_day(day):
			t.check(not ["care", "mood"].has(str(e.get("topic", ""))), "no routine or mood line written: " + str(e["text"]))
		if day >= 1 and g.diary.lines_for_day(day).any(func(e: Dictionary) -> bool: return str(e.get("topic", "")) == "wish"):
			wishes += 1
	t.check(wishes >= 7, "every morning's wish is kept as the day's record (%d)" % wishes)
	# The wish names its plant only (0.8.2.5: the meadow shows the place, the compass points to it).
	for e in g.diary.entries:
		if str(e.get("topic", "")) == "wish":
			var text := str(e["text"])
			t.check(not (text.contains("north") or text.contains("south") or text.contains("east") or text.contains("west")), "no direction: " + text)
			t.check(Diary.PLANTS.any(func(p: String) -> bool: return text.contains(p)), "the plant: " + text)


## 0.8.2.6 (J2, broken 52): Today has at most three lines, no numbers: the wish, what the tree
## lacks (with its mark), where tonight's root finds it. The need writes no diary line.
func test_today_names_the_wish_the_need_and_tonight() -> void:
	var g := GameState.new_game(11, "linden")
	var bot := RootBot.new()
	for _day in range(6):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	var lines := Journal.today_lines(g)
	t.check(lines.size() >= 2 and lines.size() <= 3, "two or three lines (%d)" % lines.size())
	t.check_eq(str(lines[0]["topic"]), "wish", "the wish first: " + str(lines[0]["text"]))
	var digits := RegEx.create_from_string("[0-9]")
	for l in lines:
		t.check(digits.search(str(l["text"])) == null, "no number: " + str(l["text"]))
	# A strong need: named in words with its mark, and tonight's line follows.
	g.sim.care_need = PackedFloat32Array([0.0, 0.9, 0.2, 0.0])
	g.sim.care_prev = PackedFloat32Array([0.0, 0.9, 0.2, 0.0])
	t.check_eq(Journal.main_need(g), Resources.Kind.NITROGEN, "nitrogen lacks most")
	lines = Journal.today_lines(g)
	t.check_eq(str(lines[1]["text"]), "Hungry for nitrogen.", "in words")
	t.check_eq(int(lines[1]["kind"]), Resources.Kind.NITROGEN, "with its mark")
	t.check(lines.size() == 3 and str(lines[2]["text"]).begins_with("Tonight: "), "and where tonight: " + str(lines.back()["text"]))
	# Nothing lacking.
	g.sim.care_need = PackedFloat32Array([0.0, 0.0, 0.0, 0.0])
	g.sim.care_prev = PackedFloat32Array([0.0, 0.0, 0.0, 0.0])
	t.check_eq(str(Journal.today_lines(g)[1]["text"]), Journal.NOTHING_MISSING, "nothing missing")
	t.check(g.diary.entries.all(func(e: Dictionary) -> bool: return str(e.get("topic", "")) != "care"), "no care line in the diary")


## 0.8.2.6 (J1, broken 51): three ribbons, no settings page; the hint pages behind the "notes"
## corner. J4 (broken 54): a first-time hint is one sentence at the screen's edge, not a page.
func test_the_book_has_three_ribbons_and_hints_are_one_sentence() -> void:
	var g := GameState.new_game(4, "linden")
	g.seen_pages["first_night"] = true
	var j := Journal.new()
	t.root.add_child(j)
	j.state = g
	j.open_diary()
	t.check_eq(j._tab_buttons.keys(), ["today", "diary", "collection"], "three ribbons")
	t.check(not j._tabs.has("settings"), "no settings page")
	t.check_eq(j.current_tab(), "today", "the book opens at Today")
	j._show_tab("notes", false)
	t.check(j._pages_list.get_child_count() >= 1, "the explanation pages wait at the back")
	j.read_page("first_night")
	t.check_eq(j.current_page(), "first_night", "a page read again opens in full")
	t.check(j._sheet_body.text.contains("WASD"), "with all its text")
	j.close_page()
	t.check(j.current_page() == "" and j.is_book_open(), "back to the book")
	j.close_diary()
	# In play: one ink sentence at the lower edge.
	j.show_page("sapling", Pages.title("sapling"), Pages.body("sapling"))
	t.check_eq(j.current_page(), "sapling", "the hint is up")
	t.check_eq(j.hint_text(), Pages.HINTS["sapling"], "its one sentence")
	t.check(not j._sheet.visible, "no torn-out page")
	t.check(j._page_sheet.anchor_top == 1.0 and j._page_sheet.anchor_bottom == 1.0, "at the screen's edge")
	var closed: Array = []
	j.page_closed.connect(func(id: String) -> void: closed.append(id))
	j._page_shown_at = -10.0
	j.close_page()
	t.check_eq(closed, ["sapling"], "gone on the tap, and the game hears it")
	t.check(not j.is_open(), "nothing left open")
	j.free()
	# Every hint is one short sentence.
	for id in Pages.ids():
		var h := Pages.hint(id)
		t.check(h != "" and h.split(" ", false).size() <= 13, "short hint for %s: %s" % [id, h])
		t.check(h.count(". ") == 0, "one sentence for %s: %s" % [id, h])


## The Collection lists the finds (from the drawers' hook when it is set), the visitors and the
## trees grown.
func test_the_collection_lists_finds_visitors_and_trees() -> void:
	var g := GameState.new_game(4, "linden")
	g.notify_find({"kind": "coin"})
	g.seen_pages["visitor_fox"] = true
	g.grove = [{"species": "birch", "days": 25, "seed": 4}]
	var j := Journal.new()
	t.root.add_child(j)
	j.state = g
	var finds := j.collection_finds()
	t.check(finds.size() == 1 and str(finds[0]["kind"]) == "coin", "the coin from the diary")
	t.check(Journal.collection_visitors(g).any(func(v: Dictionary) -> bool: return v["kind"] == "fox"), "the fox")
	j.finds_source = func() -> Array: return [{"kind": "fossil", "text": "A fossil shell.", "day": 2}]
	t.check_eq(str(j.collection_finds()[0]["kind"]), "fossil", "the drawers' hook")
	j.open_diary()
	j._show_tab("collection", false)
	var words := ""
	for row in j._collection_box.get_children():
		for c in row.get_children() + ([row] as Array):
			if c is Label:
				words += (c as Label).text + "|"
			for cc in c.get_children():
				if cc is Label:
					words += (cc as Label).text + "|"
	t.check(words.contains("A fossil shell.") and words.to_lower().contains("birch"), "the collection shows them: " + words)
	j.free()


func test_explanation_pages_are_a_third_and_keep_what_the_player_needs() -> void:
	var words := 0
	for id in Pages.TEXTS:
		var n := Pages.body(id).split(" ", false).size()
		words += n
		t.check(n <= 50, "page %s is short (%d words)" % [id, n])
	# 0.8's pages held 904 words; about a third now.
	t.check(words <= 904 * 0.4, "about a third of 0.8's length (%d of 904 words)" % words)
	var night := Pages.body("first_night")
	for need in ["blue dots are water", "green", "orange", "violet", "stick", "WASD", "dive", "life force", Pages.END_ROOT_NAME]:
		t.check(night.contains(need), "the first night still says: " + need)
	var sapling := Pages.body("sapling")
	for need in ["Tap", "east", "west", "pinch", "life force"]:
		t.check(sapling.contains(need), "the sapling page still says: " + need)
	var sunset := Pages.body("first_sunset")
	for need in ["Tap the ground", "rushes", "clover", "nettles", "comfrey", "stones"]:
		t.check(sunset.contains(need), "the sunset page still says: " + need)
	for need in ["Water", "pellets", "turn the pot", "shears", "tweezers", "wire"]:
		t.check(Pages.body("bonsai").contains(need), "the bonsai page still says: " + need)


func test_every_page_has_a_doodle_about_its_topic() -> void:
	for id in Pages.ids():
		var kind := Pages.doodle(id)
		t.check(InkSketch.has(kind), "page %s has a doodle (%s)" % [id, kind])
	var expect := {"shears": "shears", "bonsai_shears": "shears", "bonsai_water": "can", "bonsai_fertiliser": "tin",
		"bonsai_pinch": "tweezers", "bonsai_wire": "wire", "bonsai_repot": "trowel", "bonsai": "bonsai",
		"first_sunset": "sunset", "empty_night": "moon", "first_night": "root", "species_oak": "seed_oak"}
	for id in expect:
		t.check_eq(Pages.doodle(id), expect[id], "the doodle matches page " + id)
	for k in Underground.FIND_TEXTS:
		t.check(InkSketch.has(k), "a find's page has its doodle: " + k)
	for k in Visitors.LINES:
		t.check(InkSketch.has(k), "a visitor's doodle: " + k)
	for k in Clearing.FIRST_LINES:
		t.check(InkSketch.has(k), "a clearing find's doodle: " + k)
	for k in GameState.CARE_LEAVES + Care.NEED_LEAVES + ["leaf_ok"]:
		t.check(InkSketch.has(k), "the leaf of a need: " + k)


func test_every_doodle_is_drawn_and_distinct() -> void:
	var seen := {}
	for kind in InkSketch.KINDS:
		var img := InkSketch.image(kind)
		var inked := 0
		var sig := PackedByteArray()
		for y in range(0, img.get_height(), 2):
			for x in range(0, img.get_width(), 2):
				var on := img.get_pixel(x, y).a > 0.5
				if on:
					inked += 1
				if x % 8 == 0 and y % 8 == 0:
					sig.append(1 if on else 0)
		t.check(inked > 60, "%s is drawn (%d inked)" % [kind, inked])
		t.check(inked < 64 * 64 * 0.5, "%s stays a line drawing" % kind)
		var key := Marshalls.raw_to_base64(sig)
		t.check(not seen.has(key) or kind == "leaf_burnt", "%s differs from the others" % kind)
		seen[key] = kind


func test_the_seed_bag_shows_every_species_with_its_picture() -> void:
	var menu := ShedMenu.new()
	for sid in Species.ORDER:
		t.check(InkSketch.has(InkSketch.species_kind(sid)), "%s has its seed or leaf" % sid)
		var open_row := menu.seed_row(sid, true, true)
		var locked_row := menu.seed_row(sid, false, true)
		var pic := open_row.get_child(0) as TextureRect
		var faint := locked_row.get_child(0) as TextureRect
		t.check(pic != null and pic.texture != null, "%s: a picture" % sid)
		t.check(faint.modulate.a < 0.5 and pic.modulate.a > 0.9, "%s: a locked one is drawn faint" % sid)
		var words := locked_row.get_child(1)
		var says := ""
		for c in words.get_children():
			if c is Label:
				says += (c as Label).text + " "
		t.check(says.contains("locked"), "%s: a locked one says so: %s" % [sid, says])
		var plant := open_row.get_child(1).get_child(0)
		t.check(plant is Button, "%s: an open one is planted with a tap" % sid)
		open_row.free()
		locked_row.free()
	t.check(InkSketch.has(InkSketch.species_kind("juniper")), "the bonsai's juniper too")
	menu.free()


func test_the_fonts_are_ofl_and_show_every_character_the_game_writes() -> void:
	for pair in [["Kalam-Regular.ttf", "OFL-Kalam.txt"], ["PatrickHand-Regular.ttf", "OFL-PatrickHand.txt"], ["Caveat-Regular.ttf", "OFL-Caveat.txt"]]:
		t.check(ResourceLoader.exists("res://ui/fonts/" + pair[0]), pair[0] + " is bundled")
		var lic := FileAccess.get_file_as_string("res://ui/fonts/" + pair[1])
		t.check(lic.contains("SIL Open Font License") or lic.contains("SIL OPEN FONT LICENSE"), pair[1] + " is the OFL")
	t.check(FileAccess.get_file_as_string("res://assets/CREDITS.md").contains("Kalam"), "Kalam is credited")
	t.check_eq((Paper.hand_font(false) as FontFile).resource_path, Paper.BODY_FONT, "Kalam is the body hand")
	t.check_eq((Paper.clear_font() as FontFile).resource_path, Paper.CLEAR_FONT, "clearer print keeps Patrick Hand")
	t.check_eq((Paper.hand_font(true) as FontFile).resource_path, Paper.HEADING_FONT, "Caveat stays for headings")
	t.check(Paper.is_hand_font(Paper.clear_font()) and Paper.is_hand_font(Paper.hand_font(false)), "both hands switch with clearer print")
	# Every character in the game's strings, plus what a player may type.
	var chars := {}
	for c in EXTRA_CHARS:
		chars[c] = true
	for code in range(32, 127):
		chars[String.chr(code)] = true
	for dir in ["res://shared", "res://ui", "res://shed", "res://tree", "res://roots"]:
		for f in DirAccess.get_files_at(dir):
			if f.ends_with(".gd"):
				for c in _literal_chars(FileAccess.get_file_as_string(dir.path_join(f))):
					chars[c] = true
	for c in _literal_chars(FileAccess.get_file_as_string("res://main.gd")):
		chars[c] = true
	var fonts := _fonts()
	for c: String in chars:
		if NOT_HANDWRITTEN.contains(c):
			continue
		for f in fonts:
			t.check(f.has_char(c.unicode_at(0)), "%s shows '%s' (U+%04X)" % [f.resource_path.get_file(), c, c.unicode_at(0)])


## The characters inside the string literals of a script.
func _literal_chars(src: String) -> Dictionary:
	var out := {}
	var inside := false
	var prev := ""
	for c in src:
		if c == "\"" and prev != "\\":
			inside = not inside
		elif inside and c != "\n" and c != "\t" and c.unicode_at(0) >= 32:
			out[c] = true
		prev = c
	return out
