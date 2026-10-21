#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Production world-coin scale and effect in a temporary scene, with the same camera passed to Refresh and rendering.</summary>
    [InitializeOnLoad]
    public static class CoinSparklePreview
    {
        const string Key = "SwanCoinSparklePreview.";
        const int Resolution = 640;
        static readonly double[] SampleTimes = { 0.0, .8, 1.6 };
        [Serializable] sealed class Frame
        {
            public string file;
            public float seconds, shaderClock, distance, proximity;
            public bool rendererEnabled;
        }
        [Serializable] sealed class Report
        {
            public bool success, shaderSupported, shaderHasError, farDisabled;
            public int assertions, surfaceTriangles, effectTriangles, effectVertices;
            public float worldCoinDiameter, nearDistance = .95f, farDistance = 3f;
            public string shader, shaderMessages, detail;
            public string evidence = "Actual CreateCoin/WorldCoinDiameter/CoinProximitySparkles.Create and Refresh(camera, proximity), in an unsaved flat-floor Editor scene. Every capture refreshes against the same independent 640x640 mono camera before rendering. Three near samples target 0, 0.8 and 1.6 seconds; actual timestamps are recorded, followed by one disabled far sample. This isolates glint visibility; it does not verify real-world placement, PICO stereo, occlusion by the actual reef, wearer perception or performance.";
            public List<Frame> frames = new List<Frame>();
        }
        static Report report = new Report();
        static Camera view;
        static GameObject coin, floor, lightObject;
        static Renderer coinRenderer, effectRenderer;
        static CoinProximitySparkles effect;
        static Material floorMaterial;
        static RenderTexture target;
        static Texture2D pixels;
        static StreamWriter csv;
        static double started;
        static int nearFrames;
        static int lastUnityFrame = -1;
        static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        static string Output => SessionState.GetString(Key + "output", "");
        static CoinSparklePreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }

        // -executeMethod SwanUnderwater.Editor.CoinSparklePreview.Capture
        // Omit -quit/-nographics; optional -codexCoinSparkleOutput <fresh output directory>.
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use a separate batch Editor starting in Edit Mode.");
            string output = "Validation/iteration-14/coin-glints-preview";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit quit/nographics; this capture renders and owns exit.");
                if (args[i] == "-codexCoinSparkleOutput" && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            if (Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length > 0 || File.Exists(Path.Combine(output, "report.json")))
                throw new IOException("Use a fresh output directory.");
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "wall", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(Key + "error", "");
            SessionState.SetBool(Key + "active", true);
            Attach(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Error; Application.logMessageReceived += Error;
        }
        static void Error(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetString(Key + "error", message + "\n" + stack);
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "wall", "0"), CultureInfo.InvariantCulture) > TimeSpan.FromSeconds(90).Ticks)
                    throw new TimeoutException("Coin preview exceeded 90 seconds.");
                string error = SessionState.GetString(Key + "error", "");
                if (error.Length > 0) throw new InvalidOperationException(error);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (EditorApplication.isPaused) throw new InvalidOperationException("Play Mode paused during coin preview.");
                if (view == null) { Build(); return; }
                if (lastUnityFrame == Time.frameCount) return;
                lastUnityFrame = Time.frameCount;
                double elapsed = Time.timeAsDouble - started;
                if (nearFrames < SampleTimes.Length && elapsed >= SampleTimes[nearFrames])
                {
                    Save("frames/frame-" + report.frames.Count.ToString("D3") + ".png", elapsed, true);
                    nearFrames++;
                }
                if (nearFrames < SampleTimes.Length) return;
                SetDistance(3f);
                Save("far-3m-disabled.png", elapsed, false);
                report.farDisabled = !effectRenderer.enabled;
                Require(report.farDisabled, "three-metre sparkle renderer disabled");
                CheckShader();
                Finish(true, "Captured near-coin glints with matching Refresh/render camera, then disabled effect at three metres.");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static void Build()
        {
            view = new GameObject("Coin glint preview camera").AddComponent<Camera>();
            view.enabled = false; view.fieldOfView = 65f; view.aspect = 1f;
            view.nearClipPlane = .03f; view.farClipPlane = 12f;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.045f, .15f, .17f);
            view.GetUniversalAdditionalCameraData().allowXRRendering = false;
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32); target.Create();
            pixels = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Temporary opaque sand-colour floor";
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.32f, .38f, .31f); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            lightObject = new GameObject("Preview lighting");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.1f; light.shadows = LightShadows.None; light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientLight = new Color(.32f, .40f, .42f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .038f; RenderSettings.fogColor = view.backgroundColor;
            coin = CollectibleVisuals.CreateCoin(null);
            coin.transform.localScale = Vector3.one * (TreasureHunt.WorldCoinDiameter / CollectibleVisuals.CoinDiameter);
            coin.transform.position = Vector3.down * .002f;
            coinRenderer = coin.GetComponent<Renderer>();
            effect = CoinProximitySparkles.Create(coin.transform);
            Require(effect != null, "production sparkle component created");
            effectRenderer = effect.GetComponent<Renderer>();
            Require(effectRenderer != null && effectRenderer.sharedMaterial != null, "production effect renderer and material exist");
            Mesh mesh = effect.GetComponent<MeshFilter>().sharedMesh;
            report.effectTriangles = (int)mesh.GetIndexCount(0) / 3; report.effectVertices = mesh.vertexCount;
            report.surfaceTriangles = (int)coin.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3;
            report.worldCoinDiameter = coinRenderer.bounds.size.x;
            Require(Mathf.Abs(report.worldCoinDiameter - TreasureHunt.WorldCoinDiameter) < .0001f, "production world coin diameter applied");
            CheckShader(); SetDistance(.95f);
            csv = new StreamWriter(Path.Combine(Output, "frames.csv"));
            csv.WriteLine("file,seconds,shaderClock,distance,proximity,rendererEnabled,effectTriangles,effectVertices");
            started = Time.timeAsDouble; nearFrames = 0;
        }

        static void SetDistance(float distance)
        {
            Vector3 direction = new Vector3(0f, Mathf.Sin(52f * Mathf.Deg2Rad), -Mathf.Cos(52f * Mathf.Deg2Rad));
            view.transform.position = coinRenderer.bounds.center + direction * distance;
            view.transform.rotation = Quaternion.LookRotation(coinRenderer.bounds.center - view.transform.position, Vector3.up);
        }
        static void CheckShader()
        {
            Shader shader = effectRenderer.sharedMaterial.shader;
            report.shader = shader.name; report.shaderSupported = shader.isSupported;
            report.shaderHasError = ShaderUtil.ShaderHasError(shader);
            var messages = new List<string>();
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) messages.Add(message.message);
            report.shaderMessages = string.Join("\n", messages);
            Require(report.shaderSupported && !report.shaderHasError, "production sparkle shader supported without compiler errors");
        }
        static void Save(string file, double elapsed, bool expectedEnabled)
        {
            float distance = Vector3.Distance(view.transform.position, coinRenderer.bounds.center);
            float near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TreasureHunt.RevealRadius, .70f, distance));
            effect.Refresh(view, near);
            Require(effectRenderer.enabled == expectedEnabled, "actual sparkle renderer state before " + file);
            effectRenderer.GetPropertyBlock(properties);
            var sample = new Frame { file = file, seconds = (float)elapsed, shaderClock = properties.GetFloat("_Clock"),
                distance = distance, proximity = near, rendererEnabled = effectRenderer.enabled };
            RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(Output, file), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; }
            report.frames.Add(sample);
            csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5},{6},{7}",
                file, sample.seconds, sample.shaderClock, distance, near, sample.rendererEnabled, report.effectTriangles, report.effectVertices));
        }
        static void Require(bool value, string description)
        { report.assertions++; if (!value) throw new InvalidOperationException("Coin sparkle preview: " + description); }
        static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            csv?.Dispose(); csv = null; report.success = success; report.detail = detail;
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            if (effect != null) effect.Dispose();
            if (coin != null) Object.Destroy(coin);
            if (floor != null) Object.Destroy(floor);
            if (floorMaterial != null) Object.Destroy(floorMaterial);
            if (lightObject != null) Object.Destroy(lightObject);
            if (view != null) Object.Destroy(view.gameObject);
            if (pixels != null) Object.Destroy(pixels);
            if (target != null) { target.Release(); Object.Destroy(target); }
            Debug.Log((success ? "COIN_SPARKLE_PREVIEW_SUCCEEDED: " : "COIN_SPARKLE_PREVIEW_FAILED: ") + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
