extends RefCounted
## 0.6 bonsai mode (design doc section 16): growth on the same clock, the pot's cap, water and
## fertiliser as soft failures, turning the pot, pinching, wire (set and bite), the shears,
## repotting, save and load.
var t


## A bonsai at mid-morning, well watered and fed.
func _bonsai(seed: int = 3, species_id: String = "juniper") -> BonsaiSim:
	var b := BonsaiSim.starter(seed) if species_id == "juniper" else BonsaiSim.cutting(seed, species_id)
	b.clock.time_of_day = b.clock.daylight_fraction * 0.3
	return b


## Care as a player would give it: water when dry, a pellet of what runs low, repot when asked.
func _care_days(b: BonsaiSim, days: int) -> void:
	var end := b.day() + days
	while b.day() < end:
		if b.moisture < 0.35:
			b.water()
		for k in range(3):
			if b.soil[k] < 0.3:
				b.fertilise(k)
		if b.repot_due:
			b.repot(b.pot, 0.5)
		b.advance(4.0)


func test_starter_is_a_young_juniper() -> void:
	var b := _bonsai()
	t.check_eq(b.species.id, "juniper", "the starter is the juniper")
	t.check(b.species.conifer and b.species.deadwood, "the one conifer, with deadwood")
	t.check(not Species.ORDER.has("juniper"), "never planted on the clearing")
	t.check(b.leafy_count() >= 90 and b.leafy_count() < 140, "a young nursery plant: %d" % b.leafy_count())
	t.check_eq(b.pot, "nursery", "in the clay nursery pot")
	t.check(b.height() > 1.0 and b.height() < BonsaiSim.MAX_HEIGHT, "small: %.2f units" % b.height())
	t.check(not b.milestones.is_empty(), "its album page starts with the day it came")


func test_grows_by_day_not_by_night() -> void:
	var b := _bonsai(4)
	var n := b.leafy_count()
	b.advance(30.0)
	t.check(b.leafy_count() > n, "grows in the morning light: %d -> %d" % [n, b.leafy_count()])
	b.clock.time_of_day = b.clock.daylight_fraction + 0.05
	var night := b.leafy_count()
	b.advance(30.0)
	t.check_eq(b.leafy_count(), night, "no growth at night")


func test_follows_the_tree_clock() -> void:
	var g := GameState.new_game(21)
	g.phase = GameState.Phase.DAY
	g.sim.clock.time_of_day = 0.1
	var b := g.ensure_bonsai(true)
	t.check(b != null, "the test switch unlocks the bonsai")
	g.tick(0.1)
	t.check_near(b.clock.time_of_day, g.sim.clock.time_of_day, 1e-4, "the same hour as the tree")
	var n := b.leafy_count()
	for _i in range(300):
		g.tick(0.1)
	t.check_near(b.clock.time_of_day, g.sim.clock.time_of_day, 1e-4, "still the same hour")
	t.check(b.leafy_count() > n, "the day on the clearing grows the bonsai too")
	t.check_eq(g.sim.resources.life_force, g.sim.resources.life_force, "")
	var before := g.sim.resources.stock.duplicate()
	b.water()
	b.fertilise(0)
	t.check_eq(g.sim.resources.stock, before, "independent: the bonsai's care costs the tree nothing")


func test_locked_until_the_first_finished_tree() -> void:
	var g := GameState.new_game(22)
	t.check(not g.bonsai_unlocked(), "locked at the start")
	t.check(g.ensure_bonsai() == null, "no bonsai while locked")
	g.grove.append({"species": "linden", "days": 30, "seed": 1})
	t.check(g.bonsai_unlocked(), "unlocked by a finished tree")
	t.check(g.ensure_bonsai() != null, "the juniper comes")
	t.check(g.bonsai_choices().has("linden") and g.bonsai_choices().has("juniper"), "a linden cutting joins the juniper")
	t.check(not g.bonsai_choices().has("oak"), "no oak cutting yet")
	t.check_eq(g.bonsai_choices(true).size(), 7, "the test switch offers every cutting")


func test_finishing_a_tree_brings_the_juniper() -> void:
	var g := GameState.new_game(23)
	g._finish()
	t.check(g.bonsai != null and g.bonsai.species.id == "juniper", "the first finished tree unlocks it")
	t.check(g.take_events().has("bonsai"), "the scene hears of it")


func test_pot_caps_the_living_segments() -> void:
	var b := _bonsai(5)
	b.repot("cascade", 0.5)
	_care_days(b, 40)
	t.check(b.leafy_count() <= b.capacity(), "never beyond the pot: %d of %d" % [b.leafy_count(), b.capacity()])
	t.check(b.leafy_count() >= b.capacity() - 30, "and fills it: %d" % b.leafy_count())
	t.check(b.graph.size() <= Budgets.BONSAI_MAX_NODES, "graph budget")
	for pid in BonsaiSim.POTS:
		var cap := int(BonsaiSim.POTS[pid]["nodes"])
		t.check(cap >= Budgets.BONSAI_MIN_POT_NODES and cap <= Budgets.BONSAI_MAX_POT_NODES, "pot %s in 400..600" % pid)


func test_pruning_makes_it_denser_not_bigger() -> void:
	var b := _bonsai(6)
	b.repot("round", 0.5)
	_care_days(b, 30)
	var h := b.height()
	var r := b.crown_radius(h)
	# Cut the outermost branches a few times and let it grow back.
	for _round in range(4):
		var best := -1
		var best_d := 0.0
		for id in range(3, b.graph.size()):
			var p := b.graph.positions[id]
			var d := Vector2(p.x, p.z).length()
			if b.can_prune(id) and b.subtree_leafy(id) >= 8 and d > best_d:
				best_d = d
				best = id
		if best >= 0:
			t.check(b.prune(best) > 0, "a cut takes wood away")
		_care_days(b, 3)
	t.check(b.height() <= BonsaiSim.MAX_HEIGHT + BonsaiSim.STEP * 1.5, "no taller than the pot allows: %.2f" % b.height())
	t.check(b.crown_radius(b.height()) <= r + 0.3, "no wider")
	t.check(b.leafy_count() <= b.capacity(), "within the pot")


func test_water_dries_and_soft_failure() -> void:
	var b := _bonsai(7)
	b.moisture = 0.6
	b.advance(60.0)
	t.check(b.moisture < 0.6, "the soil dries over the day: %.2f" % b.moisture)
	b.moisture = 0.02
	t.check(b.droop() > 0.8, "too dry: the leaves droop")
	t.check(b.moisture_factor() > 0.0 and b.moisture_factor() < 0.5, "dry slows growth, never stops it")
	var n := b.leafy_count()
	b.advance(60.0)
	t.check(b.leafy_count() >= n, "nothing dies of thirst")
	b.water()
	b.water()
	b.water()
	t.check(b.is_too_wet(), "too much water: wet")
	t.check_near(b.moisture_factor(), BonsaiSim.WET_FACTOR, 1e-4, "too wet slows growth")
	b.moisture = 0.5
	t.check_near(b.moisture_factor(), 1.0, 1e-4, "just right")
	t.check_near(b.droop(), 0.0, 1e-4, "no droop when moist")


func test_fertiliser_soft_liebig_and_burn() -> void:
	var b := _bonsai(8)
	b.soil = PackedFloat32Array([0.0, 0.5, 0.5])
	t.check_near(b.nutrient_factor(), 0.35, 1e-4, "no nitrogen: the soft Liebig floor, not zero")
	b.fertilise(0)
	t.check(b.nutrient_factor() > 0.35, "a spoon of nitrogen helps")
	b.soil = PackedFloat32Array([0.5, 0.5, 0.5])
	t.check_eq(b.fertilise(1), 0, "one spoon does not burn")
	var burnt := b.fertilise(1)
	t.check(burnt > 0, "too much burns a few tips: %d" % burnt)
	t.check(burnt <= 8, "only a little")
	var tips := 0
	for id in range(b.graph.size()):
		if b.graph.get_flag(id, "burnt") != null:
			tips += 1
			t.check(b.graph.get_flag(id, "rest", false), "a burnt tip rests")
			t.check(not b.is_dead(id), "and does not die")
	t.check_eq(tips, burnt, "the burnt tips are marked")
	for _d in range(BonsaiSim.BURN_DAYS):
		b._new_day_passed()
	for id in range(b.graph.size()):
		t.check(b.graph.get_flag(id, "burnt") == null, "burnt tips recover after a few days")


func test_turning_the_pot_grows_the_other_side() -> void:
	var sides: Array[float] = []
	for turn in [0, 2]:
		var b := _bonsai(9)
		b.turn_pot(turn)
		var before := b.graph.size()
		_care_days(b, 3)
		var z := 0.0
		for id in range(before, b.graph.size()):
			z += b.graph.positions[id].z
		sides.append(z / maxf(1.0, b.graph.size() - before))
	t.check(sides[0] > 0.1, "the side facing the window grows (%.2f)" % sides[0])
	t.check(sides[1] < sides[0] - 0.2, "turned half round, the new growth moves to the other side (%.2f)" % sides[1])
	var c := _bonsai(9)
	c.turn_pot(1)
	c.turn_pot(1)
	c.turn_pot(1)
	c.turn_pot(1)
	t.check_eq(c.turn, 0, "four quarters make a full turn")
	c.turn_pot(-1)
	t.check_eq(c.turn, 3, "and back")


func test_pinching_stops_a_fresh_tip() -> void:
	var b := _bonsai(10)
	b.advance(20.0)
	var fresh := -1
	for id in b.living_tips():
		if b.is_fresh_tip(id):
			fresh = id
			break
	t.check(fresh >= 0, "a fresh tip to pinch")
	var p := b.graph.positions[fresh]
	t.check(b.pinch(fresh), "pinched")
	t.check(not b.pinch(fresh), "not twice")
	var id_at := fresh
	_care_days(b, 2)
	t.check(b.graph.positions[id_at] == p and b.graph.get_flag(id_at, "pinched", false), "the pinched tip stays where it is")
	t.check(b.graph.children[id_at].is_empty(), "and grows no further")
	# An old tip is no longer fresh.
	for _d in range(3):
		b._new_day_passed()
	for id in b.living_tips():
		if b.graph.ages[id] > BonsaiSim.FRESH_DAYS:
			t.check(not b.pinch(id), "an old tip cannot be pinched")
			break


func test_wire_sets_and_bites() -> void:
	var b := _bonsai(11)
	var id := _side_branch(b)
	t.check(id >= 0, "a side branch to wire")
	var start := b.graph.direction_of(id)
	var target := (start + Vector3(0, -1.2, 0)).normalized()
	t.check(b.wire(id, target), "wired")
	t.check(not b.wire(id, target), "one wire per branch")
	var now := b.graph.direction_of(id)
	t.check(now.angle_to(target) < start.angle_to(target) * 0.6, "the wire bends it half way at once")
	for _d in range(BonsaiSim.WIRE_SET_DAYS):
		b._new_day_passed()
	t.check_near(b.wire_set(id), 1.0, 1e-4, "set after a few days")
	t.check(b.graph.direction_of(id).angle_to(target) < now.angle_to(target), "the branch moved on toward the new line")
	t.check_near(b.scar(id), 0.0, 1e-4, "no scar yet")
	for _d in range(BonsaiSim.WIRE_BITE_DAYS - BonsaiSim.WIRE_SET_DAYS + 2):
		b._new_day_passed()
	t.check(b.scar(id) > 0.0, "left on too long it bites in")
	var set_dir := b.graph.direction_of(id)
	t.check(b.unwire(id), "a tap removes the wire")
	t.check(b.wired().is_empty(), "no wire left")
	t.check(b.graph.direction_of(id).angle_to(set_dir) < 1e-3, "a set branch keeps its line")
	t.check(b.scar(id) > 0.0, "the scar stays")
	t.check(not b.is_dead(id), "never fatal")


func test_wire_removed_early_springs_back() -> void:
	var b := _bonsai(12)
	var id := _side_branch(b)
	var start := b.graph.direction_of(id)
	var target := (start + Vector3(0, -1.2, 0)).normalized()
	b.wire(id, target)
	var bent := b.graph.direction_of(id)
	b.unwire(id)
	var back := b.graph.direction_of(id)
	t.check(back.angle_to(start) < bent.angle_to(start), "taken off at once it springs back part of the way")


func _side_branch(b: BonsaiSim) -> int:
	var trunk := b.trunk_chain()
	for id in range(2, b.graph.size()):
		if not trunk.has(id) and trunk.has(b.graph.parents[id]) and b.subtree_leafy(id) >= 3 and b.can_prune(id):
			return id
	return -1


func test_shears_a_third_and_juniper_deadwood() -> void:
	var b := _bonsai(13)
	var limit := b.prune_limit()
	t.check_eq(limit, int(b.leafy_count() / 3.0), "a third of the tree at most")
	t.check(not b.can_prune(0) and not b.can_prune(1), "never the trunk base")
	var big := -1
	for id in range(2, b.graph.size()):
		if b.subtree_leafy(id) > limit:
			big = id
			break
	t.check(big >= 0 and not b.can_prune(big), "a cut over a third is refused")
	t.check_eq(b.prune(big), 0, "and cuts nothing")
	var id := _side_branch(b)
	var pos := b.graph.positions[id]
	var before := b.leafy_count()
	var cut := b.prune(id)
	t.check(cut > 0 and b.leafy_count() == before - cut, "the cut wood is gone (%d)" % cut)
	var jin := -1
	for n in range(b.graph.size()):
		if b.is_jin(n) and b.graph.positions[n] == pos:
			jin = n
	t.check(jin >= 0, "the juniper keeps the stub as a silver jin")
	t.check(b.graph.get_flag(jin, "rest", false), "deadwood never grows")
	var shari := false
	for n in b.trunk_chain():
		if float(b.graph.get_flag(n, "shari", 0.0)) > 0.0:
			shari = true
	t.check(shari, "a cut at the trunk strips a line of shari below it")
	for n in range(b.graph.size()):
		t.check(n == 0 or not b.is_dead(n), "cut wood leaves the graph")


func test_broadleaf_cutting_has_no_deadwood() -> void:
	var b := _bonsai(14, "linden")
	t.check_eq(b.species.id, "linden", "a linden cutting")
	t.check(b.leafy_count() < 40, "starts as a small cutting: %d" % b.leafy_count())
	_care_days(b, 4)
	var id := _side_branch(b)
	if id >= 0:
		b.prune(id)
	for n in range(b.graph.size()):
		t.check(not b.is_jin(n), "no jin on a linden")


func test_repot_every_seventh_day() -> void:
	var b := _bonsai(15)
	b.moisture = 0.6
	for _d in range(BonsaiSim.REPOT_DAYS - 1):
		b._new_day_passed()
	t.check(not b.repot_due, "not yet after six days")
	b._new_day_passed()
	t.check(b.repot_due, "the seventh day asks for repotting")
	t.check(b.take_events().has("repot_due"), "the scene hears of it")
	for _d in range(3):
		b._new_day_passed()
	t.check(b.rootbound_factor() < 1.0 and b.rootbound_factor() >= BonsaiSim.ROOTBOUND_FLOOR, "left root bound it grows slower, never stops")
	b.soil = PackedFloat32Array([0.05, 0.05, 0.05])
	b.repot("oval", 0.6)
	t.check(not b.repot_due, "repotted")
	t.check_eq(b.pot, "oval", "into the chosen pot")
	t.check_eq(b.capacity(), 500, "its volume caps the tree")
	t.check_near(b.soil[0], BonsaiSim.FRESH_SOIL, 1e-4, "fresh soil")
	t.check(b.root_fill < 0.3, "trimmed roots have room")
	t.check_near(b.rootbound_factor(), 1.0, 1e-4, "grows freely again")
	for _d in range(BonsaiSim.REPOT_DAYS):
		b._new_day_passed()
	t.check(b.repot_due, "and a week later again")


func test_save_and_load_round_trip() -> void:
	var b := _bonsai(16)
	_care_days(b, 3)
	var id := _side_branch(b)
	b.wire(id, Vector3(1, -0.3, 0))
	b.turn_pot(1)
	b.fertilise(2)
	b._new_day_passed()
	var d: Dictionary = JSON.parse_string(JSON.stringify(b.to_dict()))
	var c := BonsaiSim.from_dict(d)
	t.check_eq(c.graph.size(), b.graph.size(), "the graph")
	t.check_eq(c.leafy_count(), b.leafy_count(), "the living wood")
	t.check(c.graph.positions[id].is_equal_approx(b.graph.positions[id]), "positions")
	t.check_eq(c.wired(), b.wired(), "the wire")
	t.check_eq(c.wire_days(c.wired()[0]), b.wire_days(b.wired()[0]), "wire days")
	t.check_eq(c.turn, 1, "the pot's turn")
	t.check_near(c.moisture, b.moisture, 1e-4, "soil moisture")
	t.check_near(c.soil[2], b.soil[2], 1e-4, "fertiliser in the soil")
	t.check_eq(c.day(), b.day(), "care days")
	t.check_eq(c.milestones.size(), b.milestones.size(), "the album lines")
	t.check_eq(str(c.rng.state), str(b.rng.state), "the rng state")
	# Both grow on identically after the load.
	b.advance(30.0)
	c.advance(30.0)
	t.check_eq(c.graph.size(), b.graph.size(), "grows on the same after a load")


func test_game_save_keeps_the_bonsai() -> void:
	var g := GameState.new_game(31)
	g.ensure_bonsai(true)
	g.bonsai.water()
	g.swap_bonsai("linden", true)
	t.check_eq(g.bonsai.species.id, "linden", "a linden cutting on the sill")
	t.check(g.bonsai_resting.has("juniper"), "the juniper rests on the shelf")
	var path := "user://test_bonsai_save.json"
	SaveData.save_game(g, path)
	var loaded := SaveData.load_game(path, Time.get_unix_time_from_system())
	t.check(loaded != null and loaded.bonsai != null, "the bonsai is in the same save")
	t.check_eq(loaded.bonsai.species.id, "linden", "the one on the sill")
	t.check_eq(loaded.bonsai.graph.size(), g.bonsai.graph.size(), "its graph")
	t.check(loaded.bonsai_resting.has("juniper"), "the resting juniper")
	t.check(loaded.swap_bonsai("juniper", true), "back to the juniper")
	t.check_eq(loaded.bonsai.species.id, "juniper", "the juniper as it was")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	var next := GameState.new_tree(32, "birch", g)
	t.check(next.bonsai == g.bonsai, "a new tree keeps the lifelong bonsai")


func test_offline_growth() -> void:
	var b := _bonsai(17)
	var n := b.leafy_count()
	var day := b.day()
	b.apply_offline(2.0 * 86400.0)
	t.check(b.leafy_count() > n, "grows while the game is closed")
	t.check_eq(b.day(), day, "the day does not move while closed")


func test_view_draws_pot_wood_foliage_and_wire() -> void:
	var g := GameState.new_game(41)
	g.ensure_bonsai(true)
	var v := BonsaiView.new()
	v._ready()
	v.setup(g)
	t.check(v._wood.mesh != null and v._wood.mesh.get_surface_count() > 0, "the wood is drawn")
	t.check(v._foliage.multimesh.instance_count >= g.bonsai.living_tips().size(), "juniper pads on the twigs")
	t.check_eq(v._shown_pot, "nursery", "in the nursery pot")
	var id := _side_branch(g.bonsai)
	g.bonsai.wire(id, Vector3(1, -0.5, 0))
	v.refresh(true)
	t.check(v._wires.mesh.get_surface_count() > 0, "a copper coil on the wired branch")
	g.bonsai.repot("oval", 0.3)
	v.refresh(true)
	t.check_eq(v._shown_pot, "oval", "the new pot is drawn")
	t.check(BonsaiView.soil_height("oval") > 0.0 and BonsaiView.soil_height("cascade") > BonsaiView.soil_height("oval"), "soil heights by pot")
	g.bonsai.moisture = 0.0
	v.refresh(true)
	t.check(g.bonsai.droop() > 0.9, "dry: the view draws drooping leaves")
	v.free()


func test_style_pages_and_bonsai_pages_exist() -> void:
	for id in BonsaiHud.STYLES:
		t.check(ResourceLoader.exists("res://ui/bonsai_styles/%s.png" % id), "style drawing " + id)
		t.check(BonsaiHud.STYLE_TEXTS.has(id), "style words " + id)
	for id in ["bonsai", "bonsai_water", "bonsai_fertiliser", "bonsai_wire", "bonsai_repot", "bonsai_pinch", "bonsai_shears"]:
		t.check(Pages.has(id), "journal page " + id)
	t.check(ResourceLoader.exists(BonsaiView.JUNIPER_COLOR), "the painted juniper atlas")
	t.check(Shed.ITEMS.has("bonsai") and ShedMenu.TAG_TEXTS.has("bonsai"), "the bonsai is a thing in the shed")
	var b := BonsaiSim.starter(3)
	b._new_day_passed()
	t.check(BonsaiHud.album_lines(b)[0].begins_with("Day 0:"), "the album page starts on day 0")


# --- 0.7: the tools on the windowsill (docs/notes/bonsai-tools-0.7.md) ---------------------

## Bonsai mode in a phone-shaped view (720 x 1600, a 1080 x 2400 screen), the default close-up.
func _sill_view(g: GameState) -> Array:
	var vp := SubViewport.new()
	vp.size = Vector2i(720, 1600)
	vp.disable_3d = false
	t.root.add_child(vp)
	var v := BonsaiView.new()
	vp.add_child(v)
	v.setup(g)
	v.visible = true
	v.active = true
	v.camera.current = true
	v.look_from(0.0, BonsaiView.PITCH, BonsaiView.DIST)
	v._update_camera(0.0)
	return [vp, v]


## Runs the tools' motions to their end (no frames pass in the test runner).
func _finish(v: BonsaiView) -> void:
	for _i in range(6):
		for tw in v.get_tree().get_processed_tweens():
			tw.custom_step(5.0)


func test_sill_tools_are_finger_size_on_a_phone() -> void:
	var g := GameState.new_game(41)
	g.ensure_bonsai(true)
	var made := _sill_view(g)
	var v: BonsaiView = made[1]
	var pts := v.object_screen_points()
	var all := BonsaiTools.HELD + BonsaiTools.TAPPED
	for id in all:
		t.check(pts.has(id), "on screen in the close-up: " + id)
		if not pts.has(id):
			continue
		var p: Vector2 = pts[id]
		t.check(p.x >= 30.0 and p.x <= 690.0 and p.y >= 220.0 and p.y <= 1540.0, "inside the phone's screen, below the status scrap: %s at %s" % [id, p])
		t.check_eq(v.tools.pick(v.camera, p, BonsaiView.TOOL_TAP), id, "a tap on it finds it")
	# Finger size: a tap area about 9 mm across, and no two overlap.
	t.check(BonsaiView.TOOL_TAP * 2.0 >= 100.0, "tap areas at least 100 canvas pixels across")
	for i in range(all.size()):
		for j in range(i + 1, all.size()):
			if pts.has(all[i]) and pts.has(all[j]):
				var d := (pts[all[i]] as Vector2).distance_to(pts[all[j]])
				t.check(d >= BonsaiView.TOOL_TAP * 2.0, "%s and %s apart (%d px)" % [all[i], all[j], int(d)])
	(made[0] as Node).free()


func test_sill_tools_do_what_the_paper_menu_did() -> void:
	var g := GameState.new_game(43)
	g.ensure_bonsai(true)
	var b := g.bonsai
	b.clock.time_of_day = b.clock.daylight_fraction * 0.3
	for _d in range(3):
		b.advance(6.0)
	var made := _sill_view(g)
	var v: BonsaiView = made[1]
	var soil := v.plant_screen_position(0) + Vector2(0, 10)
	t.check(v.on_bonsai(soil), "the soil is on the bonsai")
	t.check(not v.on_bonsai(v.object_screen_points()["pinch"]), "the front row is not")
	# One tap picks a tool up, a tap on it (or its place) puts it down.
	v.tap_object("water")
	t.check_eq(v.tool, "water", "the can is in hand")
	t.check_eq(v.tools.pick(v.camera, v.tools.rest_point(v.camera, "water"), BonsaiView.TOOL_TAP), "water", "its place answers")
	v.tap_object("water")
	t.check_eq(v.tool, "", "put down again")
	_finish(v)
	# Watering.
	v.tap_object("water")
	b.moisture = 0.1
	v.use_at(soil)
	_finish(v)
	t.check(b.moisture > 0.5, "the can waters the soil")
	t.check_eq(v.tool, "water", "and is still in hand")
	# Swapping: tapping the tin puts the can down.
	v.tap_object("fertiliser")
	t.check_eq(v.tool, "fertiliser", "the tin is in hand")
	# 0.8 (C1): no choice made yet, the tin gives what the soil holds least of; two taps.
	var low := b.tin_kind()
	var before := b.soil[low]
	v.pellet_kind = -1
	v.use_at(soil)
	_finish(v)
	t.check(b.soil[low] > before, "no choice on the slip yet: a spoon of what the soil lacks most")
	var k0 := b.soil[2]
	v.pellet_kind = 2
	v.use_at(soil)
	_finish(v)
	t.check(b.soil[2] > k0, "a spoon of K pellets")
	# The tweezers pinch a fresh tip.
	v.tap_object("pinch")
	var trunk := b.trunk_chain()
	var tip := -1
	for id in b.living_tips():
		if b.is_fresh_tip(id) and not trunk.has(id):
			tip = id
			break
	t.check(tip >= 0, "a fresh tip to pinch")
	if tip >= 0:
		v.use_at(v.plant_screen_position(tip))
		t.check(b.graph.get_flag(tip, "pinched", false) == true, "the tweezers pinch it")
	# The carved arrows turn the pot.
	v.set_tool("")
	_finish(v)
	var turn0 := b.turn
	v.tap_object("turn_right")
	_finish(v)
	t.check_eq(b.turn, posmod(turn0 + 1, 4), "the right arrow turns the pot a quarter")
	v.tap_object("turn_left")
	_finish(v)
	t.check_eq(b.turn, turn0, "the left arrow turns it back")
	# The sketchbook, the album card and the box of cuttings open their pages.
	var opened: Array[String] = []
	v.object_tapped.connect(func(id: String) -> void: opened.append(id))
	for id in ["styles", "album", "cuttings"]:
		v.tap_object(id)
	t.check_eq(opened, ["styles", "album", "cuttings"] as Array[String], "the pages open from the sill")
	(made[0] as Node).free()


func test_trowel_repots_only_when_asked() -> void:
	var g := GameState.new_game(44)
	g.ensure_bonsai(true)
	var b := g.bonsai
	var made := _sill_view(g)
	var v: BonsaiView = made[1]
	var soil := v.plant_screen_position(0) + Vector2(0, 10)
	b.repot_due = false
	v.tap_object("trowel")
	t.check_eq(v.use_trowel(), "not_yet", "on other days the trowel only says when")
	t.check(not v.is_lifted() and v.days_to_repot() >= 1, "not lifted; days to wait known")
	b.repot_due = true
	v.use_at(soil)
	_finish(v)
	t.check(v.is_lifted(), "on a repot day the trowel lifts the tree out")
	v.tap_object("shears")
	v.use_at(soil)
	t.check(v.trim_share() > 0.0, "the secateurs trim the root ball")
	v.repot_pick("oval")
	v.tap_object("trowel")
	v.use_at(soil)
	_finish(v)
	t.check(not v.is_lifted() and b.pot == "oval" and not b.repot_due, "the trowel puts it back in fresh soil, in the new pot")
	(made[0] as Node).free()


## 0.8, C1 of the 0.7 check: the pellet tin remembers the last kind chosen, in the save, so a
## spoon is two taps (tin, soil); changing the kind on the slip is optional.
func test_pellets_are_two_taps_and_remembered() -> void:
	var g := GameState.new_game(47)
	g.ensure_bonsai(true)
	var made := _sill_view(g)
	var v: BonsaiView = made[1]
	var soil := v.plant_screen_position(0) + Vector2(0, 10)
	v.tap_object("fertiliser")
	v.pellet_kind = 1
	_finish(v)
	v.tap_object("fertiliser")
	_finish(v)
	t.check_eq(v.tool, "", "the tin put down")
	var h := GameState.from_dict(JSON.parse_string(JSON.stringify(g.to_dict())))
	t.check_eq(h.bonsai.pellet_kind, 1, "the choice is kept in the save")
	(made[0] as Node).free()
	var again := _sill_view(h)
	var w: BonsaiView = again[1]
	var p0 := h.bonsai.soil[1]
	var taps := 0
	w.tap_object("fertiliser")
	taps += 1
	w.use_at(soil)
	taps += 1
	_finish(w)
	t.check(h.bonsai.soil[1] > p0, "tin, then soil: a spoon of the remembered P pellets")
	t.check(taps <= 2, "two taps")
	(again[0] as Node).free()


## 0.8, C4 of the 0.7 check: the needs read on the tree and the pot, natural signs only.
func test_needs_show_on_the_tree_and_pot() -> void:
	var g := GameState.new_game(49)
	g.ensure_bonsai(true)
	var b := g.bonsai
	var made := _sill_view(g)
	var v: BonsaiView = made[1]
	# Hunger: none on fresh soil, full on an empty one, per kind.
	b.soil = PackedFloat32Array([BonsaiSim.FRESH_SOIL, BonsaiSim.FRESH_SOIL, BonsaiSim.FRESH_SOIL])
	for k in range(3):
		t.check_near(b.hunger(k), 0.0, 1e-4, "fresh soil: no hunger for %s" % BonsaiHud.PELLETS[k])
	b.soil = PackedFloat32Array([0.0, BonsaiSim.FRESH_SOIL, BonsaiSim.FRESH_SOIL])
	t.check(b.hunger(0) > 0.9 and b.hunger(1) == 0.0, "no nitrogen left: hungry for N only")
	# The sprays' colours (the headless renderer keeps no instance colours, so the rule itself).
	var fed := Color(0.95, 1.0, 0.97, 0.3)
	var pale := BonsaiView.hungry_color(fed, 1.0, 0.0, false)
	t.check(pale.b < fed.b * 0.7 and pale.r > fed.r and pale.g >= fed.g, "short of nitrogen: the needles pale and yellow (%s)" % pale)
	var bronze := BonsaiView.hungry_color(fed, 0.0, 1.0, false)
	t.check(bronze.g < fed.g * 0.9 and bronze.r >= fed.r * 0.95, "short of phosphorus: a dull bronze (%s)" % bronze)
	var tip := BonsaiView.hungry_color(fed, 0.0, 0.0, true)
	t.check(tip.g < fed.g * 0.85 and tip.b < fed.b * 0.6, "short of potassium: a browned tip (%s)" % tip)
	t.check(BonsaiView.hungry_color(fed, 0.0, 0.0, false) == fed, "fed: unchanged")
	t.check(BonsaiView.K_TIP_SHARE > 0.2 and BonsaiView.K_TIP_SHARE < 0.7, "potassium browns some tips, not all")
	b.soil = PackedFloat32Array([0.0, BonsaiSim.FRESH_SOIL, BonsaiSim.FRESH_SOIL])
	var sig_before: Array = v._sig.duplicate()
	v.refresh(false)
	t.check(v._sig != sig_before, "a change in hunger rebuilds the foliage")
	# Dry against wet soil: the soil shader gets the moisture; the dry soil is pale and cracked.
	b.moisture = 0.05
	v._process(0.0)
	var dry: float = v._soil_mat.get_shader_parameter("wet")
	b.moisture = 0.9
	v._process(0.0)
	var wet: float = v._soil_mat.get_shader_parameter("wet")
	t.check(dry < 0.1 and wet > 0.8, "the soil shows dry and soaked (%.2f, %.2f)" % [dry, wet])
	var code := (v._soil_mat.shader as Shader).code
	t.check(code.contains("cracks(") and code.contains("soak"), "the dry top cracks, the wet one shines")
	# Repot time on the pot: roots at the rim and under the foot, the soil pushed up.
	t.check(not v.shows_rootbound(), "not yet asked: the pot looks as always")
	b.repot_due = true
	v.refresh(true)
	t.check(v.shows_rootbound(), "asked to be repotted: roots show on the pot")
	t.check(v._soil.position.y > 0.0, "and the soil is pushed up")
	b.repot("nursery", 0.3)
	v.refresh(true)
	t.check(not v.shows_rootbound() and is_zero_approx(v._soil.position.y), "repotted: gone again")
	(made[0] as Node).free()


func test_tool_labels_until_used_once() -> void:
	var g := GameState.new_game(45)
	g.ensure_bonsai(true)
	var hud := BonsaiHud.new()
	t.root.add_child(hud)
	var v := BonsaiView.new()
	t.root.add_child(v)
	hud.state = g
	hud.view = v
	for id in BonsaiTools.LABELS:
		t.check(hud.is_new(id), "a first-time label: " + id)
	v.tool_used.emit("water")
	v.tool_used.emit("burn")
	v.tool_used.emit("unwire")
	v.tool_used.emit("repot")
	v.object_tapped.emit("styles")
	for id in ["water", "fertiliser", "wire", "trowel", "styles"]:
		t.check(not hud.is_new(id), "used once, no label: " + id)
	t.check(hud.is_new("shears"), "the others keep theirs")
	hud.close_sheet()
	hud.free()
	v.free()
