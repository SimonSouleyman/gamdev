using System;
using UnityEngine;

namespace Drift.SaveSystem
{
    public enum AdventureTutorialStep { Ring, Score, Flotsam, Dodge, Surf, Go, Done }

    // Tilda's short Abenteuer briefing as plain state: six punchy explanations, told while the island waits at the
    // start line (HoldsStart - owner: "die Insel soll erst losfahren können, wenn das Tutorial fertig ist"). Every
    // step turns on "Weiter" or by itself after a while, and the race starts the moment the last one ends ("Los!")
    // or the briefing is skipped.
    public sealed class AdventureTutorialModel
    {
        public const int StepCount = 6;

        // Steps turn by themselves after this long; the last one ("Los!") after goSeconds.
        public float explainSeconds = 12f;
        public float goSeconds = 6f;

        float _stepTime;

        public AdventureTutorialStep Step { get; private set; } = AdventureTutorialStep.Done;
        public bool Active { get; private set; }
        public bool Done { get; private set; }
        public bool Skipped { get; private set; }
        public int Version { get; private set; }
        // True when the current step was reached because the previous one ran out of time (not by the player).
        public bool TimedOut { get; private set; }

        public event Action<AdventureTutorialStep> StepChanged;
        public event Action Finished;

        public int StepNumber => Math.Min((int)Step, StepCount - 1) + 1;
        public float StepTime => _stepTime;
        // The whole briefing long the island stands at the start line and does not sink.
        public bool HoldsStart => Active;
        public bool CanContinue => Active;
        // The arrow points at the next piece of flotsam ahead, or at the next island on the track.
        public bool ShowsArrow => Active && (Step == AdventureTutorialStep.Flotsam || Step == AdventureTutorialStep.Dodge);

        public float StepLimit => Step == AdventureTutorialStep.Go ? goSeconds : explainSeconds;

        public void Begin()
        {
            Active = true;
            Done = false;
            Skipped = false;
            Enter(AdventureTutorialStep.Ring);
        }

        // Restores a finished briefing (persisted flag) without firing events.
        public void MarkDone()
        {
            Active = false;
            Done = true;
            Step = AdventureTutorialStep.Done;
            Version++;
        }

        public void Skip()
        {
            if (!Active) return;
            Skipped = true;
            Finish();
        }

        public bool Continue()
        {
            if (!CanContinue) return false;
            Advance();
            return true;
        }

        public void Tick(float dt)
        {
            if (!Active || dt <= 0f) return;
            _stepTime += dt;
            if (_stepTime < StepLimit) return;
            Advance();
            if (Active) TimedOut = true;
        }

        void Advance()
        {
            var next = Step + 1;
            if (next >= AdventureTutorialStep.Done) Finish();
            else Enter(next);
        }

        void Enter(AdventureTutorialStep step)
        {
            Step = step;
            _stepTime = 0f;
            TimedOut = false;
            Version++;
            StepChanged?.Invoke(step);
        }

        void Finish()
        {
            if (Done && !Active) return;
            Active = false;
            Done = true;
            Step = AdventureTutorialStep.Done;
            Version++;
            StepChanged?.Invoke(AdventureTutorialStep.Done);
            Finished?.Invoke();
        }
    }

    // "Has the player seen the Abenteuer briefing" in PlayerPrefs. Tests point Key at a scratch key.
    public static class AdventureTutorialProgress
    {
        public const string DefaultKey = "drift_adventure_tutorial_done";
        public static string Key = DefaultKey;

        public static bool IsDone => PlayerPrefs.GetInt(Key, 0) == 1;

        public static void SetDone(bool done)
        {
            if (done) PlayerPrefs.SetInt(Key, 1);
            else PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
