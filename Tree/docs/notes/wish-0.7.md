# Build notes: a visible goal underground each night (0.7 candidate 1)

Build thread notes for specs/0.7-candidates.md section 1 (Simon, 2026-09-29: glow and a real
bigger deposit). The design thread owns design-doc.md and tuning.md; the numbers below are
meant to move into tuning.md once they have been felt on the phone. All are [PLACEHOLDER].

## Short spec (as built)

**Purpose.** The day's wish points at a real place underground, so reading the meadow turns
into a plan for the night.

**Feel.** Curiosity and a quiet "found it". Never a task: no counter, no tick marks, no list.

**Input.** None new: the morning scrap names the wish; at night the player steers as always.

**Output.**
- The seeded underground generator places a few *wish deposits*: rich topsoil patches of water
  or nitrogen, about 1.5x the size of a normal rich patch, each with its meadow hint (rushes or
  clover). They are real deposits: any root can drink from them on any night.
- Each sunrise the wish (seeded from the save seed and the day) points at one of them, or is
  one of the old day wishes (sun, looking, saving life force).
- Underground that patch glows warmer: a soft amber haze over it that breathes slowly, and its
  own dots tinted a little warmer. Close up it is plain; at the edge of the fog (where the
  ordinary dots have faded out) it stays as a faint warm smudge, so it reads as "over there".
- When tonight's root reaches it, the root drinks what it holds like any deposit, the haze
  swells once and settles, and the diary gets a line with a small ink drawing (rushes or
  clover). The journal shows the drawing next to the line.
- Missed: nothing is lost. The next night it still glows, fainter; at the following sunrise it
  is gone (a new wish has come by then).

**Working looks like.** A root aimed at it from a good starting point reaches it on most nights
with a calm tank; aiming for it reaches it far more often than random steering (broken list 5).

**Soft failure.** Not reaching it costs nothing.

**Edge cases.**
- Behind rock or out of reach: the wish only picks a wish deposit whose straight line from some
  root node (or the trunk) is free of rock and costs at most REACH_SHARE of a full calm tank.
- Already drained or reached: a patch with fewer than half its dots left, or mostly tapped by the
  roots, is not picked.
- A night without life force: the glow shows on the quiet visit, as a promise.
- Colour-blind players: warmth plus size plus a slow pulse, not only hue.
- Old saves: the new deposits are appended after the old dots, so the saved deposit state still
  lines up; the old wish text stays until the next sunrise, without a glow.

## Numbers

| Lever | Value | Range | Reason |
|---|---|---|---|
| Wish deposits (`Underground.WISH_DEPOSITS`) | 8 (4 water, 4 nitrogen, alternating around the compass) | 6 to 10 | One per direction, so the wishes name different directions; enough that drained ones can rest (regrowth) while others are picked. |
| Distance from the trunk | 3.5 to 8 m | 3 to 10 | Beyond the starter patch, well inside a calm night (a calm tank buys 20+ m near the surface). |
| Depth | 0.6 to 1.8 m | under `HINT_MAX_DEPTH` 2.2 | Topsoil, so the meadow shows a hint above it. |
| Size bonus (`WISH_SIZE`) | 1.5x the dots of a normal rich patch of the kind (nitrogen 40 to 48, water 35 to 43), radius 1.15x | 1.3 to 2 | Spec: "about 1.5x". Same amount per dot, so each dot glows like the others; the patch is fuller and a little wider. |
| Reach budget (`Diary.REACH_SHARE`) | straight-line cost at most 0.6 of a calm tank (`RootSystem.calm_life_force`, 50 today) | 0.4 to 0.8 | A steered root curves and sinks; the slack keeps "reachable" honest. |
| Wishes that point underground (`Diary.UNDERGROUND_SHARE`) | 0.75 | 0.5 to 0.9 | Most days, but some days keep the old calm wishes about the sun and the tree. |
| Reach distance (`Diary.REACH_MARGIN`) | a main-root node within the patch radius + 0.35 m of its centre | 0.2 to 0.6 | The tip passing through the patch; the collect radius does the drinking. |
| Glow strength, today's / yesterday's missed | 1.0 / 0.45 | yesterday's 0.3 to 0.6 | Yesterday's is a reminder, not a second goal. |
| Glow reach (`wish_glow.gdshader`) | full up to 7 m, then down to a faint floor of 0.22 held to 30 m, gone by 38 m | floor 0.15 to 0.35 | The dots fade out by 15 m during a run; the wish stays faintly visible beyond them. |
| Pulse | 5.2 s period, 35 % depth | 4 to 8 s | Slow breathing, clearly different from the dots' quick flicker (1.7 rad/s). |
| Warm tint of the patch's dots | 35 % toward amber | 20 to 50 % | "A little warmer", the dots stay readable by colour. |

## Checks

See `tests/test_wish.gd` and `tools/strategies.gd --strats=wish,random`. Results are logged
below after each tuning pass.
