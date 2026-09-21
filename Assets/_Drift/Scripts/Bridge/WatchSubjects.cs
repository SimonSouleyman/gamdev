using Drift.Core;
using Drift.Islands;
using Drift.Life;
using Drift.SaveSystem;
using Drift.Visuals;
using UnityEngine;

namespace Drift.Bridge
{
    // Where the camera looks while something is watched. False once the subject is gone for good.
    public delegate bool WatchFocus(out Vector3 focus);

    // One thing of a journal entry the camera can watch: a herd (WatchTools follows it with the herd chip), or any
    // other subject given as a focus function that is asked every frame, so it keeps up with a drifting island,
    // a swimming whale or a flock in the air.
    public sealed class WatchSubject
    {
        public string label;
        public float radius = 3f;
        // The island the subject lives on (camera ground clearance, the "behind the island" home pose); null at sea.
        public Island ground;
        public WatchFocus focus;
        public IslandHerdSystem herds;
        public int herd = -1;
    }

    // Finds the nearest live example of a journal entry around a point: herds, plants and critters on the loaded
    // islands (the player's own island first), flocks, fish schools, sea animals and boats in the water.
    // A new kind of journal entry needs a case here, or its cards cannot be watched.
    public static class WatchSubjects
    {
        public static WatchSubject Find(CollectEntry e, Island player, Vector2 near, FlockSystem flocks, FishSystem fish, SeaLifeSystem sea, ShipSystem ships)
        {
            switch (e.type)
            {
                case CollectType.Animal: return FindHerd(e, player, near);
                case CollectType.Plant: return FindPlant(e, player, near);
                case CollectType.Critter: return FindCritter(e, player, near);
                case CollectType.Bird:
                    return FindFlock(e, flocks, near) ?? (e.life == LifeKind.Seabird ? FindSea(e, SeaLifeSystem.Kind.Gulls, sea, near, 4f) : null);
                case CollectType.Boat: return FindShip(e, ships, near);
                default:
                    if (e.index == CollectionCatalog.FishIndex) return FindFish(e, fish, near, false);
                    if (e.hasSea && e.sea == SeaKind.BaitBall) return FindFish(e, fish, near, true);
                    return FindSeaAnimal(e, sea, near);
            }
        }

        // The player's island counts as distance 0, so what lives at home is shown first.
        static float Rank(Island island, Island player, Vector2 near) =>
            island == player ? -1f : (island.PlanarPosition - near).sqrMagnitude;

        static WatchSubject FindHerd(CollectEntry e, Island player, Vector2 near)
        {
            WatchSubject best = null;
            float bestRank = float.MaxValue;
            foreach (var island in Island.All)
            {
                if (island == null || !island.isActiveAndEnabled || island.IsSunk) continue;
                if (!island.TryGetComponent(out IslandHerdSystem herds) || !herds.enabled) continue;
                float rank = Rank(island, player, near);
                if (rank >= bestRank) continue;
                for (int h = 0; h < herds.HerdCount; h++)
                {
                    if (herds.HerdKind(h) != e.life || herds.HerdSize(h) == 0) continue;
                    best = new WatchSubject { label = e.name, ground = island, herds = herds, herd = h };
                    bestRank = rank;
                    break;
                }
            }
            return best;
        }

        static WatchSubject FindPlant(CollectEntry e, Island player, Vector2 near)
        {
            Island bestIsland = null;
            Vector2 bestLocal = default;
            bool bestTall = false;
            float bestRank = float.MaxValue;
            foreach (var island in Island.All)
            {
                if (island == null || !island.isActiveAndEnabled || island.IsSunk) continue;
                if (!island.TryGetComponent(out IslandLifeSystem life) || !life.enabled || !life.HasPlant(e.life)) continue;
                float rank = Rank(island, player, near);
                if (rank >= bestRank) continue;
                // The grown-up plant closest to the island's centre: a sapling at the shore makes a poor picture.
                int pick = -1;
                float pickScore = float.MaxValue;
                for (int i = 0; i < life.PlantCount; i++)
                {
                    if (life.PlantKindOf(i) != e.life || life.PlantDyingOf(i)) continue;
                    float score = life.PlantPositionOf(i).sqrMagnitude * (1.5f - Mathf.Clamp01(life.PlantMaturityOf(i)));
                    if (score >= pickScore) continue;
                    pickScore = score;
                    pick = i;
                }
                if (pick < 0) continue;
                bestIsland = island;
                bestLocal = life.PlantPositionOf(pick);
                bestTall = life.PlantIsTall(pick);
                bestRank = rank;
            }
            if (bestIsland == null) return null;
            var ground = bestIsland;
            var local = bestLocal;
            float lift = bestTall ? 1f : 0.3f;
            return new WatchSubject
            {
                label = e.name,
                ground = ground,
                radius = bestTall ? 2.5f : 1.6f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (ground == null || !ground.isActiveAndEnabled || ground.IsSunk) return false;
                    f = ground.transform.TransformPoint(local.x, Mathf.Max(0f, ground.SampleHeight(local)) + lift, local.y);
                    return true;
                },
            };
        }

        static WatchSubject FindCritter(CollectEntry e, Island player, Vector2 near)
        {
            Island bestIsland = null;
            IslandCrittersSystem bestSystem = null;
            int bestIndex = -1;
            float bestRank = float.MaxValue;
            foreach (var island in Island.All)
            {
                if (island == null || !island.isActiveAndEnabled || island.IsSunk) continue;
                if (!island.TryGetComponent(out IslandCrittersSystem critters) || !critters.enabled) continue;
                float rank = Rank(island, player, near);
                if (rank >= bestRank) continue;
                for (int i = 0; i < critters.CritterCount; i++)
                {
                    if (critters.KindOf(i) != e.life || critters.DyingOf(i) || critters.StateOf(i) == CritterState.Hidden) continue;
                    bestIsland = island;
                    bestSystem = critters;
                    bestIndex = i;
                    bestRank = rank;
                    break;
                }
            }
            if (bestIsland == null) return null;
            var ground = bestIsland;
            var system = bestSystem;
            int index = bestIndex;
            var kind = e.life;
            Vector2 last = system.PositionOf(index);
            float lift = kind == LifeKind.Butterfly || kind == LifeKind.Firefly ? 0.6f : 0.1f;
            return new WatchSubject
            {
                label = e.name,
                ground = ground,
                radius = 1.3f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (ground == null || system == null || !ground.isActiveAndEnabled || ground.IsSunk) return false;
                    // Indices shift when a critter leaves; the one of the same kind nearest to where it was takes over.
                    if (index >= system.CritterCount || system.KindOf(index) != kind || system.DyingOf(index))
                    {
                        index = -1;
                        float bestD = 25f;
                        for (int i = 0; i < system.CritterCount; i++)
                        {
                            if (system.KindOf(i) != kind || system.DyingOf(i)) continue;
                            float d = (system.PositionOf(i) - last).sqrMagnitude;
                            if (d >= bestD) continue;
                            bestD = d;
                            index = i;
                        }
                        if (index < 0) return false;
                    }
                    last = system.PositionOf(index);
                    f = ground.transform.TransformPoint(last.x, Mathf.Max(0f, ground.SampleHeight(last)) + lift, last.y);
                    return true;
                },
            };
        }

        static WatchSubject FindFlock(CollectEntry e, FlockSystem flocks, Vector2 near)
        {
            if (flocks == null || !flocks.isActiveAndEnabled) return null;
            bool seabird = e.life == LifeKind.Seabird;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < flocks.FlockCount; i++)
            {
                if (flocks.IsSeabird(i) != seabird || flocks.BirdCountOf(i) == 0) continue;
                float d = (flocks.PositionOf(i) - near).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return null;
            int index = best;
            return new WatchSubject
            {
                label = e.name,
                radius = 4f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (flocks == null || index >= flocks.FlockCount) return false;
                    Vector2 p = flocks.PositionOf(index);
                    f = new Vector3(p.x, flocks.HeightOf(index), p.y);
                    return true;
                },
            };
        }

        static WatchSubject FindFish(CollectEntry e, FishSystem fish, Vector2 near, bool baitBall)
        {
            if (fish == null || !fish.isActiveAndEnabled) return null;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < fish.SchoolSlots; i++)
            {
                if (!fish.SchoolActive(i) || fish.IsBaitBall(i) != baitBall) continue;
                float d = (fish.SchoolPosition(i) - near).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return null;
            int index = best;
            return new WatchSubject
            {
                label = e.name,
                radius = baitBall ? 4f : 3f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (fish == null || !fish.SchoolActive(index)) return false;
                    Vector2 p = fish.SchoolPosition(index);
                    f = new Vector3(p.x, -0.2f, p.y);
                    return true;
                },
            };
        }

        static WatchSubject FindSeaAnimal(CollectEntry e, SeaLifeSystem sea, Vector2 near)
        {
            if (!e.hasSea) return null;
            switch (e.sea)
            {
                case SeaKind.WhaleBull: return FindSea(e, SeaLifeSystem.Kind.Whale, sea, near, 9f);
                case SeaKind.WhaleCalf: return FindSea(e, SeaLifeSystem.Kind.WhalePod, sea, near, 9f, true);
                case SeaKind.FlyingFish: return null;
            }
            if (sea == null) return null;
            foreach (SeaLifeSystem.Kind kind in System.Enum.GetValues(typeof(SeaLifeSystem.Kind)))
            {
                if (kind == SeaLifeSystem.Kind.Seaweed || kind == SeaLifeSystem.Kind.Gulls || SeaLifeSystem.SeaKindOf(kind) != e.sea) continue;
                float radius = kind == SeaLifeSystem.Kind.WhalePod ? 9f : kind == SeaLifeSystem.Kind.Dolphins ? 5f : 3f;
                var found = FindSea(e, kind, sea, near, radius);
                if (found != null) return found;
            }
            return null;
        }

        static WatchSubject FindSea(CollectEntry e, SeaLifeSystem.Kind kind, SeaLifeSystem sea, Vector2 near, float radius, bool calf = false)
        {
            if (sea == null || !sea.isActiveAndEnabled) return null;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < sea.GroupSlots; i++)
            {
                if (!sea.GroupActive(i) || sea.GroupKind(i) != kind || (calf && !sea.GroupHasCalf(i))) continue;
                float d = (sea.GroupPosition(i) - near).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return null;
            int index = best;
            return new WatchSubject
            {
                label = e.name,
                radius = radius,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (sea == null || !sea.GroupActive(index) || sea.GroupKind(index) != kind) return false;
                    Vector2 p = sea.GroupPosition(index);
                    f = new Vector3(p.x, 0f, p.y);
                    return true;
                },
            };
        }

        static WatchSubject FindShip(CollectEntry e, ShipSystem ships, Vector2 near)
        {
            if (ships == null || !ships.isActiveAndEnabled || !e.hasSea) return null;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < ships.ShipSlots; i++)
            {
                if (!ships.ShipActive(i) || ShipSystem.SeaKindOf(ships.ShipKindOf(i)) != e.sea) continue;
                float d = (ships.ShipPosition(i) - near).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return null;
            int index = best;
            var kind = ships.ShipKindOf(best);
            return new WatchSubject
            {
                label = e.name,
                radius = 4f,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (ships == null || !ships.ShipActive(index) || ships.ShipKindOf(index) != kind) return false;
                    Vector2 p = ships.ShipPosition(index);
                    f = new Vector3(p.x, 0.4f, p.y);
                    return true;
                },
            };
        }
    }
}
