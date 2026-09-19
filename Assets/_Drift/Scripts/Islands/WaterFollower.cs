using UnityEngine;

namespace Drift.Islands
{
    [ExecuteAlways]
    public class WaterFollower : MonoBehaviour
    {
        public Transform follow;

        void LateUpdate()
        {
            Transform t = follow != null ? follow : (Camera.main != null ? Camera.main.transform : null);
            if (t == null) return;
            transform.position = new Vector3(t.position.x, transform.position.y, t.position.z);
        }
    }
}
