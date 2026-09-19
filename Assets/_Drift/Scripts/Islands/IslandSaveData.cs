using System;

namespace Drift.Islands
{
    [Serializable]
    public class IslandSaveData
    {
        public float posX, posZ, yaw, velX, velZ;
        public float landRadius, cell;
        public int shapeSeed, nx, nz;
        public float originX, originZ;
        public float[] heights;
    }
}
