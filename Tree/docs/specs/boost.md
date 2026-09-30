# Spec: boost costs the night (0.6.4)

Status: being built on the PC (stream s-roots). Written 2026-09-29.

**Purpose.** The day's one decision: grow faster now, in the direction of the sun, or bank life
force for a longer root tonight. Today boosting costs almost nothing felt (audit: constant boost
finished linden in 15 instead of 30 days; seeded runs: 14 to 37 days).

**What the player should feel.** A small, fair trade. "I pushed the crown west this evening, so
tonight's root is short." Never guilt, never a wall.

**Input.** A tap anywhere by day: one game hour of brighter sun, up to three ahead (unchanged). From 0.8.1 holding the screen fast-forwards the day instead (specs/fast-forward.md); a hold never boosts.

**Output.** Growth at up to 3x speed toward the boosted sun; life force for tonight clearly lower
than after a calm hour. The life force scrap should show the difference as it happens.

**What working looks like.**
- Constant boosting finishes within 15 % of calm steering on the same seed (broken list 1).
- The root after a fully boosted day is at least a third shorter than after a calm day
  (broken list 2).
- Boosting mostly changes the tree's shape and the timing of growth, not the month's length.

**Soft failure.** An empty tank means a quiet night, never a blocked tap. There is no boost
limit (survey 2), so the cost may take the tank to zero but never below.

**Edge cases.** Boost with nothing left to grow with (nutrients spent): it should warn with the
existing glowing sun rather than silently cost life force for no growth. Beech's quirk (calm
1.25x, boosted 0.7x) must stay a real difference. Offline growth is never boosted (today's rule).

**Tuning levers.** `boost_life_force_factor` (0.4), a direct life force cost per boosted hour
(new), `boost_multiplier` (3), and the calm growth cap (`max_growth_per_second`, 1 node/s).

**Design risk (sent to the build thread).** Much of boost's advantage today comes from the calm
growth cap, not from the life force: a calm day can grow at most about 75 segments however much
the roots brought, so late in the month the calm player cannot spend what the night bought and
the booster can. A higher life force cost alone will not close that gap. Lifting the calm cap
with the tree's size (so nutrients, not the cap, limit a calm day) makes boost a pure trade of
timing against reach. Separately, constant boosting stalled beech, sycamore and alder for good
(no growth for days, see tuning.md); that looks like a bug worth a look in the new tree.

**Dependencies.** Root run (specs/root-run.md), the new tree (0.6.2) which changes growth.
