extends SceneTree
## Renders a row of look-test boulders on the meadow floor in afternoon light, for judging the
## rock look on its own. Run: godot --path . -s lookdev/rocks/rocks_shot.gd -- --shots=<folder>

var shots_dir := ""
var frame := 0


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
	DirAccess.make_dir_recursive_absolute(shots_dir)
	var world := Node3D.new()
	root.add_child(world)
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	sky.sky_material = PhysicalSkyMaterial.new()
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	var we := WorldEnvironment.new()
	we.environment = env
	world.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.shadow_enabled = true
	sun.light_energy = 1.6
	sun.light_color = Color(1.0, 0.93, 0.82)
	world.add_child(sun)
	sun.look_at_from_position(Vector3(4, 3, 2), Vector3.ZERO)
	var ground := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(30, 30)
	ground.mesh = pm
	var gm := StandardMaterial3D.new()
	gm.albedo_color = Color(0.3, 0.36, 0.18)
	ground.material_override = gm
	world.add_child(ground)
	var mat := RockLook.material()
	for i in range(5):
		var r := MeshInstance3D.new()
		r.mesh = RockMesh.build(i)
		r.material_override = mat
		var s := 0.4 + 0.25 * i
		r.scale = Vector3.ONE * s
		r.position = Vector3(-4.0 + i * 2.0, s * 0.45, -i * 0.4)
		r.rotation.y = i * 1.7
		world.add_child(r)
	var cam := Camera3D.new()
	world.add_child(cam)
	cam.look_at_from_position(Vector3(0.5, 1.5, 3.8), Vector3(0.5, 0.4, -0.5))


func _process(_delta: float) -> bool:
	frame += 1
	if frame == 8:
		RenderingServer.force_draw(false)
		root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join("rocks.png"))
		quit()
	return false
