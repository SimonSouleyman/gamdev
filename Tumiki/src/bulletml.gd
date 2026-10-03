## BulletML parser and runner, following the behaviour of libBulletML
## (which the original game uses), including its quirks.
class_name BulletML

static var _cache := {}

var top_actions: Array = []
var actions := {}
var bullets := {}
var fires := {}
var horizontal := false


static func get_instance(file_name: String) -> BulletML:
	if _cache.has(file_name):
		return _cache[file_name]
	var b := BulletML.new()
	b._load(U.DATA_DIR + "barrage/" + file_name)
	_cache[file_name] = b
	return b


class BNode:
	var name := ""
	var type := ""
	var label := ""
	var children: Array = []
	var expr = null  # parsed expression (number or Array)

	func child(n: String) -> BNode:
		for c in children:
			if c.name == n:
				return c
		return null


func _load(path: String) -> void:
	var xp := XMLParser.new()
	if xp.open(path) != OK:
		push_error("BulletML not found: " + path)
		return
	var stack: Array = []
	var root: BNode = null
	while xp.read() == OK:
		match xp.get_node_type():
			XMLParser.NODE_ELEMENT:
				var n := BNode.new()
				n.name = xp.get_node_name()
				for i in xp.get_attribute_count():
					var an := xp.get_attribute_name(i)
					if an == "type":
						n.type = xp.get_attribute_value(i)
					elif an == "label":
						n.label = xp.get_attribute_value(i)
				if stack.is_empty():
					root = n
				else:
					stack.back().children.append(n)
				if not xp.is_empty():
					stack.append(n)
			XMLParser.NODE_ELEMENT_END:
				stack.pop_back()
			XMLParser.NODE_TEXT, XMLParser.NODE_CDATA:
				var t := xp.get_node_data().strip_edges()
				if t.length() > 0 and not stack.is_empty():
					stack.back().expr = Expr.parse(t)
	if root == null:
		return
	horizontal = root.type == "horizontal"
	for c in root.children:
		match c.name:
			"action":
				actions[c.label] = c
				if c.label.begins_with("top"):
					top_actions.append(c)
			"bullet":
				bullets[c.label] = c
			"fire":
				fires[c.label] = c


## Tiny arithmetic expression evaluator ($rank, $rand, $1..$n, + - * / %).
class Expr:
	# Parsed form: float, or [op, a, b], or ["rank"], ["rand"], ["p", idx], ["neg", a]
	static func parse(s: String):
		var toks := _tokenize(s)
		var pos := [0]
		var e = _parse_add(toks, pos)
		if e is Array or e is float:
			return e
		return 0.0

	static func _tokenize(s: String) -> Array:
		var toks := []
		var i := 0
		while i < s.length():
			var c := s[i]
			if c == " " or c == "\t" or c == "\n" or c == "\r":
				i += 1
			elif c in "+-*/%()":
				toks.append(c)
				i += 1
			elif c == "$":
				var j := i + 1
				while j < s.length() and (s[j].is_valid_identifier() or s[j].is_valid_int()):
					j += 1
				toks.append(s.substr(i, j - i))
				i = j
			else:
				var j := i
				while j < s.length() and (s[j].is_valid_int() or s[j] == "."):
					j += 1
				if j == i:
					j = i + 1
				toks.append(s.substr(i, j - i).to_float())
				i = j
		return toks

	static func _parse_add(t: Array, p: Array):
		var a = _parse_mul(t, p)
		while p[0] < t.size() and (str(t[p[0]]) == "+" or str(t[p[0]]) == "-") and t[p[0]] is String:
			var op: String = t[p[0]]
			p[0] += 1
			var b = _parse_mul(t, p)
			a = [op, a, b]
		return a

	static func _parse_mul(t: Array, p: Array):
		var a = _parse_unary(t, p)
		while p[0] < t.size() and t[p[0]] is String and (t[p[0]] == "*" or t[p[0]] == "/" or t[p[0]] == "%"):
			var op: String = t[p[0]]
			p[0] += 1
			var b = _parse_unary(t, p)
			a = [op, a, b]
		return a

	static func _parse_unary(t: Array, p: Array):
		if p[0] < t.size() and t[p[0]] is String and t[p[0]] == "-":
			p[0] += 1
			return ["neg", _parse_unary(t, p)]
		if p[0] < t.size() and t[p[0]] is String and t[p[0]] == "+":
			p[0] += 1
			return _parse_unary(t, p)
		return _parse_atom(t, p)

	static func _parse_atom(t: Array, p: Array):
		if p[0] >= t.size():
			return 0.0
		var tok = t[p[0]]
		p[0] += 1
		if tok is float:
			return tok
		if tok == "(":
			var e = _parse_add(t, p)
			if p[0] < t.size() and t[p[0]] is String and t[p[0]] == ")":
				p[0] += 1
			return e
		if tok == "$rank":
			return ["rank"]
		if tok == "$rand":
			return ["rand"]
		if tok.begins_with("$"):
			return ["p", tok.substr(1).to_int() - 1]
		return 0.0

	static func eval(e, params: Array, b) -> float:
		if e is float:
			return e
		if e == null:
			return 0.0
		match e[0]:
			"+":
				return eval(e[1], params, b) + eval(e[2], params, b)
			"-":
				return eval(e[1], params, b) - eval(e[2], params, b)
			"*":
				return eval(e[1], params, b) * eval(e[2], params, b)
			"/":
				var d := eval(e[2], params, b)
				return eval(e[1], params, b) / d if d != 0 else 0.0
			"%":
				var d := eval(e[2], params, b)
				return fmod(eval(e[1], params, b), d) if d != 0 else 0.0
			"neg":
				return -eval(e[1], params, b)
			"rank":
				return b.get_rank()
			"rand":
				return b.get_rand()
			"p":
				var i: int = e[1]
				return params[i] if i >= 0 and i < params.size() else 0.0
		return 0.0


## The state handed over to a newly created bullet (its actions and parameters).
class State:
	var doc: BulletML
	var acts: Array
	var params: Array

	func _init(d: BulletML, a: Array, p: Array) -> void:
		doc = d
		acts = a
		params = p


class Runner:
	var impls: Array = []

	static func from_doc(doc: BulletML) -> Runner:
		var r := Runner.new()
		for a in doc.top_actions:
			r.impls.append(Impl.new(doc, [a], []))
		return r

	static func from_state(s: State) -> Runner:
		var r := Runner.new()
		r.impls.append(Impl.new(s.doc, s.acts, s.params))
		return r

	func run(b) -> void:
		for i in impls:
			i.run(b)

	func is_end() -> bool:
		for i in impls:
			if not i.end:
				return false
		return true


class Impl:
	var doc: BulletML
	var acts: Array
	var act_idx := 0
	var base_params: Array
	# Frames: [nodes, idx, params, repeat_left]
	var stack: Array = []
	var act_turn := -1
	var end_turn := 0
	var end := false
	var prev_dir := 0.0
	var prev_dir_valid := false
	var prev_spd := 0.0
	var prev_spd_valid := false
	var change_dir = null  # [firstX, lastX, firstY, lastY]
	var change_spd = null
	var accel_x = null
	var accel_y = null

	func _init(d: BulletML, a: Array, p: Array) -> void:
		doc = d
		acts = a
		base_params = p
		if acts.is_empty():
			end = true

	func _ev(n: BNode, params: Array, b) -> float:
		if n == null:
			return 0.0
		return Expr.eval(n.expr, params, b)

	func _ref_params(n: BNode, params: Array, b) -> Array:
		var r := []
		for c in n.children:
			if c.name == "param":
				r.append(Expr.eval(c.expr, params, b))
		return r

	static func _lin(f: Array, x: int) -> float:
		if f[1] == f[0]:
			return f[3]
		return f[2] + (f[3] - f[2]) / float(f[1] - f[0]) * (x - f[0])

	func _changes(b) -> void:
		var now: int = b.get_turn()
		if change_dir != null:
			if now >= change_dir[1]:
				b.do_change_direction(change_dir[3])
				change_dir = null
			else:
				b.do_change_direction(_lin(change_dir, now))
		if change_spd != null:
			if now >= change_spd[1]:
				b.do_change_speed(change_spd[3])
				change_spd = null
			else:
				b.do_change_speed(_lin(change_spd, now))
		if accel_x != null:
			if now >= accel_x[1]:
				b.do_accel_x(accel_x[3])
				accel_x = null
			else:
				b.do_accel_x(_lin(accel_x, now))
		if accel_y != null:
			if now >= accel_y[1]:
				b.do_accel_y(accel_y[3])
				accel_y = null
			else:
				b.do_accel_y(_lin(accel_y, now))

	func run(b) -> void:
		if end:
			return
		_changes(b)
		end_turn = b.get_turn()
		if stack.is_empty():
			if act_idx >= acts.size():
				if not (act_turn > end_turn) and change_dir == null and change_spd == null \
						and accel_x == null and accel_y == null:
					end = true
				return
			stack.append([[acts[act_idx]], 0, base_params, 1])
			act_idx += 1
		if act_turn == -1:
			act_turn = b.get_turn()
		_run_sub(b)

	func _run_sub(b) -> void:
		var guard := 0
		while not stack.is_empty() and act_turn <= end_turn:
			guard += 1
			if guard > 100000:
				end = true
				return
			var f: Array = stack.back()
			var nodes: Array = f[0]
			if f[1] >= nodes.size():
				if f[3] > 1:
					f[3] -= 1
					f[1] = 0
				else:
					stack.pop_back()
				continue
			var n: BNode = nodes[f[1]]
			f[1] += 1
			var p: Array = f[2]
			match n.name:
				"action":
					stack.append([n.children, 0, p, 1])
				"actionRef":
					var a: BNode = doc.actions.get(n.label)
					if a != null:
						stack.append([a.children, 0, _ref_params(n, p, b), 1])
				"repeat":
					var times := int(_ev(n.child("times"), p, b))
					var a := n.child("action")
					if a == null:
						a = n.child("actionRef")
					if a != null:
						stack.append([[a], 0, p, maxi(times, 1)])
				"fire":
					_run_fire(n, p, b)
				"fireRef":
					var fr: BNode = doc.fires.get(n.label)
					if fr != null:
						_run_fire(fr, _ref_params(n, p, b), b)
				"changeDirection":
					_run_change_direction(n, p, b)
				"changeSpeed":
					_run_change_speed(n, p, b)
				"accel":
					_run_accel(n, p, b)
				"wait":
					var w := int(_ev(n, p, b))
					if w > 0:
						act_turn += w
				"vanish":
					b.do_vanish()

	func _get_direction(dn: BNode, p: Array, b, prev_change: bool) -> float:
		var d := _ev(dn, p, b)
		var is_default := true
		if dn.type != "":
			is_default = false
			match dn.type:
				"absolute":
					if doc.horizontal:
						d -= 90
				"relative":
					d += b.get_bullet_direction()
				"sequence":
					if prev_dir_valid:
						d += prev_dir
					else:
						d = 0
						is_default = true
				_:
					is_default = true
		if is_default:
			d += b.get_aim_direction()
		while d > 360:
			d -= 360
		while d < 0:
			d += 360
		if prev_change:
			prev_dir = d
			prev_dir_valid = true
		return d

	func _get_speed(sn: BNode, p: Array, b) -> float:
		var s := _ev(sn, p, b)
		match sn.type:
			"relative":
				s += b.get_bullet_speed()
			"sequence":
				if prev_spd_valid:
					s += prev_spd
				else:
					s = 1
		return s

	func _run_fire(fire: BNode, p: Array, b) -> void:
		var spd := 0.0
		var spd_valid := false
		var dir := 0.0
		var dir_valid := false
		var sn := fire.child("speed")
		if sn != null:
			spd = _get_speed(sn, p, b)
			spd_valid = true
			prev_spd = spd
			prev_spd_valid = true
		var dn := fire.child("direction")
		if dn != null:
			dir = _get_direction(dn, p, b, true)
			dir_valid = true
		var bn := fire.child("bullet")
		var bp := p
		if bn == null:
			var br := fire.child("bulletRef")
			if br == null:
				return
			bp = _ref_params(br, p, b)
			bn = doc.bullets.get(br.label)
			if bn == null:
				return
		sn = bn.child("speed")
		if sn != null:
			spd = _get_speed(sn, bp, b)
			spd_valid = true
			prev_spd = spd
			prev_spd_valid = true
		dn = bn.child("direction")
		if dn != null:
			dir = _get_direction(dn, bp, b, true)
			dir_valid = true
		if not spd_valid:
			spd = 1.0
			prev_spd = spd
			prev_spd_valid = true
		if not dir_valid:
			dir = b.get_aim_direction()
			prev_dir = dir
			prev_dir_valid = true
		var bacts := []
		for c in bn.children:
			if c.name == "action" or c.name == "actionRef":
				bacts.append(c)
		if bacts.is_empty():
			b.create_simple_bullet(dir, spd)
		else:
			b.create_bullet(State.new(doc, bacts, bp), dir, spd)

	func _run_change_direction(n: BNode, p: Array, b) -> void:
		var term := int(_ev(n.child("term"), p, b))
		var dn := n.child("direction")
		if dn == null:
			return
		var seq := dn.type == "sequence"
		var d: float
		if not seq:
			d = _get_direction(dn, p, b, false)
		else:
			d = _ev(dn, p, b)
		var final_turn := act_turn + term
		var first: float = b.get_bullet_direction()
		if seq:
			change_dir = [act_turn, final_turn, first, first + d * term]
		else:
			var s1 := d - first
			var s2 := s1 - 360 if s1 > 0 else s1 + 360
			var sp := s1 if absf(s1) < absf(s2) else s2
			change_dir = [act_turn, final_turn, first, first + sp]

	func _run_change_speed(n: BNode, p: Array, b) -> void:
		var term := int(_ev(n.child("term"), p, b))
		var sn := n.child("speed")
		if sn == null:
			return
		var s: float
		if sn.type != "sequence":
			s = _get_speed(sn, p, b)
		else:
			s = _ev(sn, p, b) * term + b.get_bullet_speed()
		change_spd = [act_turn, act_turn + term, b.get_bullet_speed(), s]

	func _run_accel(n: BNode, p: Array, b) -> void:
		var term := int(_ev(n.child("term"), p, b))
		var hn := n.child("horizontal")
		var vn := n.child("vertical")
		if doc.horizontal:
			if vn != null:
				accel_x = _calc_accel(_ev(vn, p, b), term, vn.type, b.get_bullet_speed_x())
			if hn != null:
				accel_y = _calc_accel(-_ev(hn, p, b), term, hn.type, b.get_bullet_speed_y())
		else:
			if hn != null:
				accel_x = _calc_accel(_ev(hn, p, b), term, hn.type, b.get_bullet_speed_x())
			if vn != null:
				accel_y = _calc_accel(_ev(vn, p, b), term, vn.type, b.get_bullet_speed_y())

	func _calc_accel(v: float, term: int, type: String, first: float) -> Array:
		var fin: float
		if type == "sequence":
			fin = first + v * term
		elif type == "relative":
			fin = first + v
		else:
			fin = v
		return [act_turn, act_turn + term, first, fin]
