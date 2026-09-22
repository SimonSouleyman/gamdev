using System.IO;
using Drift.Bridge;
using Drift.Core;
using Drift.SaveSystem;
using NUnit.Framework;

namespace Drift.Tests
{
    public class ModeFlowTests
    {
        string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "drift_flow_test_" + System.Guid.NewGuid().ToString("N") + ".json");
            BestTimes.PathOverride = _path;
        }

        [TearDown]
        public void TearDown()
        {
            BestTimes.PathOverride = null;
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void BestTimes_UsesTheOverrideNotTheRealFile()
        {
            Assert.AreEqual(_path, BestTimes.FilePath);
            BestTimes.Submit(GameMode.Adventure, 12f);
            Assert.IsTrue(File.Exists(_path));
        }

        [Test]
        public void BestTimes_OnlyALongerRunIsARecord()
        {
            Assert.AreEqual(0f, BestTimes.Get(GameMode.Adventure));
            Assert.IsFalse(BestTimes.Has(GameMode.Adventure));

            var first = BestTimes.Submit(GameMode.Adventure, 100f);
            Assert.IsTrue(first.isRecord);
            Assert.IsTrue(first.IsFirst);
            Assert.IsFalse(first.BeatPrevious);
            Assert.AreEqual(100f, first.best);

            var worse = BestTimes.Submit(GameMode.Adventure, 80f);
            Assert.IsFalse(worse.isRecord);
            Assert.AreEqual(100f, worse.best);
            Assert.AreEqual(100f, worse.previous);

            var same = BestTimes.Submit(GameMode.Adventure, 100f);
            Assert.IsFalse(same.isRecord, "equal time is not a new record");

            var better = BestTimes.Submit(GameMode.Adventure, 150f);
            Assert.IsTrue(better.BeatPrevious);
            Assert.AreEqual(100f, better.previous);
            Assert.AreEqual(150f, BestTimes.Get(GameMode.Adventure));

            Assert.IsFalse(BestTimes.Submit(GameMode.Adventure, float.NaN).isRecord);
            Assert.IsFalse(BestTimes.Submit(GameMode.Adventure, float.PositiveInfinity).isRecord);
            Assert.AreEqual(0f, BestTimes.Get(GameMode.Cozy), "modes are kept apart");
        }

        [Test]
        public void BestTimes_SurviveAReload()
        {
            BestTimes.Submit(GameMode.Adventure, 222.5f);
            BestTimes.Submit(GameMode.Cozy, 30f);
            BestTimes.PathOverride = _path; // drops the cache
            Assert.AreEqual(222.5f, BestTimes.Get(GameMode.Adventure), 1e-4f);
            Assert.AreEqual(30f, BestTimes.Get(GameMode.Cozy), 1e-4f);
            BestTimes.Clear(GameMode.Adventure);
            BestTimes.PathOverride = _path;
            Assert.AreEqual(0f, BestTimes.Get(GameMode.Adventure));
            Assert.AreEqual(30f, BestTimes.Get(GameMode.Cozy), 1e-4f);
        }

        [Test]
        public void BestTimes_BrokenFileReadsAsNoRecord()
        {
            File.WriteAllText(_path, "{ not json");
            BestTimes.PathOverride = _path;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.AreEqual(0f, BestTimes.Get(GameMode.Adventure));
                Assert.IsTrue(BestTimes.Submit(GameMode.Adventure, 5f).isRecord);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void BestTimes_Format()
        {
            Assert.AreEqual("0:00", BestTimes.Format(0f));
            Assert.AreEqual("0:00", BestTimes.Format(-3f));
            Assert.AreEqual("0:59", BestTimes.Format(59.9f));
            Assert.AreEqual("3:42", BestTimes.Format(222f));
            Assert.AreEqual("12:05", BestTimes.Format(725f));
            Assert.AreEqual("1:02:09", BestTimes.Format(3729f));
        }

        [Test]
        public void Title_BestTimeCaptionAndRecordLine()
        {
            Assert.AreEqual("Bestzeit 3:42", ModeTexts.BestTimeCaption(222f));
            StringAssert.DoesNotContain("Bestzeit", ModeTexts.BestTimeCaption(0f));
            Assert.AreEqual("Neuer Rekord!", ModeTexts.RecordLine(new BestTimes.Result { previous = 100f, best = 150f, isRecord = true }));
            Assert.AreEqual("Erste Bestzeit!", ModeTexts.RecordLine(new BestTimes.Result { previous = 0f, best = 20f, isRecord = true }));
            Assert.AreEqual("", ModeTexts.RecordLine(new BestTimes.Result { previous = 100f, best = 100f }));
            Assert.AreEqual("Neuer Versuch", ModeTexts.RestartLabel(GameMode.Adventure));
            Assert.AreEqual("Neu beginnen", ModeTexts.RestartLabel(GameMode.Cozy));
        }

        [Test]
        public void GameOver_AdventureStatsShowTimeBestFlotsamAndHits()
        {
            var st = new SessionStats { timeSurvived = 222f };
            string text = ModeTexts.AdventureStats(st, 300f, 12, 3, 9);
            StringAssert.Contains("Überlebt   3:42", text);
            StringAssert.Contains("Bestzeit   5:00", text);
            StringAssert.Contains("Treibgut   12", text);
            StringAssert.Contains("Ausgewichen   9", text);
            StringAssert.Contains("Rempler   3", text);
            StringAssert.DoesNotContain("verschmolzen", text, "nothing is merged in Abenteuer any more");
            StringAssert.Contains("Bestzeit   3:42", ModeTexts.AdventureStats(st, 0f, 0, 0, 0), "a first run is its own best time");
        }

        [Test]
        public void Hud_CozyProgressAndCalmSinkLabel()
        {
            Assert.AreEqual("7 von 20 Inseln  ·  38 % der Welt", ModeTexts.CozyProgress(7, 20, 38));
            Assert.AreEqual("Die Welt ist vereint!", ModeTexts.CozyProgress(20, 20, 100));
            foreach (float b in new[] { 0f, 0.2f, 0.54f, 0.9f })
                StringAssert.DoesNotContain("Sinkt", ModeTexts.SinkLabel(GameMode.Cozy, b));
            Assert.AreEqual("Insel ist schwer", ModeTexts.SinkLabel(GameMode.Cozy, 0.2f));
            Assert.AreEqual("Tiefgang", ModeTexts.SinkLabel(GameMode.Cozy, 0.9f));
            StringAssert.StartsWith("Sinkt", ModeTexts.SinkLabel(GameMode.Adventure, 0.2f));
            StringAssert.Contains("Treibgut", ModeTexts.SinkLabel(GameMode.Adventure, 0.2f), "flotsam is what lifts you now");
            StringAssert.Contains("18", ModeTexts.HitLabel(0.18f));
            Assert.AreEqual("Stufe 4", ModeTexts.LevelLabel(4));
            Assert.AreEqual("Auftrieb", ModeTexts.SinkLabel(GameMode.Adventure, 0.9f));
            Assert.AreEqual("Schub  ×1,6", ModeTexts.BoostLabel(1.6f));
            // A run only ever ends by sinking now - the rims of the band are invisible walls, nothing to fall over.
            Assert.AreEqual("Versunken", ModeTexts.LostTitle(GameMode.Adventure, false));
            Assert.AreEqual("Versunken", ModeTexts.LostTitle(GameMode.Adventure, true));
            Assert.AreEqual("Versunken", ModeTexts.LostTitle(GameMode.Cozy, false));
            StringAssert.DoesNotContain("Kante", ModeTexts.EdgeLabel, "the edge hint no longer warns of falling");
        }

        // Abenteuer drives itself: the hint may not promise throttle control, and it reads the same in both
        // steering schemes. Gemütlich keeps its two old hints word for word.
        [Test]
        public void Hud_AdventureHintTalksAboutSteeringAndBraking()
        {
            foreach (bool touch in new[] { false, true })
            {
                string a = ModeTexts.SteerHint(GameMode.Adventure, touch, false);
                Assert.AreEqual(a, ModeTexts.SteerHint(GameMode.Adventure, touch, true), "both schemes steer the same");
                StringAssert.Contains("lenken", a);
                StringAssert.Contains("brems", a.ToLowerInvariant());
            }
            Assert.AreEqual("Stick links: Richtung", ModeTexts.SteerHint(GameMode.Cozy, true, true));
            Assert.AreEqual("Stick links: lenken & Tempo", ModeTexts.SteerHint(GameMode.Cozy, true, false));
            Assert.AreEqual("W A S D Richtung", ModeTexts.SteerHint(GameMode.Cozy, false, true));
            Assert.AreEqual("W/S Tempo  ·  A/D lenken", ModeTexts.SteerHint(GameMode.Cozy, false, false));
        }

        [Test]
        public void Tutorial_CozyDoesNotThreatenSinking()
        {
            foreach (bool touch in new[] { false, true })
                for (var step = TutorialStep.Greeting; step < TutorialStep.Done; step++)
                {
                    string cozy = TutorialGuide.TextFor(step, touch, GameMode.Cozy).ToLowerInvariant();
                    StringAssert.DoesNotContain("sinkt sie", cozy, step.ToString());
                    StringAssert.DoesNotContain("sinken schneller", cozy, step.ToString());
                }
            StringAssert.Contains("sinkt", TutorialGuide.TextFor(TutorialStep.Buoyancy, false, GameMode.Adventure));
            StringAssert.Contains("Tiefgang", TutorialGuide.TextFor(TutorialStep.Buoyancy, false, GameMode.Cozy));
        }
    }
}
