#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Text = UnityEngine.UI.Text;
using Image = UnityEngine.UI.Image;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Production UGUI, actual font metrics, ray rectangles and callbacks; no tracked-hand/headset acceptance claim.</summary>
    public static class OceanAchievementUiTests
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        public static int Assertions { get; private set; }
        static readonly string[] Categories = { "全部", "收集", "探索", "关卡", "珍藏", "挑战" };
        [Serializable] public sealed class RayFrameSample
        {
            public string target, hit, phase;
            public int frame, activationCount;
            public bool left, armed, interactable, transitioning, fresh;
            public float seconds, strength;
        }
        [Serializable] public sealed class RayAttempt
        {
            public string target, outcome;
            public bool left, dispatched;
            public int attempt, firstFrame, lastFrame, rejectedLongFrames, activations;
            public float maximumSeconds;
        }
        public static readonly List<RayFrameSample> RayFrames = new List<RayFrameSample>();
        public static readonly List<RayAttempt> RayAttempts = new List<RayAttempt>();
        static void Check(bool value, string detail)
        { Assertions++; if (!value) throw new InvalidOperationException("Achievement UI: " + detail); }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly Camera view;
            public readonly OceanCampaignPanel panel;
            public readonly OceanCampaignState campaign;
            public readonly string directory;
            public OceanAchievementJournal journal;
            readonly float master = AudioListener.volume;
            public Fixture()
            {
                directory = Path.Combine(Path.GetTempPath(), "ocean-achievement-ui-" + Guid.NewGuid().ToString("N"));
                root = new GameObject("Isolated achievement UI fixture");
                var cameraObject = new GameObject("Achievement fixture camera"); cameraObject.transform.SetParent(root.transform, false);
                view = cameraObject.AddComponent<Camera>(); view.enabled = false;
                view.transform.position = Vector3.up * 1.6f; view.fieldOfView = 55f; view.aspect = 1.25f;
                view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.012f, .055f, .075f, 1f);
                view.cullingMask = 1 << 30; view.nearClipPlane = .05f; view.farClipPlane = 8f;
                view.GetUniversalAdditionalCameraData().allowXRRendering = false;
                var hands = root.AddComponent<HandSwimController>(); hands.enabled = false;
                campaign = new OceanCampaignState(); campaign.Save.bestStars[0] = 2; campaign.StartLevel(1);
                panel = root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(view, hands, campaign, (action, level) => { if (action == "resume") panel.CloseMenu(); });
                journal = new OceanAchievementJournal(directory); panel.BindAchievements(journal);
                panel.SetAvailable(true); panel.SkipTutorialForValidation(); panel.OpenAchievements(); Expand();
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            }
            public void Expand()
            {
                typeof(OceanCampaignPanel).GetField("openAmount", Fields).SetValue(panel, 1f);
                typeof(OceanCampaignPanel).GetMethod("ApplyExpansionPose", Fields).Invoke(panel, null);
                Canvas.ForceUpdateCanvases();
            }
            public void Populate()
            {
                journal.Evaluate(new OceanAchievementFacts { ShellSpecies = 3, FishSpecies = 2, ObservedSpecies = 12,
                    CompletedLevels = 6, ThreeStarLevels = 2, TotalStars = 18, CoinTotal = 3, CompletedMechanics = 1 }, true);
                panel.Refresh();
            }
            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AudioListener.volume = master;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        public static void RunAll()
        {
            Assertions = 0;
            using (var fixture = new Fixture())
            {
                var panel = fixture.panel; var journal = fixture.journal;
                string originalCampaign = JsonUtility.ToJson(fixture.campaign.Save);
                Check(!File.Exists(Path.Combine(fixture.directory, OceanAchievementJournal.FileName)), "opening a new empty journal does not create a file");
                Check(journal.Revision == 0 && journal.UnlockedCount == 0 && journal.UnreadCount == 0,
                    "opening an empty list does not grant achievements or mark records");
                fixture.Populate();
                Check(journal.UnlockedCount > 0 && journal.UnreadCount > 0, "production authenticated journal has real unlocks for the UI fixture");
                int unread = journal.UnreadCount; long revision = journal.Revision;
                byte[] original = File.ReadAllBytes(Path.Combine(fixture.directory, OceanAchievementJournal.FileName));
                for (int preset = 0; preset < 3; preset++)
                {
                    panel.SetTextScaleForValidation(preset); fixture.Expand();
                    for (int category = 0; category < Categories.Length; category++)
                    {
                        ClickAndCheckRay(fixture, "achievement_filter_" + category);
                        Check(panel.AchievementCategoryForValidation == Categories[category] && panel.AchievementPageForValidation == 0,
                            "category callback changes filter and resets page");
                        var expected = new HashSet<string>();
                        foreach (var definition in OceanAchievementCatalog.All)
                            if ((!definition.Historical || journal.IsUnlocked(definition.Id)) &&
                                (category == 0 || definition.Category == Categories[category])) expected.Add(definition.Id);
                        var actual = new HashSet<string>();
                        int pageCount = panel.AchievementPageCountForValidation;
                        for (int page = 0; page < pageCount; page++)
                        {
                            Check(panel.AchievementPageForValidation == page, "production page cursor advances exactly once");
                            Check(Button(panel, "achievement_previous").enabled == (page > 0) &&
                                Button(panel, "achievement_next").enabled == (page + 1 < pageCount), "pagination clamps first/final page controls");
                            int visible = 0;
                            foreach (string id in panel.AchievementVisibleIdsForValidation)
                            {
                                if (id == null) continue;
                                visible++; Check(expected.Contains(id) && actual.Add(id), "catalog identity appears once in its selected category");
                            }
                            Check(visible == Math.Min(4, expected.Count - page * 4), "four cards or the exact remaining tail are visible");
                            CheckLayout(panel, fixture.view);
                            if (page + 1 < pageCount) ClickAndCheckRay(fixture, "achievement_next");
                            else Check(!panel.ClickForValidation("achievement_next"), "disabled last-page action cannot advance");
                        }
                        Check(actual.SetEquals(expected), "category pagination covers every catalog item without omissions");
                        if (pageCount > 1) ClickAndCheckRay(fixture, "achievement_previous");
                    }
                }
                ClickAndCheckRay(fixture, "achievement_filter_1");
                string[] rows = panel.AchievementVisibleIdsForValidation;
                int partialSlot = Array.IndexOf(rows, "shell-species-5");
                Check(partialSlot >= 0 && Math.Abs(panel.AchievementFillForValidation(partialSlot) - .6f) < .0001f,
                    "actual progress image width binds to durable three-of-five progress");
                Check(HasText(panel, "3 / 5") && HasText(panel, "已解锁 · 新") && HasText(panel, "未解锁"),
                    "numeric progress and both unread-unlocked/locked states are visible together");
                Check(journal.Revision == revision && journal.UnreadCount == unread && Equal(original, File.ReadAllBytes(Path.Combine(fixture.directory, OceanAchievementJournal.FileName))),
                    "opening, all categories, all pages and text presets preserve unread file bytes");
                ClickAndCheckRay(fixture, "achievements_read_all");
                Check(journal.UnreadCount == 0 && journal.UnlockedCount > 0 && !Button(panel, "achievements_read_all").enabled,
                    "only explicit all-read callback persists seen state and preserves unlocks");
                Check(new OceanAchievementJournal(fixture.directory).UnreadCount == 0, "explicit all-read survives journal reload");
                panel.CloseMenu(); panel.OpenAchievements(); fixture.Expand();
                Check(journal.UnreadCount == 0, "reopening does not recreate notifications");
                Check(panel.ClickForValidation("back") && panel.PageNameForDiagnostics == "CollectionHub", "back returns to collection hub");
                Check(panel.ClickForValidation("show_achievements") && panel.PageNameForDiagnostics == "Achievements", "collection hub opens achievements by its production button");
                Check(JsonUtility.ToJson(fixture.campaign.Save) == originalCampaign, "all achievement menu actions leave campaign stars, collections and scoring inputs unchanged");
                panel.BindAchievements(null); panel.Refresh(); Check(!Button(panel, "achievements_read_all").enabled && HasText(panel, "记录准备中"),
                    "missing journal stays visible but cannot erase records");
                string corruptDirectory = Path.Combine(fixture.directory, "corrupt"); Directory.CreateDirectory(corruptDirectory);
                string corruptFile = Path.Combine(corruptDirectory, OceanAchievementJournal.FileName); File.WriteAllText(corruptFile, "broken");
                var readOnly = new OceanAchievementJournal(corruptDirectory); panel.BindAchievements(readOnly); panel.Refresh();
                Check(!readOnly.Writable && !Button(panel, "achievements_read_all").enabled && HasText(panel, "记录只读"),
                    "damaged journal is clearly read-only without enabling a write action");
                Check(!panel.ClickForValidation("achievements_read_all") && File.ReadAllText(corruptFile) == "broken", "disabled read-only action preserves original evidence");
                CheckLayout(panel, fixture.view);
            }
            CheckHistoricalHonourPresentation();
            Debug.Log("OceanAchievementUiTests PASS assertions=" + Assertions + "; actual UGUI/font/ray rectangles/callbacks and journal files; synthetic fixture, no physical-hand or headset claim.");
        }

        static void CheckHistoricalHonourPresentation()
        {
            using (var fixture = new Fixture())
            {
                var panel = fixture.panel; var journal = fixture.journal;
                journal.Evaluate(new OceanAchievementFacts { ObservedSpecies = 30 }, false); panel.Refresh();
                var currentIds = ReadAchievementPages(fixture);
                Check(currentIds.Count == 30 && currentIds.Contains("observe-current-all") && !currentIds.Contains("observe-all"),
                    "current list contains thirty achievable entries and hides the unearned historical honour");
                int currentUnlocked = journal.CurrentUnlockedCount;
                // Explicit historical UI fixture, independent from production available-observation facts.
                journal.Evaluate(new OceanAchievementFacts { ObservedSpecies = 31 }, false); panel.Refresh();
                Check(journal.CurrentUnlockedCount == currentUnlocked && journal.HistoricalUnlockedCount == 1,
                    "a preserved historical honour never increases the current achievement numerator");
                string file = Path.Combine(fixture.directory, OceanAchievementJournal.FileName);
                byte[] bytes = File.ReadAllBytes(file); long revision = journal.Revision;
                for (int preset = 0; preset < 3; preset++)
                {
                    panel.SetTextScaleForValidation(preset); fixture.Expand();
                    var displayed = ReadAchievementPages(fixture);
                    Check(displayed.Count == 31 && displayed.Contains("observe-all") && displayed.Contains("observe-current-all"),
                        "earned historical honour appears additionally without replacing any current entry");
                    Check(HasText(panel, "已解锁 " + currentUnlocked + "/30") && HasText(panel, "历史荣誉+1"),
                        "summary separates current completion from the extra historical honour");
                    string last = null;
                    foreach (string id in panel.AchievementVisibleIdsForValidation) if (id != null) last = id;
                    Check(last == "observe-all" && HasText(panel, "历史荣誉 · 万物入册") && HasText(panel, "31 / 31"),
                        "historical row is appended and retains its original earned thirty-one-category progress");
                }
                Check(journal.Revision == revision && Equal(bytes, File.ReadAllBytes(file)),
                    "reading historical and current UI across all text sizes does not rewrite saved achievements");
            }
        }

        static HashSet<string> ReadAchievementPages(Fixture fixture)
        {
            ClickAndCheckRay(fixture, "achievement_filter_0");
            var result = new HashSet<string>();
            int pages = fixture.panel.AchievementPageCountForValidation;
            for (int page = 0; page < pages; page++)
            {
                foreach (string id in fixture.panel.AchievementVisibleIdsForValidation)
                    if (id != null) Check(result.Add(id), "current and historical identities are displayed only once");
                CheckLayout(fixture.panel, fixture.view);
                if (page + 1 < pages) ClickAndCheckRay(fixture, "achievement_next");
            }
            return result;
        }

        /// <summary>
        /// Production ray/press-gate test, with actual Unity frames. Use CaptureRay in a separate
        /// empty PlayMode Editor; a heavy ocean render is covered by its own world transaction test.
        /// </summary>
        public static IEnumerator RunRayInScene()
        {
            RayFrames.Clear(); RayAttempts.Clear();
            using (var fixture = new Fixture())
            {
                fixture.Populate(); int unread = fixture.journal.UnreadCount;
                yield return Pinch(fixture, "achievement_filter_2", true);
                Check(fixture.panel.AchievementCategoryForValidation == "探索", "released left-hand hover plus new pinch selects category");
                Check(fixture.journal.UnreadCount == unread, "a hand-ray category gesture does not consume unread flags");
                yield return Pinch(fixture, "achievement_next", false);
                Check(fixture.panel.AchievementPageForValidation == 1, "right-hand ray plus pinch advances a real page");
                yield return Pinch(fixture, "achievements_read_all", true);
                Check(fixture.journal.UnreadCount == 0, "explicit hand-ray all-read gesture persists seen flags");
                fixture.panel.ClearValidationRays();
                Debug.Log("ACHIEVEMENT_HAND_RAY_UI_PASSED production released-hover/pinch gates and callbacks; actual Unity deltas, synthetic rays, not tracked physical hands or ocean performance");
            }
        }
        static IEnumerator Pinch(Fixture fixture, string id, bool left)
        {
            RectTransform rect = Button(fixture.panel, id).rect;
            // The normal cycle is still .65s release / .12s press / .65s release. The first
            // release also waits for the actual production gate to arm. A loading hitch can
            // consume wall-clock time while correctly contributing zero hover qualification.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                int firstSample = RayFrames.Count, before = fixture.panel.ActivationCountForValidation;
                var result = new RayAttempt { target = id, left = left, attempt = attempt, firstFrame = Time.frameCount };
                RayAttempts.Add(result);
                yield return FeedPhase(fixture, rect, id, left, 0f, .65, true, "released-hover");
                yield return FeedPhase(fixture, rect, id, left, 1f, .12, false, "pinch");
                yield return FeedPhase(fixture, rect, id, left, 0f, .65, false, "release-after");
                result.lastFrame = Time.frameCount; result.activations = fixture.panel.ActivationCountForValidation - before;
                for (int i = firstSample; i < RayFrames.Count; i++)
                {
                    result.maximumSeconds = Mathf.Max(result.maximumSeconds, RayFrames[i].seconds);
                    if (RayFrames[i].seconds > OceanMenuPressGate.MaximumSampleSeconds) result.rejectedLongFrames++;
                }
                result.dispatched = result.activations == 1;
                result.outcome = result.dispatched ? "one production activation" : result.rejectedLongFrames > 0 ? "invalid long frame; fresh qualification required" : "no activation in valid sampled frames";
                Debug.Log("[AchievementRayAttempt] " + JsonUtility.ToJson(result));
                fixture.panel.ClearValidationRays();
                Check(result.activations <= 1, "a single held pinch cannot activate more than once");
                if (result.dispatched) yield break;
                // Only a recorded invalid long-frame window may be retried. Stable-frame
                // input/geometry failures remain failures rather than being hidden by retries.
                if (result.rejectedLongFrames == 0) throw new InvalidOperationException("Achievement ray did not activate despite valid sampled deltas: " + id);
            }
            throw new TimeoutException("Achievement ray had no accepted production gesture after three recorded attempts: " + id + "; UI interaction remains unverified, not a headset performance result.");
        }
        static IEnumerator FeedPhase(Fixture fixture, RectTransform rect, string id, bool left, float strength,
            double minimumSeconds, bool requireActualArming, string phase)
        {
            double start = Time.unscaledTimeAsDouble, deadline = start + 8.0;
            while (true)
            {
                Ray ray = new Ray(fixture.view.transform.position, (rect.position - fixture.view.transform.position).normalized);
                var sample = new RayFrameSample { target = id, left = left, phase = phase, frame = Time.frameCount,
                    seconds = Time.unscaledDeltaTime, strength = strength,
                    transitioning = fixture.panel.IsTransitioning,
                    interactable = !fixture.panel.IsTransitioning && Time.unscaledTimeAsDouble >= (double)typeof(OceanCampaignPanel).GetField("blockedUntil", Fields).GetValue(fixture.panel) };
                fixture.panel.DriveMenuRayForValidation(left, ray, strength, true);
                fixture.panel.DriveMenuRayForValidation(!left, default, 0f, false);
                yield return null; // Let the unmodified production LateUpdate consume this actual frame and delta.
                object input = typeof(OceanCampaignPanel).GetField(left ? "leftHand" : "rightHand", Fields).GetValue(fixture.panel);
                var gate = (OceanMenuPressGate)input.GetType().GetField("gate", Fields).GetValue(input);
                sample.armed = gate.Armed;
                sample.fresh = (bool)input.GetType().GetField("validationFresh", Fields).GetValue(input);
                int hit = (int)input.GetType().GetField("hitIndex", Fields).GetValue(input);
                var targets = (IList)typeof(OceanCampaignPanel).GetField("targets", Fields).GetValue(fixture.panel);
                sample.hit = hit >= 0 && hit < targets.Count ? (string)targets[hit].GetType().GetField("id").GetValue(targets[hit]) : "none";
                sample.activationCount = fixture.panel.ActivationCountForValidation;
                RayFrames.Add(sample);
                bool qualified = sample.armed && sample.hit == id && sample.fresh && sample.interactable &&
                    sample.seconds > 0f && sample.seconds <= OceanMenuPressGate.MaximumSampleSeconds;
                if (Time.unscaledTimeAsDouble - start >= minimumSeconds && (!requireActualArming || qualified)) yield break;
                if (Time.unscaledTimeAsDouble >= deadline)
                    throw new TimeoutException("No stable production released-hover qualification within eight seconds: " + id + "; last=" + JsonUtility.ToJson(sample));
            }
        }

        /// <summary>Fresh graphical batch Editor only, without -quit; SessionState owns the domain-reload handoff and exit.</summary>
        public static void CaptureRay() => OceanAchievementRayPreview.Capture();

        public static void CapturePreview(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            using (var fixture = new Fixture())
            {
                Save(fixture, outputDirectory, "01-achievements-locked");
                fixture.Populate(); Save(fixture, outputDirectory, "02-achievements-progress");
                fixture.panel.ClickForValidation("achievement_filter_2"); Save(fixture, outputDirectory, "03-achievements-exploration");
                fixture.panel.SetTextScaleForValidation(2); fixture.Expand(); Save(fixture, outputDirectory, "04-achievements-large");
            }
            File.WriteAllText(Path.Combine(outputDirectory, "scope.txt"), "Production world-space UGUI rendered with isolated camera and synthetic local achievement facts. This is not a headset screenshot, physical hand-tracking test or player-earned progress.\n");
        }
        public static void Capture()
        {
            string output = Path.GetFullPath("Validation/achievement-ui-preview"); string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-codexAchievementsOutput") output = Path.GetFullPath(args[i + 1]);
            RunAll(); CapturePreview(output);
        }
        static void Save(Fixture fixture, string directory, string name)
        {
            fixture.Expand(); CheckLayout(fixture.panel, fixture.view);
            var target = new RenderTexture(1600, 1280, 24, RenderTextureFormat.ARGB32); target.Create();
            var pixels = new Texture2D(1600, 1280, TextureFormat.RGB24, false); RenderTexture previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(fixture.view, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); }
        }
        static void CheckLayout(OceanCampaignPanel panel, Camera view)
        {
            Canvas.ForceUpdateCanvases();
            foreach (Text text in panel.GetComponentsInChildren<Text>())
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) continue;
                Check(text.preferredHeight <= text.rectTransform.rect.height + .5f, "visible text fits its glyph box: " + text.name + " / " + text.text);
                foreach (char character in text.text) if (!char.IsWhiteSpace(character)) Check(text.font.HasCharacter(character), "embedded font contains " + character);
            }
            var cards = new List<RectTransform>();
            foreach (Image image in panel.GetComponentsInChildren<Image>())
                if (image.gameObject.activeInHierarchy && image.name.StartsWith("Achievement card ", StringComparison.Ordinal)) cards.Add(image.rectTransform);
            for (int i = 0; i < cards.Count; i++)
            {
                Vector3[] corners = new Vector3[4]; cards[i].GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector3 projected = view.WorldToViewportPoint(corner);
                    Check(projected.z > .3f && projected.x >= 0f && projected.x <= 1f && projected.y >= 0f && projected.y <= 1f,
                        "achievement card stays inside front camera at current comfort scale");
                }
                for (int j = i + 1; j < cards.Count; j++) Check(!Overlap(cards[i], cards[j]), "four logical achievement rows do not overlap");
            }
            var buttons = panel.GetButtonsForValidation();
            for (int i = 0; i < buttons.Count; i++) for (int j = i + 1; j < buttons.Count; j++)
                Check(!Overlap(buttons[i].rect, buttons[j].rect), "hand-ray targets remain separate: " + buttons[i].id + "/" + buttons[j].id);
        }
        static bool Overlap(RectTransform a, RectTransform b)
        {
            var x = new Vector3[4]; var y = new Vector3[4]; a.GetWorldCorners(x); b.GetWorldCorners(y);
            return Mathf.Min(x[2].x, y[2].x) - Mathf.Max(x[0].x, y[0].x) > .000001f &&
                Mathf.Min(x[2].y, y[2].y) - Mathf.Max(x[0].y, y[0].y) > .000001f;
        }
        static bool Equal(byte[] a, byte[] b)
        { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        static bool HasText(OceanCampaignPanel panel, string value)
        { foreach (Text text in panel.GetComponentsInChildren<Text>()) if (text.gameObject.activeInHierarchy && text.text.Contains(value)) return true; return false; }
        static OceanCampaignPanel.ButtonInfoForValidation Button(OceanCampaignPanel panel, string id)
        {
            foreach (var button in panel.GetButtonsForValidation()) if (button.id == id) return button;
            throw new InvalidOperationException("Missing visible achievement button " + id);
        }
        static void ClickAndCheckRay(Fixture fixture, string id)
        {
            var button = Button(fixture.panel, id); Check(button.enabled, "production action is enabled: " + id);
            Ray ray = new Ray(fixture.view.transform.position, (button.rect.position - fixture.view.transform.position).normalized);
            object[] args = { ray, 0f, false };
            object hit = typeof(OceanCampaignPanel).GetMethod("FindByRay", Fields).Invoke(fixture.panel, args);
            Check(hit != null && (string)hit.GetType().GetField("id").GetValue(hit) == id, "production hand-ray rectangle resolves correct target: " + id);
            Check(fixture.panel.ClickForValidation(id), "visible production callback succeeds: " + id);
        }
    }

    [InitializeOnLoad]
    public static class OceanAchievementRayPreview
    {
        const string Key = "OceanAchievementRayPreview.";
        static OceanAchievementRayRunner runner;
        static OceanAchievementRayPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static string Output => SessionState.GetString(Key + "output", "");
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run achievement ray validation in a fresh batch Editor in Edit Mode.");
            string output = Path.GetFullPath("Validation/achievement-ray-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Achievement ray runner owns exit and requires graphical PlayMode.");
                if ((args[i] == "-codexAchievementsOutput" || args[i] == "-codexAchievementRayOutput") && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
            }
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "report.json"))) throw new IOException("Use a fresh achievement ray evidence directory.");
            SessionState.SetString(Key + "output", output); SessionState.SetString(Key + "error", "");
            SessionState.SetString(Key + "start", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)); SessionState.SetBool(Key + "active", true);
            Attach();
            // A single empty scene removes the ocean's loading/rendering work from the UI
            // qualification experiment. The separate world harness keeps that coverage.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
        static void Attach()
        { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= Log; Application.logMessageReceived += Log; }
        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                SessionState.SetString(Key + "error", message + "\n" + stack);
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            string error = SessionState.GetString(Key + "error", "");
            if (error.Length > 0) { Finish(false, error); return; }
            long start = long.Parse(SessionState.GetString(Key + "start", "0"), CultureInfo.InvariantCulture);
            if (DateTime.UtcNow.Ticks - start > TimeSpan.FromSeconds(120).Ticks)
            { Finish(false, "Independent achievement ray runner exceeded 120 seconds; interaction remains unverified."); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (runner == null) runner = new GameObject("Independent achievement hand-ray evidence").AddComponent<OceanAchievementRayRunner>();
        }
        public static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            var report = new OceanAchievementRayRunner.Report { success = success, detail = detail, assertions = OceanAchievementUiTests.Assertions,
                configuredFrameTarget = runner != null ? runner.ConfiguredFrameTarget : 0 };
            report.frames.AddRange(OceanAchievementUiTests.RayFrames); report.attempts.AddRange(OceanAchievementUiTests.RayAttempts);
            foreach (var frame in report.frames)
            {
                report.maximumSampleSeconds = Mathf.Max(report.maximumSampleSeconds, frame.seconds);
                if (frame.seconds > OceanMenuPressGate.MaximumSampleSeconds) report.rejectedLongFrames++;
            }
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            if (runner != null) runner.RestoreFrameTarget();
            Debug.Log("ACHIEVEMENT_RAY_PREVIEW " + (success ? "PASSED" : "FAILED") + " assertions=" + report.assertions + " " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    public sealed class OceanAchievementRayRunner : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool success;
            public int assertions, configuredFrameTarget, rejectedLongFrames;
            public float maximumSampleSeconds;
            public string detail;
            public string scope = "Independent empty Unity PlayMode scene; real production campaign-panel ray rectangles, actual Unity delta and release-to-arm pinch gate, synthetic pointer injection and temporary achievement journal. No ocean placement/render performance, physical hand tracking, headset FPS or player progress claim. Every rejected/retried long-frame attempt is retained.";
            public List<OceanAchievementUiTests.RayFrameSample> frames = new List<OceanAchievementUiTests.RayFrameSample>();
            public List<OceanAchievementUiTests.RayAttempt> attempts = new List<OceanAchievementUiTests.RayAttempt>();
        }
        int previousFrameTarget;
        bool frameTargetCaptured;
        public int ConfiguredFrameTarget { get; private set; }
        IEnumerator Start()
        {
            previousFrameTarget = Application.targetFrameRate; frameTargetCaptured = true; ConfiguredFrameTarget = 72;
            Application.targetFrameRate = ConfiguredFrameTarget; // Scheduling request only; actual deltas are recorded unchanged.
            yield return OceanAchievementUiTests.RunRayInScene();
            OceanAchievementRayPreview.Finish(true, "Left category, right next-page and explicit all-read actions passed through production ray/pinch input in an empty PlayMode scene.");
        }
        public void RestoreFrameTarget() { if (frameTargetCaptured) Application.targetFrameRate = previousFrameTarget; }
        void OnDestroy() { RestoreFrameTarget(); }
    }
}
#endif
