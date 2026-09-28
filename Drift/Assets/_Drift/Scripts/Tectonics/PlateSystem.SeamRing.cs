using UnityEngine;

namespace Drift.Tectonics
{
    // How the seam ribbons are drawn on the adventure ring. Only lane boundaries along the track are surf lanes; the
    // Voronoi edges between a lane plate and the neighbouring column's plate one row further on run across the track
    // (they are the jogs where the lanes shift sideways) and drew foam bands over the whole band once the lanes had
    // wandered apart (level 3 and later). Those stay borders for the plate logic but are not drawn. Far seams climb
    // up the ring into the sky ahead, so they fade out sooner there than on the flat cozy sea.
    public partial class PlateSystem
    {
        [Header("Nähte im Ring (Abenteuer)")]
        [Tooltip("Im Ring: bis zu dieser Entfernung (u entlang der Strecke) sind die Surfspuren zu sehen; weiter oben am Ring verblassen sie.")]
        [Range(20f, 160f)] public float ringSeamFadeDistance = 85f;
        [Tooltip("Im Ring: ab diesem Anteil der Sichtweite beginnen die Surfspuren zu verblassen.")]
        [Range(0.1f, 0.95f)] public float ringSeamFadeStart = 0.45f;

        // Largest |x| component of the chord direction a ring seam may have and still be drawn (sin 30 degrees).
        public const float RingSeamMaxAcross = 0.5f;

        public static bool RingSeamRunsAlong(Vector2 p0, Vector2 p1)
        {
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 1e-4f) return false;
            return Mathf.Abs(d.x) / len <= RingSeamMaxAcross;
        }

        bool SeamDrawn(in Border b) => !_ring || RingSeamRunsAlong(b.p0, b.p1);

        // (fade start, fade end) in planar distance from the focus.
        public static Vector2 SeamFade(bool ring, float fadeDistance, float viewRadius, float seamWidth, float ringFadeDistance, float ringFadeStart)
        {
            float end = Mathf.Max(1f, Mathf.Min(fadeDistance, viewRadius - seamWidth * 0.5f));
            if (!ring) return new Vector2(end * 0.65f, end);
            end = Mathf.Max(1f, Mathf.Min(end, ringFadeDistance));
            return new Vector2(end * Mathf.Clamp(ringFadeStart, 0.05f, 0.95f), end);
        }

        Vector2 SeamFade() => SeamFade(_ring, seamFadeDistance, viewRadius, seamWidth, ringSeamFadeDistance, ringSeamFadeStart);
    }
}
