using System;
using System.Collections.Generic;
using System.IO;
using Drift.Bridge;
using Drift.Core;
using Drift.SaveSystem;
using NUnit.Framework;

namespace Drift.Tests
{
    // The Abenteuer page of "Durchgänge" (owner 2026-09-26): one entry per finished adventure run, at most MaxRuns, and a
    // reset of the adventure record and runs only. Temporary files; the owner's log and record are never touched.
    public class AdventureRunLogTests
    {
        string _log, _best;

        [SetUp]
        public void SetUp()
        {
            string id = Guid.NewGuid().ToString("N");
            _log = Path.Combine(Path.GetTempPath(), "drift_advlog_test_" + id + ".json");
            _best = Path.Combine(Path.GetTempPath(), "drift_advbest_test_" + id + ".json");
            AdventureRunLog.PathOverride = _log;
            BestDistances.PathOverride = _best;
        }

        [TearDown]
        public void TearDown()
        {
            AdventureRunLog.PathOverride = null;
            BestDistances.PathOverride = null;
            foreach (var p in new[] { _log, _best, _log + ".tmp", _best + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }

        static AdventureRun Run(float metres, int level = 3, bool record = false) =>
            AdventureRunLog.Make(metres, level, 2, 5, 9, record, new DateTime(2026, 9, 26, 23, 41, 0));

        [Test]
        public void UsesTheOverrideNotTheRealFile()
        {
            Assert.AreEqual(_log, AdventureRunLog.FilePath);
            Assert.AreEqual(0, AdventureRunLog.Runs.Count, "a missing file reads as empty");
            AdventureRunLog.Add(Run(100f));
            Assert.IsTrue(File.Exists(_log));
        }

        [Test]
        public void AddAppendsAndSurvivesAReload()
        {
            AdventureRunLog.Add(Run(4102.7f, 8, true));
            AdventureRunLog.Add(Run(1200f, 2));
            AdventureRunLog.PathOverride = _log; // drops the cache
            var runs = AdventureRunLog.Runs;
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual(4102.7f, runs[0].metres, 1e-3f, "oldest first");
            Assert.AreEqual(8, runs[0].level);
            Assert.IsTrue(runs[0].record);
            Assert.AreEqual(2, runs[0].hits);
            Assert.AreEqual(5, runs[0].dodges);
            Assert.AreEqual(9, runs[0].flotsam);
            Assert.AreEqual("2026-09-26T23:41:00", runs[0].date);
            Assert.IsFalse(runs[1].record);
        }

        [Test]
        public void MakeCleansBadNumbers()
        {
            var r = AdventureRunLog.Make(float.NaN, -3, -1, -1, -1, false, DateTime.Now);
            Assert.AreEqual(0f, r.metres);
            Assert.AreEqual(0, r.level);
            Assert.AreEqual(0, r.hits + r.dodges + r.flotsam);
        }

        [Test]
        public void TheLogKeepsOnlyTheNewestRuns()
        {
            for (int i = 0; i < AdventureRunLog.MaxRuns + 5; i++) AdventureRunLog.Add(Run(i + 1f));
            Assert.AreEqual(AdventureRunLog.MaxRuns, AdventureRunLog.Runs.Count);
            Assert.AreEqual(6f, AdventureRunLog.Runs[0].metres, "the five oldest dropped out");
            AdventureRunLog.PathOverride = _log;
            Assert.AreEqual(AdventureRunLog.MaxRuns, AdventureRunLog.Runs.Count);

            var list = new List<AdventureRun> { Run(1f), Run(2f), Run(3f) };
            AdventureRunLog.Cap(list, 2);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(2f, list[0].metres);
        }

        [Test]
        public void ABrokenFileReadsAsEmptyAndIsReplacedByTheNextRun()
        {
            File.WriteAllText(_log, "{ not json");
            AdventureRunLog.PathOverride = _log;
            // Only a warning is logged, which fails no test.
            Assert.AreEqual(0, AdventureRunLog.Runs.Count);
            AdventureRunLog.Add(Run(50f));
            AdventureRunLog.PathOverride = _log;
            Assert.AreEqual(1, AdventureRunLog.Runs.Count);
            Assert.AreEqual(0, AdventureRunLog.Parse(null).Count);
            Assert.AreEqual(0, AdventureRunLog.Parse("").Count);
            Assert.AreEqual(0, AdventureRunLog.Parse("{}").Count);
            var back = AdventureRunLog.Parse(AdventureRunLog.Serialize(new List<AdventureRun> { Run(7f), Run(8f) }));
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(8f, back[1].metres);
        }

        [Test]
        public void ResetAdventureClearsRecordAndRunsButNotCozy()
        {
            BestDistances.Submit(GameMode.Adventure, 12304f);
            BestDistances.Submit(GameMode.Cozy, 40f);
            AdventureRunLog.Add(Run(12304f, 12, true));
            int changed = 0;
            void OnChanged() => changed++;
            AdventureRunLog.Changed += OnChanged;
            try { AdventureRunLog.ResetAdventure(); }
            finally { AdventureRunLog.Changed -= OnChanged; }
            Assert.AreEqual(1, changed);
            Assert.AreEqual(0, AdventureRunLog.Runs.Count);
            Assert.IsFalse(File.Exists(_log));
            Assert.AreEqual(0f, BestDistances.Get(GameMode.Adventure));
            Assert.AreEqual(40f, BestDistances.Get(GameMode.Cozy), 1e-4f, "the cozy value stays");
        }

        [Test]
        public void TheLineShowsDayDistanceAndLevel()
        {
            Assert.AreEqual("26.09. 23:41 · 4.102 m · Stufe 8", ModeTexts.AdventureRunLine(Run(4102.9f, 8)));
            Assert.AreEqual("26.09. 23:41 · 987 m", ModeTexts.AdventureRunLine(Run(987f, 0)), "no level known");
            Assert.AreEqual("12 m · Stufe 1", ModeTexts.AdventureRunLine(new AdventureRun { metres = 12f, level = 1, date = "kaputt" }));
            Assert.AreEqual("Treibgut 9 · Ausgewichen 5 · Rempler 2", ModeTexts.AdventureRunDetails(Run(1f)));
            Assert.AreEqual("Neuer Rekord · Treibgut 9 · Ausgewichen 5 · Rempler 2", ModeTexts.AdventureRunDetails(Run(1f, 1, true)));
            Assert.AreEqual("Rekord 12.304 m", ModeTexts.AdventureRecordHeader(12304f));
            Assert.AreEqual("Noch kein Rekord", ModeTexts.AdventureRecordHeader(0f));
            Assert.AreEqual("1 Durchgang", ModeTexts.AdventureRunCount(1));
            Assert.AreEqual("3 Durchgänge", ModeTexts.AdventureRunCount(3));
        }
    }
}
