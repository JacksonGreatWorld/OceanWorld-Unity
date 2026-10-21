#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>100 durable slots with 20 published levels, production UGUI and in-memory campaign only. No rendered/stereo/headset claim.</summary>
    public static class OceanCampaign100UiTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("100-level UGUI: " + message); }
        static bool HasText(OceanCampaignPanel panel, string value)
        { foreach (var text in panel.GetComponentsInChildren<Text>()) if (text.gameObject.activeInHierarchy && text.text.Contains(value)) return true; return false; }
        static OceanCampaignPanel.ButtonInfoForValidation Button(OceanCampaignPanel panel, string id)
        { foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) return button; throw new InvalidOperationException("Missing visible button " + id); }
        static bool Visible(OceanCampaignPanel panel, string id)
        { foreach (var button in panel.GetButtonsForValidation()) if (button.id == id && button.enabled) return true; return false; }
        public static void RunAll()
        {
            Assertions = 0;
            var root = new GameObject("Isolated 100-level UI rules fixture");
            try
            {
                var viewObject = new GameObject("Isolated UI camera"); viewObject.transform.SetParent(root.transform, false);
                var view = viewObject.AddComponent<Camera>(); view.enabled = false; view.transform.position = new Vector3(0, 1.6f, 0);
                var input = root.AddComponent<HandSwimController>(); input.enabled = false;
                var state = new OceanCampaignState();
                for (int i = 0; i < state.Save.bestStars.Length; i++) state.Save.bestStars[i] = i % 3 == 0 ? 0 : i % 3 == 1 ? 2 : 3;
                state.StartLevel(1); // Existing history suppresses automatic first-user practice; no preference is written.
                var panel = root.AddComponent<OceanCampaignPanel>();
                int callbackCount = 0, acceptedStarts = 0;
                panel.Initialize(view, input, state, (action, level) => {
                    callbackCount++;
                    if (action == "start_level") { acceptedStarts++; state.StartLevel(level); panel.Refresh(); panel.CloseMenu(); }
                    else if (action == "resume") panel.CloseMenu();
                });
                panel.SetAvailable(true); panel.SkipTutorialForValidation(); panel.OpenMenu();
                var found = new HashSet<string>();
                int[] history = (int[])state.Save.bestStars.Clone();
                for (int page = 0; page < 20; page++)
                {
                    panel.ShowLevelPageForValidation(page); Canvas.ForceUpdateCanvases();
                    Check(panel.LevelPageForValidation == page && HasText(panel, OceanLevelCatalog.ChapterNames[page]), "named chapter and current page " + page);
                    var buttons = panel.GetButtonsForValidation(); int rows = 0;
                    foreach (var button in buttons)
                    {
                        if (!button.id.StartsWith("start_level_", StringComparison.Ordinal)) continue;
                        int level = int.Parse(button.id.Substring("start_level_".Length)); rows++;
                        Check(level >= page * 5 + 1 && level <= page * 5 + 5 && found.Add(button.id), "each level appears once on its own page " + level);
                        Check(button.title.Contains(OceanLevelCatalog.Get(level).Name) && button.rect.rect.height >= 60, "actual catalog title and large interaction target " + level);
                        string stars = state.Save.bestStars[level - 1] == 3 ? "★★★" : state.Save.bestStars[level - 1] == 2 ? "★★☆" : "☆☆☆";
                        Check(button.title.Contains(stars), "historical stars bind to correct durable slot " + level);
                        Check(button.enabled, "open and unavailable levels both remain hoverable/clickable " + level);
                        int beforeCallbacks = callbackCount;
                        int beforeModels = panel.InstantiatedSpeciesModelsForValidation;
                        var beforeRun = state.Current;
                        string beforeSave = JsonUtility.ToJson(state.Save);
                        Check(panel.ClickForValidation("start_level", level), "visible row accepts its actual callback " + level);
                        if (OceanCampaignAvailability.IsOpen(level))
                        {
                            Check(panel.PageNameForDiagnostics == "Confirm" && HasText(panel, OceanLevelCatalog.Get(level).Name), "published level opens normal named confirmation " + level);
                            Check(callbackCount == beforeCallbacks && ReferenceEquals(state.Current, beforeRun), "confirmation does not begin a run before acceptance " + level);
                            Check(panel.ClickForValidation("confirm") && state.Current.level == level && callbackCount == beforeCallbacks + 1, "accepted published confirmation dispatches real start " + level);
                        }
                        else
                        {
                            Check(panel.PageNameForDiagnostics == "Levels" && HasText(panel, OceanCampaignAvailability.UnavailableMessage), "unavailable row stays in picker and shows release notice " + level);
                            Check(!panel.ClickForValidation("confirm") && callbackCount == beforeCallbacks && ReferenceEquals(state.Current, beforeRun), "unavailable row exposes no confirmation or controller mutation " + level);
                            Check(JsonUtility.ToJson(state.Save) == beforeSave && panel.ActiveSpeciesModelsForValidation == 0 && panel.InstantiatedSpeciesModelsForValidation == beforeModels, "unavailable click preserves run serialization and instantiates no target model " + level);
                        }
                        panel.ShowLevelPageForValidation(page);
                    }
                    foreach (var label in panel.GetComponentsInChildren<Text>())
                        if (label.gameObject.activeInHierarchy && label.name == "Level objectives and allowance")
                            Check(label.preferredHeight <= label.rectTransform.rect.height + .5f, "catalog objective text fits its visible row " + page);
                    Check(rows == 5, "exactly five level buttons on chapter " + page);
                    Check(Button(panel, "levels_previous").enabled == (page > 0) && Button(panel, "levels_next").enabled == (page < 19), "first/middle/final pagination boundaries " + page);
                }
                Check(found.Count == 100 && acceptedStarts == OceanCampaignAvailability.AvailableLevelCount, "all one hundred stable buttons covered while only published starts dispatch");
                for (int i = 0; i < 100; i++) Check(state.Save.bestStars[i] == history[i], "published selection and unavailable clicks preserve durable history slot " + i);
                foreach (int futureLevel in new[] { 21, 50, 100 })
                {
                    // Pure state remains able to represent old saves; this does not unlock their UI or create a production run.
                    state.StartLevel(futureLevel);
                    string preserved = JsonUtility.ToJson(state.Save);
                    int beforeCallbacks = callbackCount;
                    panel.OpenMenu();
                    Check(HasText(panel, "历史进度已保留") && !Visible(panel, "close"), "archived current run offers selection instead of continuing " + futureLevel);
                    Check(!panel.ClickForValidation("resume") && !panel.ClickForValidation("show_targets") && !panel.ClickForValidation("restart_level"), "archived run cannot resume, show targets, or restart " + futureLevel);
                    Check(panel.ClickForValidation("show_levels") && panel.LevelPageForValidation == 0, "archived run picker defaults to published first page " + futureLevel);
                    panel.ShowUnavailableLevel(futureLevel);
                    Check(panel.PageNameForDiagnostics == "Levels" && HasText(panel, OceanCampaignAvailability.UnavailableMessage), "controller notice opens the relevant unavailable page " + futureLevel);
                    Check(callbackCount == beforeCallbacks && JsonUtility.ToJson(state.Save) == preserved && panel.ActiveSpeciesModelsForValidation == 0, "archived run remains intact without controller callback or target preview " + futureLevel);
                    panel.ShowResult(true);
                    Check(!Visible(panel, "result_explore") && !Visible(panel, "result_retry") && !Visible(panel, "next_level") && !Visible(panel, "ending_replay"), "archived result does not expose unopened gameplay actions " + futureLevel);
                }
                state.StartLevel(20); panel.OpenMenu(); Check(panel.ClickForValidation("show_targets"), "old level20 goal page still opens");
                var species = new HashSet<string>();
                for (int page = 0; page < 5; page++)
                {
                    foreach (var label in panel.GetComponentsInChildren<Text>()) if (label.gameObject.activeInHierarchy && label.name == "Target species") Check(species.Add(label.text), "legacy target species does not duplicate across pages");
                    Check(panel.ActiveSpeciesModelsForValidation == (page == 4 ? 1 : 5), "old target model visibility budget " + page);
                    if (page < 4) Check(panel.ClickForValidation("targets_next"), "old target next-page callback " + page);
                }
                Check(species.Count == 21 && !HasText(panel, "机制进度"), "old twenty-level collection identities remain unchanged");
                Complete(state); panel.ShowResult(true);
                Check(!Visible(panel, "next_level") && Visible(panel, "ending_replay") && HasText(panel, "当前篇章圆满") && HasText(panel, "后续关卡"), "published level20 finale offers replay and later-content notice without overlapping next");
                Check(!panel.ClickForValidation("next_level"), "finale cannot dispatch level21 through a hidden next button");
                state.StartLevel(19); Complete(state); panel.ShowResult(false);
                Check(Visible(panel, "next_level") && !Visible(panel, "ending_replay") && HasText(panel, "进入下一关"), "non-final published result retains ordinary next action");
                for (int i = 0; i < 100; i++) if (i != 18 && i != 19) Check(state.Save.bestStars[i] == history[i], "result actions retain unrelated durable history slot " + i);
                Debug.Log("OceanCampaign100UiTests PASS " + Assertions + " assertions; 100 historical UGUI slots, 20 published confirmations, unavailable guards and old collections; no rendered/headset claim.");
            }
            finally
            { if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Complete(OceanCampaignState state)
        {
            for (int i = 0; i < 10; i++) if ((state.ShellTargetMask & (1 << i)) != 0) Check(state.CollectShell(state.Current.level * 100 + i, i), "accepted shell objective");
            for (int i = 0; i < 10; i++) if ((state.FishTargetMask & (1 << i)) != 0) Check(state.CollectFish(i), "accepted fish objective");
            if (state.HasMechanic) for (int i = 0; i < state.Definition.MechanicTarget; i++) Check(state.RecordMechanicProgress(100000 + i), "accepted mechanic objective");
            if (!state.RequiresShark) return;
            Check(state.AcquireTool(), "actual tool ownership");
            for (int i = 0; i < state.SharkHitTarget; i++)
            { if (state.Current.pendingSpearRecovery) Check(state.TryRecoverSpear(), "retrieve without ammunition refill"); Check(state.TryFireSpear() && state.HitShark(), "actual spent round and hit"); }
            Check(state.CatchShark() && state.GoalComplete, "actual post-hit capture completes required mixed goals");
        }
    }
}
#endif
