using Drift.Bridge;
using Drift.Core;
using Drift.SaveSystem;
using NUnit.Framework;

namespace Drift.Tests
{
    // The slim adventure strip, the labelled cozy panel, the new-world question and the seal's album entry.
    public class HudAndSealTests
    {
        [Test]
        public void Hud_CozyRowsSayWhatTheBarsMean()
        {
            Assert.AreEqual("Auftrieb", ModeTexts.BuoyancyLabel);
            Assert.AreEqual("Welt", ModeTexts.WorldLabel);
            Assert.AreEqual("3 von 20 Inseln", ModeTexts.CozyIslands(3, 20));
            Assert.AreEqual("alle vereint", ModeTexts.CozyIslands(20, 20));
            Assert.AreEqual("", ModeTexts.CozyIslands(0, 0));
            Assert.AreEqual("sinkt langsam", ModeTexts.CozySinkStatus(true));
            Assert.AreEqual("schwimmt ruhig", ModeTexts.CozySinkStatus(false));
            // Same switch point as the old "Form NN %" (rounded to 5 %, länglich below 50).
            Assert.AreEqual("Form: rund", ModeTexts.FormLabel(0.85f));
            Assert.AreEqual("Form: rund", ModeTexts.FormLabel(0.48f));
            Assert.AreEqual("Form: länglich", ModeTexts.FormLabel(0.47f));
            StringAssert.Contains("vereine Inseln", ModeTexts.ExplainSinking);
            StringAssert.DoesNotContain("Tiefgang", ModeTexts.ExplainBuoyancy);
        }

        // Volcanoes are no world slots: listed beside the slot count, never folded into it.
        [Test]
        public void Hud_WorldProgressListsVolcanoesApart()
        {
            Assert.AreEqual("5 von 20 Inseln", ModeTexts.WorldProgressLabel(5, 20, 0));
            Assert.AreEqual("5 von 20 Inseln · 3 Vulkane", ModeTexts.WorldProgressLabel(5, 20, 3));
            Assert.AreEqual("5 von 20 Inseln · 1 Vulkan", ModeTexts.WorldProgressLabel(5, 20, 1));
            Assert.AreEqual("0 von 20 Inseln · 2 Vulkane", ModeTexts.WorldProgressLabel(0, 20, 2));
            Assert.AreEqual("5 von 20 Inseln", ModeTexts.WorldProgressLabel(5, 20, -4), "a negative count is no count");
            Assert.AreEqual("alle vereint", ModeTexts.WorldProgressLabel(20, 20, 3), "the Pangäa keeps its line");
            Assert.AreEqual("alle vereint", ModeTexts.WorldProgressLabel(21, 20, 0));
            Assert.AreEqual("", ModeTexts.WorldProgressLabel(0, 0, 3));
            Assert.AreEqual(ModeTexts.CozyIslands(7, 20), ModeTexts.WorldProgressLabel(7, 20, 0));
        }

        [Test]
        public void Hud_AdventureStartLineIsOneShortGermanSentence()
        {
            string s = ModeTexts.AdventureStartLine;
            StringAssert.Contains("Treibgut", s);
            StringAssert.Contains("Inseln", s);
            StringAssert.EndsWith("?", s);
            StringAssert.DoesNotContain("\n", s, "the panel wraps it");
            Assert.LessOrEqual(s.Length, 64, "two lines under the strip at most");
            Assert.AreEqual("Los!", ModeTexts.GoLabel);
            Assert.AreEqual("Schwung", ModeTexts.MomentumLabel);
        }

        [Test]
        public void Hud_AdventureTextsAreGermanAndShort()
        {
            Assert.AreEqual("Schub  ×1,6", ModeTexts.BoostLabel(1.6f));
            Assert.AreEqual("Schub  ×1,3", ModeTexts.BoostLabel(1.25f));
            foreach (bool touch in new[] { false, true })
            {
                string hint = ModeTexts.SteerHint(GameMode.Adventure, touch, true);
                StringAssert.DoesNotContain("Tempo", hint, "the race drives itself; there is no speed to set");
                Assert.LessOrEqual(hint.Length, 45, "fits the small chip beside the small map (FitWidth shrinks the longest ones slightly)");
            }
            Assert.Less(ModeTexts.LowBuoyancy, ModeTexts.HeavyBuoyancy);
            StringAssert.Contains("Treibgut", ModeTexts.LowBuoyancyCall);
            StringAssert.Contains("Rekord", ModeTexts.AdventurePauseNote);
            StringAssert.DoesNotContain("Bestzeit", ModeTexts.AdventurePauseNote);
        }

        [Test]
        public void Menu_NewWorldQuestion()
        {
            Assert.AreEqual("Neue Welt beginnen?", ModeTexts.NewWorldTitle);
            StringAssert.Contains("verloren", ModeTexts.NewWorldBody);
            Assert.AreEqual("Abbrechen", ModeTexts.NewWorldCancel);
            Assert.AreEqual("Neu beginnen", ModeTexts.NewWorldConfirm);
        }

        [Test]
        public void Journal_SealHasAnEntryAGlyphAndAStableId()
        {
            Assert.AreEqual("Robbe", SeaNames.German(SeaKind.Seal));
            int i = CollectionCatalog.IndexOf(SeaKind.Seal);
            Assert.GreaterOrEqual(i, 0);
            var e = CollectionCatalog.At(i);
            Assert.AreEqual(CollectSection.SeaSky, e.section);
            Assert.AreEqual(CollectType.SeaAnimal, e.type);
            Assert.IsFalse(e.collectible, "seen at sea, never collected");
            Assert.AreEqual(CollectionCatalog.SeaIdBase + (int)SeaKind.Seal, e.id);
            Assert.AreEqual(i, CollectionCatalog.IndexOfId(e.id));
            Assert.AreEqual("taucht an Inselküsten auf", CollectionCatalog.HintOf(e));
            Assert.AreEqual(JournalGlyph.Seal, JournalGlyphs.For(e));
            Assert.IsNotNull(JournalGlyphs.Of(JournalGlyph.Seal));
            // The coast visitors of the sea show map onto entries the album already had.
            Assert.GreaterOrEqual(CollectionCatalog.IndexOf(SeaKind.SeaTurtle), 0);
            Assert.GreaterOrEqual(CollectionCatalog.IndexOf(SeaKind.Ray), 0);
            Assert.GreaterOrEqual(CollectionCatalog.IndexOf(SeaKind.FlyingFish), 0);
            Assert.GreaterOrEqual(CollectionCatalog.IndexOf(SeaKind.Dolphin), 0);
        }
    

        // v0.6.8: the season as a badge in the cozy head row; hidden without a year.
        [Test]
        public void Hud_SeasonLabelHiddenWithoutProvider()
        {
            var saved = LifeEnvironment.SeasonProvider;
            try
            {
                LifeEnvironment.SeasonProvider = null;
                Assert.AreEqual(-1f, LifeEnvironment.Season);
                Assert.AreEqual(-1, WorldHud.SeasonIndex(LifeEnvironment.Season));
                Assert.AreEqual("", WorldHud.SeasonLabel(LifeEnvironment.Season));
                Assert.AreEqual("", WorldHud.SeasonLabel(float.NaN));

                LifeEnvironment.SeasonProvider = () => 0.5f;
                Assert.AreEqual("Herbst", WorldHud.SeasonLabel(LifeEnvironment.Season));
            }
            finally
            {
                LifeEnvironment.SeasonProvider = saved;
            }
            Assert.AreEqual("Frühling", WorldHud.SeasonLabel(0f));
            // Fixed quarters, the same boundaries as IslandHerdSystem.SeasonOf (the winter huddle starts with the badge).
            Assert.AreEqual("Frühling", WorldHud.SeasonLabel(0.24f));
            Assert.AreEqual("Sommer", WorldHud.SeasonLabel(0.25f));
            Assert.AreEqual("Sommer", WorldHud.SeasonLabel(0.49f));
            Assert.AreEqual("Herbst", WorldHud.SeasonLabel(0.5f));
            Assert.AreEqual("Winter", WorldHud.SeasonLabel(0.75f));
            Assert.AreEqual("Winter", WorldHud.SeasonLabel(0.9f));
            Assert.AreEqual("Frühling", WorldHud.SeasonLabel(1f));
        }
    }
}
