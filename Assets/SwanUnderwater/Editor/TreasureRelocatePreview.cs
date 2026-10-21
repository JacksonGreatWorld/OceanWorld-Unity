#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Actual scene + file protocol; Editor synthetic request/palm, never headset acceptance.</summary>
    [InitializeOnLoad]
    public static class TreasureRelocatePreview
    {
        const string Key = "SwanTreasureRelocatePreview.";
        static readonly int[] RestoredIds = { 1, 2, 3, 4, 10, 13 };
        [Serializable] sealed class Request
        {
            public int version = 1;
            public string requestId, action = "move_to_hidden_coin";
            public long expiresUnixSeconds;
            public int[] restoreConsumedShellIds = RestoredIds;
        }
        [Serializable] sealed class Result
        {
            public string requestId, error;
            public bool success, hiddenComplete;
            public int distinctCount;
            public Vector3 eyePosition, coinPosition;
        }
        [Serializable] sealed class Report
        {
            public bool success, hiddenComplete;
            public int assertions, restoredVisuals, distinctCount;
            public float eyeCoinDistance, localPositionDelta, localRotationDelta;
            public Vector3 beforeEye, afterEye, beforeHeadLocalPosition, afterHeadLocalPosition;
            public Quaternion beforeHeadLocalRotation, afterHeadLocalRotation;
            public string requestId, detail;
            public string evidence = "Actual Unity scene and production one-shot file handler/collection transaction. Request and grasp palm are synthetic Editor inputs. Main camera local pose is observed before/after, not modified by this helper. The near-coin detail camera only aims the rendered evidence; it does not move the XR camera. Not physical tracking, wearer comfort, stereo, audio or performance acceptance.";
        }
        static Report report = new Report();
        static Camera view, detailView;
        static TreasureHunt hunt;
        static TreasureFeedback feedback;
        static TreasureHunt.Pickup coin;
        static RenderTexture target;
        static Texture2D pixels;
        static int stage;
        static double stageStarted;
        static TreasureRelocatePreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        static string RequestPath => Path.Combine(Application.persistentDataPath, "treasure-playtest-request.json");
        static string ResultPath => Path.Combine(Application.persistentDataPath, "treasure-playtest-result.json");
        static string Output => SessionState.GetString(Key + "output", "");

        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use separate batch Editor.");
            if (File.Exists(RequestPath)) throw new IOException("Existing playtest request belongs to another operation; refusing overwrite.");
            string output = "Validation/iteration-12/relocate-preview-201";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit quit/nographics.");
                if (args[i] == "-codexRelocateOutput" && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            if (Directory.GetFiles(output, "*.png").Length > 0) throw new IOException("Use a fresh output directory.");
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "id", Guid.NewGuid().ToString("D"));
            SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "active", true); Attach();
            SwanUnderwater.EditorTests.HeadRelocationTests.RunAll();
            EditorSceneManager.OpenScene("Assets/Scenes/UnderwaterWorld.unity");
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
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "started", "0")) > TimeSpan.FromSeconds(150).Ticks)
                    throw new TimeoutException("Relocation preview exceeded 150 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (view == null) view = Camera.main;
                if (view == null) return;
                if (target == null)
                {
                    target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); target.Create();
                    pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                }
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("Actual intro/world failed.");
                    if (OceanEntry.Instance.RenderedFrames < 2) Submit(view);
                    return;
                }
                if (hunt == null) hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || !hunt.GameplayActive) return;
                if (stage == 0) WriteRequest();
                else if (stage == 1) ReadResult();
                else if (stage == 2 && Time.unscaledTimeAsDouble - stageStarted > .35)
                {
                    Save(view, "01-actual-eye-and-progress");
                    // Dedicated evidence camera: preserve the real tracking transform while looking down at the nearby coin.
                    detailView = new GameObject("Editor relocation detail camera").AddComponent<Camera>();
                    detailView.CopyFrom(view); detailView.enabled = false;
                    detailView.transform.SetPositionAndRotation(view.transform.position,
                        Quaternion.LookRotation(coin.renderer.bounds.center - view.transform.position, Vector3.up));
                    detailView.GetUniversalAdditionalCameraData().allowXRRendering = false;
                    feedback.Initialize(detailView); stageStarted = Time.unscaledTimeAsDouble; stage = 3;
                }
                else if (stage == 3 && Time.unscaledTimeAsDouble - stageStarted > .25)
                {
                    Save(detailView, "02-near-coin-hud-5-of-5");
                    Require(hunt.TryGraspForValidation(coin.renderer.bounds.center + Vector3.up * .20f), "nearby synthetic palm accepted by real transaction");
                    Require(hunt.Inventory.HiddenComplete && hunt.Inventory.DistinctCount == 5, "hidden reward retains five species");
                    Require(coin.consumed && !coin.visual.activeSelf, "coin consumed exactly once");
                    Require(!hunt.TryGraspForValidation(coin.renderer.bounds.center + Vector3.up * .20f), "consumed coin cannot replay");
                    stageStarted = Time.unscaledTimeAsDouble; stage = 4;
                }
                else if (stage == 4 && Time.unscaledTimeAsDouble - stageStarted > .85)
                {
                    Require(feedback.ActiveCelebrationCoins > 0, "hidden coin celebration active");
                    Save(detailView, "03-hidden-reward-and-coins");
                    report.hiddenComplete = hunt.Inventory.HiddenComplete;
                    Finish(true, "One-shot request restored 5/5 and six consumed shells, moved eye near the unconsumed coin without changing observed local head pose, then synthetic grasp triggered the real hidden reward.");
                }
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }
        static void WriteRequest()
        {
            Require(hunt.Pickups.Count == 16, "16 actual scene pickups");
            foreach (var item in hunt.Pickups) if (item.kind == -1) { Require(coin == null, "one hidden coin"); coin = item; }
            Require(coin != null, "hidden coin exists");
            feedback = Object.FindFirstObjectByType<TreasureFeedback>(); Require(feedback != null, "feedback exists");
            report.requestId = SessionState.GetString(Key + "id", "");
            report.beforeEye = view.transform.position;
            report.beforeHeadLocalPosition = view.transform.localPosition;
            report.beforeHeadLocalRotation = view.transform.localRotation;
            var request = new Request { requestId = report.requestId, expiresUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 900 };
            string json = JsonUtility.ToJson(request, true);
            Directory.CreateDirectory(Application.persistentDataPath);
            using (var stream = new FileStream(RequestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) writer.Write(json);
            File.WriteAllText(Path.Combine(Output, "request.json"), json);
            stageStarted = Time.unscaledTimeAsDouble; stage = 1;
        }
        static void ReadResult()
        {
            if (Time.unscaledTimeAsDouble - stageStarted > 12) throw new TimeoutException("No matching one-shot relocation result within 12 seconds.");
            if (!File.Exists(ResultPath)) return;
            string json = File.ReadAllText(ResultPath);
            Result result;
            try { result = JsonUtility.FromJson<Result>(json); } catch (ArgumentException) { return; }
            if (result == null || result.requestId != report.requestId) return;
            File.WriteAllText(Path.Combine(Output, "result.json"), json);
            Require(result.success, "production result: " + result.error);
            Require(!File.Exists(RequestPath), "request file consumed");
            Require(result.distinctCount == 5 && !result.hiddenComplete, "result restored five species without hidden reward");
            Require(hunt.Inventory.Mask == 31 && hunt.Inventory.DistinctCount == 5 && !hunt.Inventory.HiddenComplete, "actual inventory matches result");
            foreach (var item in hunt.Pickups)
            {
                bool expected = Array.IndexOf(RestoredIds, item.id) >= 0;
                Require(item.consumed == expected && item.visual.activeSelf != expected, "restored visual state id=" + item.id);
                if (expected) report.restoredVisuals++;
            }
            Require(report.restoredVisuals == 6 && coin.visual.activeSelf && !coin.consumed, "six shells hidden; coin remains");
            Require(feedback.CollectedMask == 31 && feedback.DistinctCount == 5, "HUD restored to five species");
            Require(string.IsNullOrEmpty(feedback.CurrentNotification) && !feedback.IsCelebrating, "restore does not emit pickup/celebration notification");
            report.afterEye = view.transform.position;
            report.afterHeadLocalPosition = view.transform.localPosition;
            report.afterHeadLocalRotation = view.transform.localRotation;
            report.localPositionDelta = Vector3.Distance(report.beforeHeadLocalPosition, report.afterHeadLocalPosition);
            report.localRotationDelta = Quaternion.Angle(report.beforeHeadLocalRotation, report.afterHeadLocalRotation);
            Require(report.localPositionDelta < .0001f && report.localRotationDelta < .05f, "observed local head pose unchanged");
            report.eyeCoinDistance = Vector3.Distance(view.transform.position, coin.renderer.bounds.center);
            Require(report.eyeCoinDistance < 1.15f, "actual eye less than 1.15m from coin");
            Require(Vector3.Distance(result.eyePosition, view.transform.position) < .05f, "reported eye matches actual eye");
            report.distinctCount = hunt.Inventory.DistinctCount; stageStarted = Time.unscaledTimeAsDouble; stage = 2;
        }
        static void Submit(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData(); bool previous = data.allowXRRendering;
            try
            {
                data.allowXRRendering = false; Canvas.ForceUpdateCanvases();
                if (feedback != null) feedback.SubmitCelebrationForPreview(camera);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            finally { data.allowXRRendering = previous; }
        }
        static void Save(Camera camera, string name)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                Submit(camera); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; }
        }
        static void Require(bool value, string description)
        { report.assertions++; if (!value) throw new InvalidOperationException("Relocation validation: " + description); }
        static void Finish(bool success, string message)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            report.success = success; report.detail = message;
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            // Never remove another caller's request, including an unparseable file.
            if (File.Exists(RequestPath))
            {
                try { if (JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath))?.requestId == report.requestId) File.Delete(RequestPath); }
                catch (Exception) { }
            }
            Debug.Log((success ? "TREASURE_RELOCATE_PREVIEW_SUCCEEDED: " : "TREASURE_RELOCATE_PREVIEW_FAILED: ") + message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
