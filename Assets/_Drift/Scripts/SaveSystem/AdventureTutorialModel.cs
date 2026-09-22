using System;
using UnityEngine;

namespace Drift.SaveSystem
{
    public enum AdventureTutorialStep { Ring, Clock, Flotsam, Dodge, Surf, Go, Done }

    // Tilda's short Abenteuer briefing as plain state: six punchy steps that advance on real race events (a piece
    // of flotsam collected, an island dodged, riding a plate boundary) or on "Weiter", and every step ends by itself
    // after a while, so the briefing never holds the race up. The only thing it asks of the game is SinkingSuspended
    // (the first step).
    public sealed class AdventureTutorialModel
    {
        public const int StepCount = 6;

        // Explanation steps turn by themselves after this long, action steps give up waiting after actionSeconds.
        public float explainSeconds = 12f;
        public float actionSeconds = 22f;
        public float goSeconds = 6f;
        public float cheerSeconds = 1.4f;
        // Riding a boundary counts once the surf has been at least surfThreshold strong for surfSeconds in total.
        public float surfThreshold = 0.35f;
        public float surfSeconds = 0.6f;

        float _stepTime, _cheer, _surfTime;
        bool _dodged, _surfed, _collected;

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
        public bool Cheering => Active && _cheer > 0f;
        // Only while she explains the ring: from the clock step on the island sinks, as she says it does.
        public bool SinkingSuspended => Active && Step == AdventureTutorialStep.Ring;
        public bool CanContinue => Active && !IsAction(Step);
        // The arrow points at the next piece of flotsam, or at the island to go round.
        public bool ShowsArrow => Active && (Step == AdventureTutorialStep.Flotsam || Step == AdventureTutorialStep.Dodge);

        public static bool IsAction(AdventureTutorialStep step) =>
            step == AdventureTutorialStep.Flotsam || step == AdventureTutorialStep.Dodge || step == AdventureTutorialStep.Surf;

        public float StepLimit => Step == AdventureTutorialStep.Go ? goSeconds : IsAction(Step) ? actionSeconds : explainSeconds;

        public void Begin()
        {
            Active = true;
            Done = false;
            Skipped = false;
            _dodged = _surfed = _collected = false;
            _cheer = 0f;
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
            Advance(false);
            return true;
        }

        // What the player already did before its step came up is remembered, so that step is skipped.
        public void ReportDodge()
        {
            if (!Active) return;
            _dodged = true;
            if (Step == AdventureTutorialStep.Dodge) Advance(true);
        }

        public void ReportSurf(float strength, float dt)
        {
            if (!Active || _surfed || dt <= 0f || strength < surfThreshold) return;
            _surfTime += dt;
            if (_surfTime < surfSeconds) return;
            _surfed = true;
            if (Step == AdventureTutorialStep.Surf) Advance(true);
        }

        public void ReportFlotsam()
        {
            if (!Active) return;
            _collected = true;
            if (Step == AdventureTutorialStep.Flotsam) Advance(true);
        }

        public void Tick(float dt)
        {
            if (!Active || dt <= 0f) return;
            _stepTime += dt;
            if (_cheer > 0f) _cheer = Math.Max(0f, _cheer - dt);
            if (_stepTime < StepLimit) return;
            if (Step == AdventureTutorialStep.Go) Finish();
            else
            {
                Advance(false);
                TimedOut = true;
            }
        }

        bool Solved(AdventureTutorialStep step) =>
            (step == AdventureTutorialStep.Dodge && _dodged) ||
            (step == AdventureTutorialStep.Surf && _surfed) ||
            (step == AdventureTutorialStep.Flotsam && _collected);

        void Advance(bool cheer)
        {
            if (cheer) _cheer = cheerSeconds;
            var next = Step + 1;
            while (next < AdventureTutorialStep.Done && Solved(next))
            {
                next++;
                _cheer = cheerSeconds;
            }
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
            _cheer = 0f;
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
