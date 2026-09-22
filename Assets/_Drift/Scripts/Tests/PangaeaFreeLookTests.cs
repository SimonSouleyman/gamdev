using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The free look after the last merge: the Pangäa is complete, the run keeps running, and only "Weiter"
    // (GameSession.CompleteRun) ends it.
    public class PangaeaFreeLookTests
    {
        GameObject _go;
        GameSession _session;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("free-look-test");
            _go.SetActive(false);
            // Given its own SaveManager, the session never looks for the one in the scene - and nothing here
            // ever calls Save or Load, so no file is touched.
            var saves = _go.AddComponent<SaveManager>();
            saves.loadOnStart = false;
            saves.autosaveInterval = 0f;
            _session = _go.AddComponent<GameSession>();
            _session.saveManager = saves;
            _go.SetActive(true);
            saves.fileName = "drift_free_look_test.json";
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void ReachingThePangaea_DoesNotEndTheRun()
        {
            _session.Model.BeginPlaying();
            int changes = 0;
            bool last = false;
            // GameSession forwards this event only while it is enabled in Play Mode; the state machine itself
            // is what the Edit Mode test can watch.
            _session.Model.PangaeaReachedChanged += on => { changes++; last = on; };

            _session.PangaeaReached = true;
            Assert.AreEqual(1, changes);
            Assert.IsTrue(last);
            Assert.IsTrue(_session.PangaeaFreeLook);
            Assert.AreEqual(GameSession.State.Playing, _session.Current, "the world keeps playing");
            Assert.IsFalse(_session.IsRunComplete);
            Assert.IsFalse(_session.InputLocked, "photos, the journal and the menus stay open");
            // But the island itself stops: from here the same input flies the camera over the finished world.
            Assert.IsTrue(GameSession.IslandInputLocked(true, false, false, _session.PangaeaFreeLook));
            Assert.IsTrue(_session.SavingAllowed);

            _session.PangaeaReached = true;
            Assert.AreEqual(1, changes, "setting it again is not a second event");
        }

        [Test]
        public void NothingCanSinkTheIslandDuringTheFreeLook()
        {
            Assert.IsTrue(GameSession.SinkAllowed(true, false, false, false, false));
            Assert.IsFalse(GameSession.SinkAllowed(true, false, false, false, true), "the Pangäa hold");
            Assert.IsFalse(GameSession.SinkAllowed(false, false, false, false, true));
            // The older overloads keep meaning what they did.
            Assert.IsTrue(GameSession.SinkAllowed(true, false, false, false));
            Assert.IsTrue(GameSession.SinkAllowed(true, false, false));
        }

        [Test]
        public void WeiterStartsTheFinale()
        {
            _session.Model.BeginPlaying();
            _session.PangaeaReached = true;
            int completed = 0;
            _session.RunCompleted += () => completed++;

            _session.CompleteRun();
            Assert.AreEqual(1, completed);
            Assert.IsTrue(_session.IsRunComplete);
            Assert.AreEqual(GameSession.State.RunComplete, _session.Current);
            Assert.IsFalse(_session.PangaeaReached, "the free look is over");
            Assert.IsFalse(_session.PangaeaFreeLook);

            _session.CompleteRun();
            Assert.AreEqual(1, completed, "a second press changes nothing");
        }

        [Test]
        public void TheFreeLookSurvivesAPause()
        {
            _session.Model.BeginPlaying();
            _session.PangaeaReached = true;
            Assert.IsTrue(_session.Model.Pause());
            Assert.IsTrue(_session.PangaeaReached, "the pause menu does not drop the Pangäa");
            Assert.IsFalse(_session.PangaeaFreeLook, "but the banner is not shown over the pause menu");
            Assert.IsTrue(_session.Model.Resume());
            Assert.IsTrue(_session.PangaeaFreeLook);
        }

        [Test]
        public void LeavingForTheTitleOrANewRunDropsIt()
        {
            _session.Model.BeginPlaying();
            _session.PangaeaReached = true;
            _session.Model.ReturnToTitle();
            Assert.IsFalse(_session.PangaeaReached);

            _session.Model.BeginPlaying();
            _session.PangaeaReached = true;
            _session.Model.Sink();
            Assert.IsFalse(_session.PangaeaReached, "a game over never keeps a free look");

            _session.Model.ReturnToTitle();
            _session.PangaeaReached = true;
            Assert.IsFalse(_session.PangaeaReached, "there is nothing to look at on the title");
        }

        [Test]
        public void CompleteRunOnlyWorksOutOfAGame()
        {
            Assert.AreEqual(GameSession.State.Title, _session.Current);
            _session.CompleteRun();
            Assert.AreEqual(GameSession.State.Title, _session.Current, "the title has no run to complete");
        }

        // ---------------------------------------------------------------- the banner

        [Test]
        public void Banner_ShowsOnlyWhileTheFinishedWorldIsBeingLookedAt()
        {
            Assert.IsTrue(PangaeaFinale.ShowBanner(true, true, false, false));
            Assert.IsFalse(PangaeaFinale.ShowBanner(false, true, false, false), "nothing merged yet");
            Assert.IsFalse(PangaeaFinale.ShowBanner(true, false, false, false), "paused");
            Assert.IsFalse(PangaeaFinale.ShowBanner(true, true, true, false), "photo mode / following a herd");
            Assert.IsFalse(PangaeaFinale.ShowBanner(true, true, false, true), "the flight into space has started");
        }
    }
}
