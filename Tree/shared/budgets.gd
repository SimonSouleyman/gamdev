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
const FOREST_TREES: int = 110
const FOREST_VARIANT_NODES: int = 320
const FOREST_BUSHES: int = 60
const MEADOW_FLOWERS: int = 1500
## Max attraction markers alive in the tree canopy at once.
const TREE_MARKERS: int = 2000
## Max main roots (one per night) a tree may grow; roughly a month plus spare nights.
const MAX_MAIN_ROOTS: int = 40
