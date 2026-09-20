using System;
using System.Collections.Generic;
using System.IO;
using Drift.Bridge;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    public class WatchToolsTests
    {
        // ------------------------------------------------------------ names (the album itself: CollectionTests)

        [Test]
        public void Names_EveryLifeKindHasAGermanNameInThePopup()
        {
            foreach (LifeKind kind in Enum.GetValues(typeof(LifeKind)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(WatchTools.NameOf(kind)), kind.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(WatchTools.PluralOf(kind)), kind.ToString());
            }
            Assert.AreEqual("Wasserschwein", WatchTools.NameOf(LifeKind.Capybara));
            Assert.AreEqual("Erdmännchen", WatchTools.NameOf(LifeKind.Meerkat));
            Assert.AreEqual("Affenbrotbaum", WatchTools.NameOf(LifeKind.Baobab));
            Assert.AreEqual("Polarfüchse", WatchTools.PluralOf(LifeKind.ArcticFox));
            Assert.AreEqual("Glühwürmchen", WatchTools.NameOf(LifeKind.Firefly));
            Assert.AreEqual("Giraffen  ·  4 Tiere  ·  grast", WatchTools.FollowChipLine(LifeKind.Giraffe, 4, (int)AnimalState.Graze));
        }

        [Test]
        public void Popup_ForeignSpeciesNameTheirHomeBiome()
        {
            Assert.AreEqual("Fremde Art von tropischen Inseln", WatchTools.OriginLine(LifeKind.Flamingo));
            Assert.AreEqual("Fremde Art von nordischen Inseln", WatchTools.OriginLine(LifeKind.Penguin));
            Assert.AreEqual("Fremde Art von Savanneninseln", WatchTools.OriginLine(LifeKind.Baobab));
            Assert.AreEqual("Fremde Art von gemäßigten Inseln", WatchTools.OriginLine(LifeKind.Sheep));
            Assert.AreEqual("Fremde Art", WatchTools.OriginLine(LifeKind.Crab), "critters have no home biome");
        }

        // ------------------------------------------------------------ tap picking

        [Test]
        public void TapPicker_PicksNearestInsideRadius()
        {
            var points = new List<Vector3>
            {
                new Vector3(100f, 100f, 5f),
                new Vector3(130f, 100f, 5f),
                new Vector3(112f, 104f, 5f),
            };
            int i = TapPicker.Nearest(new Vector2(110f, 100f), points, 40f, out float d);
            Assert.AreEqual(2, i);
            Assert.AreEqual(Mathf.Sqrt(4f + 16f), d, 1e-4f);
        }

        [Test]
        public void TapPicker_IgnoresOutOfRadiusAndBehindCamera()
        {
            var points = new List<Vector3>
            {
                new Vector3(300f, 300f, 5f),
                new Vector3(101f, 100f, -2f),
            };
            Assert.AreEqual(-1, TapPicker.Nearest(new Vector2(100f, 100f), points, 40f, out float d));
            Assert.AreEqual(-1f, d);
            float best = float.MaxValue;
            Assert.IsFalse(TapPicker.Consider(new Vector2(100f, 100f), new Vector3(100f, 100f, 0f), 40f, ref best));
            Assert.IsTrue(TapPicker.Consider(new Vector2(100f, 100f), new Vector3(100f, 130f, 1f), 40f, ref best));
            Assert.AreEqual(30f, best, 1e-4f);
            Assert.IsFalse(TapPicker.Consider(new Vector2(100f, 100f), new Vector3(100f, 131f, 1f), 40f, ref best), "farther than the current best");
        }

        // ------------------------------------------------------------ tap gating

        static TapPress CleanPress() => new TapPress { seconds = 0.12f, movedPixels = 4f };

        [Test]
        public void TapGate_AcceptsAShortStillPressWhilePlaying()
        {
            Assert.AreEqual(TapReject.None, TapPicker.Gate(true, CleanPress(), 0.6f, 30f));
            Assert.AreEqual(TapReject.NotPlaying, TapPicker.Gate(false, CleanPress(), 0.6f, 30f));
        }

        [Test]
        public void TapGate_RejectsUiDragHoldAndMultiTouch()
        {
            var p = CleanPress(); p.overUi = true;
            Assert.AreEqual(TapReject.OverUi, TapPicker.Gate(true, p, 0.6f, 30f));
            p = CleanPress(); p.multiTouch = true;
            Assert.AreEqual(TapReject.MultiTouch, TapPicker.Gate(true, p, 0.6f, 30f));
            p = CleanPress(); p.pinching = true;
            Assert.AreEqual(TapReject.Pinching, TapPicker.Gate(true, p, 0.6f, 30f));
            p = CleanPress(); p.seconds = 0.61f;
            Assert.AreEqual(TapReject.HeldTooLong, TapPicker.Gate(true, p, 0.6f, 30f));
            p = CleanPress(); p.movedPixels = 31f;
            Assert.AreEqual(TapReject.MovedTooFar, TapPicker.Gate(true, p, 0.6f, 30f));
        }

        [Test]
        public void TapGate_StickOnlyVetoesAPressItSteeredWith()
        {
            // A press that merely starts in the thumbstick half carries no flag at all: it is a tap.
            Assert.AreEqual(TapReject.None, TapPicker.Gate(true, CleanPress(), 0.6f, 30f));
            var p = CleanPress(); p.steered = true;
            Assert.AreEqual(TapReject.Steered, TapPicker.Gate(true, p, 0.6f, 30f));
        }

        [Test]
        public void TapPicker_PixelsPerUnitNeverShrinksBelowTheShortScreenSide()
        {
            // Landscape 1080p: the 1080 x 1920 Expand scaler reports 0.5625, the pick radius must not follow it down.
            Assert.AreEqual(1f, TapPicker.PixelsPerUnit(0.5625f, 1920f, 1080f), 1e-4f);
            Assert.AreEqual(1f, TapPicker.PixelsPerUnit(1f, 1080f, 1920f), 1e-4f);
            Assert.AreEqual(1440f / 1080f, TapPicker.PixelsPerUnit(0.75f, 2560f, 1440f), 1e-4f);
            Assert.GreaterOrEqual(64f * TapPicker.PixelsPerUnit(0.5625f, 1920f, 1080f), 60f);
            Assert.AreEqual(0.5f, TapPicker.PixelsPerUnit(0.5f, 540f, 960f), 1e-4f);
        }

        [Test]
        public void FollowChip_NamesTheHerdInPlural()
        {
            Assert.AreEqual("Schafe  ·  7 Tiere  ·  grast", WatchTools.FollowChipLine(LifeKind.Sheep, 7, (int)AnimalState.Graze));
            Assert.AreEqual("Hase  ·  allein  ·  schläft", WatchTools.FollowChipLine(LifeKind.Hare, 1, (int)AnimalState.Sleep));
            Assert.AreEqual("Ochsen", WatchTools.PluralOf(LifeKind.Ox));
            Assert.AreEqual("Ziegen", WatchTools.PluralOf(LifeKind.Goat));
        }

        [Test]
        public void SinkAllowed_TutorialAndPhotoHoldAreIndependent()
        {
            Assert.IsTrue(GameSession.SinkAllowed(true, false, false));
            Assert.IsFalse(GameSession.SinkAllowed(true, true, false));
            Assert.IsFalse(GameSession.SinkAllowed(true, false, true));
            Assert.IsFalse(GameSession.SinkAllowed(true, true, true));
            Assert.IsFalse(GameSession.SinkAllowed(false, false, false));
        }

        // ------------------------------------------------------------ photo album files

        static string MakeTempAlbum()
        {
            string dir = Path.Combine(Path.GetTempPath(), "drift_album_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        public void PhotoLibrary_NamesParseBackToTheirDate()
        {
            var t = new DateTime(2026, 9, 20, 12, 3, 1);
            Assert.AreEqual("drift_20260920_120301.png", PhotoLibrary.FileNameFor(t));
            Assert.IsTrue(PhotoLibrary.TryParseDate("drift_20260920_120301.png", out var back));
            Assert.AreEqual(t, back);
            Assert.IsTrue(PhotoLibrary.TryParseDate(Path.Combine("x", "drift_20260920_120301_2.png"), out back));
            Assert.AreEqual(t, back);
            Assert.IsFalse(PhotoLibrary.TryParseDate("holiday.png", out _));
            Assert.IsFalse(PhotoLibrary.TryParseDate("drift_2026.png", out _));
            Assert.AreEqual(Path.Combine("x", "drift_20260920_120301_thumb.jpg"), PhotoLibrary.ThumbPathOf(Path.Combine("x", "drift_20260920_120301.png")));
            Assert.AreEqual("20. September 2026  ·  12:03 Uhr", PhotoLibrary.FormatDate(t));
        }

        [Test]
        public void PhotoLibrary_ListsNewestFirstAndSkipsThumbnailsAndStrangers()
        {
            string dir = MakeTempAlbum();
            try
            {
                var times = new[] { new DateTime(2026, 9, 18, 8, 0, 0), new DateTime(2026, 9, 20, 12, 3, 1), new DateTime(2026, 9, 19, 23, 59, 59) };
                foreach (var t in times)
                {
                    string path = Path.Combine(dir, PhotoLibrary.FileNameFor(t));
                    File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                    File.WriteAllBytes(PhotoLibrary.ThumbPathOf(path), new byte[] { 4 });
                }
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
                File.WriteAllBytes(Path.Combine(dir, "other.png"), new byte[] { 9 });

                var list = new List<PhotoEntry>();
                PhotoLibrary.List(dir, list);
                Assert.AreEqual(3, list.Count);
                Assert.AreEqual(3, PhotoLibrary.Count(dir));
                Assert.AreEqual("drift_20260920_120301.png", list[0].name);
                Assert.AreEqual("drift_20260919_235959.png", list[1].name);
                Assert.AreEqual("drift_20260918_080000.png", list[2].name);
                Assert.AreEqual(times[1], list[0].taken);
                Assert.IsTrue(File.Exists(list[0].thumbPath));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void PhotoLibrary_UniquePathNeverOverwrites()
        {
            string dir = MakeTempAlbum();
            try
            {
                var t = new DateTime(2026, 9, 20, 12, 3, 1);
                string first = PhotoLibrary.UniquePath(dir, t);
                File.WriteAllBytes(first, new byte[] { 1 });
                string second = PhotoLibrary.UniquePath(dir, t);
                Assert.AreNotEqual(first, second);
                Assert.AreEqual("drift_20260920_120301_2.png", Path.GetFileName(second));
                File.WriteAllBytes(second, new byte[] { 1 });
                var list = new List<PhotoEntry>();
                PhotoLibrary.List(dir, list);
                Assert.AreEqual(2, list.Count);
                Assert.AreEqual("drift_20260920_120301_2.png", list[0].name, "same second: the later file sorts first");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void PhotoLibrary_DeleteRemovesPictureAndThumbnail()
        {
            string dir = MakeTempAlbum();
            try
            {
                string path = Path.Combine(dir, PhotoLibrary.FileNameFor(new DateTime(2026, 1, 2, 3, 4, 5)));
                File.WriteAllBytes(path, new byte[] { 1 });
                File.WriteAllBytes(PhotoLibrary.ThumbPathOf(path), new byte[] { 2 });
                var list = new List<PhotoEntry>();
                PhotoLibrary.List(dir, list);
                Assert.AreEqual(1, list.Count);
                Assert.IsTrue(PhotoLibrary.Delete(list[0]));
                Assert.IsFalse(File.Exists(path));
                Assert.IsFalse(File.Exists(PhotoLibrary.ThumbPathOf(path)));
                PhotoLibrary.List(dir, list);
                Assert.AreEqual(0, list.Count);
                Assert.IsFalse(PhotoLibrary.Delete(default));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void PhotoLibrary_MissingFolderIsAnEmptyAlbum()
        {
            var list = new List<PhotoEntry> { new PhotoEntry { name = "stale" } };
            PhotoLibrary.List(Path.Combine(Path.GetTempPath(), "drift_album_missing_" + Guid.NewGuid().ToString("N")), list);
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(0, PhotoLibrary.Count(null));
            Assert.AreEqual("Noch keine Fotos", PhotoLibrary.CountLabel(0));
            Assert.AreEqual("1 Foto", PhotoLibrary.CountLabel(1));
            Assert.AreEqual("12 Fotos", PhotoLibrary.CountLabel(12));
        }

        [Test]
        public void PhotoLibrary_PagingThumbSizeAndCrop()
        {
            Assert.AreEqual(1, PhotoLibrary.PageCount(0, 12));
            Assert.AreEqual(1, PhotoLibrary.PageCount(12, 12));
            Assert.AreEqual(2, PhotoLibrary.PageCount(13, 12));
            Assert.AreEqual(new Vector2Int(256, 144), PhotoLibrary.ThumbDimensions(2560, 1440, 256));
            Assert.AreEqual(new Vector2Int(144, 256), PhotoLibrary.ThumbDimensions(1080, 1920, 256));
            Assert.AreEqual(new Vector2Int(100, 50), PhotoLibrary.ThumbDimensions(100, 50, 256), "never upscaled");
            var crop = PhotoLibrary.SquareCrop(200, 100);
            Assert.AreEqual(0.25f, crop.x, 1e-5f);
            Assert.AreEqual(0.5f, crop.width, 1e-5f);
            Assert.AreEqual(1f, crop.height, 1e-5f);
            crop = PhotoLibrary.SquareCrop(100, 400);
            Assert.AreEqual(0.375f, crop.y, 1e-5f);
            Assert.AreEqual(0.25f, crop.height, 1e-5f);
        }

        [Test]
        public void PhotoLibrary_SaveWritesPngAndThumbnail()
        {
            string dir = MakeTempAlbum();
            var shot = new Texture2D(64, 36, TextureFormat.RGB24, false);
            try
            {
                var entry = PhotoLibrary.Save(shot, dir, new DateTime(2026, 9, 20, 18, 0, 0));
                Assert.IsTrue(File.Exists(entry.path));
                Assert.IsTrue(File.Exists(entry.thumbPath));
                var thumb = PhotoLibrary.LoadThumbnail(entry, false);
                Assert.IsNotNull(thumb);
                Assert.AreEqual(64, thumb.width);
                UnityEngine.Object.DestroyImmediate(thumb);
                var list = new List<PhotoEntry>();
                PhotoLibrary.List(dir, list);
                Assert.AreEqual(1, list.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(shot);
                Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------ follow override

        [Test]
        public void FollowPose_SmallRadiusUsesUnitScale()
        {
            Vector3 focus = new Vector3(10f, 0f, -4f);
            IslandChaseCamera.FollowPose(focus, Vector3.up, Vector3.back, 2f, 3f, 0.85f, 9f, 7f, 1f, out var pos, out var rot, out float scale);
            Assert.AreEqual(1f, scale, 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(pos, focus + new Vector3(0f, 9f, -7f)), 1e-4f);
            Vector3 toLook = (focus + Vector3.up * 0.4f - pos).normalized;
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, toLook), 0.9999f);
        }

        [Test]
        public void FollowPose_LargerRadiusAndZoomMoveTheCameraOut()
        {
            Vector3 focus = Vector3.zero;
            IslandChaseCamera.FollowPose(focus, Vector3.up, Vector3.back, 3f, 3f, 0.85f, 9f, 7f, 1f, out var near, out _, out _);
            IslandChaseCamera.FollowPose(focus, Vector3.up, Vector3.back, 12f, 3f, 0.85f, 9f, 7f, 1f, out var big, out _, out float bigScale);
            IslandChaseCamera.FollowPose(focus, Vector3.up, Vector3.back, 3f, 3f, 0.85f, 9f, 7f, 2f, out var zoomed, out _, out _);
            Assert.AreEqual(Mathf.Pow(4f, 0.85f), bigScale, 1e-5f);
            Assert.Greater(big.magnitude, near.magnitude);
            Assert.Greater(zoomed.magnitude, near.magnitude);
            Assert.AreEqual(18f, zoomed.y, 1e-4f);
            Assert.AreEqual(-7f * Mathf.Pow(2f, 0.65f), zoomed.z, 1e-4f);
        }

        [Test]
        public void FollowPose_FollowsTheIslandFrame()
        {
            Vector3 back = new Vector3(1f, 0f, 0f);
            IslandChaseCamera.FollowPose(Vector3.zero, Vector3.up, back, 1f, 3f, 0.85f, 9f, 7f, 1f, out var pos, out _, out _);
            Assert.AreEqual(7f, pos.x, 1e-4f);
            Assert.AreEqual(0f, pos.z, 1e-4f);
        }

        // ------------------------------------------------------------ orbit rig (photo mode + herd following)

        static PhotoRig MakeRig(float subjectRadius = 3f, float panLimit = 4.5f)
        {
            var rig = new PhotoRig();
            rig.SetLimits(subjectRadius, 1.5f, panLimit);
            return rig;
        }

        static void Settle(PhotoRig rig, OrbitInput input = default, int steps = 200)
        {
            for (int i = 0; i < steps; i++) rig.Step(input, 0.05f);
        }

        [Test]
        public void PhotoRig_PoseRoundTripsThroughFromPose()
        {
            var rig = MakeRig();
            Vector3 cam = new Vector3(3f, 8f, -6f), focus = new Vector3(1f, 0.5f, 2f);
            rig.FromPose(cam, focus);
            rig.Pose(focus, out var pos, out var rot);
            Assert.AreEqual(0f, Vector3.Distance(pos, cam), 1e-3f);
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, (focus - cam).normalized), 0.9999f);
            Assert.AreEqual(Vector2.zero, rig.offset);
        }

        [Test]
        public void PhotoRig_FromPoseKeepsTheCameraButClampsTheTargets()
        {
            var rig = MakeRig(3f);
            Vector3 focus = Vector3.zero, cam = new Vector3(0f, 0.05f, -400f);
            rig.FromPose(cam, focus);
            rig.Pose(focus, out var pos, out _);
            Assert.AreEqual(0f, Vector3.Distance(pos, cam), 1e-2f, "no jump on take-over");
            Assert.AreEqual(rig.maxDistance, rig.distanceTarget, 1e-4f);
            Assert.AreEqual(rig.minPitch, rig.pitchTarget, 1e-4f);
            Settle(rig);
            Assert.AreEqual(rig.maxDistance, rig.distance, 1e-2f);
            Assert.AreEqual(rig.minPitch, rig.pitch, 1e-2f);
        }

        [Test]
        public void PhotoRig_PivotFollowsTheSubject()
        {
            var rig = MakeRig(6f, 9f);
            Vector3 subject = new Vector3(4f, 0.3f, -2f);
            rig.FromPose(subject + new Vector3(2f, 7f, -9f), subject);
            rig.Pan(300f, -120f);
            Settle(rig);
            Vector2 offset = rig.offset;
            Assert.Greater(offset.magnitude, 0.5f);
            rig.Pose(subject, out var before, out var rotBefore);

            Vector3 moved = subject + new Vector3(10f, 0f, 0f);
            rig.Step(default, 0.016f);
            rig.Pose(moved, out var after, out var rotAfter);
            Assert.AreEqual(0f, Vector3.Distance(after - before, new Vector3(10f, 0f, 0f)), 1e-4f, "subject moves 10 u, camera moves 10 u");
            Assert.AreEqual(0f, (rig.offset - offset).magnitude, 1e-5f, "offset is relative to the subject");
            Assert.AreEqual(0f, Quaternion.Angle(rotBefore, rotAfter), 1e-3f);
            Assert.AreEqual(0f, Vector3.Distance(rig.Pivot(moved) - rig.Pivot(subject), new Vector3(10f, 0f, 0f)), 1e-5f);
        }

        [Test]
        public void PhotoRig_LimitsComeFromTheSubject()
        {
            PhotoRig.DistanceLimits(3f, 1.5f, out float min, out float max);
            Assert.AreEqual(1.5f, min, 1e-5f);
            Assert.AreEqual(36f, max, 1e-4f);
            PhotoRig.DistanceLimits(40f, 1.5f, out _, out float big);
            Assert.Greater(big, 250f);
            PhotoRig.DistanceLimits(0f, 1.5f, out _, out float tiny);
            Assert.AreEqual(15f, tiny, 1e-4f);
            var rig = new PhotoRig();
            Assert.AreEqual(8f, rig.minPitch, 1e-5f);
            Assert.AreEqual(85f, rig.maxPitch, 1e-5f);
        }

        [Test]
        public void PhotoRig_PointerOrbitZoomAndPanStayInBounds()
        {
            var rig = MakeRig(3f, 4.5f);
            rig.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            rig.Orbit(0f, 100000f);
            Assert.AreEqual(rig.maxPitch, rig.pitchTarget, 1e-4f);
            rig.Orbit(0f, -100000f);
            Assert.AreEqual(rig.minPitch, rig.pitchTarget, 1e-4f);
            rig.Zoom(0.0001f);
            Assert.AreEqual(rig.minDistance, rig.distanceTarget, 1e-4f);
            rig.Zoom(1e6f);
            Assert.AreEqual(rig.maxDistance, rig.distanceTarget, 1e-4f);
            rig.Zoom(-1f);
            Assert.AreEqual(rig.maxDistance, rig.distanceTarget, 1e-4f, "non-positive factors are ignored");

            rig.Pan(50f, -20f);
            Assert.Less(rig.offsetTarget.x, 0f);
            Assert.Greater(rig.offsetTarget.y, 0f);
            for (int i = 0; i < 50; i++) rig.Pan(-4000f, 0f);
            Assert.AreEqual(4.5f, rig.offsetTarget.magnitude, 1e-3f, "clamped to the pan radius");
            Settle(rig);
            Assert.LessOrEqual(rig.offset.magnitude, 4.5f + 1e-3f);
            rig.Pose(Vector3.zero, out var pos, out _);
            Assert.Greater(pos.y, rig.Pivot(Vector3.zero).y);

            rig.Step(new OrbitInput { center = true }, 0.016f);
            Assert.AreEqual(Vector2.zero, rig.offsetTarget);
            Settle(rig);
            Assert.AreEqual(0f, rig.offset.magnitude, 1e-3f);
        }

        [Test]
        public void PhotoRig_PanIsOffWithoutAPanRadius()
        {
            var rig = MakeRig(3f, 0f);
            rig.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            rig.Step(new OrbitInput { panX = 500f, panY = 500f }, 0.016f);
            Assert.AreEqual(Vector2.zero, rig.offsetTarget);
            rig.offsetTarget = new Vector2(3f, 0f);
            rig.Step(default, 0.016f);
            Assert.AreEqual(Vector2.zero, rig.offsetTarget, "leaving photo mode brings the pivot back to the herd");
        }

        [Test]
        public void PhotoRig_KeysRotateTiltZoomAndClamp()
        {
            var rig = MakeRig();
            rig.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            float yaw0 = rig.yawTarget, pitch0 = rig.pitchTarget, dist0 = rig.distanceTarget;

            rig.Step(new OrbitInput { keyYaw = 1f }, 0.5f);
            Assert.AreEqual(yaw0 - rig.keyYawSpeed * 0.5f, rig.yawTarget, 1e-3f, "D: the camera travels to its right");
            rig.Pose(Vector3.zero, out var pos, out _);
            Assert.Greater(pos.x, 0.1f);
            rig.Step(new OrbitInput { keyYaw = -1f }, 1f);
            Assert.AreEqual(yaw0 + rig.keyYawSpeed * 0.5f, rig.yawTarget, 1e-3f);

            rig.Step(new OrbitInput { keyPitch = 1f }, 0.2f);
            Assert.AreEqual(pitch0 + rig.keyPitchSpeed * 0.2f, rig.pitchTarget, 1e-3f, "W tilts up");
            Settle(rig, new OrbitInput { keyPitch = 1f });
            Assert.AreEqual(rig.maxPitch, rig.pitch, 1e-2f);
            Settle(rig, new OrbitInput { keyPitch = -1f });
            Assert.AreEqual(rig.minPitch, rig.pitch, 1e-2f);

            rig.distanceTarget = dist0;
            rig.Step(new OrbitInput { keyZoom = 1f }, 0.25f);
            Assert.AreEqual(dist0 * Mathf.Exp(-rig.keyZoomSpeed * 0.25f), rig.distanceTarget, 1e-3f, "Q zooms in");
            Settle(rig, new OrbitInput { keyZoom = 1f });
            Assert.AreEqual(rig.minDistance, rig.distance, 1e-2f);
            Settle(rig, new OrbitInput { keyZoom = -1f });
            Assert.AreEqual(rig.maxDistance, rig.distance, 1e-2f);

            rig.Step(new OrbitInput { zoomFactor = 0.5f }, 0f);
            Assert.AreEqual(rig.maxDistance * 0.5f, rig.distanceTarget, 1e-3f);
            Assert.IsFalse(default(OrbitInput).Any);
            Assert.IsTrue(new OrbitInput { keyYaw = 1f }.Any);
        }

        [Test]
        public void PhotoRig_SmoothingEasesAndIsFrameRateIndependent()
        {
            var a = MakeRig();
            var b = MakeRig();
            a.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            b.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            a.Orbit(200f, 0f);
            b.Orbit(200f, 0f);
            float target = a.yawTarget;
            a.Step(default, 0.1f);
            Assert.Greater(a.yaw, 0f);
            Assert.Less(a.yaw, target, "eased, not snapped");
            for (int i = 0; i < 10; i++) b.Step(default, 0.01f);
            Assert.AreEqual(a.yaw, b.yaw, 1e-3f);
            a.Step(default, 0f);
            Assert.AreEqual(b.yaw, a.yaw, 1e-3f, "dt 0 changes nothing");
            Settle(a);
            Assert.AreEqual(target, a.yaw, 1e-3f);
        }

        [Test]
        public void PhotoRig_ResetReturnsHomeTheShortWay()
        {
            var rig = MakeRig(3f, 4.5f);
            rig.FromPose(new Vector3(0f, 7f, -7f), Vector3.zero);
            rig.HomeFromTargets();
            float homeYaw = rig.homeYaw, homePitch = rig.homePitch, homeDist = rig.homeDistance;
            Settle(rig, new OrbitInput { keyYaw = -1f, keyPitch = 1f, keyZoom = -1f, panX = 3f }, 190);
            Assert.Greater(Mathf.Abs(rig.yaw - homeYaw), 360f);
            float before = rig.yaw;
            rig.Step(new OrbitInput { reset = true }, 0f);
            Assert.LessOrEqual(Mathf.Abs(rig.yawTarget - before), 180f + 1e-3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(rig.yawTarget, homeYaw), 1e-3f);
            Settle(rig);
            Assert.AreEqual(homePitch, rig.pitch, 1e-2f);
            Assert.AreEqual(homeDist, rig.distance, 1e-2f);
            Assert.AreEqual(0f, rig.offset.magnitude, 1e-3f);
        }

        [Test]
        public void PhotoRig_HeightEasesAndCloseBlendFollowsTheDistance()
        {
            var rig = MakeRig();
            rig.FromPose(new Vector3(0f, 7f, -7f), new Vector3(0f, 1f, 0f));
            Assert.AreEqual(1f, rig.height, 1e-5f);
            rig.EaseHeight(3f, 0.1f);
            Assert.Greater(rig.height, 1f);
            Assert.Less(rig.height, 3f);
            for (int i = 0; i < 200; i++) rig.EaseHeight(3f, 0.05f);
            Assert.AreEqual(3f, rig.height, 1e-3f);
            Assert.AreEqual(1f, PhotoRig.CloseBlend(1f), 1e-5f);
            Assert.AreEqual(0f, PhotoRig.CloseBlend(5.5f), 1e-5f);
            Assert.AreEqual(0f, PhotoRig.CloseBlend(40f), 1e-5f);
        }

        // ------------------------------------------------------------ holds + camera hand-back

        [Test]
        public void InputHold_PhotoAndFollowComposeWithTheSessionState()
        {
            Assert.IsFalse(GameSession.IslandInputLocked(true, false, false));
            Assert.IsTrue(GameSession.IslandInputLocked(true, true, false));
            Assert.IsTrue(GameSession.IslandInputLocked(true, false, true));
            Assert.IsTrue(GameSession.IslandInputLocked(true, true, true), "photo entered while following");
            Assert.IsTrue(GameSession.IslandInputLocked(false, false, false), "pause / title / game over lock on their own");
        }

        [Test]
        public void InputHold_ReleasingOneHolderKeepsTheOther()
        {
            var go = new GameObject("session-test");
            go.SetActive(false);
            try
            {
                var session = go.AddComponent<GameSession>();
                session.FollowInputHold = true;
                session.PhotoInputHold = true;
                Assert.IsTrue(session.WatchInputHold);
                session.PhotoInputHold = false;
                Assert.IsTrue(session.WatchInputHold, "leaving photo mode does not unlock a followed herd");
                Assert.IsTrue(session.FollowInputHold);
                session.FollowInputHold = false;
                Assert.IsFalse(session.WatchInputHold);

                session.FollowSinkHold = true;
                session.PhotoSinkHold = true;
                session.PhotoSinkHold = false;
                Assert.IsTrue(session.FollowSinkHold);
                Assert.IsFalse(session.SinkingSuspended, "the tutorial's flag is nobody else's");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SinkAllowed_FollowHoldIsAThirdIndependentHold()
        {
            Assert.IsTrue(GameSession.SinkAllowed(true, false, false, false));
            Assert.IsFalse(GameSession.SinkAllowed(true, false, false, true));
            Assert.IsFalse(GameSession.SinkAllowed(true, true, false, true));
            Assert.IsFalse(GameSession.SinkAllowed(false, false, false, false));
        }

        [Test]
        public void EasePose_BlendsWithoutSnapAtEitherEnd()
        {
            Vector3 from = new Vector3(1f, 2f, 3f), to = new Vector3(11f, 9f, -4f);
            Quaternion fromRot = Quaternion.Euler(10f, 40f, 0f), toRot = Quaternion.Euler(50f, -20f, 0f);
            IslandChaseCamera.EasePose(from, fromRot, to, toRot, 0f, out var p0, out var r0);
            IslandChaseCamera.EasePose(from, fromRot, to, toRot, 1f, out var p1, out var r1);
            IslandChaseCamera.EasePose(from, fromRot, to, toRot, 0.02f, out var pStart, out _);
            IslandChaseCamera.EasePose(from, fromRot, to, toRot, 0.5f, out var pMid, out _);
            Assert.AreEqual(0f, Vector3.Distance(p0, from), 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(p1, to), 1e-5f);
            Assert.AreEqual(0f, Quaternion.Angle(r0, fromRot), 1e-3f);
            Assert.AreEqual(0f, Quaternion.Angle(r1, toRot), 1e-3f);
            Assert.Less(Vector3.Distance(pStart, from), 0.02f * Vector3.Distance(from, to), "starts slower than linear");
            Assert.AreEqual(0f, Vector3.Distance(pMid, (from + to) * 0.5f), 1e-4f);
        }

        [Test]
        public void OrbitHint_DiffersForKeyboardAndTouch()
        {
            Assert.AreEqual("A/D drehen  ·  W/S neigen  ·  Q/E Zoom  ·  R zurück", WatchTools.OrbitHint(false, false));
            Assert.AreEqual("Ziehen: drehen  ·  Zwei Finger: Zoom", WatchTools.OrbitHint(true, false));
            StringAssert.StartsWith(WatchTools.OrbitHint(false, false), WatchTools.OrbitHint(false, true));
            StringAssert.Contains("verschieben", WatchTools.OrbitHint(true, true));
        }
    }
}
