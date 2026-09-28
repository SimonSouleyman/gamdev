extends RefCounted
## The month time-lapse (design doc 17.7): photos grouped by tree, the flip-book's pages and
## pace, the album's flip-book spreads, the MJPEG AVI writer and the flip-book's small copies.
var t

const ALBUM: Array[String] = [
	"user://p/linden_day001_morning_10.png",
	"user://p/linden_day002_morning_20.png",
	"user://p/linden_day002_camera_25.png",
	"user://p/linden_day003_morning_30.png",
	"user://p/birch_day001_morning_40.png",
	"user://p/birch_day002_morning_50.png",
	"user://p/birch_day001_morning_60.png",
]


func test_photos_are_grouped_by_tree() -> void:
	var trees := TimeLapse.trees(ALBUM)
	t.check_eq(trees.size(), 3, "a linden, a birch, and a birch planted after it (the days start again)")
	t.check_eq(trees[0]["species"], "linden", "species from the file name")
	t.check_eq((trees[0]["photos"] as Array).size(), 4, "every photo of the linden")
	t.check_eq((trees[0]["mornings"] as Array).size(), 3, "the flip-book uses the mornings")
	t.check_eq(TimeLapse.pages(trees[0]), ALBUM.slice(0, 4).filter(func(p: String) -> bool: return p.contains("morning")), "pages in the order taken")
	t.check_eq(TimeLapse.tree_of(trees, 3), 0, "the linden's last photo")
	t.check_eq(TimeLapse.tree_of(trees, 4), 1, "the birch's first")
	t.check_eq(TimeLapse.tree_of(trees, 99), -1, "past the album")
	t.check_eq(TimeLapse.trees([] as Array[String]), [], "an empty album has no trees")


func test_the_flip_book_turns_six_pages_a_second() -> void:
	t.check_eq(TimeLapse.FPS, 6, "about six pages a second")
	t.check_eq(TimeLapse.frame_at(0.0, 30), 0, "starts at the first morning")
	t.check_eq(TimeLapse.frame_at(1.0, 30), 6, "six pages on after a second")
	t.check_eq(TimeLapse.frame_at(5.0, 30), 0, "then from the start again")
	t.check_eq(TimeLapse.frame_at(9.0, 30, false), 29, "without looping it stays on the grown tree")
	t.check_eq(TimeLapse.frame_at(1.0, 0), -1, "no pages, nothing to show")
	t.check_eq(Photos.day_of(ALBUM[3]), 3, "the day from the file name")


func test_a_finished_tree_gets_its_flip_book_page_after_its_photos() -> void:
	var m := ShedMenu.new()
	m._photos = ALBUM
	m.tree_finished = false
	m._build_spreads()
	# Spreads: photos 0-1, photos 2-3, the linden's flip-book, photos 4-5, the first birch's
	# flip-book, photo 6 (the current birch, not finished: no flip-book yet).
	t.check_eq(m._spreads, [{"photo": 0}, {"photo": 2}, {"flip": 0}, {"photo": 4}, {"flip": 1}, {"photo": 6}], "flip-books after finished trees")
	m.tree_finished = true
	m._build_spreads()
	t.check_eq(m._spreads[-1], {"flip": 2}, "the current tree's flip-book once it is finished")
	m._photos = [] as Array[String]
	m._build_spreads()
	t.check_eq(m._spreads, [{"photo": 0}], "an empty album still has its first page")
	m.free()


func test_the_video_is_a_valid_mjpeg_avi() -> void:
	var img := Image.create(64, 36, false, Image.FORMAT_RGB8)
	var jpegs: Array[PackedByteArray] = []
	for i in range(5):
		img.fill(Color(i * 0.2, 0.5, 0.3))
		jpegs.append(TimeLapse.encode_frame(img, Vector2i(32, 18)))
	var data := MjpegAvi.build(jpegs, 32, 18, TimeLapse.FPS)
	t.check_eq(data.slice(0, 4).get_string_from_ascii(), "RIFF", "a RIFF file")
	t.check_eq(data.decode_u32(4), data.size() - 8, "the RIFF size covers the file")
	t.check_eq(MjpegAvi.frame_count(data), 5, "five frames in the header")
	t.check(_find(data, "MJPG") > 0, "a Motion-JPEG stream")
	# The index: one keyframe entry per frame, each pointing at a JPEG in the movi list.
	var movi := _find(data, "movi")
	var idx := _find(data, "idx1")
	t.check(movi > 0 and idx > movi, "movi list, then the index")
	var entries := data.decode_u32(idx + 4) / 16
	t.check_eq(entries, 5, "one index entry per frame")
	for e in range(entries):
		var at := idx + 8 + e * 16
		var off := data.decode_u32(at + 8)
		var size := data.decode_u32(at + 12)
		t.check_eq(data.slice(movi + off, movi + off + 4).get_string_from_ascii(), "00dc", "entry %d points at a frame chunk" % e)
		t.check(data[movi + off + 8] == 0xFF and data[movi + off + 9] == 0xD8, "frame %d is a JPEG" % e)
		t.check_eq(size, jpegs[e].size(), "frame %d size" % e)
	t.check_eq(TimeLapse.video_size(1080, 1917), Vector2i(540, 958), "even sizes for the video")


func _find(data: PackedByteArray, tag: String) -> int:
	var b := tag.to_ascii_buffer()
	for i in range(data.size() - 4):
		if data[i] == b[0] and data[i + 1] == b[1] and data[i + 2] == b[2] and data[i + 3] == b[3]:
			return i
	return -1


func test_the_flip_book_uses_small_copies_and_the_video_is_written() -> void:
	var was := Photos.DIR
	Photos.DIR = "user://test_photos_timelapse"
	Photos.clear()
	DirAccess.make_dir_recursive_absolute(Photos.DIR)
	var img := Image.create(720, 1280, false, Image.FORMAT_RGB8)
	img.fill(Color(0.3, 0.5, 0.2))
	var path := Photos.DIR.path_join("linden_day004_morning_77.png")
	img.save_png(path)
	var tex := Photos.load_thumb(path)
	t.check(tex != null, "a small copy is made for an older photo")
	t.check_eq(tex.get_width() if tex != null else 0, Photos.THUMB_WIDTH, "small")
	t.check(FileAccess.file_exists(Photos.thumb_path(path)), "and kept beside the photos")
	t.check_eq(Photos.list(), [path] as Array[String], "the small copies are not photos of their own")
	var avi := "user://test_timelapse.avi"
	var jpegs: Array[PackedByteArray] = [TimeLapse.encode_frame(img, TimeLapse.video_size(720, 1280))]
	t.check_eq(MjpegAvi.write(avi, jpegs, 540, 960, TimeLapse.FPS), OK, "the video is written")
	t.check_eq(MjpegAvi.frame_count(FileAccess.get_file_as_bytes(avi)), 1, "and reads back")
	DirAccess.remove_absolute(avi)
	Photos.clear()
	t.check(not FileAccess.file_exists(Photos.thumb_path(path)), "clearing the album clears the copies")
	Photos.DIR = was
