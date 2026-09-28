class_name TimeLapse
extends RefCounted
## The month time-lapse (design doc section 17.7): the album's morning photos of one tree, in
## order, played as a flip-book (FlipBook) and saved as a short video (MjpegAvi) that the phone
## puts into its gallery (Phone.save_video_to_gallery).

## Pages per second of the flip-book and the video.
const FPS: int = 6
## The last photo stays this many frames longer at the end of the video (the grown tree).
const HOLD_LAST: int = 6
## Width of the video (the height follows the photos, rounded to even).
const VIDEO_WIDTH: int = 540
## Where the videos go (in the user folder; the phone copies them into its gallery).
## Tools point this elsewhere.
static var DIR := "user://timelapse"


## The album's photos (Photos.list(), in the order taken) grouped by tree: a new tree begins
## when the species changes or the days start again. Each: {"species", "photos", "mornings"}.
static func trees(photos: Array[String]) -> Array:
	var out: Array = []
	var last_species := ""
	var last_day := -1
	for p in photos:
		var sp := p.get_file().get_slice("_day", 0)
		var day := Photos.day_of(p)
		if out.is_empty() or sp != last_species or day < last_day:
			out.append({"species": sp, "photos": [] as Array[String], "mornings": [] as Array[String]})
		var tree: Dictionary = out[-1]
		(tree["photos"] as Array[String]).append(p)
		if Photos.is_morning(p):
			(tree["mornings"] as Array[String]).append(p)
		last_species = sp
		last_day = day
	return out


## Index of the tree that holds photo number `photo_index` of the whole album; -1 if none.
static func tree_of(trees_list: Array, photo_index: int) -> int:
	var seen := 0
	for i in range(trees_list.size()):
		seen += (trees_list[i]["photos"] as Array).size()
		if photo_index < seen:
			return i
	return -1


## The page on show `seconds` into the flip-book of `count` pages.
static func frame_at(seconds: float, count: int, loop: bool = true) -> int:
	if count <= 0:
		return -1
	var f := int(floor(seconds * FPS))
	return posmod(f, count) if loop else mini(f, count - 1)


## The flip-book's pages: the mornings, or every photo if a tree has no morning photos.
static func pages(tree: Dictionary) -> Array[String]:
	var m: Array[String] = tree["mornings"]
	return m if not m.is_empty() else (tree["photos"] as Array[String])


## Video frame size for photos of `w` x `h`: VIDEO_WIDTH wide, even height (JPEG and players
## like even sizes).
static func video_size(w: int, h: int) -> Vector2i:
	var vh := int(round(float(VIDEO_WIDTH) * h / maxf(w, 1) / 2.0)) * 2
	return Vector2i(VIDEO_WIDTH, maxi(vh, 2))


## One video frame: the photo scaled to `size`, as a JPEG.
static func encode_frame(img: Image, size: Vector2i) -> PackedByteArray:
	var f := img.duplicate() as Image
	f.convert(Image.FORMAT_RGB8)
	f.resize(size.x, size.y, Image.INTERPOLATE_BILINEAR)
	return f.save_jpg_to_buffer(0.85)


## The video's file name for a tree (one per tree; a new save replaces it).
static func video_path(tree: Dictionary) -> String:
	var first: String = (tree["photos"] as Array[String])[0]
	return DIR.path_join("%s_%s.avi" % [tree["species"], first.get_file().get_basename().get_slice("_", first.get_file().get_basename().get_slice_count("_") - 1)])
