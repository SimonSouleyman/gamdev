class_name RootGlow
extends MeshInstance3D
## The morning reveal's glow (0.8.2.6, specs/journal-drawers-loop.md G3): for about two seconds
## at the first view of a morning, a warm light runs along last night's new roots, seen from the
## tree view as faint lines in the soil under the grass. One flat ribbon mesh laid on the ground
## over the roots (deeper roots fainter), built once per morning; one additive unshaded material,
## no lights, no extra pass. Hidden the rest of the time. Drawn without a depth test (as the wish
## place's ink ring): the meadow's grass hid it on the phone shots; seen through the grass it
## reads as light in the soil. Only within the clearing, so it never lies over the forest.

## Ribbon half-widths on the ground (m): the main root, the fine and side roots.
const MAIN_WIDTH := 0.1
const FINE_WIDTH := 0.045
const LIFT := 0.03
## Further out the ribbons widen (by this per metre from the trunk), so a far root is not lost.
const WIDEN := 0.12
## Roots this deep show at a third of their light (the soil hides them).
const DEPTH_FADE := 2.5

var _mat: ShaderMaterial
var _left: float = 0.0
## Segments in the mesh now (tests).
var segment_count: int = 0


func _init() -> void:
	var sh := Shader.new()
	sh.code = SHADER
	_mat = ShaderMaterial.new()
	_mat.shader = sh
	material_override = _mat
	cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	extra_cull_margin = 4.0
	visible = false


## Builds the ribbons over `segments` (Moments.night_segments) and starts the glow. False if
## there is nothing to show.
func show_roots(segments: Array) -> bool:
	segment_count = 0
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for s: Dictionary in segments:
		var a: Vector3 = s["a"]
		var b: Vector3 = s["b"]
		if Vector2(b.x, b.z).length() > Terrain.edge - 0.5:
			continue
		var flat := Vector2(b.x - a.x, b.z - a.z)
		if flat.length() < 0.005:
			continue
		var side := Vector2(-flat.y, flat.x).normalized() * (MAIN_WIDTH if s["main"] else FINE_WIDTH) * (1.0 + WIDEN * Vector2(b.x, b.z).length())
		var depth := maxf(0.0, -0.5 * (a.y + b.y))
		var light := lerpf(1.0, 0.35, clampf(depth / DEPTH_FADE, 0.0, 1.0)) * (1.0 if s["main"] else 0.7) * (0.6 if s.get("old", false) else 1.0)
		var u := float(s["u"])
		var pa := Terrain.at(Vector3(a.x, 0.0, a.z)) + Vector3(0, LIFT, 0)
		var pb := Terrain.at(Vector3(b.x, 0.0, b.z)) + Vector3(0, LIFT, 0)
		var sd := Vector3(side.x, 0.0, side.y)
		var c := Color(1, 1, 1, light)
		_vert(st, pa - sd, u, 0.0, c)
		_vert(st, pa + sd, u, 1.0, c)
		_vert(st, pb + sd, u, 1.0, c)
		_vert(st, pa - sd, u, 0.0, c)
		_vert(st, pb + sd, u, 1.0, c)
		_vert(st, pb - sd, u, 0.0, c)
		segment_count += 1
	if segment_count == 0:
		visible = false
		return false
	mesh = st.commit()
	_left = Moments.GLOW_SECONDS
	_mat.set_shader_parameter("progress", 0.0)
	_mat.set_shader_parameter("alpha", 1.0)
	visible = true
	return true


func _vert(st: SurfaceTool, p: Vector3, u: float, v: float, c: Color) -> void:
	st.set_color(c)
	st.set_uv(Vector2(u, v))
	st.add_vertex(p)


## The glow's state: how far along the roots its front is (0..1+) and how bright it is.
func glow_state() -> Vector2:
	if not visible:
		return Vector2.ZERO
	var t := Moments.GLOW_SECONDS - _left
	return Vector2(t / (Moments.GLOW_SECONDS * 0.7), clampf(_left / 0.6, 0.0, 1.0) * clampf(t / 0.25, 0.0, 1.0))


func stop() -> void:
	_left = 0.0
	visible = false


func _process(delta: float) -> void:
	if not visible:
		return
	_left -= delta
	if _left <= 0.0:
		stop()
		return
	var s := glow_state()
	_mat.set_shader_parameter("progress", s.x)
	_mat.set_shader_parameter("alpha", s.y)


const SHADER := """
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled;
uniform float progress = 0.0;
uniform float alpha = 1.0;
void fragment() {
	// Soft across the ribbon, a bright front running out along the night's growth, a fainter
	// light left behind it.
	float across = 1.0 - abs(UV.y * 2.0 - 1.0);
	across = smoothstep(0.0, 0.8, across);
	float behind = step(UV.x, progress);
	float front = 1.0 - smoothstep(0.0, 0.18, abs(progress - UV.x));
	float light = (0.6 * behind + 1.0 * front) * COLOR.a * across * alpha;
	// Not from a camera down in the grass, nor right under it (drawn over everything, a ribbon a
	// metre away would fill the picture).
	light *= smoothstep(0.6, 1.2, CAMERA_POSITION_WORLD.y) * smoothstep(1.8, 3.5, length(VERTEX));
	ALBEDO = vec3(1.0, 0.82, 0.42) * light * 1.2;
}
"""
