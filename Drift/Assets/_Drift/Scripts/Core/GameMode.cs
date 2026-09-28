using System;

namespace Drift.Core
{
    // Gemütlich = the cozy main game (drift, merge everything into a Pangäa, no game over);
    // Abenteuer = the ring-world survival run (score = time survived, lost by sinking).
    public enum GameMode { Cozy, Adventure }

    // The one place every system asks which mode runs. GameSession sets it when a game starts; systems that behave
    // differently per mode read Current or listen to Changed (fired only on a real change).
    public static class GameModes
    {
        public static GameMode Current { get; private set; } = GameMode.Cozy;
        public static event Action<GameMode> Changed;

        public static bool IsAdventure => Current == GameMode.Adventure;

        public static void Set(GameMode mode)
        {
            if (mode == Current) return;
            Current = mode;
            Changed?.Invoke(mode);
        }

        // The cozy file keeps its old name so existing saves load unchanged.
        public static string SaveFile(GameMode mode) => mode == GameMode.Cozy ? "drift_save.json" : "drift_adventure.json";

        public static string Label(GameMode mode) => mode == GameMode.Cozy ? "Gemütlich" : "Abenteuer";
    }
}
