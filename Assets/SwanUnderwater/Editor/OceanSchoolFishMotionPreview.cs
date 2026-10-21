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

namespace SwanUnderwater.Editor
{
    /// <summary>Actual SchoolFish GLBs and production material variants, rendered in an isolated Edit Mode scene.</summary>
    public static class OceanSchoolFishMotionPreview
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        const int Width = 1600, Height = 900;

        [Serializable] public sealed class ImageRecord
        {
            public string file, view, pose;
            public float phase, stroke, turn, bank;
            public Vector3 nose, motionDirection;
            public int foregroundPixels;
        }
        [Serializable] public sealed class SpeciesRecord
        {
            public string resource, materialFactory = "UnderwaterWorld.GetOrCreateBoidMaterials";
            public float realBodyDiagonal;
            public int renderers, vertices, triangles;
            public bool simpleSchool, gpuInstancing;
            public string[] materialShaders;
            public List<ImageRecord> images = new List<ImageRecord>();
        }
        [Serializable] public sealed class Report
        {
            public string unity, graphicsDevice, error;
            public string scope = "Real 3 SchoolFish GLBs, production cached FishSwim materials and _SCHOOL_SIMPLE variants; URP SingleCameraRequest in Edit Mode. Synthetic fixed phases and turn poses, 15 cm body diagonal, green arrow is anatomical +Z. No device, Play Mode or wearer acceptance claim.";
            public bool success;
            public int assertions;
            public List<SpeciesRecord> species = new List<SpeciesRecord>();
        }

        // Graphics required. Owns batch exit. Never use -nographics.
        // -executeMethod SwanUnderwater.Editor.OceanSchoolFishMotionPreview.Run
        // -oceanSchoolMotionOutput <fresh output directory>
        public static void Run()
        {
            string output = Path.GetFullPath(Argument("-oceanSchoolMotionOutput",
                "Validation/school-motion-preview-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")));
            var report = new Report { unity = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            Scene scene = default;
            UnderwaterWorld world = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            Material arrowMaterial = null;
            Mesh arrowMesh = null;
            bool ownsOutput = false;
            UnityEngine.Random.State random = UnityEngine.Random.state;
            bool originalFog = RenderSettings.fog;
            AmbientMode originalAmbient = RenderSettings.ambientMode;
            Color originalSky = RenderSettings.ambientSkyColor, originalEquator = RenderSettings.ambientEquatorColor,
                originalGround = RenderSettings.ambientGroundColor;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "Edit Mode and a real graphics device are required", report);
                Require(!Directory.Exists(output) || Directory.GetFileSystemEntries(output).Length == 0,
                    "output directory must be fresh; existing evidence will not be overwritten", report);
                Directory.CreateDirectory(output); ownsOutput = true;
                UnityEngine.Random.InitState(628912);
                scene = EditorSceneManager.NewPreviewScene();
                var factory = NewInScene("Inactive production school material owner", scene);
                factory.SetActive(false); world = factory.AddComponent<UnderwaterWorld>();
                MethodInfo materialsMethod = typeof(UnderwaterWorld).GetMethod("GetOrCreateBoidMaterials", Private);
                MethodInfo normalizeMethod = typeof(UnderwaterWorld).GetMethod("NormalizeFishScale", StaticPrivate);
                Require(materialsMethod != null && normalizeMethod != null, "exact production material and scaling methods exist", report);
                Shader shader = Resources.Load<Shader>("Shaders/FishSwim");
                Require(shader != null && !ShaderUtil.ShaderHasError(shader), "production FishSwim shader imports", report);

                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.28f, .39f, .43f);
                RenderSettings.ambientEquatorColor = new Color(.16f, .25f, .28f);
                RenderSettings.ambientGroundColor = new Color(.10f, .15f, .17f);
                for (int i = 0; i < 2; i++)
                {
                    Light light = NewInScene("School portrait light " + i, scene).AddComponent<Light>();
                    light.type = LightType.Directional; light.shadows = LightShadows.None;
                    light.intensity = i == 0 ? 1.5f : .55f;
                    light.color = i == 0 ? new Color(1f, .94f, .84f) : new Color(.65f, .84f, 1f);
                    light.transform.rotation = Quaternion.Euler(i == 0 ? new Vector3(38f, -48f, 0f) : new Vector3(-28f, 132f, 0f));
                }
                Camera camera = NewInScene("Real school mesh portrait camera", scene).AddComponent<Camera>();
                camera.scene = scene; camera.enabled = false; camera.orthographic = true;
                camera.aspect = Width / (float)Height; camera.nearClipPlane = .001f; camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .10f, .13f);
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.allowXRRendering = false; data.renderPostProcessing = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                Require(target.Create(), "portrait render target is available", report);
                camera.targetTexture = target;
                pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);

                arrowMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                arrowMaterial.SetColor("_BaseColor", new Color(.16f, 1f, .32f)); arrowMaterial.SetFloat("_Cull", 0f);
                LineRenderer line = NewInScene("Green anatomical forward arrow", scene).AddComponent<LineRenderer>();
                line.sharedMaterial = arrowMaterial; line.positionCount = 2; line.useWorldSpace = true;
                var arrowHead = NewInScene("Green forward arrow head", scene);
                arrowMesh = new Mesh(); arrowHead.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                arrowHead.AddComponent<MeshRenderer>().sharedMaterial = arrowMaterial;

                foreach (string name in new[] { "01_clownfish", "02_blue_tang", "23_mandarinfish" })
                {
                    GameObject prefab = Resources.Load<GameObject>("SchoolFish/" + name);
                    Require(prefab != null, "real admitted school resource exists: " + name, report);
                    var record = new SpeciesRecord { resource = "SchoolFish/" + name };
                    report.species.Add(record);
                    GameObject model = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(model, scene);
                    try
                    {
                        normalizeMethod.Invoke(null, new object[] { model, .15f });
                        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                        Material[] materials = (Material[])materialsMethod.Invoke(world, new object[] { prefab });
                        Require(renderers.Length > 0 && materials.Length > 0, "real model contains rendered production materials", report);
                        record.renderers = renderers.Length; record.gpuInstancing = true; record.simpleSchool = true;
                        record.materialShaders = new string[materials.Length];
                        for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        {
                            Material material = materials[materialIndex];
                            Require(material != null && material.shader == shader, "production FishSwim material is retained", report);
                            record.materialShaders[materialIndex] = material.shader.name;
                            record.gpuInstancing &= material.enableInstancing;
                            record.simpleSchool &= material.IsKeywordEnabled("_SCHOOL_SIMPLE");
                        }
                        Require(record.gpuInstancing && record.simpleSchool, "actual instanced _SCHOOL_SIMPLE variant is selected", report);
                        foreach (Renderer renderer in renderers) renderer.sharedMaterials = materials;
                        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                        { record.vertices += filter.sharedMesh.vertexCount; record.triangles += filter.sharedMesh.triangles.Length / 3; }
                        Bounds initialBounds = ModelBounds(renderers); record.realBodyDiagonal = initialBounds.size.magnitude;
                        Require(Mathf.Abs(record.realBodyDiagonal - .15f) < .0002f, "actual school portrait keeps production physical scale", report);

                        for (int poseIndex = 0; poseIndex < 3; poseIndex++)
                        {
                            float phase = poseIndex == 0 ? 0f : poseIndex == 1 ? Mathf.PI * .5f : Mathf.PI * 1.5f;
                            string poseName = poseIndex == 0 ? "cruise" : poseIndex == 1 ? "turn-left" : "turn-right";
                            var state = OceanSchoolFishMotion.Create(Vector3.forward, phase, materials[0].GetFloat("_SwingFreq"));
                            Vector3 desired = poseIndex == 0 ? Vector3.forward : Quaternion.Euler(0f, poseIndex == 1 ? -70f : 70f, 0f) * Vector3.forward;
                            Quaternion heading = Quaternion.identity; Vector3 velocity = Vector3.zero;
                            for (int tick = 0; tick < (poseIndex == 0 ? 100 : 16); tick++)
                                velocity = OceanSchoolFishMotion.Step(ref state, desired * (poseIndex == 0 ? .6f : 1.5f), .15f,
                                    poseIndex != 0, 1f / 72f, out heading);
                            model.transform.rotation = heading;
                            Require(velocity.sqrMagnitude > .00001f && Vector3.Dot(velocity.normalized, model.transform.forward) > .9999f,
                                "rendered body heading uses actual nose-first school steering", report);
                            var block = new MaterialPropertyBlock();
                            block.SetColor("_BaseColor", Color.white);
                            block.SetFloat("_SwingPhase", phase);
                            block.SetVector("_SwimMotion", new Vector4(phase, state.stroke, state.turn, 2f));
                            // Freeze only timestamp advancement for deterministic portraits;
                            // mode 2, instancing, mesh deformation and production materials stay active.
                            block.SetVector("_SchoolClock", new Vector4(1e9f, state.clockRate, OceanSchoolFishMotion.MaximumClockExtrapolation, 0f));
                            foreach (Renderer renderer in renderers) renderer.SetPropertyBlock(block);
                            Bounds bounds = ModelBounds(renderers); float length = record.realBodyDiagonal;
                            Transform body = model.transform;
                            Vector3 start = bounds.center + body.right * (length * .63f) - body.forward * (length * .31f);
                            Vector3 end = start + body.forward * (length * .65f), side = body.right * (length * .045f);
                            line.startWidth = line.endWidth = length * .012f;
                            line.SetPosition(0, start); line.SetPosition(1, end - body.forward * length * .09f);
                            arrowMesh.vertices = new[] { end, end - body.forward * length * .14f + side, end - body.forward * length * .14f - side };
                            arrowMesh.triangles = new[] { 0, 1, 2 }; arrowMesh.RecalculateBounds();
                            foreach (string view in new[] { "three-quarter", "top" })
                            {
                                Vector3 focus = bounds.center + body.right * (length * .10f);
                                camera.transform.position = view == "top" ? focus + body.up * (length * 3f) :
                                    focus + (body.right * 1.5f + body.up * .55f + body.forward * .85f).normalized * (length * 3f);
                                camera.transform.LookAt(focus, view == "top" ? body.forward : body.up);
                                camera.orthographicSize = length * .58f;
                                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                                RenderTexture previous = RenderTexture.active;
                                try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false, false); }
                                finally { RenderTexture.active = previous; }
                                int foreground = CountForeground(pixels.GetPixels32());
                                Require(foreground > 20000, "portrait contains a substantial rendered fish, not an empty frame", report);
                                string file = Path.Combine(output, name + "-" + poseName + "-" + view + ".png");
                                File.WriteAllBytes(file, pixels.EncodeToPNG());
                                record.images.Add(new ImageRecord { file = file, view = view, pose = poseName,
                                    phase = phase, stroke = state.stroke, turn = state.turn, bank = state.bank,
                                    nose = body.forward, motionDirection = velocity.normalized, foregroundPixels = foreground });
                            }
                        }
                        Require(record.images.Count == 6, "each real species has 3 phase/turn poses in two close views", report);
                    }
                    finally { Object.DestroyImmediate(model); }
                }
                report.success = true;
                File.WriteAllText(Path.Combine(output, "README.txt"),
                    "School fish material and pose review\n\nEach of the three real SchoolFish GLBs has cruise / turn-left / turn-right portraits from three-quarter and top views.\nGreen arrow = body +Z and actual steering direction. Physical model diagonal = 0.15 m; camera zoom exposes the school mesh and texture quality.\nMaterials are built by the production UnderwaterWorld.GetOrCreateBoidMaterials method, including _SCHOOL_SIMPLE and instancing.\nTail phases are fixed synthetic samples using mode 2; these are neither real-time gameplay frames nor headset screenshots.\nInspect fins, head stability, tail deformation, painted texture and nose direction. Pixel coverage assertions do not certify visual quality.\n");
            }
            catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
            finally
            {
                if (world != null)
                {
                    var cache = (Dictionary<string, Material[]>)typeof(UnderwaterWorld).GetField("boidMaterialCache", Private).GetValue(world);
                    var ownedMaterials = new HashSet<Material>();
                    foreach (Material[] materials in cache.Values) foreach (Material material in materials)
                        if (material != null && !AssetDatabase.Contains(material) && ownedMaterials.Add(material)) Object.DestroyImmediate(material);
                }
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                if (arrowMaterial != null) Object.DestroyImmediate(arrowMaterial);
                if (arrowMesh != null) Object.DestroyImmediate(arrowMesh);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pixels != null) Object.DestroyImmediate(pixels);
                RenderSettings.fog = originalFog; RenderSettings.ambientMode = originalAmbient;
                RenderSettings.ambientSkyColor = originalSky; RenderSettings.ambientEquatorColor = originalEquator; RenderSettings.ambientGroundColor = originalGround;
                UnityEngine.Random.state = random;
                if (ownsOutput) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("SCHOOL_FISH_MOTION_PREVIEW_" + (report.success ? "PASS " : "FAIL ") + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.success ? 0 : 1);
        }

        static GameObject NewInScene(string name, Scene scene)
        { var value = new GameObject(name); SceneManager.MoveGameObjectToScene(value, scene); return value; }
        static Bounds ModelBounds(Renderer[] renderers)
        { Bounds bounds = renderers[0].bounds; for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds); return bounds; }
        static int CountForeground(Color32[] pixels)
        {
            Color32 background = pixels[0]; int count = 0;
            foreach (Color32 pixel in pixels)
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 35) count++;
            return count;
        }
        static void Require(bool condition, string detail, Report report)
        { report.assertions++; if (!condition) throw new InvalidOperationException("School fish preview: " + detail); }
        static string Argument(string key, string fallback)
        { string[] args = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return fallback; }
    }
}
#endif
