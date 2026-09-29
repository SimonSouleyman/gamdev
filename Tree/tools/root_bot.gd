class_name RootBot
extends RefCounted
## A simple autopilot for root runs, for tests and screenshot tools: it steers like a player
## chasing the glow. It aims at the nearest deposit no root has drunk from yet (it skips
## roots.tapped and what tonight's root already touched) that lies ahead of the tip, so it never
## needs a U-turn; with `res` given it prefers the resource the tree is shortest of.

var target: Vector3 = Vector3.ZERO
var target_id: int = -1
## pick_start looks for the resource the tree is shortest of (false: any rich patch).
var start_by_need: bool = true
## A deposit counts as reachable ahead while the heading points at it at least this much.
const AHEAD: float = -0.05
## Closer than this, a deposit off to the side needs a loop: only ones well ahead count.
const NEAR: float = 1.2
const NEAR_AHEAD: float = 0.45


func stick_for(roots: RootSystem, ground: Underground, res: Resources = null) -> Vector2:
	var tip := roots.tip_position
	var weights := _kind_weights(roots, res)
	var short := _shortest_kind(roots, res)
	var best := -1
	var best_score := INF
	for id in ground.dots_near(tip + roots.heading * 1.5, 4.0):
		if not roots.is_fresh(id):
			continue
		var to := ground.dot_positions[id] - tip
		var d := to.length()
		if d < 1e-3:
			continue
		var ahead := roots.heading.dot(to / d)
		if ahead < AHEAD or (d < NEAR and ahead < NEAR_AHEAD):
			continue
		# Distance along the curve: a deposit to the side costs a longer turn.
		var score := d * (1.0 + 0.8 * (1.0 - ahead))
		score *= weights[ground.dot_kinds[id]]
		score /= sqrt(maxf(ground.fullness(id), 0.1))
		if score < best_score:
			best_score = score
			best = id
	target_id = best
	if best >= 0:
		target = ground.dot_positions[best]
	elif tip.distance_to(target) < 0.8 or target == Vector3.ZERO or (target - tip).normalized().dot(roots.heading) < -0.3:
		# Nothing close: head for the nearest rich patch with fresh deposits left.
		var c := _best_patch(roots, ground, short, func(p: Vector3) -> float: return p.distance_to(tip) if p.distance_to(tip) > 0.5 else INF)
		if c != Vector3.INF:
			target = c
	var want := (target - tip).normalized()
	var h := roots.heading
	var a := Vector2(h.x, h.z).angle_to(Vector2(want.x, want.z))
	var pitch := asin(clampf(want.y, -1, 1)) - asin(clampf(h.y, -1, 1))
	# Gentle near a deposit: the root's magnetism does the last bit.
	var gain := 1.5 if best >= 0 and tip.distance_to(target) < roots.magnet_radius else 2.5
	# Vector2.angle_to on (x, z) is clockwise seen from above; a positive stick turns right.
	return Vector2(clampf(a * gain, -1, 1), clampf(pitch * gain, -1, 1))


## Where tonight's root should start, like a player reading the meadow: the root node closest to a
## rich patch that still has fresh deposits, of the resource the tree is shortest of if `res` is given.
func pick_start(roots: RootSystem, ground: Underground, res: Resources = null) -> int:
	var g := roots.graph
	if g.size() <= 1:
		return 0
	var short := _shortest_kind(roots, res) if start_by_need else -1
	var best_node := g.size() - 1
	var best := INF
	# Every few nodes is enough to find a good starting point.
	var stride := maxi(1, g.size() / 400)
	var nearest := func(p: Vector3) -> float:
		var d := INF
		for id in range(0, g.size(), stride):
			d = minf(d, g.positions[id].distance_squared_to(p))
		return sqrt(d)
	var c := _best_patch(roots, ground, short, nearest)
	if c == Vector3.INF:
		return best_node
	for id in range(0, g.size(), stride):
		var d := g.positions[id].distance_to(c)
		if d < best:
			best = d
			best_node = id
	return best_node


## Centre of the patch with at least three fresh deposits that scores lowest by `dist`
## (halved for the `short` kind); Vector3.INF if none.
func _best_patch(roots: RootSystem, ground: Underground, short: int, dist: Callable) -> Vector3:
	var out := Vector3.INF
	var best := INF
	for patch in ground.patches:
		var c: Vector3 = patch["center"]
		var fresh := 0
		for id in ground.dots_near(c, float(patch["radius"])):
			if roots.is_fresh(id):
				fresh += 1
		if fresh < 3:
			continue
		var d: float = dist.call(c)
		if int(patch["kind"]) == short:
			d *= 0.5
		if d < best:
			best = d
			out = c
	return out


## Score factor per resource: what the tree runs short of looks closer (0.4 when its stock is
## gone, 1 with plenty); without `res`, water looks a little closer.
func _kind_weights(roots: RootSystem, res: Resources) -> PackedFloat32Array:
	if res == null:
		return PackedFloat32Array([0.75, 1.0, 1.0, 1.0])
	var out := PackedFloat32Array([1.0, 1.0, 1.0, 1.0])
	for k in range(4):
		var need: float = roots.species.needs[k]
		if need > 0.0:
			out[k] = 0.4 + 0.6 * clampf(res.stock[k] / (need * 15.0), 0.0, 1.0)
	return out


## The resource the tree has least of for its needs (stock / need), or -1 without `res`.
func _shortest_kind(roots: RootSystem, res: Resources) -> int:
	if res == null:
		return -1
	var out := -1
	var lowest := INF
	for k in range(4):
		var need: float = roots.species.needs[k]
		if need <= 0.0:
			continue
		var ratio := res.stock[k] / need
		if ratio < lowest:
			lowest = ratio
			out = k
	return out
