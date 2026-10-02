extends RefCounted
## 0.8.2.7: missed wish deposits keep glowing until a root reaches them (specs/wish-compass-vial.md
## section 5, broken item 50a; tuning.md "Glows at once").
var t


## Mornings with no root reaching anything: each day's wish is missed.
func _missing(seed: int, days: int) -> Array:
	var u := Underground.new(seed)
	var roots := RootSystem.new(seed)
	var diary := Diary.new()
	var history: Array = []
	for day in range(1, days + 1):
		diary.new_wish(u, day, seed, roots)
		history.append(diary.wish_patch)
		# The night's root goes elsewhere (round the trunk, deeper each night), missing the wish.
		var a := Vector3(cos(day * 2.4) * 6.0, -1.0 - day * 0.3, sin(day * 2.4) * 6.0)
		var id := 0
		for i in range(1, 25):
			id = roots.graph.add_node(id, roots.graph.positions[0].lerp(a, i / 24.0))
			roots.graph.set_flag(id, "main", roots.main_root_count)
		roots.main_root_count += 1
	return [u, roots, diary, history]


func _glowing(diary: Diary, u: Underground) -> Dictionary:
	var out := {}
	for g in diary.glows(u):
		out[int(g["patch"])] = g
	return out


func test_missed_wishes_keep_glowing_up_to_four() -> void:
	for seed in [3, 14, 27]:
		var r := _missing(seed, 4)
		var u: Underground = r[0]
		var diary: Diary = r[2]
		var history: Array = r[3]
		var lit := _glowing(diary, u)
		var today := int(history[-1])
		t.check(lit.has(today) and float(lit[today]["strength"]) == Diary.GLOW_TODAY and bool(lit[today]["today"]), "seed %d: today's wish at full glow" % seed)
		var earlier := {}
		for pid in history.slice(0, history.size() - 1):
			if int(pid) != today:
				earlier[int(pid)] = true
		for pid in earlier:
			t.check(lit.has(pid), "seed %d: missed deposit %d still glows (50a)" % [seed, pid])
			if lit.has(pid):
				t.check_near(float(lit[pid]["strength"]), Diary.GLOW_YESTERDAY, 1e-4, "at about half")
				t.check(not bool(lit[pid]["today"]), "and is not today's")
		t.check_eq(lit.size(), earlier.size() + 1, "seed %d: today's and the %d missed" % [seed, earlier.size()])


func test_missed_ones_reach_three_in_play() -> void:
	# The planner points a new wish at a missed one again once MISSED_MAX wait untouched (0.8),
	# so in play today's and three missed glow at most; all of them stay lit.
	for seed in [3, 14, 27, 42]:
		var r := _missing(seed, 9)
		var diary: Diary = r[2]
		var u: Underground = r[0]
		t.check(diary.missed.size() >= 2, "seed %d: missed ones pile up (%d)" % [seed, diary.missed.size()])
		t.check_eq(diary.glows(u).size(), diary.missed.size() + 1, "seed %d: each glows" % seed)


func test_a_fifth_missed_puts_out_the_oldest_only() -> void:
	var u := Underground.new(14)
	var diary := Diary.new()
	var ids: Array = []
	for pid in range(2, u.patches.size()):
		if ids.size() < 5:
			ids.append(pid)
	for pid in ids:
		diary._add_missed(u, int(pid))
	t.check_eq(diary.missed.size(), Diary.MISSED_MAX, "four missed at most")
	var lit := _glowing(diary, u)
	t.check(not lit.has(int(ids[0])), "the oldest stops glowing")
	for k in range(1, 5):
		t.check(lit.has(int(ids[k])), "the other four glow (%d)" % int(ids[k]))
	t.check(u.patch_amount(int(ids[0])) > u.patch_amount(int(ids[0]), true) * 0.99, "the oldest stays a full ordinary deposit")
	diary._add_missed(u, int(ids[4]))
	t.check_eq(diary.missed.size(), Diary.MISSED_MAX, "a deposit is counted once")


func test_a_new_wish_comes_every_morning() -> void:
	var r := _missing(14, 6)
	var history: Array = r[3]
	for k in range(history.size()):
		t.check(int(history[k]) >= 0, "morning %d has a wish" % (k + 1))


func test_reaching_an_older_glow_writes_its_line_and_puts_it_out() -> void:
	var r := _missing(14, 3)
	var u: Underground = r[0]
	var roots: RootSystem = r[1]
	var diary: Diary = r[2]
	var old := int(diary.missed[0])
	var c: Vector3 = u.patches[old]["center"]
	# A main root of tonight straight into the old glow.
	roots.main_root_count = 1
	roots.run_first_new_id = roots.graph.size()
	var a := roots.graph.positions[0]
	var id := 0
	for i in range(1, 41):
		id = roots.graph.add_node(id, a.lerp(c, i / 40.0))
		roots.graph.set_flag(id, "main", 0)
	var lines := diary.entries.size()
	t.check_eq(diary.check_reached(u, roots, 3, 4), old, "the older glow counts as reached")
	t.check_eq(diary.entries.size(), lines + 1, "its diary line is written")
	t.check(not diary.missed.has(old), "it leaves the missed ones")
	t.check(not _glowing(diary, u).has(old), "and stops glowing")
	t.check(not diary.wish_reached, "today's wish is still open")
	t.check(_glowing(diary, u).has(diary.wish_patch), "and still glows")


func test_only_todays_wish_has_the_needle_and_the_journal_line() -> void:
	var r := _missing(14, 3)
	var u: Underground = r[0]
	var diary: Diary = r[2]
	var wish_lines := diary.entries.filter(func(e: Dictionary) -> bool: return e.get("topic", "") == "wish" and int(e.get("day", 0)) == 3)
	t.check_eq(wish_lines.size(), 1, "one wish line on the day: today's")
	t.check_eq(diary.wish, Diary.wish_text(u, diary.wish_patch), "the wish text is today's")
	var rv := RootView.new()
	rv.roots = r[1]
	var glows := diary.glows(u)
	rv._wish_glows = []
	for g in glows:
		var d: Dictionary = (g as Dictionary).duplicate()
		d["reached"] = false
		rv._wish_glows.append(d)
	var way := rv.wish_way()
	var c: Vector3 = u.patches[diary.wish_patch]["center"]
	var from: Vector3 = rv.roots.graph.positions[Diary.newest_tip(rv.roots)]
	t.check(way.normalized().dot(Vector3(c.x - from.x, 0, c.z - from.z).normalized()) > 0.999, "the needle points at today's wish")
	rv._wish_glows = rv._wish_glows.filter(func(g: Dictionary) -> bool: return not bool(g["today"]))
	t.check(rv._wish_glows.size() > 0, "missed glows tonight")
	t.check_eq(rv.wish_way(), Vector3.ZERO, "no needle for a missed one")
	rv.free()


func test_missed_glows_survive_a_save_and_old_saves_load() -> void:
	var r := _missing(27, 4)
	var diary: Diary = r[2]
	var back := Diary.from_dict(JSON.parse_string(JSON.stringify(diary.to_dict())))
	t.check_eq(back.missed, diary.missed, "the missed glows are saved")
	var old := diary.to_dict()
	old.erase("missed")
	old["last_patch"] = 5
	var loaded := Diary.from_dict(old)
	t.check_eq(loaded.missed, PackedInt32Array([5]), "an old save's yesterday's wish glows on")
	old["last_patch"] = -1
	t.check_eq(Diary.from_dict(old).missed.size(), 0, "and none when it had none")
