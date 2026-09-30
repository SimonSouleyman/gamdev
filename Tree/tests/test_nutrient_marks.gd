extends RefCounted
## 0.8: shapes on the nutrient dots (specs/0.8.md section 1; broken list items 1 to 3).
var t

## The smallest mark still drawn as a shape on the phone, in pixels: the smallest dot quad
## (0.128 m) times the mark's share of it (shape_span), at the distance where shapes turn round
## (shape_far, 9.5 m), with the root camera's 62 degree view on an 800 pixel tall screen
## (the phone look; a real phone has more pixels). About 6 px.
static func smallest_px() -> int:
	var quad := 0.16 * 0.8
	var span := 0.72
	var px_per_m := 800.0 / (2.0 * 9.5 * tan(deg_to_rad(31.0)))
	return int(floor(quad * span * px_per_m))


func _difference(a: PackedFloat32Array, b: PackedFloat32Array) -> float:
	var d := 0.0
	var both := 0.0
	for i in range(a.size()):
		d += absf(a[i] - b[i])
		both += maxf(a[i], b[i])
	return d / maxf(both, 1e-4)


## Broken 1: no two shapes may be confused at the smallest size or in grey. The marks are drawn
## in one tone (only the colour differs), so a grey screenshot keeps exactly these shapes.
func test_the_four_marks_differ_at_the_smallest_phone_size() -> void:
	var px := smallest_px()
	t.check(px >= 5 and px <= 9, "smallest mark on the phone is a few pixels (%d)" % px)
	for size in [px, 12, 32]:
		var masks: Array = []
		for k in range(4):
			masks.append(NutrientMarks.coverage(k, size))
		for a in range(4):
			for b in range(a + 1, 4):
				var diff := _difference(masks[a], masks[b])
				t.check(diff > 0.3, "%s and %s differ at %d px (%.2f of their area)" % [NutrientMarks.SHAPE_NAMES[a], NutrientMarks.SHAPE_NAMES[b], size, diff])
	# The ring keeps a hole and the spark its thin arms even that small.
	var ring := NutrientMarks.coverage(3, px)
	var mid := px / 2
	t.check(ring[mid * px + mid] < 0.5, "the ring's middle stays dark at %d px" % px)


func test_marks_have_their_shapes() -> void:
	t.check(NutrientMarks.inside(0, Vector2(0, -0.3)) and NutrientMarks.inside(0, Vector2(0, 0.8)) and not NutrientMarks.inside(0, Vector2(0.5, 0.7)), "water: a round belly and a point on top")
	t.check(NutrientMarks.inside(1, Vector2.ZERO) and not NutrientMarks.inside(1, Vector2(0.6, 0.6).rotated(NutrientMarks.LEAF_TILT)), "nitrogen: a narrow leaf")
	t.check(NutrientMarks.inside(2, Vector2(0.9, 0)) and NutrientMarks.inside(2, Vector2(0, -0.9)) and not NutrientMarks.inside(2, Vector2(0.45, 0.45)), "phosphorus: four points, hollow between them")
	t.check(not NutrientMarks.inside(3, Vector2.ZERO) and NutrientMarks.inside(3, Vector2(0.65, 0)), "potassium: a ring")
	var atlas := NutrientMarks.atlas()
	t.check(atlas.get_width() == NutrientMarks.CELL * 4 and atlas.get_height() == NutrientMarks.CELL, "one atlas of four cells")
	t.check(atlas.get_image().has_mipmaps(), "mipmapped for tiny far dots")


## Broken 2 and 3: the dots underground carry their kind's shape on the same quads (no new node,
## no new draw call), dimmed dots and the wish's warm dots included.
func test_underground_dots_carry_their_shape_without_new_draw_calls() -> void:
	var rv := RootView.new()
	t.root.add_child(rv)
	var g := GameState.new_game(14)
	rv.setup(g.ground, g.roots, g.sim.resources)
	var dots: MultiMeshInstance3D = rv._dots
	var mm := dots.multimesh
	t.check(mm.use_custom_data, "the kind rides in the instance data")
	t.check_eq(mm.instance_count, g.ground.dot_count(), "one instance per dot, as before")
	var mat := dots.material_override as ShaderMaterial
	t.check(mat.get_shader_parameter("marks") == NutrientMarks.atlas(), "the dots' material samples the marks atlas")
	var ok := true
	for i in range(mm.instance_count):
		if int(round(rv.dot_look(i)[1].r)) != g.ground.dot_kinds[i]:
			ok = false
	t.check(ok, "every dot's shape is its kind")
	# Only one node draws all the dots (one draw call): count geometry that uses the dot shader.
	var users := 0
	var stack: Array[Node] = [rv]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		stack.append_array(n.get_children())
		var gi := n as GeometryInstance3D
		if gi != null and gi.material_override is ShaderMaterial and (gi.material_override as ShaderMaterial).shader == mat.shader:
			users += 1
	t.check_eq(users, 1, "one node draws every dot")
	# A dot the roots already reach is dimmed and keeps its shape.
	var i0 := 0
	g.roots.tapped[i0] = true
	rv._set_dot(i0)
	# (The headless test server keeps no instance data, so the look is read from dot_look.)
	t.check_eq(int(round(rv.dot_look(i0)[1].r)), g.ground.dot_kinds[i0], "a dimmed dot keeps its shape")
	t.check(rv.dot_look(i0)[0].get_luminance() < Resources.KIND_COLORS[g.ground.dot_kinds[i0]].get_luminance(), "and is dimmed")
	# The wish's warm dots (0.7 glow) keep theirs too.
	var glows := g.wish_glows()
	if not glows.is_empty():
		rv.set_wish_glows(glows)
		var warm: Array = rv._warm_dots.keys()
		for i in warm:
			t.check_eq(int(round(rv.dot_look(int(i))[1].r)), g.ground.dot_kinds[int(i)], "a warm wish dot keeps its shape")
	rv.free()


## Broken 2: the HUD pills, the journal's pages and care page, and the bonsai's pellet slip all
## show the marks.
func test_every_place_that_shows_a_nutrient_by_colour_shows_its_mark() -> void:
	# HUD pills.
	var tv := TreeView.new()
	var bar := HBoxContainer.new()
	for k in range(4):
		tv._pill(bar, Resources.KIND_COLORS[k], k)
	var marks := 0
	for panel in bar.get_children():
		for c in panel.get_child(0).get_children():
			if c is TextureRect and str(c.name).begins_with("mark_"):
				marks += 1
	t.check_eq(marks, 4, "each nutrient pill has its mark")
	bar.free()
	tv.free()
	# Journal pages that name the dots by colour carry the key; the text names the shapes.
	t.check("blue drops" in Pages.body("first_night") and "violet rings" in Pages.body("first_night"), "the first night's page names the shapes")
	var j := Journal.new()
	t.root.add_child(j)
	for id in Journal.MARK_PAGES:
		j.show_page(id, Pages.title(id), Pages.body(id))
		t.check(j._page_marks.visible, "page %s shows the marks" % id)
		j.close_page()
	# The care page: the marks of the dots it asks for.
	var care = preload("res://tests/test_care.gd").new()
	care.t = t
	var g: GameState = care._play(6)
	care._night(g)
	g.sim.resources.stock[Resources.Kind.WATER] = 0.0
	g.sim.assess_needs()
	care._day_to(g, 0.5)
	j.state = g
	j._refresh_care()
	var drops := 0
	for c in j._care_box.get_children():
		for m in c.get_children():
			if m is TextureRect and str(m.name) == "mark_drop":
				drops += 1
	t.check(drops >= 1, "the care page shows the drop beside tonight's root")
	t.check("blue drop" in Care.DOT_WORDS[0], "and its words name the shape")
	j.free()
	# The bonsai's pellet slip.
	var hud := BonsaiHud.new()
	t.root.add_child(hud)
	for k in range(3):
		var b: Button = hud._pellet_buttons[k]
		t.check(b.icon == NutrientMarks.icon(k + 1, 30), "pellet %s shows its mark" % BonsaiHud.PELLETS[k])
	hud.free()
