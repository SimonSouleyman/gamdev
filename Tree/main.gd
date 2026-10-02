extends Node
## The game: owns the GameState and switches between tree mode (day) and root mode (night).
## Sunset: tap the ground and the camera dives, the sound crossfades to the hum.
## Sunrise: the camera rises out of the ground while the dawn burst twinkles.
## The game starts in the garden shed (start menu and pause scene) with the tree in the doorway.
## Dev keys (PC): T cycles time speed 1x/5x/20x, H hides the UI, F9 starts a new game.

const SETTINGS_PATH := "user://settings.json"
const AUTOSAVE_SECONDS := 60.0
## The pointer while the shears are out (PC).
const SHEARS_CURSOR := preload("res://ui/icons/shears_cursor.png")

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
## The dive's first half (the tree scene still in view): the corner pictures go with the tree
## HUD. 0.8.2.2: they no longer wait for the end of every transition, so coming out of the shed
## they appear with the view (they came a moment after the journal button).
var _diving: bool = false
var shed: Shed
var shed_menu: ShedMenu
var in_shed: bool = false
## The bonsai on the windowsill (design doc section 16) and bonsai mode, its close-up.
var bonsai_view: BonsaiView
var bonsai_hud: BonsaiHud
var in_bonsai: bool = false
## A milestone came while the bonsai was not in view: its album photo waits for the next visit.
var _bonsai_photo_due: bool = false
var _corner: CanvasLayer
var _shed_button: TextureButton
var _photo_button: TextureButton
var _shears_button: TextureButton
## The sunset picture (0.8.2.4): one tap runs the rest of the day quickly to the sunset hold.
## 0.8.2.5: a walnut hourglass, the sand nearly run (Simon's pick of four; ui/icons/sunset.png).
var _sunset_button: TextureButton
var _shears_glow: TextureRect
var _glow_tween: Tween
var _flash: ColorRect
## The live picture's layers for the phone's wallpaper and screen saver (specs/0.8.md section 6).
var live_export: LiveExport


func _ready() -> void:
	# Screenshots and PC tests can force a season, weather or moon (--season=autumn ...).
	Almanac.read_cmdline()
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
	var had_save := FileAccess.file_exists(SaveData.GAME_PATH)
	var loaded: GameState = SaveData.load_game()
	start(loaded if loaded != null else GameState.new_game(int(Time.get_unix_time_from_system())))
	enter_shed(false)
	_corner.visible = true
	shed_menu.show_loading(false)
	# 0.8: the save could not be read: offer the newest good sunrise save instead of starting over.
	if loaded == null and had_save:
		_offer_morning()
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
	live_export = LiveExport.new()
	add_child(live_export)
	journal = Journal.new()
	add_child(journal)
	tree_view.page_open = func() -> bool: return journal.current_page() != ""
	# The Collection lists the finds kept in the bench's drawers (0.8.2.6).
	journal.finds_source = func() -> Array:
		var out: Array = []
		if state != null and state.finds != null:
			for f in state.finds.list():
				out.append({"kind": f["kind"], "text": "%s %s" % [f["note"], Finds.when_line(f)], "day": f["day"]})
		return out
	shed = Shed.new()
	tree_view.add_child(shed)
	# The shed is only seen from inside, in the shed scene, never in the tree scene (Simon).
	shed.visible = false
	bonsai_view = BonsaiView.new()
	shed.bonsai_spot.add_child(bonsai_view)
	bonsai_view.tool_used.connect(_on_bonsai_tool)
	bonsai_hud = BonsaiHud.new()
	add_child(bonsai_hud)
	bonsai_hud.view = bonsai_view
	bonsai_hud.any_species = func() -> bool: return bool(journal.settings.get("any_species", false))
	bonsai_hud.first_page = func(id: String) -> void: _page_once("bonsai_" + id)
	bonsai_hud.back_pressed.connect(leave_bonsai)
	shed_menu = ShedMenu.new()
	add_child(shed_menu)
	shed_menu.settings = journal.settings
	shed_menu.show_menu(false)
	shed_menu.continue_pressed.connect(leave_shed)
	shed_menu.journal_pressed.connect(func() -> void: journal.open_diary())
	shed_menu.plant_pressed.connect(plant_next)
	shed_menu.reset_pressed.connect(_reset)
	# 0.8: the save backup on the pinboard.
	shed_menu.backup_notes.fetch_state = func() -> GameState:
		save()
		return state
	shed_menu.backup_notes.load_confirmed.connect(_load_copy)
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
	tree_view.weather_fx.thunder.connect(ambience.play_thunder)
	# A cut is routine (0.8.2): the care page's "last cut" tells what it did, no diary line.
	root_view.can_start = func() -> bool: return state.can_start_run()
	root_view.run_started.connect(func(_id: int) -> void: state.mark_run_started())
	# Swipe down after the night's root: straight on to the morning (0.8.2.2: was up).
	root_view.swipe_back.connect(func() -> void:
		if state.phase == GameState.Phase.NIGHT and (state.night_done or state.night_empty) and not _transitioning:
			state.night_done = true
			state.tick(9999.0))
	root_view.run_finished.connect(func(_t: PackedFloat32Array) -> void: state.notify_run_done())
	root_view.find_touched.connect(func(f: Dictionary) -> void: state.notify_find(f))
	root_view.dots_collected.connect(func(n: int) -> void: ambience.play_collect(n))
	# 0.8.2 first-time lines: the far view's hint until it was opened once, and one line the
	# first time a root meets a rock band or a soft vein.
	root_view.far_view_opened.connect(func() -> void:
		state.first_time("far_view")
		root_view.far_hint = false)
	root_view.first_note = func(id: String) -> bool: return state.first_time("note_" + id)
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
	_meadow_wishes = state.ground.wish_deposits.size()
	# The test switch "any species now" also opens the bonsai.
	state.ensure_bonsai(bool(journal.settings.get("any_species", false)))
	bonsai_view.setup(state)
	bonsai_hud.state = state
	root_view.setup(state.ground, state.roots, state.sim.resources)
	# A new game or a load in the middle of a dive or sunrise: stop that transition.
	if _tween:
		_tween.kill()
	_fade_dive_ui(1.0)
	_transitioning = false
	_diving = false
	tree_view.rising = false
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
	# The phone's live picture shows this tree (a new tree replaces a finished one there too).
	_refresh_live.call_deferred(true)


func _show_underground(on: bool) -> void:
	_underground = on
	# By night the tree scene is hidden: the time to widen the clearing for a grown tree.
	# At sunrise (behind the black fade) the ground under the crown catches up (mushrooms).
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
## The most sim time a fast-forwarded frame may take (ms); see _process.
const FF_BUDGET_MS := 12
## Wish deposits the meadow shows plants for (it is rebuilt when a new one is placed).
var _meadow_wishes: int = -1
var _sim_accum: float = 0.0


func _process(delta: float) -> void:
	if state == null:
		return
	if in_shed:
		var h := state.sim.clock.sun_height()
		shed.daylight = clampf(h * 4.0, 0.0, 1.0) if state.phase == GameState.Phase.DAY else 0.0
	# A journal page pauses the game; transitions only block input (the dawn burst runs while the camera rises).
	var paused := journal.is_open() or in_shed or shed_menu.is_tree_page_open()
	# In bonsai mode the day runs on (the bonsai lives through the same days as the tree), but
	# only by day: the evening holds, as it does on the clearing, until the player dives.
	var bonsai_live := in_bonsai and not journal.is_open() and state.phase == GameState.Phase.DAY
	bonsai_view.input_enabled = not journal.is_open() and not bonsai_hud.is_busy() and not _transitioning
	# No diary over a dive or a sunrise.
	journal.set_button_enabled(not _transitioning)
	tree_view.input_enabled = not paused and not _transitioning
	# The "while you were away" page lies over the game: the pictures at the right would sit on
	# it and stay live (0.6 QA).
	_update_corner()
	root_view.input_enabled = not paused and not _transitioning
	root_view.process_mode = Node.PROCESS_MODE_DISABLED if paused else Node.PROCESS_MODE_INHERIT
	if not paused or bonsai_live:
		# Held to fast-forward (specs/fast-forward.md): more fixed game-time steps per frame, the
		# same steps; the result does not depend on the speed.
		_sim_accum += delta * time_scale * tree_view.time_speed()
		var steps := 0
		# 0.8.2.2: held at 8x the sim runs twice the steps of 0.8.2; past FF_BUDGET_MS of them in
		# a frame the day runs a little slower instead of the frame rate dropping (the steps stay
		# the same fixed steps, so the tree is the same).
		var budget := FF_BUDGET_MS * 1000 if tree_view.time_speed() > 1.0 else 1 << 40
		var t0 := Time.get_ticks_usec()
		while _sim_accum >= SIM_STEP and steps < 40:
			state.tick(SIM_STEP)
			_sim_accum -= SIM_STEP
			steps += 1
			if Time.get_ticks_usec() - t0 > budget:
				_sim_accum = minf(_sim_accum, SIM_STEP)
				break
		if steps == 40:
			_sim_accum = 0.0  # a long hitch: drop the rest rather than spiral
	_handle_events()
	if state.bonsai != null:
		for e in state.bonsai.take_events():
			_bonsai_event(e)
	ambience.daylight = state.phase == GameState.Phase.DAY
	ambience.rain = tree_view.rain_now if not in_shed else tree_view.rain_now * 0.6
	_autosave_timer += delta
	if _autosave_timer >= AUTOSAVE_SECONDS:
		_autosave_timer = 0.0
		save()
	_dev_label.text = ("speed x%d" % int(time_scale)) if time_scale != 1.0 else ""
	if in_shed and not in_bonsai:
		_update_shed_tags()


func _handle_events() -> void:
	for e in state.take_events():
		match e:
			"sunset":
				# The day's growth is done: the live picture shows it, drawn behind the dive's black
				# (0.8.2.2: drawn here it froze the sunset for about 1.5 s on the phone).
				_live_due = true
				# 0.8.2.5: a small ink ring round today's wish plant, before the dive.
				tree_view.show_wish_ring()
				if state.is_seed() and state.day_number() == 0:
					_page_once("planted")
					# Each species introduces itself once, the first time it is planted from the seed bag
					# (not the very first linden: the tutorial pages introduce it).
					if not state.grove.is_empty() or state.sim.species.id != "linden":
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
				# The day's wish may have placed a deposit: its rushes or clover come up on the meadow.
				if state.ground.wish_deposits.size() != _meadow_wishes:
					_meadow_wishes = state.ground.wish_deposits.size()
					tree_view.refresh_meadow()
				# Today's wish place (a wish for a deposit already there places none).
				tree_view.refresh_wish()
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
					var body := "I touched %s. It goes in the bench's drawer." % Underground.FIND_TEXTS.get(kind, kind)
					if str(state.finds.hint.get("from", "")) == kind and int(state.finds.hint.get("night", -1)) == state.day_number() + 1:
						body += " " + Finds.HINT_PAGE[str(state.finds.hint["kind"])]
					journal.show_page("find", "A find", body, kind)


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
		body = "Short of %s: it grows slowly now. Steer tonight's root to them.

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
	tree_view.show_wish_ring()
	Haptics.buzz("dive")
	ambience.set_world(false, 2.2)
	var tw := _new_tween()
	tw.tween_property(tree_view, "dive_amount", 1.0, 1.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	# 0.8.2.4 (phone: the HUD vanished in one frame, the journal button stayed through the dive):
	# the HUD, the corner pictures and the journal button fade out together, then hide.
	_dive_ui = []
	for c in tree_view.hud.get_children() + [_shed_button, _photo_button, _shears_button, journal.open_button()]:
		if c is CanvasItem:
			_dive_ui.append([c, (c as CanvasItem).modulate.a])
	tw.parallel().tween_method(_fade_dive_ui, 1.0, 0.0, DIVE_UI_FADE)
	tw.parallel().tween_callback(func() -> void:
		_diving = true
		tree_view.hud.visible = false
		_fade_dive_ui(1.0)).set_delay(DIVE_UI_FADE)
	tw.parallel().tween_property(_fade, "color:a", 1.0, 0.7).set_delay(0.9)
	tw.tween_callback(func() -> void:
		_diving = false
		_show_underground(true)
		tree_view.dive_amount = 0.0
		ambience.set_world(false, 0.8)
		_enter_night_view()
		_hold_black(tw, _live_behind_black))
	tw.tween_property(_fade, "color:a", 0.0, 0.9)
	tw.tween_callback(func() -> void:
		_transitioning = false
		save())


## How long the day's HUD takes to fade out at the dive's start (s).
const DIVE_UI_FADE := 0.35
## [CanvasItem, its alpha] for each thing that fades out at the dive's start.
var _dive_ui: Array = []


func _fade_dive_ui(v: float) -> void:
	for e in _dive_ui:
		if is_instance_valid(e[0]):
			(e[0] as CanvasItem).modulate.a = float(e[1]) * v


func _enter_night_view() -> void:
	# The day's wish glows underground, also on a quiet night (0.7).
	root_view.set_wish_glows(state.wish_glows())
	# A map scrap's or shard's mark, on the night after it was found (0.8.2.6).
	root_view.set_find_hint(state.finds.hint_for(state.day_number()))
	if state.roots.run_active:
		root_view.resume_run()
	elif state.night_empty or state.run_used:
		# Closed during the pause after a run: the night still has to move on.
		if state.run_used and not state.night_done and not state.night_empty:
			state.notify_run_done()
		root_view.quiet_night = state.night_empty
		root_view.begin_idle_overview()
	else:
		root_view.far_hint = state.roots.main_root_count >= FAR_HINT_NIGHTS and not state.seen_pages.has("far_view")
		root_view.begin_pick()
		if state.roots.graph.size() <= 1:
			# The very first night: the root starts at the seed once the page is read.
			if state.first_time("first_night"):
				journal.show_page("first_night", Pages.title("first_night"), Pages.body("first_night"))
			else:
				root_view.start_at(0)
		else:
			_page_once("pick")


## 0.8.2: the far view's one-line hint shows on the pick nights from this many roots on (by then
## the roots reach past the overview's frame), until the player has opened it once.
const FAR_HINT_NIGHTS := 4


func _rise() -> void:
	_transitioning = true
	var tw := _new_tween()
	tw.tween_property(_fade, "color:a", 1.0, 0.6)
	tw.tween_callback(func() -> void:
		_show_underground(false)
		tree_view.dive_amount = 1.0
		# 0.8.2.4: the sunrise rises from above the grass, outside the crown (TreeView.rising).
		tree_view.rising = true
		# The camera rises from its first frame on: placed now, not one frame late (0.8.2.2).
		tree_view.snap_camera()
		ambience.set_world(true, 2.5)
		_hold_black(tw, _morning_behind_black))
	tw.tween_property(_fade, "color:a", 0.0, 1.2)
	tw.parallel().tween_property(tree_view, "dive_amount", 0.0, 3.0).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	tw.tween_callback(func() -> void:
		_transitioning = false
		tree_view.rising = false
		save()
		# 0.8: the game keeps its last three sunrise saves, quietly (never a note or a reminder).
		if not ephemeral:
			Backup.keep_morning())


func _new_tween() -> Tween:
	if _tween:
		_tween.kill()
	_tween = create_tween()
	return _tween


## Frames the screen stays black after a switch behind the fade (0.8.2.2, phone: going back to
## the day hitched and the hitch showed). The first frames of a scene (a slow frame, materials
## drawn for the first time, a rebuilt mesh) used to advance the fade by their own long time, so
## the black lifted during them; now the fade waits these frames out, however long they take.
const BLACK_FRAMES := 3


## Called from a tween's callback right after the switch: pauses that tween for BLACK_FRAMES,
## after `work` (a coroutine: the album photo, the live picture) has finished behind the black.
func _hold_black(tw: Tween, work: Callable = Callable()) -> void:
	tw.pause()
	if work.is_valid():
		await work.call()
	for _i in range(BLACK_FRAMES):
		await get_tree().process_frame
	if is_instance_valid(tw) and tw.is_valid() and tw == _tween:
		tw.play()


## After the dawn burst (GameState wrote the diary line): the first-morning page or the daily wish.
func _morning() -> void:
	# (0.8.2.2: the album's morning photo and the live picture are taken behind the sunrise's
	# black now, _rise; here they froze the morning and showed the album camera's view.)
	# Visitors come as the tree grows (diary lines; the nest and the bench stay in view).
	if not Visitors.arrive(state).is_empty():
		tree_view.update_visitors()
	tree_view.show_wish("")
	if state.day_number() == 1:
		_page_once("sapling")
	elif state.diary.wish != "":
		# The day's wish is a small scrap for the morning hours, not a page that stops play
		# (0.6 review); it stays in the diary too.
		tree_view.show_wish(state.diary.wish)
		# 0.8.2.5: the compass needle points to the wish place; one line the first time.
		if state.diary.wish_patch >= 0:
			_page_once("compass")


# --- settings, saving, dev keys -----------------------------------------------

## Renders the live picture's layers for the phone (only there; tools turn it on themselves). It
## draws in a world of its own, so the game's view is not touched; skipped if the tree is unchanged.
func _refresh_live(force: bool = false) -> void:
	if not LiveExport.enabled or ephemeral or state == null:
		return
	live_export.refresh(tree_view, state, force)


## The live picture asked for at sunset (the day's growth), drawn behind the next black.
var _live_due: bool = false


## Behind a black screen: the live picture if one is due (it renders four layers, a long frame or
## several on the phone). A coroutine.
func _live_behind_black() -> void:
	if not _live_due or not LiveExport.enabled or ephemeral or state == null:
		return
	_live_due = false
	await live_export.refresh(tree_view, state)


## Behind the sunrise's black (0.8.2.2): the album's morning photo, off screen, and the live
## picture with the night's growth. A coroutine.
func _morning_behind_black() -> void:
	await _take_photo("morning", true)
	_live_due = true
	await _live_behind_black()


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
		"any_species":
			# The test switch also opens the bonsai on the windowsill.
			if on and state != null and state.ensure_bonsai(true) != null:
				shed.bonsai_ready = true
				bonsai_view.refresh(true)
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
		# One level at a time: a page opened from the book closes back to the book.
		if journal.current_page() != "" and journal.is_book_open():
			journal.close_page()
		else:
			journal.close_page()
			journal.close_diary()
	elif in_bonsai:
		if bonsai_hud.is_busy():
			bonsai_hud.close_sheet()
		elif bonsai_view.tool != "":
			bonsai_view.set_tool("")
		else:
			leave_bonsai()
	elif shed_menu.is_busy():
		shed_menu.close_boards()
	elif in_shed and shed.drawer_look() != "":
		shed.toggle_drawer(shed.drawer_look())
	elif _underground and root_view.far_view:
		root_view.leave_far_view()
	elif not in_shed and not _transitioning:
		enter_shed(true)
	elif in_shed and not _transitioning and Budgets.PHONE:
		save()
		get_tree().quit()


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_GO_BACK_REQUEST:
		_back()
		return
	# A press whose release may never arrive ends here (0.8.2.1, bug 8): alt-tab with the button
	# held, the app sent to the background, the window losing focus.
	if what in [NOTIFICATION_APPLICATION_FOCUS_OUT, NOTIFICATION_WM_WINDOW_FOCUS_OUT, NOTIFICATION_APPLICATION_PAUSED, NOTIFICATION_WM_CLOSE_REQUEST]:
		_cancel_presses()
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


## The journal and the pictures at the right, shown together (0.8.2.2: out of the shed the
## shed, camera and shears came a moment after the journal); also called at each switch.
func _update_corner() -> void:
	var page_up := shed_menu.is_tree_page_open()
	journal.set_button_visible(not in_shed and not page_up and not _diving)
	_shed_button.visible = not in_shed and not _diving and not page_up
	_photo_button.visible = not in_shed and not _underground and not _diving and not page_up and state.phase == GameState.Phase.DAY
	_shears_button.visible = _photo_button.visible and state.sim.graph.size() > 6
	if tree_view.prune_mode and not _shears_button.visible:
		_set_shears(false)
	# Only while some of the day is left (not at dusk, never at night); lit while it runs.
	var running := tree_view.running_to_sunset()
	_sunset_button.visible = _photo_button.visible and (running or tree_view.can_run_to_sunset())
	_sunset_button.modulate = Color(1.3, 1.12, 0.85) if running else Color.WHITE


## Lets go of every finger and button the views hold (a hold's fast-forward, the stick, dive).
func _cancel_presses() -> void:
	if tree_view != null:
		tree_view.cancel_press()
	if root_view != null and root_view.joystick != null:
		root_view.release_controls()


# --- the garden shed -----------------------------------------------------------------

## The pictures at the middle right under the journal: the shed (back there, a pause), the
## old camera for the album and the pruning shears.
func _build_corner() -> void:
	_corner = CanvasLayer.new()
	_corner.layer = 19
	add_child(_corner)
	_shed_button = _picture("shed", -150.0, -1.5)
	_shed_button.pressed.connect(func() -> void:
		if not _transitioning and not journal.is_open():
			enter_shed(true))
	_photo_button = _picture("camera", -48.0, 1.0)
	_photo_button.pressed.connect(func() -> void:
		if not _transitioning:
			_take_photo("camera"))
	# The shears: while out, a tap cuts a branch instead of boosting the sun (play test 4).
	_shears_button = _picture("shears", 54.0, -1.0)
	_shears_button.pressed.connect(func() -> void:
		if _transitioning:
			return
		_set_shears(not tree_view.prune_mode)
		if tree_view.prune_mode:
			_page_once("shears"))
	# The sunset: the rest of the day runs at the fast-forward's speed; a tap anywhere stops it.
	_sunset_button = _picture("sunset", 156.0, 1.5)
	_sunset_button.pressed.connect(func() -> void:
		if _transitioning:
			return
		toggle_run_to_sunset())
	# A warm glow behind the shears while they are out.
	_shears_glow = TextureRect.new()
	var g := GradientTexture2D.new()
	g.fill = GradientTexture2D.FILL_RADIAL
	g.fill_from = Vector2(0.5, 0.5)
	g.fill_to = Vector2(0.5, 0.0)
	g.gradient = Gradient.new()
	g.gradient.set_color(0, Color(1.0, 0.88, 0.5, 1.0))
	g.gradient.set_offset(1, 0.95)
	g.gradient.set_color(1, Color(1.0, 0.75, 0.3, 0.0))
	_shears_glow.texture = g
	_shears_glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	_shears_glow.offset_left = 10
	_shears_glow.offset_right = -10
	_shears_glow.offset_top = -18
	_shears_glow.offset_bottom = 18
	_shears_glow.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_shears_glow.show_behind_parent = true
	_shears_glow.visible = false
	_shears_button.add_child(_shears_glow)
	_flash = ColorRect.new()
	_flash.color = Color(1, 1, 1, 0)
	_flash.set_anchors_preset(Control.PRESET_FULL_RECT)
	_flash.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_corner.add_child(_flash)


## The sunset picture's tap: starts the run to the sunset, or stops one that runs.
func toggle_run_to_sunset() -> void:
	if tree_view.running_to_sunset():
		tree_view.run_to_sunset(false)
		return
	if tree_view.prune_mode:
		_set_shears(false)
	tree_view.run_to_sunset(true)


func _set_shears(on: bool) -> void:
	if on:
		tree_view.run_to_sunset(false)
	tree_view.prune_mode = on
	if not on:
		tree_view.pruning.preview(-1)
	# The shears glow while out, and on a PC the pointer becomes a small pair of secateurs.
	_shears_button.modulate = Color(1.3, 1.18, 0.85) if on else Color.WHITE
	_shears_glow.visible = on
	if _glow_tween != null:
		_glow_tween.kill()
		_glow_tween = null
	if on:
		_glow_tween = create_tween().set_loops()
		_glow_tween.tween_property(_shears_glow, "modulate:a", 0.45, 0.8).from(1.0)
		_glow_tween.tween_property(_shears_glow, "modulate:a", 1.0, 0.8)
	Input.set_default_cursor_shape(Input.CURSOR_ARROW)
	if on:
		Input.set_custom_mouse_cursor(SHEARS_CURSOR, Input.CURSOR_ARROW, shears_hotspot())
	else:
		Input.set_custom_mouse_cursor(null, Input.CURSOR_ARROW)


## The pointer's hot spot: the point of the blade (the opaque pixel nearest the top left corner).
static func shears_hotspot() -> Vector2:
	var img := SHEARS_CURSOR.get_image()
	var best := Vector2.ZERO
	var best_d := INF
	for y in range(img.get_height()):
		for x in range(img.get_width()):
			if img.get_pixel(x, y).a > 0.6 and x + y < best_d:
				best_d = x + y
				best = Vector2(x, y)
	return best


func _picture(icon: String, top: float, tilt: float) -> TextureButton:
	var b := Paper.picture_button(load("res://ui/icons/%s.png" % icon), top, tilt)
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
		# A finished tree's month plays as a flip-book in the album.
		shed_menu.tree_finished = state.finished
		state.ensure_bonsai(bool(journal.settings.get("any_species", false)))
		shed.bonsai_ready = state.bonsai != null
		# Only rebuilt when the bonsai changed (0.8.2.1: a full rebuild on every visit was the
		# 80 ms PC / 108-138 ms phone frame when the house icon was tapped).
		bonsai_view.refresh(false)
		# The garden's finds lie in the bench's drawers (0.8.2.6).
		shed.fill_drawers(state.finds.list())
		shed_menu.show_menu(true)
		ambience.set_world(true, 0.8)
	if animate:
		_transitioning = true
		var tw := _new_tween()
		tw.tween_property(_fade, "color:a", 1.0, 0.35)
		tw.tween_callback(func() -> void:
			switch.call()
			_hold_black(tw, _live_behind_black))
		# The room's first frames (a slow frame on the phone, materials drawn the first time)
		# stay under the black, so the shed never flashes up blank (0.8.2.1 phone test; 0.8.2.2:
		# counted in frames too, _hold_black).
		tw.tween_interval(0.12)
		tw.tween_property(_fade, "color:a", 0.0, 0.45)
		tw.tween_callback(func() -> void: _transitioning = false)
	else:
		switch.call()
	save()


## Out of the shed and back into the day (or the night, if the game was left underground).
func leave_shed() -> void:
	if not in_shed or _transitioning or in_bonsai:
		return
	_transitioning = true
	shed_menu.show_menu(false)
	shed.hide_note()
	var tw := _new_tween()
	tw.tween_property(_fade, "color:a", 1.0, 0.35)
	tw.tween_callback(func() -> void:
		shed.close_drawers()
		in_shed = false
		shed.visible = false
		tree_view.set_shed_open(false)
		tree_view.dive_amount = 0.0
		journal.set_button_visible(true)
		# Pages that were waiting (the first tutorial page) show now, in the game.
		journal.release_pages()
		_show_underground(state.phase == GameState.Phase.NIGHT)
		_update_corner()
		# The tree's camera stands where play left it from the first frame (0.8.2.2).
		tree_view.snap_camera()
		_hold_black(tw))
	tw.tween_property(_fade, "color:a", 0.0, 0.45)
	tw.tween_callback(func() -> void: _transitioning = false)


func _unhandled_input(event: InputEvent) -> void:
	if not in_shed or in_bonsai or _transitioning or journal.is_open() or shed_menu.is_busy() or _shed_tapped != "":
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
	# 0.8.2.6: leaning over an open drawer, a tap on a find shows its note; any other tap closes
	# the note, then the drawer (the view straightens up).
	if shed.drawer_look() != "":
		var fi := shed.find_at(m.position)
		if fi >= 0:
			shed.show_note(fi)
		elif shed.note_text() != "":
			shed.hide_note()
		else:
			shed.toggle_drawer(shed.drawer_look())
		return
	var item := shed.item_at(m.position)
	if item == "":
		# The bench's drawers slide out and back (0.8.2.4); nothing opens a page.
		var drawer := shed.drawer_at(m.position)
		if drawer != "":
			shed.toggle_drawer(drawer)
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


## What a thing in the shed opens: the journal the diary (its first page the tree's own), the
## album the photos, the seed bag its page (and the next seed), the pinboard the options;
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
		"gloves", "door":
			leave_shed()
		"bonsai":
			enter_bonsai()


## The labels beside the things: shown until a thing was used once, and while hovered.
func _update_shed_tags() -> void:
	var at := {}
	var below := {}
	var shown := {}
	var free := not (_transitioning or journal.is_open() or shed_menu.is_busy() or shed.drawer_look() != "")
	for item in Shed.ITEMS:
		at[item] = shed.item_tag_position(item)
		below[item] = shed.item_tag_below(item)
		shown[item] = free and (_shed_hover == item or not state.seen_pages.has("shed_used_" + item))
		if item == "bonsai" and not shed.bonsai_ready:
			shown[item] = false
	shed_menu.place_tags(at, below, shown)


# --- bonsai mode ------------------------------------------------------------------------

## Tapping the bonsai on the sill: the camera glides close to the pot and the tools come out.
func enter_bonsai() -> void:
	if in_bonsai or _transitioning or state == null or state.bonsai == null:
		return
	in_bonsai = true
	shed_menu.show_menu(false)
	bonsai_view.enter(shed.camera)
	# 0.8.2.4 (phone: the paper scraps, one still blank, came before the camera): the HUD comes
	# when the camera has arrived, with its words already written.
	_transitioning = true
	while in_bonsai and not bonsai_view.arrived():
		await get_tree().process_frame
	_transitioning = false
	if not in_bonsai:
		return
	bonsai_hud.show_hud(true)
	_page_once("bonsai")
	if state.bonsai.repot_due:
		_page_once("bonsai_repot")
	if _bonsai_photo_due:
		await get_tree().create_timer(1.2).timeout
		_bonsai_photo()


## Back to the workbench.
func leave_bonsai() -> void:
	if not in_bonsai or _transitioning:
		return
	if bonsai_view.busy:
		return
	if bonsai_view.is_lifted():
		# The tree is out of its pot: back puts it back as it was (0.7 review), a second back leaves.
		bonsai_view.repot_cancel()
		return
	_transitioning = true
	bonsai_hud.show_hud(false)
	bonsai_view.leave(shed.camera, func() -> void:
		in_bonsai = false
		shed_menu.show_menu(true)
		_transitioning = false)
	save()


func _on_bonsai_tool(kind: String) -> void:
	match kind:
		"water":
			_page_once("bonsai_water")
		"fertiliser":
			_page_once("bonsai_fertiliser")
		"burn":
			_page_once("bonsai_fertiliser")
			_page_once("bonsai_burn")
		"wire":
			_page_once("bonsai_wire")
		"repot":
			save()


func _bonsai_event(e: String) -> void:
	match e:
		"milestone":
			# The bonsai's album grows by milestones: a photo when it happens in view, else next visit.
			if in_bonsai:
				_bonsai_photo()
			else:
				_bonsai_photo_due = true
		"repot_due":
			if in_bonsai:
				_page_once("bonsai_repot")


func _bonsai_photo() -> void:
	_bonsai_photo_due = false
	if ephemeral or not in_bonsai or _photo_busy:
		return
	_photo_busy = true
	while bonsai_view.busy or _transitioning:
		await get_tree().process_frame
	# 0.8.2.4: drawn off screen and written on a worker thread (the phone froze 0.95 s here and
	# then showed one frame upside down, without the paper scraps, while the screen was read).
	var shot: Image = await Photos.shoot(bonsai_view.camera, Photos.screen_size(self))
	if in_bonsai:
		Photos.save_image(shot, state.bonsai.day(), "bonsai", state.bonsai.species.id)
	_photo_busy = false


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
## The pinboard's resets. A new tree keeps the album, the grove, the unlocked species and the
## bonsai; starting over wipes the save and the album (the settings stay).
func _reset(kind: String) -> void:
	if state == null or _transitioning:
		return
	var now := int(Time.get_unix_time_from_system())
	if kind == "all":
		Photos.clear()
		start(GameState.new_game(now))
	else:
		start(GameState.new_tree(now, state.sim.species.id, state))
	save()
	in_shed = false
	enter_shed(false)


## 0.8 "load a copy", tapped twice: the copy (checked already) replaces the game and the album.
## A copy that fails now (a full phone) leaves the game as it was, with a note.
func _load_copy(path: String, _manifest: Dictionary) -> void:
	if ephemeral or state == null or _transitioning:
		return
	var notes := shed_menu.backup_notes
	# The game as it is now is kept first (save and album), so a wrong copy can be taken back.
	save()
	var got := Backup.load_with_safety(path, state)
	if not got["ok"]:
		notes.set_note(str(Backup.NOTES.get(got["why"], Backup.NOTES["broken"])))
		return
	var loaded := SaveData.load_game(SaveData.GAME_PATH, float(got["saved_at_unix"]))
	if loaded == null:
		notes.set_note(Backup.NOTES["broken"])
		return
	if path.get_file() == "picked.zip":
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	loaded.diary.add(loaded.day_number(), Backup.diary_line(got["manifest"]), "tree", "book", "milestone")
	start(loaded)
	save()
	in_shed = false
	enter_shed(false)
	notes.set_note("My tree is back, as it was when the copy was made.")
	notes.offer_undo()


## The save could not be read at start (it was kept aside as .broken): the newest good sunrise
## save is offered on a torn page. Without one the new game simply begins, as before.
func _offer_morning() -> void:
	var path := Backup.newest_good_morning()
	var info := Backup.peek(path) if path != "" else {}
	if info.is_empty():
		return
	var offer := MorningOffer.new()
	shed_menu.add_child(offer)
	offer.answered.connect(func(take: bool) -> void:
		offer.queue_free()
		if not take or Backup.restore_morning(path) != OK:
			return
		var back := SaveData.load_game()
		if back == null:
			return
		start(back)
		save()
		in_shed = false
		enter_shed(false))
	offer.offer(Backup.morning_time(path), str(info["tree"]), int(info["day"]))


func plant_next(species_id: String) -> void:
	if state == null or not state.can_plant_next(bool(journal.settings.get("any_species", false))):
		return
	if not state.unlocked_species(bool(journal.settings.get("any_species", false))).has(species_id):
		return
	start(GameState.new_tree(int(Time.get_unix_time_from_system()), species_id, state))
	in_shed = false
	enter_shed(false)


## Saves a photo of the tree for the album (without the HUD), with a camera flash.
func _take_photo(tag: String, behind_black: bool = false) -> void:
	if ephemeral or in_shed or _underground or _photo_busy:
		return
	_photo_busy = true
	# Never through the black fade of a sunrise or a trip to the shed (the morning photo is taken
	# behind the sunrise's black on purpose, off screen).
	while _transitioning and not behind_black:
		await get_tree().process_frame
	if in_shed or _underground:
		_photo_busy = false
		return
	# The morning photos share one camera framed for the grown tree, so the album's flip-book
	# shows the tree growing instead of a tree re-framed to the same size every day.
	# 0.8.2.2 (phone: a frame from the wrong camera and a hitch on the way back to the day): the
	# album's camera draws off screen (TreeView.album_photo), so the screen never shows its pose,
	# and the photo is written on a worker thread (Photos.save_image).
	if tag == "morning":
		var shot: Image = await tree_view.album_photo()
		if not in_shed and not _underground:
			Photos.save_image(shot, state.day_number(), tag, state.sim.species.id)
		_photo_busy = false
		return
	# 0.8.2.4: off screen too, by a copy of the player's camera (no screen read-back: that showed
	# a flipped frame on the phone's GL renderer, and the HUD blinked out for the photo).
	var img: Image = await Photos.shoot(tree_view.camera, Photos.screen_size(self))
	Photos.save_image(img, state.day_number(), tag, state.sim.species.id)
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
