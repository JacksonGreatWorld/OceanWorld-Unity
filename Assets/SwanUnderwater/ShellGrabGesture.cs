using System;
using System.Numerics;

namespace SwanUnderwater
{
    /// <summary>
    /// One hand's deliberate fist gesture, independent of Unity/PXR and target selection.
    /// All finger points use the same coordinate space and metre units. Thumb/index are
    /// intentionally absent: a pinch with the other three fingers open is not a grasp.
    /// </summary>
    public sealed class ShellGrabGesture
    {
        public const float CloseCurl = 0.22f;
        public const float ReleaseCurl = 0.12f;
        public const float CloseStableSeconds = 0.10f;
        public const float OpenStableSeconds = 0.08f;
        public const float BriefFingerGapSeconds = 0.08f;

        public struct Finger
        {
            public Vector3 Proximal, Intermediate, Distal, Tip;
            public Finger(Vector3 proximal, Vector3 intermediate, Vector3 distal, Vector3 tip)
            {
                Proximal = proximal; Intermediate = intermediate; Distal = distal; Tip = tip;
            }
        }

        public struct HandSample
        {
            public bool Tracked;
            public Finger Middle, Ring, Little;
            public HandSample(bool tracked, Finger middle, Finger ring, Finger little)
            {
                Tracked = tracked; Middle = middle; Ring = ring; Little = little;
            }
        }

        public bool Active { get; private set; }
        public bool StartedThisStep { get; private set; }
        public bool Armed { get; private set; }
        public bool ValidThisStep { get; private set; }
        /// <summary>
        /// Blocks only this hand's paddle input from the first closed candidate sample.
        /// After a known fist loses finger data, hold this guard until fresh valid data.
        /// Palm tracking/validity remains the locomotion controller's separate concern.
        /// </summary>
        public bool SuppressStroke { get; private set; }
        float closeTime, openTime, releaseTime, fingerGapTime;

        /// <summary>Pause/disable reset. A subsequent valid open pose must rearm the gesture.</summary>
        public void Reset()
        {
            Active = StartedThisStep = Armed = SuppressStroke = ValidThisStep = false;
            closeTime = openTime = releaseTime = fingerGapTime = 0f;
        }

        /// <returns>True once, when a newly closed fist passes the stability interval.</returns>
        public bool Step(float dt, HandSample sample, bool interactionSuppressed = false, bool palmStillTracked = false)
        {
            StartedThisStep = ValidThisStep = false;
            if (interactionSuppressed) { Reset(); return false; }
            if (!Finite(dt) || dt <= 0f || dt > 0.1f || !sample.Tracked ||
                !TryFingerCurl(sample.Middle, out float middle) ||
                !TryFingerCurl(sample.Ring, out float ring) ||
                !TryFingerCurl(sample.Little, out float little))
            {
                // A briefly occluded fingertip must not erase an already verified open hand.
                // No missing/inferred joint can confirm a grab or advance its stability timer.
                if (palmStillTracked && Finite(dt) && dt > 0f && dt <= .1f && (Armed || Active))
                {
                    fingerGapTime += dt;
                    closeTime = openTime = releaseTime = 0f;
                    if (fingerGapTime <= BriefFingerGapSeconds) return false;
                }
                bool heldClosed = SuppressStroke;
                Reset();
                SuppressStroke = heldClosed;
                return false;
            }

            ValidThisStep = true;
            fingerGapTime = 0f;

            float minimum = Math.Min(middle, Math.Min(ring, little));
            float maximum = Math.Max(middle, Math.Max(ring, little));
            bool closed = minimum >= CloseCurl;
            bool allOpen = maximum <= ReleaseCurl;
            bool anyOpen = minimum <= ReleaseCurl;
            openTime = allOpen ? Math.Min(OpenStableSeconds, openTime + dt) : 0f;
            releaseTime = anyOpen ? Math.Min(OpenStableSeconds, releaseTime + dt) : 0f;

            if (Active && releaseTime >= OpenStableSeconds)
            {
                Active = Armed = false;
                closeTime = 0f;
            }
            if (!Active)
            {
                // Initial acquisition and reacquisition cannot turn an old closed fist
                // into a new event. Rearming requires all three fingers visibly released.
                if (openTime >= OpenStableSeconds) Armed = true;
                closeTime = Armed && closed ? Math.Min(CloseStableSeconds, closeTime + dt) : 0f;
                if (closeTime >= CloseStableSeconds)
                {
                    Active = StartedThisStep = true;
                    Armed = false;
                    closeTime = 0f;
                }
            }
            // Releasing a required finger ends a grasp without needing every finger to
            // become perfectly straight. Do not keep a separate invisible paddle latch
            // after that release. Pickup rearming still requires all three fingers open.
            SuppressStroke = Active || closed;
            return StartedThisStep;
        }

        /// <summary>
        /// Dimensionless curl = 1 - MCP-to-tip chord / three phalanx lengths.
        /// Straight is near zero; folding joints raises curl. Rigid motion, hand side,
        /// and ordinary hand-size variation cannot change this ratio.
        /// </summary>
        public static bool TryFingerCurl(Finger finger, out float curl)
        {
            curl = 0f;
            if (!Finite(finger.Proximal) || !Finite(finger.Intermediate) ||
                !Finite(finger.Distal) || !Finite(finger.Tip)) return false;
            float a = Vector3.Distance(finger.Proximal, finger.Intermediate);
            float b = Vector3.Distance(finger.Intermediate, finger.Distal);
            float c = Vector3.Distance(finger.Distal, finger.Tip);
            // Collapsed/default joints and gross coordinate errors must not look like a fist.
            if (!Finite(a) || !Finite(b) || !Finite(c) ||
                a < .0015f || b < .0015f || c < .0015f ||
                a > .12f || b > .12f || c > .12f) return false;
            float length = a + b + c;
            if (length < .025f || length > .25f) return false;
            float chord = Vector3.Distance(finger.Proximal, finger.Tip);
            if (!Finite(chord)) return false;
            curl = Math.Max(0f, Math.Min(1f, 1f - chord / length));
            return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
