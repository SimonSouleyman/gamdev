# Spec: the wish as a place, the compass needle, steering that pays, the life force vial (0.8.2.5)

Written 2026-10-02 from Simon's questions (15:09 UTC): "welchen nutzen hat der kompass? oder was
ist mit den wünschen? ich habe das im spiel noch nicht verstanden". His four card answers in this
thread (15:17 to 15:19 UTC): compass "Nadel zeigt zum Wunsch", wish "Ein Ort, gut sichtbar",
steering "Mehr Unterschied", boost cost "Sichtbar machen". His own words on the vial (15:21 UTC):
"die lebenskraft sollte nicht als Zahl sichtbar sein, sondern ein bisschen wie eine Manaphiole aus
rollenspielen wie diablo. runde glasflasche sozusagen. flüssigkeit in grün."

**The problem.** Three of the game's tools were hidden from the one person who plays it most:
- The wish was a line of text with a plant name and a direction ("Wish: the clover in the west").
  It needed the compass to read it, and on about 4 mornings in 10 it was a day wish that did
  nothing at all.
- The compass only turned with the camera, so its sole use was translating that text.
- Steering the root to deposits saved about 6 days a month. That was real, but too small to feel
  over a few nights.
- A boost's cost for the night showed only as a smaller number on a paper scrap.

## 1. The wish is a place you can see

**Purpose.** Every morning the tree wants one thing, and the player can see where it is without
reading anything.

**What the player should feel.** "There it is. Tonight I'll get there."

**Output.**
- **Every morning has a wish place.** The day wishes go away (`DAY_WISHES`, `DAY_WISH_LINES`).
  `Diary.underground_share` goes from 0.6 to 1.0. A wish that is still waiting (a far wish, up
  to 3 mornings) stays the wish, as now.
- **The place shows on the meadow.** Its plant (rushes for water, clover for N, nettles for P,
  comfrey for K) grows above the deposit visibly larger than the other meadow signs, in flower,
  with a few butterflies or a soft shimmer by day. A far wish beyond the clearing shows this
  plant at the clearing's edge in its direction (the existing rule for meadow signs, made larger).
- **At sunset** a small ink ring is drawn around the plant for a second before the dive, so the
  player knows what to aim for.
- **Underground**, the deposit glows as now (one glow at a time).
- **The journal line** names the plant without a direction: "Wish: the clover." A far wish says
  "Wish: the clover, far away."
- **Reaching it** writes the diary line with its doodle, as now.

**Not changed.** The wish never punishes. A missed wish stays an ordinary deposit, and its glow
goes out when the next wish shows.

## 2. The compass needle points to the wish

**Purpose.** The compass becomes a tool the player uses instead of a decoration.

**Output.** The old hand compass keeps its look and position. Its needle no longer shows north. It
points from the trunk toward today's wish place, turning with the camera. On a day with no
wish (a reached wish), the needle drifts slowly and rests. By night in the root view, it shows
the wish's direction from the newest root tip, so it helps steer.

**First-time page.** One line the first time the needle is seen: "The needle points to what the
tree wants."

## 3. Steering pays more

**Purpose.** A night steered to deposits should be clearly worth more than a night not steered,
so steering feels like the game and not a chore (Simon: "Mehr Unterschied").

**Target.** A steered tree finishes about 10 days before a tree whose roots are never steered.
The month per tree stays (steered 26 to 34 days). A never-steered tree still finishes, by about
day 40, or about day 42 for oak.

**Lever (sim below).** The soft Liebig floor (`GrowthSim.liebig_floor`) becomes a species value:
0.45 for linden, birch, beech, sycamore and alder, 0.55 for oak (now 0.6 for all). A tree missing
N, P or K then grows at about 45 % instead of 60 % of full speed. Steered trees reach their
deposits and barely notice it. Oak keeps a higher floor because at 0.45 its never-steered tree
finished on day 45. Bigger deposits (`capacity` 1.3 to 2.0) were tried and did not widen the gap.
With section 1 (a wish every morning), the glow follower reached all 24 wishes and finished a day
sooner.

**Visible in the morning.** After a night whose root reached a deposit, the morning shows it once:
a short ink line "the roots drank well" on the day page, and that day the crown grows by a few
more segments than yesterday. No numbers. (The morning reveal from survey G3 is the bigger version
of this; this line is its first step.)

## 4. Life force as a green vial

**Purpose.** Show life force as something you can see filling, and show what a boost costs
tonight without a number.

**Output.**
- The life force scrap with its number is replaced by a **round glass vial with green liquid**, a
  bit like the mana flask in Diablo, drawn in the game's style (real glass, a cork, a paper label
  with no number). It sits where the scrap sat.
- **By day** the liquid rises as the leaves gather life force. While a boost runs, it rises
  visibly slower, and a faint pencil mark on the glass shows how high a calm day would have
  filled it by now. The gap between the liquid and the mark is the boost's cost.
- **The same vial in tree and root mode** (Simon, 16:04 UTC: "life force soll natürlich im baum und im wurzel modus gleich sein"): same glass, same green, same place on screen, so the dive keeps it in view.
- **By night** the liquid falls as the root grows. An empty vial is the end of the night (as an
  empty tank is now).
- The **leftover** for small roots ("let roots spread") is what remains in the vial.
- No number anywhere; the life force number in the debug overlay stays for testing.

## What working looks like (broken list, short)

45. A morning without a wish place, or a journal page showing a day wish line.
46. A wish plant that is not clearly larger than the other meadow signs, or no plant for a far
    wish at the clearing's edge.
47. The compass needle not pointing to the wish place (from any camera angle), or pointing north.
48. A tree whose roots are never steered finishing after day 40 (oak after day 42), or a steered
    tree after day 34.
49. A life force number visible in normal play, or a vial that does not fall during the root run.
50. A boosted hour where the liquid and the calm-day mark stay level.

## Sim (design thread, 2026-10-02)

Short run only (linden and oak, seed 14, `tools/strategies.gd`, main at edc1c75).

| Change | Linden steered (dots) | Linden ended early | Linden straight down |
|---|---|---|---|
| As now (floor 0.6) | 30 | 36 | 38 |
| Deposits x1.3 | 29 | 35 | 37 |
| Deposits x1.6 | 29 | 36 | 37 |
| Deposits x2.0 | 29 | 35 | 36 |
| Floor 0.5 | 29 | 38 | 42 |
| Seep cap 0.5 | 30 | 39 | 45 |
| **Floor 0.45** | **30** | **40** | **40** |


| Change | Oak steered | Oak ended early | Oak straight down |
|---|---|---|---|
| As now (floor 0.6) | 33 | 41 | 41 |
| Floor 0.55 | | 42 | 42 |
| Floor 0.5 | | 43 | not by day 45 |
| Floor 0.45 | 33 | 45 | 45 |

| Change | Birch steered | Birch ended early |
|---|---|---|
| As now (floor 0.6) | 25 | 31 |
| Floor 0.45 | 26 | 34 |

| Change | Linden following the glow | Linden random steering |
|---|---|---|
| Floor 0.45, wishes on 6 mornings in 10 | 30 (14 of 15 reached) | 40 |
| Floor 0.45, a wish every morning | 29 (24 of 24 reached) | 40 |

**What it means.** Steered against never steered: linden 30 against 40 (was 30 against 36 to 38),
oak 33 against 42 at 0.55, birch 26 against 34. That is the "about 10 days" Simon chose, while
every tree still finishes within its limit. One seed only, so the PC thread's short check after
the build should look at the never-steered oak once more.
