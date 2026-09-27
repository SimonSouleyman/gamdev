using System;

namespace Drift.SaveSystem
{
    public enum TutorialStep { Greeting, Move, Zoom, Ram, Buoyancy, Form, Watch, Farewell, Done }

    public enum TildaMood { Happy, Wave, Cheer, Sleep }

    // Tilda's tutorial as plain state: the steps advance on reported game events or on "Weiter", never on their
    // own except the farewell. The only thing it asks of the game is SinkingSuspended.
    public sealed class TutorialModel
    {
        public const int StepCount = 8;

        public float moveDistance = 6f;
        public float zoomChange = 0.15f;
        public float idleSeconds = 20f;
        public float cheerSeconds = 1.6f;
        public float farewellSeconds = 10f;

        float _travel, _zoomBase, _idle, _cheer, _stepTime;
        bool _hasZoomBase, _merged, _watched;

        public TutorialStep Step { get; private set; } = TutorialStep.Done;
        public bool Active { get; private set; }
        public bool Done { get; private set; }
        public bool Skipped { get; private set; }
        public int Version { get; private set; }

        public event Action<TutorialStep> StepChanged;
        public event Action Finished;

        public int StepNumber => Math.Min((int)Step, StepCount - 1) + 1;
        public bool SinkingSuspended => Active && Step < TutorialStep.Buoyancy;
        public bool Sleepy => Active && _idle >= idleSeconds;
        public bool Cheering => Active && _cheer > 0f;
        public float Travel => _travel;

        // Steps the player may simply click through; moving and ramming have to be done.
        public bool CanContinue => Active && Step != TutorialStep.Move && Step != TutorialStep.Ram;
        public bool ShowsArrow => Active && Step == TutorialStep.Ram;

        public TildaMood Mood
        {
            get
            {
                if (Sleepy) return TildaMood.Sleep;
                if (Cheering || Step == TutorialStep.Farewell) return TildaMood.Cheer;
                return Step == TutorialStep.Greeting ? TildaMood.Wave : TildaMood.Happy;
            }
        }

        public void Begin()
        {
            Active = true;
            Done = false;
            Skipped = false;
            _merged = _watched = false;
            _cheer = 0f;
            Enter(TutorialStep.Greeting);
        }

        // Restores a finished tutorial (PlayerPrefs flag) without firing events.
        public void MarkDone()
        {
            Active = false;
            Done = true;
            Step = TutorialStep.Done;
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

        public void ReportActivity() => _idle = 0f;

        public void ReportTravel(float distance)
        {
            if (!Active || Step != TutorialStep.Move || distance <= 0f) return;
            _travel += distance;
            if (_travel > moveDistance) Advance(true);
        }

        public void ReportZoom(float zoom)
        {
            if (!Active || Step != TutorialStep.Zoom || zoom <= 0f) return;
            if (!_hasZoomBase)
            {
                _zoomBase = zoom;
                _hasZoomBase = true;
                return;
            }
            if (Math.Abs(zoom / _zoomBase - 1f) > zoomChange) Advance(true);
        }

        // A merge or a look at an animal before its step is remembered, so that step is already solved when it comes up.
        public void ReportMerge()
        {
            if (!Active) return;
            _merged = true;
            if (Step == TutorialStep.Ram) Advance(true);
        }

        public void ReportWatched()
        {
            if (!Active) return;
            _watched = true;
            if (Step == TutorialStep.Watch) Advance(true);
        }

        public void Tick(float dt)
        {
            if (!Active || dt <= 0f) return;
            _idle += dt;
            _stepTime += dt;
            if (_cheer > 0f) _cheer = Math.Max(0f, _cheer - dt);
            if (Step == TutorialStep.Farewell && _stepTime >= farewellSeconds) Finish();
        }

        void Advance(bool cheer)
        {
            if (cheer) _cheer = cheerSeconds;
            var next = Step + 1;
            if (next == TutorialStep.Ram && _merged) { next++; _cheer = cheerSeconds; }
            if (next == TutorialStep.Watch && _watched) next++;
            if (next >= TutorialStep.Done) Finish();
            else Enter(next);
        }

        void Enter(TutorialStep step)
        {
            Step = step;
            _travel = 0f;
            _hasZoomBase = false;
            _idle = 0f;
            _stepTime = 0f;
            Version++;
            StepChanged?.Invoke(step);
        }

        void Finish()
        {
            if (Done && !Active) return;
            Active = false;
            Done = true;
            Step = TutorialStep.Done;
            _cheer = 0f;
            Version++;
            StepChanged?.Invoke(TutorialStep.Done);
            Finished?.Invoke();
        }
    }
}
