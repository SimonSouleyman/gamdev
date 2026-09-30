extends SceneTree
## Copied from the 0.7 check (tree-qa/check-0.7/logs/glow.gd) for the 0.8 balance fixes.
## Run: godot --headless --path . -s tools/qa_glow.gd -- --species=linden --seed=14
## QA 0.7 r1: the wish glow per night. Plays "dots" (meadow start + RootBot). At each dive:
## how many glows show, whether today's glow belongs to an underground wish placed today, and
## whether each glowing patch is in straight-line reach of a full calm tank from any root node.
const FRAME := 1.0 / 30.0

func _init() -> void:
	var sp := "linden"
	var seed := 14
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--species="): sp = a.substr(10)
		elif a.begins_with("--seed="): seed = int(a.substr(7))
	var g := GameState.new_game(seed, sp)
	var calm := g.roots.calm_life_force
	var nights := 0
	var glow_nights := 0
	var two := 0
	var unreach := 0
	var today_unreach := 0
	var today_n := 0
	var no_wish_glow := 0
	var finish := -1
	for day in range(45):
		g.dive()
		var glows := g.wish_glows()
		nights += 1
		if not glows.is_empty(): glow_nights += 1
		if glows.size() >= 2: two += 1
		var placed_today := false
		for w in g.ground.wish_deposits:
			if int(w["day"]) == g.day_number(): placed_today = true
		for gl in glows:
			var c: Vector3 = gl["center"]
			var d := INF
			for id in range(g.roots.graph.size()):
				d = minf(d, g.roots.graph.positions[id].distance_to(c) - float(gl["radius"]))
			var ok := maxf(d, 0.0) * g.roots.cost_per_metre(c) <= calm
			if not ok: unreach += 1
			if float(gl["strength"]) >= 0.99:
				today_n += 1
				if not ok: today_unreach += 1
				if not placed_today: no_wish_glow += 1
		if g.can_start_run():
			var starter := RootBot.new()
			starter.start_by_need = true
			g.start_run(starter.pick_start(g.roots, g.ground, g.sim.resources))
			var bot := RootBot.new()
			var guard := 0
			while guard < 30000:
				guard += 1
				if not g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
					break
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		if g.finished:
			finish = day + 1
			break
	print("GLOW %-8s seed %-3d finish %d nights %d, nights with a glow %d, nights with two glows %d, today's glows %d (not placed today %d), glows out of calm reach %d (today's %d)" % [sp, seed, finish, nights, glow_nights, two, today_n, no_wish_glow, unreach, today_unreach])
	quit()
