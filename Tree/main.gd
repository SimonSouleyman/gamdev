extends Node
## The game: owns the GameState and switches between tree mode (day) and root mode (night).
## Sunset: tap the ground and the camera dives, the sound crossfades to the hum.
## Sunrise: the camera rises out of the ground while the dawn burst twinkles.
## The game starts in the garden shed (start menu and pause scene) with the tree in the doorway.
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
var shed: Shed
var shed_menu: ShedMenu
var in_shed: bool = false
var _corner: CanvasLayer
var _shed_button: Button
var _photo_button: Button
var _shears_button: Button
var _flash: ColorRect


func _ready() -> void:
	if OS.get_cmdline_user_args().has("--ephemeral"):
		ephemeral = true
	_build()
	if Budgets.MAX_FPS > 0:
		Engine.max_fps = Budgets.MAX_FPS
	if ephemeral:
		return  # tools start their own seeded game
	# A drawn page covers the first frames while the forest grows.
	shed_menu.show_loading(true)
	_corner.visible = false
	journal.set_button_visible(false)
	await get_tree().process_frame
	await get_tree().process_frame
	var loaded: GameState = SaveData.load_game()
	start(loaded if loaded != null else GameState.new_game(int(Time.get_unix_time_from_system())))
	enter_shed(false)
	_corner.visible = true
	shed_menu.show_loading(false)
	# Back after a while: a torn diary page tells what happened meanwhile (once).
	_show_away_page()


func _build() -> void:
	# "clearer print" also reaches pages and labels made later.
	Paper.watch_print(get_tree())
	tree_view = TreeView.new()
	add_child(tree_view)
	root_view = RootView.new()
	add_child(root_view)
	ambience = Ambience.new()
	add_child(ambience)
	journal = Journal.new()
	add_child(journal)
	tree_view.page_open = func() -> bool: return journal.current_page() != ""
	shed = Shed.new()
	tree_view.add_child(shed)
	# The shed is only seen from inside, in the shed scene, never in the tree scene (Simon).
	shed.visible = false
	shed_menu = ShedMenu.new()
	add_child(shed_menu)
	shed_menu.settings = journal.settings
	shed_menu.show_menu(false)
	shed_menu.continue_pressed.connect(leave_shed)
	shed_menu.journal_pressed.connect(func() -> void: journal.open_diary())
	shed_menu.plant_pressed.connect(plant_next)
	shed_menu.setting_changed.connect(func(k: String, on: bool) -> void:
		journal.set_setting(k, on)
		_apply_setting(k, on))
	_build_corner()
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
	tree_view.pruned.connect(func(n: int) -> void:
		state.diary.add(state.day_number(), "I cut off a branch (%d segments)." % n))
	root_view.can_start = func() -> bool: return state.can_start_run()
	root_view.run_started.connect(func(_id: int) -> void: state.mark_run_started())
	# Swipe up after the night's root: straight on to the morning.
	root_view.swipe_up.connect(func() -> void:
		if state.phase == GameState.Phase.NIGHT and (state.night_done or state.night_empty) and not _transitioning:
			state.night_done = true
			state.tick(9999.0))
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
	shed_menu.set_tree_name(state.tree_name())
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
	# Pages that were open when the game was saved come back.
	for id in state.pending_pages:
		if Pages.has(id) and not journal.pending_ids().has(id):
			journal.show_page(id, Pages.title(id), Pages.body(id))


func _show_underground(on: bool) -> void:
	_underground = on
	# By night the tree scene is hidden: the time to widen the clearing for a grown tree.
	if on:
		tree_view.refresh_clearing()
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
	var paused := journal.is_open() or in_shed or shed_menu.is_tree_page_open()
	# No diary over a dive or a sunrise.
	journal.set_button_enabled(not _transitioning)
	tree_view.input_enabled = not paused and not _transitioning
	_shed_button.visible = not in_shed and not _transitioning
	_photo_button.visible = not in_shed and not _underground and not _transitioning and state.phase == GameState.Phase.DAY
	_shears_button.visible = _photo_button.visible and state.sim.graph.size() > 6
	if tree_view.prune_mode and not _shears_button.visible:
		_set_shears(false)
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
	if in_shed:
		_update_shed_tags()


func _handle_events() -> void:
	for e in state.take_events():
		match e:
			"sunset":
				if state.is_seed() and state.day_number() == 0:
					_page_once("planted")
					# Each species introduces itself once, the first time it is planted.
					_page_once(Pages.species_page(state.sim.species.id))
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
			"finished":
				Haptics.buzz("finished")
				state.seen_pages["finished"] = true
				journal.show_page("finished", Pages.title("finished"), Pages.body("finished"))
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
	tree_view.hud.visible = not book and not _underground and not in_shed and not journal.settings["no_ui"]
	root_view.hud.visible = not book and _underground and not in_shed


# --- switching --------------------------------------------------------------

func _on_ground_tapped() -> void:
	if _transitioning or not state.dive():
		return
	_dive()


func _dive() -> void:
	_transitioning = true
	Haptics.buzz("dive")
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
		# Closed during the pause after a run: the night still has to move on.
		if state.run_used and not state.night_done and not state.night_empty:
			state.notify_run_done()
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
	# A photo for the album every morning, after the dawn burst.
	await _take_photo("morning")
	# Visitors come as the tree grows (diary lines; the nest and the bench stay in view).
	if not Visitors.arrive(state).is_empty():
		tree_view.update_visitors()
	if state.day_number() == 1:
		_page_once("sapling")
	elif state.diary.wish != "":
		journal.show_page("morning", "Day %d" % state.day_number(), state.diary.wish + "\n\n(Only a wish. Nothing happens if the day goes another way.)")


# --- settings, saving, dev keys -----------------------------------------------

func _apply_setting(key: String, on: bool, from_player: bool = true) -> void:
	match key:
		"sound":
			ambience.set_enabled(on)
		"no_ui":
			# The journal button stays (faint), or the setting could never be switched back.
			tree_view.hud.visible = not on and not _underground and not in_shed
			journal.set_button_faint(on)
		"battery_saver":
			Engine.max_fps = 30 if on else Budgets.MAX_FPS
		"vibration":
			Haptics.enabled = on
		"clearer_print":
			Paper.set_clear_print(on, get_tree().root)
		"notifications":
			# A daily reminder on the phone (plain Android, no Google services), re-armed at start.
			if on:
				if from_player:
					Phone.ask_notification_permission()
				Phone.schedule_daily_reminder(9, 0, "Tree", "A new morning in the clearing. See what grew overnight.")
			else:
				Phone.cancel_daily_reminder()
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
				_apply_setting(k, bool(d[k]), false)


func _save_settings() -> void:
	if ephemeral:
		return
	var f := FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(journal.settings))


func save() -> void:
	if not ephemeral and state != null:
		# An unread tutorial page must not be lost when the game closes while it is open.
		state.pending_pages = journal.pending_ids().filter(func(id: String) -> bool: return Pages.has(id))
		SaveData.save_game(state)


var _paused_at: float = -1.0
var _photo_busy := false


## Esc on a PC, the back gesture on a phone: close what is open, else go to the shed.
## In the shed with nothing open, back leaves the game (saved), as Android players expect.
func _back() -> void:
	if journal.is_open():
		journal.close_page()
		journal.close_diary()
	elif shed_menu.is_busy():
		shed_menu.close_boards()
	elif not in_shed and not _transitioning:
		enter_shed(true)
	elif in_shed and not _transitioning and Budgets.PHONE:
		save()
		get_tree().quit()


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_GO_BACK_REQUEST:
		_back()
		return
	if what == NOTIFICATION_WM_CLOSE_REQUEST or what == NOTIFICATION_APPLICATION_PAUSED:
		save()
		_paused_at = Time.get_unix_time_from_system()
	elif what == NOTIFICATION_APPLICATION_RESUMED and _paused_at > 0.0 and state != null:
		# Back from the background without a restart: the tree grew a little meanwhile too.
		var away := clampf(Time.get_unix_time_from_system() - _paused_at, 0.0, 7.0 * 86400.0)
		_paused_at = -1.0
		if away > 1.0:
			state.apply_offline(away)
			tree_view.update_visitors()
			_show_away_page()


# --- the garden shed -----------------------------------------------------------------

## The two scraps in the corner: back to the shed (pause), and the camera for the album.
func _build_corner() -> void:
	_corner = CanvasLayer.new()
	_corner.layer = 19
	add_child(_corner)
	_shed_button = _scrap("shed", Vector2(-160, -120))
	_shed_button.pressed.connect(func() -> void:
		if not _transitioning and not journal.is_open():
			enter_shed(true))
	_photo_button = _scrap("photo", Vector2(-160, -60))
	_photo_button.pressed.connect(func() -> void: _take_photo("camera"))
	# The shears: while out, a tap cuts a branch instead of boosting the sun (play test 4).
	_shears_button = _scrap("shears", Vector2(-160, 0))
	_shears_button.pressed.connect(func() -> void:
		_set_shears(not tree_view.prune_mode)
		if tree_view.prune_mode:
			_page_once("shears"))
	_flash = ColorRect.new()
	_flash.color = Color(1, 1, 1, 0)
	_flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_corner.add_child(_flash)


func _set_shears(on: bool) -> void:
	tree_view.prune_mode = on
	if not on:
		tree_view.pruning.preview(-1)
	_shears_button.text = "put away" if on else "shears"
	# The shears glow while out; on a PC the pointer turns into a cross (a scissor picture comes
	# with the picture symbols in 0.6).
	_shears_button.modulate = Color(1.35, 1.2, 0.8) if on else Color.WHITE
	Input.set_default_cursor_shape(Input.CURSOR_CROSS if on else Input.CURSOR_ARROW)


func _scrap(text: String, at: Vector2) -> Button:
	var b := Button.new()
	b.text = text
	b.focus_mode = Control.FOCUS_NONE
	b.add_theme_font_override("font", Paper.hand_font(true))
	b.add_theme_font_size_override("font_size", 24)
	for k in ["font_color", "font_hover_color", "font_pressed_color"]:
		b.add_theme_color_override(k, Paper.INK)
	for k in ["normal", "hover", "pressed"]:
		b.add_theme_stylebox_override(k, Paper.paper_box(96, 48, 110 + absi(int(at.y)), "all", 12.0))
	# Middle right (at.y is relative to the screen's middle), clear of compass and sun arc.
	b.set_anchors_preset(Control.PRESET_CENTER_RIGHT)
	b.offset_left = at.x
	b.offset_right = -20
	b.offset_top = at.y
	b.offset_bottom = at.y + 46
	b.rotation_degrees = -1.5
	_corner.add_child(b)
	return b


## Into the shed: the game pauses, the tree stands in the doorway, the menu note is on the wall.
func enter_shed(animate: bool) -> void:
	if in_shed:
		return
	in_shed = true
	journal.hold_pages()
	journal.close_diary()
	journal.set_button_visible(false)
	var switch := func() -> void:
		tree_view.visible = true
		root_view.visible = false
		root_view.hud.visible = false
		tree_view.hud.visible = false
		shed.visible = true
		shed.place()
		tree_view.set_shed_open(true)
		shed.frame_tree(state.sim.height(), tree_view.camera.environment)
		shed.camera.make_current()
		shed_menu.show_menu(true)
		ambience.set_world(true, 0.8)
	if animate:
		_transitioning = true
		var tw := _new_tween()
		tw.tween_property(_fade, "color:a", 1.0, 0.35)
		tw.tween_callback(switch)
		tw.tween_property(_fade, "color:a", 0.0, 0.45)
		tw.tween_callback(func() -> void: _transitioning = false)
	else:
		switch.call()
	save()


## Out of the shed and back into the day (or the night, if the game was left underground).
func leave_shed() -> void:
	if not in_shed or _transitioning:
		return
	_transitioning = true
	shed_menu.show_menu(false)
	var tw := _new_tween()
	tw.tween_property(_fade, "color:a", 1.0, 0.35)
	tw.tween_callback(func() -> void:
		in_shed = false
		shed.visible = false
		tree_view.set_shed_open(false)
		tree_view.dive_amount = 0.0
		journal.set_button_visible(true)
		# Pages that were waiting (the first tutorial page) show now, in the game.
		journal.release_pages()
		_show_underground(state.phase == GameState.Phase.NIGHT))
	tw.tween_property(_fade, "color:a", 0.0, 0.45)
	tw.tween_callback(func() -> void: _transitioning = false)


func _unhandled_input(event: InputEvent) -> void:
	if not in_shed or _transitioning or journal.is_open() or shed_menu.is_busy() or _shed_tapped != "":
		_shed_hover = ""
		return
	# A pointer resting on a thing shows its label (PC; on a phone the first visits show them).
	var motion := event as InputEventMouseMotion
	if motion != null:
		_shed_hover = shed.item_at(motion.position)
		return
	var m := event as InputEventMouseButton
	if m == null or m.pressed or m.button_index != MOUSE_BUTTON_LEFT:
		return
	var item := shed.item_at(m.position)
	if item == "":
		return
	# The thing answers first (sound and a small motion), then its page opens.
	_shed_tapped = item
	state.seen_pages["shed_used_" + item] = true
	await get_tree().create_timer(shed.tap(item)).timeout
	_shed_tapped = ""
	if not in_shed or journal.is_open() or shed_menu.is_busy():
		return
	open_shed_item(item)


var _shed_hover: String = ""
var _shed_tapped: String = ""


## What a thing in the shed opens: the journal the diary, the album the photos, the seed bag
## its page (and the next seed), the flower pot the tree's own page, the pinboard the options;
## the garden gloves and the open door lead outside.
func open_shed_item(item: String) -> void:
	match item:
		"journal":
			journal.open_diary()
		"album":
			shed_menu.open_album()
		"options":
			shed_menu.open_options()
		"seeds":
			shed_menu.open_seeds(state, bool(journal.settings.get("any_species", false)))
		"pot":
			shed_menu.show_tree_page(state)
		"gloves", "door":
			leave_shed()


## The labels beside the things: shown until a thing was used once, and while hovered.
func _update_shed_tags() -> void:
	var at := {}
	var below := {}
	var shown := {}
	var free := not (_transitioning or journal.is_open() or shed_menu.is_busy())
	for item in Shed.ITEMS:
		at[item] = shed.item_tag_position(item)
		below[item] = shed.item_tag_below(item)
		shown[item] = free and (_shed_hover == item or not state.seen_pages.has("shed_used_" + item))
	shed_menu.place_tags(at, below, shown)


## The "while you were away" page, once, when the game comes back after a while.
func _show_away_page() -> void:
	if state == null:
		return
	var report := state.take_away_report()
	if not report.is_empty():
		shed_menu.show_tree_page(state, report)


## The seed bag planted the next tree: a new game of that species beside the old one. The grove
## (finished trees, which unlock the species) and the pages already read carry over; the photo
## album is kept, each photo captioned with its tree. Only when this tree is finished, or with
## the test switch "any species now" on the options pinboard.
func plant_next(species_id: String) -> void:
	if state == null or not state.can_plant_next(bool(journal.settings.get("any_species", false))):
		return
	if not state.unlocked_species(bool(journal.settings.get("any_species", false))).has(species_id):
		return
	start(GameState.new_tree(int(Time.get_unix_time_from_system()), species_id, state))
	in_shed = false
	enter_shed(false)


## Saves a photo of the tree for the album (without the HUD), with a camera flash.
func _take_photo(tag: String) -> void:
	if ephemeral or in_shed or _underground or _photo_busy:
		return
	_photo_busy = true
	# Never through the black fade of a sunrise or a trip to the shed.
	while _transitioning:
		await get_tree().process_frame
	if in_shed or _underground:
		_photo_busy = false
		return
	var huds: Array = [tree_view.hud, journal, _corner]
	var was: Array = []
	for h in huds:
		was.append((h as CanvasLayer).visible)
		(h as CanvasLayer).visible = false
	await RenderingServer.frame_post_draw
	Photos.save_from(get_viewport(), state.day_number(), tag, state.sim.species.id)
	for i in range(huds.size()):
		(huds[i] as CanvasLayer).visible = was[i]
	_photo_busy = false
	if tag == "camera":
		_flash.color.a = 0.8
		create_tween().tween_property(_flash, "color:a", 0.0, 0.4)


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
			Photos.clear()
			start(GameState.new_game(int(Time.get_unix_time_from_system())))
			in_shed = false
			enter_shed(false)
		KEY_J:
			if journal.is_open():
				journal.close_diary()
			elif not _transitioning:
				journal.open_diary()
		KEY_ESCAPE:
			_back()
