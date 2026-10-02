extends RefCounted
## 0.8.2.5 (specs/wish-compass-vial.md items 1 to 3, notes/wish-0.8.2.5.md): a wish place every
## morning, its plant large and in flower on the meadow with an ink ring at sunset, the compass
## needle pointing to it, the species' Liebig floor and "the roots drank well". Broken items 45
## to 48.
var t

const OLD_SAVE := "res://tests/fixtures/tree-v0.8.1_midrun.json"
const FRAME: float = 1.0 / 30.0
const DIRECTIONS: Array[String] = ["north", "south", "east", "west"]


## One night with a full calm tank, steered toward the glow (`aim`) or wandering from the trunk.
func _night(u: Underground, roots: RootSystem, diary: Diary, day: int, aim: bool, wander: RandomNumberGenerator) -> void:
	var res := Resources.new()
	res.life_force = roots.calm_life_force
	var glows := diary.glows(u)
	var bot := RootBot.new()
	var from := 0
	if aim and not glows.is_empty():
		from = Diary.newest_tip(roots)
		bot.goal = glows[0]["center"]
		bot.goal_radius = float(glows[0]["radius"]) * 0.5
	roots.start_run(from)
	var stick := Vector2.ZERO
	for frame in range(6000):
		if aim:
			stick = bot.stick_for(roots, u, res)
		elif frame % 60 == 0:
			stick = Vector2(wander.randf_range(-0.6, 0.6), wander.randf_range(-0.6, 0.3))
		if not roots.advance(stick, false, FRAME, u, res):
			break
	if roots.run_active:
		roots.end_run(u, res)
	diary.check_reached(u, roots, day, day + 1)


## Broken item 45: a morning without a wish place, or a journal page with a day wish line.
func test_every_morning_has_a_wish_place_and_a_short_line() -> void:
	t.check_eq(Diary.underground_share, 1.0, "every morning points underground")
	var mornings := 0
	for seed in [3, 14, 27]:
		var u := Underground.new(seed)
		var roots := RootSystem.new(seed)
		var diary := Diary.new()
		var wander := RandomNumberGenerator.new()
		wander.seed = seed
		for day in range(1, 25):
			diary.new_wish(u, day, seed, roots)
			mornings += 1
			t.check(diary.wish_patch >= 0, "seed %d day %d: a wish place" % [seed, day])
			if diary.wish_patch < 0:
				continue
			var p: Dictionary = u.patches[diary.wish_patch]
			t.check(-(p["center"] as Vector3).y <= Underground.HINT_MAX_DEPTH, "in the topsoil, under the meadow")
			var line := ""
			for e in diary.page(day):
				if Diary.topic_of(e) == "wish":
					line = str(e["text"])
			var plant := Diary.PLANTS[int(p["kind"])]
			var far := Diary.is_far(u, diary.wish_patch)
			var want := ["Wish: the %s." % plant, "Still: the %s." % plant] if not far else ["Wish: the %s, far away." % plant, "Still: the %s, far away." % plant]
			t.check(want.has(line), "seed %d day %d: the plant only: '%s'" % [seed, day, line])
			for word in DIRECTIONS:
				t.check(not line.contains(word) and not diary.wish.contains(word), "no direction in '%s' / '%s'" % [line, diary.wish])
			t.check(diary.wish.begins_with("Today") and diary.wish.contains(plant), "the morning scrap names the plant: " + diary.wish)
			# Half the nights wander: missed deposits pile up and are wished for again.
			_night(u, roots, diary, day, day % 2 == 0, wander)
	t.check(mornings == 72, "all mornings ran")


func test_a_full_soil_still_gives_a_wish_place() -> void:
	var u := Underground.new(5)
	var roots := RootSystem.new(5)
	var diary := Diary.new()
	var res := Resources.new()
	# Rock all round and no new place in reach: the fallback is a deposit already in the soil.
	for kind in range(4):
		var pid := Diary.fallback_patch(u, roots, kind)
		t.check(pid >= 0, "kind %d: a deposit to wish for" % kind)
		if pid >= 0:
			t.check(Vector2((u.patches[pid]["center"] as Vector3).x, (u.patches[pid]["center"] as Vector3).z).length() >= 2.0, "never the starter patch")
	# The dot budget full: add_wish_deposit refuses, the wish still points somewhere.
	var keep := Budgets.NUTRIENT_DOTS_LOADED
	while u.dot_count() + 200 <= Budgets.NUTRIENT_DOTS_LOADED:
		u.add_wish_deposit(0, 0, Vector3(30, -1, 30), 1.0, 200)
	diary.new_wish(u, 3, 5, roots, res)
	t.check(diary.wish_patch >= 0, "a wish place with the soil's dots all used")
	t.check(u.dot_count() <= keep, "within the budget")


func test_an_old_save_with_a_day_wish_loads_and_gets_a_place() -> void:
	var json := JSON.new()
	t.check_eq(json.parse(FileAccess.get_file_as_string(OLD_SAVE)), OK, "the 0.8.1 save reads")
	var d: Dictionary = (json.data as Dictionary)["game"]
	# As it was saved on a day-wish morning before 0.8.2.5.
	d["diary"]["wish"] = "Today, look at the tree from every side before the sun sets."
	d["diary"]["wish_patch"] = -1
	(d["diary"]["entries"] as Array).append({"day": 9, "text": "Wish: see the tree from every side.", "by": "tree"})
	d.erase("drank")
	var g := GameState.from_dict(d)
	t.check(not g.drank, "no 'drank' flag in an old save")
	var page := g.diary.page(9)
	var shown := false
	for e in page:
		shown = shown or str(e["text"]) == "Wish: see the tree from every side."
	t.check(shown, "the old day wish line still shows on its page")
	t.check_eq(Compass.wish_way(g, Vector3.ZERO), Vector3.ZERO, "no place: the needle rests")
	g.diary.new_wish(g.ground, g.day_number() + 1, g.seed, g.roots, g.sim.resources)
	t.check(g.diary.wish_patch >= 0, "the next morning has a place")
	t.check(Compass.wish_way(g, Vector3.ZERO) != Vector3.ZERO, "and the needle a way")
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(h.diary.wish_patch, g.diary.wish_patch, "it survives a save")


## Broken item 46: a wish plant not clearly larger than the meadow signs, or no plant for a far
## wish at the clearing's edge.
func test_the_wish_plant_is_large_in_flower_and_at_the_edge_when_far() -> void:
	var u := Underground.new(14)
	var edge := 18.0
	var tall := {Resources.Kind.WATER: 0.9, Resources.Kind.NITROGEN: 0.25, Resources.Kind.PHOSPHORUS: 1.3, Resources.Kind.POTASSIUM: 1.0}
	for kind in range(4):
		var pid := u.add_wish_deposit(kind + 1, kind, Vector3(5.0 + kind, -1.0, -4.0), 1.2, 30)
		var hint_r := 0.0
		for h in u.surface_hints(edge):
			if (h["position"] as Vector3).distance_to(Vector3(5.0 + kind, 0, -4.0)) < 0.01 and str(h["kind"]) != "damp":
				hint_r = maxf(hint_r, float(h["radius"]))
		var wp := WishPlant.new()
		t.root.add_child(wp)
		wp.show_wish(u, pid, edge)
		t.check(wp.visible and wp.stand_radius >= hint_r * 1.5, "kind %d: the stand %.2f m against the sign's %.2f m" % [kind, wp.stand_radius, hint_r])
		var box := _plant_box(wp)
		t.check(box.size.y >= float(tall[kind]), "kind %d: tall plants (%.2f m)" % [kind, box.size.y])
		t.check(_has_flowers(wp, kind), "kind %d: in flower" % kind)
		t.check_eq(wp._butterflies.size(), WishPlant.BUTTERFLIES, "butterflies over it")
		wp.free()
	# A far wish beyond the clearing: at its edge, in its direction, not made smaller.
	var far := u.add_wish_deposit(9, Resources.Kind.NITROGEN, Vector3(20.0, -1.0, 15.0), 1.4, 30, true)
	var at := WishPlant.place_of(u, far, edge)
	var pos: Vector3 = at["position"]
	t.check(bool(at["at_edge"]), "a far wish shows at the clearing's edge")
	t.check_near(Vector2(pos.x, pos.z).length(), edge - Underground.EDGE_INSET, 0.01, "just inside the edge")
	t.check(Vector2(pos.x, pos.z).normalized().dot(Vector2(20, 15).normalized()) > 0.999, "in its direction")
	t.check_near(float(at["radius"]), 1.4 * WishPlant.SIZE, 1e-4, "as large as a near one")
	var wp := WishPlant.new()
	t.root.add_child(wp)
	wp.show_wish(u, far, edge)
	t.check(wp.visible and Vector2(wp.place.x, wp.place.z).length() < edge, "its plant grows in the clearing")
	# No wish: nothing.
	wp.show_wish(u, -1, edge)
	t.check(not wp.visible, "no wish, no plant")
	wp.free()


func _plant_box(wp: WishPlant) -> AABB:
	var box := AABB()
	for c in wp.get_children():
		if c is MeshInstance3D and _is_stand(c, wp):
			box = (c as MeshInstance3D).mesh.get_aabb()
	return box


## The stand's mesh (0.8.2.6: its own material, whose flowers open at the morning moment).
func _is_stand(c: Node, wp: WishPlant) -> bool:
	var m := (c as MeshInstance3D).material_override
	return m == Meadow._plant_material() or (m != null and m == wp._stand_mat)


## Flower colours among the plant's vertex colours: brown rush tufts, pink or white clover heads,
## green-brown nettle tassels, violet comfrey bells.
func _has_flowers(wp: WishPlant, kind: int) -> bool:
	for c in wp.get_children():
		if not (c is MeshInstance3D) or not _is_stand(c, wp):
			continue
		var arrays := (c as MeshInstance3D).mesh.surface_get_arrays(0)
		for col: Color in arrays[Mesh.ARRAY_COLOR]:
			match kind:
				Resources.Kind.WATER:
					if col.r > 0.4 and col.r > col.g * 1.2:
						return true
				Resources.Kind.NITROGEN:
					if col.r > 0.8:
						return true
				Resources.Kind.PHOSPHORUS:
					if col.r > 0.28 and absf(col.r - col.g) < 0.03:
						return true
				_:
					if col.b > col.g * 1.5:
						return true
	return false


func test_an_ink_ring_is_drawn_round_it_at_sunset() -> void:
	var u := Underground.new(14)
	var pid := u.add_wish_deposit(2, Resources.Kind.WATER, Vector3(4, -1, 3), 1.2, 30)
	var wp := WishPlant.new()
	t.root.add_child(wp)
	wp.show_wish(u, pid, 18.0)
	t.check_eq(wp.ring_state(), Vector2.ZERO, "no ring by day")
	wp.ring()
	wp._process(0.1)
	var s := wp.ring_state()
	t.check(s.x > 0.0 and s.x < 1.0 and s.y > 0.9, "drawn round, dark (%s)" % str(s))
	for _i in range(10):
		wp._process(0.1)
	t.check_near(wp.ring_state().x, 1.0, 1e-4, "a whole ring after a moment")
	t.check(wp._ring.visible, "and seen")
	for _i in range(30):
		wp._process(0.1)
	t.check_eq(wp.ring_state(), Vector2.ZERO, "gone after a few seconds")
	t.check(not wp._ring.visible, "hidden again")
	wp.free()


## Broken item 47: the needle not pointing to the wish place from any camera angle, or north.
func test_the_needle_points_to_the_wish_from_every_angle() -> void:
	var cam := Camera3D.new()
	cam.fov = 60.0
	t.root.add_child(cam)
	var wish := Vector3(6.0, 0.0, 3.0)
	for k in range(8):
		var yaw := TAU * k / 8.0
		cam.global_position = Vector3(sin(yaw) * 9.0, 7.0, cos(yaw) * 9.0)
		cam.look_at(Vector3(0, 0.5, 0))
		var a := Compass.needle_angle_for(cam.global_basis, wish)
		# The way on the screen from the trunk to the wish place.
		var s := cam.unproject_position(wish) - cam.unproject_position(Vector3.ZERO)
		var screen := atan2(s.x, -s.y)
		t.check(absf(wrapf(a - screen, -PI, PI)) < 0.5, "yaw %d: needle %.2f, on screen %.2f" % [k, a, screen])
		var north := Compass.north_angle_for(cam.global_basis)
		t.check(absf(wrapf(a - north, -PI, PI)) > 1.0, "not north")
	cam.free()


func test_the_compass_follows_the_wish_smoothly_and_rests_without_one() -> void:
	var g := GameState.new_game(14)
	g.diary.new_wish(g.ground, 2, 14, g.roots, g.sim.resources)
	var cam := Camera3D.new()
	t.root.add_child(cam)
	cam.global_position = Vector3(0, 6, 9)
	cam.look_at(Vector3(0, 0.5, 0))
	var c := Compass.new()
	c.camera = cam
	c.target = func() -> Vector3: return Compass.wish_way(g, Vector3.ZERO)
	t.root.add_child(c)
	var way := Compass.wish_way(g, Vector3.ZERO)
	t.check(way != Vector3.ZERO, "a way to today's wish")
	for _i in range(240):
		c._process(1.0 / 60.0)
	t.check(absf(wrapf(c.needle_angle() - Compass.needle_angle_for(cam.global_basis, way), -PI, PI)) < 0.05, "settled on the wish")
	# The camera turns half round: the needle swings over, never in one jump.
	cam.global_position = Vector3(0, 6, -9)
	cam.look_at(Vector3(0, 0.5, 0))
	var biggest := 0.0
	var last := c.needle_angle()
	for _i in range(300):
		c._process(1.0 / 60.0)
		biggest = maxf(biggest, absf(wrapf(c.needle_angle() - last, -PI, PI)))
		last = c.needle_angle()
	t.check(biggest < 0.25, "smooth: at most %.2f rad a frame" % biggest)
	t.check(absf(wrapf(c.needle_angle() - Compass.needle_angle_for(cam.global_basis, way), -PI, PI)) < 0.05, "on the wish again")
	# Reached: the needle drifts a little and rests.
	g.diary.wish_reached = true
	var before := c.needle_angle()
	for _i in range(600):
		c._process(1.0 / 60.0)
	var rest := c.needle_angle()
	t.check(absf(wrapf(rest - before, -PI, PI)) > 0.1, "it drifts on")
	c._process(1.0 / 60.0)
	t.check(absf(c.needle_angle() - rest) < 0.01, "and rests")
	c.free()
	cam.free()


func test_at_night_the_needle_shows_the_way_from_the_newest_tip() -> void:
	var g := GameState.new_game(14)
	g.diary.new_wish(g.ground, 2, 14, g.roots, g.sim.resources)
	var tip := g.roots.graph.add_node(0, Vector3(2.0, -1.0, 1.0))
	g.roots.graph.set_flag(tip, "main", 0)
	g.roots.main_root_count = 1
	var rv := RootView.new()
	var holder := Node.new()
	t.root.add_child(holder)
	holder.add_child(rv)
	rv.setup(g.ground, g.roots, g.sim.resources)
	rv.set_wish_glows(g.wish_glows())
	var c: Vector3 = g.ground.patches[g.diary.wish_patch]["center"]
	var want := Vector3(c.x - 2.0, 0.0, c.z - 1.0).normalized()
	t.check(rv.wish_way().normalized().dot(want) > 0.999, "from the newest tip, not the trunk")
	t.check(rv.compass.target.call() == rv.wish_way(), "the night compass uses it")
	holder.free()


## Broken item 48 (the lever; the months are tools/strategies.gd): the floor per species.
func test_the_liebig_floor_is_the_species_own() -> void:
	for sid in Species.ORDER:
		var g := GameState.new_game(3, sid)
		g.sim.clock.day_count = GrowthSim.SEEDLING_DAYS + 1
		var want := 0.55 if sid == "oak" else 0.45
		t.check_near(g.sim.growth_floor(), want, 1e-6, "%s: floor %.2f" % [sid, want])
	var s := GameState.new_game(3)
	s.sim.clock.day_count = 1
	t.check_near(s.sim.growth_floor(), GrowthSim.SEEDLING_FLOOR, 1e-6, "the seedling still lives off its seed")
	s.sim.clock.day_count = 9
	s.sim.liebig_floor = 0.5
	t.check_near(s.sim.growth_floor(), 0.5, 1e-6, "tools can still set one")


func test_the_roots_drank_well_after_a_night_at_a_deposit() -> void:
	var g := GameState.new_game(14)
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	g.start_run(0)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, FRAME) and guard < 20000:
		guard += 1
	var day := g.day_number()
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	var lines := g.diary.lines_for_day(day + 1).filter(func(e: Dictionary) -> bool: return str(e.get("topic", "")) == "drank")
	# The first morning is the seed's (no line then); the next night counts.
	t.check(lines.is_empty(), "not on the seed's first morning")
	while g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.dive()
	var from := Diary.newest_tip(g.roots)
	var aim := RootBot.new()
	var glows := g.wish_glows()
	if not glows.is_empty():
		aim.goal = glows[0]["center"]
		aim.goal_radius = float(glows[0]["radius"]) * 0.5
	g.start_run(from)
	guard = 0
	while g.steer(aim.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME) and guard < 20000:
		guard += 1
	t.check(g.drank, "steered to the wish: it drank")
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check(h.drank, "kept over a save")
	day = g.day_number()
	while g.phase != GameState.Phase.DAY:
		g.tick(0.25)
	var page := g.diary.page(day + 1)
	var texts: Array = page.map(func(e: Dictionary) -> String: return str(e["text"]))
	t.check(page.size() <= Diary.PAGE_MAX, "three lines at most")
	# 0.8.2.6: the line names the best thing reached (a wish here) instead of "drank well".
	t.check(texts.has(Diary.morning_line("wish")) or page.size() == Diary.PAGE_MAX, "the morning says so: %s" % str(texts))
	t.check(not g.drank, "once")
	# A night that reached nothing: no line.
	var q := GameState.new_game(14)
	q.drank = false
	q.diary.entries.clear()
	q._sunrise()
	t.check(q.diary.entries.filter(func(e: Dictionary) -> bool: return str(e.get("topic", "")) == "drank").is_empty(), "no line without a deposit reached")
