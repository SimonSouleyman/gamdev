using System.Collections.Generic;
using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using UnityEngine;

namespace Drift.Bridge
{
    // One creature caught in its photo-task move, with a handle to find it again next frame (the camera cue follows it).
    public struct PhotoSubject
    {
        public int task;
        public Vector3 world;
        // World extent of what the picture has to show (a body length, a spiralling pair, a murmuration).
        public float size;
        public IslandHerdSystem herds;
        public IslandCrittersSystem critters;
        public Island island;
        public FlockSystem flocks;
        public int a, b;
    }

    // The camera of a photo as plain numbers, so a photo can be judged without a Camera (tests, or after the frame).
    public struct PhotoView
    {
        public Vector3 position;
        public Quaternion rotation;
        public float tanHalfFov, aspect, near;
        public int pixelWidth, pixelHeight;

        public static PhotoView Of(Camera cam) => new PhotoView
        {
            position = cam.transform.position, rotation = cam.transform.rotation, tanHalfFov = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad),
            aspect = cam.aspect, near = cam.nearClipPlane, pixelWidth = cam.pixelWidth, pixelHeight = cam.pixelHeight,
        };

        public static PhotoView Make(Vector3 position, Quaternion rotation, float fovDeg, int width, int height) => new PhotoView
        {
            position = position, rotation = rotation, tanHalfFov = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad),
            aspect = width / (float)Mathf.Max(1, height), near = 0.05f, pixelWidth = width, pixelHeight = height,
        };

        // Viewport position (0..1, y up) and depth; false behind the camera.
        public bool Project(Vector3 world, out Vector2 viewport, out float depth)
        {
            Vector3 local = Quaternion.Inverse(rotation) * (world - position);
            depth = local.z;
            viewport = default;
            if (depth <= near) return false;
            float h = depth * tanHalfFov, w = h * aspect;
            viewport = new Vector2(0.5f + 0.5f * local.x / w, 0.5f + 0.5f * local.y / h);
            return true;
        }

        // What an object of this world size spans at that depth, as a share of the short side of the picture.
        public float ScreenShare(float size, float depth)
        {
            if (depth <= 0f || pixelHeight <= 0) return 0f;
            float pixels = size / (2f * depth * tanHalfFov) * pixelHeight;
            return pixels / Mathf.Max(1, Mathf.Min(pixelWidth, pixelHeight));
        }
    }

    // Which photo-task moves are going on around a point, and which of them a picture shows well enough: inside the
    // frame (away from its edge) and at least minShare of the picture's short side large. Only reads the simulation.
    public static class PhotoSubjects
    {
        public const float CritterSize = 0.12f, TurtleSize = 0.18f, SpiralSize = 0.32f, WaveSize = 1.6f, DiveSize = 0.3f, MurmurSize = 2.5f;

        // Every performing subject of the kinds in mask (bit = (int)LifeKind) within range of around.
        public static void Gather(Vector2 around, float range, ulong mask, FlockSystem flocks, List<PhotoSubject> into)
        {
            if (mask == 0) return;
            var islands = Island.All;
            for (int n = 0; n < islands.Count; n++)
            {
                var island = islands[n];
                if (island == null || !island.isActiveAndEnabled || island.IsSunk) continue;
                float reach = range + island.BoundingRadius;
                if ((island.PlanarPosition - around).sqrMagnitude > reach * reach) continue;
                if (island.TryGetComponent(out IslandHerdSystem herds)) AddHerds(herds, mask, into);
                if (island.TryGetComponent(out IslandCrittersSystem critters)) AddCritters(island, critters, mask, into);
            }
            if (flocks != null) AddFlocks(flocks, around, range, mask, into);
        }

        public static void AddHerds(IslandHerdSystem herds, ulong mask, List<PhotoSubject> into)
        {
            if (herds == null || !herds.isActiveAndEnabled || herds.Tier == LifeTier.Far || (herds.SpeciesPresent & mask) == 0) return;
            var tr = herds.transform;
            int hn = herds.HerdCount;
            for (int h = 0; h < hn; h++)
            {
                var kind = herds.HerdKind(h);
                if ((mask & (1UL << (int)kind)) == 0) continue;
                int task = PhotoTaskCatalog.IndexOf(kind);
                if (task < 0) continue;
                var act = PhotoTaskCatalog.At(task).activity;
                int mn = herds.HerdSize(h);
                for (int m = 0; m < mn; m++)
                {
                    if (!HerdPerforming(herds, h, m, act)) continue;
                    float body = herds.BodyLength(h) * herds.AnimalSizeFactor(h, m);
                    Vector3 local = herds.AnimalLocalPosition(h, m);
                    local.y += body * 0.35f;
                    into.Add(new PhotoSubject { task = task, world = tr.TransformPoint(local), size = body, herds = herds, a = h, b = m });
                }
            }
        }

        static bool HerdPerforming(IslandHerdSystem herds, int h, int m, AnimalActivity act) =>
            herds.ActivityOf(h, m) == act && herds.ActivityStep(h, m) == IslandHerdSystem.MovePerform && !herds.IsHidden(h, m);

        public static void AddCritters(Island island, IslandCrittersSystem critters, ulong mask, List<PhotoSubject> into)
        {
            if (critters == null || !critters.isActiveAndEnabled || critters.Tier != LifeTier.Near || (mask & CollectionCatalog.CritterMask) == 0) return;
            var tr = island.transform;
            int cn = critters.CritterCount;
            for (int i = 0; i < cn; i++)
            {
                var kind = critters.KindOf(i);
                if ((mask & (1UL << (int)kind)) == 0 || !CritterPerforming(critters, i, kind)) continue;
                into.Add(new PhotoSubject { task = PhotoTaskCatalog.IndexOf(kind), world = CritterWorld(island, critters, i), size = CritterSizeOf(kind), critters = critters, island = island, a = i });
            }
            if ((mask & (1UL << (int)LifeKind.Firefly)) != 0 && critters.FireflyWaveActive && PhotoTaskCatalog.IndexOf(LifeKind.Firefly) >= 0)
                into.Add(new PhotoSubject { task = PhotoTaskCatalog.IndexOf(LifeKind.Firefly), world = WaveWorld(island, critters), size = WaveSize, critters = critters, island = island, a = -1 });
        }

        static bool CritterPerforming(IslandCrittersSystem critters, int i, LifeKind kind)
        {
            int task = PhotoTaskCatalog.IndexOf(kind);
            if (task < 0 || PhotoTaskCatalog.At(task).source != PhotoMoveSource.Critter) return false;
            return critters.StateOf(i) == PhotoTaskCatalog.At(task).critterState && !critters.DyingOf(i) && critters.FadeOf(i) >= 0.5f;
        }

        static float CritterSizeOf(LifeKind kind) => kind == LifeKind.Turtle ? TurtleSize : kind == LifeKind.Butterfly ? SpiralSize : CritterSize;

        static Vector3 CritterWorld(Island island, IslandCrittersSystem critters, int i)
        {
            Vector2 p = critters.PositionOf(i);
            float lift = critters.KindOf(i) == LifeKind.Butterfly ? 0.2f : 0.03f;
            return island.transform.TransformPoint(p.x, Mathf.Max(0f, island.SampleHeight(p)) + lift, p.y);
        }

        static Vector3 WaveWorld(Island island, IslandCrittersSystem critters)
        {
            Vector2 p = critters.FireflyWaveOrigin;
            return island.transform.TransformPoint(p.x, Mathf.Max(0f, island.SampleHeight(p)) + critters.fireflyHeight, p.y);
        }

        public static void AddFlocks(FlockSystem flocks, Vector2 around, float range, ulong mask, List<PhotoSubject> into)
        {
            if (flocks == null || !flocks.isActiveAndEnabled) return;
            bool dives = (mask & (1UL << (int)LifeKind.Seabird)) != 0 && PhotoTaskCatalog.IndexOf(LifeKind.Seabird) >= 0;
            bool murmurs = (mask & (1UL << (int)LifeKind.Bird)) != 0 && PhotoTaskCatalog.IndexOf(LifeKind.Bird) >= 0;
            if (!dives && !murmurs) return;
            float range2 = range * range;
            int fn = flocks.FlockCount;
            for (int i = 0; i < fn; i++)
            {
                if ((flocks.PositionOf(i) - around).sqrMagnitude > range2) continue;
                if (flocks.IsSeabird(i))
                {
                    if (!dives) continue;
                    int bn = flocks.BirdCountOf(i);
                    for (int b = 0; b < bn; b++)
                        if (flocks.IsDiving(i, b))
                            into.Add(new PhotoSubject { task = PhotoTaskCatalog.IndexOf(LifeKind.Seabird), world = flocks.BirdPosition(i, b), size = DiveSize, flocks = flocks, a = i, b = b });
                }
                else if (murmurs && flocks.StateOf(i) == FlockSystem.FlockState.Murmur)
                {
                    Vector2 p = flocks.PositionOf(i);
                    into.Add(new PhotoSubject { task = PhotoTaskCatalog.IndexOf(LifeKind.Bird), world = new Vector3(p.x, flocks.HeightOf(i), p.y), size = MurmurSize, flocks = flocks, a = i, b = -1 });
                }
            }
        }

        // Where the subject is now, while it still performs; false once it stopped or went away.
        public static bool Refresh(ref PhotoSubject s)
        {
            if (s.herds != null)
            {
                var herds = s.herds;
                if (!herds.isActiveAndEnabled || s.a >= herds.HerdCount || s.b >= herds.HerdSize(s.a)) return false;
                var task = PhotoTaskCatalog.At(s.task);
                if (herds.HerdKind(s.a) != task.kind || !HerdPerforming(herds, s.a, s.b, task.activity)) return false;
                Vector3 local = herds.AnimalLocalPosition(s.a, s.b);
                local.y += s.size * 0.35f;
                s.world = herds.transform.TransformPoint(local);
                return true;
            }
            if (s.critters != null)
            {
                if (s.island == null || !s.critters.isActiveAndEnabled) return false;
                if (s.a < 0)
                {
                    if (!s.critters.FireflyWaveActive) return false;
                    s.world = WaveWorld(s.island, s.critters);
                    return true;
                }
                var kind = PhotoTaskCatalog.At(s.task).kind;
                if (s.a >= s.critters.CritterCount || s.critters.KindOf(s.a) != kind || !CritterPerforming(s.critters, s.a, kind)) return false;
                s.world = CritterWorld(s.island, s.critters, s.a);
                return true;
            }
            if (s.flocks != null)
            {
                var f = s.flocks;
                if (!f.isActiveAndEnabled || s.a >= f.FlockCount) return false;
                if (s.b >= 0)
                {
                    if (!f.IsSeabird(s.a) || s.b >= f.BirdCountOf(s.a) || !f.IsDiving(s.a, s.b)) return false;
                    s.world = f.BirdPosition(s.a, s.b);
                    return true;
                }
                if (f.IsSeabird(s.a) || f.StateOf(s.a) != FlockSystem.FlockState.Murmur) return false;
                Vector2 p = f.PositionOf(s.a);
                s.world = new Vector3(p.x, f.HeightOf(s.a), p.y);
                return true;
            }
            return false;
        }

        // Inside the frame, margin away from its edges, and large enough.
        public static bool InPicture(in PhotoView view, in PhotoSubject s, float minShare, float margin, out Vector2 viewport)
        {
            if (!view.Project(s.world, out viewport, out float depth)) return false;
            if (viewport.x < margin || viewport.x > 1f - margin || viewport.y < margin || viewport.y > 1f - margin) return false;
            return view.ScreenShare(s.size, depth) >= minShare;
        }

        // Which tasks the picture fulfils: for each, the largest subject in it (its viewport position goes to the
        // thumbnail crop). Returns how many tasks were found; tasks and centres hold them.
        public static int Judge(in PhotoView view, List<PhotoSubject> subjects, float minShare, float margin, List<int> tasks, List<Vector2> centres)
        {
            tasks.Clear();
            centres.Clear();
            var best = new List<float>();
            for (int i = 0; i < subjects.Count; i++)
            {
                var s = subjects[i];
                if (s.task < 0 || !InPicture(view, s, minShare, margin, out Vector2 vp)) continue;
                view.Project(s.world, out _, out float depth);
                float share = view.ScreenShare(s.size, depth);
                int k = tasks.IndexOf(s.task);
                if (k < 0)
                {
                    tasks.Add(s.task);
                    centres.Add(vp);
                    best.Add(share);
                }
                else if (share > best[k])
                {
                    centres[k] = vp;
                    best[k] = share;
                }
            }
            return tasks.Count;
        }

        // A square crop of the picture around a viewport point (as a normalised rect for Graphics.Blit), side = share of
        // the short side, pushed back inside the picture.
        public static Rect CropAround(int width, int height, Vector2 viewport, float share)
        {
            if (width <= 0 || height <= 0) return new Rect(0f, 0f, 1f, 1f);
            float side = Mathf.Clamp01(share) * Mathf.Min(width, height);
            float w = side / width, h = side / height;
            float x = Mathf.Clamp(viewport.x - w * 0.5f, 0f, 1f - w), y = Mathf.Clamp(viewport.y - h * 0.5f, 0f, 1f - h);
            return new Rect(x, y, w, h);
        }

        // The square picture of a task from the photo; the caller owns the texture.
        public static Texture2D MakeThumb(Texture source, Rect crop, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(source, rt, new Vector2(crop.width, crop.height), new Vector2(crop.x, crop.y));
            RenderTexture.active = rt;
            var thumb = new Texture2D(size, size, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            thumb.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            thumb.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return thumb;
        }
    }
}
