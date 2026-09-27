class_name DayCycle
extends RefCounted
## In-game clock. Day = tree mode, night = root mode. A whole day is under 10 real minutes.
## The clock only runs while the app is open (offline growth is applied separately on load).

## Real seconds for a full in-game day (day + night). Design doc: at most 10 minutes.
var seconds_per_day: float = 480.0
## 0..1 within the day. 0.0 = sunrise, 0.5 = sunset, 1.0 = next sunrise.
var time_of_day: float = 0.0
## Whole days completed since this tree was planted.
var day_count: int = 0
## Fraction of the day that is daylight.
var daylight_fraction: float = 0.625  # 5 min day, 3 min night (survey 2, to tune)
## Sun boost: brighter sun, faster growth, while active.
var boost_active: bool = false
var boost_multiplier: float = 3.0
## Boosting is a trade: growth speeds up, but leaves turn light into less life force.
var boost_life_force_factor: float = 0.6
## Height of the sun at noon. Germany in summer is about 60 degrees; seasons will tilt this later.
var noon_elevation: float = deg_to_rad(55.0)


func advance(delta_seconds: float) -> void:
	time_of_day += delta_seconds / seconds_per_day
	while time_of_day >= 1.0:
		time_of_day -= 1.0
		day_count += 1


func is_day() -> bool:
	return time_of_day < daylight_fraction


## 0 at sunrise/sunset, 1 at noon.
func sun_height() -> float:
	if not is_day():
		return 0.0
	return sin(PI * time_of_day / daylight_fraction)


## Unit vector pointing from the tree toward the sun, on its real arc:
## rises in the east (+X), stands in the south (-Z) and high at noon, sets in the west (-X).
## So a morning boost grows east, a noon boost south and upward, an evening boost west.
func sun_direction() -> Vector3:
	if not is_day():
		return Vector3.ZERO
	var angle := PI * time_of_day / daylight_fraction  # 0 = east horizon, PI = west horizon
	var arc := sin(angle)
	return Vector3(cos(angle), arc * sin(noon_elevation), -arc * cos(noon_elevation)).normalized()


## Light that leaves turn into life force. Boosting lowers the yield (the trade).
func life_force_light() -> float:
	var l := sun_height()
	return l * boost_life_force_factor if boost_active else l


## Light available for growth this instant, 0..boost_multiplier.
func light_level() -> float:
	var l := sun_height()
	return l * boost_multiplier if boost_active else l


func to_dict() -> Dictionary:
	return {"time_of_day": time_of_day, "day_count": day_count, "seconds_per_day": seconds_per_day}


static func from_dict(d: Dictionary) -> DayCycle:
	var c := DayCycle.new()
	c.time_of_day = float(d.get("time_of_day", 0.0))
	c.day_count = int(d.get("day_count", 0))
	c.seconds_per_day = float(d.get("seconds_per_day", 480.0))
	return c
