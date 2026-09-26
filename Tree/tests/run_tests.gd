extends SceneTree
## Minimal headless test runner (no addon needed).
## Run: godot --headless -s tests/run_tests.gd   (exit code 0 = all passed)

var _failures: int = 0
var _passes: int = 0
var _current: String = ""


func _init() -> void:
	var tests := [
		preload("res://tests/test_plant_graph.gd"),
		preload("res://tests/test_space_colonization.gd"),
		preload("res://tests/test_resources.gd"),
		preload("res://tests/test_day_cycle.gd"),
		preload("res://tests/test_growth_sim.gd"),
		preload("res://tests/test_save_data.gd"),
		preload("res://tests/test_tree_mesh_builder.gd"),
	]
	for script in tests:
		var suite: RefCounted = script.new()
		suite.set("t", self)
		for m in suite.get_method_list():
			var name: String = m["name"]
			if name.begins_with("test_"):
				_current = "%s.%s" % [script.resource_path.get_file(), name]
				print("RUN ", _current)
				suite.call(name)
	print("\n%d passed, %d failed" % [_passes, _failures])
	quit(0 if _failures == 0 else 1)


func check(cond: bool, msg: String = "") -> void:
	if cond:
		_passes += 1
	else:
		_failures += 1
		printerr("FAIL %s: %s" % [_current, msg])


func check_eq(a: Variant, b: Variant, msg: String = "") -> void:
	check(a == b, "%s (got %s, expected %s)" % [msg, str(a), str(b)])


func check_near(a: float, b: float, eps: float, msg: String = "") -> void:
	check(absf(a - b) <= eps, "%s (got %f, expected %f ± %f)" % [msg, a, b, eps])
