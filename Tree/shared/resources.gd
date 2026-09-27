class_name Resources
extends RefCounted
## The four underground resources plus life force. See design doc "Resources".

enum Kind { WATER, NITROGEN, PHOSPHORUS, POTASSIUM }

const KIND_NAMES: Array[String] = ["water", "nitrogen", "phosphorus", "potassium"]
## Proposal colours from the design doc, for the dots and the UI.
const KIND_COLORS: Array[Color] = [
	Color(0.35, 0.6, 1.0),   # water: blue
	Color(0.4, 0.9, 0.35),   # nitrogen: green
	Color(1.0, 0.6, 0.2),    # phosphorus: warm orange
	Color(0.7, 0.4, 0.95),   # potassium: violet
]

## Stored amounts, indexed by Kind.
var stock: PackedFloat32Array = PackedFloat32Array([0, 0, 0, 0])
var life_force: float = 0.0


func add(kind: int, amount: float) -> void:
	stock[kind] += amount


func amount(kind: int) -> float:
	return stock[kind]


## Tries to spend a bundle {Kind: amount}. Returns false and spends nothing if any is short.
func try_spend(cost: Dictionary) -> bool:
	for kind in cost:
		if stock[kind] < float(cost[kind]):
			return false
	for kind in cost:
		stock[kind] -= float(cost[kind])
	return true


func try_spend_life_force(amount_needed: float) -> bool:
	if life_force < amount_needed:
		return false
	life_force -= amount_needed
	return true


## Soft Liebig's law: growth factor 0..1 from the scarcest resource relative to the
## species' need. `needs` = per-kind demand for one unit of growth. A shortage slows
## growth down but never stops it (floor).
static func growth_factor(stock_in: PackedFloat32Array, needs: PackedFloat32Array, floor_value: float = 0.35) -> float:
	var worst := 1.0
	for k in range(4):
		if needs[k] <= 0.0:
			continue
		var ratio: float = clampf(stock_in[k] / needs[k], 0.0, 1.0)
		worst = minf(worst, ratio)
	return maxf(floor_value, worst)


func to_dict() -> Dictionary:
	return {"stock": stock, "life_force": life_force}


static func from_dict(d: Dictionary) -> Resources:
	var r := Resources.new()
	r.stock = PackedFloat32Array(d.get("stock", [0, 0, 0, 0]))
	r.life_force = float(d.get("life_force", 0.0))
	return r
