class_name LookDev
extends RefCounted
## Applies the look-test changes to a running TreeView without touching tree/: the spray crown,
## and later light and grade. Everything here is meant to move into tree/ once Simon approves.


static func apply(view: TreeView) -> void:
	var leaves: MultiMeshInstance3D = view._leaves
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.use_custom_data = true
	mm.mesh = CrownSprays.card_mesh()
	var crown := CrownSprays.populate(mm, view.state.sim, view.state.seed)
	var mat := CrownSprays.material()
	mat.set_shader_parameter("crown_centre", crown.get_center())
	mat.set_shader_parameter("crown_radii", crown.size * 0.5 + Vector3.ONE * 0.5)
	leaves.multimesh = mm
	leaves.material_override = mat
	print("look dev: %d sprays, crown %s" % [mm.instance_count, crown])
	_apply_light_and_grade(view)


## Grey-brown linden bark instead of the warm orange; filmic AgX tone mapping (softer
## highlights, no neon greens); a little less haze. (A far depth blur was tried and dropped:
## on the Mobile renderer it softened the whole crown and costs a full-screen pass.)
static func _apply_light_and_grade(view: TreeView) -> void:
	view._bark_mat.set_shader_parameter("texture_tint", Vector3(0.38, 0.36, 0.33))
	var env: Environment = view._env
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.adjustment_saturation = 1.0
	env.adjustment_contrast = 1.12
	# Less milky haze over the hero tree; the forest wall still recedes.
	env.fog_density = 0.007
	env.fog_aerial_perspective = 0.25
	if view.get_node_or_null("LookDevTuner") == null:
		var tuner := LookDevTuner.new()
		tuner.name = "LookDevTuner"
		tuner.view = view
		view.add_child(tuner)
	if not view.has_meta("lookdev_forest"):
		view.set_meta("lookdev_forest", true)
		ForestSprays.apply(view._scenery)
	if not view.has_meta("lookdev_rocks"):
		view.set_meta("lookdev_rocks", true)
		RockLook.apply_meadow(view._meadow)
