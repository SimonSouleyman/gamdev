class_name FieldLook
extends RefCounted
## 0.8.2 (specs/root-field-extras.md 1 and 2): the meshes of the wider field's rock bands and
## soft veins, and the far view's root lines. Each is one merged mesh (one draw call), whatever the
## number of bands, veins or roots, so the far view keeps the 30 fps budget.

## Vertices around a band's cross-section (a rounded box from the meadow down to its bottom).
const BAND_RING: int = 28
## A soft vein's tube.
const VEIN_RING: int = 10
## The far view's root lines: a three-sided tube per segment, a node every FAR_STRIDE nodes.
const FAR_SIDES: int = 3
const FAR_STRIDE: int = 2
## The far view's roots: older nights, the last RECENT_NIGHTS a little brighter, the newest the
## brightest (vertex colours of an unshaded material; glow picks the bright ones up).
const FAR_OLD := Color(0.62, 0.5, 0.36)
const FAR_RECENT := Color(1.0, 0.8, 0.5)
const FAR_NEWEST := Color(1.5, 1.12, 0.62)
static var recent_nights: int = 7


## All rock bands as one rocky wall mesh (the rock shader reads COLOR.r as cavity). Null if none.
static func band_mesh(ground: Underground) -> ArrayMesh:
	if ground.bands.is_empty():
		return null
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var noise := FastNoiseLite.new()
	noise.seed = ground.seed
	noise.frequency = 0.45
	var any := false
	for b in ground.bands:
		var pts: PackedVector2Array = b["points"]
		var open: PackedByteArray = b["open"]
		var half := float(b["half"])
		var bottom := float(b["bottom"])
		# Each run of closed segments is one wall piece, rounded at its two ends.
		var s := 0
		while s < open.size():
			if open[s] != 0:
				s += 1
				continue
			var e := s
			while e < open.size() and open[e] == 0:
				e += 1
			_wall_piece(st, pts, s, e, half, bottom, noise)
			any = true
			s = e
	if not any:
		return null
	st.generate_normals()
	return st.commit()


## One closed run of a band, points s..e: rings of a rounded box swept along the line, the ends
## pulled in to a rounded cap.
static func _wall_piece(st: SurfaceTool, pts: PackedVector2Array, s: int, e: int, half: float, bottom: float, noise: FastNoiseLite) -> void:
	var rings: Array = []
	var line: Array = []
	var t0 := (pts[s + 1] - pts[s]).normalized()
	var t1 := (pts[e] - pts[e - 1]).normalized()
	# Rounded ends: two shrinking rings beyond each end.
	line.append([pts[s] - t0 * half * 0.85, t0, 0.35])
	line.append([pts[s] - t0 * half * 0.5, t0, 0.8])
	for i in range(s, e + 1):
		var t := (pts[mini(i + 1, e)] - pts[maxi(i - 1, s)]).normalized()
		line.append([pts[i], t, 1.0])
	line.append([pts[e] + t1 * half * 0.5, t1, 0.8])
	line.append([pts[e] + t1 * half * 0.85, t1, 0.35])
	for entry in line:
		var c: Vector2 = entry[0]
		var t: Vector2 = entry[1]
		var k := float(entry[2])
		var n := Vector2(-t.y, t.x)
		var ring := PackedVector3Array()
		var cav := PackedFloat32Array()
		for j in range(BAND_RING):
			var a := TAU * j / BAND_RING
			# Superellipse: boxy, with rounded corners.
			var cx := signf(cos(a)) * pow(absf(cos(a)), 0.5)
			var cy := signf(sin(a)) * pow(absf(sin(a)), 0.5)
			var w := half * k * cx
			var depth := bottom * 0.5 + bottom * 0.5 * cy * lerpf(0.85, 1.0, k)
			var p := Vector3(c.x + n.x * w, -depth, c.y + n.y * w)
			# Lumpy faces: big boulder-sized bulges and a finer grain, so it reads as piled rock.
			var bump := noise.get_noise_3dv(p * 0.6) * 1.1 + noise.get_noise_3dv(p * 2.0) * 0.25
			p += Vector3(n.x, 0.0, n.y) * signf(cx) * maxf(bump, -0.35 * half) * absf(cx)
			p += Vector3(t.x, 0.0, t.y) * noise.get_noise_3dv(p * 1.3 + Vector3(5, 5, 5)) * 0.25
			p.y = minf(p.y + bump * 0.6, 0.05)
			ring.append(p)
			cav.append(clampf(0.75 + 0.5 * noise.get_noise_3dv(p * 2.3 + Vector3(9, 9, 9)), 0.4, 1.0))
		rings.append([ring, cav])
	for r in range(rings.size() - 1):
		var a: PackedVector3Array = rings[r][0]
		var b: PackedVector3Array = rings[r + 1][0]
		var ca: PackedFloat32Array = rings[r][1]
		var cb: PackedFloat32Array = rings[r + 1][1]
		for j in range(BAND_RING):
			var j2 := (j + 1) % BAND_RING
			_tri(st, a[j], b[j], b[j2], ca[j], cb[j], cb[j2])
			_tri(st, a[j], b[j2], a[j2], ca[j], cb[j2], ca[j2])
	# Close the two ends with a fan.
	for end in [0, rings.size() - 1]:
		var ring: PackedVector3Array = rings[end][0]
		var centre := Vector3.ZERO
		for p in ring:
			centre += p
		centre /= ring.size()
		for j in range(BAND_RING):
			var j2 := (j + 1) % BAND_RING
			if end == 0:
				_tri(st, centre, ring[j2], ring[j], 0.6, 0.6, 0.6)
			else:
				_tri(st, centre, ring[j], ring[j2], 0.6, 0.6, 0.6)


static func _tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, ca: float, cb: float, cc: float) -> void:
	st.set_color(Color(ca, ca, ca))
	st.add_vertex(a)
	st.set_color(Color(cb, cb, cb))
	st.add_vertex(b)
	st.set_color(Color(cc, cc, cc))
	st.add_vertex(c)


## All soft veins as one tube mesh (vertex colour: lighter, crumbly soil). Null if none.
static func vein_mesh(ground: Underground) -> ArrayMesh:
	if ground.veins.is_empty():
		return null
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var noise := FastNoiseLite.new()
	noise.seed = ground.seed + 7
	noise.frequency = 1.3
	for v in ground.veins:
		var pts: PackedVector3Array = v["points"]
		var r := float(v["radius"])
		var rings: Array = []
		for i in range(pts.size()):
			var t := (pts[mini(i + 1, pts.size() - 1)] - pts[maxi(i - 1, 0)]).normalized()
			var side := t.cross(Vector3.UP).normalized()
			var up := side.cross(t).normalized()
			# The tube narrows to its two ends.
			var k := minf(1.0, minf(float(i), float(pts.size() - 1 - i)) / 2.0) * 0.7 + 0.3
			var ring := PackedVector3Array()
			for j in range(VEIN_RING):
				var a := TAU * j / VEIN_RING
				var p := pts[i] + (side * cos(a) + up * sin(a)) * r * k
				p += (p - pts[i]).normalized() * noise.get_noise_3dv(p) * r * 0.25
				ring.append(p)
			rings.append(ring)
		for i in range(rings.size() - 1):
			var a: PackedVector3Array = rings[i]
			var b: PackedVector3Array = rings[i + 1]
			for j in range(VEIN_RING):
				var j2 := (j + 1) % VEIN_RING
				var shade := 0.85 + 0.3 * noise.get_noise_3dv(a[j] * 3.0)
				st.set_color(Color(0.62 * shade, 0.5 * shade, 0.34 * shade, 1.0))
				st.add_vertex(a[j])
				st.add_vertex(b[j])
				st.add_vertex(b[j2])
				st.add_vertex(a[j])
				st.add_vertex(b[j2])
				st.add_vertex(a[j2])
	st.generate_normals()
	return st.commit()


## The far view's roots: every main root as a line of three-sided tubes `radius` thick (side and
## fine roots are left out: too fine at that distance), coloured by the night it grew.
static func far_roots_mesh(roots: RootSystem, radius: float) -> ArrayMesh:
	var g := roots.graph
	var newest := roots.main_root_count - 1
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var any := false
	# The stride grows so a long network stays within Budgets.FAR_VIEW_SEGMENTS.
	var mains := 0
	for id in range(1, g.size()):
		if roots.root_level(id) == 0:
			mains += 1
	var stride := maxi(FAR_STRIDE, ceili(float(mains) / Budgets.FAR_VIEW_SEGMENTS))
	var segments := 0
	# Walk each main-root node back `stride` nodes along its own root.
	for id in range(1, g.size()):
		var fl = g.flags[id]
		if fl == null or (fl as Dictionary).has("fine") or not (fl as Dictionary).has("main"):
			continue
		var main := int((fl as Dictionary)["main"])
		if id % stride != 0 and not g.is_tip(id):
			continue
		if segments >= Budgets.FAR_VIEW_SEGMENTS:
			break
		var from := g.parents[id]
		for _k in range(stride - 1):
			if from <= 0:
				break
			from = g.parents[from]
		var a := g.positions[maxi(from, 0)]
		var b := g.positions[id]
		if a.distance_squared_to(b) < 1e-6:
			continue
		_far_segment(st, a, b, radius, far_color(main, newest))
		segments += 1
		any = true
	if not any:
		return null
	return st.commit()


## A main root's colour in the far view by the night it grew (`main`) and the newest night.
static func far_color(main: int, newest: int) -> Color:
	if main == newest:
		return FAR_NEWEST
	if newest - main < recent_nights:
		return FAR_RECENT
	return FAR_OLD


static func _far_segment(st: SurfaceTool, a: Vector3, b: Vector3, radius: float, color: Color) -> void:
	var t := (b - a).normalized()
	var side := t.cross(Vector3.UP)
	if side.length_squared() < 1e-4:
		side = t.cross(Vector3.RIGHT)
	side = side.normalized()
	var up := side.cross(t).normalized()
	var ra := PackedVector3Array()
	var rb := PackedVector3Array()
	for j in range(FAR_SIDES):
		var ang := TAU * j / FAR_SIDES
		var o := (side * cos(ang) + up * sin(ang)) * radius
		ra.append(a + o)
		rb.append(b + o)
	st.set_color(color)
	for j in range(FAR_SIDES):
		var j2 := (j + 1) % FAR_SIDES
		st.add_vertex(ra[j])
		st.add_vertex(rb[j])
		st.add_vertex(rb[j2])
		st.add_vertex(ra[j])
		st.add_vertex(rb[j2])
		st.add_vertex(ra[j2])


## Main-root tips (the ends of the nights' roots) a far-view tap can pick as tonight's start.
static func main_tips(roots: RootSystem) -> PackedInt32Array:
	var g := roots.graph
	var out := PackedInt32Array()
	var has_main_child := {}
	for id in range(1, g.size()):
		if roots.root_level(id) == 0:
			has_main_child[g.parents[id]] = true
	for id in range(1, g.size()):
		if roots.root_level(id) == 0 and not has_main_child.has(id):
			out.append(id)
	return out
