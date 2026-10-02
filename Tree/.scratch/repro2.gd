extends SceneTree
var main: Node
const OUT := "C:/Users/Home/AppData/Local/Temp/claude/C--Users-Home-Documents-GameDev-neues-spiel-mobil/668d54f6-ad5a-4d45-9e9d-282a1b9dbd37/scratchpad/repro2"

func _initialize() -> void:
	LiveExport.enabled = true
	LivePicture.DIR = OUT + "/a"
	main = load("res://main.tscn").instantiate()
	main.ephemeral = true
	root.add_child(main)
	_run.call_deferred()

func _dump(tag: String, dir: String) -> void:
	var meta := LivePicture.read_meta(dir)
	var s := tag + ":"
	for n in meta.get("layers", {}):
		var img := Image.load_from_file(dir.path_join(meta["layers"][n]))
		var op := 0
		var above := 0
		var hy := int(float(meta["horizon_y"]) * img.get_height()) - 4
		for y in range(0, img.get_height(), 4):
			for x in range(0, img.get_width(), 4):
				if img.get_pixel(x, y).a > 0.5:
					op += 1
					if y < hy: above += 1
		s += " %s=%d/%d" % [n, op, above]
	print(s)

func _run() -> void:
	for i in range(10):
		await process_frame
	main.start(load("res://tools/live_shot.gd").grow(42, "linden", 12))
	main.enter_shed(false)
	for i in range(60):
		await process_frame
	while main.live_export.busy:
		await process_frame
	_dump("load+shed", OUT + "/a")
	main.live_export.dir = OUT + "/b"
	root.get_viewport().disable_3d = true
	await main.live_export.refresh(main.tree_view, main.state, true)
	root.get_viewport().disable_3d = false
	_dump("disable_3d", OUT + "/b")
	quit(0)
