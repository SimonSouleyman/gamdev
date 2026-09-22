using System;
using Drift.Core;
using UnityEngine;

namespace Drift.SaveSystem
{
    public enum Milestone { Lighthouse, Harbour, Seabirds, Festival }

    // Cozy only: every few merged islands the world gains something the player keeps for the rest of the run
    // (a lighthouse, a harbour, seabirds over the high ground, evening festivals). Pure state - who builds what
    // lives in Bridge/MilestoneDirector, the settlement system and the flocks.
    //
    // The reached set is derived from the run's islandsAbsorbed and only cross-checked against the mask in the
    // save: a mask alone would survive into the next run (static state outlives a domain reload), and a count
    // alone cannot be wrong, so Restore takes the union of both and drops mask bits the count does not cover.
    public static class Milestones
    {
        public const int Count = 4;
        public const string VoicePrefix = "milestone_";
        // Without a lighthouse the minimap only reaches this far around the player (world units); with one it
        // shows the whole sea. WorldHud reads MapRange.
        public const float NearMapRange = 110f;

        static readonly int[] IslandsNeeded = { 3, 6, 10, 15 };
        static readonly string[] ShortNames = { "Leuchtturm", "Hafen", "Seevögel", "Fest" };
        static readonly string[] Toasts =
        {
            "Meilenstein: Dein Leuchtturm steht! Sein Licht zeigt dir ferne Inseln.",
            "Meilenstein: Dein Hafen ist fertig! Jetzt legen Schiffe bei dir an.",
            "Meilenstein: Seevögel kreisen über deinen Bergen.",
            "Meilenstein: Heute Abend feiern die Insulaner ein Fest mit Lampions!",
        };
        static readonly string[] VoiceKeys =
        {
            VoicePrefix + "lighthouse", VoicePrefix + "harbour", VoicePrefix + "birds", VoicePrefix + "festival",
        };

        static int s_mask;

        // Raised once per milestone the moment it is newly reached (never on load or in Abenteuer).
        public static event Action<Milestone> Celebrated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnPlaySessionStart() => s_mask = 0;

        // Milestones belong to the cozy game; an Abenteuer run never reaches one.
        public static bool Enabled => GameModes.Current == GameMode.Cozy;

        public static int Mask => s_mask;
        public static bool Has(Milestone m) => (s_mask & (1 << (int)m)) != 0;
        public static int IslandsFor(Milestone m) => IslandsNeeded[(int)m];
        public static string ShortNameOf(Milestone m) => ShortNames[(int)m];
        public static string ToastOf(Milestone m) => Toasts[(int)m];
        public static string VoiceKeyOf(Milestone m) => VoiceKeys[(int)m];

        public static void Reset() => s_mask = 0;

        public static int Capture() => s_mask;

        // Every milestone whose island count is already reached.
        public static int ImpliedMask(int islandsAbsorbed)
        {
            int mask = 0;
            for (int i = 0; i < Count; i++) if (islandsAbsorbed >= IslandsNeeded[i]) mask |= 1 << i;
            return mask;
        }

        // Loading a run (or starting one: mask 0, no islands). Nothing is celebrated - the effects are simply
        // there again.
        public static void Restore(int mask, int islandsAbsorbed)
        {
            int implied = ImpliedMask(islandsAbsorbed);
            s_mask = implied | (mask & implied);
        }

        // Call while playing; returns how many milestones were newly reached (and raises Celebrated for each).
        public static int Check(int islandsAbsorbed)
        {
            if (!Enabled) return 0;
            int fresh = ImpliedMask(islandsAbsorbed) & ~s_mask;
            if (fresh == 0) return 0;
            s_mask |= fresh;
            int n = 0;
            for (int i = 0; i < Count; i++)
            {
                if ((fresh & (1 << i)) == 0) continue;
                n++;
                Celebrated?.Invoke((Milestone)i);
            }
            return n;
        }

        // The next milestone that is still to come, or -1 when all of them are reached.
        public static int NextIndex(int islandsAbsorbed)
        {
            for (int i = 0; i < Count; i++) if (islandsAbsorbed < IslandsNeeded[i]) return i;
            return -1;
        }

        // For the HUD and the journal: "Nächster Meilenstein: Hafen bei 6 Inseln" (null when nothing is left).
        public static string NextText(int islandsAbsorbed)
        {
            int i = NextIndex(islandsAbsorbed);
            if (i < 0) return null;
            return "Nächster Meilenstein: " + ShortNames[i] + " bei " + IslandsNeeded[i] + " Inseln";
        }

        // How far the minimap shows islands; the lighthouse opens it up to the whole sea.
        public static float MapRange => Has(Milestone.Lighthouse) ? float.PositiveInfinity : NearMapRange;
    }
}
