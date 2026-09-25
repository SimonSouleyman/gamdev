using System.Collections.Generic;
using System.Linq;
using Drift.Bridge;
using Drift.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace Drift.Tests
{
    // Owner, 2026-09-25: the title has no "Weiter" of its own - "Gemütlich" asks "Weiter" / "Neu beginnen" in a small
    // window when a world is saved (closed by a tap outside, no "Abbrechen"), and the Anleitung inside a run only
    // explains that run's mode; the title's Anleitung keeps both.
    // Also: the way back to the title reads "Hauptmenü" (was "Home"), and Tilda is sorry, not cheerful, on "Versunken".
    // The test assembly has no uGUI reference, so buttons and texts are reached by type name and reflection.
    public class TitleMenuTests
    {
        readonly List<GameObject> _objects = new();

        [SetUp]
        public void SetUp() => GameModes.Set(GameMode.Cozy);

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            GameModes.Set(GameMode.Cozy);
        }

        SessionScreens MakeScreens()
        {
            var go = new GameObject("TitleMenuTest");
            _objects.Add(go);
            return go.AddComponent<SessionScreens>();
        }

        static Transform Find(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, "missing " + path);
            return t;
        }

        static Transform Canvas(SessionScreens s) => Find(s.transform, "SessionScreensCanvas");

        static Transform TitlePanel(SessionScreens s)
        {
            foreach (var t in Canvas(s).GetComponentsInChildren<Transform>(true))
                if (t.name == "TitleScreen") return Find(t, "Panel");
            Assert.Fail("no TitleScreen");
            return null;
        }

        static void Click(Transform t)
        {
            var button = t.GetComponent("Button");
            Assert.IsNotNull(button, t.name + " is no button");
            var onClick = (UnityEvent)button.GetType().GetProperty("onClick").GetValue(button);
            onClick.Invoke();
        }

        static IEnumerable<string> Texts(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Text")
                    yield return (string)c.GetType().GetProperty("text").GetValue(c);
        }

        static void SetSeedField(SessionScreens s, string text)
        {
            var field = typeof(SessionScreens).GetProperty("SeedField").GetValue(s);
            field.GetType().GetProperty("text").SetValue(field, text);
        }

        // ------------------------------------------------------------ title

        [Test]
        public void TheTitleHasNoWeiterButton()
        {
            var s = MakeScreens();
            var panel = TitlePanel(s);
            Assert.IsNull(panel.Find("Continue"), "the standalone Weiter is gone");
            CollectionAssert.DoesNotContain(Texts(panel).ToList(), "Weiter");
            Assert.IsNotNull(panel.Find("Start"), "Gemütlich");
            Assert.IsNotNull(panel.Find("Adventure"), "Abenteuer");
            // The extras moved up into the slot of the old Weiter: right under the mode hints, no gap.
            var extras = (RectTransform)Find(panel, "Extras");
            Assert.AreEqual(-1132f, extras.anchoredPosition.y, 0.5f);
            Assert.Less(((RectTransform)panel).sizeDelta.y, 1600f, "the panel shrank with it");
        }

        [Test]
        public void CozyWithoutASaveStartsANewWorldAtOnce()
        {
            var s = MakeScreens();
            int started = 0, continued = 0;
            s.SaveProbe = () => false;
            s.NewCozyWorldOverride = _ => started++;
            s.ContinueCozyOverride = () => continued++;
            Click(Find(TitlePanel(s), "Start"));
            Assert.AreEqual(1, started);
            Assert.AreEqual(0, continued);
            Assert.IsFalse(s.CozyChoiceOpen, "no window without a save");
        }

        [Test]
        public void CozyWithASaveAsksWeiterOrNeuBeginnenWithoutAbbrechen()
        {
            var s = MakeScreens();
            int started = 0;
            s.SaveProbe = () => true;
            s.NewCozyWorldOverride = _ => started++;
            Click(Find(TitlePanel(s), "Start"));
            Assert.IsTrue(s.CozyChoiceOpen);
            Assert.AreEqual(0, started, "nothing starts before a choice");

            var window = Find(s.CozyChoice.transform, "Window");
            var texts = Texts(window).ToList();
            CollectionAssert.Contains(texts, "Weiter");
            CollectionAssert.Contains(texts, "Neu beginnen");
            Assert.IsFalse(texts.Any(t => t != null && t.Contains("Abbrechen")), "no cancel button");
            Assert.IsNull(window.Find("Cancel"));
        }

        [Test]
        public void ATapOutsideTheWindowClosesIt()
        {
            var s = MakeScreens();
            int started = 0, continued = 0;
            s.SaveProbe = () => true;
            s.NewCozyWorldOverride = _ => started++;
            s.ContinueCozyOverride = () => continued++;
            s.PressCozy();
            var outside = (RectTransform)Find(s.CozyChoice.transform, "Outside");
            // Full screen, behind the window, catching the tap.
            Assert.AreEqual(Vector2.zero, outside.anchorMin);
            Assert.AreEqual(Vector2.one, outside.anchorMax);
            Assert.Less(outside.GetSiblingIndex(), Find(s.CozyChoice.transform, "Window").GetSiblingIndex());
            var image = outside.GetComponent("Image");
            Assert.IsTrue((bool)image.GetType().GetProperty("raycastTarget").GetValue(image));
            Assert.AreEqual(0f, ((Color)image.GetType().GetProperty("color").GetValue(image)).a, 1e-4f, "clear, not a dim");

            Click(outside);
            Assert.IsFalse(s.CozyChoiceOpen);
            Assert.AreEqual(0, started);
            Assert.AreEqual(0, continued);
        }

        [Test]
        public void WeiterContinuesAndNeuBeginnenTakesTheFieldsNewNumber()
        {
            var s = MakeScreens();
            int continued = 0;
            var starts = new List<int?>();
            s.SaveProbe = () => true;
            s.NewCozyWorldOverride = seed => starts.Add(seed);
            s.ContinueCozyOverride = () => continued++;

            s.PressCozy();
            Click(Find(s.CozyChoice.transform, "Window/Continue"));
            Assert.AreEqual(1, continued, "Weiter continues the saved world");
            Assert.AreEqual(0, starts.Count);
            Assert.IsFalse(s.CozyChoiceOpen);

            // With the saved world 4711 behind the title the field offers another number, and Neu beginnen builds it.
            int proposed = 0;
            int offer = SessionScreens.TitleFieldSeed(true, 4711, ref proposed);
            Assert.AreNotEqual(4711, offer);
            SetSeedField(s, offer.ToString());
            s.PressCozy();
            Click(Find(s.CozyChoice.transform, "Window/NewWorld"));
            Assert.AreEqual(1, starts.Count);
            Assert.AreEqual(offer, starts[0], "the new world is the offered number");
            Assert.AreEqual(1, continued);
            Assert.IsFalse(s.CozyChoiceOpen);

            // A typed number is taken exactly.
            SetSeedField(s, "123456");
            s.PressCozy();
            Click(Find(s.CozyChoice.transform, "Window/NewWorld"));
            Assert.AreEqual(123456, starts[1]);
        }

        [Test]
        public void TheChoiceWindowSitsInsideTheTitleScreen()
        {
            var s = MakeScreens();
            Assert.AreEqual("TitleScreen", s.CozyChoice.transform.parent.name, "hidden with the title (journal, album, run start)");
            StringAssert.Contains("ersetzt", ModeTexts.CozyChoiceNote, "the window says what Neu beginnen replaces");
            Assert.AreEqual("Welt #42", ModeTexts.SeedCaption(42, false));
            Assert.AreEqual("Welt: Standard", ModeTexts.SeedCaption(0, true));
        }

        // ------------------------------------------------------------ Anleitung per mode

        [Test]
        public void TheAnleitungScopeFollowsWhereItIsOpened()
        {
            Assert.AreEqual(HelpScope.Both, HelpScreen.ScopeFor(true, GameMode.Cozy));
            Assert.AreEqual(HelpScope.Both, HelpScreen.ScopeFor(true, GameMode.Adventure));
            Assert.AreEqual(HelpScope.Cozy, HelpScreen.ScopeFor(false, GameMode.Cozy));
            Assert.AreEqual(HelpScope.Adventure, HelpScreen.ScopeFor(false, GameMode.Adventure));

            Assert.IsTrue(HelpScreen.PagesFor(HelpScope.Cozy).All(p => HelpScreen.ModeOf(p) == GameMode.Cozy));
            Assert.IsTrue(HelpScreen.PagesFor(HelpScope.Adventure).All(p => HelpScreen.ModeOf(p) == GameMode.Adventure));
            Assert.AreEqual(HelpScreen.PageCount, HelpScreen.PagesFor(HelpScope.Both).Length);
            Assert.AreEqual(HelpScreen.PagesFor(HelpScope.Cozy).Length + HelpScreen.PagesFor(HelpScope.Adventure).Length, HelpScreen.PageCount);
        }

        [Test]
        public void ACozyRunShowsOnlyTheCozyPages()
        {
            var help = MakeScreens().Help;
            help.Open(HelpScope.Cozy, 0, false);
            Assert.IsFalse(help.TabsShown);
            Assert.AreEqual(5, help.VisiblePageCount);
            for (int i = 0; i < help.VisiblePageCount; i++)
            {
                help.Show(i);
                Assert.AreEqual(GameMode.Cozy, help.CurrentMode, "page " + i);
            }
            help.Show(99);
            Assert.AreEqual(HelpScreen.PageId.Life, help.CurrentPage, "the last cozy page ends it");
            Assert.IsFalse(help.Root.transform.Find("Panel/PageAdventureRing").gameObject.activeSelf);
        }

        [Test]
        public void AnAdventureRunShowsOnlyTheAdventurePages()
        {
            var help = MakeScreens().Help;
            help.Open(HelpScope.Adventure, 0, true);
            Assert.IsFalse(help.TabsShown);
            Assert.AreEqual(3, help.VisiblePageCount);
            Assert.AreEqual(HelpScreen.PageId.AdventureRing, help.CurrentPage);
            for (int i = 0; i < help.VisiblePageCount; i++)
            {
                help.Show(i);
                Assert.AreEqual(GameMode.Adventure, help.CurrentMode, "page " + i);
                Assert.IsFalse(help.Root.transform.Find("Panel/PageGoal").gameObject.activeSelf);
            }
        }

        [Test]
        public void TheTitleShowsBothWithTabs()
        {
            var s = MakeScreens();
            s.OpenHelp();
            var help = s.Help;
            Assert.AreEqual(HelpScope.Both, help.Scope, "no session = the title");
            Assert.IsTrue(help.TabsShown);
            Assert.AreEqual(HelpScreen.PageCount, help.VisiblePageCount);
            Assert.AreEqual(GameMode.Cozy, help.CurrentMode);
            help.ShowMode(GameMode.Adventure);
            Assert.AreEqual(HelpScreen.PageId.AdventureRing, help.CurrentPage, "the tab jumps to the first adventure page");
            Click(Find(help.Root.transform, "Panel/ModeTabs/TabCozy"));
            Assert.AreEqual(HelpScreen.PageId.Goal, help.CurrentPage);
            Click(Find(help.Root.transform, "Panel/ModeTabs/TabAdventure"));
            Assert.AreEqual(GameMode.Adventure, help.CurrentMode);

            // Reopened from a run it drops the tabs again.
            help.Open(HelpScope.Cozy, 0, false);
            Assert.IsFalse(help.TabsShown);
        }

        // ------------------------------------------------------------ Hauptmenü (owner 2026-09-25, was "Home")

        static Transform ScreenPanel(SessionScreens s, string screen)
        {
            foreach (var t in Canvas(s).GetComponentsInChildren<Transform>(true))
                if (t.name == screen) return Find(t, "Panel");
            Assert.Fail("no " + screen);
            return null;
        }

        static Component Label(Transform button)
        {
            foreach (var c in button.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Text") return c;
            Assert.Fail(button.name + " has no label");
            return null;
        }

        static float Left(RectTransform r) => r.anchoredPosition.x - r.sizeDelta.x * r.pivot.x;
        static float Right(RectTransform r) => r.anchoredPosition.x + r.sizeDelta.x * (1f - r.pivot.x);

        [Test]
        public void TheWayBackToTheTitleIsCalledHauptmenue()
        {
            var s = MakeScreens();
            Assert.AreEqual("Hauptmenü", SessionScreens.MainMenuLabel);
            var buttons = new[] { Find(ScreenPanel(s, "PauseScreen"), "Title"), Find(ScreenPanel(s, "GameOverScreen"), "AdventureButtons/Title") };
            foreach (var button in buttons)
            {
                CollectionAssert.AreEqual(new[] { "Hauptmenü" }, Texts(button).ToList(), button.parent.name);
                Assert.IsNotNull(button.Find("Icon"), "the house glyph stays");
            }
            foreach (var text in Texts(Canvas(s)))
                Assert.IsFalse(text != null && text.Contains("Home"), "left over: " + text);
        }

        [Test]
        public void HauptmenueFitsBesideNochmalOnTheGameOverScreen()
        {
            var panel = ScreenPanel(MakeScreens(), "GameOverScreen");
            var row = (RectTransform)Find(panel, "AdventureButtons");
            var again = (RectTransform)Find(row, "Again");
            var menu = (RectTransform)Find(row, "Title");
            Assert.LessOrEqual(Right(again) + 12f, Left(menu), "side by side, not overlapping");
            Assert.GreaterOrEqual(Left(again), -row.sizeDelta.x * 0.5f - 0.5f);
            Assert.LessOrEqual(Right(menu), row.sizeDelta.x * 0.5f + 0.5f);
            Assert.LessOrEqual(row.sizeDelta.x, ((RectTransform)panel).sizeDelta.x - 2f * 48f, "a margin to the panel edge");
            // The longer word gets the wider button: its label fits at (almost) full size behind the house icon.
            Assert.Greater(menu.sizeDelta.x, again.sizeDelta.x);
            foreach (var button in new[] { again, menu })
            {
                var label = Label(button);
                float preferred = (float)label.GetType().GetProperty("preferredWidth").GetValue(label);
                float room = ((RectTransform)label.transform).rect.width;
                Assert.Greater(preferred, 50f, "the font measured the text");
                Assert.LessOrEqual(preferred * 0.9f, room, button.name + $": {preferred:0} px text in {room:0} px");
            }
        }

        [Test]
        public void TildaIsSorryOnTheGameOverScreenInBothModes()
        {
            var update = typeof(SessionScreens).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var preview in new[] { SessionScreens.EditorPreview.GameOver, SessionScreens.EditorPreview.AdventureGameOver })
            {
                var s = MakeScreens();
                s.editorPreview = preview;
                update.Invoke(s, null);
                Assert.IsNotNull(s.Presenter?.View, "Tilda beside the panel");
                Assert.AreEqual(SessionScreens.GameOverPose, s.Presenter.View.Pose, preview.ToString());
            }
            Assert.AreNotEqual(TildaPose.Cheer, SessionScreens.GameOverPose, "no cheering over a sunk island");
            Assert.AreNotEqual(TildaPose.Wave, SessionScreens.GameOverPose);
        }
    }
}
