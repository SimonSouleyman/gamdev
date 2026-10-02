extends SceneTree
## The root run's sim cost per frame (0.8.2.3, docs/notes/perf-0.8.2.3.md): plays whole months
## headless with the nights of tools/strategies.gd and prints, per strategy, the average and
## longest night (all frames of the run), the slowest run frame (tip step, magnet and detour) and
## the end of the night (the fine and side roots; the view's settle not included), apart.
## `fp` is a fingerprint of the roots (every node's position), the stock and the finish day:
## equal fingerprints, equal games.
## Strategies: dots (the RootBot), straight_down, end_early (ended after 1 m), at_once (ended
## before a start every night).
## --frames=<usec>: the end of the night grows as in the root view (RootSystem.end_in_frames),
## the frame after the run's last one onward getting <usec> each; the end column is then the
## slowest of those frames. Without it everything grows in the run's last frame, as in the tools.
## Run: godot --headless --path . -s tools/root_perf.gd -- --phone [--species=linden] [--seed=14]
##      [--strats=dots,straight_down,end_early,at_once] [--frames=6000] [--nights]

const FRAME: float = 1.0 / 30.0


func _init() -> void:
	var species_id := "linden"
	var seed := 14
	var strats: Array = ["dots", "straight_down"]
	var nights := false
	var frames := -1
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--species="):
			species_id = a.substr(10)
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--strats="):
			strats = Array(a.substr(9).split(","))
		elif a.begins_with("--frames="):
			frames = int(a.substr(9))
		elif a == "--nights":
			nights = true
	for s in strats:
		print(play(str(s), species_id, seed, nights, frames))
	quit()


static func play(strat: String, species_id: String, seed: int, nights: bool, frames: int) -> String:
	var g := GameState.new_game(seed, species_id)
	var in_frames := frames > 0
	g.roots.end_in_frames = in_frames
	var night_ms: Array = []
	var run_max_us := 0
	var run_sum_us := 0
	var run_frames := 0
	var end_max_us := 0
	var end_sum_us := 0
	var end_frames_most := 0
	var lens: Array = []
	var finish := -1
	for day in range(45):
		g.dive()
		var night_us := 0
		var frame_max := 0
		var end_us := 0
		var ended := false
		if strat == "at_once" and g.can_start_run():
			var a0 := Time.get_ticks_usec()
			if in_frames:
				g.roots.end_at_once(g.ground, g.sim.resources)
				g.mark_run_started()
			else:
				g.finish_run_early()
			end_us = Time.get_ticks_usec() - a0
			night_us = end_us
			ended = true
		elif g.can_start_run():
			var from := 0 if g.roots.graph.size() <= 1 or (lens.size() > 0 and lens[-1] == 0) else g.roots.graph.size() - 1
			var bot := RootBot.new()
			if strat == "dots":
				var starter := RootBot.new()
				starter.start_by_need = true
				from = starter.pick_start(g.roots, g.ground, g.sim.resources)
			g.start_run(from)
			var guard := 0
			while guard < 30000:
				guard += 1
				var stick := Vector2(0.0, -1.0) if strat == "straight_down" else Vector2(0.3, 0.0) if strat == "end_early" else bot.stick_for(g.roots, g.ground, g.sim.resources)
				var f0 := Time.get_ticks_usec()
				var alive: bool
				if in_frames:
					# As the root view: the roots directly, the run's end told after the end grew.
					alive = g.roots.advance(stick, false, FRAME, g.ground, g.sim.resources)
					for f in g.roots.last_finds:
						g.notify_find(f)
					if alive and strat == "end_early" and g.roots.run_length >= 1.0:
						g.roots.finish_early(g.ground, g.sim.resources)
						alive = false
				else:
					alive = g.steer(stick, false, FRAME)
					if alive and strat == "end_early" and g.roots.run_length >= 1.0:
						g.finish_run_early()
						alive = false
				var us := Time.get_ticks_usec() - f0
				night_us += us
				if not alive:
					end_us = us
					ended = true
					break
				frame_max = maxi(frame_max, us)
				run_sum_us += us
				run_frames += 1
		if ended and in_frames:
			# The frames after the run's last one: the end grows on (nothing else touches the roots).
			var n := 0
			while g.roots.end_pending():
				var e0 := Time.get_ticks_usec()
				g.roots.grow_on(frames)
				var us := Time.get_ticks_usec() - e0
				end_us = maxi(end_us, us)
				night_us += us
				n += 1
			end_frames_most = maxi(end_frames_most, n)
			g.notify_run_done()
		lens.append(int(g.roots.run_length))
		night_ms.append(night_us / 1000.0)
		run_max_us = maxi(run_max_us, frame_max)
		end_max_us = maxi(end_max_us, end_us)
		end_sum_us += end_us
		if nights:
			print("  %s day %d: night %.0f ms, slowest run frame %.2f ms, end %.2f ms, nodes %d" % [strat, day + 1, night_us / 1000.0, frame_max / 1000.0, end_us / 1000.0, g.roots.graph.size()])
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		if g.finished:
			finish = day + 1
			break
	var sum := 0.0
	var most := 0.0
	for m in night_ms:
		sum += m
		most = maxf(most, m)
	var fp := hash([g.roots.graph.positions, g.roots.graph.parents, g.sim.resources.stock, finish, lens])
	return "%-14s %s seed %d finished %s | night ms avg %.0f max %.0f | run frame avg %.3f max %.2f ms | end of night%s avg %.2f max %.2f ms%s | fp %d nodes %d | root m %s" % [
		strat, species_id, seed, str(finish), sum / maxi(night_ms.size(), 1), most,
		run_sum_us / 1000.0 / maxi(run_frames, 1), run_max_us / 1000.0,
		" (slowest frame)" if in_frames else "", end_sum_us / 1000.0 / maxi(night_ms.size(), 1), end_max_us / 1000.0,
		(", up to %d more frames" % end_frames_most) if in_frames else "", fp, g.roots.graph.size(), str(lens)]
