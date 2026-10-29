#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual production catalog presentation with synthetic in-memory discovery data only.</summary>
    [InitializeOnLoad]
    public static class OceanNaturalistPreviewChecks
    {
        const string Key = "OceanNaturalistPreviewChecks.Active";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static OceanNaturalistPreviewRunner runner;
        static OceanNaturalistPreviewChecks() { if (SessionState.GetBool(Key, false)) Attach(); }
        public static string Output => SessionState.GetString(Key + ".output", "");
        public static void Capture()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Fresh batch Editor required.");
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-quit") >= 0 || Array.IndexOf(args, "-nographics") >= 0) throw new ArgumentException("Preview owns exit and requires graphics.");
            int index = Array.IndexOf(args, "-codexNaturalistOutput");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Explicit isolated report directory required.");
            string output = Path.GetFullPath(args[index + 1]);
            if (File.Exists(Path.Combine(output, "report.json"))) throw new IOException("Use a fresh evidence directory.");
            Directory.CreateDirectory(output); SessionState.SetString(Key + ".output", output);
            SessionState.SetBool(Key, true); SessionState.SetString(Key + ".started", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Attach(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorApplication.isPlaying = true;
        }
        static void Attach() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        static void Tick()
        {
            if (!SessionState.GetBool(Key, false)) return;
            long.TryParse(SessionState.GetString(Key + ".started", "0"), out long started);
            if (DateTime.UtcNow.Ticks - started > TimeSpan.FromSeconds(180).Ticks) { Finish(false, "Isolated catalog preview timeout."); return; }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (runner == null) runner = new GameObject("Isolated naturalist preview evidence").AddComponent<OceanNaturalistPreviewRunner>();
        }
        public static void Finish(bool success, string detail)
        {
            if (!SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            var report = runner != null ? runner.report : new OceanNaturalistPreviewRunner.Report();
            report.success = success; report.detail = detail;
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NATURALIST_PREVIEW_CHECKS_" + (success ? "PASS" : "FAIL") + " assertions=" + report.assertions + " " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }
        internal static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
        internal static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        internal static void ShowNaturalist(OceanCampaignPanel panel)
        {
            MethodInfo method = typeof(OceanCampaignPanel).GetMethod("ShowPage", Private);
            object page = Enum.Parse(method.GetParameters()[0].ParameterType, "Naturalist");
            method.Invoke(panel, new[] { page });
        }
    }
    public sealed class OceanNaturalistPreviewRunner : MonoBehaviour
    {
        [Serializable] public sealed class Row { public int id, changedPixels, rendererCount; public bool known, previewExists; public string name; }
        [Serializable] public sealed class Report
        {
            public bool success, noPlayerFilesWritten, fixtureDisposed;
            public int assertions, lifecycleCases;
            public string detail, scope = "Actual catalog UI; synthetic in-memory discovery records; no player saves, network, headset or device-performance claim.";
            public List<Row> rows = new List<Row>(); public List<string> issues = new List<string>();
        }
        public Report report = new Report();
        Camera view; RenderTexture target; Texture2D pixels; OceanCampaignPanel panel; OceanNaturalist naturalist;
        HandSwimController hands; string syntheticDirectory; IDisposable language;
        void Check(bool value, string detail) { report.assertions++; if (!value) report.issues.Add(detail); }
        IEnumerator Start()
        {
            IEnumerator exercise = Exercise();
            Exception failure = null;
            while (true)
            {
                bool more;
                try { more = exercise.MoveNext(); } catch (Exception ex) { failure = ex; break; }
                if (!more) break;
                yield return exercise.Current;
            }
            if (failure != null) report.issues.Add(failure.GetType().Name + ": " + failure.Message);
            if (panel != null) Object.Destroy(panel.gameObject);
            if (naturalist != null) Object.Destroy(naturalist.gameObject);
            if (hands != null) Object.Destroy(hands.gameObject);
            yield return null; yield return null;
            language?.Dispose();
            report.noPlayerFilesWritten = !Directory.Exists(syntheticDirectory);
            Check(report.noPlayerFilesWritten, "catalog presentation never created its nonexistent synthetic save directory");
            report.fixtureDisposed = panel == null && naturalist == null;
            Check(report.fixtureDisposed, "catalog fixtures disposed");
            OceanNaturalistPreviewChecks.Finish(report.issues.Count == 0, "Available catalog entries must show a real reference model before and after discovery; descriptions and discovery state remain gated.");
        }
        IEnumerator Exercise()
        {
            language = OceanLanguage.OverrideForValidation(OceanLanguageChoice.SimplifiedChinese);
            view = new GameObject("Isolated catalog camera").AddComponent<Camera>();
            view.transform.position = new Vector3(0, 1.6f, 0); view.transform.rotation = Quaternion.identity;
            view.fieldOfView = 55; view.aspect = 1.6f; view.nearClipPlane = .02f; view.farClipPlane = 10;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.012f, .044f, .067f);
            view.GetUniversalAdditionalCameraData().allowXRRendering = false;
            target = new RenderTexture(1536, 960, 24, RenderTextureFormat.ARGB32); target.Create(); view.targetTexture = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            hands = new GameObject("Disabled physical hand input fixture").AddComponent<HandSwimController>(); hands.enabled = false;
            var campaign = new OceanCampaignState(); campaign.StartLevel(1);
            panel = new GameObject("Production catalog panel fixture").AddComponent<OceanCampaignPanel>();
            panel.Initialize(view, hands, campaign, (action, id) => { }); panel.SetAvailable(true);
            naturalist = new GameObject("Disabled synthetic naturalist").AddComponent<OceanNaturalist>(); naturalist.enabled = false;
            syntheticDirectory = Path.Combine(Application.temporaryCachePath, "OceanCatalogNeverWritten-" + Guid.NewGuid().ToString("N"));
            var store = new OceanNaturalistStore(syntheticDirectory);
            typeof(OceanNaturalist).GetField("<Store>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(naturalist, store);
            panel.BindNaturalist(naturalist); panel.OpenMenu();
            float timeout = Time.realtimeSinceStartup + 3;
            while (panel.ExpansionForValidation < .999f && Time.realtimeSinceStartup < timeout) yield return null;
            Check(panel.ExpansionForValidation >= .999f, "actual menu finishes opening");
            OceanNaturalistPreviewChecks.ShowNaturalist(panel);
            for (int phase = 0; phase < 2; phase++)
            {
                if (phase == 1) foreach (var entry in OceanSpeciesCatalog.All)
                    if (OceanSpeciesCatalog.IsAvailable(entry.Id)) Check(store.State.TryRecord(entry.Id, entry.FirstLevel, "2026-10-28T00:00:00Z"), "synthetic in-memory record seeded: " + entry.Id);
                int expectedCount = phase == 0 ? 0 : OceanSpeciesCatalog.AvailableCount;
                foreach (var entry in OceanSpeciesCatalog.All)
                {
                    if (!OceanSpeciesCatalog.IsAvailable(entry.Id)) continue;
                    OceanNaturalistPreviewChecks.Set(panel, "naturalistIndex", entry.Id); panel.Refresh();
                    yield return null; yield return null;
                    Canvas.ForceUpdateCanvases();
                    var preview = OceanNaturalistPreviewChecks.Get<GameObject>(panel, "naturalistPreview");
                    var row = new Row { id = entry.Id, known = phase == 1, name = entry.Name, previewExists = preview != null && preview.activeInHierarchy };
                    row.rendererCount = preview != null ? preview.GetComponentsInChildren<Renderer>(true).Length : 0;
                    report.rows.Add(row); Check(row.previewExists, "reference preview exists: " + phase + "/" + entry.Id);
                    if (row.previewExists)
                    {
                        Color32[] visible = ReadRegion();
                        var image = preview.GetComponent<RawImage>();
                        Renderer[] renderers = preview.GetComponentsInChildren<Renderer>(true);
                        var states = new bool[renderers.Length];
                        if (image != null) image.enabled = false;
                        else for (int i = 0; i < renderers.Length; i++) { states[i] = renderers[i].enabled; renderers[i].enabled = false; }
                        Canvas.ForceUpdateCanvases(); Color32[] hidden = ReadRegion();
                        if (image != null) image.enabled = true;
                        else for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = states[i];
                        Canvas.ForceUpdateCanvases();
                        for (int i = 0; i < visible.Length; i++)
                            if (Math.Abs(visible[i].r - hidden[i].r) + Math.Abs(visible[i].g - hidden[i].g) + Math.Abs(visible[i].b - hidden[i].b) >= 12) row.changedPixels++;
                        Check(row.changedPixels >= 180, "visible reference pixels: " + phase + "/" + entry.Id + " = " + row.changedPixels);
                    }
                    Check(store.State.Count == expectedCount, "viewing a reference cannot grant discoveries: " + phase + "/" + entry.Id);
                    Text description = OceanNaturalistPreviewChecks.Get<Text>(panel, "naturalistDetails");
                    Text status = OceanNaturalistPreviewChecks.Get<Text>(panel, "naturalistStatus");
                    string authoredDescription = OceanLanguage.Text(entry.Description);
                    Check(!string.IsNullOrWhiteSpace(description.text) && (phase == 1 ? description.text.Contains(authoredDescription) : !description.text.Contains(authoredDescription)),
                        "description unlock remains tied to a genuine discovery record: " + phase + "/" + entry.Id);
                    Check(status.text == (phase == 1 ? OceanLanguage.Text("已发现 · 已保存") : OceanLanguage.Format("尚未发现\n第{0}关起可观察", entry.FirstLevel)),
                        "discovery status remains truthful: " + phase + "/" + entry.Id);
                    if (preview != null)
                    {
                        Check(preview.GetComponentsInChildren<Collider>(true).Length == 0, "reference has no gameplay grab or shot collider: " + entry.Id);
                        bool animated = false; foreach (Animator animator in preview.GetComponentsInChildren<Animator>(true)) animated |= animator.enabled;
                        Check(!animated, "reference never starts a second gameplay Animator: " + entry.Id);
                        Check(preview.transform.IsChildOf(OceanNaturalistPreviewChecks.Get<Transform>(panel, "naturalistAnchor")), "reference stays under its UI anchor: " + entry.Id);
                    }
                    if (entry.Id == 0 || entry.Id == 15 || entry.Id == 25 || entry.Id == 28 || entry.Id == 29 || entry.Id == 30) Save("catalog-" + phase + "-" + entry.Id.ToString("00"));
                }
            }
            // Reuse the actual presentation, not a separately reconstructed specimen card.
            // This new unseeded sidecar is only a memory holder; its directory must stay absent.
            var unknownStore = new OceanNaturalistStore(syntheticDirectory);
            typeof(OceanNaturalist).GetField("<Store>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(naturalist, unknownStore);
            foreach (var choice in new[] { OceanLanguageChoice.English, OceanLanguageChoice.SimplifiedChinese })
            {
                OceanLanguage.SetPreference(choice, false);
                foreach (int id in new[] { 0, 15, 25, 28, 29, 30 })
                {
                    OceanNaturalistPreviewChecks.Set(panel, "naturalistIndex", id); panel.Refresh();
                    yield return null; yield return null;
                    var entry = OceanSpeciesCatalog.Get(id);
                    var preview = OceanNaturalistPreviewChecks.Get<GameObject>(panel, "naturalistPreview");
                    string lockedStatus = OceanLanguage.Format("尚未发现\n第{0}关起可观察", entry.FirstLevel);
                    Check(OceanNaturalistPreviewChecks.Get<Text>(panel, "naturalistStatus").text == lockedStatus,
                        "locale changes retain the unknown state: " + choice + "/" + id);
                    Check(!OceanNaturalistPreviewChecks.Get<Text>(panel, "naturalistDetails").text.Contains(OceanLanguage.Text(entry.Description)),
                        "locale changes cannot reveal a locked description: " + choice + "/" + id);
                    panel.Refresh(); panel.Refresh();
                    Check(preview != null && ReferenceEquals(preview, OceanNaturalistPreviewChecks.Get<GameObject>(panel, "naturalistPreview")),
                        "same-page refresh reuses its model: " + choice + "/" + id);
                    panel.CloseMenu();
                    float deadline = Time.realtimeSinceStartup + 3;
                    while (panel.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(!panel.IsTransitioning, "actual menu collapse completes: " + id);
                    Check(preview == null || !preview.activeInHierarchy, "collapsed menu never leaves a world reference visible: " + id);
                    panel.OpenMenu(); OceanNaturalistPreviewChecks.ShowNaturalist(panel);
                    deadline = Time.realtimeSinceStartup + 3;
                    while (panel.ExpansionForValidation < .999f && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return null;
                    Check(preview != null && preview.activeInHierarchy && ReferenceEquals(preview, OceanNaturalistPreviewChecks.Get<GameObject>(panel, "naturalistPreview")),
                        "reopening restores its same model without duplicating it: " + id);
                    var anchor = OceanNaturalistPreviewChecks.Get<Transform>(panel, "naturalistAnchor");
                    Check(anchor.childCount == 1, "repeated menu opens keep exactly one owned reference: " + id);
                    Check(unknownStore.State.Count == 0 && !Directory.Exists(syntheticDirectory), "reference/locale/menu operations do not record discoveries or save files: " + id);
                    report.lifecycleCases++;
                    if (id == 28) Save("catalog-unknown-reopened-" + choice);
                }
            }
            Check(!OceanSpeciesCatalog.IsAvailable(13), "archived moray remains unavailable");
        }
        Color32[] ReadRegion()
        {
            view.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var root = OceanNaturalistPreviewChecks.Get<GameObject>(panel, "panelRoot").transform;
            Vector3 lo = view.WorldToScreenPoint(root.TransformPoint(new Vector3(-330, -80, 0)));
            Vector3 hi = view.WorldToScreenPoint(root.TransformPoint(new Vector3(-80, 100, 0)));
            int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.x, hi.x)), 0, target.width - 1), y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(lo.y, hi.y)), 0, target.height - 1);
            int width = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(hi.x - lo.x)), 1, target.width - x), height = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(hi.y - lo.y)), 1, target.height - y);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); pixels.Apply(false, false); RenderTexture.active = previous;
            return pixels.GetPixels32Region(x, y, width, height);
        }
        void Save(string name)
        { view.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); pixels.Apply(false, false); RenderTexture.active = previous; File.WriteAllBytes(Path.Combine(OceanNaturalistPreviewChecks.Output, name + ".png"), pixels.EncodeToPNG()); }
        void OnDestroy()
        {
            if (view != null) { view.targetTexture = null; Object.Destroy(view.gameObject); }
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (pixels != null) Object.Destroy(pixels);
        }
    }
    static class NaturalistPreviewPixelRegion
    {
        public static Color32[] GetPixels32Region(this Texture2D texture, int x, int y, int width, int height)
        {
            Color32[] all = texture.GetPixels32(), result = new Color32[width * height];
            for (int row = 0; row < height; row++) Array.Copy(all, (y + row) * texture.width + x, result, row * width, width);
            return result;
        }
    }
}
#endif
