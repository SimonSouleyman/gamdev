extends RefCounted
var t


func _unix(s: String) -> int:
	return int(Time.get_unix_time_from_datetime_string(s))


## Distance between two phases on the cycle (0.98 and 0.01 are close).
func _phase_gap(a: float, b: float) -> float:
	var d := absf(fposmod(a - b, 1.0))
	return minf(d, 1.0 - d)


func test_moon_phase_matches_real_eclipses() -> void:
	# Eclipses happen exactly at new moon (solar) and full moon (lunar).
	t.check(_phase_gap(Almanac.moon_phase(_unix("2024-04-08T18:21:00")), 0.0) < 0.035, "new moon at the 2024 solar eclipse")
	t.check(_phase_gap(Almanac.moon_phase(_unix("2025-09-07T18:11:00")), 0.5) < 0.035, "full moon at the September 2025 lunar eclipse")
	t.check(_phase_gap(Almanac.moon_phase(_unix("2025-09-21T19:54:00")), 0.0) < 0.035, "new moon at the September 2025 solar eclipse")
	# A week after new moon: first quarter, half lit, lit on the right.
	var q := Almanac.moon_phase(_unix("2025-09-29T12:00:00"))
	t.check(_phase_gap(q, 0.25) < 0.04, "first quarter a week later (got %f)" % q)
	t.check_near(Almanac.moon_lit_fraction(q), 0.5, 0.15, "half the disc lit")
	t.check(Almanac.moon_lit_on_right(q), "a waxing moon is lit on the right")
	t.check(not Almanac.moon_lit_on_right(0.8), "a waning moon is lit on the left")


func test_moon_lit_fraction() -> void:
	t.check_near(Almanac.moon_lit_fraction(0.0), 0.0, 1e-6, "new moon dark")
	t.check_near(Almanac.moon_lit_fraction(0.5), 1.0, 1e-6, "full moon lit")
	t.check_near(Almanac.moon_lit_fraction(0.25), 0.5, 1e-6, "quarter half lit")
	t.check(Almanac.moon_phase(_unix("2026-09-28T00:00:00")) >= 0.0 and Almanac.moon_phase(_unix("1990-01-01T00:00:00")) < 1.0, "phase stays in 0..1, also before 2000")


func test_moon_stands_where_it_should_at_night() -> void:
	var full := Almanac.moon_arc_angle(0.5)
	t.check_near(full, PI * 0.5, 1e-4, "the full moon stands in the south at its highest")
	var young := Almanac.moon_arc_angle(0.1)
	t.check(young > PI * 0.5 and young < PI, "a young crescent stands low in the west")
	var old := Almanac.moon_arc_angle(0.9)
	t.check(old > 0.0 and old < PI * 0.5, "an old crescent stands low in the east")
	var new_moon := Almanac.moon_arc_angle(0.0)
	t.check(new_moon <= 0.0 or new_moon >= PI, "no moon above the horizon at new moon")


func test_seasons_follow_the_german_calendar() -> void:
	t.check_eq(Almanac.season_for(3, 19), Almanac.Season.LATE_AUTUMN, "19 March: winter is parked, late autumn holds")
	t.check_eq(Almanac.season_for(3, 20), Almanac.Season.SPRING, "20 March: spring")
	t.check_eq(Almanac.season_for(5, 31), Almanac.Season.SPRING, "end of May: spring")
	t.check_eq(Almanac.season_for(7, 15), Almanac.Season.SUMMER, "July: summer")
	t.check_eq(Almanac.season_for(9, 28), Almanac.Season.AUTUMN, "end of September: autumn")
	t.check_eq(Almanac.season_for(11, 25), Almanac.Season.LATE_AUTUMN, "late November: late autumn")
	t.check_eq(Almanac.season_for(1, 15), Almanac.Season.LATE_AUTUMN, "January: still late autumn")


func test_season_look_eases_and_holds() -> void:
	var april := Almanac.season_look(4, 25)
	t.check(april["fresh"] > 0.99 and april["autumn"] < 0.01, "April: fresh green, no autumn")
	var july := Almanac.season_look(7, 15)
	t.check(july["fresh"] < 0.01 and july["autumn"] < 0.01 and july["fall"] < 0.01, "July: deep summer green, no falling leaves")
	var october := Almanac.season_look(10, 22)
	t.check(october["autumn"] > 0.9 and october["fall"] > 0.5, "late October: autumn colours and falling leaves")
	var december := Almanac.season_look(12, 10)
	var february := Almanac.season_look(2, 10)
	t.check(december["late"] > 0.99 and february["late"] > 0.99, "the late autumn look holds through the parked winter")
	# No sudden jumps from one day to the next in the growing year.
	for doy in range(Almanac.day_of_year(3, 21), 364):
		var a := _look_on(doy)
		var b := _look_on(doy + 1)
		for k in ["fresh", "autumn", "late", "fall"]:
			if absf(float(a[k]) - float(b[k])) > 0.08:
				t.check(false, "%s jumps on day %d" % [k, doy])
				return
	t.check(true)


func _look_on(doy: int) -> Dictionary:
	var starts := [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365]
	for m in range(12):
		if doy <= starts[m + 1]:
			return Almanac.season_look(m + 1, doy - starts[m])
	return Almanac.season_look(12, 31)


func test_forced_season_and_date() -> void:
	Almanac.season_override = "autumn"
	t.check_eq(Almanac.season_now(), Almanac.Season.AUTUMN, "a forced season wins over the date")
	Almanac.season_override = ""
	Almanac.now_override = _unix("2026-07-01T12:00:00")
	t.check_eq(Almanac.today()["month"], 7, "a forced date")
	t.check_eq(Almanac.season_now(), Almanac.Season.SUMMER, "July is summer")
	Almanac.now_override = -1


func test_weather_is_seeded_and_now_and_then() -> void:
	var date := {"year": 2026, "month": 10, "day": 3}
	t.check_eq(Almanac.weather_for(42, 5, date), Almanac.weather_for(42, 5, date), "same seed, day and date: same weather")
	var rain := 0
	var mist := 0
	var thunder := 0
	var differs := false
	for day in range(400):
		var w := Almanac.weather_for(7, day, date)
		rain += 1 if w["rain"] else 0
		mist += 1 if w["mist"] else 0
		thunder += 1 if w["thunder"] else 0
		if w != Almanac.weather_for(8, day, date):
			differs = true
	t.check(rain > 40 and rain < 160, "a shower now and then, not every day (%d of 400)" % rain)
	t.check(mist > 80, "misty mornings are common in autumn (%d of 400)" % mist)
	t.check(thunder < 40, "thunder is rare (%d of 400)" % thunder)
	t.check(differs, "another save seed has other weather")
	var winter := 0
	for day in range(400):
		winter += 1 if Almanac.weather_for(7, day, {"year": 2026, "month": 12, "day": 10})["thunder"] else 0
	t.check_eq(winter, 0, "no thunder in the parked winter")


func test_weather_amounts_over_the_day() -> void:
	var w := {"rain": true, "mist": true, "dew": true, "thunder": false, "rain_start": 0.5, "rain_length": 0.2, "thunder_at": [0.5, 0.7]}
	t.check_near(Almanac.rain_amount(w, 0.3), 0.0, 1e-6, "dry before the shower")
	t.check_near(Almanac.rain_amount(w, 0.6), 1.0, 1e-6, "raining in the middle of it")
	t.check_near(Almanac.rain_amount(w, 0.8), 0.0, 1e-6, "dry after it")
	t.check(Almanac.mist_amount(w, 0.02) > 0.99 and Almanac.mist_amount(w, 0.5) < 0.01, "mist in the morning only")
	t.check(Almanac.dew_amount(w, 0.02) > 0.99 and Almanac.dew_amount(w, 0.45) < 0.01, "dew early, dry by noon")
	t.check(Almanac.dew_amount(w, 0.72) > 0.2, "the grass glistens after the shower")
	Almanac.weather_override = "rain"
	var forced := Almanac.weather_for(1, 1, {"year": 2026, "month": 7, "day": 1})
	Almanac.weather_override = ""
	t.check(forced["rain"] and not forced["mist"] and not forced["dew"], "a forced shower")


func test_weather_lines_in_the_diary() -> void:
	t.check_eq(Almanac.diary_line({"rain": false, "mist": false, "dew": false, "thunder": false}, "morning"), "", "a plain day is not noted")
	t.check(Almanac.diary_line({"mist": true}, "morning").contains("Mist"), "mist is noted")
	t.check(Almanac.diary_line({"rain": true, "thunder": true}, "evening").contains("Thunder"), "a shower and thunder are noted")
	Almanac.weather_override = "rain"
	var g := GameState.new_game(3)
	g.sim.clock.day_count = 2
	g.phase = GameState.Phase.DAY
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction - 0.001
	var before := g.diary.entries.size()
	g.tick(1.0)
	Almanac.weather_override = ""
	t.check_eq(g.phase, GameState.Phase.SUNSET, "the day ended")
	# 0.8.2.6 (J3): the weather is mood only; no diary line.
	t.check_eq(g.diary.entries.size(), before, "the shower writes no diary line")
