using System;
using System.Collections.Generic;
using Drift.Islands;
using Drift.Life;
using UnityEngine;

namespace Drift.Audio
{
    // What the habitat around the player holds, each count weighted by how close its island or flock is.
    public struct HabitatCounts
    {
        public float songbirds, seabirds, forestCells, meadowCells, trees, reeds, sheepHerds, oxHerds, fireflies, shoreLength;
        // 0..1 share of the nearby animals that are awake (1 without herds).
        public float awake;
    }

    // Pure mapping from counts and world conditions to the LifeSynth densities; testable without a scene.
    public static class LifeSoundMix
    {
        public const float BirdsFull = 10f;
        public const float SeabirdsFull = 3f;
        public const float MeadowFull = 25f;
        public const float TreesFull = 30f;
        public const float ReedsFull = 12f;
        public const float ShoreFull = 12f;
        public const float SheepFull = 3f;
        public const float OxFull = 2f;

        // Saturating and clamped: 0 at 0, ≈ 0.86 at `full`, → 1.
        public static float Density(float count, float full)
        {
            if (count <= 0f || full <= 0f) return 0f;
            return SynthMath.Clamp01(1f - (float)Math.Exp(-2.0 * count / full));
        }

        // 1 inside `near`, fading to 0 at `far`.
        public static float Proximity(float distance, float near, float far)
        {
            if (distance <= near) return 1f;
            if (distance >= far || far <= near) return 0f;
            return 1f - (distance - near) / (far - near);
        }

        public static float DayAmount(float night) => SynthMath.Clamp01((0.55f - night) / 0.3f);
        public static float CricketNight(float night) => SynthMath.Clamp01((night - 0.35f) / 0.35f);
        public static float DuskAmount(float night) => SynthMath.Clamp01(1f - Math.Abs(night - 0.5f) / 0.3f);

        public static void Apply(in HabitatCounts c, float night, float windSpeed, float storm, float speedN, LifeSynth s)
        {
            float day = DayAmount(night);
            float calm = 1f - 0.7f * SynthMath.Clamp01(storm);
            float windN = SynthMath.Clamp01(windSpeed / 1.2f);
            s.BirdDensity = Density(c.songbirds + 0.12f * c.forestCells, BirdsFull) * day * calm;
            s.SeabirdAmount = Density(c.seabirds, SeabirdsFull) * day;
            s.CricketDensity = Density(c.meadowCells + 0.5f * c.fireflies, MeadowFull) * CricketNight(night) * calm;
            s.RustleAmount = Density(c.trees, TreesFull) * SynthMath.Clamp01(0.25f + 0.75f * windN + storm);
            s.LapAmount = SynthMath.Clamp01(1f - 1.4f * SynthMath.Clamp01(speedN)) * Density(c.shoreLength, ShoreFull);
            s.FrogAmount = Density(c.reeds, ReedsFull) * DuskAmount(night);
            s.SheepAmount = Density(c.sheepHerds, SheepFull) * day * c.awake;
            s.OxAmount = Density(c.oxHerds, OxFull) * day * c.awake;
        }
    }

    // Main-thread side: walks the islands within `range` of the player (edge to edge) and the flocks, and
    // reads the life components on them. Runs every AudioDirector.lifeUpdateInterval, allocates nothing.
    public sealed class LifeSoundScout
    {
        public float IslandNear = 8f;
        public float FlockNear = 15f;
        public float FlockFar = 80f;
        public HabitatCounts Counts;
        public int IslandsSeen { get; private set; }

        public void Evaluate(Island player, IReadOnlyList<Island> all, FlockSystem flocks, float range)
        {
            var c = default(HabitatCounts);
            c.awake = 1f;
            IslandsSeen = 0;
            if (player == null) { Counts = c; return; }

            Vector2 pp = player.PlanarPosition;
            float pr = player.BoundingRadius;
            c.shoreLength = 2f * Mathf.Sqrt(Mathf.PI * Mathf.Max(0f, player.LandArea));
            float awakeSum = 0f, awakeW = 0f;

            if (all != null)
                for (int i = 0; i < all.Count; i++)
                {
                    var isl = all[i];
                    if (isl == null || isl.IsSunk || isl.IsEmerging) continue;
                    float d = ReferenceEquals(isl, player) ? 0f
                        : Mathf.Max(0f, (isl.PlanarPosition - pp).magnitude - isl.BoundingRadius - pr);
                    if (d > range) continue;
                    float w = LifeSoundMix.Proximity(d, IslandNear, range);
                    if (w <= 0f) continue;
                    IslandsSeen++;

                    var life = isl.GetComponent<IslandLifeSystem>();
                    if (life != null && life.HasGrid)
                    {
                        life.GetHudStats(out _, out int plains, out int shrub, out int woods, out int oldGrowth, out _);
                        c.forestCells += w * (woods + oldGrowth);
                        c.meadowCells += w * (plains + shrub);
                        c.trees += w * (life.CountOf(LifeKind.Tree) + life.CountOf(LifeKind.Palm));
                        c.reeds += w * life.CountOf(LifeKind.Reed);
                    }

                    var herds = isl.GetComponent<IslandHerdSystem>();
                    if (herds != null && herds.HerdCount > 0)
                    {
                        for (int h = 0; h < herds.HerdCount; h++)
                        {
                            var kind = herds.HerdKind(h);
                            if (kind == LifeKind.Sheep) c.sheepHerds += w;
                            else if (kind == LifeKind.Ox) c.oxHerds += w;
                        }
                        awakeSum += w * (1f - herds.SleepingFraction);
                        awakeW += w;
                    }

                    var critters = isl.GetComponent<IslandCrittersSystem>();
                    if (critters != null) c.fireflies += w * critters.FireflyCount;
                }

            if (awakeW > 0f) c.awake = awakeSum / awakeW;

            if (flocks != null)
                for (int f = 0; f < flocks.FlockCount; f++)
                {
                    float d = Mathf.Max(0f, (flocks.PositionOf(f) - pp).magnitude - pr);
                    float w = LifeSoundMix.Proximity(d, FlockNear, FlockFar);
                    if (w <= 0f) continue;
                    int n = flocks.BirdCountOf(f);
                    if (flocks.IsSeabird(f)) c.seabirds += w * n;
                    else c.songbirds += w * n;
                }

            Counts = c;
        }
    }
}
