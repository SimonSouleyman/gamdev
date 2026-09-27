using UnityEngine;

namespace Drift.Core
{
    // Implemented by a component on an island GameObject that owns a dock (settlements). Ships look it up with
    // TryGetComponent<IDockProvider> and moor at pos, bow pointing against seaDir; without one they stop offshore.
    public interface IDockProvider
    {
        // pos: world position of the mooring spot on the water; seaDir: unit world direction from the dock out to sea.
        bool TryGetDockWorld(out Vector3 pos, out Vector3 seaDir);
    }
}
