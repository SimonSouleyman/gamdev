class_name Budgets
extends RefCounted
## Hard limits that keep the game affordable on a phone. Enforced in code.

## Max internodes (segments) of the tree plant graph.
const TREE_MAX_NODES: int = 3000
## Max nodes of one main root the player steers.
const ROOT_MAX_NODES_PER_MAIN_ROOT: int = 400
## Max automatic fine-root nodes that may sprout around one main root.
const FINE_ROOTS_PER_MAIN_ROOT: int = 150
## Upper limit when the run ends early and the leftover life force feeds more fine roots.
const FINE_ROOTS_MAX_PER_MAIN_ROOT: int = 600
## Max nutrient dots kept in memory for the underground at once.
const NUTRIENT_DOTS_LOADED: int = 4000
## The forest around the clearing (rendering budgets: every tree is instanced from a few variants).
## A phone gets about half: measured on a Fairphone 6, the forest and the meadow cards cost the
## most (alpha-tested overdraw).
## A PC run with `-- --phone` takes the phone path too (add `--rendering-method gl_compatibility`
## for the phone's renderer), so phone costs can be measured and photographed on a PC.
static var PHONE: bool = OS.has_feature("mobile") or OS.get_cmdline_user_args().has("--phone")
static var FOREST_TREES: int = 70 if PHONE else 140
## A phone draws the forest ring and the shrub belt as baked cards (ForestImpostors): the 3D
## forest cost the Fairphone 6 about 8 of its 30 frames a second (0.6 measurement).
static var FOREST_IMPOSTORS: bool = PHONE
## With cards, this many of the innermost trees stay real 3D trees, spread around the ring.
static var FOREST_REAL_TREES: int = 5
static var FOREST_VARIANT_NODES: int = 240 if PHONE else 320
static var FOREST_BUSHES: int = 110 if PHONE else 260
static var MEADOW_FLOWERS: int = 500 if PHONE else 1500
static var MEADOW_GRASS_CLUMPS: int = 2200 if PHONE else 9500
static var MEADOW_HERB_CLUMPS: int = 350 if PHONE else 900
static var MEADOW_VARIETY_CLUMPS: int = 450 if PHONE else 2800
## Shade plants under the crown (the living clearing) and mushroom groups after rain.
static var UNDERSTORY_PLANTS: int = 300 if PHONE else 1100
static var UNDERSTORY_MUSHROOMS: int = 12 if PHONE else 30
## Leaf sprays per forest leaf cluster, and edge herb cards.
static var FOREST_SPRAYS: int = 2 if PHONE else 5
static var EDGE_HERBS: int = 160 if PHONE else 1100
static var EDGE_FLOWERS: int = 130 if PHONE else 500
## Share of each shrub's leaf clusters a phone draws.
static var SHRUB_DENSITY: float = 0.5 if PHONE else 1.0
## Frame cap: a steady 30 on a phone saves battery.
static var MAX_FPS: int = 30 if PHONE else 0
## Max attraction markers alive in the tree canopy at once.
const TREE_MARKERS: int = 2000
## Max main roots (one per night) a tree may grow; roughly a month plus spare nights.
const MAX_MAIN_ROOTS: int = 40
