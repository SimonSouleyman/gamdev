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
## Game seconds after sunrise until the morning (diary line, first-morning page or wish).
## Just after the dawn burst, so the diary can say what the night bought.
const MORNING_DELAY: float = 11.0
## Share of the drunk dots that come back into the soil each night.
const REGROW_SHARE: float = 0.05

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
## Tutorial pages shown but not yet closed when the game was saved; they come back on load.
var pending_pages: Array = []
## Things that happened since the scenes last looked: "sunset", "dive", "sunrise",
## "run_done", "night_empty", "find:<kind>", "spent". Scenes pop them with take_events().
var _events: Array[String] = []
var _spent_announced: bool = false
## Seconds since sunrise until the morning event; -1 when it already happened.
var morning_timer: float = -1.0
## This tree has grown to its species' full size (GrowthSim.is_finished).
var finished: bool = false
## The trees finished before this one and this one once finished, oldest first:
## each {"species": id, "days": int, "seed": int}. Carried from tree to tree (the grove);
## it decides which species the seed bag offers (Species.unlocked).
var grove: Array = []
## What happened while the game was closed, for the "while you were away" diary page: set by
## apply_offline after an absence of at least AWAY_REPORT_SECONDS, taken (once) by the scene.
## {"seconds", "grown" (metres), "height", "segments" (new ones), "visitors" (ids)}; empty if none.
var away_report: Dictionary = {}

## The bonsai on the shed's windowsill (design doc section 16): null until it is unlocked by the
## first finished tree (or the test switch). Independent of the tree (no shared life force or
## resources); it follows the same clock. Only the one on the sill grows; the others (cuttings
## of finished trees, waiting their turn) rest on the shelf below: species id -> its save.
var bonsai: BonsaiSim = null
var bonsai_resting: Dictionary = {}

## An absence shorter than this gets no "while you were away" page.
const AWAY_REPORT_SECONDS: float = 3600.0


## A new game: a seed is planted at sunset; the first night is the first root run.
static func new_game(random_seed: int, species_id: String = "linden") -> GameState:
	var g := GameState.new()
	g.seed = random_seed
	g.sim = GrowthSim.new(random_seed)
	g.sim.species = Species.from_id(species_id)
	g.ground = Underground.new(random_seed)
	g.roots = RootSystem.new(random_seed)
	g.roots.species = g.sim.species
	g.sim.clock.time_of_day = g.sim.clock.daylight_fraction
	g.sim.resources.life_force = SEED_LIFE_FORCE
	g.phase = Phase.SUNSET
	g.diary.add(0, "I planted a %s seed in the clearing as the sun went down." % g.tree_name())
	g._events.append("sunset")
	return g


## The next tree, planted from the seed bag: a new game of `species_id` that keeps the grove
## and the journal pages already read (the player knows the game by now).
static func new_tree(random_seed: int, species_id: String, previous: GameState) -> GameState:
	var g := new_game(random_seed, species_id)
	if previous != null:
		g.grove = previous.grove.duplicate(true)
		g.seen_pages = previous.seen_pages.duplicate()
		# Visitors come anew to each tree (the nest was already there on a new seedling).
		for k in g.seen_pages.keys():
			if str(k).begins_with("visitor_"):
				g.seen_pages.erase(k)
		# The bonsai is a lifelong companion: it stays on the sill from tree to tree.
		g.bonsai = previous.bonsai
		g.bonsai_resting = previous.bonsai_resting.duplicate(true)
		if g.bonsai != null:
			g.bonsai.clock.time_of_day = g.sim.clock.time_of_day
	return g


func species() -> Species:
	return sim.species


## The species in lower case, for diary lines ("the silver birch is 4 m tall").
func tree_name() -> String:
	return sim.species.display_name.to_lower()


## Species ids of the finished trees.
func finished_species() -> Array:
	var out: Array = []
	for t in grove:
		out.append(str(t.get("species", "")))
	return out


## Species the seed bag offers (all six with the test switch).
func unlocked_species(any_species: bool = false) -> Array[String]:
	return Species.unlocked(finished_species(), any_species)


## The seed bag may plant a new tree: this one is finished, or the test switch is on.
func can_plant_next(any_species: bool = false) -> bool:
	return finished or any_species


# --- the bonsai ------------------------------------------------------------------------

## Bonsai mode opens after the first finished tree (section 16 H), or with the test switch.
func bonsai_unlocked(any_species: bool = false) -> bool:
	return bonsai != null or not grove.is_empty() or any_species


## The juniper comes to the sill once the bonsai is unlocked. Returns it (or null while locked).
func ensure_bonsai(any_species: bool = false) -> BonsaiSim:
	if bonsai == null and bonsai_unlocked(any_species):
		bonsai = BonsaiSim.starter(hash([seed, "bonsai"]))
		bonsai.clock.time_of_day = sim.clock.time_of_day
	return bonsai


## What may stand on the sill: the juniper, and a cutting of every finished clearing tree
## (all six with the test switch).
func bonsai_choices(any_species: bool = false) -> Array[String]:
	var out: Array[String] = ["juniper"]
	for sid in Species.ORDER:
		if any_species or finished_species().has(sid):
			out.append(sid)
	return out


## Puts another bonsai on the sill: the one there rests on the shelf (kept as it is), the new
## one comes from the shelf or, the first time, as a fresh cutting.
func swap_bonsai(species_id: String, any_species: bool = false) -> bool:
	if bonsai == null or species_id == bonsai.species.id or not bonsai_choices(any_species).has(species_id):
		return false
	bonsai_resting[bonsai.species.id] = bonsai.to_dict()
	if bonsai_resting.has(species_id):
		bonsai = BonsaiSim.from_dict(bonsai_resting[species_id])
		bonsai_resting.erase(species_id)
	else:
		bonsai = BonsaiSim.cutting(hash([seed, "cutting", species_id]), species_id)
	bonsai.clock.time_of_day = sim.clock.time_of_day
	return true


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
	var before := clock.day_count + clock.time_of_day
	_tick_loop(delta)
	# The bonsai lives through the same days: it moves on exactly as far as the clock did.
	if bonsai != null:
		bonsai.follow(clock, (clock.day_count + clock.time_of_day - before) * clock.seconds_per_day)


func _tick_loop(delta: float) -> void:
	var clock := sim.clock
	match phase:
		Phase.DAY:
			# A tapped boost lasts one game hour; the clock never stops for it.
			var clk := sim.clock
			if clk.boost_remaining > 0.0:
				clk.boost_active = true
				clk.boost_remaining -= delta
				if clk.boost_remaining <= 0.0:
					clk.boost_remaining = 0.0
					clk.boost_active = false
			var to_sunset := (clock.daylight_fraction - clock.time_of_day) * clock.seconds_per_day
			if delta >= to_sunset:
				sim.tick(maxf(0.0, to_sunset))
				clock.time_of_day = clock.daylight_fraction
				clock.boost_active = false
				clock.boost_remaining = 0.0
				phase = Phase.SUNSET
				_event("sunset")
			else:
				sim.tick(delta)
				if not finished and sim.is_finished():
					_finish()
				if morning_timer >= 0.0:
					morning_timer += delta
					if morning_timer >= MORNING_DELAY:
						morning_timer = -1.0
						write_morning_line()
						_event("morning")
				# Also when one nutrient is gone while the others still carry growth (QA round 3: the
				# player was never told which one was missing).
				if not _spent_announced and (sim.nutrients_spent() or sim.nutrient_missing()):
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
	roots.run_totals = PackedFloat32Array([0, 0, 0, 0])
	# Less than a metre of root is no run: a quiet night instead of a free fine-root harvest.
	night_empty = sim.resources.life_force < roots.cost_per_metre(Vector3.DOWN)
	var roots_full := not roots.can_start_run()
	_event("dive")
	if roots_full:
		night_empty = true
		diary.add(day_number(), "Night %d: the roots fill the soil now; I only looked around below." % (day_number() + 1))
		_event("night_empty")
	elif night_empty:
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


## The player ends tonight's root early; the rest of the life force feeds fine roots.
func finish_run_early() -> void:
	if roots.run_active:
		roots.finish_early(ground, sim.resources)
		_on_run_done()


## The view grew the root itself (RootView drives RootSystem directly): record the end of the run.
## Time away from the game grows the tree a little. In the middle of a night's root the life
## force stays as it was, or the root would run on with what the leaves gathered meanwhile.
## Visitors that are due by the tree's new size come meanwhile and are named on the away page.
func apply_offline(seconds: float) -> void:
	var life := sim.resources.life_force
	var height_before := sim.height()
	var nodes_before := sim.living_nodes()
	sim.apply_offline(seconds)
	if bonsai != null:
		bonsai.apply_offline(seconds)
	if phase == Phase.NIGHT and roots.run_active:
		sim.resources.life_force = life
	if seconds < AWAY_REPORT_SECONDS:
		return
	var came: Array[String] = Visitors.arrive(self)
	# Two absences before the page was read add up.
	var before: Dictionary = away_report
	var visitors: Array = before.get("visitors", []).duplicate()
	visitors.append_array(came)
	away_report = {
		"seconds": seconds + float(before.get("seconds", 0.0)),
		"grown": maxf(sim.height() - height_before, 0.0) + float(before.get("grown", 0.0)),
		"height": sim.height(),
		"segments": maxi(sim.living_nodes() - nodes_before, 0) + int(before.get("segments", 0)),
		"visitors": visitors,
	}


## The "while you were away" report, once: empty afterwards.
func take_away_report() -> Dictionary:
	var r := away_report
	away_report = {}
	return r


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
	if roots.leftover_spent > 1.0 and roots.count_flagged("fine", roots.main_root_count - 1) > 0:
		diary.add(day_number(), "The rest of the night's life force (%.0f) went into fine roots around it." % roots.leftover_spent)
	_event("run_done")


## Grown to the species' full size: into the grove; the seed bag offers the next seed.
func _finish() -> void:
	finished = true
	grove.append({"species": sim.species.id, "days": day_number(), "seed": seed})
	diary.add(day_number(), "The %s has grown to its full size. It dropped a seed; the seed bag in the shed is ready for the next tree." % tree_name())
	_event("finished")
	# The first finished tree opens bonsai mode: a juniper waits on the shed's windowsill.
	if bonsai == null:
		ensure_bonsai()
		diary.add(day_number(), "A young juniper stands on the windowsill in the shed now, a bonsai to shape for as long as I like.")
		_event("bonsai")
	else:
		diary.add(day_number(), "I took a cutting of the %s for the windowsill." % tree_name())


func _sunrise() -> void:
	phase = Phase.DAY
	sim.clock.boost_active = false
	sim.clock.boost_remaining = 0.0
	night_done = false
	night_empty = false
	_spent_announced = false
	var was_seed := sim.graph.size() <= 2
	ground.regrow(REGROW_SHARE, day_number())
	# The old roots drank from the deposits they reach all night.
	var drawn := roots.drink_tapped(ground, sim.resources)
	var total_drawn := drawn[0] + drawn[1] + drawn[2] + drawn[3]
	if total_drawn > 0.5:
		diary.add(day_number(), "The old roots drew water %.1f, nitrogen %.1f, phosphorus %.1f, potassium %.1f from the soil overnight." % [drawn[0], drawn[1], drawn[2], drawn[3]])
	# Species quirks on the night: root nodules (alder), the leaves' water, shaded twigs.
	var fixed := roots.nodule_nitrogen(sim.resources)
	if fixed > 0.5:
		diary.add(day_number(), "The root nodules made nitrogen %.1f overnight." % fixed)
	if not was_seed:
		sim.drink_upkeep()
		var died := sim.shade_dieback(day_number())
		if died > 0:
			diary.add(day_number(), "%d shaded twig%s died back in the crown." % [died, "" if died == 1 else "s"])
	if sim.species.in_blossom(day_number()) and not sim.species.in_blossom(day_number() - 1):
		diary.add(day_number(), "The %s is in blossom. The bees have come, and the leaves are busier than ever." % tree_name())
		_event("blossom")
	sim.start_dawn_burst()
	if was_seed and not sim.nutrients_spent():
		diary.add(day_number(), "The seed sprouted at dawn.")
	diary.wish = Diary.make_wish(ground, day_number(), seed)
	morning_timer = 0.0
	_event("sunrise")


## Called by the tree view at the end of the dawn burst, for the morning diary line.
func write_morning_line() -> void:
	var tips := sim.tip_count()
	diary.add(day_number(), "The %s is %.1f m tall with %d leaf cluster%s." % [tree_name(), sim.height(), tips, "" if tips == 1 else "s"])


# --- moving the day on ------------------------------------------------------

## Once nutrients are spent, the player may drag the sun along its arc to move time on.
## Tap: the sun shines brighter for one more game hour (up to three hours ahead).
func boost_hour() -> void:
	if phase != Phase.DAY:
		return
	var clk := sim.clock
	clk.boost_remaining = minf(clk.boost_remaining + clk.hour_seconds(), clk.hour_seconds() * 3.0)
	clk.boost_active = true


## The sun can be moved on at any time of the day (Simon, play test 2026-09-27): wait for the
## afternoon, then boost to steer the crown west, without waiting in real time.
func can_skip_time() -> bool:
	return phase == Phase.DAY


## Nothing left to grow with today: the arc glows and the hint says so.
func day_is_spent() -> bool:
	return phase == Phase.DAY and sim.nutrients_spent()


## Moves the day forward by `fraction` of a whole day (never backward, never past sunset).
## The skipped time is simulated in small steps, so the tree grows (calmly) and the leaves
## gather life force as if the player had waited.
func skip_time(fraction: float) -> void:
	if not can_skip_time() or fraction <= 0.0:
		return
	var clock := sim.clock
	var to_sunset := clock.daylight_fraction - clock.time_of_day
	var seconds := minf(fraction, to_sunset) * clock.seconds_per_day
	if fraction >= to_sunset:
		seconds += 0.01  # land on the sunset itself, not a hair before it
	# A boost already bought waits for after the skip (test loop: dragging the sun wiped it).
	var boost_left := sim.clock.boost_remaining
	sim.clock.boost_remaining = 0.0
	sim.clock.boost_active = false
	# The dawn burst (the night's growth) always plays out first.
	while sim.dawn_burst_active() and seconds > 0.0 and phase == Phase.DAY:
		tick(0.5)
		seconds -= 0.5
	# The skipped hours move the sun and fill the life force, but the tree waits: the night's
	# nutrients stay for the hour the player picked (Simon: wait for the afternoon, then boost).
	sim.growth_paused = true
	while seconds > 0.0 and phase == Phase.DAY:
		var step := minf(seconds, 2.0)
		tick(step)
		seconds -= step
	sim.growth_paused = false
	if phase == Phase.DAY:
		sim.clock.boost_remaining = boost_left
	sim.repace_rest_of_day()


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
		"pending_pages": pending_pages,
		"empty_timer": _empty_timer,
		"morning_timer": morning_timer,
		"spent_announced": _spent_announced,
		"finished": finished,
		"grove": grove,
		"bonsai": bonsai.to_dict() if bonsai != null else null,
		"bonsai_resting": bonsai_resting,
	}


static func from_dict(d_in: Dictionary) -> GameState:
	var d := d_in.duplicate(true)
	var g := GameState.new()
	g.seed = int(d.get("seed", 1))
	g.sim = GrowthSim.from_dict(SaveData.restore_sim(d["sim"]))
	g.ground = Underground.from_dict(d.get("underground", {"seed": g.seed}))
	g.roots = RootSystem.from_dict(d.get("roots", {}), g.seed)
	g.roots.species = g.sim.species
	g.diary = Diary.from_dict(d.get("diary", {}))
	g.phase = clampi(int(d.get("phase", Phase.DAY)), Phase.DAY, Phase.NIGHT) as Phase
	g._empty_timer = float(d.get("empty_timer", 0.0))
	g.morning_timer = float(d.get("morning_timer", -1.0))
	g._spent_announced = bool(d.get("spent_announced", false))
	g.run_used = bool(d.get("run_used", false))
	g.night_done = bool(d.get("night_done", false))
	g.night_empty = bool(d.get("night_empty", false))
	g.pending_pages = Array(d.get("pending_pages", []))
	g.finished = bool(d.get("finished", false))
	for t in d.get("grove", []):
		if t is Dictionary:
			g.grove.append({"species": str(t.get("species", "linden")), "days": int(t.get("days", 0)), "seed": int(t.get("seed", 0))})
	for k in d.get("seen_pages", []):
		g.seen_pages[str(k)] = true
	if d.get("bonsai") is Dictionary:
		g.bonsai = BonsaiSim.from_dict(d["bonsai"])
	if d.get("bonsai_resting") is Dictionary:
		for sid in d["bonsai_resting"]:
			if d["bonsai_resting"][sid] is Dictionary:
				g.bonsai_resting[str(sid)] = d["bonsai_resting"][sid]
	return g
