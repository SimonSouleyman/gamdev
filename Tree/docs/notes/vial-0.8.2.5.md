# Life force as a green vial (0.8.2.5)

Item 4 of `docs/specs/wish-compass-vial.md`. Simon (2026-10-02): "die lebenskraft sollte nicht als
Zahl sichtbar sein, sondern ein bisschen wie eine Manaphiole aus rollenspielen wie diablo. runde
glasflasche sozusagen. flüssigkeit in grün."

## What changed

- **`ui/vial.gd` + `ui/vial.gdshader`**: a round glass flask with green liquid, a cork in a glass
  lip, twine under the lip and a small paper tag with an ink leaf (no number). The glass, liquid,
  cork and pencil mark are painted by one canvas shader, so the Compatibility renderer (the phone)
  draws it like the Mobile one; the tag is drawn in `_draw`. Light from the upper left, an ink
  edge and a soft shadow, like the rendered HUD pictures and the compass.
- **The liquid moves calmly**: its level follows the life force on a damped spring (it lags a
  moment, overshoots a hair and settles), the surface tilts against a change and swings back,
  small waves and a slow swirl and a few drifting motes keep it alive without fuss. Its surface is
  seen a little from above (a lighter ellipse with a meniscus line).
- **By day** (`Vial.track_day`): full is what a calm day would hold at sunset (the calm life
  force so far plus a calm rest of the day: the sim's own sum without the boost, integrated over
  the sun's arc). So a calm day starts near empty and ends full.
- **The pencil mark**: the vial keeps "what a calm day would have gathered by now": calm hours add
  the real gain, boosted hours add the calm rate (`Vial.calm_rate`) over the sun they had. Once
  that stands more than 1.2 % of the vial above the liquid, a silvery graphite line with two small
  ticks shows on the glass. The gap is the boost's cost and stays for the rest of the day.
- **By night** (`Vial.track_night` in the root view): the vial keeps the day's scale (the static
  `Vial.day_capacity`) and falls as the root spends life force; an empty vial is the end of the
  run, and what is left when "let roots spread" is pressed is what goes into small roots. A game
  loaded at night uses the night's start as full.
- **HUD**: the vial sits in the top left corner where the life force scrap was (the day scrap and
  the nutrient scraps moved right beside it; the nutrient row's gap is 6 instead of 10). Underground
  the life bar and "life force N" are gone; the haul's scrap ("tonight:" and the four counts) sits
  beside the vial and shows only while the run or its end shows. No tap target before or after.
- **Journal**: the sapling page says the vial fills slower than its pencil mark while boosted,
  the first night that each metre drinks life force from the vial, the quiet night that calm days fill
  the vial. No page has a number for life force.
- The game has no debug overlay with the life force number (only the perf log); nothing to keep.

## Checks

- `tests/test_vial.gd`: the sun integral, a calm day fills without a mark and ends full, a boosted
  hour opens a gap that stays (broken 50), the vial falls in the run with no mark, and no label on
  the day or night HUD reads "life force <number>" (broken 49).
- `tools/vial_shot.gd` (phone look, 450x1000, `--rendering-method gl_compatibility -- --phone`):
  shots in `GameDev/tree-qa/vial-0.8.2.5/`: a calm day early, at noon and at sunset, a boosted
  day during and after the boost (with close-ups of the vial and its mark), the night's pick, the
  run starting, half spent, nearly empty, and the night's end.

## Open

- The mark state lives in the view: after a reload mid-day the mark starts again at the liquid.
- With large nutrient numbers (two digits each) the nutrient row reaches close to the compass.
- Simon to judge the look on the phone: size (about 11 mm wide on a 1080-wide phone), the mark's
  weight, the glow of the liquid underground.
