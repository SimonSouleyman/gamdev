using UnityEngine;

namespace Drift.Tectonics
{
    public interface IPlateRider
    {
        Vector2 PlanarPosition { get; }
        Vector2 SelfVelocity { get; }
        float Mass { get; }
    }
}
