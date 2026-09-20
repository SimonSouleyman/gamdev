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

        // What she mumbles when she dozes off; never shown.
        public const string SleepyMumble = "Oooh, muuh. Hach jaa. Mmoh, nuuh, hmm.";

        static readonly KeyValuePair<string, string>[] Lines =
        {
            Line(Greeting, "Hallo, mein Schatz! Ich bin " + K("Tilda") + ". Drück auf " + K("Start") + ", dann treiben wir zusammen los."),
            Line(GameOver, "Ach je, versunken. Das macht nichts, mein Schatz – wir fangen einfach " + K("neu") + " an."),
            Line(NewIsland, "So, auf ein Neues! Diesmal wird sie noch größer."),
            Line(Pause, "Eine kleine Pause? Lass dir ruhig Zeit, ich warte hier."),
            Line(VoiceOn, "Mh-hm! Da bin ich wieder."),
            Line("idle_1", "Such dir ruhig eine " + K("Welt-Nummer") + " aus. Jede ist ein anderes Meer."),
            Line("idle_2", "Ich dampfe hier gemütlich vor mich hin. Sag Bescheid, wenn es losgeht."),
            Line("idle_3", "Im " + K("Fotoalbum") + " findest du deine schönsten Inseln wieder."),
            Line("idle_4", "Ach, ist das Meer heute wieder schön."),
        };

        static string K(string word) => TildaBubble.Key(word);

        static KeyValuePair<string, string> Line(string key, string text) => new KeyValuePair<string, string>(key, text);

        public static IReadOnlyList<KeyValuePair<string, string>> All() => Lines;

        public static string TextOf(string key)
        {
            foreach (var l in Lines) if (l.Key == key) return l.Value;
            return null;
        }

        public static string Idle(int index) => "idle_" + (((index % IdleCount) + IdleCount) % IdleCount + 1);

        public static GrumbleMood MoodOf(string key)
        {
            switch (key)
            {
                case Greeting: case NewIsland: return GrumbleMood.Cheerful;
                case VoiceOn: return GrumbleMood.Giggly;
                case GameOver: return GrumbleMood.Soft;
                default: return GrumbleMood.Warm;
            }
        }
    }
}
