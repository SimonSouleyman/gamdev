extends SceneTree
## Prints one line per in-game day for 30 days of bot play, for tuning growth and costs.
## Run: godot --headless --path . -s tools/month_report.gd
func _init():
	var g := GameState.new_game(14)
	for day in range(30):
		var lf := g.sim.resources.life_force
		g.dive()
		var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
		var angle := day * 2.4
		g.start_run(start)
		var guard := 0
		var t0 := Time.get_ticks_msec()
		var bot := RootBot.new()
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0/30.0) and guard < 20000:
			guard += 1
		var t1 := Time.get_ticks_msec()
		while g.phase == GameState.Phase.NIGHT: g.tick(0.2)
		var before := g.sim.graph.size()
		var st := g.sim.resources.stock
		while g.phase == GameState.Phase.DAY: g.tick(1.0)
		var t2 := Time.get_ticks_msec()
		print("day %d lf %.1f run %.1fm tip %s stock %s grown %d nodes %d h %.1f tips %d markers %d | run %dms day %dms" % [day+1, lf, g.roots.run_length, g.roots.tip_position.snapped(Vector3.ONE*0.1), st, g.sim.graph.size()-before, g.sim.graph.size(), g.sim.height(), g.sim.tip_count(), g.sim.colonizer.markers.size(), t1-t0, t2-t1])
	quit()
