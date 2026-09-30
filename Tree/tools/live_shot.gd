extends SceneTree
## The live picture on a PC (specs/0.8.md section 6): grows a tree with the root bot, renders its
## layers with LiveExport (as the game does on the phone), then photographs the phone's animation
## through LivePreview at several real times of day, and a few wind frames.
## Run: godot --path . -s tools/live_shot.gd -- --shots=C:/some/folder [--days=12] [--seed=42]
##   [--species=linden] [--date=2026-06-21] [--moon=0.5] [--size=540x1200]
## --fallback: also writes the layers of a young sapling in summer green into the TreeLive library's assets
## (android_plugin/treelive/src/main/assets/live_fallback), the picture shown before any save.
## Needs a window (the layers are rendered).

var shots_dir := ""
var days := 12
var seed := 42
var species := "linden"
var screen := Vector2(540, 1200)
var fallback := false
var view: TreeView
var export: LiveExport
var game: GameState

const FALLBACK_DIR := "res://android_plugin/treelive/src/main/assets/live_fallback"


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a.begins_with("--days="):
			days = int(a.substr(7))
		elif a.begins_with("--seed="):
			seed = int(a.substr(7))
		elif a.begins_with("--species="):
			species = a.substr(10)
		elif a.begins_with("--size="):
			var p := a.substr(7).split("x")
			screen = Vector2(float(p[0]), float(p[1]))
		elif a == "--fallback":
			fallback = true
	Almanac.read_cmdline()
	DirAccess.make_dir_recursive_absolute(shots_dir)
	_run.call_deferred()


static func grow(p_seed: int, p_species: String, p_days: int) -> GameState:
	var g := GameState.new_game(p_seed, p_species)
	for day in range(p_days):
		g.dive()
		var start := 0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1
		g.start_run(start)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		if day < p_days - 1:
			while g.phase == GameState.Phase.DAY:
				g.tick(0.5)
	# Mid-morning of the last day.
	while g.sim.clock.time_of_day < g.sim.clock.daylight_fraction * 0.3 and g.phase == GameState.Phase.DAY:
		g.tick(0.5)
	g.take_events()
	return g


func _run() -> void:
	game = grow(seed, species, days)
	print("day %d: %d nodes, %.1f m" % [game.day_number(), game.sim.graph.size(), game.sim.height()])
	view = TreeView.new()
	root.add_child(view)
	await process_frame
	view.setup(game)
	view.set_hud_visible(false)
	export = LiveExport.new()
	root.add_child(export)
	for i in range(4):
		await process_frame
	var dir := shots_dir.path_join("layers")
	export.dir = dir
	var t0 := Time.get_ticks_msec()
	var ok := await export.refresh(view, game, true)
	print("layers written: %s (%d ms)" % [ok, Time.get_ticks_msec() - t0])
	if not ok:
		quit(1)
		return
	print(JSON.stringify(LivePicture.read_meta(dir)))
	if fallback:
		await _write_fallback()
	await _photograph(dir)
	quit(0)


## The picture before any save: a young sapling (a few days of growth) in the same light.
func _write_fallback() -> void:
	# In its summer green, whatever the date (it may be seen in any season before the first save).
	var season := Almanac.season_override
	Almanac.season_override = "summer"
	var young := grow(seed, "linden", 3)
	view.setup(young)
	for i in range(4):
		await process_frame
	view.apply_season()
	var abs_dir := ProjectSettings.globalize_path(FALLBACK_DIR)
	export.dir = abs_dir
	var ok := await export.refresh(view, young, true)
	print("fallback written to %s: %s" % [abs_dir, ok])
	Almanac.season_override = season
	view.setup(game)
	for i in range(4):
		await process_frame


func _photograph(dir: String) -> void:
	var p := LivePreview.new()
	p.size = screen
	p.load_from(dir)
	var sv := SubViewport.new()
	sv.size = Vector2i(screen)
	sv.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(sv)
	sv.add_child(p)
	# Real moments on the date (Almanac --date=, else today): the utc offset of German summer
	# or winter time.
	var today := Almanac.today()
	var doy := Almanac.day_of_year(today["month"], today["day"])
	var offset := 2.0 if doy > 88 and doy < 300 else 1.0
	var times := LivePicture.sun_times(doy, offset)
	print("sunrise %.2f, sunset %.2f (day %d, UTC+%d)" % [times["rise"], times["set"], doy, int(offset)])
	var moments := {"a_before_dawn": float(times["rise"]) - 0.6, "b_sunrise": float(times["rise"]) + 0.2,
		"c_morning": float(times["rise"]) + 2.5, "d_noon": float(times["noon"]), "e_golden": float(times["set"]) - 0.7,
		"f_dusk": float(times["set"]) + 0.4, "g_night": fposmod(float(times["set"]) + 3.0, 24.0)}
	for name in moments:
		p.day_of_year = doy
		p.hour = moments[name]
		p.utc_offset = offset
		p.unix = Almanac.now()
		p.t = 3.0
		p.queue_redraw()
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		sv.get_texture().get_image().save_png(shots_dir.path_join("live_%s.png" % name))
	# Wind: five frames a second apart at noon (the crown sways, the clouds drift).
	for i in range(5):
		p.hour = float(times["noon"])
		p.t = 10.0 + i * 1.3
		p.queue_redraw()
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		sv.get_texture().get_image().save_png(shots_dir.path_join("wind_%d.png" % i))
