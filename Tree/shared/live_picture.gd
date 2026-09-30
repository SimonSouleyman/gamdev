class_name LivePicture
extends RefCounted
## The live picture of the tree (specs/0.8.md section 6): the live wallpaper and the screen saver
## share one animation, drawn on the phone by a small native renderer (the TreeLive Android
## library, android_plugin/treelive) from layers the game renders of the current tree
## (LiveExport). This file is the contract between the two: where the layers lie, what the meta
## file holds, and the pure maths both sides use (real sunrise and sunset, the light's mix, the
## sky's colours, the wind, the layout on the screen). The Java class LiveScene mirrors every
## function here one to one; tests pin this side, the notes (docs/notes/live-icon-0.8.md) list
## the pairs. Pure static functions, no scene access.

## Bumped when the meta file changes shape. The phone refuses a newer version (keeps its picture).
const VERSION := 1
## Where the layers lie: the game's user folder, which is the app's files dir on Android
## (Context.getFilesDir()), so the wallpaper reads it without any permission.
static var DIR := "user://live_picture"
const META := "live.json"
## One picture of the tree per light: the renderer mixes them by the real time of day.
const LAYERS: Array[String] = ["dawn", "day", "dusk", "night"]
## Layer size: half the phone's resolution in a 9:16 frame (the album camera's aspect).
const LAYER_SIZE := Vector2i(540, 960)
## Frames a second while it is seen (tuning lever 5..20; broken list 14c: at most about 15).
const FPS := 12
## The top part of the screen the lock screen's clock covers: the crown stays below it (14e).
const KEEP_CLEAR := 0.28
## How far the crown top sways, as a share of the tree's height on screen (tuning lever).
const WIND := 0.012
## Where the sun is reckoned: the middle of Germany (the game's calendar is German too).
const LATITUDE := 51.0
const LONGITUDE := 10.0


# --- the real sun ------------------------------------------------------------------------

## Sunrise and sunset as local clock hours for a day of the year, at LATITUDE and LONGITUDE, with
## the clock `utc_offset_hours` ahead of UTC (1 in winter, 2 in summer time in Germany).
## Returns {"rise": h, "set": h, "noon": h}. The sun's declination and the equation of time are
## the usual short approximations (a few minutes off at most).
static func sun_times(day_of_year: int, utc_offset_hours: float) -> Dictionary:
	var b := TAU * float(day_of_year - 81) / 365.0
	var decl := deg_to_rad(23.44) * sin(b)
	var lat := deg_to_rad(LATITUDE)
	# The sun's upper edge at the horizon, with refraction: -0.833 degrees.
	var cos_h := (sin(deg_to_rad(-0.833)) - sin(lat) * sin(decl)) / (cos(lat) * cos(decl))
	var half := rad_to_deg(acos(clampf(cos_h, -1.0, 1.0))) / 15.0
	# Equation of time in minutes (the sundial runs ahead in autumn, behind in winter).
	var eot := 9.87 * sin(2.0 * b) - 7.53 * cos(b) - 1.5 * sin(b)
	var noon := 12.0 + utc_offset_hours - LONGITUDE / 15.0 - eot / 60.0
	return {"rise": noon - half, "set": noon + half, "noon": noon}


## The sun's height as the game reckons it (DayCycle.sun_height): 0 at sunrise and sunset, 1 at
## noon; before sunrise and after sunset, minus the hours until sunrise or since sunset.
static func sun_height(hour: float, times: Dictionary) -> float:
	var rise: float = times["rise"]
	var set_h: float = times["set"]
	if hour >= rise and hour <= set_h:
		return sin(PI * (hour - rise) / maxf(set_h - rise, 0.1))
	var since_set := fposmod(hour - set_h, 24.0)
	var until_rise := fposmod(rise - hour, 24.0)
	return -minf(since_set, until_rise)


## How much of each layer to show at this height of the sun: {"dawn", "day", "dusk", "night"},
## summing to 1. Golden light at a low sun as in the game's phone sky (golden until the sun is
## 0.45 up), deepening into night over the hour after sunset (and lifting in the hour before
## sunrise). `morning`: before noon the golden layer is the dawn one (lit from the east).
static func light_mix(h: float, morning: bool) -> Dictionary:
	var night := smoothstep(0.0, 1.0, -h)
	var golden := 1.0 - smoothstep(0.0, 0.45, h) if h >= 0.0 else 1.0
	var gold := (1.0 - night) * golden
	return {
		"dawn": gold if morning else 0.0,
		"day": (1.0 - night) * (1.0 - golden),
		"dusk": 0.0 if morning else gold,
		"night": night,
	}


## The golden share and the night share behind a mix (for the sky and the clouds).
static func golden_of(mix: Dictionary) -> float:
	var lit := 1.0 - float(mix["night"])
	return (float(mix["dawn"]) + float(mix["dusk"])) / lit if lit > 0.001 else 1.0


## The sky's two colours, top and horizon, exactly as the game paints the phone sky
## (TreeView._paint_phone_sky, without rain): clear blue by day, warm near the horizon at a low
## sun, deep blue at night.
static func sky_colors(golden: float, night: float) -> Array[Color]:
	var top := Color(0.3, 0.5, 0.8).lerp(Color(0.34, 0.42, 0.66), golden)
	var horizon := Color(0.7, 0.8, 0.9).lerp(Color(0.98, 0.7, 0.45), golden)
	top = top.lerp(Color(0.05, 0.08, 0.18), night)
	horizon = horizon.lerp(Color(0.14, 0.18, 0.3), night)
	return [top, horizon]


## Everything the renderer needs for one moment: the day of the year, the local clock hour and
## the Unix time (for the moon). {"mix", "golden", "night", "sky": [top, horizon], "moon": phase,
## "stars": 0..1}.
static func moment(day_of_year: int, hour: float, utc_offset_hours: float, unix: int) -> Dictionary:
	var times := sun_times(day_of_year, utc_offset_hours)
	var h := sun_height(hour, times)
	var mix := light_mix(h, hour < float(times["noon"]))
	var golden := golden_of(mix)
	var night: float = mix["night"]
	return {"mix": mix, "golden": golden, "night": night, "sky": sky_colors(golden, night),
		"moon": Almanac.moon_phase(unix), "stars": smoothstep(0.4, 1.0, night)}


# --- the wind ------------------------------------------------------------------------------

## How far a point of the layer moves in the wind at time t (seconds), in layer units (0..1 of
## the layer's width and height). u, v: the point (v downward). ground_y, crown_top: the trunk's
## foot and the crown's top in the same units. The trunk's foot stands still, the crown sways
## more the higher up (a bending trunk), with a slow gust and a quicker flutter; the grass below
## the foot shimmers a little.
static func wind_offset(u: float, v: float, t: float, ground_y: float, crown_top: float) -> Vector2:
	var tall := maxf(ground_y - crown_top, 0.02)
	var k := clampf((ground_y - v) / tall, 0.0, 1.0)
	var bend := pow(k, 1.6)
	var gust := 0.6 * sin(t * 0.9 + u * 1.3) + 0.4 * sin(t * 0.37 + 1.7)
	var flutter := 0.25 * k * sin(t * 2.3 + u * 7.0 + v * 5.0)
	var dx := WIND * tall * bend * (gust + flutter)
	var dy := WIND * tall * 0.2 * bend * sin(t * 1.1 + u * 3.0)
	# The meadow below the foot: a small, quick ripple, strongest near the foot's line.
	if v > ground_y - 0.01:
		var g := 1.0 - smoothstep(0.0, 0.2, v - ground_y)
		dx += 0.002 * g * sin(t * 1.7 + u * 11.0 + v * 23.0)
	# The picture's side edges stay put (no sky showing through beside the meadow).
	dx *= clampf(minf(u, 1.0 - u) / 0.04, 0.0, 1.0)
	return Vector2(dx, dy)


## Where cloud i (0..2) floats at time t: x as a share of the screen width (it leaves on the right
## and comes back on the left), y as a share of the height, in the sky above the crown.
static func cloud_position(i: int, t: float) -> Vector2:
	var speed: float = [0.0021, 0.0015, 0.0012][i % 3]
	var start: float = [0.1, 0.55, 0.85][i % 3]
	var y: float = [0.1, 0.19, 0.05][i % 3]
	return Vector2(fposmod(start + t * speed, 1.6) - 0.3, y)


## A cloud's shape: soft round puffs (x, y, radius) as shares of the cloud's width, their density
## added up (LiveRenderer.cloudBitmap and LivePreview.cloud_image draw the same).
const CLOUD_PUFFS: Array[Vector3] = [Vector3(-0.28, 0.05, 0.16), Vector3(-0.12, 0.0, 0.2), Vector3(0.06, -0.04, 0.22),
	Vector3(0.24, 0.02, 0.17), Vector3(0.36, 0.07, 0.11), Vector3(-0.38, 0.09, 0.1), Vector3(0.0, 0.08, 0.2)]


## A cloud's density at a point (x, y as shares of its width, from its middle): 0..1.
static func cloud_density(x: float, y: float) -> float:
	var a := 0.0
	for p in CLOUD_PUFFS:
		var d2 := ((x - p.x) * (x - p.x) + (y - p.y) * (y - p.y)) / (p.z * p.z)
		a += maxf(0.0, 1.0 - d2) * 0.7
	# A flatter underside, as fair-weather clouds have.
	return clampf(a, 0.0, 1.0) * (1.0 - smoothstep(0.08, 0.16, y))


## Cloud widths as a share of the screen width.
const CLOUD_WIDTHS: Array[float] = [0.42, 0.3, 0.24]


## The clouds' colour (alpha: how solid): white by day, warm at a low sun, faint and grey-blue at
## night.
static func cloud_color(golden: float, night: float) -> Color:
	var c := Color(1.0, 1.0, 1.0, 0.75).lerp(Color(1.0, 0.84, 0.7, 0.8), golden)
	return c.lerp(Color(0.3, 0.34, 0.46, 0.35), night)


## Star i of STARS: x and y as shares of the screen (in the upper sky), z its size in pixels at a
## 1080 px wide screen. The same pseudo-random numbers on both sides (doubles).
const STARS := 46


static func star(i: int) -> Vector3:
	var a := _fract(sin(float(i) * 12.9898 + 1.0) * 43758.5453)
	var b := _fract(sin(float(i) * 78.233 + 2.0) * 24634.6345)
	var c := _fract(sin(float(i) * 39.425 + 3.0) * 12345.6789)
	return Vector3(a, b * 0.55, 1.5 + c * 2.5)


static func _fract(x: float) -> float:
	return x - floor(x)


## Where the moon hangs (share of the screen) and its radius (share of the width). It is drawn
## before the tree, so a wide crown hides it rather than the other way round.
const MOON_AT := Vector2(0.78, 0.2)
const MOON_RADIUS := 0.04


## The lit part of the moon's disc, radius 1 around 0 (y downward), for a phase 0..1 (Almanac):
## the limb on the lit side, then the terminator back. Waxing lit on the right.
static func moon_outline(phase: float, steps: int = 24) -> PackedVector2Array:
	var pts := PackedVector2Array()
	var side := 1.0 if phase < 0.5 else -1.0
	var k := cos(TAU * phase)
	for i in range(steps + 1):
		var a := -PI * 0.5 + PI * i / steps
		pts.append(Vector2(side * cos(a), sin(a)))
	for i in range(steps, -1, -1):
		var a := -PI * 0.5 + PI * i / steps
		pts.append(Vector2(side * k * cos(a), sin(a)))
	return pts


# --- the layout ----------------------------------------------------------------------------

## Where the layer is drawn on a screen (pixels): as wide as the screen, standing on its bottom
## edge; if that puts the crown's top into the clock's area (KEEP_CLEAR), the layer shrinks around
## the trunk's foot until the crown is below it. Never depends on the home screen's page: the view
## does not pan (broken list 14e).
static func layout(screen: Vector2, layer: Vector2, crown_top: float, ground_y: float) -> Rect2:
	var s := screen.x / layer.x
	var rect := Rect2(0.0, screen.y - layer.y * s, screen.x, layer.y * s)
	var top := rect.position.y + crown_top * rect.size.y
	var limit := KEEP_CLEAR * screen.y
	if top < limit and ground_y > crown_top:
		var foot := rect.position.y + ground_y * rect.size.y
		var s2 := (foot - limit) / ((ground_y - crown_top) * layer.y)
		var size := layer * s2
		rect = Rect2(screen.x * 0.5 - size.x * 0.5, foot - ground_y * size.y, size.x, size.y)
	return rect


# --- the meta file -------------------------------------------------------------------------

## The meta the game writes beside the layers (LiveExport). `files`: layer name -> file name.
static func make_meta(species_id: String, day: int, height: float, files: Dictionary, ground_y: float, crown_top: float, horizon_y: float, ground_color: Color, saved_unix: int) -> Dictionary:
	return {
		"version": VERSION, "species": species_id, "day": day, "height": snappedf(height, 0.01),
		"saved": saved_unix, "size": [LAYER_SIZE.x, LAYER_SIZE.y], "layers": files.duplicate(),
		"ground_y": snappedf(ground_y, 0.0001), "crown_top": snappedf(crown_top, 0.0001),
		"horizon_y": snappedf(horizon_y, 0.0001), "ground_color": ground_color.to_html(false),
	}


## Reads a meta dictionary (from JSON). Returns {} when it cannot be used: a newer version, a
## missing layer, numbers out of range. Then the phone keeps the last good picture (14d).
static func parse_meta(d: Variant) -> Dictionary:
	if not (d is Dictionary):
		return {}
	var m := d as Dictionary
	if int(m.get("version", 0)) < 1 or int(m.get("version", 0)) > VERSION:
		return {}
	var layers: Variant = m.get("layers", {})
	if not (layers is Dictionary):
		return {}
	for name in LAYERS:
		var f := str((layers as Dictionary).get(name, ""))
		# A plain file name beside the meta, never a path elsewhere.
		if f == "" or f.contains("/") or f.contains("\\") or f.contains(".."):
			return {}
	var size: Variant = m.get("size", [])
	if not (size is Array) or (size as Array).size() != 2 or int(size[0]) < 16 or int(size[1]) < 16:
		return {}
	var ground_y := float(m.get("ground_y", -1.0))
	var crown_top := float(m.get("crown_top", -1.0))
	var horizon_y := float(m.get("horizon_y", -1.0))
	if ground_y <= 0.0 or ground_y > 1.0 or crown_top < 0.0 or crown_top >= ground_y or horizon_y < 0.0 or horizon_y > 1.0:
		return {}
	var color := str(m.get("ground_color", ""))
	if not Color.html_is_valid(color):
		return {}
	return {
		"version": int(m["version"]), "species": str(m.get("species", "linden")), "day": int(m.get("day", 0)),
		"height": float(m.get("height", 0.0)), "saved": int(m.get("saved", 0)),
		"size": Vector2i(int(size[0]), int(size[1])), "layers": (layers as Dictionary).duplicate(),
		"ground_y": ground_y, "crown_top": crown_top, "horizon_y": horizon_y, "ground_color": Color.html(color),
	}


static func meta_path(dir: String = DIR) -> String:
	return dir.path_join(META)


## The meta on disk, parsed ({} if there is none or it cannot be used).
static func read_meta(dir: String = DIR) -> Dictionary:
	var p := meta_path(dir)
	if not FileAccess.file_exists(p):
		return {}
	return parse_meta(JSON.parse_string(FileAccess.get_file_as_string(p)))


## What the picture shows, in one string: a new export is needed only when this changes (the
## tree grew or was cut, a new tree, a new season's look).
static func signature(species_id: String, nodes: int, height: float, day: int, season: Dictionary) -> String:
	var look := ""
	for k in ["fresh", "autumn", "late"]:
		look += "%.2f," % float(season.get(k, 0.0))
	return "%s|%d|%.2f|%d|%s" % [species_id, nodes, height, day, look]
