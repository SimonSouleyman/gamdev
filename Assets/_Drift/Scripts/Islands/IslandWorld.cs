using System.Collections.Generic;
using Drift.Tectonics;
using UnityEngine;

namespace Drift.Islands
{
    public class IslandWorld : MonoBehaviour
    {
        public float minClosingSpeed = 0.6f;

        void Update()
        {
            if (!Application.isPlaying) return;
            Step();
        }

        public bool Step()
        {
            var list = new List<Island>(Island.All);
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    Island a = list[i], b = list[j];
                    if (a == null || b == null || !a.isActiveAndEnabled || !b.isActiveAndEnabled) continue;
                    if (!Island.Near(a, b)) continue;
                    if (!a.DetectContact(b, out _, out _)) continue;

                    Island host = a.useKeyboardInput ? a : b.useKeyboardInput ? b : a.LandArea >= b.LandArea ? a : b;
                    Island guest = host == a ? b : a;

                    Vector2 nrm = guest.PlanarPosition - host.PlanarPosition;
                    nrm = nrm.sqrMagnitude > 1e-4f ? nrm.normalized : Vector2.right;
                    float closing = Mathf.Max(minClosingSpeed, Vector2.Dot(host.PlanarVelocity - guest.PlanarVelocity, nrm));
                    float convergence = PlateSystem.Instance != null
                        ? PlateSystem.Instance.ConvergenceAt((host.PlanarPosition + guest.PlanarPosition) * 0.5f)
                        : 0f;

                    host.MergeFrom(guest, closing, convergence);
                    return true;
                }
            return false;
        }
    }
}
