# Tree: feel and loop review (2026-09-27)

Not technical. I played one day and one month through on paper with the decisions as they stand in design doc v1.2, and wrote down where it felt good, where it went flat, and what I would add.

## One day, as the player lives it (about 8 minutes)

**Sunrise.** The notification said "your linden grew a new branch". I open the app, the camera drifts up out of the dark, the tree stands in low light. Offline growth was tiny, so the tree looks the same as yesterday. *Flat start.* The first seconds of a session are the most important ones in an idle game, and right now nothing happens in them.

**Day, five minutes.** I hold the screen to boost. There is no limit, so the best play is to hold for as long as nutrients last. Then they are gone, and I watch for the remaining minutes. Timing only matters for direction (east or west), and I have no reason to care about the direction yet. *The day has one decision and it is made in the first minute.*

**Pruning.** Once every few days a branch is marked as dying. I cut it. Nice, small, rare. Works.

**Sunset.** I tap the ground, the camera dives, the sound crossfades to the hum. *This is the best moment of the day.* A real ritual.

**Night, three minutes.** I pick a start point on a root, steer, sink, collect glowing dots in a soft fog, steer around a rock. Life force runs out, the run ends. This works as long as there is something to find. If the void is only dots, the tenth night feels like the second.

**Morning.** Back up. The nutrients I collected feed the tree over the day. *The payoff is spread thin.* I never see "this is what last night did".

## One month

Days 1 to 5: a seedling becomes a sapling, every day visibly different. Good. Days 6 to 25: the tree grows, but each day is the same loop with a slightly bigger tree. Day 20-ish: nothing marks it. Day 30: full size, gallery, new seed. *The middle of the month has no events and the end has no feeling.*

## What is strong and should stay
- Real growth that you can actually shape. Nobody else has this.
- The day and night rhythm with the dive as ritual.
- Soft failure only, and water upkeep as the one quiet pressure.
- The journal. It can carry most of the suggestions below without a single new screen.
- One run per night, everything spent. Clean and easy to explain.

## Suggestions, in order of how much they add

1. **Read the meadow to plan the roots.** What grows on the surface hints at what lies below, as in real ecology: rushes and a damp patch mean water, clover and nettles mean nitrogen, a scatter of stones means rock, moss on the north side. The player looks at the meadow by day to decide where to dig at night. This gives the tree mode something to look at and think about, gives the sun steering a reason (grow the crown toward the water side, since roots follow the crown a little), and ties the two worlds into one place. It is cosmetic to build (a few plant types placed by the underground generator) and it is the single most cohesive thing I can think of.

2. **A dawn burst.** Hold back part of the growth that the night's nutrients buy and release it in the first ten seconds after sunrise, with the twinkle, while the camera rises. The player sees what the night did. This fixes the flat start and the thin payoff at once. The daily growth total stays the same, only its timing changes.

3. **Make the boost a trade, not a button to hold.** Right now holding all day is simply correct. Proposal that keeps your "no limit" answer: boosting grows faster but converts nutrients less efficiently into life force, so a boosted day grows a bigger tree and a calmer day fills the night's tank. Shape now, or roots tonight. The choice is made by hand pressure, no UI.

4. **Let the day pass.** Five minutes of watching once nutrients are gone is long. When the tree has nothing left to grow with, a slow swipe across the sky moves the sun on, so a day can be two minutes if the player wants and five if they linger. The night keeps its length.

5. **Visitors as milestones.** The tree earns company as it grows: butterflies at the first leaves, bees when the linden flowers (around day 20; linden blossom is famous for bees), a bird's nest in the crown, a fox resting in the shade, and finally someone leaves a bench under it. Each visitor gets a diary line. This gives the middle of the month its events, and it is all mood, which matches the weather decision.

6. **Small finds underground.** A buried stone with a fossil, an old root of a tree that stood here before, a water vein that hums, a lost coin. Found by touching them, recorded as a drawing in the journal. The void gets memory and the tenth night differs from the second.

7. **The seed hand-off.** When the tree is finished, the last page of its diary shows it dropping a seed. That seed is the next tree. The grove idea already puts the new tree next to the old one; this makes the month feel like one story instead of a reset.

8. **One wish per day.** The diary opens each morning with an optional line: "today, reach the damp patch in the west". No reward, no penalty, just direction for players who want it. Later this is where tutorial steps live too.

## Things I would not add
- Any second currency, quests, or timers to speed things up. The calm is the product.
- Streaks or daily-login rewards. The notification already says the tree grew; that is the reason to return.

## Play-test plan (for the first PC prototype and every step after)

One session is one in-game day. Note the answers to these after each session, in a few words, in the thread or the diary itself.

**Per session**
- What did you do in the first ten seconds, and did anything happen for you?
- How long did you hold the boost? Did you ever let go on purpose?
- Did you know why you were growing east or west?
- Sunset: did the dive feel like a moment?
- Night: where did you go and why? Was anything a surprise? When it ended, did you want ten more seconds or were you done?
- Morning: could you see what the night bought?
- Was there any moment you wanted to close the app before the day ended? At what minute?

**Per week of play (about seven trees' days)**
- Which day was the best and which the most boring?
- Did the tree look the way you tried to shape it?
- Did drought ever happen, and did you understand how to fix it?
- Did you read or write anything in the journal?

**Values to tune while playing, with the current starting point**
- Day and night lengths: 5 and 3 minutes.
- Offline growth: 20 s of game time per real day.
- Root cost per metre and its rise with distance and depth.
- How much one night's nutrients grow the tree (target: visible growth every single day for 30 days).
- Water upkeep per leaf area (drought should be possible but rare).
- How many dots a run collects and how fast the fog fades.

**Pass or fail for Prototype 1 as a whole**
- A first-time player finishes the tutorial (seed, first run, sapling) without being told anything outside the journal.
- Thirty consecutive days each show growth you can point at.
- Nobody ever asks "what am I supposed to do now" during the day.
