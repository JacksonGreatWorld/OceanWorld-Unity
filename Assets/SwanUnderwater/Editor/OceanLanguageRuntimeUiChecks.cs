#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Regression probes for actual HUD glyphs and suspended expedition captions.
    /// Synthetic, isolated campaign data only. Does not drive hands or write player progress.</summary>
    public static class OceanLanguageRuntimeUiChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> issues = new List<string>();
        public static int Assertions { get; private set; }
        public static IReadOnlyList<string> Issues => issues;

        static void Check(bool condition, string message)
        { Assertions++; if (!condition) issues.Add(message); }
        static T Get<T>(object owner, string field)
        {
            if (owner == null) { Check(false, "Missing owner for " + field); return default; }
            var info = owner.GetType().GetField(field, Private);
            Check(info != null, "Existing private probe " + field);
            return info != null ? (T)info.GetValue(owner) : default;
        }
        static bool HasHan(string value)
        { foreach (char c in value ?? "") if (c >= '\u4e00' && c <= '\u9fff') return true; return false; }
        static Text NamedText(GameObject root, string name)
        {
            if (root != null) foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (text.name == name) return text;
            return null;
        }
        static void TextEquals(GameObject root, string name, string expected, string context)
        {
            Text label = NamedText(root, name);
            Check(label != null, context + ": actual " + name + " exists");
            if (label != null) Check(label.text == expected,
                context + ": " + name + " expected '" + expected + "', actual '" + label.text + "'");
        }
        static void CheckHud(GameObject panel, bool english, string context)
        {
            TextEquals(panel, "Title", english ? "PERFORMANCE" : "性能面板", context);
            TextEquals(panel, "Draw Label", english ? "Draw" : "绘制", context);
            TextEquals(panel, "Tri Label", english ? "Tri" : "三角", context);
            TextEquals(panel, "Pass Label", english ? "Pass" : "切换", context);
            foreach (string term in new[] { "FPS", "CPU", "GPU" }) TextEquals(panel, term + " Label", term, context);
            Text note = NamedText(panel, "Scope");
            Check(note != null && (english ? note.text.StartsWith("FPS loop ~1s", StringComparison.Ordinal) :
                note.text.StartsWith("FPS约1秒平均", StringComparison.Ordinal)), context + ": sampling explanation follows language");
            if (panel == null) return;
            Check(panel.activeInHierarchy, context + ": actual performance panel is visible");
                foreach (var text in panel.GetComponentsInChildren<Text>(true))
                {
                    if (string.IsNullOrWhiteSpace(text.text)) continue;
                    Check(text.preferredHeight <= text.rectTransform.rect.height + 1f,
                        context + ": actual glyph height fits " + text.name + " (" + text.fontSize + "pt, " +
                        text.preferredHeight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                        text.rectTransform.rect.height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "px)");
                    if (text.isActiveAndEnabled)
                    {
                        // Unity 6 returns the renderer's mesh; do not destroy or
                        // mutate geometry owned by the production Text component.
                        var mesh = text.canvasRenderer.GetMesh();
                        Check(mesh != null && mesh.vertexCount > 0, context + ": actual CanvasRenderer has nonempty glyph geometry for " +
                            text.name + " (" + text.fontSize + "pt, text='" + text.text + "')");
                    }
                    if (english) Check(!HasHan(text.text), context + ": no Chinese remains in " + text.name);
                }
        }

        sealed class MechanicSnapshot
        {
            readonly string save;
            readonly OceanMechanicRules rules;
            readonly OceanMechanicKind kind;
            readonly int stage, completed;
            readonly float dwell, oxygen, carry;
            readonly bool captionEnabled, canvasEnabled, stationActive;
            readonly Bounds[] bounds;
            readonly bool[] nodeActive;
            public MechanicSnapshot(OceanMechanicRuntime runtime, OceanCampaignState state)
            {
                save = JsonUtility.ToJson(state.Save);
                rules = runtime.Rules; kind = rules.Kind; stage = rules.Stage; completed = rules.Completed;
                dwell = rules.Dwell; oxygen = rules.Oxygen; carry = rules.CarrySeconds;
                RectTransform rect = runtime.CaptionRectForValidation;
                captionEnabled = rect.GetComponentInChildren<Text>(true).enabled;
                canvasEnabled = rect.GetComponent<Canvas>().enabled;
                stationActive = runtime.StationRoot.gameObject.activeSelf;
                bounds = new Bounds[runtime.VisibleNodeCount]; nodeActive = new bool[bounds.Length];
                for (int i = 0; i < bounds.Length; i++)
                { bounds[i] = runtime.NodeBounds(i); nodeActive[i] = runtime.NodeLabelRectForValidation(i).parent.gameObject.activeSelf; }
            }
            public void CheckUnchanged(OceanMechanicRuntime runtime, OceanCampaignState state, string context)
            {
                Check(JsonUtility.ToJson(state.Save) == save, context + ": campaign JSON, elapsed time and durable progress unchanged");
                Check(ReferenceEquals(runtime.Rules, rules) && rules.Kind == kind && rules.Stage == stage &&
                    rules.Completed == completed && rules.Dwell == dwell && rules.Oxygen == oxygen && rules.CarrySeconds == carry,
                    context + ": rule identity, transient stage and clocks unchanged");
                RectTransform rect = runtime.CaptionRectForValidation;
                Check(rect.GetComponentInChildren<Text>(true).enabled == captionEnabled && rect.GetComponent<Canvas>().enabled == canvasEnabled &&
                    runtime.StationRoot.gameObject.activeSelf == stationActive, context + ": caption and station visibility unchanged");
                Check(runtime.VisibleNodeCount == bounds.Length, context + ": no node created or removed");
                for (int i = 0; i < Math.Min(bounds.Length, runtime.VisibleNodeCount); i++)
                {
                    Check(runtime.NodeBounds(i) == bounds[i], context + ": interaction Bounds unchanged for node " + i);
                    Check(runtime.NodeLabelRectForValidation(i).parent.gameObject.activeSelf == nodeActive[i],
                        context + ": hidden node remains hidden " + i);
                }
            }
        }

        static IEnumerator CheckMechanicState(OceanMechanicRuntime runtime, OceanCampaignState state, string context, Action<string> capture)
        {
            Text caption = runtime.CaptionRectForValidation != null ? runtime.CaptionRectForValidation.GetComponentInChildren<Text>(true) : null;
            Check(caption != null && runtime.StationRoot != null && runtime.Rules != null, context + ": actual station and caption exist");
            if (caption == null || runtime.StationRoot == null || runtime.Rules == null) yield break;
            string chinese = caption.text;
            Check(HasHan(chinese), context + ": starts with authored Chinese copy");
            var snapshot = new MechanicSnapshot(runtime, state);
            Canvas.ForceUpdateCanvases(); capture?.Invoke("language-runtime-" + context + "-before");
            int index = 0;
            foreach (var choice in new[] { OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese })
            {
                string step = context + " / " + choice + " / " + index;
                OceanLanguage.SetPreference(choice, false);
                snapshot.CheckUnchanged(runtime, state, step + " immediate");
                bool english = choice == OceanLanguageChoice.English;
                Check(english ? !string.IsNullOrWhiteSpace(caption.text) && !HasHan(caption.text) : caption.text == chinese,
                    step + ": caption language updates without a gameplay tick; actual='" + caption.text + "'");
                for (int n = 0; n < runtime.VisibleNodeCount; n++)
                {
                    Text label = runtime.NodeLabelRectForValidation(n).GetComponentInChildren<Text>(true);
                    Check(label != null && !string.IsNullOrWhiteSpace(label.text), step + ": node label exists " + n);
                    if (label != null) Check(english ? !HasHan(label.text) : HasHan(label.text), step + ": node label language " + n);
                }
                yield return null;
                snapshot.CheckUnchanged(runtime, state, step + " following frame");
                Canvas.ForceUpdateCanvases();
                if (index > 0) capture?.Invoke("language-runtime-" + context + (english ? "-en" : "-zh-return"));
                index++;
            }
        }

        static bool ForceUnavailable(OceanMechanicRuntime runtime)
        {
            var field = typeof(OceanMechanicRuntime).GetField("explorationAvailable", Private);
            var refresh = typeof(OceanMechanicRuntime).GetMethod("RefreshPresentation", Private);
            Check(field != null && refresh != null, "Isolated blocked-route fixture uses existing private setup state");
            if (field == null || refresh == null) return false;
            field.SetValue(runtime, false);
            for (int i = 0; i < runtime.VisibleNodeCount; i++) runtime.NodeLabelRectForValidation(i).parent.gameObject.SetActive(false);
            refresh.Invoke(runtime, new object[] { true });
            return true;
        }

        public static IEnumerator RunInPlayMode(Camera view, Action<string> capture)
        {
            Assertions = 0; issues.Clear();
            Check(Application.isPlaying && view != null, "Existing PlayMode view required");
            if (!Application.isPlaying || view == null)
                throw new InvalidOperationException("Language runtime UI: " + string.Join("\n", issues));
            float volume = AudioListener.volume;
            bool languageHad = PlayerPrefs.HasKey(OceanLanguage.PreferenceKey);
            int languageValue = PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, 0);
            bool hudHad = PerformanceHud.HasVisibilityPreferenceForValidation, hudVisible = PerformanceHud.PreferenceVisible;
            var hud = PerformanceHud.Instance;
            Camera previousPreview = hud != null ? Get<Camera>(hud, "editorPreviewCamera") : null;
            Vector3 eye = view.transform.position; Quaternion rotation = view.transform.rotation; int culling = view.cullingMask;
            GameObject host = null; IDisposable locale = null;
            try
            {
                AudioListener.volume = 0;
                locale = OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese);
                Check(hud != null, "Reuse the existing PerformanceHud.Instance; never create a replacement diagnostics owner");
                if (hud != null)
                {
                    PerformanceHud.SetPreferenceVisible(true);
                    hud.SetEditorPreviewCamera(view);
                    yield return null;
                    var panel = Get<GameObject>(hud, "panel");
                    Check(panel != null, "Existing HUD builds its actual presentation panel");
                    Canvas.ForceUpdateCanvases(); capture?.Invoke("language-runtime-hud-before");
                    int index = 0;
                    foreach (var choice in new[] { OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese })
                    {
                        OceanLanguage.SetPreference(choice, false);
                        Canvas.ForceUpdateCanvases();
                        CheckHud(panel, choice == OceanLanguageChoice.English, "HUD immediate " + choice + " #" + index);
                        // Exercise the actual periodic LateUpdate rather than only the language callback.
                        yield return new WaitForSecondsRealtime(.55f);
                        Canvas.ForceUpdateCanvases();
                        CheckHud(panel, choice == OceanLanguageChoice.English, "HUD periodic " + choice + " #" + index);
                        Check(PerformanceHud.Instance == hud, "Language switching retains the sole runtime diagnostics owner");
                        capture?.Invoke("language-runtime-hud-" + index + (choice == OceanLanguageChoice.English ? "-en" : "-zh"));
                        index++;
                    }
                }

                OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                host = new GameObject("Isolated completed and suspended expedition language fixture");
                var hands = host.AddComponent<HandSwimController>(); hands.enabled = false;
                var state = new OceanCampaignState();
                var runtime = host.AddComponent<OceanMechanicRuntime>();
                runtime.Initialize(null, view, hands, state, null);
                Vector3 spawn = view.transform.position - Vector3.up * 1.65f - Vector3.forward * 4f;

                state.StartLevel(6);
                for (int i = 0; i < state.Definition.MechanicTarget; i++)
                    Check(state.RecordMechanicProgress(900000 + i), "Synthetic isolated completed-route state accepts unique event " + i);
                Check(state.MechanicComplete, "Synthetic route is complete without changing the real campaign");
                runtime.ConfigureLevel(spawn);
                yield return null;
                runtime.TickGameplay(.02f, true);
                yield return CheckMechanicState(runtime, state, "completed-visible", capture);
                runtime.TickGameplay(.02f, false);
                Check(!runtime.CaptionRectForValidation.GetComponent<Canvas>().enabled, "Menu pause hides completed-route caption");
                yield return CheckMechanicState(runtime, state, "completed-paused", capture);
                runtime.TickGameplay(.02f, true);
                Check(runtime.CaptionRectForValidation.GetComponent<Canvas>().enabled, "Resume restores completed-route caption without tracked input");
                yield return CheckMechanicState(runtime, state, "completed-resumed", capture);

                state.StartLevel(6); runtime.ConfigureLevel(spawn);
                yield return null;
                if (ForceUnavailable(runtime))
                {
                    runtime.TickGameplay(.02f, true);
                    yield return CheckMechanicState(runtime, state, "unavailable-visible", capture);
                    runtime.TickGameplay(.02f, false);
                    Check(!runtime.CaptionRectForValidation.GetComponent<Canvas>().enabled, "Menu pause hides unavailable-route caption");
                    yield return CheckMechanicState(runtime, state, "unavailable-paused", capture);
                    runtime.TickGameplay(.02f, true);
                    Check(runtime.CaptionRectForValidation.GetComponent<Canvas>().enabled, "Resume restores unavailable-route caption");
                    yield return CheckMechanicState(runtime, state, "unavailable-resumed", capture);
                }
                Check(view.transform.position == eye && view.transform.rotation == rotation && view.cullingMask == culling,
                    "UI checks do not move or reconfigure the viewing camera");
                Check(PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) == languageHad &&
                    (!languageHad || PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == languageValue),
                    "Nonpersistent locale switches preserve the saved language preference");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                locale?.Dispose(); AudioListener.volume = volume;
                PerformanceHud.RestoreVisibilityPreferenceForValidation(hudHad, hudVisible);
                if (hud != null) hud.SetEditorPreviewCamera(previousPreview);
                if (PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) != languageHad ||
                    (languageHad && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) != languageValue))
                {
                    if (languageHad) PlayerPrefs.SetInt(OceanLanguage.PreferenceKey, languageValue);
                    else PlayerPrefs.DeleteKey(OceanLanguage.PreferenceKey);
                    PlayerPrefs.Save();
                }
            }
            yield return null;
            Check(AudioListener.volume == volume && PerformanceHud.PreferenceVisible == hudVisible &&
                PerformanceHud.HasVisibilityPreferenceForValidation == hudHad, "Audio and HUD preference restored exactly");
            if (issues.Count != 0)
            {
                foreach (string issue in issues) Debug.LogError("LANGUAGE_RUNTIME_UI_ISSUE " + issue);
                throw new InvalidOperationException("Language runtime UI: " + issues.Count + " issue(s), assertions=" + Assertions +
                    "\n" + string.Join("\n", issues));
            }
            Debug.Log("LANGUAGE_RUNTIME_UI_CHECKS_PASS assertions=" + Assertions +
                "; actual HUD metrics/layout and suspended/completed/blocked expedition captions; isolated synthetic state, not headset acceptance.");
        }
    }
}
#endif
