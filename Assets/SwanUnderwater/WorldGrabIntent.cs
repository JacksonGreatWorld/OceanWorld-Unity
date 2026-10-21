using System;
using System.Numerics;

namespace SwanUnderwater
{
    /// <summary>A short opportunity to finish an already confirmed grasp; range/occlusion remain scene decisions.</summary>
    public sealed class WorldGrabIntent
    {
        public const float WindowSeconds = .5f, MaximumPalmTravel = .5f;
        public float RemainingSeconds { get; private set; }
        public bool Available => RemainingSeconds > 0f;
        Vector3 startedPalm;

        public void Reset() { RemainingSeconds = 0f; startedPalm = Vector3.Zero; }
        public void Step(float dt, bool validPose, bool active, bool started, Vector3 palm, bool suppressed)
            => StepCore(dt, validPose, active, started, palm, suppressed, false);

        /// <summary>
        /// A palm-tracked short finger gap can only spend time from an existing window.
        /// ShellGrabGesture bounds retained Active to 80 ms; consumers still require ValidThisStep.
        /// </summary>
        public void Step(float dt, bool palmTracked, ShellGrabGesture gesture, Vector3 palm, bool suppressed)
        {
            bool active = gesture != null && gesture.Active;
            bool complete = palmTracked && gesture != null && gesture.ValidThisStep;
            StepCore(dt, complete, active, complete && gesture.StartedThisStep, palm, suppressed,
                palmTracked && active && !complete);
        }

        void StepCore(float dt, bool validPose, bool active, bool started, Vector3 palm, bool suppressed, bool retainExisting)
        {
            if (suppressed || (!validPose && !(retainExisting && Available)) || !active ||
                !Finite(dt) || dt <= 0f || dt > .1f || !Finite(palm))
            { Reset(); return; }
            if (validPose && started) { startedPalm = palm; RemainingSeconds = WindowSeconds; return; }
            if (!Available) return;
            if (Vector3.DistanceSquared(palm, startedPalm) > MaximumPalmTravel * MaximumPalmTravel)
            { Reset(); return; }
            RemainingSeconds = Math.Max(0f, RemainingSeconds - dt);
        }
        public bool Consume()
        {
            if (!Available) return false;
            Reset(); return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
