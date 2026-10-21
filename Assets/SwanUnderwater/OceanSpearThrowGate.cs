using System;
using System.Numerics;

namespace SwanUnderwater
{
    /// <summary>
    /// One deliberate forward fist throw per pickup. No Unity/PXR dependency.
    /// Palm/head positions and head-forward must share stationary tracking coordinates
    /// in metres (Camera Offset inverse transform), never rotating head-local axes.
    /// The caller supplies the held hand's fresh palm and debounced grasp state.
    /// </summary>
    public sealed class OceanSpearThrowGate
    {
        public const float SettleSeconds = .18f;
        public const float SettleSpeed = .22f;
        public const float MinimumForwardSpeed = .75f;
        public const float MinimumForwardDistance = .10f;
        public const float MinimumThrowSeconds = .05f;
        public const float MaximumThrowSeconds = .60f;
        public const float MaximumSampleSeconds = .10f;
        public const float MaximumStepDistance = .16f;
        public const float MaximumTrackedSpeed = 5f;
        const float StartForwardSpeed = .35f;
        const float MinimumForwardFraction = .65f;

        public bool Held { get; private set; }
        public bool HeldLeft { get; private set; }
        public bool Armed { get; private set; }
        public bool Fired { get; private set; }

        int lastFrame = -1, throwSamples;
        bool hasSample, inThrow;
        float settleTime, throwTime;
        Vector3 previousPalm, previousHead, previousRelative;
        Vector3 throwPalm, throwRelative, throwForward;

        /// <summary>Call only after a successful new pickup; only this starts another held throw cycle.</summary>
        public void Acquire(bool leftHand)
        {
            Held = true; HeldLeft = leftHand; Fired = false;
            lastFrame = -1; ClearMotion();
        }

        /// <summary>Drop/level teardown. Does not turn a fired spear into another held throw.</summary>
        public void Reset()
        {
            Held = false; Fired = false; lastFrame = -1; ClearMotion();
        }

        /// <returns>True once per pickup; output direction uses the input tracking-space axes.</returns>
        public bool Step(int frame, float dt, bool leftHand, bool fresh, bool gripping,
            Vector3 palmPosition, Vector3 headPosition, Vector3 headForward, bool suppressed,
            out Vector3 direction, out float forwardSpeed)
        {
            direction = Vector3.Zero; forwardSpeed = 0f;
            if (!Held || Fired || leftHand != HeldLeft) return false;
            if (frame == lastFrame) return false;
            bool reversedFrame = frame < lastFrame;
            lastFrame = frame;
            if (frame < 0 || reversedFrame || suppressed || !fresh || !gripping ||
                !Finite(dt) || dt <= 0f || dt > MaximumSampleSeconds ||
                !Finite(palmPosition) || !Finite(headPosition) || !Finite(headForward))
            { ClearMotion(); return false; }

            float forwardLength = headForward.LengthSquared();
            Vector3 relative = palmPosition - headPosition;
            if (!Finite(forwardLength) || forwardLength < .000001f || !Finite(relative) || relative.LengthSquared() > 2.25f)
            { ClearMotion(); return false; }
            Vector3 facing = headForward / (float)Math.Sqrt(forwardLength);
            if (!hasSample)
            {
                previousPalm = palmPosition; previousHead = headPosition; previousRelative = relative;
                hasSample = true; return false;
            }

            Vector3 palmDelta = palmPosition - previousPalm, headDelta = headPosition - previousHead;
            Vector3 relativeDelta = relative - previousRelative;
            Vector3 lastPalm = previousPalm, lastRelative = previousRelative;
            previousPalm = palmPosition; previousHead = headPosition; previousRelative = relative;
            float palmDistance = palmDelta.Length(), headDistance = headDelta.Length(), relativeDistance = relativeDelta.Length();
            if (!Finite(palmDistance) || !Finite(headDistance) || !Finite(relativeDistance) ||
                palmDistance > MaximumStepDistance || headDistance > MaximumStepDistance || relativeDistance > MaximumStepDistance ||
                palmDistance / dt > MaximumTrackedSpeed || headDistance / dt > MaximumTrackedSpeed || relativeDistance / dt > MaximumTrackedSpeed)
            { ClearMotion(); return false; }

            Vector3 palmVelocity = palmDelta / dt, relativeVelocity = relativeDelta / dt;
            float palmSpeed = palmDistance / dt, relativeSpeed = relativeDistance / dt;
            if (!Armed)
            {
                settleTime = palmSpeed <= SettleSpeed && relativeSpeed <= SettleSpeed ? settleTime + dt : 0f;
                if (settleTime + .000001f >= SettleSeconds) Armed = true;
                return false;
            }

            if (!inThrow)
            {
                float startSpeed = Math.Min(Vector3.Dot(palmVelocity, facing), Vector3.Dot(relativeVelocity, facing));
                if (startSpeed < StartForwardSpeed ||
                    Vector3.Dot(relativeVelocity, facing) < relativeSpeed * MinimumForwardFraction) return false;
                inThrow = true; throwTime = 0f; throwSamples = 0;
                throwPalm = lastPalm; throwRelative = lastRelative; throwForward = facing;
            }

            throwTime += dt; throwSamples++;
            float rawForward = Vector3.Dot(palmVelocity, throwForward);
            float relativeForward = Vector3.Dot(relativeVelocity, throwForward);
            // A reversal, pause, long swing or large gaze change must settle again.
            // Neither a head-only movement nor whole-body translation supplies a throw.
            if (throwTime > MaximumThrowSeconds || Vector3.Dot(facing, throwForward) < .7071067f ||
                rawForward < .10f || relativeForward < .10f ||
                relativeForward < relativeSpeed * MinimumForwardFraction)
            { ClearMotion(); return false; }

            Vector3 relativeTravel = relative - throwRelative;
            float travel = Math.Min(Vector3.Dot(palmPosition - throwPalm, throwForward), Vector3.Dot(relativeTravel, throwForward));
            float speed = Math.Min(rawForward, relativeForward);
            if (throwSamples < 2 || throwTime + .000001f < MinimumThrowSeconds ||
                travel + .000001f < MinimumForwardDistance || speed < MinimumForwardSpeed) return false;
            float travelLength = relativeTravel.Length();
            if (!Finite(travelLength) || travelLength < .000001f) { ClearMotion(); return false; }
            direction = relativeTravel / travelLength; forwardSpeed = speed;
            Fired = true; Armed = false; inThrow = false;
            return true;
        }

        void ClearMotion()
        {
            Armed = hasSample = inThrow = false;
            settleTime = throwTime = 0f; throwSamples = 0;
            previousPalm = previousHead = previousRelative = Vector3.Zero;
            throwPalm = throwRelative = throwForward = Vector3.Zero;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    }
}
