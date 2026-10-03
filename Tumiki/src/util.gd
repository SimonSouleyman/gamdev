## Small helpers ported from abagames.util (CSV tokenizer, Rand, vector math).
class_name U

const DATA_DIR := "res://data/"


## Reads a data file and returns all comma separated, non-empty tokens.
static func read_csv(path: String) -> PackedStringArray:
	var result := PackedStringArray()
	var f := FileAccess.open(DATA_DIR + path, FileAccess.READ)
	if f == null:
		push_error("File not found: " + path)
		return result
	while not f.eof_reached():
		var line := f.get_line()
		for s in line.split(","):
			var r := s.strip_edges()
			if r.length() > 0:
				result.append(r)
	return result


class Iter:
	var data: PackedStringArray
	var idx := 0

	func _init(d: PackedStringArray) -> void:
		data = d

	func has_next() -> bool:
		return idx < data.size()

	func next() -> String:
		idx += 1
		return data[idx - 1]

	func next_f() -> float:
		return next().to_float()

	func next_i() -> int:
		return next().to_int()


class Rand:
	var rng := RandomNumberGenerator.new()

	func _init() -> void:
		rng.randomize()

	func set_seed(s: int) -> void:
		rng.seed = s

	func next_int(n: int) -> int:
		if n <= 0:
			return 0
		return rng.randi() % n

	func next_float(n: float) -> float:
		return rng.randf() * n

	func next_signed_float(n: float) -> float:
		return rng.randf() * n * 2 - n


## Vector.checkSide from the original code.
static func check_side(p: Vector2, p1: Vector2, p2: Vector2) -> float:
	var xo := p2.x - p1.x
	var yo := p2.y - p1.y
	if xo == 0:
		if yo == 0:
			return 0.0
		if yo > 0:
			return p.x - p1.x
		return p1.x - p.x
	elif yo == 0:
		if xo > 0:
			return p1.y - p.y
		return p.y - p1.y
	if xo * yo > 0:
		return (p.x - p1.x) / xo - (p.y - p1.y) / yo
	return -(p.x - p1.x) / xo + (p.y - p1.y) / yo


## Hit test against a quad given by 4 corner points (used by stuck enemies and splinters).
static func in_quad(p: Vector2, c: Array) -> bool:
	return check_side(p, c[0], c[1]) * check_side(p, c[3], c[2]) < 0 \
		and check_side(p, c[1], c[2]) * check_side(p, c[0], c[3]) < 0


## Vector.dist from the original code (octagonal distance approximation).
static func dist(a: Vector2, b: Vector2) -> float:
	var ax := absf(a.x - b.x)
	var ay := absf(a.y - b.y)
	if ax > ay:
		return ax + ay / 2
	return ay + ax / 2


static func rtod(a: float) -> float:
	return a * 180.0 / PI


static func dtor(a: float) -> float:
	return a * PI / 180.0


## Generic actor pool with the same allocation order as the original ActorPool.
class Pool:
	var actor: Array = []
	var idx := 0
	## Actors that existed at the last refresh_active(), plus the ones handed out since then
	## when `track` is set. Hit checks loop over this short list instead of the whole pool;
	## callers still check `exists`. Tracked pools must be refreshed every frame.
	var active: Array = []
	var track := false

	func _init(n: int, maker: Callable) -> void:
		for i in n:
			var a = maker.call()
			a.exists = false
			actor.append(a)
		idx = n

	func get_instance():
		for i in actor.size():
			idx -= 1
			if idx < 0:
				idx = actor.size() - 1
			if not actor[idx].exists:
				if track and not active.has(actor[idx]):
					active.append(actor[idx])
				return actor[idx]
		return null

	func get_instance_forced():
		idx -= 1
		if idx < 0:
			idx = actor.size() - 1
		if track and not active.has(actor[idx]):
			active.append(actor[idx])
		return actor[idx]

	func move() -> void:
		for a in actor:
			if a.exists:
				a.move()

	func draw() -> void:
		for a in actor:
			if a.exists:
				a.draw()

	func clear() -> void:
		for a in actor:
			a.exists = false
		active.clear()

	func refresh_active() -> void:
		active.clear()
		for a in actor:
			if a.exists:
				active.append(a)
