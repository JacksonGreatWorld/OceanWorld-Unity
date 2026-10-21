using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>A short tracked-head-safe water passage for actual map changes, independent of startup/VST.</summary>
    public sealed class OceanEnvironmentTransition : MonoBehaviour
    {
        public const float CoverSeconds = .48f, RevealSeconds = .72f;
        public float Opacity { get; private set; }
        public bool OpaqueFrameSubmitted { get; private set; }
        public bool Cancelled { get; private set; }
        public int OpaqueCameraId { get; private set; }
        public Camera View { get; private set; }
        public bool PresentationAvailable => isActiveAndEnabled && !paused && focused && !Cancelled;
        Material material;
        Mesh mesh;
        MeshRenderer surface;
        bool paused, focused = true;
        float flowTime;
        enum Stage { Cover, Hold, Reveal, Finished }
        Stage stage;

        public static bool Required(int fromLevel, int toLevel) =>
            OceanEnvironmentProfile.ForLevel(fromLevel) != OceanEnvironmentProfile.ForLevel(toLevel);

        public static OceanEnvironmentTransition Create(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            Shader shader = Resources.Load<Shader>("Shaders/OceanEnvironmentTransition");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Water passage shader is unavailable.");
            var host = new GameObject("Tracked ocean water passage");
            try
            {
                host.transform.SetParent(camera.transform, false);
                // Both eyes see one comfortable-depth surface through their real XR projections.
                // Keep pattern scale independent of the oversized quad used to cover wide eye frusta.
                float depth = Mathf.Max(2.5f, camera.nearClipPlane + .25f);
                float extent = Mathf.Max(4f, depth * 4f);
                host.transform.localPosition = Vector3.forward * depth;
                for (int layer = 0; layer < 32; layer++) if ((camera.cullingMask & (1 << layer)) != 0) { host.layer = layer; break; }
                var effect = host.AddComponent<OceanEnvironmentTransition>(); effect.View = camera;
                effect.material = new Material(shader) { name = "Ocean water passage runtime", renderQueue = 4995 };
                effect.material.SetFloat("_PatternDepth", depth);
                effect.mesh = new Mesh { name = "Stereo water passage cover" };
                effect.mesh.vertices = new[] { new Vector3(-extent,-extent,0), new Vector3(extent,-extent,0), new Vector3(-extent,extent,0), new Vector3(extent,extent,0) };
                effect.mesh.triangles = new[] { 0,2,1,2,3,1 }; effect.mesh.RecalculateBounds();
                host.AddComponent<MeshFilter>().sharedMesh = effect.mesh;
                effect.surface = host.AddComponent<MeshRenderer>(); effect.surface.sharedMaterial = effect.material;
                effect.surface.sortingOrder = 32767; effect.surface.shadowCastingMode = ShadowCastingMode.Off; effect.surface.receiveShadows = false;
                effect.SetOpacity(0f);
                Debug.Log("[OceanMapPassage] created camera=" + camera.GetInstanceID() + " no_vst=true tracked_head_unchanged=true pattern=shared_local_plane depth=" + depth);
                return effect;
            }
            catch { Destroy(host); throw; }
        }

        void OnEnable() => RenderPipelineManager.endCameraRendering += Rendered;
        void OnDisable() { RenderPipelineManager.endCameraRendering -= Rendered; Cancel("disabled"); }
        void OnApplicationPause(bool value) { paused = value; OpaqueFrameSubmitted = false; }
        void OnApplicationFocus(bool value)
        {
#if !UNITY_EDITOR
            focused = value; OpaqueFrameSubmitted = false;
#endif
        }
        void LateUpdate()
        {
            if (!PresentationAvailable || material == null) return;
            flowTime += Mathf.Clamp(Time.unscaledDeltaTime, 0f, .05f);
            material.SetFloat("_FlowTime", flowTime);
            material.SetFloat("_Rush", stage == Stage.Reveal ? Opacity : Mathf.SmoothStep(.2f, 1f, Opacity));
        }
        void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != View || !PresentationAvailable || surface == null || !surface.enabled || Opacity < .99999f) return;
            if (!OpaqueFrameSubmitted)
                Debug.Log("[OceanMapPassage] opaque_submitted camera=" + camera.GetInstanceID() + " frame=" + Time.frameCount);
            OpaqueCameraId = camera.GetInstanceID(); OpaqueFrameSubmitted = true;
        }
        public IEnumerator Cover()
        {
            stage = Stage.Cover;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (Opacity < 1f || !OpaqueFrameSubmitted)
            {
                RequireActive(deadline);
                if (PresentationAvailable && Opacity < 1f)
                    SetOpacity(Mathf.MoveTowards(Opacity, 1f, Mathf.Min(Time.unscaledDeltaTime, .05f) / CoverSeconds));
                yield return null;
            }
            stage = Stage.Hold;
            Debug.Log("[OceanMapPassage] prepare_allowed opaque_camera=" + OpaqueCameraId + " frame=" + Time.frameCount);
        }
        public IEnumerator Reveal()
        {
            stage = Stage.Reveal;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (Opacity > 0f)
            {
                RequireActive(deadline);
                if (PresentationAvailable)
                    SetOpacity(Mathf.MoveTowards(Opacity, 0f, Mathf.Min(Time.unscaledDeltaTime, .05f) / RevealSeconds));
                yield return null;
            }
            stage = Stage.Finished;
            Debug.Log("[OceanMapPassage] revealed frame=" + Time.frameCount);
        }
        void RequireActive(double deadline)
        {
            if (Cancelled || !isActiveAndEnabled) throw new OperationCanceledException("Water passage was cancelled.");
            if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("Water passage could not receive current presentation frames.");
        }
        void SetOpacity(float value)
        {
            Opacity = Mathf.Clamp01(value);
            if (material != null) material.SetFloat("_Opacity", Mathf.SmoothStep(0f, 1f, Opacity));
        }
        public void Cancel(string reason = "owner_cleanup")
        {
            if (Cancelled) return;
            Cancelled = true; OpaqueFrameSubmitted = false;
            if (surface != null) surface.enabled = false;
            Debug.Log("[OceanMapPassage] cleanup reason=" + reason + " stage=" + stage);
        }
        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= Rendered;
            if (material != null) Destroy(material); if (mesh != null) Destroy(mesh);
        }
#if UNITY_EDITOR
        public void SetPresentationForValidation(bool pause, bool focus)
        { paused = pause; focused = focus; OpaqueFrameSubmitted = false; }
#endif
    }
}
