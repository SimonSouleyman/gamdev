extends RefCounted
## 0.8 section 5: the fixes from the 0.7 balance check (docs/notes/balance-0.8.md). The soil's
## new layout, the wish that points at what the tree lacks most, one glow at a time, the night's
## finds filling only the tree's room, and a month of meadow play checked against B2 to B4.
var t

const FRAME: float = 1.0 / 30.0


## B2 and B4: fewer, larger rich patches set apart, phosphorus as plentiful as nitrogen, and
## nettles over it on the meadow. An old save keeps its old soil, dot for dot. (Layout 2, the 0.8
## soil, which 0.8 saves keep; 0.8.1's wider field is tests/test_field.gd.)
func test_the_new_soil_has_fewer_larger_patches() -> void:
	for seed in [3, 14, 27]:
		var u := Underground.new(seed, 2)
		var old := Underground.new(seed, 1)
		var rich := {0: [], 1: [], 2: [], 3: []}
		var old_rich := 0
		for p in u.patches:
			var c: Vector3 = p["center"]
			if -c.y <= Underground.TOPSOIL and Vector2(c.x, c.z).length() >= 2.0:
				rich[int(p["kind"])].append(p)
		for p in old.patches:
			var c: Vector3 = p["center"]
			if -c.y <= Underground.TOPSOIL and Vector2(c.x, c.z).length() >= 2.0:
				old_rich += 1
		var n: int = rich[0].size() + rich[1].size() + rich[2].size() + rich[3].size()
		t.check(n <= 8 and n < old_rich / 2, "seed %d: at most 8 rich topsoil patches (%d, was %d)" % [seed, n, old_rich])
		var size := 0.0
		for k in rich:
			for p in rich[k]:
				size += int(p["end"]) - int(p["first"])
		t.check(size / n >= 38.0, "seed %d: each one larger (%.0f dots on average, was about 25)" % [seed, size / n])
		var amount := [0.0, 0.0, 0.0, 0.0]
		for i in range(u.patches.size()):
			var c: Vector3 = u.patches[i]["center"]
			if -c.y <= Underground.TOPSOIL:
				amount[int(u.patches[i]["kind"])] += u.patch_amount(i, true)
		# Layout 1 held about 0.45 as much phosphorus as nitrogen.
		t.check(amount[2] >= 0.6 * amount[1], "seed %d: phosphorus far closer to nitrogen (%.0f against %.0f)" % [seed, amount[2], amount[1]])
		# Set apart: every pair of rich patches at least most of patch_gap from each other.
		var all: Array = rich[0] + rich[1] + rich[2]
		var close := 0
		for a in range(all.size()):
			for b in range(a + 1, all.size()):
				var ca: Vector3 = all[a]["center"]
				var cb: Vector3 = all[b]["center"]
				if Vector2(ca.x - cb.x, ca.z - cb.z).length() < Underground.patch_gap * 0.75:
					close += 1
		t.check(close == 0, "seed %d: the rich patches lie apart (%d pairs too close)" % [seed, close])
		var nettles := u.surface_hints().filter(func(h: Dictionary) -> bool: return str(h["kind"]) == "nettles")
		t.check_eq(nettles.size(), rich[2].size(), "seed %d: nettles over every phosphorus patch" % seed)
		# An old save (no "layout") loads its old soil with the same dots.
		var d := old.to_dict()
		d.erase("layout")
		var back := Underground.from_dict(d)
		t.check_eq(back.layout, 1, "an old save keeps layout 1")
		t.check_eq(back.dot_count(), old.dot_count(), "with the same dots")
		var again := Underground.from_dict(JSON.parse_string(JSON.stringify(u.to_dict())))
		t.check_eq(again.dot_count(), u.dot_count(), "a new save loads its new soil")


## Item 2: the wish points at the kind the tree lacks most, phosphorus and potassium included,
## with its own meadow sign, words and drawing.
func test_the_wish_points_at_what_the_tree_lacks_most() -> void:
	var cases := {
		Resources.Kind.WATER: PackedFloat32Array([0.5, 30, 30, 30]),
		Resources.Kind.NITROGEN: PackedFloat32Array([30, 0.5, 30, 30]),
		Resources.Kind.PHOSPHORUS: PackedFloat32Array([30, 30, 0.5, 30]),
		Resources.Kind.POTASSIUM: PackedFloat32Array([30, 30, 30, 0.5]),
	}
	var words := ["rushes", "clover", "nettles", "comfrey"]
	for kind in cases:
		var u := Underground.new(8)
		var roots := RootSystem.new(8)
		var res := Resources.new()
		res.stock = cases[kind]
		var diary := Diary.new()
		var placed := 0
		for day in range(1, 12):
			diary.new_wish(u, day, 8, roots, res)
			if diary.wish_patch < 0:
				continue
			placed += 1
			var p: Dictionary = u.patches[diary.wish_patch]
			t.check_eq(int(p["kind"]), kind, "short of %s: the wish deposit holds %s" % [Resources.KIND_NAMES[kind], Resources.KIND_NAMES[kind]])
			t.check(diary.wish.contains(words[kind]), "the wish names the %s: %s" % [words[kind], diary.wish])
			t.check_eq(Diary.drawing_for(u, diary.wish_patch), Diary.DRAWINGS[kind], "its drawing")
			var c: Vector3 = p["center"]
			var hinted := u.surface_hints().any(func(h: Dictionary) -> bool: return str(h["kind"]) == Diary.DRAWINGS[kind] and (h["position"] as Vector3).distance_to(Vector3(c.x, 0, c.z)) < 0.01)
			t.check(hinted, "the meadow shows %s over it" % Diary.DRAWINGS[kind])
		t.check(placed >= 3, "%s: wishes underground (%d)" % [Resources.KIND_NAMES[kind], placed])
	# A kind the species does not need is never wished for: alder needs only a little nitrogen,
	# but with none at all it still lacks nitrogen most; with plenty it lacks something else.
	var alder := RootSystem.new(4)
	alder.species = Species.alder()
	var rich := Resources.new()
	rich.stock = PackedFloat32Array([30, 30, 2, 30])
	t.check_eq(Diary.wish_kind(alder, rich, 0), Resources.Kind.PHOSPHORUS, "alder short of phosphorus: phosphorus")


## Item 4 and B4: missed wish deposits do not pile up. Mornings with the roots never reaching
## any: at most MISSED_MAX untouched ones wait in the soil, and a wish may point at one again.
func test_missed_wishes_do_not_pile_up() -> void:
	for seed in [3, 14, 27]:
		var u := Underground.new(seed)
		var roots := RootSystem.new(seed)
		var diary := Diary.new()
		var most := 0
		var again := 0
		for day in range(1, 31):
			var before := u.wish_deposits.size()
			diary.new_wish(u, day, seed, roots)
			if diary.wish_patch >= 0 and u.wish_deposits.size() == before:
				again += 1
			most = maxi(most, Diary.untouched_wishes(u, roots))
			t.check(diary.glows(u).size() <= 1, "seed %d day %d: one glow at a time" % [seed, day])
		t.check(most <= Diary.MISSED_MAX, "seed %d: at most %d missed deposits wait (%d)" % [seed, Diary.MISSED_MAX, most])
		t.check(again > 0, "seed %d: a wish points at a missed deposit again (%d times)" % [seed, again])


## B3: the night's new root fills the stock only up to the tree's room (find_hold_days of a calm
## day's need); a deposit it reaches while full is tapped all the same, for the old roots.
func test_the_night_fills_only_the_trees_room() -> void:
	var g := GameState.new_game(14)
	for _d in range(5):
		_night(g)
		_day(g)
	g.skip_time(1.0)
	var want := g.sim.day_capacity() * g.sim.node_cost()
	# Water and nitrogen full, phosphorus and potassium empty.
	g.sim.resources.stock = PackedFloat32Array([
		want * g.sim.species.needs[0] * 2.5, want * g.sim.species.needs[1] * 2.5, 0.0, 0.0])
	g.sim.resources.life_force = 80.0
	g.dive()
	var room := g.roots.run_room.duplicate()
	t.check(float(room[0]) <= 0.0 and float(room[1]) <= 0.0, "no room for water or nitrogen tonight")
	t.check(float(room[2]) > 0.0, "room for phosphorus")
	var tapped_before := g.roots.tapped.size()
	var stock_before := g.sim.resources.stock.duplicate()
	g.start_run(g.roots.graph.size() - 1)
	var bot := RootBot.new()
	while g.steer(bot.stick_for(g.roots, g.ground), false, FRAME):
		pass
	var st := g.sim.resources.stock
	t.check(st[0] <= stock_before[0] + 1e-3 and st[1] <= stock_before[1] + 1e-3, "the full kinds did not rise (%.1f, %.1f)" % [st[0] - stock_before[0], st[1] - stock_before[1]])
	t.check(st[2] <= float(room[2]) + 1e-3, "phosphorus rose at most to its room")
	t.check(g.roots.tapped.size() > tapped_before, "the deposits the root reached are tapped")
	var full_tapped := 0
	for i in g.roots.tapped:
		if g.ground.dot_kinds[i] <= Resources.Kind.NITROGEN and g.ground.fullness(i) > 0.99:
			full_tapped += 1
	t.check(full_tapped > 0, "some tapped while full, all still in the soil (%d)" % full_tapped)


## B2 to B4 in one month of meadow play (the 0.7 check's probe, tools/qa_nutri.gd): linden
## seed 3, the root bot starting by need. Days at the soft floor at most a quarter, no kind over
## two days of stock on more than a third of the days, and the choice between deposits real.
## On 0.8's soil (layout 2), which 0.8 saves keep; the wider field's month is in test_field.gd.
func test_a_month_of_meadow_play_is_balanced() -> void:
	Underground.game_layout = 2
	var g := GameState.new_game(3, "linden")
	Underground.game_layout = Underground.LAYOUT
	var needs := g.sim.species.needs
	var days := 0
	var floor_days := 0
	var over := PackedInt32Array([0, 0, 0, 0])
	var most_patches := 0
	for day in range(40):
		g.dive()
		most_patches = maxi(most_patches, _fresh_patches(g))
		if g.can_start_run():
			var starter := RootBot.new()
			g.start_run(starter.pick_start(g.roots, g.ground, g.sim.resources))
			var bot := RootBot.new()
			while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
				pass
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		var light := 0.0
		var at_floor := 0.0
		while g.phase == GameState.Phase.DAY:
			var w := maxf(g.sim.clock.sun_height(), 0.0)
			light += w
			if Resources.growth_factor(g.sim.resources.stock, needs, g.sim.growth_floor()) <= g.sim.growth_floor() + 0.05:
				at_floor += w
			g.tick(0.5)
		if g.finished:
			break
		days += 1
		if at_floor >= 0.5 * light:
			floor_days += 1
		var want := g.sim.day_capacity() * g.sim.node_cost()
		for k in range(4):
			if g.sim.resources.stock[k] > 2.0 * want * needs[k]:
				over[k] += 1
	t.check(g.finished and days >= 25 and days <= 34, "finished in about a month (%d days)" % days)
	t.check(floor_days <= days / 4, "B2: at the soft floor on at most a quarter of the days (%d of %d)" % [floor_days, days])
	for k in range(4):
		t.check(over[k] <= days / 3, "B3: %s over two days on at most a third of the days (%d of %d)" % [Resources.KIND_NAMES[k], over[k], days])
	t.check(most_patches <= 15, "B4: at most about 15 deposits in reach (%d)" % most_patches)


## Rich patches (a quarter or more of their dots fresh) within a calm tank's straight reach.
func _fresh_patches(g: GameState) -> int:
	var n := 0
	var calm := g.roots.calm_life_force
	for pid in range(g.ground.patches.size()):
		var dots := g.ground.patch_dots(pid)
		var fresh := 0
		var near := INF
		for i in dots:
			if g.roots.is_fresh(i) and g.ground.dot_collected[i] == 0 and g.ground.fullness(i) >= Care.DEPOSIT_MIN:
				fresh += 1
		if fresh < 0.25 * dots.size():
			continue
		var c: Vector3 = g.ground.patches[pid]["center"]
		for s in Diary.nearest_starts(g.roots, c, 3):
			near = minf(near, s.distance_to(c))
		if near * g.roots.cost_per_metre(c) <= calm:
			n += 1
	return n


func _night(g: GameState) -> void:
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, FRAME) and guard < 20000:
		guard += 1
	while g.phase != GameState.Phase.DAY:
		g.tick(0.5)


func _day(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(1.0)
