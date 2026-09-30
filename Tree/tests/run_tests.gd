extends SceneTree
## Minimal headless test runner (no addon needed).
## Run: godot --headless -s tests/run_tests.gd   (exit code 0 = all passed)

var _failures: int = 0
var _passes: int = 0
var _current: String = ""
var _logger := ErrorCounter.new()
var _ran := false


## Counts script errors, so a test that crashes half-way fails instead of passing silently.
class ErrorCounter extends Logger:
	var count: int = 0

	func _log_error(_function: String, _file: String, _line: int, _code: String, _rationale: String, _editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
		if error_type == ERROR_TYPE_SCRIPT or error_type == ERROR_TYPE_ERROR:
			count += 1

	func _log_message(_message: String, _error: bool) -> void:
		pass


func _init() -> void:
	OS.add_logger(_logger)


## The suites run on the first frame, when the root is in the tree (a test can put a view in a
## viewport and project points, as the bonsai's sill tools do).
func _process(_delta: float) -> bool:
	if not _ran:
		_ran = true
		_run_all()
	return false


func _run_all() -> void:
	var tests := [
		preload("res://tests/test_plant_graph.gd"),
		preload("res://tests/test_space_colonization.gd"),
		preload("res://tests/test_resources.gd"),
		preload("res://tests/test_day_cycle.gd"),
		preload("res://tests/test_growth_sim.gd"),
		preload("res://tests/test_save_data.gd"),
		preload("res://tests/test_tree_mesh_builder.gd"),
		preload("res://tests/test_underground.gd"),
		preload("res://tests/test_root_system.gd"),
		preload("res://tests/test_game_state.gd"),
		preload("res://tests/test_species.gd"),
		preload("res://tests/test_phone.gd"),
		preload("res://tests/test_hud_icons.gd"),
		preload("res://tests/test_shed.gd"),
		preload("res://tests/test_forest_impostors.gd"),
		preload("res://tests/test_clearing.gd"),
		preload("res://tests/test_time_lapse.gd"),
		preload("res://tests/test_almanac.gd"),
		preload("res://tests/test_bonsai.gd"),
		preload("res://tests/test_tree_growth_curve.gd"),
		preload("res://tests/test_care.gd"),
		preload("res://tests/test_wish.gd"),
		preload("res://tests/test_live_picture.gd"),
	]
	for script in tests:
		var suite: RefCounted = script.new()
		suite.set("t", self)
		for m in suite.get_method_list():
			var name: String = m["name"]
			if name.begins_with("test_"):
				_current = "%s.%s" % [script.resource_path.get_file(), name]
				print("RUN ", _current)
				var errors_before := _logger.count
				suite.call(name)
				if _logger.count > errors_before:
					_failures += 1
					printerr("FAIL %s: script error (see above)" % _current)
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
