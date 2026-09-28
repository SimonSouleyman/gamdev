class_name Almanac
extends RefCounted
## The real calendar behind the mood of the clearing (design doc sections 2, 11 and 17): the
## moon's phase, the season's look and the day's weather. Mood only: nothing here touches growth,
## and the sun keeps its arc. Pure static functions, so tests can pin any date.
##
## Moon: the mean synodic month counted from a known new moon (6 January 2000, 18:14 UTC). The
## real moon runs up to about half a day ahead of or behind this mean, which the eye cannot see.
## Seasons follow the German calendar: spring from 20 March, summer from 1 June, autumn from
## 1 September; from 20 November the late autumn look holds until spring (winter is parked).
## Weather: seeded per game day from the save seed and the real date; chances follow the season.

enum Season { SPRING, SUMMER, AUTUMN, LATE_AUTUMN }
const SEASON_NAMES: Array[String] = ["spring", "summer", "autumn", "late_autumn"]

const SYNODIC_MONTH := 29.530588853
## Unix time of the new moon of 2000-01-06 18:14 UTC.
const KNOWN_NEW_MOON := 947182440

## Debug and tools: a fixed Unix time instead of the clock (-1 = the real time).
static var now_override: int = -1
## Debug and tools: "spring", "summer", "autumn" or "late_autumn" forces that season's look.
static var season_override: String = ""
## Debug and tools: "rain", "mist", "dew", "thunder" or "clear" forces the day's weather.
static var weather_override: String = ""
## Debug and tools: a moon phase 0..1 instead of the real one (-1 = real).
static var moon_override: float = -1.0

## A day for each forced season, in its middle, so the look is that season's full look.
const SEASON_DAYS := {"spring": [4, 28], "summer": [7, 15], "autumn": [10, 22], "late_autumn": [12, 10]}


## Reads --season=, --weather=, --moon= and --date=YYYY-MM-DD from the command line (after --),
## so a screenshot or a PC test can force a look without any UI.
static func read_cmdline() -> void:
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--season="):
			season_override = a.substr(9)
		elif a.begins_with("--weather="):
			weather_override = a.substr(10)
		elif a.begins_with("--moon="):
			moon_override = float(a.substr(7))
		elif a.begins_with("--date="):
			now_override = int(Time.get_unix_time_from_datetime_string(a.substr(7) + "T12:00:00"))


static func now() -> int:
	return now_override if now_override >= 0 else int(Time.get_unix_time_from_system())


## Today's local date as {"year", "month", "day"}; a forced season picks a day in its middle.
static func today() -> Dictionary:
	var bias := 0 if now_override >= 0 else int(Time.get_time_zone_from_system().get("bias", 0)) * 60
	var d := Time.get_date_dict_from_unix_time(now() + bias)
	if SEASON_DAYS.has(season_override):
		d["month"] = SEASON_DAYS[season_override][0]
		d["day"] = SEASON_DAYS[season_override][1]
	return {"year": int(d["year"]), "month": int(d["month"]), "day": int(d["day"])}


# --- the moon -------------------------------------------------------------------

## Age of the moon in its cycle, 0..1: 0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter.
static func moon_phase(unix: int) -> float:
	var days := float(unix - KNOWN_NEW_MOON) / 86400.0
	return fposmod(days / SYNODIC_MONTH, 1.0)


static func moon_phase_now() -> float:
	return moon_override if moon_override >= 0.0 else moon_phase(now())


## Share of the disc that is lit, 0..1.
static func moon_lit_fraction(phase: float) -> float:
	return 0.5 * (1.0 - cos(TAU * phase))


## Waxing: lit on the right side, as seen from Germany (northern hemisphere).
static func moon_lit_on_right(phase: float) -> bool:
	return phase < 0.5


## Where the moon stands during the game's night, as an angle on the sun's arc (0 east horizon,
## PI/2 south, PI west horizon; outside 0..PI below the horizon). The night view stands for the
## whole night, so the moon is shown at the hour it stands highest: a full moon high in the south,
## a young crescent low in the west after sunset, an old one low in the east before dawn; around
## new moon it stays below the horizon, as it should.
static func moon_arc_angle(phase: float) -> float:
	# Hour of the night (0 sunset, 1 sunrise) when the moon culminates, kept inside the night.
	var f := clampf(phase * 2.0 - 0.5, 0.0, 1.0)
	return PI + f * PI - phase * TAU


# --- the seasons --------------------------------------------------------------------

static func day_of_year(month: int, day: int) -> int:
	var starts := [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
	return int(starts[clampi(month, 1, 12) - 1]) + day


static func season_for(month: int, day: int) -> Season:
	var doy := day_of_year(month, day)
	if doy >= day_of_year(3, 20) and doy < day_of_year(6, 1):
		return Season.SPRING
	if doy >= day_of_year(6, 1) and doy < day_of_year(9, 1):
		return Season.SUMMER
	if doy >= day_of_year(9, 1) and doy < day_of_year(11, 20):
		return Season.AUTUMN
	return Season.LATE_AUTUMN


static func season_now() -> Season:
	var d := today()
	return season_for(d["month"], d["day"])


## How the season looks on this date, eased so no day jumps (except the parked winter's end):
## "fresh": spring's light green (1 in April and May, gone by late June);
## "autumn": yellow, orange and red (from 1 September, full by 25 October);
## "late": the late autumn look, browner and thinner (from 25 October, full by 20 November and
## held until 19 March); "fall": how many leaves drift down (0..1).
static func season_look(month: int, day: int) -> Dictionary:
	var doy := float(day_of_year(month, day))
	var spring := float(day_of_year(3, 20))
	var parked := doy < spring
	var fresh := 0.0 if parked else 1.0 - smoothstep(float(day_of_year(5, 20)), float(day_of_year(6, 25)), doy)
	var autumn := 1.0 if parked else smoothstep(float(day_of_year(9, 1)), float(day_of_year(10, 25)), doy)
	var late := 1.0 if parked else smoothstep(float(day_of_year(10, 25)), float(day_of_year(11, 20)), doy)
	var fall := 0.6 if parked else smoothstep(float(day_of_year(9, 20)), float(day_of_year(10, 30)), doy) * (1.0 - 0.4 * late)
	return {"fresh": fresh, "autumn": autumn, "late": late, "fall": fall}


static func season_look_now() -> Dictionary:
	var d := today()
	return season_look(d["month"], d["day"])


# --- the weather ------------------------------------------------------------------------

## Chances per game day of a shower, a misty morning, dew and distant thunder, by season.
const WEATHER_CHANCES := {
	Season.SPRING: {"rain": 0.22, "mist": 0.2, "dew": 0.45, "thunder": 0.04},
	Season.SUMMER: {"rain": 0.16, "mist": 0.08, "dew": 0.4, "thunder": 0.1},
	Season.AUTUMN: {"rain": 0.24, "mist": 0.38, "dew": 0.35, "thunder": 0.03},
	Season.LATE_AUTUMN: {"rain": 0.26, "mist": 0.42, "dew": 0.15, "thunder": 0.0},
}


## The weather of one game day. Deterministic from the save seed, the game day and the real date.
## Times are fractions of the daylight (0 sunrise, 1 sunset). A shower comes in the afternoon;
## thunder only rumbles far away, twice, while the afternoon is warm.
static func weather_for(seed: int, game_day: int, date: Dictionary) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash([seed, "weather", game_day, int(date["year"]), int(date["month"]), int(date["day"])])
	var chances: Dictionary = WEATHER_CHANCES[season_for(date["month"], date["day"])]
	var w := {
		"rain": rng.randf() < float(chances["rain"]),
		"mist": rng.randf() < float(chances["mist"]),
		"dew": rng.randf() < float(chances["dew"]),
		"thunder": rng.randf() < float(chances["thunder"]),
		"rain_start": rng.randf_range(0.38, 0.62),
		"rain_length": rng.randf_range(0.14, 0.24),
		"thunder_at": [rng.randf_range(0.45, 0.6), rng.randf_range(0.65, 0.8)],
	}
	# A rainy afternoon follows a clear dawn less often than not: no dew before a shower.
	if w["rain"] and w["dew"] and rng.randf() < 0.5:
		w["dew"] = false
	match weather_override:
		"clear":
			w["rain"] = false
			w["mist"] = false
			w["dew"] = false
			w["thunder"] = false
		"rain", "mist", "dew", "thunder":
			for k in ["rain", "mist", "dew", "thunder"]:
				w[k] = k == weather_override
	return w


## Rain strength 0..1 at this point of the daylight (soft start and end of the shower).
static func rain_amount(w: Dictionary, t: float) -> float:
	if not w.get("rain", false):
		return 0.0
	var s: float = w["rain_start"]
	var e: float = s + float(w["rain_length"])
	return smoothstep(s, s + 0.04, t) * (1.0 - smoothstep(e - 0.05, e, t))


## Morning mist 0..1: thick at sunrise, gone by mid-morning.
static func mist_amount(w: Dictionary, t: float) -> float:
	return 1.0 - smoothstep(0.1, 0.32, t) if w.get("mist", false) else 0.0


## Dew on the grass 0..1: sparkles in the early sun, dry by mid-morning. After a shower the grass
## glistens again for a while.
static func dew_amount(w: Dictionary, t: float) -> float:
	var d := 1.0 - smoothstep(0.08, 0.28, t) if w.get("dew", false) else 0.0
	if w.get("rain", false):
		var e: float = float(w["rain_start"]) + float(w["rain_length"])
		d = maxf(d, smoothstep(e - 0.05, e, t) * (1.0 - smoothstep(e, e + 0.15, t)) * 0.7)
	return d


## A line for the diary when the day's weather was worth noting ("" otherwise). `part`:
## "morning" (mist and dew) or "evening" (the shower and the thunder).
static func diary_line(w: Dictionary, part: String) -> String:
	if part == "morning":
		if w.get("mist", false) and w.get("dew", false):
			return "A misty morning; dew hung on every blade of grass."
		if w.get("mist", false):
			return "Mist lay over the clearing this morning."
		if w.get("dew", false):
			return "Dew sparkled on the meadow in the first sun."
		return ""
	var lines: Array[String] = []
	if w.get("rain", false):
		lines.append("A light shower passed over the clearing in the afternoon.")
	if w.get("thunder", false):
		lines.append("Thunder rumbled somewhere far away.")
	return " ".join(lines)
