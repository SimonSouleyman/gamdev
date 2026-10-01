extends SceneTree
## Parity check of the live picture's maths: prints LivePicture's results for fixed inputs into a
## file, for comparison with the phone's copy (LiveScene.java) run on a PC JVM by
## android_plugin/treelive/parity/Parity.java. The two outputs must be identical:
##   godot --headless --path . -s tools/live_parity.gd -- gd.txt
##   javac -d out android_plugin/treelive/src/main/java/de/simonsouleyman/treelive/LiveScene.java
##     android_plugin/treelive/parity/de/simonsouleyman/treelive/Parity.java
##   java -cp out de.simonsouleyman.treelive.Parity > java.txt ; diff --strip-trailing-cr gd.txt java.txt
func _initialize() -> void:
	var out := ""
	for d in [1, 80, 172, 273, 355]:
		for h in [0.5, 5.2, 7.4, 9.0, 12.5, 17.8, 19.1, 20.3, 23.9]:
			var m := LivePicture.moment(d, h, 2.0 if d > 88 and d < 300 else 1.0, 1790000000 + d * 86400)
			var mix: Dictionary = m["mix"]
			out += "%d %.1f %.6f %.6f %.6f %.6f %.6f %.6f %.6f %.6f\n" % [d, h, mix["dawn"], mix["day"], mix["dusk"], mix["night"], m["sky"][0].r, m["sky"][1].g, m["moon"], m["stars"]]
	for i in range(20):
		var u := i / 19.0
		var v := (i * 7 % 20) / 19.0
		var o := LivePicture.wind_offset(u, v, i * 1.37, 0.9, 0.3)
		out += "w %.6f %.6f\n" % [o.x, o.y]
	for i in range(6):
		var c := LivePicture.cloud_position(i, i * 97.3)
		out += "c %.6f %.6f
" % [c.x, c.y]
	for i in range(5):
		var s := LivePicture.star(i)
		out += "s %.6f %.6f %.6f\n" % [s.x, s.y, s.z]
	var r := LivePicture.layout(Vector2(1080, 2400), Vector2(540, 960), 0.1, 0.9)
	out += "l %.3f %.3f %.3f %.3f\n" % [r.position.x, r.position.y, r.size.x, r.size.y]
	var f := FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
	f.store_string(out)
	quit()
