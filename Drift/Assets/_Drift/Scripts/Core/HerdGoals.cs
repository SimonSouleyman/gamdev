namespace Drift.Core
{
    // What a species eats: decides where a herd finds food on the vegetation grid (IslandLifeSystem.FoodAt).
    public enum Diet { Grass, Leaves, Reeds, Fish }

    // Why a herd is where it is (v0.6.8 environment layers). The watch card shows the label; None = the old
    // wander/errand behaviour. Values are only ever appended (saves and tests may index them).
    public enum HerdGoal
    {
        None,
        // Needs: grazing where there is food, moving to a greener patch, shade at noon, shelter in rain, a storm
        // scattering the herd.
        Graze, Migrate, Shade, Shelter, Scatter,
        // Relations: mixed herds, keeping a sentry near another species, the dusk truce at the waterside,
        // keeping a distance.
        Mingle, Guard, Gather, Avoid, Follow,
        // Seasons: winter huddle, hoarding, courtship, keeping to the familiar climate after a merge.
        Huddle, Hoard, Court, Climate,
    }

    public static class HerdGoals
    {
        public static string Label(HerdGoal goal)
        {
            switch (goal)
            {
                case HerdGoal.Graze: return "grast";
                case HerdGoal.Migrate: return "zieht zur neuen Weide";
                case HerdGoal.Shade: return "sucht Schatten";
                case HerdGoal.Shelter: return "sucht Schutz vor dem Regen";
                case HerdGoal.Scatter: return "vom Sturm zerstreut";
                case HerdGoal.Mingle: return "mischt sich unter andere";
                case HerdGoal.Guard: return "hält Wache";
                case HerdGoal.Gather: return "trifft sich am Wasser";
                case HerdGoal.Avoid: return "hält Abstand";
                case HerdGoal.Follow: return "folgt einer anderen Herde";
                case HerdGoal.Huddle: return "kuschelt gegen die Kälte";
                case HerdGoal.Hoard: return "legt Vorräte an";
                case HerdGoal.Court: return "Paarungszeit";
                case HerdGoal.Climate: return "bleibt im vertrauten Klima";
                default: return "";
            }
        }

        public static string DietLabel(Diet diet)
        {
            switch (diet)
            {
                case Diet.Leaves: return "Blätter";
                case Diet.Reeds: return "Schilf";
                case Diet.Fish: return "Fisch";
                default: return "Gras";
            }
        }
    }
}
