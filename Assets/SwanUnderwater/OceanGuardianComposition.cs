using System;

namespace SwanUnderwater
{
    /// <summary>World-anchored reveal composition; all offsets use the saved encounter heading.</summary>
    public static class OceanGuardianComposition
    {
        public const float WingSpan = 60f, ForwardDistance = 96f, MinimumBodyGap = 20f;
        public const float OrbitSeconds = 150f, HoldSeconds = 3f, RampSeconds = 5f, MaximumBank = 3f;
        public struct Sample
        {
            public float right, forward, height, headingRight, headingForward, bank;
        }

        public static double OrbitTime(double visibleSeconds)
        {
            if (double.IsNaN(visibleSeconds) || double.IsInfinity(visibleSeconds))
                throw new ArgumentOutOfRangeException(nameof(visibleSeconds));
            double t = Math.Max(0, visibleSeconds - HoldSeconds);
            if (t >= RampSeconds) return t - RampSeconds * .5;
            double u = t / RampSeconds;
            // Integral of smoothstep: zero initial speed/acceleration and continuous full speed.
            return RampSeconds * (u * u * u - .5 * u * u * u * u);
        }

        public static Sample Evaluate(double visibleSeconds, float anchorEyeY, float observerEyeY, float verticalEnvelope)
        {
            if (!Finite(anchorEyeY) || !Finite(observerEyeY) || !Finite(verticalEnvelope) || verticalEnvelope <= 0)
                throw new ArgumentOutOfRangeException(nameof(verticalEnvelope));
            double phase = (OrbitTime(visibleSeconds) % OrbitSeconds) * (Math.PI * 2 / OrbitSeconds);
            float s = (float)Math.Sin(phase), c = (float)Math.Cos(phase);
            return new Sample {
                right = 24f * (c - 1f), forward = ForwardDistance - 16f * s,
                height = Math.Max(anchorEyeY + MinimumBodyGap + verticalEnvelope + 1.2f * (float)Math.Sin(phase * 2),
                    observerEyeY + MinimumBodyGap + verticalEnvelope),
                headingRight = -24f * s, headingForward = -16f * c, bank = s * MaximumBank
            };
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
