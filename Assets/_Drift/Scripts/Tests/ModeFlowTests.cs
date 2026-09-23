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
            BestDistances.PathOverride = _path;
        }

        [TearDown]
        public void TearDown()
        {
            BestDistances.PathOverride = null;
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void BestDistances_UsesTheOverrideNotTheRealFile()
        {
            Assert.AreEqual(_path, BestDistances.FilePath);
            BestDistances.Submit(GameMode.Adventure, 12f);
            Assert.IsTrue(File.Exists(_path));
        }

        [Test]
        public void BestDistances_OnlyALongerRunIsARecord()
        {
            Assert.AreEqual(0f, BestDistances.Get(GameMode.Adventure));
            Assert.IsFalse(BestDistances.Has(GameMode.Adventure));

            var first = BestDistances.Submit(GameMode.Adventure, 100f);
            Assert.IsTrue(first.isRecord);
            Assert.IsTrue(first.IsFirst);
            Assert.IsFalse(first.BeatPrevious);
            Assert.AreEqual(100f, first.best);

            var worse = BestDistances.Submit(GameMode.Adventure, 80f);
            Assert.IsFalse(worse.isRecord);
            Assert.AreEqual(100f, worse.best);
            Assert.AreEqual(100f, worse.previous);

            var same = BestDistances.Submit(GameMode.Adventure, 100f);
            Assert.IsFalse(same.isRecord, "an equal distance is not a new record");

            var better = BestDistances.Submit(GameMode.Adventure, 150f);
            Assert.IsTrue(better.BeatPrevious);
            Assert.AreEqual(100f, better.previous);
            Assert.AreEqual(150f, BestDistances.Get(GameMode.Adventure));

            Assert.IsFalse(BestDistances.Submit(GameMode.Adventure, float.NaN).isRecord);
            Assert.IsFalse(BestDistances.Submit(GameMode.Adventure, float.PositiveInfinity).isRecord);
            Assert.AreEqual(0f, BestDistances.Get(GameMode.Cozy), "modes are kept apart");
        }

        [Test]
        public void BestDistances_SurviveAReload()
        {
            BestDistances.Submit(GameMode.Adventure, 222.5f);
            BestDistances.Submit(GameMode.Cozy, 30f);
            BestDistances.PathOverride = _path; // drops the cache
            Assert.AreEqual(222.5f, BestDistances.Get(GameMode.Adventure), 1e-4f);
            Assert.AreEqual(30f, BestDistances.Get(GameMode.Cozy), 1e-4f);
            BestDistances.Clear(GameMode.Adventure);
            BestDistances.PathOverride = _path;
            Assert.AreEqual(0f, BestDistances.Get(GameMode.Adventure));
            Assert.AreEqual(30f, BestDistances.Get(GameMode.Cozy), 1e-4f);
        }

        [Test]
        public void BestDistances_BrokenFileReadsAsNoRecord()
        {
            File.WriteAllText(_path, "{ not json");
            BestDistances.PathOverride = _path;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.AreEqual(0f, BestDistances.Get(GameMode.Adventure));
                Assert.IsTrue(BestDistances.Submit(GameMode.Adventure, 5f).isRecord);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void BestDistances_FormatInGermanMetres()
        {
            Assert.AreEqual("0 m", BestDistances.Format(0f));
            Assert.AreEqual("0 m", BestDistances.Format(-3f));
            Assert.AreEqual("0 m", BestDistances.Format(float.NaN));
            Assert.AreEqual("59 m", BestDistances.Format(59.9f), "whole metres, never rounded up past the real distance");
            Assert.AreEqual("999 m", BestDistances.Format(999.99f));
            Assert.AreEqual("1.000 m", BestDistances.Format(1000f));
            Assert.AreEqual("1.234 m", BestDistances.Format(1234.5f));
            Assert.AreEqual("12.345 m", BestDistances.Format(12345f));
            Assert.AreEqual("1.234.567 m", BestDistances.Format(1234567f));
            Assert.AreEqual(1234, BestDistances.Metres(1234.9f));
        }

        [Test]
        public void BestDistances_OldTimeRecordsAreDropped()
        {
            File.WriteAllText(_path, "{\"seconds\":[0.0,262.0]}");
            BestDistances.PathOverride = _path;
            Assert.AreEqual(0f, BestDistances.Get(GameMode.Adventure), "a best time is no distance");
            Assert.IsTrue(BestDistances.Submit(GameMode.Adventure, 40f).isRecord);
            BestDistances.PathOverride = _path;
            Assert.AreEqual(40f, BestDistances.Get(GameMode.Adventure), 1e-4f);
            StringAssert.DoesNotContain("seconds", File.ReadAllText(_path));
        }

        [Test]
        public void Title_RecordCaptionAndRecordLine()
        {
            Assert.AreEqual("Rekord 3.456 m", ModeTexts.BestDistanceCaption(3456f));
            Assert.AreEqual("Wie weit kommst du?", ModeTexts.BestDistanceCaption(0f));
            Assert.AreEqual("Neuer Rekord!", ModeTexts.RecordLine(new BestDistances.Result { previous = 100f, best = 150f, isRecord = true }));
            Assert.AreEqual("Erster Rekord!", ModeTexts.RecordLine(new BestDistances.Result { previous = 0f, best = 20f, isRecord = true }));
            Assert.AreEqual("", ModeTexts.RecordLine(new BestDistances.Result { previous = 100f, best = 100f }));
            Assert.AreEqual("Neuer Versuch", ModeTexts.RestartLabel(GameMode.Adventure));
            Assert.AreEqual("Neu beginnen", ModeTexts.RestartLabel(GameMode.Cozy));
        }

        [Test]
        public void GameOver_AdventureStatsShowDistanceRecordFlotsamAndHits()
        {
            var st = new SessionStats { timeSurvived = 222f, distance = 1234.6f };
            string text = ModeTexts.AdventureStats(st, 3000f, 12, 3, 9);
            StringAssert.Contains("Strecke   1.234 m", text);
            StringAssert.Contains("Rekord   3.000 m", text);
            StringAssert.Contains("Treibgut   12", text);
            StringAssert.Contains("Ausgewichen   9", text);
            StringAssert.Contains("Rempler   3", text);
            StringAssert.DoesNotContain("verschmolzen", text, "nothing is merged in Abenteuer any more");
            StringAssert.DoesNotContain("3:42", text, "Abenteuer no longer scores the time");
            StringAssert.DoesNotContain("Bestzeit", text);
            StringAssert.Contains("Rekord   1.234 m", ModeTexts.AdventureStats(st, 0f, 0, 0, 0), "a first run is its own record");
        }

        [Test]
        public void GameOver_CozyStillShowsTheTime()
        {
            Assert.AreEqual("0:00", ModeTexts.Clock(-3f));
            Assert.AreEqual("0:59", ModeTexts.Clock(59.9f));
            Assert.AreEqual("12:05", ModeTexts.Clock(725f));
            Assert.AreEqual("1:02:09", ModeTexts.Clock(3729f));
            StringAssert.Contains("Überlebt   3:42", ModeTexts.CozyStats(new SessionStats { timeSurvived = 222f }));
        }

        [Test]
        public void SessionStats_CarryTheDistance()
        {
            var model = new SessionModel();
            model.RecordDistance(50f);
            Assert.AreEqual(0f, model.Stats.distance, "only while playing");
            model.BeginPlaying();
            model.RecordDistance(120.5f);
            Assert.AreEqual(120.5f, model.Stats.distance, 1e-4f);
            model.RecordDistance(-4f);
            Assert.AreEqual(0f, model.Stats.distance);
            model.RecordDistance(88f);
            var copy = new SessionStats();
            copy.CopyFrom(model.Stats);
            Assert.AreEqual(88f, copy.distance, 1e-4f);
            model.Sink();
            model.RecordDistance(500f);
            Assert.AreEqual(88f, model.Stats.distance, 1e-4f, "the game-over screen keeps the run's distance");
            model.BeginPlaying();
            Assert.AreEqual(0f, model.Stats.distance, "a new run starts at zero");
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
            StringAssert.Contains("Auftrieb", TutorialGuide.TextFor(TutorialStep.Buoyancy, false, GameMode.Cozy));
        }
    }
}
