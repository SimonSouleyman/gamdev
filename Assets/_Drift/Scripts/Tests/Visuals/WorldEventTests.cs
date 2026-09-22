using System.Collections.Generic;
using Drift.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // The scheduling of the rare spectacles (Drift.Visuals.WorldEventScheduler): how often one starts, that the
    // cooldowns hold, that only one runs at a time and that every kind keeps to its own weather / time-of-day
    // window. Plus the geometry of the rainbow band.
    public class WorldEventTests
    {
        const float Lifetime = 60f;

        static WorldEventConditions Evening() => new WorldEventConditions
        {
            sunHeight = 0.3f, stars = 0f, moon = 0f, storm = 0.05f,
            stormDistance = 120f, sinceStorm = 5f, openWater = true, adventure = false
        };

        static WorldEventConditions Noon() => new WorldEventConditions
        {
            sunHeight = 0.95f, stars = 0f, moon = 0f, storm = 0f,
            stormDistance = float.MaxValue, sinceStorm = float.MaxValue, openWater = false, adventure = false
        };

        static WorldEventConditions Night() => new WorldEventConditions
        {
            sunHeight = -0.5f, stars = 1f, moon = 0.05f, storm = 0f,
            stormDistance = float.MaxValue, sinceStorm = float.MaxValue, openWater = true, adventure = false
        };

        // Runs the clock, confirming everything the scheduler asks for; returns when each spectacle started.
        static List<(float t, WorldEventKind k)> Run(WorldEventScheduler s, float seconds, WorldEventConditions c,
            float dt = 0.1f, float lifetime = Lifetime)
        {
            var list = new List<(float, WorldEventKind)>();
            for (float t = 0f; t < seconds; t += dt)
            {
                var k = s.Tick(dt, c);
                if (k == WorldEventKind.None) continue;
                s.Confirm(k, lifetime);
                list.Add((t, k));
            }
            return list;
        }

        static WorldEventScheduler Fresh(uint seed, float chance = 2f, float cooldown = 30f, float same = 0f)
        {
            var s = new WorldEventScheduler(seed);
            s.chancePerMinute = chance;
            s.cooldownSeconds = cooldown;
            s.sameKindCooldown = same;
            s.firstWaitMin = 10f;
            s.firstWaitMax = 30f;
            s.Reset(seed);
            return s;
        }

        [Test]
        public void OnlyOneSpectacleRunsAtATime()
        {
            for (uint seed = 1; seed < 12; seed++)
            {
                var s = Fresh(seed);
                var starts = new List<float>();
                for (float t = 0f; t < 3000f; t += 0.1f)
                {
                    var k = s.Tick(0.1f, Evening());
                    if (k == WorldEventKind.None) continue;
                    Assert.IsFalse(s.Busy, $"seed {seed}: a spectacle was offered while one was still running");
                    s.Confirm(k, Lifetime);
                    starts.Add(t);
                }
                for (int i = 1; i < starts.Count; i++)
                    Assert.GreaterOrEqual(starts[i] - starts[i - 1], Lifetime + s.cooldownSeconds - 0.2f,
                        $"seed {seed}: two spectacles closer than lifetime + cooldown");
                Assert.Greater(starts.Count, 0, $"seed {seed}");
            }
        }

        [Test]
        public void ChancePerMinute_SetsTheAverageGap()
        {
            // With the cooldown out of the way the mean gap is lifetime + 60 / chancePerMinute.
            const float hours = 6f;
            foreach (float chance in new[] { 0.5f, 1f, 2f })
            {
                int total = 0;
                for (uint seed = 1; seed <= 8; seed++)
                    total += Run(Fresh(seed, chance, 0f), 3600f * hours, Evening(), 0.25f, 20f).Count;
                float perSeed = total / 8f;
                float expected = 3600f * hours / (20f + 60f / chance);
                Assert.That(perSeed, Is.EqualTo(expected).Within(expected * 0.35f),
                    $"chance {chance}/min: {perSeed:0.0} spectacles, expected about {expected:0.0}");
            }
        }

        [Test]
        public void Cooldown_HoldsTheNextSpectacleBack()
        {
            var s = Fresh(5, 60f, 240f);
            var starts = Run(s, 5000f, Evening());
            Assert.Greater(starts.Count, 3);
            for (int i = 1; i < starts.Count; i++)
                Assert.GreaterOrEqual(starts[i].t - starts[i - 1].t, Lifetime + 240f - 0.2f);
        }

        [Test]
        public void SameKindCooldown_KeepsAKindFromRepeating()
        {
            var s = Fresh(9, 60f, 10f, 900f);
            var starts = Run(s, 6000f, Evening());
            Assert.Greater(starts.Count, 3);
            var last = new Dictionary<WorldEventKind, float>();
            foreach (var (t, k) in starts)
            {
                if (last.TryGetValue(k, out float prev)) Assert.GreaterOrEqual(t - prev, 900f - 0.2f, $"{k} repeated too soon");
                last[k] = t;
            }
        }

        [Test]
        public void NothingHappensWhileNoSpectacleFitsTheWeather()
        {
            // High noon in a calm, crowded sea: no rainbow (sun too high, no rain), no whales (no open water),
            // no night sky. The wait must not tick away either, or the first clear evening would fire at once.
            var s = Fresh(3);
            float before = s.Wait;
            var starts = Run(s, 4000f, Noon());
            Assert.AreEqual(0, starts.Count);
            Assert.AreEqual(before, s.Wait, 1e-4f);
        }

        [Test]
        public void EveryKindKeepsToItsOwnWindow()
        {
            var s = Fresh(2);
            var noon = Noon();
            var evening = Evening();
            var night = Night();

            Assert.IsFalse(s.Allowed(WorldEventKind.Rainbow, noon), "no rainbow with the sun overhead and no rain");
            Assert.IsTrue(s.Allowed(WorldEventKind.Rainbow, evening));
            Assert.IsFalse(s.Allowed(WorldEventKind.Rainbow, night), "no rainbow at night");

            var wet = evening;
            wet.storm = 0.8f;
            Assert.IsFalse(s.Allowed(WorldEventKind.Rainbow, wet), "not while you are in the rain yourself");
            var dry = evening;
            dry.stormDistance = float.MaxValue;
            dry.sinceStorm = float.MaxValue;
            Assert.IsFalse(s.Allowed(WorldEventKind.Rainbow, dry), "a rainbow needs rain somewhere");

            Assert.IsTrue(s.Allowed(WorldEventKind.WhaleMigration, evening));
            var coast = evening;
            coast.openWater = false;
            Assert.IsFalse(s.Allowed(WorldEventKind.WhaleMigration, coast), "whales need open water");
            var ring = evening;
            ring.adventure = true;
            Assert.IsFalse(s.Allowed(WorldEventKind.WhaleMigration, ring), "cozy only");

            Assert.IsTrue(s.Allowed(WorldEventKind.Aurora, night));
            Assert.IsTrue(s.Allowed(WorldEventKind.Meteors, night));
            Assert.IsFalse(s.Allowed(WorldEventKind.Aurora, evening));
            Assert.IsFalse(s.Allowed(WorldEventKind.Meteors, evening));
            var moonlit = night;
            moonlit.moon = 0.95f;
            Assert.IsFalse(s.Allowed(WorldEventKind.Aurora, moonlit), "a full moon washes the sky out");
            Assert.IsFalse(s.Allowed(WorldEventKind.Meteors, moonlit));
            var stormy = night;
            stormy.storm = 0.7f;
            Assert.IsFalse(s.Allowed(WorldEventKind.Aurora, stormy), "clouds close the sky");
        }

        [Test]
        public void NightPicksOnlyNightSpectacles()
        {
            var starts = Run(Fresh(4, 60f, 5f), 4000f, Night());
            Assert.Greater(starts.Count, 5);
            foreach (var (_, k) in starts)
                Assert.IsTrue(k == WorldEventKind.Aurora || k == WorldEventKind.Meteors || k == WorldEventKind.WhaleMigration,
                    $"{k} started at night");
        }

        [Test]
        public void TheSameKindDoesNotComeTwiceInARowWhileAnotherFits()
        {
            var starts = Run(Fresh(6, 60f, 5f), 4000f, Evening());
            Assert.Greater(starts.Count, 6);
            for (int i = 1; i < starts.Count; i++)
                Assert.AreNotEqual(starts[i - 1].k, starts[i].k, $"#{i}: {starts[i].k} twice in a row");
        }

        [Test]
        public void ThePacingDoesNotDependOnTheFrameRate()
        {
            for (uint seed = 1; seed < 5; seed++)
            {
                int fast = Run(Fresh(seed, 1.5f, 60f), 3600f, Evening(), 1f / 120f).Count;
                int slow = Run(Fresh(seed, 1.5f, 60f), 3600f, Evening(), 0.2f).Count;
                Assert.That(slow, Is.EqualTo(fast).Within(2), $"seed {seed}: {fast} at 120 fps vs {slow} at 5 fps");
            }
        }

        [Test]
        public void RareOverAWholeCozyRun()
        {
            // 20 minutes, the length of a whole cozy run, with the shipped sliders: a spectacle is something you
            // tell someone about, not a light show.
            int total = 0;
            for (uint seed = 1; seed <= 20; seed++)
            {
                var s = new WorldEventScheduler(seed);
                var starts = Run(s, 1200f, Evening(), 0.1f, 80f);
                Assert.LessOrEqual(starts.Count, 4, $"seed {seed}: {starts.Count} spectacles in 20 minutes");
                Assert.GreaterOrEqual(starts.Count, 1, $"seed {seed}: none at all in 20 minutes");
                total += starts.Count;
            }
            Assert.Greater(total, 30, "but two or three a run is the point");
        }

        [Test]
        public void AStartedSpectacleEndsByItself()
        {
            var s = Fresh(7);
            Assert.AreEqual(WorldEventKind.None, s.Active);
            s.Confirm(WorldEventKind.Aurora, 30f);
            Assert.AreEqual(WorldEventKind.Aurora, s.Active);
            for (float t = 0f; t < 29f; t += 0.5f) s.Tick(0.5f, Night());
            Assert.AreEqual(WorldEventKind.Aurora, s.Active);
            s.Tick(2f, Night());
            Assert.AreEqual(WorldEventKind.None, s.Active);
            Assert.AreEqual(0f, s.ActiveLeft, 1e-4f);
        }

        [Test]
        public void GermanNames()
        {
            Assert.AreEqual("Regenbogen", WorldEvents.German(WorldEventKind.Rainbow));
            Assert.AreEqual("Walwanderung", WorldEvents.German(WorldEventKind.WhaleMigration));
            Assert.AreEqual("Polarlicht", WorldEvents.German(WorldEventKind.Aurora));
            Assert.AreEqual("Meteorschauer", WorldEvents.German(WorldEventKind.Meteors));
            Assert.AreEqual("Regenbogen gesehen", WorldEvents.SeenLine(WorldEventKind.Rainbow));
            Assert.AreEqual("", WorldEvents.SeenLine(WorldEventKind.None));
        }

        // The northern lights and the meteor shower are two branches in DriftSky.hlsl that only run when their
        // globals are non-zero: while nothing is on, the sky has to be exactly the sky it always was.
        [Test]
        public void NoSpectacle_LeavesTheSkyGlobalsAtZero()
        {
            var go = new GameObject("WorldEventsTest");
            try
            {
                var ev = go.AddComponent<WorldEvents>();
                ev.Tick(0.1f);
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyAurora"));
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyMeteorParams"));
                Assert.AreEqual(Color.black, WorldEvents.AmbientBoost);
                Assert.AreEqual(WorldEventKind.None, ev.Active);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ASkySpectacleSwitchesItsGlobalsOnAndOffAgain()
        {
            var go = new GameObject("WorldEventsTest");
            try
            {
                var ev = go.AddComponent<WorldEvents>();
                Assert.IsTrue(ev.Trigger(WorldEventKind.Aurora));
                ev.Tick(ev.fadeSeconds);
                Assert.AreEqual(WorldEventKind.Aurora, ev.Active);
                Assert.Greater(Shader.GetGlobalVector("_SkyAurora").x, 0f, "the curtain is lit");
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyMeteorParams"), "one spectacle at a time");
                Assert.AreNotEqual(Color.black, WorldEvents.AmbientBoost);

                ev.StopAll();
                Assert.AreEqual(WorldEventKind.None, ev.Active);
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyAurora"));
                Assert.AreEqual(Color.black, WorldEvents.AmbientBoost);

                Assert.IsTrue(ev.Trigger(WorldEventKind.Meteors));
                ev.Tick(ev.fadeSeconds);
                Assert.Greater(Shader.GetGlobalVector("_SkyMeteorParams").x, 0f);
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyAurora"));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyAurora"), "disabling puts everything back");
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SkyMeteorParams"));
            }
        }

        [Test]
        public void RainbowBandIsAClosedRingOfDirections()
        {
            var mesh = RainbowArc.Build();
            try
            {
                var verts = mesh.vertices;
                Assert.AreEqual(RainbowArc.Segments * 2, verts.Length);
                Assert.AreEqual(RainbowArc.Segments * 6, mesh.triangles.Length);
                float minDeg = 180f, maxDeg = 0f;
                foreach (var v in verts)
                {
                    Assert.That(v.magnitude, Is.EqualTo(1f).Within(1e-3f), "the band's vertices are directions");
                    float deg = Vector3.Angle(v, Vector3.forward);
                    minDeg = Mathf.Min(minDeg, deg);
                    maxDeg = Mathf.Max(maxDeg, deg);
                }
                Assert.That(minDeg, Is.EqualTo(RainbowArc.InnerAngle).Within(0.05f));
                Assert.That(maxDeg, Is.EqualTo(RainbowArc.OuterAngle).Within(0.05f));
                // Both bows (42.4 and 50.4 degrees) have to fit inside the band with room for their width.
                Assert.Less(RainbowArc.InnerAngle, 40f);
                Assert.Greater(RainbowArc.OuterAngle, 54f);
                Assert.Greater(mesh.bounds.extents.x, 1000f, "never culled: its real place is around the camera");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
