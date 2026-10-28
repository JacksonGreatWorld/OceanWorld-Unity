#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Real closed-menu task-strip presentation with isolated in-memory campaign
    /// state. Keeps every failing row for a single final failure, so one long
    /// English hint cannot hide problems in later levels. No device or player-save IO.
    /// </summary>
    public static class OceanTaskStripLanguageChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> issues = new List<string>();
        public static int Assertions { get; private set; }
        public static IReadOnlyList<string> Issues => issues;

        static void Check(bool passed, string message)
        {
            Assertions++;
            if (!passed) issues.Add(message);
        }

        static T Get<T>(object owner, string fieldName)
        {
            var field = owner.GetType().GetField(fieldName, Private);
            if (field == null) throw new InvalidOperationException("Missing task-strip fixture field: " + fieldName);
            return (T)field.GetValue(owner);
        }

        static bool HasHan(string text)
        {
            foreach (char c in text)
                if ((c >= '\u3400' && c <= '\u9fff') || (c >= '\uf900' && c <= '\ufaff')) return true;
            return false;
        }

        static string Quoted(string text) => "\"" + (text ?? "").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        static string IdentitySignature()
        {
            var text = new StringBuilder();
            text.Append(OceanLevelCatalog.Count).Append('|').Append(OceanSpeciesCatalog.Count);
            for (int level = 1; level <= OceanLevelCatalog.Count; level++)
            {
                var definition = OceanLevelCatalog.Get(level);
                text.Append('|').Append(definition.Level).Append(':').Append(definition.Name);
            }
            for (int id = 0; id < OceanSpeciesCatalog.Count; id++)
            {
                var entry = OceanSpeciesCatalog.Get(id);
                text.Append('|').Append(entry.Id).Append(':').Append(entry.ResourcePath);
            }
            foreach (string path in OceanLevelCatalog.FishPaths) text.Append('|').Append(path);
            return text.ToString();
        }

        static void CheckVisibleStrip(OceanCampaignPanel panel, OceanCampaignState state, int hint,
            bool english, string context)
        {
            var expected = OceanCampaignGuide.Describe(state, hint);
            Check(!panel.IsOpen && !panel.IsTransitioning, context + ": task strip uses the actually closed menu");
            Check(panel.GuideActionForValidation == expected.Action,
                context + ": action differs from current Guide output: actual=" + Quoted(panel.GuideActionForValidation) +
                " expected=" + Quoted(expected.Action));
            Check(panel.GuideDetailsForValidation == expected.Details,
                context + ": details differ from current Guide output: actual=" + Quoted(panel.GuideDetailsForValidation) +
                " expected=" + Quoted(expected.Details));
            RectTransform root = panel.GuideRectForValidation;
            Check(root != null && root.gameObject.activeInHierarchy, context + ": real task-strip canvas is visible");
            if (root == null) return;
            int visible = 0;
            foreach (var label in root.GetComponentsInChildren<Text>(true))
            {
                if (!label.enabled || !label.gameObject.activeInHierarchy) continue;
                visible++;
                string where = context + ": " + label.name + " " + Quoted(label.text);
                Rect box = label.rectTransform.rect;
                float width = label.preferredWidth, height = label.preferredHeight;
                Check(!float.IsNaN(width) && !float.IsInfinity(width) && box.width > 0,
                    where + ": valid width measurement");
                Check(!float.IsNaN(height) && !float.IsInfinity(height) && box.height > 0,
                    where + ": valid height measurement");
                Check(width <= box.width + .5f,
                    where + ": single-line width " + width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " exceeds rect " + box.width.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " at font " + label.fontSize);
                Check(height <= box.height + .5f,
                    where + ": height " + height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " exceeds rect " + box.height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " at font " + label.fontSize);
                Check(label.text.IndexOf('\n') < 0 && label.text.IndexOf('\r') < 0,
                    where + ": compact task-strip label stays a single authored line");
                if (english) Check(!HasHan(label.text), where + ": English task strip contains residual Han text");
            }
            Check(visible >= 3, context + ": examines action, details and menu hint, not an empty hidden canvas");
        }

        static void RestoreSave(OceanSaveData target, string json)
        {
            var value = JsonUtility.FromJson<OceanSaveData>(json);
            target.version = value.version; target.bestStars = value.bestStars;
            target.shellMask = value.shellMask; target.fishMask = value.fishMask;
            target.shark = value.shark; target.currentRun = value.currentRun;
        }

        public static IEnumerator RunInPlayMode(OceanCampaignPanel panel, Camera view,
            OceanCampaignState state, Action<string> capture)
        {
            Assertions = 0; issues.Clear();
            if (!Application.isPlaying || panel == null || view == null || state == null)
                throw new ArgumentException("Task-strip language checks require the existing isolated PlayMode panel, camera and state.");
            if (!ReferenceEquals(Get<OceanCampaignState>(panel, "state"), state) ||
                panel.GetComponent<OceanCampaignController>() != null || panel.TutorialActive)
                throw new InvalidOperationException("Use the standalone campaign-menu fixture without a live controller or active lesson.");

            var hands = Get<HandSwimController>(panel, "hands");
            if (hands == null) throw new InvalidOperationException("The fixture panel has no comfort/input owner.");
            OceanComfortProfile comfort = hands.Comfort;
            bool languageHad = PlayerPrefs.HasKey(OceanLanguage.PreferenceKey);
            int languageValue = PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, 0);
            string save = JsonUtility.ToJson(state.Save);
            string identities = IdentitySignature();
            bool menuWasOpen = panel.IsOpen;
            bool wasAvailable = panel.IsAvailableForDiagnostics;
            bool locomotionWasPaused = hands.MenuLocomotionPaused;
            object previousPage = Get<object>(panel, "page");
            var showPage = panel.GetType().GetMethod("ShowPage", Private);
            Vector3 eye = view.transform.position;
            Quaternion facing = view.transform.rotation;
            int cullingMask = view.cullingMask, sceneHandle = SceneManager.GetActiveScene().handle;
            IDisposable locale = null;
            var captured = new HashSet<int>();
            try
            {
                locale = OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese);
                panel.SetAvailable(true); panel.CloseMenu();
                double deadline = Time.unscaledTimeAsDouble + 2;
                while (panel.IsTransitioning && Time.unscaledTimeAsDouble < deadline) yield return null;
                Check(!panel.IsOpen && !panel.IsTransitioning, "baseline: actual close animation finishes before task-strip checks");

                for (int preset = 0; preset < 3; preset++)
                {
                    panel.SetTextScaleForValidation(preset);
                    for (int level = 1; level <= OceanCampaignAvailability.AvailableLevelCount; level++)
                    {
                        int phases = OceanLevelCatalog.Get(level).RequiresShark ? 2 : 1;
                        for (int phase = 0; phase < phases; phase++)
                        {
                            state.StartLevel(level);
                            if (phase == 1) Check(state.AcquireTool(), "isolated fixture can hold the spear in level " + level);
                            string phaseName = phase == 0 ? "fresh" : "spear-owned";
                            panel.Refresh();
                            for (int hint = 0; hint <= 4; hint++)
                            {
                                foreach (var choice in new[] { OceanLanguageChoice.English,
                                    OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English,
                                    OceanLanguageChoice.SimplifiedChinese })
                                {
                                    bool english = choice == OceanLanguageChoice.English;
                                    string context = "level=" + level + " preset=" + preset + " hint=" + hint +
                                        " phase=" + phaseName + " language=" + choice;
                                    OceanLanguage.SetPreference(choice, false);
                                    panel.SetGuideHintForValidation(hint);
                                    panel.RefreshProgress();
                                    Canvas.ForceUpdateCanvases();
                                    CheckVisibleStrip(panel, state, hint, english, context + " immediate");
                                    // Let actual Update/LateUpdate execute, then use the controller's
                                    // production progress-refresh entrypoint. This detects a later
                                    // frame replacing localized text with a cached old-language copy.
                                    yield return null;
                                    panel.RefreshProgress(); Canvas.ForceUpdateCanvases();
                                    CheckVisibleStrip(panel, state, hint, english, context + " next-frame");
                                    Check(PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) == languageHad &&
                                        (!languageHad || PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == languageValue),
                                        context + ": nonpersistent switch leaves the saved language preference unchanged");

                                    bool captureCase = preset == 0 && english &&
                                        ((level == 5 && phase == 1 && hint == 3) ||
                                         (level == 6 && phase == 0 && hint == 2) ||
                                         (level == 20 && phase == 1 && hint == 0));
                                    if (captureCase && captured.Add(level))
                                    {
                                        try { capture?.Invoke("task-strip-language-en-level-" + level); }
                                        catch (Exception error) { Check(false, context + ": capture failed: " + error.Message); }
                                    }
                                }
                            }
                        }
                    }
                }
                Check(IdentitySignature() == identities, "level/species IDs and resource paths remain unchanged");
                Check(state.Save.shellMask == JsonUtility.FromJson<OceanSaveData>(save).shellMask &&
                    state.Save.fishMask == JsonUtility.FromJson<OceanSaveData>(save).fishMask &&
                    state.Save.shark == JsonUtility.FromJson<OceanSaveData>(save).shark,
                    "isolated presentation checks do not add collection history");
                Check(view.transform.position == eye && view.transform.rotation == facing && view.cullingMask == cullingMask &&
                    SceneManager.GetActiveScene().handle == sceneHandle,
                    "task-strip checks do not move the camera or replace the scene");
            }
            finally
            {
                RestoreSave(state.Save, save);
                hands.ApplyComfort(comfort, false);
                locale?.Dispose();
                if (PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) != languageHad ||
                    (languageHad && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) != languageValue))
                {
                    if (languageHad) PlayerPrefs.SetInt(OceanLanguage.PreferenceKey, languageValue);
                    else PlayerPrefs.DeleteKey(OceanLanguage.PreferenceKey);
                    PlayerPrefs.Save();
                }
                if (menuWasOpen) panel.OpenMenu();
                else panel.CloseMenu();
                showPage?.Invoke(panel, new[] { previousPage });
                panel.Refresh();
                panel.SetAvailable(wasAvailable);
                hands.SetMenuLocomotionPaused(locomotionWasPaused);
                Check(JsonUtility.ToJson(state.Save) == save, "finally restores the exact isolated campaign save in memory");
                Check(hands.Comfort.Speed == comfort.Speed && hands.Comfort.Volume == comfort.Volume &&
                    hands.Comfort.Turn == comfort.Turn && hands.Comfort.Seated == comfort.Seated &&
                    hands.Comfort.TextScale == comfort.TextScale,
                    "finally restores the original comfort/text-size preference in memory");
                Check(PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) == languageHad &&
                    (!languageHad || PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == languageValue),
                    "finally restores saved language preference presence and value");
            }
            Debug.Log("TASK_STRIP_LANGUAGE_CHECKS " + (issues.Count == 0 ? "PASS" : "FAIL") +
                " assertions=" + Assertions + " issues=" + issues.Count +
                "; actual closed-menu UI with synthetic isolated state/locale; no device, player file or wearer acceptance");
            if (issues.Count > 0)
                throw new InvalidOperationException("Task-strip language checks found " + issues.Count +
                    " issues. Inspect Issues for the complete list.\n" + string.Join("\n", issues.GetRange(0, Math.Min(12, issues.Count))));
        }
    }
}
#endif
