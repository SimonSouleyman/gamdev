using UnityEngine;

namespace Drift.UI
{
    public struct HudSnapshot
    {
        public float landArea;
        public float boundingRadius;
        public int bareCells;
        public int plainsCells;
        public int shrubCells;
        public int woodsCells;
        public int oldGrowthCells;
        public int burningCells;
    }

    public interface IHudDataSource
    {
        bool TryGetSnapshot(out HudSnapshot snapshot);
    }

    // Unity cannot serialize interface fields, so the main project subclasses this in an
    // assembly that can see Island / IslandLifeSystem and drops it in the inspector.
    public abstract class HudDataBehaviour : MonoBehaviour, IHudDataSource
    {
        public abstract bool TryGetSnapshot(out HudSnapshot snapshot);
    }
}
