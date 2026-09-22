using Drift.Core;
using Drift.Islands;
using UnityEngine;

namespace Drift.Visuals
{
    // Boats and islands: an island that reaches a boat faster than the boat can get away (SeaMath.ShouldBeach)
    // throws it on its beach. The hull slides up the slope to a resting spot just above the waterline, tips over,
    // and from then on lives in the island's body space (translation and body rotation carry it along) until it
    // refloats: after 20-60 s, when the island has sunk under it, or when its spot is lost to a merge or a
    // building. Moored boats ride their island the same way. Slower contacts only shove the hull aside.
    public partial class ShipSystem
    {
        // pos: world position on the water line, intensity 0..1 (closing speed and hull weight). For audio.
        public static event System.Action<Vector3, float> ShipBeached;
        public static event System.Action<Vector3> ShipRefloated;
        public static int BeachedTotal { get; private set; }
        public static int RefloatedTotal { get; private set; }

        public Island ShipHost(int i) => _ships[i].active ? _ships[i].host : null;
        public bool ShipIsBeached(int i) => _ships[i].active && _ships[i].state == ShipState.Beached;
        // 0 sliding up the beach, 1 lying there, 2 sliding back into the water.
        public int ShipBeachPhase(int i) => _ships[i].beachPhase;
        public Vector2 ShipLocalPosition(int i) => _ships[i].local;
        public Vector2 ShipLocalHeading(int i) => _ships[i].localDir;
        public float ShipRestRoll(int i) => _ships[i].restRoll;
        public float ShipRestPitch(int i) => _ships[i].restPitch;
        public float ShipSecondsLeft(int i) => _ships[i].timer;
        public float ShipWakeBob(int i) => _ships[i].wakeBob;
        public float ShipNet(int i) => _ships[i].net;
        public bool ShipRidesPlayer(int i) => _ships[i].active && Rides(ref _ships[i]);
        public int RideVertexCount => _ride != null ? _ride.vc : 0;
        public int BeachedCount => BeachedOn(null);

        public int BeachedOn(Island isl)
        {
            int n = 0;
            if (_ships == null) return 0;
            for (int i = 0; i < _ships.Length; i++)
                if (_ships[i].active && _ships[i].state == ShipState.Beached && (isl == null || _ships[i].host == isl)) n++;
            return n;
        }

        // World position of a stranded hull including its height on the beach (journal / tap / camera).
        public bool TryGetBeached(int i, out Vector3 pos, out Island host)
        {
            pos = default;
            host = null;
            if (_ships == null || i < 0 || i >= _ships.Length || !ShipIsBeached(i) || !HostAlive(_ships[i].host)) return false;
            host = _ships[i].host;
            pos = new Vector3(_ships[i].pos.x, Mathf.Max(0f, host.SampleHeight(_ships[i].local) + host.transform.position.y), _ships[i].pos.y);
            return true;
        }

        Vector2 Vel(Island isl) => isl != null && (Application.isPlaying || debugLiveVelocity) ? isl.PlanarVelocity : Vector2.zero;

        static bool HostAlive(Island h) => h != null && h.isActiveAndEnabled && !h.IsSunk;

        bool Rides(ref Ship s) =>
            _player != null && s.host == _player && maxRideVerts > 0 && (s.state == ShipState.Beached || s.state == ShipState.Moored);

        static Vector2 BodyFwd(Island isl)
        {
            Vector3 f = isl.BodyForward;
            return new Vector2(f.x, f.z);
        }

        static float KeelClear(ShipKind k) => k == ShipKind.TradingCog ? 0.3f : k == ShipKind.RowBoat ? 0.07f : k == ShipKind.FishingBoat ? 0.13f : 0.1f;

        static float SpotRadius(ShipKind k) => k == ShipKind.TradingCog ? 2.6f : k == ShipKind.RowBoat ? 0.7f : 1.1f;

        // Waves the flag for the first seconds and then again every now and then.
        bool FlagWaving(ref Ship s) => s.react > 0f || ((_clock + (s.seed & 15u)) % 14f) < 3.5f;

        void FollowHost(ref Ship s)
        {
            Vector2 fwd = BodyFwd(s.host);
            s.pos = SeaMath.FromBodyLocal(s.local, s.host.PlanarPosition, fwd);
            Vector2 d = SeaMath.DirFromBodyLocal(s.localDir, fwd);
            if (d.sqrMagnitude > 1e-6f) s.dir = d.normalized;
        }

        void SyncRide()
        {
            if (_rideGo == null) return;
            bool on = _player != null && _ride != null && _ride.vc > 0;
            if (_rideRenderer == null) _rideRenderer = _rideGo.GetComponent<MeshRenderer>();
            if (_rideRenderer.enabled != on) _rideRenderer.enabled = on;
            if (on) _rideGo.transform.SetPositionAndRotation(_player.transform.position, _player.transform.rotation);
        }

        MeshRenderer _rideRenderer;

        // ---- moored: tied to the island ----

        void Moor(ref Ship s, Island host, float seconds)
        {
            s.state = ShipState.Moored;
            s.timer = seconds;
            s.overdue = 0f;
            s.squeeze = 0f;
            s.host = HostAlive(host) ? host : null;
            if (s.host == null) return;
            Vector2 fwd = BodyFwd(s.host);
            s.local = SeaMath.ToBodyLocal(s.pos, s.host.PlanarPosition, fwd);
            s.localDir = SeaMath.DirToBodyLocal(s.dir, fwd);
            s.localDirTo = s.hasDock ? SeaMath.DirToBodyLocal(-s.dockDir, fwd) : s.localDir;
            _dirty = true;
        }

        void StepMoored(ref Ship s, float dt)
        {
            if (!HostAlive(s.host))
            {
                CastOff(ref s);
                return;
            }
            if (s.hasDock)
            {
                Vector2 nd = Vector2.Lerp(s.localDir, s.localDirTo, 1f - Mathf.Exp(-dt));
                if (nd.sqrMagnitude > 1e-6f) s.localDir = nd.normalized;
            }
            FollowHost(ref s);
            bool stay = s.kind == ShipKind.RowBoat && (s.lantern || s.sailsDown);
            if (s.timer <= 0f) s.overdue += dt;
            bool tied = SeaMath.StaysTied(Vel(s.host).magnitude, s.overdue);
            if (s.timer <= 0f && !stay && !s.sailsDown && !tied) CastOff(ref s);
        }

        void CastOff(ref Ship s)
        {
            s.state = ShipState.Sailing;
            Island next = s.kind == ShipKind.RowBoat ? s.target : PickIsland(s.pos, 230f, SeaMath.Hash(s.seed, (uint)_clock), s.target);
            s.target = next;
            s.host = null;
            s.hasDock = false;
            s.cooldown = 8f;
            s.grace = 4f;
            _dirty = true;
        }

        // ---- contact ----

        static Vector2 Gradient(Island isl, Vector2 l)
        {
            const float e = 0.3f;
            float hx = isl.SampleHeight(new Vector2(l.x + e, l.y)) - isl.SampleHeight(new Vector2(l.x - e, l.y));
            float hz = isl.SampleHeight(new Vector2(l.x, l.y + e)) - isl.SampleHeight(new Vector2(l.x, l.y - e));
            return new Vector2(hx, hz) / (2f * e);
        }

        // Unit world direction out to sea at a spot over the shelf: mostly downhill, steadied by the radial.
        static Vector2 Outward(Island isl, Vector2 local, Vector2 world)
        {
            Vector2 radial = world - isl.PlanarPosition;
            radial = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector2.right;
            Vector2 g = Gradient(isl, local);
            if (g.sqrMagnitude < 0.0025f) return radial;
            Vector2 o = SeaMath.DirFromBodyLocal(-g.normalized, BodyFwd(isl)) * 0.7f + radial * 0.3f;
            return o.sqrMagnitude > 1e-6f ? o.normalized : radial;
        }

        // True when the ship got beached this step.
        bool Contact(int idx, float dt)
        {
            ref Ship s = ref _ships[idx];
            Island over = null;
            float closing = 0f;
            Vector2 outward = default;
            for (int i = 0; i < _obstacles.count; i++)
            {
                Island isl = _obstacles.islands[i];
                if (isl == null || (s.state == ShipState.Moored && isl == s.host)) continue;
                Vector3 c = _obstacles.circles[i];
                float dx = s.pos.x - c.x, dy = s.pos.y - c.y;
                if (dx * dx + dy * dy > c.z * c.z) continue;
                // The hull touches where the ground under its middle, bow or stern rises into view.
                Vector2 l = isl.ToLocal(s.pos);
                Vector2 reach = s.dir * (HalfLength(s.kind) * 0.8f);
                if (isl.SampleHeight(l) <= hullDraft && isl.SampleHeight(isl.ToLocal(s.pos + reach)) <= hullDraft
                    && isl.SampleHeight(isl.ToLocal(s.pos - reach)) <= hullDraft) continue;
                Vector2 o = Outward(isl, l, s.pos);
                float cl = SeaMath.ClosingSpeed(i == 0 ? _playerVel : Vel(isl), s.dir * s.speed, o);
                // Squeezed between two islands: the faster one gets the boat.
                if (over != null && SeaMath.FasterHost(closing, cl) == 0) continue;
                over = isl;
                closing = cl;
                outward = o;
            }

            if (over == null)
            {
                s.squeeze = Mathf.Max(0f, s.squeeze - dt * 0.5f);
                if (_player != null && s.state != ShipState.Moored && s.target != _player)
                {
                    Vector2 off = s.pos - _playerPos;
                    float l = off.magnitude, r = _playerRadius + 2f;
                    // The bow wave nudges a hull aside, but only as fast as it can give way: a fast island still
                    // runs it down (the old shove scaled with the player's speed, so nothing was ever touched).
                    if (l < r && _playerSpeed > 0.3f)
                        s.pos += (l > 1e-3f ? off / l : s.dir) * (Mathf.Min(r - l, 1f) * Mathf.Min(1f + _playerSpeed, 0.6f * SeaMath.EvadeSpeed(SeaKindOf(s.kind))) * dt);
                }
                return false;
            }

            if (s.state == ShipState.Moored) CastOff(ref s);
            s.squeeze += dt;
            SeaKind sk = SeaKindOf(s.kind);
            // Adventure: ships are obstacles, never a reward. Running one down costs speed and shoves the hull out
            // of the line - and it must never end up beached on the player, riding along like a prize.
            bool bump = _onRing && over == _player;
            if (bump) Bump(idx, outward);
            bool may = s.grace <= 0f && s.fade > 0.5f && !over.IsEmerging && !bump;
            if (may && SeaMath.ShouldBeach(sk, closing, s.squeeze, BeachedOn(over), maxBeachedPerIsland) && TryBeach(idx, over, closing))
                return true;

            float islandSpeed = Vel(over).magnitude;
            bool gentle = may && closing < SeaMath.BeachClosingSpeed(sk);
            float push = bump ? shipHitPush + _playerSpeed : gentle ? SeaMath.EvadeSpeed(sk) : 3f + islandSpeed;
            Vector2 dirn = outward;
            if (s.kind == ShipKind.TradingCog && islandSpeed > 0.5f)
            {
                // The heavy cog is shouldered aside along the island's bow instead of out ahead of it.
                Vector2 v = Vel(over) / islandSpeed;
                Vector2 side = new Vector2(-v.y, v.x);
                if (Vector2.Dot(side, s.pos - over.PlanarPosition) < 0f) side = -side;
                dirn = outward + side * 0.9f;
                s.dir = Vector2.Lerp(s.dir, side, 1f - Mathf.Exp(-0.8f * dt)).normalized;
            }
            s.pos += dirn * (push * dt);
            return false;
        }

        // The player rammed this hull (Adventure): once per shipHitCooldown it reports the bump - spray, a heavy
        // roll, and Encounters takes the speed off the island (Island.Stagger). Never a reward of any kind.
        public static int ShipHitTotal { get; private set; }

        void Bump(int idx, Vector2 outward)
        {
            ref Ship s = ref _ships[idx];
            s.heel = Mathf.Clamp(s.heel + 0.45f, -0.7f, 0.7f);
            s.wakeBobTarget = 1f;
            s.wakeBob = 1f;
            s.grace = 6f;
            if (outward.sqrMagnitude > 1e-4f) s.dir = Vector2.Lerp(s.dir, outward.normalized, 0.35f).normalized;
            if (s.hitCool > 0f) return;
            s.hitCool = Mathf.Max(0.2f, shipHitCooldown);
            ShipHitTotal++;
            Vector3 at = new Vector3(s.pos.x, 0.1f, s.pos.y);
            if (seaLife != null && seaLife.isActiveAndEnabled) seaLife.SprayAt(s.pos, 1.3f, 16);
            else if (water != null) water.Splash(s.pos, 1.2f);
            Encounters.NotifyShipHit(s.kind, at);
        }

        // ---- running aground ----

        bool Blocked(Island isl, Vector2 local, float radius)
        {
            if (isl.TryGetComponent<IShoreBlocker>(out var blocker) && blocker.BlocksShore(local, radius)) return true;
            if (isl.TryGetComponent<IDockProvider>(out var dock) && dock.TryGetDockWorld(out Vector3 dp, out _))
            {
                Vector2 dl = isl.ToLocal(new Vector2(dp.x, dp.z));
                float r = radius + 2.5f;
                if ((dl - local).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        bool SpotFree(Island isl, Vector2 local, float radius, int self)
        {
            for (int i = 0; i < _ships.Length; i++)
            {
                if (i == self || !_ships[i].active || _ships[i].host != isl) continue;
                if (_ships[i].state != ShipState.Beached && _ships[i].state != ShipState.Moored) continue;
                Vector2 other = _ships[i].state == ShipState.Beached && _ships[i].beachPhase == 0 ? _ships[i].localTo : _ships[i].local;
                float r = radius + SpotRadius(_ships[i].kind) + 0.6f;
                if ((other - local).sqrMagnitude < r * r) return false;
            }
            return !Blocked(isl, local, radius);
        }

        // Walks up (or, from a buried spot, down) the slope to the strip just above the waterline.
        static bool March(Island isl, Vector2 l, out Vector2 rest, out Vector2 grad)
        {
            const float aim = (SeaMath.BeachMinHeight + SeaMath.BeachMaxHeight) * 0.5f;
            grad = default;
            for (int i = 0; i < 56; i++)
            {
                float h = isl.SampleHeight(l);
                grad = Gradient(isl, l);
                float gl = grad.magnitude;
                if (h >= SeaMath.BeachMinHeight && h <= SeaMath.BeachMaxHeight)
                {
                    rest = l;
                    return SeaMath.ValidBeachSpot(h, gl);
                }
                Vector2 dirn = gl > 0.02f ? grad / gl : l.sqrMagnitude > 1e-4f ? -l.normalized : Vector2.up;
                if (h > SeaMath.BeachMaxHeight) dirn = -dirn;
                float step = Mathf.Clamp(Mathf.Abs(aim - h) / Mathf.Max(gl, 0.05f), 0.04f, 0.35f);
                l += dirn * step;
            }
            rest = l;
            return false;
        }

        // Nearest free resting spot: straight up the slope first, then further and further along the shore.
        bool FindBeachSpot(Island isl, Vector2 start, int self, float radius, out Vector2 rest, out Vector2 grad)
        {
            Vector2 g0 = Gradient(isl, start);
            Vector2 up = g0.sqrMagnitude > 4e-4f ? g0.normalized : start.sqrMagnitude > 1e-4f ? -start.normalized : Vector2.up;
            Vector2 along = new Vector2(-up.y, up.x);
            for (int c = 0; c < 9; c++)
            {
                float off = ((c + 1) / 2) * (radius + 0.8f) * ((c & 1) == 1 ? 1f : -1f);
                if (March(isl, start + along * off, out rest, out grad) && SpotFree(isl, rest, radius, self)) return true;
            }
            rest = start;
            grad = g0;
            return false;
        }

        void SetRestTilt(ref Ship s, Vector2 grad)
        {
            Vector2 d = s.localDirTo;
            Vector2 rgt = new Vector2(d.y, -d.x);
            s.restRoll = SeaMath.BeachRoll(s.seed, Vector2.Dot(-grad, rgt), s.kind == ShipKind.TradingCog);
            s.restPitch = Mathf.Clamp(Mathf.Atan(Vector2.Dot(grad, d)) * 0.5f, -0.2f, 0.2f);
        }

        bool TryBeach(int idx, Island isl, float closing)
        {
            ref Ship s = ref _ships[idx];
            Vector2 fwd = BodyFwd(isl);
            Vector2 start = SeaMath.ToBodyLocal(s.pos, isl.PlanarPosition, fwd);
            if (!FindBeachSpot(isl, start, idx, SpotRadius(s.kind), out Vector2 rest, out Vector2 grad)) return false;

            s.state = ShipState.Beached;
            s.host = isl;
            s.target = null;
            s.hasDock = false;
            s.hauling = false;
            s.beachPhase = 0;
            s.beachT = 0f;
            s.local = start;
            s.localFrom = start;
            s.localTo = rest;
            s.localDirFrom = SeaMath.DirToBodyLocal(s.dir, fwd);
            s.localDir = s.localDirFrom;
            // The surf swings the hull partly broadside to the slope.
            Vector2 up = grad.sqrMagnitude > 1e-6f ? grad.normalized : (rest - start).sqrMagnitude > 1e-6f ? (rest - start).normalized : s.localDirFrom;
            Vector2 along = new Vector2(-up.y, up.x);
            if (Vector2.Dot(along, s.localDirFrom) < 0f) along = -along;
            Vector2 to = s.localDirFrom * 0.5f + along * (0.35f + 0.45f * SeaMath.Rand(s.seed, 93)) + up * 0.25f;
            s.localDirTo = to.sqrMagnitude > 1e-6f ? to.normalized : s.localDirFrom;
            s.beachDuration = Mathf.Clamp((rest - start).magnitude / Mathf.Max(1.2f, closing * 0.8f), 0.7f, 2.4f);
            SetRestTilt(ref s, grad);
            s.timer = SeaMath.BeachSeconds(SeaMath.Hash(s.seed, (uint)_clock));
            s.speed = 0f;
            s.squeeze = 0f;
            s.react = 10f;
            for (int k = 0; k < WakePoints; k++) _wakeAge[idx * WakePoints + k] = 1000f;

            float weight = s.kind == ShipKind.TradingCog ? 1f : s.kind == ShipKind.RowBoat ? 0.55f : 0.8f;
            float intensity = Mathf.Clamp01((0.35f + 0.65f * Mathf.Clamp01(closing / 6f)) * weight);
            Vector3 wp = new Vector3(s.pos.x, 0f, s.pos.y);
            if (LifeLod.Distance(wp) < 90f)
            {
                if (water != null) water.Splash(s.pos, 0.5f + 0.5f * intensity);
                if (seaLife != null) seaLife.SprayAt(s.pos, 0.4f + 0.7f * intensity, s.kind == ShipKind.TradingCog ? 12 : 8);
            }
            BeachedTotal++;
            ShipBeached?.Invoke(wp, intensity);
            _dirty = true;
            return true;
        }

        void StepBeached(int idx, float dt)
        {
            ref Ship s = ref _ships[idx];
            if (!HostAlive(s.host))
            {
                Release(ref s);
                return;
            }
            if (s.beachPhase == 1)
            {
                if (s.timer <= 0f) BeginRefloat(idx);
            }
            else
            {
                s.beachT = Mathf.Min(1f, s.beachT + dt / Mathf.Max(0.2f, s.beachDuration));
                float t = s.beachT;
                float e = s.beachPhase == 0 ? 1f - (1f - t) * (1f - t) : t * t;
                s.local = Vector2.Lerp(s.localFrom, s.localTo, e);
                Vector2 d = Vector2.Lerp(s.localDirFrom, s.localDirTo, t * t * (3f - 2f * t));
                if (d.sqrMagnitude > 1e-6f) s.localDir = d.normalized;
                if (t >= 1f)
                {
                    if (s.beachPhase == 2)
                    {
                        FollowHost(ref s);
                        Release(ref s);
                        return;
                    }
                    s.beachPhase = 1;
                    s.local = s.localTo;
                }
            }
            FollowHost(ref s);
        }

        // Twice a second while lying on the beach: sunk under the hull -> refloat; built over, uplifted by a merge
        // or turned into a cliff -> slide to the nearest free shore spot, or refloat when there is none.
        void CheckBeachSpot(int idx)
        {
            ref Ship s = ref _ships[idx];
            if (s.beachPhase != 1 || !HostAlive(s.host)) return;
            float h = s.host.SampleHeight(s.local);
            // A merge reshapes the shore (the ridge takes its volume from the rim): that is a lost spot, not a
            // sinking island, so the hull moves with the new waterline instead of floating off.
            bool reshaped = s.host.IsUplifting && s.timer > 0f && h < SeaMath.BeachMinHeight;
            if (!reshaped && SeaMath.ShouldRefloat(s.timer, h, true))
            {
                BeginRefloat(idx);
                return;
            }
            float radius = SpotRadius(s.kind);
            if (!reshaped && !SeaMath.BeachSpotLost(h, Gradient(s.host, s.local).magnitude, Blocked(s.host, s.local, radius))) return;
            if (FindBeachSpot(s.host, s.local, idx, radius, out Vector2 rest, out Vector2 grad))
            {
                s.beachPhase = 0;
                s.beachT = 0f;
                s.localFrom = s.local;
                s.localTo = rest;
                s.localDirFrom = s.localDir;
                s.localDirTo = s.localDir;
                s.beachDuration = Mathf.Clamp((rest - s.local).magnitude, 1f, 4f);
                SetRestTilt(ref s, grad);
            }
            else BeginRefloat(idx);
        }

        void BeginRefloat(int idx)
        {
            ref Ship s = ref _ships[idx];
            Island isl = s.host;
            Vector2 l = s.local;
            Vector2 dirn = l.sqrMagnitude > 1e-4f ? l.normalized : Vector2.up;
            for (int i = 0; i < 80; i++)
            {
                if (isl.SampleHeight(l) < hullDraft - 0.15f) break;
                Vector2 g = Gradient(isl, l);
                if (g.sqrMagnitude > 4e-4f) dirn = Vector2.Lerp(dirn, -g.normalized, 0.6f).normalized;
                l += dirn * 0.25f;
            }
            l += dirn;
            s.beachPhase = 2;
            s.beachT = 0f;
            s.localFrom = s.local;
            s.localTo = l;
            s.localDirFrom = s.localDir;
            Vector2 to = s.localDir * 0.4f + dirn;
            s.localDirTo = to.sqrMagnitude > 1e-6f ? to.normalized : dirn;
            s.beachDuration = Mathf.Clamp((l - s.local).magnitude / 0.9f, 2.5f, 7f);
            s.react = 0f;
        }

        void Release(ref Ship s)
        {
            s.state = s.kind == ShipKind.TradingCog ? ShipState.Crossing : ShipState.Sailing;
            s.host = null;
            s.target = null;
            s.hasDock = false;
            s.beachPhase = 0;
            s.course = s.dir;
            s.dest = s.pos + s.dir * 30f;
            s.grace = 6f;
            s.cooldown = 5f;
            s.squeeze = 0f;
            s.react = 0f;
            RefloatedTotal++;
            ShipRefloated?.Invoke(new Vector3(s.pos.x, 0f, s.pos.y));
            _dirty = true;
        }

        // A merge re-centres the absorbing island on its new centroid and removes the other one: hulls on either
        // keep their world position and get a new local pose; CheckBeachSpot then deals with the changed ground.
        void OnIslandMerged(Island host, Island other, float energy)
        {
            if (_ships == null || host == null) return;
            Vector2 hf = BodyFwd(host);
            for (int i = 0; i < _ships.Length; i++)
            {
                ref Ship s = ref _ships[i];
                if (!s.active || s.host == null || (s.state != ShipState.Beached && s.state != ShipState.Moored)) continue;
                if (s.host == host)
                {
                    Vector2 shift = s.local - SeaMath.ToBodyLocal(s.pos, host.PlanarPosition, hf);
                    s.local -= shift;
                    s.localFrom -= shift;
                    s.localTo -= shift;
                }
                else if (other != null && s.host == other)
                {
                    Vector2 of = BodyFwd(other);
                    Vector2 op = other.PlanarPosition, hp = host.PlanarPosition;
                    s.local = SeaMath.ToBodyLocal(SeaMath.FromBodyLocal(s.local, op, of), hp, hf);
                    s.localFrom = SeaMath.ToBodyLocal(SeaMath.FromBodyLocal(s.localFrom, op, of), hp, hf);
                    s.localTo = SeaMath.ToBodyLocal(SeaMath.FromBodyLocal(s.localTo, op, of), hp, hf);
                    s.localDir = SeaMath.DirToBodyLocal(SeaMath.DirFromBodyLocal(s.localDir, of), hf);
                    s.localDirFrom = SeaMath.DirToBodyLocal(SeaMath.DirFromBodyLocal(s.localDirFrom, of), hf);
                    s.localDirTo = SeaMath.DirToBodyLocal(SeaMath.DirFromBodyLocal(s.localDirTo, of), hf);
                    s.host = host;
                    if (s.state == ShipState.Moored)
                    {
                        s.hasDock = false;
                        if (s.target == other) s.target = host;
                    }
                }
                s.retarget = 0f;
                _dirty = true;
            }
        }

        // ---- verification ----

        // Throws ship i on the beach of `isl` as if it had been hit at `closing` u/s. False when no valid spot.
        public bool ForceBeach(int ship, Island isl, float closing = 4f)
        {
            if (_ships == null || ship < 0 || ship >= _ships.Length || !_ships[ship].active || !HostAlive(isl)) return false;
            if (BeachedOn(isl) >= Mathf.Min(maxBeachedPerIsland, SeaMath.MaxBeachedPerIsland)) return false;
            return TryBeach(ship, isl, closing);
        }

        public void ForceRefloat(int ship)
        {
            if (_ships != null && ship >= 0 && ship < _ships.Length && ShipIsBeached(ship) && _ships[ship].beachPhase != 2) BeginRefloat(ship);
        }

        public void SetBeachSecondsLeft(int ship, float seconds) => _ships[ship].timer = seconds;

        // Ties ship i to `isl` where it floats right now (dock-tied boats follow their island).
        public void MoorAt(int ship, Island isl, float seconds)
        {
            _ships[ship].target = isl;
            Moor(ref _ships[ship], isl, seconds);
        }
    }
}
