# Paper UI: what makes old journals and game journals look real

Reference study for the journal / paper-scrap UI (look-dev in `lookdev/paper/`). I only looked at
these sources. No screenshots from them are in the repository.

## Real notebooks, field journals, herbarium sheets

- **Naturalists' field notes** (Biodiversity Heritage Library Field Notes Project,
  https://www.biodiversitylibrary.org/collection/FieldNotesProject ;
  https://blog.biodiversitylibrary.org/2017/01/introducing-bhl-field-notes-project.html ;
  Linnean Society, https://www.linnean.org/research-collections/on-display/naturalists-notebooks)
  - The paper is never one flat colour: it yellows in soft blotches, more at the edges and
    wherever thumbs held it. The rim is visibly darker and greyer than the middle.
  - Pencil and ink sit *in* the paper: strokes lighten where the nib ran dry and darken where it
    paused. Ink feathers a little into soft paper.
  - Pages cockle (a low, wide waviness from damp) rather than crumple. A bound page curves down
    into the gutter, which is the darkest part of an open book.
  - Things are stuck in: pressed leaves held by strips of yellowed, translucent tape, specimen
    labels, clippings. They cast thin, close shadows because they lie almost flat.
- **Foxing** (AIC conservation wiki, https://conservation-wiki.com/wiki/BPG_Foxing ; CCAHA
  terminology, https://ccaha.org/resources/paper-conservation-terminology)
  - Reddish-brown spots, from pin-pricks to a few mm, sometimes "bullseye" (dark core, paler
    halo). They cluster, more near edges and the gutter where damp gets in, not evenly spread.
  - **Tide lines**: a spill dries into a pale area with a darker, wavy ring at its edge.
- **Torn-out pages**: the tear is ragged at two scales (big lazy wobble plus fine jags). The
  torn edge is *paler* than the page, because the tear splits the sheet and shows fresh
  fibres, and a few fibres stick out past the edge.
- **Crumpled then flattened paper**: a network of straight crease segments, with the facets
  between them nearly flat. The light picks each facet out with a slightly different
  brightness, and every crease has a sharp crest line. A sheet folded to be carried has one or
  two strong fold lines, with the halves tipped in opposite directions.

## Games with similar UI

- **Red Dead Redemption 2, Arthur's journal** (https://reddead.fandom.com/wiki/Journal_(RDR_2) ;
  https://www.pcgamer.com/games/action/the-coolest-in-game-art-is-in-arthur-morgans-journal-in-red-dead-redemption-2-and-no-you-cant-convince-me-otherwise/)
  Cream pages with gutter shading, pencil sketches that are grey and uneven, handwriting that
  varies in pressure, and pasted-in bits. The cover is worn leather. The page itself stays calm
  so the drawings carry it.
- **Botany Manor** (https://www.botanymanor.com/ ; https://en.wikipedia.org/wiki/Botany_Manor)
  Herbarium sheets and notes: pressed specimens with their shadows, labels, stains and aged
  edges, laid on surfaces that give real cast shadows.
- **Pentiment** (https://www.gamedeveloper.com/art/deep-dive-the-art-of-pentiment ;
  https://www.pcgamer.com/how-pentiments-hand-crafted-fonts-give-pen-and-ink-a-voice/)
  The ink is alive: letters are written in stroke by stroke, blotted and corrected. The lesson
  here is that the ink is part of the paper's material, not a label on top of it.
- **Return of the Obra Dinn** (https://www.gameuidatabase.com/gameData.php?id=1460)
  The logbook works because of restraint: a limited palette, a strong page structure, and ink
  that matches the book.
- **Heaven's Vault** (https://en.wikipedia.org/wiki/Heaven%27s_Vault), **Firewatch** notes
  (https://www.gamepressure.com/firewatch/notes/z2852c), **Assassin's Creed II** codex pages
  (https://strategywiki.org/wiki/Assassin's_Creed_II/Collection) and **Sable**: loose sheets with
  folds, pinned or clipped notes, handwriting with character, and paper that is lit, not
  printed flat.
- **Technique**: paper shader walkthrough (https://gamedevbill.com/paper-shader-in-unity/).
  It uses a few straight crease lines plus a normal map, darkened along the creases. We do the
  2D version: a normal map lit by one fixed light.

## What we took from this (implemented in `lookdev/paper/`)

| Real-world cue | How it is done |
|---|---|
| Crumples and creases | A generated normal map made of straight mountain and valley crease segments at three scales (`crumple_normal.png`), lit by one fixed top-left light in `paper_sheet.gdshader`. Its alpha holds the crease sharpness, which draws the fine crest lines. |
| Carried-folded page | Up to two fold lines per sheet. The halves tip in opposite directions and each crease gets a thin dark line. |
| Fibre, tooth | A fibre strand and grain channel (`paper_detail.png` R and A) modulates the paper colour. |
| Foxing, age | Clustered rust spots (G) that are denser near the edges, blotchy yellowing (B), and a darker, greyer rim that looks handled. |
| Tide-line stain | An optional wavy ring with a pale fill (seeded, on some sheets). |
| Torn edges | Two-scale ragged outline, a pale band of fresh fibre, and loose fibre whiskers past the tear. Cut edges get a tiny wobble and nicks. |
| Ink | `paper_ink.gdshader`: pressure varies along the line, the nib skips on the tooth, the stroke rim is darker, and a slight feathered bleed. |
| Cast shadow, curl | One pass per sheet. A soft offset shadow plus a thin contact shadow. A lifted corner throws a longer shadow and catches the light differently. |
| Bound page | Gutter shading, and the page turns away from the light towards the binding. |
| Pasted-in things | A pressed dried linden leaf (recoloured CC0 scan), translucent torn tape, and a steel paper clip. |
| Leather cover | Pebbled grain normal map, rubbed pale edges, stitched groove, spine crease. |

Not done yet (ideas): graphite smudges and erased pencil, photo corners, a page-turn curl
animation using the same shader (animate `curl`), and the ink writing itself in stroke by stroke.
