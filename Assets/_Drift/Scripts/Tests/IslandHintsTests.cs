using Drift.Bridge;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class IslandHintsTests
    {
        static ulong Bit(LifeKind k) => 1UL << (int)k;

        [Test]
        public void Classify_UnseenSpeciesWinsOverEventAndVolcano()
        {
            var f = new IslandHintFacts { present = Bit(LifeKind.Flamingo), volcano = true, fire = true };
            var kind = IslandHintLogic.Classify(f, CollectionCatalog.CollectibleMask, out var ev, out int entry);
            Assert.AreEqual(IslandHintKind.NewSpecies, kind);
            Assert.AreEqual(CollectionCatalog.IndexOf(LifeKind.Flamingo), entry);
            Assert.AreEqual(IslandHintEvent.Fire, ev, "the event is still reported for a later look");
        }

        [Test]
        public void Classify_SeenSpeciesGiveNoHint()
        {
            var f = new IslandHintFacts { present = Bit(LifeKind.Sheep) | Bit(LifeKind.Crab) };
            ulong unseen = CollectionCatalog.CollectibleMask & ~(Bit(LifeKind.Sheep) | Bit(LifeKind.Crab));
            Assert.AreEqual(IslandHintKind.None, IslandHintLogic.Classify(f, unseen, out _, out int entry));
            Assert.AreEqual(-1, entry);
        }

        [Test]
        public void Classify_BirdsAreNeverAnIslandSpecies()
        {
            // Birds are in the album but are seen in the sky, not collected on islands.
            var f = new IslandHintFacts { present = Bit(LifeKind.Bird) | Bit(LifeKind.Seabird) };
            Assert.AreEqual(IslandHintKind.None, IslandHintLogic.Classify(f, CollectionCatalog.LifeMask, out _, out _));
        }

        [Test]
        public void Classify_EventOrderAndVolcanoFallback()
        {
            var f = new IslandHintFacts { fire = true, festival = true };
            Assert.AreEqual(IslandHintKind.Event, IslandHintLogic.Classify(f, 0, out var ev, out _));
            Assert.AreEqual(IslandHintEvent.Festival, ev);

            f = new IslandHintFacts { show = true, emerging = true, volcano = true };
            IslandHintLogic.Classify(f, 0, out ev, out _);
            Assert.AreEqual(IslandHintEvent.Show, ev);

            f = new IslandHintFacts { emerging = true, volcano = true };
            Assert.AreEqual(IslandHintKind.Event, IslandHintLogic.Classify(f, 0, out ev, out _));
            Assert.AreEqual(IslandHintEvent.Eruption, ev);

            f = new IslandHintFacts { volcano = true };
            Assert.AreEqual(IslandHintKind.Volcano, IslandHintLogic.Classify(f, 0, out ev, out _));
            Assert.AreEqual(IslandHintEvent.None, ev);

            Assert.AreEqual(IslandHintKind.None, IslandHintLogic.Classify(default, CollectionCatalog.CollectibleMask, out _, out _));
        }

        [Test]
        public void PickSpecies_AnimalBeforeCritterBeforePlant()
        {
            ulong plants = CollectionCatalog.PlantMask;
            Assert.AreNotEqual(0UL, plants);
            ulong plant = plants & (~plants + 1);
            ulong mask = plant | Bit(LifeKind.Crab) | Bit(LifeKind.Giraffe);
            Assert.AreEqual(CollectionCatalog.IndexOf(LifeKind.Giraffe), IslandHintLogic.PickSpecies(mask));
            Assert.AreEqual(CollectionCatalog.IndexOf(LifeKind.Crab), IslandHintLogic.PickSpecies(plant | Bit(LifeKind.Crab)));
            int p = IslandHintLogic.PickSpecies(plant);
            Assert.AreEqual(CollectType.Plant, CollectionCatalog.At(p).type);
            Assert.AreEqual(-1, IslandHintLogic.PickSpecies(0));
            // Stable: the same mask always advertises the same kind.
            Assert.AreEqual(IslandHintLogic.PickSpecies(Bit(LifeKind.Zebra) | Bit(LifeKind.Hare)), IslandHintLogic.PickSpecies(Bit(LifeKind.Hare) | Bit(LifeKind.Zebra)));
        }

        [Test]
        public void Labels_AreGermanAndDistinct()
        {
            Assert.AreEqual("Neue Art", IslandHintLogic.LabelOf(IslandHintKind.NewSpecies, IslandHintEvent.None));
            Assert.AreEqual("Vulkan", IslandHintLogic.LabelOf(IslandHintKind.Volcano, IslandHintEvent.None));
            Assert.AreEqual("Schauspiel", IslandHintLogic.LabelOf(IslandHintKind.Event, IslandHintEvent.Show));
            Assert.AreEqual("Feuer", IslandHintLogic.LabelOf(IslandHintKind.Event, IslandHintEvent.Fire));
            Assert.AreEqual("Fest", IslandHintLogic.LabelOf(IslandHintKind.Event, IslandHintEvent.Festival));
            Assert.AreEqual("Neuer Vulkan", IslandHintLogic.LabelOf(IslandHintKind.Event, IslandHintEvent.Eruption));
            Assert.AreEqual("", IslandHintLogic.LabelOf(IslandHintKind.None, IslandHintEvent.None));
        }

        [Test]
        public void Hold_FirstLookIsBaselineThenGrowthHolds()
        {
            int last = -1;
            float until = IslandHintLogic.Hold(ref last, 5, 10f, 0f, 14f);
            Assert.AreEqual(0f, until, "history before the island came into range is no news");
            Assert.AreEqual(5, last);
            until = IslandHintLogic.Hold(ref last, 5, 11f, until, 14f);
            Assert.AreEqual(0f, until);
            until = IslandHintLogic.Hold(ref last, 6, 12f, until, 14f);
            Assert.AreEqual(26f, until, 1e-4f);
            until = IslandHintLogic.Hold(ref last, 6, 20f, until, 14f);
            Assert.AreEqual(26f, until, 1e-4f, "no growth keeps the running hold");
            until = IslandHintLogic.Hold(ref last, 7, 20f, until, 14f);
            Assert.AreEqual(34f, until, 1e-4f, "a new move extends it");
        }

        [Test]
        public void Rank_NewSpeciesPullsHarderVolcanoLess()
        {
            Assert.Less(IslandHintLogic.Rank(IslandHintKind.NewSpecies, 60f), IslandHintLogic.Rank(IslandHintKind.Event, 50f));
            Assert.Greater(IslandHintLogic.Rank(IslandHintKind.Volcano, 50f), IslandHintLogic.Rank(IslandHintKind.Event, 55f));
            Assert.AreEqual(0f, IslandHintLogic.Rank(IslandHintKind.Event, -3f));
        }

        [Test]
        public void SelectTop_BestFirstOnlyEligibleCapped()
        {
            float[] rank = { 50f, 10f, 30f, 5f, 40f, 20f };
            bool[] ok = { true, true, true, false, true, true };
            var pick = new int[3];
            Assert.AreEqual(3, IslandHintLogic.SelectTop(rank, ok, rank.Length, pick));
            CollectionAssert.AreEqual(new[] { 1, 5, 2 }, pick);

            var two = new int[3];
            Assert.AreEqual(2, IslandHintLogic.SelectTop(rank, new[] { false, false, true, false, true, false }, rank.Length, two));
            Assert.AreEqual(2, two[0]);
            Assert.AreEqual(4, two[1]);

            Assert.AreEqual(0, IslandHintLogic.SelectTop(rank, ok, rank.Length, new int[0]));
            Assert.AreEqual(0, IslandHintLogic.SelectTop(rank, ok, 0, pick), "count limits the candidates");
        }

        [Test]
        public void SelectTop_TiesKeepListOrder()
        {
            float[] rank = { 7f, 7f, 7f };
            bool[] ok = { true, true, true };
            var pick = new int[2];
            IslandHintLogic.SelectTop(rank, ok, 3, pick);
            CollectionAssert.AreEqual(new[] { 0, 1 }, pick);
        }

        [Test]
        public void SelectSpread_TwoIslandsInOneDirectionShareOneArrow()
        {
            float[] rank = { 10f, 20f, 30f, 40f };
            bool[] ok = { true, true, true, true };
            var pos = new[] { new Vector2(-430f, -90f), new Vector2(-430f, -86f), new Vector2(430f, 0f), new Vector2(-430f, 300f) };
            var pick = new int[3];
            Assert.AreEqual(3, IslandHintLogic.SelectSpread(rank, ok, pos, 4, 114f, new int[8], pick));
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, pick, "the worse of the two stacked arrows is dropped");

            var one = new int[1];
            Assert.AreEqual(1, IslandHintLogic.SelectSpread(rank, ok, pos, 4, 114f, new int[8], one));
            Assert.AreEqual(0, one[0]);
            Assert.AreEqual(1, IslandHintLogic.SelectSpread(rank, ok, pos, 4, 114f, new int[2], pick), "scratch limits the look");
        }

        [Test]
        public void OnScreen_MarginsAndBehindCamera()
        {
            Assert.IsTrue(IslandHintLogic.OnScreen(new Vector3(0.5f, 0.5f, 10f), 0.05f, 0.05f));
            Assert.IsFalse(IslandHintLogic.OnScreen(new Vector3(0.5f, 0.5f, -10f), 0.05f, 0.05f));
            Assert.IsFalse(IslandHintLogic.OnScreen(new Vector3(0.97f, 0.5f, 10f), 0.05f, 0.05f));
            Assert.IsFalse(IslandHintLogic.OnScreen(new Vector3(0.5f, 1.2f, 10f), 0.05f, 0.05f));
            Assert.IsTrue(IslandHintLogic.OnScreen(new Vector3(0.97f, 0.5f, 10f), 0f, 0f));
        }

        [Test]
        public void DirectionOf_MirrorsPointsBehindTheCamera()
        {
            var size = new Vector2(1080f, 1920f);
            var right = IslandHintLogic.DirectionOf(new Vector3(1.5f, 0.5f, 10f), size);
            Assert.Greater(right.x, 0f);
            Assert.AreEqual(0f, right.y, 1e-4f);
            // Behind the camera and projected to the right means: turn left.
            var behind = IslandHintLogic.DirectionOf(new Vector3(0.8f, 0.5f, -10f), size);
            Assert.Less(behind.x, 0f);
            Assert.AreNotEqual(Vector2.zero, IslandHintLogic.DirectionOf(new Vector3(0.5f, 0.5f, 10f), size));
        }

        [Test]
        public void EdgePoint_LandsOnTheInnerRectBorder()
        {
            var inner = new Rect(-470f, -540f, 940f, 1150f); // x -470..470, y -540..610
            var p = IslandHintLogic.EdgePoint(Vector2.zero, new Vector2(1f, 0f), inner);
            Assert.AreEqual(470f, p.x, 1e-3f);
            Assert.AreEqual(0f, p.y, 1e-3f);
            p = IslandHintLogic.EdgePoint(Vector2.zero, new Vector2(0f, 5f), inner);
            Assert.AreEqual(610f, p.y, 1e-3f);
            p = IslandHintLogic.EdgePoint(Vector2.zero, new Vector2(-1f, -1f), inner);
            Assert.AreEqual(-470f, p.x, 1e-3f, "the nearer border (left) wins on a diagonal");
            Assert.AreEqual(-470f, p.y, 1e-3f);
            p = IslandHintLogic.EdgePoint(Vector2.zero, new Vector2(3f, 1f), inner);
            Assert.AreEqual(470f, p.x, 1e-3f);
            Assert.AreEqual(470f / 3f, p.y, 1e-3f);
            Assert.IsTrue(inner.Contains(IslandHintLogic.EdgePoint(new Vector2(100f, 50f), new Vector2(-2f, 7f), inner) * 0.999f));
        }
    }
}
