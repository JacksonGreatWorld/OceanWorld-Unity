#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Isolated Editor localization checks. No device files, player rewards or network.</summary>
    public static class OceanLanguageChecks
    {
        const string SeenKey = "ocean_interactive_tutorial_seen_v1";
        static readonly string[] PreservedKeys = { OceanLanguage.PreferenceKey, SeenKey,
            "ocean_comfort_audio_environment_mute_v1", "ocean_comfort_audio_music_mute_v1", "ocean_comfort_audio_feedback_mute_v1" };
        static readonly Regex Placeholder = new Regex(@"(?<!\{)\{(\d+)(?:,[+-]?\d+)?(?::[^{}]*)?\}(?!\})", RegexOptions.CultureInvariant);
        public static int Assertions { get; private set; }
        static readonly List<string> ReadabilityIssues = new List<string>();
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Language: " + message); }

        sealed class PreferenceSnapshot
        {
            readonly bool[] present = new bool[PreservedKeys.Length];
            readonly int[] values = new int[PreservedKeys.Length];
            public PreferenceSnapshot()
            { for (int i = 0; i < PreservedKeys.Length; i++) { present[i] = PlayerPrefs.HasKey(PreservedKeys[i]); values[i] = PlayerPrefs.GetInt(PreservedKeys[i], 0); } }
            public bool Unchanged(int first = 0)
            {
                for (int i = first; i < PreservedKeys.Length; i++)
                    if (PlayerPrefs.HasKey(PreservedKeys[i]) != present[i] || (present[i] && PlayerPrefs.GetInt(PreservedKeys[i], 0) != values[i])) return false;
                return true;
            }
            public void Restore()
            {
                for (int i = 0; i < PreservedKeys.Length; i++)
                    if (present[i]) PlayerPrefs.SetInt(PreservedKeys[i], values[i]); else PlayerPrefs.DeleteKey(PreservedKeys[i]);
                PlayerPrefs.Save();
            }
        }

        public static void RunAll()
        {
            Assertions = 0;
            var preferences = new PreferenceSnapshot(); float volume = AudioListener.volume;
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.FollowSystem, "zh", "CN"))
            {
                try
                {
                    CheckDefaultsAndPersistence();
                    CheckDictionary();
                    CheckEnglishCinemaTitleGlyphs();
                    CheckPanelAndLifetime();
                    Check(preferences.Unchanged(2), "language controls do not change audio preferences");
                    Debug.Log("OCEAN_LANGUAGE_CHECKS_PASS assertions=" + Assertions + "; OS selection, persistence reload, bilingual table, actual UI callbacks and cleanup; no hardware claim.");
                }
                finally { preferences.Restore(); AudioListener.volume = volume; }
            }
            Check(preferences.Unchanged(), "all original preference presence and values restored");
        }

        static void CheckDefaultsAndPersistence()
        {
            var cases = new[] { new[] { "zh-CN", "CN" }, new[] { "en-US", "US" }, new[] { "en", "CN" },
                new[] { "zh", "US" }, new[] { "und", "CN" }, new[] { "unknown", "US" }, new[] { "unknown", "" } };
            var expected = new[] { OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English,
                OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.SimplifiedChinese,
                OceanLanguageChoice.English, OceanLanguageChoice.English };
            for (int i = 0; i < cases.Length; i++) Check(OceanLanguage.ResolveDefault(cases[i][0], cases[i][1]) == expected[i],
                "system language/region precedence " + cases[i][0] + "/" + cases[i][1]);
            Check(OceanLanguage.ResolveDefault("ZH_hans_CN", "US") == OceanLanguageChoice.SimplifiedChinese, "Chinese script/underscore tags remain Chinese");
            PlayerPrefs.DeleteKey(OceanLanguage.PreferenceKey); PlayerPrefs.Save();
            OceanLanguage.ReloadForValidation("en", "US");
            Check(OceanLanguage.Preference == OceanLanguageChoice.FollowSystem && OceanLanguage.IsEnglish, "new English installation follows system");
            Check(OceanLanguage.Text("海底世界") == "OceanWorld" && !PlayerPrefs.HasKey(OceanLanguage.PreferenceKey), "reading automatic defaults does not invent a saved override");
            OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese);
            Check(PlayerPrefs.HasKey(OceanLanguage.PreferenceKey) && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == 1, "explicit Chinese choice persists");
            OceanLanguage.ReloadForValidation("en", "US");
            Check(OceanLanguage.Preference == OceanLanguageChoice.SimplifiedChinese && !OceanLanguage.IsEnglish, "manual Chinese survives restart under English OS");
            OceanLanguage.SetPreference(OceanLanguageChoice.English);
            OceanLanguage.ReloadForValidation("zh", "CN");
            Check(OceanLanguage.Preference == OceanLanguageChoice.English && OceanLanguage.IsEnglish, "manual English survives restart under Chinese OS");
            OceanLanguage.RefreshSystemDefault();
            Check(OceanLanguage.Preference == OceanLanguageChoice.English && OceanLanguage.IsEnglish, "OS refresh cannot replace a manual choice");
            OceanLanguage.SetPreference(OceanLanguageChoice.FollowSystem);
            OceanLanguage.ReloadForValidation("und", "CN");
            Check(!OceanLanguage.IsEnglish && OceanLanguage.Preference == OceanLanguageChoice.FollowSystem && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey, -1) == 0,
                "saved follow-system restores the Chinese region fallback after reload");
            OceanLanguage.ReloadForValidation("und", "US");
            Check(OceanLanguage.IsEnglish && OceanLanguage.Preference == OceanLanguageChoice.FollowSystem, "follow-system accepts a later US region default");
            PlayerPrefs.SetInt(OceanLanguage.PreferenceKey, 99); PlayerPrefs.Save();
            OceanLanguage.ReloadForValidation("zh", "CN");
            Check(OceanLanguage.Preference == OceanLanguageChoice.FollowSystem && !OceanLanguage.IsEnglish && PlayerPrefs.GetInt(OceanLanguage.PreferenceKey) == 99,
                "invalid saved choice falls back without rewriting the original value");
            OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
        }

        static void CheckDictionary()
        {
            var entries = OceanLanguage.EnglishStringsForValidation;
            Check(entries.Count > 0, "translation catalog is nonempty");
            foreach (var pair in entries)
            {
                Check(!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value), "nonempty source and English value: " + pair.Key);
                var sourceTokens = Tokens(pair.Key); var translatedTokens = Tokens(pair.Value);
                Check(sourceTokens.SetEquals(translatedTokens), "matching format placeholders: " + pair.Key);
                int maximum = -1;
                foreach (Match token in Placeholder.Matches(pair.Key)) maximum = Math.Max(maximum, int.Parse(token.Groups[1].Value, CultureInfo.InvariantCulture));
                var samples = new object[maximum + 1]; for (int i = 0; i < samples.Length; i++) samples[i] = 1.25d;
                string.Format(CultureInfo.InvariantCulture, pair.Key, samples);
                string.Format(CultureInfo.InvariantCulture, pair.Value, samples);
            }
            string domain = DomainSnapshot();
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.English))
            {
                foreach (var pair in entries) Check(OceanLanguage.Text(pair.Key) == pair.Value, "English exact lookup: " + pair.Key);
                Check(DomainSnapshot() == domain, "English leaves durable IDs, resource paths, categories and authored data unchanged");
            }
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese))
                foreach (var pair in entries) Check(OceanLanguage.Text(pair.Key) == pair.Key, "Chinese roundtrip returns the original source: " + pair.Key);
            Check(DomainSnapshot() == domain, "Chinese roundtrip leaves authored domain data unchanged");
        }

        static HashSet<string> Tokens(string value)
        { var tokens = new HashSet<string>(StringComparer.Ordinal); foreach (Match match in Placeholder.Matches(value)) tokens.Add(match.Value); return tokens; }
        static string DomainSnapshot()
        {
            var b = new StringBuilder();
            foreach (var entry in OceanSpeciesCatalog.All) b.Append(entry.Id).Append('|').Append(entry.Name).Append('|').Append(entry.Category).Append('|').Append(entry.ResourcePath).Append('|').Append(entry.FirstLevel).Append('\n');
            foreach (var entry in OceanAchievementCatalog.All) b.Append(entry.Id).Append('|').Append(entry.Name).Append('|').Append(entry.Category).Append('|').Append(entry.Target).Append('\n');
            for (int level = 1; level <= OceanLevelCatalog.Count; level++)
            { var entry = OceanLevelCatalog.Get(level); b.Append(entry.Level).Append('|').Append(entry.Name).Append('|').Append(entry.Brief).Append('|').Append(entry.MechanicHint).Append('|').Append(entry.ShellMask).Append('|').Append(entry.FishMask).Append('\n'); }
            foreach (string path in OceanLevelCatalog.FishPaths) b.Append(path).Append('\n');
            return b.ToString();
        }

        static OceanCampaignPanel.ButtonInfoForValidation Button(OceanCampaignPanel panel, string id)
        { foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) return button; throw new InvalidOperationException("Missing language UI button " + id); }
        static bool HasButton(OceanCampaignPanel panel, string id)
        { foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) return true; return false; }
        static void Click(OceanCampaignPanel panel, string id)
        { Check(panel.ClickForValidation(id), "actual visible UI callback " + id); }
        static void CheckSelected(OceanCampaignPanel panel, OceanLanguageChoice expected)
        {
            string selected = OceanLanguage.Text(" · 已选"); int marked = 0;
            for (int i = 0; i < 3; i++)
            {
                var button = Button(panel, "language_" + i);
                Check(button.enabled && button.rect.rect.width >= 180 && button.rect.rect.height >= 44, "usable language target " + i);
                bool active = button.title.EndsWith(selected, StringComparison.Ordinal);
                Check(active == (i == (int)expected), "only the chosen language has its selected mark"); if (active) marked++;
            }
            if(expected==OceanLanguageChoice.English)
                Check(Button(panel,"language_0").title.StartsWith("Follow system (Chinese)",StringComparison.Ordinal),"manual English keeps the scoped Chinese system default visible");
            Check(marked == 1 && OceanLanguage.Preference == expected && panel.PageNameForDiagnostics == "Language", "selection preserves the language page and has exactly one selected mark");
            Check(Button(panel, "language_1").title.StartsWith("简体中文", StringComparison.Ordinal) && Button(panel, "language_2").title.StartsWith("English", StringComparison.Ordinal), "language autonyms remain recognizable in either locale");
        }

        static void CheckPanelAndLifetime()
        {
            OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
            int listeners = OceanLanguage.SubscriberCountForValidation, bindings = OceanLanguage.BoundLabelCountForValidation;
            var root = new GameObject("Isolated language UI fixture");
            try
            {
                var cameraObject = new GameObject("Language check camera"); cameraObject.transform.SetParent(root.transform, false);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.transform.position = Vector3.up * 1.6f;
                var hands = root.AddComponent<HandSwimController>(); hands.enabled = false;
                var state = new OceanCampaignState(); state.StartLevel(1); string campaign = JsonUtility.ToJson(state.Save);
                var panel = root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(camera, hands, state, (action, level) => { if (action == "resume") panel.CloseMenu(); });
                panel.SetAvailable(true); panel.OpenMenu();
                CheckNaturalistDescriptionReadability(panel);
                Check(JsonUtility.ToJson(state.Save) == campaign, "description probes never write campaign progress");
                Click(panel, "show_comfort"); Click(panel, "language_settings");
                foreach (var choice in new[] { OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.FollowSystem, OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese })
                { Click(panel, "language_" + (int)choice); CheckSelected(panel, choice); }
                Click(panel, "back"); Check(panel.PageNameForDiagnostics == "Comfort", "language back returns to settings");
                Click(panel, "back"); Click(panel, "restart_game");
                string confirmation = panel.PageNameForDiagnostics;
                OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
                Check(panel.PageNameForDiagnostics == confirmation && confirmation == "Confirm" && Button(panel, "confirm").title == OceanLanguage.Text("确认开始"), "live language change keeps an unaccepted confirmation");
                OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                Click(panel, "cancel");
                var tutorial = root.GetComponent<OceanInteractiveTutorial>(); tutorial.Begin(); panel.OpenMenu();
                OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
                Check(Button(panel, "close").title == OceanLanguage.Text("关闭教学") && tutorial.Active, "existing tutorial header updates without completing or closing the lesson");
                Click(panel, "close");
                Check(!tutorial.Active && !panel.IsOpen && JsonUtility.ToJson(state.Save) == campaign, "tutorial close and language changes preserve campaign data");
                panel.OpenMenu();
                Check(Button(panel, "show_comfort").title == OceanLanguage.Text("设置 / 教学"), "authored menu labels translate after repeated changes");
            }
            finally { Object.DestroyImmediate(root); }
            Check(OceanLanguage.SubscriberCountForValidation == listeners, "destroyed menu leaves no language listener");
            Check(OceanLanguage.BoundLabelCountForValidation == bindings, "destroyed menu leaves no authored label bindings");
            OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
            OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
            Check(OceanLanguage.SubscriberCountForValidation == listeners && OceanLanguage.BoundLabelCountForValidation == bindings, "later switches cannot revive destroyed owners");
        }

        static void CheckNaturalistDescriptionReadability(OceanCampaignPanel panel)
        {
            Text details = null;
            foreach (var label in panel.ExpandedRectForValidation.GetComponentsInChildren<Text>(true))
                if (label.name == "Naturalist description") { details = label; break; }
            Check(details != null && details.font != null, "description probe uses the actual constructed field-guide Text and font");
            string originalText = details.text; int originalSize = details.fontSize;
            var problems = new List<string>(); int tested = 0;
            try
            {
                foreach (var language in new[] { OceanLanguageChoice.SimplifiedChinese, OceanLanguageChoice.English })
                using (OceanLanguage.OverrideForValidation(language))
                {
                    foreach (var entry in OceanSpeciesCatalog.All)
                    {
                        // Probe only the isolated presentation component. Do not unlock a
                        // discovery, change Naturalist.State or construct a species model.
                        details.text = OceanLanguage.Format("{0}\n\n栖息线索：{1}\n{2}", OceanLanguage.Text(entry.Description),
                            OceanLanguage.Text(entry.Habitat), OceanLanguage.Text(entry.SearchHint));
                        ProbeNaturalistDetails(details, entry.Id, language, "known", problems); tested++;
                        if (!OceanSpeciesCatalog.IsAvailable(entry.Id))
                        {
                            details.text = OceanLanguage.Format("{0}\n\n本版暂停投放，不计入当前图鉴收集目标。已有观察记录保留。", OceanLanguage.Text(entry.Description));
                            ProbeNaturalistDetails(details, entry.Id, language, "archived", problems); tested++;
                        }
                    }
                }
            }
            finally { details.text = originalText; details.fontSize = originalSize; }
            Check(tested == (OceanSpeciesCatalog.Count + 1) * 2, "all 31 complete records and the archived note checked in both languages");
            Check(problems.Count == 0, "naturalist full-description readability: " + string.Join(" | ", problems));
            Check(details.text == originalText && details.fontSize == originalSize, "description probe restores its exact text and font size");
        }

        static void ProbeNaturalistDetails(Text details, int id, OceanLanguageChoice language, string variant, List<string> problems)
        {
            OceanCampaignPanel.FitNaturalistDetailsForValidation(details);
            string context = id + "/" + language + "/" + variant;
            if (details.fontSize < 18 || details.fontSize > 21) problems.Add(context + " font outside readable range: " + details.fontSize);
            if (details.preferredHeight > details.rectTransform.rect.height + 1f)
                problems.Add(context + " clipped at " + details.fontSize + "px: " + details.preferredHeight + "/" + details.rectTransform.rect.height);
            foreach (char character in details.text)
                if (!char.IsControl(character) && !details.font.HasCharacter(character))
                { problems.Add(context + " missing glyph U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture)); break; }
        }

        static void CheckEnglishCinemaTitleGlyphs()
        {
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.English))
            {
                Font titleFont = Resources.Load<Font>("Loading/OceanTitleFont");
                Check(titleFont != null, "chapter title check uses the actual cinematic title font");
                string chapter = OceanLanguage.Text(OceanEntry.ChapterText);
                Check(!string.IsNullOrWhiteSpace(chapter) && !Regex.IsMatch(chapter, @"[\u4e00-\u9fff]"), "chapter title has an English translation");
                titleFont.RequestCharactersInTexture(chapter, 64, FontStyle.Normal);
                var missing = new StringBuilder();
                foreach (char character in chapter)
                    if (!char.IsControl(character) && !titleFont.HasCharacter(character)) missing.Append("U+").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)).Append(' ');
                Check(missing.Length == 0, "all translated chapter-title glyphs exist in the cinematic font: " + missing);
            }
        }

        static void CheckVisibleCopy(OceanCampaignPanel panel, bool english)
        {
            foreach (var text in panel.ExpandedRectForValidation.GetComponentsInChildren<Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                bool glyphs = true;
                foreach (char character in text.text) if (!char.IsControl(character) && !text.font.HasCharacter(character)) { glyphs = false; break; }
                Check(glyphs, "all visible glyphs exist: " + text.name);
                if (english && !text.text.StartsWith("简体中文", StringComparison.Ordinal))
                    Check(!Regex.IsMatch(text.text, @"[\u4e00-\u9fff]"), "English page has no untranslated Chinese: " + text.name);
                string issue = text.transform.parent.name + "/" + text.name + ": " + text.text + " [" + text.preferredHeight + "/" + text.rectTransform.rect.height + "]";
                if(text.preferredHeight>text.rectTransform.rect.height+1f) ReadabilityIssues.Add(issue);
                else Check(true,"visible text height fits "+text.name);
                if(text.name=="Button label"&&!text.text.Contains("\n")&&text.preferredWidth>text.rectTransform.rect.width+1f)
                    ReadabilityIssues.Add("single-line button width "+issue+" ["+text.preferredWidth+"/"+text.rectTransform.rect.width+"]");
            }
        }
        static IEnumerator Press(OceanCampaignPanel panel, Camera camera, string id)
        { yield return OceanTutorialExitChecks.PressInScene(panel, camera, id); }

        /// <summary>Actual menu/ray qualification with synthetic samples, isolated Editor state only.</summary>
        public static IEnumerator RunInPlayMode(OceanCampaignPanel panel, Camera camera, OceanCampaignState state, Action<string> capture)
        {
            ReadabilityIssues.Clear();
            Assertions = 0; Check(Application.isPlaying, "language input checks require real PlayMode");
            var preferences = new PreferenceSnapshot(); float volume = AudioListener.volume;
            var hands = panel.GetComponent<HandSwimController>(); var comfort = hands != null ? hands.Comfort : OceanComfortProfile.Default;
            string campaign = JsonUtility.ToJson(state.Save); string domain = DomainSnapshot();
            using (OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese, "zh", "CN"))
            {
                try
                {
                    for (int preset = 0; preset < 3; preset++)
                    {
                        panel.SetTextScaleForValidation(preset); panel.OpenMenu(); yield return new WaitForSecondsRealtime(.6f);
                        yield return Press(panel, camera, "show_comfort"); yield return Press(panel, camera, "language_settings");
                        yield return Press(panel, camera, "language_2"); CheckSelected(panel, OceanLanguageChoice.English); CheckVisibleCopy(panel, true); capture("language-english-text-" + preset);
                        yield return Press(panel, camera, "language_1"); CheckSelected(panel, OceanLanguageChoice.SimplifiedChinese); CheckVisibleCopy(panel, false); capture("language-chinese-text-" + preset);
                        yield return Press(panel, camera, "language_0"); CheckSelected(panel, OceanLanguageChoice.FollowSystem); Check(!OceanLanguage.IsEnglish, "follow-system uses the scoped Chinese OS default");
                        capture("language-system-text-" + preset);
                    }
                    OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
                    yield return Press(panel,camera,"back");CheckVisibleCopy(panel,true);capture("language-english-settings");
                    yield return Press(panel,camera,"comfort_more");CheckVisibleCopy(panel,true);capture("language-english-audio");
                    panel.OpenMenu(); yield return new WaitForSecondsRealtime(.6f);
                    CheckVisibleCopy(panel, true); capture("language-english-menu");
                    yield return Press(panel, camera, "show_levels"); CheckVisibleCopy(panel, true); capture("language-english-levels");
                    for(int chapter=1;chapter<4;chapter++)
                    {yield return Press(panel,camera,"levels_next");CheckVisibleCopy(panel,true);capture("language-english-levels-"+(chapter+1));}
                    yield return Press(panel, camera, "back"); yield return Press(panel, camera, "show_collection"); CheckVisibleCopy(panel, true); capture("language-english-hub");
                    yield return Press(panel, camera, "naturalist_collection"); CheckVisibleCopy(panel, true); capture("language-english-naturalist");
                    yield return Press(panel, camera, "back"); yield return Press(panel, camera, "show_collection"); yield return Press(panel, camera, "show_achievements"); CheckVisibleCopy(panel, true); capture("language-english-achievements");
                    panel.OpenMenu(); yield return new WaitForSecondsRealtime(.6f); yield return Press(panel, camera, "show_targets");
                    CheckVisibleCopy(panel, true); capture("language-english-targets");
                    OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                    Check(panel.PageNameForDiagnostics == "Targets", "language refresh keeps the current goals page");
                    OceanLanguage.SetPreference(OceanLanguageChoice.English, false);
                    Check(panel.PageNameForDiagnostics == "Targets", "return to English keeps the goals page");
                    panel.OpenMenu(); yield return new WaitForSecondsRealtime(.6f); yield return Press(panel, camera, "restart_game");
                    OceanLanguage.SetPreference(OceanLanguageChoice.SimplifiedChinese, false);
                    Check(panel.PageNameForDiagnostics == "Confirm", "switching preserves the unaccepted confirmation");
                    OceanLanguage.SetPreference(OceanLanguageChoice.English, false); CheckVisibleCopy(panel, true); capture("language-english-confirm");
                    yield return Press(panel, camera, "cancel");
                    var tutorial = panel.GetComponent<OceanInteractiveTutorial>(); tutorial.Begin(); panel.CloseMenu(); yield return new WaitForSecondsRealtime(.6f);
                    Check(tutorial.Active && Button(panel, "tutorial_exit").title == OceanLanguage.Text("关闭教学"), "English retains the direct tutorial close button");
                    capture("language-english-tutorial-close"); yield return Press(panel, camera, "tutorial_exit");
                    Check(!tutorial.Active && !panel.IsOpen, "qualified English tutorial close releases the lesson");
                    Check(JsonUtility.ToJson(state.Save) == campaign && DomainSnapshot() == domain, "all menu navigation and language actions leave campaign and permanent catalog data unchanged");
                    Check(preferences.Unchanged(2), "language actions leave all audio category choices unchanged");
                    Check(ReadabilityIssues.Count==0,"visible copy fits all visited pages: "+string.Join(" | ",ReadabilityIssues));
                }
                finally
                {
                    preferences.Restore(); if (hands != null) hands.ApplyComfort(comfort, false); AudioListener.volume = volume;
                }
            }
            Check(preferences.Unchanged(), "PlayMode language and tutorial preferences restored byte-for-value");
            Debug.Log("OCEAN_LANGUAGE_PLAYMODE_PASS assertions=" + Assertions + "; three sizes and real menu callbacks with synthetic qualified rays; no physical input or headset claim.");
        }
    }
}
#endif
