extends SceneTree
## Start/loading screen proposals (0.8.2.7): the loading page's paper with "Tree" under a 2D
## silhouette of a linden grown by the game itself (space colonization, pipe-model radii), not a
## hand-made geometric tree. Five styles:
##   1 a solid ink silhouette of a half-grown linden
##   2 a grown linden as an ink drawing of its skeleton, each stroke as thick as its wood
##   3 the crown as a soft green mass, trunk and main branches drawn through it
##   4 the silhouette over a ground line, its own wood mirrored faintly below it as roots (the
##     game's real roots, joystick runs, read as a scribble at this size: _roots draws them)
##   5 cut paper: the leaf masses as a few flat clusters with a paper shadow
## Each is written at phone size (1116x2484) and as a 450x1000 preview, plus a numbered sheet.
## Run (needs a window): godot --path . -s tools/render_splash.gd -- --out=C:/folder
##   [--styles=1,2,3,4,5] [--seed=N] (overrides every style's own seed)
##   [--apply=N] also writes style N as the game's art: res://ui/splash/splash_tree.png (the
##   drawing alone, transparent, for the loading page) and res://ui/splash/boot_splash.png (the
##   whole page with the title, for project setting application/boot_splash/image).

const W := 1116
const H := 2484
## The page is laid out at the game's 720-wide reference and scaled up.
const REF := Vector2i(720, 1602)
const PREVIEW := Vector2i(450, 1000)
const INK := Color(0.2, 0.16, 0.12)

## Per style: the tree (seed, days, quarter turn as an angle), the layout (top of the tree, the
## ground line, the title) and its German caption for the sheet.
const STYLES := {
	1: {"seed": 7, "days": 14, "turn": 1.047, "top": 330.0, "ground": 1130.0, "title": 1150.0,
		"caption": "1  Halbwüchsige Linde, volle Tinten-Silhouette"},
	2: {"seed": 5, "days": 22, "turn": 2.094, "top": 260.0, "ground": 1130.0, "title": 1150.0,
		"caption": "2  Ausgewachsene Linde als Federzeichnung, Äste so dick wie ihr Holz"},
	3: {"seed": 5, "days": 20, "turn": 2.094, "top": 260.0, "ground": 1130.0, "title": 1150.0,
		"caption": "3  Krone als weiche grüne Masse, Stamm und Hauptäste sichtbar"},
	4: {"seed": 7, "days": 14, "turn": 1.047, "top": 230.0, "ground": 900.0, "title": 1330.0,
		"caption": "4  Silhouette, unter der Erdlinie ihr Geäst blass gespiegelt als Wurzelwerk"},
	5: {"seed": 7, "days": 20, "turn": 2.094, "top": 260.0, "ground": 1130.0, "title": 1150.0,
		"caption": "5  Scherenschnitt: Laubmassen als Papier-Büschel mit Schatten"},
}
var out_dir := ""
var styles: Array[int] = [1, 2, 3, 4, 5]
var seed_override := -1
var apply_style := 0
## --contact=DAYS: a contact sheet of seeds and turns in style 1 instead, to pick trees.
var contact_days := 0
## Grown trees by "seed_days", so styles sharing a tree grow it once.
var _grown := {}


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--out="):
			out_dir = a.substr(6)
		elif a.begins_with("--styles="):
			styles.clear()
			for s in a.substr(9).split(","):
				styles.append(int(s))
		elif a.begins_with("--seed="):
			seed_override = int(a.substr(7))
		elif a.begins_with("--contact="):
			contact_days = int(a.substr(10))
		elif a.begins_with("--apply="):
			apply_style = int(a.substr(8))
	Almanac.season_override = "summer"
	_run.call_deferred()


func _run() -> void:
	if out_dir != "":
		DirAccess.make_dir_recursive_absolute(out_dir)
	if contact_days > 0:
		await _contact()
		quit(0)
		return
	var previews := {}
	for s in styles:
		var data := _tree_data(s)
		var full := await _render_page(s, data, true)
		var prev := full.duplicate() as Image
		prev.resize(PREVIEW.x, PREVIEW.y, Image.INTERPOLATE_LANCZOS)
		previews[s] = prev
		if out_dir != "":
			full.save_png(out_dir.path_join("splash_%d_1116x2484.png" % s))
			prev.save_png(out_dir.path_join("splash_%d_450x1000.png" % s))
		print("style %d written" % s)
		if s == apply_style:
			DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://ui/splash"))
			full.save_png(ProjectSettings.globalize_path("res://ui/splash/boot_splash.png"))
			var bare := await _render_page(s, data, false)
			bare.save_png(ProjectSettings.globalize_path("res://ui/splash/splash_tree.png"))
			print("applied style %d: res://ui/splash/boot_splash.png and splash_tree.png" % s)
	if out_dir != "" and previews.size() > 1:
		var sheet := await _sheet(previews)
		sheet.save_png(out_dir.path_join("splash_sheet.png"))
		print("sheet written")
	quit(0)


# --- the tree, flattened ------------------------------------------------------------------------

## The linden of a style grown by the game, turned and flattened to 2D (metres, y up): node
## points, parents, radii, depth (toward the viewer is larger), the leafy nodes and the roots.
func _tree_data(style: int) -> Dictionary:
	var st: Dictionary = STYLES[style]
	var seed: int = seed_override if seed_override >= 0 else int(st["seed"])
	var days: int = st["days"]
	var key := "%d_%d" % [seed, days]
	if not _grown.has(key):
		var tools := load("res://tools/live_shot.gd")
		var game: GameState = tools.grow(seed, "linden", days)
		print("linden seed %d day %d: %d nodes, %.1f m" % [seed, game.day_number(), game.sim.graph.size(), game.sim.height()])
		_grown[key] = game
	var g: GameState = _grown[key]
	var turn: float = st["turn"]
	var tree := _flatten(g.sim.graph, turn)
	tree["leafy"] = HeroCrown.leafy_nodes(g.sim)
	tree["height"] = g.sim.height()
	tree["roots"] = _flatten(g.roots.graph, turn)
	return tree


func _flatten(g: PlantGraph, turn: float) -> Dictionary:
	var pts := PackedVector2Array()
	var depth := PackedFloat32Array()
	var alive := PackedByteArray()
	pts.resize(g.size())
	depth.resize(g.size())
	alive.resize(g.size())
	var c := cos(turn)
	var s := sin(turn)
	for id in range(g.size()):
		var p := g.positions[id]
		var x := p.x * c + p.z * s
		var z := -p.x * s + p.z * c
		pts[id] = Vector2(x + z * 0.15, p.y)
		depth[id] = z
		alive[id] = 0 if g.get_flag(id, "dead", false) else (2 if g.get_flag(id, "fine", null) != null else 1)
	return {"pts": pts, "parents": g.parents, "radii": g.radii, "depth": depth, "alive": alive}


# --- the page -----------------------------------------------------------------------------------

## The loading page at phone size: the game's paper, the drawing and the title (or, with
## `whole` false, the drawing alone on a clear background).
func _render_page(style: int, data: Dictionary, whole: bool) -> Image:
	var sv := SubViewport.new()
	sv.size = Vector2i(W, H)
	sv.size_2d_override = REF
	sv.size_2d_override_stretch = true
	sv.transparent_bg = not whole
	sv.msaa_2d = Viewport.MSAA_4X
	sv.render_target_update_mode = SubViewport.UPDATE_DISABLED
	root.add_child(sv)
	var page := Control.new()
	page.size = Vector2(REF)
	sv.add_child(page)
	if whole:
		var paper := Panel.new()
		paper.add_theme_stylebox_override("panel", Paper.paper_box(360, 640, 99, "", 40.0, Paper.PAPER, "cream"))
		paper.size = Vector2(REF)
		page.add_child(paper)
	var art := Control.new()
	art.size = Vector2(REF)
	art.draw.connect(func() -> void: draw_style(art, style, data, STYLES[style]))
	page.add_child(art)
	if whole:
		var t := Paper.ink_label("Tree", 96, Paper.INK, true)
		t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		t.size = Vector2(REF.x, 150)
		t.position = Vector2(0, float(STYLES[style]["title"]))
		page.add_child(t)
	for i in range(3):
		await process_frame
	sv.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	var img := sv.get_texture().get_image()
	sv.queue_free()
	img.convert(Image.FORMAT_RGBA8)
	return img


## Draws a style's tree into `c` (720-wide reference). Kept free of the tool so the loading page
## can call it with the same data.
static func draw_style(c: Control, style: int, d: Dictionary, lay: Dictionary) -> void:
	var pts: PackedVector2Array = d["pts"]
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	for id in range(pts.size()):
		if (d["alive"] as PackedByteArray)[id] == 1:
			lo = lo.min(pts[id])
			hi = hi.max(pts[id])
	var top: float = lay["top"]
	var ground: float = lay["ground"]
	# The crown spreads past its nodes by its leaf sprays.
	var spray := 0.55
	var span := Vector2(hi.x - lo.x + spray * 2.0, hi.y + spray)
	var fit := minf((ground - top) / span.y, (c.size.x - 50.0) / span.x)
	var tf := {"base": Vector2(c.size.x * 0.5, ground), "mid": (lo.x + hi.x) * 0.5, "fit": fit}
	var ground_col := Color(INK, 0.55)
	match style:
		1:
			_ground(c, tf, ground_col, 0.32)
			_crown_blobs(c, d, tf, spray, INK, 1.0)
			_wood(c, d, tf, INK, 1.9, 0.0, false)
		2:
			_ground(c, tf, ground_col, 0.36)
			_wood(c, d, tf, Color(INK, 0.92), 1.6, 0.0, true)
			_tip_ticks(c, d, tf, Color(INK, 0.55))
		3:
			_ground(c, tf, ground_col, 0.34)
			_soft_crown(c, d, tf, spray)
			_wood(c, d, tf, Color(INK, 0.85), 1.2, 0.022, true)
		4:
			_mirrored_roots(c, d, tf, Color(INK, 0.3))
			_ground(c, tf, Color(INK, 0.7), 0.42)
			_crown_blobs(c, d, tf, spray, INK, 1.0)
			_wood(c, d, tf, INK, 1.9, 0.0, false)
		5:
			_paper_cut(c, d, tf, spray)


static func _px(tf: Dictionary, p: Vector2) -> Vector2:
	return (tf["base"] as Vector2) + Vector2(p.x - float(tf["mid"]), -p.y) * float(tf["fit"])


static func _ground(c: Control, tf: Dictionary, col: Color, half: float) -> void:
	var b: Vector2 = tf["base"]
	var w := c.size.x * half
	var rng := RandomNumberGenerator.new()
	rng.seed = 11
	var line := PackedVector2Array()
	for i in range(25):
		var x := -w + 2.0 * w * i / 24.0
		line.append(b + Vector2(x, rng.randf_range(-1.2, 1.2) + 2.0))
	c.draw_polyline(line, col, 2.4, true)


## Every living segment as a stroke as thick as its wood (pipe model). `min_r`: thinner twigs
## are left out. `shaky`: a hand's small wobble at each joint.
static func _wood(c: Control, d: Dictionary, tf: Dictionary, col: Color, gain: float, min_r: float, shaky: bool) -> void:
	var pts: PackedVector2Array = d["pts"]
	var parents: PackedInt32Array = d["parents"]
	var radii: PackedFloat32Array = d["radii"]
	var alive: PackedByteArray = d["alive"]
	var fit: float = tf["fit"]
	var rng := RandomNumberGenerator.new()
	rng.seed = 5
	for id in range(1, pts.size()):
		var p := parents[id]
		if p < 0 or alive[id] == 0 or radii[id] < min_r:
			continue
		var a := _px(tf, pts[p])
		var b := _px(tf, pts[id])
		if shaky:
			a += Vector2(rng.randf_range(-0.5, 0.5), rng.randf_range(-0.5, 0.5))
		var w := maxf(radii[id] * 2.0 * fit * gain, 1.1)
		# A thin twig fades a little, as a light pen stroke.
		var cc := Color(col, col.a * clampf(0.45 + w * 0.25, 0.45, 1.0))
		c.draw_line(a, b, cc, w, true)
		if w > 3.0:
			c.draw_circle(b, w * 0.5, cc, true, -1.0, true)
	# The trunk's foot flares into the ground a little.
	var r0 := radii[0] * fit * gain
	var foot := _px(tf, pts[0])
	c.draw_colored_polygon(PackedVector2Array([foot + Vector2(-r0 * 2.0, 1), foot + Vector2(-r0, -r0 * 2.5),
		foot + Vector2(r0, -r0 * 2.5), foot + Vector2(r0 * 2.0, 1)]), col)


## A short leaf tick at each living tip, for the line drawing.
static func _tip_ticks(c: Control, d: Dictionary, tf: Dictionary, col: Color) -> void:
	var pts: PackedVector2Array = d["pts"]
	var parents: PackedInt32Array = d["parents"]
	var leafy: PackedInt32Array = d["leafy"]
	var rng := RandomNumberGenerator.new()
	rng.seed = 9
	for id in leafy:
		if rng.randf() > 0.55:
			continue
		var b := _px(tf, pts[id])
		var dir := (_px(tf, pts[id]) - _px(tf, pts[parents[id]])).normalized().rotated(rng.randf_range(-0.9, 0.9))
		c.draw_line(b, b + dir * rng.randf_range(4.0, 8.0), col, 1.6, true)


## The crown as one solid mass: a disc of leaf spray around every leafy node.
static func _crown_blobs(c: Control, d: Dictionary, tf: Dictionary, r_m: float, col: Color, k: float) -> void:
	var pts: PackedVector2Array = d["pts"]
	var fit: float = tf["fit"]
	var rng := RandomNumberGenerator.new()
	rng.seed = 13
	var parents: PackedInt32Array = d["parents"]
	for id in (d["leafy"] as PackedInt32Array):
		var p := _px(tf, pts[id])
		var q := _px(tf, pts[parents[id]])
		c.draw_circle(p, r_m * fit * k * rng.randf_range(0.7, 0.95), col, true, -1.0, true)
		c.draw_circle(p.lerp(q, 0.5), r_m * fit * k * 0.75, col, true, -1.0, true)
		# A fringe of single leaves, so the outline is leafy rather than round.
		for i in range(4):
			var ang := rng.randf() * TAU
			var at := p + Vector2.from_angle(ang) * r_m * fit * rng.randf_range(0.6, 0.85)
			_leaf(c, at, ang + rng.randf_range(-0.6, 0.6), r_m * fit * rng.randf_range(0.42, 0.6), col)


## One pointed leaf (a linden's heart shape, simplified) from `at`, pointing along `ang`.
static func _leaf(c: Control, at: Vector2, ang: float, length: float, col: Color) -> void:
	var poly := PackedVector2Array()
	for i in range(9):
		var t := float(i) / 8.0
		poly.append(Vector2(t * length, sin(t * PI) * pow(1.0 - t, 0.4) * length * 0.42))
	for i in range(7, 0, -1):
		var t := float(i) / 8.0
		poly.append(Vector2(t * length, -sin(t * PI) * pow(1.0 - t, 0.4) * length * 0.42))
	var xf := Transform2D(ang, at)
	c.draw_colored_polygon(xf * poly, col)


## The crown as a soft green mass: many faint discs, darker inside and below (the shade of a
## crown), lighter toward the sun at the upper left.
static func _soft_crown(c: Control, d: Dictionary, tf: Dictionary, r_m: float) -> void:
	var pts: PackedVector2Array = d["pts"]
	var depth: PackedFloat32Array = d["depth"]
	var fit: float = tf["fit"]
	var leafy: PackedInt32Array = d["leafy"]
	var rng := RandomNumberGenerator.new()
	rng.seed = 17
	var centre := Vector2.ZERO
	for id in leafy:
		centre += pts[id]
	centre /= maxf(1.0, leafy.size())
	var dark := Color(0.24, 0.32, 0.17)
	var mid := Color(0.38, 0.47, 0.25)
	var light := Color(0.6, 0.66, 0.36)
	# Back to front, so the near sprays sit on top.
	var order := Array(leafy)
	order.sort_custom(func(a: int, b: int) -> bool: return depth[a] < depth[b])
	for pass_i in range(3):
		for id in order:
			var p := pts[id]
			var lit := clampf(0.5 + (p - centre).dot(Vector2(-0.6, 0.8)) * 0.25, 0.0, 1.0)
			var col: Color
			var r := r_m * fit
			match pass_i:
				0:
					col = Color(dark, 0.07)
					r *= 1.2
				1:
					col = Color(mid.lerp(dark, 1.0 - lit), 0.2)
					r *= rng.randf_range(0.7, 1.0)
				_:
					if lit < 0.55 or rng.randf() > 0.5:
						continue
					col = Color(light, 0.18)
					r *= 0.55
			var off := Vector2(rng.randf_range(-0.2, 0.2), rng.randf_range(-0.2, 0.2)) * r_m * fit
			c.draw_circle(_px(tf, p) + off, r, col, true, -1.0, true)
			if pass_i == 1:
				var q := _px(tf, pts[(d["parents"] as PackedInt32Array)[id]])
				c.draw_circle(_px(tf, p).lerp(q, 0.5) - off, r * 0.8, Color(col, 0.12), true, -1.0, true)


## The tree's wood mirrored under the ground line, squashed and fading with depth: roots as the
## crown's echo.
static func _mirrored_roots(c: Control, d: Dictionary, tf: Dictionary, col: Color) -> void:
	var pts: PackedVector2Array = d["pts"]
	var parents: PackedInt32Array = d["parents"]
	var radii: PackedFloat32Array = d["radii"]
	var alive: PackedByteArray = d["alive"]
	var fit: float = tf["fit"]
	var ground: float = (tf["base"] as Vector2).y
	var rng := RandomNumberGenerator.new()
	rng.seed = 29
	for id in range(1, pts.size()):
		var p := parents[id]
		if p < 0 or alive[id] == 0:
			continue
		var a := _px(tf, pts[p])
		var b := _px(tf, pts[id])
		a.y = ground + (ground - a.y) * 0.55 + 3.0
		b.y = ground + (ground - b.y) * 0.55 + 3.0
		var w := maxf(radii[id] * 2.0 * fit * 1.6, 0.9)
		var fade := clampf(1.0 - (b.y - ground) / 420.0, 0.15, 1.0)
		c.draw_line(a, b, Color(col, col.a * fade), w, true)


## The tree's own roots under the ground line, faint, each as thick as its wood.
static func _roots(c: Control, r: Dictionary, tf: Dictionary, col: Color) -> void:
	var pts: PackedVector2Array = r["pts"]
	var parents: PackedInt32Array = r["parents"]
	var radii: PackedFloat32Array = r["radii"]
	var fit: float = tf["fit"]
	# Roots spread much wider than the crown: squeezed sideways to the page, depth kept.
	var reach := 0.01
	var deep := 0.01
	for p in pts:
		reach = maxf(reach, absf(p.x))
		deep = maxf(deep, -p.y)
	var squeeze := minf(1.0, (c.size.x * 0.46) / (reach * fit))
	var sink := minf(1.0, 380.0 / (deep * fit))
	var base := _px(tf, Vector2.ZERO)
	for id in range(1, pts.size()):
		var p := parents[id]
		if p < 0:
			continue
		var a := base + Vector2(pts[p].x * squeeze, -pts[p].y * sink) * fit + Vector2(0, 4)
		var b := base + Vector2(pts[id].x * squeeze, -pts[id].y * sink) * fit + Vector2(0, 4)
		var fine := (r["alive"] as PackedByteArray)[id] == 2
		var w := 1.0 if fine else clampf(radii[id] * 2.0 * fit * 1.4, 1.2, 6.0)
		# Fainter with depth, as if seen through the soil.
		var fade := clampf(1.0 - (b.y - base.y) / 420.0, 0.25, 1.0)
		c.draw_line(a, b, Color(col, col.a * fade * (0.35 if fine else 1.0)), w, true)


## Cut paper: trunk and limbs as one brown cut-out, the leaf masses as a dozen flat green
## clusters (each the hull of its leaf sprays, cut with scissors), each with a paper shadow.
static func _paper_cut(c: Control, d: Dictionary, tf: Dictionary, r_m: float) -> void:
	var pts: PackedVector2Array = d["pts"]
	var depth: PackedFloat32Array = d["depth"]
	var leafy: PackedInt32Array = d["leafy"]
	var fit: float = tf["fit"]
	var shadow := Color(0.12, 0.08, 0.04, 0.28)
	var sh := Vector2(5, 7)
	var rng := RandomNumberGenerator.new()
	rng.seed = 23
	_ground(c, tf, Color(INK, 0.5), 0.34)
	# The wood: limbs only (thin twigs disappear under the leaves), shadow first.
	var bark := Color(0.38, 0.26, 0.17)
	# The leaf masses: k-means on the leafy nodes, seeded.
	var k := 22
	var cent: Array[Vector2] = []
	for i in range(k):
		cent.append(pts[leafy[(i * 7919) % leafy.size()]])
	var label := PackedInt32Array()
	label.resize(leafy.size())
	for it in range(12):
		var sum: Array[Vector2] = []
		var cnt: Array[int] = []
		for i in range(k):
			sum.append(Vector2.ZERO)
			cnt.append(0)
		for j in range(leafy.size()):
			var best := 0
			var bd := INF
			for i in range(k):
				var dd := pts[leafy[j]].distance_squared_to(cent[i])
				if dd < bd:
					bd = dd
					best = i
			label[j] = best
			sum[best] += pts[leafy[j]]
			cnt[best] += 1
		for i in range(k):
			if cnt[i] > 0:
				cent[i] = sum[i] / cnt[i]
	var clusters := []
	for i in range(k):
		var ring := PackedVector2Array()
		var z := 0.0
		var n := 0
		for j in range(leafy.size()):
			if label[j] != i:
				continue
			var p := _px(tf, pts[leafy[j]])
			z += depth[leafy[j]]
			n += 1
			for a in range(10):
				ring.append(p + Vector2.from_angle(TAU * a / 10.0) * r_m * fit * 0.95)
		if n == 0:
			continue
		var hull := Geometry2D.convex_hull(ring)
		clusters.append({"poly": _scissor(hull, rng), "z": z / n, "y": cent[i].y})
	clusters.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return a["z"] < b["z"])
	var greens := [Color(0.27, 0.36, 0.2), Color(0.35, 0.45, 0.24), Color(0.45, 0.54, 0.3)]
	for i in range(clusters.size()):
		if i == clusters.size() / 2:
			# The limbs between the far and the near leaf masses.
			_cut_wood(c, d, tf, shadow, sh)
			_cut_wood(c, d, tf, bark, Vector2.ZERO)
		var poly: PackedVector2Array = clusters[i]["poly"]
		var moved := PackedVector2Array()
		for p in poly:
			moved.append(p + sh)
		c.draw_colored_polygon(moved, shadow)
		# Far clusters darker, near ones lighter.
		var g: Color = greens[mini(2, int(3.0 * i / clusters.size()))]
		c.draw_colored_polygon(poly, g)
		# A pale cut edge, as on real paper.
		var edge := poly.duplicate()
		edge.append(poly[0])
		c.draw_polyline(edge, Color(1, 1, 0.9, 0.18), 1.5, true)


static func _cut_wood(c: Control, d: Dictionary, tf: Dictionary, col: Color, off: Vector2) -> void:
	var pts: PackedVector2Array = d["pts"]
	var parents: PackedInt32Array = d["parents"]
	var radii: PackedFloat32Array = d["radii"]
	var alive: PackedByteArray = d["alive"]
	var fit: float = tf["fit"]
	for id in range(1, pts.size()):
		var p := parents[id]
		if p < 0 or alive[id] == 0 or radii[id] < 0.02:
			continue
		var w := maxf(radii[id] * 2.0 * fit * 1.8, 2.5)
		var a := _px(tf, pts[p]) + off
		var b := _px(tf, pts[id]) + off
		c.draw_line(a, b, col, w, true)
		c.draw_circle(b, w * 0.5, col, true, -1.0, true)
	var r0 := radii[0] * fit * 1.8
	var foot := _px(tf, pts[0]) + off
	c.draw_colored_polygon(PackedVector2Array([foot + Vector2(-r0 * 2.2, 2), foot + Vector2(-r0, -r0 * 3.0),
		foot + Vector2(r0, -r0 * 3.0), foot + Vector2(r0 * 2.2, 2)]), col)


## A hull cut by hand: each edge split into short straight cuts, a little off the line.
static func _scissor(hull: PackedVector2Array, rng: RandomNumberGenerator) -> PackedVector2Array:
	var out := PackedVector2Array()
	for i in range(hull.size() - 1):
		var a := hull[i]
		var b := hull[i + 1]
		var steps := maxi(1, int(a.distance_to(b) / 14.0))
		var nrm := (b - a).orthogonal().normalized()
		for s in range(steps):
			var t := float(s) / steps
			out.append(a.lerp(b, t) + nrm * rng.randf_range(-3.5, 2.0))
	return out


func _contact() -> void:
	var seeds := [1, 2, 3, 5, 7, 11, 42, 2026]
	var tile := Vector2i(240, 300)
	var sheet := Image.create(tile.x * 6, tile.y * seeds.size(), false, Image.FORMAT_RGBA8)
	for row in range(seeds.size()):
		var game: GameState = (load("res://tools/live_shot.gd")).grow(seeds[row], "linden", contact_days)
		for col in range(6):
			var turn := col * TAU / 6.0
			var d := _flatten(game.sim.graph, turn)
			d["leafy"] = HeroCrown.leafy_nodes(game.sim)
			d["roots"] = _flatten(game.roots.graph, turn)
			var lay := {"top": 200.0, "ground": 1300.0, "title": 1400.0}
			var sv := SubViewport.new()
			sv.size = Vector2i(720, 1500)
			sv.render_target_update_mode = SubViewport.UPDATE_DISABLED
			root.add_child(sv)
			var bg := ColorRect.new()
			bg.color = Paper.PAPER
			bg.size = Vector2(720, 1500)
			sv.add_child(bg)
			var art := Control.new()
			art.size = Vector2(720, 1500)
			art.draw.connect(func() -> void: draw_style(art, 1, d, lay))
			sv.add_child(art)
			await process_frame
			sv.render_target_update_mode = SubViewport.UPDATE_ONCE
			await RenderingServer.frame_post_draw
			var img := sv.get_texture().get_image()
			sv.queue_free()
			img.convert(Image.FORMAT_RGBA8)
			img.resize(tile.x, tile.y, Image.INTERPOLATE_BILINEAR)
			sheet.blit_rect(img, Rect2i(Vector2i.ZERO, tile), Vector2i(col * tile.x, row * tile.y))
		print("seed %d done" % seeds[row])
	sheet.save_png(out_dir.path_join("contact_day%d.png" % contact_days))


# --- the comparison sheet ----------------------------------------------------------------------

func _sheet(previews: Dictionary) -> Image:
	var gap := 40
	var cap_h := 190
	var keys := previews.keys()
	keys.sort()
	var size := Vector2i(gap + keys.size() * (PREVIEW.x + gap), gap + PREVIEW.y + cap_h)
	var sv := SubViewport.new()
	sv.size = size
	sv.render_target_update_mode = SubViewport.UPDATE_DISABLED
	root.add_child(sv)
	var bg := ColorRect.new()
	bg.color = Color(0.93, 0.92, 0.89)
	bg.size = Vector2(size)
	sv.add_child(bg)
	for i in range(keys.size()):
		var x := gap + i * (PREVIEW.x + gap)
		var tr := TextureRect.new()
		tr.texture = ImageTexture.create_from_image(previews[keys[i]])
		tr.position = Vector2(x, gap)
		tr.size = Vector2(PREVIEW)
		sv.add_child(tr)
		var num := Label.new()
		num.text = str(keys[i])
		num.add_theme_font_size_override("font_size", 64)
		num.add_theme_color_override("font_color", Color(0.15, 0.12, 0.1))
		num.position = Vector2(x, gap + PREVIEW.y + 10)
		sv.add_child(num)
		var cap := Label.new()
		cap.text = (STYLES[keys[i]]["caption"] as String).substr(3)
		cap.autowrap_mode = TextServer.AUTOWRAP_WORD
		cap.add_theme_font_size_override("font_size", 26)
		cap.add_theme_color_override("font_color", Color(0.2, 0.17, 0.14))
		cap.position = Vector2(x + 56, gap + PREVIEW.y + 22)
		cap.size = Vector2(PREVIEW.x - 56, cap_h - 30)
		sv.add_child(cap)
	for i in range(3):
		await process_frame
	sv.render_target_update_mode = SubViewport.UPDATE_ONCE
	await RenderingServer.frame_post_draw
	var img := sv.get_texture().get_image()
	sv.queue_free()
	return img
