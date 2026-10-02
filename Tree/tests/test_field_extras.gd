extends RefCounted
## 0.8.2 field extras (specs/root-field-extras.md, docs/notes/field-0.8.2.md): the far view, rock
## bands and soft veins, and the first-time lines. One test per 0.8.2 item of the spec's "broken"
## list (1, 2, 3, 4, 9; item 5, the month, is the strategies run in the notes and
## test_side_roots' calm night, which now plays on a soil with bands).
var t

const FRAME := 1.0 / 30.0
const SEEDS := [3, 14, 27]


## A main root of night `night` from node `from` in a straight line to `to` (nodes every 0.25 m).
func _line(r: RootSystem, from: int, to: Vector3, night: int) -> int:
	var a := r.graph.positions[from]
	var n := maxi(1, ceili(a.distance_to(to) / 0.25))
	var id := from
	for i in range(1, n + 1):
		id = r.graph.add_node(id, a.lerp(to, float(i) / n))
		r.graph.set_flag(id, "main", night)
	r.main_root_count = maxi(r.main_root_count, night + 1)
	return id


func _view(seed: int) -> Array:
	var g := GameState.new_game(seed)
	var rv := RootView.new()
	# Under a holder, not the root: on its own a RootView sets up a soil of its own (F6 play).
	var holder := Node.new()
	t.root.add_child(holder)
	holder.add_child(rv)
	rv.setup(g.ground, g.roots, g.sim.resources)
	return [g, rv]


# --- broken 1: the far view only outside the run, and cheap -------------------------------

func test_far_view_opens_only_outside_the_run() -> void:
	var made := _view(14)
	var g: GameState = made[0]
	var rv: RootView = made[1]
	g.sim.resources.life_force = 60.0
	_line(g.roots, 0, Vector3(3, -1, -4), 0)
	rv.begin_pick()
	t.check(rv.open_far_view(), "the far view opens while choosing the start")
	t.check(rv.camera.far >= rv.far_distance() + Underground.field_extent, "the far camera sees the whole field")
	var d := rv.far_distance()
	t.check(d >= 40.0 and d <= 60.0, "the far camera's distance stays in its 40 to 60 m range (%.1f m)" % d)
	rv.leave_far_view()
	t.check(not rv.far_view, "pinch in (or back) returns")
	t.check(rv.start_at(0), "a run starts")
	t.check(not rv.open_far_view(), "never during the run")
	var wheel := InputEventMouseButton.new()
	wheel.button_index = MOUSE_BUTTON_WHEEL_DOWN
	wheel.pressed = true
	for _i in range(40):
		rv._unhandled_input(wheel)
	t.check(not rv.far_view, "zooming out during the run keeps today's range")
	g.roots.finish_early(g.ground, g.sim.resources)
	rv._settle()
	t.check(not rv.open_far_view(), "not while tonight's root settles")
	rv._settle_t = -1.0
	t.check(rv.open_far_view(), "after the run has ended")
	rv.leave_far_view()
	rv.quiet_night = true
	rv.begin_idle_overview()
	t.check(rv.open_far_view(), "and on a quiet night")
	rv.free()


## The far view draws with a fixed handful of nodes (one mesh or MultiMesh each, whatever the
## number of roots and patches) and its root lines stay within Budgets.FAR_VIEW_SEGMENTS.
func test_far_view_is_a_few_merged_meshes() -> void:
	var made := _view(3)
	var g: GameState = made[0]
	var rv: RootView = made[1]
	var from := 0
	for night in range(30):
		var a := TAU * night / 30.0
		from = _line(g.roots, 0 if night % 3 == 0 else from, Vector3(cos(a) * (6.0 + night * 0.6), -1.0 - 0.05 * night, sin(a) * (6.0 + night * 0.6)), night)
	rv.begin_pick()
	var before := _drawn(rv)
	rv.open_far_view()
	var far := _drawn(rv)
	t.check(far <= 6 + rv._glow_nodes.size(), "the far view draws %d nodes (roots, clouds, trunk, bands, veins)" % far)
	t.check(far < before, "fewer than the normal view (%d)" % before)
	var mesh := rv._far_roots.mesh
	t.check(mesh != null and mesh.get_surface_count() == 1, "the roots are one mesh, one surface")
	var verts := (mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX] as PackedVector3Array).size()
	t.check(verts <= Budgets.FAR_VIEW_SEGMENTS * 18, "within the segment budget (%d vertices)" % verts)
	rv.free()


func _drawn(n: Node) -> int:
	var count := 0
	for c in n.get_children():
		if c is CanvasLayer:
			continue
		if c is Node3D and not (c as Node3D).visible:
			continue
		if c is GeometryInstance3D:
			count += 1
		count += _drawn(c)
	return count


## The last seven nights' roots are brighter than the older ones, the newest the brightest; side
## and fine roots are left out.
func test_far_view_roots_by_night() -> void:
	var newest := FieldLook.far_color(20, 20)
	var recent := FieldLook.far_color(15, 20)
	var old := FieldLook.far_color(5, 20)
	t.check(newest.get_luminance() > recent.get_luminance() and recent.get_luminance() > old.get_luminance(), "newest > last 7 nights > older")
	t.check(FieldLook.far_color(14, 20) == recent and FieldLook.far_color(13, 20) == old, "the last seven nights (the newest among them) count as recent")
	var r := RootSystem.new(1)
	var tip := _line(r, 0, Vector3(0, -1, -6), 0)
	var fine := r.graph.add_node(tip, Vector3(0.3, -1.2, -6.2))
	r.graph.set_flag(fine, "fine", 0)
	t.check_eq(FieldLook.main_tips(r), PackedInt32Array([tip]), "a far tap picks the end of a main root, not a fine one")


## Tapping an old root's end in the far view starts tonight's root there and flies back down.
func test_a_far_tap_starts_tonight_at_an_old_tip() -> void:
	var made := _view(14)
	var g: GameState = made[0]
	var rv: RootView = made[1]
	g.sim.resources.life_force = 60.0
	var tip := _line(g.roots, 0, Vector3(10, -1, -6), 0)
	rv.begin_pick()
	rv.open_far_view()
	rv.camera.fov = rv.far_fov()
	rv.camera.global_position = rv._orbit_position()
	rv.camera.look_at(rv._look, Vector3.UP)
	var at := rv.camera.unproject_position(g.roots.graph.positions[tip])
	t.check_eq(rv.pick_far_tip_at(at + Vector2(20, 10)), tip, "a tap near the tip picks it")
	t.check(rv.start_at(rv.pick_far_tip_at(at)), "and starts the run there")
	t.check(not rv.far_view and rv.mode == RootView.Mode.RUN, "back down for the run")
	t.check_eq(g.roots.run_start_id, tip, "from that tip")
	rv.free()


# --- broken 2: only the patches the player has come near -------------------------------------

func test_far_view_shows_known_patches_only() -> void:
	for seed in SEEDS:
		var u := Underground.new(seed, 3)
		var r := RootSystem.new(seed)
		var reach := RootView.known_reach
		var known := u.known_patches(r.graph.positions, reach, Terrain.edge)
		var far := u.far_patch_ids()
		var target := far[0]
		for pid in far:
			var c: Vector3 = u.patches[pid]["center"]
			if Vector2(c.x, c.z).length() > Terrain.edge:
				target = pid
				break
		t.check(not known.has(target), "seed %d: a far patch no root came near stays hidden" % seed)
		for i in known:
			var c: Vector3 = u.patches[i]["center"]
			var signed := -c.y <= Underground.HINT_MAX_DEPTH and Vector2(c.x, c.z).length() <= Terrain.edge - Underground.EDGE_INSET
			t.check(signed or c.length() - float(u.patches[i]["radius"]) <= reach, "seed %d: patch %d is known by its sign or by the trunk" % [seed, i])
		# A root driven to within reach of the far patch makes it known; one stopped short does not.
		var c: Vector3 = u.patches[target]["center"]
		var dir := Vector3(c.x, 0.0, c.z).normalized()
		var edge := c - dir * float(u.patches[target]["radius"])
		var short := _line(r, 0, edge - dir * (reach + 1.5), 0)
		t.check(not u.known_patches(r.graph.positions, reach, Terrain.edge).has(target), "seed %d: not yet, %.1f m short" % [seed, reach + 1.5])
		_line(r, short, edge - dir * (reach - 1.0), 1)
		t.check(u.known_patches(r.graph.positions, reach, Terrain.edge).has(target), "seed %d: known once a root came within reach" % seed)


func test_far_view_clouds_match_the_known_patches() -> void:
	var made := _view(27)
	var g: GameState = made[0]
	var rv: RootView = made[1]
	_line(g.roots, 0, Vector3(-9, -1.2, 9), 0)
	rv.begin_pick()
	rv.open_far_view()
	var known := rv.far_known_patches()
	t.check_eq(rv._far_clouds.multimesh.instance_count, known.size(), "one cloud per known patch, no more")
	t.check(not rv._dots.visible, "the single dots (all of them, known or not) hide in the far view")
	rv.free()


# --- broken 3: bands with gaps, two routes, never through rock ---------------------------------

func test_every_far_patch_has_two_routes() -> void:
	for seed in SEEDS:
		var u := Underground.new(seed, 3)
		t.check(u.bands.size() >= 2 and u.bands.size() <= 4, "seed %d: about 3 rock bands (%d)" % [seed, u.bands.size()])
		var far := u.far_patch_ids()
		var blocked := 0
		for pid in far:
			if u.straight_blocked(pid):
				blocked += 1
			t.check_eq(FieldRoutes.routes(u, pid), 2, "seed %d: patch %d has two routes" % [seed, pid])
		t.check(blocked >= 1 and blocked <= int(far.size() * 0.5), "seed %d: about a third of the straight lines blocked (%d of %d)" % [seed, blocked, far.size()])
		for b in u.bands:
			var pts: PackedVector2Array = b["points"]
			var len := (pts.size() - 1) * Underground.BAND_STEP
			var gap := 0
			for o in (b["open"] as PackedByteArray):
				gap += o
			t.check(len >= 6.0 and len <= 15.0, "seed %d: a band 6 to 15 m long (%.1f)" % [seed, len])
			t.check(float(b["half"]) * 2.0 >= 1.0 and float(b["half"]) * 2.0 <= 2.0, "1 to 2 m thick")
			t.check(gap * Underground.BAND_STEP >= Underground.band_gap + float(b["half"]) * 2.0 - 0.01, "seed %d: with a gap at least %.1f m clear" % [seed, Underground.band_gap])
			var r0 := pts[pts.size() / 2].length()
			t.check(r0 >= 9.0, "in the middle or far ring (%.1f m out)" % r0)


func test_old_saves_keep_their_soil() -> void:
	var plain := Underground.new(14, 3, 0)
	t.check(plain.bands.is_empty() and plain.veins.is_empty(), "a soil from before 0.8.2 has no bands")
	var d := plain.to_dict()
	d.erase("bands")
	var back := Underground.from_dict(d)
	t.check(back.bands.is_empty() and back.dot_count() == plain.dot_count(), "a 0.8.1 save loads without bands, dot for dot")
	for layout in [1, 2]:
		t.check(Underground.new(14, layout).bands.is_empty(), "layout %d never gets bands" % layout)
	var g := GameState.new_game(14)
	t.check(not g.ground.bands.is_empty() and not g.ground.veins.is_empty(), "a new game's wider field has bands and veins")
	var again := GameState.from_dict(g.to_dict())
	t.check_eq(again.ground.bands.size(), g.ground.bands.size(), "they come back from the save")
	t.check_eq(again.ground.dot_count(), g.ground.dot_count(), "dot for dot")
	# The soil around the bands is the old one: the same patches, the dots in a band moved to its face.
	t.check_eq(g.ground.patches.size(), plain.patches.size(), "the same rich patches")
	for i in range(plain.dot_count()):
		if g.ground.dot_positions[i] != plain.dot_positions[i]:
			t.check(plain.band_at(plain.dot_positions[i]) < 0 and g.ground.band_at(plain.dot_positions[i], 0.1) >= 0, "only dots in a band moved")
	for i in range(g.ground.dot_count()):
		if g.ground.is_inside_rock(g.ground.dot_positions[i]):
			t.check(false, "dot %d inside rock" % i)
			break


## A tip steered straight at a band never passes into it: it slides along the face (or the run
## ends there), and the magnetism never pulls toward a deposit behind rock.
func test_a_tip_never_passes_through_a_band() -> void:
	for seed in SEEDS:
		var u := Underground.new(seed, 3)
		for b in u.bands:
			var target := int(b["target"])
			var c: Vector3 = u.patches[target]["center"]
			var dir := Vector3(c.x, 0.0, c.z).normalized()
			var r := RootSystem.new(seed)
			r.fit_soil(u)
			# A root that reached a few metres short of the band on the straight line.
			var cross := 0.0
			while u.band_at(dir * cross + Vector3(0, c.y, 0)) < 0 and cross < 30.0:
				cross += 0.1
			var start := _line(r, 0, dir * (cross - 2.5) + Vector3(0, c.y, 0), 0)
			var res := Resources.new()
			res.life_force = 400.0
			r.start_run(start)
			var frames := 0
			var inside := 0
			while r.advance(Vector2.ZERO, false, FRAME, u, res) and frames < 2000:
				frames += 1
				if u.band_at(r.tip_position, 0.0) >= 0:
					inside += 1
			for id in range(r.graph.size()):
				if u.band_at(r.graph.positions[id], 0.0) >= 0 and r.root_level(id) == 0:
					inside += 1
			t.check_eq(inside, 0, "seed %d: the tip stayed out of the band toward patch %d" % [seed, target])


func test_the_magnetism_never_pulls_into_rock() -> void:
	var u := Underground.new(14, 3)
	var b: Dictionary = u.bands[0]
	var pts: PackedVector2Array = b["points"]
	var s := 0
	while (b["open"] as PackedByteArray)[s] != 0:
		s += 1
	var at := pts[s].lerp(pts[s + 1], 0.5)
	var tan := (pts[s + 1] - pts[s]).normalized()
	var n := Vector2(-tan.y, tan.x)
	var half := float(b["half"])
	# A fresh deposit right behind the band, the tip just in front of it, facing it.
	var behind := at + n * (half + 0.3)
	var front := at - n * (half + 0.4)
	u.dot_positions.append(Vector3(behind.x, -1.0, behind.y))
	u.dot_kinds.append(Resources.Kind.NITROGEN)
	u.dot_amounts.append(1.0)
	u.dot_capacity.append(1.0)
	u.dot_collected.append(0)
	u._build_grid()
	var r := RootSystem.new(14)
	r.fit_soil(u)
	r.tip_position = Vector3(front.x, -1.0, front.y)
	r.heading = Vector3(n.x, 0.0, n.y)
	var dot := u.dot_count() - 1
	t.check(u.line_blocked(r.tip_position, u.dot_positions[dot], 0.2, 0.08), "the deposit lies behind rock")
	t.check(r.fresh_ahead(u, r.magnet_radius, RootSystem.MAGNET_CONE, true) != dot, "the magnetism ignores a deposit behind rock")
	var pull := r.fresh_ahead(u, r.magnet_radius, RootSystem.MAGNET_CONE, true)
	t.check(pull < 0 or not u.line_blocked(r.tip_position, u.dot_positions[pull], 0.2, 0.08), "it only pulls along a clear line")
	# And a run toward it: the tip never enters the band.
	var res := Resources.new()
	res.life_force = 30.0
	var start := _line(r, 0, r.tip_position - Vector3(n.x, 0.0, n.y) * 0.5, 0)
	_line(r, start, Vector3(front.x, -1.0, front.y), 0)
	r.start_run(r.graph.size() - 1)
	r.heading = Vector3(n.x, 0.0, n.y)
	var frames := 0
	while r.advance(Vector2.ZERO, false, FRAME, u, res) and frames < 900:
		frames += 1
		t.check(u.band_at(r.tip_position) < 0, "the tip stays out")
		if u.band_at(r.tip_position) >= 0:
			break


# --- broken 4: the veins help, they do not decide -----------------------------------------------

func test_soft_veins_are_a_choice_not_the_answer() -> void:
	t.check(Underground.vein_cost >= 0.5 and Underground.vein_cost <= 0.8, "a metre in a vein costs 0.5 to 0.8x")
	for seed in SEEDS:
		var u := Underground.new(seed, 3)
		t.check(u.veins.size() >= 1 and u.veins.size() <= 3, "seed %d: about 2 veins" % seed)
		var r := RootSystem.new(seed)
		r.fit_soil(u)
		for v in u.veins:
			var pts: PackedVector3Array = v["points"]
			var mid := pts[pts.size() / 2]
			t.check_near(r.cost_per_metre(mid) / (r.cost_per_metre(mid) / u.soil_factor(mid)), Underground.vein_cost, 1e-4, "seed %d: cheaper inside" % seed)
			var len := (pts.size() - 1) * Underground.BAND_STEP
			t.check(len >= 8.0 and len <= 20.0 and float(v["radius"]) * 2.0 >= 1.0 and float(v["radius"]) * 2.0 <= 1.5, "8 to 20 m long, 1 to 1.5 m wide")
			# It points at its patch: its end lies short of the patch, toward it.
			var c: Vector3 = u.patches[int(v["target"])]["center"]
			var to := Vector2(c.x - pts[-1].x, c.z - pts[-1].z)
			var along := Vector2(pts[-1].x - pts[-3].x, pts[-1].z - pts[-3].z).normalized()
			t.check(to.length() < float(u.patches[int(v["target"])]["radius"]) + 2.0 and along.dot(to.normalized()) > 0.7, "seed %d: the vein points at its patch" % seed)
		# A vein is the cheaper way (than a clear straight line; a blocked line is the bands'
		# choice) to its own patch and seldom more: at most 40 % of the far patches.
		var open := 0
		var vein_best := 0
		var far := u.far_patch_ids()
		for pid in far:
			var straight := FieldRoutes.straight_cost(u, r, pid)
			if straight == INF:
				continue
			open += 1
			for k in range(u.veins.size()):
				if FieldRoutes.vein_route_cost(u, r, pid, k) < straight:
					vein_best += 1
					break
		t.check(vein_best >= 1 and vein_best <= ceili(far.size() * 0.4), "seed %d: a vein is the cheaper way to %d of %d far patches (%d with a clear line)" % [seed, vein_best, far.size(), open])


# --- broken 9: one line each, when it first matters ---------------------------------------------

func test_first_time_lines_come_one_at_a_time() -> void:
	for id in RootView.NOTES:
		var line: String = RootView.NOTES[id]
		t.check(not "\n" in line and line.length() <= 60, "%s: one short line" % id)
	var made := _view(14)
	var g: GameState = made[0]
	var rv: RootView = made[1]
	var asked: Array = []
	rv.first_note = func(id: String) -> bool:
		asked.append(id)
		return g.first_time("note_" + id)
	g.sim.resources.life_force = 60.0
	_line(g.roots, 0, Vector3(1, -1, -3), 0)
	rv.begin_pick()
	t.check(not "Pinch out" in rv._hint.text, "no far-view line before it matters")
	rv.far_hint = true
	rv._update_hud()
	t.check(rv._hint.text.count("\n") <= 1 and "Pinch out" in rv._hint.text, "then one line for it")
	rv.start_at(0)
	rv._waiting_for_input = false
	rv._check_soil_notes(FRAME)
	t.check(asked.is_empty(), "nothing said about rock at the trunk")
	var b: Dictionary = g.ground.bands[0]
	var s := 0
	while (b["open"] as PackedByteArray)[s] != 0:
		s += 1
	var p: Vector2 = (b["points"] as PackedVector2Array)[s]
	g.roots.tip_position = Vector3(p.x, -1.0, p.y)
	rv._check_soil_notes(FRAME)
	rv._update_hud()
	t.check_eq(rv._hint.text, RootView.NOTES["band"], "the first touch of a band: one line")
	rv._check_soil_notes(RootView.NOTE_SECONDS + 0.1)
	rv._update_hud()
	t.check_eq(rv._hint.text, "", "and it goes again")
	rv._run_met.clear()
	rv._check_soil_notes(FRAME)
	rv._update_hud()
	t.check_eq(rv._hint.text, "", "only the first time ever")
	rv.free()
