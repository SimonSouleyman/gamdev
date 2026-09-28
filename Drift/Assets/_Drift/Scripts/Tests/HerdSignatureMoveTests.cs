using System.Collections.Generic;
using Drift.Core;
using Drift.Life;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Every herd species has one move of its own (IslandHerdSystem.Signature.cs): it can be started, runs through
    // its steps on valid ground, shows its own pose, ends by itself and gives the animals back to normal life;
    // night and a startle cancel it. Plus the duplicate fixes of the movement audit.
    public class HerdSignatureMoveTests
    {
        readonly List<GameObject> _objects = new();
        float _night;

        static readonly LifeKind[] Kinds =
        {
            LifeKind.Hare, LifeKind.Sheep, LifeKind.Goat, LifeKind.Ox, LifeKind.Capybara, LifeKind.Flamingo, LifeKind.Tortoise,
            LifeKind.Penguin, LifeKind.Reindeer, LifeKind.ArcticFox, LifeKind.Meerkat, LifeKind.Zebra, LifeKind.Giraffe
        };

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

        static LifeBiome BiomeOf(LifeKind kind) => (LifeBiome)IslandHerdSystem.HomeBiomeOf(kind);

        // One herd of the kind on a flat island of its biome; everything else that could walk it off is switched off.
        IslandHerdSystem Stage(LifeKind kind, int size, out FakeIslandSurface surface)
        {
            bool shore = kind == LifeKind.Flamingo;
            bool plants = kind == LifeKind.Ox || kind == LifeKind.Goat;
            var go = new GameObject("Sig" + kind);
            go.SetActive(false);
            surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = shore ? 8f : 7f;
            surface.beach = shore ? 2.5f : 0f;
            surface.height = kind == LifeKind.Goat ? 1.6f : 1f;
            surface.biome = (int)BiomeOf(kind);
            var life = go.AddComponent<IslandLifeSystem>();
            life.seed = 1200 + (int)kind;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = 1200 + (int)kind;
            go.SetActive(true);
            _objects.Add(go);
            if (plants) life.Simulate(900f, 10f);
            herds.ClearHerds();
            Vector2 at = shore ? new Vector2(4.6f, 0f) : Vector2.zero;
            // The ox scratches at a tree and the goat rears at a bush at the edge of the herd: put the herd beside one.
            if (plants && life.TryRandomPlant(kind == LifeKind.Ox ? LifeKind.Tree : LifeKind.Bush, new System.Random(5), Vector2.zero, 5f, out Vector2 plant))
                at = plant + (plant.sqrMagnitude > 1e-4f ? -plant.normalized : Vector2.right) * 0.5f;
            Assert.GreaterOrEqual(herds.AddHerd(kind, at, size), 0, "no ground for " + kind);
            herds.behaviourRate = 0f;
            Run(herds, 0.5f);
            herds.behaviourRate = 1f;
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;
            return herds;
        }

        static void Run(IslandHerdSystem herds, float seconds, float dt = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / dt);
            for (int i = 0; i < n; i++) herds.Step(dt);
        }

        static int SizeFor(LifeKind kind) =>
            kind == LifeKind.ArcticFox ? 2 : kind == LifeKind.Tortoise ? 3 : kind == LifeKind.Ox || kind == LifeKind.Giraffe ? 4 : 6;

        [Test]
        public void EverySpecies_HasAMoveOfItsOwn()
        {
            var seen = new HashSet<AnimalActivity>();
            var moods = new HashSet<string>();
            foreach (var kind in Kinds)
            {
                Assert.IsTrue(IslandHerdSystem.IsHerdSpecies(kind), kind.ToString());
                var act = IslandHerdSystem.SignatureOf(kind);
                Assert.IsTrue(IslandHerdSystem.IsSignature(act), kind + " has no signature move");
                Assert.IsTrue(seen.Add(act), kind + " shares its move with another species");
                string mood = IslandHerdSystem.MoodText(AnimalState.Look, act);
                Assert.IsFalse(string.IsNullOrEmpty(mood), act + " has no German mood text");
                Assert.IsTrue(moods.Add(mood), act + " mood text is not unique");
            }
            Assert.AreEqual(13, seen.Count);
            Assert.AreEqual("macht Männchen und schlägt Haken", IslandHerdSystem.MoodText(AnimalState.Walk, AnimalActivity.Zigzag));
            Assert.AreEqual("schwingt den Hals im Halskampf", IslandHerdSystem.MoodText(AnimalState.Look, AnimalActivity.Necking));

            // Nobody can be made to do another species' move.
            var sheep = Stage(LifeKind.Sheep, 6, out _);
            Assert.IsFalse(sheep.StartChoreography(0, AnimalActivity.Zigzag));
            Assert.IsFalse(sheep.StartChoreography(0, AnimalActivity.Necking));
            Assert.AreEqual(0, sheep.SignatureMoves);
        }

        [Test]
        public void Hare_SitsUpAndZigzags() => PerformsItsMove(LifeKind.Hare);
        [Test]
        public void Sheep_WalkTheCarousel() => PerformsItsMove(LifeKind.Sheep);
        [Test]
        public void Goat_RearsUpOnItsHindLegs() => PerformsItsMove(LifeKind.Goat);
        [Test]
        public void Ox_ScratchesAtATree() => PerformsItsMove(LifeKind.Ox);
        [Test]
        public void Capybaras_SnuggleInAStarWithAnEgret() => PerformsItsMove(LifeKind.Capybara);
        [Test]
        public void Flamingos_StampTheMud() => PerformsItsMove(LifeKind.Flamingo);
        [Test]
        public void Tortoises_StretchTheirNecksInADuel() => PerformsItsMove(LifeKind.Tortoise);
        [Test]
        public void Penguins_CallToTheSky() => PerformsItsMove(LifeKind.Penguin);
        [Test]
        public void Reindeer_TakeTurnsAtTheCrater() => PerformsItsMove(LifeKind.Reindeer);
        [Test]
        public void ArcticFox_ChasesItsTail() => PerformsItsMove(LifeKind.ArcticFox);
        [Test]
        public void Meerkats_DanceTheWarDance() => PerformsItsMove(LifeKind.Meerkat);
        [Test]
        public void Zebras_GroomEachOther() => PerformsItsMove(LifeKind.Zebra);
        [Test]
        public void Giraffes_SwingTheirNecks() => PerformsItsMove(LifeKind.Giraffe);

        void PerformsItsMove(LifeKind kind)
        {
            var herds = Stage(kind, SizeFor(kind), out var surface);
            var act = IslandHerdSystem.SignatureOf(kind);
            int size = herds.HerdSize(0);
            Assert.IsTrue(herds.StartChoreography(0, act), kind + " could not start " + act);
            Assert.AreEqual(1, herds.SignatureMoves);
            Assert.IsFalse(herds.StartChoreography(0, act), "a second move while the first runs");

            bool performed = false, finished = false;
            float minPitch = 0f, maxRoll = 0f, maxLift = 0f, lowest = float.MaxValue, travel = 0f;
            bool callPose = false, lying = false, pawing = false, antiparallel = false, bouncing = false;
            var last = new Vector2[size];
            for (int m = 0; m < size; m++) last[m] = herds.AnimalPosition(0, m);
            for (int i = 0; i < 2400 && !finished; i++)
            {
                herds.Step(0.05f);
                Assert.AreEqual(size, herds.HerdSize(0), kind + ": an animal was lost");
                int doing = 0;
                for (int m = 0; m < size; m++)
                {
                    Vector2 p = herds.AnimalPosition(0, m);
                    float ground = surface.SampleHeight(p);
                    Assert.Greater(ground, kind == LifeKind.Flamingo ? -0.065f : 0.03f, kind + ": member " + m + " left the ground it may stand on");
                    if (herds.ActivityOf(0, m) != act) { last[m] = p; continue; }
                    doing++;
                    if (herds.ActivityStep(0, m) != IslandHerdSystem.MovePerform && herds.AnimalBakedPitch(0, m) >= 0f) { last[m] = p; continue; }
                    performed = true;
                    minPitch = Mathf.Min(minPitch, herds.AnimalBakedPitch(0, m));
                    maxRoll = Mathf.Max(maxRoll, Mathf.Abs(herds.AnimalBakedRoll(0, m)));
                    maxLift = Mathf.Max(maxLift, herds.AnimalLift(0, m));
                    lowest = Mathf.Min(lowest, ground);
                    travel += Vector2.Distance(last[m], p);
                    callPose |= herds.AnimalPose(0, m) == AnimalModels.PoseCall;
                    lying |= herds.AnimalStateOf(0, m) == AnimalState.Rest;
                    pawing |= herds.AnimalMoving(0, m) && Vector2.Distance(last[m], p) < 1e-4f;
                    bouncing |= herds.AnimalMoving(0, m);
                    for (int o = 0; o < size; o++)
                        if (o != m && herds.ActivityOf(0, o) == act && herds.ActivityStep(0, o) == IslandHerdSystem.MovePerform
                            && Mathf.Abs(Mathf.DeltaAngle(herds.AnimalYaw(0, m), herds.AnimalYaw(0, o))) > 150f) antiparallel = true;
                    last[m] = p;
                }
                if (performed && doing == 0) finished = true;
            }
            Assert.IsTrue(performed, kind + " never performed " + act);
            Assert.IsTrue(finished, kind + ": " + act + " never ended");
            Assert.AreEqual(0, herds.CountActivity(act));
            for (int m = 0; m < size; m++)
            {
                Assert.AreEqual(0f, herds.AnimalBakedPitch(0, m), kind + ": pitch left over");
                Assert.AreEqual(0f, herds.AnimalLift(0, m), kind + ": lift left over");
                Assert.IsFalse(herds.IsHidden(0, m));
            }

            // Each move's own look.
            switch (kind)
            {
                case LifeKind.Hare: Assert.Less(minPitch, -40f, "Männchen"); Assert.Greater(travel, 0.5f, "the zigzag dash"); break;
                case LifeKind.Goat: Assert.Less(minPitch, -40f, "up on the hind legs"); break;
                case LifeKind.Sheep: Assert.Greater(maxRoll, 5f, "leaning into the ring"); Assert.Greater(travel, 2f, "the ring turns"); break;
                case LifeKind.Ox: Assert.Greater(maxRoll, 8f, "leaning into the trunk"); break;
                case LifeKind.ArcticFox: Assert.Greater(maxRoll, 8f, "leaning into the spin"); Assert.Greater(travel, 0.3f); break;
                case LifeKind.Giraffe: Assert.Greater(maxRoll, 12f, "the necks swing"); break;
                case LifeKind.Zebra: Assert.IsTrue(antiparallel, "the pair stands head to tail"); Assert.Greater(maxRoll, 2f); break;
                case LifeKind.Capybara: Assert.IsTrue(lying, "the star lies down"); break;
                case LifeKind.Flamingo: Assert.Less(lowest, 0.03f, "the dance is in the water"); Assert.Greater(travel, 0.2f, "treading circles"); break;
                case LifeKind.Tortoise: Assert.Greater(maxLift, 0f, "up on stretched legs"); break;
                case LifeKind.Penguin: Assert.IsTrue(callPose, "beak to the sky, flippers out"); break;
                case LifeKind.Reindeer: Assert.IsTrue(pawing, "pawing on the spot"); break;
                case LifeKind.Meerkat: Assert.IsTrue(bouncing, "bouncing"); Assert.Greater(travel, 0.5f, "forward and back"); break;
            }

            // Back to normal life: the herd wanders and grazes again.
            Run(herds, 10f);
            Assert.AreEqual(0, herds.CountActivity(act));
            Assert.AreEqual(size, herds.HerdSize(0));
        }

        [Test]
        public void SignatureMoves_StopAtNight_AndOnAStartle()
        {
            foreach (var kind in new[] { LifeKind.Sheep, LifeKind.Zebra, LifeKind.Penguin })
            {
                _night = 0f;
                var herds = Stage(kind, 6, out _);
                var act = IslandHerdSystem.SignatureOf(kind);
                Assert.IsTrue(herds.StartChoreography(0, act), kind.ToString());
                Run(herds, 3f);
                Assert.Greater(herds.CountActivity(act), 0, kind + " under way");
                _night = 1f;
                Run(herds, 0.5f);
                Assert.AreEqual(0, herds.CountActivity(act), kind + " goes on at night");

                _night = 0f;
                Run(herds, herds.dawnStaggerMax + 10f);
                // By day the move can start again (once whatever the herd is busy with right now is over).
                bool again = false;
                for (int i = 0; i < 60 && !again; i++)
                {
                    again = herds.StartChoreography(0, act);
                    if (!again) Run(herds, 1f);
                }
                Assert.IsTrue(again, kind + " restarts by day");
                Run(herds, 2f);
                herds.Startle(new Vector2(20f, 0f));
                Run(herds, 0.1f);
                Assert.AreEqual(0, herds.CountActivity(act), kind + " is not cancelled by a startle");
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    Assert.AreEqual(0f, herds.AnimalBakedPitch(0, m));
                    Assert.AreEqual(0f, herds.AnimalBakedRoll(0, m));
                }
            }
        }

        [Test]
        public void SignatureMoves_HappenByThemselves()
        {
            foreach (var kind in new[] { LifeKind.Hare, LifeKind.Meerkat, LifeKind.Giraffe })
            {
                var herds = Stage(kind, SizeFor(kind), out _);
                herds.signatureRate = 0.2f;
                for (int i = 0; i < 8000 && herds.SignatureMoves == 0; i++) herds.Step(0.05f);
                Assert.Greater(herds.SignatureMoves, 0, kind + " never showed its move by itself");
            }
        }

        [Test]
        public void SignatureMove_AllocatesNothingWhileItRuns()
        {
            var herds = Stage(LifeKind.Capybara, 6, out _);
            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Snuggle));
            Run(herds, 12f);
            Assert.Greater(herds.CountActivity(AnimalActivity.Snuggle), 0);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) herds.Step(0.05f);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "the snuggle star (with its egret) allocates");

            var sheep = Stage(LifeKind.Sheep, 6, out _);
            Assert.IsTrue(sheep.StartChoreography(0, AnimalActivity.Carousel));
            Run(sheep, 10f);
            before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) sheep.Step(0.05f);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "the carousel allocates");
        }

        // ------------------------------------------------------------ duplicate fixes (movement audit)

        [Test]
        public void Reindeer_NoLongerWalkInSheepFile_AndTrotTheirStampedeInAWedge()
        {
            var herds = Stage(LifeKind.Reindeer, 6, out var surface);
            surface.radius = 10f;
            surface.version++;
            Run(herds, 0.2f);
            int line = 0;
            for (int i = 0; i < 2400; i++)
            {
                herds.Step(0.05f);
                line += herds.CountActivity(AnimalActivity.Line);
            }
            Assert.AreEqual(0, line, "single file is the sheep's way of walking");

            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Stampede));
            float spread = 0f;
            int samples = 0, leaderInFront = 0;
            for (int i = 0; i < 600 && herds.CountActivity(AnimalActivity.Stampede) > 0; i++)
            {
                herds.Step(0.05f);
                if (!herds.AnimalMoving(0, 0)) continue;
                // Sideways spread across the direction of travel: a wedge, not a bunch or a file.
                Vector2 c = herds.HerdCenter(0);
                Vector2 heading = new Vector2(Mathf.Sin(herds.AnimalYaw(0, 0) * Mathf.Deg2Rad), Mathf.Cos(herds.AnimalYaw(0, 0) * Mathf.Deg2Rad));
                Vector2 side = new Vector2(-heading.y, heading.x);
                float left = 0f, right = 0f, lead = Vector2.Dot(herds.AnimalPosition(0, 0) - c, heading);
                bool front = true;
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    float s = Vector2.Dot(herds.AnimalPosition(0, m) - c, side);
                    left = Mathf.Min(left, s);
                    right = Mathf.Max(right, s);
                    if (m > 0 && Vector2.Dot(herds.AnimalPosition(0, m) - c, heading) > lead + 0.01f) front = false;
                }
                spread = Mathf.Max(spread, right - left);
                samples++;
                if (front) leaderInFront++;
            }
            Assert.Greater(spread, herds.BodyLength(0) * 2.5f, "the stampeding reindeer fan out in a wedge");
            Assert.Greater(samples, 20);
            Assert.Greater(leaderInFront, samples / 2, "the leader trots at the tip of the wedge");
        }

        [Test]
        public void Stroll_GoesTwoAbreast()
        {
            var herds = Stage(LifeKind.Zebra, 6, out var surface);
            surface.beach = 2f;
            surface.radius = 9f;
            surface.version++;
            Run(herds, 0.2f);
            Assume.That(herds.StartChoreography(0, AnimalActivity.Stroll), "no shore in reach");
            float widest = 0f;
            for (int i = 0; i < 1200; i++)
            {
                herds.Step(0.05f);
                if (herds.CountActivity(AnimalActivity.Stroll) < 4) continue;
                Vector2 c = herds.HerdCenter(0), t = herds.HerdTarget(0) - c;
                if (t.sqrMagnitude < 1e-4f) continue;
                Vector2 side = new Vector2(-t.y, t.x).normalized;
                float left = 0f, right = 0f;
                for (int m = 0; m < herds.HerdSize(0); m++)
                {
                    float s = Vector2.Dot(herds.AnimalPosition(0, m) - c, side);
                    left = Mathf.Min(left, s);
                    right = Mathf.Max(right, s);
                }
                widest = Mathf.Max(widest, right - left);
            }
            Assert.Greater(widest, herds.BodyLength(0) * 0.6f, "a stroll walks in pairs, not in single file");
        }
    }
}
