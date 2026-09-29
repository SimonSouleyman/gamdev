extends RefCounted
var t

const PATH := "user://test_game.json"


## Plays tonight's run with a gentle curve until it ends.
func _play_night(g: GameState, from_id: int = 0, stick: Vector2 = Vector2(0.3, 0.05)) -> void:
	g.start_run(from_id)
	var guard := 0
	while g.steer(stick, false, 1.0 / 30.0) and guard < 20000:
		guard += 1


## Runs the clock until the phase changes (or a limit).
func _until_phase_changes(g: GameState, step: float = 0.5, limit: float = 2000.0) -> float:
	var start := g.phase
	var waited := 0.0
	while g.phase == start and waited < limit:
		g.tick(step)
		waited += step
	return waited


func test_new_game_starts_with_a_seed_at_sunset() -> void:
	var g := GameState.new_game(4)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "starts at sunset, ready to dive")
	t.check(g.is_seed(), "only a seed")
	t.check_near(g.sim.resources.life_force, GameState.SEED_LIFE_FORCE, 1e-4, "the seed carries life force for one root")
	t.check(g.diary.entries.size() >= 1, "the diary has its first line")


func test_tutorial_seed_then_first_run_then_sapling() -> void:
	# Acceptance: a new game starts with a seed, then one root run, then the sapling appears.
	var g := GameState.new_game(4)
	t.check(g.dive(), "tap the ground dives")
	t.check_eq(g.phase, GameState.Phase.NIGHT, "night: root mode")
	var nodes_before := g.sim.graph.size()
	_play_night(g)
	t.check(g.roots.main_root_count == 1, "the first root is permanent")
	var total := 0.0
	for k in range(4):
		total += g.sim.resources.amount(k)
	t.check(total > 5.0, "the first run drank nutrients (%f)" % total)
	t.check_eq(g.sim.graph.size(), nodes_before, "nothing grows at night")
	var waited := _until_phase_changes(g, 0.1)
	t.check_eq(g.phase, GameState.Phase.DAY, "sunrise after the run")
	t.check(waited < 20.0, "the rest of the night passes quickly (%f s)" % waited)
	t.check_eq(g.day_number(), 1, "day 1")
	for _i in range(120):
		g.tick(0.5)
	t.check(g.sim.graph.size() > nodes_before + 20, "a sapling grew (%d nodes)" % g.sim.graph.size())
	t.check(g.sim.height() > 0.5, "and it stands up (%f m)" % g.sim.height())


func test_dive_only_at_sunset() -> void:
	var g := GameState.new_game(5)
	g.dive()
	_play_night(g)
	_until_phase_changes(g, 0.1)
	t.check_eq(g.phase, GameState.Phase.DAY, "day")
	t.check(not g.dive(), "no dive during the day")
	var waited := _until_phase_changes(g, 1.0)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "the day ends at sunset")
	t.check(waited <= g.sim.clock.seconds_per_day * g.sim.clock.daylight_fraction + 1.0, "within five minutes (%f s)" % waited)
	var time_at_sunset := g.sim.clock.time_of_day
	for _i in range(100):
		g.tick(1.0)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "time holds at sunset until the player dives")
	t.check_near(g.sim.clock.time_of_day, time_at_sunset, 1e-6, "clock held")
	t.check(g.dive(), "dive at sunset")


func test_one_run_per_night() -> void:
	var g := GameState.new_game(6)
	g.dive()
	_play_night(g)
	g.sim.resources.life_force = 50.0
	t.check(not g.start_run(0), "no second run tonight")


func test_night_waits_for_the_run() -> void:
	var g := GameState.new_game(7)
	g.dive()
	g.start_run(0)
	for _i in range(400):
		g.tick(1.0)  # far longer than the night
	t.check_eq(g.phase, GameState.Phase.NIGHT, "the night does not end while the root is growing")


func test_night_without_life_force_is_a_short_visit() -> void:
	var g := GameState.new_game(8)
	g.sim.resources.life_force = 0.0
	g.dive()
	t.check(g.night_empty, "an empty night")
	t.check(not g.start_run(0), "no run without life force")
	var line: String = g.diary.entries[-1]["text"]
	t.check(line.contains("no life force"), "the diary says so: " + line)
	var waited := _until_phase_changes(g, 0.1)
	t.check_eq(g.phase, GameState.Phase.DAY, "morning comes")
	t.check(waited < GameState.EMPTY_NIGHT_VISIT + 15.0, "after a short visit (%f s)" % waited)


func test_dawn_burst_front_loads_growth_and_never_costs_growth() -> void:
	# Acceptance: part of the night's growth is released in the first ten seconds after sunrise.
	var a := _morning_after_first_night(9)
	var b := _morning_after_first_night(9)
	b.sim.dawn_burst_share = 0.0  # same morning without the burst
	b.sim.start_dawn_burst()
	var start := a.sim.graph.size()
	for _i in range(20):
		a.tick(0.5)
		b.tick(0.5)
	t.check(a.sim.graph.size() - start > (b.sim.graph.size() - start) + 5, "burst grows more in the first ten seconds (%d vs %d)" % [a.sim.graph.size() - start, b.sim.graph.size() - start])
	for _i in range(1200):
		if a.phase == GameState.Phase.DAY:
			a.tick(0.5)
		if b.phase == GameState.Phase.DAY:
			b.tick(0.5)
	# The calm pace is capped (QA round 2), so the burst is a real head start, never a loss.
	t.check(a.sim.graph.size() >= b.sim.graph.size() - 8, "the daily total is at least as big (%d vs %d)" % [a.sim.graph.size(), b.sim.graph.size()])


func _morning_after_first_night(seed: int) -> GameState:
	var g := GameState.new_game(seed)
	g.dive()
	_play_night(g, 0, Vector2(0.3, 0.05))
	_until_phase_changes(g, 0.1)
	return g


func test_drag_the_sun_once_nutrients_are_spent() -> void:
	var g := _morning_after_first_night(10)
	t.check(g.can_skip_time(), "the sun can be moved on at any time of the day")
	while g.sim.dawn_burst_active():
		g.tick(0.5)
	var before_skip := g.sim.graph.size()
	var t0 := g.sim.clock.time_of_day
	g.skip_time(0.1)
	t.check(g.sim.clock.time_of_day > t0 + 0.09, "skipping moves the sun")
	t.check_eq(g.sim.graph.size(), before_skip, "the tree rests while the sun is moved on")
	t.check(not g.sim.nutrients_spent(), "so the nutrients wait for the hour the player picked")
	g.sim.clock.boost_active = true
	for _i in range(20):
		g.tick(0.5)
	t.check(g.sim.graph.size() > before_skip, "and boosting later grows the tree")
	g.sim.clock.boost_active = false
	for k in range(4):
		g.sim.resources.stock[k] = 0.0
	t.check(g.day_is_spent(), "the day counts as spent once nutrients run out")
	var before := g.sim.clock.time_of_day
	var life_before := g.sim.resources.life_force
	g.skip_time(0.1)
	t.check(g.sim.clock.time_of_day > before + 0.09, "time moved on")
	t.check(g.sim.resources.life_force > life_before, "life force kept accruing")
	g.skip_time(5.0)
	t.check_eq(g.phase, GameState.Phase.SUNSET, "never past sunset")
	# Night length is unchanged: the night clock runs at its normal pace.
	g.sim.resources.life_force = 50.0
	g.dive()
	var night_before := g.sim.clock.time_of_day
	g.tick(10.0)
	t.check_near(g.sim.clock.time_of_day - night_before, 10.0 / g.sim.clock.seconds_per_day, 1e-4, "night runs at its normal pace")


func test_both_worlds_persist_across_save_and_switching() -> void:
	var g := GameState.new_game(11)
	g.dive()
	_play_night(g)
	_until_phase_changes(g, 0.1)
	for _i in range(40):
		g.tick(0.5)
	t.check_eq(SaveData.save_game(g, PATH), OK, "saved")
	var now := Time.get_unix_time_from_system()
	var h := SaveData.load_game(PATH, now)
	t.check(h != null, "loaded")
	if h == null:
		return
	t.check_eq(h.phase, g.phase, "phase kept")
	t.check_eq(h.sim.graph.size(), g.sim.graph.size(), "tree kept")
	t.check_eq(h.roots.graph.size(), g.roots.graph.size(), "roots kept")
	t.check_eq(h.ground.remaining_dots(), g.ground.remaining_dots(), "collected dots stay collected")
	t.check_eq(h.diary.entries.size(), g.diary.entries.size(), "diary kept")
	t.check_near(h.sim.clock.time_of_day, g.sim.clock.time_of_day, 1e-5, "clock kept")
	var later := SaveData.load_game(PATH, now + 86400.0)
	t.check_near(later.sim.clock.time_of_day, g.sim.clock.time_of_day, 1e-5, "the clock does not run while closed")
	t.check(later.sim.resources.life_force > g.sim.resources.life_force, "life force accrued while away")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(PATH))


func test_journal_pages_show_once() -> void:
	var g := GameState.new_game(12)
	t.check(g.first_time("planted"), "first time")
	t.check(not g.first_time("planted"), "then never again")


func test_daily_wish_is_written_each_morning() -> void:
	var g := _morning_after_first_night(13)
	t.check(g.diary.wish.begins_with("Today"), "a wish: " + g.diary.wish)
	var again := Diary.make_wish(g.ground, g.day_number(), g.seed)
	t.check_eq(again, g.diary.wish, "deterministic from the seed")


func test_thirty_days_each_show_growth() -> void:
	# Pass/fail for Prototype 1: thirty consecutive days each show growth you can point at.
	# A simple bot: calm days, one run per night chasing the nearest glow, starting at the newest root tip.
	var g := GameState.new_game(14)
	var no_growth_days: Array = []
	for day in range(30):
		g.dive()
		var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
		g.start_run(start)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		_until_phase_changes(g, 0.2, 400.0)
		var before := g.sim.graph.size()
		_until_phase_changes(g, 1.0)
		if g.sim.graph.size() - before < 5:
			no_growth_days.append(day + 1)
	t.check(no_growth_days.is_empty(), "every day grew at least five segments (flat days: %s)" % str(no_growth_days))
	t.check(g.sim.graph.size() <= Budgets.TREE_MAX_NODES, "tree budget holds (%d)" % g.sim.graph.size())


func test_a_night_after_the_last_possible_root_still_ends() -> void:
	var g := GameState.new_game(15)
	g.roots.main_root_count = Budgets.MAX_MAIN_ROOTS
	g.sim.resources.life_force = 50.0
	g.dive()
	t.check(g.night_empty, "no root possible: a quiet night")
	var waited := _until_phase_changes(g, 0.1)
	t.check_eq(g.phase, GameState.Phase.DAY, "and morning comes (%f s)" % waited)


func test_rng_state_survives_a_save_exactly() -> void:
	var g := GameState.new_game(16)
	g.sim.rng.randi()
	g.roots.rng.randi()
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(h.sim.rng.state, g.sim.rng.state, "tree rng state kept to the last bit")
	t.check_eq(h.roots.rng.state, g.roots.rng.state, "roots rng state kept to the last bit")


func test_a_full_tree_counts_as_spent() -> void:
	var g := _morning_after_first_night(17)
	g.sim.graph.max_nodes = g.sim.graph.size()
	t.check(g.day_is_spent(), "a tree at its node budget counts as spent")


func test_morning_survives_a_save_right_after_sunrise() -> void:
	var g := _morning_after_first_night(18)
	g.tick(3.0)
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	h.take_events()
	var got_morning := false
	for _i in range(40):
		h.tick(0.5)
		if h.take_events().has("morning"):
			got_morning = true
	t.check(got_morning, "the morning (sapling page, diary line) still comes after a load")


func test_an_unsteered_first_night_still_brings_all_four_nutrients() -> void:
	# QA: a first root that the player barely steered could miss phosphorus, and day 1 stalled.
	var missing: Array = []
	var weak_days: Array = []
	for seed in range(30, 40):
		var g := GameState.new_game(seed)
		g.dive()
		_play_night(g, 0, Vector2.ZERO)
		for k in range(4):
			if g.sim.resources.amount(k) < 1.0:
				missing.append("seed %d kind %d" % [seed, k])
		_until_phase_changes(g, 0.1)
		var before := g.sim.graph.size()
		_until_phase_changes(g, 1.0)
		if g.sim.graph.size() - before < 20:
			weak_days.append(seed)
	t.check(missing.is_empty(), "the starter patch feeds every kind: missing %s" % str(missing))
	t.check(weak_days.is_empty(), "day 1 grows a real sapling (weak seeds %s)" % str(weak_days))


func test_a_tap_boosts_one_game_hour_and_time_runs_on() -> void:
	# Simon, play test 3: tap to boost for about an hour; the clock never stops.
	var g := _morning_after_first_night(19)
	while g.sim.dawn_burst_active():
		g.tick(0.5)
	var t0 := g.sim.clock.time_of_day
	g.boost_hour()
	t.check(g.sim.clock.boost_active, "a tap boosts")
	var hour := g.sim.clock.hour_seconds()
	var waited := 0.0
	while g.sim.clock.boost_active and waited < hour * 3.0:
		g.tick(0.25)
		waited += 0.25
	t.check_near(waited, hour, 0.3, "for one game hour")
	t.check(g.sim.clock.time_of_day > t0 + 0.9 * hour / g.sim.clock.seconds_per_day, "while the clock ran on")
	g.boost_hour()
	g.boost_hour()
	g.boost_hour()
	g.boost_hour()
	t.check(g.sim.clock.boost_remaining <= hour * 3.0 + 1e-3, "at most three hours ahead")


func test_a_boost_ends_with_the_day_and_survives_a_save() -> void:
	# QA round 1: a late tap must not carry into the next morning, and a save keeps the boost.
	var g := _morning_after_first_night(23)
	while g.sim.dawn_burst_active():
		g.tick(0.5)
	g.boost_hour()
	var back := DayCycle.from_dict(g.sim.clock.to_dict())
	t.check_near(back.boost_remaining, g.sim.clock.boost_remaining, 1e-3, "boost time is saved")
	t.check(back.boost_active, "and still boosting after a load")
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction - 0.001
	g.boost_hour()
	var guard := 0
	while g.phase == GameState.Phase.DAY and guard < 100:
		g.tick(0.25)
		guard += 1
	t.check(g.phase != GameState.Phase.DAY, "the sun set")
	t.check_near(g.sim.clock.boost_remaining, 0.0, 1e-6, "and the boost ended with it")


func test_visitors_come_once_as_the_tree_grows() -> void:
	# Game feel: butterflies at the first leaves, a nest once the tree is tall, each once.
	var g := GameState.new_game(31)
	t.check(Visitors.arrive(g).is_empty(), "nobody visits a seed")
	for _day in range(8):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
	var first := Visitors.arrive(g)
	t.check(first.has("butterflies"), "butterflies have come (%s, %.1f m)" % [str(first), g.sim.height()])
	t.check(g.sim.height() < 8.0 or first.has("nest"), "a tall tree gets a nest")
	t.check(not Visitors.arrive(g).has("butterflies"), "each visitor comes once")


func test_steering_to_deposits_grows_the_biggest_tree() -> void:
	# QA r1: ending each root after 2 m grew the biggest tree, chasing dots the smallest. Now the
	# player who steers to the deposits leads; ending early still grows a tree, only a smaller one.
	var strategies := preload("res://tools/strategies.gd")
	var dots: Dictionary = strategies.play("dots", "linden", 14, 12)
	var early: Dictionary = strategies.play("end_early", "linden", 14, 12)
	t.check(int(dots["nodes"]) > int(early["nodes"]) * 1.1, "steering to deposits leads after 12 days (%d vs %d segments)" % [dots["nodes"], early["nodes"]])
	t.check(int(early["nodes"]) > 200, "ending early still grows the tree (%d segments)" % early["nodes"])
	var longest := 0
	for s in dots["secs"]:
		longest = maxi(longest, int(s))
	t.check(longest <= 60, "no night's root takes more than a minute (%d s)" % longest)


func test_boosting_trades_life_force_for_speed() -> void:
	var c := DayCycle.new()
	c.time_of_day = c.daylight_fraction * 0.5
	var calm_light := c.light_level()
	var calm_life := c.life_force_light()
	c.boost_active = true
	t.check(c.light_level() > calm_light * 1.3, "a boost grows faster")
	t.check(c.life_force_light() <= calm_life * 0.6 + 1e-6, "but the leaves gather at most 60 % of the life force")
