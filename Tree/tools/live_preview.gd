class_name LivePreview
extends Control
## A desktop copy of the phone's live picture renderer (android_plugin/treelive, LiveRenderer.java):
## the same drawing, step by step, from the same files and the same maths (LivePicture), so the
## animation can be looked at and photographed on a PC (tools/live_shot.gd). Not part of the game.

var meta: Dictionary = {}
var layers: Dictionary = {}  # name -> Texture2D
## The moment shown: day of the year, local clock hour, UTC offset, Unix time; t: seconds of wind.
var day_of_year: int = 180
var hour: float = 12.0
var utc_offset: float = 2.0
var unix: int = 0
var t: float = 0.0
## Grid of the wind mesh (as LiveRenderer.MESH_W / MESH_H).
const MESH_W := 12
const MESH_H := 24
var _clouds: Array[Texture2D] = []


func load_from(dir: String) -> bool:
	meta = LivePicture.read_meta(dir)
	if meta.is_empty():
		return false
	layers.clear()
	for name in LivePicture.LAYERS:
		var img := Image.load_from_file(ProjectSettings.globalize_path(dir.path_join(meta["layers"][name])))
		if img == null or img.is_empty():
			return false
		layers[name] = ImageTexture.create_from_image(img)
	_clouds.clear()
	for f in meta.get("clouds", []):
		var c := Image.load_from_file(ProjectSettings.globalize_path(dir.path_join(f)))
		if c != null and not c.is_empty():
			_clouds.append(ImageTexture.create_from_image(c))
	return true


func _draw() -> void:
	var w := size.x
	var h := size.y
	var m := LivePicture.moment(day_of_year, hour, utc_offset, unix)
	var sky: Array = m["sky"]
	# The sky: top colour to the horizon colour at the picture's horizon line.
	var rect := LivePicture.layout(size, Vector2(meta["size"]), meta["crown_top"], meta["ground_y"])
	var horizon_px := rect.position.y + float(meta["horizon_y"]) * rect.size.y
	draw_polygon(PackedVector2Array([Vector2(0, 0), Vector2(w, 0), Vector2(w, horizon_px), Vector2(0, horizon_px)]),
		PackedColorArray([sky[0], sky[0], sky[1], sky[1]]))
	draw_rect(Rect2(0, horizon_px, w, h - horizon_px), sky[1])
	# Stars and the moon at night.
	var stars: float = m["stars"]
	if stars > 0.0:
		for i in range(LivePicture.STARS):
			var s := LivePicture.star(i)
			var a := stars * (0.65 + 0.35 * sin(t * 0.8 + i * 1.7))
			draw_circle(Vector2(s.x * w, s.y * h), s.z * w / 1080.0, Color(1, 1, 0.95, a))
	var night: float = m["night"]
	var lit := Almanac.moon_lit_fraction(m["moon"])
	if night > 0.3 and lit > 0.03:
		var alpha := smoothstep(0.3, 0.8, night)
		var c := Vector2(LivePicture.MOON_AT.x * w, LivePicture.MOON_AT.y * h)
		var r := LivePicture.MOON_RADIUS * w
		draw_circle(c, r, Color(0.35, 0.38, 0.5, 0.25 * alpha))
		var pts := LivePicture.moon_outline(m["moon"])
		for i in range(pts.size()):
			pts[i] = c + pts[i] * r
		draw_colored_polygon(pts, Color(0.95, 0.94, 0.86, alpha))
	# The clouds drift (the images the game wrote beside the layers; none: a clear sky).
	var cc := LivePicture.cloud_color(m["golden"], night)
	for i in range(_clouds.size()):
		var p := LivePicture.cloud_position(i, t)
		var cw := LivePicture.CLOUD_WIDTHS[i] * w
		var ch := cw * LivePicture.CLOUD_ASPECT
		draw_texture_rect(_clouds[i], Rect2(p.x * w - cw * 0.5, p.y * h - ch * 0.5, cw, ch), false, cc)
	# The ground beside a narrowed picture.
	if rect.position.x > 0.5:
		draw_rect(Rect2(0, horizon_px, w, h - horizon_px), meta["ground_color"])
	# The tree: the layers in the light's mix, bent by the wind.
	var mix: Dictionary = m["mix"]
	var names := LivePicture.LAYERS.duplicate()
	names.sort_custom(func(a: String, b: String) -> bool: return float(mix[a]) > float(mix[b]))
	var first := true
	for name in names:
		var k := float(mix[name])
		if k < 0.01:
			continue
		# The strongest layer opaque, the others blended over it by their share.
		_draw_layer(layers[name], rect, 1.0 if first else k / (k + float(mix[names[0]])))
		first = false


func _draw_layer(tex: Texture2D, rect: Rect2, alpha: float) -> void:
	var gy: float = meta["ground_y"]
	var ct: float = meta["crown_top"]
	for j in range(MESH_H):
		for i in range(MESH_W):
			var uvs := PackedVector2Array()
			var pts := PackedVector2Array()
			for c in [Vector2i(i, j), Vector2i(i + 1, j), Vector2i(i + 1, j + 1), Vector2i(i, j + 1)]:
				var uv := Vector2(float(c.x) / MESH_W, float(c.y) / MESH_H)
				var off := LivePicture.wind_offset(uv.x, uv.y, t, gy, ct)
				uvs.append(uv)
				pts.append(rect.position + (uv + off) * rect.size)
			draw_polygon(pts, PackedColorArray([Color(1, 1, 1, alpha)]), uvs, tex)
