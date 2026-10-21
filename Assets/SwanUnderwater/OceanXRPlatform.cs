using UnityEngine;
using UnityEngine.XR.Management;

namespace SwanUnderwater
{
    /// <summary>Choose platform services from the running loader, never from an installed SDK alone.</summary>
    public static class OceanXRPlatform
    {
        public static string ActiveLoaderName => XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null &&
            XRGeneralSettings.Instance.Manager.activeLoader != null
            ? XRGeneralSettings.Instance.Manager.activeLoader.GetType().FullName : string.Empty;

        public static bool IsPico => ActiveLoaderName == "ByteDance.PICO.XR.PXR_Loader";
        public static bool IsQuest => Application.platform == RuntimePlatform.Android &&
            ActiveLoaderName == "UnityEngine.XR.OpenXR.OpenXRLoader";
        public static bool IsVisionOS => ActiveLoaderName == "UnityEngine.XR.VisionOS.VisionOSLoader";

        /// <summary>Compositor readiness only; this does not prove passthrough visibility.</summary>
        public static bool VisionOSImmersiveSpaceReady
        {
            get
            {
#if UNITY_VISIONOS && !UNITY_EDITOR
                return IsVisionOS && UnityEngine.XR.VisionOS.VisionOS.IsImmersiveSpaceReady();
#else
                return false;
#endif
            }
        }

        /// <summary>The visionOS provider/ARSession requests permission; never bypass its result.</summary>
        public static bool VisionOSHandsAuthorized
        {
            get
            {
#if UNITY_VISIONOS && !UNITY_EDITOR
                return IsVisionOS && UnityEngine.XR.VisionOS.VisionOS.QueryAuthorizationStatus(
                    UnityEngine.XR.VisionOS.VisionOSAuthorizationType.HandTracking) ==
                    UnityEngine.XR.VisionOS.VisionOSAuthorizationStatus.Allowed;
#else
                return false;
#endif
            }
        }
    }
}
