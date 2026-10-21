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
using UnityEngine.UI;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Exercises the project's real water-wake emitter using synthetic palm inputs.
    /// Reflection is limited to our own HandWaterWake component; no SDK is called.</summary>
    public static class HandWakeValidation
    {
        private const float Step = 1f / 90f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo StartMethod = typeof(HandWaterWake).GetMethod("Start", PrivateInstance);
        private static readonly MethodInfo EmitMethod = typeof(HandWaterWake).GetMethod("Emit", BindingFlags.Static | BindingFlags.NonPublic);

        // Separate Editor process: -executeMethod SwanUnderwater.Editor.HandWakeValidation.Run
        // Graphics are required. No Play Mode is entered; this method owns batch exit 0/1.
        [MenuItem("Swan/Codex/Validate and capture synthetic hand water wake")]
        public static void Run()
        {
            string output = Path.GetFullPath(Argument("-handWakeOutput", Path.Combine("Validation", "iteration-2",
                "hand-wake", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"))));
            Directory.CreateDirectory(output);
            var report = new Summary { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, output = output,
                scope = "Synthetic palm trajectories in an isolated Editor scene, using actual HandWaterWake.Start/Create/Emit, shader and particle configuration. No live PXR data or tracking validation.",
                clockScope = "1/90 second manual particle simulation. The private Emit ref clock is compensated by syntheticDt - Time.deltaTime so the unchanged emitter evaluates the synthetic interval.",
                trajectory = "Two mirrored palms: x=side*(0.22+0.045*sin(4t)), y=0.012*sin(3t), z=-0.55+0.85t metres; velocity is the analytic derivative; strength=0.8; duration=0.7 seconds.",
                simulationStepSeconds = Step };
            Scene preview = default;
            GameObject host = null;
            HandWaterWake wake = null;
            Material wakeMaterial = null, gloveMaterial = null;
            Mesh wakeMesh = null;
            ParticleSystem left = null, right = null;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Water wake validation requires Edit Mode.");
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                if (XRSettings.isDeviceActive || displays.Exists(display => display.running))
                    throw new InvalidOperationException("Water wake validation refuses active XR.");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Graphics required; omit -nographics.");
                if (StartMethod == null || EmitMethod == null)
                    throw new MissingMethodException("The project's HandWaterWake Start/Emit interface changed.");

                preview = EditorSceneManager.NewPreviewScene();
                // Create an inert controller stub in the current scene just long enough for the
                // real Start's FindFirstObjectByType guard. Edit Mode never calls its Start/Update,
                // and the whole temporary hierarchy immediately moves into the preview scene.
                host = new GameObject("Synthetic water wake validation");
                host.AddComponent<HandSwimController>();
                wake = host.AddComponent<HandWaterWake>();
                StartMethod.Invoke(wake, null);
                left = Read<ParticleSystem>(wake, "left");
                right = Read<ParticleSystem>(wake, "right");
                wakeMaterial = Read<Material>(wake, "material");
                wakeMesh = Read<Mesh>(wake, "quad");
                if (left == null || right == null || wakeMaterial == null || wakeMesh == null)
                    throw new InvalidOperationException("Actual HandWaterWake.Start did not create both particle systems.");
                SceneManager.MoveGameObjectToScene(host, preview);
                report.shader = wakeMaterial.shader.name;
                report.configuredMaxParticlesPerHand = left.main.maxParticles;
                report.worldSimulation = left.main.simulationSpace == ParticleSystemSimulationSpace.World &&
                    right.main.simulationSpace == ParticleSystemSimulationSpace.World;
                if (report.shader != "SwanUnderwater/HandWake" || !report.worldSimulation)
                    report.errors.Add("Unexpected runtime water material or simulation space.");

                // Manual simulation and an explicit seed avoid wall-time/culling-dependent output.
                left.useAutoRandomSeed = right.useAutoRandomSeed = false;
                left.randomSeed = 4201; right.randomSeed = 4202;
                left.Simulate(0, false, true, false); right.Simulate(0, false, true, false);
                CheckNonEmission(left, report);
                CheckBoundedLifetime(left, right, report);

                Shader gloveShader = Shader.Find("Universal Render Pipeline/Lit");
                if (gloveShader == null) throw new InvalidOperationException("Existing URP Lit shader missing.");
                gloveMaterial = new Material(gloveShader) { name = "Static glove for synthetic wake context" };
                gloveMaterial.SetColor("_BaseColor", new Color(0.10f, 0.24f, 0.27f));
                gloveMaterial.SetFloat("_Smoothness", 0.32f);
                gloveMaterial.SetFloat("_Metallic", 0.05f);
                GameObject leftGlove = CreateGlove(true, preview, gloveMaterial);
                GameObject rightGlove = CreateGlove(false, preview, gloveMaterial);
                leftGlove.SetActive(false); rightGlove.SetActive(false);

                Camera camera = CreateCamera(preview);
                Color32[] baseline = Capture(camera, Path.Combine(output, "empty-reference.png"));
                float leftClock = 0, rightClock = 0;
                const int steps = 63;
                for (int i = 1; i <= steps; i++)
                {
                    float t = i * Step;
                    Simulate(left, right, Step);
                    InvokeEmit(left, true, Palm(-1, t), Velocity(-1, t), 0.8f, ref leftClock, Step);
                    InvokeEmit(right, true, Palm(1, t), Velocity(1, t), 0.8f, ref rightClock, Step);
                }
                // Give the newest emitted particles a nonzero age so their real fade-in applies.
                Simulate(left, right, 0.04f);
                report.visibleLeftParticles = left.particleCount;
                report.visibleRightParticles = right.particleCount;
                report.movingProducesParticles = report.visibleLeftParticles > 0 && report.visibleRightParticles > 0;
                if (!report.movingProducesParticles) report.errors.Add("Moving tracked palms produced no particles.");
                Color32[] moving = Capture(camera, Path.Combine(output, "moving-wake-only.png"));
                report.visibleWakeChangedPixels = ChangedPixels(baseline, moving);
                report.visibleWakePixelFraction = (float)report.visibleWakeChangedPixels / moving.Length;
                report.wakeVisibleInImage = report.visibleWakeChangedPixels >= 150;
                if (!report.wakeVisibleInImage) report.errors.Add("Particles exist but the wake image is not visibly different from the empty reference.");

                PositionGlove(leftGlove, true, Palm(-1, steps * Step));
                PositionGlove(rightGlove, false, Palm(1, steps * Step));
                leftGlove.SetActive(true); rightGlove.SetActive(true);
                Capture(camera, Path.Combine(output, "moving-with-hands.png"));
                leftGlove.SetActive(false); rightGlove.SetActive(false);

                // Test no new particles while old ones are still present, without advancing age.
                int beforeQuiet = left.particleCount + right.particleCount;
                leftClock = rightClock = 1;
                InvokeEmit(left, true, Palm(-1, 0.7f), Vector3.zero, 0, ref leftClock, Step);
                InvokeEmit(right, false, Palm(1, 0.7f), Vector3.forward * 1.4f, 1, ref rightClock, Step);
                report.existingTailNotReemitted = beforeQuiet == left.particleCount + right.particleCount && leftClock == 0 && rightClock == 0;
                if (!report.existingTailNotReemitted) report.errors.Add("Stationary/lost tracking added particles to an existing tail.");
                Simulate(left, right, 1.2f);
                report.particlesAfterLifetime = left.particleCount + right.particleCount;
                report.lifetimeCleared = report.particlesAfterLifetime == 0;
                if (!report.lifetimeCleared) report.errors.Add("Particles remained after their maximum configured emitted lifetime.");
                Color32[] cleared = Capture(camera, Path.Combine(output, "after-lifetime.png"));
                report.clearedChangedPixels = ChangedPixels(baseline, cleared);
                report.passed = report.errors.Count == 0;
            }
            catch (Exception e)
            {
                report.errors.Add(e is TargetInvocationException && e.InnerException != null ? e.InnerException.ToString() : e.ToString());
                report.passed = false;
            }
            finally
            {
                // Runtime OnDestroy uses deferred Destroy, which is not appropriate in Edit Mode.
                // Detach only our component's owned references, then release the same objects now.
                if (wake != null)
                {
                    Write(wake, "left", null); Write(wake, "right", null);
                    Write(wake, "material", null); Write(wake, "quad", null);
                }
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (host != null) Object.DestroyImmediate(host);
                if (wakeMaterial != null) Object.DestroyImmediate(wakeMaterial);
                if (wakeMesh != null) Object.DestroyImmediate(wakeMesh);
                if (gloveMaterial != null) Object.DestroyImmediate(gloveMaterial);
                File.WriteAllText(Path.Combine(output, "summary.json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(output, "scope.txt"), report.scope + "\n" + report.clockScope + "\n" + report.trajectory +
                    "\nScreenshots: moving-wake-only proves visible real emitter output; moving-with-hands adds imported static bind-pose gloves for context; after-lifetime confirms clearing.\n" +
                    "No PXR input, headset image, dynamic finger pose, or subjective water/comfort acceptance is represented.\n");
            }
            Debug.Log("CODEX_HAND_WAKE_VALIDATION: " + (report.passed ? "PASS " : "FAIL ") + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
            else if (!report.passed) throw new InvalidOperationException(string.Join("\n", report.errors));
        }

        private static void CheckNonEmission(ParticleSystem system, Summary report)
        {
            system.Clear();
            float clock = 1;
            for (int i = 0; i < 90; i++) InvokeEmit(system, true, Vector3.zero, Vector3.zero, 0, ref clock, Step);
            report.stationaryParticles = system.particleCount;
            report.stationaryNoEmission = report.stationaryParticles == 0 && clock == 0;
            clock = 1;
            for (int i = 0; i < 90; i++) InvokeEmit(system, false, Vector3.zero, Vector3.forward * 1.4f, 1, ref clock, Step);
            report.lostTrackingParticles = system.particleCount;
            report.lostTrackingNoEmission = report.lostTrackingParticles == 0 && clock == 0;
            if (!report.stationaryNoEmission || !report.lostTrackingNoEmission)
                report.errors.Add("Stationary or lost-tracking input emitted particles.");
        }

        private static void CheckBoundedLifetime(ParticleSystem left, ParticleSystem right, Summary report)
        {
            float lc = 0, rc = 0;
            const int steps = 1800;
            report.boundedSimulationSeconds = steps * Step;
            for (int i = 0; i < steps; i++)
            {
                Simulate(left, right, Step);
                InvokeEmit(left, true, Vector3.left * 0.2f, Vector3.forward * 1.4f, 1, ref lc, Step);
                InvokeEmit(right, true, Vector3.right * 0.2f, Vector3.forward * 1.4f, 1, ref rc, Step);
                report.peakLeftParticles = Math.Max(report.peakLeftParticles, left.particleCount);
                report.peakRightParticles = Math.Max(report.peakRightParticles, right.particleCount);
            }
            report.bounded = report.peakLeftParticles > 0 && report.peakRightParticles > 0 &&
                report.peakLeftParticles <= left.main.maxParticles && report.peakRightParticles <= right.main.maxParticles;
            if (!report.bounded) report.errors.Add("Sustained actual emitter output exceeded the configured bound or was empty.");
            left.Clear(); right.Clear();
        }

        private static void InvokeEmit(ParticleSystem system, bool tracked, Vector3 palm, Vector3 velocity, float strength, ref float clock, float dt)
        {
            object[] arguments = { system, tracked, palm, velocity, strength, clock + dt - Time.deltaTime };
            EmitMethod.Invoke(null, arguments);
            clock = (float)arguments[5];
        }
        private static void Simulate(ParticleSystem left, ParticleSystem right, float seconds)
        {
            left.Simulate(seconds, false, false, false);
            right.Simulate(seconds, false, false, false);
        }
        private static Vector3 Palm(float side, float t) => new Vector3(side * (0.22f + 0.045f * Mathf.Sin(4 * t)), 0.012f * Mathf.Sin(3 * t), -0.55f + 0.85f * t);
        private static Vector3 Velocity(float side, float t) => new Vector3(side * 0.18f * Mathf.Cos(4 * t), 0.036f * Mathf.Cos(3 * t), 0.85f);

        private static GameObject CreateGlove(bool leftSide, Scene scene, Material material)
        {
            string path = "Assets/Resources/DiverHands/" + (leftSide ? "LeftHand" : "RightHand") + ".fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new FileNotFoundException("Static context glove is missing", path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterial = material;
                renderer.updateWhenOffscreen = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return model;
        }
        private static void PositionGlove(GameObject model, bool leftSide, Vector3 palm)
        {
            Transform[] bones = DiverHandVisuals.MapBones(model, leftSide);
            model.transform.rotation = Quaternion.Euler(-60, 0, 0) * Quaternion.Inverse(bones[1].rotation) * model.transform.rotation;
            model.transform.position += palm - bones[0].position;
        }

        private static Camera CreateCamera(Scene scene)
        {
            var host = new GameObject("Synthetic wake comparison camera");
            SceneManager.MoveGameObjectToScene(host, scene);
            Camera camera = host.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.07f, 0.095f);
            camera.orthographic = true;
            camera.orthographicSize = 0.32f;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.001f; camera.farClipPlane = 10;
            camera.transform.position = new Vector3(0, 0.22f, 1.2f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, -0.1f) - camera.transform.position, Vector3.up);
            var additional = camera.GetUniversalAdditionalCameraData();
            additional.allowXRRendering = false; additional.renderPostProcessing = false;
            var lightHost = new GameObject("Synthetic glove key light");
            SceneManager.MoveGameObjectToScene(lightHost, scene);
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.None;
            light.transform.rotation = camera.transform.rotation * Quaternion.Euler(20, -20, 0);

            var caption = new GameObject("Synthetic input label", typeof(RectTransform), typeof(Canvas));
            caption.transform.SetParent(camera.transform, false);
            var rect = (RectTransform)caption.transform;
            rect.sizeDelta = new Vector2(1000, 48);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(0, 0.278f, 0.5f);
            Canvas canvas = caption.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            var textHost = new GameObject("Scope", typeof(RectTransform), typeof(Text));
            textHost.transform.SetParent(caption.transform, false);
            var tr = (RectTransform)textHost.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            Text label = textHost.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 21;
            label.text = "SYNTHETIC PALM TRAJECTORY  |  EDITOR ONLY  |  NO PXR INPUT";
            label.color = new Color(0.65f, 0.85f, 0.9f); label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            Canvas.ForceUpdateCanvases();
            return camera;
        }

        private static Color32[] Capture(Camera camera, string path)
        {
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                else camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
            }
        }
        private static int ChangedPixels(Color32[] first, Color32[] second)
        {
            int changed = 0;
            for (int i = 0; i < first.Length; i++)
                if (Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) + Math.Abs(first[i].b - second[i].b) > 12) changed++;
            return changed;
        }
        private static T Read<T>(HandWaterWake wake, string name) where T : class => (T)typeof(HandWaterWake).GetField(name, PrivateInstance).GetValue(wake);
        private static void Write(HandWaterWake wake, string name, object value) => typeof(HandWaterWake).GetField(name, PrivateInstance).SetValue(wake, value);
        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        [Serializable] private sealed class Summary
        {
            public string utc, unity, output, scope, clockScope, trajectory, shader;
            public bool passed, worldSimulation, stationaryNoEmission, lostTrackingNoEmission, bounded,
                movingProducesParticles, wakeVisibleInImage, existingTailNotReemitted, lifetimeCleared;
            public float simulationStepSeconds, boundedSimulationSeconds, visibleWakePixelFraction;
            public int configuredMaxParticlesPerHand, stationaryParticles, lostTrackingParticles, peakLeftParticles,
                peakRightParticles, visibleLeftParticles, visibleRightParticles, visibleWakeChangedPixels,
                particlesAfterLifetime, clearedChangedPixels;
            public List<string> errors = new List<string>();
        }
    }
}
