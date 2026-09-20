using System.Collections.Generic;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Islands
{
    public class IslandWorld : MonoBehaviour
    {
        public float minClosingSpeed = 0.6f;
        public float mergeOverlap = 0.1f;
        public float pushRatio = 0.35f;
        public float maxContactTime = 2f;

        class Contact
        {
            public float time;
            public float impactSpeed;
        }

        readonly Dictionary<(Island, Island), Contact> _contacts = new();
        readonly HashSet<(Island, Island)> _seen = new();
        readonly List<(Island, Island)> _stale = new();

        public int ActiveContacts => _contacts.Count;

        void Update()
        {
            if (!Application.isPlaying) return;
            Step(Time.deltaTime);
        }

        public bool Step() => Step(Time.deltaTime);

        static float ClosingSpeed(Island host, Island guest, out Vector2 normal)
        {
            normal = guest.PlanarPosition - host.PlanarPosition;
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector2.right;
            return Vector2.Dot(host.PlanarVelocity - guest.PlanarVelocity, normal);
        }

        public bool Step(float dt)
        {
            // Indexing the live registry is safe because a merge (which unregisters the guest) ends both loops.
            var list = Island.All;
            _seen.Clear();
            bool merged = false;

            for (int i = 0; i < list.Count && !merged; i++)
                for (int j = i + 1; j < list.Count && !merged; j++)
                {
                    Island a = list[i], b = list[j];
                    if (a == null || b == null || !a.isActiveAndEnabled || !b.isActiveAndEnabled) continue;
                    if (a.IsEmerging || b.IsEmerging) continue;
                    if (!Island.Near(a, b)) continue;

                    Island host = a.useKeyboardInput ? a : b.useKeyboardInput ? b : a.LandArea >= b.LandArea ? a : b;
                    Island guest = host == a ? b : a;
                    if (!host.DetectContact(guest, out _, out _, out int deepCells)) continue;

                    var key = (host, guest);
                    _seen.Add(key);
                    if (!_contacts.TryGetValue(key, out var contact))
                    {
                        contact = new Contact { impactSpeed = Mathf.Max(minClosingSpeed, ClosingSpeed(host, guest, out _)) };
                        _contacts[key] = contact;
                    }
                    contact.time += dt;

                    float smallArea = Mathf.Max(1f, Mathf.Min(host.LandArea, guest.LandArea));
                    float overlap = deepCells * guest.CellArea / smallArea;

                    // Drive-in phase: the host grinds to a halt while it shoves the guest along, so the
                    // two masses visibly interlock before they become one island.
                    if (overlap < mergeOverlap && contact.time < maxContactTime)
                    {
                        host.ApplyImpactDrag(dt);
                        float closing = Mathf.Max(0f, ClosingSpeed(host, guest, out Vector2 n));
                        if (closing > 0f) guest.SetPlanarPosition(guest.PlanarPosition + n * (closing * pushRatio * dt));
                        continue;
                    }

                    float convergence = PlateSystem.Instance != null
                        ? PlateSystem.Instance.ConvergenceAt((host.PlanarPosition + guest.PlanarPosition) * 0.5f)
                        : 0f;

                    host.MergeFrom(guest, contact.impactSpeed, convergence);
                    _contacts.Remove(key);
                    _seen.Remove(key);
                    merged = true;
                }

            _stale.Clear();
            foreach (var kv in _contacts)
                if (!_seen.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var key in _stale) _contacts.Remove(key);

            return merged;
        }
    }
}
