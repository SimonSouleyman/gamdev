extends SceneTree
## Does the player's choice at night matter? Plays whole months headless with different
## night and day strategies and prints the tree's height at day 10, 20 and 30, the day it was
## finished, how long each night's root took in real seconds and how long it grew, for balancing
## the root run (QA r1). One line per strategy; --nights adds a line per day.
## Run: godot --headless --path . -s tools/strategies.gd -- [--species=linden] [--seed=42]
##      [--days=45] [--strats=dots,end_early,...] [--nights]
## Strategies (nights / days):
##   dots           the RootBot chases deposits / calm days (no boost): the "calm" player
##   end_early      the root is ended after 1 m / calm days
##   straight_down  stick held down, never steered / calm days
##   random         the stick drifts gently at random, never aiming / calm days
##   boost_all      the RootBot / the sun boosted all day long
##   boost_quit     ended after 1 m / boosted all day long
##   boost_morning  the RootBot / boosted the first third of each day
##   tip            the RootBot from the newest root tip (continuing the last root) / calm days
##   wish           from the newest root tip toward the wish's glow, then the RootBot / calm days
##   cut_marks      the RootBot / calm days, and every noon each twig the tree marks is cut at
##                  its fork (0.7, broken list 9: never sooner than dots)
##   cut_marks_tip  as cut_marks, but only the marked tip itself is cut (the smallest cut)

const FRAME: float = 1.0 / 30.0
## --set=name=value (repeatable): tuning overrides, applied to the RootSystem, or with a
## "clock." prefix to the DayCycle, "sim." to the GrowthSim or "species." to its Species, before
## the first night. --mix=, --deep=, --scatter=, --gap= try another soil (Underground.tool_arg).
static var overrides: Dictionary = {}
const ALL: Array = ["dots", "end_early", "straight_down", "random", "boost_all", "boost_quit"]


func _init() -> void:
	var species_id := "linden"
	var seed := 42
	var days := 45
	var strats: Array = ALL
	var nights := false
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--set="):
			var kv := a.substr(6).split("=")
			overrides[kv[0]] = float(kv[1])
		Underground.tool_arg(a)
		if a.begins_with("--species="):
			species_id = a.substr(10)
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--strats="):
			strats = Array(a.substr(9).split(","))
		elif a == "--nights":
			nights = true
	for s in strats:
		print(summary(play(str(s), species_id, seed, days, nights)))
	quit()


## Plays one strategy. Returns {"strat", "species", "seed", "finish" (day or -1), "heights"
## {10, 20, 30}, "nodes" (living segments at the end), "secs" and "lens" per night, "least" (fewest segments grown in a played day),
## "lf_left" (most life force left after a night)}.
static func play(strat: String, species_id: String, seed: int, days: int, nights: bool = false) -> Dictionary:
	var g := GameState.new_game(seed, species_id)
	for k in overrides:
		if str(k).begins_with("clock."):
			g.sim.clock.set(str(k).substr(6), overrides[k])
		elif str(k).begins_with("sim."):
			g.sim.set(str(k).substr(4), overrides[k])
		elif str(k).begins_with("species."):
			g.sim.species.set(str(k).substr(8), overrides[k])
		elif str(k) == "start_any":
			pass
		elif str(k) == "wish_share":
			Diary.underground_share = overrides[k]
		elif str(k) == "capacity":
			for i in range(g.ground.dot_count()):
				g.ground.dot_capacity[i] *= overrides[k]
				g.ground.dot_amounts[i] *= overrides[k]
		else:
			g.roots.set(str(k), overrides[k])
	var wander := RandomNumberGenerator.new()
	wander.seed = hash([seed, "wander"])
	var heights := {}
	var secs: Array = []
	var lens: Array = []
	var least := 1_000_000
	var lf_left := 0.0
	var finish_day := -1
	# The night's sim cost (0.8.1): wall milliseconds of each night's run and its slowest frame.
	var run_ms: Array = []
	var frame_us := 0
	var quits := strat == "end_early" or strat == "boost_quit"
	var boosts := strat.begins_with("boost")
	for day in range(days):
		g.dive()
		var t := 0.0
		var t0 := Time.get_ticks_usec()
		if g.can_start_run():
			# From the tip of the last root, or from the trunk after a night the root could not grow.
			var from := 0 if g.roots.graph.size() <= 1 or (lens.size() > 0 and lens[-1] == 0) else g.roots.graph.size() - 1
			var bot := RootBot.new()
			if strat == "tip" or strat == "wish":
				from = Diary.newest_tip(g.roots)
				var glows := g.wish_glows()
				if strat == "wish" and not glows.is_empty():
					bot.goal = glows[0]["center"]
					bot.goal_radius = float(glows[0]["radius"]) * 0.5
			elif strat == "dots" or strat.begins_with("cut_marks") or strat.begins_with("boost_") and not quits:
				var starter := RootBot.new()
				starter.start_by_need = not overrides.has("start_any")
				from = starter.pick_start(g.roots, g.ground, g.sim.resources)
			elif strat == "random":
				from = 0  # a player who does not plan starts at the trunk
			g.start_run(from)
			var stick := Vector2.ZERO
			var guard := 0
			while guard < 30000:
				guard += 1
				t += FRAME
				if quits:
					stick = Vector2(0.3, 0.0)
					if g.roots.run_length >= 1.0:
						g.finish_run_early()
				elif strat == "straight_down":
					stick = Vector2(0.0, -1.0)
				elif strat == "random":
					# A player who does not aim: the stick drifts gently every two seconds.
					if guard % 60 == 1:
						stick = Vector2(wander.randf_range(-0.6, 0.6), wander.randf_range(-0.6, 0.3))
				else:
					stick = bot.stick_for(g.roots, g.ground, g.sim.resources)
				var f0 := Time.get_ticks_usec()
				var alive := g.steer(stick, false, FRAME)
				frame_us = maxi(frame_us, Time.get_ticks_usec() - f0)
				if not alive:
					break
		run_ms.append((Time.get_ticks_usec() - t0) / 1000)
		secs.append(int(round(t)))
		lens.append(int(g.roots.run_length))
		lf_left = maxf(lf_left, g.sim.resources.life_force)
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		var before := g.sim.living_nodes()
		var pruned := false
		while g.phase == GameState.Phase.DAY:
			var tod: float = g.sim.clock.time_of_day / g.sim.clock.daylight_fraction
			if boosts and (strat != "boost_morning" or tod < 0.33):
				g.boost_hour()
			if strat.begins_with("cut_marks") and not pruned and tod >= 0.5:
				pruned = true
				cut_marks(g.sim, strat == "cut_marks_tip")
			g.tick(0.5)
		if not g.finished:
			least = mini(least, g.sim.living_nodes() - before)
		if nights:
			var st := g.sim.resources.stock
			var rt := g.roots.run_totals
			print("  %s day %d: night %ds, root %dm, h %.1f, nodes %d, lf %.1f, stock [%.0f %.0f %.0f %.0f], run drank [%.1f %.1f %.1f %.1f], left %.0f, side nodes %d/%d, thick %.2f" % [strat, day + 1, secs[-1], lens[-1], g.sim.height(), g.sim.living_nodes(), g.sim.resources.life_force, st[0], st[1], st[2], st[3],
				rt[0], rt[1], rt[2], rt[3], g.roots.leftover_spent, g.roots.side_nodes_grown[0], g.roots.side_nodes_grown[1], g.roots.thickness_of(g.roots.main_root_count - 1)])
		if day + 1 in [10, 20, 30]:
			heights[day + 1] = g.sim.height()
		if g.finished:
			finish_day = day + 1
			heights["fin"] = g.sim.height()
			break
	# Each wish deposit counts once: reached when a root reached its glow (Underground
	# .mark_wish_reached). 0.8.2.3: counting the diary's lines with a drawing also counted the
	# finds' sketches and the glows reached again (73 of 8 in sim-0.8.2.2).
	var wished := g.ground.wish_deposits.size()
	var reached := 0
	for w in g.ground.wish_deposits:
		if bool(w.get("reached", false)):
			reached += 1
	return {"strat": strat, "species": species_id, "seed": seed, "finish": finish_day, "heights": heights, "nodes": g.sim.living_nodes(),
		"secs": secs, "lens": lens, "least": least, "lf_left": lf_left, "wished": wished, "reached": reached,
		"run_ms": run_ms, "frame_us": frame_us}


## Cuts every twig the tree marks right now: at its fork, or (`tip_only`) only the tip.
## Returns the segments cut.
static func cut_marks(sim: GrowthSim, tip_only: bool = false) -> int:
	var cut := 0
	for m in sim.marks.duplicate():
		if sim.mark_strength(m) <= 0.0:
			continue
		var twig := sim.marked_twig(int(m["id"]))
		if not twig.is_empty():
			cut += sim.prune(twig[0] if tip_only else twig[-1])
	return cut


static func summary(r: Dictionary) -> String:
	var hs: Dictionary = r["heights"]
	var h := func(d: Variant) -> String: return ("%.1f" % hs[d]) if hs.has(d) else "-"
	var ms: Array = r.get("run_ms", [0])
	var most := 0
	var sum := 0
	for m in ms:
		most = maxi(most, int(m))
		sum += int(m)
	return "%-14s %-8s seed %-3d h10 %s  h20 %s  h30 %s  hfin %s  finished %s  least/day %d  lf left %.0f  wishes reached %d/%d | run ms avg %d max %d, slowest frame %.1f ms | night s %s | root m %s" % [
		r["strat"], r["species"], r["seed"], h.call(10), h.call(20), h.call(30), h.call("fin"),
		str(r["finish"]) if r["finish"] > 0 else "never", r["least"], r["lf_left"], r.get("reached", 0), r.get("wished", 0),
		sum / maxi(ms.size(), 1), most, r.get("frame_us", 0) / 1000.0, str(r["secs"]), str(r["lens"])]
