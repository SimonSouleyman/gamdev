using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Relations between species (IslandHerdSystem.Relations.cs): the matrix is symmetric with the agreed pairs; a close
    // zebra and giraffe herd mingle and part again; hares post a sentry while a fox herd is near and take it in when it
    // has gone; reindeer and penguins keep their distance calmly; at dusk herds meet at the water (also an avoiding pair);
    // sheep trail the goats; nothing starts at night.
    public class HerdRelationsTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

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

        // Herds on a wide flat island (beach > 0: a sloping shore at the rim) with every other pattern switched off.
        IslandHerdSystem Stage(float radius, float height, float beach, params (LifeKind kind, Vector2 at, int size)[] herdsWanted)
        {
            var go = new GameObject("Relations");
            go.SetActive(false);
            var s = go.AddComponent<FakeIslandSurface>();
            s.radius = radius;
            s.height = height;
            s.beach = beach;
            go.AddComponent<IslandLifeSystem>().seed = 2100;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = 2101;
            go.SetActive(true);
            _objects.Add(go);
            herds.ClearHerds();
            foreach (var w in herdsWanted) Assert.GreaterOrEqual(herds.AddHerd(w.kind, w.at, w.size), 0, "no ground for " + w.kind);
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;
            herds.meetRate = 0f;
            herds.playRate = 0f;
            herds.mingleRate = herds.followChance = herds.guardChance = herds.gatherChance = 0f;
            Run(herds, 0.5f);
            Settle(herds, 20f);
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static System.Collections.IList HerdList(IslandHerdSystem herds) =>
            (System.Collections.IList)typeof(IslandHerdSystem).GetField("_herds", Flags).GetValue(herds);

        // Skips the herds' settling time and lets them stand where they are for `hold` seconds.
        static void Settle(IslandHerdSystem herds, float hold)
        {
            foreach (var h in HerdList(herds))
            {
                var t = h.GetType();
                t.GetField("settleT", Flags).SetValue(h, 0f);
                t.GetField("wait", Flags).SetValue(h, hold);
                t.GetField("target", Flags).SetValue(h, t.GetField("center", Flags).GetValue(h));
            }
        }

        static void SetField(IslandHerdSystem herds, int index, string field, object value)
        {
            var h = HerdList(herds)[index];
            h.GetType().GetField(field, Flags).SetValue(h, value);
        }

        // Puts a whole herd somewhere else at once and keeps it standing there.
        static void Teleport(IslandHerdSystem herds, int index, Vector2 to, float hold)
        {
            var h = HerdList(herds)[index];
            var t = h.GetType();
            var from = (Vector2)t.GetField("center", Flags).GetValue(h);
            t.GetField("center", Flags).SetValue(h, to);
            t.GetField("target", Flags).SetValue(h, to);
            t.GetField("wait", Flags).SetValue(h, hold);
            foreach (var a in (System.Collections.IList)t.GetField("members", Flags).GetValue(h))
            {
                var pf = a.GetType().GetField("pos", Flags);
                pf.SetValue(a, (Vector2)pf.GetValue(a) - from + to);
            }
        }

        static string Dump(IslandHerdSystem herds, int index)
        {
            var h = HerdList(herds)[index];
            var t = h.GetType();
            return t.GetField("relMode", Flags).GetValue(h) + " " + t.GetField("errand", Flags).GetValue(h) + " c=" + t.GetField("center", Flags).GetValue(h)
                + " wait=" + t.GetField("wait", Flags).GetValue(h);
        }

        static int CountAct(IslandHerdSystem herds, int h, AnimalActivity act)
        {
            int n = 0;
            for (int m = 0; m < herds.HerdSize(h); m++) if (herds.ActivityOf(h, m) == act) n++;
            return n;
        }

        static Vector2 MemberCentroid(IslandHerdSystem herds, int h)
        {
            Vector2 c = Vector2.zero;
            for (int m = 0; m < herds.HerdSize(h); m++) c += herds.AnimalPosition(h, m);
            return c / Mathf.Max(1, herds.HerdSize(h));
        }

        [Test]
        public void Matrix_IsSymmetric_WithTheAgreedPairs()
        {
            foreach (var a in SpeciesPool.HerdKinds)
                foreach (var b in SpeciesPool.HerdKinds)
                {
                    Assert.AreEqual(IslandHerdSystem.RelationOf(a, b), IslandHerdSystem.RelationOf(b, a), a + "/" + b);
                    if (a == b) Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf(a, b), "own species " + a);
                    if (a == LifeKind.Ox || b == LifeKind.Ox) Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf(a, b), a + "/" + b);
                    if (IslandHerdSystem.GuardsAgainst(a, b)) Assert.IsFalse(IslandHerdSystem.GuardsAgainst(b, a), "both guard: " + a + "/" + b);
                }
            Assert.AreEqual(HerdRelation.Mingle, IslandHerdSystem.RelationOf(LifeKind.Zebra, LifeKind.Giraffe));
            Assert.AreEqual(HerdRelation.Mingle, IslandHerdSystem.RelationOf(LifeKind.Hare, LifeKind.Meerkat));
            Assert.AreEqual(HerdRelation.Mingle, IslandHerdSystem.RelationOf(LifeKind.Tortoise, LifeKind.Capybara));
            Assert.AreEqual(HerdRelation.Follow, IslandHerdSystem.RelationOf(LifeKind.Sheep, LifeKind.Goat));
            Assert.IsTrue(IslandHerdSystem.FollowsLead(LifeKind.Sheep, LifeKind.Goat), "the goats lead");
            Assert.IsFalse(IslandHerdSystem.FollowsLead(LifeKind.Goat, LifeKind.Sheep));
            Assert.AreEqual(HerdRelation.Avoid, IslandHerdSystem.RelationOf(LifeKind.Penguin, LifeKind.Reindeer));
            Assert.AreEqual(HerdRelation.Guard, IslandHerdSystem.RelationOf(LifeKind.Hare, LifeKind.ArcticFox));
            Assert.IsTrue(IslandHerdSystem.GuardsAgainst(LifeKind.Hare, LifeKind.ArcticFox));
            Assert.IsFalse(IslandHerdSystem.GuardsAgainst(LifeKind.ArcticFox, LifeKind.Hare), "the fox ignores the hares");
            Assert.AreEqual(HerdRelation.Guard, IslandHerdSystem.RelationOf(LifeKind.Meerkat, LifeKind.Zebra));
            Assert.IsTrue(IslandHerdSystem.GuardsAgainst(LifeKind.Meerkat, LifeKind.Giraffe));
            Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf(LifeKind.Meerkat, LifeKind.Ox), "the ox ignores everyone");
            Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf(LifeKind.Sheep, LifeKind.Zebra));
            Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf(LifeKind.Tree, LifeKind.Zebra), "not a herd species");
            Assert.AreEqual(HerdRelation.Ignore, IslandHerdSystem.RelationOf((LifeKind)999, LifeKind.Zebra));
            // Penguins have the coast in the morning, reindeer in the evening; without a clock both make room.
            Assert.IsTrue(IslandHerdSystem.YieldsTo(LifeKind.Reindeer, LifeKind.Penguin, 0.3f));
            Assert.IsFalse(IslandHerdSystem.YieldsTo(LifeKind.Penguin, LifeKind.Reindeer, 0.3f));
            Assert.IsTrue(IslandHerdSystem.YieldsTo(LifeKind.Penguin, LifeKind.Reindeer, 0.75f));
            Assert.IsFalse(IslandHerdSystem.YieldsTo(LifeKind.Reindeer, LifeKind.Penguin, 0.75f));
            Assert.IsTrue(IslandHerdSystem.YieldsTo(LifeKind.Reindeer, LifeKind.Penguin, -1f));
            Assert.IsTrue(IslandHerdSystem.YieldsTo(LifeKind.Penguin, LifeKind.Reindeer, -1f));
            foreach (var g in new[] { HerdGoal.Mingle, HerdGoal.Guard, HerdGoal.Gather, HerdGoal.Avoid, HerdGoal.Follow })
                Assert.IsNotEmpty(HerdGoals.Label(g));
        }

        [Test]
        public void Mingle_CloseZebrasAndGiraffes_GrazeIntermixedAndPartAgain()
        {
            var herds = Stage(16f, 1.6f, 0f, (LifeKind.Zebra, new Vector2(-3f, 0f), 6), (LifeKind.Giraffe, new Vector2(3f, 0f), 4));
            herds.mingleRate = 1f;
            float t = 0f;
            while (herds.RelationStarts(HerdGoal.Mingle) == 0 && t < 5f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(1, herds.RelationStarts(HerdGoal.Mingle), "a zebra and a giraffe herd 6 u apart did not mingle");
            Assert.AreEqual(HerdGoal.Mingle, herds.GoalOf(0));
            Assert.AreEqual(HerdGoal.Mingle, herds.GoalOf(1));
            Assert.AreEqual("mischt sich unter andere", herds.GoalLabelOf(0));
            float start = t;

            bool mixed = false;
            float closestCentroids = float.MaxValue;
            while ((herds.GoalOf(0) == HerdGoal.Mingle || herds.GoalOf(1) == HerdGoal.Mingle) && t < start + 150f)
            {
                herds.Step(0.05f);
                t += 0.05f;
                if (CountAct(herds, 0, AnimalActivity.Visit) == herds.HerdSize(0) && CountAct(herds, 1, AnimalActivity.Visit) == herds.HerdSize(1))
                {
                    float d = Vector2.Distance(MemberCentroid(herds, 0), MemberCentroid(herds, 1));
                    closestCentroids = Mathf.Min(closestCentroids, d);
                    // Interleaved: some giraffe's nearest neighbour of the other herd is closer than its own herd's nearest.
                    for (int m = 0; m < herds.HerdSize(1) && !mixed; m++)
                    {
                        Vector2 p = herds.AnimalPosition(1, m);
                        float own = float.MaxValue, other = float.MaxValue;
                        for (int k = 0; k < herds.HerdSize(1); k++) if (k != m) own = Mathf.Min(own, Vector2.Distance(p, herds.AnimalPosition(1, k)));
                        for (int k = 0; k < herds.HerdSize(0); k++) other = Mathf.Min(other, Vector2.Distance(p, herds.AnimalPosition(0, k)));
                        if (other < own) mixed = true;
                    }
                }
            }
            float took = t - start;
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "the zebras never left");
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(1), "the giraffes never left");
            Assert.Less(closestCentroids, 1f, "the two herds did not stand on one spot");
            Assert.IsTrue(mixed, "the two species did not stand interleaved");
            Assert.GreaterOrEqual(took, herds.mingleTime.x, "mingled shorter than mingleTime");
            Assert.LessOrEqual(took, herds.mingleTime.y + herds.mingleApproachTime + 5f, "mingled for too long");
            Run(herds, 1f);
            Assert.AreEqual(0, CountAct(herds, 0, AnimalActivity.Visit) + CountAct(herds, 1, AnimalActivity.Visit), "mingling members left over");

            Run(herds, herds.mingleCooldown.x - 5f);
            Assert.AreEqual(1, herds.RelationStarts(HerdGoal.Mingle), "mingled again during the cooldown");
        }

        [Test]
        public void Guard_HaresPostASentryWhileTheFoxIsNear_AndTakeItInWhenItLeaves()
        {
            var herds = Stage(16f, 1.6f, 0f, (LifeKind.Hare, new Vector2(-3f, 0f), 6), (LifeKind.ArcticFox, new Vector2(4f, 0f), 2));
            herds.guardChance = 1f;
            SetField(herds, 1, "wait", 1000f);
            float t = 0f;
            while (herds.GoalOf(0) != HerdGoal.Guard && t < 5f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(HerdGoal.Guard, herds.GoalOf(0), "no guard with a fox herd 7 u away");
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(1), "the fox reacted to the hares");

            int sentry = -1;
            for (int i = 0; i < 200 && sentry < 0; i++)
            {
                herds.Step(0.05f);
                for (int m = 0; m < herds.HerdSize(0); m++)
                    if (herds.ActivityOf(0, m) == AnimalActivity.Sentry && herds.AnimalArrived(0, m)) sentry = m;
            }
            Assert.GreaterOrEqual(sentry, 0, "no sentry took its post");
            Run(herds, 3f);
            Assert.AreEqual(HerdGoal.Guard, herds.GoalOf(0));
            Vector2 to = herds.HerdCenter(1) - herds.AnimalPosition(0, sentry);
            float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(herds.AnimalYaw(0, sentry), want)), 30f, "the sentry does not look at the fox");
            Assert.Less(Vector2.Distance(herds.HerdCenter(0), new Vector2(-3f, 0f)), 1f, "the hares ran off instead of watching");

            Teleport(herds, 1, new Vector2(12f, 0f), 1000f);
            Run(herds, 3f);
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "the guard stayed although the fox had gone " + Dump(herds, 0) + " / " + Dump(herds, 1));
            Assert.AreEqual(0, CountAct(herds, 0, AnimalActivity.Sentry), "the sentry stayed at its post");
        }

        [Test]
        public void Avoid_ReindeerAndPenguinsKeepTheirDistance()
        {
            var herds = Stage(14f, 1f, 0f, (LifeKind.Reindeer, new Vector2(-4f, 0f), 6), (LifeKind.Penguin, new Vector2(4f, 0f), 6));
            // The reindeer set off straight at the penguins.
            SetField(herds, 0, "target", new Vector2(4f, 0f));
            SetField(herds, 0, "wait", 0f);
            float closest = float.MaxValue;
            bool avoided = false;
            for (int i = 0; i < 1200; i++)
            {
                herds.Step(0.05f);
                closest = Mathf.Min(closest, Vector2.Distance(herds.HerdCenter(0), herds.HerdCenter(1)));
                if (herds.GoalOf(0) == HerdGoal.Avoid || herds.GoalOf(1) == HerdGoal.Avoid) avoided = true;
            }
            Assert.IsTrue(avoided, "nobody made room");
            Assert.GreaterOrEqual(herds.RelationStarts(HerdGoal.Avoid), 1);
            Assert.GreaterOrEqual(closest, herds.avoidDistance - 0.5f, "the two herds came too close");
            Assert.AreEqual(0, herds.FleeingHerds, "avoiding is not fleeing");

            // Morning: the penguins have the coast and stay, the reindeer make room; in the evening the other way round.
            LifeEnvironment.TimeOfDayProvider = () => 0.3f;
            Teleport(herds, 0, new Vector2(-3.5f, 0f), 0f);
            Teleport(herds, 1, new Vector2(3.5f, 0f), 5f);
            SetField(herds, 0, "target", new Vector2(3.5f, 0f));
            bool reindeer = false, penguins = false;
            float closestMorning = float.MaxValue;
            for (int i = 0; i < 600; i++)
            {
                herds.Step(0.05f);
                if (herds.GoalOf(0) == HerdGoal.Avoid) reindeer = true;
                if (herds.GoalOf(1) == HerdGoal.Avoid) penguins = true;
                closestMorning = Mathf.Min(closestMorning, Vector2.Distance(herds.HerdCenter(0), herds.HerdCenter(1)));
            }
            // The needs layer may walk the reindeer off first; either way they never come close.
            Assert.IsTrue(reindeer || herds.GoalOf(0) != HerdGoal.None || closestMorning >= herds.avoidDistance, "in the morning the reindeer did not make room");
            Assert.GreaterOrEqual(closestMorning, herds.avoidDistance - 0.5f, "in the morning the reindeer came too close");
            Assert.IsFalse(penguins, "in the morning the penguins made room");
        }

        [Test]
        public void Gather_AtDusk_HerdsMeetAtTheWater_AvoidingPairIncluded()
        {
            var herds = Stage(10f, 1.2f, 3f,
                (LifeKind.Reindeer, new Vector2(-2.5f, 3.5f), 5), (LifeKind.Penguin, new Vector2(2.5f, 3.5f), 6), (LifeKind.Sheep, new Vector2(0f, 1f), 5));
            herds.gatherChance = 1f;
            Run(herds, 2f);
            Assert.AreEqual(0, herds.GatheringsStarted, "a gathering by day");
            _night = 0.4f;
            float t = 0f;
            while (herds.GatheringsStarted == 0 && t < 5f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(1, herds.GatheringsStarted, "no gathering at dusk");
            int joined = 0;
            for (int h = 0; h < herds.HerdCount; h++) if (herds.GoalOf(h) == HerdGoal.Gather) joined++;
            Assert.GreaterOrEqual(joined, 2, "fewer than two herds came to the water");
            Assert.AreEqual(HerdGoal.Gather, herds.GoalOf(0), "the reindeer stayed away from the truce");
            Assert.AreEqual(HerdGoal.Gather, herds.GoalOf(1), "the penguins stayed away from the truce");
            Assert.AreEqual("trifft sich am Wasser", herds.GoalLabelOf(0));

            int mostAtWater = 0;
            for (int i = 0; i < 3000 && herds.GatheringActive; i++)
            {
                herds.Step(0.05f);
                int atWater = 0;
                for (int h = 0; h < herds.HerdCount; h++)
                {
                    int drinking = 0;
                    for (int m = 0; m < herds.HerdSize(h); m++)
                        if (herds.ActivityOf(h, m) == AnimalActivity.Drink && herds.AnimalArrived(h, m)) drinking++;
                    if (drinking > 0) atWater++;
                }
                mostAtWater = Mathf.Max(mostAtWater, atWater);
            }
            Assert.GreaterOrEqual(mostAtWater, 2, "fewer than two herds stood at the water together");
            Assert.IsFalse(herds.GatheringActive, "the gathering never ended");
            Run(herds, 2f);
            for (int h = 0; h < herds.HerdCount; h++) Assert.AreNotEqual(HerdGoal.Gather, herds.GoalOf(h));

            Run(herds, 60f);
            Assert.AreEqual(1, herds.GatheringsStarted, "a second gathering the same dusk");
        }

        [Test]
        public void Follow_SheepTrailTheGoats()
        {
            var herds = Stage(16f, 1.6f, 0f, (LifeKind.Goat, new Vector2(-4f, 0f), 5), (LifeKind.Sheep, new Vector2(5f, 0f), 6));
            herds.followChance = 1f;
            float t = 0f;
            while (herds.GoalOf(1) != HerdGoal.Follow && t < 5f) { herds.Step(0.05f); t += 0.05f; }
            Assert.AreEqual(HerdGoal.Follow, herds.GoalOf(1), "the sheep did not follow the goats 9 u away");
            Assert.AreEqual(HerdGoal.None, herds.GoalOf(0), "the goats followed the sheep");
            float closest = float.MaxValue;
            for (int i = 0; i < 400; i++)
            {
                herds.Step(0.05f);
                closest = Mathf.Min(closest, Vector2.Distance(herds.HerdCenter(0), herds.HerdCenter(1)));
            }
            Assert.Less(closest, herds.followGap + 1.5f, "the sheep never came up behind the goats");
            Assert.Greater(closest, herds.followGap - 1f, "the sheep walked into the goats");
            Run(herds, herds.followTime.y + 5f);
            Assert.AreNotEqual(HerdGoal.Follow, herds.GoalOf(1), "the sheep followed for longer than followTime");
        }

        [Test]
        public void Night_NothingStarts()
        {
            var herds = Stage(16f, 1.6f, 3f,
                (LifeKind.Zebra, new Vector2(-3f, 0f), 6), (LifeKind.Giraffe, new Vector2(3f, 0f), 4),
                (LifeKind.Meerkat, new Vector2(0f, 4f), 6), (LifeKind.Sheep, new Vector2(0f, -4f), 5), (LifeKind.Goat, new Vector2(4f, -4f), 4));
            herds.mingleRate = herds.followChance = herds.guardChance = herds.gatherChance = 1f;
            _night = 1f;
            Run(herds, 60f, 0.1f);
            foreach (var g in new[] { HerdGoal.Mingle, HerdGoal.Guard, HerdGoal.Gather, HerdGoal.Avoid, HerdGoal.Follow })
                Assert.AreEqual(0, herds.RelationStarts(g), g + " started at night");
            Assert.AreEqual(0, herds.GatheringsStarted);
            for (int h = 0; h < herds.HerdCount; h++) Assert.IsFalse(IslandHerdSystem.IsRelationGoal(herds.GoalOf(h)), "herd " + h);

            // The same herds by day do get going.
            _night = 0f;
            Run(herds, herds.dawnStaggerMax + 10f, 0.1f);
            Settle(herds, 20f);
            Run(herds, 10f, 0.1f);
            Assert.Greater(herds.RelationStarts(HerdGoal.Mingle) + herds.RelationStarts(HerdGoal.Guard) + herds.RelationStarts(HerdGoal.Follow), 0,
                "nothing started by day either");
        }
    }
}
