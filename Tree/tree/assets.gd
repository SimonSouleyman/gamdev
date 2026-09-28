class_name Assets
extends RefCounted
## The photo textures (all CC0, see assets/CREDITS.md) plugged into the procedural shaders.
## Every function falls back to the procedural look if a file is missing, so the game runs
## without the downloads too.

const BARK_DIFF := "res://assets/bark/tree_bark_03_diff_2k.jpg"
const BARK_NORMAL := "res://assets/bark/tree_bark_03_nor_gl_2k.jpg"
const BARK_ROUGH := "res://assets/bark/tree_bark_03_rough_2k.jpg"
const LEAF_COLOR := "res://assets/leaves/LeafSet004_1K-JPG_Color.jpg"
const LEAF_OPACITY := "res://assets/leaves/LeafSet004_1K-JPG_Opacity.jpg"
const LEAF_NORMAL := "res://assets/leaves/LeafSet004_1K-JPG_NormalGL.jpg"
const GROUND_COLOR := "res://assets/ground/Grass004_1K-JPG_Color.jpg"
## Leaf litter for the forest floor under the crown's shade (the living clearing).
const FLOOR_COLOR := "res://assets/ground/forest_leaves_04_diff_1k.jpg"


static func _tex(path: String) -> Texture2D:
	return load(path) as Texture2D if ResourceLoader.exists(path) else null


static func has_leaf_atlas() -> bool:
	return ResourceLoader.exists(LEAF_COLOR) and ResourceLoader.exists(LEAF_OPACITY)


static func apply_bark(mat: ShaderMaterial) -> void:
	var d := _tex(BARK_DIFF)
	if d == null:
		return
	mat.set_shader_parameter("bark_albedo", d)
	mat.set_shader_parameter("bark_normal", _tex(BARK_NORMAL))
	mat.set_shader_parameter("bark_roughness", _tex(BARK_ROUGH))
	mat.set_shader_parameter("use_textures", true)


static func apply_leaf(mat: ShaderMaterial) -> void:
	if has_leaf_atlas():
		mat.set_shader_parameter("leaf_texture", _tex(LEAF_COLOR))
		mat.set_shader_parameter("leaf_opacity", _tex(LEAF_OPACITY))
		mat.set_shader_parameter("leaf_normal", _tex(LEAF_NORMAL))
		mat.set_shader_parameter("use_atlas", true)
	else:
		mat.set_shader_parameter("leaf_texture", Foliage.leaf_texture())


static func apply_ground(mat: ShaderMaterial) -> void:
	var g := _tex(GROUND_COLOR)
	if g == null:
		return
	mat.set_shader_parameter("ground_albedo", g)
	mat.set_shader_parameter("use_texture", true)
	var f := _tex(FLOOR_COLOR)
	if f != null:
		mat.set_shader_parameter("floor_albedo", f)
		mat.set_shader_parameter("use_floor", true)
