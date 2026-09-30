class_name BackupNotes
extends VBoxContainer
## 0.8 save backup on the options pinboard: two notes, "copy my tree" and "load a copy", and a
## small paper strip below them for what happened. Calm and only on request: nothing here ever
## reminds, and the automatic sunrise saves (Backup.keep_morning) never show here.
## On the phone the notes use Android's own pickers (PhoneFiles); on a PC a copy goes into the
## game's folder (Backup.COPIES_DIR) and "load a copy" opens the system's file dialog there
## (or takes the newest copy there when there is no dialog).
## Loading asks "sure? tap again" like the reset notes, since it replaces the current game.

## Tapped a second time: load the checked copy at `path` in place of the current game (main does).
signal load_confirmed(path: String, manifest: Dictionary)

## How long "sure? tap again" waits (the resets wait 4 s; after a picker a little longer).
const ARM_SECONDS := 8.0

## Main sets this: saves the game and returns the state to copy.
var fetch_state: Callable
var copy_button: Button
var load_button: Button
var _note_strip: PanelContainer
var _note: Label
## "take back the game before": the game kept before the last loaded copy (Backup.BEFORE_LOAD).
var undo_button: Button
var _busy := false
var _armed_path := ""
var _armed_manifest: Dictionary = {}
var _arm_serial := 0


func _init() -> void:
	add_theme_constant_override("separation", 10)
	alignment = BoxContainer.ALIGNMENT_CENTER
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	# The note: a slip of paper above the two notes, only there when there is something to say
	# (the box grows upward, so the notes themselves never move).
	_note_strip = PanelContainer.new()
	_note_strip.add_theme_stylebox_override("panel", Paper.paper_box(300, 60, 172, "all", 9.0))
	_note_strip.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_note_strip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_note_strip.rotation_degrees = -0.6
	add_child(_note_strip)
	_note = Paper.ink_label("", 22, Paper.INK)
	_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_note.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_note.custom_minimum_size = Vector2(540, 0)
	var slip := VBoxContainer.new()
	slip.add_theme_constant_override("separation", 0)
	slip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_note_strip.add_child(slip)
	slip.add_child(_note)
	undo_button = Paper.ink_button("take back the game before", 22, 46.0)
	undo_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	undo_button.pressed.connect(_on_undo)
	undo_button.visible = false
	slip.add_child(undo_button)
	_note_strip.visible = false
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 30)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(row)
	copy_button = Paper.scrap_button("copy my tree", 27, 170)
	copy_button.rotation_degrees = 1.0
	copy_button.pressed.connect(_on_copy)
	row.add_child(copy_button)
	load_button = Paper.scrap_button("load a copy", 27, 171)
	load_button.rotation_degrees = -1.4
	load_button.pressed.connect(_on_load)
	row.add_child(load_button)


## A handwritten line under the notes ("" hides the slip).
func set_note(text: String) -> void:
	_note.text = text
	_note.visible = text != ""
	undo_button.visible = false
	_note_strip.visible = text != ""


## After a load (and on the board for a day after it): the game before can be taken back.
func offer_undo() -> void:
	if not Backup.can_undo_load():
		return
	undo_button.visible = true
	_note_strip.visible = true


func _on_undo() -> void:
	if _busy:
		return
	check(Backup.before_load_path(), "Before the load: ")


func note_text() -> String:
	return _note.text


## The board was closed or opened: nothing stays armed, old notes go.
func reset() -> void:
	_disarm()
	if not _busy:
		set_note("")
		offer_undo()


# --- copy my tree -------------------------------------------------------------------------

func _on_copy() -> void:
	if _busy or not fetch_state.is_valid():
		return
	_disarm()
	var state: GameState = fetch_state.call()
	if state == null:
		return
	_busy = true
	set_note("copying...")
	# Let the note show before the photos are packed.
	await get_tree().process_frame
	await get_tree().process_frame
	var name := Backup.file_name(state)
	var out := Backup.COPIES_DIR.path_join(name)
	var err := Backup.make(state, out)
	if err != OK:
		_busy = false
		set_note("There is not enough room on the phone for the copy." if err in [ERR_FILE_CANT_WRITE, ERR_OUT_OF_MEMORY, ERR_FILE_CANT_OPEN] else "The copy could not be made.")
		return
	if PhoneFiles.can_pick_documents():
		set_note("")
		if PhoneFiles.save_document(out, name, _on_saved.bind(out)):
			return
		DirAccess.remove_absolute(ProjectSettings.globalize_path(out))
		_busy = false
		set_note("The phone's \"save as\" did not open.")
		return
	_busy = false
	# A PC: the copy is in the game's folder, and the folder opens (as after "send").
	set_note("A copy of my tree is in the game's folder.")
	print("copy of the tree: ", ProjectSettings.globalize_path(out))
	if ShedMenu.open_folders and DisplayServer.get_name() != "headless":
		OS.shell_open(ProjectSettings.globalize_path(Backup.COPIES_DIR))


## The phone's answer to "save as". The zip made for it is removed either way.
func _on_saved(result: String, made: String) -> void:
	DirAccess.remove_absolute(ProjectSettings.globalize_path(made))
	_busy = false
	match result:
		"ok":
			set_note("A copy of my tree is saved.")
		"cancelled":
			set_note("")
		"no_room":
			set_note("There is not enough room for the copy there.")
		_:
			set_note("The copy could not be saved there.")


# --- load a copy --------------------------------------------------------------------------

func _on_load() -> void:
	if _armed_path != "":
		var path := _armed_path
		var manifest := _armed_manifest
		_disarm()
		load_confirmed.emit(path, manifest)
		return
	if _busy:
		return
	var picked := Backup.COPIES_DIR.path_join("picked.zip")
	if PhoneFiles.can_pick_documents():
		DirAccess.make_dir_recursive_absolute(Backup.COPIES_DIR)
		set_note("")
		_busy = PhoneFiles.open_document(picked, _on_picked.bind(picked))
		if not _busy:
			set_note("The phone's file picker did not open.")
		return
	# A PC: the system's file dialog in the game's folder, or the newest copy there.
	if DisplayServer.has_feature(DisplayServer.FEATURE_NATIVE_DIALOG_FILE) and DisplayServer.get_name() != "headless":
		DirAccess.make_dir_recursive_absolute(Backup.COPIES_DIR)
		_busy = true
		DisplayServer.file_dialog_show("Load a copy of my tree", ProjectSettings.globalize_path(Backup.COPIES_DIR), "", false,
			DisplayServer.FILE_DIALOG_MODE_OPEN_FILE, PackedStringArray(["*.zip"]), _on_dialog)
		return
	var newest := Backup.newest_copy()
	if newest == "":
		set_note("There is no copy in the game's folder yet.")
		return
	check(newest)


func _on_dialog(status: bool, paths: PackedStringArray, _filter: int) -> void:
	_busy = false
	if status and not paths.is_empty():
		check(paths[0])


## The phone's answer to "open": the file was copied to `picked`.
func _on_picked(result: String, picked: String) -> void:
	_busy = false
	match result:
		"ok":
			check(picked)
		"cancelled":
			set_note("")
		"too_big":
			set_note(Backup.NOTES["foreign"])
		"no_room":
			set_note(Backup.NOTES["no_room"])
		_:
			set_note("That file could not be read. My tree stays as it is.")


## Reads the copy (nothing is changed yet). A good one arms the note: "sure? tap again".
func check(path: String, lead: String = "") -> void:
	var got := Backup.read(path)
	if not got["ok"]:
		set_note(str(Backup.NOTES.get(got["why"], Backup.NOTES["broken"])))
		return
	_armed_path = path
	_armed_manifest = got["manifest"]
	load_button.text = "sure? tap again"
	var what := Backup.describe(_armed_manifest)
	if lead != "":
		what = lead + what.left(1).to_lower() + what.substr(1)
	set_note(what + " It replaces this game.")
	_arm_serial += 1
	var serial := _arm_serial
	get_tree().create_timer(ARM_SECONDS).timeout.connect(func() -> void:
		if is_instance_valid(self) and serial == _arm_serial and _armed_path != "":
			_disarm()
			set_note("")
			offer_undo())


func is_armed() -> bool:
	return _armed_path != ""


func _disarm() -> void:
	_arm_serial += 1
	_armed_path = ""
	_armed_manifest = {}
	load_button.text = "load a copy"
