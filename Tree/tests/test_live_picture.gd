extends RefCounted
## The live picture (specs/0.8.md section 6, broken list 14b to 14e): the real sun and the light's
## mix, the sky, the wind, the layout on phone screens, and the meta file's round trip.
var t

const DIR := "user://test_live_picture"


func test_sunrise_and_sunset_follow_the_german_calendar() -> void:
	# Midsummer in summer time: about 5:05 and 21:35 in the middle of Germany.
	var june := LivePicture.sun_times(172, 2.0)
	t.check_near(june["rise"], 5.1, 0.35, "midsummer sunrise")
	t.check_near(june["set"], 21.6, 0.35, "midsummer sunset")
	# Midwinter in winter time: about 8:20 and 16:25.
	var dec := LivePicture.sun_times(355, 1.0)
	t.check_near(dec["rise"], 8.3, 0.35, "midwinter sunrise")
	t.check_near(dec["set"], 16.4, 0.35, "midwinter sunset")
	var march := LivePicture.sun_times(80, 1.0)
	t.check_near(float(march["set"]) - float(march["rise"]), 12.1, 0.3, "about twelve hours of day at the equinox")


func test_the_light_mix_follows_the_hour() -> void:
	var doy := 172
	var times := LivePicture.sun_times(doy, 2.0)
	for hour in [0.0, 3.5, 5.2, 6.0, 9.0, 13.3, 17.0, 21.0, 21.8, 22.5, 23.9]:
		var m := LivePicture.moment(doy, hour, 2.0, 0)
		var mix: Dictionary = m["mix"]
		var sum := 0.0
		for k in LivePicture.LAYERS:
			sum += float(mix[k])
			t.check(float(mix[k]) >= 0.0, "no negative share at %.1f" % hour)
		t.check_near(sum, 1.0, 1e-6, "the shares add up at %.1f" % hour)
	t.check_near(LivePicture.moment(doy, times["noon"], 2.0, 0)["mix"]["day"], 1.0, 1e-6, "noon is day")
	t.check_near(LivePicture.moment(doy, 1.0, 2.0, 0)["mix"]["night"], 1.0, 1e-6, "one at night is night")
	var dawn: Dictionary = LivePicture.moment(doy, float(times["rise"]) + 0.1, 2.0, 0)["mix"]
	t.check(float(dawn["dawn"]) > 0.8 and float(dawn["dusk"]) == 0.0, "just after sunrise: the dawn light")
	var dusk: Dictionary = LivePicture.moment(doy, float(times["set"]) - 0.1, 2.0, 0)["mix"]
	t.check(float(dusk["dusk"]) > 0.8 and float(dusk["dawn"]) == 0.0, "just before sunset: the evening light")
	var late: Dictionary = LivePicture.moment(doy, float(times["set"]) + 1.05, 2.0, 0)["mix"]
	t.check_near(late["night"], 1.0, 1e-6, "an hour after sunset it is night")
	var twilight: Dictionary = LivePicture.moment(doy, float(times["set"]) + 0.5, 2.0, 0)["mix"]
	t.check(float(twilight["night"]) > 0.2 and float(twilight["night"]) < 0.8, "half an hour after sunset: dusk deepening")


func test_sky_matches_the_game_phone_sky() -> void:
	var day := LivePicture.sky_colors(0.0, 0.0)
	t.check(day[0].is_equal_approx(Color(0.3, 0.5, 0.8)) and day[1].is_equal_approx(Color(0.7, 0.8, 0.9)), "clear day sky as TreeView._paint_phone_sky")
	var night := LivePicture.sky_colors(1.0, 1.0)
	t.check(night[0].is_equal_approx(Color(0.05, 0.08, 0.18)) and night[1].is_equal_approx(Color(0.14, 0.18, 0.3)), "night sky as the game's")
	var unix := int(Time.get_unix_time_from_datetime_string("2025-09-07T18:11:00"))
	t.check_near(LivePicture.moment(250, 23.0, 2.0, unix)["moon"], Almanac.moon_phase(unix), 1e-9, "the moon's real phase")
	t.check(LivePicture.moment(250, 23.0, 2.0, unix)["stars"] > 0.9, "stars at night")
	t.check_near(LivePicture.moment(250, 13.0, 2.0, unix)["stars"], 0.0, 1e-9, "no stars by day")


func test_moon_outline_covers_the_lit_part() -> void:
	for phase in [0.0, 0.25, 0.5, 0.75]:
		var area := absf(_area(LivePicture.moon_outline(phase, 64)))
		t.check_near(area, PI * Almanac.moon_lit_fraction(phase), 0.02, "moon area at phase %.2f" % phase)
	t.check(LivePicture.moon_outline(0.25)[0].x >= 0.0, "a waxing moon is lit on the right")


func _area(p: PackedVector2Array) -> float:
	var a := 0.0
	for i in range(p.size()):
		var j := (i + 1) % p.size()
		a += p[i].x * p[j].y - p[j].x * p[i].y
	return a * 0.5


func test_wind_moves_the_crown_not_the_foot() -> void:
	var gy := 0.9
	var ct := 0.3
	var most := 0.0
	for i in range(200):
		var time := i * 0.37
		var foot := LivePicture.wind_offset(0.5, gy, time, gy, ct)
		t.check(absf(foot.x) <= LivePicture.GRASS + 1e-6 and absf(foot.y) < 1e-9, "the trunk's foot stays (only the grass ripples)")
		var top := LivePicture.wind_offset(0.5, ct, time, gy, ct)
		most = maxf(most, top.length())
		t.check(LivePicture.wind_offset(0.0, ct, time, gy, ct).x == 0.0, "the picture's left edge stays")
		t.check(LivePicture.wind_offset(1.0, 0.95, time, gy, ct).x == 0.0, "the right edge stays")
	t.check(most > 0.001, "the crown moves")
	t.check(most < LivePicture.WIND * (gy - ct) * 1.6 + LivePicture.LEAF * 1.3, "softly: about 3 %% of the tree's height")
	t.check(LivePicture.FPS >= 5 and LivePicture.FPS <= 15, "a low frame rate (14c)")


## 0.8.1, broken list 33: on the Fairphone (1116 px wide) the 0.8 wind moved a young tree's crown
## by about 3 px and the clouds by under 1 px a second, which read as a still picture. The motion
## must be seen within a few seconds, for a young tree too, and stay light.
func test_wind_is_visible_on_the_phone() -> void:
	var screen_w := 1116.0
	for tree in [[0.62, 0.9], [0.3, 0.9], [0.13, 0.9]]:  # a young tree, a grown one, a tall one
		var ct: float = tree[0]
		var gy: float = tree[1]
		var lo := 1e9
		var hi := -1e9
		var leaf_step := 0.0
		for i in range(120):  # ten seconds at 12 fps
			var time := i / 12.0
			var x := LivePicture.wind_offset(0.5, ct + 0.02, time, gy, ct).x * screen_w
			lo = minf(lo, x)
			hi = maxf(hi, x)
			# Neighbouring cells of the crown (24 by 48 mesh) move apart: the leaves shimmer.
			var v := lerpf(ct, gy, 0.3)
			var a := LivePicture.wind_offset(0.5, v, time, gy, ct)
			var b := LivePicture.wind_offset(0.5 + 1.0 / 24.0, v, time, gy, ct)
			leaf_step = maxf(leaf_step, absf(a.x - b.x) * screen_w)
		t.check(hi - lo >= 12.0, "the crown top sways at least 12 px in 10 s (crown %.2f: %.1f px)" % [ct, hi - lo])
		t.check(hi - lo <= 90.0, "light wind, not a storm (crown %.2f: %.1f px)" % [ct, hi - lo])
		t.check(leaf_step >= 2.0, "neighbouring leaves move apart (%.1f px)" % leaf_step)
	var grass := 0.0
	for i in range(120):
		grass = maxf(grass, absf(LivePicture.wind_offset(0.37, 0.95, i / 12.0, 0.9, 0.3).x) * screen_w)
	t.check(grass >= 3.0, "the near grass ripples (%.1f px)" % grass)
	# Clouds: several pixels a second, a screen width in minutes, not half an hour.
	for i in range(3):
		var px_per_s := (LivePicture.cloud_position(i, 10.0).x - LivePicture.cloud_position(i, 0.0).x) * screen_w / 10.0
		t.check(px_per_s >= 2.0 and px_per_s <= 8.0, "cloud %d drifts %.1f px a second" % [i, px_per_s])


func test_layout_keeps_the_crown_below_the_clock_and_never_pans() -> void:
	var layer := Vector2(LivePicture.LAYER_SIZE)
	for screen in [Vector2(1080, 2400), Vector2(1116, 2484), Vector2(720, 1280), Vector2(1440, 3200), Vector2(2400, 1080), Vector2(1080, 1080)]:
		for crown in [[0.05, 0.9], [0.13, 0.9], [0.33, 0.9], [0.5, 0.88]]:
			var r := LivePicture.layout(screen, layer, crown[0], crown[1])
			var top := r.position.y + float(crown[0]) * r.size.y
			var foot := r.position.y + float(crown[1]) * r.size.y
			t.check(top >= LivePicture.KEEP_CLEAR * screen.y - 0.5, "crown below the clock on %s (%s)" % [screen, crown])
			t.check(foot <= screen.y + 0.5, "the foot on screen %s" % screen)
			t.check_near(r.position.x + r.size.x * 0.5, screen.x * 0.5, 0.5, "centred, whatever the home page")
	var full := LivePicture.layout(Vector2(1080, 2400), layer, 0.13, 0.9)
	t.check_near(full.size.x, 1080.0, 0.01, "a phone shows the layer full width")
	t.check_near(full.end.y, 2400.0, 0.01, "standing on the bottom edge")


func _meta() -> Dictionary:
	return LivePicture.make_meta("linden", 12, 8.456, {"dawn": "tree_1_dawn.webp", "day": "tree_1_day.webp", "dusk": "tree_1_dusk.webp", "night": "tree_1_night.webp"}, 0.9, 0.2, 0.86, Color(0.2, 0.3, 0.1), 1790000000)


func test_meta_round_trip_and_refusals() -> void:
	var m := LivePicture.parse_meta(JSON.parse_string(JSON.stringify(_meta())))
	t.check(not m.is_empty(), "the meta reads back")
	t.check_eq(m.get("species"), "linden", "species")
	t.check_eq(m.get("day"), 12, "day")
	t.check_near(m.get("ground_y", 0.0), 0.9, 1e-6, "foot")
	t.check_near(m.get("crown_top", 0.0), 0.2, 1e-6, "crown top")
	t.check_eq(m.get("size"), LivePicture.LAYER_SIZE, "size")
	t.check((m.get("ground_color", Color.BLACK) as Color).is_equal_approx(Color.html(Color(0.2, 0.3, 0.1).to_html(false))), "ground colour")
	t.check_eq((m.get("layers", {}) as Dictionary).get("night"), "tree_1_night.webp", "layer names")
	t.check_eq(m.get("clouds"), LivePicture.CLOUD_FILES, "cloud names")
	var clear := _meta()
	clear.erase("clouds")
	t.check((LivePicture.parse_meta(clear).get("clouds") as Array).is_empty(), "no clouds named: a clear sky, still a picture")
	var newer := _meta()
	newer["version"] = LivePicture.VERSION + 1
	t.check(LivePicture.parse_meta(newer).is_empty(), "a newer picture is refused (the phone keeps its own)")
	var missing := _meta()
	(missing["layers"] as Dictionary).erase("dusk")
	t.check(LivePicture.parse_meta(missing).is_empty(), "a missing layer is refused")
	var sneaky := _meta()
	sneaky["layers"]["day"] = "../../shared_prefs/x.xml"
	t.check(LivePicture.parse_meta(sneaky).is_empty(), "no paths outside the folder")
	var upside := _meta()
	upside["crown_top"] = 0.95
	t.check(LivePicture.parse_meta(upside).is_empty(), "a crown below its foot is refused")
	t.check(LivePicture.parse_meta("not json").is_empty(), "garbage is refused")
	t.check(LivePicture.parse_meta(JSON.parse_string("[1, 2]")).is_empty(), "a wrong shape is refused")


func test_layers_and_meta_on_disk() -> void:
	_clear()
	var images := {}
	var files := {}
	for name in LivePicture.LAYERS:
		var img := Image.create(40, 60, false, Image.FORMAT_RGBA8)
		img.fill(Color(0.2, 0.5, 0.2, 1.0))
		img.fill_rect(Rect2i(0, 0, 40, 20), Color(0, 0, 0, 0))
		images[name] = img
		files[name] = "tree_7_%s.webp" % name
	DirAccess.make_dir_recursive_absolute(DIR)
	FileAccess.open(DIR.path_join("tree_3_day.webp"), FileAccess.WRITE).store_string("old")
	var meta := LivePicture.make_meta("oak", 3, 1.0, files, 0.9, 0.3, 0.34, Color(0.2, 0.5, 0.2), 7)
	t.check(LiveExport.encode_layers(DIR, images, files), "the layers are written")
	t.check(LiveExport.commit(DIR, meta), "the meta is written")
	var back := LivePicture.read_meta(DIR)
	t.check_eq(back.get("species"), "oak", "the meta reads back from disk")
	for name in LivePicture.LAYERS:
		var img := Image.load_from_file(ProjectSettings.globalize_path(DIR.path_join(files[name])))
		t.check(img != null and img.get_width() == 40 and img.get_pixel(20, 5).a < 0.1 and img.get_pixel(20, 50).a > 0.9, "layer %s keeps its clear sky" % name)
	t.check(not FileAccess.file_exists(DIR.path_join("tree_3_day.webp")), "older layers are removed")
	t.check(not FileAccess.file_exists(DIR.path_join(LivePicture.META + ".tmp")), "no half-written meta left")
	t.check(LiveExport.ensure_clouds(DIR), "the clouds are written")
	for f in LivePicture.CLOUD_FILES:
		t.check(FileAccess.file_exists(DIR.path_join(f)), "cloud %s beside the layers" % f)
	# Written again: the new set replaces the old one whole.
	var files2 := {}
	for name in LivePicture.LAYERS:
		files2[name] = "tree_8_%s.webp" % name
	t.check(LiveExport.encode_layers(DIR, images, files2) and LiveExport.commit(DIR, LivePicture.make_meta("oak", 4, 1.1, files2, 0.9, 0.3, 0.34, Color.GREEN, 8)), "written again")
	t.check_eq(LivePicture.read_meta(DIR).get("day"), 4, "the new picture")
	t.check(not FileAccess.file_exists(DIR.path_join("tree_7_day.webp")), "the old set is gone")
	t.check(FileAccess.file_exists(DIR.path_join(LivePicture.CLOUD_FILES[0])), "the clouds stay")
	var m := LiveExport.measure(images["day"])
	t.check_near(m["crown_top"], 20.0 / 60.0, 0.02, "the picture's top measured")
	t.check_near(m["horizon_y"], 20.0 / 60.0, 0.02, "the horizon measured")
	_clear()
	t.check(LivePicture.read_meta(DIR).is_empty(), "no picture: nothing to read (the phone shows its sapling)")


func test_clouds_are_soft_varied_veils() -> void:
	var imgs: Array[Image] = []
	for i in range(3):
		imgs.append(LivePicture.cloud_image(i))
	for img in imgs:
		var w := img.get_width()
		var h := img.get_height()
		var edge := 0.0
		for x in range(w):
			edge = maxf(edge, maxf(img.get_pixel(x, 0).a, img.get_pixel(x, h - 1).a))
		for y in range(h):
			edge = maxf(edge, maxf(img.get_pixel(0, y).a, img.get_pixel(w - 1, y).a))
		t.check(edge < 0.02, "a cloud fades out before its image's edge")
		var jump := 0.0
		var most := 0.0
		for y in range(0, h, 2):
			for x in range(1, w):
				var a := img.get_pixel(x, y).a
				most = maxf(most, a)
				jump = maxf(jump, absf(a - img.get_pixel(x - 1, y).a))
		t.check(most > 0.3, "a cloud is there")
		t.check(jump < 0.12, "no hard edge inside a cloud (largest step %.3f)" % jump)
	t.check(imgs[0].get_data() != imgs[1].get_data() and imgs[1].get_data() != imgs[2].get_data(), "each cloud its own shape")
	t.check(LivePicture.CLOUD_ASPECT <= 0.3 and LivePicture.CLOUD_WIDTHS[0] >= 0.6, "wide and thin")
	t.check(LivePicture.cloud_color(0.0, 0.0).a <= 0.6, "low contrast by day")


func test_signature_changes_with_the_tree() -> void:
	var look := {"fresh": 0.0, "autumn": 0.2, "late": 0.0}
	var a := LivePicture.signature("linden", 400, 6.0, 9, look)
	t.check_eq(a, LivePicture.signature("linden", 400, 6.0, 9, look), "same tree, same picture")
	t.check(a != LivePicture.signature("linden", 380, 6.0, 9, look), "a cut branch makes a new picture")
	t.check(a != LivePicture.signature("oak", 400, 6.0, 9, look), "a new tree makes a new picture")
	t.check(a != LivePicture.signature("linden", 400, 6.0, 9, {"fresh": 0.0, "autumn": 0.5, "late": 0.0}), "a new season's look too")


func _clear() -> void:
	var d := DirAccess.open(DIR)
	if d == null:
		return
	for f in d.get_files():
		d.remove(f)
