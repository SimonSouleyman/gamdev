class_name HeroCrown
extends RefCounted
## The player's crown (0.6.2, the tree rebuilt): leaf sprays gathered into leaf masses at the
## twig ends instead of sprinkled evenly along every twig. The leafy nodes (the last few
## segments of each shoot) are grouped into cells; each cell becomes one leaf mass, a rough
## half-ball of sprays facing out and up. Between the masses the sky shows through; each spray
## carries its mass's normal (so a mass shades as one lump with a lit and a shaded side) and an
## occlusion value (dark inside the crown and under each mass, bright at the sunlit rim).
## Rendering only reads the plant graph; seeded, so the crown never flickers between rebuilds.

const COLOR := "res://lookdev/crown/hero_spray_color.png"
const NORMAL := "res://lookdev/crown/hero_spray_normal.png"
## Sprays on the whole crown: a phone pays for each in overdraw.
static var BUDGET: int = 1800 if Budgets.PHONE else 3600
## Twigs thicker than this (pipe-model radius) carry no leaves.
const LEAF_RADIUS := 0.05
## For tools: leaf masses and leafy nodes of the last crown built.
static var last_masses: int = 0
static var last_leafy: int = 0


static func material() -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://tree/hero_crown.gdshader")
	mat.set_shader_parameter("spray_color", load(COLOR))
	mat.set_shader_parameter("spray_normal", load(NORMAL))
	mat.set_shader_parameter("cheap", Budgets.PHONE)
	return mat


## Size of one leaf mass for a tree of this height: a sapling's leaves sit in small bunches,
## a grown linden's in masses a couple of metres across.
static func mass_size(height: float) -> float:
	return clampf(0.55 + height * 0.11, 0.7, 3.2)


## Size of one spray card (a bunch of about twenty leaves): leaves a few centimetres long on a
## sapling, a hand long on a grown tree, so they stay readable from the far camera.
static func spray_size(height: float) -> float:
	return clampf(0.28 + height * 0.04, 0.3, 1.35)


## Care cues baked into the crown (0.6.3), per unit of the signal: new shoots short of nitrogen
## carry fewer and smaller sprays; short of phosphorus or potassium, some leaf masses stay bare
## (inner and lower ones first).
## (0.6.3 review: the shape has to carry the sign on its own in autumn, so it is stronger, and
## the older masses thin a little too, not only the day's new shoots.)
const N_SPRAYS := 0.65
const N_SIZE := 0.42
const N_OLD := 0.4
const PK_BARE := 0.45
## A twig the tree marks for pruning (0.7, notes/marks-0.7.md, review fixes notes/fix-0.7.md):
## its sprays thin by this share at full sign (more in autumn, where it is the first twig to go
## bare) and are smaller; they do not hang (hanging is thirst's sign). Their colour turns dull and
## dry in the shader (hero_crown.gdshader, the strength rides in the cell index).
const MARK_THIN := 0.5
const MARK_THIN_AUTUMN := 0.85
## The marked twig's share of its leaf mass is at least this (a lone tip is still a clump).
const MARK_MIN_SHARE := 1.0
## Its sprays hang towards the ground by this share (0.7 review: 0, the droop was thirst's cue)
## and are this much smaller.
const MARK_HANG := 0.0
const MARK_SMALL := 0.3
## Bark greying (shader: 1 - vertex alpha): a marked twig at full sign, a twig that died back.
const MARK_BARK := 0.9
const WITHERED_BARK := 0.85


## Fills `mm` (TRANSFORM_3D, colours and custom data on) with the crown of `sim`. Returns the
## bounds of the leafy part of the crown. `care` is GameState.care_signals() (empty: none).
## `autumn` (0..1, the season look) makes a marked twig's sign sparser rather than duller.
static func populate(mm: MultiMesh, sim: GrowthSim, seed: int, care: PackedFloat32Array = PackedFloat32Array(), autumn: float = 0.0) -> AABB:
	var tired := sim.tired_nodes()
	var n_short := care[Resources.Kind.NITROGEN] if care.size() == 4 else 0.0
	var pk_short := maxf(care[Resources.Kind.PHOSPHORUS], care[Resources.Kind.POTASSIUM]) if care.size() == 4 else 0.0
	var g := sim.graph
	var height := sim.height()
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "hero crown"])
	var leafy := leafy_nodes(sim)
	if leafy.is_empty():
		mm.instance_count = 0
		return AABB(Vector3(0, maxf(height, 0.2) * 0.5, 0), Vector3.ONE * 0.2)
	var bounds := AABB(g.positions[leafy[0]], Vector3.ZERO)
	for id in leafy:
		bounds = bounds.expand(g.positions[id])
	var centre := bounds.get_center()
	var radii := (bounds.size * 0.5).max(Vector3.ONE * 0.25)
	# Leafy nodes into cells (a fixed offset per tree, so the grid never lines up with the trunk).
	var cell := mass_size(height)
	var offset := Vector3(rng.randf(), rng.randf(), rng.randf()) * cell
	var cells := {}
	for id in leafy:
		var key := Vector3i(((g.positions[id] + offset) / cell).floor())
		var acc: Array = cells.get(key, [Vector3.ZERO, 0, 0, 0.0, 0, Vector3.ZERO])
		acc[0] += g.positions[id]
		acc[1] += 1
		if id >= sim.young_from:
			acc[2] += 1
		if tired.has(id):
			acc[3] = maxf(float(acc[3]), float(tired[id]))
			acc[4] += 1
			acc[5] += g.positions[id]
		cells[key] = acc
	var keys := cells.keys()
	keys.sort()
	var masses: Array = []
	var total_nodes := 0
	for key in keys:
		var acc: Array = cells[key]
		# [centre, nodes, young share, key, mark strength, marked share, marked twig's centre]
		var tired_at: Vector3 = acc[5] / float(acc[4]) if int(acc[4]) > 0 else acc[0] / float(acc[1])
		masses.append([acc[0] / float(acc[1]), int(acc[1]), float(acc[2]) / float(acc[1]), key, float(acc[3]), float(acc[4]) / float(acc[1]), tired_at])
		total_nodes += int(acc[1])
	var mean_nodes := float(total_nodes) / masses.size()
	last_masses = masses.size()
	last_leafy = leafy.size()
	var card := spray_size(height)
	# Enough sprays to close each mass, within the budget.
	var want := 0
	var plan: Array = []
	var shrink: Array = []
	var tired_plan: Array = []
	for m in masses:
		var weight := clampf(sqrt(float(m[1]) / mean_nodes), 0.7, 1.3)
		var r := cell * 0.5 * weight
		var n := clampi(int(3.0 + 8.0 * pow(r / card, 2.0)), 3, 24)
		# A sapling's few shoots carry few leaves; a grown tree's twig stands for many.
		n = mini(n, 1 + int(ceil(float(m[1]) * lerpf(0.3, 3.0, smoothstep(2.0, 15.0, height)))))
		# Care: new shoots short of nitrogen carry fewer, smaller sprays.
		var young: float = lerpf(N_OLD, 1.0, m[2]) * n_short
		n = maxi(1, int(round(n * (1.0 - N_SPRAYS * young))))
		shrink.append(1.0 - N_SIZE * young)
		# Care: short of phosphorus or potassium, a leaf mass stays bare (inner and lower first).
		if pk_short > 0.0:
			var rel_m: Vector3 = (m[0] - centre) / radii
			var inner := clampf(1.0 - rel_m.length() * 0.7 - rel_m.y * 0.3, 0.0, 1.0)
			var roll := float(posmod(hash([seed, "bare", m[3]]), 1000)) / 1000.0
			if roll < pk_short * PK_BARE * (0.6 + 0.8 * inner):
				n = 0
		# A marked twig: its share of the mass (at least MARK_MIN_SHARE, so a short twig still
		# shows) thins out; shape first, so it reads in autumn too.
		var tired_m: float = m[4]
		var tired_n := 0
		if tired_m > 0.0 and n > 0:
			var own := maxi(1, int(round(n * clampf(maxf(float(m[5]), MARK_MIN_SHARE), 0.0, 1.0))))
			tired_n = maxi(1, int(round(own * (1.0 - lerpf(MARK_THIN, MARK_THIN_AUTUMN, clampf(autumn, 0.0, 1.0)) * tired_m))))
			n = n - own + tired_n
		tired_plan.append(tired_n)
		plan.append(n)
		want += n
	var squeeze := minf(1.0, float(BUDGET) / maxf(want, 1.0))
	var count := 0
	for k in range(plan.size()):
		if int(plan[k]) > 0:
			plan[k] = maxi(2, int(round(float(plan[k]) * squeeze)))
		count += int(plan[k])
	mm.instance_count = count
	var i := 0
	for k in range(masses.size()):
		# Each mass has its own random stream, so a care cue that changes one mass (or leaves it
		# bare) never reshuffles the rest of the crown.
		rng.seed = hash([seed, "mass", masses[k][3]])
		var at_mass: Vector3 = masses[k][0]
		var weight := clampf(sqrt(float(masses[k][1]) / mean_nodes), 0.7, 1.3)
		var r := cell * 0.5 * weight
		var rel := (at_mass - centre) / radii
		# The outside of the crown at this mass, and the mass's own "up and out".
		var out := rel.normalized() if rel.length_squared() > 1e-4 else Vector3.UP
		var mass_up := (out + Vector3.UP * 0.6).normalized()
		# Upper, outer masses catch the sun and grow a little yellower; lower inner ones are deeper.
		var sunny := clampf(0.5 + 0.35 * out.y + 0.25 * (rel.length() - 0.6), 0.0, 1.0)
		var mass_tint := Color(1.0, 1.0, 1.0).lerp(Color(1.04, 1.02, 0.88), sunny * rng.randf_range(0.1, 0.6))
		mass_tint = mass_tint * rng.randf_range(0.88, 1.04)
		# The first sprays of a mass with a marked twig are that twig's: around it, dull, hanging.
		var tired_left := mini(int(tired_plan[k]), int(plan[k]))
		for _s in range(int(plan[k])):
			var tired_k: float = masses[k][4] if _s < tired_left else 0.0
			# A direction over the mass, mostly on its upper and outer side.
			var d := Vector3(rng.randfn(), rng.randfn(), rng.randfn()).normalized()
			if d.dot(mass_up) < -0.1:
				d = (d + mass_up * 1.2).normalized()
			var at := at_mass + d * r * rng.randf_range(0.35, 0.9)
			if tired_k > 0.0:
				at = (masses[k][6] as Vector3) + (at - at_mass) * 0.9
			# Leaves face out of the mass and up to the light.
			var face := (d * 0.6 + out * 0.3 + Vector3.UP * 0.35 + Vector3(rng.randf_range(-0.3, 0.3), 0, rng.randf_range(-0.3, 0.3))).normalized()
			var along := (d + Vector3.UP * 0.25 + Vector3(rng.randf_range(-0.6, 0.6), rng.randf_range(-0.3, 0.3), rng.randf_range(-0.6, 0.6)))
			along = along - face * along.dot(face)
			if along.length_squared() < 1e-3:
				along = face.cross(Vector3.RIGHT)
			along = along.normalized()
			# A marked twig's sprays are smaller (and would hang by MARK_HANG).
			if tired_k > 0.0 and MARK_HANG > 0.0:
				along = along.lerp(Vector3.DOWN, MARK_HANG * tired_k).normalized()
				face = (face - along * face.dot(along)).normalized()
			var side := along.cross(face).normalized()
			var s := card * rng.randf_range(0.85, 1.2) / sqrt(squeeze) * float(shrink[k]) * (1.0 - MARK_SMALL * tired_k)
			var basis := Basis(side, along, face).scaled(Vector3(s, s, s))
			# The card's stem end is its origin: shift so the leaves sit around `at`.
			mm.set_instance_transform(i, Transform3D(basis, at - along * s * 0.45))
			# Shading normal: the side of the mass, bent towards the outside of the crown.
			var n := (d * 0.55 + out * 0.45 + Vector3.UP * 0.1).normalized()
			# Occlusion: deep inside the crown, under a mass, on the crown's underside.
			var spray_rel := (at - centre) / radii
			var depth := clampf(1.0 - spray_rel.length(), 0.0, 1.0)
			var under := clampf(-d.dot(mass_up), 0.0, 1.0)
			var low := clampf(-spray_rel.y, 0.0, 1.0)
			var occlusion := clampf(depth * 1.1 + under * 0.55 + low * 0.3, 0.0, 0.92)
			var c := mass_tint * rng.randf_range(0.92, 1.06)
			mm.set_instance_color(i, Color(c.r, c.g, c.b * rng.randf_range(0.9, 1.05), occlusion))
			var e := oct_encode(n)
			# The cell index of the spray picture; a marked twig's strength in its fraction.
			mm.set_instance_custom_data(i, Color(float(rng.randi() % 4) + 0.9 * tired_k, rng.randf(), e.x, e.y))
			i += 1
	return AABB(centre - radii, radii * 2.0)


## The nodes that carry leaves: living, thin, within a few segments of a shoot tip, and above
## the bare lower trunk. A young whip keeps leaves along most of its stem.
static func leafy_nodes(sim: GrowthSim) -> PackedInt32Array:
	var g := sim.graph
	var n := g.size()
	var height := sim.height()
	var reach := clampi(int(10.0 - height * 0.4), 4, 9)
	var bare_below := sim.crown_base()
	var to_tip := segments_to_tip(g)
	var out := PackedInt32Array()
	for id in range(2, n):
		if to_tip[id] <= reach and g.radii[id] < LEAF_RADIUS and g.positions[id].y >= bare_below and not g.get_flag(id, "dead", false):
			out.append(id)
	return out


## Segments from each node to its nearest living leaf tip (children have larger ids); NO_TIP for
## dead wood and for a twig whose shaded end died back (flag "withered", GrowthSim.shade_dieback):
## it stays bare, as it is no leaf cluster in the sim either.
const NO_TIP := 1 << 20


static func segments_to_tip(g: PlantGraph) -> PackedInt32Array:
	var n := g.size()
	var to_tip := PackedInt32Array()
	to_tip.resize(n)
	to_tip.fill(NO_TIP)
	for id in range(n - 1, -1, -1):
		if g.get_flag(id, "dead", false):
			continue
		var best := NO_TIP
		var any := false
		for c in (g.children[id] as Array):
			if not g.get_flag(c, "dead", false) and to_tip[c] < NO_TIP:
				any = true
				best = mini(best, to_tip[c] + 1)
		if any:
			to_tip[id] = best
		else:
			var living := false
			for c in (g.children[id] as Array):
				if not g.get_flag(c, "dead", false):
					living = true
			to_tip[id] = NO_TIP if living or g.get_flag(id, "withered", false) else 0
	return to_tip


## Bark colours per node for the branch mesh (BranchMeshBuilder.node_colors): white, with the
## alpha lowered where the bark greys (bark.gdshader): a marked twig by its sign, the thin wood
## of a twig that died back in the shade.
static func bark_colors(sim: GrowthSim) -> PackedColorArray:
	var g := sim.graph
	var out := PackedColorArray()
	out.resize(g.size())
	out.fill(Color(1, 1, 1, 1))
	var to_tip := segments_to_tip(g)
	for id in range(g.size()):
		if to_tip[id] >= NO_TIP and g.radii[id] < LEAF_RADIUS and not g.get_flag(id, "dead", false):
			out[id] = Color(1, 1, 1, 1.0 - WITHERED_BARK)
	var tired := sim.tired_nodes()
	for id: int in tired:
		out[id] = Color(1, 1, 1, minf(out[id].a, 1.0 - MARK_BARK * float(tired[id])))
	return out


## Octahedral encoding of a unit vector into 0..1 (two floats of instance custom data).
static func oct_encode(v: Vector3) -> Vector2:
	v = v / (absf(v.x) + absf(v.y) + absf(v.z))
	var e := Vector2(v.x, v.z)
	if v.y < 0.0:
		e = Vector2((1.0 - absf(v.z)) * signf(v.x) if v.x != 0.0 else 1.0 - absf(v.z), (1.0 - absf(v.x)) * signf(v.z) if v.z != 0.0 else 1.0 - absf(v.x))
	return e * 0.5 + Vector2(0.5, 0.5)
