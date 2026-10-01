# Spec: extras for the wider root field (0.8.2 and 0.8.3)

Written 2026-10-01 from the coordinator's five ideas (2026-09-30, 18:40 UTC), which Simon
accepted on 2026-10-01 at 08:01 UTC ("alle gut"). They build on the wider root field of 0.8.1
(specs/0.8.md, "A wider root field"). Numbers are [PLACEHOLDER] in tuning.md; the build thread
tunes them. **0.8.2:** zooming far out underground, rock bands and soft veins, a first-time
player check. **0.8.3:** a fungal network, an ink drawing of the whole root network in the album.

## 1. Zooming far out underground (0.8.2)

**Purpose.** With patches up to 30 m away and roots continued over several nights, the player
needs to see the whole field to plan a journey, and to see what the past nights built.

**What the player should feel.** Looking at a map they drew themselves: "there is my long root
from Tuesday, and the nettle patch is just beyond it."

**Input.** Pinch out underground, past today's limit (30 m camera distance), up to a view of the
whole field. Pinch in or the back gesture returns to the normal camera. Available while choosing
the start, on the quiet visit and after the run has ended; during the run the pinch keeps
today's range, so the far view never costs driving time.

**Output.** A slow pull-back to a high three-quarter view over the field:
- every root the tree has grown, main roots as warm lines, the last seven nights' roots a little
  brighter, the newest one brightest; side roots too fine to draw at that distance are left out;
- rich patches the player has come near (within the fog's reach of any root, or shown by a
  meadow sign) as soft coloured clouds; unknown patches stay hidden, so exploring still matters;
- the day's wish glow, rock bands (below) as dark shapes, the trunk as a small mark at the top.
Tapping an old root's tip in the far view picks it as tonight's start and flies back down.

**Soft failure.** None; it is a view.

**Edge cases.** First night (only the seed root): the far view is nearly empty, which is honest.
A save from before 0.8.1 has a small field; the view simply frames what is there.

**Tuning levers.** Maximum camera distance, how many recent nights glow brighter, the fog reach
that counts a patch as "known".

## 2. Rock bands and soft soil veins (0.8.2)

**Purpose.** In the wider field, the straight line to a far patch should not always be the way.
A small route choice makes the journey a decision without making it hard.

**Output.**
- About 3 rock bands in the middle and far rings: curved walls 6 to 15 m long, 1 to 2 m thick,
  each with at least one gap. Roots cannot pass through rock (as with today's rocks); the
  magnetism never pulls a tip into rock. Potassium dots gather along bands as they do by rocks.
- About 2 soft soil veins: tubes 1 to 1.5 m wide and 8 to 20 m long of lighter, crumbly soil in
  which a metre of root costs about 0.6x.
- Every far patch can be reached by at least two routes; about a third of them have the straight
  line blocked by a band, about a third have a vein pointing toward them.
- On the meadow, stones already mean rock: bands under the clearing show a line of stones.

**What the player should feel.** "Around the rock, or along the soft vein?" A choice, not a maze.

**Soft failure.** A root that hits a band stops there as it does at a rock today; the leftover
goes into side roots. Nothing is lost.

**Tuning levers.** Number and length of bands, gap width, vein cost factor and count.

## 3. Fungal network (0.8.3)

**Purpose.** A rare, quiet discovery that rewards exploring the far field, in the spirit of the
underground finds.

**Output.** A few hidden mycelium patches (about 3, in the middle and far rings) show as faint
white threads only when a root comes within about 1 m. When a new root touches one, the
network links it for 3 nights to the nearest rich patch the tree has not reached, within about
15 m: each night that patch gives the tree what a fine root would drink (the fine share, 0.1).
A thin white thread is drawn between the two in the far view; the journal gets one line and a
doodle of the mushrooms that appear on the meadow above it the next morning. Each mycelium
patch links once per tree.

**What the player should feel.** Wonder: "the forest is helping."

**Soft failure.** Missing it costs nothing; there is no counter and no hint that it exists
beyond the threads near a root (pillar: no quest lists).

**Must not.** Make far patches pay without driving there (it is rare and lasts 3 nights), or
change the month's length (tuning item 9).

**Tuning levers.** Number of patches, discovery radius, nights linked, share.

## 4. The root network in the album (0.8.3)

**Purpose.** The root work of a month is invisible once the tree is finished; the album should
keep it.

**Output.** When a tree finishes, its album page gets, beside the final photo, an ink drawing of
its whole root network seen from the side, drawn from the save: main roots in firm strokes, side
roots in fine hatching, the trunk and the ground line at the top, the species' name and the day
count in the caption. It is drawn once at the finish and stored with the album; it can be shared
like a Polaroid (specs/0.8.md section 3). Trees finished before 0.8.3 keep their page without
one (their roots are gone from the save).

**Tuning levers.** Drawing size, stroke thickness by root level, paper and ink colours.

## 5. First-time player check after 0.8.2

After 0.8.2, play a fresh install as a new player would (onboarding-check.md, one thing taught
at a time) and check that holding to fast-forward, the side roots, the wider field, the far view
and the rock bands each make sense without more than one line of explanation, at the moment
they first matter. Findings go into onboarding-check.md as "what happened", then the fixes into
the next 0.8.x.

## What "broken" looks like

1. The far view is reachable during a running root, or costs frames: below about 30 fps on the
   Fairphone while zoomed out.
2. The far view shows rich patches the player has never come near, or hides ones they have.
3. A far patch has only one route, or the straight line is blocked for more than about half of
   them; or a tip can pass through a band, or the magnetism pulls it into rock.
4. The soft veins make one route always best, so the choice disappears.
5. The steered month leaves its target range with bands and veins in place (tuning "Days to
   finish"), or nights leave 20 to 44 s.
6. The fungal network fires more than about once a week on average, links to a patch the tree
   already drinks from, or makes ending early pay (tuning item 4).
7. Anything counts mycelium patches or links (pillar: no quest lists).
8. The root drawing is missing on a tree finished after 0.8.3, does not match the save's roots,
   or a finished tree's album page breaks for trees from before 0.8.3.
9. The first-time check finds a new thing that needs more than one line to understand, or that
   is taught before it first matters.
