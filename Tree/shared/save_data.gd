class_name SaveData
extends RefCounted
## Save/load of the whole game state to user://. Step 9 of the build plan.
## Stores a unix timestamp so offline growth can be applied on load.

const SAVE_PATH := "user://tree_save.json"
const GAME_PATH := "user://tree_game.json"


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


## The whole game (tree, roots, underground, diary, loop). Offline growth is applied on load.
static func save_game(state: GameState, path: String = GAME_PATH) -> Error:
	var data := {"saved_at_unix": Time.get_unix_time_from_system(), "game": state.to_dict()}
	# Write a temporary file and swap it in, so a crash mid-write never loses the tree.
	var tmp := path + ".tmp"
	var f := FileAccess.open(tmp, FileAccess.WRITE)
	if f == null:
		return FileAccess.get_open_error()
	f.store_string(JSON.stringify(data, "", false, true))
	f.close()
	return DirAccess.rename_absolute(ProjectSettings.globalize_path(tmp), ProjectSettings.globalize_path(path))


static func load_game(path: String = GAME_PATH, now_unix: float = -1.0) -> GameState:
	if not FileAccess.file_exists(path):
		return null
	# A JSON instance reports a parse failure quietly (JSON.parse_string logs an engine error).
	var json := JSON.new()
	var parsed: Variant = json.data if json.parse(FileAccess.get_file_as_string(path)) == OK else null
	if not _looks_like_a_game(parsed):
		# Keep the broken file aside instead of letting the next autosave overwrite it.
		DirAccess.rename_absolute(ProjectSettings.globalize_path(path), ProjectSettings.globalize_path(path + ".broken"))
		return null
	var data := parsed as Dictionary
	var state := GameState.from_dict(data["game"])
	if now_unix < 0.0:
		now_unix = Time.get_unix_time_from_system()
	# At most a week counts: after that the tree simply waited for the player.
	var away := clampf(now_unix - float(data.get("saved_at_unix", now_unix)), 0.0, 7.0 * 86400.0)
	if away > 0.0:
		state.sim.apply_offline(away)
	return state


## GDScript has no try/catch, so a save is checked for the shape GameState.from_dict reads
## before it is trusted; anything else counts as broken.
static func _looks_like_a_game(parsed: Variant) -> bool:
	if not (parsed is Dictionary) or not ((parsed as Dictionary).get("game") is Dictionary):
		return false
	var game: Dictionary = parsed["game"]
	if not (game.get("sim") is Dictionary):
		return false
	var sim: Dictionary = game["sim"]
	if not (sim.get("graph") is Dictionary):
		return false
	var graph: Dictionary = sim["graph"]
	for key in ["positions", "parents", "radii", "ages"]:
		if not (graph.get(key) is Array):
			return false
	var n := (graph["parents"] as Array).size()
	if n < 1 or (graph["positions"] as Array).size() != n * 3 or (graph["radii"] as Array).size() != n or (graph["ages"] as Array).size() != n:
		return false
	for key in ["roots", "underground", "diary"]:
		if game.has(key) and not (game[key] is Dictionary):
			return false
	if game.has("roots") and (game["roots"] as Dictionary).has("graph"):
		var rg: Variant = game["roots"]["graph"]
		if not (rg is Dictionary) or not ((rg as Dictionary).get("parents") is Array) or not ((rg as Dictionary).get("positions") is Array):
			return false
		if ((rg as Dictionary)["positions"] as Array).size() != ((rg as Dictionary)["parents"] as Array).size() * 3:
			return false
	return true


static func flatten_sim(sim_dict: Dictionary) -> Dictionary:
	return _flatten_packed(sim_dict)


static func restore_sim(sim_dict: Dictionary) -> Dictionary:
	return _restore_packed(sim_dict)


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
