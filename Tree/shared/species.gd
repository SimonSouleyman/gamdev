class_name Species
extends RefCounted
## Per-species profile (design doc section 15): needs, size, growth shape and ONE quirk on a
## mechanic the game already has. Every mechanic reads its species field; nothing checks a
## species name. Six broadleaves, unlocked in ORDER: finishing one tree unlocks the next.

const ORDER: Array[String] = ["linden", "birch", "beech", "sycamore", "alder", "oak"]

var id: String = "linden"
var display_name: String = "Linden"
var latin: String = "Tilia cordata"
## Real-life size cap in metres.
var max_height: float = 30.0
var max_crown_radius: float = 12.0
## Resource demand per unit of growth, indexed by Resources.Kind (water, N, P, K).
var needs: PackedFloat32Array = PackedFloat32Array([1.0, 0.8, 0.3, 0.4])

# --- growth shape ---------------------------------------------------------------
## Apical dominance 0..1 (Borchert-Honda lambda): high = tall and narrow, low = broad.
## Also the share of new markers placed just above the leader (GrowthSim).
var apical_dominance: float = 0.25
## How strongly shoots bend toward the sun.
var phototropism: float = 0.5
## Extra pull upward on every shoot: 0 = the linden's balance, > 0 straighter up (alder),
## < 0 flatter, spreading layers (beech).
var gravitropism: float = 0.0
## Hanging twig tips (birch): new shoots out in the crown bend down by this share of a segment.
var twig_droop: float = 0.0
## Extra random kink per segment (oak's zigzag branches), added to the colonizer's jitter.
var crookedness: float = 0.0
## Share of its height a grown tree stands on a bare trunk: the crown lifts as the lower
## branches are shaded out and shed (oak low and spreading, birch high).
var crown_base: float = 0.3

# --- pace and size ----------------------------------------------------------------
## Growth speed factor on GrowthSim.max_growth_per_second and the dawn burst.
var pace: float = 1.0
## Pace for the first `early_days` days (beech starts slow, birch fast).
var early_pace: float = 1.0
var early_days: int = 0
## The tree is finished (full species size) once it carries this many segments.
var finish_nodes: int = 2000
## Days the month report aims at (design doc section 15: about a month, birch 25, oak 35).
var target_days: int = 30

# --- the one quirk (neutral values = no quirk) ---------------------------------------
## Life force from the leaves, all day (birch's thin crown gives a little less).
var life_force: float = 1.0
## Life force factor in calm (unboosted) hours and while boosted (beech: patient).
var calm_life_force: float = 1.0
var boost_life_force: float = 1.0
## Blossom week (linden): days `blossom_from`..`blossom_to` the leaves give this much more.
var blossom_from: int = -1
var blossom_to: int = -1
var blossom_life_force: float = 1.0
## Shaded dieback speed: 1 = normal, 2 = twice as fast (birch), 0 = never (beech).
var shade_dieback: float = 1.0
## The leaves' daily water upkeep factor (beech: drought bites earlier).
var water_upkeep: float = 1.0
## Root cost factor in the topsoil (birch: pioneer).
var topsoil_root_cost: float = 1.0
## Share of the depth surcharge a root pays while it points downward (oak: taproot).
var down_depth_cost: float = 1.0
## Pruning a shoot tip makes it fork into two new shoots (sycamore: twin buds).
var twin_buds: bool = false
## Nitrogen the root nodules make each night, per metre of root (alder).
var nodule_nitrogen: float = 0.0
## How much more a root draws from a water deposit per contact (alder drains them faster).
var water_draw: float = 1.0

# --- look -------------------------------------------------------------------------
## Multiplies the bark photo (TreeView._bark_mat texture_tint) and the leaf sprays (tint_mul).
var bark_tint: Color = Color(0.38, 0.36, 0.33)
var leaf_tint: Color = Color(1.02, 1.03, 0.95)
## Bonsai (design doc section 16): a conifer carries scale-like foliage pads instead of leaf
## sprays; deadwood turns cut branches into silver jin and strips the trunk below into shari
## (the juniper's quirk). Only the juniper is a conifer; it never grows on the clearing.
var conifer: bool = false
var deadwood: bool = false
## The journal's species page: its look and its quirk, one handwritten line each.
var look_text: String = ""
var quirk_text: String = ""


static func linden() -> Species:
	var s := Species.new()
	s.finish_nodes = 2100
	s.blossom_from = 18
	s.blossom_to = 22
	s.blossom_life_force = 1.2
	s.look_text = "A broad, dense dome of heart-shaped leaves on grey bark that fissures with age."
	s.quirk_text = "Blossom week: around days 18 to 22 the bees come and the leaves give a fifth more life force."
	return s


static func birch() -> Species:
	var s := Species.new()
	s.id = "birch"
	s.display_name = "Silver birch"
	s.latin = "Betula pendula"
	s.max_height = 25.0
	s.max_crown_radius = 6.0
	s.needs = PackedFloat32Array([0.7, 0.4, 0.2, 0.3])
	s.apical_dominance = 0.3
	s.phototropism = 0.45
	s.twig_droop = 0.5
	s.crown_base = 0.36
	s.early_pace = 1.15
	s.early_days = 8
	s.finish_nodes = 1880
	s.target_days = 25
	s.life_force = 0.9
	s.shade_dieback = 2.0
	s.topsoil_root_cost = 0.7
	s.bark_tint = Color(0.95, 0.94, 0.9)
	s.leaf_tint = Color(1.06, 1.1, 0.86)
	s.look_text = "Slender and airy: white bark with black diamonds, hanging twig tips, small toothed leaves."
	s.quirk_text = "Pioneer: roots in the topsoil cost a third less, but shaded twigs die back twice as fast and the thin crown gives a little less life force."
	return s


static func beech() -> Species:
	var s := Species.new()
	s.id = "beech"
	s.display_name = "Beech"
	s.latin = "Fagus sylvatica"
	s.max_height = 35.0
	s.max_crown_radius = 12.0
	s.needs = PackedFloat32Array([0.9, 0.7, 0.4, 0.6])
	s.apical_dominance = 0.22
	s.phototropism = 0.3
	s.gravitropism = -0.15
	s.early_pace = 0.75  # slow start, but every early day still shows (0.6.x: 0.6 grew 16 segments on day 4)
	s.early_days = 10
	s.pace = 1.1
	s.finish_nodes = 2080
	s.shade_dieback = 0.0
	s.calm_life_force = 1.25
	s.boost_life_force = 0.7
	s.water_upkeep = 1.3
	s.bark_tint = Color(0.62, 0.63, 0.62)
	s.leaf_tint = Color(1.04, 1.1, 0.88)
	s.look_text = "Smooth silver-grey bark like an elephant's skin, flat layers of glossy oval leaves."
	s.quirk_text = "Patient: shaded branches never die back and calm hours give a quarter more life force, but a boost gives less and the leaves drink a third more water."
	return s


static func sycamore() -> Species:
	var s := Species.new()
	s.id = "sycamore"
	s.display_name = "Sycamore maple"
	s.latin = "Acer pseudoplatanus"
	s.max_height = 30.0
	s.max_crown_radius = 10.0
	s.needs = PackedFloat32Array([1.0, 1.0, 0.4, 0.6])
	s.apical_dominance = 0.25
	s.phototropism = 0.65
	s.early_pace = 1.1
	s.early_days = 8
	s.finish_nodes = 1800
	s.twin_buds = true
	s.bark_tint = Color(0.5, 0.45, 0.39)
	s.leaf_tint = Color(0.97, 1.03, 0.88)
	s.look_text = "Big five-lobed leaves on paired shoots, a rounded crown, bark flaking in plates."
	s.quirk_text = "Twin buds: cut a shoot tip and it forks into two, so the shears shape a denser crown. Hungry for nitrogen."
	return s


static func alder() -> Species:
	var s := Species.new()
	s.id = "alder"
	s.display_name = "Black alder"
	s.latin = "Alnus glutinosa"
	s.max_height = 25.0
	s.max_crown_radius = 6.0
	s.needs = PackedFloat32Array([1.4, 0.1, 0.5, 0.3])
	s.apical_dominance = 0.4
	s.phototropism = 0.35
	s.gravitropism = 0.3
	s.finish_nodes = 1740
	s.nodule_nitrogen = 0.01
	s.water_draw = 1.5
	s.bark_tint = Color(0.3, 0.28, 0.27)
	s.leaf_tint = Color(0.82, 0.9, 0.8)
	s.look_text = "A narrow cone on one straight leader, round dark leaves with a notched tip, small woody cones."
	s.quirk_text = "Nitrogen maker: root nodules make a little nitrogen every night, more the longer the roots; the thirsty roots drain water deposits faster."
	return s


static func oak() -> Species:
	var s := Species.new()
	s.id = "oak"
	s.display_name = "Pedunculate oak"
	s.latin = "Quercus robur"
	s.max_height = 30.0
	s.max_crown_radius = 14.0
	s.needs = PackedFloat32Array([0.8, 0.6, 0.5, 0.7])
	s.apical_dominance = 0.15
	s.phototropism = 0.4
	s.gravitropism = -0.05
	s.crookedness = 0.22
	s.crown_base = 0.2
	s.pace = 0.85
	s.finish_nodes = 1970
	s.target_days = 35
	s.down_depth_cost = 0.5
	s.bark_tint = Color(0.27, 0.24, 0.21)
	s.leaf_tint = Color(0.9, 0.97, 0.8)
	s.look_text = "A massive short trunk with deeply furrowed bark, gnarled zigzag branches, a wide crown of lobed leaves."
	s.quirk_text = "Taproot: a root pointing downward pays only half the depth surcharge, so deep water and the potassium by the rocks come within reach."
	return s


## The bonsai starter (section 16 E): the one conifer, never planted on the clearing (not in
## ORDER). Its needs and shape feed BonsaiSim; the tree-size values are never used.
static func juniper() -> Species:
	var s := Species.new()
	s.id = "juniper"
	s.display_name = "Juniper"
	s.latin = "Juniperus chinensis"
	s.max_height = 6.0
	s.max_crown_radius = 3.0
	s.needs = PackedFloat32Array([0.8, 0.6, 0.4, 0.5])
	s.apical_dominance = 0.2
	s.phototropism = 0.45
	s.gravitropism = -0.1
	s.crookedness = 0.12
	s.conifer = true
	s.deadwood = true
	s.bark_tint = Color(0.52, 0.36, 0.28)
	s.leaf_tint = Color(0.78, 0.9, 0.76)
	s.look_text = "Dense pads of tiny scale leaves, red-brown bark that peels in strips, silver deadwood."
	s.quirk_text = "Deadwood: a cut branch stays as a silver jin, and a cut close to the trunk strips a line of bark below it into shari."
	return s


## Growth pace on `day` (the first days may be slower or faster).
func pace_on(day: int) -> float:
	return early_pace if day < early_days else pace


func in_blossom(day: int) -> bool:
	return blossom_from >= 0 and day >= blossom_from and day <= blossom_to


## Factor on the life force the leaves gather: the species, calm or boosted, blossom week.
func life_force_factor(day: int, boosted: bool) -> float:
	var f := life_force * (boost_life_force if boosted else calm_life_force)
	if in_blossom(day):
		f *= blossom_life_force
	return f


## Title and body of the species page in the journal.
func page_title() -> String:
	return display_name


func page_body() -> String:
	return "%s (%s)\n\n%s" % [look_text, latin, quirk_text]


func to_dict() -> Dictionary:
	return {"id": id}


static func from_id(species_id: String) -> Species:
	match species_id:
		"birch":
			return birch()
		"beech":
			return beech()
		"sycamore":
			return sycamore()
		"alder":
			return alder()
		"oak":
			return oak()
		"juniper":
			return juniper()
		_:
			return linden()


static func all() -> Array[Species]:
	var out: Array[Species] = []
	for sid in ORDER:
		out.append(from_id(sid))
	return out


## Species the seed bag offers: the linden always, each other one once the one before it in
## ORDER has grown to its full size (`finished_ids`); every one with the test switch.
static func unlocked(finished_ids: Array, any_species: bool = false) -> Array[String]:
	var out: Array[String] = []
	for i in range(ORDER.size()):
		if any_species or i == 0 or finished_ids.has(ORDER[i - 1]):
			out.append(ORDER[i])
	return out
