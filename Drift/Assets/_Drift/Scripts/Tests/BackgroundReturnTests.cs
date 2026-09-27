using Drift.SaveSystem;
using Drift.UI;
using NUnit.Framework;

namespace Drift.Tests
{
    // Back from the background without the process being killed: the cozy island catches up for the time away
    // (GameSession.OnForegrounded) and the tilt takes a new middle when it steers again (TiltSteering).
    public class BackgroundReturnTests
    {
        [Test]
        public void ShortSwitches_AreIgnored()
        {
            Assert.IsFalse(GameSession.CatchesUpAfter(0.0));
            Assert.IsFalse(GameSession.CatchesUpAfter(1.99));
            Assert.IsTrue(GameSession.CatchesUpAfter(2.0));
            Assert.IsTrue(GameSession.CatchesUpAfter(600.0));
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(1.5, 1f, 1800f));
        }

        [Test]
        public void CatchUp_RunsOnTheLifeClock()
        {
            Assert.AreEqual(60f, GameSession.CatchUpSecondsFor(60.0, 1f, 1800f), 1e-3f);
            Assert.AreEqual(150f, GameSession.CatchUpSecondsFor(60.0, 2.5f, 1800f), 1e-3f);
            Assert.AreEqual(3f, GameSession.CatchUpSecondsFor(30.0, 0.1f, 1800f), 1e-3f);
        }

        [Test]
        public void CatchUp_IsCappedLikeALoad()
        {
            Assert.AreEqual(1800f, GameSession.CatchUpSecondsFor(3600.0 * 24, 1f, 1800f));
            Assert.AreEqual(1800f, GameSession.CatchUpSecondsFor(1000.0, 2f, 1800f));
            Assert.AreEqual(1800f, GameSession.CatchUpSecondsFor(double.PositiveInfinity, 1f, 1800f));
        }

        [Test]
        public void BrokenInput_CatchesUpNothing()
        {
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(-3600.0, 1f, 1800f), "clock set back");
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(double.NaN, 1f, 1800f));
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(60.0, 0f, 1800f));
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(60.0, float.NaN, 1800f));
            Assert.AreEqual(0f, GameSession.CatchUpSecondsFor(60.0, 1f, 0f));
        }

        [Test]
        public void Tilt_RecentersWhenItStartsSteeringAgain()
        {
            // The game comes back paused: the stale first frame (still marked active) does not take the pose,
            // the paused frames do not, "Weiter" does.
            Assert.IsFalse(TiltSteering.ResumeRecenterDue(true, true, true));
            Assert.IsFalse(TiltSteering.ResumeRecenterDue(true, false, true));
            Assert.IsFalse(TiltSteering.ResumeRecenterDue(true, false, false));
            Assert.IsTrue(TiltSteering.ResumeRecenterDue(true, true, false));
        }

        [Test]
        public void Tilt_WithoutABackgroundReturn_AResumeKeepsTheMiddle()
        {
            Assert.IsFalse(TiltSteering.ResumeRecenterDue(false, true, false));
        }
    }
}
