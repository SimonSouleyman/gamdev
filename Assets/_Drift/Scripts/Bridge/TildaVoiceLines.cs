using System.Collections.Generic;
using Drift.Audio;

namespace Drift.Bridge
{
    // Tilda's short remarks beside the menus, key -> the German text her speech bubble shows while she grumbles it
    // (she has no spoken words any more). Rich text: TildaBubble.Key() marks the one or two words that matter. The
    // tutorial and the Anleitung keep their own texts (TutorialGuide.TextFor, HelpScreen tips).
    public static class TildaVoiceLines
    {
        public const string Greeting = "greeting";
        public const string Pause = "pause";
        public const string GameOver = "gameover";
        public const string NewIsland = "new_island";
        public const string VoiceOn = "voice_on";
        public const int IdleCount = 4;
        // Abenteuer: she wears her "schnelle Brille" there and talks like a race coach. ForMode picks the variant.
        public const string AdventurePrefix = "adv_";
        public const string AdventureRecord = "adv_record";

        // What she mumbles when she dozes off; never shown.
        public const string SleepyMumble = "Oooh, muuh. Hach jaa. Mmoh, nuuh, hmm.";

        static readonly KeyValuePair<string, string>[] Lines =
        {
            Line(Greeting, "Hi! Ich bin " + K("Tilda") + ". Tipp auf " + K("Gemütlich") + " oder " + K("Abenteuer") + ", dann treiben wir zusammen los."),
            Line(GameOver, "Ach je, versunken. Das macht nichts – wir fangen einfach " + K("neu") + " an."),
            Line(NewIsland, "So, auf ein Neues! Diesmal wird sie noch größer."),
            Line(Pause, "Eine kleine Pause? Lass dir ruhig Zeit, ich warte hier."),
            Line(VoiceOn, "Mh-hm! Da bin ich wieder."),
            Line("idle_1", "Such dir ruhig eine " + K("Welt-Nummer") + " aus. Jede ist ein anderes Meer."),
            Line("idle_2", "Ich dampfe hier gemütlich vor mich hin. Sag Bescheid, wenn es losgeht."),
            Line("idle_3", "Im " + K("Fotoalbum") + " findest du deine schönsten Inseln wieder."),
            Line("idle_4", "Ach, ist das Meer heute wieder schön."),
            Line("adv_gameover", "Uff, abgesoffen! Aber das war eine " + K("starke Strecke") + ". Gleich nochmal?"),
            Line("adv_new_island", "Brille sitzt, Lava kocht. Auf die Plätze, fertig – " + K("los") + "!"),
            Line("adv_pause", "Kurz verschnaufen? Gut so. Meine Brille läuft eh gerade an."),
            Line(AdventureRecord, K("Neuer Rekord") + "! Da beschlägt mir glatt die Brille."),
            // Meilensteine im Gemütlich-Modus (Drift.SaveSystem.Milestones.VoiceKeyOf).
            Line("milestone_lighthouse", "Ein " + K("Leuchtturm") + "! Jetzt findest du auch die Inseln hinterm Horizont."),
            Line("milestone_harbour", "Dein eigener " + K("Hafen") + " – da legt bestimmt bald jemand an."),
            Line("milestone_birds", "Hörst du die " + K("Seevögel") + "? Die bleiben jetzt bei uns."),
            Line("milestone_festival", "Heute Abend wird " + K("gefeiert") + "!"),
            Line("pangaea_ready", "Alles vereint! Bleib ruhig noch ein bisschen."),
        };

        static string K(string word) => TildaBubble.Key(word);

        static KeyValuePair<string, string> Line(string key, string text) => new KeyValuePair<string, string>(key, text);

        public static IReadOnlyList<KeyValuePair<string, string>> All() => Lines;

        public static string TextOf(string key)
        {
            foreach (var l in Lines) if (l.Key == key) return l.Value;
            return null;
        }

        // The Abenteuer variant of a remark (adv_ + key) when there is one and the run is an adventure.
        public static string ForMode(string key, bool adventure)
        {
            if (!adventure || key == null) return key;
            string variant = AdventurePrefix + key;
            return TextOf(variant) != null ? variant : key;
        }

        public static string Idle(int index) => "idle_" + (((index % IdleCount) + IdleCount) % IdleCount + 1);

        public static GrumbleMood MoodOf(string key)
        {
            switch (key)
            {
                case Greeting: case NewIsland: case "adv_new_island": return GrumbleMood.Cheerful;
                case VoiceOn: case AdventureRecord: return GrumbleMood.Giggly;
                case GameOver: return GrumbleMood.Soft;
                default: return GrumbleMood.Warm;
            }
        }
    }
}
