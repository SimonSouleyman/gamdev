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
	return clampf(0.36 + height * 0.036, 0.4, 1.35)


## Fills `mm` (TRANSFORM_3D, colours and custom data on) with the crown of `sim`. Returns the
## bounds of the leafy part of the crown.
static func populate(mm: MultiMesh, sim: GrowthSim, seed: int) -> AABB:
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
		var acc: Array = cells.get(key, [Vector3.ZERO, 0])
		acc[0] += g.positions[id]
		acc[1] += 1
		cells[key] = acc
	var keys := cells.keys()
	keys.sort()
	var masses: Array = []
	var total_nodes := 0
	for key in keys:
		var acc: Array = cells[key]
		masses.append([acc[0] / float(acc[1]), int(acc[1])])
		total_nodes += int(acc[1])
	var mean_nodes := float(total_nodes) / masses.size()
	last_masses = masses.size()
	last_leafy = leafy.size()
	var card := spray_size(height)
	# Enough sprays to close each mass, within the budget.
	var want := 0
	var plan: Array = []
	for m in masses:
		var weight := clampf(sqrt(float(m[1]) / mean_nodes), 0.7, 1.3)
		var r := cell * 0.5 * weight
		var n := clampi(int(3.0 + 8.0 * pow(r / card, 2.0)), 3, 24)
		# A sapling's few shoots carry few leaves; a grown tree's twig stands for many.
		n = mini(n, 1 + int(ceil(float(m[1]) * lerpf(0.7, 3.0, smoothstep(3.0, 15.0, height)))))
		plan.append(n)
		want += n
	var squeeze := minf(1.0, float(BUDGET) / maxf(want, 1.0))
	var count := 0
	for k in range(plan.size()):
		plan[k] = maxi(2, int(round(float(plan[k]) * squeeze)))
		count += int(plan[k])
	mm.instance_count = count
	var i := 0
	for k in range(masses.size()):
		var at_mass: Vector3 = masses[k][0]
		var weight := clampf(sqrt(float(masses[k][1]) / mean_nodes), 0.7, 1.3)
		var r := cell * 0.5 * weight
		var rel := (at_mass - centre) / radii
		# The outside of the crown at this mass, and the mass's own "up and out".
		var out := rel.normalized() if rel.length_squared() > 1e-4 else Vector3.UP
		var mass_up := (out + Vector3.UP * 0.6).normalized()
		# Upper, outer masses catch the sun and grow a little yellower; lower inner ones are deeper.
		var sunny := clampf(0.5 + 0.35 * out.y + 0.25 * (rel.length() - 0.6), 0.0, 1.0)
		var mass_tint := Color(1.0, 1.0, 1.0).lerp(Color(1.05, 1.03, 0.86), sunny * rng.randf_range(0.2, 0.8))
		mass_tint = mass_tint * rng.randf_range(0.88, 1.04)
		for _s in range(int(plan[k])):
			# A direction over the mass, mostly on its upper and outer side.
			var d := Vector3(rng.randfn(), rng.randfn(), rng.randfn()).normalized()
			if d.dot(mass_up) < -0.1:
				d = (d + mass_up * 1.2).normalized()
			var at := at_mass + d * r * rng.randf_range(0.35, 0.9)
			# Leaves face out of the mass and up to the light.
			var face := (d * 0.6 + out * 0.3 + Vector3.UP * 0.35 + Vector3(rng.randf_range(-0.3, 0.3), 0, rng.randf_range(-0.3, 0.3))).normalized()
			var along := (d + Vector3.UP * 0.25 + Vector3(rng.randf_range(-0.6, 0.6), rng.randf_range(-0.3, 0.3), rng.randf_range(-0.6, 0.6)))
			along = along - face * along.dot(face)
			if along.length_squared() < 1e-3:
				along = face.cross(Vector3.RIGHT)
			along = along.normalized()
			var side := along.cross(face).normalized()
			var s := card * rng.randf_range(0.85, 1.2) / sqrt(squeeze)
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
			var occlusion := clampf(depth * 0.95 + under * 0.4 + low * 0.3, 0.0, 0.9)
			var c := mass_tint * rng.randf_range(0.92, 1.06)
			mm.set_instance_color(i, Color(c.r, c.g, c.b * rng.randf_range(0.9, 1.05), occlusion))
			var e := oct_encode(n)
			mm.set_instance_custom_data(i, Color(float(rng.randi() % 4), rng.randf(), e.x, e.y))
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
	# Segments from each node to its nearest living tip (children have larger ids).
	var to_tip := PackedInt32Array()
	to_tip.resize(n)
	to_tip.fill(1 << 20)
	for id in range(n - 1, -1, -1):
		if g.get_flag(id, "dead", false):
			continue
		var best := 1 << 20
		var any := false
		for c in (g.children[id] as Array):
			if not g.get_flag(c, "dead", false):
				any = true
				best = mini(best, to_tip[c] + 1)
		to_tip[id] = best if any else 0
	var out := PackedInt32Array()
	for id in range(2, n):
		if to_tip[id] <= reach and g.radii[id] < LEAF_RADIUS and g.positions[id].y >= bare_below and not g.get_flag(id, "dead", false):
			out.append(id)
	return out


## Octahedral encoding of a unit vector into 0..1 (two floats of instance custom data).
static func oct_encode(v: Vector3) -> Vector2:
	v = v / (absf(v.x) + absf(v.y) + absf(v.z))
	var e := Vector2(v.x, v.z)
	if v.y < 0.0:
		e = Vector2((1.0 - absf(v.z)) * signf(v.x) if v.x != 0.0 else 1.0 - absf(v.z), (1.0 - absf(v.x)) * signf(v.z) if v.z != 0.0 else 1.0 - absf(v.x))
	return e * 0.5 + Vector2(0.5, 0.5)
