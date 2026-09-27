using System.Collections.Generic;
using Drift.Bridge;
using Drift.Islands;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LastIslandHintTests
    {
        [Test]
        public void LastIsland_OutranksEverything_AndHasItsOwnLabel()
        {
            float last = IslandHintLogic.Rank(IslandHintKind.LastIsland, 500f);
            Assert.Less(last, IslandHintLogic.Rank(IslandHintKind.NewSpecies, 0f));
            Assert.Less(last, IslandHintLogic.Rank(IslandHintKind.Volcano, 0f));
            Assert.AreEqual("Letzte Insel", IslandHintLogic.LabelOf(IslandHintKind.LastIsland, IslandHintEvent.None));
            Assert.IsNotNull(HintGlyphs.Of(HintGlyph.Island));
        }

        // An island that streamed out after drifting respawns at its saved pose; the far pointer has to lead there.
        [Test]
        public void SlotPosition_IsTheSavedPoseOfADriftedIsland_ElseThePlan()
        {
            var go = new GameObject("TestStreamer");
            go.SetActive(false);
            try
            {
                var ws = go.AddComponent<WorldStreamer>();
                ws.seed = 777;
                ws.RestoreState(Vector2.zero, null);
                var slots = ws.WorldSlots();
                Assert.Greater(slots.Count, 1);
                Assert.IsTrue(ws.TryGetSlotPosition(1, out var planned));
                Assert.AreEqual(slots[1].pos, planned, "no saved pose: the planned spot");

                var drifted = slots[1].pos + new Vector2(23f, -17f);
                ws.RestoreOverrides(new List<IslandSaveData> { new IslandSaveData { slotKey = slots[1].key, posX = drifted.x, posZ = drifted.y } });
                Assert.IsTrue(ws.TryGetSlotPosition(1, out var at));
                Assert.AreEqual(drifted, at);
                Assert.IsTrue(ws.TryGetSlotPosition(0, out var other));
                Assert.AreEqual(slots[0].pos, other, "only the drifted slot moves");

                Assert.IsFalse(ws.TryGetSlotPosition(-1, out _));
                Assert.IsFalse(ws.TryGetSlotPosition(slots.Count, out _));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
