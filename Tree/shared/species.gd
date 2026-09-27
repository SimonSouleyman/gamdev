class_name Species
extends RefCounted
## Per-species growth profile. First species: linden (Tilia).

var id: String = "linden"
var display_name: String = "Linden"
## Real-life size cap in metres.
var max_height: float = 30.0
var max_crown_radius: float = 12.0
## Resource demand per unit of growth, indexed by Resources.Kind (water, N, P, K).
var needs: PackedFloat32Array = PackedFloat32Array([1.0, 0.8, 0.3, 0.4])
## Apical dominance 0..1 (Borchert-Honda lambda): high = tall and narrow, low = broad.
## Also the share of new markers placed just above the leader (GrowthSim).
var apical_dominance: float = 0.15
## How strongly shoots bend toward the sun.
var phototropism: float = 0.5
## How strongly shoots resist gravity (negative = droop).
var gravitropism: float = 0.15


static func linden() -> Species:
	return Species.new()


static func birch() -> Species:
	var s := Species.new()
	s.id = "birch"
	s.display_name = "Birch"
	s.max_height = 25.0
	s.max_crown_radius = 6.0
	s.needs = PackedFloat32Array([0.7, 0.4, 0.2, 0.3])
	s.apical_dominance = 0.3
	s.phototropism = 0.45
	return s


func to_dict() -> Dictionary:
	return {"id": id}


static func from_id(species_id: String) -> Species:
	match species_id:
		"birch":
			return birch()
		_:
			return linden()
