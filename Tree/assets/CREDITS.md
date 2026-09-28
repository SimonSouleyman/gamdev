# Asset credits

All third-party assets in this project, with their licences. CC0 needs no attribution; it is listed anyway so the source is known.

| File(s) | Source | Licence |
|---|---|---|
| `assets/bark/tree_bark_03_*_2k.jpg` | Poly Haven, "Tree Bark 03" (https://polyhaven.com/a/tree_bark_03) | CC0 |
| `assets/wood/weathered_planks_*`, `old_planks_02_*`, `wood_table_worn_*` (1k) | Poly Haven, "Weathered Planks", "Old Planks 02", "Wood Table Worn" (https://polyhaven.com) | CC0 |
| `assets/leaves/LeafSet004_1K-JPG_*` | ambientCG, "Leaf Set 004" (https://ambientcg.com/view?id=LeafSet004) | CC0 |
| `assets/ground/Grass004_1K-JPG_*` | ambientCG, "Grass 004" (https://ambientcg.com/view?id=Grass004) | CC0 |
| `assets/ground/forest_leaves_04_diff_1k.jpg` (leaf litter under the crown's shade) | Poly Haven, "Forest Leaves 04" (https://polyhaven.com/a/forest_leaves_04) | CC0 |
| `assets/paper/paper_cream.png`, `paper_grid.png`, `paper_beige.png` (downscaled from Papier13, 11, 7) | OpenGameArt, "Paper Textures (seamless)" (https://opengameart.org/content/paper-textures-seamless) | CC0 |
| `assets/sounds/forest_ambience.mp3` | OpenGameArt, "Forest Ambience" by TinyWorlds (https://opengameart.org/content/forest-ambience) | CC0 |
| `assets/sounds/birds.ogg` | OpenGameArt, "Ambient Bird Sounds" by isaiah658 (https://opengameart.org/content/ambient-bird-sounds) | CC0 |
| `assets/sounds/crickets.mp3` | OpenGameArt, "Crickets Ambient Noise - loopable" by Wolfgang_ (https://opengameart.org/content/crickets-ambient-noise-loopable) | CC0 |
| `assets/sounds/water_flowing.ogg` | OpenGameArt, "30 CC0 SFX loops" by rubberduck (https://opengameart.org/content/30-cc0-sfx-loops) | CC0 |
| `tools/icon_models/Camera_01/` (rendered to `ui/icons/camera.png`) | Poly Haven, "Camera 01" by Rajil Jose Macatangay (https://polyhaven.com/a/Camera_01) | CC0 |
| `tools/icon_models/seadogs_compass/` (rendered to `ui/icons/compass_*.png`) | Poly Haven, "Seadogs Compass" by Benny Weimer (https://polyhaven.com/a/seadogs_compass) | CC0 |
| `tools/icon_models/book_encyclopedia_set_01/` (one volume rendered to `ui/icons/journal.png`) | Poly Haven, "Book Encyclopedia Set 01" by John Malcolm (https://polyhaven.com/a/book_encyclopedia_set_01) | CC0 |
| `ui/fonts/Caveat-Regular.ttf` | Google Fonts, Caveat by Impallari Type | SIL Open Font License 1.1 (`ui/fonts/OFL-Caveat.txt`) |
| `ui/fonts/PatrickHand-Regular.ttf` | Google Fonts, Patrick Hand by Patrick Wagesreiter | SIL Open Font License 1.1 (`ui/fonts/OFL-PatrickHand.txt`) |

The HUD's shed and shears pictures (`ui/icons/shed.png`, `shears.png`, `shears_cursor.png`) are small models built in `tools/render_icons.gd` from the Poly Haven wood textures above. Everything else (tree, roots, meadow plants, leaf fallback, paper, the wind, the underground hum and the collect note) is generated in code.
| `assets/shed/WoodenTable_03/*` (1k glTF) | Poly Haven, "Wooden Table 03" (https://polyhaven.com/a/WoodenTable_03): the workbench | CC0 |
| `assets/shed/garden_gloves_01/*` (1k glTF) | Poly Haven, "Garden Gloves 01" (https://polyhaven.com/a/garden_gloves_01) | CC0 |
| `assets/shed/planter_pot_clay/*` (1k glTF) | Poly Haven, "Planter Pot Clay" (https://polyhaven.com/a/planter_pot_clay): the flower pot | CC0 |
| `assets/shed/watering_can_metal_01/*` (1k glTF) | Poly Haven, "Watering Can Metal 01" (https://polyhaven.com/a/watering_can_metal_01) | CC0 |
| `assets/shed/trowel_01/*` (1k glTF) | Poly Haven, "Trowel 01" (https://polyhaven.com/a/trowel_01) | CC0 |
| `assets/sounds/shed_book_open.ogg`, `shed_book_flip.ogg`, `shed_gloves.ogg`, `shed_pin.ogg`, `shed_door.ogg` (bookOpen, bookFlip2, cloth2, metalClick, creak1) | OpenGameArt, "50 RPG sound effects" by Kenney (https://opengameart.org/content/50-rpg-sound-effects) | CC0 |
| `assets/sounds/shed_paper_bag.wav` (snd_use_map, cut to 1.3 s, mono) | OpenGameArt, "Opening and closing a map sounds" by spring-spring (https://opengameart.org/content/opening-and-closing-a-map-sounds) | CC0 |
| `assets/sounds/shed_clay_pot.ogg` (item_stone_02) | OpenGameArt, "80 CC0 RPG SFX" by rubberduck (https://opengameart.org/content/80-cc0-rpg-sfx) | CC0 |
| `assets/bonsai/Gravel022_Color.jpg`, `Gravel022_NormalGL.jpg` (downscaled to 512) | ambientCG, "Gravel 022" (https://ambientcg.com/view?id=Gravel022): the bonsai's fine gravel | CC0 |
| `assets/bonsai/Moss002_Color.jpg`, `Moss002_NormalGL.jpg` (downscaled to 512) | ambientCG, "Moss 002" (https://ambientcg.com/view?id=Moss002): moss cushions on the bonsai's soil | CC0 |

The bonsai reuses the Poly Haven clay planter (nursery pot) and watering can above and the sounds above (clay pot, metal click, paper bag, water). Its juniper foliage atlas (`lookdev/bonsai/make_juniper.py`) and the style pages (`lookdev/bonsai/make_styles.py`) are painted by Pillow scripts; the glazed pots, the fertiliser tin, the wire coils and the root ball are built in code.
Everything else (the shed room, the journal, album and seed bag on the workbench, tree, roots, meadow plants, leaf fallback, paper, the wind, the underground hum and the collect note) is generated in code.
Everything else (tree, roots, meadow plants, the shade plants and mushrooms of `lookdev/grass/understory_atlas.png`, leaf fallback, paper, the wind, the underground hum and the collect note) is generated in code.
| `assets/sounds/rain.ogg` (track 1 of the pack) | OpenGameArt, "Rain (loopable)" by Ylmir (https://opengameart.org/content/rain-loopable) | CC0 |
| `assets/sounds/thunder.ogg` (`sfx100v2_thunder_01.ogg`) | OpenGameArt, "100 CC0 SFX #2" by rubberduck (https://opengameart.org/content/100-cc0-sfx-2) | CC0 |
Everything else (tree, roots, meadow plants, leaf fallback, paper, the wind, the underground hum, the collect note, the stars, the moon and the falling leaves) is generated in code.
