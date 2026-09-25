using System;
using Drift.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
    // Night readability of the adventure's hazards, kept pure so it can be tested without a GPU. The shore rim is
    // mirrored by Drift/IslandTerrain (_DriftShoreRim), the storm lift and the rain's side fade by Drift/Storm.
    public static class NightReadability
    {
        // Strength of the cool shore rim: 0 by day, the mode's share at night.
        public static float ShoreRimStrength(float night, bool adventure, float adventureShare, float cozyShare) =>
            Mathf.Clamp01(night) * Mathf.Clamp01(adventure ? adventureShare : cozyShare);

        // Only the waterline gets it: from the cut-away sea floor (-0.45) up to the wet sand, gone by the grass.
        public static float ShoreRimMask(float height) =>
            Smooth(-0.45f, -0.2f, height) * (1f - Smooth(0.1f, 0.5f, height));

        // Shape of the rim over the shore: a faint band everywhere, brightest where the ground turns away from the
        // view (ndv = dot(normal, view), 1 facing the camera, 0 edge-on), so the outline stands out most.
        public static float ShoreRimShape(float height, float ndv)
        {
            float f = 1f - Mathf.Clamp01(ndv);
            return ShoreRimMask(height) * (0.25f + 0.75f * f * f);
        }

        // A storm puff's colour at night is never darker than the moonlit floor, so the mass stays a grey-blue shape
        // on a dark sea; the day look (night 0) is unchanged.
        public static Color StormPuffLift(Color col, Color floor, float night)
        {
            float k = Mathf.Clamp01(night);
            return new Color(Mathf.Max(col.r, floor.r * k), Mathf.Max(col.g, floor.g * k), Mathf.Max(col.b, floor.b * k), col.a);
        }

        // A rain shaft is an open cylinder: its veil and streaks fade where the wall turns edge-on to the camera, so
        // the shaft has soft sides instead of the hard outline of a box.
        public static float RainSideFade(float facing, float from, float to) => Smooth(from, to, Mathf.Abs(facing));

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    [ExecuteAlways]
    [DefaultExecutionOrder(150)]
    public class DayNightCycle : MonoBehaviour
    {
        public Light sun;
        public WaterFeedback water;
        public string sunObjectName = "Directional Light";

        public float dayLength = 360f;
        [Range(0f, 1f)] public float timeOfDay = 0.3f;
        public bool advanceInEditMode = false;

        public float axisTilt = 35f;
        public float azimuth = 40f;
        public float minSunElevation = 14f;

        public Color noonColor = new Color(1f, 0.97f, 0.9f);
        public Color horizonColor = new Color(1f, 0.6f, 0.35f);
        // Moonlight: a silver blue, not the saturated blue of before (blue light on green and brown ground went almost
        // black on a phone screen). Every Drift shader lights with the main light only (_Ambient is a share of it), so
        // this colour and intensity ARE the night's brightness.
        public Color nightColor = new Color(0.62f, 0.74f, 1f);
        public float dayIntensity = 1.2f;
        public float nightIntensity = 0.55f;

        // The moon runs opposite the sun on a flatter orbit and is up a little before sunset (moonLead). moonTilt 52:
        // it culminates at 38 degrees - the chase camera looks down past the limb and sees the moon as its mirror
        // image in the sea, which needs it 15-40 degrees up to land in the portrait view. At night the directional
        // light swings over to the moon's side while it is dimmest (the moonlight key, see "Mondnacht").
        public float moonTilt = 52f;
        public float moonAzimuthOffset = 25f;
        public float moonLead = 0.3f;
        public bool moonLight = true;
        [Range(0f, 1f)] public float moonPhase = 0.62f;
        public float moonCycleDays = 8f;

        [Header("Mondnacht")]
        [Tooltip("Der Mond steht nachts vor der Kamera (so viele Grad seitlich der Blickrichtung) statt auf einer festen Himmelsrichtung, hinter der er meist verschwand. Auf- und Untergang behält er (Bahn oben).")]
        public bool moonInView = true;
        [Range(-90f, 90f)] public float moonViewOffset = 8f;
        [Tooltip("So schnell (Grad pro Sekunde) wandert der Mond der Blickrichtung nach.")]
        public float moonViewFollow = 30f;
        [Tooltip("Mondlicht als Hauptlicht: nie flacher als so viele Grad über dem Horizont (ein tiefer Mond streift flachen Boden nur).")]
        [Range(0f, 90f)] public float moonKeyElevation = 50f;
        [Tooltip("Richtung des Mondlichts gegenüber dem Mond gedreht (Grad, 0 = vom Mond her).")]
        [Range(-180f, 180f)] public float moonKeyYaw = 0f;
        [Tooltip("Mondphase nur in diesem Bereich (0.5 = Vollmond): eine dünne Sichel gibt kein Mondlicht. Neu begonnen wird nur, während der Mond untergegangen ist.")]
        [Range(0f, 1f)] public float moonPhaseMin = 0.3f;
        [Range(0f, 1f)] public float moonPhaseMax = 0.7f;
        [Tooltip("Flaches Umgebungslicht nachts mindestens so hell (sRGB; nur für URP-Lit-Materialien, die Drift-Shader hellen über das Mondlicht auf).")]
        public Color nightAmbientFloor = new Color(0.40f, 0.45f, 0.60f);
        [Tooltip("Sieht die Kamera Himmel (Abenteuer-Ring, flache Blicke: der obere Bildrand mindestens so viele Grad über dem Horizont), steht der Mond höchstens moonFitMargin Grad unter dem oberen Bildrand.")]
        public float moonFitTop = 15f;
        public float moonFitMargin = 9f;
        [Tooltip("Kamera, vor der der Mond steht (leer = Camera.main).")]
        public Camera viewCamera;

        // One palette for sky, horizon haze, water tints and flat ambient (see Drift.Core.SkyPalette): evaluated
        // once per Apply() into Sky; CurvedWorld pushes it to the sky / haze shaders, WaterFeedback gets
        // horizon + deep, RenderSettings gets the ambient, so sea, haze and sky meet in one colour all day.
        public SkyPalette palette = new SkyPalette();
        public bool driveAmbient = true;
        public bool driveWater = true;
        [Tooltip("Flachwasser-Türkis und Schaum nachts mit dem Mondlicht abdunkeln (sonst leuchten sie als helle Streifen an jeder Küste).")]
        public bool dimWaterAtNight = true;

        // Storm tint multiplies onto whatever time of day produced, so a night storm is darker than
        // either alone instead of the two fighting over the light.
        [Range(0f, 1f)] public float stormDim = 0.35f;
        [Tooltip("Wie stark ein Blitz Sonne und Umgebungslicht kurz aufhellt.")]
        [Range(0f, 3f)] public float flashSunBoost = 1.2f;
        public Color flashAmbient = new Color(0.55f, 0.6f, 0.75f);
        [Range(0f, 1f)] public float stormDesaturate = 0.4f;
        public Color stormAmbientTint = new Color(0.72f, 0.75f, 0.82f);
        public Color stormSkyTint = new Color(0.6f, 0.62f, 0.68f);
        [Range(0f, 1f)] public float stormSkyDesaturate = 0.75f;

        [Header("Nachts lesbar")]
        [Tooltip("Abenteuer: nachts ein schwacher, kühler Lichtsaum an der Küste jeder Insel, damit Hindernisse sich vom dunklen Meer abheben (0 = aus). Bei Tag nie sichtbar.")]
        [Range(0f, 1f)] public float adventureShoreRim = 1f;
        [Tooltip("Gemütlich: derselbe Küstensaum (0 = aus).")]
        [Range(0f, 1f)] public float cozyShoreRim = 0f;
        [Tooltip("Farbe des Küstensaums bei voller Stärke (sRGB).")]
        public Color shoreRimColor = new Color(0.32f, 0.40f, 0.58f);

        static readonly int ShoreRimId = Shader.PropertyToID("_DriftShoreRim");

        Quaternion _origRotation;
        Color _origColor;
        float _origIntensity;
        AmbientMode _origAmbientMode;
        Color _origAmbient;
        bool _captured;

        public float TimeOfDay
        {
            get => timeOfDay;
            set => timeOfDay = Mathf.Repeat(value, 1f);
        }
        public float NightAmount { get; private set; }
        public float HorizonAmount { get; private set; }
        public float StormAmount { get; private set; }
        public float SunHeight { get; private set; }
        // Direction towards whatever lights the world (the sun, the moon at night), never below minSunElevation.
        public Vector3 SunDirection { get; private set; }
        public Vector3 TrueSunDirection { get; private set; } = Vector3.up;
        public Vector3 MoonDirection { get; private set; } = Vector3.down;
        // Where the orbit alone would put the moon (the bearing before moonInView turned it in front of the camera).
        public Vector3 OrbitMoonDirection { get; private set; } = Vector3.down;
        public float MoonLightAmount { get; private set; }
        public float StarVisibility { get; private set; }
        public float SunDiscVisibility { get; private set; }
        public Color SunColor { get; private set; }
        public float SunIntensity { get; private set; }
        // The palette at this time of day with the storm applied (sRGB, like every colour field here).
        public SkyKey Sky { get; private set; }
        public Color AmbientColor { get; private set; }
        public Color WaterSky { get; private set; }
        public Color WaterDeep { get; private set; }
        // Multiplier (linear) for the water's shallow tint and foam: white by day, the night light relative to the noon
        // light after dark. Those two colours are fixed material colours, the only ones in the water not already
        // following the time of day (deep and sky tint come from the palette, glints from the light).
        public Color WaterLight { get; private set; } = Color.white;

        Func<float> _nightProvider;
        [NonSerialized] float _dt, _moonYaw;
        [NonSerialized] bool _moonYawSet;

        Func<float> _timeProvider;

        void OnEnable()
        {
            Resolve();
            Capture();
            Apply();
            _nightProvider = () => NightAmount;
            LifeEnvironment.NightProvider = _nightProvider;
            _timeProvider = () => TimeOfDay;
            LifeEnvironment.TimeOfDayProvider = _timeProvider;
        }

        void OnDisable()
        {
            if (LifeEnvironment.NightProvider == _nightProvider) LifeEnvironment.NightProvider = null;
            if (LifeEnvironment.TimeOfDayProvider == _timeProvider) LifeEnvironment.TimeOfDayProvider = null;
            Restore();
            if (water != null) water.ClearSky();
            Shader.SetGlobalVector(ShoreRimId, Vector4.zero);
        }

        void Update()
        {
            if (Application.isPlaying || advanceInEditMode) Step(Time.deltaTime);
            else Apply();
        }

        void Resolve()
        {
            if (sun == null)
            {
                var go = GameObject.Find(sunObjectName);
                if (go != null) sun = go.GetComponent<Light>();
            }
            if (water == null) water = GetComponent<WaterFeedback>();
            if (water == null) water = FindAnyObjectByType<WaterFeedback>();
        }

        void Capture()
        {
            if (_captured) return;
            if (sun != null)
            {
                _origRotation = sun.transform.rotation;
                _origColor = sun.color;
                _origIntensity = sun.intensity;
            }
            _origAmbientMode = RenderSettings.ambientMode;
            _origAmbient = RenderSettings.ambientLight;
            _captured = true;
        }

        void Restore()
        {
            if (!_captured) return;
            _captured = false;
            if (sun != null)
            {
                sun.transform.rotation = _origRotation;
                sun.color = _origColor;
                sun.intensity = _origIntensity;
            }
            if (driveAmbient)
            {
                RenderSettings.ambientMode = _origAmbientMode;
                RenderSettings.ambientLight = _origAmbient;
            }
        }

        // White while NightAmount is 0 (the day look is unchanged), easing to light / noon light per channel at night.
        public static Color NightWaterLight(Color sunColor, float sunIntensity, Color noon, float dayIntensity, float night)
        {
            // Light colours reach the shaders linear, so the ratio is taken there: it is what the terrain gets.
            Color l = sunColor.linear, n = noon.linear;
            float k = sunIntensity / Mathf.Max(0.01f, dayIntensity);
            var ratio = new Color(
                Mathf.Clamp01(l.r * k / Mathf.Max(0.01f, n.r)),
                Mathf.Clamp01(l.g * k / Mathf.Max(0.01f, n.g)),
                Mathf.Clamp01(l.b * k / Mathf.Max(0.01f, n.b)), 1f);
            // Half of the moonlight's blue only: turquoise times the full tint turned into a saturated blue band.
            float lum = 0.2126f * ratio.r + 0.7152f * ratio.g + 0.0722f * ratio.b;
            ratio = Color.Lerp(new Color(lum, lum, lum, 1f), ratio, 0.5f);
            return Color.Lerp(Color.white, ratio, Mathf.Clamp01(night));
        }

        public void Step(float dt)
        {
            _dt = dt;
            if (dayLength > 0f)
            {
                timeOfDay = Mathf.Repeat(timeOfDay + dt / dayLength, 1f);
                if (moonCycleDays > 0f)
                    moonPhase = SkyMath.AdvanceMoonPhase(moonPhase, dt / (dayLength * moonCycleDays), moonPhaseMin, moonPhaseMax, OrbitMoonDirection.y < -0.05f);
            }
            Apply();
            _dt = 0f;
        }

        Vector3 ClampElevation(Vector3 dir)
        {
            float minH = Mathf.Sin(minSunElevation * Mathf.Deg2Rad);
            if (dir.y >= minH) return dir;
            Vector2 flat = new Vector2(dir.x, dir.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector2.up;
            flat = flat.normalized * Mathf.Cos(minSunElevation * Mathf.Deg2Rad);
            return new Vector3(flat.x, minH, flat.y);
        }

        // Play Mode: the moon's bearing trails the camera's (it "follows you", like the moon from a moving car); while it is
        // down it snaps, so it always rises in view. Edit Mode keeps the orbit's own bearing.
        Vector3 MoonInView(Vector3 orbit)
        {
            if (!moonInView || !Application.isPlaying) { _moonYawSet = false; return orbit; }
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return orbit;
            float target = SkyMath.YawOf(cam.transform.forward) + moonViewOffset;
            if (!_moonYawSet || orbit.y < -0.1f) { _moonYaw = target; _moonYawSet = true; }
            else _moonYaw = Mathf.MoveTowardsAngle(_moonYaw, target, Mathf.Max(0f, moonViewFollow) * _dt);
            Vector3 moon = SkyMath.MoonAt(orbit, _moonYaw);
            // Looking down past the limb (cozy chase, watch) the moon shows as its mirror image in the sea and keeps
            // its orbit's height; where the sky is in view, a high moon would stand just above the top edge.
            float top = Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg + cam.fieldOfView * 0.5f;
            if (top >= moonFitTop && moon.y > 0f) moon = SkyMath.CapElevation(moon, Mathf.Max(5f, top - moonFitMargin));
            return moon;
        }

        public void Apply()
        {
            if (sun == null || water == null) Resolve();
            Capture();

            Vector3 toSun = SkyMath.SunDirection(timeOfDay, axisTilt, azimuth);
            Vector3 toMoon = SkyMath.MoonDirection(timeOfDay, moonTilt, azimuth + moonAzimuthOffset, moonLead);
            OrbitMoonDirection = toMoon;
            toMoon = MoonInView(toMoon);
            SunHeight = toSun.y;
            TrueSunDirection = toSun;
            MoonDirection = toMoon;

            MoonLightAmount = moonLight ? SkyMath.MoonLightBlend(SunHeight) : 0f;
            Vector3 lightDir = ClampElevation(toSun);
            // Both ends are at least minSunElevation up, so the arc passes overhead (never through the ground) and
            // turns at an even rate: about 8 degrees a second at most with the default day length.
            if (MoonLightAmount > 0f)
                lightDir = Vector3.Slerp(lightDir.normalized, ClampElevation(SkyMath.MoonKeyLight(toMoon, moonKeyElevation, moonKeyYaw)).normalized, MoonLightAmount);
            SunDirection = lightDir.normalized;

            HorizonAmount = 1f - Mathf.Clamp01(Mathf.Abs(SunHeight - 0.12f) / 0.32f);
            NightAmount = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.18f, SunHeight));
            StarVisibility = SkyMath.StarVisibility(SunHeight);
            SunDiscVisibility = SkyMath.SunDiscVisibility(SunHeight);

            Color dayCol = Color.Lerp(noonColor, horizonColor, HorizonAmount);
            SunColor = Color.Lerp(dayCol, nightColor, SkyMath.LightNight(SunHeight));
            float dusk = 1f - 0.25f * HorizonAmount * (1f - NightAmount);
            SunIntensity = Mathf.Max(nightIntensity, Mathf.Lerp(dayIntensity, nightIntensity, NightAmount) * dusk);

            palette ??= new SkyPalette();
            SkyKey sky = palette.Evaluate(timeOfDay);
            sky.ambient = SkyMath.LiftAmbient(sky.ambient, nightAmbientFloor, NightAmount);

            float storm = water != null ? water.Storm : 0f;
            StormAmount = storm;
            if (storm > 0f)
            {
                float lum = SunColor.r * 0.3f + SunColor.g * 0.59f + SunColor.b * 0.11f;
                SunColor = Color.Lerp(SunColor, new Color(lum, lum, lum), stormDesaturate * storm);
                SunIntensity *= 1f - stormDim * storm;
                sky = SkyMath.ApplyStorm(sky, storm, stormSkyDesaturate, stormSkyTint, stormAmbientTint);
                StarVisibility *= 1f - storm;
                SunDiscVisibility *= 1f - 0.9f * storm;
            }
            Sky = sky;
            AmbientColor = sky.ambient;
            WaterSky = sky.horizon;
            WaterDeep = sky.waterDeep;

            // A lightning flash lights the whole scene for a moment (StormVisuals.Flash, 0 without a strike).
            float flash = StormVisuals.Flash;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);
                sun.color = flash > 0f ? Color.Lerp(SunColor, new Color(0.85f, 0.9f, 1f), flash) : SunColor;
                sun.intensity = SunIntensity + flash * flashSunBoost;
            }
            if (driveAmbient)
            {
                // WorldEvents.AmbientBoost is black unless the northern lights are out, so the normal sky and the
                // normal ambient are untouched while no spectacle runs.
                RenderSettings.ambientMode = AmbientMode.Flat;
                // Alpha stays 1: summing colours gave it 2, which then showed up as a scene change on every save.
                var ambient = AmbientColor + flashAmbient * flash + WorldEvents.AmbientBoost;
                ambient.a = 1f;
                RenderSettings.ambientLight = ambient;
            }
            float rim = NightReadability.ShoreRimStrength(NightAmount, GameModes.IsAdventure, adventureShoreRim, cozyShoreRim);
            Color rimCol = shoreRimColor.linear * rim;
            Shader.SetGlobalVector(ShoreRimId, new Vector4(rimCol.r, rimCol.g, rimCol.b, rim));

            WaterLight = dimWaterAtNight ? NightWaterLight(SunColor, SunIntensity, noonColor, dayIntensity, NightAmount) : Color.white;
            if (driveWater && water != null) water.SetSky(WaterSky, WaterDeep, WaterLight);
        }
    }
}
