# Roadmap ideas after 0.5.2 (proposal, 2026-09-28)

A candidate list for 0.6, 0.7 and later, drawn from relaxed and cosy games and filtered through Tree's design. Simon answered survey batch 1 on 2026-09-28; the outcome is design doc section 17. The buckets below are the original proposal.

## The filter: Tree's pillars
Taken from design doc sections 1, 2 and 7 and the decision log.
1. **Calm, no pressure:** nothing rushes, nothing dies, soft failure only, at most one notification a day.
2. **One realistic tree:** photorealistic look, algorithmic growth, a small richly animated clearing.
3. **The journal and the shed:** every menu is a real object or a handwritten page, never a plain window.
4. **A daily ritual:** about one day and night per real day, one month per tree, one tree after another.
5. **Free and self-contained:** no ads, no purchases, no Google services, local only.
6. **Runs on the Fairphone:** the forest edge is already the main cost; 30 fps is the goal.

An idea that breaks a pillar goes into "never", with the reason.

## What the reference games teach
| Game | What fits Tree |
|---|---|
| Viridi, Pocket Plants | A plant that lives in real time; checking in is a small daily pleasure, not a chore. Tree already does this. |
| Animal Crossing | The real calendar and real sky make the world feel alive: moon phases, seasons, rare events on real dates. Visitors you can collect. |
| Neko Atsume | Coming back to see who visited while you were away, and a notebook of every visitor ever seen. |
| Alba, A Short Hike | A field guide filled by photographing animals; small finds on a short walk. |
| Unpacking, Spiritfarer | Wordless storytelling through objects; a gentle farewell ritual when something ends. |
| Terra Nil, Cloud Gardens | The landscape itself changes because of what you grew: plants and animals move in on their own. |
| Townscaper, Tiny Glade | Pure joy of touch: every tap answers with a small, satisfying reaction and sound. |
| Dorfromantik | Optional gentle goals (Tree has the daily wish) instead of scores. |
| Forest (focus app) | A time-lapse of what grew; sharing a picture of it. |
| Flower, Journey | Wind and light as emotion; no text needed. |
| Egg Inc, Tamagotchi | The "while you were away" moment; care as attachment. Their pressure parts (upgrades, hunger, death) break pillar 1. |
| Stardew Valley | Weather and seasons change what a day looks and sounds like. Its music and task lists do not fit (ambience only, no chores). |

## Already decided for 0.6 (unchanged)
Picture icons in the HUD (book, hut, pruning shears, closed album); the compass as an old hand compass; moon and starry sky with few clouds; the shed workbench in the centre with real models as the menu; a simpler forest on the phone for 30 fps; bench visitor removed; bonsai mode (section 16).

**Sequencing proposal:** 0.6 is already the biggest version so far. Bonsai is large and builds on the new shed table. Suggestion: 0.6 = the look and shed pack plus the 30 fps forest, tested on the phone; bonsai becomes 0.7 on top of the finished shed. (Survey question 1.)

## Candidates

Effort: S small (a day of building), M medium, L large. Phone risk: how much it could cost frames on the Fairphone. ★ = recommended.

### Still into 0.6 (small, fit what 0.6 already touches)
| Idea | From | Why it fits | Effort | Phone risk |
|---|---|---|---|---|
| ★ **Real moon phase** | Animal Crossing | The 0.6 moon shows today's real phase (full, half, new) from the date. One calculation, no new art. | S | none |
| ★ **"While you were away" page** | Neko Atsume, Egg Inc | On return after offline time, the diary opens on a short handwritten page: how many cm it grew, who visited, a sketch. Makes coming back feel good without pressure. | S | none |
| ★ **Tap answers** | Townscaper | Tapping the shed objects plays a small real sound and motion (book creaks open, seed bag rustles, album page flutters). Belongs to the new table menu anyway. | S | none |
| ★ **Readable text option** | Accessibility | A pinboard switch that swaps the handwriting for a clearer print hand (Patrick Hand) and makes it a bit larger. Caveat is beautiful but hard to read on a phone. | S | none |
| Light haptics | Accessibility, feel | A short soft vibration on a cut, a dive and a finished tree; switch on the pinboard. | S | none |

### Next version 0.7
| Idea | From | Why it fits | Effort | Phone risk |
|---|---|---|---|---|
| ★ **Bonsai mode** (if moved, see above) | own design | Section 16, unchanged. | L | low |
| ★ **Seasons as a look** | Animal Crossing, Stardew | Spring, summer and autumn follow the real calendar as pure mood: fresh light green in May, deep green in summer, yellow linden and red maple leaves falling in October, mist. Growth rules stay the same. Winter stays parked (late autumn look until spring). The data model is already planned for it (section 11). | L | low to medium (falling leaves are particles) |
| ★ **Field guide** | Alba, Neko Atsume | A journal section with a sketch page per visitor, filled when the player photographs it with the album camera. More visitors: squirrel, woodpecker, jay, deer at dawn, hedgehog at dusk, owl call at night, bees at the linden blossom, species visitors from section 15. Collection without score. | M | low (one animal at a time) |
| ★ **Time-lapse of the month** | Forest, Viridi | The album's daily photos play as a flip-book on the finished tree's double page, and can be saved as a short video to the phone's gallery. The farewell moment gets its payoff. | M | none (plays photos) |
| **Weather moods** | Stardew, Flower | Now and then a light rain shower (rain on the leaves, drops on the camera, a wet sound), a misty morning, dew on the grass, a distant thunder. Mood only, as decided. | M | medium (rain particles; phone gets a lighter version) |
| **Living clearing** | Terra Nil, Cloud Gardens | As the crown grows, the ground under it changes by itself: sun meadow gives way to shade plants (wood anemone, fern, moss), mushrooms after rain. The tree visibly shapes its world. | M | low to medium |
| **Brush pile from pruning** | Terra Nil, real gardening | Cut branches no longer vanish but gather in a small pile at the clearing edge; over days a hedgehog or wren moves in. Pruning gets a natural second life. Nature only, nothing man-made. | S | low |
| **Share a photo** | sharing | A "send" button in the album opens Android's share sheet with the Polaroid. Works without Google services. | S | none |
| **Save backup** | safety | A pinboard note "copy my tree" writes the save to a file the player can keep; "load" reads it back. One month of growth is worth protecting. | S | none |
| **Colour-blind nutrient marks** | Accessibility | Each nutrient dot also gets its own glow shape (drop, leaf, spark, ring), so green and orange are never confused. | S | none |

### Later or maybe
| Idea | From | Why / why not yet | Effort | Phone risk |
|---|---|---|---|---|
| **Curiosity shelf and the old gardener** | Unpacking, Spiritfarer | Underground finds become real objects on a shelf in the shed; some of them are the old gardener's things (a pocket knife, a letter, a pressed leaf) that tell, without words, who planted the forest around the clearing. Gentle mystery and lore. Needs the 0.6 shed first. | M | none |
| **3D grove** | own design (section 7) | Finished trees stand around the clearing and can be visited. Already a planned later milestone; each extra big tree costs a lot on the phone, so it needs impostors first. | L | high |
| **Live wallpaper** | own design (section 11) | Parked milestone; the still wallpaper already works. | M | high (battery) |
| **Winter** | Animal Crossing | Parked: a bare linden from November to April, and what the player does then is open. Could become the bonsai's season, or a snowy, restful month. | L | medium |
| **Night sky events** | Animal Crossing | Perseids in August, a full-moon night with a moon-lit root run, first snow. Needs seasons and the 0.6 sky. | S each | none |
| **Name and carve** | Tamagotchi | Name the tree; the name is carved into the bark and written on its album page. Small attachment. | S | none |
| **More species** | section 15 alternatives | Hornbeam, wild cherry, willow, horse chestnut after the six. | M each | none |
| **Placed habitat objects** | Neko Atsume | Nest box, bee hotel, stone pile to invite visitors. Simon did not like the bench as a man-made object in front of the tree, so only if he wants it. | M | low |
| **PC version** | decided later | Godot Forward+ with full forest and shadows. | M | n/a |

### Never (they break a pillar)
- **Daily streaks, login rewards, energy timers, "come back or it withers"** (Forest, Duolingo, Tamagotchi hunger): pillar 1, calm and no pressure.
- **Shop, currency, ads, cosmetics for money** (Egg Inc): pillar 5, free.
- **Online leaderboards, friends' trees, cloud save** (many idle games): pillar 5, local only and no Google services. Sharing a photo through the share sheet is the calm alternative.
- **Background music** (Stardew, Journey): decided "ambience only, no music". Only if Simon changes that.
- **Quest lists and task checklists** (Stardew): the daily wish is the only goal, without reward or penalty.
- **Conifers in the clearing:** decided broadleaf only; juniper lives only as a bonsai.

## Open survey questions (batch 1)
1. Bonsai in 0.6, or 0.6 without bonsai and bonsai as 0.7? ★ Own 0.7.
2. Seasons: calendar look in 0.7 (winter parked), later, or never? ★ Look in 0.7.
3. Which collection direction first: field guide, living clearing, or curiosity shelf? ★ Field guide.
4. Time-lapse of the month from the album photos: 0.7, 0.6, or not? ★ 0.7.
5. Weather moods (rain, mist, dew): 0.7, later, or never? ★ 0.7 with a light phone version.

## Answers (Simon, 2026-09-28 16:40 to 16:42 UTC)
1. Bonsai: own version 0.7.
2. Seasons: look in 0.7.
3. Collection direction: living clearing first (not the recommended field guide).
4. Time-lapse: 0.7.
5. Weather moods: 0.7.
The small 0.6 additions were accepted without objection.
