using System.Collections.Generic;
using System.IO;
using Drift.Bridge;
using Drift.Core;
using Drift.Life;
using Drift.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace Drift.Tests
{
    // Photo tasks ("Fotoaufgaben"): one per species with a move of its own, judged from what a picture shows, kept
    // across runs in their own file, and announced as a toast.
    public class PhotoTaskTests
    {
        readonly List<GameObject> _objects = new();
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "drift_phototasks_test_" + System.Guid.NewGuid().ToString("N"));
            LifeLod.DistanceProvider = _ => 0f;
            LifeEnvironment.NightProvider = () => 0f;
            LifeEnvironment.TimeOfDayProvider = null;
            LifeEnvironment.PointOfInterest = null;
            LifeEnvironment.ViewDistanceProvider = null;
            LifeEnvironment.WindProvider = null;
            LifeEnvironment.StormProvider = null;
            IslandLifeSystem.ResetSeasonReference();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            LifeLod.DistanceProvider = null;
            LifeEnvironment.NightProvider = null;
            IslandLifeSystem.ResetSeasonReference();
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static int T(LifeKind k) => PhotoTaskCatalog.IndexOf(k);

        // ------------------------------------------------------------ catalog

        [Test]
        public void Catalog_OneTaskPerSpeciesWithAMoveOfItsOwn()
        {
            Assert.AreEqual(19, PhotoTaskCatalog.Count, "13 herd species, 4 critters, 2 birds");
            var moves = new HashSet<string>();
            for (int i = 0; i < PhotoTaskCatalog.Count; i++)
            {
                var t = PhotoTaskCatalog.At(i);
                Assert.AreEqual(i, t.index);
                Assert.AreEqual(i, T(t.kind));
                Assert.GreaterOrEqual(t.entry, 0, t.kind + " is not in the journal");
                Assert.AreEqual(t.kind, CollectionCatalog.At(t.entry).life);
                Assert.AreEqual(i, PhotoTaskCatalog.IndexOfEntry(t.entry));
                Assert.IsTrue(moves.Add(t.move), "move twice: " + t.move);
                Assert.IsTrue(t.phrase.StartsWith("beim ") || t.phrase.StartsWith("bei der "), t.phrase);
                Assert.IsTrue(t.phrase.EndsWith(t.move));
                Assert.IsFalse(string.IsNullOrEmpty(t.hint));
                Assert.IsTrue(t.hint.EndsWith("!"), t.hint);
                if (t.source == PhotoMoveSource.Herd) Assert.AreEqual(IslandHerdSystem.SignatureOf(t.kind), t.activity);
            }
            for (int i = 0; i < CollectionCatalog.Count; i++)
            {
                var e = CollectionCatalog.At(i);
                if (e.type == CollectType.Animal)
                    Assert.AreEqual(IslandHerdSystem.IsSignature(IslandHerdSystem.SignatureOf(e.life)), PhotoTaskCatalog.IndexOfEntry(i) >= 0, e.name);
                if (e.type == CollectType.SeaAnimal || e.type == CollectType.Boat || e.type == CollectType.Plant)
                    Assert.AreEqual(-1, PhotoTaskCatalog.IndexOfEntry(i), e.name + " has no move to photograph");
            }
            Assert.AreEqual("Flamingo beim Schlammtanz", PhotoTaskCatalog.At(T(LifeKind.Flamingo)).title);
            Assert.AreEqual("Polarfuchs bei der Schwanzjagd", PhotoTaskCatalog.At(T(LifeKind.ArcticFox)).title);
            Assert.AreEqual("Krabbe beim Winkertanz", PhotoTaskCatalog.At(T(LifeKind.Crab)).title);
            Assert.AreEqual("Die Flamingos tanzen im Schlamm!", PhotoTaskCatalog.At(T(LifeKind.Flamingo)).hint);
            Assert.AreEqual(CritterState.Spiral, PhotoTaskCatalog.At(T(LifeKind.Butterfly)).critterState);
            Assert.AreEqual(PhotoMoveSource.SeabirdDive, PhotoTaskCatalog.At(T(LifeKind.Seabird)).source);
            Assert.AreEqual(-1, T(LifeKind.Grass));
            Assert.AreEqual(19, DiscoveryJournal.BitCount(PhotoTaskCatalog.Mask));
        }

        // ------------------------------------------------------------ judging a picture

        // A portrait phone picture from the origin looking along +z.
        static PhotoView Portrait() => PhotoView.Make(Vector3.zero, Quaternion.identity, 60f, 1080, 1920);

        static PhotoSubject Fake(LifeKind kind, Vector3 world, float size) => new PhotoSubject { task = T(kind), world = world, size = size };

        [Test]
        public void Judge_CountsOnlyWhatIsInTheFrameAndLargeEnough()
        {
            var view = Portrait();
            var subjects = new List<PhotoSubject>
            {
                Fake(LifeKind.Flamingo, new Vector3(0.2f, 0f, 3f), 0.07f),   // close, in the middle
                Fake(LifeKind.Hare, new Vector3(0f, 0f, 40f), 0.045f),       // a speck far away
                Fake(LifeKind.Sheep, new Vector3(0f, 0f, -3f), 0.08f),       // behind the camera
                Fake(LifeKind.Goat, new Vector3(6f, 0f, 3f), 0.07f),         // beside the frame
                Fake(LifeKind.Ox, new Vector3(0.96f, 0f, 3f), 0.12f),        // cut by the frame edge
                new PhotoSubject { task = -1, world = new Vector3(0f, 0f, 3f), size = 1f },
            };
            var tasks = new List<int>();
            var centres = new List<Vector2>();
            int n = PhotoSubjects.Judge(view, subjects, 0.015f, 0.04f, tasks, centres);
            Assert.AreEqual(1, n);
            Assert.AreEqual(T(LifeKind.Flamingo), tasks[0]);
            Assert.Greater(centres[0].x, 0.5f);
            Assert.AreEqual(0.5f, centres[0].y, 1e-3f);

            // Zoomed in on the hare (a telephoto view) it counts as well.
            var tele = PhotoView.Make(Vector3.zero, Quaternion.identity, 3f, 1080, 1920);
            n = PhotoSubjects.Judge(tele, subjects, 0.015f, 0.04f, tasks, centres);
            Assert.IsTrue(tasks.Contains(T(LifeKind.Hare)));
        }

        [Test]
        public void Judge_SizeThresholdAndBestSubjectPerTask()
        {
            var view = Portrait();
            Assert.IsTrue(view.Project(new Vector3(0f, 0f, 2f), out var vp, out float depth));
            Assert.AreEqual(new Vector2(0.5f, 0.5f), vp);
            Assert.AreEqual(2f, depth, 1e-5f);
            // 0.1 wide at depth 2 with a 60° vertical field: 0.1 / (2 * 2 * tan 30°) of the 1920 px height = 83 px,
            // which is 7.7 % of the 1080 px short side.
            Assert.AreEqual(0.1f / (4f * Mathf.Tan(30f * Mathf.Deg2Rad)) * 1920f / 1080f, view.ScreenShare(0.1f, 2f), 1e-4f);

            var subjects = new List<PhotoSubject>
            {
                Fake(LifeKind.Sheep, new Vector3(-0.5f, 0f, 6f), 0.084f),
                Fake(LifeKind.Sheep, new Vector3(0.3f, 0.2f, 2.5f), 0.084f),
            };
            var tasks = new List<int>();
            var centres = new List<Vector2>();
            Assert.AreEqual(1, PhotoSubjects.Judge(view, subjects, 0.015f, 0.04f, tasks, centres), "two sheep, one task");
            Assert.Greater(centres[0].x, 0.5f, "the centre is the one that is larger in the picture");
            Assert.AreEqual(0, PhotoSubjects.Judge(view, subjects, 0.5f, 0.04f, tasks, centres), "nothing fills half the picture");
        }

        [Test]
        public void CropAround_IsASquareInsideThePicture()
        {
            var r = PhotoSubjects.CropAround(1920, 1080, new Vector2(0.5f, 0.5f), 0.5f);
            Assert.AreEqual(540f, r.width * 1920f, 0.01f);
            Assert.AreEqual(540f, r.height * 1080f, 0.01f);
            Assert.AreEqual(0.5f, r.center.x, 1e-4f);
            r = PhotoSubjects.CropAround(1080, 1920, new Vector2(0.98f, 0.02f), 0.5f);
            Assert.AreEqual(r.width * 1080f, r.height * 1920f, 0.01f);
            Assert.AreEqual(1f, r.xMax, 1e-4f);
            Assert.AreEqual(0f, r.yMin, 1e-4f);
        }

        // ------------------------------------------------------------ a real herd in its move

        [Test]
        public void LiveHerd_InItsSignatureMoveIsAPhotoSubject()
        {
            var go = new GameObject("PhotoTaskHares");
            go.SetActive(false);
            var surface = go.AddComponent<FakeIslandSurface>();
            surface.radius = 7f;
            surface.height = 1f;
            surface.biome = (int)LifeBiome.Temperate;
            go.AddComponent<IslandLifeSystem>().seed = 77;
            var herds = go.AddComponent<IslandHerdSystem>();
            herds.seed = 77;
            go.SetActive(true);
            _objects.Add(go);
            herds.ClearHerds();
            Assert.GreaterOrEqual(herds.AddHerd(LifeKind.Hare, Vector2.zero, 6), 0);
            herds.behaviourRate = 0f;
            for (int i = 0; i < 10; i++) herds.Step(0.05f);
            herds.behaviourRate = 1f;
            herds.strollRate = herds.visitRate = herds.spreadRate = herds.signatureRate = 0f;

            var subjects = new List<PhotoSubject>();
            PhotoSubjects.AddHerds(herds, PhotoTaskCatalog.Mask, subjects);
            Assert.AreEqual(0, subjects.Count, "grazing hares are no photo task");

            Assert.IsTrue(herds.StartChoreography(0, AnimalActivity.Zigzag));
            for (int i = 0; i < 2400 && subjects.Count == 0; i++)
            {
                herds.Step(0.05f);
                PhotoSubjects.AddHerds(herds, PhotoTaskCatalog.Mask, subjects);
            }
            Assert.Greater(subjects.Count, 0, "the hare never showed its move");
            var s = subjects[0];
            Assert.AreEqual(T(LifeKind.Hare), s.task);
            Assert.AreSame(herds, s.herds);
            Assert.Greater(s.size, 0.02f);

            // Masked out (task done), the hare is not looked at.
            var none = new List<PhotoSubject>();
            PhotoSubjects.AddHerds(herds, PhotoTaskCatalog.Mask & ~(1UL << (int)LifeKind.Hare), none);
            Assert.AreEqual(0, none.Count);

            // A close picture of it fulfils the task, one from far away does not.
            var tasks = new List<int>();
            var centres = new List<Vector2>();
            var close = PhotoView.Make(s.world + new Vector3(0f, 0.4f, -1.2f), Quaternion.LookRotation(new Vector3(0f, -0.4f, 1.2f)), 60f, 1920, 1080);
            Assert.AreEqual(1, PhotoSubjects.Judge(close, subjects, 0.015f, 0.04f, tasks, centres));
            var far = PhotoView.Make(s.world + new Vector3(0f, 12f, -30f), Quaternion.LookRotation(new Vector3(0f, -12f, 30f)), 60f, 1920, 1080);
            Assert.AreEqual(0, PhotoSubjects.Judge(far, subjects, 0.015f, 0.04f, tasks, centres));

            // The cue follows it while the move lasts and lets go afterwards.
            Assert.IsTrue(PhotoSubjects.Refresh(ref s));
            bool ended = false;
            for (int i = 0; i < 2400 && !ended; i++)
            {
                herds.Step(0.05f);
                ended = !PhotoSubjects.Refresh(ref s);
            }
            Assert.IsTrue(ended, "the cue never let go of the hare");
        }

        // ------------------------------------------------------------ the book across runs

        [Test]
        public void Book_CompletesOnceAndTracksTheOpenMask()
        {
            var book = new PhotoTaskBook(null);
            Assert.AreEqual(0, book.DoneCount);
            Assert.AreEqual(PhotoTaskCatalog.Mask, book.OpenMask);
            Assert.AreEqual("Fotoaufgaben 0/19", book.Line);
            var when = new System.DateTime(2026, 9, 22, 18, 30, 0);
            Assert.IsTrue(book.Complete(T(LifeKind.Flamingo), when));
            Assert.IsFalse(book.Complete(T(LifeKind.Flamingo), when.AddDays(1)), "done is done");
            Assert.IsFalse(book.Complete(-1, when));
            Assert.IsFalse(book.IsOpen(LifeKind.Flamingo));
            Assert.IsTrue(book.IsOpen(LifeKind.Hare));
            Assert.AreEqual(when, book.DoneAt(T(LifeKind.Flamingo)));
            Assert.AreEqual("22.09.2026", PhotoTaskBook.DateLabel(book.DoneAt(T(LifeKind.Flamingo))));
            Assert.AreEqual("Fotoaufgaben 1/19", book.Line);
            Assert.IsFalse(book.Save(), "a book without a folder never writes");
        }

        [Test]
        public void Book_RoundTripsThroughItsFile()
        {
            var book = new PhotoTaskBook(_dir);
            book.Load();
            Assert.AreEqual(0, book.DoneCount, "no file yet: an empty book");
            var when = new System.DateTime(2026, 9, 22, 18, 30, 0);
            book.Complete(T(LifeKind.Flamingo), when, PhotoTaskBook.ThumbFileName(T(LifeKind.Flamingo)));
            book.Complete(T(LifeKind.Seabird), when.AddMinutes(5));
            Assert.IsTrue(book.Save());
            Assert.IsTrue(File.Exists(Path.Combine(_dir, PhotoTaskBook.FileName)));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, PhotoTaskBook.FileName + ".tmp")));

            var again = new PhotoTaskBook(_dir);
            again.Load();
            Assert.AreEqual(2, again.DoneCount);
            Assert.IsTrue(again.IsDone(T(LifeKind.Flamingo)));
            Assert.IsTrue(again.IsDone(T(LifeKind.Seabird)));
            Assert.IsFalse(again.IsDone(T(LifeKind.Hare)));
            Assert.AreEqual(when, again.DoneAt(T(LifeKind.Flamingo)));
            Assert.AreEqual(when.AddMinutes(5), again.DoneAt(T(LifeKind.Seabird)));
            Assert.AreEqual(Path.Combine(_dir, PhotoTaskBook.ThumbFolder, "task_" + (int)LifeKind.Flamingo + ".png"), again.ThumbPath(T(LifeKind.Flamingo)));
            Assert.IsNull(again.ThumbPath(T(LifeKind.Seabird)));
            Assert.AreEqual(book.OpenMask, again.OpenMask);

            // Saving over an existing file works too.
            again.Complete(T(LifeKind.Hare), when);
            Assert.IsTrue(again.Save());
            var third = new PhotoTaskBook(_dir);
            third.Load();
            Assert.AreEqual(3, third.DoneCount);
        }

        [Test]
        public void Book_SurvivesBrokenAndForeignFiles()
        {
            Directory.CreateDirectory(_dir);
            string path = Path.Combine(_dir, PhotoTaskBook.FileName);
            File.WriteAllText(path, "{ not json");
            var book = new PhotoTaskBook(_dir);
            book.Load();
            Assert.AreEqual(0, book.DoneCount);

            // Unknown kinds (a plant, a number from a later version), bad dates and bad file names are skipped.
            File.WriteAllText(path, "{\"version\":1,\"ids\":[" + (int)LifeKind.Grass + ",999," + (int)LifeKind.Zebra + "," + (int)LifeKind.Crab
                + "],\"dates\":[\"x\",\"y\",\"not a date\",\"2026-09-20 07:05\"],\"thumbs\":[\"\",\"\",\"../evil.png\",\"task_9.png\"]}");
            book.Load();
            Assert.AreEqual(2, book.DoneCount);
            Assert.IsTrue(book.IsDone(T(LifeKind.Zebra)));
            Assert.AreEqual(System.DateTime.MinValue, book.DoneAt(T(LifeKind.Zebra)));
            Assert.IsNull(book.ThumbName(T(LifeKind.Zebra)));
            Assert.AreEqual(new System.DateTime(2026, 9, 20, 7, 5, 0), book.DoneAt(T(LifeKind.Crab)));
            Assert.AreEqual("task_9.png", book.ThumbName(T(LifeKind.Crab)));

            book.Restore(null);
            Assert.AreEqual(0, book.DoneCount);
        }

        // ------------------------------------------------------------ the species journal across runs

        static int E(LifeKind k) => CollectionCatalog.IndexOf(k);
        static ulong Bit(LifeKind k) => 1UL << (int)k;

        [Test]
        public void JournalBook_KeepsTheAlbumWhenANewRunStarts()
        {
            var day = new System.DateTime(2026, 9, 22, 18, 0, 0);
            DiscoveryJournal.Clock = () => day;
            try
            {
                var j = new DiscoveryJournal();
                j.AttachBook(new JournalBook(_dir));
                j.ReportPresence(Bit(LifeKind.Sheep), 0f);
                Assert.IsTrue(j.MarkSeen(E(LifeKind.Zebra), 30f));
                j.SetCount(LifeKind.Sheep, 9);
                Assert.AreEqual(2, j.NewThisRun);
                Assert.AreEqual(2, j.RunSeenCount);

                j.Reset();   // the save manager starts a new world
                Assert.AreEqual(CollectState.Collected, j.StateOf(E(LifeKind.Sheep)), "collected stays collected");
                Assert.AreEqual(CollectState.Seen, j.StateOf(E(LifeKind.Zebra)));
                Assert.AreEqual(2, j.SeenCount);
                Assert.AreEqual(1, j.CollectedCount);
                Assert.AreEqual(9, j.BestOf(E(LifeKind.Sheep)), "the record stays");
                Assert.AreEqual(0, j.RunSeenCount);
                Assert.AreEqual(0, j.NewThisRun);
                Assert.AreEqual(CollectState.Unknown, j.RunStateOf(E(LifeKind.Zebra)));
                Assert.AreEqual(0UL, j.UnseenLifeMask & Bit(LifeKind.Zebra), "hints know the zebra");
                Assert.AreNotEqual(0UL, j.RunUnseenLifeMask & Bit(LifeKind.Zebra), "the run scan still looks for it");
                Assert.IsTrue(j.SeenBefore(E(LifeKind.Zebra)));
                Assert.AreEqual(day, j.SeenOn(E(LifeKind.Zebra)));
                Assert.AreEqual("am 22.09.", JournalPanel.WhenOf(j, E(LifeKind.Zebra)));
                Assert.AreEqual(0, j.Capture().ids.Length, "the new run's save holds nothing yet");

                // Meeting a known kind again is no "first time" news but counts for the run.
                Assert.IsFalse(j.MarkSeen(E(LifeKind.Zebra), 5f));
                Assert.AreEqual(1, j.RunSeenCount);
                Assert.AreEqual(0, j.NewThisRun);
                Assert.AreEqual(0UL, j.MarkSeen(Bit(LifeKind.Zebra) | Bit(LifeKind.Giraffe), 6f) & Bit(LifeKind.Zebra));
                Assert.AreEqual(1, j.NewThisRun, "the giraffe is new");
                Assert.AreEqual("bei 0:06", JournalPanel.WhenOf(j, E(LifeKind.Giraffe)));
                // Sheep on the new island are news for this run.
                j.ReportPresence(0, 7f);
                Assert.AreEqual(Bit(LifeKind.Sheep), j.ReportPresence(Bit(LifeKind.Sheep), 8f));
                Assert.AreEqual(1, j.RunCollectedCount);
                Assert.AreEqual(1, j.CollectedCount);
                Assert.AreEqual(3, j.Capture().ids.Length);
            }
            finally
            {
                DiscoveryJournal.Clock = () => System.DateTime.Now;
            }
        }

        [Test]
        public void JournalBook_RoundTripsThroughItsFileIntoANewJournal()
        {
            var book = new JournalBook(_dir);
            book.Load();
            Assert.AreEqual(0, book.SeenCount);
            var j = new DiscoveryJournal();
            j.AttachBook(book);
            j.ReportPresence(Bit(LifeKind.Hare) | Bit(LifeKind.Grass), 0f);
            j.MarkSeen(SeaKind.Dolphin, 10f);
            j.SetCount(LifeKind.Hare, 12);
            Assert.IsTrue(book.Absorb(j));
            Assert.IsFalse(book.Absorb(j), "nothing new the second time");
            Assert.IsTrue(book.Save());
            Assert.IsTrue(File.Exists(Path.Combine(_dir, JournalBook.FileName)));

            var loaded = new JournalBook(_dir);
            loaded.Load();
            Assert.AreEqual(3, loaded.SeenCount);
            Assert.AreEqual(2, loaded.CollectedCount);
            Assert.AreEqual(12, loaded.BestOf(E(LifeKind.Hare)));
            Assert.AreEqual(CollectState.Seen, loaded.StateOf(CollectionCatalog.IndexOf(SeaKind.Dolphin)));

            var next = new DiscoveryJournal();
            next.AttachBook(loaded);
            Assert.AreEqual(3, next.SeenCount);
            Assert.AreEqual(CollectState.Collected, next.StateOf(E(LifeKind.Hare)));
            Assert.AreEqual(0, next.RunSeenCount);
            Assert.AreEqual(JournalPanel.RunLine(next), "Diese Reise: 0 Arten gesehen  ·  0 neu entdeckt");
        }

        [Test]
        public void JournalBook_OldSaveIsMergedOnceAndBrokenFilesAreEmpty()
        {
            // A run saved before the book existed: its block is restored first, the book is attached later.
            var old = new DiscoveryJournal();
            old.ReportPresence(Bit(LifeKind.Goat), 0f);
            old.MarkSeen(LifeKind.Penguin, 40f);
            var saved = JsonUtility.FromJson<JournalSaveData>(JsonUtility.ToJson(old.Capture()));

            var j = new DiscoveryJournal();
            j.Restore(saved);
            var book = new JournalBook(_dir);
            j.AttachBook(book);
            Assert.AreEqual(2, book.SeenCount, "the old save's discoveries went into the book");
            Assert.AreEqual(CollectState.Collected, book.StateOf(E(LifeKind.Goat)));
            // Loading it again (a second Restore) adds nothing to the book.
            int v = book.Version;
            j.Restore(saved);
            book.Absorb(j);
            Assert.AreEqual(v, book.Version);
            Assert.AreEqual(2, j.RunSeenCount, "the loaded run still knows what it met");
            Assert.IsTrue(j.SeenBefore(E(LifeKind.Penguin)), "the book already had it: the album shows its day");

            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, JournalBook.FileName), "{ broken");
            var broken = new JournalBook(_dir);
            broken.Load();
            Assert.AreEqual(0, broken.SeenCount);
            Assert.IsFalse(new JournalBook(null).Save());
        }

        // ------------------------------------------------------------ toast

        [Test]
        public void Toast_PhotoTaskComesFirstAndPointsAtTheSpecies()
        {
            var toasts = new CollectionToasts();
            toasts.Collected(CollectionCatalog.IndexOf(LifeKind.Sheep));
            toasts.PhotoTaskDone(T(LifeKind.Flamingo));
            toasts.PhotoTaskDone(T(LifeKind.Flamingo));
            Assert.AreEqual(2, toasts.Pending);
            Assert.IsTrue(toasts.Tick(0.1f));
            var t = toasts.Current;
            Assert.IsTrue(t.photo);
            Assert.IsTrue(t.strong);
            Assert.AreEqual("Fotoaufgabe erfüllt: Flamingo beim Schlammtanz!", t.text);
            Assert.AreEqual(CollectionCatalog.IndexOf(LifeKind.Flamingo), t.first);
            Assert.AreEqual(1, t.count);
            toasts.Dismiss();
            for (int i = 0; i < 20 && !toasts.Showing; i++) toasts.Tick(0.1f);
            Assert.IsFalse(toasts.Current.photo);
            Assert.AreEqual("Neu auf deiner Insel: Schaf!", toasts.Current.text);

            toasts.Clear();
            toasts.PhotoTaskDone(T(LifeKind.Hare));
            toasts.PhotoTaskDone(T(LifeKind.Crab));
            toasts.Tick(0.1f);
            Assert.AreEqual("2 Fotoaufgaben erfüllt!", toasts.Current.text);
        }
    }
}
