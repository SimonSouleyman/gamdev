using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Travel encounters (Drift.Visuals.Encounters): the pacing of dolphins / whales / flotsam around the driving
    // player, the rewards, and collecting a piece of flotsam.
    public class EncounterTests
    {
        static EncounterConditions Driving(float speed = 5f) => new EncounterConditions
        {
            speed = speed, openWater = true, storm = 0f, night = 0f, adventure = false, busy = false
        };

        // Drives for `seconds` in steps of dt, confirming whatever the pacer asks for; returns the times of each start.
        static System.Collections.Generic.List<(float t, EncounterKind k)> Drive(EncounterPacer p, float seconds, EncounterConditions c, float dt = 0.1f)
        {
            var list = new System.Collections.Generic.List<(float, EncounterKind)>();
            for (float t = 0f; t < seconds; t += dt)
            {
                var k = p.Tick(dt, c);
                if (k == EncounterKind.None) continue;
                p.Confirm(k);
                list.Add((t, k));
            }
            return list;
        }

        [Test]
        public void Gaps_StayWithin20To40SecondsOfDriving()
        {
            for (uint seed = 1; seed < 20; seed++)
            {
                var p = new EncounterPacer(seed);
                var starts = Drive(p, 600f, Driving());
                Assert.GreaterOrEqual(starts.Count, 600 / 40 - 1, $"seed {seed}");
                Assert.LessOrEqual(starts.Count, 600 / 20 + 1, $"seed {seed}");
                Assert.LessOrEqual(starts[0].t, p.firstGapMax + 0.2f, "the first encounter comes early in a session");
                for (int i = 1; i < starts.Count; i++)
                {
                    float gap = starts[i].t - starts[i - 1].t;
                    Assert.GreaterOrEqual(gap, p.minGap - 0.2f, $"seed {seed} #{i}");
                    Assert.LessOrEqual(gap, p.maxGap + 0.2f, $"seed {seed} #{i}");
                }
            }
        }

        [Test]
        public void Resting_DoesNotAdvanceThePacing()
        {
            var p = new EncounterPacer(3);
            float before = p.Timer;
            var starts = Drive(p, 300f, Driving(0.5f));
            Assert.AreEqual(0, starts.Count);
            Assert.AreEqual(before, p.Timer, 1e-4f);
            Assert.AreEqual(0f, p.DrivenSeconds, 1e-4f);
        }

        [Test]
        public void Busy_HoldsTheTimer()
        {
            var p = new EncounterPacer(4);
            var c = Driving();
            c.busy = true;
            float before = p.Timer;
            Assert.AreEqual(0, Drive(p, 200f, c).Count);
            Assert.AreEqual(before, p.Timer, 1e-4f);
        }

        [Test]
        public void NeverTheSameKindTwiceInARow_WhenThereIsAChoice()
        {
            var p = new EncounterPacer(9);
            p.whaleMinGap = 0f;
            var starts = Drive(p, 3000f, Driving());
            Assert.Greater(starts.Count, 60);
            int dolphins = 0, whales = 0, flotsam = 0;
            for (int i = 0; i < starts.Count; i++)
            {
                if (i > 0) Assert.AreNotEqual(starts[i - 1].k, starts[i].k, $"#{i}");
                if (starts[i].k == EncounterKind.Dolphins) dolphins++;
                else if (starts[i].k == EncounterKind.Whale) whales++;
                else flotsam++;
            }
            Assert.Greater(dolphins, 0);
            Assert.Greater(whales, 0);
            Assert.Greater(flotsam, 0);
        }

        [Test]
        public void Whales_AreRarerAndSpacedOut()
        {
            var p = new EncounterPacer(12);
            var starts = Drive(p, 1800f, Driving());
            float lastWhale = -1e9f;
            int whales = 0;
            foreach (var s in starts)
            {
                if (s.k != EncounterKind.Whale) continue;
                whales++;
                Assert.GreaterOrEqual(s.t - lastWhale, p.whaleMinGap - 0.2f);
                lastWhale = s.t;
            }
            Assert.Greater(whales, 0);
            Assert.Less(whales, starts.Count / 3);
        }

        [Test]
        public void Adventure_OnlyDolphins_TheFlotsamIsTheRingsOwnStream()
        {
            var p = new EncounterPacer(5);
            p.whaleMinGap = 0f;
            var c = Driving();
            c.adventure = true;
            var starts = Drive(p, 1200f, c);
            Assert.Greater(starts.Count, 20);
            // Whales are cozy only, and in Abenteuer the ring lays out its own steady flotsam (Encounters.TickTrack),
            // so the pacer must not drop single pieces on top of it.
            foreach (var s in starts) Assert.AreEqual(EncounterKind.Dolphins, s.k);
        }

        [Test]
        public void Storm_KeepsOnlyFlotsam_AndHeavyStormPauses()
        {
            var p = new EncounterPacer(6);
            var c = Driving();
            c.storm = 0.5f;
            var starts = Drive(p, 600f, c);
            Assert.Greater(starts.Count, 10);
            foreach (var s in starts) Assert.AreEqual(EncounterKind.Flotsam, s.k);

            var q = new EncounterPacer(6);
            c.storm = 0.8f;
            Assert.AreEqual(0, Drive(q, 600f, c).Count);
        }

        [Test]
        public void Night_IsCalmer_AndHasNoWhales()
        {
            var day = Drive(new EncounterPacer(8) { whaleMinGap = 0f }, 1200f, Driving());
            var c = Driving();
            c.night = 1f;
            var night = Drive(new EncounterPacer(8) { whaleMinGap = 0f }, 1200f, c);
            Assert.Less(night.Count, day.Count * 0.75f);
            foreach (var s in night) Assert.AreNotEqual(EncounterKind.Whale, s.k);
        }

        [Test]
        public void Dolphins_NeedSpeedAndOpenWater()
        {
            var p = new EncounterPacer(2);
            var slow = Driving(2f);
            Assert.IsFalse(p.Allowed(EncounterKind.Dolphins, slow));
            Assert.IsTrue(p.Allowed(EncounterKind.Flotsam, slow));
            var coast = Driving();
            coast.openWater = false;
            Assert.IsFalse(p.Allowed(EncounterKind.Dolphins, coast));
            Assert.IsFalse(p.Allowed(EncounterKind.Whale, coast));
            Assert.IsTrue(p.Allowed(EncounterKind.Flotsam, coast));
            foreach (var s in Drive(p, 600f, coast)) Assert.AreEqual(EncounterKind.Flotsam, s.k);
        }

        [Test]
        public void Unconfirmed_RetriesSoon()
        {
            var p = new EncounterPacer(1);
            var c = Driving();
            EncounterKind k = EncounterKind.None;
            float t = 0f;
            while (k == EncounterKind.None && t < 60f) { k = p.Tick(0.1f, c); t += 0.1f; }
            Assert.AreNotEqual(EncounterKind.None, k);
            Assert.AreEqual(p.retrySeconds, p.Timer, 1e-4f);
            Assert.AreEqual(0, p.Fired);
        }

        [Test]
        public void SameSeed_SameSequence()
        {
            var a = Drive(new EncounterPacer(77), 900f, Driving());
            var b = Drive(new EncounterPacer(77), 900f, Driving());
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }

        [Test]
        public void Rewards_FitTheMode()
        {
            var barrel = EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Barrel, false);
            Assert.Greater(barrel.buoyancy, 0f);
            Assert.LessOrEqual(barrel.buoyancy, 0.05f);
            Assert.AreEqual(0f, barrel.boostSeconds);
            var bottle = EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Bottle, false);
            Assert.IsTrue(bottle.bottleNote);
            Assert.AreEqual(0f, EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Buoy, false).buoyancy);
            // Abenteuer: flotsam is the survival resource now (islands are obstacles), and since the owner asked for
            // "mehr boost" a piece is also worth a real surge (>= 2 s, AdventureCompanionTests pins the numbers).
            foreach (ShipSystem.FlotsamKind k in System.Enum.GetValues(typeof(ShipSystem.FlotsamKind)))
            {
                var adv = EncounterPacer.RewardFor(k, true);
                if (k == ShipSystem.FlotsamKind.Buoy)
                {
                    Assert.AreEqual(0f, adv.buoyancy, "a buoy is not collected");
                    continue;
                }
                Assert.Greater(adv.buoyancy, 0.02f, k.ToString());
                Assert.LessOrEqual(adv.buoyancy, 0.1f, k.ToString());
                Assert.GreaterOrEqual(adv.boostSeconds, 2f, k.ToString());
                Assert.LessOrEqual(adv.boostSeconds, 3f, k.ToString());
            }
            Assert.Greater(EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Crate, true).buoyancy,
                EncounterPacer.RewardFor(ShipSystem.FlotsamKind.Driftwood, true).buoyancy, "a crate is worth more than a plank");
            for (uint s = 0; s < 50; s++) Assert.IsNotEmpty(EncounterPacer.BottleNote(s));
        }

        [Test]
        public void AheadPoint_LiesOnTheCourse()
        {
            Vector2 p = EncounterPacer.AheadPoint(new Vector2(10f, 5f), new Vector2(0f, 3f), 4f, 20f, 2f);
            Assert.AreEqual(12f, p.x, 1e-4f);
            Assert.AreEqual(29f, p.y, 1e-4f);
        }

        [Test]
        public void Collecting_RaisesTheEvent_OncePerPiece_ButNotForBuoys()
        {
            var go = new GameObject("EncounterTests_Ships");
            go.SetActive(false);
            try
            {
                var ships = go.AddComponent<ShipSystem>();
                int bottle = ships.SpawnRouteFlotsam(ShipSystem.FlotsamKind.Bottle, new Vector2(5000f, 5000f));
                int buoy = ships.SpawnFlotsamAt(ShipSystem.FlotsamKind.Buoy, new Vector2(5010f, 5000f));
                Assert.GreaterOrEqual(bottle, 0);
                Assert.IsTrue(ships.FlotsamIsRoute(bottle));
                Assert.AreEqual(1, ships.RouteFlotsamLeft);

                int raised = 0;
                ShipSystem.FlotsamKind got = ShipSystem.FlotsamKind.Driftwood;
                System.Action<ShipSystem.FlotsamKind, Vector3> h = (k, pos) => { raised++; got = k; };
                Encounters.FlotsamCollected += h;
                try
                {
                    int before = Encounters.CollectedTotal;
                    Assert.IsTrue(ships.CollectFlotsam(bottle));
                    Assert.IsFalse(ships.CollectFlotsam(bottle), "a piece is collected once");
                    Assert.IsFalse(ships.CollectFlotsam(buoy), "buoys stay");
                    Assert.AreEqual(1, raised);
                    Assert.AreEqual(ShipSystem.FlotsamKind.Bottle, got);
                    Assert.AreEqual(before + 1, Encounters.CollectedTotal);
                    Assert.IsTrue(ships.FlotsamCollecting(bottle));
                    Assert.AreEqual(0, ships.RouteFlotsamLeft);
                    Assert.AreEqual(1, ships.FlotsamCollectedTotal);
                }
                finally { Encounters.FlotsamCollected -= h; }
                Assert.IsTrue(ShipSystem.IsCollectible(ShipSystem.FlotsamKind.Crate));
                Assert.IsFalse(ShipSystem.IsCollectible(ShipSystem.FlotsamKind.Buoy));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
