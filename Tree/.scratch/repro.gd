extends SceneTree
var main: Node
const OUT := "C:/Users/Home/AppData/Local/Temp/claude/C--Users-Home-Documents-GameDev-neues-spiel-mobil/668d54f6-ad5a-4d45-9e9d-282a1b9dbd37/scratchpad/repro"

func _initialize() -> void:
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()

func _dump(tag: String, dir: String) -> void:
	var meta := LivePicture.read_meta(dir)
	print(tag, " meta: ", JSON.stringify(meta.get("layers", {})))
	for n in meta.get("layers", {}):
		var p: String = dir.path_join(meta["layers"][n])
		var img := Image.load_from_file(p)
		var op := 0
		for y in range(0, img.get_height(), 4):
			for x in range(0, img.get_width(), 4):
				if img.get_pixel(x, y).a > 0.5: op += 1
		print("  ", n, " exists=", FileAccess.file_exists(p), " opaque samples=", op, " top=", LiveExport.measure(img))

func _run() -> void:
	for i in range(30):
		await process_frame
	main.start(load("res://tools/live_shot.gd").grow(42, "linden", 12))
	for i in range(30):
		await process_frame
	print("phase ", main.state.phase, " nodes ", main.state.sim.graph.size())
	var ex: LiveExport = main.live_export
	ex.dir = OUT + "/day"
	print("day ok: ", await ex.refresh(main.tree_view, main.state, true))
	_dump("day", ex.dir)
	main._show_underground(true)
	for i in range(3):
		await process_frame
	ex.dir = OUT + "/under"
	print("under ok: ", await ex.refresh(main.tree_view, main.state, true))
	_dump("under", ex.dir)
	quit(0)
