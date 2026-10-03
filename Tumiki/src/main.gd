## Portrait layout, touch controls and HUD around the game view.
extends Control

const VIEW_ASPECT := 4.0 / 3.0  # game view height / width
const TOUCH_SENS := 1.35         # world units moved per world unit of finger movement
const KEY_SPEED := 0.4

var game: Game
var container: SubViewportContainer
var sub: SubViewport
var bg: BG
var overlay: Overlay
var font: Font

var view_rect := Rect2()
var slow_rect := Rect2()
var pause_rect := Rect2()
var yes_rect := Rect2()
var no_rect := Rect2()

var move_finger := -1
var slow_finger := -1
var move_acc := Vector2.ZERO  # accumulated finger movement in screen pixels
var autoplay := false
var shots_dir := ""
var frame := 0


func _ready() -> void:
	font = ThemeDB.fallback_font
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	container = SubViewportContainer.new()
	container.stretch = true
	container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(container)
	sub = SubViewport.new()
	sub.msaa_3d = Viewport.MSAA_2X
	sub.handle_input_locally = false
	container.add_child(sub)
	var we := WorldEnvironment.new()
	var env := Environment.new()
	env.background_mode = Environment.BG_CANVAS
	env.background_canvas_max_layer = 0
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	we.environment = env
	sub.add_child(we)
	bg = BG.new()
	sub.add_child(bg)
	game = Game.new()
	sub.add_child(game)
	var cl := CanvasLayer.new()
	cl.layer = 1
	sub.add_child(cl)
	overlay = Overlay.new()
	overlay.main = self
	cl.add_child(overlay)
	bg.game = game
	for a in OS.get_cmdline_user_args():
		if a == "--autoplay":
			autoplay = true
		elif a.begins_with("--shots="):
			shots_dir = a.substr(8)
	get_viewport().size_changed.connect(_layout)
	_layout()


func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT or what == NOTIFICATION_APPLICATION_PAUSED:
		if game != null and game.state == Game.IN_GAME:
			game.toggle_pause()


func _layout() -> void:
	var s := get_viewport_rect().size
	var w := s.x
	var h := w * VIEW_ASPECT
	var panel_min := 220.0
	if h + panel_min > s.y:
		h = s.y - panel_min
		w = h / VIEW_ASPECT
	var top := clampf((s.y - h - panel_min) * 0.25, 0, 90)
	view_rect = Rect2((s.x - w) / 2, top, w, h)
	container.position = view_rect.position
	container.size = view_rect.size
	var py := view_rect.end.y
	var ph := s.y - py
	var bs := minf(ph * 0.62, 200)
	slow_rect = Rect2(s.x - bs - 36, py + (ph - bs) / 2, bs, bs)
	pause_rect = Rect2(28, py + (ph - 90) / 2, 90, 90)
	yes_rect = Rect2(view_rect.position.x + w * 0.12, view_rect.position.y + h * 0.62, w * 0.34, 90)
	no_rect = Rect2(view_rect.position.x + w * 0.54, view_rect.position.y + h * 0.62, w * 0.34, 90)
	queue_redraw()


func _input(event: InputEvent) -> void:
	if event is InputEventScreenTouch:
		var p: Vector2 = event.position
		if event.pressed:
			if pause_rect.has_point(p) and (game.state == Game.IN_GAME or game.state == Game.PAUSE):
				game.toggle_pause()
				return
			if game.state == Game.GAMEOVER and game.credit > 0 and game.cnt > 64:
				if yes_rect.has_point(p):
					game.choose_continue(true)
					return
				if no_rect.has_point(p):
					game.choose_continue(false)
					return
			if game.state == Game.PAUSE:
				game.toggle_pause()
				return
			if slow_rect.grow(30).has_point(p) and slow_finger < 0:
				slow_finger = event.index
			elif move_finger < 0:
				move_finger = event.index
		else:
			if event.index == slow_finger:
				slow_finger = -1
			if event.index == move_finger:
				move_finger = -1
	elif event is InputEventScreenDrag:
		if event.index == move_finger:
			move_acc += event.relative
	elif event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_P or event.keycode == KEY_ESCAPE:
			game.toggle_pause()


func _physics_process(_delta: float) -> void:
	# Convert finger movement into world movement. Screen up = world +x, screen left = world +y.
	var k := 40.0 / view_rect.size.y * TOUCH_SENS
	var mv := Vector2(-move_acc.y * k, -move_acc.x * k)
	move_acc = Vector2.ZERO
	var kv := Vector2.ZERO
	if Input.is_key_pressed(KEY_UP) or Input.is_key_pressed(KEY_W):
		kv.x = KEY_SPEED
	elif Input.is_key_pressed(KEY_DOWN) or Input.is_key_pressed(KEY_S):
		kv.x = -KEY_SPEED
	if Input.is_key_pressed(KEY_LEFT) or Input.is_key_pressed(KEY_A):
		kv.y = KEY_SPEED
	elif Input.is_key_pressed(KEY_RIGHT) or Input.is_key_pressed(KEY_D):
		kv.y = -KEY_SPEED
	if kv.x != 0 and kv.y != 0:
		kv *= 0.707
	var key_slow := Input.is_key_pressed(KEY_X) or Input.is_key_pressed(KEY_SHIFT)
	game.in_move = mv + kv
	game.in_fire = true
	game.in_slow = slow_finger >= 0 or key_slow
	game.in_button = move_finger >= 0 or Input.is_key_pressed(KEY_Z) or Input.is_key_pressed(KEY_SPACE) \
		or Input.is_key_pressed(KEY_ENTER)
	if autoplay:
		_autoplay()
	bg.queue_redraw()
	overlay.queue_redraw()
	queue_redraw()


## Simple bot for automated testing: `-- --autoplay [--shots=<folder>] [--invincible]`.
func _autoplay() -> void:
	frame += 1
	game.in_button = (frame % 40) < 20
	if game.state == Game.GAMEOVER and game.cnt > 70:
		game.choose_continue(true)
	var t := frame * 0.02
	var target := Vector2(-10 + sin(t * 0.7) * 6, sin(t) * 12)
	game.in_move = (target - game.ship.pos) * 0.1
	game.in_slow = (frame % 300) < 40
	if shots_dir != "" and frame in [330, 900, 1500, 2100]:
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png(shots_dir.path_join("shot_%05d.png" % frame))
	if frame % 600 == 0:
		var nb := 0
		for b in game.bullets.pool.actor:
			if b.exists:
				nb += 1
		var ne := 0
		for e in game.enemies.actor:
			if e.exists:
				ne += 1
		print("f=%d state=%d stage=%d score=%d left=%d enemies=%d bullets=%d stuck=%d fps=%d" % [
			frame, game.state, game.stage, game.score, game.left, ne, nb, game.ship.stuck.count(),
			Engine.get_frames_per_second()])


func _draw() -> void:
	var s := get_viewport_rect().size
	draw_rect(Rect2(Vector2.ZERO, s), Color(0.06, 0.06, 0.08))
	if game == null:
		return
	if game.state == Game.IN_GAME or game.state == Game.PAUSE or game.state == Game.START_GAME:
		var on := slow_finger >= 0
		var c := slow_rect.get_center()
		var rad := slow_rect.size.x / 2
		draw_circle(c, rad, Color(0.6, 0.8, 0.8, 0.35 if on else 0.15))
		draw_arc(c, rad, 0, TAU, 64, Color(0.6, 0.8, 0.8, 0.9), 4, true)
		_text("SLOW", c + Vector2(0, 12), 34, Color(0.85, 0.95, 0.95), HORIZONTAL_ALIGNMENT_CENTER)
		var pc := pause_rect.get_center()
		draw_arc(pc, 40, 0, TAU, 48, Color(0.8, 0.8, 0.6, 0.8), 3, true)
		draw_rect(Rect2(pc + Vector2(-14, -16), Vector2(9, 32)), Color(0.8, 0.8, 0.6))
		draw_rect(Rect2(pc + Vector2(5, -16), Vector2(9, 32)), Color(0.8, 0.8, 0.6))
	var hint_y := view_rect.end.y + 34
	if game.state == Game.IN_GAME:
		_text("Drag anywhere to move", Vector2(s.x / 2 - 40, hint_y), 20, Color(1, 1, 1, 0.25), HORIZONTAL_ALIGNMENT_CENTER)


func _text(t: String, p: Vector2, size: int, c: Color, align := HORIZONTAL_ALIGNMENT_LEFT) -> void:
	var w := font.get_string_size(t, HORIZONTAL_ALIGNMENT_LEFT, -1, size).x
	var x := p.x
	if align == HORIZONTAL_ALIGNMENT_CENTER:
		x -= w / 2
	elif align == HORIZONTAL_ALIGNMENT_RIGHT:
		x -= w
	draw_string(font, Vector2(x, p.y), t, HORIZONTAL_ALIGNMENT_LEFT, -1, size, c)


## Draws the field background (sky, ground and mountains) behind the 3D scene.
class BG extends Node2D:
	var game: Game

	func _draw() -> void:
		if game == null or game.field.pattern == null:
			return
		var s := get_viewport_rect().size
		draw_rect(Rect2(Vector2.ZERO, s), game.field.pattern.back)
		# Original 640x480 landscape screen -> portrait view (landscape right = portrait up).
		var xf := Transform2D(Vector2(0, -s.y / 640.0), Vector2(s.x / 480.0, 0), Vector2(0, s.y))
		game.field.draw_back(self, xf)


## Text drawn over the game view: score, letters, score signs, title and game over screens.
class Overlay extends Node2D:
	const LETTER_COLORS := [Color(1, 0.6, 0.6), Color(0.6, 1, 0.6), Color(0.6, 0.6, 1),
		Color(1, 1, 0.6), Color(1, 0.6, 1), Color(0.6, 1, 1)]
	var main

	func _w2s(p: Vector2) -> Vector2:
		return main.game.camera.unproject_position(Vector3(p.x, p.y, 0))

	func _text(t: String, p: Vector2, size: int, c: Color, center := false, outline := 0) -> void:
		var f: Font = main.font
		var x := p.x
		if center:
			x -= f.get_string_size(t, HORIZONTAL_ALIGNMENT_LEFT, -1, size).x / 2
		if outline > 0:
			draw_string_outline(f, Vector2(x, p.y), t, HORIZONTAL_ALIGNMENT_LEFT, -1, size, outline, Color(0, 0, 0, 0.6 * c.a))
		draw_string(f, Vector2(x, p.y), t, HORIZONTAL_ALIGNMENT_LEFT, -1, size, c)

	func _draw() -> void:
		var g: Game = main.game
		if g == null:
			return
		var s := get_viewport_rect().size
		var sc := s.x / 640.0
		var wu := s.y / 40.0  # pixels per world unit
		# Bouncing letters (stage names, warnings).
		for ml in g.letters.pool.actor:
			if ml.exists:
				var fs := int(ml.size * 2.6 * sc)
				var p := Vector2(ml.pos.x * sc, ml.pos.y * sc + 40)
				draw_set_transform(p, deg_to_rad(ml.deg), Vector2.ONE)
				_text(ml.ch, Vector2(0, fs * 0.35), fs, LETTER_COLORS[ml.color % 6], true, 4)
				draw_set_transform(Vector2.ZERO, 0, Vector2.ONE)
		if g.state == Game.TITLE:
			_draw_title(g, s)
			return
		# Score signs.
		for ss in g.signs.actor:
			if ss.exists:
				var fs := int(maxf(ss.size * 2.6 * wu, 14))
				_text(str(ss.num), _w2s(ss.pos), fs, Color(0.8, 0.8, 0.6, minf(1, ss.cnt / 20.0)), true, 3)
		# "CATCH ME!" signs on the first splinters.
		for sp in g.splinters.actor:
			if sp.exists and sp.has_sign and (sp.cnt & 31) < 24:
				_text("CATCH ME!", _w2s(sp.pos) + Vector2(0, -wu * 2.5), int(wu * 1.3), Color(0.8, 0.8, 0.6), true, 4)
		# Score and boss timer.
		_text("SCORE", Vector2(16, 34), 22, Color(0.9, 0.9, 0.7), false, 3)
		_text(str(g.score), Vector2(100, 34), 30, Color(1, 1, 0.8), false, 4)
		_text("HI " + str(maxi(g.hi_score, g.score)), Vector2(s.x - 16 - 200, 34), 20, Color(0.8, 0.8, 0.8, 0.8), false, 3)
		if g.boss_timer < Game.BOSSTIMER_FREEZED and (g.boss_dst_cnt & 31) > 8:
			var t := maxi(g.boss_timer, 0)
			var tx := "%02d:%02d.%02d" % [t / 60000, (t / 1000) % 60, (t / 10) % 100]
			_text(tx, Vector2(s.x - 16 - 200, 68), 26, Color(1, 0.7, 0.5), false, 3)
		match g.state:
			Game.PAUSE:
				if (g.pause_cnt % 60) < 40:
					_text("PAUSE", Vector2(s.x / 2, s.y * 0.45), 64, Color(1, 1, 0.8), true, 6)
				_text("tap to resume", Vector2(s.x / 2, s.y * 0.45 + 50), 26, Color(1, 1, 1, 0.8), true, 3)
			Game.GAMEOVER:
				if g.credit > 0 and g.cnt > 64:
					var o: Vector2 = main.view_rect.position
					_text("CONTINUE?", Vector2(s.x / 2, s.y * 0.58), 40, Color(0.9, 0.9, 0.7), true, 5)
					for b in [[main.yes_rect, "YES"], [main.no_rect, "NO"]]:
						var r: Rect2 = b[0]
						var rr := Rect2(r.position - o, r.size)
						draw_rect(rr, Color(0, 0, 0, 0.35))
						draw_rect(rr, Color(0.9, 0.9, 0.7), false, 3)
						_text(b[1], rr.get_center() + Vector2(0, 14), 40, Color(1, 1, 0.8), true)
					_text("CREDIT %d" % g.credit, Vector2(s.x / 2, s.y * 0.62 + 140), 24, Color(0.8, 0.8, 0.8), true, 3)
				elif g.cnt > 64:
					_text("tap to return", Vector2(s.x / 2, s.y * 0.62), 26, Color(1, 1, 1, 0.8), true, 3)

	func _draw_title(g: Game, s: Vector2) -> void:
		var t := "TUMIKI"
		var t2 := "FIGHTERS"
		var fs := int(s.x / 6.2)
		var x0 := s.x / 2 - fs * 0.62 * t.length() / 2
		for i in t.length():
			var c: Color = LETTER_COLORS[(i * 5 + 1) % 6]
			_text(t[i], Vector2(x0 + i * fs * 0.62 + fs * 0.31, s.y * 0.26), fs, c, true, 8)
		var fs2 := int(s.x / 8.5)
		x0 = s.x / 2 - fs2 * 0.66 * t2.length() / 2
		for i in t2.length():
			var c: Color = LETTER_COLORS[(i * 2 + 3) % 6]
			_text(t2[i], Vector2(x0 + i * fs2 * 0.66 + fs2 * 0.33, s.y * 0.26 + fs2 * 1.1), fs2, c, true, 7)
		if g.cnt % 64 < 40:
			_text("TAP TO START", Vector2(s.x / 2, s.y * 0.62), 34, Color(1, 1, 0.8), true, 4)
		if g.hi_score > 0:
			_text("HI-SCORE  " + str(g.hi_score), Vector2(s.x / 2, s.y * 0.72), 26, Color(0.9, 0.9, 0.9), true, 3)
		_text("original game (c) 2004 Kenta Cho / ABA Games", Vector2(s.x / 2, s.y * 0.95), 16, Color(1, 1, 1, 0.6), true, 2)
