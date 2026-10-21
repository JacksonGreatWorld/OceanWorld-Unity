using System;

namespace SwanUnderwater
{
    /// <summary>
    /// Per-pointer hover-then-pinch qualification, independent of Unity and the tracking SDK.
    /// The caller owns ray hit testing, fresh sample validation and arbitration across pointers.
    /// </summary>
    public sealed class OceanMenuPressGate
    {
        public const float PressThreshold = .75f;
        public const float ReleaseThreshold = .35f;
        public const float HoverSeconds = .10f;
        public const float MaximumSampleSeconds = .10f;
        public bool Armed { get; private set; }

        int lastFrame = -1, hoveredTarget = -1;
        float releasedHoverSeconds;

        /// <summary>
        /// Returns targetId once on a qualified press, otherwise -1. A target ID must be
        /// nonnegative and stable across frames. strength is a finite value in [0,1].
        /// Fresh=false, invalid input, no target or interactable=false cancels qualification.
        /// Call once per pointer per frame, including frames with no hit or missing tracking.
        /// </summary>
        public int Step(int frame, float dt, bool fresh, float strength, int targetId, bool interactable)
        {
            if (frame == lastFrame) return -1;
            bool frameWentBackwards = frame < lastFrame;
            lastFrame = frame;
            if (frame < 0 || frameWentBackwards || !fresh || !interactable || targetId < 0 ||
                !Finite(dt) || dt <= 0f || dt > MaximumSampleSeconds ||
                !Finite(strength) || strength < 0f || strength > 1f)
            { Reset(); return -1; }

            if (hoveredTarget != targetId)
            {
                Reset(); hoveredTarget = targetId;
                // Arrival cannot retroactively spend dt hovering over a newly observed target.
                return -1;
            }

            if (strength <= ReleaseThreshold)
            {
                releasedHoverSeconds = Math.Min(HoverSeconds, releasedHoverSeconds + dt);
                if (releasedHoverSeconds + .000001f >= HoverSeconds) Armed = true;
                return -1;
            }

            // Mid-range input never arms the pointer. An already-qualified hover remains
            // eligible while the user gradually closes their fingers toward the press threshold.
            releasedHoverSeconds = 0f;
            if (strength < PressThreshold || !Armed) return -1;
            Armed = false;
            return targetId;
        }

        /// <summary>
        /// Clear on page changes, open/close, relocation and animation boundaries.
        /// Retain frame de-duplication so Reset cannot enable a second dispatch in the same frame.
        /// </summary>
        public void Reset()
        { hoveredTarget = -1; releasedHoverSeconds = 0f; Armed = false; }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
