#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real presentation objects with synthetic message data. No pickups,
    /// campaign state, achievement awards, tracked input or device actions.</summary>
    public static class OceanLanguageWorldChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int Assertions { get; private set; }
        public static bool ExistingHudChecked { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Language world: " + message); }
        static T Get<T>(object owner, string name)
        {
            var field = owner.GetType().GetField(name, Private);
            if (field == null) throw new InvalidOperationException("Missing read-only presentation probe: " + name);
            return (T)field.GetValue(owner);
        }
        static bool HasHan(string value)
        { foreach (char c in value) if (c >= '\u4e00' && c <= '\u9fff') return true; return false; }

        readonly struct ToastSnapshot
        {
            public readonly string Raw;
            public readonly double Started;
            public readonly float Duration;
            public readonly int Queued, Mask, Distinct, Coins;
            public readonly bool Celebrating;
            public readonly float Opacity;
            readonly bool[] audioPlaying;
            readonly AudioClip[] audioClips;
            public ToastSnapshot(TreasureFeedback feedback)
            {
                Raw = feedback.CurrentNotification; Started = Get<double>(feedback, "toastStarted");
                Duration = Get<float>(feedback, "toastDuration"); Queued = Get<int>(feedback, "queueCount");
                Mask = feedback.CollectedMask; Distinct = feedback.DistinctCount;
                Coins = feedback.ActiveCelebrationCoins; Celebrating = feedback.IsCelebrating;
                Opacity = Get<CanvasGroup>(feedback, "toastAlpha").alpha;
                audioPlaying=new bool[3];audioClips=new AudioClip[3];int audioIndex=0;
                foreach(string field in new[]{"pickupAudio","rewardAudio","musicAudio"})
                {var source=Get<AudioSource>(feedback,field);audioPlaying[audioIndex]=source.isPlaying;audioClips[audioIndex++]=source.clip;}
            }
            public void CheckUnchanged(TreasureFeedback feedback, string context)
            {
                Check(feedback.CurrentNotification == Raw, context + " keeps raw message identity");
                Check(Get<double>(feedback, "toastStarted") == Started && Get<float>(feedback, "toastDuration") == Duration,
                    context + " does not restart or extend the toast");
                Check(Get<int>(feedback, "queueCount") == Queued, context + " keeps notification ordering");
                Check(feedback.CollectedMask == Mask && feedback.DistinctCount == Distinct &&
                    feedback.ActiveCelebrationCoins == Coins && feedback.IsCelebrating == Celebrating,
                    context + " cannot collect an item or start a reward");
                Check(Get<CanvasGroup>(feedback, "toastAlpha").alpha == Opacity, context + " keeps its current fade phase");
                int audioIndex=0;
                foreach (string field in new[] { "pickupAudio", "rewardAudio", "musicAudio" })
                {
                    var source=Get<AudioSource>(feedback,field);
                    Check(source.isPlaying==audioPlaying[audioIndex]&&source.clip==audioClips[audioIndex],context+" preserves observed playback state for "+field);
                    audioIndex++;
                }
            }
        }

        public static IEnumerator RunInPlayMode(Camera view, Action<string> capture)
        {
            Assertions = 0; ExistingHudChecked = false;
            Check(Application.isPlaying && view != null, "requires an existing real PlayMode camera");
            float oldVolume = AudioListener.volume;
            bool languageHad = PlayerPrefs.HasKey(OceanLanguage.PreferenceKey);
            int languageValue = PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, 0);
            Vector3 eye = view.transform.position; Quaternion rotation = view.transform.rotation;
            int mask = view.cullingMask;
            GameObject host = null; OceanCinematicTitles titles = null; Material material = null;
            IDisposable locale = null;
            int initialSubscribers = OceanLanguage.SubscriberCountForValidation;
            try
            {
                AudioListener.volume = 0;
                locale = OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese);
                host = new GameObject("Isolated language notification presentation");
                var feedback = host.AddComponent<TreasureFeedback>(); feedback.Initialize(view);
                feedback.SetJournalVisible(false); feedback.SetVisible(true);

                const string firstRaw = "请先寻找：扇贝";
                feedback.ShowLocalizedCampaignMessage(firstRaw,
                    () => OceanLanguage.Format("请先寻找：{0}", OceanLanguage.Text("扇贝")), pickup:false);
                int liveCount = 2, eventCount = liveCount, eventGoal = 5;
                string queuedRaw = "你收集到1种贝壳：扇贝（2/5）";
                feedback.ShowLocalizedCampaignMessage(queuedRaw,
                    () => OceanLanguage.Format("你收集到1种贝壳：{0}（{1}/{2}）", OceanLanguage.Text("扇贝"), eventCount, eventGoal), pickup:false);
                liveCount = 99;
                Check(liveCount != eventCount && Get<int>(feedback, "queueCount") == 1,
                    "queued synthetic message has an immutable event-value snapshot");
                var first = new ToastSnapshot(feedback);
                Text toast = Get<Text>(feedback, "toastLabel");
                Check(toast.text == firstRaw, "actual toast starts in Chinese");
                foreach (var choice in new[] { OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English })
                {
                    OceanLanguage.SetPreference(choice, false);
                    first.CheckUnchanged(feedback, "switch " + choice);
                    Check(choice == OceanLanguageChoice.English ? toast.text.StartsWith("Find this one first:", StringComparison.Ordinal) && !HasHan(toast.text) : toast.text == firstRaw,
                        "current toast glyphs change immediately to " + choice);
                    Check(toast.rectTransform.localScale.x > 0f && toast.rectTransform.localScale.x <= 1f,
                        "translated toast retains bounded fitting");
                }
                Canvas.ForceUpdateCanvases(); capture?.Invoke("language-world-feedback-en");

                double timeout = Time.unscaledTimeAsDouble + 7;
                while (feedback.CurrentNotification == firstRaw)
                {
                    Check(Time.unscaledTimeAsDouble < timeout, "the original current toast still expires");
                    yield return null;
                }
                Check(feedback.CurrentNotification == queuedRaw && Get<int>(feedback, "queueCount") == 0,
                    "normal toast expiry reveals exactly the queued message");
                Check(Get<double>(feedback, "toastStarted") >= first.Started + first.Duration - .02,
                    "language switching cannot expire the original toast early");
                Check(toast.text.StartsWith("Shell collected:", StringComparison.Ordinal) && toast.text.Contains("2/5") &&
                    !toast.text.Contains("99") && !HasHan(toast.text),
                    "queued English rendering uses event values, not later data");
                var second = new ToastSnapshot(feedback);
                OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                second.CheckUnchanged(feedback, "queued message returns to Chinese");
                Check(toast.text == queuedRaw, "queued message can return to its exact original Chinese copy");
                Canvas.ForceUpdateCanvases(); capture?.Invoke("language-world-feedback-zh");
                timeout = Time.unscaledTimeAsDouble + 7;
                while (!string.IsNullOrEmpty(feedback.CurrentNotification))
                {
                    Check(Time.unscaledTimeAsDouble < timeout, "the queued toast expires at its normal duration");
                    yield return null;
                }
                Check(Time.unscaledTimeAsDouble >= second.Started + second.Duration - .02 && feedback.CanShowExplorationMessage,
                    "the queue returns to idle without inventing another event");
                Check(feedback.CollectedMask == 0 && feedback.DistinctCount == 0 && !feedback.IsCelebrating,
                    "presentation checks leave collection and rewards untouched");
                feedback.SetVisible(false);

                Font body = Resources.Load<Font>("Treasure/TreasureFont");
                Font titleFont = Resources.Load<Font>("Loading/OceanTitleFont");
                Shader shader = Resources.Load<Shader>("PerformanceHudUI");
                Check(body != null && titleFont != null && shader != null, "uses admitted production fonts and title shader");
                material = new Material(shader) { name = "Isolated language cinematic material" };
                int layer = 0;
                while (layer < 31 && (view.cullingMask & (1 << layer)) == 0) layer++;
                titles = OceanCinematicTitles.Create(view.transform, view, titleFont, body, material, layer);
                titles.Show(true); titles.Animate(1.9f, 5.2f);
                Text title = Get<Text>(titles, "title");
                RectTransform content = Get<RectTransform>(titles, "content");
                float titleAlpha = titles.TitleOpacity, hintAlpha = titles.HintOpacity;
                Vector2 contentPosition = content.anchoredPosition;
                Vector3 contentScale = content.localScale;
                Check(title.text == OceanEntry.ChapterText && titleAlpha > .5f && hintAlpha > .5f,
                    "actual chapter typography is already mid-presentation");
                foreach (var choice in new[] { OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese })
                {
                    OceanLanguage.SetPreference(choice, false);
                    Check(title.text == (choice == OceanLanguageChoice.English ? "Level 1 - Shell Treasure Hunt" : OceanEntry.ChapterText),
                        "chapter title glyphs change to " + choice);
                    Check(titles.IsVisible && titles.TitleOpacity == titleAlpha && titles.HintOpacity == hintAlpha &&
                        content.anchoredPosition == contentPosition && content.localScale == contentScale && Get<bool>(titles, "chapter") && !Get<bool>(titles, "failed"),
                        "title language switching cannot restart, hide or change its presentation stage");
                    Check(title.preferredWidth * title.rectTransform.localScale.x <= 810.5f && title.rectTransform.localScale.x > 0f,
                        "translated title is refitted to its existing safe width");
                    Canvas.ForceUpdateCanvases(); capture?.Invoke(choice == OceanLanguageChoice.English ? "language-world-title-en" : "language-world-title-zh");
                }

                // Reuse a panel only if its owner has already created it. Do not create
                // a diagnostics singleton, change HUD preferences or replace its camera.
                var hud = PerformanceHud.Instance;
                var panel = hud != null ? Get<GameObject>(hud, "panel") : null;
                if (panel != null)
                {
                    Text note = Get<Text>(hud, "note"), header = null;
                    foreach (var candidate in panel.GetComponentsInChildren<Text>(true))
                        if (candidate.name == "Title") header = candidate;
                    Check(note != null && header != null, "existing HUD exposes its real title and sampling note");
                    bool visible = panel.activeSelf;
                    OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
                    Check(header.text == "PERFORMANCE" && note.text.StartsWith("FPS loop ~1s", StringComparison.Ordinal),
                        "existing performance HUD immediately renders English");
                    OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                    Check(header.text == "性能面板" && note.text.StartsWith("FPS约1秒平均", StringComparison.Ordinal),
                        "existing performance HUD immediately renders Chinese");
                    Check(PerformanceHud.Instance == hud && panel.activeSelf == visible,
                        "HUD translation keeps the existing owner and visibility");
                    ExistingHudChecked = true;
                }
                Check(view.transform.position == eye && view.transform.rotation == rotation && view.cullingMask == mask,
                    "presentation localization never moves or reconfigures the caller's camera");
                Check(PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) == languageHad &&
                    (!languageHad || PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == languageValue),
                    "nonpersistent validation leaves the saved language preference exact");
            }
            finally
            {
                if (titles != null) Object.DestroyImmediate(titles.gameObject);
                if (host != null) Object.DestroyImmediate(host);
                if (material != null) Object.DestroyImmediate(material);
                locale?.Dispose(); AudioListener.volume = oldVolume;
                if (PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) != languageHad ||
                    (languageHad && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) != languageValue))
                {
                    if (languageHad) PlayerPrefs.SetInt(OceanLanguage.PreferenceKey, languageValue);
                    else PlayerPrefs.DeleteKey(OceanLanguage.PreferenceKey);
                    PlayerPrefs.Save();
                }
            }
            // The production feedback owner schedules its generated canvases for normal
            // end-of-frame destruction; let that cleanup finish without touching the view.
            yield return null;
            Check(OceanLanguage.SubscriberCountForValidation == initialSubscribers,
                "owned title and feedback subscriptions are fully released");
            Debug.Log("LANGUAGE_WORLD_CHECKS_PASS assertions=" + Assertions + "; existingHud=" + ExistingHudChecked +
                "; actual presentation with synthetic message data, no gameplay/device/wearer acceptance");
        }
    }
}
#endif
