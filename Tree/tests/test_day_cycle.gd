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
	t.check(c.sun_direction().z > 0.3, "noon sun stands in the south (+Z)")
	t.check(c.sun_direction().y > 0.5, "noon sun stands high")
	t.check(absf(c.sun_direction().x) < 1e-6, "noon sun neither east nor west")
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
	t.check_near(c.seconds_per_day * c.daylight_fraction, 120.0, 1e-3, "two minutes of daylight (play test 3)")
	t.check_near(c.seconds_per_day * (1.0 - c.daylight_fraction), 72.0, 1e-3, "a short night clock")
	t.check_near(c.hour_seconds() * DayCycle.DAYLIGHT_HOURS, 120.0, 1e-3, "fourteen game hours of daylight")


func test_boost_multiplies_light() -> void:
	var c := DayCycle.new()
	c.time_of_day = c.daylight_fraction * 0.5
	var base := c.light_level()
	c.boost_active = true
	t.check_near(c.light_level(), base * c.boost_multiplier, 1e-6, "boost multiplies")


func test_boost_lowers_life_force_yield() -> void:
	var c := DayCycle.new()
	c.time_of_day = c.daylight_fraction * 0.5
	var calm := c.life_force_light()
	c.boost_active = true
	t.check(c.life_force_light() < calm, "boosting turns light into less life force")
	t.check(c.light_level() > calm, "but into more growth")
