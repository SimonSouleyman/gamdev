using System;
using Drift.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Drift.Visuals
{
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
        public Color nightColor = new Color(0.5f, 0.62f, 0.95f);
        public float dayIntensity = 1.2f;
        public float nightIntensity = 0.5f;

        // The moon runs opposite the sun on a flatter orbit (it stays low, where the chase camera can see it) and
        // is up a little before sunset (moonLead). At night the directional light swings over to the moon's side
        // while it is dimmest, so moonlit glints on the sea sit under the moon.
        public float moonTilt = 66f;
        public float moonAzimuthOffset = 25f;
        public float moonLead = 0.3f;
        public bool moonLight = true;
        [Range(0f, 1f)] public float moonPhase = 0.62f;
        public float moonCycleDays = 8f;

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
            if (dayLength > 0f)
            {
                timeOfDay = Mathf.Repeat(timeOfDay + dt / dayLength, 1f);
                if (moonCycleDays > 0f) moonPhase = Mathf.Repeat(moonPhase + dt / (dayLength * moonCycleDays), 1f);
            }
            Apply();
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

        public void Apply()
        {
            if (sun == null || water == null) Resolve();
            Capture();

            Vector3 toSun = SkyMath.SunDirection(timeOfDay, axisTilt, azimuth);
            Vector3 toMoon = SkyMath.MoonDirection(timeOfDay, moonTilt, azimuth + moonAzimuthOffset, moonLead);
            SunHeight = toSun.y;
            TrueSunDirection = toSun;
            MoonDirection = toMoon;

            MoonLightAmount = moonLight ? SkyMath.MoonLightBlend(SunHeight) : 0f;
            Vector3 lightDir = ClampElevation(toSun);
            // Both ends are at least minSunElevation up, so the arc passes overhead (never through the ground) and
            // turns at an even rate: about 8 degrees a second at most with the default day length.
            if (MoonLightAmount > 0f) lightDir = Vector3.Slerp(lightDir.normalized, ClampElevation(toMoon).normalized, MoonLightAmount);
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
            WaterLight = dimWaterAtNight ? NightWaterLight(SunColor, SunIntensity, noonColor, dayIntensity, NightAmount) : Color.white;
            if (driveWater && water != null) water.SetSky(WaterSky, WaterDeep, WaterLight);
        }
    }
}
