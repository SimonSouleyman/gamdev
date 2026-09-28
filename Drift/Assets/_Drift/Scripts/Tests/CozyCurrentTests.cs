using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Tectonics;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Owner, 2026-09-23 (cozy, thumbstick): "Selbst gegen die Strömung möchte ich mit der Insel vorankommen, mit der
    // Strömung schneller" and "Nur an den Grenzen soll es besonders viel Boost geben", plus 3 x 3 bigger plates.
    public class CozyCurrentTests
    {
        const float Dt = 0.02f;
        readonly List<GameObject> _objects = new();
        PlateSystem _previousPlates;
        bool _steering;

        [SetUp]
        public void SetUp()
        {
            _previousPlates = PlateSystem.Instance;
            _steering = Island.DirectionSteering;
        }

        [TearDown]
        public void TearDown()
        {
            Island.DirectionSteering = _steering;
            foreach (var go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            if (_previousPlates != null)
            {
                _previousPlates.enabled = false;
                _previousPlates.enabled = true;
            }
        }

        // The scene's cozy plate motion (Planet.unity: waveAmplitude 22, waveSpeed 0.3).
        PlateSystem MakePlates(int seed = 4242)
        {
            var go = new GameObject("TestPlates");
            go.SetActive(false);
            var ps = go.AddComponent<PlateSystem>();
            ps.seed = seed;
            ps.showBorders = false;
            ps.enableEvents = false;
            ps.waveAmplitude = 22f;
            ps.waveSpeed = 0.3f;
            ps.borderRefreshInterval = 0f;
            go.SetActive(true);
            _objects.Add(go);
            return ps;
        }

        Island MakePlayer(PlateSystem ps, GameMode mode = GameMode.Cozy)
        {
            var go = new GameObject("TestIsland");
            go.SetActive(false);
            var isl = go.AddComponent<Island>();
            isl.ModeOverride = mode;
            isl.useKeyboardInput = true;
            isl.sinkEnabled = false;
            isl.landRadius = 3f;
            isl.shapeSeed = 4711;
            isl.moveSpeed = 9.5f;
            isl.velocityAlign = 1.5f;
            isl.driftRotation = 0f;
            go.SetActive(true);
            _objects.Add(go);
            ps.focus = go.transform;
            return isl;
        }

        // Freezes the plates with the strongest-moving core of the world at the peak of its swing and returns it.
        static PlateSystem.Plate StrongestPlate(PlateSystem ps, Island isl)
        {
            ps.Restore(new PlateSaveData());
            PlateSystem.PlateCore best = null;
            float bestPeak = 0f;
            for (int x = 0; x < ps.gridPeriod; x++)
                for (int z = 0; z < ps.gridPeriod; z++)
                {
                    var c = ps.NearestPlate(new Vector2((x + 0.5f) * ps.cellSize, (z + 0.5f) * ps.cellSize)).core;
                    float peak = ps.waveAmplitude * c.ampK * c.omegaK * ps.waveSpeed;
                    if (peak > bestPeak) { bestPeak = peak; best = c; }
                }
            isl.SetPlanarPosition(best.home);
            ps.Restore(new PlateSaveData { waveClock = Mathf.Repeat(-best.phase, Mathf.PI * 2f) / best.omegaK });
            return ps.NearestPlate(best.home);
        }

        // Mean planar velocity along `measure` over the second half of `seconds`, driving `dir`; the island is held
        // at `at` so the plate (and seam) under it stays the same.
        static float Run(Island isl, Vector2 at, Vector2 dir, Vector2 measure, float seconds = 6f)
        {
            isl.SetSelfVelocity(Vector2.zero);
            isl.SetPlanarPosition(at);
            for (int k = 0; k < 60; k++) isl.Tick(Vector2.zero, Vector2.zero, Dt);
            isl.SetSelfVelocity(Vector2.zero);
            int n = Mathf.RoundToInt(seconds / Dt), m = 0;
            float sum = 0f;
            for (int k = 0; k < n; k++)
            {
                isl.SetPlanarPosition(at);
                isl.Tick(Vector2.zero, dir, Dt);
                if (k < n / 2) continue;
                sum += Vector2.Dot(isl.PlanarVelocity, measure);
                m++;
            }
            return sum / m;
        }

        [Test]
        public void InteriorCurrent_IsAGentleShareCappedByTheRidersTopSpeed()
        {
            var ps = MakePlates();
            const float top = 8.5f;
            float cap = ps.interiorCurrentCap * top;
            float last = 0f;
            for (float speed = 0.25f; speed <= 30f; speed += 0.25f)
            {
                Vector2 v = new Vector2(0.6f, -0.8f) * speed;
                Vector2 felt = ps.InteriorCurrent(v, top);
                Assert.Greater(Vector2.Dot(felt.normalized, v.normalized), 0.99999f, "same direction as the plate");
                Assert.LessOrEqual(felt.magnitude, speed * ps.interiorCurrentShare + 1e-4f, "never more than the share");
                Assert.Less(felt.magnitude, cap, "never reaches the cap");
                Assert.Greater(felt.magnitude, last, "a faster plate still carries a little more");
                last = felt.magnitude;
            }
            Assert.AreEqual(0.25f * ps.interiorCurrentShare, ps.InteriorCurrent(new Vector2(0.25f, 0f), top).magnitude, 0.01f,
                "a slow plate carries almost exactly its share");
            Assert.AreEqual(Vector2.zero, ps.InteriorCurrent(Vector2.zero, top));
        }

        [Test]
        public void AgainstTheStrongestCurrent_TheIslandKeepsSixtyPercentOfItsSpeed()
        {
            Island.DirectionSteering = true;
            var ps = MakePlates();
            var isl = MakePlayer(ps);
            var plate = StrongestPlate(ps, isl);
            Vector2 v = plate.velocity;
            Assert.Greater(v.magnitude, isl.MaxSpeed, "the test plate moves faster than the island can drive");
            Vector2 d = v.normalized;
            Vector2 site = plate.position;
            Assert.IsFalse(ps.NearestSeam(site, 20f, out _, out _, out _), "deep inside the plate");

            float top = isl.MaxSpeed;
            float against = Run(isl, site, -d, -d);
            float with = Run(isl, site, d, d);
            float idle = Run(isl, site, Vector2.zero, d);
            Assert.GreaterOrEqual(against / top, 0.6f, $"against the current {against:F2} of {top:F2}");
            Assert.GreaterOrEqual(with / top, 1.15f, $"with the current clearly faster: {with:F2} of {top:F2}");
            Assert.Greater(idle, 0.5f, "a resting island still drifts with its plate");
            Assert.Less(idle, 0.4f * top, "... but slowly");
        }

        [Test]
        public void AtABoundary_TheSurfIsFarStrongerThanTheInterior()
        {
            Island.DirectionSteering = true;
            var ps = MakePlates();
            var isl = MakePlayer(ps);
            var plate = StrongestPlate(ps, isl);
            float top = isl.MaxSpeed;
            float interiorBest = Run(isl, plate.position, plate.velocity.normalized, plate.velocity.normalized);

            ps.Restore(new PlateSaveData { waveClock = 3.1f });
            int checkedSeams = 0;
            for (int kind = 0; kind < 3; kind++)
            {
                PlateSystem.Border? pick = null;
                foreach (var b in ps.Borders)
                    if ((int)b.kind == kind && !b.open0 && !b.open1 && b.length > 15f && (pick == null || b.length > pick.Value.length)) pick = b;
                if (pick == null) continue;
                var seam = pick.Value;
                Vector2 q = ps.SeamPoint(seam, 0.5f), t = ps.SeamTangent(seam, 0.5f);
                foreach (var dir in new[] { t, -t })
                {
                    float surf = Run(isl, q, dir, dir);
                    Assert.Greater(surf / top, 1.8f, $"{seam.kind} seam: {surf:F2} of {top:F2}");
                    Assert.Greater(surf, interiorBest * 1.4f, $"{seam.kind} seam {surf:F2} vs best interior {interiorBest:F2}");
                }
                checkedSeams++;
            }
            Assert.Greater(checkedSeams, 0);
        }

        [Test]
        public void Adventure_FeelsTheFullPlateCurrent()
        {
            var ps = MakePlates();
            var isl = MakePlayer(ps, GameMode.Adventure);
            var plate = StrongestPlate(ps, isl);
            float idle = Run(isl, plate.position, Vector2.zero, plate.velocity.normalized);
            Assert.AreEqual(plate.velocity.magnitude, idle, 0.05f, "no gentle interior outside the cozy mode");

            ps.SetRingLayout(0f, 30f, 640f);
            for (int i = 0; i < 20; i++)
            {
                Vector2 p = new Vector2(-25f + 2.5f * i, 31f * i);
                Assert.AreEqual(ps.SampleVelocity(p), ps.CarryVelocity(p, 8f), "the ring lanes carry in full");
            }
            ps.ClearRingLayout();
        }

        [Test]
        public void PlateGrid_IsThreeByThreeAndTilesTheCozyWorld()
        {
            var ps = MakePlates();
            var sgo = new GameObject("TestStreamer");
            sgo.SetActive(false);
            _objects.Add(sgo);
            var streamer = sgo.AddComponent<WorldStreamer>();
            Assert.AreEqual(3, ps.gridPeriod);
            Assert.AreEqual(102f, ps.cellSize, 1e-4f);
            Assert.AreEqual(streamer.chunkSize * streamer.worldChunks, ps.WorldSize, 1e-3f, "plates wrap with the world");

            float w = ps.WorldSize;
            var cores = new HashSet<Vector2Int>();
            for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                {
                    Vector2 p = new Vector2(-w + i * w / 4f + 3.7f, -w + j * w / 4f + 1.3f);
                    var plate = ps.NearestPlate(p);
                    cores.Add(plate.core.id);
                    Assert.That(plate.core.id.x, Is.InRange(0, 2));
                    Assert.That(plate.core.id.y, Is.InRange(0, 2));
                    var copy = ps.NearestPlate(p + new Vector2(w, -w));
                    Assert.AreSame(plate.core, copy.core, "the same plate one world further");
                    Assert.AreEqual(plate.velocity.x, copy.velocity.x, 1e-4f);
                    Assert.AreEqual(plate.velocity.y, copy.velocity.y, 1e-4f);
                }
            Assert.AreEqual(9, cores.Count, "nine plates in the cozy world");
        }

        [Test]
        public void Restore_DropsOffsetsOfAFinerOldGrid()
        {
            var ps = MakePlates();
            var old = new PlateSaveData { time = 5f, waveClock = 1.5f };
            old.offsets.Add(new PlateOffsetData { cx = 3, cz = 1, ox = 4f, oy = 0f, px = 1f, py = 0f });
            old.offsets.Add(new PlateOffsetData { cx = 1, cz = 2, ox = 0f, oy = 2f, px = 0f, py = 1f });
            ps.Restore(old);
            var saved = ps.Capture();
            Assert.AreEqual(1, saved.offsets.Count);
            Assert.AreEqual(1, saved.offsets[0].cx);
            Assert.AreEqual(2, saved.offsets[0].cz);
        }
    }
}
