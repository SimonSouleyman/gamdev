class_name ShedLayouts
extends RefCounted
## Proposals for the shed's layout (0.8.2.4, Simon: "the shed view I don't like yet: ten pictures
## with different places for the door, the pinboard, the bonsai and the things"). Each is a set of
## overrides for Shed's defaults (layout 0, the 0.8.2.2 view); Shed.layout picks one
## (`--shed-layout=N`). Shed frame: the door's wall is +Z, the eye faces it from -Z by default,
## +X is the screen's left then. Keys (all optional):
##   door_x, door_w, door_rot (the leaf's hinge turn), door_hinge (+1: hinge on the +X post)
##   win_wall (0 front, 1 left +X, 2 right -X, 3 back), win_u (along the wall as seen from inside,
##     left to right is +u... in the window's own frame, see Shed._wall_basis), win_y
##   pin_at, pin_rot, pin_scale, pin_on_door (pin_at then in the door leaf's frame)
##   bench (false: none), bench_at, bench_rot, bench_scale
##   things: name -> [position, turn] (on the bench's top in its frame, or the room's with no bench)
##   tags: name -> [offset from the thing, below]
##   shelves: [[centre of the board's top, size]]
##   eye, yaw (radians, PI faces the door), pitch (degrees, preferred), yaw_range (radians)
##   lamp, pendant (a hanging enamel lamp you see), rug ([position, turn] or null), can ([position, turn] or null)
##   caption: the German line for Simon's sheet.

const COUNT := 10


static func config(i: int) -> Dictionary:
	match i:
		1:
			# Door in the middle, the pinboard left of it, the sill right, the bench low and
			# square to the eye, its four things in a tidy two by two.
			return {
				"caption": "Tür mittig, Pinnwand links, Fensterbank rechts, Werkbank unten",
				"door_x": 0.0, "door_w": 0.72,
				"win_wall": 0, "win_u": Vector2(-0.82, -0.52), "win_y": Vector2(1.18, 1.7),
				"pin_at": Vector3(0.66, 1.42, 1.34), "pin_scale": 0.78,
				"bench_at": Vector3(0.0, 0.05, 0.2),
				"things": {
					"journal": [Vector3(0.2, 0.0, -0.1), 0.08], "gloves": [Vector3(-0.2, 0.0, -0.12), 1.5],
					"album": [Vector3(0.18, 0.0, 0.14), -0.04], "seeds": [Vector3(-0.22, 0.0, 0.1), 0.2]},
				"tags": {"journal": [Vector3(0, 0, -0.12), true], "gloves": [Vector3(0, 0, -0.08), true],
					"album": [Vector3(0, 0.07, 0.1), false], "seeds": [Vector3(0, 0.2, 0.0), false]},
				"eye": Vector3(0.0, 1.66, -1.3), "pitch": -22.0,
				"lamp": Vector3(0.0, 2.12, 0.5),
			}
		2:
			# A corner of the shed seen across the bench, which stands turned toward the eye:
			# the door on the front wall, the window and the pinboard on the side wall.
			return {
				"caption": "Eckblick: Werkbank schräg davor, Tür rechts, Fenster und Pinnwand an der linken Wand",
				"door_x": 0.82, "door_w": 0.64,
				"win_wall": 1, "win_u": Vector2(-0.9, -0.62), "win_y": Vector2(1.2, 1.72),
				"pin_at": Vector3(1.54, 1.5, 0.08), "pin_rot": -PI * 0.5, "pin_scale": 0.72,
				"bench_at": Vector3(0.05, 0.05, -0.1), "bench_rot": 0.82,
				"eye": Vector3(-1.2, 1.72, -1.22), "yaw": -2.33, "pitch": -24.0, "yaw_range": 0.3,
				"lamp": Vector3(1.0, 2.12, 0.9),
				"rug": [Vector3(-0.5, 0.0, -0.75), 0.82], "can": [Vector3(1.3, 0.05, 1.12), 2.0],
			}
		3:
			# The bench seen from above at an angle, close; the wall with the door, the pinboard
			# and the window behind it, small at the picture's top.
			return {
				"caption": "Werkbank schräg von oben, groß; Tür, Pinnwand, Fenster an der Wand dahinter",
				"door_x": -0.4, "door_w": 0.66,
				"win_wall": 0, "win_u": Vector2(0.2, 0.48), "win_y": Vector2(1.18, 1.62),
				"pin_at": Vector3(0.34, 1.98, 1.34), "pin_scale": 0.62,
				"bench_at": Vector3(0.0, 0.05, 0.3),
				"things": {
					"journal": [Vector3(0.24, 0.0, -0.08), 0.15], "gloves": [Vector3(-0.08, 0.0, -0.15), 1.3],
					"album": [Vector3(0.04, 0.0, 0.16), -0.06], "seeds": [Vector3(-0.3, 0.0, 0.08), -0.3]},
				"eye": Vector3(-0.05, 2.15, -0.8), "pitch": -40.0, "vmid": -0.12,
				"lamp": Vector3(0.6, 2.1, 0.7),
				"rug": null,
			}
		4:
			# Stacked on one wall: the bench against the front wall, the window with the sill right
			# above it, the pinboard above that; the door beside.
			return {
				"caption": "Alles an einer Wand gestapelt: Werkbank unten, Fensterbank darüber, Pinnwand oben, Tür links",
				"door_x": 0.52, "door_w": 0.66,
				"win_wall": 0, "win_u": Vector2(-0.58, -0.26), "win_y": Vector2(1.26, 1.72),
				"pin_at": Vector3(-0.42, 2.03, 1.34), "pin_scale": 0.58,
				"bench_at": Vector3(-0.36, 0.05, 1.07), "bench_scale": Vector3(0.88, 1.0, 1.0),
				"things": {
					"journal": [Vector3(0.36, 0.0, -0.17), 0.12], "album": [Vector3(0.08, 0.0, -0.18), -0.05],
					"gloves": [Vector3(-0.16, 0.0, -0.21), 1.5], "seeds": [Vector3(-0.38, 0.0, -0.12), -0.3]},
				"tags": {"journal": [Vector3(0, 0, -0.12), true], "album": [Vector3(0, 0.07, 0.0), false],
					"gloves": [Vector3(0, 0, -0.08), true], "seeds": [Vector3(0, 0.19, 0.0), false]},
				"bonsai_tag": [Vector3(-0.3, 0.12, -0.1), false], "thing_scale": 1.15, "pick_scale": 1.15,
				"eye": Vector3(-0.1, 1.66, -0.85), "pitch": -20.0, "vmid": 0.0,
				"lamp": Vector3(0.1, 2.12, 0.6),
				"rug": [Vector3(-0.1, 0.0, 0.25), 0.03], "can": null,
			}
		5:
			# No bench: one column on the wall beside the door, the pinboard at the top, the window
			# with the sill in the middle, a low open shelf unit with the things under it.
			return {
				"caption": "Regal statt Werkbank: Pinnwand oben, Fensterbank Mitte, Dinge im Regal unten, Tür rechts",
				"door_x": -0.42, "door_w": 0.64,
				"win_wall": 0, "win_u": Vector2(0.27, 0.57), "win_y": Vector2(1.24, 1.72),
				"pin_at": Vector3(0.42, 2.05, 1.34), "pin_scale": 0.6,
				"bench": false, "thing_scale": 1.35,
				"shelf_unit": [Vector3(0.42, 0.1, 1.2), Vector3(0.7, 0.85, 0.34), [0.4]],
				"things": {
					"journal": [Vector3(0.6, 0.95, 1.13), 0.1], "album": [Vector3(0.3, 0.95, 1.15), -0.05],
					"seeds": [Vector3(0.26, 0.4, 1.12), -0.25], "gloves": [Vector3(0.58, 0.4, 1.08), 1.6]},
				"tags": {"journal": [Vector3(0.0, 0.07, 0.0), false], "album": [Vector3(0.0, 0.08, 0.0), false],
					"seeds": [Vector3(0, 0.0, -0.2), true], "gloves": [Vector3(0, 0, -0.16), true]},
				"bonsai_tag": [Vector3(-0.32, 0.14, -0.1), false],
				"eye": Vector3(-0.05, 1.5, -1.3), "pitch": -14.0, "vmid": 0.0,
				"lamp": Vector3(-0.2, 2.15, 0.8),
				"rug": [Vector3(-0.2, 0.0, 0.5), 0.02], "can": [Vector3(-0.95, 0.05, 1.1), 2.6],
			}
		6:
			# The sill with the bonsai as the hero: a wide, tall window in the middle of the picture
			# instead of the door, the bench right before it, the pinboard left, the door right.
			return {
				"caption": "Fenster als Mitte: großes Fenster mit Bonsai mittig, Werkbank davor, Pinnwand links, Tür rechts",
				"door_x": -0.7, "door_w": 0.58,
				"win_wall": 0, "win_u": Vector2(-0.28, 0.28), "win_y": Vector2(1.12, 1.86),
				"pin_at": Vector3(0.66, 1.52, 1.34), "pin_scale": 0.6,
				"bench_at": Vector3(0.0, 0.05, 0.2), "bench_scale": Vector3(0.8, 1.0, 1.0),
				"things": {
					"journal": [Vector3(0.2, 0.0, -0.1), 0.15], "gloves": [Vector3(-0.06, 0.0, -0.17), 1.45],
					"album": [Vector3(0.12, 0.0, 0.13), -0.05], "seeds": [Vector3(-0.26, 0.0, 0.04), -0.3]},
				"eye": Vector3(0.0, 1.55, -1.3), "pitch": -14.0, "vmid": 0.0,
				"lamp": Vector3(0.0, 2.14, 0.75),
			}
		7:
			# Cosy: wide double doors open to the tree as the picture, nothing before them; the
			# window and the sill left of them, a small bench against the wall right, under the
			# pinboard.
			return {
				"caption": "Offene Doppeltür als Hauptbild, Weg frei; Fensterbank links, kleine Werkbank mit Pinnwand rechts",
				"door_x": 0.14, "door_w": 0.8, "double_door": true,
				"win_wall": 0, "win_u": Vector2(0.7, 0.94), "win_y": Vector2(1.2, 1.68),
				"pin_at": Vector3(-0.8, 1.6, 1.34), "pin_scale": 0.56,
				"bench_at": Vector3(-0.82, 0.05, 1.04), "bench_scale": Vector3(0.6, 1.0, 1.0),
				"thing_scale": 1.3, "pick_scale": 1.35,
				"things": {
					"journal": [Vector3(0.15, 0.0, -0.1), 0.15], "gloves": [Vector3(-0.12, 0.0, -0.2), 1.45],
					"album": [Vector3(0.06, 0.0, 0.1), -0.05], "seeds": [Vector3(-0.22, 0.0, 0.06), -0.3]},
				"tags": {"journal": [Vector3(0, 0, -0.14), true], "gloves": [Vector3(0, 0, -0.1), true],
					"album": [Vector3(0, 0.09, 0.06), false], "seeds": [Vector3(0, 0.25, 0.0), false]},
				"eye": Vector3(0.0, 1.45, -1.35), "pitch": -6.0, "vmid": 0.04,
				"lamp": Vector3(0.2, 2.14, 0.6),
				"rug": [Vector3(0.1, 0.0, 0.55), 0.02], "can": [Vector3(0.75, 0.05, 1.1), 2.4],
			}
		8:
			# The pinboard hangs on the inside of the door, which stands open into the room; the
			# window and the sill beside the doorway, the bench low.
			return {
				"caption": "Pinnwand an der offenen Tür, Fensterbank rechts, Werkbank unten",
				"door_x": 0.18, "door_w": 0.78, "door_hinge": 1.0, "door_rot": -0.86,
				"win_wall": 0, "win_u": Vector2(-0.8, -0.5), "win_y": Vector2(1.18, 1.7),
				"pin_on_door": true, "pin_at": Vector3(-0.4, 1.38, -0.045), "pin_scale": 0.74,
				"bench_at": Vector3(0.0, 0.05, -0.05),
				"things": {
					"journal": [Vector3(0.2, 0.0, -0.1), 0.15], "gloves": [Vector3(-0.08, 0.0, -0.16), 1.4],
					"album": [Vector3(0.02, 0.0, 0.13), -0.08], "seeds": [Vector3(-0.28, 0.0, 0.0), -0.3]},
				"eye": Vector3(-0.05, 1.64, -1.3), "pitch": -20.0,
				"lamp": Vector3(-0.3, 2.06, -0.6),
			}
		9:
			# A hanging enamel lamp at the top, everything near eye height: one long board under the
			# pinboard with the books standing on it, covers to the room; the sill beside it.
			return {
				"caption": "Hängelampe oben, alles auf Augenhöhe: Wandbrett mit stehenden Büchern unter der Pinnwand",
				"door_x": -0.54, "door_w": 0.56,
				"win_wall": 0, "win_u": Vector2(0.5, 0.74), "win_y": Vector2(1.16, 1.64),
				"pin_at": Vector3(0.06, 1.8, 1.34), "pin_scale": 0.6,
				"bench": false, "thing_scale": 1.3, "pick_scale": 1.12,
				"shelves": [[Vector3(0.04, 1.16, 1.22), Vector3(0.56, 0.035, 0.34)]],
				"things": {
					"journal": [Vector3(0.2, 1.16 + 0.14, 1.27), 0.0, -1.38], "album": [Vector3(-0.04, 1.16 + 0.15, 1.28), 0.0, -1.4],
					"seeds": [Vector3(-0.17, 1.16, 1.15), -0.25], "gloves": [Vector3(0.12, 1.16, 1.11), 1.5]},
				"tags": {"journal": [Vector3(0.0, 0.2, 0.0), false], "album": [Vector3(0, 0.22, 0.0), false],
					"seeds": [Vector3(0, 0.0, -0.17), true], "gloves": [Vector3(0, 0, -0.12), true]},
				"eye": Vector3(0.0, 1.55, -1.2), "pitch": -4.0, "vmid": 0.06,
				"lamp": Vector3(0.0, 1.98, 0.45), "pendant": true,
				"rug": null, "can": [Vector3(-0.2, 0.05, 1.0), 2.4],
				"props": [["planter_pot_clay", Vector3(0.12, 0.1, 1.08), 1.0, 0.1], ["planter_pot_clay", Vector3(0.36, 0.1, 1.12), 0.8, 1.2], ["trowel_01", Vector3(-0.02, 0.12, 0.98), 0.75, 0.4]],
			}
		10:
			# A stable door: its lower half shut, a broad ledge on it with the things, the upper
			# half open to the tree; the pinboard and the window either side.
			return {
				"caption": "Stalltür: untere Hälfte zu, Dinge auf ihrem Brett, oben offen zum Baum; Pinnwand links, Fenster rechts",
				"door_x": 0.0, "door_w": 0.86, "dutch": true, "pick_scale": 1.2,
				"win_wall": 0, "win_u": Vector2(-0.84, -0.6), "win_y": Vector2(1.16, 1.64),
				"pin_at": Vector3(0.68, 1.52, 1.34), "pin_scale": 0.58,
				"bench": false, "thing_scale": 1.3,
				"things": {
					"journal": [Vector3(0.24, 1.0, 1.31), 0.12], "album": [Vector3(-0.04, 1.0, 1.33), -0.05],
					"seeds": [Vector3(-0.3, 1.0, 1.3), -0.3], "gloves": [Vector3(0.06, 1.0, 1.2), 1.55]},
				"tags": {"journal": [Vector3(0.0, 0.0, -0.17), true], "album": [Vector3(0, 0.09, 0.0), false],
					"seeds": [Vector3(0, 0.24, 0.0), false], "gloves": [Vector3(0, 0, -0.12), true]},
				"eye": Vector3(0.0, 1.62, -0.85), "pitch": -12.0, "vmid": 0.0,
				"lamp": Vector3(0.0, 2.14, 0.75),
				"rug": [Vector3(0.0, 0.0, 0.6), 0.02], "can": [Vector3(0.7, 0.05, 1.1), 2.4],
			}
	return {}
