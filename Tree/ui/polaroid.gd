class_name Polaroid
extends RefCounted
## 0.8 "send": a photo from the album as the Polaroid it is in the album (the photo cropped
## square, the white card and the handwritten caption), drawn on its own, without any HUD, lying
## on the album's page. The card is built exactly as ShedMenu builds the album's cards, then
## drawn three times as large, so text and paper stay sharp on another phone.

## The album card's size in the album (ShedMenu._build_album) and the page around it.
const CARD_WIDTH := 326.0
const MARGIN := 34.0
## Drawn this many times the album's size.
const SCALE := 3
## Shared pictures go here (the game's folder on a PC, which opens after sending).
static var DIR := "user://sent"


## The file the Polaroid of `photo_path` is written to.
static func path_for(photo_path: String) -> String:
	return DIR.path_join(photo_path.get_file().get_basename() + "_polaroid.png")


## The caption the album writes under the photo; a photo without one shares with its date.
static func caption_for(photo_path: String) -> String:
	var cap := Photos.caption(photo_path)
	if cap.strip_edges() != "":
		return cap
	return Backup.local_date(Photos._stamp(photo_path) / 1000.0)


## Draws the Polaroid under `host` (a node in the tree) and writes it as a PNG. Returns its path,
## or "" when it could not be drawn (a headless run draws nothing).
static func render(host: Node, photo_path: String) -> String:
	var photo := Photos.load_texture(photo_path)
	if photo == null or DisplayServer.get_name() == "headless":
		return ""
	var vp := SubViewport.new()
	vp.transparent_bg = false
	vp.disable_3d = true
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	vp.size_2d_override_stretch = true
	vp.size = Vector2i(512, 512)
	host.add_child(vp)
	var page := PanelContainer.new()
	PaperLook.apply(page, "book_page", 95, MARGIN, {"paper_color": Color(0.8, 0.72, 0.6), "crumple": 0.22, "foxing": 0.6, "mottle": 0.6, "edge_age": 0.0, "shadow_alpha": 0.0, "pad": 0.0})
	vp.add_child(page)
	var card := PanelContainer.new()
	# The album's card (ShedMenu._build_album, side 0), glued in straight.
	PaperLook.apply(card, "strip", 120, 14.0, {"torn": Vector4.ZERO, "crumple": 0.12, "paper_color": Color(0.95, 0.94, 0.9), "foxing": 0.15, "edge_age": 0.35, "curl": 5.0})
	page.add_child(card)
	var v := VBoxContainer.new()
	card.add_child(v)
	var tex := TextureRect.new()
	tex.custom_minimum_size = Vector2(300, 300)
	tex.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	tex.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	tex.texture = photo
	v.add_child(tex)
	var cap := Paper.ink_label(caption_for(photo_path), 26)
	cap.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	cap.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	cap.custom_minimum_size = Vector2(250, 0)
	v.add_child(cap)
	# Clearer print reaches this label too (Paper.watch_print), as in the album.
	await host.get_tree().process_frame
	var logical := page.get_combined_minimum_size().ceil()
	page.size = logical
	vp.size_2d_override = Vector2i(logical)
	vp.size = Vector2i(logical) * SCALE
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	var img := vp.get_texture().get_image()
	vp.queue_free()
	if img == null or img.is_empty():
		return ""
	DirAccess.make_dir_recursive_absolute(DIR)
	var out := path_for(photo_path)
	return out if img.save_png(out) == OK else ""
