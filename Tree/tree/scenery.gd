class_name Scenery
extends Node3D
## The world around the tree (design doc: "a living, cohesive world"): other trees in the
## distance, grown by the same algorithm as the hero tree; drifting clouds; wildflowers in the
## grass; butterflies around the crown, birds crossing the sky, pollen in the sunlight and
## fireflies at dusk. All mood: nothing here touches the simulation. Seeded like everything else.

const FOREST_TREES := Budgets.FOREST_TREES
## The open middle where the player's tree grows; the underground reaches about as far.
const CLEARING_RADIUS := 18.0
const BUSHES := Budgets.FOREST_BUSHES
const CLOUDS := 16
const FLOWERS := Budgets.MEADOW_FLOWERS
const BUTTERFLIES := 6
const BIRDS := 5

var _rng := RandomNumberGenerator.new()
var _clouds: Array[MeshInstance3D] = []
var _cloud_mats: Array[ShaderMaterial] = []
var _butterflies: MultiMeshInstance3D
var _birds: MultiMeshInstance3D
var _pollen: GPUParticles3D
var _fireflies: GPUParticles3D
var _time: float = 0.0
var _bird_t: float = -1.0
var _bird_from: Vector3
var _bird_to: Vector3
var _bird_wait: float = 8.0
var _butterfly_params: Array = []


func build(seed: int, bark: Material, leaf: Material, noise: Texture2D) -> void:
	# The forest gets its own copies of the materials, which dissolve near the camera.
	bark = bark.duplicate()
	leaf = leaf.duplicate()
	(bark as ShaderMaterial).set_shader_parameter("near_fade", 9.0)
	(leaf as ShaderMaterial).set_shader_parameter("near_fade", 9.0)
	# Depth separation (Simon: the tree must stand out from the forest): the wood is darker,
	# cooler and without the hero tree's rim light.
	(bark as ShaderMaterial).set_shader_parameter("tint_mul", Color(0.62, 0.64, 0.66))
	(leaf as ShaderMaterial).set_shader_parameter("tint_mul", Color(0.66, 0.76, 0.78))
	(bark as ShaderMaterial).set_shader_parameter("rim_strength", 0.0)
	(leaf as ShaderMaterial).set_shader_parameter("rim_strength", 0.0)
	for c in get_children():
		c.queue_free()
	_clouds.clear()
	_cloud_mats.clear()
	_rng.seed = hash([seed, "scenery"])
	_noise = noise
	_build_distant_trees(seed, bark, leaf)
	_near_shed.clear()
	_build_bushes(leaf)
	_build_backdrop()
	_build_clouds(noise)
	_build_flowers()
	_build_butterflies()
	_build_birds()
	_pollen = _motes(Color(1.0, 0.95, 0.75, 0.9), 60, Vector3(5, 3, 5), Vector3(0, 3, 0), 0.025)
	_fireflies = _motes(Color(0.85, 1.0, 0.45, 1.0), 40, Vector3(9, 0.8, 9), Vector3(0, 0.6, 0), 0.04)


# --- the forest around the clearing ----------------------------------------------

## The kinds of trees around the clearing (Simon's reference photos: a closed wall of mixed
## species, different heights and greens). Broadleaves are grown by the real growth model with
## their own shape; the spruce is built directly. Each kind has its own bark and leaf colour.
const KINDS: Array[Dictionary] = [
	{"name": "oak", "apical": 0.12, "crown": 12.0, "photo": 0.3, "nodes": 380, "bark": Color(0.42, 0.37, 0.32), "leaf": Color(0.5, 0.62, 0.36), "card": 0.24, "scale": Vector2(2.6, 3.4)},
	{"name": "beech", "apical": 0.2, "crown": 9.0, "photo": 0.4, "nodes": 340, "bark": Color(0.75, 0.74, 0.7), "leaf": Color(0.66, 0.8, 0.42), "card": 0.22, "scale": Vector2(2.7, 3.6)},
	{"name": "birch", "apical": 0.22, "crown": 4.5, "photo": 0.45, "nodes": 280, "bark": Color(1.45, 1.42, 1.35), "leaf": Color(0.8, 0.92, 0.5), "card": 0.19, "scale": Vector2(2.6, 3.4)},
	{"name": "linden", "apical": 0.1, "crown": 12.0, "photo": 0.5, "nodes": 330, "bark": Color(0.55, 0.47, 0.38), "leaf": Color(0.58, 0.72, 0.4), "card": 0.22, "scale": Vector2(2.5, 3.3)},
	{"name": "spruce", "spruce": true, "bark": Color(0.4, 0.33, 0.28), "leaf": Color(0.36, 0.5, 0.36), "card": 0.45, "scale": Vector2(1.5, 2.1)},
]
## Bump when the forest generator changes, so cached meshes are regrown.
const FOREST_CACHE_VERSION := 10


func _grow_variant(seed: int, variant: int, bark: Material, leaf: Material) -> ArrayMesh:
	var kind: Dictionary = KINDS[variant]
	# Grown once per seed, then kept in the user folder: later starts just load them.
	var key := hash([FOREST_CACHE_VERSION, Budgets.FOREST_VARIANT_NODES, kind, Assets.has_leaf_atlas()])
	var cache := "user://cache/forest_%d_%d_%d.res" % [seed, variant, key]
	if _cache_file_ok(cache):
		var cached := ResourceLoader.load(cache, "", ResourceLoader.CACHE_MODE_IGNORE) as ArrayMesh
		if cached != null and cached.get_surface_count() == 2:
			cached.surface_set_material(0, bark)
			cached.surface_set_material(1, leaf)
			return cached
	var local := RandomNumberGenerator.new()
	local.seed = hash([seed, "background tree growth", variant])
	var mesh: ArrayMesh
	if kind.get("spruce", false):
		mesh = _build_spruce(local, kind)
	else:
		mesh = _build_broadleaf(seed, variant, local, kind)
	if mesh.get_surface_count() == 2:
		DirAccess.make_dir_recursive_absolute("user://cache")
		ResourceSaver.save(mesh, cache, ResourceSaver.FLAG_COMPRESS)
		mesh.surface_set_material(0, bark)
		mesh.surface_set_material(1, leaf)
	return mesh


## A file the engine can read as a resource (so a truncated cache file is skipped quietly).
func _cache_file_ok(path: String) -> bool:
	if not FileAccess.file_exists(path):
		return false
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null or f.get_length() < 64:
		return false
	var magic := f.get_buffer(4).get_string_from_ascii()
	return magic == "RSCC" or magic == "RSRC"


## Removes cached forests of other seeds or older generator versions.
var _current_forest_files: Dictionary = {}


func _prune_cache(seed: int) -> void:
	_current_forest_files.clear()
	for v in range(KINDS.size()):
		var key := hash([FOREST_CACHE_VERSION, Budgets.FOREST_VARIANT_NODES, KINDS[v], Assets.has_leaf_atlas()])
		_current_forest_files["forest_%d_%d_%d.res" % [seed, v, key]] = true
	var dir := DirAccess.open("user://cache")
	if dir == null:
		return
	for f in dir.get_files():
		if f.begins_with("forest_") and not _current_forest_files.has(f):
			dir.remove(f)


func _build_broadleaf(seed: int, variant: int, local: RandomNumberGenerator, kind: Dictionary) -> ArrayMesh:
	var sim := GrowthSim.new(hash([seed, "background tree", variant]))
	sim.species.apical_dominance = kind["apical"]
	sim.species.max_crown_radius = kind["crown"]
	sim.species.phototropism = kind["photo"]
	sim.graph.max_nodes = mini(int(kind["nodes"]), Budgets.FOREST_VARIANT_NODES)
	# Grown quickly with small steps (big steps would make bushes): only the shape matters here.
	sim.max_growth_per_second = 6.0
	sim.markers_per_second = 30.0
	for _step in range(1500):
		for k in range(4):
			sim.resources.stock[k] = 30.0
		sim.clock.time_of_day = sim.clock.daylight_fraction * local.randf_range(0.3, 0.7)
		sim.tick(0.45)
		if sim.graph.is_full():
			break
	var builder := BranchMeshBuilder.new()
	builder.radius_scale = 3.2
	var mesh := ArrayMesh.new()
	var wood := builder.build(sim.graph)
	if wood.get_surface_count() > 0:
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, _tinted(wood.surface_get_arrays(0), kind["bark"]))
	var spots: Array[Transform3D] = []
	var g := sim.graph
	for id in range(2, g.size()):
		# Bare lower trunks: leaves only in the upper crown, so the wood has depth and trunks.
		if g.radii[id] >= 0.05 or g.positions[id].y < 0.4 * sim.height():
			continue
		for k in range(2):
			var s: float = kind["card"] * local.randf_range(0.85, 1.2)
			var off := Vector3(local.randf_range(-1, 1), local.randf_range(-0.5, 0.8), local.randf_range(-1, 1)) * s * 1.5 * float(k)
			spots.append(Transform3D(Basis(Vector3.UP, local.randf() * TAU).scaled(Vector3.ONE * s), g.positions[id] + off))
	_add_leaf_surface(mesh, spots, kind["leaf"], local)
	return mesh


## A spruce: a straight trunk and tiers of dark leaf cards narrowing to a point.
func _build_spruce(local: RandomNumberGenerator, kind: Dictionary) -> ArrayMesh:
	var h := local.randf_range(11.0, 15.0)
	var trunk := PlantGraph.new(Vector3.ZERO, 64)
	var prev := 0
	for i in range(1, 12):
		prev = trunk.add_node(prev, Vector3(local.randf_range(-0.05, 0.05), h * i / 11.0, local.randf_range(-0.05, 0.05)))
	trunk.update_radii()
	var builder := BranchMeshBuilder.new()
	builder.radius_scale = 7.0
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, _tinted(builder.build(trunk).surface_get_arrays(0), kind["bark"]))
	var spots: Array[Transform3D] = []
	var tiers := 14
	for t in range(tiers):
		var y := h * (0.15 + 0.88 * t / float(tiers))
		var r := (1.0 - (y / h)) * 2.8 + 0.35
		var n := int(6 + r * 7)
		for k in range(n):
			var a := TAU * k / n + local.randf() * 0.4
			var d := r * sqrt(local.randf_range(0.2, 1.0))
			var s: float = kind["card"] * local.randf_range(0.8, 1.2) * (0.6 + r * 0.25)
			# Drooping sprays: cards tilt downward toward the outside.
			var basis := Basis(Vector3.UP, a).rotated(Vector3(cos(a + PI * 0.5), 0, sin(a + PI * 0.5)), 0.35).scaled(Vector3(s, s * 0.7, s))
			spots.append(Transform3D(basis, Vector3(cos(a) * d, y - d * 0.25, sin(a) * d)))
	_add_leaf_surface(mesh, spots, kind["leaf"], local)
	return mesh


## Sets a surface's vertex colours (bark tint per kind).
func _tinted(arrays: Array, tint: Color) -> Array:
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var colors := PackedColorArray()
	colors.resize(verts.size())
	colors.fill(tint)
	arrays[Mesh.ARRAY_COLOR] = colors
	return arrays


func _add_leaf_surface(mesh: ArrayMesh, spots: Array[Transform3D], tint: Color, local: RandomNumberGenerator) -> void:
	var cluster := Foliage.cluster_mesh(8, 1.0, Assets.has_leaf_atlas()).surface_get_arrays(0)
	var cv: PackedVector3Array = cluster[Mesh.ARRAY_VERTEX]
	var cn: PackedVector3Array = cluster[Mesh.ARRAY_NORMAL]
	var cu: PackedVector2Array = cluster[Mesh.ARRAY_TEX_UV]
	var ci: PackedInt32Array = cluster[Mesh.ARRAY_INDEX]
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	for xf in spots:
		var v := local.randf_range(0.8, 1.1)
		var col := Color(tint.r * v, tint.g * v, tint.b * v)
		var base := verts.size()
		for i in range(cv.size()):
			verts.append(xf * cv[i])
			normals.append((xf.basis * cn[i]).normalized())
			uvs.append(cu[i])
			colors.append(col)
		for i in ci:
			indices.append(base + i)
	if indices.is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)


func _build_distant_trees(seed: int, bark: Material, leaf: Material) -> void:
	_prune_cache(seed)
	var variants: Array[ArrayMesh] = []
	for v in range(KINDS.size()):
		variants.append(_grow_variant(seed, v, bark, leaf))
	var per_variant: Array = []
	for _v in range(KINDS.size()):
		per_variant.append([])
	# A closed wall of mixed trees around the clearing: a dense first row close to the edge,
	# then deeper wood. Oak and beech dominate, birches at the bright edge, spruce behind.
	var weights := [3, 3, 3, 2, 3]
	for t in range(FOREST_TREES):
		var ang := TAU * (float(t) + _rng.randf() * 0.8) / FOREST_TREES * 3.0
		var front := t % 3 != 2
		var d := CLEARING_RADIUS + (_rng.randf_range(2.6, 7.0) if front else _rng.randf_range(8.0, 24.0))
		var kind := _weighted(weights)
		if front and kind == 4 and _rng.randf() < 0.6:
			kind = 2
		(per_variant[kind] as Array).append(Terrain.at(Vector3(cos(ang) * d, 0, sin(ang) * d)) + Vector3(0, -0.3, 0))
	for v in range(KINDS.size()):
		var spots: Array = per_variant[v]
		if spots.is_empty():
			continue
		var mmi := MultiMeshInstance3D.new()
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = variants[v]
		mm.instance_count = spots.size()
		var sc: Vector2 = KINDS[v]["scale"]
		for i in range(spots.size()):
			var s := _rng.randf_range(sc.x, sc.y)
			mm.set_instance_transform(i, Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3.ONE * s), spots[i]))
		mmi.multimesh = mm
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)


func _weighted(weights: Array) -> int:
	var total := 0
	for w in weights:
		total += int(w)
	var r := _rng.randi() % total
	for i in range(weights.size()):
		r -= int(weights[i])
		if r < 0:
			return i
	return 0


const BACKDROP_SHADER := """
shader_type spatial;
render_mode cull_front, depth_draw_opaque, unshaded;
uniform vec3 tint = vec3(1.0);
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
void fragment() {
	vec2 uv = vec2(UV.x * 6.0, UV.y);
	float crowns = texture(noise, uv * vec2(3.0, 1.5)).r;
	// A soft, rounded crown line: overlapping bumps, fading out at the top instead of a hard cut.
	float bumps = 0.5 + 0.5 * sin(UV.x * 380.0 + crowns * 6.0) * 0.5 + 0.25 * sin(UV.x * 157.0);
	float line = 0.12 + 0.16 * texture(noise, vec2(uv.x * 2.0, 0.3)).r + 0.05 * bumps;
	ALPHA = smoothstep(line, line + 0.06, UV.y);
	float trunk = smoothstep(0.55, 0.6, texture(noise, vec2(uv.x * 14.0, 0.7)).r) * smoothstep(0.55, 0.8, UV.y);
	vec3 leaves = mix(vec3(0.07, 0.11, 0.06), vec3(0.16, 0.24, 0.11), crowns);
	ALBEDO = mix(leaves, vec3(0.1, 0.08, 0.06), trunk * 0.6) * mix(1.0, 0.55, UV.y) * tint;
	ROUGHNESS = 1.0;
}
"""
var _noise: Texture2D
var _wall: MeshInstance3D


## The deep wood behind the trees: a dark ring, so no gap between trunks looks out onto open land.
func _build_backdrop() -> void:
	var wall := MeshInstance3D.new()
	_wall = wall
	var cyl := CylinderMesh.new()
	cyl.top_radius = 52.0
	cyl.bottom_radius = 52.0
	cyl.height = 22.0
	cyl.radial_segments = 64
	cyl.cap_top = false
	cyl.cap_bottom = false
	wall.mesh = cyl
	# Painted deep wood: dark foliage masses, faint trunks, and a ragged tree line on top
	# instead of a hard edge.
	var sh := Shader.new()
	sh.code = BACKDROP_SHADER
	var mat := ShaderMaterial.new()
	mat.shader = sh
	mat.set_shader_parameter("noise", _noise)
	wall.material_override = mat
	wall.position = Vector3(0, 10.0, 0)
	wall.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(wall)


## The edge of the clearing in three layers (Simon, play test 3): low herbs and flowers in
## front, a belt of mixed shrubs at middle height behind them, then the trees. Each shrub species
## has its own size, shape and colour; some flower.
const SHRUBS: Array[Dictionary] = [
	{"name": "hazel", "size": Vector3(2.2, 3.4, 2.2), "leaf": Color(0.62, 0.78, 0.4), "clusters": 110, "card": 0.55, "flowers": Color(0, 0, 0, 0)},
	{"name": "hawthorn", "size": Vector3(1.8, 2.6, 1.8), "leaf": Color(0.48, 0.64, 0.32), "clusters": 90, "card": 0.42, "flowers": Color(0.97, 0.96, 0.92, 1)},
	{"name": "elder", "size": Vector3(2.0, 2.9, 1.9), "leaf": Color(0.4, 0.56, 0.28), "clusters": 100, "card": 0.48, "flowers": Color(0.95, 0.92, 0.75, 1)},
	{"name": "holly", "size": Vector3(1.2, 2.4, 1.2), "leaf": Color(0.22, 0.34, 0.18), "clusters": 80, "card": 0.34, "flowers": Color(0.75, 0.12, 0.1, 1)},
	{"name": "blackthorn", "size": Vector3(2.2, 1.6, 2.0), "leaf": Color(0.36, 0.48, 0.26), "clusters": 90, "card": 0.38, "flowers": Color(0, 0, 0, 0)},
]
## Front band of low herbs, ferns and flowers along the edge.
const EDGE_HERBS := 1100
## Tall flowers among them (foxglove, campion, yarrow, buttercup).
const EDGE_FLOWERS := 500
const EDGE_FLOWER_COLORS: Array[Color] = [Color(0.78, 0.4, 0.66), Color(0.86, 0.3, 0.42), Color(0.95, 0.93, 0.86), Color(0.95, 0.82, 0.25)]


func _build_bushes(forest_leaf: Material) -> void:
	# The shrubs stand in front of the wood and catch more light than its shaded crowns.
	var leaf := forest_leaf.duplicate() as ShaderMaterial
	leaf.set_shader_parameter("tint_mul", Color(1.0, 1.05, 0.95))
	var cluster := Foliage.cluster_mesh(6, 1.0, Assets.has_leaf_atlas()).surface_get_arrays(0)
	var cv: PackedVector3Array = cluster[Mesh.ARRAY_VERTEX]
	var cn: PackedVector3Array = cluster[Mesh.ARRAY_NORMAL]
	var cu: PackedVector2Array = cluster[Mesh.ARRAY_TEX_UV]
	var ci: PackedInt32Array = cluster[Mesh.ARRAY_INDEX]
	var flower_mat := StandardMaterial3D.new()
	flower_mat.vertex_color_use_as_albedo = true
	flower_mat.roughness = 0.7
	for k in range(SHRUBS.size()):
		var kind: Dictionary = SHRUBS[k]
		var size: Vector3 = kind["size"]
		var tint_base: Color = kind["leaf"]
		var verts := PackedVector3Array()
		var normals := PackedVector3Array()
		var uvs := PackedVector2Array()
		var colors := PackedColorArray()
		var indices := PackedInt32Array()
		var blossoms: Array[Vector3] = []
		for _c in range(int(kind["clusters"])):
			var v := Vector3(_rng.randf_range(-1, 1), _rng.randf_range(0, 1), _rng.randf_range(-1, 1))
			if v.length() > 1.0:
				continue
			var xf := Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3.ONE * float(kind["card"]) * _rng.randf_range(0.8, 1.2)), v * size)
			var t := _rng.randf_range(0.8, 1.1) * lerpf(0.7, 1.0, v.y)
			var col := Color(tint_base.r * t, tint_base.g * t, tint_base.b * t)
			var base := verts.size()
			for i in range(cv.size()):
				verts.append(xf * cv[i])
				normals.append(((xf.basis * cn[i]).normalized() + v.normalized()).normalized())
				uvs.append(cu[i])
				colors.append(col)
			for i in ci:
				indices.append(base + i)
			if (kind["flowers"] as Color).a > 0.0 and v.length() > 0.7 and _rng.randf() < 0.8:
				blossoms.append(v * size * 1.03)
		var arrays := []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX] = verts
		arrays[Mesh.ARRAY_NORMAL] = normals
		arrays[Mesh.ARRAY_TEX_UV] = uvs
		arrays[Mesh.ARRAY_COLOR] = colors
		arrays[Mesh.ARRAY_INDEX] = indices
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		mesh.surface_set_material(0, leaf)
		# Blossom umbels or berries as small bright spheres on the outside of the shrub.
		if not blossoms.is_empty():
			var st := SurfaceTool.new()
			st.begin(Mesh.PRIMITIVE_TRIANGLES)
			var sphere := SphereMesh.new()
			sphere.radius = 0.045
			sphere.height = 0.07
			sphere.radial_segments = 6
			sphere.rings = 3
			var sa := sphere.surface_get_arrays(0)
			var sv: PackedVector3Array = sa[Mesh.ARRAY_VERTEX]
			var sn: PackedVector3Array = sa[Mesh.ARRAY_NORMAL]
			var si: PackedInt32Array = sa[Mesh.ARRAY_INDEX]
			var fc: Color = kind["flowers"]
			for b in blossoms:
				for idx in si:
					st.set_color(fc)
					st.set_normal(sn[idx])
					st.add_vertex(sv[idx] + b)
			var fm := st.commit()
			mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, fm.surface_get_arrays(0))
			mesh.surface_set_material(1, flower_mat)
		var mmi := MultiMeshInstance3D.new()
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = mesh
		mm.instance_count = BUSHES / SHRUBS.size()
		for i in range(mm.instance_count):
			var ang := _rng.randf() * TAU
			# The middle layer: a belt of shrubs between the herbs and the first trees, and a few
			# more as undergrowth deeper in the wood.
			var d := CLEARING_RADIUS + (_rng.randf_range(-1.2, 2.2) if i % 4 != 3 else _rng.randf_range(3.0, 12.0))
			var s := _rng.randf_range(0.8, 1.3)
			mm.set_instance_transform(i, Transform3D(Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3(s, s * _rng.randf_range(0.85, 1.2), s)), Terrain.at(Vector3(cos(ang) * d, 0, sin(ang) * d)) + Vector3(0, -0.15, 0)))
		mmi.multimesh = mm
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)
		_split_near_shed(mmi, 3.4)
	_build_edge_herbs()


## The front layer: tall herbs, ferns and flowers along the foot of the shrubs.
func _build_edge_herbs() -> void:
	for layer in [["herbs", Foliage.clump_texture(true, 21), Vector2(0.6, 1.0), Vector2(0.7, 1.2), Color(1, 1, 1)],
			["ferns", Foliage.clump_texture(false, 22), Vector2(0.8, 1.3), Vector2(0.8, 1.2), Color(0.7, 0.85, 0.6)]]:
		var mmi := MultiMeshInstance3D.new()
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_colors = true
		mm.mesh = Foliage.clump_mesh()
		mm.instance_count = EDGE_HERBS / 2
		var tint: Color = layer[4]
		for i in range(mm.instance_count):
			var ang := _rng.randf() * TAU
			var d := CLEARING_RADIUS - _rng.randf_range(0.6, 3.0)
			var w := _rng.randf_range(layer[2].x, layer[2].y)
			var h := _rng.randf_range(layer[3].x, layer[3].y)
			var basis := Basis(Vector3.UP, _rng.randf() * TAU).scaled(Vector3(w, h, w))
			mm.set_instance_transform(i, Transform3D(basis, Terrain.at(Vector3(cos(ang) * d, 0, sin(ang) * d)) + Vector3(0, -0.03, 0)))
			var v := _rng.randf_range(0.85, 1.1)
			mm.set_instance_color(i, Color(tint.r * v, tint.g * v, tint.b * v))
		mmi.multimesh = mm
		var mat := ShaderMaterial.new()
		mat.shader = preload("res://tree/grass_card.gdshader")
		mat.set_shader_parameter("clump_texture", layer[1])
		mat.set_shader_parameter("fade_start", 60.0)
		mat.set_shader_parameter("fade_end", 90.0)
		mmi.material_override = mat
		mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)
		_split_near_shed(mmi, 3.0)
	# Flower heads on thin stems, standing a little above the herbs.
	var head := SphereMesh.new()
	head.radius = 0.06
	head.height = 0.14
	head.radial_segments = 6
	head.rings = 3
	var fmat := StandardMaterial3D.new()
	fmat.vertex_color_use_as_albedo = true
	fmat.roughness = 0.8
	head.material = fmat
	var fmi := MultiMeshInstance3D.new()
	var fm := MultiMesh.new()
	fm.transform_format = MultiMesh.TRANSFORM_3D
	fm.use_colors = true
	fm.mesh = head
	fm.instance_count = EDGE_FLOWERS
	for i in range(EDGE_FLOWERS):
		var ang := _rng.randf() * TAU
		var d := CLEARING_RADIUS - _rng.randf_range(0.8, 3.2)
		var at := Terrain.at(Vector3(cos(ang) * d, 0, sin(ang) * d)) + Vector3(0, _rng.randf_range(0.5, 1.2), 0)
		var k := _rng.randi() % EDGE_FLOWER_COLORS.size()
		# Foxgloves are tall spikes; the others round heads.
		var sc := Vector3(1, 2.6, 1) if k == 0 else Vector3.ONE * _rng.randf_range(0.8, 1.3)
		fm.set_instance_transform(i, Transform3D(Basis.from_scale(sc), at))
		fm.set_instance_color(i, EDGE_FLOWER_COLORS[k])
	fmi.multimesh = fm
	fmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(fmi)
	_split_near_shed(fmi, 3.0)


## Edge plants standing where the (hidden) shed is: moved into their own node, hidden while the
## shed scene shows, so nothing grows through the shed's floor and walls.
var _near_shed: Array[MultiMeshInstance3D] = []


func _split_near_shed(mmi: MultiMeshInstance3D, radius: float) -> void:
	var mm := mmi.multimesh
	var near: Array[int] = []
	var far: Array[int] = []
	for i in range(mm.instance_count):
		var o := mm.get_instance_transform(i).origin
		if Vector2(o.x - Shed.ORIGIN.x, o.z - Shed.ORIGIN.z).length() < radius:
			near.append(i)
		else:
			far.append(i)
	if near.is_empty():
		return
	var parts: Array[MultiMesh] = []
	for ids in [far, near]:
		var m := MultiMesh.new()
		m.transform_format = MultiMesh.TRANSFORM_3D
		m.use_colors = mm.use_colors
		m.mesh = mm.mesh
		m.instance_count = (ids as Array).size()
		for j in range((ids as Array).size()):
			m.set_instance_transform(j, mm.get_instance_transform(ids[j]))
			if mm.use_colors:
				m.set_instance_color(j, mm.get_instance_color(ids[j]))
		parts.append(m)
	mmi.multimesh = parts[0]
	var n := mmi.duplicate() as MultiMeshInstance3D
	n.multimesh = parts[1]
	n.visible = not _shed_open
	add_child(n)
	_near_shed.append(n)


var _shed_open := false


func set_shed_open(on: bool) -> void:
	_shed_open = on
	for n in _near_shed:
		n.visible = not on


# --- sky ------------------------------------------------------------------------

func _build_clouds(noise: Texture2D) -> void:
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE
	quad.orientation = PlaneMesh.FACE_Y
	for i in range(CLOUDS):
		var m := MeshInstance3D.new()
		m.mesh = quad
		var mat := ShaderMaterial.new()
		mat.shader = preload("res://tree/cloud.gdshader")
		mat.set_shader_parameter("noise", noise)
		mat.set_shader_parameter("seed", _rng.randf() * 10.0)
		mat.set_shader_parameter("coverage", _rng.randf_range(0.42, 0.52))
		m.material_override = mat
		m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		var a := _rng.randf() * TAU
		var d := _rng.randf_range(40.0, 260.0)
		m.position = Vector3(cos(a) * d, _rng.randf_range(70.0, 110.0), sin(a) * d)
		var w := _rng.randf_range(70.0, 140.0)
		m.scale = Vector3(w, 1.0, w * _rng.randf_range(0.45, 0.8))
		m.rotation.y = _rng.randf() * TAU
		add_child(m)
		_clouds.append(m)
		_cloud_mats.append(mat)


# --- meadow life ----------------------------------------------------------------

func _build_flowers() -> void:
	var head := CylinderMesh.new()
	head.top_radius = 0.011
	head.bottom_radius = 0.007
	head.radial_segments = 8
	head.height = 0.012
	head.radial_segments = 6
	head.rings = 1
	var mmi := MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = head
	var palette: Array[Color] = [Color(0.95, 0.95, 0.92), Color(1.0, 0.85, 0.2), Color(0.62, 0.45, 0.85), Color(0.4, 0.55, 0.95), Color(0.95, 0.5, 0.6)]
	var spots: Array[Transform3D] = []
	var colors: Array[Color] = []
	# Flowers grow in little drifts, one colour each.
	for _d in range(60):
		var a := _rng.randf() * TAU
		var r := 1.2 + 22.0 * sqrt(_rng.randf())
		var center := Vector3(cos(a) * r, 0, sin(a) * r)
		var col := palette[_rng.randi() % palette.size()]
		for _f in range(FLOWERS / 60):
			var p := Terrain.at(center + Vector3(_rng.randf_range(-1.2, 1.2), 0.0, _rng.randf_range(-1.2, 1.2))) + Vector3(0, _rng.randf_range(0.04, 0.14), 0)
			spots.append(Transform3D(Basis(Vector3.RIGHT, _rng.randf_range(-0.3, 0.3)).scaled(Vector3.ONE * _rng.randf_range(0.7, 1.3)), p))
			colors.append(col * _rng.randf_range(0.9, 1.05))
	mm.instance_count = spots.size()
	for i in range(spots.size()):
		mm.set_instance_transform(i, spots[i])
		mm.set_instance_color(i, colors[i])
	mmi.multimesh = mm
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.roughness = 0.7
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(mmi)


## Two wing quads meeting at the body (x = 0); the shader flaps them.
func _wings_mesh(span: float, depth: float) -> ArrayMesh:
	var hw := span * 0.5
	var hd := depth * 0.5
	var verts := PackedVector3Array([
		Vector3(-hw, 0, -hd), Vector3(0, 0, -hd), Vector3(0, 0, hd), Vector3(-hw, 0, hd),
		Vector3(0, 0, -hd), Vector3(hw, 0, -hd), Vector3(hw, 0, hd), Vector3(0, 0, hd)])
	var uvs := PackedVector2Array([
		Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1),
		Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)])
	var normals := PackedVector3Array()
	for _i in range(8):
		normals.append(Vector3.UP)
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _critters(count: int, span: float, flap: float) -> MultiMeshInstance3D:
	var mmi := MultiMeshInstance3D.new()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = _wings_mesh(span, span * 0.6)
	mm.instance_count = count
	mmi.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://tree/critter.gdshader")
	mat.set_shader_parameter("flap_speed", flap)
	mmi.material_override = mat
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mmi.extra_cull_margin = 200.0
	add_child(mmi)
	return mmi


func _build_butterflies() -> void:
	_butterflies = _critters(BUTTERFLIES, 0.16, 16.0)
	var colors: Array[Color] = [Color(1.0, 0.95, 0.5), Color(0.95, 0.95, 0.92), Color(0.95, 0.5, 0.2), Color(0.45, 0.6, 1.0)]
	_butterfly_params.clear()
	for i in range(BUTTERFLIES):
		_butterflies.multimesh.set_instance_color(i, colors[i % colors.size()])
		_butterflies.multimesh.set_instance_custom_data(i, Color(_rng.randf(), _rng.randf(), 0, 0))
		_butterfly_params.append([_rng.randf_range(1.2, 3.5), _rng.randf_range(0.15, 0.35), _rng.randf() * TAU, _rng.randf_range(0.3, 1.4)])


func _build_birds() -> void:
	_birds = _critters(BIRDS, 0.9, 6.0)
	for i in range(BIRDS):
		_birds.multimesh.set_instance_color(i, Color(0.12, 0.12, 0.14))
		_birds.multimesh.set_instance_custom_data(i, Color(_rng.randf(), _rng.randf(), 0, 0))
	_birds.visible = false


## Small glowing specks drifting in a box: pollen in the sun, fireflies at dusk.
func _motes(color: Color, amount: int, extents: Vector3, center: Vector3, size: float) -> GPUParticles3D:
	var p := GPUParticles3D.new()
	p.amount = amount
	p.lifetime = 6.0
	p.preprocess = 6.0
	p.position = center
	p.visibility_aabb = AABB(-extents * 2.0, extents * 4.0)
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = extents
	pm.gravity = Vector3(0, 0.02, 0)
	pm.initial_velocity_min = 0.02
	pm.initial_velocity_max = 0.12
	pm.direction = Vector3(1, 0.3, 0.2)
	pm.spread = 180.0
	pm.turbulence_enabled = true
	pm.turbulence_noise_strength = 0.4
	pm.turbulence_noise_scale = 3.0
	var fade := Gradient.new()
	fade.set_color(0, Color(color, 0.0))
	fade.set_color(1, Color(color, 0.0))
	fade.add_point(0.25, color)
	fade.add_point(0.75, color)
	var ramp := GradientTexture1D.new()
	ramp.gradient = fade
	pm.color_ramp = ramp
	p.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE * size
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_ENABLED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	mat.vertex_color_use_as_albedo = true
	var dot := GradientTexture2D.new()
	dot.fill = GradientTexture2D.FILL_RADIAL
	dot.fill_from = Vector2(0.5, 0.5)
	dot.fill_to = Vector2(1.0, 0.5)
	var g := Gradient.new()
	g.set_color(0, Color(1, 1, 1, 1))
	g.set_color(1, Color(1, 1, 1, 0))
	dot.gradient = g
	dot.width = 32
	dot.height = 32
	mat.albedo_texture = dot
	quad.material = mat
	p.draw_pass_1 = quad
	p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(p)
	return p


# --- every frame ------------------------------------------------------------------

## `day`: the sun is up. `h`: sun height 0..1. `tree_height`: the hero tree, for butterflies and pollen.
func update(delta: float, day: bool, h: float, sun_color: Color, tree_height: float, camera_pos: Vector3 = Vector3.ZERO) -> void:
	_time += delta
	# The deep wood always rises above the camera's eye line, so no view looks out over it.
	if _wall:
		var top := maxf(18.0, camera_pos.y + 14.0)
		_wall.scale.y = top / 22.0
		_wall.position.y = top * 0.5 - 2.0
		(_wall.material_override as ShaderMaterial).set_shader_parameter("tint", Color(0.55, 0.6, 0.55).lerp(sun_color, 0.1) * (0.5 + 0.5 * clampf(h * 3.0, 0.0, 1.0)))
	# Clouds: drift with the wind, white by day, warm and dim at the low sun.
	var cloud_col := Color(1.0, 0.72, 0.55).lerp(Color(1, 1, 1), clampf(h * 3.0, 0.0, 1.0))
	var bright := 0.35 + 0.75 * clampf(h * 2.5, 0.0, 1.0) if day else 0.3
	for i in range(_clouds.size()):
		var c := _clouds[i]
		c.position.x += delta * 1.2
		if c.position.x > 280.0:
			c.position.x = -280.0
		_cloud_mats[i].set_shader_parameter("sun_color", cloud_col)
		_cloud_mats[i].set_shader_parameter("brightness", bright)
	# Butterflies: only by day, once the tree has leaves, fluttering around the crown.
	_butterflies.visible = day and tree_height > 0.6
	if _butterflies.visible:
		var mm := _butterflies.multimesh
		for i in range(mm.instance_count):
			var prm: Array = _butterfly_params[i]
			var r: float = prm[0] * (0.6 + tree_height * 0.08)
			var t := _time * float(prm[1]) + float(prm[2])
			var p := Vector3(cos(t) * r + sin(t * 2.3) * 0.4, float(prm[3]) + tree_height * 0.25 + sin(t * 3.1) * 0.3, sin(t) * r * 0.8 + cos(t * 1.7) * 0.4)
			var ahead := Vector3(-sin(t), 0, cos(t) * 0.8).normalized()
			var basis := Basis.looking_at(ahead, Vector3.UP)
			mm.set_instance_transform(i, Transform3D(basis, p))
	# Birds: now and then a small flock crosses the sky, flapping.
	_update_birds(delta, day)
	_pollen.emitting = day and h > 0.15
	_pollen.position = Vector3(0, maxf(1.5, tree_height * 0.5), 0)
	_fireflies.emitting = not day


func _update_birds(delta: float, day: bool) -> void:
	if _bird_t < 0.0:
		_birds.visible = false
		_bird_wait -= delta
		if _bird_wait <= 0.0 and day:
			var a := _rng.randf() * TAU
			var h := _rng.randf_range(22.0, 40.0)
			_bird_from = Vector3(cos(a) * 70.0, h, sin(a) * 70.0)
			_bird_to = Vector3(-cos(a + 0.5) * 70.0, h + _rng.randf_range(-5, 5), -sin(a + 0.5) * 70.0)
			_bird_t = 0.0
		return
	_bird_t += delta / 14.0
	if _bird_t >= 1.0:
		_bird_t = -1.0
		_bird_wait = _rng.randf_range(25.0, 60.0)
		return
	_birds.visible = true
	var head := _bird_from.lerp(_bird_to, _bird_t)
	var dir := (_bird_to - _bird_from).normalized()
	var side := dir.cross(Vector3.UP).normalized()
	var basis := Basis.looking_at(dir, Vector3.UP)
	var mm := _birds.multimesh
	for i in range(mm.instance_count):
		# A loose V behind the leader.
		var rank := float((i + 1) / 2)
		var s := -1.0 if i % 2 == 0 else 1.0
		var p := head - dir * rank * 2.2 + side * s * rank * 1.8 + Vector3(0, sin(_time + i) * 0.4, 0)
		mm.set_instance_transform(i, Transform3D(basis, p))
