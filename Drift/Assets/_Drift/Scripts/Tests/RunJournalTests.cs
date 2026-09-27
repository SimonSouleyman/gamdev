using System;
using System.IO;
using Drift.Bridge;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Everything runs against a temporary RunJournal.RootOverride; the owner's run journal is never read or written.
    public class RunJournalTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "drift_runjournal_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            RunJournal.RootOverride = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            RunJournal.WaitForWrite();
            RunJournal.RootOverride = null;
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        static RunRecord Record(int n) => new RunRecord
        {
            date = new DateTime(2026, 9, 20 + n, 18, 30, 0).ToString("s"),
            seed = 4200 + n,
            playSeconds = 600f + n,
            islandsMerged = 18 + n,
            landArea = 1500.5f + n,
            speciesSeen = 20 + n,
            photos = n,
        };

        static Texture2D Picture(int w, int h, Color32 left, Color32 right)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = x < w / 2 ? left : right;
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        [Test]
        public void Records_RoundTripThroughTheFileWithPicture()
        {
            var pic = Picture(1024, 576, new Color32(200, 40, 30, 255), new Color32(20, 60, 220, 255));
            bool changed = false;
            Action onChanged = () => changed = true;
            RunJournal.Changed += onChanged;
            try { RunJournal.Add(Record(1), pic); }
            finally { RunJournal.Changed -= onChanged; }
            Assert.IsTrue(changed);
            Assert.IsTrue(pic != null, "the caller keeps its texture");
            UnityEngine.Object.DestroyImmediate(pic);

            RunJournal.WaitForWrite();
            Assert.IsTrue(File.Exists(Path.Combine(_dir, RunJournal.FileName)));
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_dir, RunJournal.ImageFolderName), "*.png").Length);

            RunJournal.Reload();
            Assert.IsFalse(RunJournal.Loaded);
            Assert.AreEqual(1, RunJournal.Records.Count);
            var r = RunJournal.Records[0];
            var e = Record(1);
            Assert.AreEqual(e.date, r.date);
            Assert.AreEqual(e.seed, r.seed);
            Assert.AreEqual(e.playSeconds, r.playSeconds, 1e-4f);
            Assert.AreEqual(e.islandsMerged, r.islandsMerged);
            Assert.AreEqual(e.landArea, r.landArea, 1e-3f);
            Assert.AreEqual(e.speciesSeen, r.speciesSeen);
            Assert.AreEqual(e.photos, r.photos);
            Assert.IsFalse(string.IsNullOrEmpty(r.image));

            var loaded = RunJournal.LoadPicture(r);
            try
            {
                Assert.IsNotNull(loaded);
                Assert.AreEqual(RunJournal.PictureMaxSide, loaded.width, "downscaled to the maximum side");
                Assert.AreEqual(288, loaded.height, "aspect kept");
                Assert.IsFalse(loaded.isReadable, "no CPU copy kept for a picture that is only shown");
            }
            finally
            {
                if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            }

            var check = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(check.LoadImage(File.ReadAllBytes(Path.Combine(_dir, RunJournal.ImageFolderName, r.image))));
                Color32 a = check.GetPixel(40, 100), b = check.GetPixel(470, 100);
                Assert.AreEqual(200, a.r, 2);
                Assert.AreEqual(40, a.g, 2);
                Assert.AreEqual(220, b.b, 2);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(check);
            }
        }

        [Test]
        public void Records_KeepTheirOrderAndNumbers()
        {
            for (int i = 1; i <= 3; i++) RunJournal.Add(Record(i), null);
            RunJournal.Reload();
            Assert.AreEqual(3, RunJournal.Records.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(4201 + i, RunJournal.Records[i].seed, "oldest first in the file");
                Assert.AreEqual(i + 1, RunJournal.NumberOf(RunJournal.Records[i]));
                Assert.IsTrue(string.IsNullOrEmpty(RunJournal.Records[i].image));
                Assert.IsNull(RunJournal.LoadPicture(RunJournal.Records[i]));
            }
        }

        [Test]
        public void MissingFile_IsAnEmptyJournal()
        {
            Assert.AreEqual(0, RunJournal.Records.Count);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, RunJournal.FileName)), "reading writes nothing");
        }

        [Test]
        public void CorruptFile_StartsAnEmptyJournalAndKeepsACopy()
        {
            string path = Path.Combine(_dir, RunJournal.FileName);
            File.WriteAllText(path, "{ \"records\": [ {\"seed\": 1, ");
            RunJournal.Reload();
            Assert.AreEqual(0, RunJournal.Records.Count);
            Assert.IsTrue(File.Exists(path + ".corrupt"));

            RunJournal.Add(Record(2), null);
            RunJournal.Reload();
            Assert.AreEqual(1, RunJournal.Records.Count);
            Assert.AreEqual(4202, RunJournal.Records[0].seed);
        }

        [Test]
        public void Clear_RemovesRecordsAndPictures()
        {
            var pic = Picture(64, 32, new Color32(10, 200, 10, 255), new Color32(10, 200, 10, 255));
            RunJournal.Add(Record(1), pic);
            UnityEngine.Object.DestroyImmediate(pic);
            RunJournal.Clear();
            RunJournal.Reload();
            Assert.AreEqual(0, RunJournal.Records.Count);
            Assert.AreEqual(0, Directory.GetFiles(Path.Combine(_dir, RunJournal.ImageFolderName), "*.png").Length);
        }

        [Test]
        public void Downscale_AveragesBoxes()
        {
            var src = new Color32[4 * 2];
            for (int i = 0; i < src.Length; i++) src[i] = i % 2 == 0 ? new Color32(0, 0, 0, 255) : new Color32(200, 100, 50, 255);
            var dst = RunJournal.Downscale(src, 4, 2, 2, out int w, out int h);
            Assert.AreEqual(2, w);
            Assert.AreEqual(1, h);
            Assert.AreEqual(100, dst[0].r);
            Assert.AreEqual(50, dst[1].g);
        }

        [Test]
        public void Formatting_IsGerman()
        {
            Assert.AreEqual("12 Min.", RunJournalPanel.FormatPlayTime(12 * 60 + 10));
            Assert.AreEqual("45 Sek.", RunJournalPanel.FormatPlayTime(45f));
            Assert.AreEqual("1 Std. 05 Min.", RunJournalPanel.FormatPlayTime(65 * 60));
            Assert.AreEqual("22. September 2026, 18:42 Uhr", RunJournalPanel.FormatDate("2026-09-22T18:42:00"));
            var crop = RunJournalPanel.CoverCrop(1080, 1920, 16f / 9f);
            Assert.AreEqual(1f, crop.width, 1e-4f);
            Assert.Less(crop.height, 0.5f);
        }

        [Test]
        public void Panel_ListsTheJournalNewestFirstAndClosesOnBack()
        {
            for (int i = 1; i <= 3; i++) RunJournal.Add(Record(i), null);
            var go = new GameObject("RunJournalPanelTest");
            try
            {
                var panel = go.AddComponent<RunJournalPanel>();
                Assert.IsFalse(panel.IsOpen);
                panel.Open();
                Assert.IsTrue(panel.IsOpen);
                Assert.IsTrue(RunJournalPanel.OwnsBack);
                Assert.AreEqual(3, panel.CardCount);

                var content = go.transform.Find("RunJournalCanvas/SafeArea/RunJournalScreen/Fit/Panel/List/Content");
                Assert.IsNotNull(content);
                var first = content.GetChild(0).GetComponentsInChildren<UnityEngine.UI.Text>(true);
                bool newest = false;
                foreach (var t in first) if (t.text == "Pangäa #3") newest = true;
                Assert.IsTrue(newest, "the newest run is on top");

                panel.Back();
                Assert.IsFalse(panel.IsOpen);

                panel.OpenSample(0);
                Assert.IsTrue(panel.IsOpen);
                Assert.AreEqual(0, panel.CardCount);
                panel.OpenSample(6);
                for (int i = 0; i < 8; i++) panel.Tick();
                Assert.Greater(panel.LoadedPictures, 0);
                panel.ShowPicture(0);
                Assert.IsTrue(panel.ViewingPicture);
                panel.Back();
                Assert.IsFalse(panel.ViewingPicture);
                Assert.IsTrue(panel.IsOpen);
                panel.Close();
                Assert.AreEqual(0, panel.LoadedPictures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
