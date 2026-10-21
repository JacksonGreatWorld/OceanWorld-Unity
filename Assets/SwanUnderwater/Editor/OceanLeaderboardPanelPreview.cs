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

namespace SwanUnderwater.Editor
{
    /// <summary>Real production panel and ray hit/press path, with explicitly synthetic public rows. No network or headset operations.</summary>
    [InitializeOnLoad]
    public static class OceanLeaderboardPanelPreview
    {
        const string Key="SwanLeaderboardPreview.";
        static OceanLeaderboardPreviewRunner runner;
        static OceanLeaderboardPanelPreview(){if(SessionState.GetBool(Key+"active",false))Attach();}
        public static string Output=>SessionState.GetString(Key+"output","");
        // Omit -quit/-nographics. Optional -codexLeaderboardOutput <fresh directory>.
        public static void Capture()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use an Edit Mode batch process; capture owns exit.");
            string output="Validation/iteration-24/leaderboard/unity-preview";
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++){if(args[i]=="-quit"||args[i]=="-nographics")throw new ArgumentException("Omit quit/nographics.");if(args[i]=="-codexLeaderboardOutput"&&i+1<args.Length)output=args[++i];}
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            if(File.Exists(Path.Combine(output,"report.json")))throw new IOException("Use a fresh output directory.");
            SessionState.SetString(Key+"output",output);SessionState.SetString(Key+"error","");SessionState.SetString(Key+"start",DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));SessionState.SetBool(Key+"active",true);
            Attach();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
        }
        static void Attach(){EditorApplication.update-=Tick;EditorApplication.update+=Tick;Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;}
        static void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)SessionState.SetString(Key+"error",text+"\n"+stack);}
        static void Tick()
        {
            if(!SessionState.GetBool(Key+"active",false))return;
            string error=SessionState.GetString(Key+"error","");
            if(error.Length>0){Finish(false,error);return;}
            if(DateTime.UtcNow.Ticks-long.Parse(SessionState.GetString(Key+"start","0"),CultureInfo.InvariantCulture)>TimeSpan.FromSeconds(210).Ticks){Finish(false,"Preview exceeded 210 seconds.");return;}
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(runner==null)runner=new GameObject("Leaderboard synthetic input preview").AddComponent<OceanLeaderboardPreviewRunner>();
        }
        public static void Finish(bool passed,string detail)
        {
            if(!SessionState.GetBool(Key+"active",false))return;
            SessionState.SetBool(Key+"active",false);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            var report=runner!=null?runner.report:new OceanLeaderboardPreviewRunner.Report();report.success=passed;report.detail=detail;
            File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("LEADERBOARD_PANEL_PREVIEW "+(passed?"PASSED":"FAILED")+" assertions="+report.assertions+" "+detail);
            EditorApplication.Exit(passed?0:1);
        }
    }

    public sealed class OceanLeaderboardPreviewRunner:MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool success;
            public int assertions,loadCalls,renameCalls,closeCalls;
            public string detail;
            public string evidence="Production OceanLeaderboardPanel and OceanMenuPressGate, synthetic fresh hand rays supplied through editor-only injection, async full CJK resource load, actual UGUI rendered to mono camera. Menu handoff uses the same callbacks as the controller with an in-memory campaign, without initializing the network bridge. Public row fixtures are synthetic and no HTTP/auth/device tracking/real stereo/performance acceptance is claimed.";
            public List<string> checks=new List<string>();
        }
        public Report report=new Report();
        OceanLeaderboardPanel panel;
        OceanCampaignPanel menu;
        HandSwimController hands;
        Camera view;
        RenderTexture target;
        Texture2D pixels;
        string savedName;
        IEnumerator Start()
        {
            SwanUnderwater.EditorTests.OceanMenuPressGateTests.RunAll();
            Require(OceanLeaderboardPanel.EraseLast("海豚2")=="海豚","CJK nickname erase");
            Require(OceanLeaderboardPanel.EraseLast("A\ud83d\udc20")=="A","surrogate pair nickname erase");
            Require(OceanLeaderboardPanel.EraseLast("e\u0301")=="","combining mark nickname erase");
            view=new GameObject("Leaderboard preview camera").AddComponent<Camera>();view.transform.position=new Vector3(0,1.6f,0);view.enabled=false;view.fieldOfView=55;view.aspect=1;view.nearClipPlane=.02f;view.farClipPlane=10;
            view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=new Color(.012f,.044f,.067f);view.GetUniversalAdditionalCameraData().allowXRRendering=false;
            target=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);target.Create();pixels=new Texture2D(1024,1024,TextureFormat.RGB24,false);
            hands=new GameObject("Disabled physical tracker for synthetic rays").AddComponent<HandSwimController>();hands.enabled=false;
            panel=new GameObject("Leaderboard panel").AddComponent<OceanLeaderboardPanel>();panel.Initialize(view,hands,HandleAction);
            Require(!panel.IsReady,"Initialize does not load full CJK or build canvas");
            var campaign=new OceanCampaignState();campaign.StartLevel(1);
            menu=new GameObject("Production campaign menu").AddComponent<OceanCampaignPanel>();
            menu.Initialize(view,hands,campaign,(id,value)=>{if(id=="leaderboard"){menu.CloseMenu();menu.SetAvailable(false);panel.Show("设备-a1b2c3","a1b2c3d4");}else if(id=="resume")menu.CloseMenu();});
            menu.SetAvailable(true);menu.OpenMenu();
            Require(Paused(),"main menu pauses artificial locomotion");
            yield return new WaitForSecondsRealtime(1);
            Save("00-main-menu.png");
            yield return MenuPress("leaderboard");
            Require(panel.IsOpen&&Paused()&&!menu.IsOpen,"menu to leaderboard handoff retains pause while full font loads");
            while(!panel.IsReady)yield return null;
            yield return Hold("next",0,.65f);
            Require(panel.IsOpen&&panel.LoadedRowCount==20&&report.loadCalls==1,"explicit open loads one synthetic page after async font");
            Save("01-first-page.png");
            // Holding pinch while entering a different control must not activate it.
            yield return Hold("edit_name",1,.65f);
            Require(panel.GetTargetForValidation("list").gameObject.activeInHierarchy,"held pinch does not open keyboard");
            // Tracking loss between open-hand hover and pinch must invalidate the qualification.
            yield return Hold("edit_name",0,.3f);yield return Hold("edit_name",0,.1f,false);yield return Hold("edit_name",1,.2f);
            Require(panel.GetTargetForValidation("list").gameObject.activeInHierarchy,"lost tracking prevents stale pinch activation");
            RectTransform list=panel.GetTargetForValidation("list");Vector3 start=list.TransformPoint(new Vector3(0,-110,0));Vector3 end=list.TransformPoint(new Vector3(0,110,0));
            yield return HoldPoint(start,0,.65f);yield return HoldPoint(start,1,.12f);
            double dragStart=Time.unscaledTimeAsDouble;
            while(Time.unscaledTimeAsDouble-dragStart<.65){float t=(float)((Time.unscaledTimeAsDouble-dragStart)/.65);Aim(Vector3.Lerp(start,end,t),1,true);yield return null;}
            yield return HoldPoint(end,1,.12f);yield return HoldPoint(end,0,.12f);
            Require(panel.FirstVisibleRow>=3,"pinch-drag scrolls real list geometry");
            Require(panel.GetTargetForValidation("list").gameObject.activeInHierarchy&&report.renameCalls==0,"drag cannot activate nickname editor");
            for(int i=0;i<7;i++)yield return Press("next");
            Require(report.loadCalls==3&&panel.LoadedRowCount==47,"forward navigation consumes three cursors without an artificial last page");
            Require(panel.RangeForValidation.Contains("已到底"),"bottom marker appears only after the last cached row");
            Save("02-final-page.png");
            yield return Press("previous");Require(panel.FirstVisibleRow<41,"backward navigation within cached pages");
            Require(panel.RangeForValidation.Contains("下滑查看更多"),"cached rows below the viewport still advertise further scrolling");
            yield return Press("edit_name");Require(!list.gameObject.activeInHierarchy,"qualified ray pinch opens nickname keyboard");
            yield return Press("clear");
            yield return TypeHan("cheng","程");yield return TypeHan("xiao","晓");yield return TypeHan("long","龙");
            Require(panel.DraftNickname=="程晓龙","real ray input composes arbitrary name from pinyin candidates");Save("03-chinese-pinyin.png");
            yield return Press("input_mode");yield return Press("key_A");
            Require(panel.DraftNickname=="程晓龙A","English mode appends Latin character");yield return Press("erase");
            yield return Press("save_name");
            Require(report.renameCalls==1&&panel.IsKeyboardOpenForValidation&&panel.DraftNickname=="程晓龙","queued offline rename stays editable and is not reported as confirmed");
            panel.SetProfile("设备-a1b2c3","a1b2c3d4");
            Require(panel.IsKeyboardOpenForValidation,"unrelated profile refresh cannot close pending nickname");
            panel.SetNicknameResult("设备-a1b2c3","程晓龙","rejected","nickname_taken");
            Require(panel.IsKeyboardOpenForValidation&&panel.DraftNickname=="程晓龙","conflict retains draft and old confirmed name");Save("04-name-conflict.png");
            yield return Press("key_2");yield return Press("save_name");
            Require(savedName=="程晓龙2"&&report.renameCalls==2&&list.gameObject.activeInHierarchy,"server-confirmed retry returns to list");Save("05-renamed.png");
            panel.ClearValidationRays(); view.transform.rotation=Quaternion.Euler(0,180,0);
            double recoverAt=Time.unscaledTimeAsDouble;
            // ResetPointers holds input for .35s, then recovery requires .8s of fresh frame time.
            while(Time.unscaledTimeAsDouble-recoverAt<1.65)
            {panel.DriveRayForValidation(true,default,0,false);panel.DriveRayForValidation(false,default,0,false);yield return null;}
            Require(!OceanCampaignPanel.ShouldRecoverMenu(view.transform.InverseTransformPoint(panel.GetTargetForValidation("list").position)),"sustained head turn recovers inaccessible leaderboard in front");
            int version=panel.RequestVersion;yield return Press("close");
            Require(!panel.IsOpen&&panel.RequestVersion>version&&report.closeCalls==1,"close increments request generation and dispatches once");
            foreach(var ray in panel.GetComponentsInChildren<LineRenderer>(true))Require(!ray.enabled,"close hides every hand/controller ray");
            panel.SetPage(Fixture(0,2),null,2,2,false);Require(!panel.IsOpen,"late page cannot reopen closed menu");
            Require(menu.IsOpen&&Paused(),"return to main menu preserves only its intended pause");
            yield return new WaitForSecondsRealtime(.7f);
            yield return MenuPress("resume");
            yield return new WaitForSecondsRealtime(.6f);
            Require(!menu.IsOpen&&!Paused()&&!menu.WorldInputBlocked,"resume releases menu pause and world input gate");
            OceanLeaderboardPanelPreview.Finish(true,"All production UI paths passed with synthetic hand-ray input. Headset tracking, readability and signed networking still need separate acceptance.");
        }
        void HandleAction(string id,string value)
        {
            if(id=="load")
            {
                report.loadCalls++;int offset=value.Length==0?0:int.Parse(value,CultureInfo.InvariantCulture);int count=Math.Min(20,47-offset);
                panel.SetPage(Fixture(offset,count),offset+count<47?(offset+count).ToString(CultureInfo.InvariantCulture):null,47,182,offset>0,"验证示例数据 · 非线上记录");
            }
            else if(id=="rename")
            {
                report.renameCalls++;panel.SetBusy(false);
                if(report.renameCalls==1)panel.SetNicknameResult("设备-a1b2c3",value,"pending","");
                else{savedName=value;panel.SetProfile(value,"a1b2c3d4");panel.SetNicknameResult(value,value,"accepted","");}
            }
            else if(id=="close"){report.closeCalls++;menu.SetAvailable(true);menu.OpenMenu();}
            else throw new InvalidOperationException("Unexpected UI action "+id);
        }
        static OceanLeaderboardPanel.Row[] Fixture(int first,int count)
        {
            var rows=new OceanLeaderboardPanel.Row[count];string[] names={"珊瑚小队","蓝鲸旅人","海豚2","潜水员A","海星伙伴"};
            for(int i=0;i<count;i++){int n=first+i;long fish=350-n*5,coins=20-n/3;rows[i]=new OceanLeaderboardPanel.Row{rank=n+1,publicId=n.ToString("x12"),nickname=names[n%names.Length]+(n+1),shellTotal=80-n,fishTotal=fish,coinTotal=coins,score=80-n+fish*2+coins*10,isMe=n==2};}return rows;
        }
        IEnumerator TypeHan(string syllable,string han)
        {
            foreach(char c in syllable.ToUpperInvariant())yield return Press("key_"+c);
            Require(panel.CompositionForValidation==syllable,"ray pinyin composition: "+syllable);
            for(int page=0;page<40;page++)
            {
                for(int slot=0;slot<8;slot++)if(panel.CandidateForValidation(slot)==han)
                {
                    if(han=="晓")Save("03a-candidate-page.png");
                    if(slot==0)yield return Press("space");else yield return Press("candidate_"+slot);
                    Require(panel.CompositionForValidation==""&&panel.DraftNickname.EndsWith(han,StringComparison.Ordinal),"candidate commits Han and clears composition: "+han);yield break;
                }
                yield return Press("candidate_next");
            }
            throw new InvalidOperationException("Han absent from candidate pages: "+han);
        }
        bool Paused()=>(bool)typeof(HandSwimController).GetField("menuLocomotionPaused",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(hands);
        IEnumerator MenuPress(string id)
        {
            RectTransform rect=null;foreach(var button in menu.GetButtonsForValidation())if(button.id==id)rect=button.rect;
            Require(rect!=null,"production menu target exists: "+id);
            foreach(float strength in new[]{0f,1f,0f})
            {
                double begin=Time.unscaledTimeAsDouble;float duration=strength==0?.65f:.12f;
                do{menu.DriveMenuRayForValidation(true,new Ray(view.transform.position,(rect.position-view.transform.position).normalized),strength,true);menu.DriveMenuRayForValidation(false,default,0,false);yield return null;}while(Time.unscaledTimeAsDouble-begin<duration);
            }
            menu.ClearValidationRays();
        }
        IEnumerator Press(string id){yield return Hold(id,0,.62f);yield return Hold(id,1,.10f);yield return Hold(id,0,.12f);}
        IEnumerator Hold(string id,float strength,float seconds,bool fresh=true)
        {var rect=panel.GetTargetForValidation(id);Require(rect!=null,"target exists: "+id);yield return HoldPoint(rect.TransformPoint(rect.rect.center),strength,seconds,fresh);}
        IEnumerator HoldPoint(Vector3 point,float strength,float seconds,bool fresh=true)
        {double start=Time.unscaledTimeAsDouble;do{Aim(point,strength,fresh);yield return null;}while(Time.unscaledTimeAsDouble-start<seconds);}
        void Aim(Vector3 point,float strength,bool fresh){panel.DriveRayForValidation(true,new Ray(view.transform.position,(point-view.transform.position).normalized),strength,fresh);panel.DriveRayForValidation(false,default,0,false);}
        void Require(bool condition,string detail){report.assertions++;report.checks.Add(detail+": "+condition);if(!condition)throw new InvalidOperationException("Leaderboard preview: "+detail);}
        void Save(string file)
        {
            Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var prior=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1024,1024),0,0);pixels.Apply();RenderTexture.active=prior;
            File.WriteAllBytes(Path.Combine(OceanLeaderboardPanelPreview.Output,file),pixels.EncodeToPNG());
        }
        void OnDestroy(){if(target!=null){target.Release();Destroy(target);}if(pixels!=null)Destroy(pixels);}
    }
}
#endif
