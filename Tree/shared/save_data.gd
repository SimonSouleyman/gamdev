class_name SaveData
extends RefCounted
## Save/load of the whole game state to user://. Step 9 of the build plan.
## Stores a unix timestamp so offline growth can be applied on load.

const SAVE_PATH := "user://tree_save.json"


static func save(sim: GrowthSim, path: String = SAVE_PATH) -> Error:
	var data := {
		"saved_at_unix": Time.get_unix_time_from_system(),
		"sim": _flatten_packed(sim.to_dict()),
	}
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		return FileAccess.get_open_error()
	f.store_string(JSON.stringify(data, "", false, true))
	f.close()
	return OK


## Returns the loaded sim with offline growth applied, or null if no save exists.
static func load(path: String = SAVE_PATH, now_unix: float = -1.0) -> GrowthSim:
	if not FileAccess.file_exists(path):
		return null
	var text := FileAccess.get_file_as_string(path)
	var parsed: Variant = JSON.parse_string(text)
	if parsed == null or not (parsed is Dictionary):
		return null
	var data := parsed as Dictionary
	var sim := GrowthSim.from_dict(_restore_packed(data["sim"]))
	var saved_at := float(data.get("saved_at_unix", 0.0))
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	var away := maxf(0.0, now_unix - saved_at)
	if away > 0.0:
		sim.apply_offline(away)
	return sim


## JSON cannot store Vector3 arrays, so they are saved as flat [x, y, z, x, y, z, ...] lists.
static func _flatten_packed(sim_dict: Dictionary) -> Dictionary:
	var g: Dictionary = sim_dict["graph"]
	g["positions"] = _flatten(g["positions"])
	sim_dict["markers"] = _flatten(sim_dict.get("markers", PackedVector3Array()))
	return sim_dict


static func _restore_packed(sim_dict: Dictionary) -> Dictionary:
	var g: Dictionary = sim_dict["graph"]
	g["positions"] = _unflatten(g["positions"])
	sim_dict["markers"] = _unflatten(sim_dict.get("markers", []))
	return sim_dict


static func _flatten(vectors: PackedVector3Array) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	for v in vectors:
		out.append(v.x)
		out.append(v.y)
		out.append(v.z)
	return out


static func _unflatten(raw: Variant) -> PackedVector3Array:
	var out := PackedVector3Array()
	var flat: Array = raw
	var i := 0
	while i + 2 < flat.size():
		out.append(Vector3(float(flat[i]), float(flat[i + 1]), float(flat[i + 2])))
		i += 3
	return out
