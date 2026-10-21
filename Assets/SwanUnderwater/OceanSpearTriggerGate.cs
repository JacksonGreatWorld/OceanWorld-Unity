using System;

namespace SwanUnderwater
{
    /// <summary>
    /// One visible spear gun session's index-finger trigger. No Unity/SDK dependencies or ammunition ownership.
    /// indexCurl uses ShellGrabGesture.TryFingerCurl on the index finger's four joints;
    /// gripping comes independently from the middle, ring and little fingers.
    /// </summary>
    public sealed class OceanSpearTriggerGate
    {
        public const float ReleaseCurl = .12f;
        public const float PressCurl = .22f;
        public const float ReleaseStableSeconds = .08f;
        public const float PressStableSeconds = .04f;
        public const float MaximumFrameSeconds = .10f;
        public const float MaximumSampleContribution = .04f;

        public bool Held { get; private set; }
        public bool HeldLeft { get; private set; }
        public bool Armed { get; private set; }
        /// <summary>Qualified trigger inputs in this visible session, not projectile/ammo transactions.</summary>
        public int ShotsFired { get; private set; }

        bool hasFrame, hasSample;
        int lastFrame, releaseSamples, pressSamples;
        float releaseSeconds, pressSeconds;

        /// <summary>Begin a newly visible aiming session. Hidden/revealed guns must extend the index again.</summary>
        public void Acquire(bool leftHand)
        {
            Reset();
            Held = true;
            HeldLeft = leftHand;
        }

        /// <summary>End the held lifecycle. A later Step cannot begin another session.</summary>
        public void Reset()
        {
            Held = HeldLeft = false;
            ShotsFired = 0;
            hasFrame = false;
            lastFrame = 0;
            Suspend();
        }

        /// <summary>
        /// Cancel gesture qualification, preserving hand ownership and the session input count.
        /// Use on pause, transient index loss or long frames. Recovery must extend the
        /// index again. The caller separately decides when palm loss/grip release hides the gun.
        /// </summary>
        public void Suspend()
        {
            Armed = hasSample = false;
            releaseSeconds = pressSeconds = 0f;
            releaseSamples = pressSamples = 0;
        }

        /// <returns>
        /// True exactly once per deliberate extension-to-curl cycle. The caller owns the
        /// shared ammunition transaction and may reject an input when no rounds remain.
        /// frame is the current Unity frame, not a separate count for each hand.
        /// </returns>
        public bool Step(int frame, float dt, bool leftHand, bool fresh, bool gripping,
            float indexCurl, bool suppressed = false)
        {
            if (!Held || leftHand != HeldLeft) return false;
            if (suppressed || !fresh || !gripping || !Finite(dt) || dt <= 0f ||
                dt > MaximumFrameSeconds || !Finite(indexCurl) || indexCurl < 0f || indexCurl > 1f)
            {
                lastFrame = frame;
                hasFrame = true;
                Suspend();
                return false;
            }
            // A duplicate cannot spend time twice. Missed/backwards frames cannot bridge
            // a stale qualification, even when a caller forgot to explicitly Suspend.
            if (hasFrame && frame == lastFrame) return false;
            if (hasFrame && ((long)frame - lastFrame != 1L)) Suspend();
            lastFrame = frame;
            hasFrame = true;
            // dt describes time before this observation, which may predate this visible session/recovery.
            if (!hasSample) { hasSample = true; return false; }

            float elapsed = Math.Min(dt, MaximumSampleContribution);
            if (indexCurl <= ReleaseCurl)
            {
                pressSeconds = 0f;
                pressSamples = 0;
                releaseSeconds = Math.Min(ReleaseStableSeconds, releaseSeconds + elapsed);
                releaseSamples = Math.Min(2, releaseSamples + 1);
                if (releaseSamples >= 2 && releaseSeconds >= ReleaseStableSeconds - .000001f)
                    Armed = true;
                return false;
            }

            releaseSeconds = 0f;
            releaseSamples = 0;
            if (!Armed || indexCurl < PressCurl)
            {
                pressSeconds = 0f;
                pressSamples = 0;
                return false;
            }

            pressSeconds = Math.Min(PressStableSeconds, pressSeconds + elapsed);
            pressSamples = Math.Min(2, pressSamples + 1);
            if (pressSamples < 2 || pressSeconds < PressStableSeconds - .000001f) return false;
            ShotsFired++;
            Armed = false;
            pressSeconds = 0f;
            pressSamples = 0;
            return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
