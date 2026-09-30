extends SceneTree
## Photographs the day's wish underground (0.7): the glow from the overview, far off at the edge
## of the fog during a run, close up, the moment it is reached, and the diary line with its ink
## drawing. Plays the first night headless-style on GameState, then shows the second night in the
## real scene. Needs a window. Never touches the player's save.
## Run: godot --path . -s tools/wish_shot.gd -- --shots=C:/some/folder [--seed=14]

var main: Node
var shots_dir: String = ""
var seed: int = 14
var stage: int = 0
var stage_time: float = 0.0
var bot := RootBot.new()
var glow: Dictionary = {}
var reached_at: float = -1.0

const FRAME: float = 1.0 / 30.0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
	if shots_dir != "":
		DirAccess.make_dir_recursive_absolute(shots_dir)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(name: String) -> void:
	print("shot ", name)
	if shots_dir == "":
		return
	RenderingServer.force_draw(false)
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join(name + ".png"))


## A game at the second (or a later) sunset whose wish points underground.
func _prepare() -> GameState:
	for s in range(seed, seed + 40):
		var g := GameState.new_game(s)
		for page in ["planted", "first_sunset", "first_night", "pick", "first_run_done", "sapling", "spent", "empty_night"]:
			g.seen_pages[page] = true
		g.dive()
		g.start_run(0)
		while g.steer(Vector2(0.3, 0.05), false, FRAME):
			pass
		while g.phase != GameState.Phase.DAY:
			g.tick(0.5)
		if g.diary.wish_patch < 0:
			continue
		g.skip_time(1.0)
		g.dive()
		g.take_events()
		g.sim.resources.life_force = g.roots.calm_life_force
		print("seed %d: %s" % [s, g.diary.wish])
		return g
	return null


func _process(delta: float) -> bool:
	if not has_meta("started"):
		set_meta("started", true)
		var g := _prepare()
		if g == null:
			print("no seed with a wish underground")
			quit(1)
			return false
		main.start(g)
		glow = g.wish_glows()[0]
		return false
	stage_time += delta
	var s: GameState = main.state
	var rv: RootView = main.root_view
	if main.journal.current_page() != "":
		main.journal.close_page()
	match stage:
		0:
			if stage_time > 3.0:
				_shot("01_overview_glow")
				# Start far from the glow (the trunk), facing it, so it first shows at the fog's edge.
				var far := 0
				rv.start_at(far)
				s.roots.heading = ((glow["center"] as Vector3) - s.roots.tip_position).normalized()
				s.roots.heading.y = clampf(s.roots.heading.y, -0.5, 0.2)
				s.roots.heading = s.roots.heading.normalized()
				bot.goal = glow["center"]
				bot.goal_radius = float(glow["radius"]) * 0.5
				_next()
		1:
			rv.scripted_stick = bot.stick_for(s.roots, s.ground, s.sim.resources) if s.roots.run_active else null
			# The root waits for the first move; a stick already pointing at the glow is too small.
			rv.scripted_dive = bool(rv.get("_waiting_for_input"))
			if stage_time > 90.0:
				print("timed out steering")
				_next()
			var d := s.roots.tip_position.distance_to(glow["center"])
			if stage_time > 1.2 and not has_meta("far"):
				set_meta("far", true)
				print("far shot at %.1f m" % d)
				_shot("02_run_glow_far")
			if d < float(glow["radius"]) + 2.2 and not has_meta("near"):
				set_meta("near", true)
				print("near shot at %.1f m" % d)
				_shot("03_run_glow_near")
			if s.diary.wish_reached or rv.get("_wish_glows").size() > 0 and rv.get("_wish_glows")[0]["reached"]:
				if reached_at < 0.0:
					reached_at = stage_time
			if reached_at >= 0.0 and stage_time - reached_at > 0.45:
				_shot("04_reached_swell")
				_next()
			if not s.roots.run_active and reached_at < 0.0 and stage_time > 2.0:
				print("the run ended before reaching the glow")
				_next()
		2:
			rv.scripted_stick = bot.stick_for(s.roots, s.ground, s.sim.resources) if s.roots.run_active else null
			if stage_time > 1.6 and not has_meta("after"):
				set_meta("after", true)
				_shot("05_reached_after")
			if s.phase != GameState.Phase.NIGHT or (s.night_done and not rv.is_settling() and stage_time > 3.0):
				_next()
			elif stage_time > 60.0:
				_next()
		3:
			if stage_time > 0.5:
				main.journal.open_diary()
				_next()
		4:
			if stage_time > 1.0:
				_shot("06_diary_line")
				for e in s.diary.entries:
					if e.has("drawing"):
						print("diary: [%s] %s" % [e["drawing"], e["text"]])
				quit(0)
	return false


func _next() -> void:
	stage += 1
	stage_time = 0.0
