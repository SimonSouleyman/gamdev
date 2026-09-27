using System.Collections.Generic;
using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;

namespace Drift.Tests
{
    public class IslandNamesTests
    {
        [Test]
        public void ForSeed_IsTheSameNameForTheSameWorld()
        {
            foreach (int seed in new[] { 0, 1, -7, 94303, int.MaxValue, int.MinValue, 482913 })
                Assert.AreEqual(IslandNames.ForSeed(seed), IslandNames.ForSeed(seed), "seed " + seed);
            Assert.AreNotEqual(IslandNames.ForSeed(94303), IslandNames.ForSeed(94304));
        }

        [Test]
        public void ForSeed_NeverAnExistingIsland()
        {
            for (int seed = -2000; seed < 12000; seed++)
            {
                string name = IslandNames.ForSeed(seed);
                Assert.IsFalse(IslandNames.IsRealName(name), "seed " + seed + " produced the real name " + name);
            }
            // The machine can land on a real name (Got + land) - that roll has to be thrown away, not shipped.
            bool hitOne = false;
            for (int seed = -2000; seed < 12000 && !hitOne; seed++)
                for (int v = 0; v < 16; v++)
                    if (IslandNames.IsRealName(IslandNames.Variant(seed, v))) { hitOne = true; break; }
            Assert.IsTrue(hitOne, "the blacklist would be pointless if no roll ever hit it");
        }

        [Test]
        public void ForSeed_ReadsLikeAnIslandName()
        {
            for (int seed = -500; seed < 5000; seed++)
            {
                string name = IslandNames.ForSeed(seed);
                Assert.IsTrue(IslandNames.IsPlausible(name), "seed " + seed + ": " + name);
                Assert.GreaterOrEqual(name.Length, IslandNames.MinLength, name);
                Assert.LessOrEqual(name.Length, IslandNames.MaxLength, name);
                StringAssert.DoesNotContain(" ", name);
            }
        }

        [Test]
        public void ForSeed_ThousandsOfWorldsGetTheirOwnName()
        {
            var seen = new HashSet<string>();
            const int runs = 4000;
            for (int seed = 0; seed < runs; seed++) seen.Add(IslandNames.ForSeed(seed * 7919));
            Assert.Greater(seen.Count, runs * 0.96f, "too many repeats: only " + seen.Count + " of " + runs);
            // Every region is actually used.
            var regions = new HashSet<IslandNames.Region>();
            for (int seed = 0; seed < 200; seed++) regions.Add(IslandNames.RegionOf(seed));
            Assert.AreEqual(5, regions.Count);
        }

        [Test]
        public void IsRealName_IgnoresCaseAndSpace()
        {
            Assert.IsTrue(IslandNames.IsRealName("Gotland"));
            Assert.IsTrue(IslandNames.IsRealName("tahiti"));
            Assert.IsTrue(IslandNames.IsRealName(" Corsica "));
            Assert.IsFalse(IslandNames.IsRealName("Kaloheia"));
            Assert.IsFalse(IslandNames.IsRealName(""));
            Assert.IsFalse(IslandNames.IsRealName(null));
            Assert.Greater(IslandNames.Blacklist.Count, 100);
        }

        [Test]
        public void IsPlausible_RejectsWhatTheUiCannotShow()
        {
            Assert.IsFalse(IslandNames.IsPlausible("Ka"), "too short");
            Assert.IsFalse(IslandNames.IsPlausible("Kaloheiavestholm"), "too long");
            Assert.IsFalse(IslandNames.IsPlausible("kaloheia"), "no capital");
            Assert.IsFalse(IslandNames.IsPlausible("Kalo heia"), "no space");
            Assert.IsFalse(IslandNames.IsPlausible("Kaloh3ia"));
            Assert.IsTrue(IslandNames.IsPlausible("Kaloheia"));
        }

        // ---------------------------------------------------------------- the name on screen

        [Test]
        public void TitleOf_NameFirst_NumberForOldRecords()
        {
            Assert.AreEqual("Kaloheia", RunJournal.TitleOf(new RunRecord { name = "Kaloheia" }, 3));
            Assert.AreEqual("Pangäa #3", RunJournal.TitleOf(new RunRecord { name = "" }, 3), "records from before the names");
            Assert.AreEqual("Pangäa #1", RunJournal.TitleOf(new RunRecord(), 0), "not in the journal yet");
            Assert.AreEqual("Pangäa #1", RunJournal.TitleOf(null, 1));
        }

        [Test]
        public void CardSubtitle_CarriesTheNumberOnlyWhenTheTitleIsAName()
        {
            var named = new RunRecord { name = "Kaloheia", date = "2026-09-22T18:42:00" };
            string line = RunJournalPanel.CardSubtitle(named, 4);
            StringAssert.Contains("Pangäa #4", line);
            StringAssert.Contains("22. September 2026", line);
            var old = new RunRecord { date = "2026-09-22T18:42:00" };
            StringAssert.DoesNotContain("Pangäa", RunJournalPanel.CardSubtitle(old, 4));
            StringAssert.Contains("22. September", RunJournalPanel.CardSubtitle(old, 4));
            Assert.AreEqual("Pangäa #2", RunJournalPanel.CardSubtitle(new RunRecord { name = "Tobaira" }, 2), "a record without a date");
        }
    }
}
