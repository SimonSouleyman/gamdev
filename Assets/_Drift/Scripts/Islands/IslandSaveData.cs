using System;
using Drift.Life;
using UnityEngine;

namespace Drift.Islands
{
    [Serializable]
    public class IslandSaveData
    {
        public float posX, posZ, yaw, velX, velZ;
        // yaw = heading; bodyYaw = heightfield yaw relative to it (absent in old saves = 0).
        public float bodyYaw;
        public float landRadius, cell, sink;
        public int shapeSeed, nx, nz, kind;
        // IslandArchetype; absent in old saves = 0 = Classic, which is what every island was.
        public int archetype;
        public bool isVolcano;
        public float originX, originZ;
        public float[] heights;
        public long slotKey;
        public float emergeRemaining;
        public LifeSaveData life;

        // JsonUtility writes a null array/object as an empty one, so presence is tested by length.
        public bool HasShape => heights != null && heights.Length > 0 && nx > 0 && nz > 0;
        public bool HasLife => life != null && life.stage != null && life.stage.Length > 0 && life.nx > 0 && life.nz > 0;
    }

    public static class IslandSaveUtil
    {
        public static IslandSaveData CapturePose(Island island)
        {
            Vector2 p = island.PlanarPosition, v = island.SelfVelocity;
            return new IslandSaveData
            {
                posX = p.x, posZ = p.y,
                yaw = island.Yaw,
                bodyYaw = Mathf.DeltaAngle(0f, island.BodyYaw + island.BodyTurnRemaining),
                velX = v.x, velZ = v.y,
                landRadius = island.landRadius, shapeSeed = island.shapeSeed,
                isVolcano = island.isVolcano, kind = (int)island.kind, archetype = (int)island.archetype
            };
        }

        // The data includes the herds (LifeSaveData.herds). Without force it is only returned once the island
        // has lived a while or is burning, so a freshly streamed island stays a cheap pose-only override.
        public static LifeSaveData CaptureLife(Island island, bool force)
        {
            var life = island.GetComponent<IslandLifeSystem>();
            if (life == null) return null;
            LifeSaveData d;
            try { d = life.Capture(); }
            catch (Exception) { return null; }
            if (d == null || d.stage == null || d.stage.Length == 0) return null;
            if (force || life.LifeAge > 30f) return d;
            for (int i = 0; i < d.stage.Length; i++)
                if (d.burn[i] > 0.01f || d.fireT[i] > 0f) return d;
            return null;
        }

        // Call after the island GameObject is active: OnEnable has generated the planned shape, this replaces it.
        // IslandLifeSystem.Restore restores the herds too; only a shape without life data regenerates from seed.
        public static void ApplySaved(Island island, IslandSaveData d)
        {
            if (d.HasShape) island.Restore(d);
            else
            {
                island.SetPlanarPosition(new Vector2(d.posX, d.posZ));
                island.SetSelfVelocity(new Vector2(d.velX, d.velZ));
                island.SetBodyYaw(d.bodyYaw);
            }

            var life = island.GetComponent<IslandLifeSystem>();
            var herds = island.GetComponent<IslandHerdSystem>();
            if (d.HasLife && life != null)
            {
                life.Restore(d.life);
                return;
            }
            if (!d.HasShape) return;
            if (life != null) life.Repopulate();
            if (herds != null) herds.Repopulate();
            var critters = island.GetComponent<IslandCrittersSystem>();
            if (critters != null) critters.Repopulate();
            var settlement = island.GetComponent<IslandSettlementSystem>();
            if (settlement != null) settlement.Repopulate();
        }
    }
}
