using System;
using System.IO;
using System.Text.RegularExpressions;
using Drift.Islands;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class RunStartTimeTests
    {
        static readonly long Now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc).Ticks;
        static long Ago(double seconds) => Now - (long)(seconds * TimeSpan.TicksPerSecond);

        [Test]
        public void WithoutAStoredStart_TheStartIsNowMinusThePlayTime()
        {
            Assert.AreEqual(Ago(600), RunStartTime.Resolve(0, 0f, Now, 600f));
            Assert.AreEqual(Now, RunStartTime.Resolve(0, 0f, Now, -5f));
        }

        [Test]
        public void AStoredStart_SurvivesAnAppRestart()
        {
            // Played 10 min, closed the app for an hour, played 10 more: the estimate would miss the first photos.
            long stored = Ago(4800);
            Assert.AreEqual(stored, RunStartTime.Resolve(stored, 600f, Now, 1200f));
        }

        [Test]
        public void AStoredStart_IsNeverLaterThanTheEstimate()
        {
            Assert.AreEqual(Ago(1200), RunStartTime.Resolve(Ago(100), 60f, Now, 1200f));
        }

        [Test]
        public void AStaleOrImpossibleStoredStart_FallsBackToTheEstimate()
        {
            Assert.AreEqual(Ago(30), RunStartTime.Resolve(Ago(90000), 3600f, Now, 30f), "play time went back: a new run");
            Assert.AreEqual(Ago(30), RunStartTime.Resolve(Now + TimeSpan.TicksPerHour, 0f, Now, 30f), "a start in the future");
        }

        [Test]
        public void AFileFromV067_HasNoStart_AndStillReads()
        {
            var data = new SaveGame
            {
                worldGenVersion = WorldStreamer.WorldGenVersion,
                runStartUtcTicks = Ago(3000),
                player = new IslandSaveData { heights = new float[4], nx = 2, nz = 2 },
            };
            data.stats.timeSurvived = 900f;
            string json = JsonUtility.ToJson(data);
            Assert.AreEqual(data.runStartUtcTicks, JsonUtility.FromJson<SaveGame>(json).runStartUtcTicks);

            string old = Regex.Replace(json, "\"runStartUtcTicks\":-?[0-9]+,?", "");
            StringAssert.DoesNotContain("runStartUtcTicks", old);
            var back = JsonUtility.FromJson<SaveGame>(old);
            Assert.AreEqual(0L, back.runStartUtcTicks);
            Assert.AreEqual(900f, back.stats.timeSurvived);
            Assert.AreEqual(Ago(900), RunStartTime.Resolve(back.runStartUtcTicks, back.stats.timeSurvived, Now, back.stats.timeSurvived));

            string path = Path.Combine(Path.GetTempPath(), "drift_runstart_test.json");
            try
            {
                File.WriteAllText(path, old);
                Assert.IsTrue(SaveManager.Peek(path, out _, out _), "an old file is still a valid save");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
