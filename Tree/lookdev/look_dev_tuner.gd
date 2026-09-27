class_name LookDevTuner
extends Node
## Runs after TreeView's own per-frame light update (children process after their parent) and
## adjusts what the look test changes over the day: less sun glare in the haze at a low sun,
## which washed the backlit crown out to a milky grey.

var view: TreeView


func _process(_delta: float) -> void:
	var env: Environment = view._env
	env.fog_sun_scatter *= 0.35
