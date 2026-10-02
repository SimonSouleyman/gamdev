extends SceneTree
## 0.8.2 rock bands and soft veins (specs/root-field-extras.md 2): prints, per seed, the bands and
## veins of the wider field, which far patches have their straight line from the trunk blocked,
## which a vein points at, and whether every far patch has two routes (FieldRoutes).
## Run: godot --headless --path . -s tools/qa_bands.gd -- [--seeds=3,14,27] [Underground.tool_arg ...]


func _initialize() -> void:
	var seeds: Array = [3, 14, 27]
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--seeds="):
			seeds = []
			for s in a.substr(8).split(","):
				seeds.append(int(s))
		else:
			Underground.tool_arg(a)
	for seed in seeds:
		var u := Underground.new(int(seed), 3)
		var far := u.far_patch_ids()
		print("seed %d: %d bands, %d veins, %d far patches, %d dots" % [seed, u.bands.size(), u.veins.size(), far.size(), u.dot_count()])
		for b in u.bands:
			var pts: PackedVector2Array = b["points"]
			var gap := 0
			for o in (b["open"] as PackedByteArray):
				gap += o
			print("  band: %.1f m long, %.2f m thick, %.1f m deep, gap %d segments, from r=%.1f to r=%.1f, target %d" % [
				(pts.size() - 1) * Underground.BAND_STEP, float(b["half"]) * 2.0, float(b["bottom"]), gap, pts[0].length(), pts[-1].length(), int(b["target"])])
		for v in u.veins:
			var vp: PackedVector3Array = v["points"]
			print("  vein: %.1f m long, %.2f m wide, r=%.1f to %.1f, target %d" % [(vp.size() - 1) * Underground.BAND_STEP, float(v["radius"]) * 2.0, Vector2(vp[0].x, vp[0].z).length(), Vector2(vp[-1].x, vp[-1].z).length(), int(v["target"])])
		var blocked := 0
		var two := 0
		for pid in far:
			var c: Vector3 = u.patches[pid]["center"]
			var bl := FieldRoutes.straight_blocked(u, pid)
			if bl:
				blocked += 1
			var routes := FieldRoutes.routes(u, pid)
			if routes >= 2:
				two += 1
			print("    patch %d (r=%.1f): straight %s, routes %d" % [pid, Vector2(c.x, c.z).length(), "blocked" if bl else "clear", routes])
		print("  blocked %d of %d, two routes %d of %d" % [blocked, far.size(), two, far.size()])
	quit(0)
