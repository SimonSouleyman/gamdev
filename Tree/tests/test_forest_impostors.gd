extends RefCounted
## The phone's card forest (ForestImpostors, design doc section 17 part 1).
var t


func test_every_kind_has_baked_cells() -> void:
	for set_info in [["trees", Scenery.KINDS.size()], ["shrubs", Scenery.SHRUBS.size()]]:
		var cells := ForestImpostors.cells(set_info[0])
		t.check(cells.size() <= ForestImpostors.MAX_CELLS, "%s fit the shader's cell array" % set_info[0])
		for kind in range(int(set_info[1])):
			t.check(not ForestImpostors.cells_of_kind(set_info[0], kind).is_empty(), "%s kind %d is baked" % [set_info[0], kind])
		for cell in cells:
			var uv: Rect2 = cell["uv"]
			t.check(uv.position.x >= 0.0 and uv.position.y >= 0.0 and uv.end.x <= 1.0 and uv.end.y <= 1.0, "cell inside its atlas")
			t.check((cell["size"] as Vector2).x > 0.3 and (cell["size"] as Vector2).y > 0.3, "cell has a size in metres")
			var x_off: float = cell["x_off"]
			t.check(x_off > 0.0 and x_off < 1.0, "the trunk stands on the card")
			var cuts: PackedVector2Array = cell["cuts"]
			t.check_eq(cuts.size(), 4, "a cut per corner")
			for c in cuts:
				# Cuts of at most half the card never cross: the octagon stays convex.
				t.check(c.x >= 0.0 and c.x <= 0.5 and c.y >= 0.0 and c.y <= 0.5, "corner cut within half the card")


func test_cards_draw_from_the_clearing_outwards() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var plants: Array = []
	for i in range(30):
		var a := rng.randf() * TAU
		plants.append([i % Scenery.KINDS.size(), Vector3(cos(a), 0.0, sin(a)) * rng.randf_range(20.0, 40.0), 3.0])
	# (Checked on the layout: a headless run keeps no MultiMesh data.)
	var cards := ForestImpostors.layout("trees", plants, rng)
	t.check_eq(cards.size(), 30, "one card per tree")
	var mmi := ForestImpostors.build("trees", plants, rng, ForestImpostors.material("trees"))
	t.check_eq(mmi.multimesh.mesh.get_surface_count(), 1, "one surface: one draw call for the ring")
	mmi.free()
	var last := 0.0
	var ordered := true
	var right_kind := true
	var cells := ForestImpostors.cells("trees")
	for card in cards:
		var o: Vector3 = (card[0] as Transform3D).origin
		var r := Vector2(o.x, o.z).length()
		ordered = ordered and r >= last - 0.001
		last = r
		var cell := int((card[1] as Color).r)
		# The sort keeps each card with its own plant: its cell shows that plant's kind.
		var nearest: Array = plants[0]
		for p in plants:
			if Vector2(p[1].x - o.x, p[1].z - o.z).length() < Vector2(nearest[1].x - o.x, nearest[1].z - o.z).length():
				nearest = p
		right_kind = right_kind and int(cells[cell]["kind"]) == int(nearest[0])
	t.check(ordered, "cards ordered by distance from the clearing's centre")
	t.check(right_kind, "each card shows its tree's kind")


func test_a_pc_keeps_every_tree_real() -> void:
	# (The tests run on a PC without --phone.)
	t.check(not Budgets.FOREST_IMPOSTORS, "no cards on a PC")
	var scenery := Scenery.new()
	var trees := _ring(40)
	t.check_eq(scenery._real_trees(trees).size(), 40, "all trees stay 3D")
	scenery.free()


func test_a_phone_keeps_the_innermost_tree_of_each_sector() -> void:
	var was := Budgets.FOREST_IMPOSTORS
	Budgets.FOREST_IMPOSTORS = true
	var scenery := Scenery.new()
	var trees := _ring(40)
	var real := scenery._real_trees(trees)
	Budgets.FOREST_IMPOSTORS = was
	scenery.free()
	t.check_eq(real.size(), Budgets.FOREST_REAL_TREES, "a handful of real trees")
	var sectors := {}
	for i in real:
		var p: Vector3 = trees[i][1]
		var sector := int(fposmod(atan2(p.z, p.x), TAU) / TAU * Budgets.FOREST_REAL_TREES) % Budgets.FOREST_REAL_TREES
		sectors[sector] = true
		for j in range(trees.size()):
			var q: Vector3 = trees[j][1]
			var other := int(fposmod(atan2(q.z, q.x), TAU) / TAU * Budgets.FOREST_REAL_TREES) % Budgets.FOREST_REAL_TREES
			if other == sector:
				t.check(Vector2(q.x, q.z).length() >= Vector2(p.x, p.z).length(), "the real tree is its sector's innermost")
	t.check_eq(sectors.size(), Budgets.FOREST_REAL_TREES, "spread around the ring")


## Trees [kind, foot] around a clearing of 18 m, at seeded distances.
func _ring(n: int) -> Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = 3
	var trees: Array = []
	for i in range(n):
		var a := TAU * i / n
		trees.append([i % 5, Vector3(cos(a), 0.0, sin(a)) * rng.randf_range(20.0, 30.0)])
	return trees
