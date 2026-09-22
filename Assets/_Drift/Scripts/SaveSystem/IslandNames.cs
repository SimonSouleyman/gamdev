using System;
using System.Collections.Generic;

namespace Drift.SaveSystem
{
    // Names for a finished Pangäa ("Kaloheia", "Tobaraique", "Svalenholm"). Built from syllables that were taken
    // apart from real island names of five regions, so a generated name sounds like it could be on a map without
    // being one: every real name the parts came from is in Blacklist and an exact hit is rejected and re-rolled.
    // ForSeed is pure and deterministic - the same world seed always gets the same name, in every session and on
    // every platform (the little xorshift below, not System.Random, whose stream is a runtime detail).
    public static class IslandNames
    {
        public const int MinLength = 4;
        public const int MaxLength = 14;

        public enum Region { Nordic, Polynesian, Mediterranean, Caribbean, Gaelic }

        sealed class Parts
        {
            public string[] heads, mids, tails;
        }

        static readonly Parts[] Syllables =
        {
            // Nordic: Svalbard, Bornholm, Gotland, Lofoten, Senja, Hitra, Runde, Sotra, Karmøy, Askøy, Samsø, Læsø ...
            new Parts
            {
                heads = new[] { "Sval", "Born", "Got", "Lof", "Sen", "Hit", "Run", "Sot", "Karm", "Ask", "Sam", "Laes", "Vaer", "Trae", "Froy", "Smo", "Veg", "Lek", "Grim", "Stor", "Nord", "Vest", "Hald", "Sker" },
                mids = new[] { "a", "e", "i", "o", "u", "en", "el", "ar", "un", "or", "in", "al", "ing", "os", "er", "and" },
                tails = new[] { "oy", "holm", "vik", "sund", "nes", "dal", "mark", "land", "borg", "havn", "ey", "skar", "tind", "fjell", "hamn", "strand", "berg", "fors", "vaag", "torp" },
            },
            // Polynesian: Kauai, Maui, Molokai, Moorea, Rarotonga, Aitutaki, Tikehau, Rangiroa, Upolu, Savaii ...
            new Parts
            {
                heads = new[] { "Ka", "Ma", "Nu", "Ra", "Ta", "Va", "Mo", "Ha", "Le", "Ai", "Ru", "Pa", "Fa", "Ni", "To", "Mau", "Hi", "Lo", "Te", "Wai", "Mana", "Kai", "Pu", "Tu" },
                mids = new[] { "lo", "he", "va", "ku", "ra", "mo", "ti", "la", "nu", "pu", "hei", "ma", "ka", "na", "ri", "wa" },
                tails = new[] { "ia", "oa", "ea", "ai", "au", "iki", "ura", "ana", "eia", "ora", "una", "ola", "aki", "iti", "ahu", "anui", "iva", "eka", "oro", "atu" },
            },
            // Mediterranean: Corsica, Sardinia, Lampedusa, Pantelleria, Naxos, Andros, Milos, Serifos, Lipari ...
            new Parts
            {
                heads = new[] { "Cor", "Sar", "Lam", "Pant", "Mal", "Sal", "Ther", "Kyth", "Nax", "And", "Mil", "Ser", "Cap", "Lip", "Zan", "Tin", "Par", "Isch", "Elb", "Hyd", "Ski", "Kos", "Sam", "Rhod" },
                mids = new[] { "i", "e", "o", "an", "in", "el", "ar", "is", "on", "ol", "er", "at", "al", "en", "or", "it" },
                tails = new[] { "os", "a", "ia", "ina", "era", "oli", "ona", "ano", "isa", "esa", "ola", "ica", "eno", "ora", "anto", "ello", "anos", "iri", "ossa", "elia" },
            },
            // Caribbean: Tobago, Barbuda, Anguilla, Nevis, Martinique, Curaçao, Grenada, Antigua, Bequia ...
            new Parts
            {
                heads = new[] { "Tob", "Barb", "Ang", "Nev", "Mart", "Cur", "Gren", "Anti", "Mont", "Sab", "Dom", "Vieq", "Cul", "Ar", "Bon", "Jam", "Beq", "Carri", "Must", "Canou", "Pal", "Cay", "Mar", "Tort" },
                mids = new[] { "a", "i", "u", "ua", "ia", "er", "or", "an", "in", "al", "on", "en", "ar", "il", "os", "ur" },
                tails = new[] { "ago", "uda", "illa", "ique", "uba", "aire", "ada", "ola", "inas", "eque", "ica", "aica", "ura", "ana", "eira", "arte", "igo", "atia", "enca", "ibo" },
            },
            // Scottish / Irish: Inishmore, Arran, Kerrera, Benbecula, Colonsay, Rathlin, Islay, Iona, Staffa ...
            new Parts
            {
                heads = new[] { "Inish", "Arr", "Kerr", "Ben", "Dun", "Kil", "Bal", "Inver", "Glen", "Ard", "Col", "Jur", "Isl", "Ion", "Sky", "Mull", "Rath", "Tir", "Eig", "Barr", "Lew", "Harr", "Uis", "Staff" },
                mids = new[] { "a", "i", "o", "na", "mo", "ra", "an", "en", "ul", "il", "er", "ar", "ai", "ea", "ow", "in" },
                tails = new[] { "more", "beg", "ray", "say", "bay", "mull", "lish", "vore", "keen", "dara", "garry", "nish", "shee", "whin", "loch", "rick", "ross", "wick", "bost", "quay" },
            },
        };

        // Every real name a syllable was taken from. A generated name that lands on one of them exactly is thrown
        // away and rolled again - the names may sound like these, they may never be one of them.
        static readonly string[] RealNames =
        {
            "Svalbard", "Bornholm", "Gotland", "Oland", "Lofoten", "Senja", "Hitra", "Runde", "Sotra", "Karmoy",
            "Askoy", "Samso", "Laeso", "Vaeroy", "Traena", "Froya", "Smola", "Vega", "Leka", "Grimsey",
            "Storoya", "Nordkapp", "Vestvagoy", "Halden", "Skerray", "Hiiumaa", "Saaremaa", "Fano", "Mon", "Anholm",
            "Kauai", "Maui", "Molokai", "Lanai", "Oahu", "Niihau", "Moorea", "Bora", "Tahiti", "Raiatea",
            "Huahine", "Rarotonga", "Upolu", "Savaii", "Tongatapu", "Nukuhiva", "Aitutaki", "Mangaia", "Rurutu",
            "Tikehau", "Fakarava", "Rangiroa", "Manihi", "Takaroa", "Kaiteriteri", "Waiheke", "Manahiki", "Tuvalu",
            "Corsica", "Sardinia", "Lampedusa", "Pantelleria", "Malta", "Salina", "Thera", "Kythira", "Naxos",
            "Andros", "Milos", "Serifos", "Capri", "Lipari", "Zante", "Tinos", "Paros", "Ischia", "Elba", "Hydra",
            "Skiathos", "Kos", "Samos", "Rhodos", "Santorini", "Mykonos", "Lesbos", "Korcula",
            "Tobago", "Barbuda", "Anguilla", "Nevis", "Martinique", "Curacao", "Grenada", "Antigua", "Montserrat",
            "Saba", "Dominica", "Vieques", "Culebra", "Aruba", "Bonaire", "Jamaica", "Bequia", "Carriacou",
            "Mustique", "Canouan", "Palominos", "Caye", "Marie", "Tortola", "Barbados", "Cuba",
            "Inishmore", "Arran", "Kerrera", "Benbecula", "Dunvegan", "Killarney", "Ballycastle", "Inverness",
            "Glencoe", "Ardmore", "Colonsay", "Jura", "Islay", "Iona", "Skye", "Mull", "Rathlin", "Tiree", "Eigg",
            "Barra", "Lewis", "Harris", "Uist", "Staffa", "Rum", "Canna", "Gigha", "Achill", "Valentia",
        };

        static readonly HashSet<string> Blocked = new HashSet<string>(RealNames, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> Blacklist => RealNames;

        // Case-insensitive: one of the real names the syllables were taken from.
        public static bool IsRealName(string name) => !string.IsNullOrEmpty(name) && Blocked.Contains(name.Trim());

        // Letters only, one capital at the front, within the length range - what the UI can show in one line.
        public static bool IsPlausible(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < MinLength || name.Length > MaxLength) return false;
            if (name[0] < 'A' || name[0] > 'Z') return false;
            for (int i = 1; i < name.Length; i++)
                if (name[i] < 'a' || name[i] > 'z') return false;
            return true;
        }

        // The name of the Pangäa of that world seed.
        public static string ForSeed(int seed)
        {
            for (int variant = 0; variant < 16; variant++)
            {
                string name = Variant(seed, variant);
                if (!IsRealName(name) && IsPlausible(name)) return name;
            }
            // Unreachable in practice; still deterministic and never a real name.
            return Variant(seed, 0) + "ia";
        }

        public static Region RegionOf(int seed) => (Region)(new Rng(seed, 0).Range(Syllables.Length));

        // One roll of the syllable machine. variant > 0 is the re-roll after a blacklist hit.
        public static string Variant(int seed, int variant)
        {
            var rng = new Rng(seed, variant);
            var p = Syllables[rng.Range(Syllables.Length)];
            string head = p.heads[rng.Range(p.heads.Length)];
            string tail = p.tails[rng.Range(p.tails.Length)];
            // Two syllables in the middle most of the time: that is where the variety comes from, so that
            // thousands of worlds hardly ever get the same name twice.
            int mids = rng.Range(8) switch { 0 => 0, 1 => 1, 2 => 1, 3 => 1, _ => 2 };
            string a = mids > 0 ? p.mids[rng.Range(p.mids.Length)] : "";
            string b = mids > 1 ? p.mids[rng.Range(p.mids.Length)] : "";

            for (; mids >= 0; mids--)
            {
                string name = Assemble(head, mids > 0 ? a : "", mids > 1 ? b : "", tail);
                if (name.Length <= MaxLength) return Pad(name, p, ref rng);
            }
            return Pad(Assemble(head, "", "", tail), p, ref rng);
        }

        static string Pad(string name, Parts p, ref Rng rng)
        {
            // A very short roll ("Kaia") gets one more syllable rather than a name of three letters.
            for (int i = 0; name.Length < MinLength && i < 4; i++) name = Join(name, p.mids[rng.Range(p.mids.Length)]);
            return name.Length >= MinLength ? name : name + "ia";
        }

        static string Assemble(string head, string a, string b, string tail) => Join(Join(Join(head, a), b), tail);

        // Glues two syllables: a doubled letter at the seam collapses ("Ka"+"ai" -> "Kai") and no more than three
        // vowels ever meet, so "eia" stays possible but "aeia" cannot happen.
        static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            if (char.ToLowerInvariant(a[a.Length - 1]) == char.ToLowerInvariant(b[0])) b = b.Substring(1);
            int trailing = 0;
            for (int i = a.Length - 1; i >= 0 && IsVowel(a[i]); i--) trailing++;
            while (b.Length > 0 && IsVowel(b[0]) && trailing + LeadingVowels(b) > 3) b = b.Substring(1);
            return b.Length == 0 ? a : a + b;
        }

        static int LeadingVowels(string s)
        {
            int n = 0;
            while (n < s.Length && IsVowel(s[n])) n++;
            return n;
        }

        static bool IsVowel(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'a': case 'e': case 'i': case 'o': case 'u': case 'y': return true;
                default: return false;
            }
        }

        // xorshift32: a few bits of state, the same stream everywhere, unlike System.Random.
        struct Rng
        {
            uint _s;

            public Rng(int seed, int variant)
            {
                _s = (uint)seed * 2654435761u ^ (uint)(variant + 1) * 2246822519u ^ 0x9E3779B9u;
                if (_s == 0u) _s = 0x6D2B79F5u;
                for (int i = 0; i < 3; i++) Step();
            }

            uint Step()
            {
                _s ^= _s << 13;
                _s ^= _s >> 17;
                _s ^= _s << 5;
                return _s;
            }

            public int Range(int count) => count <= 1 ? 0 : (int)(Step() % (uint)count);
        }
    }
}
