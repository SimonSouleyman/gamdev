class_name Haptics
extends RefCounted
## A small buzz in the hand for the moments that matter (design doc section 17, 0.6): a cut with
## the shears, the dive into the ground at sunset and a finished tree. Off with the pinboard
## switch "vibration". Plain Input.vibrate_handheld: the phone's own vibrator, no services.

## Milliseconds and strength (0 to 1) per moment: a short snip, a soft thud, a long glad hum.
const PATTERNS := {
	"cut": [35, 0.6],
	"dive": [90, 0.45],
	"finished": [320, 0.7],
}

static var enabled: bool = true


## Buzzes for `kind`; returns false when switched off or the kind is unknown.
static func buzz(kind: String) -> bool:
	if not enabled or not PATTERNS.has(kind):
		return false
	var p: Array = PATTERNS[kind]
	if OS.has_feature("mobile"):
		Input.vibrate_handheld(int(p[0]), float(p[1]))
	return true
