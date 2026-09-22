using System.Collections.Generic;
using Drift.Islands;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Adventure loot: the sea animals Encounters lays on the ring (whale, sea turtle, fish shoal), collecting them
    // by driving over them, the escort that then swims alongside and pushes the island on, and the bigger flotsam
    // boost. Ships are the counterpart: they are obstacles and never pay anything.
    public class AdventureCompanionTests
    {
        static readonly RingGeometry Ring = new RingGeometry(9000f, 28f, 640f);

        readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            // A test director that was switched on takes over Encounters' static instance; give the scene its own back.
            foreach (var e in Object.FindObjectsByType<Encounters>(FindObjectsSortMode.None))
            {
                e.enabled = false;
                e.enabled = true;
            }
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            _objects.Add(go);
            return go;
        }

        // Left inactive on purpose: nothing of the scene's own sea is touched, everything is driven by hand.
        (Encounters enc, SeaLifeSystem sea, FishSystem fish) MakeTrack(float spacing)
        {
            var go = New("AdvCompanions");
            var ships = go.AddComponent<ShipSystem>();
            var sea = go.AddComponent<SeaLifeSystem>();
            var fish = go.AddComponent<FishSystem>();
            var enc = go.AddComponent<Encounters>();
            enc.ships = ships;
            enc.seaLife = sea;
            enc.fish = fish;
            enc.seed = 23;
            enc.animalSpacing = spacing;
            return (enc, sea, fish);
        }

        Island MakeIsland(Vector2 pos, float radius, int seed)
        {
            var go = New("AdvCompanionIsle_" + seed);
            go.transform.position = new Vector3(pos.x, 0f, pos.y);
            var isl = go.AddComponent<Island>();
            isl.useKeyboardInput = false;
            isl.sinkEnabled = false;
            isl.landRadius = radius;
            isl.cellSize = IslandArchetypes.CellSize(radius);
            isl.shapeSeed = seed;
            go.SetActive(true);
            return isl;
        }

        static List<Vector2> Pickups(SeaLifeSystem sea)
        {
            var list = new List<Vector2>();
            for (int i = 0; i < sea.GroupSlots; i++)
                if (sea.GroupIsPickup(i)) list.Add(sea.GroupPosition(i));
            return list;
        }

        [Test]
        public void TheTrackCarriesAnimals_InsideTheBand()
        {
            var (enc, sea, fish) = MakeTrack(45f);
            sea.maxPickups = 4;
            fish.maxPickupSchools = 3;
            var start = new Vector2(Ring.centerX, 1000f);
            int laid = 0;
            // Walks the player along the band so the frontier keeps moving on.
            for (int step = 0; step < 24; step++)
            {
                enc.LayTrack(Ring, new Vector2(start.x, start.y + step * 20f), 1f, 30f, 60f, 3f);
                laid = enc.TrackAnimals;
            }
            Assert.GreaterOrEqual(laid, 4, "a steady supply of animals on the track");
            Assert.Greater(sea.PickupsCollected + sea.PickupCount + fish.PickupSchoolCount, 0, "something is waiting out there");
            foreach (var p in Pickups(sea))
                Assert.IsTrue(Ring.Inside(p, 2f), "inside the band, not folded over the rim: " + p);
        }

        [Test]
        public void AnimalsNeverLieOnAnIsland()
        {
            var (enc, sea, _) = MakeTrack(30f);
            var start = new Vector2(Ring.centerX, 2000f);
            var rock = MakeIsland(new Vector2(Ring.centerX, start.y + 40f), 8f, 1234);
            for (int step = 0; step < 8; step++)
                enc.LayTrack(Ring, new Vector2(start.x, start.y + step * 8f), 1f, 30f, 70f, 3f);
            var pickups = Pickups(sea);
            Assert.Greater(pickups.Count, 0);
            foreach (var p in pickups)
            {
                Vector2 d = new Vector2(p.x - rock.PlanarPosition.x, Ring.AlongDelta(p.y, rock.PlanarPosition.y));
                Assert.Greater(d.magnitude, rock.BoundingRadius, "never on the island: " + p);
            }
        }

        [Test]
        public void DrivingOverAnAnimal_MakesItAnEscort_AndTheEscortIsCapped()
        {
            var (_, sea, _) = MakeTrack(100f);
            sea.maxPickups = 4;
            sea.maxCompanions = 2;
            var at = new Vector2(500f, 500f);
            for (int i = 0; i < 4; i++)
                sea.SpawnPickup(i % 2 == 0 ? SeaLifeSystem.Kind.Whale : SeaLifeSystem.Kind.Turtle,
                    at + new Vector2(i * 0.5f, 0f), Vector2.up);
            Assert.AreEqual(sea.maxPickups, sea.PickupCount, "no more pickups than the cap");

            int joined = 0;
            System.Action<CompanionKind, Vector3> announce = (k, p) => joined++;
            Encounters.CompanionJoined += announce;
            try
            {
                int got = sea.CollectPickupsAt(at, 4f);
                Assert.AreEqual(sea.maxCompanions, got, "the island picks up as many as it may carry");
                Assert.AreEqual(sea.maxCompanions, sea.CompanionCount);
                Assert.AreEqual(sea.maxCompanions, joined, "every one of them is announced");
            }
            finally { Encounters.CompanionJoined -= announce; }

            for (int i = 0; i < sea.GroupSlots; i++)
                if (sea.GroupIsCompanion(i)) Assert.Greater(sea.CompanionSecondsLeft(i), 1f);
            // A pickup far away stays where it is.
            Assert.AreEqual(0, sea.CollectPickupsAt(at + new Vector2(400f, 0f), 4f));
        }

        [Test]
        public void AShoalIsAnEscortToo()
        {
            var (_, _, fish) = MakeTrack(100f);
            var at = new Vector2(700f, 700f);
            Assert.GreaterOrEqual(fish.SpawnPickupShoal(at, Vector2.up), 0);
            Assert.AreEqual(1, fish.PickupSchoolCount);
            Assert.AreEqual(1, fish.CollectPickupsAt(at, 3f));
            Assert.AreEqual(1, fish.EscortCount);
            Assert.AreEqual(0, fish.PickupSchoolCount);
        }

        [Test]
        public void EveryEscortAddsSpeed_UpToTheCap()
        {
            var (enc, sea, _) = MakeTrack(100f);
            enc.companionBoost = 0.2f;
            enc.maxCompanionStack = 2;
            sea.maxPickups = 4;
            sea.maxCompanions = 4;
            Assert.AreEqual(1f, enc.CompanionFactor, 1e-4f, "nothing swimming along, no push");
            var at = new Vector2(900f, 900f);
            for (int i = 0; i < 3; i++) sea.SpawnPickup(SeaLifeSystem.Kind.Turtle, at, Vector2.up);
            sea.CollectPickupsAt(at, 4f);
            Assert.AreEqual(3, sea.CompanionCount);
            Assert.AreEqual(3, enc.CompanionCount);
            Assert.AreEqual(1.4f, enc.CompanionFactor, 1e-4f, "the stack stops at maxCompanionStack");
        }

        [Test]
        public void FlotsamPaysAClearlyBiggerBoost_AndTheSameBuoyancy()
        {
            foreach (ShipSystem.FlotsamKind k in System.Enum.GetValues(typeof(ShipSystem.FlotsamKind)))
            {
                var adv = EncounterPacer.RewardFor(k, true);
                if (k == ShipSystem.FlotsamKind.Buoy)
                {
                    Assert.AreEqual(0f, adv.boostSeconds, "a buoy is not collected");
                    continue;
                }
                // Was 1.2 s for everything; the push is what the run is about now.
                Assert.GreaterOrEqual(adv.boostSeconds, 2f, k.ToString());
                Assert.LessOrEqual(adv.boostSeconds, 3f, k.ToString());
                // The buoyancy is the survival resource and must not have moved.
                Assert.Greater(adv.buoyancy, 0.02f, k.ToString());
                Assert.LessOrEqual(adv.buoyancy, 0.1f, k.ToString());
            }
            Assert.Greater(EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Crate, true).boostSeconds,
                EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Bottle, true).boostSeconds, "a crate is worth more than a bottle");
            // Cozy is untouched: flotsam there is a small lift and never a boost.
            foreach (ShipSystem.FlotsamKind k in System.Enum.GetValues(typeof(ShipSystem.FlotsamKind)))
                Assert.AreEqual(0f, EncounterPacer.RewardFor(k, false).boostSeconds, k.ToString());
        }

        [Test]
        public void AShipHitCostsSpeedAndPaysNothing()
        {
            var go = New("AdvCompanionShipHit");
            var enc = go.AddComponent<Encounters>();
            enc.shipStaggerSeconds = 1.2f;
            enc.shipStaggerFactor = 0.4f;
            // Switched on: only the running director can take the speed off the island (TearDown gives the scene
            // its own back).
            go.SetActive(true);

            var isl = New("AdvCompanionPlayer");
            isl.transform.position = Vector3.zero;
            var player = isl.AddComponent<Island>();
            player.useKeyboardInput = true;
            player.sinkEnabled = false;
            player.landRadius = 6f;
            player.cellSize = IslandArchetypes.CellSize(6f);
            isl.SetActive(true);

            float before = player.RawBuoyancy;
            int hits = 0;
            System.Action<ShipSystem.ShipKind, Vector3> h = (k, p) => hits++;
            Encounters.ShipHit += h;
            try
            {
                Encounters.NotifyShipHit(ShipSystem.ShipKind.SailBoat, Vector3.zero);
                Assert.AreEqual(1, hits, "the bump is announced");
                Assert.IsTrue(player.Staggered, "and it costs speed");
                Assert.AreEqual(0.4f, player.StaggerFactor, 1e-3f);
                Assert.AreEqual(before, player.RawBuoyancy, 1e-4f, "islands stay the danger: a ship costs no buoyancy");
                Assert.IsFalse(player.Boosting, "and never pays a boost");
            }
            finally { Encounters.ShipHit -= h; }
        }

        [Test]
        public void EveryHullFitsInsideTheBand()
        {
            foreach (ShipSystem.ShipKind k in System.Enum.GetValues(typeof(ShipSystem.ShipKind)))
            {
                float m = ShipSystem.ShipBandMargin(k);
                Assert.Greater(m, 0.5f, k.ToString());
                Assert.Less(m, Ring.halfWidth * 0.5f, k.ToString() + " still leaves water to sail in");
            }
            Assert.Greater(ShipSystem.ShipBandMargin(ShipSystem.ShipKind.TradingCog),
                ShipSystem.ShipBandMargin(ShipSystem.ShipKind.RowBoat), "the long cog keeps further off the rim");
        }
    }
}
