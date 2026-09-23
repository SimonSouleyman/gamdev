using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class PangaeaFinaleTests
    {
        [Test]
        public void AllMerged_OnlyInCozyWhilePlayingWithAnEmptyWorld()
        {
            Assert.IsTrue(PangaeaFinale.AllMerged(true, true, 20, 0));
            Assert.IsFalse(PangaeaFinale.AllMerged(true, true, 20, 1), "one island left");
            Assert.IsFalse(PangaeaFinale.AllMerged(false, true, 20, 0), "adventure mode");
            Assert.IsFalse(PangaeaFinale.AllMerged(true, false, 20, 0), "paused / title / game over");
            Assert.IsFalse(PangaeaFinale.AllMerged(true, true, 0, 0), "a world without islands is not a Pangäa");
        }

        [Test]
        public void Confirm_NeedsTheConditionForTheWholeHoldTime()
        {
            float held = 0f;
            Assert.IsFalse(PangaeaFinale.Confirm(true, ref held, 0.5f, 1.5f));
            Assert.IsFalse(PangaeaFinale.Confirm(true, ref held, 0.5f, 1.5f));
            Assert.IsTrue(PangaeaFinale.Confirm(true, ref held, 0.5f, 1.5f));
        }

        [Test]
        public void Confirm_ResetsWhenTheConditionBreaks()
        {
            float held = 0f;
            PangaeaFinale.Confirm(true, ref held, 1f, 1.5f);
            Assert.IsFalse(PangaeaFinale.Confirm(false, ref held, 1f, 1.5f));
            Assert.AreEqual(0f, held);
            Assert.IsFalse(PangaeaFinale.Confirm(true, ref held, 1f, 1.5f));
            Assert.IsTrue(PangaeaFinale.Confirm(true, ref held, 1f, 1.5f));
            float h2 = 0f;
            Assert.IsFalse(PangaeaFinale.Confirm(true, ref h2, -5f, 0.5f), "negative dt never counts");
            Assert.AreEqual(0f, h2);
        }

        [Test]
        public void GlobeRadius_ScalesWithThePangaeaWithinLimits()
        {
            Assert.AreEqual(60f, PangaeaFinale.GlobeRadius(10f, 1.9f, 60f, 260f), 1e-4f);
            Assert.AreEqual(95f, PangaeaFinale.GlobeRadius(50f, 1.9f, 60f, 260f), 1e-4f);
            Assert.AreEqual(260f, PangaeaFinale.GlobeRadius(500f, 1.9f, 60f, 260f), 1e-4f);
        }

        [Test]
        public void OrbitDistance_MakesTheSphereSpanTheRequestedAngle()
        {
            const float r = 100f, tilt = 9f, angle = 18f;
            float l = PangaeaFinale.OrbitDistance(r, tilt, angle);
            PangaeaFinale.OrbitPose(Vector3.zero, 30f, tilt, l, out Vector3 cam, out Quaternion rot);
            var centre = new Vector3(0f, -r, 0f);
            float seen = Mathf.Asin(r / Vector3.Distance(cam, centre)) * Mathf.Rad2Deg;
            Assert.AreEqual(angle, seen, 0.01f);
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, (Vector3.zero - cam).normalized), 0.9999f, "looks at the Pangäa");
            Assert.AreEqual(Mathf.Cos(tilt * Mathf.Deg2Rad) * l, cam.y, 1e-3f);
        }

        [Test]
        public void OrbitPose_StraightDownStillHasAStableUp()
        {
            PangaeaFinale.OrbitPose(new Vector3(5f, 0f, 5f), 0f, 0f, 50f, out Vector3 pos, out Quaternion rot);
            Assert.AreEqual(new Vector3(5f, 50f, 5f), pos);
            Assert.Less(Vector3.Distance(rot * Vector3.forward, Vector3.down), 1e-4f);
            Assert.IsFalse(float.IsNaN(rot.x));
        }

        [Test]
        public void FormatStats_ListsTheRun()
        {
            var r = new RunRecord { playSeconds = 754f, islandsMerged = 19, landArea = 1234.4f, speciesSeen = 8, photos = 3 };
            string s = PangaeaFinale.FormatStats(r);
            StringAssert.Contains("12:34", s);
            StringAssert.Contains("19", s);
            StringAssert.Contains("1234", s);
            StringAssert.Contains("Arten gesehen   8", s);
            StringAssert.Contains("Fotos   3", s);
            StringAssert.Contains("1:00:00", PangaeaFinale.FormatStats(new RunRecord { playSeconds = 3600f }));
            Assert.AreEqual("", PangaeaFinale.FormatStats(null));
        }
    }
}
