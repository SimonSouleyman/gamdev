extends SceneTree
## Before/after in the real game: runs main.tscn, photographs the first journal page, the HUD
## and the open journal; with --after it first swaps the paper for PaperLook at runtime (the
## calls that adopting it in ui/ would make, see paper_look.gd), without touching ui/.
## Run: godot --path . --rendering-driver vulkan --resolution 720x1280 -s lookdev/paper/game_shot.gd -- --shots=<folder> [--after]

var main: Node
var t := 0.0
var step := 0
var shots_dir := "user://paper_shots"
var after := false


func _initialize() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shots="):
			shots_dir = a.substr(8)
		elif a == "--after":
			after = true
	DirAccess.make_dir_recursive_absolute(shots_dir)
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)


func _shot(n: String) -> void:
	RenderingServer.force_draw(false)
	var prefix := "game_after_" if after else "game_before_"
	root.get_viewport().get_texture().get_image().save_png(shots_dir.path_join(prefix + n + ".png"))


func _process(d: float) -> bool:
	if step == 0:
		main.start(GameState.new_game(42))
		if after:
			_adopt()
		step = 1
		return false
	t += d
	if step == 1 and t > 1.5:
		_shot("page")
		main.journal.close_page()
		step = 2
	elif step == 2 and t > 2.5:
		_shot("hud")
		main.journal.open_diary()
		step = 3
	elif step == 3 and t > 3.5:
		_shot("diary")
		quit()
	return false


## What adopting PaperLook in ui/ amounts to, done from outside for the screenshot.
func _adopt() -> void:
	var j: Journal = main.journal
	PaperLook.apply(j._page_sheet, "torn_page", 40, 34.0)
	PaperLook.apply(j._book_page, "book_page", 11, 34.0)
	for c in j._book.get_children():
		if c is Panel and (c as Panel).get_theme_stylebox("panel") is StyleBoxTexture:
			PaperLook.apply_leather(c)
			break
	PaperLook.ink_all(j)
	var hud: Node = main.tree_view.hud
	var i := 0
	for c in _all(hud):
		if c is PaperNote:
			PaperLook.apply(c, "strip", 61, 18.0)
		elif c is PanelContainer:
			i += 1
			PaperLook.apply(c, "scrap", 20 + i, 10.0)
	PaperLook.ink_all(hud)


func _all(n: Node) -> Array[Node]:
	var out: Array[Node] = []
	for c in n.get_children():
		out.append(c)
		out.append_array(_all(c))
	return out
