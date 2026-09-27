using System.Collections.Generic;
using Drift.Bridge;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-22: "keine benachrichtigungen zu arten oder kamerasymbole im abenteuermodus. es geht nur ums
    // rennen." Nothing about collecting or watching exists in an adventure run, and the run leaves the cozy books
    // untouched; in Gemütlich everything works exactly as before.
    public class AdventureQuietTests
    {
        readonly List<GameObject> _objects = new();
        Island _island;
        IslandHerdSystem _herds;
        WatchTools _watch;
        GameSession _session;
        IslandChaseCamera _chase;
        DiscoveryJournal _journal;
        PhotoSubject _cue;
        int _hare;

        [SetUp]
        public void SetUp()
        {
            GameModes.Set(GameMode.Cozy);
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();

            var islandGo = new GameObject("QuietIsland");
            islandGo.SetActive(false);
            _island = islandGo.AddComponent<Island>();
            _island.useKeyboardInput = false;
            _island.sinkEnabled = false;
            _island.landRadius = 7f;
            _island.shapeSeed = 4711;
            _island.driftRotation = 0f;
            islandGo.AddComponent<IslandLifeSystem>().seed = 77;
            _herds = islandGo.AddComponent<IslandHerdSystem>();
            _herds.seed = 77;
            islandGo.SetActive(true);
            _objects.Add(islandGo);

            var camGo = new GameObject("QuietCamera");
            camGo.transform.position = new Vector3(0f, 6f, -8f);
            camGo.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            camGo.AddComponent<Camera>();
            _chase = camGo.AddComponent<IslandChaseCamera>();
            _chase.target = _island;
            _objects.Add(camGo);

            var sessionGo = new GameObject("QuietSession");
            sessionGo.SetActive(false);
            _session = sessionGo.AddComponent<GameSession>();
            _objects.Add(sessionGo);

            var toolsGo = new GameObject("QuietTools");
            toolsGo.SetActive(false);
            _watch = toolsGo.AddComponent<WatchTools>();
            _watch.player = _island;
            _watch.chaseCamera = _chase;
            _watch.session = _session;
            _watch.lifeBook = false;
            _watch.keepJournal = false;
            toolsGo.SetActive(true);
            // Never the owner's real files: an empty book in memory, so every task is open.
            _watch.PhotoTasks = new PhotoTaskBook(null);
            _objects.Add(toolsGo);

            _herds.ClearHerds();
            Assert.GreaterOrEqual(_herds.AddHerd(LifeKind.Hare, Vector2.zero, 6), 0);
            _herds.behaviourRate = 0f;
            for (int i = 0; i < 10; i++) _herds.Step(0.05f);
            _herds.behaviourRate = 1f;
            _herds.strollRate = _herds.visitRate = _herds.spreadRate = _herds.signatureRate = 0f;
            _hare = CollectionCatalog.IndexOf(LifeKind.Hare);
            _journal = new DiscoveryJournal();

            var subjects = new List<PhotoSubject>();
            Assert.IsTrue(_herds.StartChoreography(0, AnimalActivity.Zigzag));
            for (int i = 0; i < 2400 && subjects.Count == 0; i++)
            {
                _herds.Step(0.05f);
                PhotoSubjects.AddHerds(_herds, PhotoTaskCatalog.Mask, subjects);
            }
            Assert.Greater(subjects.Count, 0, "the hare never showed its move");
            _cue = subjects[0];
        }

        [TearDown]
        public void TearDown()
        {
            GameModes.Set(GameMode.Cozy);
            if (_watch != null)
            {
                _watch.ExitPhotoMode();
                _watch.ReturnToIsland();
            }
            if (_session != null)
            {
                _session.FollowInputHold = _session.FollowSinkHold = false;
                _session.PhotoInputHold = _session.PhotoSinkHold = false;
            }
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        // ------------------------------------------------------------ the rule itself

        [Test]
        public void Rule_EverythingButTheAlbumIsCozyOnly()
        {
            foreach (WatchFeature f in System.Enum.GetValues(typeof(WatchFeature)))
            {
                Assert.IsTrue(WatchRules.Allowed(f, GameMode.Cozy), f + " belongs to the cozy game");
                Assert.AreEqual(f == WatchFeature.PhotoAlbum, WatchRules.Allowed(f, GameMode.Adventure),
                    f + ": only the album survives the adventure mode (it hangs off the title, not off a run)");
            }
            Assert.IsTrue(WatchRules.Enabled);
            GameModes.Set(GameMode.Adventure);
            Assert.IsFalse(WatchRules.Enabled);
            Assert.IsFalse(WatchRules.Allowed(WatchFeature.DiscoveryToast));
            Assert.IsTrue(WatchRules.Allowed(WatchFeature.PhotoAlbum));
        }

        // ------------------------------------------------------------ nothing is recorded, nothing is announced

        [Test]
        public void Adventure_ScanRecordsNothingAndQueuesNoToast()
        {
            GameModes.Set(GameMode.Adventure);
            _watch.Scan(_journal, _island.PlanarPosition, 0f);
            _watch.Scan(_journal, _island.PlanarPosition, 2f);
            Assert.AreEqual(0, _journal.SeenCount, "an adventure run must not fill the cozy album");
            Assert.AreEqual(0, _journal.CollectedCount);
            Assert.IsFalse(_journal.Baselined, "the journal is not even opened for a run that does not collect");
            Assert.AreEqual(0, _watch.Toasts.Pending);
            Assert.IsFalse(_watch.Toasts.Showing);

            GameModes.Set(GameMode.Cozy);
            _watch.Scan(_journal, _island.PlanarPosition, 0f);
            _watch.Scan(_journal, _island.PlanarPosition, 2f);
            Assert.Greater(_journal.CollectedCount, 0, "the same scan collects in Gemütlich");
            Assert.AreEqual(CollectState.Collected, _journal.StateOf(LifeKind.Hare));
        }

        [Test]
        public void Adventure_NoCueNoPhotoTaskNoJournalEntry()
        {
            var book = _watch.PhotoTasks;
            int task = PhotoTaskCatalog.IndexOf(LifeKind.Hare);
            Assert.GreaterOrEqual(task, 0);
            Assert.IsTrue(book.IsOpen(LifeKind.Hare));

            GameModes.Set(GameMode.Adventure);
            _watch.SetPhotoCue(_cue);
            Assert.IsFalse(_watch.PhotoCueVisible, "no floating camera buttons in the race");
            Assert.AreEqual(-1, _watch.PhotoCueTask);
            var view = PhotoView.Of(Camera.main);
            Assert.AreEqual(0, _watch.CheckPhotoTasks(view, null), "a photo in the race ticks nothing off");
            Assert.IsFalse(book.IsDone(task));
            Assert.AreEqual(0, book.DoneCount);
            Assert.AreEqual(0, _watch.Toasts.Pending);

            GameModes.Set(GameMode.Cozy);
            _watch.SetPhotoCue(_cue);
            Assert.IsTrue(_watch.PhotoCueVisible, "in Gemütlich the cue is exactly as before");
            Assert.AreEqual(_cue.task, _watch.PhotoCueTask);
        }

        [Test]
        public void Adventure_TappingAnAnimalDoesNothingAndNoModeOpens()
        {
            GameModes.Set(GameMode.Adventure);
            _watch.ShowPopup(_island, _herds, 0, 2);
            Assert.IsFalse(_watch.PopupVisible, "tapping an animal gives no card");
            Assert.IsFalse(_watch.BeginWatch(WatchSubjects.OfHerd(_island, _herds, 0)));
            Assert.IsFalse(_watch.Following);
            Assert.IsFalse(_watch.Watch(_hare));
            _watch.EnterPhotoMode();
            Assert.IsFalse(_watch.PhotoActive, "the photo camera is off in the race");
            _watch.OpenJournal();
            Assert.IsFalse(_watch.JournalOpen);
            Assert.AreEqual("", _watch.NewsText, "not even a \"ist gerade nicht in der Nähe\" line");

            GameModes.Set(GameMode.Cozy);
            _watch.ShowPopup(_island, _herds, 0, 2);
            Assert.IsTrue(_watch.PopupVisible);
            Assert.IsTrue(_watch.BeginWatch(WatchSubjects.OfHerd(_island, _herds, 0)), "Gemütlich is untouched");
            Assert.IsTrue(_watch.Following);
            _watch.ReturnToIsland();
            _watch.OpenJournal();
            Assert.IsTrue(_watch.JournalOpen);
            _watch.CloseJournal();
        }

        [Test]
        public void Adventure_IslandHintsStayOff()
        {
            var go = new GameObject("QuietHints");
            go.SetActive(false);
            var hints = go.AddComponent<IslandHints>();
            hints.player = _island;
            hints.session = _session;
            hints.editorPreview = true;
            go.SetActive(true);
            _objects.Add(go);

            hints.RefreshNow();
            Assert.IsTrue(hints.Shown, "the preview draws hints in Gemütlich");

            GameModes.Set(GameMode.Adventure);
            hints.RefreshNow();
            Assert.IsFalse(hints.Shown, "no island hints in the race");
            Assert.AreEqual(0, hints.VisibleBubbles);
            Assert.AreEqual(0, hints.VisibleEdges);
        }
    }
}
