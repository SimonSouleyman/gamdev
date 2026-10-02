class_name FieldRoutes
extends RefCounted
## 0.8.2 (specs/root-field-extras.md 2, broken 3 and 4): checks on the wider field's route choice,
## for the tests and tools/qa_bands.gd. Pure data. A route is a path through the topsoil (a grid
## of CELL metres at ROUTE_DEPTH, keeping CLEARANCE from rock) from the trunk to a patch's edge.

const CELL: float = 0.5
const ROUTE_DEPTH: float = 1.0
const CLEARANCE: float = 0.25
## A second route keeps this far from the first one (outside the trunk's and the patch's ends).
const APART: float = 1.5

static var _cache: Dictionary = {}


## The straight line from the trunk to the patch (at its depth, up to its edge) passes a band.
static func straight_blocked(u: Underground, pid: int) -> bool:
	return u.straight_blocked(pid)


## How many clearly different routes lead from the trunk to the patch: 0, 1 or 2 (a second one
## at least APART from the shortest everywhere between the trunk and the patch).
static func routes(u: Underground, pid: int) -> int:
	var grid := _grid(u)
	var c: Vector3 = u.patches[pid]["center"]
	var goal := Vector2(c.x, c.z)
	var reach := float(u.patches[pid]["radius"])
	var first := _path(u, grid, goal, reach, {})
	if first.is_empty():
		return 0
	var shut := {}
	var n := _side(u)
	var k := ceili(APART / CELL)
	for cell in first:
		var at := _centre(u, cell)
		if at.length() < 2.5 or at.distance_to(goal) < reach + 1.0:
			continue
		for dx in range(-k, k + 1):
			for dy in range(-k, k + 1):
				if Vector2(dx, dy).length() * CELL <= APART:
					var q: Vector2i = cell + Vector2i(dx, dy)
					if q.x >= 0 and q.y >= 0 and q.x < n and q.y < n:
						shut[q] = true
	return 1 if _path(u, grid, goal, reach, shut).is_empty() else 2


## Life force of a straight root from the trunk to the patch's edge, and of the route through a
## soft vein (to its start, along it, then on to the patch); INF when blocked.
static func straight_cost(u: Underground, sys: RootSystem, pid: int) -> float:
	var c: Vector3 = u.patches[pid]["center"]
	var goal := c - Vector3(c.x, 0.0, c.z).normalized() * float(u.patches[pid]["radius"])
	return Diary.line_cost(u, sys, Vector3(0.0, c.y, 0.0), goal)


static func vein_route_cost(u: Underground, sys: RootSystem, pid: int, vein: int) -> float:
	var c: Vector3 = u.patches[pid]["center"]
	var pts: PackedVector3Array = u.veins[vein]["points"]
	var total := Diary.line_cost(u, sys, Vector3(0.0, pts[0].y, 0.0), pts[0])
	for i in range(pts.size() - 1):
		total += Diary.line_cost(u, sys, pts[i], pts[i + 1])
	var goal := c - (c - pts[-1]).normalized() * float(u.patches[pid]["radius"])
	total += Diary.line_cost(u, sys, pts[-1], goal)
	return total


static func _side(u: Underground) -> int:
	return ceili(2.0 * u.extent / CELL)


static func _cell(u: Underground, p: Vector2) -> Vector2i:
	return Vector2i(floori((p.x + u.extent) / CELL), floori((p.y + u.extent) / CELL))


static func _centre(u: Underground, cell: Vector2i) -> Vector2:
	return Vector2((cell.x + 0.5) * CELL - u.extent, (cell.y + 0.5) * CELL - u.extent)


## Open cells (true) of the soil's route grid, cached per soil.
static func _grid(u: Underground) -> PackedByteArray:
	var key := u.get_instance_id()
	if _cache.has(key):
		return _cache[key]
	var n := _side(u)
	var grid := PackedByteArray()
	grid.resize(n * n)
	for y in range(n):
		for x in range(n):
			var at := _centre(u, Vector2i(x, y))
			var open := at.length() <= u.extent - CLEARANCE and not u.is_inside_rock(Vector3(at.x, -ROUTE_DEPTH, at.y), CLEARANCE)
			grid[y * n + x] = 1 if open else 0
	if _cache.size() > 8:
		_cache.clear()
	_cache[key] = grid
	return grid


## The shortest open path (8 neighbours, breadth first) from the trunk to within `reach` of
## `goal`, avoiding `shut`; its cells, or empty.
static func _path(u: Underground, grid: PackedByteArray, goal: Vector2, reach: float, shut: Dictionary) -> Array:
	var n := _side(u)
	var start := _cell(u, Vector2.ZERO)
	var came := {start: start}
	var queue: Array[Vector2i] = [start]
	var head := 0
	var dirs := [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1), Vector2i(1, 1), Vector2i(1, -1), Vector2i(-1, 1), Vector2i(-1, -1)]
	while head < queue.size():
		var cur: Vector2i = queue[head]
		head += 1
		if _centre(u, cur).distance_to(goal) <= reach:
			var out: Array = []
			var back := cur
			while back != start:
				out.append(back)
				back = came[back]
			out.append(start)
			return out
		for d in dirs:
			var q: Vector2i = cur + d
			if q.x < 0 or q.y < 0 or q.x >= n or q.y >= n or came.has(q) or shut.has(q):
				continue
			if grid[q.y * n + q.x] == 0 and _centre(u, q).length() > 1.0:
				continue
			came[q] = cur
			queue.append(q)
	return []
