class_name GameState
extends RefCounted
## The whole game in one object: the tree, the roots, the underground, the diary and
## the day/night loop that switches between them (design doc sections 3, 5, 7, 9, 12).
## The clock drives the mode: DAY is tree mode; at sunset time holds until the player
## taps the ground (SUNSET); NIGHT is root mode, and it lasts until tonight's run is spent,
## then fast-forwards to sunrise. Pure data: scenes read it and call its methods.

enum Phase { DAY, SUNSET, NIGHT }

## Life force the seed carries for its very first root.
const SEED_LIFE_FORCE: float = 20.0
## Once tonight's run is over, the rest of the night passes this many times faster.
const NIGHT_FAST_FORWARD: float = 30.0
## Real seconds for the short visit underground on a night without life force.
const EMPTY_NIGHT_VISIT: float = 5.0
## Seconds kept in hand before sunrise while the run is not finished.
const NIGHT_HOLD_MARGIN: float = 0.5

var seed: int = 1
var sim: GrowthSim
var ground: Underground
var roots: RootSystem
var diary := Diary.new()
var phase: Phase = Phase.DAY
## Tonight's single run was used.
var run_used: bool = false
## Tonight is over (run spent, or an empty visit ended): fast-forward to sunrise.
var night_done: bool = false
## Tonight there was no life force for a root.
var night_empty: bool = false
var _empty_timer: float = 0.0
## One-time journal pages already shown (the tutorial lives in the journal).
var seen_pages: Dictionary = {}
## Things that happened since the scenes last looked: "sunset", "dive", "sunrise",
## "run_done", "night_empty", "find:<kind>", "spent". Scenes pop them with take_events().
var _events: Array[String] = []
var _spent_announced: bool = false


## A new game: a seed is planted at sunset; the first night is the first root run.
static func new_game(random_seed: int) -> GameState:
	var g := GameState.new()
	g.seed = random_seed
	g.sim = GrowthSim.new(random_seed)
	g.ground = Underground.new(random_seed)
	g.roots = RootSystem.new(random_seed)
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction
	g.sim.resources.life_force = SEED_LIFE_FORCE
	g.phase = Phase.SUNSET
	g.diary.add(0, "I planted a linden seed in the meadow as the sun went down.")
	g._events.append("sunset")
	return g


func day_number() -> int:
	return sim.clock.day_count


func take_events() -> Array[String]:
	var out := _events
	_events = []
	return out


func _event(e: String) -> void:
	_events.append(e)


func is_seed() -> bool:
	return sim.graph.size() <= 2


# --- the loop ---------------------------------------------------------------

func tick(delta: float) -> void:
	var clock := sim.clock
	match phase:
		Phase.DAY:
			var to_sunset := (clock.daylight_fraction - clock.time_of_day) * clock.seconds_per_day
			if delta >= to_sunset:
				sim.tick(maxf(0.0, to_sunset))
				clock.time_of_day = clock.daylight_fraction
				clock.boost_active = false
				phase = Phase.SUNSET
				_event("sunset")
			else:
				sim.tick(delta)
				if not _spent_announced and sim.nutrients_spent():
					_spent_announced = true
					_event("spent")
		Phase.SUNSET:
			pass  # time holds until the player taps the ground
		Phase.NIGHT:
			if night_empty and not night_done:
				_empty_timer += delta
				if _empty_timer >= EMPTY_NIGHT_VISIT:
					night_done = true
			var to_dawn := (1.0 - clock.time_of_day) * clock.seconds_per_day
			if night_done:
				var d := delta * NIGHT_FAST_FORWARD
				if d >= to_dawn:
					sim.tick(to_dawn + 0.001)
					_sunrise()
				else:
					sim.tick(d)
			else:
				sim.tick(minf(delta, maxf(0.0, to_dawn - NIGHT_HOLD_MARGIN)))


## Tap the ground at sunset: dive into root mode.
func dive() -> bool:
	if phase != Phase.SUNSET:
		return false
	phase = Phase.NIGHT
	# Past the exact sunset instant, so the clock reads night.
	sim.clock.time_of_day = sim.clock.daylight_fraction + 0.0001
	run_used = false
	night_done = false
	_empty_timer = 0.0
	night_empty = sim.resources.life_force < roots.cost_per_metre(Vector3.DOWN) * roots.step_length
	_event("dive")
	if night_empty:
		diary.add(day_number(), "Night %d: no life force was left for a root. I only looked around below." % (day_number() + 1))
		_event("night_empty")
	return true


func can_start_run() -> bool:
	return phase == Phase.NIGHT and not run_used and not night_empty and roots.can_start_run()


func start_run(from_id: int) -> bool:
	if not can_start_run():
		return false
	if not roots.start_run(from_id):
		return false
	run_used = true
	return true


## The view started the run on RootSystem itself (RootView drives the roots directly).
func mark_run_started() -> void:
	run_used = true


## One frame of steering; returns false when the run has ended.
func steer(stick: Vector2, dive_held: bool, delta: float) -> bool:
	if not roots.run_active:
		return false
	var alive := roots.advance(stick, dive_held, delta, ground, sim.resources)
	for f in roots.last_finds:
		_on_find(f)
	if not alive:
		_on_run_done()
	return alive


## The view grew the root itself (RootView drives RootSystem directly): record the end of the run.
func notify_run_done() -> void:
	if phase == Phase.NIGHT and run_used and not night_done:
		_on_run_done()


func notify_find(f: Dictionary) -> void:
	_on_find(f)


func _on_find(f: Dictionary) -> void:
	var kind := str(f["kind"])
	diary.add(day_number(), "Found %s." % Underground.FIND_TEXTS.get(kind, kind))
	_event("find:" + kind)


func _on_run_done() -> void:
	night_done = true
	var t := roots.run_totals
	diary.add(day_number(), "Night %d: the new root grew %.0f m and drank water %.1f, nitrogen %.1f, phosphorus %.1f, potassium %.1f." % [
		day_number() + 1, roots.run_length, t[0], t[1], t[2], t[3]])
	_event("run_done")


func _sunrise() -> void:
	phase = Phase.DAY
	sim.clock.boost_active = false
	night_done = false
	night_empty = false
	_spent_announced = false
	var was_seed := sim.graph.size() <= 2
	sim.start_dawn_burst()
	if was_seed and not sim.nutrients_spent():
		diary.add(day_number(), "Day %d: the seed sprouted at dawn." % day_number())
	diary.wish = Diary.make_wish(ground, day_number(), seed)
	_event("sunrise")


## Called by the tree view at the end of the dawn burst, for the morning diary line.
func write_morning_line() -> void:
	diary.add(day_number(), "Day %d: the linden is %.1f m tall with %d leaf clusters." % [day_number(), sim.height(), sim.tip_count()])


# --- moving the day on ------------------------------------------------------

## Once nutrients are spent, the player may drag the sun along its arc to move time on.
func can_skip_time() -> bool:
	return phase == Phase.DAY and sim.nutrients_spent()


## Moves the day forward by `fraction` of a whole day (never backward, never past sunset).
func skip_time(fraction: float) -> void:
	if not can_skip_time() or fraction <= 0.0:
		return
	var clock := sim.clock
	var seconds := minf(fraction, clock.daylight_fraction - clock.time_of_day) * clock.seconds_per_day
	# Life force keeps accruing for the skipped time, as if the player had waited.
	tick(seconds)


# --- journal pages ------------------------------------------------------------

## True the first time a page is asked for; the page then counts as seen.
func first_time(page: String) -> bool:
	if seen_pages.has(page):
		return false
	seen_pages[page] = true
	return true


# --- save -------------------------------------------------------------------

func to_dict() -> Dictionary:
	return {
		"version": 2,
		"seed": seed,
		"sim": SaveData.flatten_sim(sim.to_dict()),
		"underground": ground.to_dict(),
		"roots": roots.to_dict(),
		"diary": diary.to_dict(),
		"phase": phase,
		"run_used": run_used,
		"night_done": night_done,
		"night_empty": night_empty,
		"seen_pages": seen_pages.keys(),
	}


static func from_dict(d: Dictionary) -> GameState:
	var g := GameState.new()
	g.seed = int(d.get("seed", 1))
	g.sim = GrowthSim.from_dict(SaveData.restore_sim(d["sim"]))
	g.ground = Underground.from_dict(d.get("underground", {"seed": g.seed}))
	g.roots = RootSystem.from_dict(d.get("roots", {}), g.seed)
	g.diary = Diary.from_dict(d.get("diary", {}))
	g.phase = int(d.get("phase", Phase.DAY)) as Phase
	g.run_used = bool(d.get("run_used", false))
	g.night_done = bool(d.get("night_done", false))
	g.night_empty = bool(d.get("night_empty", false))
	for k in d.get("seen_pages", []):
		g.seen_pages[str(k)] = true
	return g
