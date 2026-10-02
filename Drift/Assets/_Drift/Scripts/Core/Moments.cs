using System;
using UnityEngine;

namespace Drift.Core
{
    public enum MomentKind
    {
        Signature, Play, Race, Spar, Errand, AnimalDive,
        BirdMurmur, BirdLanding, BirdDive, Butterflies,
        Festival,
        WhaleSurface, WhaleBreach, DolphinJump, FishJump,
        // Appended 2026-09-23 (LifeDirector): a crab wave / turtle nest trip / butterfly spiral, a firefly wave.
        CritterMove, FireflyWave,
        // Appended 2026-09-23 (sea life): coast visitors, leaps and flights around the player island.
        TurtleBreath, RayLeap, FlyingFish, GullDive, GullsTakeOff, JellySwarm, WhaleFluke, SealPopUp, SealHaulOut,
        // Appended 2026-09-23 (LifeDirector, night): a sleeping animal lifts its head and settles again.
        SleepStir,
        // Appended 2026-09-24 (herd meetings): two herds meet and greet, chase, shove, trek or dance together.
        Meeting,
        // Appended 2026-10-02 (environment layers, v0.6.8): a herd leaves for a greener patch, the dusk gathering at the
        // water, two species mingling, the winter huddle, courtship, shelter from the rain.
        Migrate, Gather, Mingle, Huddle, Court, Shelter,
    }

    // Something a watcher would notice, reported where and when it starts. Costs a null check without listeners;
    // the playtest recorder counts them near the camera (the cozy "something every 10 s" target).
    public static class Moments
    {
        // A moment counts as noticed when it is on screen and its subject is at least this tall in pixels.
        public const float NoticePx = 20f;

        public static event Action<MomentKind, Vector3> Seen;

        public static bool Listening => Seen != null;

        public static void Report(MomentKind kind, Vector3 world) => Seen?.Invoke(kind, world);

        public static void Report(MomentKind kind, Transform island, Vector2 local, float height)
        {
            if (Seen == null || island == null) return;
            Seen(kind, island.TransformPoint(new Vector3(local.x, height, local.y)));
        }

        // How tall (world units) the subject of a moment is on screen: the noticed rule multiplies it with the
        // pixels per unit at its distance. Shared by the playtest recorder and the cozy LifeDirector.
        public static float SubjectSize(MomentKind kind)
        {
            switch (kind)
            {
                case MomentKind.Signature: return 1.2f;
                case MomentKind.Play:
                case MomentKind.Race:
                case MomentKind.Spar: return 0.9f;
                case MomentKind.Errand:
                case MomentKind.AnimalDive: return 0.7f;
                case MomentKind.Migrate:
                case MomentKind.Shelter:
                case MomentKind.Huddle:
                case MomentKind.Court: return 1.5f;
                case MomentKind.Gather:
                case MomentKind.Mingle: return 2.5f;
                case MomentKind.BirdMurmur: return 5f;
                case MomentKind.BirdLanding: return 1.5f;
                case MomentKind.BirdDive: return 1f;
                case MomentKind.Butterflies: return 0.3f;
                case MomentKind.Festival: return 3f;
                case MomentKind.WhaleSurface: return 5f;
                case MomentKind.WhaleBreach: return 8f;
                case MomentKind.DolphinJump: return 2f;
                case MomentKind.FishJump: return 0.8f;
                case MomentKind.CritterMove: return 0.3f;
                case MomentKind.FireflyWave: return 3f;
                case MomentKind.TurtleBreath: return 0.8f;
                case MomentKind.RayLeap: return 1.7f;
                case MomentKind.FlyingFish: return 2f;
                case MomentKind.GullDive: return 1f;
                case MomentKind.GullsTakeOff: return 1.5f;
                case MomentKind.JellySwarm: return 3f;
                case MomentKind.WhaleFluke: return 3f;
                case MomentKind.SealPopUp: return 0.6f;
                case MomentKind.SealHaulOut: return 1.3f;
                case MomentKind.SleepStir: return 0.5f;
                case MomentKind.Meeting: return 1.5f;
                default: return 4f;
            }
        }

        // Pixels one world unit covers at `distance` from a perspective camera.
        public static float PxPerUnit(float distance, float pixelHeight, float fieldOfView) =>
            pixelHeight / (2f * Mathf.Max(0.01f, distance) * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad));

        // Viewport point inside the screen with `margin` (0.02 = the recorder's rule) and in front of the camera.
        public static bool OnScreen(Vector3 viewport, float margin) =>
            viewport.z > 0f && viewport.x > margin && viewport.x < 1f - margin && viewport.y > margin && viewport.y < 1f - margin;

        // The noticed rule for a subject of `size` world units at `world`, seen by `cam`.
        public static bool Noticed(Camera cam, Vector3 world, float size, float margin = 0.02f, float minPx = NoticePx)
        {
            if (cam == null) return false;
            if (!OnScreen(cam.WorldToViewportPoint(world), margin)) return false;
            float d = Vector3.Distance(cam.transform.position, world);
            return size * PxPerUnit(d, cam.pixelHeight, cam.fieldOfView) >= minPx;
        }
    }
}
