extends RefCounted
## 0.8.1 look (specs/0.8.md broken items 17, 25 and 31; numbers in docs/notes/look-0.8.1.md).
var t


## Broken 25: the frame follows the tree's real height and crown width on the portrait screen:
## the whole tree inside the band below the HUD, never small and low, from seedling to grown.
func test_the_frame_holds_the_tree_from_seedling_to_grown() -> void:
	var aspect := 450.0 / 800.0
	for c in [[0.2, 0.3], [0.8, 0.7], [4.4, 2.6], [8.0, 4.0], [14.6, 9.0], [25.4, 34.0]]:
		var h: float = c[0]
		var w: float = c[1]
		var pf := TreeView.play_frame(h, w, aspect)
		var half := pf.x
		# Screen shares from the top (straight-on view, the camera looking at the focus).
		var foot := 0.5 + pf.y / (2.0 * half)
		var top := 0.5 - (h - pf.y) / (2.0 * half)
		t.check(top >= TreeView.FRAME_TOP - 0.001, "%.1f m: the top is not cut and clear of the HUD (%.2f)" % [h, top])
		t.check(foot <= TreeView.FRAME_BASE + 0.001, "%.1f m: the foot is in view (%.2f)" % [h, foot])
		t.check(w <= 2.0 * half * aspect * (1.0 - 2.0 * TreeView.FRAME_SIDE) + 0.001, "%.1f m: the crown's %.1f m fit across" % [h, w])
		if w / aspect < h:
			# A tree taller than broad fills the band, whatever its size.
			t.check(top < TreeView.FRAME_TOP + 0.05 and foot > TreeView.FRAME_BASE - 0.05, "%.1f m: fills the band (%.2f to %.2f)" % [h, top, foot])
	var seedling := TreeView.play_frame(0.8, 0.7, aspect)
	t.check(0.8 / (2.0 * seedling.x) > 0.3, "a young sapling fills about a third of the screen, not a speck in the grass")


## Broken 31: the crown does not shadow itself into dark blotches (its own leaf masses and their
## occlusion give the soft shade inside); it still casts its shadow on the meadow.
func test_the_crown_takes_no_blotchy_self_shadow() -> void:
	var code := (preload("res://tree/hero_crown.gdshader") as Shader).code
	var mode := code.substr(code.find("render_mode"), 160)
	t.check("shadows_disabled" in mode.substr(0, mode.find(";")), "the crown's sprays receive no shadow map")


## Broken 17: the soil is a very dark warm grey, not black and not lit like day; the old roots
## and tonight's root carry their own soft glow and rim, tonight's the brighter.
func test_roots_read_in_the_dark() -> void:
	var rv := RootView.new()
	t.root.add_child(rv)
	var env := rv.camera.environment
	var bg := env.background_color.get_luminance()
	t.check(bg > 0.02 and bg < 0.08, "the soil is near black but not black (%.3f)" % bg)
	var old := rv._static_roots.material_override as ShaderMaterial
	var live := rv._live_roots.material_override as ShaderMaterial
	var og: Color = old.get_shader_parameter("glow")
	var lg: Color = live.get_shader_parameter("glow")
	t.check(og.get_luminance() >= 0.5, "old roots glow softly (%.2f)" % og.get_luminance())
	t.check(lg.get_luminance() > og.get_luminance(), "tonight's root glows brighter than the old ones")
	t.check((old.get_shader_parameter("glow_rim") as Color).get_luminance() > 0.1, "a soft rim outlines thin fine roots")
	t.check(rv._builder.min_radius >= 0.025, "fine roots are thick enough to see from the overview")
	rv.free()


## Broken 17 (tree mode): the night is lifted for the phone, but stays night.
func test_the_night_is_brighter_but_still_night() -> void:
	t.check(NightSky.MOON_BASE > 0.14 and NightSky.MOON_BASE + NightSky.MOON_LIT < 1.0, "moonlight brighter than 0.8, far below the sun")
	t.check(TreeView.NIGHT_EXPOSURE_LIFT > 0.0 and TreeView.NIGHT_EXPOSURE_LIFT <= 0.6, "the eye adapts a little, not to daylight")


## 0.8.2 (look review, hud_shears): with the shears out the whole crown is in view, its top
## below the HUD and its foot above the hint, from a sapling to a grown tree.
func test_the_shears_view_holds_the_whole_crown() -> void:
	for aspect in [450.0 / 1000.0, 720.0 / 1600.0, 450.0 / 800.0]:
		for c in [[0.8, 0.7], [4.4, 2.6], [6.0, 4.5], [14.6, 9.0], [25.4, 20.0]]:
			var h: float = c[0]
			var w: float = c[1]
			var half := TreeView.prune_frame(h, w, aspect)
			var focus := h * TreeView.PRUNE_FOCUS
			var top := 0.5 - (h - focus) / (2.0 * half)
			var foot := 0.5 + focus / (2.0 * half)
			t.check(top >= TreeView.FRAME_TOP - 0.001, "%.1f m: the crown's top is not cut (%.2f)" % [h, top])
			t.check(foot <= TreeView.PRUNE_BASE + 0.001, "%.1f m: the foot is above the hint (%.2f)" % [h, foot])
			t.check(w * 0.5 <= half * aspect * (1.0 - 2.0 * TreeView.FRAME_SIDE) + 0.001, "%.1f m: the crown fits across" % h)
