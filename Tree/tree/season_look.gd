class_name SeasonLook
extends RefCounted
## The seasons as a look (design doc section 17, part 4): tints the hero crown, the forest and
## shrubs, the edge herbs and the meadow after Almanac.season_look. Growth is not touched. Spring
## is a light fresh green, summer a deep green, autumn yellow, orange and red, and from late
## November the late autumn look holds (browner, thinner crowns, straw-coloured grass).

## How each species turns in autumn: 0 yellow .. 1 red.
const AUTUMN_HUE := {"linden": 0.12, "birch": 0.02, "beech": 0.5, "sycamore": 0.3, "alder": 0.2, "oak": 0.58}


## Grass colour for a season look: summer deep green, spring fresher, autumn and late autumn drier.
static func grass_tint(look: Dictionary) -> Color:
	var c := Color(0.92, 0.98, 0.9)
	c = c.lerp(Color(1.05, 1.12, 0.8), float(look["fresh"]))
	c = c.lerp(Color(1.1, 0.97, 0.66), float(look["autumn"]) * 0.8)
	c = c.lerp(Color(1.1, 0.86, 0.52), float(look["late"]))
	return c


## Leaf colour on top of the species' tint: summer a little deeper, spring lighter (the shader
## adds the young green and the autumn colours).
static func leaf_tint(look: Dictionary) -> Color:
	return Color(0.95, 0.98, 0.94).lerp(Color(1.04, 1.06, 0.92), float(look["fresh"]))


## Applies the look to every leaf and grass material of the tree view and its scenery.
static func apply(view: TreeView, look: Dictionary, species_id: String) -> void:
	var spray := view._spray_mat
	_set_leaf(spray, look)
	spray.set_shader_parameter("autumn_hue", float(AUTUMN_HUE.get(species_id, 0.15)))
	for mat in leaf_materials(view._scenery):
		_set_leaf(mat, look)
	# 0.8.2.1: the card forest and the painted far wood turn too (they stayed summer green).
	for mat in forest_card_materials(view._scenery):
		_set_leaf(mat, look)
	var grass := grass_tint(look)
	for mat in grass_materials(view):
		mat.set_shader_parameter("season_tint", grass)


static func _set_leaf(mat: ShaderMaterial, look: Dictionary) -> void:
	for k in ["fresh", "autumn", "late"]:
		mat.set_shader_parameter(k, float(look[k]))


## The forest's and shrubs' leaf-spray materials (ForestSprays gives them one shared template).
static func leaf_materials(scenery: Node) -> Array[ShaderMaterial]:
	var out: Array[ShaderMaterial] = []
	for c in scenery.get_children():
		var mmi := c as MultiMeshInstance3D
		if mmi == null or mmi.multimesh == null or not (mmi.multimesh.mesh is ArrayMesh):
			continue
		var mesh := mmi.multimesh.mesh as ArrayMesh
		for s in range(mesh.get_surface_count()):
			var mat := mesh.surface_get_material(s) as ShaderMaterial
			if mat != null and mat.shader != null and mat.shader.resource_path.ends_with("leaf_spray.gdshader") and not out.has(mat):
				out.append(mat)
	return out


## The phone forest's card sets (ForestImpostors) and the painted far wood: materials with an
## `autumn` uniform drawn on the scenery's own nodes.
static func forest_card_materials(scenery: Node) -> Array[ShaderMaterial]:
	var out: Array[ShaderMaterial] = []
	for c in scenery.get_children():
		var g := c as GeometryInstance3D
		if g == null:
			continue
		var mat := g.material_override as ShaderMaterial
		if mat == null or mat.shader == null or out.has(mat):
			continue
		if mat.shader.resource_path.ends_with("forest_impostor.gdshader") or "uniform float autumn" in mat.shader.code:
			out.append(mat)
	return out


## The meadow layers and the herbs and ferns along the edge.
static func grass_materials(view: TreeView) -> Array[ShaderMaterial]:
	var out: Array[ShaderMaterial] = []
	var nodes: Array = [view._grass, view._herbs, view._meadow2]
	# The ferns and flowers in the crown's shade turn with the meadow.
	if view._understory != null:
		nodes.append_array(view._understory.get_children())
	nodes.append_array(view._scenery.get_children())
	for n in nodes:
		var mmi := n as MultiMeshInstance3D
		if mmi == null:
			continue
		var mat := mmi.material_override as ShaderMaterial
		if mat != null and mat.shader != null and (mat.shader.resource_path.ends_with("grass_card.gdshader") or mat.shader.resource_path.ends_with("meadow_grass.gdshader")) and not out.has(mat):
			out.append(mat)
	return out


## Sets a uniform on all grass materials (dew, wet).
static func set_grass(view: TreeView, param: String, value: float) -> void:
	for mat in grass_materials(view):
		mat.set_shader_parameter(param, value)
