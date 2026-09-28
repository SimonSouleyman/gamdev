class_name LookDevTuner
extends Node
## Runs after TreeView's own per-frame light update (children process after their parent) and
## adjusts what the look test changes over the day: less sun glare in the haze at a low sun,
## which washed the backlit crown out to a milky grey, and a golden hour that lasts: the sun
## stays warm until it is well up, and the haze takes its colour.

var view: TreeView


func _process(_delta: float) -> void:
	var env: Environment = view._env
	env.fog_sun_scatter *= 0.35
	var h := view.state.sim.clock.sun_height()
	if h <= 0.0:
		return
	# Sunny, not overcast: more direct sun, less flat fill, so leaves and grass get real light
	# and shade.
	env.ambient_light_energy *= 0.6
	view._sun_light.light_energy *= 1.45
	var golden := 1.0 - smoothstep(0.03, 0.55, h)
	var sun: DirectionalLight3D = view._sun_light
	sun.light_color = Color(1.0, 0.95, 0.88).lerp(Color(1.0, 0.7, 0.4), golden)
	sun.light_energy *= 1.0 + 0.25 * golden
	env.fog_light_color = env.fog_light_color.lerp(Color(0.85, 0.7, 0.5), golden * 0.5)
