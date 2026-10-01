# Spec: side roots from the fine roots, and thicker roots for a full run

Written 2026-09-30 from Simon's 0.8 phone notes (17:43 UTC). It is a changed mechanic, so it is
specced before it is built (design-practice.md). Simon (2026-09-30, 17:45 UTC) keeps it in 0.8.x
rather than 0.9; at 18:29 UTC he split fixes (0.8.1) from new features, so this lands in
**0.8.2** (specs/0.8.md, "0.8.1 and 0.8.2"), after 0.8.1 widened the root field. Numbers are levers, not orders; the build thread records what it
picks in tuning.md.

Simon's words, dictated (summary): the roots that sprout on their own from the player's root
should get a second level. Out of these first roots come second roots that grow by themselves
toward water and nutrients, not very far for now. Life force left at the end of the night goes
into these small roots. Whoever drives the root to the very end of the life force gets no or very
few extra roots; instead that root grows thicker.

**Reading taken (confirmed by Simon, 2026-10-01 14:42 UTC: "ja"):** leftover
life force buys second-level side roots; life force spent on the player's own root makes that
root thicker. Both come from the same tank, so every night is a split between the two.

## What changes against today

Today (root_system.gd `end_run`): fine roots sprout along the new root toward dots within
1.6 m, and each point of leftover life force buys 6 more fine-root nodes and 0.12 m more reach
(at most +6 m), plus a few roots into empty soil. **This replaces that rule:** the first-level
fine roots no longer grow with the leftover (fixed count and reach), and the leftover goes into
a second level instead.

**Purpose.** Make the end of the night a readable choice with a visible result either way: a
root that went far and grew strong, or a root that stopped early and filled its surroundings
with a fan of small roots. Today the leftover only makes the same fine roots a little longer,
which the player barely sees.

**What the player should feel.** "I stopped here, and look how it spreads out" or "I went all
the way, and that root is a real anchor now". Neither feels like a mistake.

**Input.** None new: steering and "end root here" as today.

**Output.**
1. First level, as today but fixed: along the new root, fine roots sprout toward dots within
   1.6 m, 150 nodes, drinking at the fine share (0.1).
2. Second level, new: from the tips of the first-level roots, side roots grow by space
   colonization toward fresh dots within a short reach, about 1.0 m plus 0.05 m per point of
   leftover life force, at most 2.5 m. They get about 4 nodes per point of leftover. They drink
   at the fine share as well, and they draw in and grow over about 3 s at the end of the run so
   the player sees them spread. Where no dot is in reach (the root field is wider from 0.8.1),
   a few short tips still sprout so the fan shows.
2b. Third level, new (Simon, 2026-09-30, 18:15 UTC: "aus Seitenwurzeln nochmal kleinere
   Seitenwurzeln"): from the second-level roots, smaller roots branch once more. The leftover's
   nodes split about 70 % to the second level and 30 % to the third. Third-level roots grow from
   along and at the tips of the second level toward fresh dots within about 0.3 m plus 0.02 m per
   point of leftover, at most 0.8 m; where no dot is that close they still sprout a few short
   tips, so the fan reads as branched. They are drawn about half as thick as the second level and
   a shade dimmer, drink at half the fine share (0.05), and grow in right after the second level
   within the same 3 s. A third level only starts from a second-level root at least about 0.5 m
   long.
3. Thickening, new: the share of the night's tank spent on the player's own root sets how thick
   that root is drawn, from 1.0x (ended at once) to about 1.5x (tank run dry). A thicker root
   takes up to 1.25x groundwater seep per metre, so the full run has a small lasting gain.
   Near-empty tank (less than about 10 % of the start left): no second level at all.

**What working looks like.**
- Ending early still loses clearly to steering (tuning broken items 4 and 10a stand): the second
  level must not bring back the 0.6 problem where quitting early won. Its reach is short and it
  drinks at the fine share for exactly that reason.
- Running the tank dry and ending with some life force left finish within about 2 days of each
  other when both steer well; the choice is a style, not a trap.
- Both results are visible from the root-run camera at the end of the night, on the phone.

**Soft failure.** No wrong answer: both uses of the life force help the tree. A run that ends
at once still gets a small fan of side roots, as today's unsteered tree still finishes.

**Edge cases.**
- Stuck against rock: the leftover goes into the second level, as it goes into fine roots today.
- Old saves: existing roots keep their thickness 1.0x and have no second level.
- Node budget: the second and third levels together get at most about 250 nodes per night (first
  level 150), all within `FINE_ROOTS_MAX_PER_MAIN_ROOT` (600); the
  root graph size does not grow.
- Phone: third-level roots are the thinnest thing drawn underground; they must still read in
  the darker night view (see the darkness note in specs/0.8.md).
- Oak (deep-root quirk) and birch (cheap topsoil) keep their quirks; the second level follows
  the same cost rules as fine roots (none, it is paid from the leftover).

**Tuning levers.** Second-level reach base and per life force, nodes per life force, the split
between the second and third level, third-level reach and drink share, the node cap, the
"near-empty" threshold, the thickness range, the seep bonus for thickness.

## What "broken" looks like

1. Ending every root at once finishes as fast as steering, or less than 4 days later (tuning 4).
2. Running the tank dry is always worse than ending with a quarter left, or always better, by
   more than about 2 days on the same seed.
3. The second level grows past 2.5 m from its first-level root, or reaches dots the player could
   not see in the fog from the new root.
4. A tree never steered finishes later than tuning 10a allows, or sooner than a steered one.
5. Side roots or the thicker root cannot be told apart on the phone at the end-of-run camera.
6. The second level costs a visible frame drop underground (below 30 fps where it held before)
   or pushes the root graph past its budget.
7. Thickness or side roots change after the night is over (they are set once, at the run's end).
8. With the third level, the underground drops below 30 fps on the Fairphone (where it held
   before), a night's roots pass the node cap, or the three levels cannot be told apart at the
   end-of-run camera (the third must read as finer and dimmer, never as a grey fuzz).
9. The third level makes ending early pay: items 1 and 4 still hold with it.
