extends Node3D
## Grey-box tree mode (Prototype 1, steps 3–4): runs the GrowthSim, rebuilds the mesh,
## moves the sun with the in-game clock, and offers a boost button and a fast-forward.

@onready var mesh_instance: MeshInstance3D = $Tree
@onready var sun: DirectionalLight3D = $Sun
@onready var boost_button: Button = $UI/Boost
@onready var info: Label = $UI/Info
@onready var speed: HSlider = $UI/Speed

var sim: GrowthSim
var builder := TreeMeshBuilder.new()
var _rebuild_timer: float = 0.0
## Rebuild the mesh at most this often (seconds).
const REBUILD_INTERVAL := 0.25


func _ready() -> void:
	sim = SaveData.load()
	if sim == null:
		sim = GrowthSim.new(int(Time.get_unix_time_from_system()))
		# Prototype: the seedling starts with a small nutrient pack (the tutorial root run comes later).
		for k in range(4):
			sim.resources.add(k, 20.0)
	builder.radius_scale = 3.0
	boost_button.button_down.connect(func() -> void: sim.clock.boost_active = true)
	boost_button.button_up.connect(func() -> void: sim.clock.boost_active = false)
	_rebuild()


func _process(delta: float) -> void:
	sim.tick(delta * speed.value)
	_update_sun()
	_rebuild_timer += delta
	if _rebuild_timer >= REBUILD_INTERVAL:
		_rebuild_timer = 0.0
		_rebuild()
	info.text = "Day %d  %s  %s\nNodes %d  Height %.2f m\nLife force %.1f   Water %.1f  N %.1f  P %.1f  K %.1f" % [
		sim.clock.day_count,
		"day" if sim.clock.is_day() else "night",
		"BOOST" if sim.clock.boost_active else "",
		sim.graph.size(), sim.height(), sim.resources.life_force,
		sim.resources.amount(0), sim.resources.amount(1), sim.resources.amount(2), sim.resources.amount(3)]


func _update_sun() -> void:
	var dir := sim.clock.sun_direction()
	if dir == Vector3.ZERO:
		sun.light_energy = 0.05
		return
	sun.light_energy = 1.0 * sim.clock.light_level() + 0.1
	sun.look_at_from_position(dir * 20.0, Vector3.ZERO, Vector3.UP)


func _rebuild() -> void:
	mesh_instance.mesh = builder.build(sim.graph)


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST or what == NOTIFICATION_APPLICATION_PAUSED:
		SaveData.save(sim)
