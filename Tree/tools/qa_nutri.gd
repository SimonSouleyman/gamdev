extends SceneTree
## Copied from the 0.7 check (tree-qa/check-0.7/logs/nutri.gd) for the 0.8 balance fixes, with a
## per-kind count added. Run: godot --headless --path . -s tools/qa_nutri.gd -- --species=alder
## --seed=14 [--style=dots|tip] [--v]
## QA 0.7 r1: nutrient numbers in meadow-reading play ("dots": RootBot start by need + RootBot).
## Per night (at dusk, before the run): the tree's scarcest kind (stock / need), whether a fresh
## deposit of it is in reach of a full calm tank from any root start (Care.reachable),
## fresh dots / patches in reach with a calm tank and with tonight's real life force; whether
## the run touched a fresh deposit. Per day: tick-weighted growth factor, share of daylight at
## the soft floor, dusk stock over two days' growth.
const FRAME := 1.0 / 30.0
## --set=sim.x=v / roots.x=v overrides, applied to the new game.
static var sets: Dictionary = {}

func _init() -> void:
	var sp := "linden"
	var seed := 14
	var style := "dots"
	var verbose := false
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--species="): sp = a.substr(10)
		elif a.begins_with("--seed="): seed = int(a.substr(7))
		elif a.begins_with("--style="): style = a.substr(8)
		elif a == "--v": verbose = true
		elif Underground.tool_arg(a): pass
		elif a.begins_with("--set="):
			var sv := a.substr(6).split("=")
			sets[sv[0]] = float(sv[1])
	play(sp, seed, style, verbose)
	quit()


static func in_reach(g: GameState, kind: int, life_force: float) -> Array:
	# [dots, patches] fresh (untapped, >= DEPOSIT_MIN) within reach of any root start cell.
	var roots := g.roots
	var ground := g.ground
	var starts := {}
	for id in range(roots.graph.size()):
		var p := roots.graph.positions[id]
		var key := Vector3i((p / 1.5).floor())
		if not starts.has(key):
			starts[key] = p
	var pts: Array = starts.values()
	var n := 0
	var pset := {}
	var pid_of := {}
	for pi in range(ground.patches.size()):
		for i in range(int(ground.patches[pi]["first"]), int(ground.patches[pi]["end"])):
			pid_of[i] = pi
	for i in range(ground.dot_count()):
		if kind >= 0 and ground.dot_kinds[i] != kind:
			continue
		if ground.dot_collected[i] != 0 or roots.tapped.has(i) or ground.fullness(i) < Care.DEPOSIT_MIN:
			continue
		var at := ground.dot_positions[i]
		var d := INF
		for p in pts:
			d = minf(d, at.distance_to(p))
		if d * roots.cost_per_metre(at) > life_force:
			continue
		n += 1
		if pid_of.has(i): pset[pid_of[i]] = int(pset.get(pid_of[i], 0)) + 1
	var wishes := 0
	# A real deposit: at least a quarter of its dots still fresh (not a few regrown stragglers).
	var real := 0
	for pi in pset:
		if bool(ground.patches[pi].get("wish", false)): wishes += 1
		if int(pset[pi]) >= 0.25 * ground.patch_dots(pi).size(): real += 1
	return [n, pset.size(), wishes, real]


static func play(sp: String, seed: int, style: String, verbose: bool) -> void:
	var g := GameState.new_game(seed, sp)
	for k in sets:
		if str(k).begins_with("sim."): g.sim.set(str(k).substr(4), sets[k])
		elif str(k).begins_with("roots."): g.roots.set(str(k).substr(6), sets[k])
		elif str(k) == "wish_share": Diary.underground_share = sets[k]
	var needs := g.sim.species.needs
	var calm := g.roots.calm_life_force
	var nights := 0
	var b1_ok := 0
	var b1_ok_real := 0
	var touched_nights := 0
	var late_n := 0
	var late_touch := 0
	var late_reach := 0
	var max_patches := 0
	var max_wish := 0
	var max_real := 0
	var min_real := 1000
	var max_dots := 0
	var max_patches_real := 0
	var half_days := 0
	var floor_days := 0
	var b3_days := 0
	var b3_any := 0
	var min_m_per_dot := INF
	var m_per_dot: Array = []
	var days_n := 0
	var finish := -1
	var floor_kind := PackedInt32Array([0, 0, 0, 0])
	var over_kind := PackedInt32Array([0, 0, 0, 0])
	var used := PackedFloat32Array([0, 0, 0, 0])
	for day in range(45):
		var dn := day + 1
		# Dusk reading (before the dive): scarcest kind among needed.
		var st := g.sim.resources.stock
		var scarce := -1
		var worst := INF
		for k in range(4):
			if needs[k] > 0.0 and st[k] / needs[k] < worst:
				worst = st[k] / needs[k]
				scarce = k
		g.dive()
		var lf := g.sim.resources.life_force
		var can := g.can_start_run()
		var r_calm := in_reach(g, scarce, calm) if can else [0, 0]
		var r_any := in_reach(g, -1, calm) if can else [0, 0]
		var r_real := in_reach(g, -1, lf) if can else [0, 0]
		var ok := can and not Care.reachable(g, scarce, calm).is_empty()
		var ok_real := can and not Care.reachable(g, scarce, lf).is_empty()
		nights += 1
		if ok: b1_ok += 1
		if ok_real: b1_ok_real += 1
		max_patches = maxi(max_patches, r_any[1])
		if r_any.size() > 2: max_wish = maxi(max_wish, r_any[2])
		if r_any.size() > 3:
			max_real = maxi(max_real, r_any[3])
			if dn > 1: min_real = mini(min_real, r_any[3])
		max_dots = maxi(max_dots, r_any[0])
		max_patches_real = maxi(max_patches_real, r_real[1])
		var tapped_before := g.roots.tapped.size()
		if can:
			var from := 0
			if style == "dots":
				var starter := RootBot.new()
				starter.start_by_need = true
				from = starter.pick_start(g.roots, g.ground, g.sim.resources)
			elif style == "tip":
				from = Diary.newest_tip(g.roots)
			g.start_run(from)
			var bot := RootBot.new()
			var guard := 0
			while guard < 30000:
				guard += 1
				if not g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
					break
		var touched := g.roots.tapped.size() > tapped_before
		var ntouch: int = g.roots._run_touched.size() if can else 0
		if ntouch > 0: min_m_per_dot = minf(min_m_per_dot, g.roots.run_length / ntouch)
		if ntouch > 0 and g.roots.run_length > 1.5: m_per_dot.append(g.roots.run_length / ntouch)
		if touched: touched_nights += 1
		if dn >= 20 and dn <= 30:
			late_n += 1
			if touched: late_touch += 1
			if r_any[0] > 0: late_reach += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		var wsum := 0.0
		var fsum := 0.0
		var at_floor := 0.0
		var cause := PackedFloat32Array([0, 0, 0, 0])
		var before := g.sim.living_nodes()
		while g.phase == GameState.Phase.DAY:
			var f := Resources.growth_factor(g.sim.resources.stock, needs, g.sim.growth_floor())
			var w := maxf(g.sim.clock.sun_height(), 0.0)
			wsum += w
			fsum += w * f
			if f <= g.sim.growth_floor() + 0.05:
				at_floor += w
				var lo := INF
				var lk := 0
				for k in range(4):
					if needs[k] > 0.0 and g.sim.resources.stock[k] / needs[k] < lo:
						lo = g.sim.resources.stock[k] / needs[k]
						lk = k
				cause[lk] += w
			var cost := g.sim.node_cost()
			var n0 := g.sim.graph.size()
			g.tick(0.5)
			for k in range(4):
				used[k] += maxf(0, g.sim.graph.size() - n0) * cost * needs[k]
		var avgf := fsum / maxf(wsum, 1e-6)
		var grown := g.sim.living_nodes() - before
		if not g.finished:
			days_n += 1
			if avgf <= 0.6: half_days += 1
			if at_floor / maxf(wsum, 1e-6) >= 0.5:
				floor_days += 1
				var ck := 0
				for k in range(4):
					if cause[k] > cause[ck]: ck = k
				floor_kind[ck] += 1
			var want := g.sim.day_capacity() * g.sim.node_cost()
			var over := false
			for k in range(4):
				if needs[k] > 0.0 and g.sim.resources.stock[k] > 2.0 * want * needs[k]:
					over = true
					over_kind[k] += 1
			# B3 per tuning.md: all needed kinds cover two days (the run stops mattering)
			var all_over := true
			for k in range(4):
				if needs[k] > 0.0 and g.sim.resources.stock[k] <= 2.0 * want * needs[k]:
					all_over = false
			if all_over: b3_days += 1
			if over: b3_any += 1
		if verbose:
			var s2 := g.sim.resources.stock
			print("  d%d scarce %d reach(calm) %s real lf %.0f %s any %s/%s patches (real %s), touched %s grown %d avgf %.2f floorshare %.2f stock [%.0f %.0f %.0f %.0f]" % [dn, scarce, str(ok), lf, str(ok_real), r_any[1], r_real[1], r_any[3] if r_any.size() > 3 else -1, str(touched), grown, avgf, at_floor / maxf(wsum, 1e-6), s2[0], s2[1], s2[2], s2[3]])
		if g.finished:
			finish = dn
			break
	print("NUTRI %-8s seed %-3d style %s finish %s | B1 scarce-kind in calm reach %d/%d (%.0f%%), with real lf %d/%d | touched fresh %d/%d | B2 days avg factor<=0.6 %d/%d, >=half daylight at floor %d/%d | B3 all needed stock>2 days %d/%d (any kind %d) | B4 max fresh in calm reach: patches %d dots %d (real lf patches %d) | run metres per fresh dot touched: median %.1f min %.1f | B5 d20-30 touched %d/%d, any fresh in calm reach %d/%d" % [
		sp, seed, style, str(finish) if finish > 0 else "never", b1_ok, nights, 100.0 * b1_ok / maxf(1, nights), b1_ok_real, nights, touched_nights, nights,
		half_days, days_n, floor_days, days_n, b3_days, days_n, b3_any, max_patches, max_dots, max_patches_real, _median(m_per_dot), min_m_per_dot, late_touch, late_n, late_reach, late_n])
	print("B4WISH %-8s seed %-3d max wish patches in reach %d | deposits in reach with a quarter or more of their dots fresh: %d to %d (nights 2 on)" % [sp, seed, max_wish, min_real, max_real])
	print("KINDS %-8s seed %-3d floor days by kind: %s | stock over 2 days by kind: %s | used W %.0f N %.0f P %.0f K %.0f" % [sp, seed, _pct(floor_kind, days_n), _pct(over_kind, days_n), used[0], used[1], used[2], used[3]])


static func _pct(a: PackedInt32Array, n: int) -> String:
	return "W %d%% N %d%% P %d%% K %d%%" % [100 * a[0] / maxi(n, 1), 100 * a[1] / maxi(n, 1), 100 * a[2] / maxi(n, 1), 100 * a[3] / maxi(n, 1)]


static func _median(a: Array) -> float:
	if a.is_empty(): return -1.0
	a.sort()
	return float(a[a.size() / 2])
