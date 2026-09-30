extends SceneTree
## QA driver (reviewer 1, check 0.6.3; kept in tools/ by sim-0.6.3). Plays months like
## tools/strategies.gd "dots" (meadow start + RootBot) or "quit" (root ended at 1 m), with a
## pruning mode, and logs care signals, reach, growth and heights per day.
## --species= --seed= --roots=dots|quit|random --prune=none|daily|max|stump --days=45 [--nights]
##   daily: one noon cut of up to a fifth (largest branch <= fifth, like tests/test_care.gd)
##   max:   repeated cuts all day long (each cut <= a fifth of the living tree, as tree/pruning.gd
##          allows), until nothing cuttable is left above 30 % height; at noon
##   stump: repeated cuts anywhere above the trunk base (first_id 3), each <= max(8, fifth), up to 200 cuts, day 12 only, then no pruning

const FRAME := 1.0 / 30.0
## --set=sim.name=v / roots.name=v / clock.name=v (repeatable): tuning overrides.
static var overrides: Dictionary = {}


func _init() -> void:
	var sp := "linden"
	var seed := 14
	var roots := "dots"
	var prune := "none"
	var days := 45
	var nights := false
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--species="): sp = a.substr(10)
		elif a.begins_with("--seed="): seed = int(a.substr(7))
		elif a.begins_with("--roots="): roots = a.substr(8)
		elif a.begins_with("--prune="): prune = a.substr(8)
		elif a.begins_with("--days="): days = int(a.substr(7))
		elif a == "--nights": nights = true
		elif a.begins_with("--set="):
			var kv := a.substr(6).split("=")
			overrides[kv[0]] = float(kv[1])
	play(sp, seed, roots, prune, days, nights)
	quit()


static func _cuttable(sim: GrowthSim, limit: int, min_y: float) -> int:
	var best := -1
	var best_n := 0
	for id in range(3, sim.graph.size()):
		if sim.graph.get_flag(id, "dead", false):
			continue
		if sim.graph.positions[id].y < min_y:
			continue
		var n := sim._subtree_size(id)
		if n <= limit and n > best_n:
			best_n = n
			best = id
	return best


static func play(sp: String, seed: int, roots: String, prune: String, days: int, nights: bool) -> void:
	var g := GameState.new_game(seed, sp)
	for k: String in overrides:
		var obj: Object = g.sim if k.begins_with("sim.") else (g.roots if k.begins_with("roots.") else g.sim.clock)
		obj.set(k.get_slice(".", 1), overrides[k])
	var wander := RandomNumberGenerator.new()
	wander.seed = hash([seed, "wander"])
	var finish := -1
	var least := 1 << 30
	var least_day := -1
	var sig_days := [0, 0, 0, 0]
	var need_days := [0, 0, 0, 0]
	var hidden_unreach := 0  # need >= SHOW_MIN at noon but no signal because out of reach
	var sig_no_need := 0      # signal at 60 % daylight while today's need is 0 (broken 11)
	var sig_unreach_dusk := 0 # signal shown at noon but no deposit in reach with the real dusk life force (broken 12)
	var sig_first := [-1, -1, -1, -1]
	var shrink := 0          # days where height at dusk < height at dawn with no cut that day (broken 14)
	var shrink_txt := ""
	var total_cut := 0
	var growths: Array = []
	var zero_stuck := 0
	var streak := 0
	var worst_streak := 0
	var night_max := 0
	var lf_dusk_max := 0.0
	var b3 := [0, 0, 0, 0]
	var floor_days := 0
	for day in range(days):
		g.dive()
		var t := 0.0
		if g.can_start_run():
			var from := 0
			if roots == "dots":
				from = RootBot.new().pick_start(g.roots, g.ground, g.sim.resources)
			elif roots == "quit":
				from = 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
			g.start_run(from)
			var bot := RootBot.new()
			var stick := Vector2.ZERO
			var guard := 0
			while guard < 30000:
				guard += 1
				t += FRAME
				if roots == "quit":
					stick = Vector2(0.3, 0.0)
					if g.roots.run_length >= 1.0:
						g.finish_run_early()
				elif roots == "random":
					if guard % 60 == 1:
						stick = Vector2(wander.randf_range(-0.6, 0.6), wander.randf_range(-0.6, 0.3))
				else:
					stick = bot.stick_for(g.roots, g.ground, g.sim.resources)
				if not g.steer(stick, false, FRAME):
					break
		night_max = maxi(night_max, int(round(t)))
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		var dn := g.day_number()
		var before := g.sim.living_nodes()
		var h_dawn := g.sim.height()
		var cut_today := 0
		var noon_sig := PackedFloat32Array([0, 0, 0, 0])
		var noon_need := g.sim.care_need.duplicate()
		var done_noon := false
		var done_late := false
		while g.phase == GameState.Phase.DAY:
			var tod: float = g.sim.clock.time_of_day / g.sim.clock.daylight_fraction
			if not done_noon and tod >= 0.5:
				done_noon = true
				noon_sig = g.care_signals()
				var shown := g.sim.care_shown()
				for k in range(4):
					if g.sim.care_need[k] >= Care.SHOW_MIN:
						need_days[k] += 1
					if noon_sig[k] >= Care.SHOW_MIN:
						sig_days[k] += 1
						if sig_first[k] < 0: sig_first[k] = dn
				var strongest := 0
				for k in range(4):
					if shown[k] > shown[strongest]: strongest = k
				if shown[strongest] >= Care.SHOW_MIN and noon_sig[strongest] < Care.SHOW_MIN:
					hidden_unreach += 1
				if not g.finished:
					if prune == "daily":
						var budget := int(g.sim.living_nodes() * 0.2)
						var id := _cuttable(g.sim, budget, g.sim.height() * 0.3)
						if id >= 0: cut_today += g.sim.prune(id)
					elif prune == "max":
						for _i in range(60):
							var lim := maxi(8, int(g.sim.living_nodes() * 0.2))
							var id := _cuttable(g.sim, lim, g.sim.height() * 0.3)
							if id < 0: break
							cut_today += g.sim.prune(id)
					elif prune == "tips":
						var n_cut := 0
						for id in range(3, g.sim.graph.size()):
							if n_cut >= 20: break
							if g.sim.is_shoot_tip(id) and g.sim._subtree_size(id) == 1 and g.sim.graph.positions[id].y > g.sim.height() * 0.3:
								cut_today += g.sim.prune(id)
								n_cut += 1
					elif prune == "some":
						var id := _cuttable(g.sim, 30, g.sim.height() * 0.3)
						if id >= 0: cut_today += g.sim.prune(id)
					elif prune == "week" and dn >= 8 and dn <= 14:
						var id := _cuttable(g.sim, int(g.sim.living_nodes() * 0.2), g.sim.height() * 0.3)
						if id >= 0: cut_today += g.sim.prune(id)
					elif prune == "stump" and dn == 12:
						for _i in range(200):
							var lim := maxi(8, int(g.sim.living_nodes() * 0.2))
							var id := _cuttable(g.sim, lim, -100.0)
							if id < 0: break
							cut_today += g.sim.prune(id)
						print("  stump day %d: cut %d, living now %d, height %.2f" % [dn, cut_today, g.sim.living_nodes(), g.sim.height()])
			if not done_late and tod >= 0.6:
				done_late = true
				var s := g.care_signals()
				for k in range(4):
					if s[k] >= Care.SHOW_MIN and g.sim.care_need[k] < 0.001:
						sig_no_need += 1
			g.tick(0.5)
		total_cut += cut_today
		# Dusk: does the shown need still have a deposit in reach with the real life force?
		for k in range(4):
			if noon_sig[k] >= Care.SHOW_MIN and Care.reachable(g, k, g.sim.resources.life_force).is_empty():
				sig_unreach_dusk += 1
		lf_dusk_max = maxf(lf_dusk_max, g.sim.resources.life_force)
		if not g.finished:
			var want := g.sim.day_capacity() * g.sim.node_cost()
			for k in range(4):
				if g.sim.species.needs[k] > 0.0 and g.sim.resources.stock[k] > 2.0 * want * g.sim.species.needs[k]:
					b3[k] += 1
			if noon_need.size() == 4 and (noon_need[1] > 0.99 or noon_need[2] > 0.99 or noon_need[3] > 0.99):
				floor_days += 1
		var grown := g.sim.living_nodes() - before + cut_today
		if g.sim.height() < h_dawn - 0.05 and cut_today == 0:
			shrink += 1
			shrink_txt += " d%d(%.2f->%.2f)" % [dn, h_dawn, g.sim.height()]
		if not g.finished:
			growths.append(grown)
			if grown < least:
				least = grown
				least_day = dn
			var st := g.sim.resources.stock
			if grown <= 0 and (g.sim.resources.life_force > 1.0 or st[0] + st[1] + st[2] + st[3] > 1.0):
				zero_stuck += 1
		if nights:
			var st2 := g.sim.resources.stock
			print("  d%d night %ds grown %d cut %d h %.2f living %d grownN %d lf %.1f need [%.2f %.2f %.2f %.2f] sig [%.2f %.2f %.2f %.2f] stock [%.0f %.0f %.0f %.0f]" % [dn, int(round(t)), grown, cut_today, g.sim.height(), g.sim.living_nodes(), g.sim.grown_nodes(), g.sim.resources.life_force,
				noon_need[0], noon_need[1], noon_need[2], noon_need[3], noon_sig[0], noon_sig[1], noon_sig[2], noon_sig[3], st2[0], st2[1], st2[2], st2[3]])
		if g.finished:
			finish = day + 1
			break
	# A3: 3 days in a row under half the average
	var avg := 0.0
	for x in growths: avg += float(x)
	avg /= maxf(1.0, growths.size())
	for x in growths:
		if float(x) < avg * 0.5:
			streak += 1
			worst_streak = maxi(worst_streak, streak)
		else:
			streak = 0
	print("QA %-8s seed %-3d roots %-6s prune %-5s finished %s least %d (day %d) avg %.0f halfstreak %d zero-stuck %d nightmax %ds lfdusk %.0f cut %d | signal days W/N/P/K %s first %s | need days %s | hidden(out of reach) %d | sig w/o need %d | sig unreachable at dusk %d | shrink %d%s | B3 days stock>2 days W/N/P/K %s | full-need(NPK) days %d | days %d | h %.1f living %d" % [
		sp, seed, roots, prune, str(finish) if finish > 0 else "never", least, least_day, avg, worst_streak, zero_stuck, night_max, lf_dusk_max, total_cut,
		str(sig_days), str(sig_first), str(need_days), hidden_unreach, sig_no_need, sig_unreach_dusk, shrink, shrink_txt, str(b3), floor_days, growths.size(), g.sim.height(), g.sim.living_nodes()])
