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
	# Big enough to serve as the phone's wallpaper (album: "as wallpaper").
	var w := 1080
	img.resize(w, int(float(w) * img.get_height() / img.get_width()), Image.INTERPOLATE_BILINEAR)
	DirAccess.make_dir_recursive_absolute(DIR)
	var path := "%s/%s_day%03d_%s_%d.png" % [DIR, species_id, day, tag, int(Time.get_unix_time_from_system() * 1000.0)]
	img.save_png(path)
	_save_thumb(img, path)
	return path


## The same, from an image already read back, written on a worker thread (0.8.2.2: the resize and
## the PNG and JPG encoding were most of the morning photo's hitch on the phone). Returns the path
## the photo will have.
static func save_image(img: Image, day: int, tag: String, species_id: String = "linden") -> String:
	if img == null or img.is_empty():
		return ""
	DirAccess.make_dir_recursive_absolute(DIR)
	DirAccess.make_dir_recursive_absolute(DIR.path_join("thumbs"))
	var path := "%s/%s_day%03d_%s_%d.png" % [DIR, species_id, day, tag, int(Time.get_unix_time_from_system() * 1000.0)]
	WorkerThreadPool.add_task(_encode.bind(img, path), false, "album photo")
	return path


## What `cam` sees, drawn off screen by a copy of it in the same world (a coroutine: the image
## comes after the next drawn frame). 0.8.2.4: the bonsai's milestone photo and the camera scrap
## read the screen back instead. On the phone's GL renderer that showed one frame upside down
## (the screen drawn flipped while it was read), and the bonsai's PNG was encoded on the main
## thread, the 0.95 s freeze before it. The screen is never read now; save with save_image.
static func shoot(cam: Camera3D, size: Vector2i) -> Image:
	var main_vp := cam.get_viewport()
	var vp := SubViewport.new()
	vp.size = size
	vp.render_target_update_mode = SubViewport.UPDATE_ONCE
	vp.msaa_3d = main_vp.msaa_3d
	vp.scaling_3d_mode = main_vp.scaling_3d_mode
	vp.scaling_3d_scale = main_vp.scaling_3d_scale
	vp.positional_shadow_atlas_size = main_vp.positional_shadow_atlas_size
	var c := Camera3D.new()
	c.environment = cam.environment
	c.attributes = cam.attributes
	c.near = cam.near
	c.far = cam.far
	c.fov = cam.fov
	c.cull_mask = cam.cull_mask
	c.keep_aspect = cam.keep_aspect
	vp.add_child(c)
	# Under the camera: the SubViewport draws the world its parent's viewport draws.
	cam.add_child(vp)
	c.global_transform = cam.global_transform
	c.make_current()
	await RenderingServer.frame_post_draw
	var img := vp.get_texture().get_image()
	vp.queue_free()
	return img


## The screen's size in pixels (the size the photos had when they were read from the screen).
static func screen_size(node: Node) -> Vector2i:
	var vp := node.get_viewport()
	return (vp as Window).size if vp is Window else Vector2i(vp.get_visible_rect().size)


static func _encode(img: Image, path: String) -> void:
	var w := 1080
	img.resize(w, int(float(w) * img.get_height() / img.get_width()), Image.INTERPOLATE_BILINEAR)
	img.save_png(path)
	_save_thumb(img, path)


## Small copies for the flip-book (TimeLapse), in a folder beside the photos: a month of full
## photos would take seconds to load and hundreds of megabytes on a phone.
const THUMB_WIDTH := 360


static func thumb_path(path: String) -> String:
	return DIR.path_join("thumbs").path_join(path.get_file().get_basename() + ".jpg")


static func _save_thumb(img: Image, path: String) -> void:
	var small := img.duplicate() as Image
	small.resize(THUMB_WIDTH, int(float(THUMB_WIDTH) * img.get_height() / img.get_width()), Image.INTERPOLATE_BILINEAR)
	DirAccess.make_dir_recursive_absolute(DIR.path_join("thumbs"))
	small.save_jpg(thumb_path(path), 0.85)


## The flip-book's copy of a photo (made from the photo the first time, for older albums).
static func load_thumb(path: String) -> Texture2D:
	var tp := thumb_path(path)
	if not FileAccess.file_exists(tp):
		var img := Image.load_from_file(ProjectSettings.globalize_path(path))
		if img == null or img.is_empty():
			return null
		_save_thumb(img, path)
	return load_texture(tp)


## The day in a photo's file name.
static func day_of(path: String) -> int:
	var f := path.get_file()
	return int(f.substr(f.find("_day") + 4, 3))


static func is_morning(path: String) -> bool:
	return path.get_file().contains("_morning_")


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
	# The bonsai's album grows by milestones (section 16 D).
	if f.contains("_bonsai_"):
		return "%s bonsai, care day %d" % [tree, day]
	return ("%s, day %d" % [tree, day]) + (", my photo" if own else ", morning")


## Removes every photo (the new-game dev key; a new tree from the seed bag keeps the album).
static func clear() -> void:
	var d := DirAccess.open(DIR)
	if d == null:
		return
	for f in d.get_files():
		d.remove(f)
	var t := DirAccess.open(DIR.path_join("thumbs"))
	if t != null:
		for f in t.get_files():
			t.remove(f)
