using Drift.Life;
using Drift.SaveSystem;
using UnityEngine;

namespace Drift.Bridge
{
    // Ordered by priority: an island shows only its highest hint.
    // LastIsland (cozy, one planned island left) outranks everything and is shown at any distance.
    public enum IslandHintKind { None, Volcano, Event, NewSpecies, LastIsland }
    public enum IslandHintEvent { None, Show, Fire, Festival, Eruption }

    // What one scan found on an island (IslandHints fills it from the life systems, tests from fake data).
    public struct IslandHintFacts
    {
        // Bit (int)LifeKind of what lives there and can be seen from its shore right now.
        public ulong present;
        public bool volcano, emerging, show, fire, festival;
    }

    // The pure part of the island hints: which hint an island gets, which species it advertises, which hints win
    // the few bubbles and edge arrows, and where on the screen border an arrow sits. No Unity objects, no allocation.
    public static class IslandHintLogic
    {
        // "Neue Art" beats "Ereignis" beats "Vulkan"; entry = catalog index of the advertised species (-1 otherwise).
        public static IslandHintKind Classify(in IslandHintFacts f, ulong unseen, out IslandHintEvent ev, out int entry)
        {
            entry = PickSpecies(f.present & unseen & CollectionCatalog.CollectibleMask);
            ev = f.show ? IslandHintEvent.Show : f.emerging ? IslandHintEvent.Eruption
                : f.festival ? IslandHintEvent.Festival : f.fire ? IslandHintEvent.Fire : IslandHintEvent.None;
            if (entry >= 0) return IslandHintKind.NewSpecies;
            if (ev != IslandHintEvent.None) return IslandHintKind.Event;
            return f.volcano ? IslandHintKind.Volcano : IslandHintKind.None;
        }

        // The most exciting new kind of the mask: an animal before a critter before a plant, the lowest bit within
        // a group so the icon does not flicker between scans. -1 for an empty mask.
        public static int PickSpecies(ulong mask)
        {
            if (mask == 0) return -1;
            ulong pick = mask & CollectionCatalog.AnimalMask;
            if (pick == 0) pick = mask & CollectionCatalog.CritterMask;
            if (pick == 0) pick = mask;
            int bit = 0;
            while ((pick & 1UL) == 0) { pick >>= 1; bit++; }
            return CollectionCatalog.IndexOf((LifeKind)bit);
        }

        public static string LabelOf(IslandHintKind kind, IslandHintEvent ev)
        {
            switch (kind)
            {
                case IslandHintKind.LastIsland: return "Letzte Insel";
                case IslandHintKind.NewSpecies: return "Neue Art";
                case IslandHintKind.Volcano: return "Vulkan";
                case IslandHintKind.Event:
                    switch (ev)
                    {
                        case IslandHintEvent.Show: return "Schauspiel";
                        case IslandHintEvent.Fire: return "Feuer";
                        case IslandHintEvent.Festival: return "Fest";
                        case IslandHintEvent.Eruption: return "Neuer Vulkan";
                    }
                    break;
            }
            return "";
        }

        // A counter (signature moves, ignitions) grew since the last look: the event holds until now + hold. The first
        // look (last < 0) only takes the baseline, so what happened before the island came into range is no news.
        public static float Hold(ref int last, int current, float now, float until, float hold)
        {
            bool grew = last >= 0 && current > last;
            last = current;
            return grew ? Mathf.Max(until, now + hold) : until;
        }

        // Sort key of a hint: distance, a new species counting as if it were closer.
        public static float Rank(IslandHintKind kind, float distance, float newSpeciesBonus = 1.35f)
        {
            float d = Mathf.Max(0f, distance);
            if (kind == IslandHintKind.LastIsland) return -1f;
            return kind == IslandHintKind.NewSpecies ? d / newSpeciesBonus : kind == IslandHintKind.Volcano ? d * 1.25f : d;
        }

        // Up to result.Length candidates with eligible[i] and the lowest rank[i], best first; returns how many.
        public static int SelectTop(float[] rank, bool[] eligible, int count, int[] result)
        {
            int n = 0, cap = result.Length;
            for (int i = 0; i < count; i++)
            {
                if (!eligible[i]) continue;
                float r = rank[i];
                int at = n < cap ? n : cap;
                while (at > 0 && rank[result[at - 1]] > r) at--;
                if (at >= cap) continue;
                int end = n < cap ? n : cap - 1;
                for (int k = end; k > at; k--) result[k] = result[k - 1];
                result[at] = i;
                if (n < cap) n++;
            }
            return n;
        }

        // Like SelectTop, but a candidate whose widget would sit closer than minDistance to a better one is skipped
        // (two islands in the same direction share one arrow). scratch bounds how many candidates are looked at.
        public static int SelectSpread(float[] rank, bool[] eligible, Vector2[] pos, int count, float minDistance, int[] scratch, int[] result)
        {
            int m = SelectTop(rank, eligible, count, scratch), n = 0;
            float min2 = minDistance * minDistance;
            for (int k = 0; k < m && n < result.Length; k++)
            {
                int c = scratch[k];
                bool crowded = false;
                for (int j = 0; j < n && !crowded; j++) crowded = (pos[result[j]] - pos[c]).sqrMagnitude < min2;
                if (!crowded) result[n++] = c;
            }
            return n;
        }

        // Inside the viewport rect shrunk by margin (fractions), and in front of the camera.
        public static bool OnScreen(Vector3 viewport, float marginX, float marginY) => OnScreen(viewport, marginX, marginY, marginY);

        // marginTop: the band under the top edge (the HUD) that counts as off screen.
        public static bool OnScreen(Vector3 viewport, float marginX, float marginY, float marginTop) =>
            viewport.z > 0f && viewport.x >= marginX && viewport.x <= 1f - marginX && viewport.y >= marginY && viewport.y <= 1f - marginTop;

        // Screen direction (from the centre, in canvas units, y up) towards a viewport point; a point behind the camera
        // is mirrored so the arrow still points the way to turn.
        public static Vector2 DirectionOf(Vector3 viewport, Vector2 canvasSize)
        {
            var d = new Vector2((viewport.x - 0.5f) * canvasSize.x, (viewport.y - 0.5f) * canvasSize.y);
            if (viewport.z < 0f) d = -d;
            if (d.sqrMagnitude < 1e-6f) d = Vector2.down;
            return d;
        }

        // Where the ray from origin along dir leaves the rect (origin inside it).
        public static Vector2 EdgePoint(Vector2 origin, Vector2 dir, Rect inner)
        {
            float t = float.MaxValue;
            if (dir.x > 1e-6f) t = Mathf.Min(t, (inner.xMax - origin.x) / dir.x);
            else if (dir.x < -1e-6f) t = Mathf.Min(t, (inner.xMin - origin.x) / dir.x);
            if (dir.y > 1e-6f) t = Mathf.Min(t, (inner.yMax - origin.y) / dir.y);
            else if (dir.y < -1e-6f) t = Mathf.Min(t, (inner.yMin - origin.y) / dir.y);
            if (t == float.MaxValue) return origin;
            var p = origin + dir * Mathf.Max(0f, t);
            return new Vector2(Mathf.Clamp(p.x, inner.xMin, inner.xMax), Mathf.Clamp(p.y, inner.yMin, inner.yMax));
        }
    }
}
