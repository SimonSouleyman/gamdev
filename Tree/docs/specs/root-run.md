# Spec: the root run pays for steering, and the night has a calm length (0.6.4)

Status: being built on the PC (stream s-roots). Written 2026-09-29 from the build thread's plan
and the seeded runs in tuning.md. Numbers are levers, not orders; the build thread picks values
and records them in tuning.md.

**Purpose.** The night is where the player reads the meadow and makes the day's second choice.
Today ending the root at once grows the tree fastest on all six species, so the choice is empty.

**What the player should feel.** "I went for that damp patch and it paid off tomorrow." Calm
concentration for under a minute, then rest.

**Input.** An invisible floating stick, swipe up to dive, "let roots spread" (was "end root here"), the start point on an old root.

**Controls (Simon, 2026-10-02, 20:31 UTC; next build).** The start button at the bottom right goes away; "let roots spread" is the only button left in root mode. The drawn stick on the left goes away too: the stick is invisible. The first tap anywhere on the screen starts the root. From then on, touching anywhere and dragging steers in the direction of the drag (the touch point is the stick's centre, the drag is its tilt); lifting the finger keeps the held heading, as now. Simon: "Das ist intuitiver."

**Output.** Nutrients from dots the new root touches (first contact), fine roots from the
leftover life force, deposits the network keeps drinking each night, and a permanent root.

**What working looks like.**
- Chasing the dots the meadow points to beats ending early and beats random steering, on every
  species and at least three seeds (broken list 4 to 6 in tuning.md).
- A full tank after a calm day buys a root of about 40 to 60 s; a fully boosted day about a
  third less; a night never runs past 60 s (broken list 7, 8).

**Soft failure.** A poor run only means fewer nutrients and a slower tree tomorrow. Nothing is
lost, and the root stays.

**Edge cases.**
- No life force: the 5 s quiet visit, as today.
- The run stuck against rock: it ends after 4 s as today; the leftover becomes fine roots.
- The 100 m main-root cap and the 40 main-root cap: with a 40 to 60 s night the first one should
  no longer be reached; if the soil fills, the quiet visit as today.
- The app closed mid-run: life force stays as it was (today's rule).
- Species quirks keep working: birch topsoil discount, oak downward discount, alder nodules.

**Tuning levers (in order of expected effect).**
1. Leftover life force to fine roots (`fine_nodes_per_life_force`, now 6): lower it, or place
   the leftover fine roots along the new root's far half instead of around its end. This is
   what makes quitting pay today.
2. Touched-dot reward (`FIRST_SHARE`, now 0.4): raise it so a touched dot is clearly worth more
   than the same life force spent on fine roots.
3. Night length: root speed up (now 0.9 m/s) and cost per metre scaled to the tank, so a full
   calm tank buys 40 to 60 s whatever the tree's size (today the tank grows with the crown, so
   nights grow through the month).
4. Pickup radius (now 0.55 m) and turn rate (now 1.7 rad/s) for easier steering.

**Dependencies.** Boost (specs/boost.md) sets how full the tank is; care signals
(specs/care-and-pruning.md) point to the dot kind worth chasing; the daily wish and the 0.7
nightly target (specs/0.7-candidates.md).

**Design risk (sent to the build thread).** If the night gets shorter but the life force keeps
growing with the crown, the unused rest piles up (seed 27 ended the month with 656 unused). The
tank and the night need to scale together.

**Replay camera (Simon, 2026-10-02, 20:15 UTC).** When the night's root is finished, the replay frames tonight's new root: the camera centres on it and moves close enough that it fills most of the screen (older roots stay in view only as context), instead of the wider view of the whole network.
