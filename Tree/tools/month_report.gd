extends SceneTree
## Prints one line per in-game day of bot play until the tree is finished (or 45 days), for
## tuning growth and costs, then a summary line.
## Run: godot --headless --path . -s tools/month_report.gd -- [--species=<id>|all] [--seed=14]
func _init():
	var species_arg := "linden"
	var seed := 14
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--species="):
			species_arg = a.substr(10)
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
	var ids: Array = Species.ORDER if species_arg == "all" else [species_arg]
	var summaries: Array[String] = []
	for sid in ids:
		summaries.append(_report(str(sid), seed))
	print("")
	for line in summaries:
		print(line)
	quit()


func _report(species_id: String, seed: int) -> String:
	var g := GameState.new_game(seed, species_id)
	var sp := g.sim.species
	print("== %s (target %d days, finish at %d segments)" % [sp.display_name, sp.target_days, sp.finish_nodes])
	var least := 1_000_000
	var finished_day := -1
	for day in range(45):
		var lf := g.sim.resources.life_force
		g.dive()
		var bot := RootBot.new()
		g.start_run(bot.pick_start(g.roots, g.ground, g.sim.resources))
		var guard := 0
		var t0 := Time.get_ticks_msec()
		while g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, 1.0/30.0) and guard < 20000:
			guard += 1
		var t1 := Time.get_ticks_msec()
		while g.phase == GameState.Phase.NIGHT: g.tick(0.2)
		var before := g.sim.graph.size()
		var st := g.sim.resources.stock
		while g.phase == GameState.Phase.DAY: g.tick(1.0)
		var t2 := Time.get_ticks_msec()
		var grown := g.sim.graph.size() - before
		least = mini(least, grown)
		print("day %d lf %.1f run %.1fm stock [%.0f %.0f %.0f %.0f] grown %d nodes %d h %.1f tips %d | run %dms day %dms" % [day+1, lf, g.roots.run_length, st[0], st[1], st[2], st[3], grown, g.sim.graph.size(), g.sim.height(), g.sim.tip_count(), t1-t0, t2-t1])
		if g.finished:
			finished_day = day + 1
			break
	return "%-16s finished day %s (target %d), %d segments, %.1f m tall, least growth in a day %d" % [
		sp.display_name, str(finished_day) if finished_day > 0 else "never", sp.target_days, g.sim.graph.size(), g.sim.height(), least]
