using UnityEngine;

namespace Drift.UI
{
    public enum SteerScheme
    {
        // Push/tilt/press where you want to go and the island drifts straight there, without turning first.
        Richtung,
        // The old scheme: A/D turn the island, W drives it forwards.
        Lenkrad
    }

    // Which steering scheme the player picked, kept in PlayerPrefs beside the tilt tuning. The pause menu's
    // "Steuerung" screen writes it, SessionScreens reads it and drives Island.DirectionSteering from it.
    public static class SteerSettings
    {
        const string Key = "drift_steer_direct";

        static bool _read;
        static bool _direct = true;

        public static bool Direct
        {
            get { Read(); return _direct; }
            set
            {
                Read();
                if (_direct == value) return;
                _direct = value;
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static SteerScheme Scheme => Direct ? SteerScheme.Richtung : SteerScheme.Lenkrad;

        // The scene's own default, used until the player has decided once. Ignored as soon as the setting
        // has been saved, so the inspector never overrides a choice made in the menu.
        public static void SetFallback(bool direct)
        {
            if (PlayerPrefs.HasKey(Key)) { Read(); return; }
            _read = true;
            _direct = direct;
        }

        static void Read()
        {
            if (_read) return;
            _read = true;
            _direct = PlayerPrefs.GetInt(Key, 1) != 0;
        }
    }
}
