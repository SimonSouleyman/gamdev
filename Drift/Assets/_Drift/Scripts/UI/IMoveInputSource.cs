using UnityEngine;

namespace Drift.UI
{
    public interface IMoveInputSource
    {
        // Same shape as Island.Tick(input, dt): x = turn (right positive), y = throttle (forward positive).
        Vector2 Move { get; }
    }
}
