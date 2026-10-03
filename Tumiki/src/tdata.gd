## Tumikis (bricks), tumiki sets, enemy specs, stage and field patterns,
## loaded from the original data files.
class_name TData

const SHAPE_STR := ["s", "ul", "ur", "dr", "dl", "u", "r", "d", "l", "pu", "pdr", "pr", "pur", "pd", "pf"]
const COLOR_STR := ["r", "g", "b", "y", "p", "a", "w", "gr"]
const BULLET_SHAPE_STR := ["b", "a", "r"]
const BULLET_COLOR_STR := ["r", "a", "p"]

const PROPELLER_SHAPE := 9
const PROPELLER_SHAPE_FRONT := 14
const PROPELLER_OFFSET := 2.2
const DAMAGED_COLOR := 6
const WOUNDED_COLOR := 0
const BULLET_SPEED_RATIO := 1.2

static var propeller_cnt := 0

static var _tumiki_sets := {}
static var _enemy_specs := {}


class Tumiki:
	var ofs: Vector2
	var size: Vector2
	var check_hit_size: Vector2
	var shape: int
	var color: int
	var barrage: Array = []

	func _init(sh: int, cl: int, x: float, y: float, sx: float, sy: float, ratio: float) -> void:
		shape = sh
		color = cl
		ofs = Vector2(x * ratio, y * ratio)
		size = Vector2(sx * ratio * 0.5 - 0.15, sy * ratio * 0.5 - 0.15)
		check_hit_size = Vector2(size.x + 0.7, size.y + 0.7)

	func add_top_bullet(idx: int, target, type: int):
		if barrage.is_empty() or idx >= barrage.size():
			return null
		return barrage[idx].add_top_bullet(target, type)

	func _draw_propeller(r: BlockRenderer, base: Transform3D, deg: float, shade: int, sz: float, front: bool, layer: int) -> void:
		var d: float = deg if front else (shape - PROPELLER_SHAPE) * PI / 4 + deg
		var t := base
		if front:
			t = t * Transform3D(Basis(Vector3(1, 0, 0), PI / 2), Vector3.ZERO)
		var ang := deg_to_rad(TData.propeller_cnt * 17.0 / size.x)
		t = t * Transform3D(Basis(Vector3(-sin(d), cos(d), 0).normalized(), ang), Vector3.ZERO)
		t = t * Transform3D(Basis(Vector3(0, 0, 1), d), Vector3.ZERO)
		var zs := (size.x + size.y) / 2 * sz
		var oy := zs if front else 0.0
		for side in [-1.0, 1.0]:
			var bt := t * Transform3D(Basis.from_scale(Vector3(size.x * sz, size.y * sz, zs)),
				Vector3(side * size.x * PROPELLER_OFFSET * sz, oy, oy))
			r.block_t(9, color, shade, bt, layer)

	## Draw rotated by `deg` around the set's origin `pos`.
	func draw_rot(r: BlockRenderer, pos: Vector2, z: float, shade: int, deg: float, sz: float, layer: int) -> void:
		var ox := (ofs.x * cos(deg) - ofs.y * sin(deg)) * sz
		var oy := (ofs.x * sin(deg) + ofs.y * cos(deg)) * sz
		if shape < PROPELLER_SHAPE:
			r.block(shape, color, shade, pos.x + ox, pos.y + oy, z, deg,
				size.x * sz, size.y * sz, (size.x + size.y) / 2 * sz, layer)
		else:
			var base := Transform3D(Basis.IDENTITY, Vector3(pos.x + ox, pos.y + oy, z))
			_draw_propeller(r, base, deg, shade, sz, shape == PROPELLER_SHAPE_FRONT, layer)

	func draw_xy(r: BlockRenderer, x: float, y: float, z: float, shade: int, damaged: bool, wounded: bool, layer: int, sc: float = 1.0) -> void:
		if shape < PROPELLER_SHAPE:
			var cl := color
			if damaged:
				cl = TData.DAMAGED_COLOR
			elif wounded:
				cl = TData.WOUNDED_COLOR
			r.block(shape, cl, shade, (x + ofs.x) * sc, (y + ofs.y) * sc, z * sc, 0,
				size.x * sc, size.y * sc, (size.x + size.y) / 2 * sc, layer)
		else:
			var base := Transform3D(Basis.from_scale(Vector3(sc, sc, sc)), Vector3.ZERO) \
				* Transform3D(Basis.IDENTITY, Vector3(x + ofs.x, y + ofs.y, z))
			_draw_propeller(r, base, 0, shade, 1, shape == PROPELLER_SHAPE_FRONT, layer)

	func check_hit(p: Vector2, px: float, py: float) -> bool:
		if shape != 0:
			return false
		var ox := p.x - px - ofs.x
		var oy := p.y - py - ofs.y
		return ox > -check_hit_size.x and ox < check_hit_size.x \
			and oy > -check_hit_size.y and oy < check_hit_size.y


class Barrage:
	var parser: Array = []
	var rank: Array = []
	var speed: Array = []
	var shape := 0
	var color := 0
	var size := 0.0
	var y_reverse := 1.0
	var prev_wait := 0
	var post_wait := 0

	func add_bml(fname: String, r: float, s: float) -> void:
		parser.append(BulletML.get_instance(fname))
		rank.append(r)
		speed.append(s)

	func add_top_bullet(target, type: int):
		if size <= 0:
			return null
		var cl := color
		var rev := 1.0
		if type == Bullets.SHIP:
			cl = 3
			rev = -1.0
		return Game.I.bullets.add_top_bullet(parser, rank, speed, 0, 0, PI / 2 * 3, 0,
			shape, cl, size, rev, y_reverse * rev, target, type, prev_wait, post_wait)


class TumikiSet:
	var tumiki: Array = []
	var score := 0
	var fire_score := 0
	var fire_score_interval := 0
	var size_xm := INF
	var size_xp := 0.0
	var size_ym := INF
	var size_yp := 0.0
	var size := 0.0

	func _init(fname: String) -> void:
		var si := U.Iter.new(U.read_csv("tumiki/" + fname))
		var ratio := si.next_f()
		score = si.next_i()
		fire_score = si.next_i()
		fire_score_interval = si.next_i()
		while si.has_next():
			var v := si.next()
			var shape: int = TData.SHAPE_STR.find(v)
			v = si.next()
			var color: int = maxi(TData.COLOR_STR.find(v), 0)
			var x := si.next_f()
			var y := si.next_f()
			var sx := si.next_f()
			var sy := si.next_f()
			var ti := Tumiki.new(shape, color, x, y, sx, sy, ratio)
			size_xp = maxf(size_xp, ti.ofs.x + ti.size.x)
			size_xm = minf(size_xm, ti.ofs.x - ti.size.x)
			size_yp = maxf(size_yp, ti.ofs.y + ti.size.y)
			size_ym = minf(size_ym, ti.ofs.y - ti.size.y)
			while true:
				v = si.next()
				if v == "e":
					break
				elif v == "s":
					ti.barrage.append(Barrage.new())
					continue
				var br := Barrage.new()
				br.shape = TData.BULLET_SHAPE_STR.find(v)
				br.color = maxi(TData.BULLET_COLOR_STR.find(si.next()), 0)
				br.size = si.next_f()
				br.y_reverse = si.next_f()
				br.prev_wait = si.next_i()
				br.post_wait = si.next_i()
				while true:
					var bml := si.next()
					if bml == "e":
						break
					var r := si.next_f()
					var s := si.next_f()
					br.add_bml(bml, r, s * TData.BULLET_SPEED_RATIO)
				ti.barrage.append(br)
			tumiki.append(ti)
		size = -size_xm + size_xp - size_ym + size_yp

	## Returns a list of [actor, tumiki] for all fired top bullets.
	func add_top_bullets(idx: int, target, type: int) -> Array:
		var res := []
		for t in tumiki:
			var ba = t.add_top_bullet(idx, target, type)
			if ba != null:
				res.append(TopBullet.new(ba, t))
		return res

	func break_into_fragments(x: float, y: float, d: float) -> void:
		for t in tumiki:
			var ox: float = t.ofs.x * cos(d) - t.ofs.y * sin(d)
			var oy: float = t.ofs.x * sin(d) + t.ofs.y * cos(d)
			var fr = Game.I.fragments.get_instance_forced()
			fr.set_frag(t.shape, t.color, x + ox, y + oy, t.size)

	func draw_rot(r: BlockRenderer, pos: Vector2, z: float, shade: int, deg: float, sz: float = 1.0, layer: int = 0) -> void:
		for t in tumiki:
			t.draw_rot(r, pos, z, shade, deg, sz, layer)

	func draw_xy(r: BlockRenderer, x: float, y: float, z: float, damaged: bool, wounded: bool, shade: int = 0, layer: int = 0, sc: float = 1.0) -> void:
		for t in tumiki:
			t.draw_xy(r, x, y, z, shade, damaged, wounded, layer, sc)

	func check_hit(p: Vector2, x: float, y: float) -> bool:
		for t in tumiki:
			if t.check_hit(p, x, y):
				return true
		return false


class TopBullet:
	var actor
	var tumiki: Tumiki
	var deactivated := false
	var cover_checked := false

	func _init(a, t: Tumiki) -> void:
		actor = a
		tumiki = t


static func tumiki_set(fname: String) -> TumikiSet:
	if not _tumiki_sets.has(fname):
		_tumiki_sets[fname] = TumikiSet.new(fname)
	return _tumiki_sets[fname]


class PartSpec:
	var tumiki_set: TumikiSet
	var ofs := Vector2.ZERO
	var shield := 0.0
	var destroyed_form_idx := 99999
	var damage_to_main_body := 0.0


class AttackForm:
	var shield: float
	var barrage_ptn_start_idx: int
	var attack_period: Array = []
	var break_period: Array = []


class EnemySpec:
	var parts: Array = []
	var attack_form: Array = []
	var size_xm := INF
	var size_xp := 0.0
	var size_ym := INF
	var size_yp := 0.0

	func _init(fname: String) -> void:
		var si := U.Iter.new(U.read_csv("enemy/" + fname))
		var body := PartSpec.new()
		body.tumiki_set = TData.tumiki_set(si.next())
		parts.append(body)
		var body_shield_set := false
		var ai := 0
		while true:
			var v := si.next()
			if v == "e":
				break
			var shield := v.to_float()
			if not body_shield_set:
				body.shield = shield
				body_shield_set = true
			var af := AttackForm.new()
			af.shield = shield
			af.barrage_ptn_start_idx = ai
			while true:
				v = si.next()
				if v == "e":
					break
				af.attack_period.append(v.to_int())
				af.break_period.append(si.next_i())
				ai += 1
			attack_form.append(af)
		while si.has_next():
			var ps := PartSpec.new()
			ps.tumiki_set = TData.tumiki_set(si.next())
			ps.ofs = Vector2(si.next_f(), si.next_f())
			ps.shield = si.next_f()
			ps.destroyed_form_idx = si.next_i()
			ps.damage_to_main_body = si.next_f()
			parts.append(ps)
		for p in parts:
			size_xp = maxf(size_xp, p.ofs.x + p.tumiki_set.size_xp)
			size_xm = minf(size_xm, p.ofs.x + p.tumiki_set.size_xm)
			size_yp = maxf(size_yp, p.ofs.y + p.tumiki_set.size_yp)
			size_ym = minf(size_ym, p.ofs.y + p.tumiki_set.size_ym)


static func enemy_spec(fname: String) -> EnemySpec:
	if not _enemy_specs.has(fname):
		_enemy_specs[fname] = EnemySpec.new(fname)
	return _enemy_specs[fname]


## Enemy movement patterns.
class MovePattern:
	var deg := 0.0
	var is_bml := false
	# BulletML movement
	var parser: BulletML
	var speed := 1.0
	# Points movement (keyed by barrage pattern index, -1 = basic)
	var point := {}
	var speeds := {}
	var withdraw_cnt := 0


class AppearancePattern:
	var start_time: int
	var duration: int
	var interval: int
	var pos_type: int  # 0 = front, 1 = top
	var pos: float
	var width: float
	var wait_till_destroyed: bool
	var spec: EnemySpec
	var move: MovePattern


class StagePattern:
	var pattern: Array = []
	var rand_seed := 0
	var warning_cnt := 0

	func _init(fname: String) -> void:
		var si := U.Iter.new(U.read_csv("stage/" + fname))
		rand_seed = si.next_i()
		warning_cnt = si.next_i()
		while si.has_next():
			var ap := AppearancePattern.new()
			ap.start_time = si.next_i()
			ap.duration = si.next_i()
			ap.interval = si.next_i()
			ap.pos_type = 0 if si.next() == "f" else 1
			ap.pos = si.next_f()
			ap.width = si.next_f()
			ap.wait_till_destroyed = si.next() == "y"
			ap.spec = TData.enemy_spec(si.next())
			var mp := MovePattern.new()
			var v := si.next()
			if v == "p":
				mp.withdraw_cnt = si.next_i()
				var at_idx := -1
				while true:
					mp.speeds[at_idx] = si.next_f()
					mp.point[at_idx] = []
					while true:
						v = si.next()
						if v == "e":
							break
						mp.point[at_idx].append(Vector2(v.to_float(), si.next_f()))
					v = si.next()
					if v == "e":
						break
					at_idx = v.to_int()
			else:
				mp.is_bml = true
				mp.parser = BulletML.get_instance(v)
				mp.speed = si.next_f()
			mp.deg = PI / 2 * 3 if ap.pos_type == 0 else PI
			ap.move = mp
			pattern.append(ap)


class FieldLine:
	var tumiki_set: Array = []
	var interval: Array = []
	var z := 0.0
	var cnt := 0
	var on_ground := false


class FieldPattern:
	var line: Array = []
	var rand_seed := 0
	var scroll_speed := 0.0
	var back: Color
	var ground: Color
	var mount_top: Color
	var mount_root: Color

	func _init(fname: String) -> void:
		var si := U.Iter.new(U.read_csv("field/" + fname))
		rand_seed = si.next_i()
		scroll_speed = si.next_f()
		back = Color(si.next_f(), si.next_f(), si.next_f())
		ground = Color(si.next_f(), si.next_f(), si.next_f())
		mount_top = Color(si.next_f(), si.next_f(), si.next_f())
		mount_root = Color((back.r * 2 + ground.r) / 3, (back.g * 2 + ground.g) / 3, (back.b * 2 + ground.b) / 3)
		while si.has_next():
			var z := si.next_f()
			var fl := FieldLine.new()
			if z > 0:
				fl.z = -z
				fl.on_ground = true
			else:
				fl.z = z
			while true:
				var v := si.next()
				if v == "e":
					break
				fl.interval.append(v.to_int())
			while true:
				var v := si.next()
				if v == "e":
					break
				fl.tumiki_set.append(TData.tumiki_set(v))
			line.append(fl)
