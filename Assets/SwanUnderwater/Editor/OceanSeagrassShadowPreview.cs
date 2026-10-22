#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Graphical regression: actual grass shaders must respond to a real caster,
    /// retain ambient in shade and lose the shadow beyond URP's configured distance.</summary>
    public static class OceanSeagrassShadowPreview
    {
        [Serializable] sealed class Pair
        {
            public string shader, litImage, shadowImage;
            public int cascades, changedPixels, receiverPixels;
            public bool distant;
            public float shadowDistance, eyeDistance, averageDifference, minimumShadedLuminance;
        }
        [Serializable] sealed class Report
        {
            public bool success, baseline, restoredContext, responseValidated;
            public string error;
            public string scope = "Real graphical Editor URP render of both production grass shaders, controlled opaque blades and a shadows-only occluder. Identical geometry, pose, material and ambient per pair; only directional-light shadows toggle. Not headset stereo or Android frame-time acceptance.";
            public List<Pair> pairs = new List<Pair>();
        }

        public static int ValidateBuilt(UnderwaterWorld world)
        {
            if (world == null || !world.IsReady) throw new InvalidOperationException("A completed production world is required.");
            var meadow = world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>(true);
            if (meadow == null) throw new InvalidOperationException("The production meadow is missing.");
            int receivers = 0;
            foreach (MeshRenderer renderer in meadow.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.receiveShadows || renderer.shadowCastingMode != ShadowCastingMode.Off)
                    throw new InvalidOperationException("Grass must receive the reef shadow without becoming a caster: " + renderer.name);
                Material material = renderer.sharedMaterial;
                if (material == null || material.passCount != 1 || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Production grass must retain a supported one-pass material.");
                receivers++;
            }
            if (receivers == 0) throw new InvalidOperationException("The production meadow has no receivers.");
            Debug.Log("SEAGRASS_SHADOW_RECEIVERS_PASS environment=" + world.ActiveEnvironment + " renderers=" + receivers);
            return receivers;
        }

        // Fresh graphical batch Editor, no -quit/-nographics. Copy this helper unchanged
        // into a baseline workspace and append -oceanSeagrassShadowBaseline to retain
        // old renders without treating their missing shadow response as a test pass.
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use a fresh graphical batch Editor in Edit Mode.");
            string output = "Validation/seagrass-shadow-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            var report = new Report();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Preview owns process exit and needs graphics.");
                if (args[i] == "-oceanSeagrassShadowOutput" && i + 1 < args.Length) output = args[++i];
                if (args[i] == "-oceanSeagrassShadowBaseline") report.baseline = true;
            }
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                throw new IOException("Use a fresh output directory.");
            Directory.CreateDirectory(output);
            RenderPipelineAsset oldQuality = QualitySettings.renderPipeline, oldDefault = GraphicsSettings.defaultRenderPipeline;
            Scene oldScene = SceneManager.GetActiveScene(), scene = default;
            bool oldFog = RenderSettings.fog, oldAsync = ShaderUtil.allowAsyncCompilation;
            Material oldSky = RenderSettings.skybox; Light oldSun = RenderSettings.sun;
            AmbientMode oldAmbient = RenderSettings.ambientMode; Color oldAmbientLight = RenderSettings.ambientLight;
            SphericalHarmonicsL2 oldProbe = RenderSettings.ambientProbe;
            UniversalRenderPipelineAsset pipeline = null;
            RenderTexture target = null; Texture2D pixels = null; Mesh mesh = null;
            var owned = new List<Material>();
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                ShaderUtil.allowAsyncCompilation = false;
                pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
                pipeline.name = "Unsaved grass shadow diagnostic Mobile pipeline";
                QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                RenderSettings.fog = false; RenderSettings.skybox = null;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.13f, .18f, .18f);
                var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(RenderSettings.ambientLight);
                RenderSettings.ambientProbe = probe;
                var root = new GameObject("Unsaved grass shadow diagnostic");
                SceneManager.MoveGameObjectToScene(root, scene);
                var camera = new GameObject("Fixed shadow receiver camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform); camera.enabled = false; camera.scene = scene;
                camera.orthographic = true; camera.orthographicSize = 1.3f; camera.aspect = 16f / 9f;
                camera.nearClipPlane = .05f; camera.farClipPlane = 80f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var sun = new GameObject("Only diagnostic main light").AddComponent<Light>();
                sun.transform.SetParent(root.transform); sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.identity; sun.intensity = 1.5f;
                sun.color = new Color(1f, .95f, .8f);
                OceanReefShadowPolicy.ConfigureSun(sun); RenderSettings.sun = sun;
                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                caster.name = "Real opaque shadows-only occluder"; caster.transform.SetParent(root.transform);
                caster.transform.position = new Vector3(0, 1f, -1.2f);
                caster.transform.localScale = new Vector3(1.3f, 2.4f, .15f);
                Object.DestroyImmediate(caster.GetComponent<Collider>());
                var casterMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(casterMaterial);
                var casterRenderer = caster.GetComponent<MeshRenderer>(); casterRenderer.sharedMaterial = casterMaterial;
                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                mesh = Blades();
                var grass = new GameObject("Fixed blade receiver"); grass.transform.SetParent(root.transform);
                grass.AddComponent<MeshFilter>().sharedMesh = mesh;
                var receiver = grass.AddComponent<MeshRenderer>(); receiver.receiveShadows = true;
                receiver.shadowCastingMode = ShadowCastingMode.Off;
                target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32); target.Create();
                pixels = new Texture2D(640, 360, TextureFormat.RGB24, false); camera.targetTexture = target;
                string[] paths = { "Shaders/Seagrass", "Shaders/SanctuarySeagrass" };
                foreach (string path in paths)
                {
                    Shader shader = Resources.Load<Shader>(path);
                    if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                        throw new InvalidOperationException("Production shader unavailable: " + path);
                    var material = new Material(shader); owned.Add(material); receiver.sharedMaterial = material;
                    material.SetFloat("_SwayStrength", 0f);
                    if (material.passCount != 1) throw new InvalidOperationException("Grass gained an extra rendering pass.");
                    foreach (int cascades in new[] { 1, 2 })
                    {
                        pipeline.shadowCascadeCount = cascades;
                        foreach (bool distant in new[] { false, true })
                        {
                            float eyeDistance = distant ? pipeline.shadowDistance + 8f : 6f;
                            camera.transform.SetPositionAndRotation(new Vector3(0, 1f, -eyeDistance), Quaternion.identity);
                            var pair = new Pair { shader = shader.name, cascades = cascades, distant = distant,
                                shadowDistance = pipeline.shadowDistance, eyeDistance = eyeDistance };
                            string stem = Path.GetFileName(path) + "-cascade" + cascades + (distant ? "-beyond-fade" : "-near");
                            sun.shadows = LightShadows.None;
                            Color32[] lit = Read(camera, target, pixels);
                            pair.litImage = stem + "-unoccluded.png";
                            File.WriteAllBytes(Path.Combine(output, pair.litImage), pixels.EncodeToPNG());
                            sun.shadows = LightShadows.Soft;
                            Color32[] shaded = Read(camera, target, pixels);
                            pair.shadowImage = stem + "-occluded.png";
                            File.WriteAllBytes(Path.Combine(output, pair.shadowImage), pixels.EncodeToPNG());
                            long difference = 0; pair.minimumShadedLuminance = 255f;
                            for (int pixel = 0; pixel < lit.Length; pixel++)
                            {
                                if (lit[pixel].r + lit[pixel].g + lit[pixel].b < 20) continue;
                                pair.receiverPixels++;
                                int delta = Math.Abs(lit[pixel].r - shaded[pixel].r) + Math.Abs(lit[pixel].g - shaded[pixel].g) + Math.Abs(lit[pixel].b - shaded[pixel].b);
                                difference += delta;
                                if (delta > 9)
                                {
                                    pair.changedPixels++;
                                    pair.minimumShadedLuminance = Mathf.Min(pair.minimumShadedLuminance,
                                        (shaded[pixel].r + shaded[pixel].g + shaded[pixel].b) / 3f);
                                }
                            }
                            pair.averageDifference = pair.receiverPixels > 0 ? difference / (pair.receiverPixels * 3f) : 0f;
                            report.pairs.Add(pair);
                            if (pair.receiverPixels < 500) throw new InvalidOperationException("No meaningful grass coverage: " + stem);
                            if (!report.baseline && !distant && (pair.changedPixels < 100 || pair.minimumShadedLuminance < 2f))
                                throw new InvalidOperationException("Near grass must respond to the real caster and retain ambient: " + stem);
                            if (distant && pair.averageDifference > 2f)
                                throw new InvalidOperationException("Grass retained a dark shadow beyond the configured fade: " + stem);
                        }
                    }
                }
                report.success = true;
                report.responseValidated = !report.baseline;
            }
            catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
            finally
            {
                QualitySettings.renderPipeline = oldQuality; GraphicsSettings.defaultRenderPipeline = oldDefault;
                RenderSettings.fog = oldFog; RenderSettings.skybox = oldSky; RenderSettings.sun = oldSun;
                RenderSettings.ambientMode = oldAmbient; RenderSettings.ambientLight = oldAmbientLight; RenderSettings.ambientProbe = oldProbe;
                ShaderUtil.allowAsyncCompilation = oldAsync;
                if (oldScene.IsValid() && oldScene.isLoaded) SceneManager.SetActiveScene(oldScene);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                foreach (Material material in owned) if (material != null) Object.DestroyImmediate(material);
                if (mesh != null) Object.DestroyImmediate(mesh);
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pipeline != null) Object.DestroyImmediate(pipeline);
                report.restoredContext = QualitySettings.renderPipeline == oldQuality && GraphicsSettings.defaultRenderPipeline == oldDefault && SceneManager.GetActiveScene() == oldScene;
                if (!report.restoredContext) report.success = false;
                File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("SEAGRASS_SHADOW_PREVIEW " + (report.success ? "PASS" : "FAIL") + " baseline=" + report.baseline + " pairs=" + report.pairs.Count);
            EditorApplication.Exit(report.success ? 0 : 1);
        }

        static Color32[] Read(Camera camera, RenderTexture target, Texture2D pixels)
        {
            for (int warm = 0; warm < 2; warm++)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active = previous; }
        }

        static Mesh Blades()
        {
            var points = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var motion = new List<Vector2>(); var colors = new List<Color>(); var triangles = new List<int>();
            for (int blade = 0; blade < 19; blade++)
            {
                float x = -2.05f + blade * .22f; int first = points.Count;
                points.Add(new Vector3(x, 0, 0)); points.Add(new Vector3(x + .12f, 0, 0));
                points.Add(new Vector3(x + .12f, 2, 0)); points.Add(new Vector3(x, 2, 0));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int vertex = 0; vertex < 4; vertex++) { normals.Add(Vector3.back); motion.Add(new Vector2(2, 0)); colors.Add(Color.white); }
                triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
            }
            var mesh = new Mesh { name = "Fixed opaque grass shadow receivers" };
            mesh.SetVertices(points); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetUVs(1, motion); mesh.SetUVs(2, motion);
            mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
    }
}
#endif
