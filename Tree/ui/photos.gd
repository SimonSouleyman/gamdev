class_name Photos
extends RefCounted
## Photos of the tree for the album (Simon, play test 3): one every morning after the dawn burst,
## and any taken with the camera scrap. Kept as small PNGs in the user folder; the file name
## carries the day, so the album can caption them.

## Tools point this elsewhere so they never touch the player's album.
static var DIR := "user://photos"


## The file name starts with the species, so one album holds every tree, one after another.
static func save_from(viewport: Viewport, day: int, tag: String, species_id: String = "linden") -> String:
	var img := viewport.get_texture().get_image()
	if img == null or img.is_empty():
		return ""
	var w := 360
	img.resize(w, int(float(w) * img.get_height() / img.get_width()), Image.INTERPOLATE_BILINEAR)
	DirAccess.make_dir_recursive_absolute(DIR)
	var path := "%s/%s_day%03d_%s_%d.png" % [DIR, species_id, day, tag, int(Time.get_unix_time_from_system() * 1000.0)]
	img.save_png(path)
	return path


static func list() -> Array[String]:
	var out: Array[String] = []
	var d := DirAccess.open(DIR)
	if d == null:
		return out
	for f in d.get_files():
		if f.ends_with(".png"):
			out.append(DIR.path_join(f))
	# In the order taken: the time stamp is the last part of the name.
	out.sort_custom(func(a: String, b: String) -> bool: return _stamp(a) < _stamp(b))
	return out


static func _stamp(path: String) -> int:
	return int(path.get_file().get_basename().get_slice("_", path.get_file().get_basename().get_slice_count("_") - 1))


static func load_texture(path: String) -> Texture2D:
	var img := Image.load_from_file(ProjectSettings.globalize_path(path))
	return ImageTexture.create_from_image(img) if img != null and not img.is_empty() else null


static func caption(path: String) -> String:
	var f := path.get_file()
	var day := int(f.substr(f.find("day") + 3, 3))
	var own := f.contains("_camera_")
	var tree := Species.from_id(f.get_slice("_day", 0)).display_name
	return ("%s, day %d" % [tree, day]) + (", my photo" if own else ", morning")


## Removes every photo (the new-game dev key; a new tree from the seed bag keeps the album).
static func clear() -> void:
	var d := DirAccess.open(DIR)
	if d == null:
		return
	for f in d.get_files():
		d.remove(f)
