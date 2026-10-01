extends RefCounted
## 0.8.1, item 18 (docs/notes/shed-0.8.1.md): every bonsai tool handled by simulated touches on
## phone-shaped screens. The views see the 720-wide canvas (stretch mode canvas_items), the touches
## arrive in the screen's own pixels, as on the phone: a mouse event emulated from the touch, then
## the touch. Each tool is picked up, aimed (the highlight shows and nothing happens yet), used on
## release, and put down; every action is counted in taps. Taps on the other things never use the
## tool in hand, touches on the soil never pick up a thing, and a finger's wobble is still a tap.
var t

## Screen (physical pixels) and the canvas the game draws it at. 1080 x 2400 and 720 x 1600 are
## the 6.4" 20:9 phone at two densities (both a 720 x 1600 canvas); 720 x 1280 is the reference.
const SIZES := [[Vector2i(1080, 2400), Vector2i(720, 1600)], [Vector2i(720, 1600), Vector2i(720, 1600)], [Vector2i(720, 1280), Vector2i(720, 1280)]]
## A fingertip's half width on screen (canvas pixels; about 5 mm on the 6.4" phone).
const FINGER := 54.0

var _vp: SubViewport
var _view: BonsaiView
var _hud: BonsaiHud
var _scale: Vector2
var taps: int = 0


func _make(g: GameState, phys: Vector2i, canvas: Vector2i) -> void:
	_vp = SubViewport.new()
	_vp.size = phys
	_vp.size_2d_override = canvas
	_vp.size_2d_override_stretch = true
	_vp.disable_3d = false
	t.root.add_child(_vp)
	_view = BonsaiView.new()
	_vp.add_child(_view)
	_view.setup(g)
	_view.visible = true
	_view.active = true
	_view.camera.current = true
	_view.look_from(0.0, BonsaiView.PITCH, BonsaiView.DIST)
	_view._update_camera(0.0)
	_hud = BonsaiHud.new()
	_vp.add_child(_hud)
	_hud.state = g
	_hud.view = _view
	# Every cutting at hand, so the box of cuttings lies on the sill too.
	_hud.any_species = func() -> bool: return true
	_hud.show_hud(true)
	_scale = Vector2(phys) / Vector2(canvas)
	_settle()


func _free() -> void:
	_vp.free()


## Frames pass: the tool in hand follows the finger, the motions run to their end, the paper moves.
func _settle() -> void:
	for _i in range(3):
		_view._process(0.1)
		_hud._process(0.1)
	for _i in range(6):
		for tw in _view.get_tree().get_processed_tweens():
			tw.custom_step(5.0)
	_view._process(0.1)
	_hud._process(0.1)
	# No frames pass in the test runner: the paper's containers lay out their children now.
	for _k in range(2):
		for c in _hud.find_children("*", "Container", true, false):
			(c as Container).notification(Container.NOTIFICATION_SORT_CHILDREN)
		_hud._process(0.1)


## One finger down or up at a canvas point, as the phone sends it.
func _touch(at: Vector2, down: bool) -> void:
	var p := at * _scale
	var m := InputEventMouseButton.new()
	m.button_index = MOUSE_BUTTON_LEFT
	m.pressed = down
	m.position = p
	m.global_position = p
	m.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
	_vp.push_input(m)
	var s := InputEventScreenTouch.new()
	s.index = 0
	s.pressed = down
	s.position = p
	_vp.push_input(s)


## The finger slides (pressed) from one canvas point to another.
func _slide(from: Vector2, to: Vector2, steps: int = 4) -> void:
	var last := from
	for i in range(1, steps + 1):
		var at := from.lerp(to, float(i) / steps)
		var m := InputEventMouseMotion.new()
		m.position = at * _scale
		m.global_position = m.position
		m.relative = (at - last) * _scale
		m.button_mask = MOUSE_BUTTON_MASK_LEFT
		_vp.push_input(m)
		var d := InputEventScreenDrag.new()
		d.index = 0
		d.position = at * _scale
		d.relative = (at - last) * _scale
		_vp.push_input(d)
		last = at


func _tap(at: Vector2) -> void:
	taps += 1
	_touch(at, true)
	_touch(at, false)
	_settle()


func _thing(id: String) -> Vector2:
	return _view.object_screen_points()[id]


## Picks a tool up with one tap and checks it is clearly in hand.
func _pick_up(id: String, where: String) -> void:
	taps = 0
	_tap(_thing(id))
	t.check_eq(_view.tool, id, "%s: one tap picks it up" % where)
	t.check(_view.tools.ghost.visible, "%s: its place on the sill shows a put-back ring" % where)
	t.check(_hud._hint.text.begins_with("In hand:"), "%s: the scrap says what is in hand (%s)" % [where, _hud._hint.text])
	var tag := _hud._labels[id] as PanelContainer
	t.check(tag.visible and (tag.get_child(0) as Label).text == BonsaiHud.PUT_BACK, "%s: its place says \"put back\"" % where)


## Presses on a target: the aim shows (highlight on) and nothing happens yet; the tool's working
## end sits on the finger and its body is clear above it. Returns whether something was aimed at.
func _aim(at: Vector2, kind: String, where: String) -> bool:
	taps += 1
	_touch(at, true)
	_settle()
	t.check_eq(str(_view.aimed.get("kind", "")), kind, "%s: pressing aims at the %s" % [where, kind])
	var tip := _view.tool_tip_screen()
	t.check(tip.distance_to(at) < 6.0, "%s: the tool's working end is on the finger (%s vs %s)" % [where, tip, at])
	var top := INF
	for p in _view.tools.hull(_view.camera, _view.tool):
		top = minf(top, p.y)
	t.check(at.y - top >= FINGER, "%s: the tool reaches a fingertip above the touch (%d px)" % [where, int(at.y - top)])
	var body := _view.tool_body_screen()
	t.check(body.y < at.y - 20.0, "%s: the tool's body is above the finger (%s, touch %s)" % [where, body, at])
	return not _view.aimed.is_empty()


func _release(at: Vector2) -> void:
	_touch(at, false)
	_settle()


func _put_down(where: String) -> void:
	var before := taps
	var id := _view.tool
	var rp := _view.tools.rest_point(_view.camera, id)
	_tap(rp)
	t.check_eq(_view.tool, "", "%s: a tap on its place puts it down" % where)
	t.check(not _view.tools.ghost.visible, "%s: the put-back ring is gone" % where)
	t.check_eq(taps - before, 1, "%s: one tap to put it down" % where)


## A spot where a touch means nothing: no thing, nothing the tool works on, no paper.
func _empty_spot(canvas: Vector2i) -> Vector2:
	for y in range(260, canvas.y - 100, 40):
		for x in range(40, canvas.x - 40, 40):
			var p := Vector2(x, y)
			if _view.pick_object(p) == "" and _view.aim_target(p).is_empty() and not _view.on_bonsai(p) and _hud_at(p) == null:
				var clear := true
				for id in _view.object_screen_points():
					if (_view.object_screen_points()[id] as Vector2).distance_to(p) < BonsaiView.TOOL_TAP * 2.0:
						clear = false
				if clear:
					return p
	return Vector2(-1, -1)


func _hud_at(p: Vector2) -> Control:
	for c in _hud.find_children("*", "Control", true, false):
		var ctl := c as Control
		if ctl.is_visible_in_tree() and ctl.mouse_filter != Control.MOUSE_FILTER_IGNORE and ctl.get_global_rect().has_point(p):
			return ctl
	return null


func _grown_game(seed: int) -> GameState:
	var g := GameState.new_game(seed)
	g.ensure_bonsai(true)
	var b := g.bonsai
	b.clock.time_of_day = b.clock.daylight_fraction * 0.3
	for _d in range(4):
		b.advance(6.0)
	return g


## A side branch toward the camera (for the shears and the wire), as a canvas point on its middle.
func _side_branch(b: BonsaiSim) -> int:
	var trunk := b.trunk_chain()
	var best := -1
	var best_y := INF
	for id in range(2, b.graph.size()):
		if trunk.has(id) or b.is_jin(id) or b.is_dead(id) or not trunk.has(b.graph.parents[id]):
			continue
		if b.subtree_leafy(id) < 3:
			continue
		var p := _view.plant_screen_position(id)
		if p.y < best_y and p.y > 300.0:
			best_y = p.y
			best = id
	return best


func _branch_point(b: BonsaiSim, id: int) -> Vector2:
	var g := b.graph
	return _view.plant_screen_position(g.parents[id]).lerp(_view.plant_screen_position(id), 0.6)


func test_every_tool_by_touch_on_phone_sizes() -> void:
	for size in SIZES:
		_tools_on(size[0], size[1])


func _tools_on(phys: Vector2i, canvas: Vector2i) -> void:
	var where := "%dx%d" % [phys.x, phys.y]
	var g := _grown_game(51)
	var b := g.bonsai
	_make(g, phys, canvas)
	var soil := _view.plant_screen_position(0) + Vector2(0, 10)
	var counts := {}
	# Every thing whole on the screen, at least finger size (9 mm across: 48 dp) and apart.
	var pts := _view.object_screen_points()
	var all := BonsaiTools.HELD + BonsaiTools.TAPPED
	for id in all:
		t.check(pts.has(id), "%s: on screen: %s" % [where, id])
		if not pts.has(id):
			continue
		for p in _view.tools.hull(_view.camera, id):
			t.check(p.x >= -BonsaiTools.HULL_GROW and p.x <= canvas.x + BonsaiTools.HULL_GROW and p.y <= canvas.y + BonsaiTools.HULL_GROW, "%s: %s whole on screen (%s)" % [where, id, p])
		t.check_eq(_view.pick_object(pts[id]), id, "%s: a tap on %s finds it" % [where, id])
		# No paper over a thing on the sill.
		for c in _hud.find_children("*", "Control", true, false):
			var ctl := c as Control
			if ctl.is_visible_in_tree() and ctl.mouse_filter != Control.MOUSE_FILTER_IGNORE and ctl != _hud._sheet:
				t.check(not ctl.get_global_rect().has_point(pts[id]), "%s: %s is not under the paper (%s)" % [where, id, ctl])
	var ppi := Vector2(phys).length() / 6.4
	var mm := BonsaiView.TOOL_TAP * 2.0 * _scale.x / ppi * 25.4
	t.check(mm >= 9.0, "%s: tap areas %.1f mm across" % [where, mm])
	for i in range(all.size()):
		for j in range(i + 1, all.size()):
			if pts.has(all[i]) and pts.has(all[j]):
				var d := (pts[all[i]] as Vector2).distance_to(pts[all[j]])
				t.check(d >= BonsaiView.TOOL_TAP * 2.0, "%s: %s and %s apart (%d px)" % [where, all[i], all[j], int(d)])
	# A touch anywhere on the soil or the pot never finds a thing on the sill, with any tool.
	var half := BonsaiView.soil_half(b.pot)
	var rim: Array[Vector2] = [soil]
	for k in range(12):
		var a := TAU * k / 12.0
		for r in [0.5, 0.9]:
			rim.append(_view.camera.unproject_position(_view._base.to_global(Vector3(cos(a) * half.x * r, BonsaiView.soil_height(b.pot), sin(a) * half.y * r))))
		rim.append(_view.camera.unproject_position(_view._base.to_global(Vector3(cos(a) * half.x, BonsaiView.soil_height(b.pot) * 0.5, sin(a) * half.y))))
	for tool in ["", "water", "fertiliser", "trowel"]:
		_view.set_tool(tool)
		_settle()
		for p in rim:
			if _view.on_bonsai(p):
				t.check_eq(_view.pick_object(p), "", "%s: with '%s' in hand a touch on the pot at %s finds no sill thing" % [where, tool, p])
	_view.set_tool("")
	_settle()

	# The watering can: pick up, aim at the soil (it lights up, nothing yet), lift to water.
	b.moisture = 0.1
	_pick_up("water", where + " can")
	if _aim(soil, "soil", where + " can"):
		t.check(_view._soil_ring.visible, where + " can: the soil's ring shows")
		t.check_near(b.moisture, 0.1, 0.02, where + " can: no water before the finger lifts")
	_release(soil)
	t.check(b.moisture > 0.5, "%s can: lifting the finger waters (%.2f)" % [where, b.moisture])
	t.check(not _view._soil_ring.visible, where + " can: the ring goes")
	counts["water"] = taps
	t.check_eq(_view.tool, "water", where + " can: still in hand after use")
	# A touch off everything with the can: nothing happens, it stays in hand.
	var m0 := b.moisture
	var empty := _empty_spot(canvas)
	t.check(empty.x >= 0.0, where + ": an empty spot on screen")
	_tap(empty)
	t.check(_view.tool == "water" and is_equal_approx(b.moisture, m0), where + " can: a touch on the wall does nothing")
	_put_down(where + " can")

	# The pellet tin: two taps for the remembered kind; the slip changes it.
	_pick_up("fertiliser", where + " tin")
	var kind := _view.pellet_kind
	var before := b.soil[kind]
	_aim(soil, "soil", where + " tin")
	t.check_near(b.soil[kind], before, 1e-5, where + " tin: no pellets before the finger lifts")
	_release(soil)
	t.check(b.soil[kind] > before, "%s tin: a spoon of %s" % [where, BonsaiHud.PELLETS[kind]])
	counts["fertiliser"] = taps
	var k_button := _hud._pellet_buttons[2]
	t.check(k_button.is_visible_in_tree(), where + " tin: the slip shows")
	_tap(k_button.get_global_rect().get_center())
	t.check_eq(_view.pellet_kind, 2, where + " tin: a tap on K on the slip chooses K")
	t.check_eq(_view.tool, "fertiliser", where + " tin: and the tin stays in hand")
	var k0 := b.soil[2]
	_tap(soil)
	t.check(b.soil[2] > k0, where + " tin: then a tap on the soil spoons K")
	_put_down(where + " tin")

	# The shears: touch a branch (the cut shows), lift to cut.
	var cut := _side_branch(b)
	t.check(cut >= 0, where + " shears: a side branch")
	_pick_up("shears", where + " shears")
	if cut >= 0:
		var at := _branch_point(b, cut)
		var green := b.leafy_count()
		if _aim(at, "cut", where + " shears"):
			t.check(_view.pruning.target >= 0, where + " shears: the cut is marked")
			t.check_eq(b.leafy_count(), green, where + " shears: nothing cut before the finger lifts")
		_release(at)
		t.check(b.leafy_count() < green, "%s shears: lifting cuts (%d -> %d)" % [where, green, b.leafy_count()])
	counts["shears"] = taps
	_put_down(where + " shears")

	# The tweezers: touch a fresh tip (ringed), lift to pinch.
	var tip := -1
	var trunk := b.trunk_chain()
	for id in b.living_tips():
		var p := _view.plant_screen_position(id)
		if b.is_fresh_tip(id) and not trunk.has(id) and p.y > 300.0 and p.x > 60.0 and p.x < canvas.x - 60.0:
			tip = id
			break
	t.check(tip >= 0, where + " tweezers: a fresh tip on screen")
	_pick_up("pinch", where + " tweezers")
	if tip >= 0:
		# A touch a little beside the tip still finds it (a fingertip's offset).
		var at := _view.plant_screen_position(tip) + Vector2(12, 14)
		if _aim(at, "tip", where + " tweezers"):
			t.check(_view._tip_ring.visible, where + " tweezers: the tip is ringed")
			t.check(not b.graph.get_flag(int(_view.aimed["id"]), "pinched", false), where + " tweezers: not pinched before the finger lifts")
		var aimed_tip := int(_view.aimed.get("id", -1))
		_release(at)
		t.check(aimed_tip >= 0 and b.graph.get_flag(aimed_tip, "pinched", false) == true, where + " tweezers: lifting pinches the ringed tip")
	counts["pinch"] = taps
	_put_down(where + " tweezers")

	# The copper wire: touch a branch (traced), drag it into a new line.
	var wid := _side_branch(b)
	_pick_up("wire", where + " wire")
	if wid >= 0:
		var at := _branch_point(b, wid)
		taps += 1
		_touch(at, true)
		_settle()
		t.check_eq(str(_view.aimed.get("kind", "")), "branch", where + " wire: pressing traces the branch")
		var traced := int(_view.aimed.get("id", -1))
		_slide(at, at + Vector2(0, 90))
		_release(at + Vector2(0, 90))
		t.check(b.wired().has(traced), "%s wire: the drag wires it (%s)" % [where, b.wired()])
		counts["wire"] = taps
		# A tap on the wired branch takes the wire off.
		var at2 := _branch_point(b, traced)
		_tap(at2)
		t.check(not b.wired().has(traced), where + " wire: a tap on a wired branch frees it")
	_put_down(where + " wire")

	# The trowel: on another day it only says when; on a repot day it lifts the tree out; the
	# shears trim the root ball; a pot on the slip; the trowel puts it back in fresh soil.
	b.repot_due = false
	var said := []
	var on_said := func(text: String) -> void: said.append(text)
	_view.said.connect(on_said)
	_pick_up("trowel", where + " trowel")
	_tap(soil)
	t.check(not _view.is_lifted() and said.size() == 1 and str(said[0]).begins_with("Not yet"), where + " trowel: not yet")
	b.repot_due = true
	taps = 1
	_aim(soil, "pot", where + " trowel")
	_release(soil)
	t.check(_view.is_lifted(), where + " trowel: on a repot day it lifts the tree out")
	counts["trowel"] = taps
	# Swapping tools: a tap on the shears while the trowel is in hand.
	_tap(_thing("shears"))
	t.check_eq(_view.tool, "shears", where + " repot: one tap swaps the trowel for the shears")
	var ball := _view.plant_screen_position(0) + Vector2(10, 0)
	_tap(ball)
	t.check(_view.trim_share() > 0.0, where + " repot: the shears trim the root ball")
	var oval := _hud._pot_buttons["oval"] as Button
	t.check(oval.is_visible_in_tree(), where + " repot: the pot slip shows")
	_tap(oval.get_global_rect().get_center())
	t.check_eq(_view.new_pot(), "oval", where + " repot: a tap on the slip picks the oval pot")
	_tap(_thing("trowel"))
	_tap(_view.plant_screen_position(0) + Vector2(0, 10))
	t.check(not _view.is_lifted() and b.pot == "oval", where + " repot: the trowel puts it back in the oval pot")
	_view.said.disconnect(on_said)
	_put_down(where + " trowel")
	_view.refresh(true)
	_settle()

	# The carved arrows turn the pot, also with a tool in hand (which stays in hand).
	var turn0 := b.turn
	taps = 0
	_tap(_thing("turn_right"))
	t.check_eq(b.turn, posmod(turn0 + 1, 4), where + " arrows: one tap turns the pot")
	counts["turn"] = taps
	_pick_up("water", where + " can again")
	_tap(_thing("turn_left"))
	t.check(b.turn == turn0 and _view.tool == "water", where + " arrows: with the can in hand the arrow turns, the can stays")
	# The other things: a tap opens their page, never waters.
	var m1 := b.moisture
	var opened: Array[String] = []
	var on_open := func(id: String) -> void: opened.append(id)
	_view.object_tapped.connect(on_open)
	for id in ["styles", "album", "cuttings"]:
		_tap(_thing(id))
		_hud.close_sheet()
		_settle()
	t.check_eq(opened, ["styles", "album", "cuttings"] as Array[String], where + ": the sketchbook, the album card and the cuttings open with one tap")
	t.check(is_equal_approx(b.moisture, m1), where + ": none of them watered")
	_view.object_tapped.disconnect(on_open)
	# Every other tool answers a tap while the can is in hand (a swap, never a pour).
	for id in BonsaiTools.HELD:
		if id == _view.tool:
			continue
		_tap(_thing(id))
		t.check_eq(_view.tool, id, "%s: a tap on the %s swaps to it" % [where, id])
	t.check(is_equal_approx(b.moisture, m1), where + ": no swap watered")
	_put_down(where + " last")
	# A finger's wobble (18 px) is still a tap.
	var p0 := _thing("pinch")
	_touch(p0, true)
	_slide(p0, p0 + Vector2(12, 13), 3)
	_touch(p0 + Vector2(12, 13), false)
	_settle()
	t.check_eq(_view.tool, "pinch", where + ": a wobbling tap still picks the tweezers up")
	_put_down(where + " wobble")
	# Taps per action (C1: at most two from the close-up, the pick-up included).
	for k in counts:
		t.check(int(counts[k]) <= 2, "%s: %s takes %d taps" % [where, k, int(counts[k])])
	print("%s taps per action: %s" % [where, counts])
	_free()
