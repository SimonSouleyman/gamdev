class_name RockLook
extends RefCounted
## Material and swap helpers for the look-test rocks.


static func noise_texture() -> NoiseTexture2D:
	var tex := NoiseTexture2D.new()
	tex.width = 512
	tex.height = 512
	tex.seamless = true
	tex.generate_mipmaps = true
	var fnl := FastNoiseLite.new()
	fnl.seed = 11
	fnl.frequency = 0.02
	fnl.fractal_octaves = 5
	tex.noise = fnl
	return tex


static func material(moss: float = 0.6, wetness: float = 0.0) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://lookdev/rocks/rock.gdshader")
	mat.set_shader_parameter("noise", noise_texture())
	mat.set_shader_parameter("moss_amount", moss)
	mat.set_shader_parameter("wetness", wetness)
	return mat


## Underground: the sphere rocks of a RootView become fractured, damp boulders.
static func apply_roots(rv: RootView) -> void:
	var mat := material(0.0, 0.45)
	mat.set_shader_parameter("stone_a", Color(0.42, 0.39, 0.36))
	mat.set_shader_parameter("stone_b", Color(0.27, 0.25, 0.23))
	mat.set_shader_parameter("void_fill", 0.25)
	var i := 0
	for c in rv._content.get_children():
		var m := c as MeshInstance3D
		if m == null or not (m.mesh is SphereMesh):
			continue
		var r: float = (m.mesh as SphereMesh).radius
		if r < 0.3:
			continue  # glowing finds are small spheres; rocks are big
		m.mesh = RockMesh.build(i)
		m.material_override = mat
		# The sphere had the rock's radius; the boulder is about as big, turned at random.
		m.scale = Vector3.ONE * r * 1.05
		m.rotation = Vector3(i * 0.7, i * 1.9, i * 0.3)
		i += 1


## Surface: the little sphere stones in the meadow become mossy fractured pebbles.
static func apply_meadow(meadow: Node3D) -> void:
	var mat := material(0.35, 0.0)
	var meshes: Array[ArrayMesh] = []
	for k in range(6):
		meshes.append(RockMesh.build(100 + k, 2))
	var i := 0
	for c in meadow.get_children():
		var m := c as MeshInstance3D
		if m == null or not (m.mesh is SphereMesh):
			continue
		var sm := m.mesh as SphereMesh
		# Stones are the flattened low-poly spheres (height = 1.2 x radius).
		if sm.radial_segments != 7:
			continue
		m.mesh = meshes[i % meshes.size()]
		m.material_override = mat
		m.scale = Vector3.ONE * sm.radius * 1.1
		m.position.y -= sm.radius * 0.2
		i += 1
