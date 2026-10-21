using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Keep the fish encounter Mixed, then request Full behind the opaque passage.
    /// The SwiftUI selection is diagnostic state, not proof of compositor or eye visibility.</summary>
    [DisallowMultipleComponent]
    public sealed class OceanVisionOSImmersion : MonoBehaviour
    {
        bool full, paused, focused = true, faulted;
        int lastRequested = -1, lastSelection = -1;
        double nextCheck;

        public static OceanVisionOSImmersion Begin(Camera camera)
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            if (camera == null) return null;
            var owner = camera.GetComponent<OceanVisionOSImmersion>();
            if (owner == null) owner = camera.gameObject.AddComponent<OceanVisionOSImmersion>();
            owner.focused = Application.isFocused;
            owner.full = false;
            owner.lastRequested = -1;
            owner.nextCheck = 0;
            return owner;
#else
            return null;
#endif
        }

        public void EnterOcean()
        {
            // Called only after the entry has submitted an opaque warp frame. The owner
            // lives on the tracked camera, so destroying optional captions cannot undo Full.
            full = true;
            nextCheck = 0;
            Reconcile();
        }

        void Update()
        {
            if (Time.realtimeSinceStartupAsDouble < nextCheck) return;
            nextCheck = Time.realtimeSinceStartupAsDouble + .5;
            Reconcile();
        }

        void Reconcile()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            if (faulted || paused || !focused || !OceanXRPlatform.IsVisionOS ||
                !OceanXRPlatform.VisionOSImmersiveSpaceReady) return;
            try
            {
                int desired = full ? 1 : 0;
                if (lastRequested != desired || OceanVisionOS_GetFullImmersionRequested() != desired)
                {
                    OceanVisionOS_SetFullImmersion(desired);
                    lastRequested = desired;
                    Debug.Log($"[OceanImmersion] requested={(full ? "Full" : "Mixed")} frame={Time.frameCount}; native SwiftUI request, not visual acceptance");
                }
                int selection = OceanVisionOS_GetFullImmersionSelection();
                if (selection != lastSelection)
                {
                    lastSelection = selection;
                    Debug.Log($"[OceanImmersion] swiftui_selection={(selection == 1 ? "Full" : "Mixed")} frame={Time.frameCount}; system presentation unverified");
                }
            }
            catch (Exception exception)
            {
                faulted = true;
                Debug.LogError("[OceanImmersion] native_bridge_failed: " + exception.Message);
            }
#endif
        }

        void OnApplicationPause(bool value) { paused = value; if (!value) nextCheck = 0; }
        void OnApplicationFocus(bool value) { focused = value; if (value) nextCheck = 0; }
        // Do not reopen an immersive space or force Mixed when the OS suspends the app.
        // SwiftUI owns presentation, and its retained selection survives temporary focus loss.

#if UNITY_VISIONOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void OceanVisionOS_SetFullImmersion(int full);
        [DllImport("__Internal")] static extern int OceanVisionOS_GetFullImmersionRequested();
        [DllImport("__Internal")] static extern int OceanVisionOS_GetFullImmersionSelection();
#endif
    }
}
