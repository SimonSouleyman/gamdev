# Spec: the tree shows what it lacks, and pruning has a felt effect (0.6.3)

Status: planned on the PC right after the new tree (0.6.2). Written 2026-09-29 from the build
thread's plan: drooping or pale leaves show needs, a care page in the journal, pruning with a
real effect (strength for the remaining branches, a denser crown).

## Care signals

**Purpose.** Connect the day to the night: the tree tells the player which dots to chase tonight,
so reading the tree and reading the meadow become one plan.

**What the player should feel.** Attentive care, like noticing a houseplant is thirsty. Never
alarm.

**Input.** None from the player; the signal comes from the stock the roots brought compared with
what tomorrow's growth needs.

**Output.** One visible sign on the tree, following real deficiency symptoms:
- water short: leaves hang (droop) and the crown looks thinner;
- nitrogen short: leaves pale, yellow-green, older leaves first;
- phosphorus short: leaves darker and dull with a purple tinge, few new shoots;
- potassium short: brown, scorched leaf edges.
Only the most limiting need shows on the tree; the journal's care page lists all four in
handwriting with the meadow hint for each ("rushes and the damp patch mean water").

**What working looks like.** Within one day the sign matches the soft Liebig factor the sim
already computes; after a night that brought the missing kind, the sign eases over the morning
and is gone by noon (broken list 11). The player can act on it: a dot of that kind lies within
reach of some root start (broken list 12).

**Soft failure.** The sign only shows the slowdown that already exists; it takes nothing away
by itself. Drought keeps its existing effect (leaves drop); nothing kills the tree.

**Edge cases.**
- Seedling and first day: no leaves to show it; the care page only.
- Autumn look (seasons follow the real calendar: autumn from 1 September, late autumn from
  20 November until spring on 20 March): yellow and red leaves hide "pale" and "scorched". Simon
  plays in autumn right now, so for the next six months colour signs would barely read. The
  signs should therefore lead with shape (hang angle, sparse new shoots, curled or thin leaves)
  and use colour only as a second cue; the care page carries the rest. This is the main clash to
  test.
- Rain and dew: the wet sheen must not read as "healthy" over a real drought.
- A finished tree: no signals.
- Phone: a material parameter on the leaf cards (tint, hang angle), no new draw calls.

**Tuning levers.** Threshold where a sign starts (for example the factor below 0.7), how fast it
eases (hours), how strong the tint is.

## Pruning with an effect

**Purpose.** Make the shears a shaping tool, not only a way to remove wood. Real trees answer a
cut by waking buds below it and giving the remaining branches more strength.

**What the player should feel.** "I cut there, and the next morning it answered." A gardener's
patience.

**Input.** Cut mode as in 0.5.2 (anchored camera, preview, release to cut; never the trunk,
never more than a fifth).

**Output.** At the next dawn:
- a share of the cut wood's growth comes back as extra shoots near the cut: two or three buds
  wake within about a metre below it (denser crown there);
- cutting the leader lets the strongest side shoot below take over.
Sycamore's twin buds stay its quirk (an instant fork on a shoot tip, on top of this).

**What working looks like.** A cut shows new shoots near it by the next day; pruning the maximum
every day never finishes a tree sooner than not pruning (broken list 13), because only a share
of the cut comes back.

**Soft failure.** A bad cut costs wood and some time, never the tree. The one-fifth rule stays.

**Edge cases.** Cutting a branch that is dying back anyway (shaded) should still be worth it:
it wakes buds where there is light (link to the 0.7 candidate "branches the tree marks").
Cutting at night: cut mode is a day tool. Several cuts in one day: the refund adds up but is
capped by the fifth. A finished tree can still be pruned for its look, with no growth effect.

**Tuning levers.** Share of the cut that comes back (suggest 0.2 to 0.4), buds woken per cut
(2 to 3), distance below the cut (about 1 m).

**Dependencies.** The new tree (0.6.2) owns the plant graph; care signals and pruning share its
leaf material and the journal (care page next to the pruning page). Bonsai has its own care and
is not changed here.
