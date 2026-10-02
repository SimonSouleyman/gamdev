using System;
using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class LifeDirectorTests
    {
        static LifePacer OnlyKinds(int seed, params LifeNudge[] kinds)
        {
            var p = new LifePacer(seed);
            for (int i = 0; i < LifePacer.Count; i++) p.weights[i] = 0f;
            foreach (var k in kinds) p.weights[(int)k] = 1f;
            return p;
        }

        static bool Contains(LifeNudge[] buf, int n, LifeNudge k)
        {
            for (int i = 0; i < n; i++) if (buf[i] == k) return true;
            return false;
        }

        // ------------------------------------------------------------ pacing

        [Test]
        public void Pacer_AsksForANudgeOnlyAfterTheJitteredGap()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var p = new LifePacer(seed);
                float t = 0f;
                while (!p.Tick(0.05f) && t < 30f) t += 0.05f;
                t += 0.05f;
                Assert.GreaterOrEqual(t, p.nudgeMin - 1e-3f, "seed " + seed);
                Assert.LessOrEqual(t, p.nudgeMax + 0.06f, "seed " + seed);
            }
        }

        [Test]
        public void Pacer_JitterStaysInItsBoundsAndVaries()
        {
            var p = new LifePacer(7);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 300; i++)
            {
                p.Tick(0.5f);
                p.Notice();
                Assert.GreaterOrEqual(p.Due, p.nudgeMin);
                Assert.LessOrEqual(p.Due, p.nudgeMax);
                lo = Mathf.Min(lo, p.Due);
                hi = Mathf.Max(hi, p.Due);
            }
            Assert.Greater(hi - lo, 1.5f, "not a metronome");
            Assert.Less(p.nudgeMax + p.retrySeconds, 10f, "the first retry still lands inside the 10 s target");
        }

        [Test]
        public void Pacer_NoticedMomentsRestartTheWaitAndTrackTheGaps()
        {
            var p = new LifePacer(3);
            p.Tick(3f);
            p.Notice();
            Assert.AreEqual(3f, p.LastGap, 1e-4f);
            Assert.AreEqual(0f, p.SinceNoticed, 1e-4f);
            p.Tick(5f);
            Assert.IsFalse(p.Tick(0f), "5 s is below the shortest nudge");
            p.Notice();
            p.Tick(2f);
            p.Notice();
            Assert.AreEqual(5f, p.LongestGap, 1e-4f);
            Assert.AreEqual(2f, p.LastGap, 1e-4f);
            Assert.AreEqual(3, p.NoticedCount);
        }

        [Test]
        public void Pacer_RearmStartsAFreshWaitWithoutCountingAGap()
        {
            var p = new LifePacer(4);
            Assert.IsTrue(p.Tick(40f));
            p.Rearm();
            Assert.AreEqual(0f, p.SinceNoticed, 1e-4f);
            Assert.AreEqual(0f, p.LongestGap, 1e-4f);
            Assert.IsFalse(p.Tick(1f));
        }

        [Test]
        public void Pacer_FailRetriesShortlyAndConfirmWaitsForTheReport()
        {
            var p = new LifePacer(5);
            Assert.IsTrue(p.Tick(p.nudgeMax + 0.01f));
            p.Fail();
            Assert.IsFalse(p.Tick(p.retrySeconds * 0.5f));
            Assert.IsTrue(p.Tick(p.retrySeconds * 0.6f));
            Assert.AreEqual(1, p.Failed);

            p.Confirm(LifeNudge.Fish);
            Assert.IsFalse(p.Tick(p.graceSeconds * 0.8f), "the triggered moment gets time to be reported");
            Assert.IsTrue(p.Tick(p.graceSeconds * 0.3f), "nothing was noticed: try again");
            Assert.AreEqual(LifeNudge.Fish, p.Last);
            Assert.AreEqual(1, p.Fired);
        }

        // ------------------------------------------------------------ choice

        [Test]
        public void Pacer_OrderIsAPermutationOfTheAvailableKinds()
        {
            var p = new LifePacer(11);
            var buf = new LifeNudge[LifePacer.Count];
            int n = p.Order(buf);
            Assert.AreEqual(LifePacer.Count, n);
            var seen = new HashSet<LifeNudge>();
            for (int i = 0; i < n; i++) Assert.IsTrue(seen.Add(buf[i]), "duplicate " + buf[i]);

            p.weights[(int)LifeNudge.Whale] = 0f;
            n = p.Order(buf);
            Assert.AreEqual(LifePacer.Count - 1, n);
            Assert.IsFalse(Contains(buf, n, LifeNudge.Whale), "zero weight is never tried");
        }

        [Test]
        public void Pacer_NeverTheSameKindTwiceInARow()
        {
            var p = new LifePacer(21);
            var buf = new LifeNudge[LifePacer.Count];
            for (int round = 0; round < 200; round++)
            {
                p.Tick(200f);
                p.Notice();
                int n = p.Order(buf);
                Assert.Greater(n, 0);
                Assert.IsFalse(Contains(buf, n, p.Last), "round " + round);
                // Confirm a random entry so every kind gets to be "last" some time.
                p.Confirm(buf[round % n]);
            }
        }

        [Test]
        public void Pacer_CooldownsHoldAKindBack()
        {
            var p = OnlyKinds(2, LifeNudge.HerdSignature, LifeNudge.Fish);
            p.cooldowns[(int)LifeNudge.HerdSignature] = 20f;
            p.cooldowns[(int)LifeNudge.Fish] = 0f;
            var buf = new LifeNudge[LifePacer.Count];
            p.Confirm(LifeNudge.HerdSignature);
            p.Tick(1f);
            p.Confirm(LifeNudge.Fish);
            p.Tick(4f);
            p.Notice();
            Assert.AreEqual(0, p.Order(buf), "signature cooling down, fish just ran");
            p.Tick(16f);
            p.Notice();
            int n = p.Order(buf);
            Assert.AreEqual(1, n);
            Assert.AreEqual(LifeNudge.HerdSignature, buf[0]);
        }

        [Test]
        public void Pacer_ALongGapAllowsARepeatAfterAShortCooldown()
        {
            var p = OnlyKinds(9, LifeNudge.HerdPlay);
            p.cooldowns[(int)LifeNudge.HerdPlay] = 30f;
            var buf = new LifeNudge[LifePacer.Count];
            p.Confirm(LifeNudge.HerdPlay);
            p.Tick(p.lastResortCooldown + 1f);
            Assert.IsFalse(p.LastResort);
            Assert.AreEqual(0, p.Order(buf), "no repeat while the gap is still short");
            p.Tick(p.lastResortGap);
            Assert.IsTrue(p.LastResort);
            Assert.AreEqual(1, p.Order(buf));
            Assert.AreEqual(LifeNudge.HerdPlay, buf[0]);

            p.Confirm(LifeNudge.HerdPlay);
            p.Tick(p.lastResortCooldown * 0.5f);
            Assert.AreEqual(0, p.Order(buf), "even the last resort keeps a short cooldown");
        }

        // The night watch: a sleeping herd in a close-up, only a firefly wave and a sleeper stirring fit into the view.
        [TestCase(true)]
        [TestCase(false)]
        public void Pacer_NightWatchKeepsTheGapUnderTenSeconds(bool withFireflies)
        {
            var p = withFireflies ? OnlyKinds(5, LifeNudge.Fireflies, LifeNudge.SleepStir) : OnlyKinds(5, LifeNudge.SleepStir);
            var buf = new LifeNudge[LifePacer.Count];
            for (int f = 0; f < 3000; f++)
            {
                if (!p.Tick(0.1f)) continue;
                int n = p.Order(buf);
                if (n == 0) { p.Fail(); continue; }
                p.Confirm(buf[0]);
                p.Notice();
            }
            Assert.Less(p.LongestGap, 10f);
            Assert.Greater(p.Fired, 28);
        }

        [Test]
        public void Pacer_WeightsSteerTheFirstChoice()
        {
            var p = OnlyKinds(13, LifeNudge.HerdSignature, LifeNudge.Fish);
            p.weights[(int)LifeNudge.HerdSignature] = 9f;
            p.weights[(int)LifeNudge.Fish] = 1f;
            var buf = new LifeNudge[LifePacer.Count];
            int first = 0;
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(2, p.Order(buf));
                if (buf[0] == LifeNudge.HerdSignature) first++;
            }
            Assert.That(first, Is.InRange(840, 960));
        }

        [Test]
        public void Pacer_SameSeedSameChoices()
        {
            var a = new LifePacer(77);
            var b = new LifePacer(77);
            var ba = new LifeNudge[LifePacer.Count];
            var bb = new LifeNudge[LifePacer.Count];
            Assert.AreEqual(a.Due, b.Due);
            for (int r = 0; r < 20; r++)
            {
                int na = a.Order(ba), nb = b.Order(bb);
                Assert.AreEqual(na, nb);
                for (int i = 0; i < na; i++) Assert.AreEqual(ba[i], bb[i]);
            }
        }

        [Test]
        public void Pacer_CountMatchesTheNudgeKinds()
        {
            Assert.AreEqual(LifePacer.Count, Enum.GetValues(typeof(LifeNudge)).Length - 1);
            var p = new LifePacer(1);
            for (int i = 0; i < LifePacer.Count; i++)
            {
                Assert.Greater(p.weights[i], 0f, ((LifeNudge)i).ToString());
                Assert.GreaterOrEqual(p.cooldowns[i], 0f, ((LifeNudge)i).ToString());
            }
        }

        // ------------------------------------------------------------ what gets started / noticed

        [Test]
        public void SpeciesMove_IsARoutineTheSpeciesReallyHas()
        {
            Assert.AreEqual(AnimalActivity.Slide, LifeDirector.SpeciesMove(LifeKind.Penguin));
            Assert.AreEqual(AnimalActivity.Stampede, LifeDirector.SpeciesMove(LifeKind.Zebra));
            Assert.AreEqual(AnimalActivity.None, LifeDirector.SpeciesMove(LifeKind.Hare));
            foreach (LifeKind kind in Enum.GetValues(typeof(LifeKind)))
            {
                var move = LifeDirector.SpeciesMove(kind);
                if (move == AnimalActivity.None) continue;
                Assert.IsTrue(IslandHerdSystem.IsHerdSpecies(kind), kind.ToString());
                Assert.IsFalse(IslandHerdSystem.IsSignature(move), kind.ToString());
            }
            Assert.IsFalse(LifeDirector.ReportsItself(AnimalActivity.Stampede));
            Assert.IsTrue(LifeDirector.ReportsItself(AnimalActivity.Slide));
        }

        [Test]
        public void Night_SleepingHerdsAreNeverNudgedAndTheGlowAndSeaTakeOver()
        {
            for (int i = 0; i < LifePacer.Count; i++)
            {
                var k = (LifeNudge)i;
                float night = LifeDirector.WeightFactor(k, true, true, 2f, 2f, 1.5f);
                float day = LifeDirector.WeightFactor(k, false, false, 2f, 2f, 1.5f);
                if (LifeDirector.IsHerdNudge(k))
                {
                    Assert.AreEqual(0f, night, k + " must not wake a herd");
                    Assert.AreEqual(2f, LifeDirector.WeightFactor(k, false, true, 2f, 2f, 1.5f), k + " watched by day");
                }
                if (k == LifeNudge.Fireflies)
                {
                    Assert.AreEqual(0f, day);
                    Assert.AreEqual(2f, night);
                }
                if (k == LifeNudge.Critter) Assert.AreEqual(0f, night);
                if (k == LifeNudge.SleepStir)
                {
                    Assert.AreEqual(0f, day, "a stir only happens in sleep");
                    Assert.AreEqual(2f, night, "the watched herd's sleepers first");
                }
                if (k == LifeNudge.Dolphins || k == LifeNudge.Whale || k == LifeNudge.SeaShow || k == LifeNudge.Fish)
                    Assert.Greater(night, day);
            }
            // Something is always left to try at night.
            int awake = 0;
            for (int i = 0; i < LifePacer.Count; i++) if (LifeDirector.WeightFactor((LifeNudge)i, true, true, 2f, 2f, 1.5f) > 0f) awake++;
            Assert.GreaterOrEqual(awake, 4);
        }

        [Test]
        public void FarView_OnlyBigSubjectsStayInPlay()
        {
            for (int i = 0; i < LifePacer.Count; i++)
            {
                var k = (LifeNudge)i;
                float near = LifeDirector.WeightFactor(k, false, false, 2f, 2f, 1.5f, false, 2f);
                float far = LifeDirector.WeightFactor(k, false, false, 2f, 2f, 1.5f, true, 2f);
                Assert.AreEqual(LifeDirector.WeightFactor(k, false, false, 2f, 2f, 1.5f), near, k + ": near view unchanged");
                if (LifeDirector.IsBigNudge(k)) Assert.AreEqual(near * 2f, far, 1e-5f, k.ToString());
                else Assert.AreEqual(0f, far, k + " is a few pixels in the far view");
            }
            Assert.Greater(LifeDirector.WeightFactor(LifeNudge.Flock, false, false, 2f, 2f, 1.5f, true, 2f), 0f, "murmurs by day");
            Assert.Greater(LifeDirector.WeightFactor(LifeNudge.Fireflies, true, false, 2f, 2f, 1.5f, true, 2f), 0f, "firefly waves by night");
            Assert.Greater(LifeDirector.WeightFactor(LifeNudge.Whale, true, false, 2f, 2f, 1.5f, true, 2f), 0f);
        }

        [Test]
        public void FarView_SeaShowsAreFilteredBySize()
        {
            // 12.5 px per unit (a 1000+ island at the default zoom) and the 22 px far pick: 1.76 u at least.
            float min = LifeDirector.MinSubjectSize(22f, 12.5f);
            Assert.AreEqual(1.76f, min, 1e-3f);
            Assert.GreaterOrEqual(SeaShow.SubjectSize(SeaShowKind.WhaleBlow), min);
            Assert.GreaterOrEqual(SeaShow.SubjectSize(SeaShowKind.DolphinPass), min);
            Assert.GreaterOrEqual(SeaShow.SubjectSize(SeaShowKind.FlyingFish), min);
            Assert.Less(SeaShow.SubjectSize(SeaShowKind.Seal), min);
            Assert.Less(SeaShow.SubjectSize(SeaShowKind.Turtle), min);
            Assert.Less(SeaShow.SubjectSize(SeaShowKind.FishJump), min);
            // Close up everything fits.
            float close = LifeDirector.MinSubjectSize(26f, 150f);
            for (int i = 0; i < SeaShow.KindCount; i++) Assert.GreaterOrEqual(SeaShow.SubjectSize((SeaShowKind)i), close, ((SeaShowKind)i).ToString());
            // What a far show reports is really noticed there (20 px).
            Assert.GreaterOrEqual(SeaShow.SubjectSize(SeaShowKind.DolphinPass) * 12.5f, Moments.NoticePx);
        }

        // Far view: murmurs, sea shows, dolphins, whales by day; firefly waves (10 s) and the sea by night.
        [TestCase(false)]
        [TestCase(true)]
        public void Pacer_FarViewKeepsTheGapUnderTenSeconds(bool night)
        {
            var p = night ? OnlyKinds(8, LifeNudge.Fireflies, LifeNudge.SeaShow) : OnlyKinds(8, LifeNudge.Flock, LifeNudge.SeaShow, LifeNudge.Dolphins);
            p.cooldowns[(int)LifeNudge.Fireflies] = 10f;
            var buf = new LifeNudge[LifePacer.Count];
            for (int f = 0; f < 3000; f++)
            {
                if (!p.Tick(0.1f)) continue;
                int n = p.Order(buf);
                if (n == 0) { p.Fail(); continue; }
                p.Confirm(buf[0]);
                p.Notice();
            }
            Assert.Less(p.LongestGap, 10f);
        }

        [Test]
        public void NoticedRule_MatchesTheRecorder()
        {
            // 1920 px tall portrait, 60 degrees: one unit at 10 u covers 1920 / (20 * tan 30) = 166 px.
            float ppu = Moments.PxPerUnit(10f, 1920f, 60f);
            Assert.AreEqual(166.28f, ppu, 0.05f);
            Assert.GreaterOrEqual(Moments.SubjectSize(MomentKind.Butterflies) * ppu, Moments.NoticePx);
            Assert.Less(Moments.SubjectSize(MomentKind.Butterflies) * Moments.PxPerUnit(100f, 1920f, 60f), Moments.NoticePx);
            Assert.IsTrue(Moments.OnScreen(new Vector3(0.5f, 0.5f, 3f), 0.02f));
            Assert.IsFalse(Moments.OnScreen(new Vector3(0.5f, 0.5f, -3f), 0.02f), "behind the camera");
            Assert.IsFalse(Moments.OnScreen(new Vector3(0.01f, 0.5f, 3f), 0.02f));
            Assert.IsFalse(Moments.OnScreen(new Vector3(0.95f, 0.5f, 3f), 0.1f), "the director keeps a wider margin");
            foreach (MomentKind k in Enum.GetValues(typeof(MomentKind)))
                Assert.Greater(Moments.SubjectSize(k), 0f, k.ToString());
            Assert.Greater(Moments.SubjectSize(MomentKind.FireflyWave), Moments.SubjectSize(MomentKind.CritterMove));
        }
    

        // ------------------------------------------------------------ moment toasts (v0.6.8)

        [Test]
        public void MomentToast_TextsNameTheSpecies()
        {
            Assert.AreEqual("Die Zebras ziehen zur neuen Weide", LifeDirector.MomentToastText(HerdGoal.Migrate, "Zebras", null));
            Assert.AreEqual("Alle treffen sich am Wasser", LifeDirector.MomentToastText(HerdGoal.Gather, "Zebras", null));
            Assert.AreEqual("Zebras und Giraffen ziehen zusammen", LifeDirector.MomentToastText(HerdGoal.Mingle, "Zebras", "Giraffen"));
            Assert.AreEqual("Die Schafe kuscheln gegen die Kälte", LifeDirector.MomentToastText(HerdGoal.Huddle, "Schafe", null));
            Assert.AreEqual("Paarungszeit bei den Hasen", LifeDirector.MomentToastText(HerdGoal.Court, "Hasen", null));
            Assert.AreEqual("Paarungszeit bei den Schafen", LifeDirector.MomentToastText(HerdGoal.Court, "Schafe", null));
            Assert.AreEqual("Die Zebras suchen Schutz vor dem Regen", LifeDirector.MomentToastText(HerdGoal.Shelter, "Zebras", null));
        }

        [Test]
        public void MomentToast_NeverEmptyForAToastGoal()
        {
            var goals = new[] { HerdGoal.Migrate, HerdGoal.Gather, HerdGoal.Mingle, HerdGoal.Huddle, HerdGoal.Court, HerdGoal.Shelter };
            foreach (var g in goals)
            {
                Assert.IsNotEmpty(LifeDirector.MomentToastText(g, null, null), g.ToString());
                Assert.IsNotEmpty(LifeDirector.MomentToastText(g, "", ""), g.ToString());
                Assert.AreEqual(g, LifeDirector.ToastGoalOfName(g.ToString()), "moment kinds are matched by name");
            }
            Assert.AreEqual("Die Herde sucht Schutz vor dem Regen", LifeDirector.MomentToastText(HerdGoal.Shelter, null, null));
            Assert.AreEqual("Zwei Herden ziehen zusammen", LifeDirector.MomentToastText(HerdGoal.Mingle, "Zebras", null));
            Assert.AreEqual("Zwei Herden ziehen zusammen", LifeDirector.MomentToastText(HerdGoal.Mingle, "Zebras", "Zebras"));
            Assert.AreEqual("", LifeDirector.MomentToastText(HerdGoal.Graze, "Zebras", null), "grazing is no news");
            Assert.AreEqual("", LifeDirector.MomentToastText(HerdGoal.None, "Zebras", null));
        }

        [Test]
        public void MomentToast_OnlyTheEnvironmentMomentsBecomeToasts()
        {
            Assert.AreEqual(HerdGoal.None, LifeDirector.ToastGoalOf(MomentKind.Signature));
            Assert.AreEqual(HerdGoal.None, LifeDirector.ToastGoalOf(MomentKind.Meeting));
            Assert.AreEqual(HerdGoal.None, LifeDirector.ToastGoalOf((MomentKind)(-1)));
            Assert.AreEqual(HerdGoal.None, LifeDirector.ToastGoalOf((MomentKind)10000));
            Assert.AreEqual(HerdGoal.None, LifeDirector.ToastGoalOfName("Graze"));
            foreach (MomentKind k in Enum.GetValues(typeof(MomentKind)))
                Assert.AreEqual(LifeDirector.ToastGoalOfName(k.ToString()), LifeDirector.ToastGoalOf(k), k.ToString());
        }

        [Test]
        public void MomentToast_DativePlural()
        {
            Assert.AreEqual("Hasen", LifeDirector.DativePlural("Hasen"));
            Assert.AreEqual("Schafen", LifeDirector.DativePlural("Schafe"));
            Assert.AreEqual("Rentieren", LifeDirector.DativePlural("Rentiere"));
            Assert.AreEqual("Zebras", LifeDirector.DativePlural("Zebras"));
            Assert.AreEqual("Erdmännchen", LifeDirector.DativePlural("Erdmännchen"));
            Assert.AreEqual("Polarfüchsen", LifeDirector.DativePlural("Polarfüchse"));
            Assert.AreEqual("", LifeDirector.DativePlural(null));
        }

        [Test]
        public void MomentToast_GateKeepsItNewsNotATicker()
        {
            var gate = new MomentToastGate { gapSeconds = 40f, cooldownSeconds = 150f };
            Assert.IsFalse(gate.Allow(HerdGoal.None, 0f));
            Assert.IsTrue(gate.Allow(HerdGoal.Migrate, 0f));
            gate.Note(HerdGoal.Migrate, 0f);
            Assert.IsFalse(gate.Allow(HerdGoal.Shelter, 39f), "the gap holds for every goal");
            Assert.IsTrue(gate.Allow(HerdGoal.Shelter, 40f));
            Assert.IsFalse(gate.Allow(HerdGoal.Migrate, 149f), "the same news waits for its cooldown");
            Assert.IsTrue(gate.Allow(HerdGoal.Migrate, 150f));
            Assert.IsFalse(gate.Allow((HerdGoal)999, 1000f));
            Assert.AreEqual(1, gate.Shown);
            gate.Reset();
            Assert.IsTrue(gate.Allow(HerdGoal.Migrate, 0f));
        }
    }
}
