# 0.8.2.5: the wish as a place, the compass needle, steering that pays (items 1 to 3)

Built from specs/wish-compass-vial.md items 1 to 3 (broken items 45 to 48). Item 4 (the life
force vial) is a separate branch.

## 1. The wish is a place

- `Diary.underground_share` 1.0: every morning has a wish place. `DAY_WISHES` and
  `DAY_WISH_LINES` are gone. Where no new deposit can be placed (rock all round, the field's rim,
  or the soil's dot budget full, which a 40-day soil with nothing collected reaches by day 28 to
  30), the wish points at a deposit already in the soil (`Diary.fallback_patch`): the nearest
  untouched wish deposit of the kind, then of any kind, then the nearest plain deposit with most
  of its dots fresh, never the starter patch, never deeper than the meadow shows. Such a fallback
  morning does not count in the running far share (those mornings were day wishes before), so
  half of the new wishes still go far.
- Journal line: "Wish: the clover." / "Wish: the clover, far away." / "Still: ..." The morning
  scrap: "Today, find what feeds the clover." No direction anywhere.
- `tree/wish_plant.gd` (WishPlant, a Meadow): the plant over today's wish deposit, its stand 1.6x
  the meadow sign's radius, the plants 1.6x as large (clover 2.6x, so its heads stand over the
  meadow grass), in flower (brown rush tufts, pink or white clover heads, nettle tassels, violet
  comfrey bells), three butterflies and a faint shimmer of motes by day. A far wish beyond the
  clearing shows at the clearing's edge (EDGE_INSET inside) in its direction, not made smaller.
  Rebuilt at sunrise and on load; hidden once the wish is reached.
- Ink ring: drawn round the stand in 0.7 s at the sunset event and again as the dive starts, holds,
  fades over 2.6 s in all. Dark ink with a pale paper edge (the plain ink was lost on the dusk
  grass). It is drawn without a depth test, so for that moment it shows over the sapling's trunk.
- First-time page "The compass": "The needle points to what the tree wants." on the first
  morning with a wish place after the sapling page. To stay within the journal's word budget
  (test: a third of 0.8's), the sunset page lost five words ("to the roots", "a ... wish").
- Old saves: a day wish saved before 0.8.2.5 keeps its line on its page (legacy topic), the
  needle rests that day, the next morning has a place. No save format change except the new
  "drank" flag (default false).

## 2. The compass needle

`ui/compass.gd`: the dial still turns with the camera (N on the dial is north); the needle points
along `target` (`Compass.needle_angle_for(camera basis, way)`), a damped spring as before. By day
the way is trunk to today's wish deposit (`Compass.wish_way`); in the root view
(`RootView.wish_way`) from the growing tip during a run, else the newest root tip, to tonight's
glow. With no wish (reached) it drifts 0.5 rad on slowly and rests.

## 3. Steering pays

- `Species.liebig_floor` 0.45, oak 0.55; `GrowthSim.liebig_floor` is now an override for tools
  (-1: the species'). The seedling floor (0.6, first 3 days) stays.
- "The roots drank well." the morning after a night whose main root came within a deposit's
  radius (any patch but the starter one; `GameState.run_reached_deposit`, flag `drank`, saved).
  Topic "drank": the day page's third place after find, visitor and milestone, before the
  weather, so the page keeps at most three lines.

## Short check (2026-10-02)

- Tests: new `tests/test_wish_0825.gd` (broken items 45 to 48); test_wish, test_journal and
  test_side_roots updated for no day wishes. Full suite run once, then the touched suites.
- `tools/autoplay.gd` exit 0.
- `tools/strategies.gd --seed=14 --strats=dots,end_early,wish`: linden 30 / 40 / 29 (wish
  follower 24 of 24 reached), oak 33 / 42 / 32 (26 of 26). As the spec's sim.
- Phone look (`tools/wish_place_shot.gd`, gl_compatibility, 450x1000, `--phone`), shots in
  GameDev/tree-qa/wish-0.8.2.5: day 6 seed 42, a clover wish 13.6 m out reads as a pink and white
  flowering stand behind the sapling; the needle points at it, a little right of straight up
  as the stand lies (wish_day_compass crop); the ink ring at sunset reads clearly.
- For Simon on the phone: is the stand big enough from the usual camera, and do the butterflies
  show at all at that distance (they are 11 cm)?
