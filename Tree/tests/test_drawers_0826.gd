extends RefCounted
## 0.8.2.6 (specs/journal-drawers-loop.md D1, D2; notes/drawers-0.8.2.6.md): the finds lie in the
## bench's drawers, a tap shows a find's note, a map scrap or shard marks something for the next
## night at most once a week, and no find ever changes growth, life force or nutrients.
## Broken list items 55 and 56.
var t

const SIM_STEP := 1.0 / 30.0


## A game a few nights in (the roots have grown; the tree is a sapling).
func _game(seed: int = 21, nights: int = 3) -> GameState:
	var g := GameState.new_game(seed)
	var bot := RootBot.new()
	for n in range(nights):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, SIM_STEP) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if n < nights - 1:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	g.take_events()
	return g


## The soil's next unfound find of this kind, touched as the roots touch it.
func _touch(g: GameState, kind: String) -> Dictionary:
	for f in g.ground.finds:
		if f["kind"] == kind and not f["found"]:
			f["found"] = true
			g.notify_find(f)
			return f
	return {}


# --- 55: every reached find lies in a drawer ----------------------------------------------

func test_the_soil_holds_map_scraps_and_shards() -> void:
	for layout in [1, 2, 3]:
		var u := Underground.new(5, layout)
		var kinds := {}
		for f in u.finds:
			kinds[f["kind"]] = int(kinds.get(f["kind"], 0)) + 1
			t.check(Finds.KINDS.has(str(f["kind"])), "a known kind (%s)" % f["kind"])
			if Finds.HINTS.has(str(f["kind"])):
				t.check(not u.is_inside_rock(f["position"]), "layout %d: %s not inside a rock" % [layout, f["kind"]])
		t.check_eq(int(kinds.get("map_scrap", 0)), Underground.MAP_SCRAPS, "layout %d: map scraps" % layout)
		t.check_eq(int(kinds.get("shard", 0)), Underground.SHARDS, "layout %d: shards" % layout)
	# The older finds keep their places and the dots their ids (the new ones come last, from a
	# generator of their own): a soil saved before 0.8.2.6 loads with its found flags.
	var u := Underground.new(9)
	u.finds[0]["found"] = true
	var d := u.to_dict()
	d["finds_found"] = (d["finds_found"] as Array).slice(0, 4)
	var v := Underground.from_dict(d)
	t.check_eq(v.finds.size(), u.finds.size(), "an old soil gets the new finds")
	t.check(v.finds[0]["found"], "an old soil keeps its found finds")
	t.check(not v.finds[-1]["found"], "the new finds are still in the ground")
	t.check_eq(v.dot_count(), u.dot_count(), "the dots are the same")


func test_a_reached_find_lies_in_its_drawer() -> void:
	var g := _game()
	for kind in Finds.KINDS:
		_touch(g, kind)
	var list := g.finds.list()
	var kinds: Array = []
	for f in list:
		kinds.append(f["kind"])
		t.check(Finds.DRAWER_NAMES.has(f["drawer"]), "%s in a drawer that shows (%s)" % [f["kind"], f["drawer"]])
		t.check(str(f["note"]) != "", "%s has its line" % f["kind"])
		t.check_eq(int(f["day"]), g.day_number(), "%s found tonight" % f["kind"])
	for kind in Finds.KINDS:
		t.check(kinds.has(kind), "%s kept" % kind)
	# The shed lays them out: each in its drawer, under that drawer's Contents.
	var pair := _shed()
	var shed: Shed = pair[1]
	shed.fill_drawers(list)
	t.check_eq(shed.find_count(), list.size(), "every find lies in the drawers")
	for key in Finds.DRAWER_NAMES:
		for f in shed.finds_in(key):
			t.check_eq(Finds.drawer_for(str(f["kind"])), key, "%s in %s" % [f["kind"], key])
	# Filling again with the same list keeps the same things (no rebuild on every visit).
	var first: Node3D = shed._finds_shown[0]["node"]
	shed.fill_drawers(g.finds.list())
	t.check(is_instance_valid(first) and shed._finds_shown[0]["node"] == first, "not rebuilt for the same finds")
	(pair[0] as Node).free()


func test_finds_are_saved_and_stay_from_tree_to_tree() -> void:
	var g := _game()
	_touch(g, "coin")
	_touch(g, "map_scrap")
	var back := GameState.from_dict(g.to_dict())
	t.check_eq(back.finds.count(), 2, "saved and loaded")
	t.check_eq(back.finds.list()[1]["kind"], "map_scrap", "in order")
	t.check_eq(back.finds.hint.get("night", -1), g.finds.hint.get("night", -2), "the hint saved")
	# Through the real save file's JSON too.
	var path := "user://test_drawers_0826.json"
	SaveData.save_game(g, path)
	var loaded := SaveData.load_game(path)
	t.check(loaded != null and loaded.finds.count() == 2, "through the save file")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	# The next tree keeps them (they belong to the garden).
	g.grove.append({"species": "linden", "days": 30, "seed": 21})
	var next := GameState.new_tree(77, "oak", g)
	t.check_eq(next.finds.count(), 2, "the next tree keeps the finds")
	_touch(next, "fossil")
	var last: Dictionary = next.finds.list()[-1]
	t.check_eq(last["tree"], 2, "found under the second tree")
	t.check_eq(last["species"], "oak", "under the oak")


func test_an_old_save_puts_its_found_finds_in_the_drawers() -> void:
	var g := _game()
	_touch(g, "fossil")
	_touch(g, "coin")
	var d := g.to_dict()
	d.erase("finds")
	var old := GameState.from_dict(d)
	t.check_eq(old.finds.count(), 2, "the soil's found finds come into the drawers")
	for f in old.finds.list():
		t.check_eq(int(f["day"]), g.day_number(), "%s with the night of its diary line" % f["kind"])


# --- 56: a find never changes growth, life force or nutrients ---------------------------------

func test_a_find_changes_no_growth_life_force_or_nutrients() -> void:
	for kind in Finds.KINDS:
		var g := _game(33, 2)
		var stock := g.sim.resources.stock.duplicate()
		var life := g.sim.resources.life_force
		var nodes := g.sim.graph.size()
		var roots := g.roots.graph.size()
		var amounts := g.ground.dot_amounts.duplicate()
		var f := _touch(g, kind)
		t.check(not f.is_empty(), "%s touched" % kind)
		t.check_eq(g.sim.resources.stock, stock, "%s: the stock unchanged" % kind)
		t.check_eq(g.sim.resources.life_force, life, "%s: the life force unchanged" % kind)
		t.check_eq(g.sim.graph.size(), nodes, "%s: the tree unchanged" % kind)
		t.check_eq(g.roots.graph.size(), roots, "%s: the roots unchanged" % kind)
		t.check(g.ground.dot_amounts == amounts, "%s: the deposits unchanged" % kind)
		# The next day grows as the same day without the find would.
		var twin := _game(33, 2)
		for _i in range(400):
			g.tick(0.5)
			twin.tick(0.5)
		t.check_eq(g.sim.graph.size(), twin.sim.graph.size(), "%s: the day after grows the same" % kind)
		t.check_near(g.sim.resources.life_force, twin.sim.resources.life_force, 1e-4, "%s: the same life force a day on" % kind)


# --- D2: a hint for the next night, at most one a week ---------------------------------------

func test_a_map_scrap_marks_a_far_patch_for_the_next_night_only() -> void:
	var g := _game()
	var day := g.day_number()
	_touch(g, "map_scrap")
	var h := g.finds.hint
	t.check_eq(str(h.get("kind", "")), "patch", "a map scrap marks a patch")
	t.check_eq(int(h.get("night", -1)), day + 1, "for the next night")
	t.check(g.ground.far_patch_ids().has(int(h.get("patch", -1))), "a far patch")
	t.check(g.finds.hint_for(day).is_empty(), "not tonight")
	t.check(not g.finds.hint_for(day + 1).is_empty(), "the next night")
	t.check(g.finds.hint_for(day + 2).is_empty(), "only that night")
	t.check(g.finds.items[-1]["hint"], "the find gave it")
	# A second one in the same week gives none; a week later one may.
	_touch(g, "shard")
	t.check_eq(int(g.finds.hint["night"]), day + 1, "no second hint in a week")
	t.check(not g.finds.items[-1]["hint"], "the shard gave none")
	t.check(not g.finds.hint_allowed(g.tree_number(), day + Finds.HINT_EVERY_DAYS - 1), "not within the week")
	t.check(g.finds.hint_allowed(g.tree_number(), day + Finds.HINT_EVERY_DAYS), "a week later")
	# Other kinds never give one.
	var g2 := _game(22)
	for kind in ["fossil", "coin", "old_root", "water_vein"]:
		_touch(g2, kind)
	t.check(g2.finds.hint.is_empty(), "the soil's own finds give no hint")


func test_a_shard_marks_a_gap_in_a_rock_band() -> void:
	var g := _game()
	t.check(not g.ground.bands.is_empty(), "the wider field has bands")
	_touch(g, "shard")
	var h := g.finds.hint
	t.check_eq(str(h.get("kind", "")), "gap", "a shard marks a gap")
	var b: Dictionary = g.ground.bands[int(h["band"])]
	var p: Vector3 = h["pos"]
	# The mark lies in the gap: no rock there, and the band's rock on either side near it.
	t.check(g.ground.band_at(p) < 0, "no rock at the mark")
	var near_rock := false
	var pts: PackedVector2Array = b["points"]
	for q in pts:
		if q.distance_to(Vector2(p.x, p.z)) < 6.0:
			near_rock = true
	t.check(near_rock, "the band's line passes the mark")


func test_the_hint_shows_in_the_root_view() -> void:
	var g := _game()
	var rv := RootView.new()
	var holder := Node.new()
	t.root.add_child(holder)
	holder.add_child(rv)
	rv.setup(g.ground, g.roots, g.sim.resources)
	_touch(g, "map_scrap")
	rv.set_find_hint(g.finds.hint_for(g.day_number()))
	t.check(rv._hint_mark == null or not rv._hint_mark.visible, "no mark tonight (it is for the next night)")
	rv.set_find_hint(g.finds.hint_for(g.day_number() + 1))
	t.check(rv._hint_mark.visible, "the mark the next night")
	t.check(rv._hint_mark.global_position.is_equal_approx(g.finds.hint["pos"]), "over the patch")
	# It stays in the far view (the content's frame holds it).
	rv.begin_pick()
	if rv.open_far_view():
		t.check(rv._hint_mark.visible, "seen in the far view")
		t.check(rv._far_pts.has(g.finds.hint["pos"]), "the far view frames it")
	rv.set_find_hint({})
	t.check(not rv._hint_mark.visible, "gone without a hint")
	holder.free()


# --- the drawers on the phone: lean, read, tap a find -----------------------------------------

func _shed(canvas: Vector2i = Vector2i(450, 1000)) -> Array:
	var vp := SubViewport.new()
	vp.size = canvas
	vp.disable_3d = false
	t.root.add_child(vp)
	var shed := Shed.new()
	vp.add_child(shed)
	shed.camera.current = true
	shed.fit_view()
	return [vp, shed]


func _all_finds() -> Array:
	var f := Finds.new()
	for i in range(Finds.KINDS.size()):
		f.add(Finds.KINDS[i], i + 2, "linden", 1)
	# A second coin from a second tree.
	f.add("coin", 4, "oak", 2)
	return f.list()


func test_an_open_drawer_shows_its_finds_large_enough_to_tap() -> void:
	for canvas in [Vector2i(450, 1000), Vector2i(720, 1600), Vector2i(720, 1280)]:
		var pair := _shed(canvas)
		var shed: Shed = pair[1]
		shed.fill_drawers(_all_finds())
		for key in Finds.DRAWER_NAMES:
			shed.toggle_drawer(key)
			(shed._busy["drawer_" + key] as Tween).custom_step(5.0)
			shed.finish_lean()
			t.check_eq(shed.drawer_look(), key, "%s: the view leans over the open drawer" % key)
			var shown: Array[Vector2] = []
			for i in range(shed.find_count()):
				var e: Dictionary = shed._finds_shown[i]
				if e["drawer"] != key:
					continue
				var kind := str(e["item"]["kind"])
				var s := shed.find_screen(i)
				var tag := "%s %s %s" % [canvas, key, kind]
				t.check(s.x > 20 and s.x < canvas.x - 20 and s.y > canvas.y * 0.2 and s.y < canvas.y * 0.9, "%s on screen (%s)" % [tag, s])
				# Readable: a find spans at least 6 % of the screen's width.
				var n: Node3D = e["node"]
				var side := shed.camera.global_transform.basis.x * float(FindModels.SIZE[kind])
				var px := shed.camera.unproject_position(n.global_position).distance_to(shed.camera.unproject_position(n.global_position + side))
				t.check(px > canvas.x * 0.06, "%s large enough (%.0f px)" % [tag, px])
				# A tap on it finds it and shows its note.
				var hit := shed.find_at(s)
				t.check(hit >= 0 and str(shed._finds_shown[hit]["item"]["kind"]) == kind, "%s: a tap on it finds it" % tag)
				if hit >= 0 and shed._finds_shown[hit]["item"]["kind"] == kind:
					shed.show_note(hit)
					t.check(shed.note_text().begins_with(Finds.note(kind)), "%s: its line" % tag)
					t.check(shed.note_text().contains("night"), "%s: the night it was found" % tag)
				shown.append(s)
			t.check(shown.size() >= 2, "%s: its finds show (%d)" % [key, shown.size()])
			# A tap on the empty liner far from every find is none.
			t.check_eq(shed.find_at(Vector2(canvas.x * 0.5, canvas.y * 0.03)), -1, "%s: a tap off the finds is none" % key)
			# Closing straightens the view.
			shed.toggle_drawer(key)
			(shed._busy["drawer_" + key] as Tween).custom_step(5.0)
			shed.finish_lean()
			t.check_eq(shed.drawer_look(), "", "%s: upright again" % key)
			t.check(shed.camera.transform.is_equal_approx(shed._fit_xf), "%s: the room's view back" % key)
			t.check_eq(shed.note_text(), "", "%s: the note gone" % key)
		# Leaving the shed slides every drawer back.
		shed.toggle_drawer("drawer01")
		shed.close_drawers()
		t.check(not shed.is_drawer_open("drawer01"), "closed on leaving")
		t.check(shed.camera.transform.is_equal_approx(shed._fit_xf), "upright on leaving")
		(pair[0] as Node).free()
