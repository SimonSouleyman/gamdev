extends RefCounted
var t


func test_same_seed_same_underground() -> void:
	var a := Underground.new(12)
	var b := Underground.new(12)
	var c := Underground.new(13)
	t.check_eq(a.dot_positions, b.dot_positions, "same seed, same dots")
	t.check_eq(a.rock_centers, b.rock_centers, "same seed, same rocks")
	t.check(a.dot_positions != c.dot_positions, "other seed, other dots")


func test_dot_budget_and_all_below_ground() -> void:
	var u := Underground.new(3)
	t.check(u.dot_count() > 500, "plenty of dots (%d)" % u.dot_count())
	t.check(u.dot_count() <= Budgets.NUTRIENT_DOTS_LOADED, "dot budget respected")
	var above := 0
	var in_rock := 0
	for i in range(u.dot_count()):
		if u.dot_positions[i].y >= 0.0:
			above += 1
		if u.is_inside_rock(u.dot_positions[i]):
			in_rock += 1
	t.check_eq(above, 0, "no dot above the meadow")
	t.check_eq(in_rock, 0, "no dot inside a rock")


func test_layers_topsoil_rich_deep_watery() -> void:
	# Topsoil holds most N and P; deeper there is more water share, K near rocks and more rock.
	var u := Underground.new(8)
	var top := [0, 0, 0, 0]
	var deep := [0, 0, 0, 0]
	for i in range(u.dot_count()):
		if -u.dot_positions[i].y <= Underground.TOPSOIL:
			top[u.dot_kinds[i]] += 1
		else:
			deep[u.dot_kinds[i]] += 1
	t.check(top[Resources.Kind.NITROGEN] > deep[Resources.Kind.NITROGEN], "N mostly in topsoil")
	t.check(top[Resources.Kind.PHOSPHORUS] > deep[Resources.Kind.PHOSPHORUS], "P mostly in topsoil")
	t.check(deep[Resources.Kind.POTASSIUM] > top[Resources.Kind.POTASSIUM], "K mostly deep")
	var shallow_rocks := 0
	var deep_rocks := 0
	for c in u.rock_centers:
		if -c.y > Underground.DEPTH * 0.5:
			deep_rocks += 1
		else:
			shallow_rocks += 1
	t.check(deep_rocks > shallow_rocks / 2, "rocks get more common deeper (%d deep, %d shallow)" % [deep_rocks, shallow_rocks])


func test_starter_patch_near_the_seed() -> void:
	var u := Underground.new(21)
	var near := u.dots_near(Vector3(0, -1, 0), 3.0)
	t.check(near.size() >= 20, "first run always finds food (%d dots near the seed)" % near.size())


func test_collect_marks_and_adds() -> void:
	var u := Underground.new(4)
	var res := Resources.new()
	var id := 0
	var kind := u.dot_kinds[id]
	var cap := u.dot_capacity[id]
	var got := u.collect(PackedInt32Array([id]), res)
	t.check_eq(got.size(), 1, "drawn from")
	t.check_near(res.amount(kind), cap / Underground.DEPOSIT_SHARES, 1e-5, "one share into the matching resource")
	t.check_near(u.dot_amounts[id], cap * (1.0 - 1.0 / Underground.DEPOSIT_SHARES), 1e-5, "the deposit keeps the rest")
	for _k in range(10):
		u.collect(PackedInt32Array([id]), res)
	t.check_near(res.amount(kind), cap, 1e-4, "a deposit gives exactly what it held")
	t.check_eq(u.collect(PackedInt32Array([id]), res).size(), 0, "then it is empty")
	t.check(not (u.dots_near(u.dot_positions[id], 0.01) as PackedInt32Array).has(id), "empty deposits are not found again")


func test_surface_hints_match_below() -> void:
	var u := Underground.new(5)
	var hints := u.surface_hints()
	var kinds := {}
	for h in hints:
		kinds[h["kind"]] = true
		var p: Vector3 = h["position"]
		match str(h["kind"]):
			"rushes", "damp":
				t.check(_patch_below(u, p, Resources.Kind.WATER), "water below %s at %s" % [h["kind"], p])
			"clover", "nettles":
				t.check(_patch_below(u, p, Resources.Kind.NITROGEN), "nitrogen below %s at %s" % [h["kind"], p])
			"stones":
				var rock := false
				for r in range(u.rock_centers.size()):
					var c := u.rock_centers[r]
					if Vector2(c.x - p.x, c.z - p.z).length() < 0.01 and -c.y - u.rock_radii[r] < 1.2:
						rock = true
				t.check(rock, "shallow rock below stones")
	for k in ["rushes", "clover", "moss"]:
		t.check(kinds.has(k), "meadow shows %s" % k)


func _patch_below(u: Underground, p: Vector3, kind: int) -> bool:
	for patch in u.patches:
		var c: Vector3 = patch["center"]
		if patch["kind"] == kind and Vector2(c.x - p.x, c.z - p.z).length() < 0.01 and -c.y <= Underground.HINT_MAX_DEPTH:
			return true
	return false


func test_finds_touched_once() -> void:
	var u := Underground.new(6)
	t.check(u.finds.size() >= 3, "there are finds")
	var f: Dictionary = u.finds[0]
	var got := u.touch_finds(f["position"])
	t.check_eq(got.size(), 1, "touched")
	t.check_eq(u.touch_finds(f["position"]).size(), 0, "only once")


func test_round_trip() -> void:
	var u := Underground.new(9)
	var res := Resources.new()
	u.collect(PackedInt32Array([1, 2, 3]), res)
	u.touch_finds(u.finds[1]["position"])
	var v := Underground.from_dict(JSON.parse_string(JSON.stringify(u.to_dict())))
	t.check_eq(v.dot_collected, u.dot_collected, "empty deposits kept")
	t.check_eq(v.dot_amounts, u.dot_amounts, "deposit levels kept")
	t.check_eq(v.finds[1]["found"], true, "found finds kept")
	t.check_near(v.dot_amounts[1], v.dot_capacity[1] * (1.0 - 1.0 / Underground.DEPOSIT_SHARES), 1e-5, "a drawn deposit is lower")
