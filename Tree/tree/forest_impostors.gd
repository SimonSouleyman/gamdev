class_name ForestImpostors
extends RefCounted
## The phone's forest (design doc section 17, part 1): trees and shrubs drawn as single upright
## cards with baked pictures of the real forest kinds (tools/bake_forest_impostors.gd). The 3D
## forest cost the Fairphone about a third of its frame time, nearly all of it in the many
## layers of cut-out leaf sprays; a card is one layer and two triangles. All cards of a set are
## one MultiMesh, so the whole ring is one draw call.

const DIR := "res://assets/forest_impostors"
## Cells per atlas (kinds x sides); the shader's `cells` array has this many slots.
const MAX_CELLS := 10

## Colour per set. The card wall reads fuller and brighter than the 3D ring it replaces (it has
## more trees and no dithered gaps), so its trees are a little darker and cooler, keeping the
## player's tree in front (Simon, play test 4). The shrubs matched the 3D shrubs as they are.
const TINTS := {"trees": Color(0.85, 0.88, 0.93), "shrubs": Color(1.0, 1.0, 1.0)}

static var _cells: Resource


## The baked cells of one set ("trees" or "shrubs"): [{kind, uv, size, x_off, y_min, cuts}].
static func cells(set_name: String) -> Array:
	if _cells == null:
		_cells = load(DIR.path_join("cells.tres"))
	return _cells.get_meta(set_name, [])


## The cells of a set that show this kind (both sides), as indices into cells(set_name).
static func cells_of_kind(set_name: String, kind: int) -> PackedInt32Array:
	var out := PackedInt32Array()
	var all := cells(set_name)
	for i in range(all.size()):
		if int(all[i]["kind"]) == kind:
			out.append(i)
	return out


## One card per plant: `plants` holds [kind, foot position, scale]. The cards are ordered from
## the clearing outwards, so from any camera inside the clearing the near ones draw first and
## hide the ones behind before their pixels are shaded.
static func build(set_name: String, plants: Array, rng: RandomNumberGenerator, material: ShaderMaterial) -> MultiMeshInstance3D:
	var cards := layout(set_name, plants, rng)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = card_mesh()
	mm.instance_count = cards.size()
	var reach := 0.0
	for i in range(cards.size()):
		var xf: Transform3D = cards[i][0]
		mm.set_instance_transform(i, xf)
		mm.set_instance_custom_data(i, cards[i][1])
		reach = maxf(reach, Vector2(xf.origin.x, xf.origin.z).length() + xf.basis.x.length() + xf.basis.y.length())
	var mmi := MultiMeshInstance3D.new()
	mmi.multimesh = mm
	mmi.material_override = material
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The shader turns and places the cards, so the engine cannot know their bounds.
	mmi.custom_aabb = AABB(Vector3(-reach, -5.0, -reach), Vector3(reach * 2.0, 60.0, reach * 2.0))
	return mmi


## The cards for `plants`, in drawing order: [transform, custom data] each. The basis carries
## the card's size in metres; custom data = (cell, trunk across the card, brightness, wind phase).
static func layout(set_name: String, plants: Array, rng: RandomNumberGenerator) -> Array:
	var all := cells(set_name)
	var order := plants.duplicate()
	order.sort_custom(func(a: Array, b: Array) -> bool:
		return Vector2(a[1].x, a[1].z).length_squared() < Vector2(b[1].x, b[1].z).length_squared())
	var out: Array = []
	for p in order:
		var choices := cells_of_kind(set_name, int(p[0]))
		var c := choices[rng.randi() % choices.size()] if not choices.is_empty() else 0
		var cell: Dictionary = all[c]
		var s: float = p[2]
		var size: Vector2 = cell["size"] * s
		var foot: Vector3 = p[1] + Vector3(0.0, float(cell["y_min"]) * s, 0.0)
		out.append([Transform3D(Basis.from_scale(Vector3(size.x, size.y, 1.0)), foot),
			Color(c, cell["x_off"], rng.randf_range(0.85, 1.1), rng.randf() * TAU)])
	return out


## The material for one set, with its atlas and cell rectangles.
static func material(set_name: String) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://tree/forest_impostor.gdshader")
	mat.set_shader_parameter("albedo_atlas", load(DIR.path_join(set_name + "_albedo.png")))
	mat.set_shader_parameter("normal_atlas", load(DIR.path_join(set_name + "_normal.png")))
	var rects := PackedVector4Array()
	var bottom := PackedVector4Array()
	var top := PackedVector4Array()
	for cell in cells(set_name):
		var r: Rect2 = cell["uv"]
		rects.append(Vector4(r.position.x, r.position.y, r.size.x, r.size.y))
		var c: PackedVector2Array = cell["cuts"]
		bottom.append(Vector4(c[0].x, c[0].y, c[1].x, c[1].y))
		top.append(Vector4(c[2].x, c[2].y, c[3].x, c[3].y))
	for a in [rects, bottom, top]:
		a.resize(MAX_CELLS)
	mat.set_shader_parameter("cells", rects)
	mat.set_shader_parameter("cuts_bottom", bottom)
	mat.set_shader_parameter("cuts_top", top)
	var tint: Color = TINTS[set_name]
	mat.set_shader_parameter("tint_mul", Vector3(tint.r, tint.g, tint.b))
	return mat


## A unit card with its corners cut off (an octagon): UV is the card corner a vertex belongs
## to, (0, 1) at the left foot; UV2 the direction the vertex moves along the edge by that
## cell's cut (the shader places everything, from the cell's `cuts_bottom` and `cuts_top`).
static func card_mesh() -> ArrayMesh:
	var corners := [Vector2(0, 1), Vector2(1, 1), Vector2(1, 1), Vector2(1, 0), Vector2(1, 0), Vector2(0, 0), Vector2(0, 0), Vector2(0, 1)]
	var moves := [Vector2(1, 0), Vector2(-1, 0), Vector2(0, 1), Vector2(0, -1), Vector2(-1, 0), Vector2(1, 0), Vector2(0, -1), Vector2(0, 1)]
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var uv2s := PackedVector2Array()
	for i in range(8):
		verts.append(Vector3(corners[i].x - 0.5, 1.0 - corners[i].y, 0.0))
		normals.append(Vector3.BACK)
		uvs.append(corners[i])
		uv2s.append(moves[i])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	# A fan from the first vertex (the octagon is convex).
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 7])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh
