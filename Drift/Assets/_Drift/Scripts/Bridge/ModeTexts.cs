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

        // Abenteuer scores the distance along the ring: the caption under its title button, the HUD's best line.
        public static string BestDistanceCaption(float best) => best > 0f ? RecordLabel(best) : "Wie weit kommst du?";
        public static string RecordLabel(float best) => "Rekord " + BestDistances.Format(best);
        public const string FirstRunLabel = "Erster Versuch";
        public const string NewRecordLabel = "Neuer Rekord!";

        public static string RecordLine(BestDistances.Result r) => r.BeatPrevious ? NewRecordLabel : r.IsFirst ? "Erster Rekord!" : "";

        public static string AdventureStats(SessionStats st, float best) =>
            AdventureStats(st, best, AdventureRunStats.Flotsam, AdventureRunStats.Hits, AdventureRunStats.Dodges);

        // Islands are obstacles in Abenteuer: what counts is the distance, the flotsam you picked up and how often
        // you hit an island (and slipped past one).
        public static string AdventureStats(SessionStats st, float best, int flotsam, int hits, int dodges)
        {
            return
                $"Strecke   {BestDistances.Format(st.distance)}\n" +
                $"Rekord   {BestDistances.Format(best > st.distance ? best : st.distance)}\n" +
                // One value per line: the three side by side wrapped on a phone and left a lone "·" at the line end.
                $"Treibgut   {flotsam}\n" +
                $"Ausgewichen   {dodges}\n" +
                $"Rempler   {hits}";
        }

        // "3:42", "12:05", "1:02:09": Gemütlich still shows how long a world lasted.
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            long s = (long)System.Math.Floor(seconds);
            long h = s / 3600, m = s / 60 % 60, sec = s % 60;
            return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
        }

        public static string CozyStats(SessionStats st)
        {
            return
                $"Überlebt   {Clock(st.timeSurvived)}\n" +
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

        // ---- the labelled HUD rows (both modes) and the cozy explanation card

        public const string BuoyancyLabel = "Auftrieb";
        public const string WorldLabel = "Welt";
        // Abenteuer: shown in the status slot when the bar runs low and nothing louder is going on.
        public const string LowBuoyancyCall = "Hol dir Treibgut!";
        // Below this the adventure bar pulses and the call above appears.
        public const float LowBuoyancy = 0.35f;

        // Right of the world bar: how many of the world's islands are already part of yours.
        public static string CozyIslands(int merged, int total)
        {
            if (total <= 0) return "";
            if (merged >= total) return "alle vereint";
            return merged + " von " + total + " Inseln";
        }

        // "5 von 20 Inseln · 3 Vulkane": consumed counts the planned world slots that are merged away; the volcanoes
        // rise out of the sea on their own, are no world slots and are listed apart (only once there are any).
        public static string WorldProgressLabel(int consumed, int total, int volcanoes)
        {
            string islands = CozyIslands(consumed, total);
            if (volcanoes <= 0 || total <= 0 || consumed >= total) return islands;
            return islands + " · " + VolcanoCount(volcanoes);
        }

        public static string VolcanoCount(int volcanoes) => volcanoes == 1 ? "1 Vulkan" : volcanoes + " Vulkane";

        // Beside "Landmasse": in Gemütlich the island sinks slowly towards a floor, which is why the number shrinks
        // while nothing happens; at the floor it simply floats.
        public static string CozySinkStatus(bool sinking) => sinking ? "sinkt langsam" : "schwimmt ruhig";

        // Right of the buoyancy bar; 0.5 is where the old "Form 50 %" switched from länglich to rund.
        public static string FormLabel(float compactness) => compactness >= 0.475f ? "Form: rund" : "Form: länglich";

        // The card behind a tap on the cozy HUD (and shown once by itself the first time the island sinks).
        public const string ExplainSinking = "Deine Insel sinkt langsam – vereine Inseln, dann wächst sie wieder.";
        public const string ExplainWorld = "Welt: so viel Land hast du schon vereint.";
        public const string ExplainBuoyancy = "Auftrieb: so hoch schwimmt deine Insel. Rund schwimmt besser als länglich.";
        public const string ExplainClose = "Tippen zum Schließen";

        // ---- menus

        // Pause menu in Abenteuer: leaving early never scores.
        public const string AdventurePauseNote = "Abbrechen zählt nicht als Rekord";
        // Asked before a new cozy world replaces the one that is saved or running.
        public const string NewWorldTitle = "Neue Welt beginnen?";
        public const string NewWorldBody = "Deine jetzige Welt geht verloren.";
        public const string NewWorldCancel = "Abbrechen";
        public const string NewWorldConfirm = "Neu beginnen";
        // "Gemütlich" on the title with a saved world: continue it or start a new one - the second replaces the save,
        // which the one line above the buttons says, so there is no further question.
        public const string CozyContinue = "Weiter";
        public const string CozyNewWorld = "Neu beginnen";
        public const string CozyChoiceNote = "„Neu beginnen“ ersetzt deine gespeicherte Welt.";
        public static string SeedCaption(int seed, bool legacy) => legacy ? "Welt: Standard" : "Welt #" + seed;

        // The driving hint of the HUD. Abenteuer drives itself ("man fährt immer"): the input only steers sideways,
        // pulling back brakes and can never bring the island to a stop, so both steering schemes read the same.
        // tilt: the phone's tilt steering is on, so no finger steers. Touch steering is an invisible stick: a finger
        // goes down anywhere off the buttons and drags.
        public static string SteerHint(GameMode mode, bool touch, bool direct, bool tilt = false)
        {
            if (mode == GameMode.Adventure)
                return tilt ? "Neigen: seitlich lenken, zu dir kippen bremst" : touch ? "Ziehen: seitlich lenken, nach unten bremsen" : "A/D lenken  ·  S bremsen";
            if (tilt) return "Handy neigen: Richtung";
            if (touch) return direct ? "Finger aufsetzen und ziehen: Richtung" : "Finger aufsetzen und ziehen: lenken & Tempo";
            return direct ? "W A S D Richtung" : "W/S Tempo  ·  A/D lenken";
        }

        // The help's touch row: there is nothing on screen to point at, so it says where the finger goes.
        public static string SteerHelp(GameMode mode, bool direct)
        {
            const string where = "Irgendwo aufsetzen und ziehen:\n";
            if (mode == GameMode.Adventure) return where + "seitlich lenken, nach unten bremsen";
            return where + (direct ? "die Insel fährt in diese Richtung" : "lenken & Tempo");
        }

        static readonly System.Globalization.CultureInfo German = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

        // German decimal comma: "Schub  ×1,6".
        public static string BoostLabel(float factor) => factor > 1.01f ? "Schub  ×" + factor.ToString("0.0", German) : "Schub!";

        // The short adventure HUD calls: a hit, a close pass, riding a plate boundary, and the difficulty level.
        public static string HitLabel(float lostShare) => lostShare > 0.005f
            ? "Rums!  −" + Mathf.RoundToInt(lostShare * 100f) + " % Auftrieb"
            : "Rums!";

        public const string DodgeLabel = "Knapp vorbei!";
        // Under the adventure strip while the race waits on the start line; it fades out with the "Los!".
        public const string AdventureStartLine = "Weiche Inseln aus, sammle Treibgut – wie weit kommst du?";
        public const string GoLabel = "Los!";
        public const string MomentumLabel = "Schwung";
        // At the rim of the band: there is nothing to fall over, the world simply ends there.
        public const string EdgeLabel = "Hier endet die Welt!";

        // The title of the game-over screen. A run only ever ends by sinking; the flag is kept for the caller.
        public static string LostTitle(GameMode mode, bool overEdge) => "Versunken";
        public const string SurfLabel = "Surfen!";
        public static string LevelLabel(int level) => "Stufe " + Mathf.Max(1, level);

        public static string RestartLabel(GameMode mode) => mode == GameMode.Adventure ? "Neuer Versuch" : "Neu beginnen";

        // ---- the "Abenteuer" page of the run journal ("Durchgänge")

        public const string AdventureResetLabel = "Abenteuer-Rekord zurücksetzen";
        public const string AdventureResetQuestion = "Rekord und Abenteuer-Durchgänge löschen?";
        public const string AdventureResetNote = "Das lässt sich nicht rückgängig machen.\nDeine gemütlichen Welten bleiben erhalten.";
        public const string AdventureResetYes = "Ja";
        public const string AdventureResetCancel = "Abbrechen";
        public const string AdventureLogEmpty = "Noch kein Abenteuer gefahren";
        public const string AdventureLogEmptyBody = "Jeder Versuch im Abenteuer bekommt hier eine Zeile: wann, wie weit und bis zu welcher Stufe.";

        public static string AdventureRecordHeader(float best) => best > 0f ? RecordLabel(best) : "Noch kein Rekord";

        public static string AdventureRunCount(int runs) => runs <= 0 ? "Deine Versuche im Abenteuer" : runs == 1 ? "1 Durchgang" : runs + " Durchgänge";

        // "26.09. 23:41 · 4.102 m · Stufe 8"; an unreadable date or an unknown level is left out.
        public static string AdventureRunLine(AdventureRun r)
        {
            if (r == null) return "";
            string line = BestDistances.Format(r.metres);
            if (r.level > 0) line += " · " + LevelLabel(r.level);
            if (!string.IsNullOrEmpty(r.date) && System.DateTime.TryParse(r.date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
                line = t.ToString("dd.MM. HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " · " + line;
            return line;
        }

        // The small line under it: "Neuer Rekord · Treibgut 12 · Ausgewichen 5 · Rempler 3".
        public static string AdventureRunDetails(AdventureRun r)
        {
            if (r == null) return "";
            string details = "Treibgut " + r.flotsam + " · Ausgewichen " + r.dodges + " · Rempler " + r.hits;
            return r.record ? NewRecordLabel.TrimEnd('!') + " · " + details : details;
        }
    }
}
