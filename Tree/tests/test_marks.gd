extends RefCounted
## 0.7: branches the tree marks for pruning (docs/notes/marks-0.7.md). A shaded twig the seeded
## shade dieback will take shows it a day or two ahead: thin, dull leaves and greying bark.
## Broken list items 6, 7 and 9 (docs/specs/0.7-candidates.md) each have a test here.
var t


## A night with the root bot; `before_dawn` runs once the root is done, before the sunrise.
func _night(g: GameState, before_dawn: Callable = Callable()) -> void:
	g.dive()
	g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
	var bot := RootBot.new()
	var guard := 0
	while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
		guard += 1
	if before_dawn.is_valid():
		before_dawn.call()
	while g.phase == GameState.Phase.NIGHT:
		g.tick(0.25)


func _day(g: GameState) -> void:
	while g.phase == GameState.Phase.DAY:
		g.tick(1.0)


func _day_to(g: GameState, share: float) -> void:
	while g.phase == GameState.Phase.DAY and g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * share:
		g.tick(0.5)


func _play(days: int, seed: int = 14, species_id: String = "linden") -> GameState:
	var g := GameState.new_game(seed, species_id)
	for _d in range(days):
		_night(g)
		_day(g)
	return g


## A night without a root: the stock is set plentiful before dawn, so the tree's growth never
## depends on how the night went (two copies of one tree stay comparable).
func _fed_night(g: GameState) -> void:
	g.dive()
	if g.can_start_run():
		g.start_run(0)
		g.finish_run_early()
	for k in range(4):
		g.sim.resources.stock[k] = 400.0
	var guard := 0
	while g.phase != GameState.Phase.DAY and guard < 100000:
		guard += 1
		g.tick(0.25)


func _mm() -> MultiMesh:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	return mm


## Broken 6: never more than three marked twigs at once, and each shows at least a day and at
## most two before it dies back (the mark is a true forecast of the seeded dieback).
func test_marks_warn_one_to_two_days_ahead() -> void:
	var g := GameState.new_game(14, "linden")
	var seen := {}  # "since:id" -> true, marks followed to their end
	var ended_by_dieback := 0
	var most := 0
	var shown_days := 0
	for _d in range(24):
		var before: Array = g.sim.marks.duplicate(true)
		var removed := g.sim.removed_nodes
		_night(g)
		var day := g.day_number()
		most = maxi(most, g.sim.marks.size())
		t.check(g.sim.marks.size() <= GrowthSim.MARK_MAX, "day %d: at most %d marks (%d)" % [day, GrowthSim.MARK_MAX, g.sim.marks.size()])
		if g.sim.removed_nodes != removed:
			_day(g)
			continue  # ids moved with the compaction
		for m in before:
			var id := int(m["id"])
			var still := g.sim.marks.any(func(x: Dictionary) -> bool: return int(x["id"]) == id)
			if g.sim.graph.get_flag(id, "shed", false):
				continue  # the crown lifted and shed the branch it grew on
			if g.sim.graph.get_flag(id, "dead", false):
				ended_by_dieback += 1
				var warned := day - int(m["since"])
				t.check(warned >= 1 and warned <= GrowthSim.MARK_WARN_DAYS, "tip %d warned %d days before it died back" % [id, warned])
				t.check_eq(day, int(m["due"]), "tip %d died on the day its mark foretold" % id)
			elif not still:
				# Light reached it (no longer shaded): it lives, and the sign fades.
				t.check(not g.sim.shaded_tips().has(id), "a mark ends early only when light reaches the twig")
		for m in g.sim.marks:
			seen["%d:%d" % [int(m["since"]), int(m["id"])]] = true
			var ahead := int(m["due"]) - day
			t.check(ahead >= 1 and ahead <= GrowthSim.MARK_WARN_DAYS, "due %d day(s) ahead" % ahead)
		_day_to(g, 0.5)
		if not g.sim.tired_nodes().is_empty():
			shown_days += 1
		for m in g.sim.marks:
			t.check(g.sim.mark_strength(m) >= GrowthSim.MARK_FIRST_DAY - 0.01, "a mark shows fully by noon")
		_day(g)
	t.check(seen.size() >= 5, "marks appear on a growing linden (%d)" % seen.size())
	t.check(ended_by_dieback >= 3, "marked twigs died back as foretold (%d)" % ended_by_dieback)
	t.check(shown_days >= 8, "a marked twig shows on many days (%d of 24)" % shown_days)
	t.check(most <= GrowthSim.MARK_MAX, "never more than three")


## The sign eases in over the morning and is stronger on the last day.
func test_mark_strength_grows_towards_the_dieback() -> void:
	var g := _play(12)
	var sim := g.sim
	var day := sim.clock.day_count
	var early := {"id": 5, "due": day + 2, "since": day}
	var last := {"id": 5, "due": day + 1, "since": day - 1}
	# 5 is an inner node, alive; mark_strength only reads the days.
	t.check_near(sim.mark_strength(last), GrowthSim.MARK_LAST_DAY, 0.001, "the day before: full sign")
	t.check_near(sim.mark_strength(early), GrowthSim.MARK_FIRST_DAY, 0.001, "two days ahead: a fainter sign")
	t.check_eq(sim.mark_strength({"id": 5, "due": day, "since": day - 2}), 0.0, "gone once the day has come")


## Broken 7: no marks on beech (it has no shade dieback), and none on the bonsai.
func test_no_marks_on_beech_or_the_bonsai() -> void:
	var g := GameState.new_game(14, "beech")
	var any := 0
	for _d in range(18):
		_night(g)
		any += g.sim.marks.size()
		_day_to(g, 0.5)
		any += g.sim.tired_nodes().size()
		_day(g)
	t.check_eq(any, 0, "beech never marks a twig")
	t.check(g.sim.shaded_tips().size() > 0, "though its crown has shaded twigs (%d)" % g.sim.shaded_tips().size())
	var b := g.ensure_bonsai(true)
	t.check(not ("marks" in b), "the bonsai has no marks")
	for _d in range(4):
		b.follow(g.sim.clock, g.sim.clock.seconds_per_day)
	var withered := 0
	for id in range(b.graph.size()):
		if b.graph.get_flag(id, "withered", false):
			withered += 1
	t.check_eq(withered, 0, "nothing withers on the bonsai")
	var src := FileAccess.get_file_as_string("res://shed/bonsai_view.gd")
	t.check(not src.contains("tired_nodes") and not src.contains("bark_colors"), "the bonsai view draws no marks")


## Broken 9: cutting every marked twig at noon (at its fork, or just the tip) never leaves a tree
## nearer its finish than letting them die back. Two copies of one tree, fed alike every night.
## Growth noise between two copies of one tree that differ only by a cut: 0.5 % of its segments.
const NOISE: float = 0.005


func test_cutting_marked_twigs_never_finishes_sooner() -> void:
	var start := _play(10)
	var calm := GameState.from_dict(JSON.parse_string(JSON.stringify(start.to_dict())))
	var fork := GameState.from_dict(JSON.parse_string(JSON.stringify(start.to_dict())))
	var tip := GameState.from_dict(JSON.parse_string(JSON.stringify(start.to_dict())))
	var cut_fork := 0
	var cut_tip := 0
	for _d in range(10):
		for g: GameState in [calm, fork, tip]:
			_fed_night(g)
			_day_to(g, 0.5)
		cut_fork += _cut_marks(fork.sim, false)
		cut_tip += _cut_marks(tip.sim, true)
		for g: GameState in [calm, fork, tip]:
			_day(g)
	t.check(cut_fork > 0 and cut_tip > 0, "marked twigs were cut (%d, %d segments)" % [cut_fork, cut_tip])
	# A cut reshapes the crown, so the colonizer's seeded growth differs by a few segments either
	# way (0.8: 1197 against 1196); NOISE is far below a tenth of a day's growth (80 to 100).
	var noise := int(calm.sim.grown_nodes() * NOISE)
	t.check(fork.sim.grown_nodes() <= calm.sim.grown_nodes() + noise, "cut at the fork: no nearer the finish (%d vs %d grown)" % [fork.sim.grown_nodes(), calm.sim.grown_nodes()])
	t.check(tip.sim.grown_nodes() <= calm.sim.grown_nodes() + noise, "cut at the tip: no nearer the finish (%d vs %d grown)" % [tip.sim.grown_nodes(), calm.sim.grown_nodes()])


func _cut_marks(sim: GrowthSim, tip_only: bool) -> int:
	var cut := 0
	for m in sim.marks.duplicate():
		if sim.mark_strength(m) <= 0.0:
			continue
		var twig := sim.marked_twig(int(m["id"]))
		if not twig.is_empty():
			cut += sim.prune(twig[0] if tip_only else twig[-1])
	return cut


## A marked tip was dying anyway: cutting only it gives nothing back; cutting the twig at its
## fork gives the usual share of the living wood.
func test_a_dying_tip_gives_nothing_back() -> void:
	var g := _play(14)
	var sim := g.sim
	_day_to(g, 0.5)
	t.check(not sim.marks.is_empty(), "a mark to cut on day 14")
	if sim.marks.is_empty():
		return
	var twig := sim.marked_twig(int(sim.marks[0]["id"]))
	sim.cuts.clear()
	sim.prune(twig[0])
	t.check(sim.cuts.is_empty(), "the dying tip alone: no refund")
	t.check(sim.tired_nodes().is_empty() or not sim.tired_nodes().has(twig[0]), "and its sign is gone")
	if twig.size() >= 2:
		var rest := sim._subtree_size(twig[-1])
		sim.prune(twig[-1])
		t.check_eq(int(sim.cuts[0]["nodes"]), rest, "the rest of the twig counts as a cut")


## The look: the marked twig's leaf mass thins and dulls, its bark greys; a twig that died back
## stays bare; an unmarked crown is exactly as before.
func test_the_sign_is_in_the_crown_and_the_bark() -> void:
	var g := _play(14)
	var sim := g.sim
	_day_to(g, 0.5)
	var marks: Array = sim.marks.duplicate(true)
	t.check(not marks.is_empty(), "marks on day 14")
	var marked := _mm()
	HeroCrown.populate(marked, sim, g.seed)
	var marked_autumn := _mm()
	HeroCrown.populate(marked_autumn, sim, g.seed, PackedFloat32Array(), 1.0)
	var bark := HeroCrown.bark_colors(sim)
	var tired := sim.tired_nodes()
	for id: int in tired:
		t.check(bark[id].a < 1.0 - 0.3, "node %d greys" % id)
	sim.marks = []
	var plain := _mm()
	HeroCrown.populate(plain, sim, g.seed)
	t.check(marked.instance_count < plain.instance_count, "a marked twig's mass is thinner (%d < %d sprays)" % [marked.instance_count, plain.instance_count])
	t.check(marked_autumn.instance_count < marked.instance_count, "in autumn thinner still (%d < %d)" % [marked_autumn.instance_count, marked.instance_count])
	t.check(marked.instance_count > plain.instance_count * 0.85, "the rest of the crown stays full")
	var plain_bark := HeroCrown.bark_colors(sim)
	for id: int in tired:
		t.check(plain_bark[id].a > 0.99 or HeroCrown.segments_to_tip(sim.graph)[id] >= HeroCrown.NO_TIP, "unmarked: no grey")
	# A shaded tip that died back: its twig carries no leaves and greys.
	var died := -1
	for id in range(sim.graph.size()):
		if sim.graph.get_flag(id, "withered", false) and not sim.graph.get_flag(id, "dead", false):
			died = id
			break
	t.check(died >= 0, "some twig died back in the shade by day 14")
	if died >= 0:
		var alive := false
		for c in sim.graph.children[died]:
			alive = alive or not sim.graph.get_flag(c, "dead", false)
		if not alive:
			t.check(not HeroCrown.leafy_nodes(sim).has(died), "the died-back twig is bare")
			t.check(plain_bark[died].a < 0.5 or sim.graph.radii[died] >= HeroCrown.LEAF_RADIUS, "and grey")


## One calm handwritten line on the care page while a twig shows the sign, never a count.
func test_care_page_mentions_a_tired_branch() -> void:
	var g := _play(14)
	_day_to(g, 0.5)
	var text := ""
	for sec in Care.page(g):
		text += str(sec["text"]) + "\n"
	var tired := not g.sim.tired_nodes().is_empty()
	t.check(tired, "a mark on day 14")
	t.check_eq(text.contains(Care.TIRED_LINE), tired, "the line shows while a twig is marked")
	var digits := RegEx.create_from_string("[0-9]")
	t.check(digits.search(Care.TIRED_LINE) == null, "no count in the line")
	g.sim.marks = []
	text = ""
	for sec in Care.page(g):
		text += str(sec["text"]) + "\n"
	t.check(not text.contains(Care.TIRED_LINE), "no line without a mark")


func test_marks_are_saved() -> void:
	var g := _play(12)
	g.sim.marks = [{"id": 40, "due": 14, "since": 12}]
	var back := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(back.sim.marks.size(), 1, "marks saved")
	t.check_eq(int(back.sim.marks[0]["due"]), 14, "with their day")
