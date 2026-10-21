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
    [InitializeOnLoad]
    public static class OceanCampaignPreview
    {
        const string Key = "SwanCampaignPreview.";
        static RenderTexture target;
        static Texture2D pixels;
        static bool running;
        static Report report = new Report();
        [Serializable] sealed class Report
        {
            public bool success;
            public int assertions;
            public string detail;
            public string scope = "Actual Unity world, menu, meshes, storage and pickup transactions with explicitly synthetic Editor inputs. Not real hand tracking, headset comfort, sound output or device performance acceptance.";
            public List<string> screenshots = new List<string>();
        }
        static OceanCampaignPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Separate batch Editor required.");
            string output = Path.GetFullPath("Validation/iteration-15/campaign-preview");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Rendered preview owns exit.");
                if (args[i] == "-codexCampaignOutput" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
            }
            Directory.CreateDirectory(output);
            if (Directory.GetFiles(output, "*.png").Length > 0) throw new IOException("Use a fresh preview output folder.");
            SessionState.SetString(Key + "output", output);
            SessionState.SetString(Key + "started", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetBool(Key + "active", true); Attach();
            EditorTests.OceanMenuPressGateTests.RunAll();
            EditorTests.OceanCampaignTests.RunAll();
            EditorTests.OceanCampaignCreatureTests.RunAll();
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
                if (DateTime.UtcNow.Ticks - long.Parse(SessionState.GetString(Key + "started", "0")) > TimeSpan.FromSeconds(300).Ticks)
                    throw new TimeoutException("Campaign integration exceeded 300 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                Camera view = Camera.main; if (view == null) return;
                if (target == null)
                { target = new RenderTexture(1600, 900, 24); target.Create(); pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false); }
                if (OceanEntry.Instance != null)
                {
                    if (OceanEntry.Instance.HasFailed) throw new InvalidOperationException("World initialization failed.");
                    if (OceanEntry.Instance.RenderedFrames < 2) Submit(view);
                    return;
                }
                if (running) return;
                var hunt = Object.FindFirstObjectByType<TreasureHunt>();
                if (hunt == null || !hunt.CoreActive || hunt.Campaign == null) return;
                running = true; hunt.StartCoroutine(Exercise(hunt, view));
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        static IEnumerator Exercise(TreasureHunt hunt, Camera view)
        {
            var campaign = hunt.Campaign; var state = campaign.State;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMenuExtrasOnly") >= 0)
            {
                campaign.HandleAction("start_level", 3);
                yield return new WaitForSecondsRealtime(.8f);
                Save(view, "01-level-3-compact-bar");
                campaign.HandleAction("menu", 0);
                yield return new WaitForSecondsRealtime(.8f);
                yield return MenuRecovery(campaign, view);
                Require(campaign.Panel.ClickForValidation("show_collection"), "collection available after recovery");
                yield return new WaitForSecondsRealtime(.5f);
                Require(campaign.Panel.ClickForValidation("close"), "collection can collapse to bar");
                yield return new WaitForSecondsRealtime(.1f);
                Save(view, "04-collection-closing-animation");
                yield return new WaitForSecondsRealtime(.7f);
                Require(!campaign.Panel.IsOpen && !campaign.Panel.IsTransitioning, "collection collapse completes");
                Save(view, "05-level-3-bar-returned");
                Finish(true, "Compact mixed-goal bar text, collection collapse and new-distance menu recovery verified; synthetic Editor poses.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexMenuRayOnly") >= 0)
            {
                yield return MenuRays(campaign, view);
                Finish(true, "Head-following touch bar, actual ray-plane hit tests, hover/pinch/release, animation input locks, two-hand arbitration, invalid samples and target changes verified in rendered Unity scene with synthetic hand rays.");
                yield break;
            }
            if (!campaign.Panel.IsOpen) campaign.HandleAction("menu", 0);
            yield return new WaitForSecondsRealtime(.8f);
            Require(campaign.Panel.IsOpen && !hunt.GameplayActive, "explicit menu blocks gameplay");
            float pausedSeconds = state.Current.remainingSeconds;
            yield return new WaitForSecondsRealtime(.6f);
            Require(state.Current.remainingSeconds == pausedSeconds, "menu pauses countdown");
            Save(view, "01-level-menu");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignRecoveryOnly") >= 0)
            {
                yield return MenuRecovery(campaign, view);
                Finish(true, "Actual LateUpdate menu recovery verified after large position/yaw changes; ordinary 30cm movement remains fixed, no immediate jump; synthetic Editor head poses.");
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-codexCampaignUiOnly") >= 0)
            {
                yield return UiOnly(campaign, view);
                Finish(true, "Actual Unity menu, all current-target pages and all collection pages rendered; visible text line-height checked. Gameplay already verified in campaign-preview-1.");
                yield break;
            }
            for (int level = 1; level <= 4; level++)
            {
                Require(campaign.Panel.ClickForValidation("show_levels"), "level chooser button wired");
                Require(campaign.Panel.ClickForValidation("start_level", level), "level button wired " + level);
                Require(campaign.Panel.ClickForValidation("confirm"), "level confirmation wired " + level);
                yield return null; yield return null;
                Require(state.Current.level == level && hunt.GameplayActive, "selected level enters gameplay " + level);
                int species = level == 1 ? 5 : level == 2 ? 10 : level == 3 ? 2 : 0;
                Require(hunt.Pickups.Count == species + Math.Min(5, species) * 2 + 1, "level layout count " + level);
                Require(state.TimeLimitSeconds == (level == 1 ? 1800 : 1200), "actual level duration " + level);
                Save(view, "level-" + level + "-objectives");
                Require(campaign.Panel.ClickForValidation("menu"), "HUD menu button wired");
                Require(campaign.Panel.ClickForValidation("show_targets"), "current target button wired");
                yield return null; Save(view, "level-" + level + "-target-models");
                if (level == 2)
                {
                    Require(campaign.Panel.ClickForValidation("targets_next"), "ten-shell target pagination wired");
                    yield return null; Save(view, "level-2-targets-second-page");
                }
                Require(campaign.Panel.ClickForValidation("back"), "target page back wired");
                Require(campaign.Panel.ClickForValidation("resume"), "resume button wired");
                yield return null; yield return null;
                for (int kind = 0; kind < species; kind++)
                {
                    TreasureHunt.Pickup item = null;
                    foreach (var p in hunt.Pickups) if (p.kind == kind && p.habitat == "sand") { item = p; break; }
                    Require(item != null, "reachable sand fallback for species " + kind);
                    Require(!hunt.TryGraspForValidation(item.renderer.bounds.center + Vector3.up * 3), "distant shell rejected");
                    Require(hunt.TryGraspForValidation(item.renderer.bounds.center + Vector3.up * .2f), "production shell transaction " + kind);
                    Require(!item.visual.activeSelf && state.IsConsumed(item.id), "consumed visual and state agree");
                    Require(!hunt.TryGraspForValidation(item.renderer.bounds.center + Vector3.up * .2f), "same item not replayed");
                }
                foreach (var creature in campaign.Creatures.Targets)
                {
                    if (creature.IsShark || creature.IsAmbient || (state.Current.fishMask & (1 << creature.Species)) != 0) continue;
                    Require(campaign.Creatures.TryGrasp(creature.WorldBounds.center + Vector3.up * .05f, true), "production fish capture " + creature.Species);
                }
                Require(state.GoalComplete && state.Stars == 2, "actual goals produce two stars " + level);
                TreasureHunt.Pickup coin = null;
                foreach (var p in hunt.Pickups) if (p.kind < 0) coin = p;
                Require(coin != null && hunt.TryGraspForValidation(coin.renderer.bounds.center + Vector3.up * .2f), "coin accepted after goal " + level);
                Require(state.Stars == 3 && state.Save.bestStars[level - 1] == 3, "coin upgrades best stars " + level);
                campaign.HandleAction("menu", 0); yield return null;
                campaign.SaveNow();
                Require(campaign.LastSaveSucceeded, "real local save successful");
                Require(OceanSaveStore.TryLoad(campaign.SaveDirectory, out var loaded, out _), "real local load successful");
                Require(loaded.currentRun.level == level && loaded.currentRun.coinCollected && loaded.currentRun.completed, "active run roundtrip");
                hunt.RebuildDiscoveries(new Vector3(0, 0, -4), new OceanCampaignState(loaded));
                foreach (var p in hunt.Pickups)
                    Require(p.visual.activeSelf == !state.IsConsumed(p.id), "recreated pickup visibility matches save");
                yield return null;
            }
            campaign.HandleAction("start_level", 1); yield return null; yield return null;
            state.Tick(1800f, true); yield return null; yield return null;
            Require(state.Failed && campaign.Panel.IsOpen && !hunt.GameplayActive, "timeout opens failure menu");
            Save(view, "02-timeout-retry");
            Require(campaign.Panel.ClickForValidation("result_retry"), "failure retry button wired");
            yield return null; yield return null;
            Require(!state.Failed && state.Current.shellMask == 0 && state.Current.remainingSeconds > 1799, "retry clears only active run");
            Require(state.Save.shellMask == 1023 && state.Save.fishMask == 15 && state.Save.bestStars[0] == 3, "retry retains collection and best stars");
            campaign.HandleAction("start_level", 5); yield return null; yield return null;
            Require(state.UnlimitedTime && state.Current.remainingSeconds == 0 && !state.TimedOut, "fifth level untimed in scene");
            yield return new WaitForSecondsRealtime(.85f);
            OceanSpearGunPreview.RunLevelFive(campaign, view, Require, Save,
                SessionState.GetString(Key + "output", ""));
            yield return new WaitForSecondsRealtime(3.5f);
            Require(campaign.Panel.IsOpen, "ending result appears after celebration delay");
            Save(view, "04-campaign-ending");
            Require(campaign.Panel.ClickForValidation("result_menu"), "ending main-menu button wired");
            yield return null;
            Save(view, "05-progress-and-stars");
            campaign.Panel.ShowCollectionForValidation(); yield return null;
            Save(view, "06-collection-library");
            Require(campaign.Panel.ClickForValidation("collection_next_0"), "collection pagination wired"); yield return null;
            Save(view, "07-new-shell-species");
            Require(campaign.Panel.ClickForValidation("collection_next_1"), "marine collection pagination wired"); yield return null;
            Save(view, "08-fish-and-shark-library");
            Require(campaign.Panel.ClickForValidation("back"), "collection back button wired");
            Require(campaign.Panel.ClickForValidation("restart_game"), "restart-game button wired");
            Require(campaign.Panel.ClickForValidation("confirm"), "restart-game confirmation wired");
            yield return null; yield return null;
            Require(state.Current.level == 1 && state.Current.shellMask == 0 && state.Save.bestStars[3] == 3, "restart game starts level one preserving history");
            campaign.HandleAction("menu", 0);
            campaign.SaveNow();
            Finish(true, "Five level layouts, real shell/fish pickup transactions, two-to-three-star upgrade, timed failure, retry, collection retention and actual local save/load verified; synthetic Editor input.");
        }
        static Ray RayTo(OceanCampaignPanel panel, Camera view, string id)
        {
            foreach (var button in panel.GetButtonsForValidation())
                if (button.id == id)
                {
                    Vector3 origin = view.transform.position + view.transform.right * -.15f - view.transform.up * .20f;
                    return new Ray(origin, (button.rect.position - origin).normalized);
                }
            throw new InvalidOperationException("Button missing: " + id);
        }
        static IEnumerator MenuRays(OceanCampaignController campaign, Camera view)
        {
            var panel = campaign.Panel;
            Require(!panel.IsOpen && !panel.IsTransitioning, "startup leaves menu collapsed");
            Transform bar = view.transform.Find("Campaign goals and menu entry");
            Require(bar != null && bar.gameObject.activeInHierarchy, "head-attached touch bar visible");
            Vector3 barLocal = bar.localPosition;
            Require(Mathf.Abs(barLocal.x) < .001f && barLocal.y > .15f && barLocal.z > .5f, "bar centered above view");
            Vector3 eye = view.transform.position; Quaternion rotation = view.transform.rotation;
            view.transform.SetPositionAndRotation(eye + Vector3.right * .3f, Quaternion.AngleAxis(25, Vector3.up) * rotation);
            yield return null;
            Require(Vector3.Distance(bar.localPosition, barLocal) < .001f, "bar follows head without drifting");
            view.transform.SetPositionAndRotation(eye, rotation);
            yield return new WaitForSecondsRealtime(.5f);
            Save(view, "01-collapsed-touch-bar");
            Ray toBar = RayTo(panel, view, "menu");
            panel.DriveMenuRayForValidation(true, toBar, 1f);
            yield return new WaitForSecondsRealtime(.5f);
            Require(!panel.IsOpen && panel.ActivationCountForValidation == 0, "held pinch entering bar cannot open");
            panel.DriveMenuRayForValidation(true, toBar, 0f);
            yield return new WaitForSecondsRealtime(.3f);
            Require(panel.IsTargetHoveredForValidation("menu"), "actual hand ray creates bar hover");
            Save(view, "02-bar-hover-gold");
            // Rendering/PNG readback can stall the Editor; require fresh released hover again.
            yield return new WaitForSecondsRealtime(.4f);
            panel.DriveMenuRayForValidation(true, toBar, 1f);
            yield return new WaitForSecondsRealtime(.12f);
            Require(panel.IsOpen && panel.IsTransitioning && !campaign.GameplayAllowed, "pinch starts paused expansion");
            Save(view, "03-expanding-menu");
            yield return new WaitForSecondsRealtime(.75f);
            Require(panel.IsOpen && !panel.IsTransitioning && panel.ActivationCountForValidation == 1, "held pinch cannot click through expansion");
            panel.DriveMenuRayForValidation(true, default, 0, false);
            Save(view, "04-expanded-gold-menu");
            float time = campaign.State.Current.remainingSeconds;
            Ray toLevels = RayTo(panel, view, "show_levels");
            panel.DriveMenuRayForValidation(false, toLevels, 0f);
            yield return new WaitForSecondsRealtime(.3f);
            Require(panel.IsTargetHoveredForValidation("show_levels"), "right hand ray highlights distant button");
            Require(campaign.State.Current.remainingSeconds == time, "menu pauses level timer");
            Save(view, "05-right-hand-hover");
            yield return new WaitForSecondsRealtime(.4f);
            panel.DriveMenuRayForValidation(false, toLevels, 1f);
            yield return new WaitForSecondsRealtime(.5f);
            Require(panel.ActivationCountForValidation == 2, "right hand pinch selects level page");
            Save(view, "06-gold-level-select");
            // Two hands pressing the same frame dispatch exactly once.
            Ray toBack = RayTo(panel, view, "back");
            panel.DriveMenuRayForValidation(true, toBack, 0);
            panel.DriveMenuRayForValidation(false, toBack, 0);
            yield return new WaitForSecondsRealtime(.3f);
            panel.DriveMenuRayForValidation(true, toBack, 1);
            panel.DriveMenuRayForValidation(false, toBack, 1);
            yield return new WaitForSecondsRealtime(.5f);
            Require(panel.ActivationCountForValidation == 3, "simultaneous two hands dispatch once");
            Ray toCollection = RayTo(panel, view, "show_collection");
            panel.DriveMenuRayForValidation(false, default, 0, false);
            panel.DriveMenuRayForValidation(true, toCollection, 0);
            yield return new WaitForSecondsRealtime(.3f);
            panel.DriveMenuRayForValidation(true, default, 0, false);
            yield return new WaitForSecondsRealtime(.12f);
            panel.DriveMenuRayForValidation(true, toCollection, 1);
            yield return new WaitForSecondsRealtime(.3f);
            Require(panel.ActivationCountForValidation == 3, "lost tracking invalidates armed press");
            panel.DriveMenuRayForValidation(true, toCollection, 0);
            yield return new WaitForSecondsRealtime(.3f);
            panel.DriveMenuRayForValidation(true, toCollection, 1);
            yield return new WaitForSecondsRealtime(.5f);
            Require(panel.ActivationCountForValidation == 4, "release after recovery enables explicit selection");
            Save(view, "07-gold-collection");
            panel.DriveMenuRayForValidation(true, default, 0, false);
            Require(panel.ClickForValidation("back"), "return from collection");
            yield return new WaitForSecondsRealtime(.5f);
            Ray toClose = RayTo(panel, view, "close");
            panel.DriveMenuRayForValidation(false, toClose, 0);
            yield return new WaitForSecondsRealtime(.3f);
            panel.DriveMenuRayForValidation(false, toClose, 1);
            yield return new WaitForSecondsRealtime(.10f);
            Require(!panel.IsOpen && panel.IsTransitioning && !campaign.GameplayAllowed, "closing animation blocks world pickup");
            Save(view, "08-closing-menu");
            yield return new WaitForSecondsRealtime(.6f);
            Require(!panel.IsOpen && !panel.IsTransitioning && campaign.GameplayAllowed, "collapsed menu returns control");
            Require(bar.gameObject.activeInHierarchy, "touch bar restored after close");
            panel.ClearValidationRays();
            Save(view, "09-collapsed-again");
        }

        static IEnumerator MenuRecovery(OceanCampaignController campaign, Camera view)
        {
            Transform menu = campaign.transform.Find("Ocean campaign menu");
            Require(menu != null && menu.gameObject.activeInHierarchy, "actual menu exists");
            Vector3 originalPosition = view.transform.position;
            Quaternion originalRotation = view.transform.rotation;
            Vector3 anchor = menu.position;
            foreach (Vector3 offset in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                view.transform.position = originalPosition + offset * .30f;
                yield return new WaitForSecondsRealtime(1.1f);
                Require(Vector3.Distance(menu.position, anchor) < .001f, "ordinary head translation does not move menu " + offset);
            }
            view.transform.position = originalPosition + Vector3.up * 1.6f;
            yield return new WaitForSecondsRealtime(.3f);
            Require(Vector3.Distance(menu.position, anchor) < .001f, "brief loss of reach does not jump menu");
            for (int i = 0; i < 24; i++) yield return new WaitForSecondsRealtime(.06f);
            Require(Vector3.Distance(menu.position, anchor) > 1.4f, "sustained height change recalls menu");
            Require(!OceanCampaignPanel.ShouldRecoverMenu(view.transform.InverseTransformPoint(menu.position)), "recalled menu within reach");
            anchor = menu.position;
            view.transform.rotation = Quaternion.AngleAxis(150, Vector3.up) * originalRotation;
            yield return new WaitForSecondsRealtime(.3f);
            Require(Vector3.Distance(menu.position, anchor) < .001f, "brief turn does not move menu");
            for (int i = 0; i < 24; i++) yield return new WaitForSecondsRealtime(.06f);
            Require(!OceanCampaignPanel.ShouldRecoverMenu(view.transform.InverseTransformPoint(menu.position)), "sustained turn recalls menu in front");
            Save(view, "02-menu-recovered-after-turn");
            view.transform.SetPositionAndRotation(originalPosition, originalRotation);
            for (int i = 0; i < 24; i++) yield return new WaitForSecondsRealtime(.06f);
            Require(!OceanCampaignPanel.ShouldRecoverMenu(view.transform.InverseTransformPoint(menu.position)), "menu returns to initial pose");
            Save(view, "03-menu-recovered-after-return");
        }

        static IEnumerator UiOnly(OceanCampaignController campaign, Camera view)
        {
            for (int level = 1; level <= 5; level++)
            {
                Require(campaign.Panel.ClickForValidation("show_levels"), "open level selection");
                Save(view, "level-list-" + level);
                Require(campaign.Panel.ClickForValidation("start_level", level), "choose level");
                Save(view, "confirm-level-" + level);
                Require(campaign.Panel.ClickForValidation("confirm"), "confirm level");
                yield return null; yield return null;
                Require(campaign.Panel.ClickForValidation("menu"), "open gameplay menu");
                Require(campaign.Panel.ClickForValidation("show_targets"), "show required species");
                yield return null; Save(view, "targets-level-" + level);
                if (level == 2)
                {
                    Require(campaign.Panel.ClickForValidation("targets_next"), "second target page");
                    yield return null; Save(view, "targets-level-2-page-2");
                }
                Require(campaign.Panel.ClickForValidation("back"), "return to menu");
            }
            for (int page = 0; page < 3; page++)
            { campaign.Panel.ShowCollectionForValidation(page); yield return null; Save(view, "collection-page-" + (page + 1)); }
            campaign.HandleAction("menu", 0);
        }

        static void Submit(Camera view)
        {
            var data = view.GetUniversalAdditionalCameraData(); bool previous = data.allowXRRendering;
            try { data.allowXRRendering = false; Canvas.ForceUpdateCanvases(); Object.FindFirstObjectByType<TreasureFeedback>()?.SubmitCelebrationForPreview(view);
                RenderPipeline.SubmitRenderRequest(view, new UniversalRenderPipeline.SingleCameraRequest { destination = target }); }
            finally { data.allowXRRendering = previous; }
        }
        static void Save(Camera view, string name)
        {
            RenderTexture previous = RenderTexture.active;
            try { Submit(view);
                foreach (var text in Object.FindFirstObjectByType<OceanCampaignPanel>().GetComponentsInChildren<UnityEngine.UI.Text>())
                    if (text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text))
                        Require(text.preferredHeight <= text.rectTransform.rect.height + .5f,
                            "visible label fits: " + text.name + " value=" + text.text + " height=" + text.preferredHeight + "/" + text.rectTransform.rect.height);
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(SessionState.GetString(Key + "output", ""), name + ".png"), pixels.EncodeToPNG()); report.screenshots.Add(name + ".png"); }
            finally { RenderTexture.active = previous; }
        }
        static void Require(bool value, string reason)
        { report.assertions++; if (!value) throw new InvalidOperationException("Campaign integration: " + reason); }
        static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Error;
            report.success = success; report.detail = detail;
            File.WriteAllText(Path.Combine(SessionState.GetString(Key + "output", ""), "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log((success ? "CAMPAIGN_PREVIEW_SUCCEEDED: " : "CAMPAIGN_PREVIEW_FAILED: ") + detail);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
