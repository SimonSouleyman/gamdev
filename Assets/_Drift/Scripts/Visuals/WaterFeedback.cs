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
        [Header("Strömung im Wasser")]
        [Tooltip("Stärke der Schaumflocken, die mit der Plattenströmung treiben (Richtung und Tempo zeigen, wohin das Meer die Insel schiebt).")]
        [Range(0f, 2f)] public float currentStreakStrength = 0.6f;
        [Tooltip("Zusätzliche Betonung der Flocken um die Spielerinsel, je stärker die Strömung sie gerade mitnimmt.")]
        [Range(0f, 2f)] public float currentPlayerEmphasis = 0.6f;
        [Tooltip("Kantenlänge des Strömungsfelds um den Blickpunkt (Welteinheiten). Außerhalb blenden die Flocken aus.")]
        public float currentFieldSize = 320f;
        [Tooltip("Auflösung des Strömungsfelds (Texel pro Kante).")]
        public int currentFieldResolution = 64;
        [Tooltip("Wie oft pro Sekunde das Strömungsfeld aus den Platten neu berechnet wird.")]
        public float currentFieldRate = 4f;
        [Tooltip("Breite der Grenzzone (Brandung) beiderseits einer Plattengrenze, in der die Flocken dichter werden.")]
        public float currentEdgeWidth = 10f;
        [Tooltip("Plattentempo, das die Textur höchstens abbildet (Einheiten/s); schneller wird abgeschnitten.")]
        public float currentFieldMaxSpeed = 8f;
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
        [Tooltip("Schaum und Tempolinien folgen der echten Küstenlinie (Abstandsfeld). Aus = nur Bugwelle und Kielwasserlinien.")]
        public bool coastField = true;
        [Tooltip("Wie weit von der Küste weg die Strömungsflocken um die Insel betont werden (Welteinheiten).")]
        [Range(2f, 30f)] public float coastEmphasisRange = 12f;

        [Header("Tempogefühl im Wasser")]
        [Tooltip("Tempogefühl im Wasser: Bugwelle, Gischt, Tempolinien und die Strömungs-Rückmeldung. Aus = Wasser wie bisher.")]
        public bool speedFeelEnabled = true;
        [Tooltip("Wie viel kräftiger Bugwelle und Kielwasser bei vollem Tempo werden.")]
        [Range(0f, 2f)] public float speedWakeBoost = 0.8f;
        [Tooltip("Stärke der Tempolinien, die bei Fahrt neben der Insel vorbeiziehen. 0 = aus.")]
        [Range(0f, 2f)] public float speedLineStrength = 0.45f;
        [Tooltip("Wie viel heller und länger die Schaumflocken werden, wenn du mit der Strömung fährst (Strömungsgefühl).")]
        [Range(0f, 2f)] public float flowFoamBoost = 1f;
        [Tooltip("Wie deutlich eine Plattengrenze aufleuchtet, während du darauf surfst.")]
        [Range(0f, 2f)] public float surfFoamStrength = 1f;
        [Tooltip("Stärke des Gischtrings beim Verschmelzen. 0 = aus.")]
        [Range(0f, 1f)] public float mergeSprayStrength = 0.9f;
        [Tooltip("Wie lange der Gischtring beim Verschmelzen zu sehen ist (Sekunden).")]
        [Range(0.2f, 2f)] public float sprayLife = 0.9f;

        static readonly int PlayerPosId = Shader.PropertyToID("_PlayerPos");
        static readonly int PlayerVelId = Shader.PropertyToID("_PlayerVel");
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
        static readonly int CoastFieldId = Shader.PropertyToID("_CoastField");
        static readonly int CoastParamsId = Shader.PropertyToID("_CoastParams");
        static readonly int CoastRotId = Shader.PropertyToID("_CoastRot");
        static readonly int PlayerWakeEdgeId = Shader.PropertyToID("_PlayerWakeEdge");
        static readonly int PlayerWakeId = Shader.PropertyToID("_PlayerWake");
        static readonly int CurrentFieldId = Shader.PropertyToID("_CurrentField");
        static readonly int CurrentFieldParamsId = Shader.PropertyToID("_CurrentFieldParams");
        static readonly int CurrentEmphasisId = Shader.PropertyToID("_CurrentEmphasis");
        static readonly int SpeedFeelId = Shader.PropertyToID("_SpeedFeel");
        static readonly int SpeedFeelGainsId = Shader.PropertyToID("_SpeedFeelGains");
        static readonly int SprayPosId = Shader.PropertyToID("_SprayPos");
        public const int ReachSlots = 32;

        MaterialPropertyBlock _block;
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
        readonly CoastField _coast = new();
        Vector4 _coastParams;
        Vector4 _wakeEdge, _wake;
        bool _hasWakeEdge;
        CurrentField _field;
        float _fieldAge = float.MaxValue;
        Vector4 _fieldParams, _emphasis;
        readonly SpeedFeel.Tracker _feel = new();
        Vector2 _sprayPos;
        float _sprayAge = 10f, _sprayStrength;

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
        public CurrentField Field => _field;
        // The signed-distance field of the player island's coastline (body space) the water reads.
        public CoastField Coast => _coast;
        public Vector4 CoastParams => _coastParams;
        // 0..1.4 how much the island is under way, 0..1 how long it has been riding the current, 0..1 surf.
        public float SpeedDrive => _feel.Drive;
        public float FlowAmount => _feel.Flow;
        public float SurfAmount => _feel.Surf;
        public float CurrentAlignment => _feel.Alignment;
        public Vector4 SpeedFeelVector { get; private set; }
        public Vector4 SprayVector { get; private set; }
        // The plate current actually carrying the player island (Island._carry in Play Mode, the plate under it otherwise).
        public Vector2 CarryVelocity { get; private set; }
        public Vector4 CurrentEmphasis => _emphasis;

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
            Island.Merged += OnMerged;
        }

        void OnDisable()
        {
            Island.Merged -= OnMerged;
            if (Drift.Core.LifeEnvironment.WindProvider == _windProvider) Drift.Core.LifeEnvironment.WindProvider = null;
            if (Drift.Core.LifeEnvironment.StormProvider == _stormProvider) Drift.Core.LifeEnvironment.StormProvider = null;
            if (waterRenderer != null) waterRenderer.SetPropertyBlock(null);
            _field?.Release();
            _field = null;
            _fieldAge = float.MaxValue;
            _coast.Release();
            _coastParams = Vector4.zero;
            _reachOwner = null;
            _reachVersion = -1;
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

        // A wide, fast ring of spray, thrown where two islands meet. Its own slot, so the fish splashes above
        // keep working during a merge.
        public void Spray(Vector2 pos, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f) return;
            _sprayPos = pos;
            _sprayAge = 0f;
            _sprayStrength = strength;
        }

        void OnMerged(Island host, Island guest, float energy)
        {
            if (!speedFeelEnabled || mergeSprayStrength <= 0f || host == null || guest == null) return;
            Vector2 contact = (host.PlanarPosition + guest.PlanarPosition) * 0.5f;
            Spray(contact, mergeSprayStrength * Mathf.Clamp01(0.45f + energy / 220f));
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

        // The island's outline, in body space, on a shape change or twice a second: the radial profile (used only
        // to find the two widest points the V lines leave from) and the coast distance field the water samples.
        // Both are body space, so nothing has to be rebuilt while the island only moves or turns.
        void RefreshOutline(Island p, float dt)
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
            // The distance field is the expensive half (a height sample per texel), and in body space it only goes
            // stale when the shape itself does - which Island.Version already reports, sinking included. So it is
            // rebuilt on a version change only, never on the idle half-second tick.
            if (!changed && _coastParams.w > 0f) return;
            _coastParams = coastField && _coast.Refresh(p)
                ? new Vector4(_coast.Origin.x, _coast.Origin.y, 1f / _coast.Size, 1f)
                : Vector4.zero;
        }

        // The two widest outline points across the velocity - where the V lines leave the island - found in body
        // space so no trigonometry runs per entry. The churned wake itself follows the coast field, not this box.
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
            // How far astern the churn reaches, measured from the shore - never past the coast field, whose
            // border fade would otherwise cut the foam off along a straight line.
            float wakeLen = Mathf.Min(0.9f * width + 2f, 0.72f * (_coastParams.w > 0f ? _coast.Margin : 1e4f));
            float veeLen = 2.4f * width + 4f;
            _wake = new Vector4(wakeLen, veeLen, _reachMax + veeLen + 2f, Mathf.Clamp(width / 8f, 0.6f, 1.25f));
        }

        // The view's centre on the water: the camera's look ray on the sea plane, else the player.
        Vector2 FieldCentre()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Transform t = cam.transform;
                Vector3 f = t.forward;
                if (f.y < -0.05f)
                {
                    float s = Mathf.Min(-t.position.y / f.y, currentFieldSize * 0.3f);
                    return new Vector2(t.position.x + f.x * s, t.position.z + f.z * s);
                }
                Vector2 f2 = new Vector2(f.x, f.z);
                if (f2.sqrMagnitude > 1e-6f) return new Vector2(t.position.x, t.position.z) + f2.normalized * (currentFieldSize * 0.3f);
            }
            return PlayerPos;
        }

        void UpdateField(PlateSystem plates, float dt)
        {
            int res = Mathf.Clamp(currentFieldResolution, 8, 128);
            if (_field == null || _field.Resolution != res)
            {
                _field?.Release();
                _field = new CurrentField(res);
                _fieldAge = float.MaxValue;
            }
            _fieldAge += dt;
            float interval = currentFieldRate > 0f ? 1f / currentFieldRate : 0f;
            if (plates == null)
            {
                _fieldParams = Vector4.zero;
                return;
            }
            if (dt > 0f && _fieldAge < interval) return;
            _fieldAge = 0f;
            float size = Mathf.Max(16f, currentFieldSize);
            _field.Refresh(plates, FieldCentre(), size, Mathf.Max(0.5f, currentFieldMaxSpeed), currentEdgeWidth);
            _fieldParams = new Vector4(_field.Origin.x, _field.Origin.y, 1f / _field.Size, _field.MaxSpeed);
        }

        // Flecks around the player get brighter the harder the current carries the island, and more so when it
        // pushes against the way the player is steering (the "why am I not getting anywhere" case).
        void UpdateEmphasis(Island p)
        {
            if (p == null || currentPlayerEmphasis <= 0f)
            {
                _emphasis = Vector4.zero;
                return;
            }
            float carry = CarryVelocity.magnitude;
            float against = 0f;
            Vector2 self = p.SelfVelocity;
            if (carry > 1e-3f && self.sqrMagnitude > 1e-4f)
                against = Mathf.Clamp01(-Vector2.Dot(CarryVelocity / carry, self.normalized));
            float strength = currentPlayerEmphasis * Mathf.Clamp01(carry / 2.5f) * (1f + against);
            // x is a distance from the COASTLINE, not a radius: around a branched island a circle put the
            // emphasis on open water between the arms and missed the water right off the outer shores.
            _emphasis = new Vector4(Mathf.Max(coastEmphasisRange, 1f), strength, against, 0f);
        }

        // Drive / flow / surf for the shader. In edit mode dt is 0, so the tracker snaps to the island's
        // current state - which is exactly what an edit-mode render wants to show.
        void UpdateSpeedFeel(Island p, float dt)
        {
            _feel.Step(p, dt);
            if (!speedFeelEnabled)
            {
                SpeedFeelVector = Vector4.zero;
                SprayVector = Vector4.zero;
                return;
            }
            SpeedFeelVector = new Vector4(SpeedFeel.Framing(_feel.Drive), _feel.Flow, _feel.Surf, _feel.Speed);
            // z is the ring's age as a share of its life, so the slider really sets how long it takes.
            SprayVector = new Vector4(_sprayPos.x, _sprayPos.y, _sprayAge / Mathf.Max(0.05f, sprayLife), _sprayStrength);
        }

        Vector4 SpeedFeelGains => speedFeelEnabled
            ? new Vector4(speedLineStrength, speedWakeBoost, flowFoamBoost, surfFoamStrength)
            : Vector4.zero;

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
            UpdateField(plates, dt);

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
                RefreshOutline(p, dt);
            }
            else
            {
                PlayerVel = Vector2.zero;
                PlayerRadius = 0f;
                _coastParams = Vector4.zero;
                _reachOwner = null;
            }
            UpdateWake(alive ? p : null, dt);
            CarryVelocity = !alive ? Vector2.zero : Application.isPlaying ? p.PlanarVelocity - p.WaterVelocity : RawCurrent;
            UpdateEmphasis(alive ? p : null);
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
            _sprayAge += dt;
            if (_sprayAge > sprayLife) _sprayStrength = 0f;
            UpdateSpeedFeel(alive ? p : null, dt);

            _block.SetVector(PlayerPosId, new Vector4(PlayerPos.x, PlayerPos.y, 0f, 0f));
            _block.SetVector(PlayerVelId, new Vector4(PlayerVel.x, PlayerVel.y, 0f, 0f));
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
            _block.SetVector(CoastParamsId, _coastParams);
            _block.SetVector(CoastRotId, new Vector4(Mathf.Cos(BodyYawRad), Mathf.Sin(BodyYawRad), 0f, 0f));
            _block.SetVector(PlayerWakeEdgeId, _wakeEdge);
            _block.SetVector(PlayerWakeId, _wake);
            _block.SetVector(CurrentFieldParamsId, _fieldParams);
            _block.SetVector(CurrentEmphasisId, _emphasis);
            _block.SetVector(SpeedFeelId, SpeedFeelVector);
            _block.SetVector(SpeedFeelGainsId, SpeedFeelGains);
            _block.SetVector(SprayPosId, SprayVector);
            if (_field != null && _field.Texture != null) _block.SetTexture(CurrentFieldId, _field.Texture);
            if (_coast.Texture != null) _block.SetTexture(CoastFieldId, _coast.Texture);
            if (_hasSky)
            {
                _block.SetColor(SkyColorId, _sky);
                _block.SetColor(DeepColorId, _deep);
            }

            if (waterRenderer != null) waterRenderer.SetPropertyBlock(_block);
        }
    }
}
