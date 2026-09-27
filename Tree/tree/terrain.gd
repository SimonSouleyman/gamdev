class_name Terrain
extends RefCounted
## The ground of the clearing is not flat (Simon, play test 3): a level spot where the tree
## stands, gentle swells and hollows in the meadow, and ground that rises a little toward the
## forest, as in a real woodland clearing. Everything that stands on the ground asks here.
## Rendering only; the simulation and the underground keep y = 0 as the surface.

## Radius of the level spot around the trunk.
const FLAT_RADIUS: float = 3.5
## Where the meadow meets the forest (set by Scenery with the clearing, which grows with the tree).
static var edge: float = 18.0

static var _noise: FastNoiseLite


static func _n() -> FastNoiseLite:
	if _noise == null:
		_noise = FastNoiseLite.new()
		_noise.seed = 17
		_noise.frequency = 0.07
		_noise.fractal_octaves = 3
	return _noise


static func height(x: float, z: float) -> float:
	var d := Vector2(x, z).length()
	var open := smoothstep(FLAT_RADIUS, FLAT_RADIUS + 6.0, d)
	var swell := _n().get_noise_2d(x, z) * 1.4
	var ripple := _n().get_noise_2d(x * 3.1 + 40.0, z * 3.1) * 0.12
	# The ground rises gently toward the trees.
	var rim := smoothstep(edge * 0.6, edge + 12.0, d) * 1.6
	return open * (swell + ripple) + rim


static func at(p: Vector3) -> Vector3:
	return Vector3(p.x, height(p.x, p.z), p.z)


## A square grid mesh `size` metres wide with `cells` cells per side, heights from height().
## Finer in the middle than at the edge (cells bunch toward the centre).
static func ground_mesh(size: float, cells: int) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var coords := PackedFloat32Array()
	for i in range(cells + 1):
		var t := float(i) / cells * 2.0 - 1.0
		# Denser near 0: x = sign * |t|^1.6 * half size.
		coords.append(signf(t) * pow(absf(t), 1.6) * size * 0.5)
	for j in range(cells):
		for i in range(cells):
			var x0 := coords[i]
			var x1 := coords[i + 1]
			var z0 := coords[j]
			var z1 := coords[j + 1]
			var a := Vector3(x0, height(x0, z0), z0)
			var b := Vector3(x1, height(x1, z0), z0)
			var c := Vector3(x1, height(x1, z1), z1)
			var d := Vector3(x0, height(x0, z1), z1)
			for v in [a, c, b, a, d, c]:
				st.set_uv(Vector2(v.x, v.z) * 0.1)
				st.add_vertex(v)
	st.generate_normals()
	return st.commit()
