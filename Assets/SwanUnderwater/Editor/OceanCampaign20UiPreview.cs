#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SwanUnderwater.Editor
{
    /// <summary>Actual production UGUI, in-memory state and synthetic hand rays; no saved game, world or network is initialized.</summary>
    [InitializeOnLoad]
    public static class OceanCampaign20UiPreview
    {
        const string Key = "SwanCampaign20Ui.";
        static OceanCampaign20UiRunner runner;
        static OceanCampaign20UiPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static string Output => SessionState.GetString(Key + "output", "");
        // Separate graphical Editor, without -quit or -nographics.
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start a fresh batch Editor in Edit Mode.");
            string output = "Validation/iteration-23/campaign-ui/unity-preview";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Preview owns exit and requires graphical rendering.");
                if (args[i] == "-codexCampaign20Output" && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "report.json"))) throw new IOException("Use a fresh preview directory.");
            SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "error", "");
            SessionState.SetString(Key + "start", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)); SessionState.SetBool(Key + "active", true);
            Attach(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorApplication.isPlaying = true;
        }
        static void Attach() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Log; Application.logMessageReceived += Log; }
        static void Log(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) SessionState.SetString(Key + "error", text + "\n" + stack); }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            string error = SessionState.GetString(Key + "error", ""); if (error.Length > 0) { Finish(false, error); return; }
            if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "start", "0"), CultureInfo.InvariantCulture) > TimeSpan.FromSeconds(180).Ticks)
            { Finish(false, "UI preview exceeded 180 seconds."); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (runner == null) runner = new GameObject("Campaign twenty-level UI evidence").AddComponent<OceanCampaign20UiRunner>();
        }
        public static void Finish(bool passed, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            var report = runner != null ? runner.report : new OceanCampaign20UiRunner.Report(); report.success = passed; report.detail = detail;
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("CAMPAIGN20_UI " + (passed ? "PASSED" : "FAILED") + " assertions=" + report.assertions + " " + detail); EditorApplication.Exit(passed ? 0 : 1);
        }
    }
    public sealed class OceanCampaign20UiRunner : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool success; public int assertions; public string detail;
            public string evidence = "Production OceanCampaignPanel/UGUI, Catalog and State, real imported species meshes, single Editor camera. State uses accepted collection/fire/hit/catch transactions; one actual hand-ray/pinch route is driven with synthetic fresh poses. No headset tracking, stereo legibility, comfort, game-world placement, device performance, persistent save or HTTP assertions.";
            public List<string> checks = new List<string>(), screenshots = new List<string>();
        }
        public Report report = new Report();
        OceanCampaignPanel panel; OceanCampaignState state; HandSwimController hands; Camera view; RenderTexture target; Texture2D pixels;
        IEnumerator Start()
        {
            view = new GameObject("Campaign UI camera").AddComponent<Camera>(); view.transform.position = new Vector3(0, 1.6f, 0);
            view.enabled = false; view.fieldOfView = 55; view.aspect = 1; view.nearClipPlane = .02f; view.farClipPlane = 10;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.012f, .044f, .067f); view.GetUniversalAdditionalCameraData().allowXRRendering = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.8f, .87f, .9f);
            var light = new GameObject("UI species illumination").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = .9f; light.transform.rotation = Quaternion.Euler(30, -30, 0); light.shadows = LightShadows.None;
            target = new RenderTexture(1200, 1200, 24, RenderTextureFormat.ARGB32); target.Create(); pixels = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
            hands = new GameObject("Disabled tracker for UI input injection").AddComponent<HandSwimController>(); hands.enabled = false;
            state = new OceanCampaignState(); state.StartLevel(1);
            panel = new GameObject("Production campaign panel").AddComponent<OceanCampaignPanel>(); panel.Initialize(view, hands, state, Handle);
            panel.SetAvailable(true); panel.OpenMenu(); yield return new WaitForSecondsRealtime(.8f);
            Require(Paused(), "opening menu holds locomotion"); Require(panel.ActiveSpeciesModelsForValidation == 0, "menu creates no active species models");
            Save("00-menu");
            // Every catalog row remains reachable as five large buttons per chapter, with no target overlap.
            var levels = new HashSet<string>();
            for (int chapter = 0; chapter < OceanLevelCatalog.ChapterNames.Length; chapter++)
            {
                panel.ShowLevelPageForValidation(chapter); yield return null;
                int count = 0;
                foreach (var button in panel.GetButtonsForValidation()) if (button.id.StartsWith("start_level_", StringComparison.Ordinal))
                { count++; Require(levels.Add(button.id), "unique level row " + button.id); Require(button.rect.rect.height >= 60, "large ray target " + button.id); }
                Require(count == 5, "five level rows on chapter " + chapter); Require(HasText(OceanLevelCatalog.ChapterNames[chapter]), "catalog chapter name " + chapter);
                Save("chapter-" + (chapter + 1)); CheckButtonSeparation();
            }
            Require(levels.Count == OceanLevelCatalog.Count, "all catalog levels selectable");
            panel.ShowLevelPageForValidation(0); yield return new WaitForSecondsRealtime(.6f);
            yield return Press("levels_next"); Require(panel.LevelPageForValidation == 1, "fresh hover/release/pinch turns chapter via production hit-test");
            panel.ShowLevelPageForValidation(3); Require(panel.ClickForValidation("start_level", 20), "choose level twenty"); Save("confirm-level-20");
            Require(panel.ClickForValidation("confirm") && state.Current.level == 20, "confirmation emits actual level twenty callback");
            panel.OpenMenu(); yield return new WaitForSecondsRealtime(.6f);
            // All twenty-one collection identities, but at most five visible model roots.
            for (int page = 0; page < 5; page++)
            {
                panel.ShowCollectionForValidation(page); yield return null;
                Require(panel.ActiveSpeciesModelsForValidation == (page == 4 ? 1 : 5), "only collection page models active " + page);
                if (page == 3) for (int species = 5; species < 10; species++) Require(HasText(OceanLevelCatalog.FishNames[species]), "new fish collection name " + species);
                Save("collection-" + (page + 1));
            }
            state.StartLevel(8); ShowTargets(); yield return null;
            Require(HasText("1. 小丑鱼") && HasText("下一目标"), "ordered fish has ordinal and next cue");
            Require(!state.CollectFish(1) && state.CollectFish(0), "ordered transaction rejects later fish and accepts first"); panel.Refresh();
            Require(HasText("2. 蓝倒吊") && StatusFor("2. 蓝倒吊") == "下一目标", "next fish advances after accepted transaction"); Save("ordered-fish-8");
            state.StartLevel(14); ShowTargets(); yield return null;
            Require(StatusFor("1. 扇贝") == "下一目标", "ordered shell starts with first selected shell");
            Require(!state.CollectShell(11402, 2) && state.CollectShell(11400, 0), "ordered shell transaction rejects later and accepts first"); panel.Refresh();
            Require(StatusFor("2. 宝螺") == "下一目标", "next shell advances"); Save("ordered-shell-14");
            state.StartLevel(20); ShowTargets();
            var speciesSeen = new HashSet<string>();
            for (int page = 0; page < 5; page++)
            {
                yield return null;
                foreach (Text label in panel.GetComponentsInChildren<Text>()) if (label.name == "Target species" && label.gameObject.activeInHierarchy) Require(speciesSeen.Add(label.text), "unique final target " + label.text);
                Require(panel.ActiveSpeciesModelsForValidation == (page == 4 ? 1 : 5), "final target active model budget page " + page);
                Save("targets-20-page-" + (page + 1)); if (page < 4) Require(panel.ClickForValidation("targets_next"), "next final target page");
            }
            Require(speciesSeen.Count == 21 && speciesSeen.Contains("大白鲨"), "final has ten shells ten fish and shark");
            Require(!HasText("下一目标"), "unordered final does not invent a required sequence");
            foreach (int level in new[] { 5, 10, 15, 20 })
            {
                state.StartLevel(level); CompleteTargets(); panel.ShowResult(true); yield return new WaitForSecondsRealtime(.45f);
                Require(state.GoalComplete && state.Stars == 2, "goals award two stars " + level);
                Require(Visible("next_level") == (level < OceanLevelCatalog.Count) && Visible("ending_replay") == (level == OceanLevelCatalog.Count), "only catalog final level has ending " + level);
                Require(Visible("result_explore") && state.CanCollect, "two-star result permits remaining coin exploration " + level);
                Require(HasText(level == OceanLevelCatalog.Count ? "冒险圆满" : "关卡完成"), "correct result heading " + level); Save("result-" + level + "-two-stars");
            }
            for (int coin = 0; coin < 4; coin++) Require(state.CollectCoin(OceanLevelCatalog.CoinId(coin)), "partial final coin accepted " + coin);
            panel.Refresh(); Require(state.Stars == 2 && HasText("金币 4/5"), "four of five coins retains two stars and exact progress"); Save("result-20-four-coins");
            Require(state.CollectCoin(OceanLevelCatalog.CoinId(4)), "fifth final coin accepted"); panel.Refresh(); Require(state.Stars == 3 && HasText("金币 5/5"), "all final coins upgrade to three stars"); Save("result-20-three-stars");
            state.StartLevel(20); Require(state.AcquireTool(), "boss failure acquires tool");
            for (int shot = 0; shot < state.ShotLimit; shot++) Require(state.TryFireSpear(), "spent boss shot " + shot);
            Require(state.FinalizeSpearAmmo(false), "finite ammunition failure transaction"); panel.ShowResult(false); yield return null;
            Require(Visible("result_retry") && !Visible("result_explore"), "failed boss retry only"); Save("result-20-ammo-failure");
            state.StartLevel(OceanLevelCatalog.Count); CompleteTargets(); panel.ShowResult(true); yield return null;
            Require(!Visible("next_level") && Visible("ending_replay") && HasText("冒险圆满"), "new catalog final ending stays reachable"); Save("result-100-two-stars");
            state.StartLevel(18); panel.OpenMenu(); yield return new WaitForSecondsRealtime(.4f); Require(panel.ClickForValidation("resume"), "resume callback");
            yield return new WaitForSecondsRealtime(.65f); Require(!Paused() && !panel.IsOpen && !panel.WorldInputBlocked, "closing releases pause and transition gate"); Save("hud-level-18");
            OceanCampaign20UiPreview.Finish(true, "One hundred catalog levels, preserved first-twenty model previews, ordered goals, multi-coin stars and catalog-final ending passed in the Editor UI harness.");
        }
        void Handle(string action, int level)
        { if (action == "start_level") { state.StartLevel(level); panel.Refresh(); panel.CloseMenu(); } else if (action == "resume") panel.CloseMenu(); else if (action == "menu") panel.OpenMenu(); else throw new InvalidOperationException("Unexpected preview callback " + action); }
        void ShowTargets() { panel.OpenMenu(); Require(panel.ClickForValidation("show_targets"), "open required species"); }
        void CompleteTargets()
        {
            for (int species = 0; species < 10; species++) if ((state.ShellTargetMask & (1 << species)) != 0) Require(state.CollectShell(state.Current.level * 100 + species, species), "valid shell completion " + species);
            for (int species = 0; species < 10; species++) if ((state.FishTargetMask & (1 << species)) != 0) Require(state.CollectFish(species), "valid fish completion " + species);
            if (state.HasMechanic) for (int n = 0; n < state.Definition.MechanicTarget; n++) Require(state.RecordMechanicProgress(100000 + n), "accepted required mechanic node");
            if (!state.RequiresShark) return;
            Require(state.AcquireTool(), "acquire boss tool");
            for (int hit = 0; hit < state.SharkHitTarget; hit++) { if (state.Current.pendingSpearRecovery) Require(state.TryRecoverSpear(), "retrieve final-tier spear without refilling"); Require(state.TryFireSpear() && state.HitShark(), "accepted shot/hit pair " + hit); }
            Require(state.CatchShark(), "catch after catalog hit target");
        }
        bool Visible(string id) { foreach (var b in panel.GetButtonsForValidation()) if (b.id == id) return b.enabled; return false; }
        bool HasText(string value) { foreach (var label in panel.GetComponentsInChildren<Text>()) if (label.gameObject.activeInHierarchy && label.text.Contains(value)) return true; return false; }
        string StatusFor(string name)
        { foreach (var label in panel.GetComponentsInChildren<Text>()) if (label.name == "Target species" && label.text == name) foreach (var sibling in label.transform.parent.GetComponentsInChildren<Text>()) if (sibling.name == "This run target progress") return sibling.text; return ""; }
        bool Paused() => (bool)typeof(HandSwimController).GetField("menuLocomotionPaused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(hands);
        IEnumerator Press(string id)
        {
            RectTransform rect = null; foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) rect = button.rect;
            Require(rect != null, "ray target exists " + id);
            foreach (float strength in new[] { 0f, 1f, 0f })
            {
                double begin = Time.unscaledTimeAsDouble; float duration = strength == 0 ? .65f : .12f;
                do { panel.DriveMenuRayForValidation(true, new Ray(view.transform.position, (rect.position - view.transform.position).normalized), strength, true); panel.DriveMenuRayForValidation(false, default, 0, false); yield return null; } while (Time.unscaledTimeAsDouble - begin < duration);
            }
            panel.ClearValidationRays();
        }
        void CheckButtonSeparation()
        {
            var buttons = panel.GetButtonsForValidation();
            for (int i = 0; i < buttons.Count; i++) for (int j = i + 1; j < buttons.Count; j++)
            {
                Vector3[] a = new Vector3[4], b = new Vector3[4]; buttons[i].rect.GetWorldCorners(a); buttons[j].rect.GetWorldCorners(b);
                // Menu lies parallel to XY for this fixed camera. Shared edges are allowed, positive overlap is not.
                float x = Mathf.Min(a[2].x, b[2].x) - Mathf.Max(a[0].x, b[0].x), y = Mathf.Min(a[2].y, b[2].y) - Mathf.Max(a[0].y, b[0].y);
                Require(x < .00001f || y < .00001f, "separate ray buttons " + buttons[i].id + "/" + buttons[j].id);
            }
        }
        void Require(bool condition, string detail) { report.assertions++; report.checks.Add(detail + ": " + condition); if (!condition) throw new InvalidOperationException("Campaign twenty-level UI: " + detail); }
        void Save(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach (Text label in panel.GetComponentsInChildren<Text>()) if (label.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(label.text))
            {
                Require(label.preferredHeight <= label.rectTransform.rect.height + .5f, "label height " + label.name + " " + label.text);
                // All body/title strings render the actual embedded glyph, not an OS fallback or tofu.
                foreach (char character in label.text) if (!char.IsWhiteSpace(character)) Require(label.font.HasCharacter(character), "font glyph " + character + " in " + label.name);
            }
            RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture prior = RenderTexture.active; RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); RenderTexture.active = prior;
            string file = name + ".png"; File.WriteAllBytes(Path.Combine(OceanCampaign20UiPreview.Output, file), pixels.EncodeToPNG()); report.screenshots.Add(file);
        }
        void OnDestroy() { if (target != null) { target.Release(); Destroy(target); } if (pixels != null) Destroy(pixels); }
    }
}
#endif
