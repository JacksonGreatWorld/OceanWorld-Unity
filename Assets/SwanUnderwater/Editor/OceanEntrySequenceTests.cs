#if UNITY_EDITOR || OCEAN_ENTRY_TEST_MAIN
using System;
using System.Collections.Generic;
using Beat = SwanUnderwater.OceanEntrySequence.Beat;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Exercises the actual clock/state model without Unity, XR or a replacement sequencer.
    /// Establishes sequencing and readable time, not rendered Warp coverage or headset comfort.
    /// Offline: csc -define:OCEAN_ENTRY_TEST_MAIN -out:/tmp/OceanEntryTests.exe
    ///          OceanEntrySequence.cs Editor/OceanEntrySequenceTests.cs; mono /tmp/OceanEntryTests.exe
    /// </summary>
    public static class OceanEntrySequenceTests
    {
        static int assertions;
        static readonly Beat[] Expected = {
            Beat.Approach, Beat.Invite, Beat.Wiggle, Beat.Turn, Beat.Dash, Beat.Warp,
            Beat.Reveal, Beat.Welcome, Beat.Quiet, Beat.Chapter, Beat.Finished
        };

        static void Require(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("Ocean entry sequence: " + message);
        }

        static bool Near(float a, float b) { return Math.Abs(a - b) < .0001f; }

        static void Log(string message)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        static OceanEntrySequence At(Beat target)
        {
            var sequence = new OceanEntrySequence();
            for (int frame = 0; sequence.Current != target && frame < 10000; frame++)
                sequence.Step(1f / 90f, true);
            if (sequence.Current != target) throw new InvalidOperationException("Cannot reach fixture beat " + target);
            return sequence;
        }

        static void WorldMustBeReady()
        {
            var sequence = new OceanEntrySequence();
            bool exposedBeforeReady = false, reachedDeparture = false;
            // Sixty seconds is far beyond the complete ordinary opening, with readiness never granted.
            for (int frame = 0; frame < 3600; frame++)
            {
                sequence.Step(1f / 60f, false);
                exposedBeforeReady |= sequence.WorldRevealed;
                reachedDeparture |= sequence.Current == Beat.Dash;
            }
            Require(sequence.Current == Beat.Warp && reachedDeparture && !exposedBeforeReady,
                "all visible fish beats finish without loading; a long wait stays at opaque Warp");
            Require(sequence.TimeInBeat > 38f && Near(sequence.Progress, 1f),
                "the fixture really exhausted Warp time without overflowing its progress");
            Require(sequence.Step(1f / 120f, true) && sequence.Current == Beat.Reveal && sequence.TimeInBeat == 0f,
                "readiness releases exactly into a fresh Reveal, without carrying the long wait forward");
            sequence.Step(20f, true);
            Require(sequence.Current == Beat.Reveal && sequence.WorldRevealed && sequence.TimeInBeat <= .05001f,
                "one asset-loading hitch after readiness cannot consume the reveal fade");
            Log("PASS worldReady gate: visible fish finishes independently; 60-second wait then fresh Reveal despite a 20-second frame");
        }

        static void Trace(string name, float[] deltas)
        {
            var sequence = new OceanEntrySequence();
            var visited = new List<Beat> { sequence.Current };
            var dwell = new Dictionary<Beat, double>();
            bool consistentTransitions = true, revealedTooSoon = false, excessiveSingleStep = false;
            double inBeat = 0;
            for (int frame = 0; !sequence.Finished && frame < 20000; frame++)
            {
                float dt = deltas[frame % deltas.Length];
                Beat before = sequence.Current;
                float clockBefore = sequence.TotalVisibleTime;
                inBeat += dt;
                bool changed = sequence.Step(dt, true);
                bool actuallyChanged = before != sequence.Current;
                consistentTransitions &= changed == actuallyChanged;
                excessiveSingleStep |= sequence.TotalVisibleTime - clockBefore > .0501f;
                revealedTooSoon |= sequence.Current < Beat.Reveal && sequence.WorldRevealed;
                if (!actuallyChanged) continue;
                consistentTransitions &= (int)sequence.Current == (int)before + 1 && sequence.TimeInBeat == 0f;
                dwell[before] = inBeat;
                inBeat = 0;
                visited.Add(sequence.Current);
            }
            Require(sequence.Finished, name + ": reaches the end in a bounded number of updates");
            bool ordered = visited.Count == Expected.Length;
            for (int i = 0; ordered && i < Expected.Length; i++) ordered &= visited[i] == Expected[i];
            Require(ordered && consistentTransitions,
                name + ": Approach/Invite/Wiggle/Turn/Dash/Warp/Reveal/Welcome/Quiet/Chapter all occur, one per transition");
            Require(!excessiveSingleStep && !revealedTooSoon,
                name + ": long frames never skip visibility time or expose the world before Reveal");
            // Readability/animation contracts intentionally use independent minimums, not DurationOf.
            Require(dwell[Beat.Approach] >= 2.7999 && dwell[Beat.Invite] >= 6.7999 && dwell[Beat.Wiggle] >= 1.9999 && dwell[Beat.Turn] >= 1.2999 && dwell[Beat.Dash] >= 2.3999 && dwell[Beat.Warp] >= 1.7999,
                name + ": a complete left/right lap, continued cruise, turn, dash and Warp each get their presentation time");
            Require(dwell[Beat.Reveal] >= 1.2999 && dwell[Beat.Welcome] >= 5.5999 &&
                    dwell[Beat.Quiet] >= .7999 && dwell[Beat.Chapter] >= 5.1999,
                name + ": the reveal finishes before two readable subtitles separated by a quiet gap");
            float stopped = sequence.TotalVisibleTime;
            bool restarted = sequence.Step(100f, false);
            Require(!restarted && sequence.Finished && sequence.WorldRevealed && sequence.TotalVisibleTime == stopped,
                name + ": Finished is stable even when readiness subsequently changes");
            Log($"PASS {name}: Welcome={dwell[Beat.Welcome]:F3}s, gap={dwell[Beat.Quiet]:F3}s, " +
                $"Chapter={dwell[Beat.Chapter]:F3}s (elapsed update time, not GPU/render validation)");
        }

        static void SubtitleReadability(Beat beat, float minimumFullOpacity)
        {
            float duration = OceanEntrySequence.DurationOf(beat);
            Func<float, float> opacity = time => OceanEntrySequence.SubtitleOpacity(time, duration);
            Require(Near(opacity(-.1f), 0) && Near(opacity(0), 0) &&
                    Near(opacity(duration), 0) && Near(opacity(duration + .1f), 0),
                beat + ": opacity is zero at and beyond both boundaries");
            Require(Near(opacity(.7f), .5f) && Near(opacity(duration - .8f), .5f),
                beat + ": both fades include a visible midpoint instead of an abrupt cut");
            Require(Near(opacity(1.4f), 1f) && Near(opacity(duration / 2f), 1f) &&
                    Near(opacity(duration - 1.6f), 1f) && duration - 3f >= minimumFullOpacity,
                beat + ": a continuous fully opaque reading hold separates the 1.4s/1.6s fades");
            bool bounded = true, monotonic = true, holdOpaque = true;
            float previousIn = 0, previousOut = 1;
            for (int i = 0; i <= 100; i++)
            {
                float p = i / 100f;
                float value = opacity(p * duration);
                float fadeIn = opacity(1.4f * p), fadeOut = opacity(duration - 1.6f + 1.6f * p);
                bounded &= !float.IsNaN(value) && value >= 0 && value <= 1;
                monotonic &= fadeIn + .0001f >= previousIn && fadeOut <= previousOut + .0001f;
                holdOpaque &= Near(opacity(1.4f + (duration - 3f) * p), 1f);
                previousIn = fadeIn;
                previousOut = fadeOut;
            }
            Require(bounded && monotonic && holdOpaque, beat + ": opacity remains bounded, fades monotonic, hold opaque");
        }

        static void InvalidDeltasAndFailure()
        {
            var running = At(Beat.Welcome);
            running.Step(.025f, true);
            float before = running.TimeInBeat, total = running.TotalVisibleTime;
            bool ignored = true;
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                ignored &= !running.Step(invalid, true);
            Require(ignored && running.Current == Beat.Welcome && running.TimeInBeat == before && running.TotalVisibleTime == total,
                "invalid frame deltas cannot poison time, advance a caption, or change phase");

            foreach (Beat failedDuring in new[] { Beat.Approach, Beat.Invite, Beat.Warp, Beat.Welcome })
            {
                var sequence = At(failedDuring);
                sequence.Step(.025f, true);
                float frozen = sequence.TotalVisibleTime;
                sequence.Fail();
                bool advanced = false;
                for (int frame = 0; frame < 1200; frame++) advanced |= sequence.Step(frame % 2 == 0 ? .016f : 20f, frame % 3 == 0);
                sequence.Fail();
                Require(!advanced && sequence.Current == Beat.Failed && !sequence.Finished && !sequence.WorldRevealed &&
                        sequence.TimeInBeat == 0 && sequence.TotalVisibleTime == frozen,
                    "Fail during " + failedDuring + " remains terminal after repeated time/readiness changes and repeated Fail");
            }
        }

        static void NaturalSwimClock()
        {
            // Test the production clock, not a copy of its modulation formula. Independent angular
            // limits reject the old almost-stationary front segment and its sudden acceleration.
            // The actual 3D path, fish shader and moving-head response remain the rendered preview's job.
            float lap = OceanEntrySequence.SwimOrbitSeconds;
            foreach (int hz in new[] { 30, 90, 120 })
            {
                float dt=1f/hz;
                OceanEntrySequence.SwimOrbit(0f,out float previousPhase,out float previousRate);
                bool finite=true,forward=true,continuous=true,derivativeConsistent=true;
                double minRate=previousRate,maxRate=previousRate,maxAcceleration=0,totalAngle=0;
                int frames=(int)Math.Ceiling(2.0*lap/dt);
                for(int frame=1;frame<=frames;frame++)
                {
                    OceanEntrySequence.SwimOrbit(frame*dt,out float phase,out float rate);
                    finite &= !float.IsNaN(phase)&&!float.IsInfinity(phase)&&!float.IsNaN(rate)&&!float.IsInfinity(rate);
                    float delta=phase-previousPhase;
                    if(delta < -(float)Math.PI)delta+=(float)(2*Math.PI);
                    forward &= rate>0f&&delta>0f;
                    continuous &= delta < 1.1f*dt;
                    derivativeConsistent &= Math.Abs(delta/dt-(previousRate+rate)*.5f)<.015f;
                    minRate=Math.Min(minRate,rate);maxRate=Math.Max(maxRate,rate);
                    maxAcceleration=Math.Max(maxAcceleration,Math.Abs(rate-previousRate)/dt);
                    totalAngle+=delta;previousPhase=phase;previousRate=rate;
                }
                Require(finite&&forward,hz+"Hz: orbit advances finitely through two complete laps");
                Require(minRate>.45&&maxRate<1.0&&maxRate/minRate<1.65,
                    hz+"Hz: relaxed cruise never nearly stops or speeds up into a burst");
                Require(maxAcceleration<.20,hz+"Hz: angular acceleration stays gentle throughout the lap");
                Require(continuous&&derivativeConsistent,
                    hz+"Hz: reported velocity agrees with phase increments, including wrap and frame-rate changes");
                Require(totalAngle>=4*Math.PI-.002&&totalAngle<4*Math.PI+.06,
                    hz+"Hz: both sides are traversed once per lap without a hidden phase reset");
                Log($"PASS relaxed swim clock {hz}Hz: rate={minRate:F3}–{maxRate:F3}rad/s, max acceleration={maxAcceleration:F3}rad/s²; production clock only");
            }
            foreach(float fraction in new[]{0f,.17f,.5f,.93f})
            {
                OceanEntrySequence.SwimOrbit(fraction*lap,out float before,out float beforeRate);
                OceanEntrySequence.SwimOrbit((1f+fraction)*lap,out float after,out float afterRate);
                Require(Math.Abs(Math.Sin(before)-Math.Sin(after))<.0001&&Math.Abs(Math.Cos(before)-Math.Cos(after))<.0001&&Near(beforeRate,afterRate),
                    "continued Invite/Wiggle clock preserves position and velocity across one lap");
            }
        }

        static void PlayableCaptionBoundary()
        {
            var sequence = new OceanEntrySequence();
            bool premature = false, skipped = false;
            for (int i = 0; sequence.Current != Beat.Warp && i < 3000; i++)
            {
                premature |= sequence.ControlsAllowed;
                skipped |= sequence.DismissPlayableTitles();
                sequence.Step(1f / 90f, false);
            }
            Require(sequence.Current == Beat.Warp && !premature && !skipped,
                "dismiss/interaction cannot bypass any fish presentation or pre-ready cover");
            Require(sequence.TotalVisibleTime > 15.2f && sequence.TotalVisibleTime < 15.5f,
                "the complete calm encounter reaches Warp around 15.3 visible seconds");
            for (int i = 0; i < 600; i++) sequence.Step(1f / 60f, false);
            Require(!sequence.ControlsAllowed && !sequence.DismissPlayableTitles(),
                "arbitrarily long opaque loading never enables controls or dismisses its protection");
            sequence.Step(1f / 60f, true);
            Require(sequence.Current == Beat.Reveal && !sequence.ControlsAllowed && !sequence.DismissPlayableTitles(),
                "content-ready still requires the full reveal fade before input");
            for (int i = 0; i < 60; i++) sequence.Step(1f / 60f, true);
            Require(sequence.Current == Beat.Reveal && !sequence.ControlsAllowed,
                "one second of the 1.3-second fade remains input-protected");
            while (sequence.Current == Beat.Reveal) sequence.Step(1f / 60f, true);
            Require(sequence.Current == Beat.Welcome && sequence.ControlsAllowed && !sequence.Finished,
                "Welcome begins with controls released and captions still alive");
            Require(Near(OceanEntrySequence.DurationOf(Beat.Invite) + OceanEntrySequence.DurationOf(Beat.Wiggle), OceanEntrySequence.SwimOrbitSeconds),
                "shorter invitation plus farewell retains one full original-speed orbit");
            foreach (Beat beat in new[] { Beat.Welcome, Beat.Quiet, Beat.Chapter })
            {
                var playable = At(beat); float time = playable.TotalVisibleTime;
                Require(playable.ControlsAllowed && playable.DismissPlayableTitles() && playable.Finished &&
                    playable.ControlsAllowed && playable.TotalVisibleTime == time && !playable.DismissPlayableTitles(),
                    beat + ": a deliberate playable action ends captions once without replaying input or advancing time");
            }
            sequence.Fail();
            Require(!sequence.ControlsAllowed && !sequence.DismissPlayableTitles(), "failure never exposes playable controls");
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Swan Underwater/Validate Ocean Entry Sequence")]
#endif
        public static void RunAll()
        {
            assertions = 0;
            WorldMustBeReady();
            PlayableCaptionBoundary();
            foreach (int hz in new[] { 30, 60, 72, 90, 120 }) Trace(hz + " FPS", new[] { 1f / hz });
            Trace("constant 5-second hitches", new[] { 5f });
            Trace("mixed frame times + 2-second hitches", new[] { 1f / 120f, 1f / 30f, 2f, 1f / 72f, .2f });
            SubtitleReadability(Beat.Welcome, 2.5f);
            SubtitleReadability(Beat.Chapter, 2.1f);
            InvalidDeltasAndFailure();
            NaturalSwimClock();
            Log($"OceanEntrySequenceTests PASS: {assertions} assertions. Sequencing/readability only; rendered Warp coverage and XR behavior require integration validation.");
        }

#if OCEAN_ENTRY_TEST_MAIN
        public static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
#endif
    }
}
#endif
