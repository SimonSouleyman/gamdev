extends RefCounted
var t


func test_spend_all_or_nothing() -> void:
	var r := Resources.new()
	r.add(Resources.Kind.WATER, 2.0)
	r.add(Resources.Kind.NITROGEN, 0.5)
	var ok := r.try_spend({Resources.Kind.WATER: 1.0, Resources.Kind.NITROGEN: 1.0})
	t.check(not ok, "refused when nitrogen short")
	t.check_near(r.amount(Resources.Kind.WATER), 2.0, 1e-6, "water untouched")
	t.check(r.try_spend({Resources.Kind.WATER: 1.0, Resources.Kind.NITROGEN: 0.5}), "affordable bundle")
	t.check_near(r.amount(Resources.Kind.NITROGEN), 0.0, 1e-6, "nitrogen spent")


func test_liebig_soft_minimum() -> void:
	var needs := PackedFloat32Array([1, 1, 1, 1])
	t.check_near(Resources.growth_factor(PackedFloat32Array([5, 5, 5, 5]), needs), 1.0, 1e-6, "plenty = full")
	t.check_near(Resources.growth_factor(PackedFloat32Array([5, 0.5, 5, 5]), needs), 0.5, 1e-6, "scarcest limits")
	t.check_near(Resources.growth_factor(PackedFloat32Array([0, 0, 0, 0]), needs), 0.35, 1e-6, "never zero: a shortage slows to about a third")
	t.check_near(Resources.growth_factor(PackedFloat32Array([0, 9, 9, 9]), PackedFloat32Array([0, 1, 1, 1])), 1.0, 1e-6, "unneeded kinds ignored")


func test_life_force() -> void:
	var r := Resources.new()
	r.life_force = 1.0
	t.check(not r.try_spend_life_force(2.0), "too much")
	t.check(r.try_spend_life_force(1.0), "exact")
	t.check_near(r.life_force, 0.0, 1e-6, "empty")
