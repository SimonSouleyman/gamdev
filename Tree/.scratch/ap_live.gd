extends "res://tools/autoplay.gd"
const OUT := "C:/Users/Home/AppData/Local/Temp/claude/C--Users-Home-Documents-GameDev-neues-spiel-mobil/668d54f6-ad5a-4d45-9e9d-282a1b9dbd37/scratchpad/aplive"
var _last := ""
func _initialize() -> void:
	LiveExport.enabled = true
	LivePicture.DIR = OUT + "/live_picture"
	super._initialize()
	main.live_export.dir = LivePicture.DIR

func _process(delta: float) -> bool:
	var m := LivePicture.read_meta(LivePicture.DIR)
	var s := JSON.stringify(m.get("layers", {}))
	if s != _last:
		_last = s
		var info := ""
		for n in m.get("layers", {}):
			var p: String = LivePicture.DIR.path_join(m["layers"][n])
			var img := Image.load_from_file(p) if FileAccess.file_exists(p) else null
			var op := 0
			if img:
				for y in range(0, img.get_height(), 4):
					for x in range(0, img.get_width(), 4):
						if img.get_pixel(x, y).a > 0.5: op += 1
			info += " %s:%s/%d" % [n, img != null, op]
		_log("LIVE meta changed: day %s busy %s %s" % [m.get("day", "-"), main.live_export.busy, info])
	return super._process(delta)
