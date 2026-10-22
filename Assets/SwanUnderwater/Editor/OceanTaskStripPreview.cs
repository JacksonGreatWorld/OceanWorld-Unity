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
    public static class OceanTaskStripPreview
    {
        const string Key = "SwanTaskStrip.";
        static OceanTaskStripRunner runner;
        static OceanTaskStripPreview() { if (SessionState.GetBool(Key + "active", false)) Attach(); }
        public static string Output => SessionState.GetString(Key + "output", "");
        public static bool PerformanceOnly => SessionState.GetBool(Key + "performanceOnly", false);
        // Separate graphical Editor, without -quit or -nographics.
        public static void Capture() => Begin(false);
        // Narrow regression: does not execute the older campaign/guide state fixture.
        public static void CapturePerformance() => Begin(true);
        static void Begin(bool performanceOnly)
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start a fresh batch Editor in Edit Mode.");
            string output = performanceOnly ? "Validation/performance-menu/unity-preview" : "Validation/iteration-25/task-strip/unity-preview";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-quit" || args[i] == "-nographics") throw new ArgumentException("Preview owns exit and requires graphical rendering.");
                if ((args[i] == "-codexTaskStripOutput" || args[i] == "-codexPerformanceMenuOutput") && i + 1 < args.Length) output = args[++i];
            }
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "report.json"))) throw new IOException("Use a fresh preview directory.");
            SessionState.SetBool(Key + "hadPreference", PerformanceHud.HasVisibilityPreferenceForValidation);
            SessionState.SetBool(Key + "preference", PerformanceHud.PreferenceVisible);
            SessionState.SetBool(Key + "performanceOnly", performanceOnly);
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
            if (runner == null) runner = new GameObject("Compact task strip evidence").AddComponent<OceanTaskStripRunner>();
        }
        public static void RestorePreference() => PerformanceHud.RestoreVisibilityPreferenceForValidation(SessionState.GetBool(Key + "hadPreference", false), SessionState.GetBool(Key + "preference", true));
        public static void Finish(bool passed, string detail)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            SessionState.SetBool(Key + "active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            var report = runner != null ? runner.report : new OceanTaskStripRunner.Report(); report.success = passed; report.detail = detail;
            if(runner!=null)runner.RestorePreviewContext();
            RestorePreference();
            report.preferenceRestored = PerformanceHud.HasVisibilityPreferenceForValidation == SessionState.GetBool(Key + "hadPreference", false) && PerformanceHud.PreferenceVisible == SessionState.GetBool(Key + "preference", true);
            File.WriteAllText(Path.Combine(Output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("TASK_STRIP_UI " + (passed ? "PASSED" : "FAILED") + " assertions=" + report.assertions + " " + detail); EditorApplication.Exit(passed ? 0 : 1);
        }
    }
    public sealed class OceanTaskStripRunner : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool success,preferenceRestored,reusedRuntimeHud,cameraOverrideReleased;
            public int assertions,toggleActions,performancePresets;
            public bool defaultHiddenVerified,persistentPreferenceVerified;
            public string detail;
            public string evidence="Actual production CampaignPanel, Guide and PerformanceHud in a single-camera Editor scene. Guide states use accepted State transactions; ray activation uses synthetic fresh poses through production hit/press logic. No headset tracking, stereo readability, physical comfort, timing or network claim. Preference presence/value restored on every normal/error exit.";
            public List<string> checks=new List<string>(),screenshots=new List<string>();
            public List<SwanUnderwater.EditorTests.OceanCampaignGuideTests.GuideCase> stateCases=new List<SwanUnderwater.EditorTests.OceanCampaignGuideTests.GuideCase>();
        }
        public Report report=new Report();
        OceanCampaignPanel panel;OceanCampaignState state;HandSwimController hands;PerformanceHud performance;Camera view;RenderTexture target;Texture2D pixels;
        bool ownsPerformance,cameraOverrideApplied;
        IEnumerator Start()
        {
            try
            {
                if(OceanTaskStripPreview.PerformanceOnly)report.evidence="Actual production CampaignPanel and PerformanceHud in an isolated single-camera Editor scene. Three text-size presets, main/nested buttons, stored preference reads and HUD visibility use production UI callbacks and qualified synthetic ray pinches. In-memory level state only; no player JSON, world or network initialization. No headset tracking, restart, stereo readability, device performance or physical comfort claim. Original performance preference presence/value restored on normal/error exit.";
                if(!OceanTaskStripPreview.PerformanceOnly){SwanUnderwater.EditorTests.OceanCampaignGuideTests.RunAll();report.stateCases.AddRange(SwanUnderwater.EditorTests.OceanCampaignGuideTests.Cases);}
                view=new GameObject("Task strip preview camera").AddComponent<Camera>();view.transform.position=new Vector3(0,1.6f,0);view.fieldOfView=55;view.aspect=1.6f;view.nearClipPlane=.02f;view.farClipPlane=10;
                view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.012f,.044f,.067f);view.GetUniversalAdditionalCameraData().allowXRRendering=false;
                target=new RenderTexture(1536,960,24,RenderTextureFormat.ARGB32);target.Create();pixels=new Texture2D(1536,960,TextureFormat.RGB24,false);view.targetTexture=target;
                hands=new GameObject("Disabled physical hand tracker").AddComponent<HandSwimController>();hands.enabled=false;
                state=new OceanCampaignState();state.StartLevel(OceanTaskStripPreview.PerformanceOnly?1:5);
                panel=new GameObject("Production compact campaign panel").AddComponent<OceanCampaignPanel>();panel.Initialize(view,hands,state,Handle);panel.SetAvailable(true);
                // RuntimeDiagnostics.Install automatically supplies a HUD after this empty scene loads.
                // Borrow that process-local instance and never create another diagnostics host.
                int diagnosticsCount=UnityEngine.Object.FindObjectsByType<RuntimeDiagnostics>(FindObjectsSortMode.None).Length;
                performance=PerformanceHud.Instance;report.reusedRuntimeHud=performance!=null;
                if(performance==null){performance=new GameObject("Production performance HUD").AddComponent<PerformanceHud>();ownsPerformance=true;}
                Require(performance==PerformanceHud.Instance&&UnityEngine.Object.FindObjectsByType<PerformanceHud>(FindObjectsSortMode.None).Length==1,"preview reuses the sole runtime HUD without replacing its diagnostics host");
                Require(UnityEngine.Object.FindObjectsByType<RuntimeDiagnostics>(FindObjectsSortMode.None).Length==diagnosticsCount,"preview creates no extra diagnostic singleton");
                cameraOverrideApplied=true;performance.SetEditorPreviewCamera(view);
                if(OceanTaskStripPreview.PerformanceOnly)
                {
                    yield return CheckPerformanceMenu();
                }
                else
                {
                PerformanceHud.SetPreferenceVisible(true);
                panel.OpenMenu();yield return new WaitForSecondsRealtime(.8f);
                Require(performance.PanelVisibleForValidation,"actual performance panel appears when enabled");CheckButtons();Save("00-menu-performance-on");
                yield return Press("toggle_performance");
                Require(report.toggleActions==1&&!PerformanceHud.PreferenceVisible&&!performance.PanelVisibleForValidation,"qualified pinch immediately hides actual HUD");
                Require(HasButtonText("toggle_performance","性能面板：关"),"button follows actual stored off preference");Save("01-menu-performance-off");
                yield return Press("toggle_performance");yield return new WaitForSecondsRealtime(.15f);
                Require(report.toggleActions==2&&PerformanceHud.PreferenceVisible&&performance.PanelVisibleForValidation,"second qualified pinch restores actual HUD");
                Require(HasButtonText("toggle_performance","性能面板：开"),"button follows actual stored on preference");Save("02-menu-performance-on-again");
                panel.CloseMenu();yield return new WaitForSecondsRealtime(.7f);
                Require(!panel.WorldInputBlocked,"collapse releases transition input gate");
                Require(panel.GuideRectForValidation.rect.height==56&&panel.GuideRectForValidation.localScale.x==.00085f,"bar shrinks from 70 to 56 without shrinking physical text scale");
                Require(panel.GuideRectForValidation.parent==view.transform&&Mathf.Abs(panel.GuideRectForValidation.localPosition.y-.210f)<.0001f,"compact strip remains head-local and slightly lower");
                for(int level=1;level<=20;level++)
                {
                    state.StartLevel(level);panel.Refresh();panel.SetGuideHintForValidation(0);yield return null;CheckGuide();
                    if(level==1||level==3||level==8||level==14||level==17||level==20)Save("guide-level-"+level);
                    panel.SetGuideHintForValidation(1);CheckGuide(1);
                }
                state.StartLevel(5);panel.Refresh();Save("boss-find-speargun");
                Require(state.AcquireTool(),"actual tool acquire");for(int i=0;i<4;i++){Require(state.TryFireSpear(),"actual shot");if(i<3)Require(state.HitShark(),"actual hit");}
                panel.Refresh();CheckGuide();Require(panel.GuideActionForValidation.Contains("3/10")&&panel.GuideDetailsForValidation.Contains("剩余子弹 16/20"),"visible strings label three hits versus sixteen rounds");Save("boss-3-hits-16-rounds");
                while(state.Current.sharkHits<state.SharkHitTarget)Require(state.TryFireSpear()&&state.HitShark(),"accepted remaining shot/hit");panel.Refresh();CheckGuide();
                Require(panel.GuideActionForValidation.Contains("靠近鲨鱼，握拳抓获"),"stunned boss visible capture instruction");Save("boss-stunned-grasp");
                Require(state.CatchShark(),"actual boss capture");panel.Refresh();CheckGuide();Save("boss-caught-find-coin");
                state.StartLevel(14);Require(state.CollectShell(1400,0),"actual ordered shell discovery");panel.Refresh();CheckGuide();Require(panel.GuideActionForValidation.Contains("下一种宝螺"),"next ordered shell visible after progress");Save("ordered-next-shell");
                state.StartLevel(20);state.AcquireTool();for(int i=0;i<14;i++){state.TryFireSpear();state.HitShark();}state.CatchShark();panel.Refresh();panel.SetGuideHintForValidation(0);CheckGuide();Save("mixed-after-boss-shell-hint");
                panel.SetGuideHintForValidation(1);CheckGuide(1);Save("mixed-after-boss-fish-hint");
                panel.ClearValidationRays();yield return new WaitForSecondsRealtime(.5f);yield return Press("menu");yield return new WaitForSecondsRealtime(.5f);
                Require(panel.IsOpen,"thinner strip still opens via real ray/pinch gate");
                }
            }
            finally { RestorePreviewContext(); }
            OceanTaskStripPreview.Finish(true,OceanTaskStripPreview.PerformanceOnly?
                "Direct main-menu performance control, nested synchronization, three text presets, actual HUD and qualified synthetic ray pinches passed; preference restored. No device or stereo claim.":
                "Compact action/count/time copy, all twenty initial guides, actual boss/ordered transitions and actual performance UI toggle passed with synthetic hand rays.");
        }
        IEnumerator CheckPerformanceMenu()
        {
            // Exercise an absent preference, rather than assuming this Editor's previous choice.
            // Capture/Finish preserve its presence and value on normal and error exits.
            PerformanceHud.RestoreVisibilityPreferenceForValidation(false,false);panel.Refresh();
            panel.OpenMenu();yield return new WaitForSecondsRealtime(.8f);
            Require(!PerformanceHud.HasVisibilityPreferenceForValidation&&!PerformanceHud.PreferenceVisible&&!performance.PanelVisibleForValidation,"absent preference leaves actual HUD hidden by default");
            report.defaultHiddenVerified=true;
            for(int preset=0;preset<3;preset++)
            {
                panel.SetTextScaleForValidation(preset);panel.OpenMenu();yield return null;
                CheckButtons();CheckVisibleButtonText();Require(HasButtonText("toggle_performance","性能面板：关"),"main menu exposes the off switch at text preset "+preset);
                Save("performance-"+preset+"-main-off");
                int actions=report.toggleActions;
                yield return Press("toggle_performance");yield return new WaitForSecondsRealtime(.15f);
                Require(report.toggleActions==actions+1&&PerformanceHud.HasVisibilityPreferenceForValidation&&PerformanceHud.PreferenceVisible&&performance.PanelVisibleForValidation,"main-menu qualified ray/pinch turns actual HUD on once at preset "+preset);
                Require(HasButtonText("toggle_performance","性能面板：开"),"main switch reflects saved on preference");
                // Getter rereads PlayerPrefs; reopening must use that value without resetting it.
                panel.CloseMenu();yield return new WaitForSecondsRealtime(.35f);panel.OpenMenu();yield return new WaitForSecondsRealtime(.5f);
                Require(PerformanceHud.PreferenceVisible&&HasButtonText("toggle_performance","性能面板：开")&&performance.PanelVisibleForValidation,"closing/reopening preserves the stored enabled preference");
                report.persistentPreferenceVerified=true;Save("performance-"+preset+"-main-on");
                yield return Press("show_comfort");yield return Press("comfort_more");
                Require(HasButtonText("toggle_performance","性能面板：开"),"nested switch immediately shares main switch preference");
                CheckButtons();CheckVisibleButtonText();Save("performance-"+preset+"-nested-on");
                yield return Press("toggle_performance");
                Require(report.toggleActions==actions+2&&!PerformanceHud.PreferenceVisible&&!performance.PanelVisibleForValidation,"nested qualified ray/pinch turns actual HUD off once");
                Require(HasButtonText("toggle_performance","性能面板：关"),"nested switch reflects saved off preference");
                yield return Press("back");yield return Press("back");
                Require(panel.PageNameForDiagnostics=="Menu"&&HasButtonText("toggle_performance","性能面板：关"),"main switch synchronizes after nested change");
                report.performancePresets++;
            }
        }
        void Handle(string action,int level)
        {
            if(action=="toggle_performance"){report.toggleActions++;PerformanceHud.SetPreferenceVisible(!PerformanceHud.PreferenceVisible);panel.Refresh();}
            else if(action=="menu")panel.OpenMenu();else if(action=="resume")panel.CloseMenu();else throw new InvalidOperationException("Unexpected task-strip callback "+action);
        }
        void CheckGuide(int hint=0)
        {
            var expected=OceanCampaignGuide.Describe(state,hint);Require(panel.GuideActionForValidation==expected.Action&&panel.GuideDetailsForValidation==expected.Details,"actual panel matches current state/action and labeled counts "+state.Current.level);
            foreach(Text text in panel.GuideRectForValidation.GetComponentsInChildren<Text>())if(text.gameObject.activeInHierarchy)
            {
                Require(text.preferredWidth<=text.rectTransform.rect.width+.5f,"task-strip single-line width: "+text.text);
                Require(text.preferredHeight<=text.rectTransform.rect.height+.5f,"task-strip font ascent height: "+text.text);
                foreach(char c in text.text)if(!char.IsWhiteSpace(c))Require(text.font.HasCharacter(c),"embedded task-strip glyph "+c);
            }
        }
        bool HasButtonText(string id,string text){foreach(var b in panel.GetButtonsForValidation())if(b.id==id)return b.title==text;return false;}
        void CheckButtons()
        {
            var buttons=panel.GetButtonsForValidation();
            foreach(var b in buttons)if(b.id=="leaderboard"||b.id=="show_comfort"||b.id=="toggle_performance")Require(b.rect.rect.width>=180&&b.rect.rect.height>=44,"menu options keep usable hit targets "+b.id);
            for(int i=0;i<buttons.Count;i++)for(int j=i+1;j<buttons.Count;j++)
            {
                var a=new Vector3[4];var b=new Vector3[4];buttons[i].rect.GetWorldCorners(a);buttons[j].rect.GetWorldCorners(b);
                Require(Mathf.Min(a[2].x,b[2].x)-Mathf.Max(a[0].x,b[0].x)<.00001f||Mathf.Min(a[2].y,b[2].y)-Mathf.Max(a[0].y,b[0].y)<.00001f,"separate menu controls "+buttons[i].id+"/"+buttons[j].id);
            }
        }
        void CheckVisibleButtonText()
        {
            Canvas.ForceUpdateCanvases();
            foreach(var button in panel.GetButtonsForValidation())
                foreach(Text text in button.rect.GetComponentsInChildren<Text>())
                    if(text.gameObject.activeInHierarchy)
                    {
                        Require(text.preferredHeight<=text.rectTransform.rect.height+.5f,"visible button text is not vertically truncated: "+button.id);
                        Require(text.preferredWidth<=text.rectTransform.rect.width+.5f,"visible button text fits horizontally: "+button.id);
                    }
        }
        IEnumerator Press(string id)
        {
            RectTransform rect=null;foreach(var b in panel.GetButtonsForValidation())if(b.id==id)rect=b.rect;Require(rect!=null,"ray target exists "+id);
            foreach(float strength in new[]{0f,1f,0f})
            {
                double start=Time.unscaledTimeAsDouble;float seconds=strength==0?.65f:.12f;
                do{panel.DriveMenuRayForValidation(true,new Ray(view.transform.position,(rect.position-view.transform.position).normalized),strength,true);panel.DriveMenuRayForValidation(false,default,0,false);yield return null;}while(Time.unscaledTimeAsDouble-start<seconds);
            }
            panel.ClearValidationRays();
        }
        void Require(bool condition,string message){report.assertions++;report.checks.Add(message+": "+condition);if(!condition)throw new InvalidOperationException("Task strip preview: "+message);}
        void Save(string name)
        {
            Canvas.ForceUpdateCanvases();if(panel.GuideRectForValidation.gameObject.activeInHierarchy)CheckGuide(name=="mixed-after-boss-fish-hint"?1:0);
            foreach(Text t in panel.GetComponentsInChildren<Text>())if(t.gameObject.activeInHierarchy)foreach(char c in t.text)if(!char.IsWhiteSpace(c))Require(t.font.HasCharacter(c),"menu glyph "+c);
            RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();RenderTexture.active=old;
            string file=name+".png";File.WriteAllBytes(Path.Combine(OceanTaskStripPreview.Output,file),pixels.EncodeToPNG());report.screenshots.Add(file);
        }
        public void RestorePreviewContext()
        {
            // Fresh batch/empty scene: the auto-created HUD began with the default (null) preview override.
            // Clearing it restores Camera.main selection; neither camera's pose/parent is ever modified.
            if(cameraOverrideApplied&&performance!=null)performance.SetEditorPreviewCamera(null);
            cameraOverrideApplied=false;report.cameraOverrideReleased=true;OceanTaskStripPreview.RestorePreference();
        }
        void OnDestroy(){RestorePreviewContext();if(ownsPerformance&&performance!=null)Destroy(performance.gameObject);if(view!=null)view.targetTexture=null;if(target!=null){target.Release();Destroy(target);}if(pixels!=null)Destroy(pixels);}
    }
}
#endif
