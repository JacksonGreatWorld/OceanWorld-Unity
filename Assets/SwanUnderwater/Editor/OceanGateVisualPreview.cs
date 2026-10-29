#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Same rendered lifecycle probe for the admitted 315 runtime and later transient-cue candidates.</summary>
    public static class OceanGateVisualPreview
    {
        [Serializable] sealed class Row
        {
            public string name, image, shader;
            public int progress, target, addedColliders, enabledMeshRenderers, visibleRingRenderers;
            public bool complete, cueActive, cueExpired, temporaryVisualOffControl;
            public float cueOpacity = -1f, ripplePhase = -1f;
            public Vector3 cameraPosition, cameraForward, logicalGatePosition;
        }
        [Serializable] sealed class Report
        {
            public bool success, restoredContext, hasTransientApi;
            public string unity, graphicsDevice, error;
            public int motionFrameCount;
            public float motionSampleSeconds;
            public string scope = "Actual OceanMechanicRuntime with synthetic tracked samples and in-memory level-6 state, URP PreviewScene rendering. Renderer counts mean enabled/active components, not camera pixel or image-quality acceptance. A row marked temporaryVisualOffControl hides only the cue's visual root for a paired reference; logical state and labels remain unchanged. No player files, device, stereo or performance claim.";
            public List<Row> rows = new List<Row>();
        }
        static int frame = 4100000;
        static void Samples(OceanMechanicRuntime runtime, Camera camera, int count)
        {
            for (int i = 0; i < count; i++)
                runtime.StepSceneTest(.05f, frame++, new Ray(camera.transform.position, camera.transform.forward), 0f, true,
                    default, 0f, false, camera.transform.position, camera.transform.forward, .35f);
        }
        static void Aim(Camera camera, Vector3 center, Vector3 forward, float distance)
        { camera.transform.SetPositionAndRotation(center - forward * distance, Quaternion.LookRotation(forward, Vector3.up)); }
        static void Cross(OceanMechanicRuntime runtime, Camera camera, out Vector3 center, out Vector3 forward)
        {
            center = runtime.NodePosition(0); forward = runtime.StationRoot.GetChild(0).forward;
            Aim(camera, center, forward, .15f); Samples(runtime, camera, 12);
            camera.transform.position = center + forward * .15f; Samples(runtime, camera, 1);
        }
        static void Restart(OceanMechanicRuntime runtime, OceanCampaignState state, Camera camera)
        {
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);
            state.StartLevel(6); runtime.ConfigureLevel(Vector3.zero);
        }
        static T Optional<T>(object instance, string property, T fallback)
        {
            if (instance == null) return fallback;
            PropertyInfo info = instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            return info == null ? fallback : (T)info.GetValue(instance);
        }
        static void Record(string name, string output, Report report, OceanMechanicRuntime runtime, OceanCampaignState state,
            Camera camera, RenderTexture target, RenderTexture resolved, Texture2D pixels, bool temporaryVisualOffControl = false)
        {
            runtime.RefreshCaptionPoseForValidation();
            var row = new Row { name = name, image = name + ".png", progress = state.Current.mechanicProgress,
                target = state.Definition.MechanicTarget, complete = state.MechanicComplete,
                temporaryVisualOffControl = temporaryVisualOffControl,
                cameraPosition = camera.transform.position, cameraForward = camera.transform.forward,
                logicalGatePosition = runtime.NodePosition(0), addedColliders = runtime.GetComponentsInChildren<Collider>(true).Length };
            foreach (MeshRenderer renderer in runtime.StationRoot.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !renderer.forceRenderingOff)
                {
                    row.enabledMeshRenderers++;
                    if (renderer.gameObject.name == "Mission node 0" || renderer.gameObject.name.IndexOf("Transient", StringComparison.Ordinal) >= 0)
                        row.visibleRingRenderers++;
                }
            // Reflection deliberately keeps this probe compilable against the unchanged 315 baseline.
            object cue = Optional<object>(runtime, "GateEffectForValidation", null);
            row.cueActive = Optional(cue, "CueActive", false); row.cueExpired = Optional(cue, "Expired", false);
            row.cueOpacity = Optional(cue, "Opacity", -1f); row.ripplePhase = Optional(cue, "RipplePhase", -1f);
            Transform cueRoot = Optional<Transform>(cue, "Root", null);
            MeshRenderer cueRenderer = cueRoot == null ? null : cueRoot.GetComponentInChildren<MeshRenderer>(true);
            row.shader = cueRenderer == null || cueRenderer.sharedMaterial == null || cueRenderer.sharedMaterial.shader == null ?
                "" : cueRenderer.sharedMaterial.shader.name;
            for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            Graphics.Blit(target, resolved); RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = resolved; pixels.ReadPixels(new Rect(0, 0, resolved.width, resolved.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Path.Combine(output, row.image), pixels.EncodeToPNG()); }
            finally { RenderTexture.active = previous; }
            report.rows.Add(row);
        }

        // Graphical isolated batch Editor; omit -quit/-nographics. This entry point owns exit.
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an isolated graphical batch Editor in Edit Mode.");
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-codexGatePreviewOutput");
            if (index < 0 || index + 1 >= args.Length || Array.IndexOf(args, "-quit") >= 0 || Array.IndexOf(args, "-nographics") >= 0)
                throw new ArgumentException("Explicit fresh -codexGatePreviewOutput required; Capture owns exit.");
            string output = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0) throw new IOException("Preserve existing evidence; use a fresh output directory.");
            Directory.CreateDirectory(output);
            var report = new Report { unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                hasTransientApi = typeof(OceanMechanicRuntime).GetProperty("GateEffectForValidation") != null };
            var oldQuality = QualitySettings.renderPipeline; var oldDefault = GraphicsSettings.defaultRenderPipeline;
            Scene oldScene = SceneManager.GetActiveScene(), scene = default;
            bool oldFog = RenderSettings.fog, oldAsync = ShaderUtil.allowAsyncCompilation; FogMode oldFogMode = RenderSettings.fogMode;
            float oldFogStart = RenderSettings.fogStartDistance, oldFogEnd = RenderSettings.fogEndDistance; Color oldFogColor = RenderSettings.fogColor;
            Material oldSky = RenderSettings.skybox; Light oldSun = RenderSettings.sun; AmbientMode oldMode = RenderSettings.ambientMode;
            Color oldAmbient = RenderSettings.ambientLight; SphericalHarmonicsL2 oldProbe = RenderSettings.ambientProbe;
            UniversalRenderPipelineAsset pipeline = null; RenderTexture target = null, resolved = null; Texture2D pixels = null; Material groundMaterial = null;
            OceanMechanicRuntime runtime = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene(); ShaderUtil.allowAsyncCompilation = false;
                pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
                pipeline.msaaSampleCount = 4; QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                RenderSettings.skybox = null; RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 7f; RenderSettings.fogEndDistance = 28f; RenderSettings.fogColor = new Color(.025f, .14f, .20f);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.18f, .25f, .28f);
                var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(RenderSettings.ambientLight); RenderSettings.ambientProbe = probe;
                var root = new GameObject("Isolated actual expedition cue lifecycle"); SceneManager.MoveGameObjectToScene(root, scene);
                Camera camera = new GameObject("Gate paired capture camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                camera.enabled = false; camera.scene = scene; camera.fieldOfView = 68f; camera.aspect = 16f / 9f;
                camera.nearClipPlane = .02f; camera.farClipPlane = 40f; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false; camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                Light sun = new GameObject("Gate portrait sun").AddComponent<Light>(); sun.transform.SetParent(root.transform, false);
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.LookRotation(new Vector3(-.4f, -.8f, -.5f)); sun.intensity = 1.1f; sun.shadows = LightShadows.None; RenderSettings.sun = sun;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.SetParent(root.transform, false); ground.transform.position = new Vector3(0, -.02f, 10f); ground.transform.localScale = Vector3.one * 8f;
                Object.DestroyImmediate(ground.GetComponent<Collider>()); groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                groundMaterial.SetColor("_BaseColor", new Color(.42f, .43f, .32f)); groundMaterial.SetFloat("_Smoothness", .05f); ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; if (!target.Create()) throw new IOException("Render target allocation failed.");
                resolved = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); if (!resolved.Create()) throw new IOException("Readback target allocation failed.");
                pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); camera.targetTexture = target;
                var state = new OceanCampaignState(); runtime = root.AddComponent<OceanMechanicRuntime>(); runtime.Initialize(null, camera, null, state, null);
                Restart(runtime, state, camera); Vector3 first = runtime.NodePosition(0), normal = runtime.StationRoot.GetChild(0).forward;
                Aim(camera, first, normal, 2f); Record("configured-before-gameplay", output, report, runtime, state, camera, target, resolved, pixels);
                Samples(runtime, camera, 20); Record("approach", output, report, runtime, state, camera, target, resolved, pixels);
                if (Array.IndexOf(args, "-codexRipplePhases") >= 0)
                {
                    // Same camera, lighting and logical state; only qualified owner ticks
                    // advance the effect phase between these original render captures.
                    Record("ripple-phase-0", output, report, runtime, state, camera, target, resolved, pixels);
                    object cue = Optional<object>(runtime, "GateEffectForValidation", null);
                    Transform visual = Optional<Transform>(cue, "Root", null);
                    if (visual == null) throw new InvalidOperationException("Ripple phase references require an owned transient cue root.");
                    bool wasActive = visual.gameObject.activeSelf;
                    try
                    {
                        visual.gameObject.SetActive(false);
                        Record("ripple-no-effect-reference", output, report, runtime, state, camera, target, resolved, pixels, true);
                    }
                    finally { if (visual != null) visual.gameObject.SetActive(wasActive); }
                    Samples(runtime, camera, 5); Record("ripple-phase-1", output, report, runtime, state, camera, target, resolved, pixels);
                    Samples(runtime, camera, 5); Record("ripple-phase-2", output, report, runtime, state, camera, target, resolved, pixels);
                    if (Array.IndexOf(args, "-codexRippleAnimation") >= 0)
                    {
                        // Frame 000 shares phase-2's state. Subsequent frames advance
                        // one explicit .05s owner tick, not wall-clock or device FPS.
                        report.motionFrameCount = 40; report.motionSampleSeconds = .05f;
                        for (int motion = 0; motion < report.motionFrameCount; motion++)
                        {
                            if (motion > 0) Samples(runtime, camera, 1);
                            Record("ripple-motion-" + motion.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                                output, report, runtime, state, camera, target, resolved, pixels);
                        }
                    }
                }
                Cross(runtime, camera, out first, out normal); Samples(runtime, camera, 2);
                Aim(camera, first, normal, 2f); Record("pass-0", output, report, runtime, state, camera, target, resolved, pixels);
                Samples(runtime, camera, 12); Record("settled-after-first", output, report, runtime, state, camera, target, resolved, pixels);
                Cross(runtime, camera, out _, out _); Cross(runtime, camera, out Vector3 last, out Vector3 lastNormal);
                if (!state.MechanicComplete || state.Current.mechanicProgress != 3) throw new InvalidOperationException("Actual three crossings did not complete the side activity.");
                Aim(camera, last, lastNormal, 2f); Record("final-after-third", output, report, runtime, state, camera, target, resolved, pixels);
                Restart(runtime, state, camera); first = runtime.NodePosition(0); normal = runtime.StationRoot.GetChild(0).forward;
                Aim(camera, first, normal, 2f); Samples(runtime, camera, 20); runtime.SuspendSceneTest(); Record("paused", output, report, runtime, state, camera, target, resolved, pixels);
                Restart(runtime, state, camera); first = runtime.NodePosition(0); normal = runtime.StationRoot.GetChild(0).forward;
                Aim(camera, first, normal, 12f); Samples(runtime, camera, 20); Record("far", output, report, runtime, state, camera, target, resolved, pixels);
                Restart(runtime, state, camera); first = runtime.NodePosition(0); normal = runtime.StationRoot.GetChild(0).forward;
                Aim(camera, first, normal, 2f); Samples(runtime, camera, 200); Record("idle", output, report, runtime, state, camera, target, resolved, pixels);
                report.success = true;
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            finally
            {
                // Ordinary MonoBehaviour EditMode teardown is not relied upon for dynamically owned meshes/materials.
                if (runtime != null) typeof(OceanMechanicRuntime).GetMethod("Clear", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                QualitySettings.renderPipeline = oldQuality; GraphicsSettings.defaultRenderPipeline = oldDefault;
                RenderSettings.fog = oldFog; RenderSettings.fogMode = oldFogMode; RenderSettings.fogColor = oldFogColor;
                RenderSettings.fogStartDistance = oldFogStart; RenderSettings.fogEndDistance = oldFogEnd; RenderSettings.skybox = oldSky; RenderSettings.sun = oldSun;
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient; RenderSettings.ambientProbe = oldProbe; ShaderUtil.allowAsyncCompilation = oldAsync;
                if (groundMaterial != null) Object.DestroyImmediate(groundMaterial); if (pipeline != null) Object.DestroyImmediate(pipeline);
                if (pixels != null) Object.DestroyImmediate(pixels); if (target != null) { target.Release(); Object.DestroyImmediate(target); } if (resolved != null) { resolved.Release(); Object.DestroyImmediate(resolved); }
                report.restoredContext = QualitySettings.renderPipeline == oldQuality && GraphicsSettings.defaultRenderPipeline == oldDefault && SceneManager.GetActiveScene() == oldScene;
                report.success &= report.restoredContext; File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("GATE_VISUAL_PREVIEW_" + (report.success ? "PASS" : "FAIL") + " " + output); EditorApplication.Exit(report.success ? 0 : 1);
        }
    }
}
#endif
