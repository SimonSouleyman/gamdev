using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Two herds that meet interact in one of five patterns (IslandHerdSystem.Meetings.cs): each can be started, runs
    // to its end on flat and on sloped ground, keeps every animal on its own ground, and gives both herds back to
    // normal life apart from each other; night and a startle cancel it; the natural trigger fires and cools down.
    public class HerdMeetingTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        [SetUp]
        public void SetUp()
        {
            _night = 0f;
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => _night;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // Two herds side by side on a flat plateau (height 1.6: goats may stand there too) or on the flank of a cone.
        IslandHerdSystem Stage(LifeKind ka, int na, LifeKind kb, int nb, bool hill, out IIslandSurface surface, bool mature = false)
        {
            var go = new GameObject("Meet" + ka + kb);
            go.SetActive(false);
            if (hill)
            {
                var s = go.AddComponent<FakeHillSurface>();
                s.radius = 11f;
                s.peak = 3.2f;
                surface = s;
            }
            else
            {
                var s = go.AddComponent<FakeIslandSurface>();
                s.radius = 9f;
                s.height = 1.6f;
                surface = s;
            }
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = 1500 + (int)ka;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = 1500 + (int)ka * 3 + (int)kb;
            go.SetActive(true);
            _objects.Add(go);
            if (mature) life.Simulate(900f, 10f);
            herds.ClearHerds();
            Vector2 ca = hill ? new Vector2(-1.3f, 5.2f) : new Vector2(-1.4f, 0f), cb = hill ? new Vector2(1.3f, 5.2f) : new Vector2(1.4f, 0f);
            Assert.GreaterOrEqual(herds.AddHerd(ka, ca, na), 0, "no ground for " + ka);
            Assert.GreaterOrEqual(herds.AddHerd(kb, cb, nb), 0, "no ground for " + kb);
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;
            herds.meetRate = 0f;
            herds.playRate = 0f;
            herds.behaviourRate = 0f;
            Run(herds, 0.5f);
            herds.behaviourRate = 1f;
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static int Meeting(IslandHerdSystem herds)
        {
            int n = 0;
            for (int h = 0; h < herds.HerdCount; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                    if (IslandHerdSystem.IsMeeting(herds.ActivityOf(h, m))) n++;
            return n;
        }

        [Test]
        public void Greet_FrontAnimalsMeetNoseToNose() =>
            RunsToTheEnd(LifeKind.Sheep, 6, LifeKind.Goat, 5, MeetPattern.Greet, false);

        [Test]
        public void Tag_RunnersChaseRoundBothHerds() =>
            RunsToTheEnd(LifeKind.Hare, 7, LifeKind.Sheep, 6, MeetPattern.Tag, false);

        [Test]
        public void Shove_TwoLeadersPushHeadToHeadWithOnlookers() =>
            RunsToTheEnd(LifeKind.Goat, 6, LifeKind.Ox, 4, MeetPattern.Shove, false);

        [Test]
        public void Trek_BothHerdsWalkAsOneLineAndSplit() =>
            RunsToTheEnd(LifeKind.Zebra, 6, LifeKind.Sheep, 6, MeetPattern.Trek, false);

        [Test]
        public void RingDance_HerdsCircleThenLineUp() =>
            RunsToTheEnd(LifeKind.Reindeer, 6, LifeKind.Sheep, 5, MeetPattern.RingDance, false);

        [Test]
        public void AllPatterns_WorkOnSlopedGround()
        {
            for (int p = 0; p < 5; p++) RunsToTheEnd(LifeKind.Sheep, 6, LifeKind.Reindeer, 5, (MeetPattern)p, true);
        }

        void RunsToTheEnd(LifeKind ka, int na, LifeKind kb, int nb, MeetPattern pattern, bool hill)
        {
            var herds = Stage(ka, na, kb, nb, hill, out var surface);
            string what = pattern + " " + ka + "+" + kb + (hill ? " on the hill" : "");
            int sizeA = herds.HerdSize(0), sizeB = herds.HerdSize(1);
            Assert.IsTrue(herds.TryStartMeeting(0, 1, (int)pattern), what + ": could not start");
            Assert.AreEqual(1, herds.HerdMeetingPartner(0));
            Assert.AreEqual(0, herds.HerdMeetingPartner(1));
            Assert.IsFalse(herds.TryStartMeeting(0, 1, (int)pattern), what + ": a second meeting while the first runs");
            Assert.IsFalse(herds.HerdFree(1), what + ": the partner is busy");

            bool performed = false, finished = false;
            float closest = float.MaxValue, maxSpread = 0f, travelled = 0f, orbit = 0f;
            int maxShovers = 0, maxCheer = 0, maxTag = 0, maxTrek = 0;
            Vector2 mid0 = (herds.HerdCenter(0) + herds.HerdCenter(1)) * 0.5f;
            float angA0 = 0f;
            bool angSet = false;
            var last = new Dictionary<int, Vector2>();
            for (int i = 0; i < 1600 && !finished; i++)
            {
                herds.Step(0.05f);
                Assert.AreEqual(sizeA, herds.HerdSize(0), what + ": an animal of herd A was lost");
                Assert.AreEqual(sizeB, herds.HerdSize(1), what + ": an animal of herd B was lost");
                for (int h = 0; h < 2; h++)
                    for (int m = 0; m < herds.HerdSize(h); m++)
                    {
                        float ground = surface.SampleHeight(herds.AnimalPosition(h, m));
                        Assert.Greater(ground, 0.14f, what + ": herd " + h + " member " + m + " left the land");
                    }
                int phase = herds.HerdMeetingPhase(0);
                if (phase == 2)
                {
                    Assert.AreEqual((int)pattern, herds.HerdMeetingPattern(0), what + ": fell back to another pattern");
                    performed = true;
                    int shovers = 0, cheer = 0, tag = 0, trek = 0;
                    Vector2 lo = new Vector2(float.MaxValue, float.MaxValue), hi = -lo;
                    for (int h = 0; h < 2; h++)
                        for (int m = 0; m < herds.HerdSize(h); m++)
                        {
                            var act = herds.ActivityOf(h, m);
                            Vector2 p = herds.AnimalPosition(h, m);
                            if (act == AnimalActivity.Shove) shovers++;
                            if (act == AnimalActivity.Cheer) cheer++;
                            if (act == AnimalActivity.Trek) { trek++; lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
                            if (act == AnimalActivity.Tag)
                            {
                                tag++;
                                int key = h * 100 + m;
                                if (last.TryGetValue(key, out var q)) travelled += Vector2.Distance(q, p);
                                last[key] = p;
                            }
                        }
                    maxShovers = Mathf.Max(maxShovers, shovers);
                    maxCheer = Mathf.Max(maxCheer, cheer);
                    maxTag = Mathf.Max(maxTag, tag);
                    maxTrek = Mathf.Max(maxTrek, trek);
                    if (trek > 0) maxSpread = Mathf.Max(maxSpread, (hi - lo).magnitude);
                    if (pattern == MeetPattern.Greet || pattern == MeetPattern.Shove)
                    {
                        // The two front animals: the pair of members of different herds that stand closest.
                        for (int m = 0; m < herds.HerdSize(0); m++)
                            for (int k = 0; k < herds.HerdSize(1); k++)
                                closest = Mathf.Min(closest, Vector2.Distance(herds.AnimalPosition(0, m), herds.AnimalPosition(1, k)));
                    }
                    if (pattern == MeetPattern.RingDance)
                    {
                        Vector2 r = herds.HerdCenter(0) - mid0;
                        float ang = Mathf.Atan2(r.y, r.x) * Mathf.Rad2Deg;
                        if (!angSet) { angA0 = ang; angSet = true; }
                        orbit = Mathf.Max(orbit, Mathf.Abs(Mathf.DeltaAngle(angA0, ang)));
                    }
                }
                if (performed && phase < 0) finished = true;
            }
            Assert.IsTrue(performed, what + ": never performed");
            Assert.IsTrue(finished, what + ": never ended");
            Assert.AreEqual(1, herds.MeetingsCompletedOf(pattern), what + ": not counted as completed");
            Assert.AreEqual(0, Meeting(herds), what + ": meeting activity left over");
            Assert.AreEqual(0, herds.ActiveMeetings);
            for (int h = 0; h < 2; h++)
                for (int m = 0; m < herds.HerdSize(h); m++)
                {
                    Assert.AreEqual(0f, herds.AnimalBakedPitch(h, m), what + ": pitch left over");
                    Assert.AreEqual(0f, herds.AnimalBakedRoll(h, m), what + ": roll left over");
                    Assert.AreEqual(0f, herds.AnimalLift(h, m), what + ": lift left over");
                }

            float body = Mathf.Max(herds.BodyLength(0), herds.BodyLength(1));
            switch (pattern)
            {
                case MeetPattern.Greet:
                    Assert.Less(closest, body * 1.2f, what + ": the front animals never met nose to nose");
                    break;
                case MeetPattern.Tag:
                    Assert.GreaterOrEqual(maxTag, 2, what + ": fewer than two runners");
                    Assert.Greater(travelled, 4f, what + ": the runners hardly ran");
                    break;
                case MeetPattern.Shove:
                    Assert.AreEqual(2, maxShovers, what + ": not exactly two shoving leaders");
                    Assert.GreaterOrEqual(maxCheer, sizeA + sizeB - 2, what + ": the others did not watch");
                    Assert.Less(closest, body * 1.2f, what + ": the leaders never touched heads");
                    break;
                case MeetPattern.Trek:
                    Assert.AreEqual(sizeA + sizeB, maxTrek, what + ": not everyone walked in the line");
                    Assert.Greater(maxSpread, 1.2f, what + ": the line never stretched out");
                    break;
                case MeetPattern.RingDance:
                    Assert.Greater(orbit, 150f, what + ": the herds did not circle each other");
                    break;
            }

            // Back to normal life: both walk apart to herdSpacing at least (later wanders are their own business).
            float apart = 0f;
            for (int i = 0; i < 200; i++)
            {
                herds.Step(0.05f);
                apart = Mathf.Max(apart, Vector2.Distance(herds.HerdCenter(0), herds.HerdCenter(1)));
            }
            Assert.GreaterOrEqual(apart, herds.herdSpacing, what + ": the herds did not separate");
            Run(herds, 20f);
            Assert.AreEqual(0, Meeting(herds));
        }

        [Test]
        public void Tag_TheYoungRunFirst()
        {
            var herds = Stage(LifeKind.Hare, 6, LifeKind.Hare, 6, false, out _, mature: true);
            herds.growthInterval = 0.5f;
            herds.growthChance = 1f;
            for (int i = 0; i < 400 && (herds.HerdYoungCount(0) == 0 || herds.HerdYoungCount(1) == 0); i++) herds.Step(0.05f);
            Assert.Greater(herds.HerdYoungCount(0), 0);
            Assert.Greater(herds.HerdYoungCount(1), 0);
            herds.growthInterval = 1e6f;
            Assert.IsTrue(herds.TryStartMeeting(0, 1, (int)MeetPattern.Tag));
            bool youngRan = false, adultRan = false;
            for (int i = 0; i < 600 && herds.HerdMeetingPhase(0) >= 0; i++)
            {
                herds.Step(0.05f);
                for (int h = 0; h < 2; h++)
                    for (int m = 0; m < herds.HerdSize(h); m++)
                    {
                        if (herds.ActivityOf(h, m) != AnimalActivity.Tag) continue;
                        if (herds.IsYoung(h, m)) youngRan = true;
                        else adultRan = true;
                    }
            }
            Assert.IsTrue(youngRan, "the young did not run");
            Assert.IsFalse(adultRan, "adults ran although young were there");
        }

        [Test]
        public void Night_And_Startle_CancelAMeetingForBothHerds()
        {
            var herds = Stage(LifeKind.Sheep, 6, LifeKind.Zebra, 6, false, out _);
            Assert.IsTrue(herds.TryStartMeeting(0, 1, (int)MeetPattern.RingDance));
            for (int i = 0; i < 400 && herds.HerdMeetingPhase(0) != 2; i++) herds.Step(0.05f);
            Assert.AreEqual(2, herds.HerdMeetingPhase(0));
            Assert.Greater(Meeting(herds), 0);
            herds.Startle(new Vector2(0f, -3f), 2f);
            herds.Step(0.05f);
            Assert.AreEqual(-1, herds.HerdMeetingPhase(0));
            Assert.AreEqual(-1, herds.HerdMeetingPhase(1));
            Assert.AreEqual(0, Meeting(herds));
            Assert.AreEqual(1, herds.MeetingsAborted);

            Run(herds, 10f);
            Assert.IsTrue(herds.TryStartMeeting(0, 1, (int)MeetPattern.Greet), "a forced meeting ignores the cooldown");
            for (int i = 0; i < 400 && herds.HerdMeetingPhase(0) != 2; i++) herds.Step(0.05f);
            _night = 1f;
            herds.Step(0.05f);
            Assert.AreEqual(-1, herds.HerdMeetingPhase(0), "night ends it");
            Assert.AreEqual(0, Meeting(herds));
            Assert.IsFalse(herds.TryStartMeeting(0, 1, 0), "sleeping herds do not meet");
            Run(herds, 60f);
            Assert.AreEqual(0, Meeting(herds));
        }

        [Test]
        public void Natural_HerdsMeetByThemselvesAndCoolDown()
        {
            var herds = Stage(LifeKind.Sheep, 6, LifeKind.Goat, 5, false, out _);
            herds.meetRate = 0.1f;
            // Herds settle for 30 s after they came into being before they meet anyone.
            Run(herds, 20f);
            Assert.AreEqual(0, herds.MeetingsStarted, "no meeting while the herds settle");
            float t = 0f;
            while (herds.MeetingsStarted == 0 && t < 120f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(1, herds.MeetingsStarted, "the neighbours never met");
            int first = herds.MeetingsStarted;
            while (herds.HerdMeetingPhase(0) >= 0 && t < 200f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(1, herds.MeetingsCompleted + herds.MeetingsAborted);
            // The pair cools down for at least meetCooldown.x seconds.
            Run(herds, herds.meetCooldown.x - 5f);
            Assert.AreEqual(first, herds.MeetingsStarted, "met again during the cooldown");
        }

        [Test]
        public void Meetings_DoNotAllocateWhileTheyRun()
        {
            var herds = Stage(LifeKind.Sheep, 8, LifeKind.Zebra, 7, false, out _);
            for (int p = 0; p < 5; p++)
            {
                herds.ClearHerds();
                herds.AddHerd(LifeKind.Sheep, new Vector2(-1.4f, 0f), 8);
                herds.AddHerd(LifeKind.Zebra, new Vector2(1.4f, 0f), 7);
                Run(herds, 0.5f);
                Assert.IsTrue(herds.TryStartMeeting(0, 1, p), ((MeetPattern)p) + " could not start");
                Run(herds, 0.2f);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600 && herds.HerdMeetingPhase(0) >= 0; i++) herds.Step(0.05f);
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(0L, bytes, ((MeetPattern)p) + " allocated while it ran");
            }
        }

        [Test]
        public void Moods_And_Moment()
        {
            for (var act = AnimalActivity.Greet; act <= AnimalActivity.RingDance; act++)
            {
                Assert.IsTrue(IslandHerdSystem.IsMeeting(act));
                Assert.IsNotEmpty(IslandHerdSystem.MoodText(AnimalState.Look, act), act.ToString());
                Assert.IsFalse(IslandHerdSystem.IsSignature(act));
            }
            Assert.IsFalse(IslandHerdSystem.IsMeeting(AnimalActivity.Necking));
            Assert.Greater(Moments.SubjectSize(MomentKind.Meeting), 1f);

            var herds = Stage(LifeKind.Sheep, 6, LifeKind.Sheep, 6, false, out _);
            int seen = 0;
            System.Action<MomentKind, Vector3> on = (k, w) => { if (k == MomentKind.Meeting) seen++; };
            Moments.Seen += on;
            try { Assert.IsTrue(herds.TryStartMeeting(0)); }
            finally { Moments.Seen -= on; }
            Assert.AreEqual(1, seen);
        }
    }
}
