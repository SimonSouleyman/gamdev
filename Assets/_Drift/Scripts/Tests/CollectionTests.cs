using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class CollectionTests
    {
        static ulong Bits(params LifeKind[] kinds)
        {
            ulong m = 0;
            foreach (var k in kinds) m |= 1UL << (int)k;
            return m;
        }

        static int Ix(LifeKind k) => CollectionCatalog.IndexOf(k);
        static int Ix(SeaKind k) => CollectionCatalog.IndexOf(k);

        // ------------------------------------------------------------ catalog

        [Test]
        public void Catalog_HoldsEveryBiomeSpeciesOnceAndTheSeaBesides()
        {
            var ids = new HashSet<int>();
            var names = new HashSet<string>();
            for (int i = 0; i < CollectionCatalog.Count; i++)
            {
                var e = CollectionCatalog.At(i);
                Assert.AreEqual(i, e.index);
                Assert.IsTrue(ids.Add(e.id), "duplicate id " + e.id);
                Assert.IsTrue(names.Add(e.name), "duplicate name " + e.name);
                Assert.IsFalse(string.IsNullOrEmpty(e.name));
                Assert.AreEqual(i, CollectionCatalog.IndexOfId(e.id));
                Assert.IsFalse(string.IsNullOrEmpty(CollectionCatalog.HintOf(e)));
                if (e.collectible) Assert.IsTrue(e.hasLife, e.name + ": only island life can be collected");
            }
            for (int b = 0; b < Biomes.Count; b++)
                foreach (var kind in Biomes.Collectibles((LifeBiome)b))
                {
                    int i = Ix(kind);
                    Assert.GreaterOrEqual(i, 0, kind + " is missing");
                    Assert.IsTrue(CollectionCatalog.At(i).collectible);
                    Biomes.TryHomeOf(kind, out var home);
                    Assert.AreEqual((int)home, (int)CollectionCatalog.At(i).section, kind + " sits under its home biome");
                }
            Assert.AreEqual(10, CollectionCatalog.CountIn(CollectSection.Temperate));
            Assert.AreEqual(8, CollectionCatalog.CountIn(CollectSection.Tropical), "palm and reed count for the temperate islands");
            Assert.AreEqual(9, CollectionCatalog.CountIn(CollectSection.Nordic));
            Assert.AreEqual(9, CollectionCatalog.CountIn(CollectSection.Savanna));
            Assert.AreEqual(20, CollectionCatalog.CountIn(CollectSection.SeaSky));
            Assert.AreEqual(4, CollectionCatalog.CollectibleIn(CollectSection.SeaSky), "the critters");
            Assert.AreEqual(40, CollectionCatalog.CollectibleCount);
            Assert.AreEqual(56, CollectionCatalog.Count);
            Assert.AreEqual(-1, Ix(SeaKind.Driftwood));
            Assert.AreEqual("Fischschwarm", CollectionCatalog.At(Ix(SeaKind.BaitBall)).name);
            Assert.AreEqual("Walbulle", CollectionCatalog.At(Ix(SeaKind.WhaleBull)).name);
            Assert.AreEqual("lebt auf tropischen Inseln", CollectionCatalog.HintOf(CollectionCatalog.At(Ix(LifeKind.Flamingo))));
            Assert.AreEqual("wächst auf Savanneninseln", CollectionCatalog.HintOf(CollectionCatalog.At(Ix(LifeKind.Baobab))));
        }

        [Test]
        public void Catalog_SaveIdsAreTheEnumNumbers()
        {
            Assert.AreEqual((int)LifeKind.Giraffe, CollectionCatalog.At(Ix(LifeKind.Giraffe)).id);
            Assert.AreEqual(CollectionCatalog.SeaIdBase + (int)SeaKind.Dolphin, CollectionCatalog.At(Ix(SeaKind.Dolphin)).id);
            Assert.AreEqual(CollectionCatalog.FishId, CollectionCatalog.At(CollectionCatalog.FishIndex).id);
            Assert.AreEqual(DiscoveryJournal.BitCount(CollectionCatalog.CollectibleMask), CollectionCatalog.CollectibleCount);
            Assert.AreEqual(CollectionCatalog.CollectibleMask, CollectionCatalog.AnimalMask | CollectionCatalog.PlantMask | CollectionCatalog.CritterMask);
        }

        // ------------------------------------------------------------ states

        [Test]
        public void Journal_GoesFromUnknownToSeenToCollected()
        {
            var j = new DiscoveryJournal();
            int f = Ix(LifeKind.Flamingo);
            Assert.AreEqual(CollectState.Unknown, j.StateOf(f));
            Assert.IsTrue(j.MarkSeen(LifeKind.Flamingo, 12f));
            Assert.IsFalse(j.MarkSeen(LifeKind.Flamingo, 99f));
            Assert.AreEqual(CollectState.Seen, j.StateOf(f));
            Assert.AreEqual(12f, j.SeenAt(f));
            Assert.AreEqual(1, j.SeenCount);
            Assert.AreEqual(0, j.CollectedCount);

            Assert.IsTrue(j.MarkCollected(LifeKind.Flamingo, 40f));
            Assert.IsFalse(j.MarkCollected(LifeKind.Flamingo, 80f));
            Assert.AreEqual(CollectState.Collected, j.StateOf(f));
            Assert.AreEqual(12f, j.SeenAt(f), "the first sighting stays");
            Assert.AreEqual(40f, j.CollectedAt(f));
            Assert.AreEqual(1, j.SeenCount);
            Assert.AreEqual(1, j.CollectedCount);
        }

        [Test]
        public void Journal_NeverDowngrades()
        {
            var j = new DiscoveryJournal();
            j.MarkCollected(LifeKind.Sheep, 5f);
            Assert.IsFalse(j.MarkSeen(LifeKind.Sheep, 9f));
            Assert.AreEqual(CollectState.Collected, j.StateOf(LifeKind.Sheep));
            Assert.AreEqual(5f, j.SeenAt(Ix(LifeKind.Sheep)), "collecting something unseen counts as seeing it");

            j.ReportPresence(Bits(LifeKind.Sheep, LifeKind.Tree), 6f);
            j.ReportPresence(0, 7f);
            Assert.AreEqual(CollectState.Collected, j.StateOf(LifeKind.Sheep), "a lost species stays in the book");
            Assert.AreEqual(CollectState.Collected, j.StateOf(LifeKind.Tree));
            Assert.IsFalse(j.PresentNow(Ix(LifeKind.Sheep)));
            Assert.AreEqual(2, j.CollectedCount);
        }

        [Test]
        public void Journal_SeaAndSkyCanOnlyBeSeen()
        {
            var j = new DiscoveryJournal();
            Assert.IsFalse(j.MarkCollected(Ix(SeaKind.Dolphin), 1f));
            Assert.IsFalse(j.MarkCollected(LifeKind.Bird, 1f));
            Assert.AreEqual(CollectState.Unknown, j.StateOf(SeaKind.Dolphin));
            Assert.IsTrue(j.MarkSeen(SeaKind.Dolphin, 3f));
            Assert.IsTrue(j.MarkSeen(CollectionCatalog.FishIndex, 4f));
            Assert.IsFalse(j.MarkSeen(SeaKind.Driftwood, 4f), "not in the album");
            // A bird bit in the presence mask is ignored.
            Assert.AreEqual(0UL, j.ReportPresence(Bits(LifeKind.Bird, LifeKind.Seabird), 5f));
            Assert.AreEqual(CollectState.Unknown, j.StateOf(LifeKind.Bird));
        }

        [Test]
        public void Journal_PresenceReportsOnlyWhatIsNew()
        {
            var j = new DiscoveryJournal();
            Assert.IsFalse(j.Baselined);
            ulong natives = Bits(LifeKind.Grass, LifeKind.Hare, LifeKind.Crab);
            Assert.AreEqual(natives, j.ReportPresence(natives, 0f));
            Assert.IsTrue(j.Baselined);
            int v = j.Version;
            Assert.AreEqual(0UL, j.ReportPresence(natives, 1f));
            Assert.AreEqual(v, j.Version, "an unchanged mask changes nothing");

            ulong merged = natives | Bits(LifeKind.Flamingo, LifeKind.Fern);
            Assert.AreEqual(Bits(LifeKind.Flamingo, LifeKind.Fern), j.ReportPresence(merged, 90f));
            Assert.AreEqual(90f, j.CollectedAt(Ix(LifeKind.Flamingo)));
            Assert.AreEqual(merged, j.PresentMask);
            Assert.AreEqual(5, j.CollectedCount);
            Assert.AreEqual(merged, j.CollectedLifeMask);

            // The flamingos die out and come back with a later merge: no second collection.
            j.ReportPresence(merged & ~Bits(LifeKind.Flamingo), 120f);
            Assert.IsFalse(j.PresentNow(Ix(LifeKind.Flamingo)));
            Assert.AreEqual(0UL, j.ReportPresence(merged, 200f));
            Assert.IsTrue(j.PresentNow(Ix(LifeKind.Flamingo)));
            Assert.AreEqual(90f, j.CollectedAt(Ix(LifeKind.Flamingo)));
        }

        [Test]
        public void Journal_CountsKeepTheRecordAndDropWithTheSpecies()
        {
            var j = new DiscoveryJournal();
            j.ReportPresence(Bits(LifeKind.Sheep), 0f);
            int s = Ix(LifeKind.Sheep);
            j.SetCount(LifeKind.Sheep, 7);
            j.SetCount(LifeKind.Sheep, 12);
            j.SetCount(LifeKind.Sheep, 4);
            Assert.AreEqual(4, j.CurrentOf(s));
            Assert.AreEqual(12, j.BestOf(s));
            int v = j.Version;
            j.SetCount(LifeKind.Sheep, 4);
            Assert.AreEqual(v, j.Version);
            j.ReportPresence(0, 50f);
            Assert.AreEqual(0, j.CurrentOf(s), "gone with the species");
            Assert.AreEqual(12, j.BestOf(s));
            j.SetCount(LifeKind.Sheep, -3);
            Assert.AreEqual(0, j.CurrentOf(s));
        }

        [Test]
        public void Journal_UnseenMaskShrinksAndSectionsAddUp()
        {
            var j = new DiscoveryJournal();
            Assert.AreEqual(CollectionCatalog.LifeMask, j.UnseenLifeMask);
            ulong seen = j.MarkSeen(Bits(LifeKind.Zebra, LifeKind.Acacia, LifeKind.Giraffe), 8f);
            Assert.AreEqual(Bits(LifeKind.Zebra, LifeKind.Acacia, LifeKind.Giraffe), seen);
            Assert.AreEqual(0UL, j.MarkSeen(Bits(LifeKind.Zebra), 9f));
            Assert.AreEqual(0UL, j.UnseenLifeMask & Bits(LifeKind.Zebra, LifeKind.Acacia, LifeKind.Giraffe));
            j.MarkCollected(LifeKind.Zebra, 10f);
            Assert.AreEqual(3, j.SeenIn(CollectSection.Savanna));
            Assert.AreEqual(1, j.CollectedIn(CollectSection.Savanna));
            Assert.AreEqual(0, j.SeenIn(CollectSection.Nordic));
            Assert.IsFalse(j.AllSeen);

            for (int i = 0; i < CollectionCatalog.Count; i++) j.MarkSeen(i, 1f);
            Assert.IsTrue(j.AllSeen);
            Assert.AreEqual(0UL, j.UnseenLifeMask);
            Assert.IsFalse(j.Complete);
            for (int i = 0; i < CollectionCatalog.Count; i++) j.MarkCollected(i, 2f);
            Assert.IsTrue(j.Complete);
            Assert.AreEqual(CollectionCatalog.CollectibleCount, j.CollectedCount);
        }

        [Test]
        public void Journal_VersionBumpsOnlyOnChange()
        {
            var j = new DiscoveryJournal();
            int v0 = j.Version;
            j.AddYoungBorn(0);
            j.AddFiresSeen(-2);
            j.ReportStage(0);
            Assert.AreEqual(v0, j.Version);
            j.AddYoungBorn(2);
            j.AddFiresSeen(1);
            j.MarkSeen(LifeKind.Crab, 1f);
            j.MarkSeen(LifeKind.Crab, 2f);
            j.ReportStage(2);
            j.ReportStage(1);
            Assert.AreEqual(v0 + 4, j.Version);
            Assert.AreEqual(2, j.BestStage, "the highest stage of the run");
        }

        // ------------------------------------------------------------ save

        [Test]
        public void Journal_SerializesAndRestoresThroughJson()
        {
            var j = new DiscoveryJournal();
            j.MarkSeen(LifeKind.Penguin, 33.5f);
            j.MarkSeen(SeaKind.WhaleCalf, 61f);
            j.ReportPresence(Bits(LifeKind.Sheep, LifeKind.Bamboo), 200f);
            j.SetCount(LifeKind.Sheep, 9);
            j.SetCount(LifeKind.Sheep, 5);
            j.AddYoungBorn(7);
            j.AddFiresSeen(2);
            j.ReportStage(3);
            string json = JsonUtility.ToJson(j.Capture());

            var back = new DiscoveryJournal();
            back.MarkSeen(LifeKind.Ox, 1f);
            back.Restore(JsonUtility.FromJson<JournalSaveData>(json));
            Assert.AreEqual(4, back.SeenCount);
            Assert.AreEqual(2, back.CollectedCount);
            Assert.AreEqual(CollectState.Seen, back.StateOf(LifeKind.Penguin));
            Assert.AreEqual(CollectState.Seen, back.StateOf(SeaKind.WhaleCalf));
            Assert.AreEqual(CollectState.Collected, back.StateOf(LifeKind.Bamboo));
            Assert.AreEqual(CollectState.Unknown, back.StateOf(LifeKind.Ox), "Restore replaces, it does not merge");
            Assert.AreEqual(33.5f, back.SeenAt(Ix(LifeKind.Penguin)), 1e-4f);
            Assert.AreEqual(200f, back.CollectedAt(Ix(LifeKind.Sheep)), 1e-4f);
            Assert.AreEqual(9, back.BestOf(Ix(LifeKind.Sheep)));
            Assert.AreEqual(0, back.CurrentOf(Ix(LifeKind.Sheep)), "the count right now comes from the next scan");
            Assert.IsFalse(back.Baselined);
            Assert.AreEqual(7, back.YoungBorn);
            Assert.AreEqual(2, back.FiresSeen);
            Assert.AreEqual(3, back.BestStage);
            Assert.AreEqual(0UL, back.UnseenLifeMask & Bits(LifeKind.Penguin, LifeKind.Sheep, LifeKind.Bamboo));
            // What the island holds after loading is where the run goes on from, not news; the merge after it is.
            back.ReportPresence(Bits(LifeKind.Sheep, LifeKind.Bamboo), 210f);
            Assert.AreEqual(Bits(LifeKind.Fern), back.ReportPresence(Bits(LifeKind.Sheep, LifeKind.Bamboo, LifeKind.Fern), 220f));
        }

        [Test]
        public void Journal_OldElevenSpeciesBlockMigratesToSeen()
        {
            // A version 5 file: species ids of the old JournalSpecies table, no ids block.
            string v5 = "{\"species\":[0,1,4,6,10,3],\"firstSeen\":[12.0,47.0,95.0,63.0,140.0],\"youngBorn\":3,\"firesSeen\":1}";
            var j = new DiscoveryJournal();
            j.Restore(JsonUtility.FromJson<JournalSaveData>(v5));
            Assert.AreEqual(6, j.SeenCount);
            Assert.AreEqual(0, j.CollectedCount, "whether they live on the island shows with the first scan");
            Assert.AreEqual(CollectState.Seen, j.StateOf(LifeKind.Hare));
            Assert.AreEqual(CollectState.Seen, j.StateOf(LifeKind.Sheep));
            Assert.AreEqual(CollectState.Seen, j.StateOf(LifeKind.Bird));
            Assert.AreEqual(CollectState.Seen, j.StateOf(LifeKind.Crab));
            Assert.AreEqual(CollectState.Seen, j.StateOf(LifeKind.Ox));
            Assert.AreEqual(CollectState.Seen, j.StateOf(CollectionCatalog.FishIndex));
            Assert.AreEqual(CollectState.Unknown, j.StateOf(LifeKind.Goat));
            Assert.AreEqual(47f, j.SeenAt(Ix(LifeKind.Sheep)));
            Assert.AreEqual(0f, j.SeenAt(Ix(LifeKind.Ox)), "missing time reads as 0");
            Assert.AreEqual(3, j.YoungBorn);
            Assert.AreEqual(1, j.FiresSeen);
            for (int s = 0; s <= (int)JournalSpecies.Fish; s++) Assert.GreaterOrEqual(DiscoveryJournal.LegacyIndex((JournalSpecies)s), 0);

            j.ReportPresence(Bits(LifeKind.Hare, LifeKind.Sheep), 300f);
            Assert.AreEqual(CollectState.Collected, j.StateOf(LifeKind.Hare));
            Assert.AreEqual(12f, j.SeenAt(Ix(LifeKind.Hare)));
        }

        [Test]
        public void Journal_MissingBlockAndStrangeIdsRestoreCleanly()
        {
            var old = JsonUtility.FromJson<SaveGame>("{\"version\":4}");
            Assert.AreEqual(4, old.version);
            var j = new DiscoveryJournal();
            j.MarkSeen(LifeKind.Hare, 1f);
            j.Restore(old.journal);
            Assert.AreEqual(0, j.SeenCount);
            j.MarkSeen(LifeKind.Crab, 2f);
            j.Restore(null);
            Assert.AreEqual(0, j.SeenCount);
            Assert.AreEqual(CollectionCatalog.LifeMask, j.UnseenLifeMask);

            j.Restore(new JournalSaveData
            {
                ids = new[] { (int)LifeKind.Hare, 9999, -4, CollectionCatalog.SeaIdBase + (int)SeaKind.Dolphin, (int)LifeKind.Hare },
                states = new[] { 2, 2, 2, 2 }, seenAt = new[] { 5f }, youngBorn = -4,
            });
            Assert.AreEqual(2, j.SeenCount);
            Assert.AreEqual(CollectState.Collected, j.StateOf(LifeKind.Hare));
            Assert.AreEqual(CollectState.Seen, j.StateOf(SeaKind.Dolphin), "a dolphin cannot be collected, whatever the file says");
            Assert.AreEqual(1, j.CollectedCount);
            Assert.AreEqual(0, j.YoungBorn);
        }

        [Test]
        public void Save_Version6CarriesTheAlbum()
        {
            Assert.AreEqual(6, SaveGame.CurrentVersion);
            Assert.LessOrEqual(SaveGame.MinReadableVersion, 5, "version 5 saves keep loading");
            var j = new DiscoveryJournal();
            j.ReportPresence(Bits(LifeKind.Goat), 9f);
            var save = new SaveGame { journal = j.Capture() };
            var back = JsonUtility.FromJson<SaveGame>(JsonUtility.ToJson(save));
            Assert.AreEqual(6, back.version);
            var r = new DiscoveryJournal();
            r.Restore(back.journal);
            Assert.AreEqual(CollectState.Collected, r.StateOf(LifeKind.Goat));
        }

        // ------------------------------------------------------------ toasts

        [Test]
        public void Toasts_NameOneOrTwoSpeciesAndCountMore()
        {
            var t = new CollectionToasts();
            Assert.IsFalse(t.Tick(1f));
            t.Collected(Ix(LifeKind.Flamingo));
            Assert.IsTrue(t.Tick(0.016f));
            Assert.AreEqual("Neu auf deiner Insel: Flamingo!", t.Current.text);
            Assert.IsTrue(t.Current.strong);

            // A merge brings five species in one scan while the first toast is still up.
            foreach (var k in new[] { LifeKind.Fern, LifeKind.Hibiscus, LifeKind.Banana, LifeKind.Tortoise, LifeKind.Capybara }) t.Collected(Ix(k));
            t.Collected(Ix(LifeKind.Fern));
            Assert.AreEqual(5, t.Pending);
            Assert.IsFalse(t.Tick(1f));
            Assert.AreEqual("Neu auf deiner Insel: Flamingo!", t.Current.text);
            Assert.IsTrue(t.Tick(t.strongSeconds), "the first toast ends");
            Assert.IsFalse(t.Showing);
            Assert.IsFalse(t.Tick(t.gapSeconds * 0.5f), "a short gap between two toasts");
            Assert.IsFalse(t.Tick(t.gapSeconds));
            Assert.IsTrue(t.Tick(0.016f));
            Assert.AreEqual("5 neue Arten gesammelt!", t.Current.text);
            Assert.AreEqual(0, t.Pending);

            Assert.AreEqual("Neu auf deiner Insel: Zebra und Giraffe!", CollectionToasts.CollectedText(new[] { Ix(LifeKind.Zebra), Ix(LifeKind.Giraffe) }, 2, 2));
        }

        [Test]
        public void Toasts_SightingsAreQuietAndGiveWayToCollections()
        {
            var t = new CollectionToasts();
            t.Seen(Ix(SeaKind.Dolphin));
            t.Seen(Ix(LifeKind.Penguin));
            t.Seen(Ix(LifeKind.Penguin));
            t.Collected(Ix(LifeKind.Penguin));
            Assert.AreEqual(2, t.Pending, "seen and collected in one go is one piece of news");
            Assert.IsTrue(t.Tick(0.016f));
            Assert.AreEqual("Neu auf deiner Insel: Pinguin!", t.Current.text);
            Assert.AreEqual(1, t.Pending, "the dolphin waits its turn");
            Assert.IsTrue(t.Tick(10f));
            Assert.IsFalse(t.Tick(10f));
            Assert.IsTrue(t.Tick(0.016f));
            Assert.IsFalse(t.Current.strong);
            Assert.AreEqual("Zum ersten Mal gesehen: Delfin", t.Current.text);
            Assert.AreEqual("3 neue Arten gesehen", CollectionToasts.SeenText(new[] { Ix(SeaKind.Dolphin), Ix(SeaKind.Ray), Ix(LifeKind.Bird) }, 3, 3));

            t.Collected(Ix(LifeKind.Ox));
            t.Clear();
            Assert.IsFalse(t.Showing);
            Assert.AreEqual(0, t.Pending);
            Assert.IsFalse(t.Tick(5f));
        }

        [Test]
        public void Toasts_OverflowStillCounts()
        {
            var t = new CollectionToasts();
            for (int i = 0; i < CollectionCatalog.Count; i++) t.Collected(i);
            Assert.IsTrue(t.Tick(0.016f));
            Assert.AreEqual(CollectionCatalog.Count + " neue Arten gesammelt!", t.Current.text);
        }

        // ------------------------------------------------------------ Lebensbuch

        [Test]
        public void LifeBook_AddsUpAcrossRunsAndRoundTripsAsText()
        {
            var book = new LifeBook();
            Assert.AreEqual(0, book.Count);
            Assert.IsTrue(book.Add(Bits(LifeKind.Hare, LifeKind.Tree)));
            Assert.IsFalse(book.Add(Bits(LifeKind.Hare)), "nothing new, nothing to write");
            Assert.IsFalse(book.Add(Bits(LifeKind.Bird)), "birds are not collectible");
            Assert.IsTrue(book.Add(Bits(LifeKind.Baobab)));
            Assert.AreEqual(3, book.Count);
            Assert.IsTrue(book.Has(LifeKind.Baobab));
            Assert.IsFalse(book.Has(LifeKind.Sheep));
            Assert.AreEqual(CollectionCatalog.CollectibleCount, LifeBook.Total);

            string text = LifeBook.Format(book.Mask);
            Assert.IsTrue(LifeBook.TryParse(text, out ulong mask));
            Assert.AreEqual(book.Mask, mask);
            Assert.IsTrue(LifeBook.TryParse(LifeBook.Format(ulong.MaxValue), out mask));
            Assert.AreEqual(ulong.MaxValue, mask);
            Assert.IsFalse(LifeBook.TryParse("", out _));
            Assert.IsFalse(LifeBook.TryParse("not a number", out _));
            StringAssert.Contains("3/" + LifeBook.Total, book.Line);
        }

        [Test]
        public void LifeBook_PersistsInPlayerPrefs()
        {
            const string key = "drift_lifebook_test";
            try
            {
                PlayerPrefs.DeleteKey(key);
                var book = new LifeBook();
                book.Load(key);
                Assert.AreEqual(0UL, book.Mask);
                book.Add(Bits(LifeKind.Flamingo, LifeKind.Spruce));
                book.Save(key);
                var again = new LifeBook();
                again.Load(key);
                Assert.AreEqual(book.Mask, again.Mask);
                PlayerPrefs.SetString(key, "zzz");
                again.Load(key);
                Assert.AreEqual(0UL, again.Mask, "a broken entry reads as empty");
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        // ------------------------------------------------------------ album texts

        [Test]
        public void Album_CardDetailFollowsTheState()
        {
            var j = new DiscoveryJournal();
            var flamingo = CollectionCatalog.At(Ix(LifeKind.Flamingo));
            var dolphin = CollectionCatalog.At(Ix(SeaKind.Dolphin));
            var firefly = CollectionCatalog.At(Ix(LifeKind.Firefly));
            Assert.AreEqual("lebt auf tropischen Inseln", JournalPanel.DetailOf(j, flamingo, j.StateOf(flamingo.index)));
            j.MarkSeen(flamingo.index, 1f);
            Assert.AreEqual("noch nicht auf deiner Insel", JournalPanel.DetailOf(j, flamingo, j.StateOf(flamingo.index)));
            j.MarkSeen(dolphin.index, 1f);
            Assert.AreEqual("in freier Wildbahn gesehen", JournalPanel.DetailOf(j, dolphin, j.StateOf(dolphin.index)));

            j.ReportPresence(Bits(LifeKind.Flamingo, LifeKind.Firefly), 2f);
            Assert.AreEqual("lebt auf deiner Insel", JournalPanel.DetailOf(j, flamingo, CollectState.Collected));
            j.SetCount(LifeKind.Flamingo, 9);
            Assert.AreEqual("9 auf deiner Insel", JournalPanel.DetailOf(j, flamingo, CollectState.Collected));
            j.SetCount(LifeKind.Flamingo, 4);
            Assert.AreEqual("4 auf deiner Insel  ·  Rekord 9", JournalPanel.DetailOf(j, flamingo, CollectState.Collected));
            j.ReportPresence(0, 3f);
            Assert.AreEqual("zurzeit nicht auf deiner Insel", JournalPanel.DetailOf(j, flamingo, CollectState.Collected));
            Assert.AreEqual("gerade nicht zu sehen", JournalPanel.DetailOf(j, firefly, CollectState.Collected));
            Assert.AreEqual("Gesammelt 2/40  ·  Gesehen 3/56", JournalPanel.ProgressLine(j));
        }

        [Test]
        public void Album_PagesFollowTheLayout()
        {
            Assert.AreEqual(1, JournalPanel.PagesOf((int)CollectSection.Temperate, 10));
            Assert.AreEqual(2, JournalPanel.PagesOf((int)CollectSection.SeaSky, 10));
            Assert.AreEqual(2, JournalPanel.PagesOf((int)CollectSection.SeaSky, 15));
            Assert.AreEqual(1, JournalPanel.PagesOf(JournalPanel.IslandTab, 10));
            for (int k = 0; k < 14; k++) Assert.IsFalse(string.IsNullOrEmpty(JournalPanel.BuildingName((BuildingKind)k)));
        }
    }
}
