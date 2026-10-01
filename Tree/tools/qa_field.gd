extends SceneTree
## The wider root field (0.8.1, items 29 and 34, docs/notes/sim-0.8.1.md). Plays a month and
## prints, per night and in sum:
## - rich patches the night's root touched (a fresh dot of a rich topsoil patch drunk or tapped;
##   not the starter patch, not wish deposits), and rich patches "in reach of a calm night from
##   the best start": the patch's near edge within a straight drive that tonight's tank would pay
##   at tonight's pace, from where the night started (the best start in "dots" play);
## - how far the root got from its start (straight), and from the trunk;
## - the wish: share of underground-wish days that point at a patch Underground.FAR_RING or more
##   from the trunk, and for each far wish how many nights it took to reach it ("wish" style
##   follows the glow from the newest tip, like strategies.gd "wish");
## - for the first week, which kinds a calm night can reach in the near ring (rich patches,
##   Diary.reachable), and whether every needed kind has a fresh deposit within one calm night
##   (Care.reachable on a calm tank);
## - B1 for the wider field: the scarcest kind at dusk has a fresh deposit within one calm night,
##   and within two (a root continued: twice a calm tank).
## Run: godot --headless --path . -s tools/qa_field.gd -- --species=linden --seed=14
##      [--style=dots|wish] [--v] [--set=roots.x=v] [soil args as Underground.tool_arg]
const FRAME := 1.0 / 30.0
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


## Index of the rich patch each dot belongs to (rich: a layout patch outside the starter, not a
## wish deposit, in the topsoil); -1 otherwise.
static func rich_of(ground: Underground) -> PackedInt32Array:
	var out := PackedInt32Array()
	out.resize(ground.dot_count())
	out.fill(-1)
	for pi in range(ground.patches.size()):
		var p: Dictionary = ground.patches[pi]
		var c: Vector3 = p["center"]
		if bool(p.get("wish", false)) or -c.y > Underground.TOPSOIL or Vector2(c.x, c.z).length() < 2.0:
			continue
		for i in range(int(p["first"]), int(p["end"])):
			out[i] = pi
	return out


## Rich patches with at least a quarter of their dots fresh whose near edge a straight root from
## `from` reaches on `life_force` at the run's cost scale. With `dists` given, their near edges'
## distances from `from` are appended to it.
static func in_straight_reach(g: GameState, from: Vector3, life_force: float, cost_scale: float, dists: Array = []) -> int:
	var n := 0
	for pi in range(g.ground.patches.size()):
		var p: Dictionary = g.ground.patches[pi]
		var c: Vector3 = p["center"]
		if bool(p.get("wish", false)) or -c.y > Underground.TOPSOIL or Vector2(c.x, c.z).length() < 2.0:
			continue
		var fresh := 0
		var dots := g.ground.patch_dots(pi)
		for i in dots:
			if g.roots.is_fresh(i) and g.ground.dot_collected[i] == 0:
				fresh += 1
		if fresh < 0.25 * dots.size():
			continue
		var to := c - from
		var goal := c - to.normalized() * float(p["radius"]) if to.length() > float(p["radius"]) else from
		dists.append(from.distance_to(goal))
		var cost := Diary.line_cost(g.ground, g.roots, from, goal) * cost_scale
		if cost <= life_force:
			n += 1
	return n


static func play(sp: String, seed: int, style: String, verbose: bool) -> void:
	var g := GameState.new_game(seed, sp)
	for k in sets:
		if str(k).begins_with("sim."): g.sim.set(str(k).substr(4), sets[k])
		elif str(k).begins_with("roots."): g.roots.set(str(k).substr(6), sets[k])
		elif str(k) == "far_share": Diary.far_share = sets[k]
	var rich := rich_of(g.ground)
	var touched_n: Array = []
	var reach_n: Array = []
	var drive_n: Array = []
	var dist_days := 0
	var disp: Array = []
	var far_tip: Array = []
	var wish_days := 0
	var far_days := 0
	var far_wishes := {}
	var near_week_kinds := {}
	var finish := -1
	var calm := g.roots.calm_life_force
	var week_all := 0
	var b1_one := 0
	var b1_two := 0
	var nights := 0
	for day in range(45):
		var dn := day + 1
		var st := g.sim.resources.stock
		var needs := g.sim.species.needs
		var scarce := 0
		var worst := INF
		for k in range(4):
			if needs[k] > 0.0 and st[k] / needs[k] < worst:
				worst = st[k] / needs[k]
				scarce = k
		g.dive()
		if g.can_start_run():
			nights += 1
			if not Care.reachable(g, scarce, calm).is_empty(): b1_one += 1
			if not Care.reachable(g, scarce, 2.0 * calm).is_empty(): b1_two += 1
			if dn <= 7:
				var all := true
				for k in range(4):
					if needs[k] > 0.0 and Care.reachable(g, k, calm).is_empty():
						all = false
				if all: week_all += 1
		var lf := g.sim.resources.life_force
		if g.diary.wish_patch >= 0:
			wish_days += 1
			var wc: Vector3 = g.ground.patches[g.diary.wish_patch]["center"]
			if Vector2(wc.x, wc.z).length() >= Underground.FAR_RING:
				dist_days += 1
			if Diary.is_far(g.ground, g.diary.wish_patch):
				far_days += 1
				if not far_wishes.has(g.diary.wish_patch):
					far_wishes[g.diary.wish_patch] = {"day": dn, "reached": -1}
		# First week: the kinds of the near ring's patches a calm tank reaches from the trunk.
		if dn <= 7:
			for pi in range(g.ground.patches.size()):
				var p: Dictionary = g.ground.patches[pi]
				var c: Vector3 = p["center"]
				var r := Vector2(c.x, c.z).length()
				if bool(p.get("wish", false)) or r < 2.0 or r > Underground.FAR_RING or -c.y > Underground.TOPSOIL:
					continue
				if Diary.reachable(g.ground, pi, g.roots):
					near_week_kinds[int(p["kind"])] = true
		if g.can_start_run():
			var from := 0
			var bot := RootBot.new()
			if style == "wish":
				from = Diary.newest_tip(g.roots)
				var glows := g.wish_glows()
				if not glows.is_empty():
					bot.goal = glows[0]["center"]
					bot.goal_radius = float(glows[0]["radius"]) * 0.5
			else:
				var starter := RootBot.new()
				starter.start_by_need = true
				from = starter.pick_start(g.roots, g.ground, g.sim.resources)
			var start := g.roots.graph.positions[from]
			g.roots.pace_run(lf)
			var dists: Array = []
			reach_n.append(in_straight_reach(g, start, lf, g.roots.run_cost_scale, dists))
			g.start_run(from)
			g.roots.pace_run(lf)
			var guard := 0
			while guard < 30000:
				guard += 1
				if not g.steer(bot.stick_for(g.roots, g.ground, g.sim.resources), false, FRAME):
					break
			var hit := {}
			for i in g.roots._run_touched:
				if int(i) < rich.size() and rich[int(i)] >= 0:
					hit[rich[int(i)]] = true
			touched_n.append(hit.size())
			var end := g.roots.tip_position
			disp.append(start.distance_to(end))
			drive_n.append(dists.filter(func(d: float) -> bool: return d <= disp[-1]).size())
			far_tip.append(Vector2(end.x, end.z).length())
			for pid in far_wishes:
				if int(far_wishes[pid]["reached"]) < 0 and g.diary.wish_reached and g.diary.wish_patch == pid:
					far_wishes[pid]["reached"] = dn
			if verbose:
				print("  d%d lf %.0f start %.1f m out, root %.0f m, straight %.1f m, tip %.1f m out, rich touched %d, in straight reach %d, wish %s%s" % [dn, lf, Vector2(start.x, start.z).length(), g.roots.run_length, disp[-1], far_tip[-1], hit.size(), reach_n[-1], g.diary.wish_patch, " far" if Diary.is_far(g.ground, g.diary.wish_patch) else ""])
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(0.5)
		if g.finished:
			finish = dn
			break
	var nights_to := []
	var never := 0
	for pid in far_wishes:
		var w: Dictionary = far_wishes[pid]
		if int(w["reached"]) < 0:
			never += 1
		else:
			nights_to.append(int(w["reached"]) - int(w["day"]) + 1)
	var kinds := near_week_kinds.keys()
	kinds.sort()
	print("FIELD %-8s seed %-3d style %s finish %s | rich patches touched a night: median %s max %s (%s) | in straight reach of tonight's tank from the start: median %s max %s | within tonight's straight drive: median %s max %s | straight metres from start: median %.0f max %.0f | tip from trunk max %.0f m | wish days far %d/%d (%.0f%%), 12 m or more from the trunk %d | far wishes %d: nights to reach %s, not reached %d | near-ring kinds reachable in week 1: %s, every kind within a calm night on %d of the first 7 nights | B1 scarcest kind within one calm night %d/%d, within two %d/%d" % [
		sp, seed, style, str(finish) if finish > 0 else "never", _median(touched_n), _max(touched_n), _hist(touched_n), _median(reach_n), _max(reach_n), _median(drive_n), _max(drive_n),
		_median(disp), _max(disp), _max(far_tip), far_days, wish_days, 100.0 * far_days / maxf(wish_days, 1), dist_days, far_wishes.size(), str(nights_to), never, str(kinds), week_all, b1_one, nights, b1_two, nights])


static func _median(a: Array) -> float:
	if a.is_empty(): return -1.0
	var b := a.duplicate()
	b.sort()
	return float(b[b.size() / 2])


static func _max(a: Array) -> float:
	var m := -1.0
	for x in a: m = maxf(m, float(x))
	return m


static func _hist(a: Array) -> String:
	var h := {}
	for x in a: h[int(x)] = int(h.get(int(x), 0)) + 1
	var keys := h.keys()
	keys.sort()
	var out := []
	for k in keys: out.append("%dx%d" % [h[k], k])
	return " ".join(out)
