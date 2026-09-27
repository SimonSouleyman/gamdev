using System.Globalization;
using UnityEngine;

namespace Drift.SaveSystem
{
    // The "Lebensbuch": which kinds were ever collected, across all runs. One bit per LifeKind, kept as a hex
    // string in PlayerPrefs (there is no 64-bit PlayerPrefs type) and only written when a bit is new.
    public sealed class LifeBook
    {
        public const string PrefKey = "drift_lifebook";

        public ulong Mask { get; private set; }
        public int Count => DiscoveryJournal.BitCount(Mask & CollectionCatalog.CollectibleMask);
        public static int Total => CollectionCatalog.CollectibleCount;

        public bool Has(Drift.Life.LifeKind kind) => (Mask & (1UL << (int)kind)) != 0;

        // True when the run's collection held something the book did not know yet.
        public bool Add(ulong collectedMask)
        {
            ulong merged = Mask | (collectedMask & CollectionCatalog.CollectibleMask);
            if (merged == Mask) return false;
            Mask = merged;
            return true;
        }

        public void Clear() => Mask = 0;

        public static string Format(ulong mask) => mask.ToString("x", CultureInfo.InvariantCulture);

        public static bool TryParse(string text, out ulong mask)
        {
            mask = 0;
            return !string.IsNullOrEmpty(text) && ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask);
        }

        public void Load(string key = PrefKey)
        {
            Mask = TryParse(PlayerPrefs.GetString(key, ""), out ulong mask) ? mask & CollectionCatalog.CollectibleMask : 0;
        }

        public void Save(string key = PrefKey)
        {
            PlayerPrefs.SetString(key, Format(Mask));
            PlayerPrefs.Save();
        }

        public string Line => "Lebensbuch: " + Count + "/" + Total + " Arten auf allen Reisen gesammelt";
    }
}
