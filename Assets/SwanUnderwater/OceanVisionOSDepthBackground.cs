using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Finite sea background for visionOS depth reprojection, hidden during the MR entry.</summary>
    [DisallowMultipleComponent]
    public sealed class OceanVisionOSDepthBackground : MonoBehaviour
    {
        Camera view;
        GameObject shell;
        MeshRenderer shellRenderer;
        Material material;
        bool depthRangeApplied, visibilityReported, lastReportedVisible;
        float appliedNear, appliedFar;
        System.IntPtr appliedLayerRenderer;

        public static void Attach(Camera camera, Material sky)
        {
            if (camera == null || sky == null || !OceanXRPlatform.IsVisionOS) return;
            var component = camera.GetComponent<OceanVisionOSDepthBackground>();
            if (component == null) component = camera.gameObject.AddComponent<OceanVisionOSDepthBackground>();
            component.Initialize(camera, sky);
        }

        void Initialize(Camera camera, Material sky)
        {
            view = camera;
            // Use an explicit URP mesh pass for compositor depth. Reusing the
            // legacy skybox pass did not occlude the room in the verified R3 run.
            var finiteShader = Resources.Load<Shader>("Shaders/VisionOSSeaBackground");
            if (finiteShader == null || !finiteShader.isSupported)
            {
                Debug.LogError("[OceanVisionOSDepth] Dedicated URP finite background shader unavailable; room occlusion is not guaranteed.");
                if (shellRenderer != null) shellRenderer.enabled = false;
                return;
            }
            if (material != null) Destroy(material);
            material = new Material(finiteShader) { name = "visionOS URP finite sea background" };
            // Preserve runtime source colors/intensities exactly, without a second
            // color-space conversion or copying the skybox's render state/keywords.
            material.SetColor("_TopColor", sky.GetColor("_TopColor"));
            material.SetColor("_HorizonColor", sky.GetColor("_HorizonColor"));
            material.SetColor("_BottomColor", sky.GetColor("_BottomColor"));
            material.SetColor("_SunColor", sky.GetColor("_SunColor"));
            material.SetFloat("_TopIntensity", sky.GetFloat("_TopIntensity"));
            material.SetFloat("_HorizonIntensity", sky.GetFloat("_HorizonIntensity"));
            // Render as opaque before URP's AfterOpaques depth copy. The source sky remains unchanged.
            material.renderQueue = (int)RenderQueue.Geometry;
            if (shell == null)
            {
                shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "visionOS sea depth shell";
                var collider = shell.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                shell.transform.SetParent(view.transform, false);
                shellRenderer = shell.GetComponent<MeshRenderer>();
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
                shellRenderer.lightProbeUsage = LightProbeUsage.Off;
                shellRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                shellRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            shellRenderer.sharedMaterial = material;
            shellRenderer.enabled = false;
            depthRangeApplied = false;
            visibilityReported = false;
            SyncDepthRange();
            ReportState("created", false, 0f);
        }

        void LateUpdate()
        {
            if (shellRenderer == null || view == null) return;
            SyncDepthRange();
            bool revealed = OceanEntry.Instance == null || OceanEntry.Instance.WorldRevealed;
            bool show = OceanXRPlatform.IsVisionOS && revealed && view.clearFlags == CameraClearFlags.Skybox;
            shellRenderer.enabled = show;
            // A unit primitive sphere has radius .5. Remain inside the far plane so reversed-Z is nonzero.
            float radius = Mathf.Min(150f, view.farClipPlane * .9f);
            if (show)
            {
                shell.transform.SetPositionAndRotation(view.transform.position, Quaternion.identity);
                shell.transform.localScale = Vector3.one * (radius * 2f);
            }
            if (!visibilityReported || show != lastReportedVisible)
            {
                visibilityReported = true;
                lastReportedVisible = show;
                ReportState("visibility", show, radius);
            }
        }

        void SyncDepthRange()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            if (view == null || !OceanXRPlatform.IsVisionOS) return;
            // XR visionOS 2.4.3 does not infer the camera clip range. Its native
            // culling default is 40 m, unlike our camera range and 150 m sea shell.
            // Wait for the compositor and reapply after a layer/focus restart.
            bool ready = UnityEngine.XR.VisionOS.VisionOS.IsImmersiveSpaceReady();
            System.IntPtr layer = UnityEngine.XR.VisionOS.VisionOS.GetLayerRenderer();
            if (!ready || layer == System.IntPtr.Zero) { depthRangeApplied = false; return; }
            float near = view.nearClipPlane, far = view.farClipPlane;
            if (float.IsNaN(near) || float.IsInfinity(near) || float.IsNaN(far) || float.IsInfinity(far) ||
                near <= 0f || far <= near) { depthRangeApplied = false; return; }
            // Apple's public layer capability defines a minimum near distance.
            // The verified runtime rejects .05 m with a fatal native assertion; its minimum is .1 m.
            const float minimumNear = .1f;
            if (near < minimumNear)
            {
                if (far <= minimumNear) { depthRangeApplied = false; return; }
                float previous = near;
                view.nearClipPlane = minimumNear;
                near = view.nearClipPlane; // Camera projection and the submitted range must agree.
                Debug.Log($"[OceanVisionOSDepth] near_clamped from={previous:F4} to={near:F4} reason=compositor_minimum_near_0.1m frame={Time.frameCount}");
            }
            if (depthRangeApplied && layer == appliedLayerRenderer && near == appliedNear && far == appliedFar) return;
            UnityEngine.XR.VisionOS.VisionOS.SetDepthRange(near, far);
            appliedNear = near; appliedFar = far; appliedLayerRenderer = layer; depthRangeApplied = true;
            Debug.Log($"[OceanVisionOSDepth] depth_range_requested near={near:F4} far={far:F2} minimumNear={minimumNear:F4} frame={Time.frameCount}; public SDK call, visual result unverified");
#endif
        }

        void ReportState(string reason, bool show, float radius)
        {
            var shader = material != null ? material.shader : null;
            var sky = RenderSettings.skybox;
            Debug.Log($"[OceanVisionOSDepth] reason={reason} show={show} clear={view.clearFlags} alpha={view.backgroundColor.a:F2}" +
                $" near={view.nearClipPlane:F4} far={view.farClipPlane:F2} radius={radius:F2} renderer={shellRenderer.enabled}" +
                $" layer={shell.layer} cameraIncludesLayer={(view.cullingMask & (1 << shell.layer)) != 0}" +
                $" shader={(shader != null ? shader.name : "null")} supported={(shader != null && shader.isSupported)}" +
                $" queue={material.renderQueue} depthWrite={material.GetFloat("_DepthWrite"):F0} passes={material.passCount}" +
                $" boundsCenter={shellRenderer.bounds.center:F2} boundsSize={shellRenderer.bounds.size:F2}" +
                $" sourceSky={(sky != null ? sky.name : "null")} rangeRequested={depthRangeApplied} frame={Time.frameCount}");
        }

        void OnApplicationFocus(bool focused) { if (!focused) depthRangeApplied = false; }
        void OnApplicationPause(bool paused) { if (paused) depthRangeApplied = false; }
        void OnEnable() { depthRangeApplied = false; visibilityReported = false; }
        void OnDisable()
        {
            depthRangeApplied = false;
            visibilityReported = false;
            if (shellRenderer != null) shellRenderer.enabled = false;
        }
        void OnDestroy()
        {
            if (shell != null) Destroy(shell);
            if (material != null) Destroy(material);
        }
    }
}
