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
    // "Tier beobachten" (2026-09-22, owner: "auf die Meldung drücken soll den normalen Beobachten-Modus geben" and
    // "die Kamera-Symbole funktionieren nicht"): there is exactly one watch mode, every way in ends in the same
    // state, and the photo-task cue can actually be hit with a finger while the animal walks about.
    public class WatchModeTests
    {
        readonly List<GameObject> _objects = new();
        Island _island;
        IslandHerdSystem _herds;
        WatchTools _watch;
        GameSession _session;
        IslandChaseCamera _chase;
        PhotoSubject _cue;
        int _entry;

        // Everything the four entry points must agree on. Photo mode is an extra layer the cue puts on top, so it
        // is not part of the watch state itself.
        struct WatchState
        {
            public bool following, observing, driven, inputHold, sinkHold;
            public int herd;
            public Vector3 focus;
            public string chip;
            public float homeYaw, homePitch, homeDistance;

            public override string ToString() =>
                $"following={following} observing={observing} driven={driven} herd={herd} focus={focus} chip=\"{chip}\" " +
                $"home={homeYaw:F3}/{homePitch:F3}/{homeDistance:F3} holds={inputHold}/{sinkHold}";
        }

        WatchState Snapshot() => new WatchState
        {
            following = _watch.Following,
            observing = _watch.Observing,
            driven = _watch.CameraDriven,
            inputHold = _session.FollowInputHold,
            sinkHold = _session.FollowSinkHold,
            herd = _watch.FollowedHerd,
            focus = _watch.FollowFocus,
            chip = _watch.FollowChipText,
            homeYaw = _watch.Rig.homeYaw,
            homePitch = _watch.Rig.homePitch,
            homeDistance = _watch.Rig.homeDistance,
        };

        [SetUp]
        public void SetUp()
        {
            // The watch tools are cozy-only now (WatchRules); the Editor may still sit in Adventure from a play run.
            GameModes.Set(GameMode.Cozy);
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();

            var islandGo = new GameObject("WatchTestIsland");
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

            var camGo = new GameObject("WatchTestCamera");
            camGo.transform.position = new Vector3(0f, 6f, -8f);
            camGo.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            camGo.AddComponent<Camera>();
            _chase = camGo.AddComponent<IslandChaseCamera>();
            _chase.target = _island;
            _objects.Add(camGo);

            var sessionGo = new GameObject("WatchTestSession");
            sessionGo.SetActive(false);
            _session = sessionGo.AddComponent<GameSession>();
            _objects.Add(sessionGo);

            var toolsGo = new GameObject("WatchTestTools");
            toolsGo.SetActive(false);
            _watch = toolsGo.AddComponent<WatchTools>();
            _watch.player = _island;
            _watch.chaseCamera = _chase;
            _watch.session = _session;
            _watch.lifeBook = false;
            _watch.keepJournal = false;
            toolsGo.SetActive(true);
            // Never the owner's real file: an empty book in memory, so every task is open.
            _watch.PhotoTasks = new PhotoTaskBook(null);
            _objects.Add(toolsGo);

            _herds.ClearHerds();
            Assert.GreaterOrEqual(_herds.AddHerd(LifeKind.Hare, Vector2.zero, 6), 0);
            _herds.behaviourRate = 0f;
            for (int i = 0; i < 10; i++) _herds.Step(0.05f);
            _herds.behaviourRate = 1f;
            _herds.strollRate = _herds.visitRate = _herds.spreadRate = _herds.signatureRate = 0f;
            _entry = CollectionCatalog.IndexOf(LifeKind.Hare);

            // One hare in its signature move: that is what a photo-task cue floats over.
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

        // ------------------------------------------------------------ one watch mode, four ways in

        [Test]
        public void AnimalTap_StartsTheWatchModeWithTheCreatureCard()
        {
            _watch.ShowPopup(_island, _herds, 0, 2);
            Assert.IsTrue(_watch.PopupVisible);
            _watch.FollowPopupHerd();

            var state = Snapshot();
            Assert.IsTrue(state.following);
            Assert.IsFalse(state.observing, "a herd is never the second kind of watching");
            Assert.IsTrue(state.driven, "the orbit rig drives the camera");
            Assert.IsTrue(state.inputHold && state.sinkHold);
            Assert.AreEqual(0, state.herd);
            Assert.AreSame(_herds, _watch.Watched.herds);
            Assert.IsTrue(_watch.PopupVisible, "the card says what is being watched");
            Assert.AreEqual(WatchTools.NameOf(LifeKind.Hare), _watch.PopupTitle);
            StringAssert.Contains(WatchTools.PluralOf(LifeKind.Hare), state.chip);
        }

        [Test]
        public void ToastTap_GivesTheSameWatchModeAsTappingTheAnimal()
        {
            _watch.ShowPopup(_island, _herds, 0, 2);
            _watch.FollowPopupHerd();
            var byTap = Snapshot();
            bool popupByTap = _watch.PopupVisible;
            _watch.ReturnToIsland();

            // "Zum ersten Mal gesehen: Hase" and a finger on the chip.
            _watch.Toasts.Seen(_entry);
            Assert.IsTrue(_watch.Toasts.Tick(0.1f));
            Assert.AreEqual(1, _watch.Toasts.Current.count);
            _watch.OnNewsTapped();

            Assert.AreEqual(byTap.ToString(), Snapshot().ToString());
            Assert.AreEqual(popupByTap, _watch.PopupVisible);
            Assert.IsFalse(_watch.PhotoActive, "watching, not photographing");
        }

        [Test]
        public void JournalCardTap_GivesTheSameWatchModeAsTappingTheAnimal()
        {
            _watch.ShowPopup(_island, _herds, 0, 2);
            _watch.FollowPopupHerd();
            var byTap = Snapshot();
            _watch.ReturnToIsland();

            var journal = new DiscoveryJournal();
            journal.ReportPresence(1UL << (int)LifeKind.Hare, 0f);
            _watch.OpenJournal();
            _watch.JournalView.Fill(journal, null, default);
            _watch.ShowJournalTab(0);
            _watch.JournalView.Fill(journal, null, default);
            Assert.IsTrue(_watch.JournalView.TapCard(_entry), "the hare card was not on the page");

            Assert.IsFalse(_watch.JournalOpen, "the journal closes when the camera goes out");
            Assert.AreEqual(byTap.ToString(), Snapshot().ToString());
        }

        [Test]
        public void PhotoCueTap_GivesTheSameWatchModePlusPhotoMode()
        {
            _watch.ShowPopup(_island, _herds, 0, 2);
            _watch.FollowPopupHerd();
            var byTap = Snapshot();
            _watch.ReturnToIsland();

            _watch.SetPhotoCue(_cue);
            Assert.IsTrue(_watch.PhotoCueVisible);
            Assert.AreEqual(PhotoTaskCatalog.IndexOf(LifeKind.Hare), _watch.PhotoCueTask);
            _watch.PhotographCue();

            var state = Snapshot();
            Assert.IsTrue(state.following, "the cue watches the herd it floats over");
            Assert.AreEqual(byTap.herd, state.herd);
            Assert.AreEqual(byTap.chip, state.chip);
            Assert.AreEqual(byTap.observing, state.observing);
            Assert.AreEqual(0f, Vector3.Distance(byTap.focus, state.focus), 1e-4f);
            Assert.IsTrue(_watch.PhotoActive, "and opens photo mode on it");
            Assert.IsFalse(_watch.PhotoCueVisible, "the cue steps aside inside photo mode");
        }

        [Test]
        public void EveryEntryPointEndsInTheSameWatchMode()
        {
            _watch.ShowPopup(_island, _herds, 0, 2);
            _watch.FollowPopupHerd();
            string byTap = Snapshot().ToString();
            _watch.ReturnToIsland();

            Assert.IsTrue(_watch.Watch(_entry), "journal card / toast route");
            string byIndex = Snapshot().ToString();
            _watch.ReturnToIsland();

            _watch.BeginWatch(WatchSubjects.OfHerd(_island, _herds, 0));
            string bySubject = Snapshot().ToString();
            _watch.ReturnToIsland();

            _watch.BeginWatch(WatchSubjects.OfCue(_cue));
            string byCue = Snapshot().ToString();

            Assert.AreEqual(byTap, byIndex);
            Assert.AreEqual(byTap, bySubject);
            Assert.AreEqual(byTap, byCue);
        }

        [Test]
        public void LeavingWatchModeIsTheSameWayOutWhereverItStarted()
        {
            foreach (var start in new System.Action[]
            {
                () => { _watch.ShowPopup(_island, _herds, 0, 2); _watch.FollowPopupHerd(); },
                () => _watch.Watch(_entry),
                () => _watch.BeginWatch(WatchSubjects.OfCue(_cue)),
            })
            {
                start();
                Assert.IsTrue(_watch.Following);
                _watch.ReturnToIsland();
                Assert.IsFalse(_watch.Following);
                Assert.IsFalse(_watch.CameraDriven);
                Assert.IsFalse(_session.FollowInputHold);
                Assert.IsFalse(_session.FollowSinkHold);
                Assert.AreEqual(-1, _watch.FollowedHerd);
            }
        }

        // ------------------------------------------------------------ the cue is a finger target

        [Test]
        public void CueHitArea_IsFingerSizedAndCoversTheDrawnIcon()
        {
            _watch.SetPhotoCue(_cue);
            var hit = _watch.PhotoCueHitRect;
            var icon = _watch.PhotoCueIconRect;
            Assert.IsNotNull(hit);
            Assert.GreaterOrEqual(WatchTools.CueHitSize(10f), 90f, "never smaller than a fingertip");
            Assert.AreEqual(2f * _watch.cueTapRadius, WatchTools.CueHitSize(_watch.cueTapRadius), 1e-4f);
            Assert.GreaterOrEqual(hit.rect.width, 90f);
            Assert.GreaterOrEqual(hit.rect.height, 90f);

            // Every corner of the drawn icon lies inside the area that takes the tap.
            var iconCorners = new Vector3[4];
            icon.GetWorldCorners(iconCorners);
            foreach (var c in iconCorners)
                Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(hit, c, null), "icon corner " + c + " outside the hit rect");
            Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(hit, _watch.PhotoCueScreen, null), "the tap centre is the icon centre");
        }

        [Test]
        public void CueTap_SurvivesTheIconWalkingOffUnderTheFinger()
        {
            _watch.SetPhotoCue(_cue);
            Vector2 down = _watch.PhotoCueScreen;
            Assert.IsTrue(_watch.CueContains(down));
            Assert.IsFalse(_watch.CueContains(down + new Vector2(600f, 0f)), "far away is not the cue");

            // Press on the icon, the hare dashes, the finger comes up where the icon no longer is: still a tap on
            // the cue, because the press remembered what it went down on.
            var press = new TapPress { seconds = 0.12f, movedPixels = 6f, overUi = true };
            Assert.AreEqual(TapReject.None, _watch.Release(press, down + new Vector2(6f, 4f), true));
            Assert.IsTrue(_watch.Following, "the cue took the camera to the hare");
            Assert.IsTrue(_watch.PhotoActive);
        }

        [Test]
        public void CueOverUi_IsNotSwallowedByTheTapGateButADragStillIs()
        {
            _watch.SetPhotoCue(_cue);
            Vector2 down = _watch.PhotoCueScreen;
            // Without the cue flag the same press is thrown away as "UI on top" - that is what used to happen.
            var press = new TapPress { seconds = 0.12f, movedPixels = 6f, overUi = true };
            Assert.AreEqual(TapReject.OverUi, _watch.Release(press, down));
            Assert.IsFalse(_watch.Following);

            var drag = new TapPress { seconds = 0.12f, movedPixels = 500f, overUi = true };
            Assert.AreEqual(TapReject.MovedTooFar, _watch.Release(drag, down + new Vector2(500f, 0f), true));
            Assert.IsFalse(_watch.Following, "dragging the view is not a tap");
        }

        [Test]
        public void Cue_LetsGoWhenTheMoveEnds()
        {
            var cue = _cue;
            Assert.IsTrue(PhotoSubjects.Refresh(ref cue));
            _watch.SetPhotoCue(cue);
            Assert.IsTrue(_watch.PhotoCueVisible);

            bool ended = false;
            for (int i = 0; i < 2400 && !ended; i++)
            {
                _herds.Step(0.05f);
                ended = !PhotoSubjects.Refresh(ref cue);
            }
            Assert.IsTrue(ended, "the hare never stopped hook-turning");

            // What UpdatePhotoCue does the frame the move ends.
            _watch.SetPhotoCue(default);
            Assert.IsFalse(_watch.PhotoCueVisible);
            Assert.AreEqual(-1, _watch.PhotoCueTask);
            Assert.IsFalse(_watch.CueContains(_watch.PhotoCueScreen), "a gone cue cannot be tapped");
        }

        // ------------------------------------------------------------ subjects

        [Test]
        public void CueSubject_IsTheHerdTheIconFloatsOver()
        {
            var subject = WatchSubjects.OfCue(_cue);
            Assert.IsNotNull(subject);
            Assert.AreSame(_herds, subject.herds);
            Assert.AreEqual(0, subject.herd);
            Assert.AreSame(_island, subject.ground);
            Assert.AreEqual(WatchTools.NameOf(LifeKind.Hare), subject.label);

            Assert.IsNull(WatchSubjects.OfHerd(_island, _herds, 9), "no such herd");
            Assert.IsNull(WatchSubjects.OfHerd(null, _herds, 0));
        }
    }
}
