using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>
    /// Allocation-free state for the 1,200 instanced school fish. Steering rotates the
    /// anatomical +Z nose first; translation never slides along an unmade turn.
    /// The shader extrapolates a continuous tail clock between staggered MPB uploads.
    /// </summary>
    public static class OceanSchoolFishMotion
    {
        public const float MaximumPitch = 38f, MaximumBank = 12f;
        public const float CruiseTurnRate = 165f, EscapeTurnRate = 255f;
        public const float MaximumStep = .05f, MaximumClockExtrapolation = .30f;

        public struct State
        {
            public float yaw, pitch, bank, speed, phase, cadence, stroke, turn;
            public float clockTimestamp, clockRate;
            public bool clockReady;
        }

        public static State Create(Vector3 heading, float phase, float cadence)
        {
            if (heading.sqrMagnitude < .00001f) heading = Vector3.forward;
            return new State
            {
                yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg,
                pitch = Mathf.Clamp(-Mathf.Atan2(heading.y, new Vector2(heading.x, heading.z).magnitude) * Mathf.Rad2Deg,
                    -MaximumPitch, MaximumPitch),
                phase = phase, cadence = Mathf.Max(.3f, cadence), stroke = .55f
            };
        }

        public static Vector3 Step(ref State state, Vector3 desiredVelocity, float bodyLength,
            bool escaping, float elapsed, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(state.pitch, state.yaw, state.bank);
            float dt = BoundedDelta(elapsed);
            if (dt <= 0f || !Finite(desiredVelocity)) return Vector3.zero;
            float wantedSpeed = desiredVelocity.magnitude;
            float targetYaw = state.yaw, targetPitch = 0f;
            if (wantedSpeed > .001f)
            {
                // Pure rise/dive (for example a reef clearance request) has no
                // horizontal bearing. Keep the animal's current heading instead
                // of atan2(0,0) forcing every school fish to face world north.
                if (desiredVelocity.x * desiredVelocity.x + desiredVelocity.z * desiredVelocity.z > .000001f)
                    targetYaw = Mathf.Atan2(desiredVelocity.x, desiredVelocity.z) * Mathf.Rad2Deg;
                targetPitch = Mathf.Clamp(-Mathf.Atan2(desiredVelocity.y,
                    new Vector2(desiredVelocity.x, desiredVelocity.z).magnitude) * Mathf.Rad2Deg,
                    -MaximumPitch, MaximumPitch);
            }
            float previousYaw = state.yaw;
            state.yaw = Mathf.MoveTowardsAngle(state.yaw, targetYaw,
                dt * (escaping ? EscapeTurnRate : CruiseTurnRate));
            state.pitch = Mathf.MoveTowards(state.pitch, targetPitch, dt * (escaping ? 105f : 68f));
            float yawRate = Mathf.DeltaAngle(previousYaw, state.yaw) / dt;
            state.turn = Mathf.Lerp(state.turn, Mathf.Clamp(yawRate / 160f, -1f, 1f), 1f - Mathf.Exp(-dt * 12f));
            state.bank = Mathf.MoveTowards(state.bank, Mathf.Clamp(-yawRate * .055f, -MaximumBank, MaximumBank), dt * 65f);
            rotation = Quaternion.Euler(state.pitch, state.yaw, state.bank);

            float error = Mathf.Abs(Mathf.DeltaAngle(state.yaw, targetYaw));
            float alignment = error >= 85f ? 0f : Mathf.Pow(Mathf.Max(0f, Mathf.Cos(error * Mathf.Deg2Rad)), 2f);
            float targetSpeed = wantedSpeed * alignment;
            state.speed = Mathf.MoveTowards(state.speed, targetSpeed, dt * (targetSpeed < state.speed ? 5.5f : escaping ? 5f : 2.8f));
            state.speed = Mathf.Min(state.speed, wantedSpeed * alignment);
            float effort = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, 8f,
                state.speed / Mathf.Max(.08f, bodyLength)));
            state.stroke = Mathf.Lerp(state.stroke, Mathf.Lerp(.50f, 1.23f, effort), 1f - Mathf.Exp(-dt * 8f));
            return rotation * Vector3.forward * state.speed;
        }

        /// <summary>
        /// Rebase on exactly the old GPU prediction, not simulation dt. Thus a .2s
        /// frame, a two-second pause or a changed cadence cannot rewind the tail on
        /// the next staggered upload. CPU steering time is independently bounded.
        /// </summary>
        public static Vector4 CaptureShaderClock(ref State state, float bodyLength, float timestamp, out Vector4 motion)
        {
            if (!Finite(timestamp)) timestamp = state.clockTimestamp;
            if (state.clockReady)
                state.phase = Mathf.Repeat(PredictShaderPhase(state.phase,
                    new Vector4(state.clockTimestamp, state.clockRate, MaximumClockExtrapolation, 0f), timestamp), Mathf.PI * 20f);
            state.clockTimestamp = timestamp;
            state.clockReady = true;
            float effort = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, 8f,
                state.speed / Mathf.Max(.08f, bodyLength)));
            state.clockRate = state.cadence * Mathf.Lerp(.72f, 2.15f, effort);
            motion = new Vector4(state.phase, state.stroke, state.turn, 2f);
            return new Vector4(timestamp, state.clockRate, MaximumClockExtrapolation, 0f);
        }

        // Keep this formula identical to FishSwim.shader mode 2.
        public static float PredictShaderPhase(float phase, Vector4 clock, float timestamp)
            => phase + Mathf.Clamp(timestamp - clock.x, 0f, Mathf.Max(0f, clock.z)) * clock.y;

        public static float BoundedDelta(float elapsed) => Finite(elapsed) && elapsed > 0f ? Mathf.Min(elapsed, MaximumStep) : 0f;

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
