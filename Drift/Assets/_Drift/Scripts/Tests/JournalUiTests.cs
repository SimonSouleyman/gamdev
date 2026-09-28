using System;
using System.Collections.Generic;
using Drift.Bridge;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Tests
{
    // v0.6.5 UI round: the steady animal card, the journal cards without the "Ansehen" icon and with a big photo slot,
    // the reset dialog, and a new world on every run.
    public class JournalUiTests
    {
        // ------------------------------------------------------------ steady animal card

        [Test]
        public void SteadyCard_DoesNotMoveWhileTheAnimalWobblesInsideTheDeadZone()
        {
            var pos = new Vector2(200f, 400f);
            var vel = Vector2.zero;
            var zone = new Vector2(110f, 80f);
            var start = pos;
            for (int f = 0; f < 600; f++)
            {
                // Hops, steps and a swaying camera: +-60 x, +-40 y around the start.
                var target = start + new Vector2(60f * Mathf.Sin(f * 0.7f), 40f * Mathf.Sin(f * 1.9f + 1f));
                pos = WatchTools.SteadyStep(pos, target, ref vel, zone, 0.35f, 1f / 60f);
            }
            Assert.AreEqual(start, pos);
        }

        [Test]
        public void SteadyCard_GlidesAfterAnAnimalThatWalksAwayWithoutGoingBack()
        {
            var pos = Vector2.zero;
            var vel = Vector2.zero;
            var zone = new Vector2(110f, 80f);
            float lastX = pos.x, maxStep = 0f;
            for (int f = 0; f < 300; f++)
            {
                // Walks right at 200 units/s with a hop of 30 on top.
                var target = new Vector2(200f * f / 60f, 30f * Mathf.Abs(Mathf.Sin(f * 0.5f)));
                pos = WatchTools.SteadyStep(pos, target, ref vel, zone, 0.35f, 1f / 60f);
                Assert.GreaterOrEqual(pos.x, lastX - 1e-4f, "the card never swings back");
                maxStep = Mathf.Max(maxStep, pos.x - lastX);
                lastX = pos.x;
                Assert.LessOrEqual(target.x - pos.x, zone.x + 200f * 0.35f + 10f, "it keeps up with the animal (lag of a critically damped follow)");
            }
            Assert.AreEqual(0f, pos.y, 1e-3f, "the hops stay inside the zone");
            Assert.Less(maxStep, 5f, "a smooth glide, no jumps");
        }

        [Test]
        public void SteadyCard_SnapsToWholePixels()
        {
            var p = WatchTools.SnapToPixels(new Vector2(10.26f, -3.74f), 0.5f);
            Assert.AreEqual(new Vector2(10f, -4f), p);
            Assert.AreEqual(new Vector2(10f, -4f), WatchTools.SnapToPixels(new Vector2(10.2f, -3.8f), 1f));
        }

        // ------------------------------------------------------------ new world every run

        [Test]
        public void NewWorld_RandomOtherNeverRepeatsTheWorld()
        {
            for (int i = 0; i < 200; i++)
            {
                int s = WorldSeeds.RandomOther(i % 3 == 0 ? 5 : i);
                Assert.AreNotEqual(i % 3 == 0 ? 5 : i, s);
                Assert.Greater(s, 0);
            }
        }

        [Test]
        public void NewWorld_TitleFieldOffersANewNumberWhileASavedRunWaits()
        {
            int proposed = 0;
            Assert.AreEqual(4711, SessionScreens.TitleFieldSeed(false, 4711, ref proposed), "a fresh world behind the title is what Start builds");
            int offer = SessionScreens.TitleFieldSeed(true, 4711, ref proposed);
            Assert.AreNotEqual(4711, offer, "the saved run's world is for Weiter, Start gets a new one");
            Assert.AreEqual(offer, SessionScreens.TitleFieldSeed(true, 4711, ref proposed), "the offer stays until it is used");
            proposed = 4711;
            Assert.AreNotEqual(4711, SessionScreens.TitleFieldSeed(true, 4711, ref proposed));

            Assert.IsTrue(SessionScreens.ReturnedFromRun(GameSession.State.RunComplete));
            Assert.IsTrue(SessionScreens.ReturnedFromRun(GameSession.State.Paused));
            Assert.IsTrue(SessionScreens.ReturnedFromRun(GameSession.State.GameOver));
            Assert.IsFalse(SessionScreens.ReturnedFromRun((GameSession.State)(-1)), "the app's first title keeps its world");
            Assert.IsFalse(SessionScreens.ReturnedFromRun(GameSession.State.Title));
        }

        // ------------------------------------------------------------ journal cards

        GameObject _host;
        JournalPanel _panel;
        readonly List<int> _watched = new();
        readonly List<JournalResetScope> _resets = new();

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.DestroyImmediate(_host);
            _host = null;
            _watched.Clear();
            _resets.Clear();
        }

        DiscoveryJournal BuildPanel(PhotoTaskBook tasks)
        {
            _host = new GameObject("JournalUiTest", typeof(RectTransform));
            _host.hideFlags = HideFlags.HideAndDontSave;
            var root = (RectTransform)_host.transform;
            root.sizeDelta = new Vector2(1080f, 1920f);
            _panel = new JournalPanel();
            _panel.Build(root, null, _watched.Add, _resets.Add);
            var j = new DiscoveryJournal();
            foreach (var i in CollectionCatalog.EntriesOf(CollectSection.Tropical)) j.MarkCollected(i, 10f);
            foreach (var i in CollectionCatalog.EntriesOf(CollectSection.Tropical)) j.SetCount(i, 18);
            j.ReportPresence(0UL, 11f);
            _panel.SetPhotoTasks(tasks, null);
            _panel.Fill(j, null, default);
            _panel.SetTab((int)CollectSection.Tropical);
            return j;
        }

        static Rect LocalRect(RectTransform rt, RectTransform space)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 a = space.InverseTransformPoint(c[0]), b = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        static float RenderedRight(Text t, RectTransform space) => LocalRect(t.rectTransform, space).xMin + t.preferredWidth * t.rectTransform.localScale.x;

        [Test]
        public void Card_PhotoSlotIsBigAndClearOfTheTexts()
        {
            var tasks = new PhotoTaskBook(null);
            BuildPanel(tasks);
            int flamingo = CollectionCatalog.IndexOf(LifeKind.Flamingo);
            Assert.IsTrue(_panel.CardRects(flamingo, out var card, out var slot, out var name, out var detail, out var when));
            Assert.IsTrue(slot.gameObject.activeSelf);
            var s = LocalRect(slot, card);
            var look = LocalRect((RectTransform)slot.Find("Look"), card);
            Assert.GreaterOrEqual(look.width, 100f, "a slot of at least 100 units");
            Assert.GreaterOrEqual(s.width, 110f, "the tap area is wider still");
            Assert.AreEqual(card.rect.height, s.height, 0.5f, "the tap area is the full card height");
            Assert.AreEqual(0f, look.center.y, 0.5f, "vertically centred");
            Assert.Greater(s.xMax, card.rect.xMax - 10f, "at the right edge");
            foreach (var t in new[] { name, detail, when })
            {
                Assert.LessOrEqual(LocalRect(t.rectTransform, card).xMax, s.xMin, t.text + ": layout rect runs into the slot");
                Assert.LessOrEqual(RenderedRight(t, card), s.xMin + 1f, t.text + ": rendered text runs into the slot");
            }
            Assert.IsNull(card.Find("Watch"), "the Ansehen icon is gone");

            // The longest texts a card can show still end before the slot.
            detail.text = "selten · noch nicht auf deiner Insel";
            Canvas.ForceUpdateCanvases();
            Assert.LessOrEqual(RenderedRight(detail, card), s.xMin + 1f, "long detail");
            detail.text = "18 auf deiner Insel  ·  Rekord 124";
            Assert.LessOrEqual(RenderedRight(detail, card), s.xMin + 1f, "record detail");
        }

        [Test]
        public void Card_SlotTapShowsTheTaskAndNeverWatches()
        {
            var tasks = new PhotoTaskBook(null);
            BuildPanel(tasks);
            int flamingo = CollectionCatalog.IndexOf(LifeKind.Flamingo);
            int task = PhotoTaskCatalog.IndexOf(LifeKind.Flamingo);
            Assert.IsTrue(_panel.TapSlot(flamingo));
            Assert.AreEqual(0, _watched.Count, "the slot is not the card's watch button");
            Assert.AreEqual(task, _panel.DetailTask);
            Assert.AreEqual("Fotografiere Flamingos beim Schlammtanz.", _panel.DetailText);
            Assert.IsTrue(_panel.Back(), "back closes the task card first");
            Assert.AreEqual(-1, _panel.DetailTask);
            Assert.IsFalse(_panel.Back());

            Assert.IsTrue(_panel.TapCard(flamingo));
            CollectionAssert.AreEqual(new[] { flamingo }, _watched, "the rest of the card still watches");

            _panel.ShowTask(task);
            Assert.AreEqual(JournalPanel.TasksTab, _panel.Tab);
            Assert.AreEqual(task / _panel.Capacity, _panel.Page);
            Assert.AreEqual(task, _panel.HighlightTask);
        }

        [Test]
        public void Card_TaskLinesReadAsGerman()
        {
            Assert.AreEqual("Fotografiere Hasen beim Hakenschlagen.", JournalPanel.TaskLine(PhotoTaskCatalog.IndexOf(LifeKind.Hare)));
            for (int i = 0; i < PhotoTaskCatalog.Count; i++)
            {
                string line = JournalPanel.TaskLine(i);
                StringAssert.StartsWith("Fotografiere ", line);
                StringAssert.Contains(PhotoTaskCatalog.At(i).phrase, line);
            }
        }

        [Test]
        public void Reset_ButtonOnTheIslandPageAndAConfirmationFirst()
        {
            BuildPanel(new PhotoTaskBook(null));
            Assert.IsFalse(_panel.ResetButtonShown, "not on a species page");
            _panel.SetTab(JournalPanel.IslandTab);
            Assert.IsTrue(_panel.ResetButtonShown);
            _panel.OpenResetConfirm();
            Assert.IsTrue(_panel.ResetConfirmOpen);
            Assert.AreEqual(0, _resets.Count, "opening the dialog resets nothing");
            Assert.IsTrue(_panel.Back());
            Assert.IsFalse(_panel.ResetConfirmOpen);
            Assert.AreEqual(0, _resets.Count, "cancel resets nothing");

            _panel.OpenResetConfirm();
            _panel.ConfirmReset(JournalResetScope.SpeciesAndTasks);
            _panel.OpenResetConfirm();
            _panel.ConfirmReset(JournalResetScope.Everything);
            CollectionAssert.AreEqual(new[] { JournalResetScope.SpeciesAndTasks, JournalResetScope.Everything }, _resets);
            Assert.IsFalse(_panel.OverlayOpen);
        }
    }
}
