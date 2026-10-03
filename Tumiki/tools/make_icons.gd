extends SceneTree
# Generates the launcher icons from tumiki-style blocks.

const C := [Color(0.9, 0.6, 0.6), Color(0.6, 0.9, 0.6), Color(0.6, 0.6, 0.9), Color(0.8, 0.8, 0.6),
	Color(0.8, 0.6, 0.8), Color(0.6, 0.8, 0.8)]
# Ship seen from above, pointing up: [x, y, color]
const SHIP := [[0, -2, 0], [0, -1, 2], [-1, 0, 2], [0, 0, 2], [1, 0, 2], [-2, 1, 3], [0, 1, 2], [2, 1, 3],
	[0, 2, 5], [-1, 2, 4], [1, 2, 4]]

func _block(img: Image, cx: float, cy: float, s: float, col: Color) -> void:
	var r := Rect2i(int(cx - s / 2), int(cy - s / 2), int(s), int(s))
	img.fill_rect(r, col * 0.6)
	img.fill_rect(r.grow(-int(s * 0.08)), col * 0.9)
	img.fill_rect(Rect2i(r.position + Vector2i(int(s * 0.08), int(s * 0.08)), Vector2i(int(s * 0.84), int(s * 0.12))), col.lightened(0.2))

func _ship(img: Image, size: int, scale: float, bg: bool) -> void:
	if bg:
		img.fill(Color(0.1, 0.3, 0.5))
	var s := size * scale
	for b in SHIP:
		_block(img, size / 2.0 + b[0] * s, size / 2.0 + b[1] * s, s * 0.96, C[b[2]])

func _init() -> void:
	var a := Image.create(192, 192, false, Image.FORMAT_RGBA8)
	_ship(a, 192, 0.15, true)
	a.save_png("res://icon.png")
	var fg := Image.create(432, 432, false, Image.FORMAT_RGBA8)
	fg.fill(Color(0, 0, 0, 0))
	_ship(fg, 432, 0.085, false)
	fg.save_png("res://icon_fg.png")
	var bgi := Image.create(432, 432, false, Image.FORMAT_RGBA8)
	bgi.fill(Color(0.1, 0.3, 0.5))
	bgi.save_png("res://icon_bg.png")
	quit()
