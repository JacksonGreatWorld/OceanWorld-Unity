#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Feedback-only synthetic Editor scene: no XR rig, saved scene changes or headset acceptance.</summary>
    [InitializeOnLoad]
    public static class TreasureFeedbackPreview
    {
        const string Key = "TreasureFeedbackPreview.";
        const int Resolution = 640;
        static readonly double[] KeyTimes = { .35, .9, 1.0, 1.8, 2.2, 2.5, 3.1, 4.6, 6.5 };
        static readonly string[] KeyNames = { "peak-035", "peak-090", "coverage-100", "peak-180", "level-view-220", "yaw60-coverage-250", "peak-310", "peak-460", "cleared-650" };
        static readonly FieldInfo Matrices = typeof(TreasureFeedback).GetField("coinMatrices", BindingFlags.Instance | BindingFlags.NonPublic);
        [Serializable] sealed class Frame
        {
            public int frame, activeCoins, maxActiveCoins, visibleCoinCenters, occupiedGridCells;
            public int[] gridCounts;
            public float seconds, minimumCenterDepth, pitch, yaw;
            public string file;
        }
        [Serializable] sealed class Report
        {
            public bool success, clearedAtEnd, clearedWhenHidden;
            public int assertions, maxActiveCoins, frames;
            public float minimumCenterDepth = -1f;
            public string detail;
            public string evidence = "Actual production feedback in a synthetic empty Editor scene, rendered mono at 640x640. Only the helper's camera changes: initial pitch 52 degrees, level at 2.2s, yaw 60 degrees at 2.45s. Reflection reads existing coinMatrices only. Grid occupancy counts projected coin centers, not pixel coverage. Sampling targets 20Hz; CSV records actual times, not claimed runtime FPS. No real XR pose, headset stereo, audio or performance acceptance.";
            public List<Frame> samples = new List<Frame>();
        }
        static Report report = new Report();
        static Camera view;
        static TreasureFeedback feedback;
        static RenderTexture target;
        static Texture2D pixels;
        static StreamWriter csv;
        static double started, rewardStarted, nextFrame, interruptionStarted;
        static int stage, nextKey;
        static string Output => SessionState.GetString(Key + "output", "");
        static TreasureFeedbackPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }

        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch Editor.");
            string output = "Validation/iteration-14/feedback-preview";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit quit/nographics.");
                if (args[i] == "-codexFeedbackOutput" && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            if (Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length > 0) throw new IOException("Use a fresh output directory.");
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "wall", DateTime.UtcNow.Ticks.ToString());
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
        { if (type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "wall", "0")) > TimeSpan.FromSeconds(90).Ticks)
                    throw new TimeoutException("Feedback preview exceeded 90 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (view == null) Build();
                double now = Time.unscaledTimeAsDouble;
                report.maxActiveCoins = Math.Max(report.maxActiveCoins, feedback.ActiveCelebrationCoins);
                if (stage == 0 && now - started > .75) { Save("01-collection"); stage++; }
                else if (stage == 1 && now - started > 3.4)
                {
                    feedback.SetProgress(31); feedback.ShowHiddenReward(); rewardStarted = now;
                    nextFrame = 0; stage = 2;
                }
                else if (stage == 2)
                {
                    double elapsed = now - rewardStarted;
                    view.transform.rotation = elapsed < 2.2 ? Quaternion.Euler(52f, 0f, 0f) : Quaternion.Euler(0f, elapsed < 2.45 ? 0f : 60f, 0f);
                    if (elapsed >= nextFrame)
                    {
                        CaptureFrame(elapsed);
                        // Do not fabricate duplicate frames after a slow readback; retain actual timestamps.
                        nextFrame = (Math.Floor(elapsed * 20.0) + 1.0) / 20.0;
                    }
                    if (elapsed >= 6.5)
                    {
                        report.clearedAtEnd = !feedback.IsCelebrating && feedback.ActiveCelebrationCoins == 0;
                        Require(report.clearedAtEnd, "celebration cleared by 6.5 seconds");
                        Require(nextKey == KeyTimes.Length, "all requested key times captured");
                        feedback.ShowHiddenReward(); interruptionStarted = now; stage = 3;
                    }
                }
                else if (stage == 3 && now - interruptionStarted > .3)
                {
                    Require(feedback.IsCelebrating && feedback.ActiveCelebrationCoins > 0, "second synthetic celebration active before hiding");
                    feedback.SetVisible(false);
                    report.clearedWhenHidden = !feedback.IsCelebrating && feedback.ActiveCelebrationCoins == 0 &&
                        string.IsNullOrEmpty(feedback.CurrentNotification) && !feedback.Canvas.gameObject.activeSelf;
                    Require(report.clearedWhenHidden, "hiding immediately cancels coins, notification and HUD");
                    interruptionStarted = now; stage = 4;
                }
                else if (stage == 4 && now - interruptionStarted > .2)
                {
                    Save("hidden-mid-celebration-cleared");
                    feedback.SetVisible(true);
                    Require(feedback.DistinctCount == 5 && feedback.Canvas.gameObject.activeSelf && !feedback.IsCelebrating,
                        "showing again restores five-species HUD without replaying reward");
                    Save("restored-hud-no-replay");
                    Finish(true, "Synthetic feedback preview completed: key moments, camera turn, per-frame 4x3 center projection/depth, normal expiry and interrupted cleanup.");
                }
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static void Build()
        {
            Require(Matrices != null, "production coinMatrices field available for read-only observation");
            view = new GameObject("Feedback preview camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            view.tag = "MainCamera"; view.transform.position = new Vector3(0f, 1.65f, 0f);
            view.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
            view.fieldOfView = 70f; view.aspect = 1f; view.nearClipPlane = .05f; view.farClipPlane = 30f;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.09f, .20f, .25f);
            view.GetUniversalAdditionalCameraData().allowXRRendering = false;
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32); target.Create();
            pixels = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Opaque preview floor (coins must remain visible while looking down)";
            floor.transform.localScale = Vector3.one * 3f;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(.44f, .52f, .49f); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Light light = new GameObject("Preview main light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            RenderSettings.ambientLight = new Color(.45f, .50f, .53f); RenderSettings.fog = false;
            feedback = new GameObject("Feedback only").AddComponent<TreasureFeedback>();
            feedback.Initialize(view); feedback.ShowCollection(0, "扇贝", 1, false); started = Time.unscaledTimeAsDouble;
            csv = new StreamWriter(Path.Combine(Output, "frames.csv"));
            csv.WriteLine("frame,seconds,file,activeCoins,maxActiveCoins,visibleCoinCenters,occupiedGridCells,minimumCenterDepth,pitch,yaw,g00,g10,g20,g30,g01,g11,g21,g31,g02,g12,g22,g32");
        }
        static void CaptureFrame(double elapsed)
        {
            byte[] png = Render(); // Refreshes view-relative matrices through the same production draw hook.
            var matrices = (Matrix4x4[])Matrices.GetValue(feedback);
            int count = feedback.ActiveCelebrationCoins;
            Require(count >= 0 && count <= matrices.Length, "active count within existing instance array");
            var sample = new Frame { frame = report.samples.Count, seconds = (float)elapsed, activeCoins = count,
                maxActiveCoins = Math.Max(report.maxActiveCoins, count), gridCounts = new int[12], minimumCenterDepth = -1f,
                pitch = elapsed < 2.2 ? 52f : 0f, yaw = elapsed < 2.45 ? 0f : 60f };
            for (int i = 0; i < count; i++)
            {
                Vector3 projected = view.WorldToViewportPoint(matrices[i].MultiplyPoint3x4(Vector3.zero));
                Require(!float.IsNaN(projected.z) && !float.IsInfinity(projected.z) && projected.z >= .80f,
                    "active coin center depth at least .80m");
                sample.minimumCenterDepth = sample.minimumCenterDepth < 0f ? projected.z : Mathf.Min(sample.minimumCenterDepth, projected.z);
                if (projected.x < 0f || projected.x >= 1f || projected.y < 0f || projected.y >= 1f) continue;
                sample.visibleCoinCenters++; sample.gridCounts[Mathf.FloorToInt(projected.y * 3f) * 4 + Mathf.FloorToInt(projected.x * 4f)]++;
            }
            foreach (int occupied in sample.gridCounts) if (occupied > 0) sample.occupiedGridCells++;
            if (count > 0) report.minimumCenterDepth = report.minimumCenterDepth < 0f ? sample.minimumCenterDepth : Mathf.Min(report.minimumCenterDepth, sample.minimumCenterDepth);
            report.maxActiveCoins = sample.maxActiveCoins;
            sample.file = "frames/frame-" + sample.frame.ToString("D4") + ".png";
            File.WriteAllBytes(Path.Combine(Output, sample.file), png); report.samples.Add(sample); report.frames = report.samples.Count;
            csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2},{3},{4},{5},{6},{7:F6},{8:F1},{9:F1},{10}",
                sample.frame, elapsed, sample.file, count, sample.maxActiveCoins, sample.visibleCoinCenters, sample.occupiedGridCells,
                sample.minimumCenterDepth, sample.pitch, sample.yaw, string.Join(",", sample.gridCounts)));
            while (nextKey < KeyTimes.Length && elapsed >= KeyTimes[nextKey])
            {
                if (nextKey == 2 || nextKey == 5) Require(sample.visibleCoinCenters > 0, "projected coins remain visible at " + KeyNames[nextKey]);
                File.WriteAllBytes(Path.Combine(Output, KeyNames[nextKey] + ".png"), png);
                Debug.Log($"[FeedbackPreview] {KeyNames[nextKey]} actual={elapsed:F3}s active={count} visible={sample.visibleCoinCenters} grid={sample.occupiedGridCells}/12 minCenterDepth={sample.minimumCenterDepth:F3}m");
                nextKey++;
            }
        }
        static byte[] Render()
        {
            feedback.SubmitCelebrationForPreview(view);
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0); pixels.Apply(false, false);
                return pixels.EncodeToPNG();
            }
            finally { RenderTexture.active = previous; }
        }
        static void Save(string name) => File.WriteAllBytes(Path.Combine(Output, name + ".png"), Render());
        static void Require(bool value, string message)
        { report.assertions++; if (!value) throw new InvalidOperationException("Feedback preview: " + message); }
        static void Finish(bool success, string message)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            csv?.Dispose(); csv = null; report.success = success; report.detail = message;
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(Output, "result.txt"), (success ? "PASS\n" : "FAIL\n") + message);
            Debug.Log((success ? "FEEDBACK_PREVIEW_SUCCEEDED: " : "FEEDBACK_PREVIEW_FAILED: ") + message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
