using System;
using System.IO;
using Drift.Bridge;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // "Tagebuch zurücksetzen" and the one-time reset of v0.6.5, against a temporary folder, test PlayerPrefs keys and a
    // temporary RunJournal root; the owner's files and keys are never touched.
    public class JournalResetTests
    {
        const string LifeKey = "drift_test_lifebook_reset";
        const string FlagKey = "drift_test_reset_flag";
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "drift_reset_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            RunJournal.RootOverride = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            RunJournal.WaitForWrite();
            RunJournal.RootOverride = null;
            PlayerPrefs.DeleteKey(LifeKey);
            PlayerPrefs.DeleteKey(FlagKey);
            try { Directory.Delete(_dir, true); } catch (Exception) { }
        }

        string P(params string[] parts) => Path.Combine(_dir, Path.Combine(parts));

        static void Touch(string path, string text = "x")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        // Every store the reset knows about, filled: album book, photo tasks with a picture, two photos with thumbs, a run
        // chronicle entry, a run save with a journal block, the best distances (must survive) and the LifeBook key.
        void FillStores()
        {
            var book = new JournalBook(_dir);
            book.Learn(CollectionCatalog.IndexOf(LifeKind.Flamingo), CollectState.Collected, DateTime.Now, DateTime.Now, 7);
            Assert.IsTrue(book.Save());
            var tasks = new PhotoTaskBook(_dir);
            tasks.Complete(PhotoTaskCatalog.IndexOf(LifeKind.Flamingo), DateTime.Now, "task_x.png");
            Assert.IsTrue(tasks.Save());
            Touch(P(PhotoTaskBook.ThumbFolder, "task_x.png"));
            Touch(P(JournalReset.PhotoFolderName, "drift_20260924_101010.png"));
            Touch(P(JournalReset.PhotoFolderName, "drift_20260924_101010_thumb.jpg"));
            Touch(P(JournalReset.PhotoFolderName, "drift_20260924_111111.png"));
            RunJournal.Add(new RunRecord { seed = 42, name = "Testland" }, null);
            RunJournal.WaitForWrite();
            var j = new DiscoveryJournal();
            j.MarkCollected(LifeKind.Hare, 12f);
            Touch(P("drift_save.json"), JsonUtility.ToJson(new SaveGame { worldSeed = 777, journal = j.Capture() }));
            Touch(P(BestDistances.FileName), "{}");
            PlayerPrefs.SetString(LifeKey, "ff");
        }

        static int SaveJournalEntries(string path)
        {
            var data = JsonUtility.FromJson<SaveGame>(File.ReadAllText(path));
            return data.journal != null && data.journal.ids != null ? data.journal.ids.Length : 0;
        }

        JournalResetTargets Targets() => new JournalResetTargets { directory = _dir, lifeBookKey = LifeKey };

        [Test]
        public void SpeciesScope_ClearsAlbumAndTasksButKeepsPhotosAndChronicle()
        {
            FillStores();
            Assert.AreEqual(1, SaveJournalEntries(P("drift_save.json")));
            var cleared = JournalReset.Run(JournalResetScope.SpeciesAndTasks, Targets());

            Assert.IsFalse(File.Exists(P(JournalBook.FileName)));
            Assert.IsFalse(File.Exists(P(PhotoTaskBook.FileName)));
            Assert.AreEqual(0, Directory.GetFiles(P(PhotoTaskBook.ThumbFolder)).Length);
            Assert.IsFalse(PlayerPrefs.HasKey(LifeKey));
            Assert.AreEqual(3, Directory.GetFiles(P(JournalReset.PhotoFolderName)).Length, "the photo album stays");
            Assert.AreEqual(1, RunJournal.Records.Count, "the chronicle stays");
            Assert.IsTrue(File.Exists(P("drift_save.json")), "the run goes on");
            Assert.AreEqual(0, SaveJournalEntries(P("drift_save.json")), "its journal layer is empty");
            Assert.AreEqual(777, JsonUtility.FromJson<SaveGame>(File.ReadAllText(P("drift_save.json"))).worldSeed);
            Assert.IsTrue(File.Exists(P(BestDistances.FileName)));
            CollectionAssert.Contains(cleared, JournalBook.FileName);
            CollectionAssert.Contains(cleared, "PlayerPrefs " + LifeKey);
        }

        [Test]
        public void Everything_AlsoClearsPhotosAndChronicle()
        {
            FillStores();
            Touch(P(JournalReset.PhotoFolderName, "notes.txt"));
            JournalReset.Run(JournalResetScope.Everything, Targets());

            Assert.IsFalse(File.Exists(P(JournalBook.FileName)));
            Assert.IsFalse(File.Exists(P(PhotoTaskBook.FileName)));
            var left = Directory.GetFiles(P(JournalReset.PhotoFolderName));
            Assert.AreEqual(1, left.Length, "only the game's own photos go");
            Assert.AreEqual("notes.txt", Path.GetFileName(left[0]));
            Assert.AreEqual(0, RunJournal.Records.Count);
            Assert.IsFalse(File.Exists(RunJournal.FilePath));
            Assert.AreEqual(0, SaveJournalEntries(P("drift_save.json")));
            Assert.IsTrue(File.Exists(P(BestDistances.FileName)), "the best distance stays");
            RunJournal.Reload();
            Assert.AreEqual(0, RunJournal.Records.Count, "nothing comes back from disk");
        }

        [Test]
        public void AutoReset_ClearsEverythingDropsTheRunAndSetsItsFlag()
        {
            FillStores();
            PlayerPrefs.DeleteKey(FlagKey);
            var cleared = JournalReset.RunAuto(_dir, LifeKey, FlagKey);

            Assert.AreEqual(1, PlayerPrefs.GetInt(FlagKey, 0));
            Assert.IsFalse(File.Exists(P("drift_save.json")), "the run in progress is dropped");
            Assert.IsFalse(File.Exists(P(JournalBook.FileName)));
            Assert.IsFalse(File.Exists(P(PhotoTaskBook.FileName)));
            Assert.AreEqual(0, Directory.GetFiles(P(JournalReset.PhotoFolderName)).Length);
            Assert.AreEqual(0, RunJournal.Records.Count);
            Assert.IsFalse(PlayerPrefs.HasKey(LifeKey));
            Assert.IsTrue(File.Exists(P(BestDistances.FileName)));
            CollectionAssert.Contains(cleared, "drift_save.json");

            // A book that loads from the emptied folder is empty, and so is what the species pool reads.
            var book = new JournalBook(_dir);
            book.Load();
            Assert.AreEqual(0, book.SeenCount);
            Assert.AreEqual(0UL, book.SeenLifeMask);
            var tasks = new PhotoTaskBook(_dir);
            tasks.Load();
            Assert.AreEqual(0, tasks.DoneCount);
        }

        [Test]
        public void AutoReset_IsDueUntilItsFlagIsSet()
        {
            Assert.AreEqual("drift_reset_v065", JournalReset.AutoKey);
            PlayerPrefs.DeleteKey(FlagKey);
            Assert.AreEqual(0, PlayerPrefs.GetInt(FlagKey, 0));
            JournalReset.RunAuto(_dir, LifeKey, FlagKey, false);
            Assert.AreEqual(1, PlayerPrefs.GetInt(FlagKey, 0));
        }

        [Test]
        public void LiveBooks_AreEmptiedAndTheBookStaysAttached()
        {
            var book = new JournalBook(null);
            var j = new DiscoveryJournal();
            j.AttachBook(book);
            j.MarkCollected(LifeKind.Sheep, 3f);
            j.MarkSeen(LifeKind.Giraffe, 4f);
            book.Absorb(j);
            Assert.Greater(book.SeenCount, 0);
            var life = new LifeBook();
            life.Add(j.CollectedLifeMask);
            var tasks = new PhotoTaskBook(null);
            tasks.Complete(0, DateTime.Now);

            JournalReset.Run(JournalResetScope.SpeciesAndTasks, new JournalResetTargets
            {
                journal = j, book = book, lifeBook = life, tasks = tasks, lifeBookKey = null, runJournal = false,
            });

            Assert.AreEqual(0, j.SeenCount);
            Assert.AreEqual(0, j.CollectedCount);
            Assert.AreEqual(0, j.RunSeenCount);
            Assert.AreSame(book, j.Book, "the empty book stays attached");
            Assert.AreEqual(0, book.SeenCount);
            Assert.AreEqual(0UL, life.Mask);
            Assert.AreEqual(0, tasks.DoneCount);
            Assert.AreEqual(PhotoTaskCatalog.Mask, tasks.OpenMask);

            // What the run meets afterwards is new again, and flows into the empty book.
            Assert.IsTrue(j.MarkSeen(LifeKind.Giraffe, 9f));
            book.Absorb(j);
            Assert.AreEqual(1, book.SeenCount);
        }

        [Test]
        public void ClearSaveJournal_KeepsTheRestOfTheSave()
        {
            var j = new DiscoveryJournal();
            j.MarkCollected(LifeKind.Hare, 5f);
            j.AddYoungBorn(3);
            var save = new SaveGame { worldSeed = 1234, milestones = 5, speciesPool = 99, journal = j.Capture() };
            save.stats.islandsAbsorbed = 7;
            Touch(P("drift_save.json"), JsonUtility.ToJson(save));
            Assert.IsTrue(JournalReset.ClearSaveJournal(P("drift_save.json")));
            var back = JsonUtility.FromJson<SaveGame>(File.ReadAllText(P("drift_save.json")));
            Assert.AreEqual(1234, back.worldSeed);
            Assert.AreEqual(5, back.milestones);
            Assert.AreEqual(99, back.speciesPool);
            Assert.AreEqual(7, back.stats.islandsAbsorbed);
            var restored = new DiscoveryJournal();
            restored.Restore(back.journal);
            Assert.AreEqual(0, restored.SeenCount);
            Assert.AreEqual(0, restored.YoungBorn);
            Assert.IsFalse(JournalReset.ClearSaveJournal(P("missing.json")));
        }

        [Test]
        public void PhotoLibrary_UsesTheFolderTheResetEmpties()
        {
            Assert.AreEqual(JournalReset.PhotoPrefix, PhotoLibrary.Prefix);
            Assert.AreEqual(JournalReset.PhotoFolderName, Path.GetFileName(PhotoLibrary.DefaultDirectory));
            StringAssert.StartsWith(JournalReset.PhotoPrefix, PhotoLibrary.FileNameFor(DateTime.Now));
        }
    }
}
