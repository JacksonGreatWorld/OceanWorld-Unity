using System;
using System.Numerics;

namespace SwanUnderwater
{
    /// <summary>
    /// Deterministic hand-paddle recognition, independent of Unity and the tracking SDK.
    /// All positions and directions must use the same stationary tracking-space coordinates.
    /// Artificial rig movement must never be included in palm velocity.
    /// </summary>
    public sealed class WaterStrokeModel
    {
        /// <summary>Last recognition decision for one hand; diagnostics only, never a motion input.</summary>
        public enum PaddleDecision
        {
            TrackingUnavailable = 0,
            FirstSample = 1,
            Discontinuity = 2,
            InteractionSuppressed = 3,
            Stabilizing = 4,
            AboveZone = 5,
            BelowZone = 6,
            BehindZone = 7,
            BeyondReach = 8,
            LateralZone = 9,
            Recovery = 10,
            VerticalOnly = 11,
            TooSlow = 12,
            PullTooShort = 13,
            Power = 14,
            InvalidFrame = 15,
            UpperIntent = 16
        }

        public struct HandSample
        {
            public bool Tracked;
            public bool InteractionSuppressed;
            public Vector3 Position;

            public HandSample(bool tracked, Vector3 position, bool interactionSuppressed = false)
            {
                Tracked = tracked;
                Position = position;
                InteractionSuppressed = interactionSuppressed;
            }
        }

        public struct Output
        {
            public Vector3 LeftVelocity, RightVelocity, Direction;
            public float LeftStrength, RightStrength;
            public bool TrackingLost, NoTrackedHands;
            public int NewStrokes;
            public float Strength => Math.Min(1.5f, LeftStrength + RightStrength);
        }

        private sealed class Paddle
        {
            public PaddleDecision Decision;
            public bool HasSample, Tracked, InSegment, Recognized, UpwardSegment;
            public float StableTime, SegmentTime, QuietTime;
            public Vector3 PreviousPosition, PreviousHeadPosition, Velocity, RelativeVelocity;
            public Vector3 SegmentOrigin, SegmentHeadPosition, Forward;

            public void Reset()
            {
                Decision = PaddleDecision.TrackingUnavailable;
                HasSample = Tracked = InSegment = Recognized = UpwardSegment = false;
                StableTime = SegmentTime = QuietTime = 0f;
                PreviousPosition = PreviousHeadPosition = Velocity = RelativeVelocity = Vector3.Zero;
                SegmentOrigin = SegmentHeadPosition = Vector3.Zero;
            }

            public void EndSegment()
            {
                InSegment = Recognized = UpwardSegment = false;
                SegmentTime = QuietTime = 0f;
            }
        }

        private readonly Paddle left = new Paddle();
        private readonly Paddle right = new Paddle();
        public float StableTrackingSeconds = 0.2f;
        public float MinimumPullDistance = 0.04f;
        public float MinimumPullSpeed = 0.10f;
        public int LeftStrokeCount { get; private set; }
        public int RightStrokeCount { get; private set; }
        public int CompletedStrokeCount => LeftStrokeCount + RightStrokeCount;
        public double TrackingSampleSeconds { get; private set; }
        public double LeftTrackedSeconds { get; private set; }
        public double RightTrackedSeconds { get; private set; }
        public PaddleDecision LeftDecision => left.Decision;
        public PaddleDecision RightDecision => right.Decision;

        /// <summary>Reset gesture history without erasing session diagnostic counters.</summary>
        public void Reset()
        {
            left.Reset();
            right.Reset();
        }

        public Output Step(float dt, HandSample leftSample, HandSample rightSample,
            Vector3 headPosition, Vector3 headForward, bool seated = false)
        {
            var output = new Output();
            if (!Finite(dt) || dt <= 0f || dt > 0.1f || !Finite(headPosition) || !Finite(headForward))
            {
                Reset();
                output.TrackingLost = output.NoTrackedHands = true;
                left.Decision = right.Decision = PaddleDecision.InvalidFrame;
                return output;
            }
            leftSample.Tracked &= Finite(leftSample.Position);
            rightSample.Tracked &= Finite(rightSample.Position);
            TrackingSampleSeconds += dt;
            if (leftSample.Tracked) LeftTrackedSeconds += dt;
            if (rightSample.Tracked) RightTrackedSeconds += dt;
            output.TrackingLost = (left.Tracked && !leftSample.Tracked) || (right.Tracked && !rightSample.Tracked);
            output.NoTrackedHands = !leftSample.Tracked && !rightSample.Tracked;
            int before = CompletedStrokeCount;
            Vector3 leftDirection, rightDirection;
            bool leftStarted, rightStarted;
            output.LeftStrength = StepHand(left, leftSample, -1f, dt, headPosition, headForward,
                seated, out leftDirection, out leftStarted);
            output.RightStrength = StepHand(right, rightSample, 1f, dt, headPosition, headForward,
                seated, out rightDirection, out rightStarted);
            output.TrackingLost |= (leftSample.Tracked && !left.Tracked) || (rightSample.Tracked && !right.Tracked);
            // A discontinuous hand is unusable even if the SDK marked its sample tracked.
            // Stop translation only when neither hand remains usable; one lost hand
            // must not clear the other hand's accumulated paddle propulsion.
            output.NoTrackedHands = !left.Tracked && !right.Tracked;
            if (leftStarted) LeftStrokeCount++;
            if (rightStarted) RightStrokeCount++;
            output.NewStrokes = CompletedStrokeCount - before;
            output.LeftVelocity = left.Velocity;
            output.RightVelocity = right.Velocity;
            output.Direction = Normalized(leftDirection * output.LeftStrength + rightDirection * output.RightStrength);
            return output;
        }

        private float StepHand(Paddle paddle, HandSample sample, float side, float dt,
            Vector3 headPosition, Vector3 headForward, bool seated,
            out Vector3 direction, out bool started)
        {
            direction = Vector3.Zero;
            started = false;
            if (!sample.Tracked)
            {
                paddle.Reset();
                paddle.Decision = PaddleDecision.TrackingUnavailable;
                return 0f;
            }
            paddle.Tracked = true;
            if (!paddle.HasSample)
            {
                paddle.PreviousPosition = sample.Position;
                paddle.PreviousHeadPosition = headPosition;
                paddle.HasSample = true;
                paddle.Decision = PaddleDecision.FirstSample;
                return 0f;
            }
            Vector3 delta = sample.Position - paddle.PreviousPosition;
            Vector3 headDelta = headPosition - paddle.PreviousHeadPosition;
            paddle.PreviousPosition = sample.Position;
            paddle.PreviousHeadPosition = headPosition;
            // Reject discontinuities instead of converting reacquisition/teleport into thrust.
            if (delta.LengthSquared() > 0.18f * 0.18f || delta.LengthSquared() / (dt * dt) > 25f)
            {
                paddle.Reset();
                paddle.Decision = PaddleDecision.Discontinuity;
                return 0f;
            }
            Vector3 rawVelocity = delta / dt;
            float smoothing = 1f - (float)Math.Exp(-dt / 0.055f);
            paddle.Velocity += (rawVelocity - paddle.Velocity) * smoothing;
            paddle.RelativeVelocity += ((delta - headDelta) / dt - paddle.RelativeVelocity) * smoothing;
            // Interaction suppression is not tracking loss. Keep the already-observed
            // stable tracking interval, so a newly released paddle can start its fresh
            // segment immediately rather than losing another .2 seconds of that pull.
            paddle.StableTime = Math.Min(Math.Max(.1f, StableTrackingSeconds), paddle.StableTime + dt);
            if (sample.InteractionSuppressed)
            {
                paddle.EndSegment();
                paddle.Decision = PaddleDecision.InteractionSuppressed;
                return 0f;
            }
            if (paddle.StableTime < Math.Max(0.1f, StableTrackingSeconds))
            {
                paddle.Decision = PaddleDecision.Stabilizing;
                return 0f;
            }

            Vector3 relativePalm = sample.Position - headPosition;
            Vector3 previousRelativePalm = relativePalm - delta + headDelta;
            // An unqualified high pointing/retraction segment cannot spend its old
            // distance immediately when the hand is lowered into the chest zone.
            // A high stroke that already qualified stays one continuous stroke.
            if (paddle.InSegment && !paddle.Recognized && relativePalm.Y <= -.14f && previousRelativePalm.Y > -.14f)
                paddle.EndSegment();
            Vector3 gazeHorizontal = Normalized(new Vector3(headForward.X, 0f, headForward.Z));
            if (gazeHorizontal.LengthSquared() < 0.01f) gazeHorizontal = Vector3.UnitZ;
            Vector3 forward = paddle.InSegment ? paddle.Forward : gazeHorizontal;
            Vector3 rightward = new Vector3(forward.Z, 0f, -forward.X);
            float reach = Vector3.Dot(relativePalm, forward);
            // Natural breaststroke can start near eye height. In this upper region,
            // qualify a net outward sweep below so pointing/retraction alone cannot swim.
            // True overhead gestures remain outside the zone.
            if (relativePalm.Y > 0.12f || relativePalm.Y < -1.15f ||
                reach < -0.3f || reach > 1.05f || Math.Abs(Vector3.Dot(relativePalm, rightward)) > 1.05f)
            {
                paddle.EndSegment();
                paddle.Decision = relativePalm.Y > 0.12f ? PaddleDecision.AboveZone :
                    relativePalm.Y < -1.15f ? PaddleDecision.BelowZone :
                    reach < -0.3f ? PaddleDecision.BehindZone :
                    reach > 1.05f ? PaddleDecision.BeyondReach : PaddleDecision.LateralZone;
                return 0f;
            }
            float backward = -Vector3.Dot(paddle.Velocity, forward);
            float outward = Vector3.Dot(paddle.Velocity, rightward) * side;
            float horizontalSpeed = (float)Math.Sqrt(backward * backward + outward * outward);
            float productiveSpeed = Math.Max(0f, backward) + 0.7f * Math.Max(0f, outward);
            float relativeProductiveSpeed = Math.Max(0f, -Vector3.Dot(paddle.RelativeVelocity, forward)) +
                0.7f * Math.Max(0f, Vector3.Dot(paddle.RelativeVelocity, rightward) * side);
            float downSpeed = Math.Min(-paddle.Velocity.Y, -paddle.RelativeVelocity.Y);
            bool upwardPush = headForward.Y >= .55f && downSpeed >= Math.Max(.10f, MinimumPullSpeed);
            if (upwardPush)
            {
                productiveSpeed = Math.Max(productiveSpeed, downSpeed * .85f);
                relativeProductiveSpeed = Math.Max(relativeProductiveSpeed, downSpeed * .85f);
            }
            // Require motion both in tracking space and relative to the head: neither walking
            // backwards with still arms nor moving only the head should pump the virtual swimmer.
            productiveSpeed = Math.Min(productiveSpeed, relativeProductiveSpeed);
            bool recovering = backward < -0.12f || (outward < -0.14f && backward < MinimumPullSpeed);
            bool raisingHand = Math.Abs(paddle.Velocity.Y) > 1.6f * (horizontalSpeed + 0.03f);
            if ((recovering || raisingHand) && !upwardPush)
            {
                paddle.EndSegment();
                paddle.Decision = recovering ? PaddleDecision.Recovery : PaddleDecision.VerticalOnly;
                return 0f;
            }
            if (productiveSpeed < Math.Max(0.06f, MinimumPullSpeed))
            {
                paddle.QuietTime += dt;
                if (paddle.QuietTime > 0.10f) paddle.EndSegment();
                paddle.Decision = PaddleDecision.TooSlow;
                return 0f;
            }
            if (!paddle.InSegment)
            {
                paddle.InSegment = true;
                paddle.UpwardSegment = upwardPush;
                paddle.Forward = gazeHorizontal;
                paddle.SegmentOrigin = sample.Position - delta;
                paddle.SegmentHeadPosition = headPosition - headDelta;
                paddle.SegmentTime = 0f;
                forward = gazeHorizontal;
                rightward = new Vector3(forward.Z, 0f, -forward.X);
            }
            paddle.QuietTime = 0f;
            paddle.SegmentTime += dt;
            Vector3 net = sample.Position - paddle.SegmentOrigin;
            float workDistance = Math.Max(0f, -Vector3.Dot(net, forward)) +
                                 0.7f * Math.Max(0f, Vector3.Dot(net, rightward) * side);
            Vector3 relativeNet = net - (headPosition - paddle.SegmentHeadPosition);
            float relativeDistance = Math.Max(0f, -Vector3.Dot(relativeNet, forward)) +
                                    0.7f * Math.Max(0f, Vector3.Dot(relativeNet, rightward) * side);
            if (paddle.UpwardSegment)
            {
                workDistance = Math.Max(workDistance, Math.Max(0f, -net.Y) * .85f);
                relativeDistance = Math.Max(relativeDistance, Math.Max(0f, -relativeNet.Y) * .85f);
            }
            workDistance = Math.Min(workDistance, relativeDistance);
            float outwardDistance = Math.Min(Math.Max(0f, Vector3.Dot(net, rightward) * side),
                Math.Max(0f, Vector3.Dot(relativeNet, rightward) * side));
            if (relativePalm.Y > -.14f && outwardDistance < .025f &&
                !(paddle.UpwardSegment && upwardPush))
            {
                paddle.Decision = PaddleDecision.UpperIntent;
                return 0f;
            }
            // Net displacement within this continuous segment prevents tiny oscillations accumulating.
            float requiredDistance = Math.Max(0.02f, MinimumPullDistance) * (seated ? 0.75f : 1f);
            if (!paddle.Recognized && workDistance >= requiredDistance && paddle.SegmentTime >= 0.09f)
            {
                paddle.Recognized = true;
                started = true;
            }
            if (!paddle.Recognized)
            {
                // This includes a pull that has not yet met its minimum continuous duration.
                paddle.Decision = PaddleDecision.PullTooShort;
                return 0f;
            }

            Vector3 reaction = Normalized(new Vector3(-paddle.Velocity.X, 0f, -paddle.Velocity.Z));
            Vector3 travelHorizontal = Normalized(gazeHorizontal * 0.75f + reaction * 0.25f);
            // Gaze pitch and the opposite paddle motion combine for gentle ascent/descent.
            // This changes locomotion only; the real headset pose is never altered.
            float gazeY = Clamp(headForward.Y, -0.7f, 0.7f);
            if (Math.Abs(gazeY) < 0.12f) gazeY = 0f;
            float paddleY = Clamp(-paddle.Velocity.Y / Math.Max(0.15f, horizontalSpeed), -0.8f, 0.8f);
            float vertical = Clamp(gazeY * 0.75f + paddleY * 0.30f, -0.65f, 0.65f);
            direction = Normalized(travelHorizontal + Vector3.UnitY * vertical);
            if (paddle.UpwardSegment && upwardPush)
                direction = Normalized(Normalized(headForward) * .75f + Vector3.UnitY * .35f + reaction * .10f);
            paddle.Decision = PaddleDecision.Power;
            return Clamp((productiveSpeed - 0.04f) / 0.60f, 0f, 1f);
        }

        /// <summary>Continuous paddle acceleration and passive water drag, bounded in m/s.</summary>
        public static Vector3 AdvanceVelocity(Vector3 velocity, Output paddle, float dt,
            float cruiseSpeed, float maximumSpeed, float drag)
        {
            if (!Finite(dt) || dt <= 0f || dt > 0.1f || paddle.NoTrackedHands) return Vector3.Zero;
            float damping = (float)Math.Exp(-Math.Max(0.1f, drag) * dt);
            velocity *= damping;
            // Water resistance rises with paddle speed: an easy slow sweep should not instantly
            // reach the same cruising speed as a strong fast pull.
            float effort = paddle.Strength;
            velocity += paddle.Direction * (effort * effort * Math.Max(0.1f, cruiseSpeed) * 2f * dt);
            return ClampMagnitude(velocity, Math.Max(0.1f, maximumSpeed));
        }

        private static Vector3 ClampMagnitude(Vector3 value, float maximum)
        {
            float length = value.Length();
            return length > maximum ? value * (maximum / length) : value;
        }

        private static Vector3 Normalized(Vector3 value)
        {
            float length = value.Length();
            return length > 0.00001f ? value / length : Vector3.Zero;
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
