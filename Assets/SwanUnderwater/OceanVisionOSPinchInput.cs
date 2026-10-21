using UnityEngine;
#if UNITY_VISIONOS
using UnityEngine.InputSystem;
using UnityEngine.XR.VisionOS;
using UnityEngine.XR.VisionOS.InputDevices;
#endif

namespace SwanUnderwater
{
    /// <summary>Native pinch intent in tracking-local coordinates; never modifies XR hand tracking flags.</summary>
    public sealed class OceanVisionOSPinchInput : System.IDisposable
    {
        public enum Phase { None, Began, Moved, Ended, Cancelled }

        /// <summary>Public SDK projection boundary shared by the device reader and deterministic consumers/tests.</summary>
        public readonly struct Sample
        {
            public readonly Phase phase;
            public readonly bool pinch, tracked;
            public readonly Vector3 position;
            public readonly Ray trackingRay;
            public Sample(Phase phase, bool pinch, bool tracked, Vector3 position, Ray trackingRay)
            { this.phase = phase; this.pinch = pinch; this.tracked = tracked; this.position = position; this.trackingRay = trackingRay; }
        }

        // Native input-device position can be offset from the wrist. Both wrists in range is ambiguous.
        public const float MaximumWristDistance = .25f;
        public const int SlotCount = 2;
        public int BeganCount { get; private set; }
        public int AcceptedBeginCount => BeganCount;
        public int MenuConsumedCount { get; private set; }
        public int WorldConsumedCount { get; private set; }

        sealed class Slot
        {
            public bool releaseSeen, active, menuConsumed, worldConsumed;
            public int matchedHand = -1, beganFrame = -1;
            public Vector3 position;
            public Ray ray;
            public void Cancel(bool released)
            {
                releaseSeen = released; active = menuConsumed = worldConsumed = false;
                matchedHand = beganFrame = -1; position = default; ray = default;
            }
        }

        readonly Slot[] slots = { new Slot(), new Slot() };
        readonly Sample[] projected = new Sample[SlotCount];
        readonly Sample[] latest = new Sample[SlotCount], pending = new Sample[SlotCount], gameplay = new Sample[SlotCount];
        readonly bool[] captureReleaseSeen = new bool[SlotCount], hasPending = new bool[SlotCount];
        readonly Phase[] observedPhase = new Phase[SlotCount];
        readonly int[] pendingFrame = new int[SlotCount];
        readonly object projectionIdentity = new object();
        object sourceIdentity;
        int sourceDeviceId;
        bool devicePresent, disposed;
#if UNITY_VISIONOS
        bool pollingEnabled, subscribed;
#endif
        int sampledFrame = int.MinValue;

        public void Poll(int frame, bool enabled, bool leftWristTracked, Vector3 leftWrist,
            bool rightWristTracked, Vector3 rightWrist)
        {
            if (disposed || frame < 0 || !enabled) { Reset(); return; }
#if UNITY_VISIONOS
            if (!subscribed) { InputSystem.onAfterUpdate += CaptureAfterInputUpdate; subscribed = true; }
            pollingEnabled = true;
            ReadSdkSamples(frame);
#else
            CaptureSamples(frame, true, false, projected);
#endif
            PollCapturedSamples(frame, enabled, leftWristTracked, leftWrist, rightWristTracked, rightWrist);
        }

#if UNITY_VISIONOS
        void CaptureAfterInputUpdate()
        { if (pollingEnabled && !disposed) ReadSdkSamples(Time.frameCount); }

        void ReadSdkSamples(int frame)
        {
            var device = InputSystem.GetDevice<VisionOSSpatialPointerDevice>();
            bool available = device != null && device.added && device.enabled && device.inputs.Count == SlotCount;
            if (available)
                for (int i = 0; i < SlotCount; i++)
                {
                    var state = device.inputs[i].ReadValue();
                    bool pinch = state.Kind == VisionOSSpatialPointerKind.DirectPinch || state.Kind == VisionOSSpatialPointerKind.IndirectPinch;
                    bool valid = state.isTracked && Finite(state.inputDevicePosition) && ValidRotation(state.inputDeviceRotation) &&
                        Finite(state.startRayOrigin) && ValidDirection(state.startRayDirection) && ValidRotation(state.interactionRayRotation);
                    projected[i] = new Sample(ToPhase(state.phase), pinch, valid, state.inputDevicePosition,
                        valid ? new Ray(state.startRayOrigin, state.interactionRayRotation * Vector3.forward) : default);
                }
            CaptureSamples(frame, true, available, projected, device, device != null ? device.deviceId : 0);
        }
#endif

        /// <summary>Input-update boundary: keep an unconsumed Began through Moved, but cancel it on termination.</summary>
        public void CaptureSamples(int frame, bool enabled, bool available, Sample[] samples, object inputSource = null, int deviceId = 0)
        {
            if (disposed || !enabled || !available || samples == null || samples.Length != SlotCount)
            { Reset(); return; }
            inputSource = inputSource ?? projectionIdentity;
            if (sourceIdentity != null && (!ReferenceEquals(sourceIdentity, inputSource) || sourceDeviceId != deviceId))
            {
#if UNITY_VISIONOS
                bool enabledPolling = pollingEnabled;
#endif
                Reset();
#if UNITY_VISIONOS
                pollingEnabled = enabledPolling;
#endif
            }
            sourceIdentity = inputSource; sourceDeviceId = deviceId;
            devicePresent = true;
            for (int i = 0; i < SlotCount; i++)
            {
                var sample = samples[i];
                latest[i] = sample;
                if (sample.phase == Phase.None || sample.phase == Phase.Ended || sample.phase == Phase.Cancelled)
                { captureReleaseSeen[i] = true; hasPending[i] = false; slots[i].Cancel(true); }
                else if (!Usable(sample))
                { captureReleaseSeen[i] = false; hasPending[i] = false; slots[i].Cancel(false); }
                else if (sample.phase == Phase.Began && observedPhase[i] != Phase.Began)
                {
                    if (captureReleaseSeen[i]) { pending[i] = sample; pendingFrame[i] = frame; hasPending[i] = true; }
                    captureReleaseSeen[i] = false;
                }
                observedPhase[i] = sample.phase;
            }
        }

        /// <summary>Gameplay boundary: an input-update edge can survive only its own or the immediately following frame.</summary>
        public void PollCapturedSamples(int frame, bool enabled, bool leftWristTracked, Vector3 leftWrist,
            bool rightWristTracked, Vector3 rightWrist)
        {
            if (disposed || !enabled || !devicePresent || frame < 0)
            { Reset(); return; }
            if (sampledFrame == frame) return; // Do not discard an edge captured later in this same gameplay frame.
            for (int i = 0; i < SlotCount; i++)
            {
                gameplay[i] = latest[i];
                if (hasPending[i])
                {
                    bool timely = frame >= pendingFrame[i] && (long)frame - pendingFrame[i] <= 1;
                    if (timely)
                    {
                        // The captured release belongs to this new gesture, including when no gameplay poll saw it.
                        slots[i].Cancel(true);
                        // Preserve the intended OS selection ray, but near grabs use the latest physical device position.
                        var current = latest[i];
                        gameplay[i] = new Sample(Phase.Began, current.pinch, current.tracked, current.position, pending[i].trackingRay);
                    }
                    else
                    {
                        slots[i].Cancel(false);
                        gameplay[i] = new Sample(Phase.Moved, gameplay[i].pinch, gameplay[i].tracked, gameplay[i].position, gameplay[i].trackingRay);
                    }
                    hasPending[i] = false;
                }
                else if (gameplay[i].phase == Phase.Began)
                    gameplay[i] = new Sample(Phase.Moved, gameplay[i].pinch, gameplay[i].tracked, gameplay[i].position, gameplay[i].trackingRay);
            }
            PollSamples(frame, enabled, true, leftWristTracked, leftWrist, rightWristTracked, rightWrist, gameplay);
        }

        /// <summary>Process translated samples once per logical frame. A negative release baseline supports frame-zero consumers; public intent queries require nonnegative current frames.</summary>
        public void PollSamples(int frame, bool enabled, bool devicePresent, bool leftWristTracked, Vector3 leftWrist,
            bool rightWristTracked, Vector3 rightWrist, Sample[] samples)
        {
            if (disposed || !enabled || !devicePresent || samples == null || samples.Length != SlotCount || frame < sampledFrame)
            { Reset(); sampledFrame = frame; return; }
            if (sampledFrame == frame) return;
            this.devicePresent = devicePresent;
            sampledFrame = frame;
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = slots[i]; var sample = samples[i];
                if (sample.phase == Phase.None || sample.phase == Phase.Ended || sample.phase == Phase.Cancelled)
                { slot.Cancel(true); continue; }
                if (!Usable(sample))
                { slot.Cancel(false); continue; }
                if (sample.phase == Phase.Began && slot.releaseSeen)
                {
                    slot.releaseSeen = false; slot.active = true; slot.menuConsumed = slot.worldConsumed = false;
                    slot.beganFrame = frame; slot.matchedHand = MatchHand(sample.position, leftWristTracked, leftWrist, rightWristTracked, rightWrist);
                    // A second pointer cannot acquire the hand already owning a live native gesture.
                    if (slot.matchedHand >= 0 && slots[1 - i].active && slots[1 - i].matchedHand == slot.matchedHand)
                        slot.matchedHand = -1;
                    BeganCount++;
                }
                if (!slot.active) continue; // Initial/recovered held input cannot manufacture a Began.
                slot.position = sample.position; slot.ray = sample.trackingRay;
                if (slot.matchedHand >= 0 && !MatchedWristCurrent(slot.matchedHand, sample.position,
                    leftWristTracked, leftWrist, rightWristTracked, rightWrist))
                    slot.matchedHand = -1; // Retain menu ownership; never reassign the gesture to the other hand.
            }
        }

        public bool TryGetMenuPress(int slot, int frame, out Ray trackingRay)
        {
            trackingRay = default;
            if (!Current(frame) || slot < 0 || slot >= SlotCount) return false;
            var input = slots[slot];
            if (!input.active || input.beganFrame != frame || input.menuConsumed || input.worldConsumed) return false;
            trackingRay = input.ray; return true;
        }

        public bool ConsumeByMenu(int slot, int frame)
        {
            if (!Current(frame) || slot < 0 || slot >= SlotCount) return false;
            var input = slots[slot];
            if (!input.active || input.menuConsumed || input.worldConsumed) return false;
            input.menuConsumed = true; MenuConsumedCount++; return true;
        }

        public bool IsConsumedByMenu(int slot, int frame) => Current(frame) && slot >= 0 && slot < SlotCount && slots[slot].menuConsumed;
        public bool HasCurrentInput(int frame) => devicePresent && Current(frame);

        public bool TryGetWorldPinch(OceanHandSide side, int frame, out bool started, out bool active, out Vector3 trackingPosition)
        {
            started = active = false; trackingPosition = default;
            if (!Current(frame) || (int)side < 0 || (int)side > 1) return false;
            foreach (var input in slots)
                if (input.active && input.matchedHand == (int)side && !input.menuConsumed && !input.worldConsumed)
                { started = input.beganFrame == frame; active = true; trackingPosition = input.position; return true; }
            return false;
        }

        public bool ConsumeWorld(OceanHandSide side, int frame)
        {
            if (!Current(frame) || (int)side < 0 || (int)side > 1) return false;
            foreach (var input in slots)
                if (input.active && input.matchedHand == (int)side && !input.menuConsumed && !input.worldConsumed)
                { input.worldConsumed = true; WorldConsumedCount++; return true; }
            return false;
        }

        /// <summary>Cancel all intent, require release again, retain diagnostic lifetime counters.</summary>
        public void Reset()
        {
            foreach (var input in slots) input.Cancel(false);
            for (int i = 0; i < SlotCount; i++)
            { captureReleaseSeen[i] = hasPending[i] = false; observedPhase[i] = Phase.None; latest[i] = pending[i] = gameplay[i] = default; }
            sampledFrame = int.MinValue; devicePresent = false; sourceIdentity = null; sourceDeviceId = 0;
#if UNITY_VISIONOS
            pollingEnabled = false;
#endif
        }

        /// <summary>Page/open/close reset: keep the gesture already owned by UI until its terminal event.</summary>
        public void ResetForMenu()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!slots[i].active || !slots[i].menuConsumed) slots[i].Cancel(false);
                captureReleaseSeen[i] = hasPending[i] = false;
            }
            // Preserve sampledFrame so another same-frame poll cannot restore the cleared world intent.
        }

        public void Dispose()
        {
            if (disposed) return;
#if UNITY_VISIONOS
            if (subscribed) { InputSystem.onAfterUpdate -= CaptureAfterInputUpdate; subscribed = false; }
#endif
            Reset(); disposed = true;
        }

        bool Current(int frame) => !disposed && frame >= 0 && sampledFrame == frame;
        static bool Usable(Sample sample) => (sample.phase == Phase.Began || sample.phase == Phase.Moved) && sample.pinch && sample.tracked &&
            Finite(sample.position) && Finite(sample.trackingRay.origin) && ValidDirection(sample.trackingRay.direction);
        static int MatchHand(Vector3 position, bool leftTracked, Vector3 left, bool rightTracked, Vector3 right)
        {
            bool nearLeft = leftTracked && Near(position, left), nearRight = rightTracked && Near(position, right);
            return nearLeft == nearRight ? -1 : nearLeft ? 0 : 1;
        }
        static bool MatchedWristCurrent(int hand, Vector3 position, bool leftTracked, Vector3 left, bool rightTracked, Vector3 right)
            => hand == 0 ? leftTracked && Near(position, left) : rightTracked && Near(position, right);
        static bool Near(Vector3 position, Vector3 wrist) => Finite(wrist) &&
            (position - wrist).sqrMagnitude <= MaximumWristDistance * MaximumWristDistance;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool ValidDirection(Vector3 direction) => Finite(direction) && Finite(direction.sqrMagnitude) && direction.sqrMagnitude > .000001f;
#if UNITY_VISIONOS
        static bool ValidRotation(Quaternion rotation)
        {
            float norm = Quaternion.Dot(rotation, rotation);
            return Finite(rotation.x) && Finite(rotation.y) && Finite(rotation.z) && Finite(rotation.w) && Finite(norm) && norm > .01f;
        }
        static Phase ToPhase(VisionOSSpatialPointerPhase phase)
        {
            switch (phase)
            {
                case VisionOSSpatialPointerPhase.Began: return Phase.Began;
                case VisionOSSpatialPointerPhase.Moved: return Phase.Moved;
                case VisionOSSpatialPointerPhase.Ended: return Phase.Ended;
                case VisionOSSpatialPointerPhase.Cancelled: return Phase.Cancelled;
                default: return Phase.None;
            }
        }
#endif
    }
}
