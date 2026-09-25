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

    // One thing the camera can watch. There is exactly one watch mode ("Tier beobachten"): whatever the player
    // tapped — an animal, a discovery toast, a journal card, a photo-task cue — becomes one of these and goes
    // through WatchTools.BeginWatch. A herd carries its system and index (herd chip, popup, herd framing); anything
    // else brings a focus function that is asked every frame, so it keeps up with a drifting island, a swimming
    // whale or a flock in the air.
    public sealed class WatchSubject
    {
        public string label;
        public float radius = 3f;
        // The island the subject lives on (camera ground clearance, the "behind the island" home pose); null at sea.
        public Island ground;
        public WatchFocus focus;
        public IslandHerdSystem herds;
        public int herd = -1;
        // The critter the watch popup names, when the subject is one (the kind comes from the system).
        public IslandCrittersSystem critters;
        public int critter = -1;
        // Something that stands still (a lighthouse, the harbour, the festival ground): WatchTools frames it from a
        // lower angle, circles it slowly while the player does not touch the camera and rings its base for a while.
        public bool still;
        // How far above the focus (its base on the ground) the camera aims - half a lighthouse, not its doorstep.
        public float lift;
        // The camera's pitch for a still subject in degrees; 0 = WatchTools.stillPitch (a tower from the side). A
        // village is looked at from higher up so the houses do not hide each other and the fire.
        public float pitch;
        // Share of half the short picture side the still subject fills at the start; 0 = WatchTools.stillFill. A
        // village is framed closer than a tower, whose surroundings are the point.
        public float fill;
        // The notice when it is gone; null = "<label> ist weitergezogen".
        public string gone;
        // Something in the air (a flock): the camera's pivot rides at the subject's own height instead of on the ground
        // under it, and the framing distance and pitch follow `radius` (kept up to date by the focus) and the height.
        public bool air;
    }

    // Finds the nearest live example of a journal entry around a point: herds, plants and critters on the loaded
    // islands (the player's own island first), flocks, fish schools, sea animals and boats in the water.
    // A new kind of journal entry needs a case here, or its cards cannot be watched.
    public static class WatchSubjects
    {
        // ---------------------------------------------------------------- the four entry points build one of these

        public static WatchSubject OfHerd(Island island, IslandHerdSystem herds, int herd)
        {
            if (island == null || herds == null || herd < 0 || herd >= herds.HerdCount) return null;
            return new WatchSubject { label = LifeNames.Of(herds.HerdKind(herd)), ground = island, herds = herds, herd = herd };
        }

        // A photo-task cue: the very creature that is showing its move right now.
        public static WatchSubject OfCue(PhotoSubject cue)
        {
            if (cue.task < 0) return null;
            if (cue.herds != null)
            {
                cue.herds.TryGetComponent(out Island island);
                return OfHerd(island, cue.herds, cue.a);
            }
            var entry = CollectionCatalog.At(PhotoTaskCatalog.At(cue.task).entry);
            if (cue.critters != null && cue.a >= 0 && cue.island != null)
                return OfCritter(entry, cue.island, cue.critters, cue.a);
            if (cue.flocks != null) return OfFlock(entry, cue.flocks, cue.a);
            // The firefly wave: watched while it runs.
            var s = cue;
            return new WatchSubject
            {
                label = entry.name,
                ground = cue.island,
                radius = Mathf.Max(1.5f, cue.size),
                focus = (out Vector3 f) =>
                {
                    bool ok = PhotoSubjects.Refresh(ref s);
                    f = s.world;
                    return ok;
                },
            };
        }

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
            return OfCritter(e, bestIsland, bestSystem, bestIndex);
        }

        public static WatchSubject OfCritter(CollectEntry e, Island island, IslandCrittersSystem critters, int index)
        {
            if (island == null || critters == null || index < 0 || index >= critters.CritterCount) return null;
            var ground = island;
            var system = critters;
            int live = index;
            var kind = system.KindOf(index);
            Vector2 last = system.PositionOf(index);
            float lift = kind == LifeKind.Butterfly || kind == LifeKind.Firefly ? 0.6f : 0.1f;
            return new WatchSubject
            {
                label = e.name,
                ground = ground,
                radius = 1.3f,
                critters = system,
                critter = index,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (ground == null || system == null || !ground.isActiveAndEnabled || ground.IsSunk) return false;
                    // Indices shift when a critter leaves; the one of the same kind nearest to where it was takes over.
                    if (live >= system.CritterCount || system.KindOf(live) != kind || system.DyingOf(live))
                    {
                        live = -1;
                        float bestD = 25f;
                        for (int i = 0; i < system.CritterCount; i++)
                        {
                            if (system.KindOf(i) != kind || system.DyingOf(i)) continue;
                            float d = (system.PositionOf(i) - last).sqrMagnitude;
                            if (d >= bestD) continue;
                            bestD = d;
                            live = i;
                        }
                        if (live < 0) return false;
                    }
                    last = system.PositionOf(live);
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
            return OfFlock(e, flocks, best);
        }

        public static WatchSubject OfFlock(CollectEntry e, FlockSystem flocks, int index)
        {
            if (flocks == null || index < 0 || index >= flocks.FlockCount) return null;
            var system = flocks;
            int slot = index;
            var subject = new WatchSubject { label = e.name, radius = 4f, air = true };
            // The birds are drawn round the flock's steering point (a pulk, the seabird chain on its circle, a landed
            // group on the ground, a diver under water): the camera aims at their middle. The steering point moves
            // smoothly, the middle can jump (a bird dives, the chain forms), so only the offset between them is eased.
            Vector3 offset = default;
            float spread = 2f;
            int frame = -1;
            subject.focus = (out Vector3 f) =>
            {
                f = default;
                if (system == null || !system.isActiveAndEnabled || slot >= system.FlockCount || system.BirdCountOf(slot) == 0) return false;
                Vector3 anchor = system.AnchorOf(slot);
                int now = Time.frameCount;
                if (now != frame)
                {
                    bool first = frame < 0;
                    frame = now;
                    Vector3 want = offset;
                    float wantSpread = spread;
                    if (system.TryGetView(slot, out Vector3 middle, out float s))
                    {
                        want = middle - anchor;
                        wantSpread = s;
                    }
                    float k = first ? 1f : 1f - Mathf.Exp(-FlockFocusEase * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
                    offset = Vector3.Lerp(offset, want, k);
                    spread = Mathf.Lerp(spread, wantSpread, k);
                    subject.radius = Mathf.Max(FlockMinRadius, spread + FlockRadiusMargin);
                }
                f = anchor + offset;
                return true;
            };
            return subject;
        }

        // How fast (per second) the watched point follows a jump of the birds' middle relative to the flock; the framing
        // radius is the farthest bird plus a margin (WatchTools eases it once more).
        const float FlockFocusEase = 2.5f;
        const float FlockRadiusMargin = 0.8f;
        const float FlockMinRadius = 1.5f;

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
                case SeaKind.Seal: return FindSeal(e, sea, near);
                // The bursts last two seconds: there is nothing left to follow by the time the camera arrives.
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
            return best < 0 ? null : OfSeaGroup(e, sea, best, radius);
        }

        public static WatchSubject OfSeaGroup(CollectEntry e, SeaLifeSystem sea, int slot, float radius)
        {
            if (sea == null || slot < 0 || slot >= sea.GroupSlots || !sea.GroupActive(slot)) return null;
            int index = slot;
            var kind = sea.GroupKind(slot);
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

        // A sea group tapped in the water: its journal entry (a lone whale is the Walbulle) and framing radius.
        public static WatchSubject OfSeaGroup(SeaLifeSystem sea, int slot)
        {
            if (sea == null || slot < 0 || slot >= sea.GroupSlots || !sea.GroupActive(slot)) return null;
            var kind = sea.GroupKind(slot);
            var seaKind = kind == SeaLifeSystem.Kind.Whale ? SeaKind.WhaleBull : SeaLifeSystem.SeaKindOf(kind);
            int entry = CollectionCatalog.IndexOf(seaKind);
            if (entry < 0) return null;
            float radius = kind == SeaLifeSystem.Kind.WhalePod || kind == SeaLifeSystem.Kind.Whale ? 9f : kind == SeaLifeSystem.Kind.Dolphins ? 5f : 3f;
            return OfSeaGroup(CollectionCatalog.At(entry), sea, slot, radius);
        }

        // A flock tapped in the air.
        public static WatchSubject OfFlock(FlockSystem flocks, int index)
        {
            if (flocks == null || index < 0 || index >= flocks.FlockCount) return null;
            int entry = CollectionCatalog.IndexOf(flocks.IsSeabird(index) ? LifeKind.Seabird : LifeKind.Bird);
            return entry < 0 ? null : OfFlock(CollectionCatalog.At(entry), flocks, index);
        }

        // ---------------------------------------------------------------- landmarks and milestones

        public static string LandmarkName(BuildingKind kind) => kind == BuildingKind.Lighthouse ? "Leuchtturm" : kind == BuildingKind.Dock ? "Hafen" : "";

        // The buildings a tap (and the sparkle) can pick out of a settlement.
        public static bool IsLandmark(BuildingKind kind) => kind == BuildingKind.Lighthouse || kind == BuildingKind.Dock;

        // Index of the settlement's building of that kind that is still there (a finished one before a site), -1 if none.
        public static int LandmarkIndex(IslandSettlementSystem settlement, BuildingKind kind)
        {
            if (settlement == null) return -1;
            int best = -1;
            for (int i = 0; i < settlement.BuildingCount; i++)
            {
                if (settlement.BuildingKindOf(i) != kind) continue;
                var state = settlement.BuildingStateOf(i);
                if (state == BuildingState.Sinking || state == BuildingState.Charred) continue;
                if (state == BuildingState.Done) return i;
                if (best < 0) best = i;
            }
            return best;
        }

        // Where a landmark stands in the settlement's (island's) frame: the lighthouse's foot, the middle of the
        // harbour's jetty.
        public static Vector3 LandmarkWorld(IslandSettlementSystem settlement, int index, out float height)
        {
            var tr = settlement.transform;
            var surface = settlement.GetComponent<IIslandSurface>();
            Vector2 local = settlement.BuildingPositionOf(index);
            height = settlement.BuildingHeightOf(index);
            float ground = surface != null ? Mathf.Max(0f, surface.SampleHeight(local)) : 0f;
            Vector3 foot = tr.TransformPoint(local.x, ground, local.y);
            // The harbour (v0.6.5): jetty, boat, the fisherman's hut and its clutter as one picture.
            if (settlement.BuildingKindOf(index) == BuildingKind.Dock && settlement.TryGetHarbourView(index, out Vector3 centre, out _))
                return centre;
            if (settlement.BuildingKindOf(index) == BuildingKind.Dock && settlement.TryGetDockWorld(out Vector3 end, out _))
            {
                Vector3 mid = Vector3.Lerp(foot, end, 0.5f);
                mid.y = Mathf.Max(foot.y * 0.5f, end.y);
                return mid;
            }
            return foot;
        }

        public static WatchSubject OfLandmark(IslandSettlementSystem settlement, BuildingKind kind)
        {
            int index = LandmarkIndex(settlement, kind);
            if (index < 0) return null;
            var system = settlement;
            var island = settlement.GetComponent<Island>();
            LandmarkWorld(settlement, index, out float height);
            string label = LandmarkName(kind);
            float harbourRadius = 0f;
            bool harbour = kind == BuildingKind.Dock && settlement.TryGetHarbourView(index, out _, out harbourRadius);
            return new WatchSubject
            {
                label = label,
                ground = island,
                still = true,
                // A lighthouse is framed with the ground round its foot; the jetty with the water it reaches into.
                radius = harbour ? Mathf.Max(0.5f, harbourRadius) : kind == BuildingKind.Dock ? Mathf.Max(0.5f, height * 2f) : Mathf.Max(0.6f, height * 1.2f),
                lift = harbour ? 0.1f : height * 0.45f,
                // Hut, jetty and boat are small: frame them tight, a little from above (the hut stands behind the jetty).
                fill = harbour ? SettlementFill : 0f,
                pitch = harbour ? 32f : 0f,
                gone = label + " ist nicht mehr da",
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (system == null || !system.isActiveAndEnabled || (island != null && (island.IsSunk || !island.isActiveAndEnabled))) return false;
                    int i = LandmarkIndex(system, kind);
                    if (i < 0) return false;
                    f = LandmarkWorld(system, i, out _);
                    return true;
                },
            };
        }

        // ---------------------------------------------------------------- settlements at their campfire

        // The sparkle's height over the fire (it sits a little above the flames, not on the ground).
        public const float CampfireSparkleSize = 0.5f;
        // A village from above: steep enough that roofs do not hide the fire and the folk round it.
        public const float SettlementPitch = 38f;
        public const float SettlementFill = 0.85f;

        static readonly string[] SettlementNames = { "Siedlung", "Zeltlager", "Weiler", "Dorf", "Stadt" };

        // "Dorf am Lagerfeuer": what the follow chip says while a settlement is watched.
        public static string SettlementLabel(SettlementStage stage)
        {
            int i = (int)stage;
            return (i >= 0 && i < SettlementNames.Length ? SettlementNames[i] : SettlementNames[0]) + " am Lagerfeuer";
        }

        public static bool CampfireStands(IslandSettlementSystem s, int i)
        {
            if (s == null || i < 0 || i >= s.BuildingCount || s.BuildingKindOf(i) != BuildingKind.Campfire) return false;
            var state = s.BuildingStateOf(i);
            return state != BuildingState.Sinking && state != BuildingState.Charred;
        }

        // The campfire of a village, preferring the building index it had (-1 when the village has none standing).
        public static int CampfireOf(IslandSettlementSystem s, int village, int hint = -1)
        {
            if (s == null) return -1;
            if (CampfireStands(s, hint) && s.BuildingVillageOf(hint) == village) return hint;
            if (village < 0) return -1;
            for (int i = 0; i < s.BuildingCount; i++)
                if (CampfireStands(s, i) && s.BuildingVillageOf(i) == village) return i;
            return -1;
        }

        // The fire's foot on the ground, lifted a little: where a tap is measured.
        public static Vector3 CampfireWorld(IslandSettlementSystem s, int i)
        {
            var surface = s.GetComponent<IIslandSurface>();
            Vector2 local = s.BuildingPositionOf(i);
            float ground = surface != null ? Mathf.Max(0f, surface.SampleHeight(local)) : 0f;
            return s.transform.TransformPoint(local.x, ground + 0.15f, local.y);
        }

        // How much ground the camera frames round the fire: its village's homes with a margin, at least a camp's worth.
        // A lighthouse or harbour counts to the village it was added to but stands far off at the coast, so landmarks
        // are left out (a camp with a lighthouse framed the whole island from 60 units away).
        public static float SettlementFrameRadius(IslandSettlementSystem s, int village, int fire)
        {
            if (s == null || fire < 0 || fire >= s.BuildingCount || village < 0) return 2.2f;
            Vector2 c = s.BuildingPositionOf(fire);
            float r = 0f;
            for (int i = 0; i < s.BuildingCount; i++)
            {
                if (IsLandmark(s.BuildingKindOf(i)) || s.BuildingVillageOf(i) != village) continue;
                r = Mathf.Max(r, (s.BuildingPositionOf(i) - c).magnitude + s.BuildingRadiusOf(i));
            }
            return Mathf.Clamp(r * 0.9f + 0.5f, 2.2f, 7f);
        }

        // Tapping a campfire watches its settlement: a still subject centred on the fire, framed wide enough to show
        // the houses round it, slowly circled like a landmark. The village keeps growing while it is watched, so the
        // framing radius and the label follow it.
        public static WatchSubject OfCampfire(IslandSettlementSystem settlement, int building)
        {
            if (!CampfireStands(settlement, building)) return null;
            var system = settlement;
            var island = settlement.GetComponent<Island>();
            int village = settlement.BuildingVillageOf(building);
            int fire = building;
            var stage = village >= 0 && village < settlement.VillageCount ? settlement.VillageStage(village) : SettlementStage.Camp;
            var subject = new WatchSubject
            {
                label = SettlementLabel(stage),
                ground = island,
                still = true,
                pitch = SettlementPitch,
                fill = SettlementFill,
                radius = SettlementFrameRadius(settlement, village, building),
                lift = 0.2f,
                gone = "Die Siedlung ist nicht mehr da",
            };
            Vector3 last = CampfireWorld(settlement, building);
            subject.focus = (out Vector3 f) =>
            {
                f = default;
                if (system == null || !system.isActiveAndEnabled || (island != null && (island.IsSunk || !island.isActiveAndEnabled))) return false;
                int i = CampfireOf(system, village, fire);
                if (i < 0)
                {
                    // Village indices shift when one is given up: the standing fire nearest to where this one was.
                    float bestSqr = 9f;
                    for (int k = 0; k < system.BuildingCount; k++)
                    {
                        if (!CampfireStands(system, k)) continue;
                        float d = (CampfireWorld(system, k) - last).sqrMagnitude;
                        if (d >= bestSqr) continue;
                        bestSqr = d;
                        i = k;
                    }
                    if (i < 0) return false;
                    village = system.BuildingVillageOf(i);
                }
                fire = i;
                last = CampfireWorld(system, i);
                f = last - Vector3.up * 0.15f;
                // The village grows while it is watched; the frame follows a few times a second.
                if (Time.frameCount % 20 == 0) subject.radius = SettlementFrameRadius(system, village, i);
                return true;
            };
            return subject;
        }

        // Where the folk hold their festival: the ground in the middle of the real villages while it runs, else the
        // most grown village (a landmark-only village has stage None and is never chosen over a real one).
        public static WatchSubject OfFestival(IslandSettlementSystem settlement)
        {
            if (settlement == null || settlement.VillageCount == 0 || !FestivalPlace(settlement, out _)) return null;
            var system = settlement;
            var island = settlement.GetComponent<Island>();
            return new WatchSubject
            {
                label = "Festplatz",
                ground = island,
                still = true,
                radius = 2.2f,
                lift = 0.15f,
                gone = "Das Dorf ist nicht mehr da",
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (system == null || !system.isActiveAndEnabled || (island != null && (island.IsSunk || !island.isActiveAndEnabled))) return false;
                    if (!FestivalPlace(system, out Vector2 local)) return false;
                    var surface = system.GetComponent<IIslandSurface>();
                    f = system.transform.TransformPoint(local.x, surface != null ? Mathf.Max(0f, surface.SampleHeight(local)) : 0f, local.y);
                    return true;
                },
            };
        }

        static bool FestivalPlace(IslandSettlementSystem s, out Vector2 local)
        {
            local = default;
            if (s.Festival || s.LanternCount > 0)
            {
                local = s.FestivalGround;
                return true;
            }
            int best = -1;
            for (int v = 0; v < s.VillageCount; v++)
                if (s.VillageStage(v) != SettlementStage.None && (best < 0 || s.VillageStage(v) > s.VillageStage(best))) best = v;
            if (best < 0) return false;
            local = s.VillageCenter(best);
            return true;
        }

        // The seabirds that came with the milestone: a home flock of the player's island, else the nearest seabirds.
        public static int HomeSeabirdFlock(FlockSystem flocks, Island player)
        {
            if (flocks == null || !flocks.isActiveAndEnabled) return -1;
            Vector2 near = player != null ? player.PlanarPosition : Vector2.zero;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < flocks.FlockCount; i++)
            {
                if (!flocks.IsSeabird(i) || flocks.BirdCountOf(i) == 0) continue;
                float d = (flocks.PositionOf(i) - near).sqrMagnitude;
                if (flocks.IsHomeFlock(i) && (player == null || flocks.TargetOf(i) == player)) d -= 1e9f;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            return best;
        }

        // What a milestone toast shows when it is tapped; null when the milestone has nothing to look at right now
        // (the lighthouse could not be placed yet, no seabirds are out, the island has no village) - the toast then
        // offers no tap.
        public static WatchSubject OfMilestone(Milestone m, Island player, FlockSystem flocks)
        {
            var settlement = player != null ? player.GetComponent<IslandSettlementSystem>() : null;
            switch (m)
            {
                case Milestone.Lighthouse: return OfLandmark(settlement, BuildingKind.Lighthouse);
                case Milestone.Harbour: return OfLandmark(settlement, BuildingKind.Dock);
                case Milestone.Seabirds:
                {
                    var s = OfFlock(flocks, HomeSeabirdFlock(flocks, player));
                    if (s != null) s.label = Milestones.ShortNameOf(Milestone.Seabirds);
                    return s;
                }
                case Milestone.Festival: return OfFestival(settlement);
            }
            return null;
        }

        // The cheap question behind the toast's tap affordance (asked a few times a second).
        public static bool MilestoneHasSubject(Milestone m, Island player, FlockSystem flocks)
        {
            var settlement = player != null ? player.GetComponent<IslandSettlementSystem>() : null;
            switch (m)
            {
                case Milestone.Lighthouse: return LandmarkIndex(settlement, BuildingKind.Lighthouse) >= 0;
                case Milestone.Harbour: return LandmarkIndex(settlement, BuildingKind.Dock) >= 0;
                case Milestone.Seabirds: return HomeSeabirdFlock(flocks, player) >= 0;
                case Milestone.Festival: return settlement != null && settlement.VillageCount > 0 && FestivalPlace(settlement, out _);
            }
            return false;
        }

        // Seals visit the coast of any island in view; the nearest one that is out is watched.
        static WatchSubject FindSeal(CollectEntry e, SeaLifeSystem sea, Vector2 near)
        {
            if (sea == null || !sea.isActiveAndEnabled) return null;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < sea.SealSlots; i++)
            {
                if (!sea.SealActive(i)) continue;
                float d = (sea.SealPosition(i) - near).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            return OfSeal(e, sea, best);
        }

        public static WatchSubject OfSeal(SeaLifeSystem sea, int slot)
        {
            int index = CollectionCatalog.IndexOf(SeaKind.Seal);
            return index < 0 ? null : OfSeal(CollectionCatalog.At(index), sea, slot);
        }

        // Follows one seal slot while it stays out: in the water, looking at the island or lying on the beach.
        public static WatchSubject OfSeal(CollectEntry e, SeaLifeSystem sea, int slot)
        {
            if (sea == null || slot < 0 || slot >= sea.SealSlots || !sea.SealActive(slot)) return null;
            var ground = CoastOf(sea.SealPosition(slot));
            return new WatchSubject
            {
                label = e.name,
                radius = 2f,
                ground = ground,
                focus = (out Vector3 f) =>
                {
                    f = default;
                    if (sea == null || !sea.SealActive(slot)) return false;
                    Vector2 p = sea.SealPosition(slot);
                    float y = 0.1f;
                    // On the beach (2) or hauling out (4) it lies on the ground, not at sea level.
                    int state = sea.SealStateOf(slot);
                    if ((state == 2 || state == 4) && ground != null && ground.isActiveAndEnabled && !ground.IsSunk)
                        y = Mathf.Max(0.1f, ground.SampleHeight(ground.ToLocal(p)) + 0.2f);
                    f = new Vector3(p.x, y, p.y);
                    return true;
                },
            };
        }

        // The island whose coast a seal visits: the one whose rim is nearest.
        static Island CoastOf(Vector2 pos)
        {
            Island best = null;
            float bestGap = 12f;
            var all = Island.All;
            for (int i = 0; i < all.Count; i++)
            {
                var island = all[i];
                if (island == null || !island.isActiveAndEnabled || island.IsSunk) continue;
                float gap = (island.PlanarPosition - pos).magnitude - island.BoundingRadius;
                if (gap >= bestGap) continue;
                bestGap = gap;
                best = island;
            }
            return best;
        }

        // For a tap on the sea: the active seal nearest to the tap on screen within radiusPx, -1 if none.
        public static int SealAtScreen(SeaLifeSystem sea, Camera cam, Vector2 tap, float radiusPx)
        {
            if (sea == null || cam == null || !sea.isActiveAndEnabled) return -1;
            int best = -1;
            float bestD = radiusPx;
            for (int i = 0; i < sea.SealSlots; i++)
            {
                if (!sea.SealActive(i) || sea.SealStateOf(i) == 0) continue;
                Vector2 p = sea.SealPosition(i);
                Vector3 screen = cam.WorldToScreenPoint(new Vector3(p.x, 0.2f, p.y));
                if (screen.z <= 0f) continue;
                float d = (new Vector2(screen.x, screen.y) - tap).magnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = i;
            }
            return best;
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
