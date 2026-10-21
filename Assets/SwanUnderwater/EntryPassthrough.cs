using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace SwanUnderwater
{
    /// <summary>
    /// Owns the entry's temporary camera/VST request. A successful request or runtime
    /// blend-mode event does not prove that passthrough is visible in either eye.
    /// The caller owns its culling mask, entry visuals, and the transition timing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntryPassthrough : MonoBehaviour, IDisposable
    {
        static EntryPassthrough owner;
        Camera view;
        UniversalAdditionalCameraData cameraData;
        CameraClearFlags originalClear;
        Color originalBackground;
        bool originalHDR, originalPostProcessing, originalHDROutput;
        bool originalStopNaN, originalDithering, originalVst;
        AntialiasingMode originalAntialiasing;
        bool initialized, applied, closed, paused, focused = true;
        bool wantsStart, statusSubscribed, requestTouched;
        ARCameraManager questCamera;
        bool questCameraOwned, questCameraOriginalEnabled, questRequestTouched;
        bool visionRequestTouched;
#if (UNITY_ANDROID || UNITY_VISIONOS) && !UNITY_EDITOR
        XRDisplaySubsystem activeDisplay;
#endif

        /// <summary>Local request state only; this is not a headset visibility test.</summary>
        public bool IsActive => applied && (visionRequestTouched ? VisionRequestActive() : questRequestTouched
            ? questCamera != null && questCamera.enabled && questCamera.subsystem != null && questCamera.subsystem.running
            : ReadVstRequest());
        public bool HasRuntimeStatus { get; private set; }
        /// <summary>Latest SDK environment-blend event, not a camera-image/eye-buffer test.</summary>
        public bool RuntimeReportsEnabled { get; private set; }
        public string LastFailure { get; private set; }

        public static EntryPassthrough Begin(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (owner != null) throw new InvalidOperationException("An entry passthrough owner already exists.");
            var host = new GameObject("Entry passthrough state");
            host.transform.SetParent(camera.transform, false);
            var entry = host.AddComponent<EntryPassthrough>();
            owner = entry;
            entry.view = camera;
            entry.cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            entry.originalClear = camera.clearFlags;
            entry.originalBackground = camera.backgroundColor;
            entry.originalHDR = camera.allowHDR;
            entry.originalVst = ReadVstRequest();
            if (entry.cameraData != null)
            {
                entry.originalPostProcessing = entry.cameraData.renderPostProcessing;
                entry.originalHDROutput = entry.cameraData.allowHDROutput;
                entry.originalAntialiasing = entry.cameraData.antialiasing;
                entry.originalStopNaN = entry.cameraData.stopNaN;
                entry.originalDithering = entry.cameraData.dithering;
            }
            entry.focused = Application.isFocused;
            entry.initialized = true;
            entry.TryStart();
            return entry;
        }

        /// <summary>
        /// Retry after XR initialization if needed. Unsupported providers and Editor return false
        /// without changing the camera. Restore permanently ends this lease.
        /// </summary>
        public bool TryStart()
        {
            if (!initialized || closed) return false;
            wantsStart = true;
            if (IsActive) return true;
            if (paused || !focused || !isActiveAndEnabled) return Fail("Application is paused, unfocused, or entry is disabled.");
            if (view == null || !view.isActiveAndEnabled) return Fail("Entry camera is unavailable.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (OceanXRPlatform.IsQuest) return TryStartQuest();
#endif
#if UNITY_VISIONOS && !UNITY_EDITOR
            if (OceanXRPlatform.IsVisionOS) return TryStartVisionOS();
#endif
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
            if (!OceanXRPlatform.IsPico) return Fail("Waiting for a supported XR loader to start.");
            var manager = FindFirstObjectByType<PXR_Manager>();
            if (manager == null || !manager.isActiveAndEnabled) return Fail("No active PXR_Manager in the scene.");
            var xr = XRGeneralSettings.Instance;
            var loader = xr != null && xr.Manager != null ? xr.Manager.activeLoader as PXR_Loader : null;
            var display = loader != null ? loader.GetLoadedSubsystem<XRDisplaySubsystem>() : null;
            if (display == null || !display.running) return Fail("PICO XR display has not started; retry after XR initialization.");
            var settings = Resources.Load<PXR_ProjectSetting>("PXR_ProjectSetting");
            if (settings == null || !settings.videoSeeThrough) return Fail("Build requires PXR_ProjectSetting.videoSeeThrough and enable_vst=1.");
            if (view.targetTexture != null || view.stereoTargetEye != StereoTargetEyeMask.Both)
                return Fail("Entry requires the tracked stereo display camera, without a target texture.");
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset && cameraData == null)
                return Fail("The URP entry camera has no UniversalAdditionalCameraData.");
            if (cameraData != null && (cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraStack != null && cameraData.cameraStack.Count > 0)))
                return Fail("Entry expects a base camera without an overlay stack that can overwrite alpha.");
            try
            {
                HasRuntimeStatus = false;
                RuntimeReportsEnabled = false;
                if (!statusSubscribed)
                {
                    PXR_Manager.VstDisplayStatusChanged += OnVstStatus;
                    statusSubscribed = true;
                }
                applied = true; // Set before mutations so exceptions still roll back.
                ApplyTransparentCamera();
                requestTouched = true;
                PXR_Manager.EnableVideoSeeThrough = true;
                activeDisplay = display;
                RenderPipelineManager.endCameraRendering -= ReportEyeTarget;
                RenderPipelineManager.endCameraRendering += ReportEyeTarget;
                LastFailure = null;
                Debug.Log($"[EntryPassthrough] request_enabled frame={Time.frameCount} graphics={SystemInfo.graphicsDeviceType} alpha=0 hdr=false post=false; visibility_unverified");
                return true;
            }
            catch (Exception exception)
            {
                RestoreState("start_failed");
                return Fail("VST request failed: " + exception.Message);
            }
#else
            return Fail("Passthrough requires a supported headset player; Editor preview does not validate passthrough.");
#endif
        }

        void ApplyTransparentCamera()
        {
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.clear;
            view.allowHDR = false;
            if (cameraData == null) return;
            cameraData.renderPostProcessing = false;
            cameraData.allowHDROutput = false;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.stopNaN = false;
            cameraData.dithering = false;
        }

        bool VisionRequestActive()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            return activeDisplay != null && activeDisplay.running && OceanXRPlatform.VisionOSImmersiveSpaceReady &&
                UnityEngine.XR.VisionOS.VisionOS.GetLayerRenderer() != IntPtr.Zero;
#else
            return false;
#endif
        }

#if UNITY_VISIONOS && !UNITY_EDITOR
        bool TryStartVisionOS()
        {
            // Metal Mixed/Automatic is a build-time prerequisite. visionOS 2.4.3 has no
            // public runtime immersion-style query; alpha and compositor readiness are not eye proof.
            var loader = XRGeneralSettings.Instance.Manager.activeLoader;
            var display = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            if (display == null || !display.running || !OceanXRPlatform.VisionOSImmersiveSpaceReady ||
                UnityEngine.XR.VisionOS.VisionOS.GetLayerRenderer() == IntPtr.Zero)
                return Fail("visionOS Metal compositor is not ready; retry after immersive-space initialization.");
            if (view.targetTexture != null || view.stereoTargetEye != StereoTargetEyeMask.Both || cameraData == null ||
                cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraStack != null && cameraData.cameraStack.Count > 0))
                return Fail("visionOS entry requires a tracked URP base camera with no render texture or overlay stack.");
            try
            {
                applied = true;
                ApplyTransparentCamera(); // Explicitly resets alpha after a previous Skybox clear.
                visionRequestTouched = true;
                activeDisplay = display;
                HasRuntimeStatus = false; // No public passthrough-status event on this route.
                RuntimeReportsEnabled = false;
                RenderPipelineManager.endCameraRendering -= ReportEyeTarget;
                RenderPipelineManager.endCameraRendering += ReportEyeTarget;
                LastFailure = null;
                Debug.Log($"[EntryPassthrough] provider=VisionOSMetal alpha=0 compositor_ready=true frame={Time.frameCount}; requires_mixed_or_automatic; eye_visibility_unverified");
                return true;
            }
            catch (Exception exception)
            {
                RestoreState("visionos_start_failed");
                return Fail("visionOS passthrough request failed: " + exception.Message);
            }
        }

        void Update()
        {
            // Immersive-space loss is not guaranteed to arrive as ApplicationPause.
            // Release our camera changes while it is absent, then let the existing lease retry.
            if (!initialized || closed || !wantsStart) return;
            if (visionRequestTouched && !VisionRequestActive()) RestoreState("visionos_space_unavailable");
            if (!applied && !paused && focused && OceanXRPlatform.IsVisionOS) ResumeIfNeeded();
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        bool TryStartQuest()
        {
            var loader = XRGeneralSettings.Instance.Manager.activeLoader;
            var display = loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            var cameraSubsystem = loader.GetLoadedSubsystem<XRCameraSubsystem>();
            if (display == null || !display.running || cameraSubsystem == null)
                return Fail("Quest display/camera subsystem is not ready; enable Meta Camera, Session and Composition Layers features.");
            if (view.targetTexture != null || view.stereoTargetEye != StereoTargetEyeMask.Both || cameraData == null ||
                cameraData.renderType != CameraRenderType.Base ||
                (cameraData.cameraStack != null && cameraData.cameraStack.Count > 0))
                return Fail("Quest entry requires a tracked URP base camera with no render texture or overlay stack.");
            try
            {
                applied = true;
                ApplyTransparentCamera();
                if (questCamera == null)
                {
                    questCamera = view.GetComponent<ARCameraManager>();
                    questCameraOriginalEnabled = questCamera != null && questCamera.enabled;
                    questCameraOwned = questCamera == null;
                    // The public AR Foundation manager starts/stops Meta's composition underlay.
                    // This app does not read CPU camera images. Meta Camera 2.4.1 also declares
                    // camera-access permissions in the manifest; their native behavior needs device validation.
                    questRequestTouched = true;
                    if (questCameraOwned) questCamera = view.gameObject.AddComponent<ARCameraManager>();
                }
                questRequestTouched = true;
                if (questCamera.subsystem == null || !questCamera.subsystem.running) questCamera.enabled = false;
                questCamera.enabled = true;
                if (questCamera.subsystem == null || !questCamera.subsystem.running)
                {
                    RestoreState("quest_camera_not_ready");
                    return Fail("Quest passthrough camera manager has not started; retrying.");
                }
                activeDisplay = display;
                HasRuntimeStatus = true;
                RuntimeReportsEnabled = true; // Subsystem running is not proof of visible pixels.
                RenderPipelineManager.endCameraRendering -= ReportEyeTarget;
                RenderPipelineManager.endCameraRendering += ReportEyeTarget;
                LastFailure = null;
                Debug.Log($"[EntryPassthrough] provider=QuestOpenXR request_enabled frame={Time.frameCount} alpha=0; subsystem_running=true; eye_visibility_unverified");
                return true;
            }
            catch (Exception exception)
            {
                RestoreState("quest_start_failed");
                return Fail("Quest passthrough request failed: " + exception.Message);
            }
        }
#endif

        public bool ResumeIfNeeded()
        {
            return !closed && wantsStart && !paused && focused && TryStart();
        }

        /// <summary>Final release. Restores the Begin snapshot and cancels all automatic resume.</summary>
        public void Restore()
        {
            if (closed) return;
            closed = true;
            wantsStart = false;
            RestoreState("released");
            if (owner == this) owner = null;
        }

        public void Dispose()
        {
            Restore();
            if (this != null) { if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject); }
        }

        void OnApplicationPause(bool value)
        {
            paused = value;
            if (!initialized || closed) return;
            if (value) RestoreState("paused");
            else ResumeIfNeeded();
        }

        void OnApplicationFocus(bool value)
        {
            focused = value;
            if (!initialized || closed) return;
            if (!value) RestoreState("focus_lost");
            else ResumeIfNeeded();
        }

        void OnApplicationQuit() => Restore();
        void OnDisable() { if (initialized) Restore(); }
        void OnDestroy() => Restore();

        void RestoreState(string reason)
        {
#if (UNITY_ANDROID || UNITY_VISIONOS) && !UNITY_EDITOR
            RenderPipelineManager.endCameraRendering -= ReportEyeTarget;
            activeDisplay = null;
#endif
#if ENABLE_PICO_XR_SDK
            if (statusSubscribed)
            {
                PXR_Manager.VstDisplayStatusChanged -= OnVstStatus;
                statusSubscribed = false;
            }
#endif
            bool wasApplied = applied;
            applied = false;
            HasRuntimeStatus = false;
            RuntimeReportsEnabled = false;
            visionRequestTouched = false;
            if (questRequestTouched)
            {
                try { if (questCamera != null) questCamera.enabled = questCameraOriginalEnabled; }
                catch (Exception exception) { Debug.LogWarning("[EntryPassthrough] quest_restore_failed: " + exception.Message); }
                questRequestTouched = false;
            }
            if (closed && questCameraOwned && questCamera != null)
            {
                Destroy(questCamera);
                questCamera = null;
                questCameraOwned = false;
            }
            // Camera restoration must still happen if a native request/event subscriber throws.
            if (wasApplied && view != null)
            {
                view.clearFlags = originalClear;
                view.backgroundColor = originalBackground;
                view.allowHDR = originalHDR;
                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = originalPostProcessing;
                    cameraData.allowHDROutput = originalHDROutput;
                    cameraData.antialiasing = originalAntialiasing;
                    cameraData.stopNaN = originalStopNaN;
                    cameraData.dithering = originalDithering;
                }
            }
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
            if (requestTouched)
            {
                try { PXR_Manager.EnableVideoSeeThrough = originalVst; }
                catch (Exception exception) { Debug.LogWarning("[EntryPassthrough] restore_request_failed: " + exception.Message); }
                requestTouched = false;
            }
#endif
            if (wasApplied)
                Debug.Log($"[EntryPassthrough] restored reason={reason} priorRequest={originalVst} frame={Time.frameCount}");
        }

#if (UNITY_ANDROID || UNITY_VISIONOS) && !UNITY_EDITOR
        void ReportEyeTarget(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || !applied || activeDisplay == null || !activeDisplay.running) return;
            int count = activeDisplay.GetRenderPassCount();
            if (count == 0) return;
            RenderPipelineManager.endCameraRendering -= ReportEyeTarget;
            for (int index = 0; index < count; index++)
            {
                activeDisplay.GetRenderPass(index, out var pass);
                var format = pass.renderTargetDesc.graphicsFormat;
                bool alpha = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(format);
                Debug.Log($"[EntryPassthrough] eye_target pass={index} format={format} hasAlpha={alpha} frame={Time.frameCount}; pixels_and_visibility_unverified");
                if (!alpha) Debug.LogWarning("[EntryPassthrough] XR target has no alpha channel; passthrough composition is not validated.");
            }
        }
#endif

#if ENABLE_PICO_XR_SDK
        void OnVstStatus(PxrVstStatus status)
        {
            if (!applied || closed) return;
            HasRuntimeStatus = true;
            RuntimeReportsEnabled = status == PxrVstStatus.Enabled;
            Debug.Log($"[EntryPassthrough] runtime_blend_status={status} frame={Time.frameCount}; eye_visibility_unverified");
        }
#endif

        static bool ReadVstRequest()
        {
#if ENABLE_PICO_XR_SDK
            return OceanXRPlatform.IsPico && PXR_Manager.EnableVideoSeeThrough;
#else
            return false;
#endif
        }

        bool Fail(string reason)
        {
            if (LastFailure != reason) Debug.LogWarning("[EntryPassthrough] " + reason);
            LastFailure = reason;
            return false;
        }
    }
}
