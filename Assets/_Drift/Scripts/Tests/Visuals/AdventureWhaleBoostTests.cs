using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner (v0.6.4): "Wenn man den Wal einsammelt, soll man für 4 Sekunden einen stärkeren Boost als bei dem Treibgut
    // bekommen, außerdem soll man durch Inseln fahren können für die Zeit. Der Wal soll während dieser Zeit in der
    // unmittelbaren Nähe der Insel schwimmen ... Sobald es fertig ist, soll der Wal abtauchen und wegschwimmen."
    public class AdventureWhaleBoostTests
    {
        readonly List<GameObject> _objects = new();
        readonly List<Island> _scenePlayers = new();
        System.Func<Vector2, Vector2> _savedConstraint;

        [SetUp]
        public void SetUp()
        {
            _savedConstraint = Island.PositionConstraint;
            Island.PositionConstraint = null;
            // Encounters finds the player through Island.All: the scene's own player island must not get the boost.
            _scenePlayers.Clear();
            foreach (var other in Island.All)
                if (other != null && other.useKeyboardInput) _scenePlayers.Add(other);
            foreach (var other in _scenePlayers) other.useKeyboardInput = false;
        }

        [TearDown]
        public void TearDown()
        {
            Island.PositionConstraint = _savedConstraint;
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            foreach (var other in _scenePlayers) if (other != null) other.useKeyboardInput = true;
            _scenePlayers.Clear();
        }

        Island MakePlayer(Vector2 pos)
        {
            var go = new GameObject("WhalePlayer");
            go.SetActive(false);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = true;
            isl.sinkEnabled = false;
            isl.ModeOverride = GameMode.Adventure;
            isl.landRadius = 3f;
            isl.cellSize = IslandArchetypes.CellSize(3f);
            isl.shapeSeed = 411;
            isl.carryResponse = 0f;
            isl.surfResponse = 0f;
            isl.AdventureCruise = 0.62f;
            isl.AdventureSpeedScale = 1.45f;
            isl.AdventureTrack = Vector2.up;
            go.SetActive(true);
            _objects.Add(go);
            return isl;
        }

        SeaLifeSystem MakeSea(Island player)
        {
            var go = new GameObject("WhaleSea");
            go.SetActive(false);
            _objects.Add(go);
            var sea = go.AddComponent<SeaLifeSystem>();
            sea.debugPlayer = player;
            sea.maxCompanions = 2;
            return sea;
        }

        int WhaleSlot(SeaLifeSystem sea)
        {
            for (int i = 0; i < sea.GroupSlots; i++) if (sea.GroupIsWhaleBoost(i)) return i;
            return -1;
        }

        [Test]
        public void CollectingAWhale_GivesAFourSecondBoost_AboveTheFlotsam_AndTheGhost()
        {
            var player = MakePlayer(new Vector2(3000f, 3000f));
            var sea = MakeSea(player);
            Assert.GreaterOrEqual(sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(0f, 3f), Vector2.up), 0);
            int joined = 0;
            System.Action<Vector3> started = p => joined++;
            Encounters.WhaleBoostStarted += started;
            try { Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius)); }
            finally { Encounters.WhaleBoostStarted -= started; }
            Assert.AreEqual(1, joined, "announced (audio, HUD)");
            Assert.AreEqual(1, sea.WhaleBoostCount);
            Assert.AreEqual(0, sea.CompanionCount, "no longer a 22-s escort");
            Assert.IsTrue(player.Boosting);
            Assert.AreEqual(2.5f, player.BoostFactor, 1e-3f, "x2.5, clearly above the flotsam's x1.65");
            Assert.AreEqual(4f, player.BoostRemaining, 1e-3f);
            Assert.IsTrue(player.Ghosting);
            Assert.AreEqual(4f, player.GhostRemaining, 1e-3f);
            // Runs its four seconds, then it is over.
            for (float t = 0f; t < 3.9f; t += 0.05f) player.Tick(Vector2.zero, 0.05f);
            Assert.IsTrue(player.Boosting && player.Ghosting, "still on just before the four seconds");
            for (float t = 0f; t < 0.3f; t += 0.05f) player.Tick(Vector2.zero, 0.05f);
            Assert.IsFalse(player.Boosting);
            Assert.IsFalse(player.Ghosting);
        }

        [Test]
        public void TheWhale_SwimsRightBesideTheIsland_ThenDivesAndSwimsOff()
        {
            var player = MakePlayer(new Vector2(3000f, 4000f));
            var sea = MakeSea(player);
            sea.Step(0f);
            Assert.GreaterOrEqual(sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(2f, 6f), Vector2.up), 0);
            Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius));
            int slot = WhaleSlot(sea);
            Assert.GreaterOrEqual(slot, 0);

            const float dt = 0.05f;
            // The island races along at the whale boost's pace; SeaLifeSystem reads its velocity from the debug hook.
            float speed = 2.5f * player.MaxSpeed;
            sea.debugPlayerVelocity = new Vector2(0f, speed);
            Vector2 pos = player.PlanarPosition;
            void Advance()
            {
                pos.y += speed * dt;
                player.Tick(Vector2.zero, dt);
                player.SetPlanarPosition(pos);
                sea.Step(dt);
            }
            float worst = 0f, worstEdge = 0f;
            int up = 0, frames = 0;
            for (float t = 0f; t < 3.8f; t += dt)
            {
                Advance();
                Assert.IsTrue(sea.GroupIsWhaleBoost(slot), "beside the island the whole boost, t=" + t);
                if (t < 0.6f) continue;
                Vector2 d = sea.GroupPosition(slot) - player.PlanarPosition;
                worst = Mathf.Max(worst, Mathf.Abs(d.y));
                worstEdge = Mathf.Max(worstEdge, Mathf.Abs(d.x) - player.BoundingRadius);
                Assert.Greater(Mathf.Abs(d.x), player.BoundingRadius, "beside the coast, not on top of the island");
                frames++;
                if (sea.WhaleState(slot) == 1) up++;
            }
            Assert.Less(worst, player.BoundingRadius + 2f, "keeps up along the track at x2.5");
            Assert.Less(worstEdge, 4f, "a few units off the coast - unmistakably with the island");
            Assert.AreEqual(frames, up, "surfaced (and blowing) the whole time");

            // Four seconds are over (no obstacle keeps the ghost alive): the whale dives and turns away.
            for (float t = 0f; t < 0.6f; t += dt) Advance();
            Assert.IsFalse(sea.GroupIsWhaleBoost(slot), "the boost whale is released");
            Assert.AreEqual(2, sea.WhaleState(slot), "it lifts its fluke and dives");
            Vector2 at = sea.GroupPosition(slot);
            float side = Mathf.Sign(at.x - player.PlanarPosition.x);
            // The boost is over, the island is back at its pace; the whale is left behind, swimming off.
            speed = player.AdventureCruise * player.MaxSpeed;
            sea.debugPlayerVelocity = new Vector2(0f, speed);
            for (float t = 0f; t < 3f; t += dt) Advance();
            Vector2 after = sea.GroupPosition(slot);
            Assert.Greater((after.x - at.x) * side, 1f, "and swims off to its own side");
            Assert.AreNotEqual(1, sea.WhaleState(slot), "without surfacing next to the island again");
        }

        [Test]
        public void AWhaleIsCollectedEvenWithAFullEscort()
        {
            var player = MakePlayer(new Vector2(3000f, 5000f));
            var sea = MakeSea(player);
            sea.maxPickups = 4;
            var at = player.PlanarPosition;
            sea.SpawnPickup(SeaLifeSystem.Kind.Turtle, at, Vector2.up);
            sea.SpawnPickup(SeaLifeSystem.Kind.Turtle, at + new Vector2(0.5f, 0f), Vector2.up);
            Assert.AreEqual(2, sea.CollectPickupsAt(at, 4f));
            Assert.AreEqual(2, sea.CompanionCount);
            sea.SpawnPickup(SeaLifeSystem.Kind.Whale, at + new Vector2(0f, 2f), Vector2.up);
            Assert.AreEqual(1, sea.CollectPickupsAt(at, 4f), "the whale is a boost, not a third escort");
            Assert.AreEqual(1, sea.WhaleBoostCount);
            Assert.AreEqual(2, sea.CompanionCount);
        }
    }
}
