extends RefCounted
var t


func test_full_day_under_ten_minutes() -> void:
	var c := DayCycle.new()
	t.check(c.seconds_per_day <= 600.0, "day + night at most 10 real minutes")


func test_sun_rises_east_sets_west() -> void:
	var c := DayCycle.new()
	c.time_of_day = 0.05
	t.check(c.sun_direction().x > 0.0, "morning sun in the east")
	c.time_of_day = c.daylight_fraction * 0.5
	t.check_near(c.sun_height(), 1.0, 1e-6, "noon at full height")
	c.time_of_day = c.daylight_fraction * 0.9
	t.check(c.sun_direction().x < 0.0, "evening sun in the west")
	c.time_of_day = c.daylight_fraction + 0.5 * (1.0 - c.daylight_fraction)
	t.check(not c.is_day(), "night")
	t.check_eq(c.sun_direction(), Vector3.ZERO, "no sun at night")


func test_advance_counts_days() -> void:
	var c := DayCycle.new()
	c.seconds_per_day = 100.0
	c.advance(250.0)
	t.check_eq(c.day_count, 2, "two days passed")
	t.check_near(c.time_of_day, 0.5, 1e-6, "half a day into the third")


func test_day_is_five_minutes_night_three() -> void:
	var c := DayCycle.new()
	t.check_near(c.seconds_per_day * c.daylight_fraction, 300.0, 1e-3, "five minutes of daylight")
	t.check_near(c.seconds_per_day * (1.0 - c.daylight_fraction), 180.0, 1e-3, "three minutes of night")


func test_boost_multiplies_light() -> void:
	var c := DayCycle.new()
	c.time_of_day = c.daylight_fraction * 0.5
	var base := c.light_level()
	c.boost_active = true
	t.check_near(c.light_level(), base * c.boost_multiplier, 1e-6, "boost multiplies")
