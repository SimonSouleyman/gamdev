using Drift.Core;
using NUnit.Framework;

namespace Drift.Tests
{
    public class FrameStatsLoggerTests
    {
        [Test]
        public void Summary_ReportsAverageP95MaxAndSlowFrames()
        {
            var ms = new float[100];
            for (int i = 0; i < ms.Length; i++) ms[i] = 16.7f;
            ms[10] = 50f;
            ms[20] = 40f;
            string s = FrameStatsLogger.Summary(ms, 100, 1.7f);
            StringAssert.StartsWith("Drift-FPS avg 58.8", s);
            StringAssert.Contains("max 50.0ms", s);
            StringAssert.Contains("slow 2/100", s);
            Assert.AreEqual("Drift-FPS none", FrameStatsLogger.Summary(ms, 0, 1f));
        }
    }
}
