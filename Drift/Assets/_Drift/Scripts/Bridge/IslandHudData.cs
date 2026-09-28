using Drift.Islands;
using Drift.Life;
using Drift.UI;

namespace Drift.Bridge
{
    public class IslandHudData : HudDataBehaviour
    {
        public Island player;

        IslandLifeSystem _life;

        public override bool TryGetSnapshot(out HudSnapshot snapshot)
        {
            snapshot = default;
            if (player == null)
                foreach (var i in Island.All) if (i != null && i.useKeyboardInput) { player = i; break; }
            if (player == null) return false;
            if (_life == null || _life.gameObject != player.gameObject) _life = player.GetComponent<IslandLifeSystem>();

            snapshot.landArea = player.LandArea;
            snapshot.boundingRadius = player.BoundingRadius;
            if (_life != null)
            {
                _life.GetHudStats(out snapshot.bareCells, out snapshot.plainsCells, out snapshot.shrubCells,
                    out snapshot.woodsCells, out snapshot.oldGrowthCells, out snapshot.burningCells);
            }
            return true;
        }
    }
}
