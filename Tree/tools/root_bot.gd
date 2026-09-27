class_name RootBot
extends RefCounted
## A simple autopilot for root runs, for tests and screenshot tools: it steers toward the
## nearest uncollected dot (preferring water), like a player chasing the glow.

var target: Vector3 = Vector3.ZERO


func stick_for(roots: RootSystem, ground: Underground) -> Vector2:
	var tip := roots.tip_position
	var best := -1
	var best_score := INF
	for id in ground.dots_near(tip + roots.heading * 1.5, 4.0):
		var score := ground.dot_positions[id].distance_to(tip) * (0.6 if ground.dot_kinds[id] == Resources.Kind.WATER else 1.0)
		if score < best_score:
			best_score = score
			best = id
	if best >= 0:
		target = ground.dot_positions[best]
	elif tip.distance_to(target) < 0.8 or target == Vector3.ZERO:
		# Nothing close: head for the nearest rich patch with dots left.
		var far := INF
		for patch in ground.patches:
			var c: Vector3 = patch["center"]
			if ground.dots_near(c, float(patch["radius"])).size() < 3:
				continue
			var d := c.distance_to(tip)
			if d < far and d > 0.5:
				far = d
				target = c
	var want := (target - tip).normalized()
	var h := roots.heading
	var a := Vector2(h.x, h.z).angle_to(Vector2(want.x, want.z))
	var pitch := asin(clampf(want.y, -1, 1)) - asin(clampf(h.y, -1, 1))
	# Vector2.angle_to on (x, z) is clockwise seen from above; a positive stick turns right.
	return Vector2(clampf(a * 2.0, -1, 1), clampf(pitch * 2.0, -1, 1))
