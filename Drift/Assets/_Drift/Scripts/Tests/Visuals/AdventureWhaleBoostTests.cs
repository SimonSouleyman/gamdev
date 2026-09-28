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
    // v0.6.5: "Der Wal-Boost ist zu stark. Setze auf 2x runter. Die Dauer muss auch auf 3 s reduziert werden. ...
    // abwechselnd auf beiden Seiten der Insel, zum Beispiel ... 1 s links, 1 s rechts, 1 s links, evtl. mit Ab- und
    // Auftauchen." Plus: the whale lagged beside the island (the meshes were rebuilt at 15 Hz only).
    public class AdventureWhaleBoostTests
    {
        readonly List<GameObject> _objects = new();
        readonly List<Island> _scenePlayers = new();
        System.Func<Vector2, Vector2> _savedConstraint;
        Encounters _sceneEncounters;
        float _savedSeconds, _savedFactor;

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
            // The scene's Encounters serializes its own boost values: run on the code defaults.
            _sceneEncounters = Object.FindAnyObjectByType<Encounters>();
            if (_sceneEncounters != null)
            {
                _savedSeconds = _sceneEncounters.whaleBoostSeconds;
                _savedFactor = _sceneEncounters.whaleBoostFactor;
                var defaults = Defaults();
                _sceneEncounters.whaleBoostSeconds = defaults.whaleBoostSeconds;
                _sceneEncounters.whaleBoostFactor = defaults.whaleBoostFactor;
            }
        }

        [TearDown]
        public void TearDown()
        {
            Island.PositionConstraint = _savedConstraint;
            if (_sceneEncounters != null)
            {
                _sceneEncounters.whaleBoostSeconds = _savedSeconds;
                _sceneEncounters.whaleBoostFactor = _savedFactor;
            }
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            foreach (var other in _scenePlayers) if (other != null) other.useKeyboardInput = true;
            _scenePlayers.Clear();
        }

        // A never-enabled Encounters: its field values are the code defaults and it does not become the instance.
        Encounters Defaults()
        {
            var go = new GameObject("EncounterDefaults");
            go.SetActive(false);
            _objects.Add(go);
            return go.AddComponent<Encounters>();
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
        public void TheWhaleBoost_IsThreeSecondsAtTwiceThePace_AndTheGhost()
        {
            var defaults = Defaults();
            Assert.AreEqual(3f, defaults.whaleBoostSeconds, 1e-4f, "owner: 3 s");
            Assert.AreEqual(2f, defaults.whaleBoostFactor, 1e-4f, "owner: x2");

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
            Assert.AreEqual(2f, player.BoostFactor, 1e-3f, "x2, still above the flotsam's x1.65");
            Assert.AreEqual(3f, player.BoostRemaining, 1e-3f);
            Assert.IsTrue(player.Ghosting);
            Assert.AreEqual(3f, player.GhostRemaining, 1e-3f);
            // Runs its three seconds, then it is over.
            for (float t = 0f; t < 2.9f; t += 0.05f) player.Tick(Vector2.zero, 0.05f);
            Assert.IsTrue(player.Boosting && player.Ghosting, "still on just before the three seconds");
            for (float t = 0f; t < 0.3f; t += 0.05f) player.Tick(Vector2.zero, 0.05f);
            Assert.IsFalse(player.Boosting);
            Assert.IsFalse(player.Ghosting);
        }

        [Test]
        public void TheFlank_ChangesEveryThirdOfTheBoost_UnderTheIsland()
        {
            float Side(float t, float total, out float dip) => SeaLifeSystem.WhaleBoostCrossing(t, total, 0.4f, out dip);
            float dip;
            Assert.AreEqual(0f, Side(0.1f, 3f, out dip)); Assert.AreEqual(0f, dip);
            Assert.AreEqual(0f, Side(0.5f, 3f, out dip)); Assert.AreEqual(0f, dip, "up on its first side");
            Assert.AreEqual(1f, Side(1.5f, 3f, out dip)); Assert.AreEqual(0f, dip, "the other side in the second second");
            Assert.AreEqual(0f, Side(2.5f, 3f, out dip)); Assert.AreEqual(0f, dip, "back on the first in the third");
            Assert.AreEqual(0.5f, Side(1f, 3f, out dip), 1e-4f, "half way across at the change");
            Assert.AreEqual(1f, dip, 1e-4f, "right under the island");
            Assert.AreEqual(0.5f, Side(2f, 3f, out dip), 1e-4f);
            Assert.AreEqual(1f, dip, 1e-4f);
            Assert.AreEqual(0f, Side(3.6f, 3f, out dip), "held on inside a coast: it stays put");
            Assert.AreEqual(0f, dip);
            // It always takes a third of the boost: a 6-s boost changes at 2 s and 4 s.
            Assert.AreEqual(0f, Side(1.5f, 6f, out _));
            Assert.AreEqual(1f, Side(3f, 6f, out _));
            Assert.AreEqual(0.5f, Side(4f, 6f, out _), 1e-4f);
            // Smooth: no jump anywhere along the way.
            float last = Side(0f, 3f, out _);
            for (float t = 0.01f; t < 3.5f; t += 0.01f)
            {
                float v = Side(t, 3f, out _);
                Assert.Less(Mathf.Abs(v - last), 0.1f, "t=" + t);
                last = v;
            }
        }

        [Test]
        public void TheWhale_ChangesFlanks_DippingUnderTheIsland_ThenDivesAndSwimsOff()
        {
            var player = MakePlayer(new Vector2(3000f, 4000f));
            var sea = MakeSea(player);
            sea.Step(0f);
            // Met on the left: left, right, left.
            Assert.GreaterOrEqual(sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(-2f, 6f), Vector2.up), 0);
            Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius));
            int slot = WhaleSlot(sea);
            Assert.GreaterOrEqual(slot, 0);

            const float dt = 0.02f;
            // The island races along at the whale boost's pace; SeaLifeSystem reads its velocity from the debug hook.
            float speed = 2f * player.MaxSpeed;
            sea.debugPlayerVelocity = new Vector2(0f, speed);
            Vector2 pos = player.PlanarPosition;
            void Advance()
            {
                pos.y += speed * dt;
                player.Tick(Vector2.zero, dt);
                player.SetPlanarPosition(pos);
                sea.Step(dt);
            }
            float r = player.BoundingRadius;
            float worstAlong = 0f, worstEdge = 0f;
            int beside = 0, under = 0;
            Vector2 lastD = Vector2.zero;
            float worstStep = 0f;
            for (int n = 1; n * dt < 2.95f; n++)
            {
                Advance();
                float t = n * dt;
                Assert.IsTrue(sea.GroupIsWhaleBoost(slot), "with the island the whole boost, t=" + t);
                Vector2 d = sea.GroupPosition(slot) - player.PlanarPosition;
                if (t > 0.3f)
                {
                    worstAlong = Mathf.Max(worstAlong, Mathf.Abs(d.y));
                    worstStep = Mathf.Max(worstStep, (d - lastD).magnitude);
                }
                lastD = d;
                float expect = t > 0.3f && t < 0.72f || t > 2.28f ? -1f : t > 1.28f && t < 1.72f ? 1f : 0f;
                if (expect != 0f)
                {
                    Assert.AreEqual(expect, Mathf.Sign(d.x), "the flank at t=" + t);
                    Assert.AreEqual(expect, sea.WhaleBoostSide(slot));
                    Assert.Greater(Mathf.Abs(d.x), r, "beside the coast, not on top of the island, t=" + t);
                    worstEdge = Mathf.Max(worstEdge, Mathf.Abs(d.x) - r);
                    Assert.AreEqual(1, sea.WhaleState(slot), "up and breathing");
                    Assert.Less(sea.GroupDepth(slot), 0.05f, "surfaced on its side, t=" + t);
                    beside++;
                }
                if (Mathf.Abs(t - 1f) < 0.05f || Mathf.Abs(t - 2f) < 0.05f)
                {
                    Assert.Greater(sea.GroupDepth(slot), 0.9f, "dipped down, t=" + t);
                    Assert.Less(Mathf.Abs(d.x), r, "passing under the island, t=" + t);
                    under++;
                }
            }
            Assert.Greater(beside, 60);
            Assert.Greater(under, 6);
            Assert.Less(worstAlong, r + 2f, "keeps up along the track at x2");
            Assert.Less(worstEdge, 4f, "a few units off the coast - unmistakably with the island");
            Assert.Less(worstStep, 1.2f, "moves across smoothly, no jumps");

            // Three seconds are over (no obstacle keeps the ghost alive): the whale dives and turns away.
            for (int n = 0; n < 15; n++) Advance();
            Assert.IsFalse(sea.GroupIsWhaleBoost(slot), "the boost whale is released");
            Assert.AreEqual(2, sea.WhaleState(slot), "it lifts its fluke and dives");
            Vector2 at = sea.GroupPosition(slot);
            Assert.Less(at.x - player.PlanarPosition.x, 0f, "from the left flank, where it ended");
            // The boost is over, the island is back at its pace; the whale is left behind, swimming off.
            speed = player.AdventureCruise * player.MaxSpeed;
            sea.debugPlayerVelocity = new Vector2(0f, speed);
            for (int n = 0; n < 150; n++) Advance();
            Vector2 after = sea.GroupPosition(slot);
            Assert.Greater(at.x - after.x, 1f, "and swims off to its own side");
            Assert.AreNotEqual(1, sea.WhaleState(slot), "without surfacing next to the island again");
        }

        [Test]
        public void ASecondWhale_TakesOver_TheFirstDivesAway()
        {
            var player = MakePlayer(new Vector2(3000f, 7000f));
            var sea = MakeSea(player);
            sea.Step(0f);
            sea.maxPickups = 4;
            Assert.GreaterOrEqual(sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(-2f, 5f), Vector2.up), 0);
            Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius));
            int first = WhaleSlot(sea);
            const float dt = 0.02f;
            float speed = 2f * player.MaxSpeed;
            sea.debugPlayerVelocity = new Vector2(0f, speed);
            Vector2 pos = player.PlanarPosition;
            for (int n = 0; n < 50; n++)
            {
                pos.y += speed * dt;
                player.Tick(Vector2.zero, dt);
                player.SetPlanarPosition(pos);
                sea.Step(dt);
            }
            Assert.GreaterOrEqual(sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(2f, 5f), Vector2.up), 0);
            Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius));
            Assert.AreEqual(1, sea.WhaleBoostCount, "one whale beside the island, never two");
            Assert.IsFalse(sea.GroupIsWhaleBoost(first));
            Assert.AreEqual(2, sea.WhaleState(first), "the first one dives off");
            // The new boost's ghost ride does not hold the old whale either: 3 s later only the new one ends.
            for (int n = 0; n < 160; n++)
            {
                pos.y += speed * dt;
                player.Tick(Vector2.zero, dt);
                player.SetPlanarPosition(pos);
                sea.Step(dt);
            }
            Assert.AreEqual(0, sea.WhaleBoostCount, "over after its own three seconds");
        }

        [Test]
        public void EscortsAtRacePace_AreDrawnEveryFrame()
        {
            var player = MakePlayer(new Vector2(3000f, 6000f));
            var sea = MakeSea(player);
            sea.Step(0f);
            sea.Step(0.02f);
            Assert.IsFalse(sea.EscortEveryFrame, "nothing alongside: the usual 15 Hz");
            sea.SpawnPickup(SeaLifeSystem.Kind.Whale, player.PlanarPosition + new Vector2(2f, 5f), Vector2.up);
            Assert.AreEqual(1, sea.CollectPickupsAt(player.PlanarPosition, player.BoundingRadius));
            sea.debugPlayerVelocity = new Vector2(0f, 40f);
            player.Tick(Vector2.zero, 0.02f);
            sea.Step(0.02f);
            Assert.IsTrue(sea.EscortEveryFrame, "the boost whale is rebuilt every frame");
            Assert.Greater(sea.AboveVertexCount + sea.UnderVertexCount, 0);
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
