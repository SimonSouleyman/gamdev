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

        [Test]
        public void Hud_AdventureTextsAreGermanAndShort()
        {
            Assert.AreEqual("Schub  ×1,6", ModeTexts.BoostLabel(1.6f));
            Assert.AreEqual("Schub  ×1,3", ModeTexts.BoostLabel(1.25f));
            foreach (bool touch in new[] { false, true })
            {
                string hint = ModeTexts.SteerHint(GameMode.Adventure, touch, true);
                StringAssert.DoesNotContain("Tempo", hint, "the race drives itself; there is no speed to set");
                Assert.LessOrEqual(hint.Length, 40, "fits the small chip beside the small map");
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
    }
}
