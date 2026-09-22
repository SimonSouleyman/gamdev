using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // The planet look on top of the flat torus: pushes the globals of Shaders/DriftCurve.hlsl per camera, right
    // before it renders. Vertices are wrapped onto a sphere of radius R touching the sea at the point the camera
    // looks at (identity there, a drop of ~d^2 / (2R) around it), and R shrinks as the view grows, from
    // maxRadius (a tiny island close up is practically flat) to minRadius (a continent or a zoomed-out view
    // shows a round limb). Only the main camera, cameras registered through Register() and (optionally) the
    // Scene view are bent; the island preview and the portrait camera of Tilda render flat.
    // Also owns what the limb needs to look closed: the horizon haze the world shaders fade into, a sky dome
    // (Drift/CurvedSky) whose gradient starts at the limb instead of at eye level, and a widened culling matrix
    // (renderer bounds are unbent, so islands near the limb would otherwise be culled while still on screen).
    // The sky (Shaders/DriftSky.hlsl: gradient, sun, moon, stars, Milky Way, clouds) gets all its globals from
    // here: the colours are DayNightCycle.Sky, the shared palette, so the limb colour of the sky is by
    // construction the haze colour and the water's reflection tint.
    // Adventure mode (RingWorld.Active): the world is rolled up into the inside of a closed ring instead (DriftRingWS),
    // the sea ending cleanly at the two rims. The rims are invisible - only a foam line lies on the water there
    // (RingRims) - plus a haze by distance along the ring and a culling box around the whole band.
    [ExecuteAlways]
    [DefaultExecutionOrder(300)]
    public class CurvedWorld : MonoBehaviour
    {
        public float maxRadius = 900f;
        public float minRadius = 260f;
        [Range(0f, 2f)] public float strength = 1f;
        public bool flatten;
        // Camera-to-focus distance mapped (logarithmically) onto maxRadius..minRadius. The chase camera sits at
        // ~12 u for a fresh island at zoom 1 and beyond 120 u for a continent or a fully zoomed-out view.
        public float viewNear = 12f;
        public float viewFar = 120f;
        // Closer than viewNear the camera is so low that even maxRadius would put the horizon a few dozen units
        // away, so R keeps growing by (viewNear / distance)^closeFlatten: a close-up stays an open sea.
        public float closeFlatten = 1.5f;
        // Off by default: handles and gizmos are not bent, so a bent Scene view is misleading to edit in.
        public bool bendSceneView;

        [Range(0f, 1f)] public float haze = 0.8f;
        [Range(0f, 0.95f)] public float hazeStart = 0.35f;
        public float flatHazeEnd = 700f;
        // The haze is complete at this fraction of the limb distance: the last stretch before the limb is a pixel
        // or two on screen, so ending the fade on the limb itself left a visible line between sea and sky.
        [Range(0.5f, 1f)] public float hazeFull = 0.85f;
        // A low camera sees the far sea at a grazing angle: everything beyond a third of the limb distance is
        // squeezed into half a degree under the horizon. Below lowLimbAngle (degrees the limb lies under eye level)
        // the haze therefore starts and completes much closer, so it still is a soft band on screen.
        [Range(0f, 0.9f)] public float lowHazeStart = 0.1f;
        [Range(0.2f, 1f)] public float lowHazeFull = 0.55f;
        public float lowLimbAngle = 4f;
        public float highLimbAngle = 20f;

        public bool drawSky = true;
        public Material skyMaterial;
        // Fallback for cameras that clear to the skybox but are not bent (they get no dome). Play Mode only unless
        // skyboxInEditMode: a runtime material written into RenderSettings would be saved into the scene as "none".
        public bool driveSkybox = true;
        public bool skyboxInEditMode;
        public Material skyboxMaterial;
        public DayNightCycle dayNight;
        // Time of day the palette is read at while there is no DayNightCycle.
        [Range(0f, 1f)] public float fallbackTimeOfDay = 0.4f;
        public float skyGradient = 4f;

        public float sunSize = 2f;
        public float sunHorizonScale = 1.35f;
        [Range(0f, 2f)] public float sunHalo = 1f;
        public float moonSize = 2.4f;
        [Range(0f, 2f)] public float moonHalo = 1f;
        public Color moonColor = new Color(0.93f, 0.95f, 1f);
        [Range(0f, 1f)] public float dayMoon = 0.3f;

        // Cells per unit direction: 64 gives about 6 000 stars over the visible half of the sky.
        [Range(16f, 128f)] public float starDensity = 64f;
        [Range(0.02f, 0.25f)] public float starSize = 0.11f;
        [Range(0f, 3f)] public float starBrightness = 1f;
        public float starTwinkle = 2.2f;
        [Range(0f, 3f)] public float milkyWay = 0.8f;
        public float shootingStarPeriod = 7f;
        [Range(0f, 1f)] public float shootingStarChance = 0.6f;
        public float shootingStarDuration = 0.9f;
        public float shootingStarLength = 9f;

        // The sky shader's own cloud layer (a noise field seen from below, DriftSky.hlsl). It reads as thin streaks
        // rather than as clouds and doubles what the puffy clumps of CloudShadows already draw, so it is off: the
        // sky keeps only its gradient, sun, moon and stars.
        [Tooltip("Die Schleierwolken des Himmels zeichnen. Aus = am Himmel sind nur die Schäfchenwolken zu sehen.")]
        public bool skyClouds;
        // Cover of the sky clouds relative to CloudShadows' cover (same field, offset and wind as the shadows).
        [Tooltip("Dichte der Schleierwolken, falls sie eingeschaltet sind.")]
        [Range(0f, 2f)] public float cloudAmount = 1.15f;
        public float cloudHeight = 150f;
        // Degrees the limb lies under eye level: beyond the second value the camera looks at a planet from
        // outside and a cloud layer "above" it makes no sense, so the sky clouds fade out in between.
        public Vector2 cloudFadeLimbAngle = new Vector2(36f, 48f);
        // Larger = the layer bends down towards the horizon, so far clouds stay puffs instead of thin streaks.
        [Range(0.05f, 0.6f)] public float cloudHorizonSoftening = 0.22f;
        [Range(0f, 3f)] public float cloudShading = 1.1f;
        public float cullTop = 14f;
        public float cullBottom = -4f;

        [Header("Ringwelt (Abenteuer)")]
        [Tooltip("Dunst über dem Ring: Anteil der Horizontfarbe auf Inseln und Wänden in der Ferne (das Meer wird bei vollem Dunst ganz zur Horizontfarbe).")]
        [Range(0f, 1f)] public float ringHaze = 0.6f;
        [Tooltip("Ab dieser Entfernung entlang des Rings beginnt der Dunst.")]
        public float ringHazeStart = 60f;
        [Tooltip("Entfernung, bei der der Dunst voll wäre. Größer als der halbe Umfang: die Gegenseite des Rings hoch am Himmel bleibt sichtbar.")]
        public float ringHazeEnd = 620f;
        [Tooltip("Die Schaumlinie an den beiden Rändern des Rings zeichnen (die Ränder selbst bleiben unsichtbar).")]
        public bool ringRims = true;
        [Tooltip("Material der Schaumlinie (leer = Drift/RingRim).")]
        public Material rimMaterial;
        [Tooltip("Länge eines Abschnitts der Schaumlinie.")]
        public float rimSegment = 8f;
        [Tooltip("Wie weit die Schaumlinie vom Rand nach innen reicht.")]
        [Range(1f, 12f)] public float rimFoamWidth = 4f;

        [Header("Außerhalb des Rings")]
        [Tooltip("Jenseits der Ränder des Bands nur den Sternenhimmel zeigen: kein Himmelsverlauf, keine Wolken, kein Dunst. Über dem Band bleibt der Himmel wie immer.")]
        [Range(0f, 1f)] public float ringSpace = 1f;
        [Tooltip("Farbe des Weltraums zwischen den Sternen.")]
        public Color ringSpaceColor = new Color(0.016f, 0.021f, 0.047f);
        [Tooltip("Wie hell die Sterne draußen bei Tag stehen (1 = so hell wie nachts).")]
        [Range(0f, 1f)] public float ringSpaceStars = 1f;
        [Tooltip("Sonne und Mond auch draußen als Scheibe zeigen (ohne Hof).")]
        [Range(0f, 1f)] public float ringSpaceDiscs = 1f;
        [Tooltip("Wie viel von der Milchstraße draußen übrig bleibt. Voll aufgedreht liegt ihr Staub als graues Gewölk über dem Sternenfeld.")]
        [Range(0f, 1f)] public float ringSpaceMilkyWay = 0.3f;
        [Tooltip("Weiche Kante am Rand des Bands, in Einheiten.")]
        [Range(0f, 4f)] public float ringSpaceEdge = 0.6f;

        const string SkyName = "CurvedSky";
        const string SkyShader = "Drift/CurvedSky";
        const string SkyboxShader = "Drift/Skybox";
        const string RimName = "RingRims";
        const string RimShader = "Drift/RingRim";

        static readonly int FocusId = Shader.PropertyToID("_CurveFocus");
        static readonly int InvRadiusId = Shader.PropertyToID("_CurveInvRadius");
        static readonly int RingId = Shader.PropertyToID("_CurveRing");
        static readonly int RimGlowId = Shader.PropertyToID("_RimGlow");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int LightAmountId = Shader.PropertyToID("_LightAmount");
        static readonly int FogColorId = Shader.PropertyToID("_CurveFogColor");
        static readonly int FogParamsId = Shader.PropertyToID("_CurveFogParams");
        static readonly int ZenithId = Shader.PropertyToID("_CurveSkyZenith");
        static readonly int SkyCenterId = Shader.PropertyToID("_CurveSkyCenter");
        static readonly int SkyDrawId = Shader.PropertyToID("_CurveSkyDraw");
        static readonly int SunDirId = Shader.PropertyToID("_SkySunDir");
        static readonly int SunDiscId = Shader.PropertyToID("_SkySunDisc");
        static readonly int SunGlowId = Shader.PropertyToID("_SkySunGlow");
        static readonly int AntiGlowId = Shader.PropertyToID("_SkyAntiGlow");
        static readonly int MoonDirId = Shader.PropertyToID("_SkyMoonDir");
        static readonly int MoonRightId = Shader.PropertyToID("_SkyMoonRight");
        static readonly int MoonUpId = Shader.PropertyToID("_SkyMoonUp");
        static readonly int MoonColorId = Shader.PropertyToID("_SkyMoonColor");
        static readonly int StarsId = Shader.PropertyToID("_SkyStars");
        static readonly int StarParamsId = Shader.PropertyToID("_SkyStarParams");
        static readonly int StarRot0Id = Shader.PropertyToID("_SkyStarRot0");
        static readonly int StarRot1Id = Shader.PropertyToID("_SkyStarRot1");
        static readonly int StarRot2Id = Shader.PropertyToID("_SkyStarRot2");
        static readonly int CloudLitId = Shader.PropertyToID("_SkyCloudLit");
        static readonly int CloudDarkId = Shader.PropertyToID("_SkyCloudDark");
        static readonly int CloudLightId = Shader.PropertyToID("_SkyCloudLight");
        static readonly int ShootHeadId = Shader.PropertyToID("_SkyShootHead");
        static readonly int ShootTailId = Shader.PropertyToID("_SkyShootTail");
        static readonly int SpaceId = Shader.PropertyToID("_SkySpace");
        static readonly int SpaceColorId = Shader.PropertyToID("_SkySpaceColor");

        static readonly HashSet<Camera> Extra = new HashSet<Camera>();
        static CurvedWorld _active;
        static readonly Vector2[] EdgeSamples =
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.5f),
            new Vector2(1f, 1f), new Vector2(0.75f, 1f), new Vector2(0.5f, 1f), new Vector2(0.25f, 1f),
            new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0.75f), new Vector2(1f, 0.75f),
        };

        Material _skyMat;
        Mesh _rimMesh;
        Material _rimMat;
        Vector4 _rimKey;
        Material _skyboxMat;
        Material _skyboxBefore;
        bool _skyboxSet;
        Mesh _skyMesh;
        Camera _culled;
        SkyPalette _fallbackPalette;
        Vector3 _sunDir = Vector3.up, _moonDir = Vector3.down;
        float _sunChord2 = 1e-3f, _moonInvChord = 25f, _moonPhase = 0.5f;
        float _skyClock;
        Vector4 _cloudLit;

        // State of the main camera's bend (what gameplay code should use through Bend/DropAt).
        public Vector2 Focus { get; private set; }
        public float Radius { get; private set; } = float.PositiveInfinity;
        public float InvRadius { get; private set; }
        public float ViewBlend { get; private set; }
        public float LimbDistance { get; private set; }
        // sRGB, exactly the palette's horizon / zenith (the haze colour and DayNightCycle.WaterSky are the same value).
        public Color HorizonColor { get; private set; }
        public Color ZenithColor { get; private set; }
        // Drives the shooting stars; advances in Play Mode, settable for verification.
        public float SkyClock { get => _skyClock; set => _skyClock = value; }

        public static CurvedWorld Active => _active;

        // Cameras other than the main one that should show the bent world (photo / verification cameras).
        public static void Register(Camera cam) { if (cam != null) Extra.Add(cam); }
        public static void Unregister(Camera cam) => Extra.Remove(cam);

        // Where a world position is drawn: use it before WorldToScreenPoint for labels and tap picking far
        // from the focus (at the focus the two are identical). Mirrors DriftCurveWS in Shaders/DriftCurve.hlsl.
        public static Vector3 Bend(Vector3 world)
        {
            var cw = _active;
            if (cw == null) return world;
            var ring = RingWorld.Active;
            if (ring != null) return ring.Geometry.Bend(world, cw.Focus);
            return Wrap(world, cw.Focus, cw.InvRadius);
        }

        // Shader globals of the ring bend (_CurveRing): the band's lip is where the world ends.
        public static Vector4 RingParams(RingGeometry g) =>
            new Vector4(1f / Mathf.Max(1f, g.Radius), g.centerX, g.halfWidth, g.HalfLength);

        public static Vector3 Wrap(Vector3 world, Vector2 focus, float invR)
        {
            if (invR <= 0f || world.y < -1000f) return world;
            float dx = world.x - focus.x, dz = world.z - focus.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float arc = Mathf.Max(d * invR, 1e-5f);
            float t = Mathf.Min(arc, 3f);
            float s2 = Mathf.Sin(t * 0.5f), c2 = Mathf.Cos(t * 0.5f);
            float versin = 2f * s2 * s2;
            float k = 2f * s2 * c2 / arc * (1f + world.y * invR);
            return new Vector3(focus.x + dx * k, world.y * (1f - versin) - versin / invR, focus.y + dz * k);
        }

        // How far the sea surface at worldXZ is drawn below y = 0.
        public static float DropAt(Vector2 worldXZ)
        {
            var cw = _active;
            if (cw == null) return 0f;
            var ring = RingWorld.Active;
            if (ring != null) return -ring.Geometry.Bend(new Vector3(worldXZ.x, 0f, worldXZ.y), cw.Focus).y;
            if (cw.InvRadius <= 0f) return 0f;
            float t = Mathf.Min((worldXZ - cw.Focus).magnitude * cw.InvRadius, 3f);
            float s2 = Mathf.Sin(t * 0.5f);
            return 2f * s2 * s2 / cw.InvRadius;
        }

        void OnEnable()
        {
            _active = this;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            Refresh();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (_active == this) _active = null;
            if (_culled != null) { _culled.ResetCullingMatrix(); _culled = null; }
            Shader.SetGlobalFloat(InvRadiusId, 0f);
            Shader.SetGlobalVector(RingId, Vector4.zero);
            Shader.SetGlobalVector(SpaceId, Vector4.zero);
            Shader.SetGlobalFloat(RimGlowId, 0f);
            DestroyRims();
            Shader.SetGlobalVector(FogColorId, Vector4.zero);
            Shader.SetGlobalVector(FogParamsId, Vector4.zero);
            Shader.SetGlobalFloat(SkyDrawId, 0f);
            DestroySky();
            RestoreSkybox();
            InvRadius = 0f;
            Radius = float.PositiveInfinity;
        }

        void LateUpdate()
        {
            if (Application.isPlaying) _skyClock += Time.deltaTime;
            Refresh();
        }

        public void Refresh()
        {
            if (dayNight == null) dayNight = GetComponent<DayNightCycle>();
            UpdateColors();
            EnsureSky();
            EnsureSkybox();
            var cam = Camera.main;
            var ring = RingWorld.Active;
            EnsureRims(ring);
            if (cam == null) return;
            if (ring != null)
            {
                Focus = WaterFollower.ViewFocus(cam.transform);
                InvRadius = 0f;
                Radius = float.PositiveInfinity;
                ViewBlend = 0f;
                LimbDistance = ring.Geometry.HalfLength;
                PlaceRims(ring, Focus);
                return;
            }
            Solve(cam, out Vector2 focus, out float invR, out float t, out _, out float limb, out _);
            Focus = focus;
            InvRadius = invR;
            Radius = invR > 0f ? 1f / invR : float.PositiveInfinity;
            ViewBlend = t;
            LimbDistance = limb;
        }

        public float RadiusForViewDistance(float viewDistance) => RadiusForViewDistance(viewDistance, out _);

        public float RadiusForViewDistance(float viewDistance, out float blend)
        {
            float near = Mathf.Max(0.01f, viewNear);
            viewDistance = Mathf.Max(0.01f, viewDistance);
            blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Log(near), Mathf.Log(Mathf.Max(near + 0.01f, viewFar)), Mathf.Log(viewDistance)));
            float r = Mathf.Max(1f, Mathf.Lerp(maxRadius, minRadius, blend));
            if (viewDistance < near && closeFlatten > 0f) r *= Mathf.Pow(near / Mathf.Max(0.25f, viewDistance), closeFlatten);
            return r;
        }

        static Vector4 Lin(Color c, float w) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, w); }

        // Everything about the sky that does not depend on the camera. Palette colours are sRGB and go to the
        // shaders as linear values (SetGlobalVector converts nothing; WaterFeedback's SetColor converts the water's
        // copy of the same horizon colour the same way).
        void UpdateColors()
        {
            bool live = dayNight != null && dayNight.isActiveAndEnabled;
            SkyKey sky;
            float stars, disc, phase, turn, storm = 0f;
            Vector3 axis, light;
            if (live)
            {
                sky = dayNight.Sky;
                stars = dayNight.StarVisibility;
                disc = dayNight.SunDiscVisibility;
                _sunDir = dayNight.TrueSunDirection;
                _moonDir = dayNight.MoonDirection;
                phase = dayNight.moonPhase;
                axis = SkyMath.CelestialAxis(dayNight.axisTilt, dayNight.azimuth);
                turn = dayNight.timeOfDay;
                storm = dayNight.StormAmount;
                light = dayNight.SunDirection;
            }
            else
            {
                _fallbackPalette ??= new SkyPalette();
                sky = _fallbackPalette.Evaluate(fallbackTimeOfDay);
                _sunDir = SkyMath.SunDirection(fallbackTimeOfDay, 35f, 40f);
                _moonDir = SkyMath.MoonDirection(fallbackTimeOfDay, 66f, 65f, 0.3f);
                stars = SkyMath.StarVisibility(_sunDir.y);
                disc = SkyMath.SunDiscVisibility(_sunDir.y);
                phase = 0.62f;
                axis = SkyMath.CelestialAxis(35f, 40f);
                turn = fallbackTimeOfDay;
                light = _sunDir;
            }
            HorizonColor = sky.horizon;
            ZenithColor = sky.zenith;
            _moonPhase = phase;

            float sunChord = 2f * Mathf.Sin(0.5f * Mathf.Deg2Rad * Mathf.Max(0.05f, sunSize) * SkyMath.SunDiscScale(_sunDir.y, sunHorizonScale));
            _sunChord2 = sunChord * sunChord;
            _moonInvChord = 1f / (2f * Mathf.Sin(0.5f * Mathf.Deg2Rad * Mathf.Max(0.05f, moonSize)));
            float moonVis = Mathf.Lerp(dayMoon, 1f, Mathf.SmoothStep(0f, 1f, stars)) * (1f - 0.85f * storm);

            Shader.SetGlobalVector(ZenithId, Lin(sky.zenith, 0f));
            Shader.SetGlobalVector(SunDiscId, Lin(sky.sunDisc, disc));
            Shader.SetGlobalVector(SunGlowId, Lin(sky.sunGlow, sky.sunGlow.a));
            Shader.SetGlobalVector(AntiGlowId, Lin(sky.antiGlow, sky.antiGlow.a));
            Shader.SetGlobalVector(MoonColorId, Lin(moonColor, moonVis));
            Shader.SetGlobalVector(StarsId, new Vector4(stars * starBrightness, starDensity, starTwinkle, milkyWay));
            Shader.SetGlobalVector(StarParamsId, new Vector4(starSize, moonHalo * SkyMath.MoonLit(phase), sunHalo, Mathf.Max(0.1f, skyGradient)));
            _cloudLit = Lin(sky.cloudLit, skyClouds ? cloudAmount : 0f);
            Shader.SetGlobalVector(CloudLitId, _cloudLit);
            Shader.SetGlobalVector(CloudDarkId, Lin(sky.cloudDark, cloudHeight));
            var flatLight = new Vector2(light.x, light.z);
            float fl = flatLight.magnitude;
            flatLight = fl > 1e-4f ? flatLight / fl : Vector2.zero;
            Shader.SetGlobalVector(CloudLightId, new Vector4(flatLight.x, flatLight.y, cloudShading * Mathf.Clamp01(fl * 1.5f), Mathf.Max(0.05f, cloudHorizonSoftening)));

            // World direction -> star space: the field turns with the sun around the celestial axis.
            Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.Inverse(Quaternion.AngleAxis(turn * 360f, axis)));
            Shader.SetGlobalVector(StarRot0Id, rot.GetRow(0));
            Shader.SetGlobalVector(StarRot1Id, rot.GetRow(1));
            Shader.SetGlobalVector(StarRot2Id, rot.GetRow(2));

            float width = 2f * Mathf.Sin(0.5f * Mathf.Deg2Rad * 0.22f);
            if (stars > 0f && SkyMath.ShootingStar(_skyClock, shootingStarPeriod, shootingStarChance, shootingStarDuration, shootingStarLength,
                    out Vector3 head, out Vector3 tail, out float intensity))
            {
                Shader.SetGlobalVector(ShootHeadId, new Vector4(head.x, head.y, head.z, intensity));
                Shader.SetGlobalVector(ShootTailId, new Vector4(tail.x, tail.y, tail.z, width));
            }
            else
            {
                Shader.SetGlobalVector(ShootHeadId, new Vector4(0f, 1f, 0f, 0f));
                Shader.SetGlobalVector(ShootTailId, new Vector4(0f, 1f, 0.01f, width));
            }
        }

        // Sun and moon are drawn where this camera sees them: elevation counts from the limb, not from eye level,
        // so the sun sets into the planet's edge at every zoom and can never hang above it at night.
        void PushCelestial(Vector4 skyCenter)
        {
            var centre = new Vector3(skyCenter.x, skyCenter.y, skyCenter.z);
            Vector3 s = SkyMath.Apparent(_sunDir, centre, skyCenter.w);
            Vector3 m = SkyMath.Apparent(_moonDir, centre, skyCenter.w);
            Shader.SetGlobalVector(SunDirId, new Vector4(s.x, s.y, s.z, _sunChord2));
            Vector4 lit = _cloudLit;
            lit.w *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cloudFadeLimbAngle.x, cloudFadeLimbAngle.y, SkyMath.LimbDepression(centre, skyCenter.w)));
            Shader.SetGlobalVector(CloudLitId, lit);
            Shader.SetGlobalVector(MoonDirId, new Vector4(m.x, m.y, m.z, _moonInvChord));
            Vector3 right = Vector3.Cross(Vector3.up, m);
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(m, right);
            // The terminator leans a little, as it does for a moon near the horizon.
            Quaternion lean = Quaternion.AngleAxis(-28f, m);
            right = lean * right;
            up = lean * up;
            float a = _moonPhase * Mathf.PI * 2f;
            Shader.SetGlobalVector(MoonRightId, new Vector4(right.x, right.y, right.z, Mathf.Sin(a)));
            Shader.SetGlobalVector(MoonUpId, new Vector4(up.x, up.y, up.z, -Mathf.Cos(a)));
        }

        // hazeCenter / hazeEnd: the limb in unbent planar coordinates (a circle around the point under the camera);
        // skyCenter: direction to the centre of the planet and the cosine of its angular radius.
        void Solve(Camera cam, out Vector2 focus, out float invR, out float t, out Vector2 hazeCenter, out float hazeEnd, out Vector4 skyCenter)
        {
            Transform ct = cam.transform;
            Vector3 cp = ct.position;
            focus = WaterFollower.ViewFocus(ct);
            float h = Mathf.Max(0.05f, cp.y);
            var toCam = new Vector2(cp.x, cp.z) - focus;
            float b = toCam.magnitude;
            float viewDist = Mathf.Sqrt(b * b + h * h);
            float r = RadiusForViewDistance(viewDist, out t);
            invR = flatten || strength <= 0f ? 0f : strength / r;
            float far = cam.farClipPlane * 0.9f;
            if (invR <= 0f)
            {
                hazeCenter = new Vector2(cp.x, cp.z);
                hazeEnd = Mathf.Min(flatHazeEnd, far);
                skyCenter = new Vector4(0f, -1f, 0f, 0f);
                return;
            }
            r = 1f / invR;
            var toCentre = new Vector3(focus.x - cp.x, -r - cp.y, focus.y - cp.z);
            float dist = Mathf.Max(toCentre.magnitude, r * 1.0001f);
            float sinA = r / dist;
            skyCenter = toCentre / dist;
            skyCenter.w = Mathf.Sqrt(Mathf.Max(0f, 1f - sinA * sinA));
            // The limb is the small circle at the arc angle acos(R / dist) around the point under the camera.
            float beta = Mathf.Atan2(b, h + r);
            hazeCenter = b > 1e-4f ? focus + toCam * (r * beta / b) : focus;
            hazeEnd = Mathf.Min(r * Mathf.Acos(Mathf.Clamp01(sinA)), far);
        }

        bool Bends(Camera cam)
        {
            if (cam.cameraType == CameraType.SceneView) return bendSceneView;
            if (cam.cameraType != CameraType.Game) return false;
            return cam == Camera.main || Extra.Contains(cam);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            var flatSky = new Vector4(0f, -1f, 0f, 0f);
            Shader.SetGlobalVector(RingId, Vector4.zero);
            Shader.SetGlobalVector(SpaceId, Vector4.zero);
            if (!Bends(cam))
            {
                Shader.SetGlobalFloat(InvRadiusId, 0f);
                Shader.SetGlobalVector(FogColorId, Lin(HorizonColor, 0f));
                Shader.SetGlobalVector(FogParamsId, new Vector4(1e6f, 0f, 0f, 0f));
                Shader.SetGlobalVector(SkyCenterId, flatSky);
                // The dome is for the world cameras; the island preview and portrait cameras keep their clear colour.
                Shader.SetGlobalFloat(SkyDrawId, cam.cameraType == CameraType.SceneView ? 1f : 0f);
                PushCelestial(flatSky);
                return;
            }

            // The Scene view shows the game's bend (same focus and radius, no haze) so what is edited matches what is played.
            bool own = cam.cameraType != CameraType.SceneView || Camera.main == null;
            var ring = RingWorld.Active;
            if (ring != null)
            {
                BeginRingCamera(cam, own, ring);
                return;
            }
            Solve(own ? cam : Camera.main, out Vector2 focus, out float invR, out _, out Vector2 hazeCenter, out float hazeEnd, out Vector4 skyCenter);
            Shader.SetGlobalVector(FocusId, new Vector4(focus.x, focus.y, 0f, 0f));
            Shader.SetGlobalFloat(InvRadiusId, invR);
            Shader.SetGlobalVector(FogColorId, Lin(HorizonColor, own ? haze : 0f));
            float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lowLimbAngle, highLimbAngle, hazeEnd * invR * Mathf.Rad2Deg));
            if (invR <= 0f) low = 0f;
            float startF = Mathf.Lerp(hazeStart, lowHazeStart, low), fullF = Mathf.Lerp(hazeFull, lowHazeFull, low);
            float start = hazeEnd * startF;
            float full = hazeEnd * Mathf.Max(fullF, startF + 0.05f);
            Shader.SetGlobalVector(FogParamsId, own ? new Vector4(start, 1f / Mathf.Max(1f, full - start), hazeCenter.x, hazeCenter.y) : new Vector4(1e6f, 0f, 0f, 0f));
            Shader.SetGlobalVector(SkyCenterId, own ? skyCenter : flatSky);
            Shader.SetGlobalFloat(SkyDrawId, 1f);
            PushCelestial(own ? skyCenter : flatSky);
            if (invR > 0f) WidenCulling(cam, focus, invR, hazeCenter, hazeEnd);
        }

        void BeginRingCamera(Camera cam, bool own, RingWorld ring)
        {
            RingGeometry g = ring.Geometry;
            Vector2 focus = WaterFollower.ViewFocus((own ? cam : Camera.main).transform);
            Vector3 cp = cam.transform.position;
            Shader.SetGlobalVector(FocusId, new Vector4(focus.x, focus.y, 0f, 0f));
            Shader.SetGlobalFloat(InvRadiusId, 0f);
            Shader.SetGlobalVector(RingId, RingParams(g));
            Shader.SetGlobalFloat(RimGlowId, ring.EdgeWarning);
            Shader.SetGlobalVector(FogColorId, Lin(HorizonColor, own ? ringHaze : 0f));
            float start = Mathf.Max(0f, ringHazeStart), end = Mathf.Max(start + 1f, ringHazeEnd);
            Shader.SetGlobalVector(FogParamsId, own ? new Vector4(start, 1f / (end - start), cp.x, cp.z) : new Vector4(1e6f, 0f, 0f, 0f));
            var flatSky = new Vector4(0f, -1f, 0f, 0f);
            Shader.SetGlobalVector(SkyCenterId, flatSky);
            Shader.SetGlobalFloat(SkyDrawId, 1f);
            // Past the two rims the world ends and space begins: the sky shader turns every direction that leaves
            // the band through an open end of the ring into the plain starfield (day and night).
            Shader.SetGlobalVector(SpaceId, new Vector4(own ? ringSpace : 0f, ringSpaceStars, Mathf.Max(0.01f, ringSpaceEdge), ringSpaceMilkyWay));
            Shader.SetGlobalVector(SpaceColorId, Lin(ringSpaceColor, ringSpaceDiscs));
            PushCelestial(flatSky);
            PlaceRims(ring, focus);
            RingCulling(cam, g, focus);
        }

        // Renderer bounds are unbent, and inside the ring nearly the whole band can be on screen (ahead climbing, the
        // far side overhead): everything within the band and half a circumference of the focus survives culling.
        // An axis-aligned box as the culling matrix; its depth maps to 0..1, which is inside the clip range of every
        // graphics API.
        void RingCulling(Camera cam, RingGeometry g, Vector2 focus)
        {
            float half = g.HalfLength + 40f;
            float x0 = g.MinX - 40f, x1 = g.MaxX + 40f;
            float y0 = -200f, y1 = g.Radius + 50f;
            var m = Matrix4x4.zero;
            m.m00 = 2f / (x1 - x0); m.m03 = -(x1 + x0) / (x1 - x0);
            m.m11 = 2f / (y1 - y0); m.m13 = -(y1 + y0) / (y1 - y0);
            m.m22 = 0.5f / half; m.m23 = -(focus.y - half) * (0.5f / half);
            m.m33 = 1f;
            cam.cullingMatrix = m;
            _culled = cam;
        }

        void EnsureRims(RingWorld ring)
        {
            Transform rims = transform.Find(RimName);
            if (ring == null || !ringRims)
            {
                if (rims != null || _rimMesh != null) DestroyRims();
                return;
            }
            Material mat = rimMaterial;
            if (mat == null)
            {
                if (_rimMat == null)
                {
                    var shader = Shader.Find(RimShader);
                    if (shader == null) return;
                    _rimMat = new Material(shader) { name = "RingRims (runtime)", hideFlags = HideFlags.DontSave };
                }
                mat = _rimMat;
            }
            RingGeometry g = ring.Geometry;
            var key = new Vector4(g.halfWidth, g.circumference, rimFoamWidth, rimSegment);
            if (_rimMesh == null || key != _rimKey)
            {
                if (_rimMesh != null) Kill(_rimMesh);
                float snap = RingRims.Snap(rimSegment);
                _rimMesh = RingRims.Build(g.halfWidth, g.circumference + 2f * snap, rimFoamWidth, rimSegment);
                _rimKey = key;
            }
            if (rims == null)
            {
                var go = new GameObject(RimName) { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                rims = go.transform;
            }
            var filter = rims.GetComponent<MeshFilter>();
            if (filter.sharedMesh != _rimMesh) filter.sharedMesh = _rimMesh;
            var renderer = rims.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != mat) renderer.sharedMaterial = mat;
        }

        // Re-centred on the focus in whole panel groups (the foam stays on the edge), never rotated or scaled.
        void PlaceRims(RingWorld ring, Vector2 focus)
        {
            Transform rims = transform.Find(RimName);
            if (rims == null) return;
            float snap = RingRims.Snap(rimSegment);
            var pos = new Vector3(ring.Geometry.centerX, 0f, Mathf.Round(focus.y / snap) * snap);
            if (rims.position != pos) rims.position = pos;
            if (rims.rotation != Quaternion.identity) rims.rotation = Quaternion.identity;
            Vector3 ls = transform.lossyScale;
            if (ls != Vector3.one && ls.x != 0f && ls.y != 0f && ls.z != 0f)
                rims.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        }

        void DestroyRims()
        {
            Transform rims = transform.Find(RimName);
            if (rims != null) Kill(rims.gameObject);
            if (_rimMesh != null) Kill(_rimMesh);
            if (_rimMat != null) Kill(_rimMat);
            _rimMesh = null;
            _rimMat = null;
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_culled != cam) return;
            cam.ResetCullingMatrix();
            _culled = null;
        }

        // A renderer is culled by its unbent bounds, which are not where it is drawn. The boundary rays of the
        // frustum are traced to the planet (or, where they miss it, to the limb in their direction); the unbent
        // copies of those points are the farthest anything visible can be from the real frustum, so they bound
        // what must survive culling.
        void WidenCulling(Camera cam, Vector2 focus, float invR, Vector2 hazeCenter, float hazeEnd)
        {
            Vector3 cp = cam.transform.position;
            float r = 1f / invR;
            var centre = new Vector3(focus.x, -r, focus.y);
            Vector3 oc = cp - centre;
            float cq = Vector3.Dot(oc, oc) - r * r;
            Matrix4x4 view = cam.worldToCameraMatrix;
            float near = cam.nearClipPlane;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * cam.aspect;
            float right = tanH, top = tanV, bottom = tanV;
            const float maxTan = 8f;

            for (int i = 0; i < EdgeSamples.Length; i++)
            {
                Vector3 d = cam.ViewportPointToRay(new Vector3(EdgeSamples[i].x, EdgeSamples[i].y, 0f)).direction;
                Vector2 flat;
                float bq = Vector3.Dot(oc, d);
                float disc = bq * bq - cq;
                float s = disc >= 0f ? -bq - Mathf.Sqrt(disc) : -1f;
                if (s > 0f)
                {
                    Vector3 n = (cp + d * s - centre) * invR;
                    var az = new Vector2(n.x, n.z);
                    float m = az.magnitude;
                    flat = m > 1e-5f ? focus + az * (r * Mathf.Acos(Mathf.Clamp(n.y, -1f, 1f)) / m) : focus;
                }
                else
                {
                    var dxz = new Vector2(d.x, d.z);
                    float fl = dxz.magnitude;
                    if (fl < 1e-4f) continue;
                    flat = hazeCenter + dxz * (hazeEnd / fl);
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 v = view.MultiplyPoint3x4(new Vector3(flat.x, k == 0 ? cullTop : cullBottom, flat.y));
                    float depth = -v.z;
                    if (depth < near * 2f) { right = top = bottom = maxTan; continue; }
                    right = Mathf.Max(right, Mathf.Abs(v.x) / depth);
                    top = Mathf.Max(top, v.y / depth);
                    bottom = Mathf.Max(bottom, -v.y / depth);
                }
            }

            right = Mathf.Min(maxTan, right * 1.08f);
            top = Mathf.Min(maxTan, top * 1.08f);
            bottom = Mathf.Min(maxTan, bottom * 1.08f);
            Matrix4x4 proj = Matrix4x4.Frustum(-right * near, right * near, -bottom * near, top * near, near, cam.farClipPlane);
            cam.cullingMatrix = proj * view;
            _culled = cam;
        }

        void EnsureSky()
        {
            Transform sky = transform.Find(SkyName);
            if (!drawSky)
            {
                if (sky != null) DestroySky();
                return;
            }
            Material mat = skyMaterial;
            if (mat == null)
            {
                if (_skyMat == null)
                {
                    var shader = Shader.Find(SkyShader);
                    if (shader == null) return;
                    _skyMat = new Material(shader) { name = "CurvedSky (runtime)", hideFlags = HideFlags.DontSave };
                }
                mat = _skyMat;
            }
            if (_skyMesh == null) _skyMesh = BuildSkyMesh();
            if (sky == null)
            {
                var go = new GameObject(SkyName) { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                sky = go.transform;
            }
            var filter = sky.GetComponent<MeshFilter>();
            if (filter.sharedMesh != _skyMesh) filter.sharedMesh = _skyMesh;
            var renderer = sky.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != mat) renderer.sharedMaterial = mat;
        }

        void EnsureSkybox()
        {
            if (!driveSkybox || !(Application.isPlaying || skyboxInEditMode))
            {
                RestoreSkybox();
                return;
            }
            Material mat = skyboxMaterial;
            if (mat == null)
            {
                if (_skyboxMat == null)
                {
                    var shader = Shader.Find(SkyboxShader);
                    if (shader == null) return;
                    _skyboxMat = new Material(shader) { name = "DriftSkybox (runtime)", hideFlags = HideFlags.DontSave };
                }
                mat = _skyboxMat;
            }
            if (RenderSettings.skybox == mat) return;
            if (!_skyboxSet) _skyboxBefore = RenderSettings.skybox;
            _skyboxSet = true;
            RenderSettings.skybox = mat;
        }

        void RestoreSkybox()
        {
            if (_skyboxSet)
            {
                _skyboxSet = false;
                Material now = RenderSettings.skybox;
                if (now == null || now == _skyboxMat || now == skyboxMaterial) RenderSettings.skybox = _skyboxBefore;
            }
            if (_skyboxMat != null) Kill(_skyboxMat);
            _skyboxMat = null;
        }

        void DestroySky()
        {
            Transform sky = transform.Find(SkyName);
            if (sky != null) Kill(sky.gameObject);
            if (_skyMesh != null) Kill(_skyMesh);
            if (_skyMat != null) Kill(_skyMat);
            _skyMesh = null;
            _skyMat = null;
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        // Unit sphere of directions; the shader places it around the rendering camera, so the huge bounds only
        // serve to keep it from ever being culled.
        static Mesh BuildSkyMesh()
        {
            const int seg = 24, rings = 12;
            var verts = new Vector3[(seg + 1) * (rings + 1)];
            var tris = new int[seg * rings * 6];
            for (int r = 0; r <= rings; r++)
            {
                float el = Mathf.PI * (r / (float)rings - 0.5f);
                for (int s = 0; s <= seg; s++)
                {
                    float az = 2f * Mathf.PI * s / seg;
                    verts[r * (seg + 1) + s] = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                }
            }
            int q = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tris[q++] = a; tris[q++] = b; tris[q++] = a + 1;
                    tris[q++] = a + 1; tris[q++] = b; tris[q++] = b + 1;
                }
            var mesh = new Mesh { name = "CurvedSkyDome", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }
    }
}
