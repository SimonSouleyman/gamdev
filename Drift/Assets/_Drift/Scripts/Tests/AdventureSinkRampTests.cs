using Drift.Islands;
using NUnit.Framework;

namespace Drift.Tests
{
    // v0.6.7.2: the adventure sink rate grows with the distance and never caps (owner: a 12 km run was too easy).
    public class AdventureSinkRampTests
    {
        [Test]
        public void StartsAtTheBaseRate()
        {
            Assert.AreEqual(1.15f, RingWorld.SinkScaleFor(1.15f, 0.5f, 0f), 1e-4f);
        }

        [Test]
        public void HalfAsMuchAgainPerKilometre_NoCeiling()
        {
            Assert.AreEqual(1.15f * 2f, RingWorld.SinkScaleFor(1.15f, 0.5f, 2000f), 1e-4f);
            Assert.AreEqual(1.15f * 7f, RingWorld.SinkScaleFor(1.15f, 0.5f, 12000f), 1e-4f, "12 km sinks seven times as fast");
            Assert.Greater(RingWorld.SinkScaleFor(1.15f, 0.5f, 30000f), RingWorld.SinkScaleFor(1.15f, 0.5f, 12000f));
        }

        [Test]
        public void GainZeroKeepsTheOldFlatRate()
        {
            Assert.AreEqual(1.15f, RingWorld.SinkScaleFor(1.15f, 0f, 9000f), 1e-4f);
        }
    }
}
