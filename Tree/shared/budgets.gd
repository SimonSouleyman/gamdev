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
static var PHONE: bool = OS.has_feature("mobile")
static var FOREST_TREES: int = 26 if PHONE else 140
static var FOREST_VARIANT_NODES: int = 240 if PHONE else 320
static var FOREST_BUSHES: int = 50 if PHONE else 260
static var MEADOW_FLOWERS: int = 500 if PHONE else 1500
static var MEADOW_GRASS_CLUMPS: int = 2200 if PHONE else 9500
static var MEADOW_HERB_CLUMPS: int = 350 if PHONE else 900
static var MEADOW_VARIETY_CLUMPS: int = 450 if PHONE else 2800
## Leaf sprays per forest leaf cluster, and edge herb cards.
static var FOREST_SPRAYS: int = 2 if PHONE else 5
static var EDGE_HERBS: int = 160 if PHONE else 1100
static var EDGE_FLOWERS: int = 130 if PHONE else 500
## Share of each shrub's leaf clusters a phone draws.
static var SHRUB_DENSITY: float = 0.5 if PHONE else 1.0
## Frame cap: a steady 30 on a phone saves battery.
static var MAX_FPS: int = 30 if PHONE else 0
## The bonsai (design doc section 16): the pot's volume sets how many living segments it carries
## (between these two); the graph itself never holds more than BONSAI_MAX_NODES (cut wood is
## compacted away, silver deadwood stays).
const BONSAI_MIN_POT_NODES: int = 400
const BONSAI_MAX_POT_NODES: int = 600
const BONSAI_MAX_NODES: int = 800
## Juniper sprays per green twig (a phone draws fewer).
static var BONSAI_SPRAYS_PER_TWIG: int = 2 if PHONE else 3
## Attraction markers alive around the bonsai's crown at once.
const BONSAI_MARKERS: int = 220
## Max attraction markers alive in the tree canopy at once.
const TREE_MARKERS: int = 2000
## Max main roots (one per night) a tree may grow; roughly a month plus spare nights.
const MAX_MAIN_ROOTS: int = 40
