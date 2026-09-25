using Drift.Bridge;
using NUnit.Framework;

namespace Drift.Tests
{
    public class LastIslandHintTests
    {
        [Test]
        public void LastIsland_OutranksEverything_AndHasItsOwnLabel()
        {
            float last = IslandHintLogic.Rank(IslandHintKind.LastIsland, 500f);
            Assert.Less(last, IslandHintLogic.Rank(IslandHintKind.NewSpecies, 0f));
            Assert.Less(last, IslandHintLogic.Rank(IslandHintKind.Volcano, 0f));
            Assert.AreEqual("Letzte Insel", IslandHintLogic.LabelOf(IslandHintKind.LastIsland, IslandHintEvent.None));
            Assert.IsNotNull(HintGlyphs.Of(HintGlyph.Island));
        }
    }
}
