extends SceneTree
## Root mode with the look-test rocks: a night after a few days, the camera turned towards the
## nearest rock. Run: godot --path . -s lookdev/rocks/roots_shot.gd -- --shots=<folder> [--before]

var shots_dir := ""
var before := false
var frame := 0
var rv: RootView
var game: GameState


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a == "--before":
			before = true
	DirAccess.make_dir_recursive_absolute(shots_dir)
	game = GameState.new_game(42)
	game.dive()
	rv = RootView.new()
	root.add_child(rv)


func _process(_delta: float) -> bool:
	frame += 1
	if frame == 1:
		rv.setup(game.ground, game.roots, game.sim.resources)
		rv.hud.visible = false
		if not before:
			RockLook.apply_roots(rv)
	if frame >= 3:
		# Look at the rock nearest the trunk, from a little way off.
		var best := Vector3.ZERO
		var best_d := INF
		for i in range(game.ground.rock_centers.size()):
			var d := game.ground.rock_centers[i].length()
			if d < best_d:
				best_d = d
				best = game.ground.rock_centers[i]
		rv._look = best
		rv._orbit_distance = 4.5
	if frame == 30:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("roots_%s.png" % ("before" if before else "after")))
		quit()
	return false
