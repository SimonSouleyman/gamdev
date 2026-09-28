using Drift.Core;

namespace Drift.Life
{
    // German display names for every LifeKind and biome (journal, tap popup). Indexed by the enum value, so new
    // kinds are only ever appended.
    public static class LifeNames
    {
        static readonly string[] Singular =
        {
            "Gras", "Wiesenblume", "Busch", "Baum", "Hase", "Schaf", "Ziege", "Ochse", "Vogel", "Krabbe", "Schildkröte",
            "Schmetterling", "Glühwürmchen", "Seevogel", "Palme", "Schilf",
            "Wasserschwein", "Flamingo", "Riesenschildkröte", "Rentier", "Pinguin", "Polarfuchs", "Zebra", "Giraffe", "Erdmännchen",
            "Farn", "Hibiskus", "Bananenstaude", "Bambus", "Urwaldbaum",
            "Rentierflechte", "Fliegenpilz", "Heidekraut", "Wacholder", "Fichte", "Birke",
            "Savannengras", "Aloe", "Dornbusch", "Termitenhügel", "Schirmakazie", "Affenbrotbaum"
        };

        static readonly string[] PluralNames =
        {
            "Gräser", "Wiesenblumen", "Büsche", "Bäume", "Hasen", "Schafe", "Ziegen", "Ochsen", "Vögel", "Krabben", "Schildkröten",
            "Schmetterlinge", "Glühwürmchen", "Seevögel", "Palmen", "Schilf",
            "Wasserschweine", "Flamingos", "Riesenschildkröten", "Rentiere", "Pinguine", "Polarfüchse", "Zebras", "Giraffen", "Erdmännchen",
            "Farne", "Hibiskus", "Bananenstauden", "Bambus", "Urwaldbäume",
            "Rentierflechten", "Fliegenpilze", "Heidekraut", "Wacholder", "Fichten", "Birken",
            "Savannengras", "Aloen", "Dornbüsche", "Termitenhügel", "Schirmakazien", "Affenbrotbäume"
        };

        static readonly string[] Biomes = { "Gemäßigt", "Tropisch", "Nordisch", "Savanne" };

        public static int KindCount => Singular.Length;

        public static string Of(LifeKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < Singular.Length ? Singular[i] : kind.ToString();
        }

        public static string Plural(LifeKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < PluralNames.Length ? PluralNames[i] : kind.ToString();
        }

        public static string OfBiome(LifeBiome biome) => OfBiome((int)biome);

        public static string OfBiome(int biome) => biome >= 0 && biome < Biomes.Length ? Biomes[biome] : Biomes[0];
    }
}
