extends RefCounted
## Nutrient shapes and dots. 0.8 put shapes on the dots (specs/0.8.md section 1); 0.8.1 (Simon,
## broken item 20): in play the nutrients are plain coloured dots everywhere (tree mode, the HUD,
## the root run's dots, the wish glow); the shapes stay only in the journal's key and on the
## pellet tins, so dot-shape items 1 to 3 now count only those.
var t

## The smallest the shapes are drawn: the journal key's marks (legend at 24 pt) and the pellet tin.
const KEY_SIZES: Array[int] = [24, 30, 34]


func _difference(a: PackedFloat32Array, b: PackedFloat32Array) -> float:
	var d := 0.0
	var both := 0.0
	for i in range(a.size()):
		d += absf(a[i] - b[i])
		both += maxf(a[i], b[i])
	return d / maxf(both, 1e-4)


## Broken 1 (journal key and tins): no two shapes may be confused at the sizes they are drawn, or
## in grey (they are drawn in one tone, only the colour differs).
func test_the_four_marks_differ_where_they_are_shown() -> void:
	for size in KEY_SIZES:
		var masks: Array = []
		for k in range(4):
			masks.append(NutrientMarks.coverage(k, size))
		for a in range(4):
			for b in range(a + 1, 4):
				var diff := _difference(masks[a], masks[b])
				t.check(diff > 0.3, "%s and %s differ at %d px (%.2f of their area)" % [NutrientMarks.SHAPE_NAMES[a], NutrientMarks.SHAPE_NAMES[b], size, diff])
	var px := KEY_SIZES[0]
	var ring := NutrientMarks.coverage(3, px)
	t.check(ring[(px / 2) * px + px / 2] < 0.5, "the ring's middle stays open at %d px" % px)


func test_marks_have_their_shapes() -> void:
	t.check(NutrientMarks.inside(0, Vector2(0, -0.3)) and NutrientMarks.inside(0, Vector2(0, 0.8)) and not NutrientMarks.inside(0, Vector2(0.5, 0.7)), "water: a round belly and a point on top")
	t.check(NutrientMarks.inside(1, Vector2.ZERO) and not NutrientMarks.inside(1, Vector2(0.6, 0.6).rotated(NutrientMarks.LEAF_TILT)), "nitrogen: a narrow leaf")
	t.check(NutrientMarks.inside(2, Vector2(0.9, 0)) and NutrientMarks.inside(2, Vector2(0, -0.9)) and not NutrientMarks.inside(2, Vector2(0.45, 0.45)), "phosphorus: four points, hollow between them")
	t.check(not NutrientMarks.inside(3, Vector2.ZERO) and NutrientMarks.inside(3, Vector2(0.65, 0)), "potassium: a ring")


## Broken 20: underground every dot is a plain round glow in its kind's colour (no shape atlas, no
## per-dot kind data), one node draws them all; reached dots dim, the wish's dots only warm up.
func test_underground_dots_are_plain_coloured_dots() -> void:
	var rv := RootView.new()
	t.root.add_child(rv)
	var g := GameState.new_game(14)
	rv.setup(g.ground, g.roots, g.sim.resources)
	var dots: MultiMeshInstance3D = rv._dots
	var mm := dots.multimesh
	t.check(not mm.use_custom_data, "no shape data rides with the dots")
	t.check_eq(mm.instance_count, g.ground.dot_count(), "one instance per dot, as before")
	var mat := dots.material_override as ShaderMaterial
	var code := mat.shader.code
	t.check(not ("sampler2D" in code) and not ("INSTANCE_CUSTOM" in code), "the dot shader draws no shapes")
	var ok := true
	for i in range(mm.instance_count):
		if not rv.dot_look(i).is_equal_approx(Resources.KIND_COLORS[g.ground.dot_kinds[i]]):
			ok = false
	t.check(ok, "every fresh dot glows in its kind's colour")
	var users := 0
	var stack: Array[Node] = [rv]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		stack.append_array(n.get_children())
		var gi := n as GeometryInstance3D
		if gi != null and gi.material_override is ShaderMaterial and (gi.material_override as ShaderMaterial).shader == mat.shader:
			users += 1
	t.check_eq(users, 1, "one node draws every dot")
	var i0 := 0
	g.roots.tapped[i0] = true
	rv._set_dot(i0)
	t.check(rv.dot_look(i0).get_luminance() < Resources.KIND_COLORS[g.ground.dot_kinds[i0]].get_luminance(), "a reached dot is dimmed")
	var glows := g.wish_glows()
	if not glows.is_empty():
		rv.set_wish_glows(glows)
		for i in rv._warm_dots.keys():
			var c: Color = rv.dot_look(int(i))
			t.check(c.r >= Resources.KIND_COLORS[g.ground.dot_kinds[int(i)]].r - 0.001, "a wish dot is its colour, a little warmer")
	# The run's catch on the HUD: plain coloured dots, no marks.
	var shapes := 0
	var plain := 0
	for c in rv._counts.get_children():
		if c is TextureRect:
			shapes += 1
		elif c is Label and (c as Label).text == "●":
			plain += 1
	t.check_eq(shapes, 0, "the run's catch shows no shapes")
	t.check_eq(plain, 4, "but a coloured dot per kind")
	rv.free()


## Broken 20 (tree mode): the HUD pills show plain coloured dots; the journal's care and hint
## pages keep the shapes as a key, and the pellet tins keep theirs.
func test_shapes_only_in_the_journal_key_and_on_the_tins() -> void:
	# HUD pills.
	var tv := TreeView.new()
	var bar := HBoxContainer.new()
	for k in range(4):
		tv._pill(bar, Resources.KIND_COLORS[k], k)
	var dots := 0
	var shapes := 0
	for panel in bar.get_children():
		for c in panel.get_child(0).get_children():
			if c is TextureRect:
				shapes += 1
			elif c is Label and (c as Label).text == "●":
				dots += 1
	t.check_eq(shapes, 0, "no nutrient pill shows a shape")
	t.check_eq(dots, 4, "each nutrient pill has its coloured dot")
	bar.free()
	tv.free()
	t.check("blue dots are water" in Pages.body("first_night") and not ("drops" in Pages.body("first_night")), "the first night's page names the dots by colour")
	# The journal key (care and meadow-hint pages).
	var j := Journal.new()
	t.root.add_child(j)
	for id in Journal.MARK_PAGES:
		j.show_page(id, Pages.title(id), Pages.body(id))
		t.check(j._page_marks.visible, "page %s shows the marks" % id)
		j.close_page()
	# The Today page (0.8.2.6, the care page's place): the mark of the dots it asks for.
	var care = preload("res://tests/test_care.gd").new()
	care.t = t
	var g: GameState = care._play(6)
	care._night(g)
	g.sim.resources.stock[Resources.Kind.WATER] = 0.0
	g.sim.assess_needs()
	care._day_to(g, 0.5)
	j.state = g
	j._refresh_today()
	var drops := 0
	for c in j._today_box.get_children():
		for m in c.get_children():
			if m is TextureRect and str(m.name) == "mark_drop":
				drops += 1
	t.check(drops >= 1, "the Today page shows the drop beside the need")
	t.check(Care.DOT_WORDS[0] == "blue", "and its words name the colour")
	j.free()
	# The bonsai's pellet slip.
	var hud := BonsaiHud.new()
	t.root.add_child(hud)
	for k in range(3):
		var b: Button = hud._pellet_buttons[k]
		t.check(b.icon == NutrientMarks.icon(k + 1, 30), "pellet %s shows its mark" % BonsaiHud.PELLETS[k])
	hud.free()
