class_name WishPlant
extends Meadow
## Today's wish place on the meadow (0.8.2.5, specs/wish-compass-vial.md item 1): the plant over
## the wish deposit (rushes over water, clover over nitrogen, nettles over phosphorus, comfrey over
## potassium) grows SIZE times the meadow's sign for it, in flower, with a few butterflies by day.
## A far wish beyond the clearing shows it at the clearing's edge in its direction (the meadow
## signs' rule, but not made smaller). At sunset a small ink ring is drawn around it for a moment
## before the dive (ring()). Only reads the ground; the deposit itself is Underground's.

## The stand's radius and its plants' height against the meadow's sign for the same deposit.
const SIZE: float = 1.6
## The ink ring: its radius against the stand's, how long it is drawn and how long it stays.
const RING_SCALE: float = 1.35
const RING_DRAW: float = 0.7
const RING_SECONDS: float = 2.6
const BUTTERFLIES: int = 3
## Clover is low: its wish plants stand taller still, so their heads show over the meadow grass.
const CLOVER_SIZE: float = 2.6

## The patch shown (-1: none) and where its stand grows.
var patch_id: int = -1
var place: Vector3 = Vector3.ZERO
## The stand's radius on the ground.
var stand_radius: float = 0.0
var _butterflies: Array[Node3D] = []
var _ring: MeshInstance3D
var _ring_mat: ShaderMaterial
var _ring_left: float = 0.0
var _light: float = 1.0
var _t: float = 0.0
var _edge: float = INF


## Where the wish plant of `pid` grows: {"position" (on the ground plane), "radius" (the
## stand's), "at_edge" (a far deposit beyond the clearing)}.
static func place_of(ground: Underground, pid: int, edge: float) -> Dictionary:
	var p: Dictionary = ground.patches[pid]
	var c: Vector3 = p["center"]
	var flat := Vector2(c.x, c.z)
	var rim := edge - Underground.EDGE_INSET
	var at_edge := flat.length() > rim
	if at_edge:
		flat = flat.normalized() * maxf(rim, 2.0)
	return {"position": Vector3(flat.x, 0.0, flat.y), "radius": float(p["radius"]) * SIZE, "at_edge": at_edge}


## Shows the plant of wish deposit `pid` (-1: none), rebuilt only when the patch or the clearing changed.
func show_wish(ground: Underground, pid: int, edge: float) -> void:
	if pid == patch_id and edge == _edge:
		return
	patch_id = pid
	_edge = edge
	for c in get_children():
		c.queue_free()
	_sheens.clear()
	_butterflies.clear()
	_ring = null
	if pid < 0 or pid >= ground.patches.size():
		visible = false
		return
	visible = true
	_rng.seed = hash([ground.seed, "wish_plant", pid])
	var at := place_of(ground, pid, edge)
	place = Terrain.at(_clear_of_shed(at["position"]))
	stand_radius = float(at["radius"])
	_build_stand(int(ground.patches[pid]["kind"]))
	for i in range(BUTTERFLIES):
		_butterflies.append(_butterfly(i))
	_build_shimmer()
	_build_ring()


func _build_stand(kind: int) -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var r := stand_radius
	match kind:
		Resources.Kind.WATER:
			_damp_patch(place, r * 1.25)
			_stand(st, r * 0.8, 34, _rush)
		Resources.Kind.NITROGEN:
			_stand(st, r, 60, _clover_plant, CLOVER_SIZE)
		Resources.Kind.PHOSPHORUS:
			_stand(st, r * 0.8, 40, _nettle)
		_:
			_stand(st, r * 0.75, 11, _comfrey)
	st.generate_normals()
	var m := _add(st.commit(), _plant_material(), Vector3.ZERO)
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


## `count` plants within `radius` of the place, each made by `make` at the origin and set down
## SIZE times as large, turned at random (denser toward the middle).
func _stand(st: SurfaceTool, radius: float, count: int, make: Callable, size: float = SIZE) -> void:
	for _i in range(count):
		var a := _rng.randf() * TAU
		var d := radius * pow(_rng.randf(), 0.6)
		var q := place + Vector3(cos(a) * d, 0.0, sin(a) * d)
		if Vector2(q.x, q.z).length() < BARE_RADIUS + 0.2:
			continue
		var one := SurfaceTool.new()
		one.begin(Mesh.PRIMITIVE_TRIANGLES)
		make.call(one, Vector3.ZERO)
		var s := size * _rng.randf_range(0.85, 1.1)
		st.append_from(one.commit(), 0, Transform3D(Basis(Vector3.UP, _rng.randf() * TAU) * Basis.from_scale(Vector3.ONE * s), Terrain.at(q)))


## A rush tuft in flower: tall round stems, a brown flower tuft on the side near the top of some.
func _rush(st: SurfaceTool, at: Vector3) -> void:
	var col := Color(0.22, 0.38, 0.14) * _rng.randf_range(0.85, 1.1)
	for _k in range(_rng.randi_range(6, 10)):
		var lean := Vector3(_rng.randf_range(-0.18, 0.18), 1.0, _rng.randf_range(-0.18, 0.18)).normalized()
		var h := _rng.randf_range(0.5, 0.85)
		var base := at + Vector3(_rng.randf_range(-0.04, 0.04), 0.0, _rng.randf_range(-0.04, 0.04))
		_strip(st, base, base + lean * h, 0.01, col.lightened(_rng.randf() * 0.1))
		if _rng.randf() < 0.55:
			var node := base + lean * h * 0.78
			for b in range(4):
				var q := node + Vector3(_rng.randf_range(-0.025, 0.025), 0.01 * b, _rng.randf_range(-0.025, 0.025))
				_bell(st, q + Vector3(0, 0.03, 0), 0.022, Color(0.48, 0.33, 0.16))


## A clover plant in flower: trefoil leaves low down and one or two round heads, white or pink,
## standing above them.
func _clover_plant(st: SurfaceTool, at: Vector3) -> void:
	var leaf_col := Color(0.24, 0.5, 0.2) * _rng.randf_range(0.9, 1.1)
	for _k in range(_rng.randi_range(3, 5)):
		var yaw := _rng.randf() * TAU
		var stem_top := at + Vector3(cos(yaw) * 0.04, _rng.randf_range(0.05, 0.09), sin(yaw) * 0.04)
		_strip(st, at, stem_top, 0.004, leaf_col.darkened(0.2))
		for j in range(3):
			var a := yaw + TAU * j / 3.0
			_leaf(st, stem_top, Vector3(cos(a), 0.12, sin(a)).normalized(), 0.05, 0.03, leaf_col)
	var pink := _rng.randf() < 0.5
	for _h in range(_rng.randi_range(1, 2)):
		var top := at + Vector3(_rng.randf_range(-0.05, 0.05), _rng.randf_range(0.16, 0.24), _rng.randf_range(-0.05, 0.05))
		_strip(st, at, top, 0.004, leaf_col.darkened(0.25))
		var head := Color(0.86, 0.45, 0.62) if pink else Color(0.96, 0.95, 0.9)
		_head(st, top + Vector3(0, 0.025, 0), 0.032, head)


## A small round flower head (an octahedron, a little taller than wide).
func _head(st: SurfaceTool, c: Vector3, r: float, col: Color) -> void:
	var top := c + Vector3(0, r * 1.2, 0)
	var bottom := c - Vector3(0, r * 0.9, 0)
	var ring: Array[Vector3] = []
	for k in range(6):
		var a := TAU * k / 6.0
		ring.append(c + Vector3(cos(a), 0, sin(a)) * r)
	for k in range(6):
		var a := ring[k]
		var b := ring[(k + 1) % 6]
		_tri(st, top, a, b, col.lightened(0.1), col, col)
		_tri(st, bottom, b, a, col.darkened(0.15), col, col)


## A butterfly: two wings that beat, circling over the stand by day.
func _butterfly(i: int) -> Node3D:
	var colours: Array[Color] = [Color(0.98, 0.9, 0.35), Color(0.97, 0.96, 0.92), Color(0.95, 0.55, 0.2)]
	var mat := StandardMaterial3D.new()
	mat.albedo_color = colours[i % colours.size()]
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	var b := Node3D.new()
	b.set_meta("phase", _rng.randf() * TAU)
	b.set_meta("speed", _rng.randf_range(0.35, 0.6) * (1.0 if i % 2 == 0 else -1.0))
	b.set_meta("height", _rng.randf_range(0.6, 1.1))
	for side in [-1.0, 1.0]:
		var hinge := Node3D.new()
		var wing := MeshInstance3D.new()
		var q := QuadMesh.new()
		q.size = Vector2(0.11, 0.09)
		wing.mesh = q
		wing.material_override = mat
		wing.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		wing.rotation.x = -PI * 0.5
		wing.position = Vector3(0.055 * side, 0.0, 0.0)
		hinge.add_child(wing)
		hinge.set_meta("side", side)
		b.add_child(hinge)
	add_child(b)
	_place_butterfly(b)
	return b


func _place_butterfly(b: Node3D) -> void:
	var a := float(b.get_meta("phase")) + _t * float(b.get_meta("speed"))
	var r := stand_radius * (0.55 + 0.25 * sin(_t * 0.7 + float(b.get_meta("phase"))))
	var h := float(b.get_meta("height")) + 0.15 * sin(_t * 1.9 + float(b.get_meta("phase")))
	b.position = place + Vector3(cos(a) * r, h, sin(a) * r)
	b.rotation.y = -a + (PI if float(b.get_meta("speed")) < 0.0 else 0.0)
	var beat := 0.15 + 1.0 * absf(sin(_t * 14.0 + float(b.get_meta("phase"))))
	for hinge in b.get_children():
		(hinge as Node3D).rotation.z = beat * float(hinge.get_meta("side"))


## A soft shimmer over the stand by day: a faint warm haze with a few motes drifting up.
var _shimmer_mat: ShaderMaterial


func _build_shimmer() -> void:
	var sh := Shader.new()
	sh.code = SHIMMER_SHADER
	_shimmer_mat = ShaderMaterial.new()
	_shimmer_mat.shader = sh
	var q := QuadMesh.new()
	q.size = Vector2.ONE * stand_radius * 2.4
	var m := MeshInstance3D.new()
	m.mesh = q
	m.material_override = _shimmer_mat
	m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	m.position = place + Vector3(0, stand_radius * 0.55, 0)
	m.extra_cull_margin = 2.0
	add_child(m)


## The ink ring: a hand-drawn circle lying on the ground around the stand, drawn round once.
func _build_ring() -> void:
	var sh := Shader.new()
	sh.code = RING_SHADER
	_ring_mat = ShaderMaterial.new()
	_ring_mat.shader = sh
	_ring_mat.set_shader_parameter("seed", _rng.randf() * TAU)
	_ring_mat.render_priority = 2
	var radius := stand_radius * RING_SCALE + 0.3
	_ring = _add(_ground_sheet(place, radius, 0.0, 16), _ring_mat, Vector3(0, 0.08, 0))
	_ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_ring.visible = false


## Draws the ink ring now; it stays RING_SECONDS, then fades.
func ring() -> void:
	if _ring == null:
		return
	_ring_left = RING_SECONDS
	_ring.visible = true


## The ring's state now: how much of it is drawn (0 to 1) and how dark it is (0 to 1).
func ring_state() -> Vector2:
	if _ring == null or _ring_left <= 0.0:
		return Vector2.ZERO
	var t := RING_SECONDS - _ring_left
	return Vector2(clampf(t / RING_DRAW, 0.0, 1.0), clampf(_ring_left / 0.6, 0.0, 1.0))


func set_daylight(light: float) -> void:
	super(light)
	_light = light


func _process(delta: float) -> void:
	if patch_id < 0:
		return
	_t += delta
	var day := _light > 0.35
	if _shimmer_mat != null:
		_shimmer_mat.set_shader_parameter("strength", smoothstep(0.3, 0.8, _light))
	for b in _butterflies:
		b.visible = day
		if day:
			_place_butterfly(b)
	if _ring != null and _ring_left > 0.0:
		_ring_left -= delta
		var s := ring_state()
		_ring_mat.set_shader_parameter("drawn", s.x)
		_ring_mat.set_shader_parameter("alpha", s.y)
		_ring.visible = _ring_left > 0.0


const SHIMMER_SHADER := """
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
uniform float strength = 1.0;
void vertex() {
	// Always facing the camera.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float haze = 1.0 - smoothstep(0.0, 1.0, length(c));
	haze *= haze * (0.8 + 0.2 * sin(TIME * 1.3));
	float motes = 0.0;
	for (int i = 0; i < 7; i++) {
		float f = float(i);
		float rise = fract(TIME * 0.11 + f * 0.37);
		vec2 p = vec2(sin(TIME * 0.4 + f * 1.9) * 0.55, 0.8 - rise * 1.5);
		motes += (1.0 - smoothstep(0.0, 0.045, length(c - p))) * sin(rise * 3.14159);
	}
	ALBEDO = vec3(1.0, 0.94, 0.7) * (haze * 0.22 + motes * 0.7) * strength;
}
"""


const RING_SHADER := """
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, blend_mix;
uniform float drawn = 0.0;
uniform float alpha = 0.0;
uniform float seed = 0.0;
void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float a = atan(c.y, c.x);
	// Progress round the circle from where the pen set down; the stroke ends a little past its
	// start and a little outside it, as a quick hand ring does.
	float prog = fract((a - seed) / 6.2831853);
	float wob = 0.86 + 0.025 * sin(a * 3.0 + seed) + 0.012 * sin(a * 7.0 + seed * 2.0);
	float rr = wob + 0.035 * prog;
	float w = 0.04 * (0.65 + 0.35 * sin(prog * 3.14159));
	float d = abs(length(c) - rr);
	float shown = step(prog, drawn);
	float line = 1.0 - smoothstep(w * 0.55, w, d);
	// The overlap past the start: a short second pass near the start, slightly outside.
	float tail = (1.0 - smoothstep(w * 0.4, w * 0.8, abs(length(c) - (wob + 0.04)))) * step(prog, 0.08) * step(1.0, drawn + 0.001);
	float ink = clamp(max(line * shown, tail), 0.0, 1.0);
	// A pale paper edge either side of the ink, so the ring reads on the dusk grass too.
	float halo = (1.0 - smoothstep(w, w * 2.6, d)) * shown;
	ALBEDO = mix(vec3(0.93, 0.89, 0.78), vec3(0.09, 0.07, 0.06), ink);
	ALPHA = max(ink * 0.95, halo * 0.45) * alpha;
}
"""
