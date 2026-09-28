class_name MjpegAvi
extends RefCounted
## A small writer for Motion-JPEG AVI files (the format Godot's own MovieWriter uses for .avi):
## a RIFF file with one video stream, each frame a whole JPEG, and an idx1 index. Plays in VLC,
## browsers' download previews and most PC players; the phone plugin turns it into an MP4 for
## the gallery (android_plugin/README.md). Pure data: no scene access.


## Writes `jpegs` (JPEG files in memory, all `width` x `height`) as an AVI at `fps` frames per
## second to `path`. Returns OK or the file error.
static func write(path: String, jpegs: Array[PackedByteArray], width: int, height: int, fps: int) -> Error:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		return FileAccess.get_open_error()
	f.store_buffer(build(jpegs, width, height, fps))
	f.close()
	return OK


## The whole file in memory.
static func build(jpegs: Array[PackedByteArray], width: int, height: int, fps: int) -> PackedByteArray:
	var biggest := 0
	var movi := StreamPeerBuffer.new()
	var index := StreamPeerBuffer.new()
	movi.put_data("movi".to_ascii_buffer())
	for j in jpegs:
		biggest = maxi(biggest, j.size())
		# Offsets in idx1 count from the "movi" tag.
		index.put_data("00dc".to_ascii_buffer())
		index.put_u32(0x10)  # AVIIF_KEYFRAME: every MJPEG frame stands alone
		index.put_u32(movi.get_position())
		index.put_u32(j.size())
		movi.put_data("00dc".to_ascii_buffer())
		movi.put_u32(j.size())
		movi.put_data(j)
		if j.size() % 2 == 1:
			movi.put_u8(0)  # chunks are padded to an even size
	var frames := jpegs.size()

	var avih := StreamPeerBuffer.new()
	avih.put_u32(int(1000000.0 / fps))  # microseconds per frame
	avih.put_u32(biggest * fps)  # max bytes per second
	avih.put_u32(0)  # padding granularity
	avih.put_u32(0x10)  # AVIF_HASINDEX
	avih.put_u32(frames)
	avih.put_u32(0)  # initial frames
	avih.put_u32(1)  # streams
	avih.put_u32(biggest)  # suggested buffer size
	avih.put_u32(width)
	avih.put_u32(height)
	for _i in range(4):
		avih.put_u32(0)

	var strh := StreamPeerBuffer.new()
	strh.put_data("vids".to_ascii_buffer())
	strh.put_data("MJPG".to_ascii_buffer())
	strh.put_u32(0)  # flags
	strh.put_u16(0)  # priority
	strh.put_u16(0)  # language
	strh.put_u32(0)  # initial frames
	strh.put_u32(1)  # scale
	strh.put_u32(fps)  # rate: fps = rate / scale
	strh.put_u32(0)  # start
	strh.put_u32(frames)  # length
	strh.put_u32(biggest)
	strh.put_u32(0xFFFFFFFF)  # quality: default
	strh.put_u32(0)  # sample size: varies
	strh.put_u16(0)
	strh.put_u16(0)
	strh.put_u16(width)
	strh.put_u16(height)

	var strf := StreamPeerBuffer.new()  # BITMAPINFOHEADER
	strf.put_u32(40)
	strf.put_32(width)
	strf.put_32(height)
	strf.put_u16(1)  # planes
	strf.put_u16(24)  # bit count
	strf.put_data("MJPG".to_ascii_buffer())
	strf.put_u32(width * height * 3)
	for _i in range(4):
		strf.put_u32(0)

	var strl := _list("strl", _chunk("strh", strh.data_array) + _chunk("strf", strf.data_array))
	var hdrl := _list("hdrl", _chunk("avih", avih.data_array) + strl)
	var body := "AVI ".to_ascii_buffer() + hdrl + _chunk("LIST", movi.data_array) + _chunk("idx1", index.data_array)
	return _chunk("RIFF", body)


## Frames in an AVI written here (for tests and the tool): the length in the main header.
static func frame_count(data: PackedByteArray) -> int:
	if data.size() < 56 or data.slice(0, 4).get_string_from_ascii() != "RIFF" or data.slice(8, 12).get_string_from_ascii() != "AVI ":
		return -1
	# RIFF(12) LIST(8) "hdrl"(4) "avih"(8): dwTotalFrames is the fifth field.
	return data.decode_u32(32 + 16)


static func _chunk(tag: String, data: PackedByteArray) -> PackedByteArray:
	var out := StreamPeerBuffer.new()
	out.put_data(tag.to_ascii_buffer())
	out.put_u32(data.size())
	out.put_data(data)
	if data.size() % 2 == 1:
		out.put_u8(0)
	return out.data_array


static func _list(kind: String, data: PackedByteArray) -> PackedByteArray:
	return _chunk("LIST", kind.to_ascii_buffer() + data)
