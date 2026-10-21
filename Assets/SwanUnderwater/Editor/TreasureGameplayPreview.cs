#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Actual scene integration with explicitly synthetic grasps; never ships in the player.</summary>
    [InitializeOnLoad]
    public static class TreasureGameplayPreview
    {
        const string Key = "SwanTreasurePreview.";
        static RenderTexture target;
        static Texture2D pixels;
        static bool running;
        static Report report = new Report();
        [Serializable] sealed class Placement
        {
            public int id, kind;
            public string habitat;
            public Vector3 position, bounds, approach, approachPosition, graspPosition;
            public float overhangClearance;
        }
        [Serializable] sealed class Report
        {
            public bool success;
            public int assertions, shellCount, coinCount, finalSpecies, mask;
            public bool hiddenComplete;
            public string detail, evidence = "Actual Unity scene/meshes/UI and production distance/occlusion/collection transaction, driven by synthetic Editor palm positions. Not tracked-hand, headset, audio-output or performance acceptance.";
            public List<Placement> placements = new List<Placement>();
            public List<string> screenshots = new List<string>();
        }
        static TreasureGameplayPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use separate batch Editor.");
            string output = "Validation/iteration-11/gameplay-preview";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Omit quit/nographics for rendered preview.");
                if (args[i] == "-codexTreasureOutput" && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            if (Directory.GetFiles(output, "*.png").Length > 0) throw new IOException("Use a fresh preview directory.");
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "active", true);
            Attach();
            EditorTests.TreasureCollectionTests.RunAll();
            EditorTests.ShellGrabGestureTests.RunAll();
            SwanUnderwater.EditorTests.ReefSegmentGeometryTests.RunAll();
            EditorSceneManager.OpenScene("Assets/Scenes/UnderwaterWorld.unity");
            EditorApplication.isPlaying = true;
        }
        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= Error; Application.logMessageReceived += Error;
        }
        static void Error(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack);
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "started", "0")) > TimeSpan.FromSeconds(160).Ticks)
                    throw new TimeoutException("Treasure preview exceeded 160 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                Camera view = Camera.main;
                if (view == null) return;
                if (target == null)
                {
                    target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); target.Create();
                    pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                }
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("Actual world initialization failed.");
                    if (OceanEntry.Instance.RenderedFrames < 2) Submit(view);
                    return;
                }
                if (running) return;
                var hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || !hunt.GameplayActive) return;
                running = true;
                hunt.StartCoroutine(Exercise(hunt, view));
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        static IEnumerator Exercise(TreasureHunt hunt, Camera view)
        {
            Require(hunt.Pickups.Count == 16, "15 shells and one coin");
            int[] kinds = new int[5];
            foreach (var item in hunt.Pickups)
            {
                report.placements.Add(new Placement { id = item.id, kind = item.kind, habitat = item.habitat,
                    position = item.GroundAnchor, bounds = item.renderer.bounds.size, overhangClearance = item.overhangClearance,
                    approach = item.approach, approachPosition = item.approachPosition, graspPosition = item.graspPosition });
                if (item.kind >= 0) { kinds[item.kind]++; report.shellCount++; } else report.coinCount++;
                Require(item.renderer.bounds.size.sqrMagnitude > .01f && item.renderer.bounds.size.sqrMagnitude < .5f, "bounded model size");
                if (item.habitat == "coral-overhang") Require(item.overhangClearance >= .55f, "real coral gap");
            }
            foreach (int count in kinds) Require(count == 3, "three reachable candidates per species");
            Require(report.coinCount == 1, "single hidden coin");
            var first = hunt.Pickups[0];
            Aim(view, first);
            yield return new WaitForSecondsRealtime(.4f);
            var glow = new MaterialPropertyBlock();
            first.renderer.GetPropertyBlock(glow);
            Require(glow.GetFloat(CollectibleVisuals.ProximityGlowId) > .01f, "nearby shell softly glows");
            view.transform.position += Vector3.back * 9f;
            yield return new WaitForSecondsRealtime(.2f);
            first.renderer.GetPropertyBlock(glow);
            Require(glow.GetFloat(CollectibleVisuals.ProximityGlowId) < .001f, "distant shell glow off");
            Aim(view, first);
            yield return new WaitForSecondsRealtime(.2f);
            Save(view, "01-first-shell-on-sand");
            Vector3 palm = first.renderer.bounds.center + Vector3.up * .60f;
            Require(!hunt.TryGraspForValidation(palm + Vector3.up * 3f), "out of range rejected");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Editor-only reach obstruction";
            blocker.transform.position = first.renderer.bounds.center + Vector3.up * .30f;
            blocker.transform.localScale = Vector3.one * .15f;
            Physics.SyncTransforms();
            Require(!hunt.TryGraspForValidation(palm), "reef obstruction rejected");
            blocker.transform.position = palm; Physics.SyncTransforms();
            Require(!hunt.TryGraspForValidation(palm), "palm inside rock rejected");
            blocker.transform.position = first.renderer.bounds.center + Vector3.up * .035f;
            blocker.transform.localScale = Vector3.one * .02f; Physics.SyncTransforms();
            Require(!hunt.TryGraspForValidation(palm), "obstruction in final eight centimetres rejected");
            Object.DestroyImmediate(blocker); Physics.SyncTransforms();
            Require(hunt.TryGraspForValidation(palm), "near grasp collects");
            Require(!first.visual.activeSelf && hunt.Inventory.DistinctCount == 1, "disappears and increments");
            Require(!hunt.TryGraspForValidation(palm), "same palm cannot replay consumed pickup");
            yield return new WaitForSecondsRealtime(.8f); Save(view, "02-new-shell-feedback");
            yield return new WaitForSecondsRealtime(3.0f);
            var duplicate = hunt.Pickups[5]; Aim(view, duplicate);
            yield return new WaitForSecondsRealtime(.4f); Save(view, "03-shell-in-grass");
            Require(hunt.TryGraspForValidation(duplicate.renderer.bounds.center + Vector3.up * .2f), "grass grasp");
            Require(hunt.Inventory.DistinctCount == 1 && !duplicate.visual.activeSelf, "duplicate disappears without new species");
            yield return new WaitForSecondsRealtime(.8f); Save(view, "04-duplicate-feedback");
            yield return new WaitForSecondsRealtime(3.0f);
            var coin = hunt.Pickups[15]; Aim(view, coin);
            yield return new WaitForSecondsRealtime(.4f); Save(view, "05-hidden-coin");
            Require(hunt.TryGraspForValidation(coin.renderer.bounds.center + Vector3.up * .2f), "hidden coin grasp");
            Require(hunt.Inventory.HiddenComplete && hunt.Inventory.DistinctCount == 1, "hidden reward independent");
            double burstStart = Time.unscaledTimeAsDouble;
            int burstFrame = 0; bool firstBurst = false, secondBurst = false;
            using (var frames = new StreamWriter(Path.Combine(SessionState.GetString(Key + "output", ""), "coins.csv")))
            {
                frames.WriteLine("frame,seconds");
                while (Time.unscaledTimeAsDouble - burstStart < 4.4)
                {
                    double elapsed = Time.unscaledTimeAsDouble - burstStart;
                    Save(view, "coin-" + burstFrame.ToString("D4"));
                    frames.WriteLine(burstFrame + "," + elapsed.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
                    burstFrame++;
                    if (!firstBurst && elapsed >= .75) { Save(view, "06-coin-shower"); firstBurst = true; }
                    if (!secondBurst && elapsed >= 1.95) { Save(view, "07-hidden-task-feedback"); secondBurst = true; }
                    yield return new WaitForSecondsRealtime(.05f);
                }
            }
            yield return new WaitForSecondsRealtime(2.5f);
            Aim(view, hunt.Pickups[10]); yield return new WaitForSecondsRealtime(.4f); Save(view, "08-shell-under-coral");
            for (int kind = 1; kind < 5; kind++)
            {
                var item = hunt.Pickups[kind]; Aim(view, item);
                yield return new WaitForSecondsRealtime(.25f); Save(view, "species-" + kind);
                Require(hunt.TryGraspForValidation(item.renderer.bounds.center + Vector3.up * .2f), "collect remaining species " + kind);
                Require(hunt.Inventory.DistinctCount == kind + 1, "distinct count " + kind);
                yield return new WaitForSecondsRealtime(kind == 4 ? 4.0f : 3.2f);
            }
            Save(view, "09-level-complete");
            yield return new WaitForSecondsRealtime(3.5f); Save(view, "10-complete-progress");
            Require(hunt.Inventory.LevelComplete && hunt.Inventory.Mask == 31, "level complete exactly at five species");
            Require(!coin.visual.activeSelf && hunt.Inventory.HiddenComplete, "coin remains consumed");
            for (int i = 10; i < 15; i++)
            {
                var hidden = hunt.Pickups[i];
                Require(hunt.TryGraspForValidation(hidden.graspPosition), "verified coral approach is grabbable " + i);
                Require(hidden.consumed && hunt.Inventory.DistinctCount == 5, "coral duplicate consumed without new species " + i);
            }
            report.finalSpecies = hunt.Inventory.DistinctCount; report.mask = hunt.Inventory.Mask; report.hiddenComplete = hunt.Inventory.HiddenComplete;
            Finish(true, "Scene initialized; all habitat placements, real distance/occlusion checks, duplicate handling, five-species completion and hidden coin reward exercised. Grasp inputs were synthetic Editor positions.");
        }

        static void Aim(Camera view, TreasureHunt.Pickup item)
        {
            Vector3 center = item.renderer.bounds.center;
            view.transform.position = item.habitat == "coral-overhang" ? item.approachPosition :
                center + item.approach * .9f + Vector3.up * .65f;
            view.transform.rotation = Quaternion.LookRotation(center - view.transform.position, Vector3.up);
        }
        static void Submit(Camera view)
        {
            var data = view.GetUniversalAdditionalCameraData(); bool previous = data.allowXRRendering;
            try
            {
                data.allowXRRendering = false; Canvas.ForceUpdateCanvases();
                Object.FindFirstObjectByType<TreasureFeedback>()?.SubmitCelebrationForPreview(view);
                RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            }
            finally { data.allowXRRendering = previous; }
        }
        static void Save(Camera view, string name)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                Submit(view); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), name + ".png"), pixels.EncodeToPNG());
                report.screenshots.Add(name + ".png");
            }
            finally { RenderTexture.active = previous; }
        }
        static void Require(bool value, string reason)
        { report.assertions++; if (!value) throw new InvalidOperationException("Treasure integration: " + reason); }
        static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false);
            EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            report.success = success; report.detail = detail;
            string output = SessionState.GetString(Key + "output", "");
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log((success ? "TREASURE_PREVIEW_SUCCEEDED: " : "TREASURE_PREVIEW_FAILED: ") + detail);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
