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
    /// <summary>Real fish factory materials, actual source meshes and a real opaque caster.
    /// Isolated graphical Editor evidence; no scene, assets, player saves or device are modified.</summary>
    public static class OceanFishLightingPreview
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Receive = "_FISH_RECEIVE_SHADOWS", Simple = "_SCHOOL_SIMPLE";
        [Serializable] sealed class Pair
        {
            public string resource, mode, litImage, shadowImage;
            public int cascades, receiverPixels, changedPixels;
            public bool distant, shadowExpected, receiveKeyword, simpleKeyword;
            public float averageDifference, shadedLuminance, eyeDistance;
        }
        [Serializable] sealed class Report
        {
            public bool success, baseline, responseValidated, restoredContext;
            public string error;
            public string scope = "Production ApplyFishSwimShader/GetOrCreateBoidMaterials with real Fish/SchoolFish sources. Fixed poses and materials, actual Mobile URP draws, only the real directional-light shadow toggles within each pair. Inactive world host prevents Awake/entry/network. No player JSON, animation acceptance, Android stereo, GPU timing or wearer claim.";
            public int assertions;
            public List<Pair> pairs = new List<Pair>();
        }

        sealed class Factory : IDisposable
        {
            public readonly UnderwaterWorld world;
            readonly GameObject host;
            readonly Material template;
            readonly List<GameObject> models = new List<GameObject>();
            readonly UnityEngine.Random.State random = UnityEngine.Random.state;
            readonly HashSet<Material> materials = new HashSet<Material>();
            public Factory()
            {
                host = new GameObject("Inactive fish material factory"); host.SetActive(false);
                world = host.AddComponent<UnderwaterWorld>();
                Shader shader = Resources.Load<Shader>("Shaders/FishSwim");
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Production fish shader unavailable.");
                template = new Material(shader); materials.Add(template);
                typeof(UnderwaterWorld).GetField("fishSwimTemplate", Private).SetValue(world, template);
            }
            public GameObject Hero(string resource)
            {
                GameObject prefab = Resources.Load<GameObject>(resource);
                if (prefab == null) throw new InvalidOperationException("Fish source unavailable: " + resource);
                GameObject model = Object.Instantiate(prefab); models.Add(model);
                typeof(UnderwaterWorld).GetMethod("ApplyFishSwimShader", Private).Invoke(world, new object[] { model, prefab.name });
                return model;
            }
            public Material[] SchoolMaterials(GameObject prefab) =>
                (Material[])typeof(UnderwaterWorld).GetMethod("GetOrCreateBoidMaterials", Private).Invoke(world, new object[] { prefab });
            public GameObject School(string resource)
            {
                GameObject prefab = Resources.Load<GameObject>(resource);
                if (prefab == null) throw new InvalidOperationException("School source unavailable: " + resource);
                GameObject model = Object.Instantiate(prefab); models.Add(model);
                Material[] selected = SchoolMaterials(prefab);
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = selected;
                return model;
            }
            public void Dispose()
            {
                foreach (var model in models) if (model != null) Object.DestroyImmediate(model);
                if (world != null)
                {
                    var heroes = (Dictionary<string, Material>)typeof(UnderwaterWorld).GetField("heroMaterialCache", Private).GetValue(world);
                    foreach (var value in heroes.Values) if (value != null) materials.Add(value);
                    var schools = (Dictionary<string, Material[]>)typeof(UnderwaterWorld).GetField("boidMaterialCache", Private).GetValue(world);
                    foreach (var array in schools.Values) foreach (var value in array) if (value != null) materials.Add(value);
                }
                foreach (var material in materials) if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
                if (host != null) Object.DestroyImmediate(host);
                UnityEngine.Random.state = random;
            }
        }

        // Deterministic factory checks can join a headless regression run; no GPU claim.
        public static void RunAll()
        {
            var report = new Report();
            using (var factory = new Factory()) CheckFactories(factory, report);
            Debug.Log("FISH_LIGHTING_MATERIAL_CHECKS_PASS assertions=" + report.assertions + "; actual hero/task and school material factories, no GPU or device claim.");
        }

        static void CheckFactories(Factory factory, Report report)
        {
            foreach (string resource in new[] { "Fish/01_clownfish", "Fish/02_blue_tang", "Fish/15_great_white_shark" })
            {
                GameObject hero = factory.Hero(resource);
                Renderer[] renderers = hero.GetComponentsInChildren<Renderer>(true);
                Check(renderers.Length > 0, "real hero/task source has renderers", report);
                foreach (Renderer renderer in renderers)
                    foreach (Material material in renderer.sharedMaterials)
                        Check(material != null && material.passCount == 1 && material.enableInstancing &&
                            !material.IsKeywordEnabled(Simple) && (report.baseline || material.IsKeywordEnabled(Receive)) && renderer.receiveShadows,
                            "hero/task uses one-pass instanced detailed shadow receiver: " + resource, report);
                hero.SetActive(false);
            }
            foreach (string resource in new[] { "SchoolFish/01_clownfish", "SchoolFish/02_blue_tang", "SchoolFish/23_mandarinfish", "Fish/15_great_white_shark" })
            {
                GameObject prefab = Resources.Load<GameObject>(resource);
                Check(prefab != null, "actual cached school/patrol source exists", report);
                Material[] selected = factory.SchoolMaterials(prefab);
                Check(ReferenceEquals(selected, factory.SchoolMaterials(prefab)), "school material array remains cached", report);
                foreach (Material material in selected)
                    Check(material != null && material.passCount == 1 && material.enableInstancing && !material.IsKeywordEnabled(Receive) &&
                        (!resource.StartsWith("SchoolFish/", StringComparison.Ordinal) || material.IsKeywordEnabled(Simple)),
                        "school/patrol factory never inherits the hero shadow keyword: " + resource, report);
            }
        }

        // Fresh graphical batch Editor, omit -quit/-nographics. Owns process exit.
        // -executeMethod SwanUnderwater.Editor.OceanFishLightingPreview.Capture
        // -oceanFishLightingOutput <fresh directory> [-oceanFishLightingBaseline]
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use a fresh graphical batch Editor in Edit Mode.");
            string output = "Validation/fish-lighting-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            var report = new Report();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Preview owns exit and requires graphics.");
                if (args[i] == "-oceanFishLightingOutput" && i + 1 < args.Length) output = args[++i];
                if (args[i] == "-oceanFishLightingBaseline") report.baseline = true;
            }
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0) throw new IOException("Use a fresh output directory.");
            Directory.CreateDirectory(output);
            var oldQuality = QualitySettings.renderPipeline; var oldDefault = GraphicsSettings.defaultRenderPipeline;
            Scene oldScene = SceneManager.GetActiveScene(), scene = default;
            bool oldFog = RenderSettings.fog, oldAsync = ShaderUtil.allowAsyncCompilation;
            Material oldSky = RenderSettings.skybox; Light oldSun = RenderSettings.sun;
            AmbientMode oldAmbient = RenderSettings.ambientMode; Color oldLight = RenderSettings.ambientLight;
            SphericalHarmonicsL2 oldProbe = RenderSettings.ambientProbe;
            Factory factory = null; UniversalRenderPipelineAsset pipeline = null;
            RenderTexture target = null; Texture2D pixels = null;
            var owned = new List<Material>();
            try
            {
                scene = EditorSceneManager.NewPreviewScene(); ShaderUtil.allowAsyncCompilation = false;
                pipeline = Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
                QualitySettings.renderPipeline = pipeline; GraphicsSettings.defaultRenderPipeline = pipeline;
                RenderSettings.fog = false; RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.12f, .16f, .20f);
                var probe = new SphericalHarmonicsL2(); probe.AddAmbientLight(RenderSettings.ambientLight); RenderSettings.ambientProbe = probe;
                var root = new GameObject("Unsaved actual fish shadow diagnostic"); SceneManager.MoveGameObjectToScene(root, scene);
                var camera = new GameObject("Fixed fish lighting camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.enabled = false; camera.scene = scene; camera.orthographic = true; camera.orthographicSize = .65f;
                camera.aspect = 16f / 9f; camera.nearClipPlane = .02f; camera.farClipPlane = 80f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var sun = new GameObject("Only fish diagnostic main light").AddComponent<Light>(); sun.transform.SetParent(root.transform);
                sun.type = LightType.Directional; sun.transform.rotation = Quaternion.LookRotation(new Vector3(.35f, -.30f, 1f));
                sun.intensity = 1.3f; sun.color = new Color(1f, .95f, .85f); OceanReefShadowPolicy.ConfigureSun(sun); RenderSettings.sun = sun;
                Vector3 center = new Vector3(0, 1f, 0);
                var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); caster.transform.SetParent(root.transform);
                caster.transform.position = center - sun.transform.forward * 1.2f;
                caster.transform.rotation = sun.transform.rotation; caster.transform.localScale = new Vector3(2.2f, 2.2f, .15f);
                Object.DestroyImmediate(caster.GetComponent<Collider>());
                var casterMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(casterMaterial);
                caster.GetComponent<MeshRenderer>().sharedMaterial = casterMaterial;
                caster.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32); target.Create();
                pixels = new Texture2D(640, 360, TextureFormat.RGB24, false); camera.targetTexture = target;
                factory = new Factory(); CheckFactories(factory, report);
                var hero = factory.Hero("Fish/01_clownfish"); Prepare(hero, root.transform, center);
                foreach (int cascades in new[] { 1, 2 }) foreach (bool distant in new[] { false, true })
                {
                    pipeline.shadowCascadeCount = cascades;
                    CapturePair(hero, "Fish/01_clownfish", "hero", distant, true, camera, sun, pipeline, target, pixels, output, report);
                }
                // The same real full-detail fish/material is a negative control when opted out.
                foreach (Renderer renderer in hero.GetComponentsInChildren<Renderer>()) foreach (Material source in renderer.sharedMaterials) source.DisableKeyword(Receive);
                CapturePair(hero, "Fish/01_clownfish", "hero-optout", false, false, camera, sun, pipeline, target, pixels, output, report);
                hero.SetActive(false);
                foreach (string name in new[] { "01_clownfish", "02_blue_tang", "23_mandarinfish" })
                {
                    var school = factory.School("SchoolFish/" + name); Prepare(school, root.transform, center);
                    CapturePair(school, "SchoolFish/" + name, "school", false, false, camera, sun, pipeline, target, pixels, output, report);
                    if (name == "01_clownfish" && !report.baseline)
                    {
                        // Test the shader's compile-time simple-school exclusion independently
                        // of the factory guard, without changing a production/shared source asset.
                        foreach (Renderer renderer in school.GetComponentsInChildren<Renderer>()) foreach (Material material in renderer.sharedMaterials) material.EnableKeyword(Receive);
                        CapturePair(school, "SchoolFish/" + name, "school-forced-keyword", false, false, camera, sun, pipeline, target, pixels, output, report);
                    }
                    school.SetActive(false);
                }
                report.responseValidated = !report.baseline; report.success = true;
            }
            catch (Exception error) { report.error = error.ToString(); Debug.LogException(error); }
            finally
            {
                QualitySettings.renderPipeline = oldQuality; GraphicsSettings.defaultRenderPipeline = oldDefault;
                RenderSettings.fog = oldFog; RenderSettings.skybox = oldSky; RenderSettings.sun = oldSun;
                RenderSettings.ambientMode = oldAmbient; RenderSettings.ambientLight = oldLight; RenderSettings.ambientProbe = oldProbe;
                ShaderUtil.allowAsyncCompilation = oldAsync;
                if (factory != null) factory.Dispose();
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                foreach (Material material in owned) if (material != null) Object.DestroyImmediate(material);
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (pipeline != null) Object.DestroyImmediate(pipeline);
                report.restoredContext = QualitySettings.renderPipeline == oldQuality && GraphicsSettings.defaultRenderPipeline == oldDefault && SceneManager.GetActiveScene() == oldScene;
                if (!report.restoredContext) report.success = false;
                File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("FISH_LIGHTING_PREVIEW_" + (report.success ? "PASS" : "FAIL") + " baseline=" + report.baseline + " pairs=" + report.pairs.Count);
            EditorApplication.Exit(report.success ? 0 : 1);
        }

        static void Prepare(GameObject model, Transform parent, Vector3 center)
        {
            model.transform.SetParent(parent, false); model.transform.rotation = Quaternion.Euler(0, 60f, 0);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds; foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.transform.localScale *= .95f / Mathf.Max(.001f, bounds.size.magnitude);
            bounds = renderers[0].bounds; foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            model.transform.position += center - bounds.center;
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                foreach (Material material in renderer.sharedMaterials)
                { material.SetFloat("_SwingAmp", 0f); material.SetFloat("_VerticalBob", 0f); }
            }
        }

        static void CapturePair(GameObject model, string resource, string mode, bool distant, bool receive, Camera camera, Light sun,
            UniversalRenderPipelineAsset pipeline, RenderTexture target, Texture2D pixels, string output, Report report)
        {
            Vector3 center = new Vector3(0, 1f, 0); float distance = distant ? pipeline.shadowDistance + 8f : 3f;
            camera.transform.SetPositionAndRotation(center + new Vector3(-.20f, .06f, -1f).normalized * distance,
                Quaternion.LookRotation(new Vector3(.20f, -.06f, 1f)));
            Material first = model.GetComponentInChildren<Renderer>().sharedMaterial;
            var pair = new Pair { resource = resource, mode = mode, distant = distant, cascades = pipeline.shadowCascadeCount,
                shadowExpected = receive && !distant, eyeDistance = distance, receiveKeyword = first.IsKeywordEnabled(Receive), simpleKeyword = first.IsKeywordEnabled(Simple) };
            string stem = Path.GetFileName(resource) + "-" + mode + "-cascade" + pair.cascades + (distant ? "-far" : "-near");
            sun.shadows = LightShadows.None; Color32[] lit = Read(camera, target, pixels);
            pair.litImage = stem + "-lit.png"; File.WriteAllBytes(Path.Combine(output, pair.litImage), pixels.EncodeToPNG());
            sun.shadows = LightShadows.Soft; Color32[] shaded = Read(camera, target, pixels);
            pair.shadowImage = stem + "-shadow.png"; File.WriteAllBytes(Path.Combine(output, pair.shadowImage), pixels.EncodeToPNG());
            long difference = 0, shadedSum = 0;
            for (int i = 0; i < lit.Length; i++)
            {
                if (lit[i].r + lit[i].g + lit[i].b < 20) continue;
                pair.receiverPixels++; int delta = Math.Abs(lit[i].r - shaded[i].r) + Math.Abs(lit[i].g - shaded[i].g) + Math.Abs(lit[i].b - shaded[i].b);
                difference += delta; shadedSum += shaded[i].r + shaded[i].g + shaded[i].b;
                if (delta > 9) pair.changedPixels++;
            }
            pair.averageDifference = pair.receiverPixels > 0 ? difference / (pair.receiverPixels * 3f) : 0f;
            pair.shadedLuminance = pair.receiverPixels > 0 ? shadedSum / (pair.receiverPixels * 3f) : 0f;
            report.pairs.Add(pair);
            Check(pair.receiverPixels > 500, "real fish has meaningful raster coverage: " + stem, report);
            if (pair.shadowExpected && !report.baseline)
                Check(pair.changedPixels > 100 && pair.averageDifference > 2f && pair.shadedLuminance > 2f, "near hero responds to a real caster and retains indirect light: " + stem, report);
            else Check(pair.averageDifference < 2f, "far/opted-out/simple fish does not retain an occluder shadow: " + stem, report);
        }

        static Color32[] Read(Camera camera, RenderTexture target, Texture2D pixels)
        {
            for (int i = 0; i < 2; i++) RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); return pixels.GetPixels32(); }
            finally { RenderTexture.active = previous; }
        }
        static void Check(bool value, string description, Report report)
        { report.assertions++; if (!value) throw new InvalidOperationException("Fish lighting: " + description); }
    }
}
#endif
