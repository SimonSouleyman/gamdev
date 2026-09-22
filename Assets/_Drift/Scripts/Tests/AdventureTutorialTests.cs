using System.Collections.Generic;
using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class AdventureTutorialTests
    {
        static AdventureTutorialModel Started()
        {
            var m = new AdventureTutorialModel();
            m.Begin();
            return m;
        }

        [Test]
        public void BeginsAtTheRingWithSinkingHeldOnlyThere()
        {
            var m = Started();
            Assert.IsTrue(m.Active);
            Assert.AreEqual(AdventureTutorialStep.Ring, m.Step);
            Assert.AreEqual(1, m.StepNumber);
            Assert.IsTrue(m.SinkingSuspended);
            Assert.IsTrue(m.CanContinue);
            Assert.IsTrue(m.Continue());
            Assert.AreEqual(AdventureTutorialStep.Clock, m.Step);
            Assert.IsFalse(m.SinkingSuspended, "from the clock step on the island sinks");
        }

        [Test]
        public void ActionStepsWaitForTheirEventAndCannotBeClickedThrough()
        {
            var m = Started();
            m.Continue();
            m.Continue();
            Assert.AreEqual(AdventureTutorialStep.Flotsam, m.Step);
            Assert.IsFalse(m.CanContinue);
            Assert.IsFalse(m.Continue());
            Assert.IsTrue(m.ShowsArrow);

            m.ReportFlotsam();
            Assert.AreEqual(AdventureTutorialStep.Dodge, m.Step);
            Assert.IsTrue(m.Cheering);
            Assert.IsTrue(m.ShowsArrow, "the arrow now points at the island to go round");

            m.ReportDodge();
            Assert.AreEqual(AdventureTutorialStep.Surf, m.Step);

            m.ReportSurf(0.2f, 1f);
            Assert.AreEqual(AdventureTutorialStep.Surf, m.Step, "too weak to count");
            m.ReportSurf(0.8f, 0.3f);
            Assert.AreEqual(AdventureTutorialStep.Surf, m.Step, "not long enough yet");
            m.ReportSurf(0.8f, 0.4f);
            Assert.AreEqual(AdventureTutorialStep.Go, m.Step);
            Assert.AreEqual(AdventureTutorialModel.StepCount, m.StepNumber);
            Assert.IsTrue(m.CanContinue);
            Assert.IsTrue(m.Continue());
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.Active);
            Assert.IsFalse(m.Skipped);
        }

        [Test]
        public void WhatWasDoneEarlySkipsItsStep()
        {
            var m = Started();
            m.ReportFlotsam();
            m.ReportDodge();
            Assert.AreEqual(AdventureTutorialStep.Ring, m.Step, "the explanation is not cut short");
            m.Continue();
            m.Continue();
            Assert.AreEqual(AdventureTutorialStep.Surf, m.Step, "flotsam and dodging already done");
            Assert.IsTrue(m.Cheering);
        }

        [Test]
        public void EveryStepEndsByItselfSoTheRaceIsNeverHeldUp()
        {
            var m = Started();
            var seen = new List<AdventureTutorialStep>();
            m.StepChanged += s => seen.Add(s);
            float total = 0f;
            while (m.Active && total < 300f)
            {
                m.Tick(0.1f);
                total += 0.1f;
            }
            Assert.IsTrue(m.Done);
            float bound = 2f * m.explainSeconds + 3f * m.actionSeconds + m.goSeconds + 1f;
            Assert.Less(total, bound);
            CollectionAssert.AreEqual(new[]
            {
                AdventureTutorialStep.Clock, AdventureTutorialStep.Flotsam, AdventureTutorialStep.Dodge,
                AdventureTutorialStep.Surf, AdventureTutorialStep.Go, AdventureTutorialStep.Done,
            }, seen);
        }

        [Test]
        public void ATimeoutIsNotACheer()
        {
            var m = Started();
            m.Continue();
            m.Continue();
            m.Tick(m.actionSeconds + 0.01f);
            Assert.AreEqual(AdventureTutorialStep.Dodge, m.Step);
            Assert.IsTrue(m.TimedOut);
            Assert.IsFalse(m.Cheering);
        }

        [Test]
        public void SkipFinishesOnceAndEventsFire()
        {
            var m = new AdventureTutorialModel();
            int finished = 0;
            m.Finished += () => finished++;
            m.Skip();
            Assert.AreEqual(0, finished, "nothing to skip before it begins");
            m.Begin();
            m.Skip();
            m.Skip();
            Assert.AreEqual(1, finished);
            Assert.IsTrue(m.Skipped);
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.SinkingSuspended);
            m.ReportDodge();
            m.Tick(100f);
            Assert.AreEqual(AdventureTutorialStep.Done, m.Step);
        }

        [Test]
        public void MarkDoneRestoresSilently()
        {
            var m = new AdventureTutorialModel();
            int changes = 0;
            m.StepChanged += _ => changes++;
            m.MarkDone();
            Assert.IsTrue(m.Done);
            Assert.IsFalse(m.Active);
            Assert.AreEqual(0, changes);
            m.Begin();
            Assert.IsTrue(m.Active, "a replay begins again");
        }

        [Test]
        public void ProgressPersistsUnderAScratchKey()
        {
            string real = AdventureTutorialProgress.Key;
            Assert.AreEqual(AdventureTutorialProgress.DefaultKey, real);
            bool realExisted = PlayerPrefs.HasKey(AdventureTutorialProgress.DefaultKey);
            int realValue = PlayerPrefs.GetInt(AdventureTutorialProgress.DefaultKey, 0);
            AdventureTutorialProgress.Key = "drift_test_adventure_tutorial_" + System.Guid.NewGuid().ToString("N");
            try
            {
                Assert.IsFalse(AdventureTutorialProgress.IsDone);
                AdventureTutorialProgress.SetDone(true);
                Assert.IsTrue(AdventureTutorialProgress.IsDone);
                AdventureTutorialProgress.SetDone(false);
                Assert.IsFalse(AdventureTutorialProgress.IsDone);
                Assert.IsFalse(PlayerPrefs.HasKey(AdventureTutorialProgress.Key));
            }
            finally
            {
                PlayerPrefs.DeleteKey(AdventureTutorialProgress.Key);
                AdventureTutorialProgress.Key = real;
            }
            Assert.AreEqual(realExisted, PlayerPrefs.HasKey(AdventureTutorialProgress.DefaultKey), "the owner's flag is untouched");
            Assert.AreEqual(realValue, PlayerPrefs.GetInt(AdventureTutorialProgress.DefaultKey, 0));
        }

        [Test]
        public void TextsArePunchyAndMarkTheirKeyWords()
        {
            for (var s = AdventureTutorialStep.Ring; s < AdventureTutorialStep.Done; s++)
                foreach (bool touch in new[] { false, true })
                {
                    string text = AdventureTutorialGuide.TextFor(s, touch);
                    Assert.IsFalse(string.IsNullOrEmpty(text), s.ToString());
                    StringAssert.Contains("<b>", text, s + " marks a key word");
                    Assert.LessOrEqual(TildaBubble.Pages(text, TildaBubble.MaxCharsFor(1000f)).Count, 2, s + " is short");
                    Assert.Less(TildaBubble.Plain(text).Length, 190, s.ToString());
                }
            Assert.AreEqual("", AdventureTutorialGuide.TextFor(AdventureTutorialStep.Done, false));
        }

        [Test]
        public void PosesFollowTheMood()
        {
            Assert.AreEqual(TildaPose.Wave, AdventureTutorialGuide.PoseFor(AdventureTutorialStep.Ring, false, true, false));
            Assert.AreEqual(TildaPose.Cheer, AdventureTutorialGuide.PoseFor(AdventureTutorialStep.Surf, true, true, false));
            Assert.AreEqual(TildaPose.Cheer, AdventureTutorialGuide.PoseFor(AdventureTutorialStep.Go, false, false, false));
            Assert.AreEqual(TildaPose.Present, AdventureTutorialGuide.PoseFor(AdventureTutorialStep.Dodge, false, true, true));
            Assert.AreEqual(TildaPose.Idle, AdventureTutorialGuide.PoseFor(AdventureTutorialStep.Dodge, false, false, true));
        }

        [Test]
        public void AdventureRemarksHaveVariantsAndFallBack()
        {
            Assert.AreEqual("adv_gameover", TildaVoiceLines.ForMode(TildaVoiceLines.GameOver, true));
            Assert.AreEqual(TildaVoiceLines.GameOver, TildaVoiceLines.ForMode(TildaVoiceLines.GameOver, false));
            Assert.AreEqual(TildaVoiceLines.VoiceOn, TildaVoiceLines.ForMode(TildaVoiceLines.VoiceOn, true), "no variant: the usual line");
            Assert.IsNotNull(TildaVoiceLines.TextOf(TildaVoiceLines.AdventureRecord));
        }

        [Test]
        public void ShadesArePartOfTheModelAndOnlyOneAccessoryShows()
        {
            var host = new GameObject("TildaShadesProbe") { hideFlags = HideFlags.HideAndDontSave };
            var material = TildaModel.CreateMaterial();
            try
            {
                var parts = TildaModel.Build(host.transform, material, 0);
                Assert.IsNotNull(parts.shades);
                Assert.AreEqual(TildaAccessory.ReadingGlasses, parts.accessory);
                Assert.IsTrue(parts.glasses.gameObject.activeSelf);
                Assert.IsFalse(parts.shades.gameObject.activeSelf, "cozy Tilda is unchanged");

                TildaModel.SetAccessory(parts, TildaAccessory.SportShades);
                Assert.IsFalse(parts.glasses.gameObject.activeSelf);
                Assert.IsTrue(parts.shades.gameObject.activeSelf);
                Assert.IsFalse(parts.ballL.parent.gameObject.activeSelf, "eyes behind the mirror are off");
                TildaModel.SetAccessory(parts, TildaAccessory.ReadingGlasses);
                Assert.IsTrue(parts.ballR.parent.gameObject.activeSelf);
                TildaModel.SetAccessory(parts, TildaAccessory.SportShades);

                // The shimmer repaints the lens; a streak makes it brighter where it passes.
                var animator = new TildaAnimator(parts);
                animator.Apply(TildaPose.Idle, false, 0.62f, 0f, true);
                Color rest = TildaModel.ShadesColor(new Vector2(0.3f, 0.5f), 0f, 3f);
                Color lit = TildaModel.ShadesColor(new Vector2(0.3f, 0.5f), 0f, 0.3f);
                Assert.Greater(lit.grayscale, rest.grayscale + 0.1f);
                Color bottom = TildaModel.ShadesColor(new Vector2(0f, 0.1f), 0f, 3f).gamma;
                Color top = TildaModel.ShadesColor(new Vector2(0f, 0.9f), 0f, 3f).gamma;
                Assert.Greater(bottom.r, bottom.b, "warm at the bottom");
                Assert.Greater(top.b, top.r, "blue at the top");

                // The lens stands in front of her eyes and covers them.
                for (float u = -0.6f; u <= 0.6f; u += 0.3f)
                    Assert.Less(TildaModel.ShadesBottom(u), TildaModel.ShadesTop(u) - 0.15f);
                Assert.Less(parts.vertexCount, 20000);
            }
            finally
            {
                foreach (var f in host.GetComponentsInChildren<MeshFilter>(true)) Object.DestroyImmediate(f.sharedMesh);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(material);
            }
        }
    }
}
