using System;

namespace SwanUnderwater
{
    /// <summary>Visible-time cinematic sequencing, independent of XR/rendering and scene loading speed.</summary>
    public sealed class OceanEntrySequence
    {
        public enum Beat { Approach, Invite, Wiggle, Turn, Dash, Warp, Reveal, Welcome, Quiet, Chapter, Finished, Failed }
        public Beat Current { get; private set; } = Beat.Approach;
        public float TimeInBeat { get; private set; }
        public float TotalVisibleTime { get; private set; }
        public bool WorldRevealed => Current >= Beat.Reveal && Current <= Beat.Finished;
        // A fully faded reveal releases controls; the readable captions remain an overlay.
        public bool ControlsAllowed => Current >= Beat.Welcome && Current <= Beat.Finished;
        public const float SwimOrbitSeconds = 8.8f;
        public bool Finished => Current == Beat.Finished;
        public bool SkipRequested { get; private set; }
        public float Duration => DurationOf(Current);
        public float Progress => Math.Min(1f, TimeInBeat / Math.Max(.001f, Duration));

        public static float DurationOf(Beat beat)
        {
            switch (beat)
            {
                case Beat.Approach: return 2.8f;
                case Beat.Invite: return 6.8f;
                case Beat.Wiggle: return 2f; // Invite + Wiggle retain one complete, unaccelerated 8.8s lap.
                case Beat.Turn: return 1.3f;
                case Beat.Dash: return 2.4f;
                case Beat.Warp: return 1.8f;
                case Beat.Reveal: return 1.3f;
                case Beat.Welcome: return 5.6f;
                case Beat.Quiet: return .8f;
                case Beat.Chapter: return 5.2f;
                default: return 0f;
            }
        }

        /// <summary>A continuous, periodic swimming clock with only a small speed variation.
        /// The reef fish keeps cruising instead of almost stopping then rushing around the lap.</summary>
        public static void SwimOrbit(float seconds, out float phase, out float radiansPerSecond)
        {
            float lap = SwimOrbitSeconds;
            double theta = (seconds / lap - Math.Floor(seconds / lap)) * (2.0 * Math.PI);
            phase = (float)(theta - .18 * Math.Sin(theta));
            radiansPerSecond = (float)((2.0 * Math.PI / lap) * (1.0 - .18 * Math.Cos(theta)));
        }

        public bool Step(float dt, bool worldReady)
        {
            if (Finished || Current == Beat.Failed || float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f) return false;
            if (SkipRequested && ControlsAllowed && worldReady)
            { Current = Beat.Finished; TimeInBeat = 0f; return true; }
            // A slow asset frame must not skip a whole turn, dash or subtitle fade.
            dt = Math.Min(dt, .05f);
            TotalVisibleTime += dt;
            TimeInBeat += dt;
            if (TimeInBeat < Duration) return false;
            // Keep heavy scene creation behind the fully opaque Warp, after the visible fish.
            // Readiness must not extend a fish lap while the same main thread builds the reef.
            if (Current == Beat.Warp && !worldReady) return false;
            Current = SkipRequested && Current == Beat.Reveal && worldReady ? Beat.Finished : (Beat)((int)Current + 1);
            TimeInBeat = 0f;
            return true;
        }

        /// <summary>Request an opaque loading cover, never readiness or controls. The renderer
        /// must approve that cover before the caller passes worldReady to Step.</summary>
        public bool RequestSkip()
        {
            if (Finished || Current == Beat.Failed || SkipRequested) return false;
            SkipRequested = true;
            if (Current <= Beat.Warp) { Current = Beat.Warp; TimeInBeat = DurationOf(Beat.Warp); }
            return true;
        }

        public void Fail() { Current = Beat.Failed; TimeInBeat = 0f; }

        public bool DismissPlayableTitles()
        {
            if (!ControlsAllowed || Finished) return false;
            Current = Beat.Finished; TimeInBeat = 0f;
            return true;
        }

        public static float SubtitleOpacity(float seconds, float duration)
        {
            const float fadeIn = 1.4f, fadeOut = 1.6f;
            float a = Math.Max(0f, Math.Min(1f, Math.Min(seconds / fadeIn, (duration - seconds) / fadeOut)));
            return a * a * (3f - 2f * a);
        }
    }
}
