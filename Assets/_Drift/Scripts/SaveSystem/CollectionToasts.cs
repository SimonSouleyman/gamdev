namespace Drift.SaveSystem
{
    public struct CollectionToast
    {
        public string text;
        // A new species on the island; false for the quiet "seen for the first time" line.
        public bool strong;
    }

    // Turns journal news into at most one toast at a time. News that arrives while a toast is up (or in the same
    // scan, as after a merge that brings five species) waits and is combined into one line.
    public sealed class CollectionToasts
    {
        const int Max = 24;

        public float strongSeconds = 4f;
        public float subtleSeconds = 2.6f;
        public float gapSeconds = 0.5f;

        readonly int[] _collected = new int[Max], _seen = new int[Max];
        int _collectedCount, _seenCount, _collectedTotal, _seenTotal;
        float _timer, _gap;

        public CollectionToast Current { get; private set; }
        public bool Showing => !string.IsNullOrEmpty(Current.text);
        public int Pending => _collectedTotal + _seenTotal;

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

        public void Clear()
        {
            _collectedCount = _seenCount = _collectedTotal = _seenTotal = 0;
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
            if (_collectedTotal > 0)
            {
                Current = new CollectionToast { text = CollectedText(_collected, _collectedCount, _collectedTotal), strong = true };
                _collectedCount = _collectedTotal = 0;
                _timer = strongSeconds;
                return true;
            }
            if (_seenTotal > 0)
            {
                Current = new CollectionToast { text = SeenText(_seen, _seenCount, _seenTotal), strong = false };
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
