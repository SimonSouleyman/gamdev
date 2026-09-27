using System.Collections.Generic;
using Drift.SaveSystem;
using NUnit.Framework;

namespace Drift.Tests
{
    public class TutorialTests
    {
        static TutorialModel Started()
        {
            var m = new TutorialModel();
            m.Begin();
            return m;
        }

        static void DriveTo(TutorialModel m, TutorialStep step)
        {
            int guard = 0;
            while (m.Active && m.Step < step && guard++ < 20)
            {
                switch (m.Step)
                {
                    case TutorialStep.Move: m.ReportTravel(m.moveDistance + 1f); break;
                    case TutorialStep.Ram: m.ReportMerge(); break;
                    default: m.Continue(); break;
                }
            }
        }

        [Test]
        public void FreshModelIsInactiveAndNotDone()
        {
            var m = new TutorialModel();
            Assert.IsFalse(m.Active);
            Assert.IsFalse(m.Done);
            Assert.IsFalse(m.SinkingSuspended);
            Assert.IsFalse(m.Continue());
        }

        [Test]
        public void StepsRunInOrder()
        {
            var m = Started();
            var seen = new List<TutorialStep>();
            m.StepChanged += seen.Add;
            DriveTo(m, TutorialStep.Done);
            Assert.AreEqual(new[]
            {
                TutorialStep.Move, TutorialStep.Zoom, TutorialStep.Ram, TutorialStep.Buoyancy,
                TutorialStep.Form, TutorialStep.Watch, TutorialStep.Farewell, TutorialStep.Done,
            }, seen.ToArray());
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.Active);
            Assert.IsFalse(m.Skipped);
        }

        [Test]
        public void StepNumbersCountOneToEight()
        {
            var m = Started();
            for (int n = 1; n <= TutorialModel.StepCount; n++)
            {
                Assert.AreEqual(n, m.StepNumber);
                DriveTo(m, m.Step + 1);
            }
            Assert.IsTrue(m.Done);
        }

        [Test]
        public void GreetingAdvancesOnContinueOnly()
        {
            var m = Started();
            m.ReportTravel(100f);
            m.ReportZoom(1f);
            m.ReportZoom(2f);
            Assert.AreEqual(TutorialStep.Greeting, m.Step);
            Assert.IsTrue(m.CanContinue);
            Assert.IsTrue(m.Continue());
            Assert.AreEqual(TutorialStep.Move, m.Step);
        }

        [Test]
        public void MoveNeedsRealTravelAndCannotBeClickedThrough()
        {
            var m = Started();
            m.Continue();
            Assert.IsFalse(m.CanContinue);
            Assert.IsFalse(m.Continue());
            m.ReportTravel(3f);
            m.ReportTravel(-50f);
            Assert.AreEqual(TutorialStep.Move, m.Step);
            m.ReportTravel(3.5f);
            Assert.AreEqual(TutorialStep.Zoom, m.Step);
            Assert.IsTrue(m.Cheering);
        }

        [Test]
        public void ZoomAdvancesOnFifteenPercentChangeOrContinue()
        {
            var m = Started();
            DriveTo(m, TutorialStep.Zoom);
            m.ReportZoom(1f);
            m.ReportZoom(1.1f);
            m.ReportZoom(0.9f);
            Assert.AreEqual(TutorialStep.Zoom, m.Step);
            m.ReportZoom(0.8f);
            Assert.AreEqual(TutorialStep.Ram, m.Step);

            var clicked = Started();
            DriveTo(clicked, TutorialStep.Zoom);
            Assert.IsTrue(clicked.Continue());
            Assert.AreEqual(TutorialStep.Ram, clicked.Step);
        }

        [Test]
        public void ZoomBaselineIsTakenWhenTheStepStarts()
        {
            var m = Started();
            m.ReportZoom(3f);
            DriveTo(m, TutorialStep.Zoom);
            m.ReportZoom(1f);
            Assert.AreEqual(TutorialStep.Zoom, m.Step);
            m.ReportZoom(1.2f);
            Assert.AreEqual(TutorialStep.Ram, m.Step);
        }

        [Test]
        public void RamAdvancesOnMergeOnly()
        {
            var m = Started();
            DriveTo(m, TutorialStep.Ram);
            Assert.IsTrue(m.ShowsArrow);
            Assert.IsFalse(m.Continue());
            m.ReportTravel(100f);
            Assert.AreEqual(TutorialStep.Ram, m.Step);
            m.ReportMerge();
            Assert.AreEqual(TutorialStep.Buoyancy, m.Step);
            Assert.IsFalse(m.ShowsArrow);
            Assert.AreEqual(TildaMood.Cheer, m.Mood);
        }

        [Test]
        public void EarlyMergeSolvesTheRamStepInAdvance()
        {
            var m = Started();
            m.Continue();
            m.ReportMerge();
            Assert.AreEqual(TutorialStep.Move, m.Step);
            m.ReportTravel(10f);
            Assert.AreEqual(TutorialStep.Zoom, m.Step);
            m.Continue();
            Assert.AreEqual(TutorialStep.Buoyancy, m.Step);
        }

        [Test]
        public void WatchAdvancesOnPopupOrContinue()
        {
            var m = Started();
            DriveTo(m, TutorialStep.Watch);
            m.ReportWatched();
            Assert.AreEqual(TutorialStep.Farewell, m.Step);

            var early = Started();
            early.ReportWatched();
            DriveTo(early, TutorialStep.Form);
            early.Continue();
            Assert.AreEqual(TutorialStep.Farewell, early.Step);
        }

        [Test]
        public void SinkingIsSuspendedOnlyInStepsOneToFour()
        {
            var m = Started();
            for (var step = TutorialStep.Greeting; step <= TutorialStep.Farewell; step++)
            {
                DriveTo(m, step);
                Assert.AreEqual(step, m.Step);
                Assert.AreEqual(step <= TutorialStep.Ram, m.SinkingSuspended, step.ToString());
            }
            DriveTo(m, TutorialStep.Done);
            Assert.IsFalse(m.SinkingSuspended);
        }

        [Test]
        public void SkipFinishesAndReleasesSinking()
        {
            var m = Started();
            m.Continue();
            int finished = 0;
            m.Finished += () => finished++;
            Assert.IsTrue(m.SinkingSuspended);
            m.Skip();
            Assert.IsTrue(m.Done);
            Assert.IsTrue(m.Skipped);
            Assert.IsFalse(m.Active);
            Assert.IsFalse(m.SinkingSuspended);
            Assert.AreEqual(TutorialStep.Done, m.Step);
            Assert.AreEqual(1, finished);
            m.Skip();
            m.ReportMerge();
            m.Tick(100f);
            Assert.AreEqual(1, finished);
        }

        [Test]
        public void MarkDoneIsSilentAndBeginReplays()
        {
            var m = new TutorialModel();
            int events = 0;
            m.StepChanged += _ => events++;
            m.MarkDone();
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.Active);
            Assert.AreEqual(0, events);
            m.Begin();
            Assert.IsTrue(m.Active);
            Assert.IsFalse(m.Done);
            Assert.AreEqual(TutorialStep.Greeting, m.Step);
            Assert.IsTrue(m.SinkingSuspended);
        }

        [Test]
        public void IdlePlayerMakesTildaSleepyUntilActivity()
        {
            var m = Started();
            Assert.AreEqual(TildaMood.Wave, m.Mood);
            m.Continue();
            m.Tick(19f);
            Assert.IsFalse(m.Sleepy);
            Assert.AreEqual(TildaMood.Happy, m.Mood);
            m.Tick(1.5f);
            Assert.IsTrue(m.Sleepy);
            Assert.AreEqual(TildaMood.Sleep, m.Mood);
            m.ReportActivity();
            Assert.IsFalse(m.Sleepy);
            Assert.AreEqual(TildaMood.Happy, m.Mood);
        }

        [Test]
        public void StepChangeWakesTilda()
        {
            var m = Started();
            m.Tick(25f);
            Assert.IsTrue(m.Sleepy);
            m.Continue();
            Assert.IsFalse(m.Sleepy);
        }

        [Test]
        public void CheerFadesAfterASuccess()
        {
            var m = Started();
            DriveTo(m, TutorialStep.Ram);
            m.ReportMerge();
            Assert.IsTrue(m.Cheering);
            m.Tick(m.cheerSeconds + 0.1f);
            Assert.IsFalse(m.Cheering);
            Assert.AreEqual(TildaMood.Happy, m.Mood);
        }

        [Test]
        public void FarewellEndsOnItsOwn()
        {
            var m = Started();
            DriveTo(m, TutorialStep.Farewell);
            Assert.AreEqual(TildaMood.Cheer, m.Mood);
            m.Tick(m.farewellSeconds - 1f);
            Assert.IsTrue(m.Active);
            m.Tick(1.5f);
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.Skipped);
        }

        [Test]
        public void VersionMovesWithEveryStep()
        {
            var m = Started();
            int v = m.Version;
            m.Continue();
            Assert.Greater(m.Version, v);
            v = m.Version;
            m.ReportTravel(1f);
            Assert.AreEqual(v, m.Version);
        }
    }
}
