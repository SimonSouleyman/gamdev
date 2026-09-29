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
- Each sunrise the wish (seeded from the save seed and the day) is either one of the old day
  wishes (sun, looking, saving life force) or points underground. Then the seeded underground
  generator places a *wish deposit*: a rich topsoil patch about 1.5x a normal one, of water or
  nitrogen (whichever the tree is shorter of), a few metres beyond the newest root tip, with its
  meadow hint (rushes or clover). It is a real deposit: any root can drink from it on any night,
  and it stays after its wish has faded.
- Design check (coordinator, 2026-09-30): steering on from the newest tip gave almost nothing
  over ending every root at once, because the benefit came from where the root starts. So the
  deposit is placed where *continuing the last root* leads: beyond the newest tip, outward from
  the trunk where it can, and past the reach of the fine roots a stub would sprout.
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
- Behind rock or out of reach: the deposit is only placed where it clears every rock and the
  straight line from the newest tip is free of rock and costs at most REACH_SHARE of a full calm
  tank; up to 24 tries, turning further aside each time; if none fits, a day wish instead.
- The newest tip at the edge of the world: the tries turn sideways and back inward.
- The dot budget (4000) full: a day wish instead (a month adds about 25 deposits of about 40 dots).
- A night without life force: the glow shows on the quiet visit, as a promise.
- Colour-blind players: warmth plus size plus a slow pulse, not only hue.
- Save and load: placed deposits are saved (day, kind, centre, radius, count) and placed again in
  the same order on load, so their dot ids and states line up. An old save has none; its wish text
  stays until the next sunrise, without a glow.

## Numbers

| Lever | Value | Range | Reason |
|---|---|---|---|
| Placed beyond the newest tip (`Diary.AHEAD_MIN`, `AHEAD_MAX`) | 5.5 to 8.5 m | 4 to 10 | Past the fine roots of a 1 m stub (their reach is 1.6 m plus up to 6 m from leftover life force, most of it thinly), so continuing the root is what finds it; well inside a calm night (a calm tank buys about 20 m near the surface). |
| Kind | water or nitrogen, whichever stock is lower for the species' needs | | The wish points at what the tree lacks; the meadow shows rushes or clover for exactly these two. |
| Depth | 0.6 to 1.8 m (the tip's depth, nudged) | under `HINT_MAX_DEPTH` 2.2 | Topsoil, so the meadow shows a hint above it. |
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
