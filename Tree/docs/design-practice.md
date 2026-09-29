# Design practice for Tree

How design work is done on Tree, by any thread or person. Adapted on 2026-09-29 from Simon's
"GameDesigner" role prompt to this game's pillars (design doc section 17) and its scale: one
developer, one tester (Simon), no analytics.

## Where things live
- `docs/design-doc.md`: what the game is (vision, loops, mechanics, decisions). Versioned, with
  the decision log at the end.
- `docs/tuning.md`: every number that shapes pacing or the life-force economy, and the
  "broken" list checked before each full review.
- `docs/specs/`: one short spec per mechanic that is new or changing.
- `docs/onboarding-check.md`: the first session checked against the onboarding checklist.
- The build thread owns the game code, scenes and `CHANGELOG.md`; design work changes docs only
  and reaches the code through the build thread.

## The practice
1. **Start from what the player feels and decides.** A mechanic earns its place with a real
   choice or a calm, beautiful moment. Mood features (weather, seasons, visitors) may stay
   without being choices, as long as they add no UI burden.
2. **Spec before building.** A new or changed mechanic gets a short spec first: purpose, what
   the player should feel, input, output, what working looks like, the soft failure (drooping,
   dieback, never death), edge cases, tuning levers, dependencies. Settled mechanics are not
   rewritten just to fill the template.
3. **One tuning table.** Every pacing or economy number (boost cost, dot rewards, night length,
   growth rates, pruning effect and the like) sits in `docs/tuning.md` with value, range and
   reason. Values not yet tried on the phone are marked [PLACEHOLDER]. When code changes such a
   number, the table changes in the same pull request or the next one.
4. **Define "broken" first.** Before each full check, write down what failure looks like (for
   example: ending the root run early grows a bigger tree than chasing dots; constant boosting
   finishes a tree far sooner than a month; a night runs past a minute) and check against it.
5. **Paper or headless sim before code.** Check an economy change on paper or with seeded
   headless runs over several play styles (steer, quit early, constant boost, wander) before it
   is built. `tools/month_report.gd` is the build thread's version of this.
6. **Test notes keep what happened apart from what it means.** In early builds, feel comes before
   balance.
7. **The design doc stays versioned** with its decision log.

## Left out on purpose
These parts of the role prompt clash with the pillars (calm, no pressure, free, local only) or
with the scale, and are not used:
- Streaks, seasonal rankings, daily or login rewards.
- Loss aversion, sunk-cost pressure and other pressure tactics.
- Shop, whale, dolphin and minnow economics; inflation metrics per paying player.
- Social pressure and Cialdini-style influence mapping.
- Completion-rate metrics (onboarding above 90 % and similar): there is one tester and no
  analytics. The onboarding checklist is used as a design check instead.

Also set aside for now, because they do not fit the scale:
- Monte Carlo runs over thousands of players: a handful of seeded play styles is enough.
- A full interaction matrix for every pair of systems: only pairs that touch the economy
  (boost, life force, roots, care, pruning) are checked.
- Genre-hybrid work and "mechanic biopsies" of other genres: used only when a new mechanic is
  borrowed, as was done for the roadmap (section 17).

Retention comes only from the game itself: the tree growing while you are away, the album, the
daily wish, the next species.
