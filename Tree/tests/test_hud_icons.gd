extends RefCounted
## HUD pictures and the hand compass (design doc section 17, 0.6 item 2).
var t


func test_hud_pictures_exist_and_read_on_a_phone() -> void:
	for name in ["journal", "shed", "camera", "shears", "compass_case", "compass_dial", "compass_needle", "shears_cursor"]:
		var tex := load("res://ui/icons/%s.png" % name) as Texture2D
		t.check(tex != null, name + " picture exists")
		if tex != null and not name.begins_with("compass") and name != "shears_cursor":
			# Drawn about 96 canvas units tall (about 140 px on a 1080x2400 phone): enough pixels.
			t.check(maxi(tex.get_width(), tex.get_height()) >= 200, name + " picture is sharp enough")
	# The tap area is at least as large as the old paper scraps (140 x 46).
	t.check(Paper.PICTURE_TAP.x >= 140.0 and Paper.PICTURE_TAP.y >= 46.0, "tap area not smaller")


func test_compass_points_north() -> void:
	# Looking north (-z): north is straight up.
	t.check_near(Compass.north_angle_for(Basis.IDENTITY), 0.0, 1e-4, "facing north")
	# Looking east (+x): north is to the left.
	var east := Basis(Vector3.UP, -PI * 0.5)
	t.check_near(Compass.north_angle_for(east), -PI * 0.5, 1e-4, "facing east")
	# Looking south from above at a slant: north straight down.
	var south := Basis(Vector3.UP, PI) * Basis(Vector3.RIGHT, -0.6)
	t.check_near(absf(Compass.north_angle_for(south)), PI, 1e-4, "facing south")


func test_shears_pointer_hotspot_is_the_blade_point() -> void:
	var h: Vector2 = load("res://main.gd").shears_hotspot()
	t.check(h.x < 32.0 and h.y < 32.0, "hot spot in the top left quarter (the blade's point)")
