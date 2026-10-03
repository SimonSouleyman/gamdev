# Spec: hold to fast-forward the day (0.8.2)

Written 2026-09-30 from Simon's note (17:52 UTC): "Im Baummodus soll man den Boost weiterhin mit
einem Tap starten. Wenn man den Bildschirm gedrückt hält, soll die Zeit vorspulen. Also der Tag
soll mit den gesetzten Boosts an den richtigen Zeitpunkten 4x schneller vergehen." A new input,
so it is specced first; it lands in 0.8.2 together with the side roots (Simon, 18:29 UTC: fixes in 0.8.1, new features in 0.8.2) (specs/side-roots.md).

**Purpose.** Let a player who has set the day's boosts watch the rest of the day unfold without
waiting it out, while the tap stays the one decision of the day.

**What the player should feel.** "Let's see how this plays out", like leaning back and watching
clouds race. Never "I must skip to be efficient".

**Input.** In tree mode by day (the normal view, not with the shears out):
- A short tap (under 0.6 s, as today) boosts one game hour, up to three ahead. Unchanged.
- Holding the finger still (no move past the drag threshold, 14 px) for 0.6 s starts the
  fast-forward; it runs while the finger stays down and stops on release. A hold never boosts.
- A finger that moves past the threshold before 0.6 s turns the camera, as today. Once the
  fast-forward runs, moving the finger does not turn the camera (one gesture, one meaning).

**Output.** The day's clock runs at 32x real speed while held (Simon, 2026-10-03, 0.8.2.8: "still too slow, double it"; 4x until 0.8.2.1, 8x until 0.8.2.3, 16x until 0.8.2.7). From 0.8.2.4 a sunset button (0.8.2.5: a walnut hourglass, a real object like the other pictures) runs the rest of the day without holding, until the sunset hold; a tap on the screen stops it. From 0.8.2.8 the hourglass takes about 7 real seconds for whatever is left of the day (Simon: "the day should pass within 7 seconds"; 16x until 0.8.2.6, 32x in 0.8.2.7); the day's moments still show during it but no longer slow it. A day being fast-forwarded takes coarser sim steps (at most 0.5 game s, `main._ff_step`) when the fixed steps do not fit the per-frame sim budget (12 ms), so the speed is the real speed on the phone; a watched day keeps the fixed steps. While holding, at the four day moments (moments-0.8.2.6) the clock drops to 1x for 2 s and eases in again over 0.5 s. Everything that follows game time follows it:
growth, life force, the sun on its arc, boosts already set (they burn off at their game hours, so
the same boosted hours happen at the same times of day, only quicker), weather and visitors. The
look says what is happening without a number: clouds and shadows race, and a small ink double arrow
(0.8.2.5; a small hourglass until then) pulses by the time scrap while it runs.

**What working looks like.**
- A day held from morning to sunset takes a thirty-second of the real time (120 s of daylight:
  about 4 s held, plus the moments' 2 s each); the hourglass ends the rest of the day in about 7 s.
- Holding changes nothing that matters: the same seed and the same taps give the same tree, the
  same life force at sunset and the same night length, held or not, as long as the fixed steps fit
  the frame. From 0.8.2.8 a slow phone takes coarser steps while fast-forwarding, so its tree can
  differ slightly (a few segments, a little life force); it stays a fair, calm day. Accepted:
  Simon wants the day to pass quickly more than he wants exact replays, and the seeded sims run
  with fixed steps.
- The phone keeps its frame rate while holding.

**Interactions.**
- *Boost costs the night:* unaffected. The tank at sunset is set by game time, so a held day buys
  the same night as a watched one.
- *Draggable sun:* dragging the glowing sun (once nothing is left to grow with) still jumps the
  day on; the hold is the gentle version for any time of day. The sun arc takes the drag, the
  rest of the screen takes the hold, so they never collide.
- *Offline growth:* unaffected; it is counted in real days away and has nothing to do with the
  day's clock.
- *Sunset:* the fast-forward stops at the sunset hold. The dive stays the player's own tap or
  swipe; a hold never ends the day or starts the night.
- *Night and root run:* no fast-forward underground; the run is already paced by the tank, and the
  night after the run already passes at 30x.
- *The dev key T* (1x/5x/20x) stays a PC dev tool.

**Soft failure.** Nothing is lost by holding or by never holding.

**Edge cases.** A hold that starts in the last game minutes before sunset simply ends at the
sunset hold. A page or the shed opening mid-hold ends it (the release is treated as having
happened, as today for boosts). Two fingers (pinch zoom) never start it. The first-time moment:
the journal's tap page mentions the hold in one line, no new page (onboarding-check.md).

**Tuning levers.** Speed (hold 32x, hourglass about 7 s for the rest of the day), the coarsest fast step (0.5 game s), the hold time before it starts (0.6 s), whether the speed eases
in over about half a second.

## What "broken" looks like

1. A hold boosts, or a tap starts the fast-forward.
2. The same seed and taps give a clearly different tree, tank or night length when held (more than
   a few segments, or a night more than a few seconds shorter or longer).
3. The fast-forward runs past the sunset hold, starts the dive or runs underground.
4. The phone drops below the frame rate it holds by day without holding (below about 25 fps on
   the Fairphone) or hitches when the hold starts.
5. The camera turns or the sun moves by drag while the fast-forward runs.
6. A number, timer or "skip" button appears for it; the hold shows only through the world and
   the small ink double arrow.
