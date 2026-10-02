extends SceneTree
func _initialize() -> void:
	_run.call_deferred()
func _run() -> void:
	var game: GameState = load("res://tools/live_shot.gd").grow(42, "linden", 12)
	var view := TreeView.new()
	root.add_child(view)
	await process_frame
	view.setup(game)
	var ex := LiveExport.new()
	root.add_child(ex)
	ex.dir = "C:/Users/Home/AppData/Local/Temp/claude/C--Users-Home-Documents-GameDev-neues-spiel-mobil/668d54f6-ad5a-4d45-9e9d-282a1b9dbd37/scratchpad/notree"
	for i in range(4):
		await process_frame
	view._tree_mesh.mesh = null
	view._leaves.multimesh.visible_instance_count = 0
	var ok := await ex.refresh(view, game, true)
	print("ok=", ok, " err=", ex.last_error, " meta=", FileAccess.file_exists(ex.dir + "/live.json"))
	quit(0)
