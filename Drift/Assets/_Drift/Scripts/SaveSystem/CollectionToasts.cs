namespace Drift.SaveSystem
{
    public struct CollectionToast
    {
        public string text;
        // A new species on the island; false for the quiet "seen for the first time" line.
        public bool strong;
        // Catalog index of the first entry the toast names and how many it covers (0 = not about an entry).
        public int first, count;
        // A photo task was done: first is then the task's catalog entry.
        public bool photo;
    }

    // Turns journal news into at most one toast at a time. News that arrives while a toast is up (or in the same
    // scan, as after a merge that brings five species) waits and is combined into one line.
    public sealed class CollectionToasts
    {
        const int Max = 24;

        public float strongSeconds = 4f;
        public float subtleSeconds = 2.6f;
        public float gapSeconds = 0.5f;

        readonly int[] _collected = new int[Max], _seen = new int[Max], _photos = new int[Max];
        int _collectedCount, _seenCount, _collectedTotal, _seenTotal, _photoCount, _photoTotal;
        float _timer, _gap;

        public CollectionToast Current { get; private set; }
        public bool Showing => !string.IsNullOrEmpty(Current.text);
        public int Pending => _collectedTotal + _seenTotal + _photoTotal;

        public void Collected(int index)
        {
            for (int i = 0; i < _collectedCount; i++) if (_collected[i] == index) return;
            // Seen and collected in one go is one piece of news.
            for (int i = 0; i < _seenCount; i++)
            {
                if (_seen[i] != index) continue;
                _seen[i] = _seen[--_seenCount];
                _seenTotal--;
                break;
            }
            if (_collectedCount < Max) _collected[_collectedCount++] = index;
            _collectedTotal++;
        }

        public void Seen(int index)
        {
            for (int i = 0; i < _seenCount; i++) if (_seen[i] == index) return;
            for (int i = 0; i < _collectedCount; i++) if (_collected[i] == index) return;
            if (_seenCount < Max) _seen[_seenCount++] = index;
            _seenTotal++;
        }

        // A photo task (PhotoTaskCatalog index) was done. Comes before the rest of the queue.
        public void PhotoTaskDone(int task)
        {
            for (int i = 0; i < _photoCount; i++) if (_photos[i] == task) return;
            if (_photoCount < Max) _photos[_photoCount++] = task;
            _photoTotal++;
        }

        // Takes the toast on screen away now (it was tapped); what is queued still follows after the gap.
        public void Dismiss()
        {
            if (!Showing) return;
            Current = default;
            _timer = 0f;
            _gap = gapSeconds;
        }

        public void Clear()
        {
            _collectedCount = _seenCount = _collectedTotal = _seenTotal = _photoCount = _photoTotal = 0;
            _timer = _gap = 0f;
            Current = default;
        }

        // True when Current changed: a toast came up or the last one went away.
        public bool Tick(float dt)
        {
            if (Showing)
            {
                _timer -= dt;
                if (_timer > 0f) return false;
                Current = default;
                _gap = gapSeconds;
                return true;
            }
            if (_gap > 0f)
            {
                _gap -= dt;
                return false;
            }
            if (_photoTotal > 0)
            {
                Current = new CollectionToast
                {
                    text = PhotoTaskCatalog.DoneText(_photos, _photoCount, _photoTotal), strong = true, photo = true,
                    first = _photoCount > 0 ? PhotoTaskCatalog.At(_photos[0]).entry : 0, count = _photoCount > 0 ? _photoTotal : 0,
                };
                _photoCount = _photoTotal = 0;
                _timer = strongSeconds;
                return true;
            }
            if (_collectedTotal > 0)
            {
                Current = new CollectionToast
                {
                    text = CollectedText(_collected, _collectedCount, _collectedTotal), strong = true,
                    first = _collectedCount > 0 ? _collected[0] : 0, count = _collectedCount > 0 ? _collectedTotal : 0,
                };
                _collectedCount = _collectedTotal = 0;
                _timer = strongSeconds;
                return true;
            }
            if (_seenTotal > 0)
            {
                Current = new CollectionToast
                {
                    text = SeenText(_seen, _seenCount, _seenTotal), strong = false,
                    first = _seenCount > 0 ? _seen[0] : 0, count = _seenCount > 0 ? _seenTotal : 0,
                };
                _seenCount = _seenTotal = 0;
                _timer = subtleSeconds;
                return true;
            }
            return false;
        }

        public static string CollectedText(int[] indices, int listed, int total)
        {
            if (total == 1 && listed >= 1) return "Neu auf deiner Insel: " + CollectionCatalog.At(indices[0]).name + "!";
            if (total == 2 && listed >= 2) return "Neu auf deiner Insel: " + CollectionCatalog.At(indices[0]).name + " und " + CollectionCatalog.At(indices[1]).name + "!";
            return total + " neue Arten gesammelt!";
        }

        public static string SeenText(int[] indices, int listed, int total)
        {
            if (total == 1 && listed >= 1) return "Zum ersten Mal gesehen: " + CollectionCatalog.At(indices[0]).name;
            return total + " neue Arten gesehen";
        }
    }
}
