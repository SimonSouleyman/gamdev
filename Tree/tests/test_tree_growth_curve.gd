extends RefCounted
## 0.6.2, the tree rebuilt: the growth curve (a whip first, one leader, height after the
## species' curve, the crown lifting), the leaf masses and the camera framing by real height.
var t


## Plays `days` days with the root bot (calm days), like tools/month_report.gd.
func _play(species_id: String, days: int, seed: int = 14) -> GameState:
	var g := GameState.new_game(seed, species_id)
	for _day in range(days):
		g.dive()
		g.start_run(0 if g.roots.graph.size() <= 1 else g.roots.graph.size() - 1)
		var bot := RootBot.new()
		var guard := 0
		while g.steer(bot.stick_for(g.roots, g.ground), false, 1.0 / 30.0) and guard < 20000:
			guard += 1
		while g.phase == GameState.Phase.NIGHT:
			g.tick(0.25)
		while g.phase == GameState.Phase.DAY:
			g.tick(1.0)
	return g


func test_game_trees_follow_the_curve_the_bare_sim_does_not() -> void:
	t.check(GameState.new_game(3).sim.natural_form, "a game tree follows the growth curve")
	t.check(not GrowthSim.new(3).natural_form, "the bare simulation (forest trees) does not")
	var g := GameState.new_game(3)
	var back := GrowthSim.from_dict(g.sim.to_dict())
	t.check(back.natural_form, "saved with the tree")


func test_a_young_tree_is_a_thin_whip() -> void:
	var g := _play("linden", 2)
	var sim := g.sim
	t.check(sim.height() > 0.8 and sim.height() < 3.0, "about a metre a day at first (%.1f m after two days)" % sim.height())
	t.check(sim.height() <= sim.target_height() + 0.6, "never above the curve (%.1f m, curve %.1f m)" % [sim.height(), sim.target_height()])
	# One leader: the top of the tree stands almost straight above the foot.
	var top := Vector3.ZERO
	for p in sim.graph.positions:
		if p.y > top.y:
			top = p
	t.check(Vector2(top.x, top.z).length() < top.y * 0.35, "one upright leader (%s)" % top)
	t.check(sim.graph.radii[1] * TreeView.wood_scale(sim.height()) < 0.06, "a thin stem, not a pole (%.3f m)" % (sim.graph.radii[1] * TreeView.wood_scale(sim.height())))


func test_height_follows_the_species_curve() -> void:
	var sim := GrowthSim.new(1)
	sim.natural_form = true
	sim.clock.day_count = 1
	var early := sim.target_height()
	sim.clock.day_count = 8
	var week := sim.target_height()
	sim.clock.day_count = 31
	var month := sim.target_height()
	t.check(early < 1.5, "a sapling on the first day (%.1f m)" % early)
	t.check(week > 5.0 and week < 11.0, "a young tree after a week (%.1f m)" % week)
	t.check(month > 0.8 * sim.species.max_height and month <= sim.species.max_height, "near the species height after its month (%.1f m)" % month)
	# The pace ramps up: a seedling adds a few shoots, a grown crown many.
	sim.clock.day_count = 1
	var slow := sim.max_pace()
	sim.clock.day_count = 25
	t.check(sim.max_pace() > slow * 2.0, "the pace rises as the tree grows")


func test_the_crown_lifts_and_shed_wood_counts_as_grown() -> void:
	var sim := GrowthSim.new(2)
	sim.natural_form = true
	# A 10 m trunk with a small side branch at 1 m.
	# Node 1 is the seedling's stub at 0.15 m: the trunk grows on from it.
	var prev := 1
	for i in range(1, 21):
		prev = sim.graph.add_node(prev, Vector3(0, i * 0.5, 0))
	# A few twigs at the top, so the trunk is thicker than the side branch.
	for k in range(5):
		sim.graph.add_node(prev, Vector3(0.2 * cos(k), 10.2, 0.2 * sin(k)))
	var low := sim.graph.add_node(3, Vector3(0.4, 1.1, 0))
	sim.graph.add_node(low, Vector3(0.8, 1.2, 0))
	sim.graph.update_radii()
	var grown := sim.grown_nodes()
	t.check(sim.crown_base() > 1.5, "a 10 m tree stands on a bare trunk (%.1f m)" % sim.crown_base())
	var shed := sim.shed_lower_branches()
	t.check_eq(shed, 2, "the low side branch is shed")
	t.check(sim.graph.get_flag(low, "dead", false), "it is gone from the tree")
	t.check_eq(sim.grown_nodes(), grown, "shed wood still counts toward the finished tree")
	t.check(not sim.graph.get_flag(10, "dead", false), "the trunk stays")


func test_a_full_height_crown_keeps_room_to_finish() -> void:
	var sim := GrowthSim.new(4)
	sim.natural_form = true
	var r_young := sim.crown_radius(sim.species.max_height)
	for i in range(1, sim.species.finish_nodes):
		sim.graph.add_node(0, Vector3(0, 0.1, 0))
	t.check(sim.crown_radius(sim.species.max_height) > r_young, "a full crown widens past the species radius")


func test_leaf_masses_at_the_twig_ends() -> void:
	var g := _play("linden", 10)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = CrownSprays.card_mesh()
	HeroCrown.populate(mm, g.sim, g.seed)
	t.check(mm.instance_count > 0 and mm.instance_count <= HeroCrown.BUDGET * 1.1, "sprays within the budget (%d)" % mm.instance_count)
	t.check(HeroCrown.last_masses * 3 <= mm.instance_count, "gathered into masses (%d sprays in %d masses)" % [mm.instance_count, HeroCrown.last_masses])
	# No leaves on the bare trunk below the crown, all leafy nodes near a shoot tip.
	var base := g.sim.crown_base()
	for id in HeroCrown.leafy_nodes(g.sim):
		if g.sim.graph.positions[id].y < base:
			t.check(false, "a leafy node below the crown base")
			return
	# Deterministic: the same tree gives the same crown.
	var first := mm.get_instance_transform(0)
	HeroCrown.populate(mm, g.sim, g.seed)
	t.check_eq(mm.get_instance_transform(0), first, "the same crown every rebuild")


func test_camera_frames_the_real_height() -> void:
	t.check(TreeView.frame_height(0.5) > 3.0, "a sapling stands small in a frame of a few metres")
	t.check(TreeView.frame_height(0.5) / 0.5 > TreeView.frame_height(8.0) / 8.0, "a young tree fills less of the frame than an older one")
	t.check(absf(TreeView.frame_height(25.0) - 25.0) < 0.01, "a grown tree is framed by its own height")
