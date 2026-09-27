# Changelog

## v0.2 (2026-09-27)

Everything since v0.1, from Simon's play tests and seven rounds of independent review
(three QA rounds with three testers each, plus a realism-only round against the reference photos).

### Gameplay (play test 1)
- The sun can be moved on at any time: drag it along the arc at the top. The tree rests meanwhile
  (leaves still gather life force), so the nutrients wait for the hour you pick, e.g. wait for the
  afternoon, then boost to grow west. The dawn burst always plays out first.
- A root can be ended early ("end root here" or E). The rest of the night's life force goes into
  more and longer fine roots around it. Ending before the root grew keeps the life force.
- No marker ball at the root tip; tonight's root glows warmer than the old ones.
- The soil slowly refills each night, so the month never runs dry.
- A missing nitrogen, phosphorus or potassium slows growth to about a third instead of stopping it;
  the day only counts as "spent" when water runs out or the tree is full.
- Growth is paced so a calm day lasts until sunset and the node budget lasts the whole month;
  the linden stops growing taller at its species height.
- The simulation runs in fixed steps, so the tree grows the same at any frame rate.

### Look (play tests 1 and 2, reference photos)
- A small forest clearing instead of an open meadow: a closed wall of oak, beech, birch, linden and
  spruce (grown by the same growth model, each with its own shape, bark and leaf colour), bushes at
  the edge, painted deep wood behind; the camera stays inside the clearing.
- The meadow is dense soft grass clumps with herbs and wildflowers, dry and lush patches, shade at
  the forest edge; rushes, clover, nettles, stones and damp patches still hint at what lies below.
- The player's tree: smooth branches with photo bark, a single trunk and a crown, photo leaf sprays
  with a shaded interior; it stands out from the darker, cooler forest by a light rim and haze.
- Light: physical sky, drifting clouds, warm light and golden haze at a low sun, a bluer noon,
  soft shadows, MSAA and FXAA; branches and leaves sway in the wind.
- Life: butterflies, a bird flock now and then, pollen in the sun, fireflies at dusk.
- Underground: smooth, textured roots; soft glow puffs and a note when a root drinks a dot.

### Journal (play test 1)
- The big menu is a leather notebook with cloth ribbon bookmarks (diary, pages to read again,
  settings) and page turns; hints are torn squared-notebook pages; every readout (life force,
  nutrients, day, hints, compass, buttons, stick) is handwritten on paper scraps.
- Handwriting fonts Caveat and Patrick Hand (OFL); CC0 photo textures for bark, leaves, ground and
  paper (credits in assets/CREDITS.md).

### Fixes from the reviews (selection)
- The sky was mirrored (the sun rose in the west); south is now +Z and the camera looks south.
- No stuck boost, stick or dive button after a page; a pinch or sun drag interrupted by a page is
  dropped; a boost held through sunset no longer dives; re-reading a page never starts a run.
- A root can no longer tunnel through overlapping rocks, get wedged, or drain life force while stuck.
- Saves: exact RNG state and growth carry-overs, atomic writes, broken saves kept aside, the
  morning and dawn burst survive a reload, time in the background is grown on resume.
- Performance: compressed photo textures with mipmaps, forest meshes cached per seed, grass fading
  with distance, range mesh builds, fixed-step simulation.

### Tools
- tools/autoplay.gd (two days with screenshots), tools/grow_shot.gd (--days, --hour),
  tools/month_report.gd; 255 headless tests.

### Known limits
- Grey-box underground rocks, meadow hints as separate meshes (draw calls), no forest LOD yet,
  the whole tree mesh is rebuilt while it grows (a phone will need a thread or incremental builds).
- The crown is still sparser than a real linden; the full light model (shadow grid) is a later step.

## v0.1 (2026-09-27)
Prototype 1: the whole loop as a grey box (seed, root runs, sapling, sun steering, dawn burst,
diary, meadow hints, placeholder ambience), 227 tests.
