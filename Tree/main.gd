extends Node
## The game: owns the GameState and switches between tree mode (day) and root mode (night).
## Sunset: tap the ground and the camera dives, the sound crossfades to the hum.
## Sunrise: the camera rises out of the ground while the dawn burst twinkles.
## Dev keys (PC): T cycles time speed 1x/5x/20x, H hides the UI, F9 starts a new game.

const SETTINGS_PATH := "user://settings.json"
const AUTOSAVE_SECONDS := 60.0

var state: GameState
var tree_view: TreeView
var root_view: RootView
var ambience: Ambience
var journal: Journal
var time_scale: float = 1.0
## Set by tools: do not load or write the player's save.
var ephemeral: bool = false

var _fade: ColorRect
var _dev_label: Label
var _transitioning: bool = false
var _autosave_timer: float = 0.0
var _tween: Tween
var _underground: bool = false


func _ready() -> void:
	if OS.get_cmdline_user_args().has("--ephemeral"):
		ephemeral = true
	_build()
	if ephemeral:
		return  # tools start their own seeded game
	var loaded: GameState = SaveData.load_game()
	start(loaded if loaded != null else GameState.new_game(int(Time.get_unix_time_from_system())))


func _build() -> void:
	tree_view = TreeView.new()
	add_child(tree_view)
	root_view = RootView.new()
	add_child(root_view)
	ambience = Ambience.new()
	add_child(ambience)
	journal = Journal.new()
	add_child(journal)
	var fade_layer := CanvasLayer.new()
	fade_layer.layer = 30
	add_child(fade_layer)
	_fade = ColorRect.new()
	_fade.color = Color(0, 0, 0, 0)
	_fade.set_anchors_preset(Control.PRESET_FULL_RECT)
	_fade.mouse_filter = Control.MOUSE_FILTER_IGNORE
	fade_layer.add_child(_fade)
	_dev_label = Label.new()
	_dev_label.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	_dev_label.offset_left = 12
	_dev_label.offset_top = -40
	_dev_label.add_theme_font_size_override("font_size", 20)
	_dev_label.add_theme_color_override("font_shadow_color", Color.BLACK)
	fade_layer.add_child(_dev_label)

	tree_view.ground_tapped.connect(_on_ground_tapped)
	root_view.can_start = func() -> bool: return state.can_start_run()
	root_view.run_started.connect(func(_id: int) -> void: state.mark_run_started())
	root_view.run_finished.connect(func(_t: PackedFloat32Array) -> void: state.notify_run_done())
	root_view.find_touched.connect(func(f: Dictionary) -> void: state.notify_find(f))
	root_view.dots_collected.connect(func(n: int) -> void: ambience.play_collect(n))
	journal.page_closed.connect(_on_page_closed)
	journal.opened_changed.connect(_on_journal_opened)
	journal.setting_changed.connect(_apply_setting)
	_load_settings()


## Shows a game state: used on start, on load and by the new-game dev key.
func start(p_state: GameState) -> void:
	state = p_state
	journal.clear_pages()
	journal.state = state
	tree_view.setup(state)
	root_view.setup(state.ground, state.roots, state.sim.resources)
	# A new game or a load in the middle of a dive or sunrise: stop that transition.
	if _tween:
		_tween.kill()
	_transitioning = false
	_fade.color.a = 0.0
	tree_view.dive_amount = 0.0
	if state.phase == GameState.Phase.NIGHT:
		_show_underground(true)
		_enter_night_view()
	else:
		_show_underground(false)
	# Events from before the scenes existed (the planting sunset) are handled like live ones.
	_handle_events()


func _show_underground(on: bool) -> void:
	_underground = on
	tree_view.visible = not on
	tree_view.hud.visible = not on and not journal.settings["no_ui"]
	root_view.visible = on
	# The night needs its stick and dive button, so "no UI" only clears the day.
	root_view.hud.visible = on
	if on:
		root_view.camera.make_current()
	else:
		tree_view.camera.make_current()
	ambience.set_world(not on, 0.01)
	ambience.daylight = not on


# --- the loop ---------------------------------------------------------------

## The simulation runs in fixed steps, so the tree grows the same at 30 fps (battery saver),
## 60 fps or 120 fps from the same seed.
const SIM_STEP := 1.0 / 30.0
var _sim_accum: float = 0.0


func _process(delta: float) -> void:
	if state == null:
		return
	# A journal page pauses the game; transitions only block input (the dawn burst runs while the camera rises).
	var paused := journal.is_open()
	# No diary over a dive or a sunrise.
	journal.set_button_enabled(not _transitioning)
	tree_view.input_enabled = not paused and not _transitioning
	root_view.input_enabled = not paused and not _transitioning
	root_view.process_mode = Node.PROCESS_MODE_DISABLED if paused else Node.PROCESS_MODE_INHERIT
	if not paused:
		_sim_accum += delta * time_scale
		var steps := 0
		while _sim_accum >= SIM_STEP and steps < 40:
			state.tick(SIM_STEP)
			_sim_accum -= SIM_STEP
			steps += 1
		if steps == 40:
			_sim_accum = 0.0  # a long hitch: drop the rest rather than spiral
	_handle_events()
	ambience.daylight = state.phase == GameState.Phase.DAY
	_autosave_timer += delta
	if _autosave_timer >= AUTOSAVE_SECONDS:
		_autosave_timer = 0.0
		save()
	_dev_label.text = ("speed x%d" % int(time_scale)) if time_scale != 1.0 else ""


func _handle_events() -> void:
	for e in state.take_events():
		match e:
			"sunset":
				if state.is_seed() and state.day_number() == 0:
					_page_once("planted")
				else:
					_page_once("first_sunset")
			"spent":
				_spent_page()
			"night_empty":
				_page_once("empty_night")
			"run_done":
				_page_once("first_run_done")
				save()
			"sunrise":
				_rise()
			"morning":
				_morning()
			_:
				if e.begins_with("find:"):
					var kind := e.substr(5)
					journal.show_page("find", "A find", "I touched %s.\n\nIt is written in the diary now." % Underground.FIND_TEXTS.get(kind, kind))


## The first time the tree runs out: which nutrient is missing and where to find it tonight.
## Never before the first-morning page (it would come before the sapling is introduced).
func _spent_page() -> void:
	if not state.seen_pages.has("sapling"):
		state._spent_announced = false  # ask again after the first-morning page
		return
	if not state.first_time("spent"):
		return
	var body := Pages.body("spent")
	var missing: Array[String] = []
	var names: Array[String] = ["water (blue dots)", "nitrogen (green dots)", "phosphorus (orange dots)", "potassium (violet dots)"]
	for k in range(4):
		if state.sim.resources.stock[k] < state.sim.cost_per_node * state.sim.species.needs[k]:
			missing.append(names[k])
	if not missing.is_empty() and state.sim.resources.stock[0] >= state.sim.cost_per_node:
		body = "The tree is short of %s, so it only grows very slowly now. Tonight, steer the root toward them.

" % " and ".join(missing) + body
	journal.show_page("spent", Pages.title("spent"), body)


func _page_once(id: String) -> void:
	if state.first_time(id):
		journal.show_page(id, Pages.title(id), Pages.body(id))


func _on_page_closed(id: String) -> void:
	# The first root starts at the seed as soon as its page is read.
	if id == "first_night" and state.can_start_run() and state.roots.graph.size() <= 1:
		root_view.start_at(0)


## The open book covers the screen: the HUD scraps would only peek over its edge.
func _on_journal_opened(_open: bool) -> void:
	if _open:
		root_view.release_controls()
	var book := journal.is_book_open()
	tree_view.hud.visible = not book and not _underground and not journal.settings["no_ui"]
	root_view.hud.visible = not book and _underground


# --- switching --------------------------------------------------------------

func _on_ground_tapped() -> void:
	if _transitioning or not state.dive():
		return
	_dive()


func _dive() -> void:
	_transitioning = true
	tree_view.hud.visible = false
	ambience.set_world(false, 2.2)
	var tw := _new_tween()
	tw.tween_property(tree_view, "dive_amount", 1.0, 1.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	tw.parallel().tween_property(_fade, "color:a", 1.0, 0.7).set_delay(0.9)
	tw.tween_callback(func() -> void:
		_show_underground(true)
		tree_view.dive_amount = 0.0
		ambience.set_world(false, 0.8)
		_enter_night_view())
	tw.tween_property(_fade, "color:a", 0.0, 0.9)
	tw.tween_callback(func() -> void:
		_transitioning = false
		save())


func _enter_night_view() -> void:
	if state.roots.run_active:
		root_view.resume_run()
	elif state.night_empty or state.run_used:
		root_view.quiet_night = state.night_empty
		root_view.begin_idle_overview()
	else:
		root_view.begin_pick()
		if state.roots.graph.size() <= 1:
			# The very first night: the root starts at the seed once the page is read.
			if state.first_time("first_night"):
				journal.show_page("first_night", Pages.title("first_night"), Pages.body("first_night"))
			else:
				root_view.start_at(0)
		else:
			_page_once("pick")


func _rise() -> void:
	_transitioning = true
	var tw := _new_tween()
	tw.tween_property(_fade, "color:a", 1.0, 0.6)
	tw.tween_callback(func() -> void:
		_show_underground(false)
		tree_view.dive_amount = 1.0
		ambience.set_world(true, 2.5))
	tw.tween_property(_fade, "color:a", 0.0, 1.2)
	tw.parallel().tween_property(tree_view, "dive_amount", 0.0, 3.0).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	tw.tween_callback(func() -> void:
		_transitioning = false
		save())


func _new_tween() -> Tween:
	if _tween:
		_tween.kill()
	_tween = create_tween()
	return _tween


## After the dawn burst (GameState wrote the diary line): the first-morning page or the daily wish.
func _morning() -> void:
	if state.day_number() == 1:
		_page_once("sapling")
	elif state.diary.wish != "":
		journal.show_page("morning", "Day %d" % state.day_number(), state.diary.wish + "\n\n(Only a wish. Nothing happens if the day goes another way.)")


# --- settings, saving, dev keys -----------------------------------------------

func _apply_setting(key: String, on: bool) -> void:
	match key:
		"sound":
			ambience.set_enabled(on)
		"no_ui":
			# The journal button stays (faint), or the setting could never be switched back.
			tree_view.hud.visible = not on and not _underground
			journal.set_button_faint(on)
		"battery_saver":
			Engine.max_fps = 30 if on else 0
	_save_settings()


func _load_settings() -> void:
	# Tools (ephemeral runs) neither read nor write the player's settings.
	if ephemeral or not FileAccess.file_exists(SETTINGS_PATH):
		return
	var d: Variant = JSON.parse_string(FileAccess.get_file_as_string(SETTINGS_PATH))
	if d is Dictionary:
		for k in (d as Dictionary):
			if journal.settings.has(k):
				journal.set_setting(k, bool(d[k]))
				_apply_setting(k, bool(d[k]))


func _save_settings() -> void:
	if ephemeral:
		return
	var f := FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(journal.settings))


func save() -> void:
	if not ephemeral and state != null:
		SaveData.save_game(state)


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST or what == NOTIFICATION_APPLICATION_PAUSED:
		save()


func _unhandled_key_input(event: InputEvent) -> void:
	var k := event as InputEventKey
	if k == null or not k.pressed or k.echo:
		return
	match k.physical_keycode:
		KEY_T:
			time_scale = 5.0 if time_scale == 1.0 else (20.0 if time_scale == 5.0 else 1.0)
		KEY_H:
			var on: bool = not journal.settings["no_ui"]
			journal.set_setting("no_ui", on)
			_apply_setting("no_ui", on)
		KEY_F9:
			start(GameState.new_game(int(Time.get_unix_time_from_system())))
		KEY_J:
			if journal.is_open():
				journal.close_diary()
			elif not _transitioning:
				journal.open_diary()
		KEY_ESCAPE:
			journal.close_page()
			journal.close_diary()
