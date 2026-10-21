using System;

namespace SwanUnderwater
{
    /// <summary>
    /// Adds yaw only to confirmed single-hand water strokes. Positive yaw is a right turn.
    /// Tracking visibility is a reset/arming condition, never evidence that a hand paddled.
    /// Independent of Unity/SDK so timing and recovery can be exercised with real stroke trajectories.
    /// </summary>
    public sealed class SingleHandTurnModel
    {
        public enum TurnState { AwaitingStableInput, Idle, PendingLeft, PendingRight, LeftPull, RightPull, Paired }

        public float MaximumYawSpeed = 25f;
        public float AngularAcceleration = 100f;
        public float AngularDeceleration = 150f;
        public float PairingDelay = 0.28f;
        public float RecentPartnerWindow = 0.35f;
        public float TrackingSettleSeconds = 0.20f;
        public float QuietRearmSeconds = 0.18f;
        // Preserve a perceptible turn for a real short pull that finishes during pairing.
        // The bank is filled only before confirmation, never by holding a stationary hand.
        public float MaximumDeferredTurnDegrees = 3f;
        public float YawVelocity { get; private set; }
        public TurnState State { get; private set; } = TurnState.AwaitingStableInput;

        const float MinimumPower = 0.10f;
        int trackingMask = -1, candidateSide;
        float stableTime, quietTime, candidateTime;
        float deferredTurn, peakPower;
        float leftAge = float.PositiveInfinity, rightAge = float.PositiveInfinity;
        bool armed, paired;

        public void Reset()
        {
            trackingMask = -1;
            ClearGestureState();
        }

        void ClearGestureState()
        {
            armed = paired = false;
            stableTime = quietTime = candidateTime = 0f;
            deferredTurn = peakPower = 0f;
            candidateSide = 0;
            leftAge = rightAge = float.PositiveInfinity;
            YawVelocity = 0f;
            State = TurnState.AwaitingStableInput;
        }

        /// <returns>Yaw delta in degrees for this frame. Apply it to the rig around the head, never the camera's local pose.</returns>
        public float Step(float dt, float leftStrength, float rightStrength, bool leftTracked, bool rightTracked,
            bool trackingDiscontinuity = false, bool suppressed = false)
        {
            if (!Finite(dt) || dt <= 0f || dt > 0.1f || !Finite(leftStrength) || !Finite(rightStrength))
            {
                Reset();
                return 0f;
            }
            int mask = (leftTracked ? 1 : 0) | (rightTracked ? 2 : 0);
            if (mask != trackingMask || mask == 0 || trackingDiscontinuity || suppressed)
            {
                trackingMask = mask;
                ClearGestureState();
                return 0f;
            }
            stableTime += dt;
            float left = leftTracked ? Clamp(leftStrength, 0f, 1f) : 0f;
            float right = rightTracked ? Clamp(rightStrength, 0f, 1f) : 0f;
            bool leftPull = left > MinimumPower, rightPull = right > MinimumPower;
            bool anyPull = leftPull || rightPull;
            if (!armed)
            {
                quietTime = anyPull ? 0f : quietTime + dt;
                // A stable one-hand mask is sufficient. After a visibility change, an old
                // continuing pull cannot become a turn: a quiet/recovery interval is required.
                if (stableTime >= Math.Max(0.1f, TrackingSettleSeconds) &&
                    quietTime >= Math.Max(0.12f, QuietRearmSeconds))
                {
                    armed = true;
                    quietTime = 0f;
                    State = TurnState.Idle;
                }
                return 0f;
            }

            leftAge = leftPull ? 0f : leftAge + dt;
            rightAge = rightPull ? 0f : rightAge + dt;
            if (paired)
            {
                // Preserve bilateral/alternating propulsion even if one hand finishes first.
                // A brief shared recovery rearms the next distinct gesture.
                quietTime = anyPull ? 0f : quietTime + dt;
                if (quietTime >= Math.Max(0.12f, QuietRearmSeconds))
                {
                    paired = false;
                    candidateSide = 0;
                    candidateTime = quietTime = 0f;
                    deferredTurn = peakPower = 0f;
                    leftAge = rightAge = float.PositiveInfinity;
                    State = TurnState.Idle;
                }
                YawVelocity = 0f;
                return 0f;
            }
            float pairingWindow = Math.Max(PairingDelay, RecentPartnerWindow);
            if ((leftPull && rightAge <= pairingWindow) || (rightPull && leftAge <= pairingWindow))
            {
                paired = true;
                candidateSide = 0;
                candidateTime = quietTime = 0f;
                deferredTurn = peakPower = 0f;
                YawVelocity = 0f;
                State = TurnState.Paired;
                return 0f;
            }

            float target = 0f;
            if (!anyPull && deferredTurn <= 0f)
            {
                candidateSide = 0;
                candidateTime = 0f;
                peakPower = 0f;
                State = TurnState.Idle;
            }
            else
            {
                int side = anyPull ? (leftPull ? 1 : -1) : candidateSide;
                if (anyPull && candidateSide != side)
                {
                    candidateSide = side;
                    candidateTime = 0f;
                    deferredTurn = peakPower = 0f;
                }
                float power = leftPull ? left : rightPull ? right : 0f;
                float maximumSpeed = Clamp(MaximumYawSpeed, 0f, 40f);
                float delay = Math.Max(0.18f, PairingDelay);
                if (candidateTime < delay)
                    deferredTurn = Math.Min(Clamp(MaximumDeferredTurnDegrees, 0f, 3f),
                        deferredTurn + power * maximumSpeed * dt);
                peakPower = Math.Max(peakPower, power);
                candidateTime += dt;
                bool confirmed = candidateTime >= delay;
                State = confirmed ? (side > 0 ? TurnState.LeftPull : TurnState.RightPull)
                    : (side > 0 ? TurnState.PendingLeft : TurnState.PendingRight);
                if (confirmed)
                {
                    // Live thrust remains the normal steering source. A short completed pull
                    // may spend its small finite bank, with braking as that bank runs out.
                    float deferredSpeed = Math.Min(peakPower * maximumSpeed,
                        (float)Math.Sqrt(2f * Math.Max(1f, AngularDeceleration) * deferredTurn));
                    target = side * Math.Max(power * maximumSpeed, deferredSpeed);
                }
            }
            float rate = Math.Abs(target) > Math.Abs(YawVelocity) ? AngularAcceleration : AngularDeceleration;
            YawVelocity = MoveTowards(YawVelocity, target, Math.Max(1f, rate) * dt);
            // Match the locomotion integrator's long-frame cap; no catch-up rotation burst.
            float delta = YawVelocity * Math.Min(dt, 0.05f);
            deferredTurn = Math.Max(0f, deferredTurn - Math.Abs(delta));
            return delta;
        }

        static float MoveTowards(float value, float target, float delta) =>
            Math.Abs(target - value) <= delta ? target : value + Math.Sign(target - value) * delta;
        static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
