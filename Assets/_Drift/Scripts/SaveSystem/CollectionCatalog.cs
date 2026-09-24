using System.Collections.Generic;
using Drift.Core;
using Drift.Life;

namespace Drift.SaveSystem
{
    public enum CollectState { Unknown, Seen, Collected }
    public enum CollectSection { Temperate, Tropical, Nordic, Savanna, SeaSky }
    public enum CollectType { Animal, Plant, Critter, Bird, SeaAnimal, Boat }

    public sealed class CollectEntry
    {
        // Position in CollectionCatalog.All (changes when the catalog grows) and the number a save stores (never changes).
        public int index, id;
        public CollectSection section;
        public CollectType type;
        public bool hasLife, hasSea;
        public LifeKind life;
        public SeaKind sea;
        public string name;
        // Only what can live on the player's island can be collected; sea and sky are watched.
        public bool collectible;
    }

    // Everything the album knows: the herd animals and plants of the four biomes (each under the first biome
    // that brings it forth, so palm and reed count once), the critters, birds, sea animals (the seal among them) and boats.
    // Save ids: (int)LifeKind for island life, SeaIdBase + (int)SeaKind for the sea, FishId for the fish schools.
    public static class CollectionCatalog
    {
        public const int SeaIdBase = 100;
        public const int FishId = 200;
        public const int SectionCount = 5;

        static readonly CollectEntry[] Entries;
        static readonly int[] LifeIndex = new int[64];
        static readonly int[] SeaIndex;
        static readonly int[][] BySection;
        static readonly int[] CollectiblePerSection = new int[SectionCount];
        static readonly Dictionary<int, int> ById = new Dictionary<int, int>();

        static readonly string[] SectionNames = { "Gemäßigt", "Tropisch", "Nordisch", "Savanne", "Meer & Himmel" };
        static readonly string[] SectionIslands = { "gemäßigten Inseln", "tropischen Inseln", "nordischen Inseln", "Savanneninseln", "" };

        public static int Count => Entries.Length;
        public static int CollectibleCount { get; private set; }
        public static int FishIndex { get; private set; }
        // Bit (int)LifeKind of every collectible kind: what IslandHerdSystem.SpeciesPresent and
        // IslandLifeSystem.PlantsPresent can report, plus the four critters.
        public static ulong CollectibleMask { get; private set; }
        // Bit (int)LifeKind of every life kind with an entry (collectibles and the two birds).
        public static ulong LifeMask { get; private set; }
        public static ulong AnimalMask { get; private set; }
        public static ulong PlantMask { get; private set; }
        public static ulong CritterMask { get; private set; }

        static CollectionCatalog()
        {
            var list = new List<CollectEntry>();
            for (int i = 0; i < LifeIndex.Length; i++) LifeIndex[i] = -1;

            for (int b = 0; b < Biomes.Count; b++)
            {
                var biome = (LifeBiome)b;
                var animals = Biomes.Animals(biome);
                foreach (var kind in Biomes.Collectibles(biome))
                {
                    if (!Biomes.TryHomeOf(kind, out var home) || home != biome || LifeIndex[(int)kind] >= 0) continue;
                    AddLife(list, (CollectSection)b, System.Array.IndexOf(animals, kind) >= 0 ? CollectType.Animal : CollectType.Plant, kind, true);
                }
            }
            AddLife(list, CollectSection.SeaSky, CollectType.Critter, LifeKind.Crab, true);
            AddLife(list, CollectSection.SeaSky, CollectType.Critter, LifeKind.Turtle, true);
            AddLife(list, CollectSection.SeaSky, CollectType.Critter, LifeKind.Butterfly, true);
            AddLife(list, CollectSection.SeaSky, CollectType.Critter, LifeKind.Firefly, true);
            AddLife(list, CollectSection.SeaSky, CollectType.Bird, LifeKind.Bird, false);
            AddLife(list, CollectSection.SeaSky, CollectType.Bird, LifeKind.Seabird, false);

            int seaKinds = System.Enum.GetValues(typeof(SeaKind)).Length;
            SeaIndex = new int[seaKinds];
            for (int i = 0; i < seaKinds; i++) SeaIndex[i] = -1;
            FishIndex = list.Count;
            list.Add(new CollectEntry { id = FishId, section = CollectSection.SeaSky, type = CollectType.SeaAnimal, name = "Fisch" });
            foreach (var k in new[] { SeaKind.BaitBall, SeaKind.FlyingFish, SeaKind.Dolphin, SeaKind.SeaTurtle, SeaKind.Seal, SeaKind.Jellyfish, SeaKind.Ray, SeaKind.Whale, SeaKind.WhaleCalf, SeaKind.WhaleBull })
                AddSea(list, CollectType.SeaAnimal, k);
            foreach (var k in new[] { SeaKind.RowBoat, SeaKind.SailBoat, SeaKind.FishingBoat, SeaKind.TradingCog })
                AddSea(list, CollectType.Boat, k);

            Entries = list.ToArray();
            var per = new List<int>[SectionCount];
            for (int s = 0; s < SectionCount; s++) per[s] = new List<int>();
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                e.index = i;
                ById[e.id] = i;
                per[(int)e.section].Add(i);
                if (e.hasLife) LifeMask |= 1UL << (int)e.life;
                if (e.type == CollectType.Animal) AnimalMask |= 1UL << (int)e.life;
                else if (e.type == CollectType.Plant) PlantMask |= 1UL << (int)e.life;
                else if (e.type == CollectType.Critter) CritterMask |= 1UL << (int)e.life;
                if (!e.collectible) continue;
                CollectibleCount++;
                CollectiblePerSection[(int)e.section]++;
                CollectibleMask |= 1UL << (int)e.life;
            }
            BySection = new int[SectionCount][];
            for (int s = 0; s < SectionCount; s++) BySection[s] = per[s].ToArray();
        }

        static void AddLife(List<CollectEntry> list, CollectSection section, CollectType type, LifeKind kind, bool collectible)
        {
            LifeIndex[(int)kind] = list.Count;
            list.Add(new CollectEntry { id = (int)kind, section = section, type = type, hasLife = true, life = kind, name = LifeNames.Of(kind), collectible = collectible });
        }

        static void AddSea(List<CollectEntry> list, CollectType type, SeaKind kind)
        {
            SeaIndex[(int)kind] = list.Count;
            list.Add(new CollectEntry { id = SeaIdBase + (int)kind, section = CollectSection.SeaSky, type = type, hasSea = true, sea = kind, name = SeaNames.German(kind) });
        }

        public static CollectEntry At(int index) => Entries[index];

        // -1 for kinds without an entry.
        public static int IndexOf(LifeKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < LifeIndex.Length ? LifeIndex[i] : -1;
        }

        public static int IndexOf(SeaKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < SeaIndex.Length ? SeaIndex[i] : -1;
        }

        public static int IndexOfId(int id) => ById.TryGetValue(id, out int i) ? i : -1;

        // Shared arrays; do not modify them.
        public static int[] EntriesOf(CollectSection section) => BySection[(int)section];
        public static int CountIn(CollectSection section) => BySection[(int)section].Length;
        public static int CollectibleIn(CollectSection section) => CollectiblePerSection[(int)section];
        public static string NameOf(CollectSection section) => SectionNames[(int)section];

        // Where an entry nobody has seen yet is to be found ("lebt auf tropischen Inseln").
        public static string HintOf(CollectEntry e)
        {
            switch (e.type)
            {
                case CollectType.Animal: return "lebt auf " + SectionIslands[(int)e.section];
                case CollectType.Plant: return "wächst auf " + SectionIslands[(int)e.section];
                case CollectType.Critter:
                    return e.life == LifeKind.Crab || e.life == LifeKind.Turtle ? "lebt am Strand"
                        : e.life == LifeKind.Butterfly ? "fliegt über Blumen" : "leuchtet in der Nacht";
                case CollectType.Bird: return "zieht über den Himmel";
                case CollectType.Boat: return "fährt über das Meer";
                default: return e.hasSea && e.sea == SeaKind.Seal ? "taucht an Inselküsten auf" : "schwimmt im offenen Meer";
            }
        }

        // Herd animals and critters come in rarity tiers (SpeciesPool): a rare one turns up only in some runs.
        public static bool IsRare(CollectEntry e) => e.hasLife && SpeciesPool.IsPooled(e.life) && SpeciesPool.RarityOf(e.life) == Rarity.Rare;

        public static string RarityLabel(CollectEntry e) => e.hasLife && SpeciesPool.IsPooled(e.life) ? SpeciesPool.LabelOf(SpeciesPool.RarityOf(e.life)) : "";

        // "von tropischen Inseln" for a kind that belongs to another biome; "" for kinds without a home biome.
        public static string OriginOf(LifeKind kind) =>
            Biomes.TryHomeOf(kind, out var home) ? "von " + SectionIslands[(int)home] : "";
    }
}
