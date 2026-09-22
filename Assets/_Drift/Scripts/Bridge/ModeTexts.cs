using Drift.Core;
using Drift.Islands;
using Drift.SaveSystem;
using UnityEngine;

namespace Drift.Bridge
{
    // The per-mode texts of the menus and the HUD, kept apart from the layout code so they can be tested.
    public static class ModeTexts
    {
        public const string CozyTagline = "Treibe, sammle, wachse zu einem Kontinent.";
        public const string CozyHint = "ohne Zeitdruck";
        // Below this buoyancy the HUD speaks up: an alarm in Abenteuer, a calm note in Gemütlich.
        public const float HeavyBuoyancy = 0.55f;

        public static string BestTimeCaption(float best) => best > 0f ? "Bestzeit " + BestTimes.Format(best) : "Wie lange hältst du durch?";

        public static string RecordLine(BestTimes.Result r) => r.BeatPrevious ? "Neuer Rekord!" : r.IsFirst ? "Erste Bestzeit!" : "";

        public static string AdventureStats(SessionStats st, float best) =>
            AdventureStats(st, best, AdventureRunStats.Flotsam, AdventureRunStats.Hits, AdventureRunStats.Dodges);

        // Islands are obstacles in Abenteuer: what counts is the time, the flotsam you picked up and how often you
        // hit an island (and slipped past one).
        public static string AdventureStats(SessionStats st, float best, int flotsam, int hits, int dodges)
        {
            return
                $"Überlebt   {BestTimes.Format(st.timeSurvived)}\n" +
                $"Bestzeit   {BestTimes.Format(best > st.timeSurvived ? best : st.timeSurvived)}\n" +
                $"Treibgut   {flotsam}   ·   Ausgewichen   {dodges}   ·   Rempler   {hits}";
        }

        public static string CozyStats(SessionStats st)
        {
            return
                $"Überlebt   {BestTimes.Format(st.timeSurvived)}\n" +
                $"Inseln gesammelt   {st.islandsAbsorbed}\n" +
                $"Davon Vulkane   {st.volcanoesAbsorbed}\n" +
                $"Größte Landmasse   {st.peakLandMass:F0}";
        }

        // "7 von 20 Inseln  ·  38 % der Welt"; merged counts the islands that are gone from the world.
        public static string CozyProgress(int merged, int total, int percent)
        {
            if (total <= 0) return percent + " % der Welt";
            if (merged >= total) return "Die Welt ist vereint!";
            return merged + " von " + total + " Inseln  ·  " + percent + " % der Welt";
        }

        public static string SinkLabel(GameMode mode, float buoyancy)
        {
            bool heavy = buoyancy < HeavyBuoyancy;
            if (mode == GameMode.Adventure) return heavy ? "Sinkt – hol dir Treibgut!" : "Auftrieb";
            return heavy ? "Insel ist schwer" : "Tiefgang";
        }

        // The driving hint of the HUD. Abenteuer drives itself ("man fährt immer"): the input only steers sideways,
        // pulling back brakes and can never bring the island to a stop, so both steering schemes read the same.
        public static string SteerHint(GameMode mode, bool touch, bool direct)
        {
            if (mode == GameMode.Adventure)
                return touch ? "Stick links/rechts: lenken  ·  zurück: bremsen" : "A/D lenken  ·  S bremst  ·  Tempo läuft";
            if (touch) return direct ? "Stick links: Richtung" : "Stick links: lenken & Tempo";
            return direct ? "W A S D Richtung" : "W/S Tempo  ·  A/D lenken";
        }

        public static string BoostLabel(float factor) => factor > 1.01f ? "Schub  ×" + factor.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) : "Schub!";

        // The short adventure HUD calls: a hit, a close pass, riding a plate boundary, and the difficulty level.
        public static string HitLabel(float lostShare) => lostShare > 0.005f
            ? "Rums!  −" + Mathf.RoundToInt(lostShare * 100f) + " % Auftrieb"
            : "Rums!";

        public const string DodgeLabel = "Knapp vorbei!";
        // At the rim of the band: there is nothing to fall over, the world simply ends there.
        public const string EdgeLabel = "Hier endet die Welt!";

        // The title of the game-over screen. A run only ever ends by sinking; the flag is kept for the caller.
        public static string LostTitle(GameMode mode, bool overEdge) => "Versunken";
        public const string SurfLabel = "Surfen!";
        public static string LevelLabel(int level) => "Stufe " + Mathf.Max(1, level);

        public static string RestartLabel(GameMode mode) => mode == GameMode.Adventure ? "Neuer Versuch" : "Neu beginnen";
    }
}
