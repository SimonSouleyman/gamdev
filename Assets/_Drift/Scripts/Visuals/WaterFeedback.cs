using Drift.Islands;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Visuals
{
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    public class WaterFeedback : MonoBehaviour
    {
        public Renderer waterRenderer;
        public Island player;
        public string waterObjectName = "Ground";
        [Range(0f, 2f)] public float currentStreakStrength = 0.35f;
        public float currentSmoothing = 4f;
        public bool wakeRelativeToCurrent = true;
        public float stormSmoothing = 1.5f;
        [Range(-1f, 1f)] public float stormOverride = -1f;
        public float globalWindSpeed = 0.6f;
        public float globalWindPeriod = 240f;
        public float windCurrentWeight = 0.6f;
        public float windGustWeight = 0.4f;
        public float windSmoothing = 1.5f;
        public float splashLife = 0.8f;
        // Wake from the island's real outline: below wakeMinSpeed the shader skips the wake entirely.
        public float wakeMinSpeed = 0.05f;
        public float wakeEdgeSmoothing = 8f;
        public float reachRefreshInterval = 0.5f;
        public float reachMinInterval = 0.1f;

        static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
        static readonly int PlayerVelId = Shader.PropertyToID("_PlayerVel");
        static readonly int PlayerRadiusId = Shader.PropertyToID("_PlayerRadius");
        static readonly int RingPosId = Shader.PropertyToID("_RingPos");
        static readonly int RingAgeId = Shader.PropertyToID("_RingAge");
        static readonly int RingStrengthId = Shader.PropertyToID("_RingStrength");
        static readonly int CurrentDirId = Shader.PropertyToID("_CurrentDir");
        static readonly int CurrentSpeedId = Shader.PropertyToID("_CurrentSpeed");
        static readonly int CurrentFoamId = Shader.PropertyToID("_CurrentFoam");
        static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
        static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        static readonly int StormId = Shader.PropertyToID("_Storm");
        static readonly int WindDirId = Shader.PropertyToID("_WindDir");
        static readonly int SplashPosId = Shader.PropertyToID("_SplashPos");
        static readonly int IslandDataId = Shader.PropertyToID("_IslandData");
        static readonly int PlayerReachId = Shader.PropertyToID("_PlayerReach");
        static readonly int PlayerBodyYawId = Shader.PropertyToID("_PlayerBodyYaw");
        static readonly int PlayerWakeEdgeId = Shader.PropertyToID("_PlayerWakeEdge");
        static readonly int PlayerWakeId = Shader.PropertyToID("_PlayerWake");
        const int IslandSlots = 8;
        public const int ReachSlots = 32;

        MaterialPropertyBlock _block;
        readonly Vector4[] _islandData = new Vector4[IslandSlots];
        readonly float[] _islandDist = new float[IslandSlots];
        Vector4 _ringPos, _ringAge, _ringStrength;
        Vector2 _currentDir = Vector2.right;
        float _currentSpeed;
        float _storm;
        Vector2 _wind = Vector2.right * 0.6f;
        float _clock;
        Vector2 _splashPos;
        float _splashAge = 10f;
        float _splashStrength;
        bool _hasSky;
        Color _sky, _deep;
        readonly float[] _reachRaw = new float[ReachSlots];
        readonly Vector4[] _reach = new Vector4[ReachSlots];
        static Vector2[] _reachDirs;
        Island _reachOwner;
        int _reachVersion = -1;
        float _reachAge;
        float _reachMax;
        Vector4 _wakeEdge, _wake;
        bool _hasWakeEdge;

        public Vector2 PlayerPos { get; private set; }
        public Vector2 PlayerVel { get; private set; }
        public float PlayerRadius { get; private set; }
        public Vector2 CurrentDir => _currentDir;
        public float CurrentSpeed => _currentSpeed;
        public Vector2 RawCurrent { get; private set; }
        public float Storm => _storm;
        public float RawStorm { get; private set; }
        public Vector2 Wind => _wind;
        public Vector2 WindDir => _wind.sqrMagnitude > 1e-6f ? _wind.normalized : Vector2.right;
        public float WindSpeed => _wind.magnitude;
        public Vector2 GlobalWind { get; private set; }
        public float Clock => _clock;
        public MaterialPropertyBlock Block => _block;
        // x = land reach along body angle k * 360/32 (0 = body forward, clockwise), y = d reach / d angle.
        public Vector4[] Reach => _reach;
        public float BodyYawRad { get; private set; }
        public Vector4 WakeEdge => _wakeEdge;
        public Vector4 WakeParams => _wake;
        public int ReachRefreshCount { get; private set; }
        public Renderer Water => waterRenderer;

        System.Func<Vector2> _windProvider;
        System.Func<float> _stormProvider;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            Resolve();
            Step(0f);
            _windProvider = () => _wind;
            _stormProvider = () => _storm;
            Drift.Core.LifeEnvironment.WindProvider = _windProvider;
            Drift.Core.LifeEnvironment.StormProvider = _stormProvider;
        }

        void OnDisable()
        {
            if (Drift.Core.LifeEnvironment.WindProvider == _windProvider) Drift.Core.LifeEnvironment.WindProvider = null;
            if (Drift.Core.LifeEnvironment.StormProvider == _stormProvider) Drift.Core.LifeEnvironment.StormProvider = null;
            if (waterRenderer != null) waterRenderer.SetPropertyBlock(null);
        }

        void LateUpdate()
        {
            Step(Application.isPlaying ? Time.deltaTime : 0f);
        }

        public void SetRing(int index, Vector2 pos, float age, float strength)
        {
            if (index == 0)
            {
                _ringPos.x = pos.x; _ringPos.y = pos.y;
                _ringAge.x = age; _ringStrength.x = strength;
            }
            else
            {
                _ringPos.z = pos.x; _ringPos.w = pos.y;
                _ringAge.y = age; _ringStrength.y = strength;
            }
        }

        public void Splash(Vector2 pos, float strength)
        {
            _splashPos = pos;
            _splashAge = 0f;
            _splashStrength = Mathf.Clamp01(strength);
        }

        public void SetSky(Color sky, Color deep)
        {
            _hasSky = true;
            _sky = sky;
            _deep = deep;
        }

        public void ClearSky() => _hasSky = false;

        public Island FindPlayer()
        {
            if (player != null && player.isActiveAndEnabled) return player;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl != null && isl.useKeyboardInput && isl.isActiveAndEnabled) return isl;
            }
            return null;
        }

        void Resolve()
        {
            if (waterRenderer == null)
            {
                var go = GameObject.Find(waterObjectName);
                if (go != null) waterRenderer = go.GetComponent<Renderer>();
            }
        }

        // The eight islands nearest the player, as (x, z, boundingRadius, 1); unused slots have w = 0.
        void CollectIslands()
        {
            int n = 0;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var isl = all[i];
                if (isl == null || !isl.isActiveAndEnabled || isl.IsSunk) continue;
                Vector2 c = isl.PlanarPosition;
                float r = isl.BoundingRadius;
                float d = (c - PlayerPos).magnitude - r;
                if (n == IslandSlots && d >= _islandDist[n - 1]) continue;
                int k = n < IslandSlots ? n++ : IslandSlots - 1;
                while (k > 0 && _islandDist[k - 1] > d)
                {
                    _islandDist[k] = _islandDist[k - 1];
                    _islandData[k] = _islandData[k - 1];
                    k--;
                }
                _islandDist[k] = d;
                _islandData[k] = new Vector4(c.x, c.y, r, 1f);
            }
            for (int i = n; i < IslandSlots; i++) _islandData[i] = Vector4.zero;
        }

        // Body-space radial outline, lightly blurred (the raw profile steps by half a heightfield cell) with its
        // angular derivative. Costs a few thousand heightfield samples, so only on a shape change or twice a second.
        void RefreshReach(Island p, float dt)
        {
            _reachAge += dt;
            bool changed = p != _reachOwner || p.Version != _reachVersion;
            bool due = changed ? (p != _reachOwner || !Application.isPlaying || _reachAge >= reachMinInterval) : _reachAge >= reachRefreshInterval;
            if (!due) return;
            _reachOwner = p;
            _reachVersion = p.Version;
            _reachAge = 0f;
            ReachRefreshCount++;
            p.LandReach(_reachRaw);
            const int n = ReachSlots;
            _reachMax = 0f;
            for (int k = 0; k < n; k++)
            {
                float r = 0.2f * _reachRaw[(k + n - 1) % n] + 0.6f * _reachRaw[k] + 0.2f * _reachRaw[(k + 1) % n];
                _reach[k].x = r;
                if (r > _reachMax) _reachMax = r;
            }
            float inv = n / (4f * Mathf.PI);
            for (int k = 0; k < n; k++)
                _reach[k].y = (_reach[(k + 1) % n].x - _reach[(k + n - 1) % n].x) * inv;
        }

        // The widest outline points across the velocity (where the V lines start and between which the
        // turbulent wake lies), found in body space so no trigonometry runs per entry.
        void UpdateWake(Island p, float dt)
        {
            float speed = PlayerVel.magnitude;
            if (p == null || speed < wakeMinSpeed || _reachMax <= 0f)
            {
                _wake = Vector4.zero;
                _hasWakeEdge = false;
                return;
            }
            if (_reachDirs == null)
            {
                _reachDirs = new Vector2[ReachSlots];
                for (int k = 0; k < ReachSlots; k++)
                {
                    float a = k * 2f * Mathf.PI / ReachSlots;
                    _reachDirs[k] = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                }
            }
            float c = Mathf.Cos(BodyYawRad), s = Mathf.Sin(BodyYawRad);
            Vector2 dirW = PlayerVel / speed;
            Vector2 dirB = new Vector2(dirW.x * c - dirW.y * s, dirW.x * s + dirW.y * c);
            Vector2 perpB = new Vector2(-dirB.y, dirB.x);
            float cL = float.MinValue, cR = float.MaxValue, aL = 0f, aR = 0f;
            for (int k = 0; k < ReachSlots; k++)
            {
                float r = _reach[k].x;
                float ac = r * Vector2.Dot(_reachDirs[k], perpB);
                if (ac > cL) { cL = ac; aL = r * Vector2.Dot(_reachDirs[k], dirB); }
                if (ac < cR) { cR = ac; aR = r * Vector2.Dot(_reachDirs[k], dirB); }
            }
            var edge = new Vector4(aL, cL, aR, cR);
            float k2 = _hasWakeEdge && dt > 0f ? 1f - Mathf.Exp(-wakeEdgeSmoothing * dt) : 1f;
            _wakeEdge = Vector4.Lerp(_wakeEdge, edge, k2);
            _hasWakeEdge = true;
            float width = Mathf.Max(0.5f, _wakeEdge.y - _wakeEdge.w);
            float wakeLen = 1.5f * width + 1f;
            float veeLen = 2.4f * width + 4f;
            _wake = new Vector4(wakeLen, veeLen, _reachMax + veeLen + 2f, Mathf.Clamp(width / 8f, 0.6f, 1.25f));
        }

        public void Step(float dt)
        {
            _block ??= new MaterialPropertyBlock();
            if (waterRenderer == null) Resolve();
            _clock += dt;

            var p = FindPlayer();
            bool alive = p != null && !p.IsSunk;
            if (alive) PlayerPos = p.PlanarPosition;

            var plates = PlateSystem.Instance;
            RawCurrent = plates != null ? plates.SampleVelocity(PlayerPos) : Vector2.zero;

            if (alive)
            {
                // Island._carry only tracks the plate current inside Tick (Play Mode); in edit mode it
                // is zero, so subtracting the current there would fake a wake on a resting island.
                if (!Application.isPlaying) PlayerVel = p.SelfVelocity;
                // Not PlanarVelocity - RawCurrent: the island only follows the current with a lag, so where the
                // current jumps (crossing into another plate, or a plate the island itself shoves along) that
                // difference pointed sideways for a second and the wake swung out of the stern as a wedge.
                else PlayerVel = wakeRelativeToCurrent ? p.WaterVelocity : p.PlanarVelocity;
                PlayerRadius = p.BoundingRadius;
                Vector3 bf = p.BodyForward;
                BodyYawRad = Mathf.Atan2(bf.x, bf.z);
                RefreshReach(p, dt);
            }
            else
            {
                PlayerVel = Vector2.zero;
                PlayerRadius = 0f;
            }
            UpdateWake(alive ? p : null, dt);
            float rawSpeed = RawCurrent.magnitude;
            Vector2 rawDir = rawSpeed > 1e-4f ? RawCurrent / rawSpeed : _currentDir;
            float k = dt > 0f ? 1f - Mathf.Exp(-currentSmoothing * dt) : 1f;
            _currentSpeed = Mathf.Lerp(_currentSpeed, rawSpeed, k);
            Vector2 blended = Vector2.Lerp(_currentDir, rawDir, k);
            _currentDir = blended.sqrMagnitude > 1e-6f ? blended.normalized : rawDir;

            var storms = StormSystem.Instance;
            Vector2 gust = Vector2.zero;
            float rawStorm = 0f;
            if (storms != null)
            {
                rawStorm = storms.IntensityAt(PlayerPos);
                if (rawStorm > 0f) gust = storms.GustAt(PlayerPos, storms.Clock);
            }
            if (stormOverride >= 0f) rawStorm = stormOverride;
            RawStorm = rawStorm;
            float ks = dt > 0f ? 1f - Mathf.Exp(-stormSmoothing * dt) : 1f;
            _storm = Mathf.Lerp(_storm, rawStorm, ks);

            float wa = globalWindPeriod > 0f ? _clock * (Mathf.PI * 2f / globalWindPeriod) + 0.7f : 0.7f;
            GlobalWind = new Vector2(Mathf.Cos(wa), Mathf.Sin(wa)) * globalWindSpeed;
            Vector2 rawWind = GlobalWind + RawCurrent * windCurrentWeight + gust * windGustWeight;
            float kw = dt > 0f ? 1f - Mathf.Exp(-windSmoothing * dt) : 1f;
            _wind = Vector2.Lerp(_wind, rawWind, kw);

            _splashAge += dt;
            if (_splashAge > splashLife) _splashStrength = 0f;
            CollectIslands();

            _block.SetVector(PlayerPosId, new Vector4(PlayerPos.x, PlayerPos.y, 0f, 0f));
            _block.SetVector(PlayerVelId, new Vector4(PlayerVel.x, PlayerVel.y, 0f, 0f));
            _block.SetFloat(PlayerRadiusId, PlayerRadius);
            _block.SetVector(RingPosId, _ringPos);
            _block.SetVector(RingAgeId, _ringAge);
            _block.SetVector(RingStrengthId, _ringStrength);
            _block.SetVector(CurrentDirId, new Vector4(_currentDir.x, _currentDir.y, 0f, 0f));
            _block.SetFloat(CurrentSpeedId, _currentSpeed);
            _block.SetFloat(CurrentFoamId, currentStreakStrength);
            _block.SetFloat(StormId, _storm);
            Vector2 wd = WindDir;
            _block.SetVector(WindDirId, new Vector4(wd.x, wd.y, WindSpeed, 0f));
            _block.SetVector(SplashPosId, new Vector4(_splashPos.x, _splashPos.y, _splashAge, _splashStrength));
            _block.SetVectorArray(IslandDataId, _islandData);
            _block.SetVectorArray(PlayerReachId, _reach);
            _block.SetFloat(PlayerBodyYawId, BodyYawRad);
            _block.SetVector(PlayerWakeEdgeId, _wakeEdge);
            _block.SetVector(PlayerWakeId, _wake);
            if (_hasSky)
            {
                _block.SetColor(SkyColorId, _sky);
                _block.SetColor(DeepColorId, _deep);
            }

            if (waterRenderer != null) waterRenderer.SetPropertyBlock(_block);
        }
    }
}
