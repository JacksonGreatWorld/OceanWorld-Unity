#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual UI layout, rendering bounds and graphic lifecycle. Synthetic states are explicitly diagram-only.</summary>
    public static class OceanComfortVisualTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++;if(!value)throw new InvalidOperationException("Comfort visual: "+message); }
        readonly struct Layout
        {
            public readonly int Pixels;
            public readonly Vector2 Size;
            public Layout(Text text){Pixels=text.fontSize;Size=text.rectTransform.rect.size;}
        }
        public static void RunAll()
        {
            Assertions=0;float master=AudioListener.volume;
            AutomaticOfferQualifications();
            var root=new GameObject("Isolated comfort size and teaching graphic fixture");
            try
            {
                var cameraObject=new GameObject("Comfort fixture camera");cameraObject.transform.SetParent(root.transform,false);
                var view=cameraObject.AddComponent<Camera>();view.enabled=false;view.aspect=16f/9f;view.fieldOfView=60;
                view.transform.position=Vector3.up*1.6f;
                var hands=root.AddComponent<HandSwimController>();hands.enabled=false;
                var state=new OceanCampaignState();state.Save.bestStars[0]=2;state.StartLevel(1);
                var panel=root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(view,hands,state,(action,level)=>{if(action=="resume")panel.CloseMenu();});
                panel.SetAvailable(true);panel.OpenMenu();
                float initialScale=panel.ExpandedRectForValidation.localScale.x;
                var layouts=new Dictionary<Text,Layout>();
                for(int preset=0;preset<3;preset++)
                {
                    layouts.Clear();
                    foreach(var text in panel.GetComponentsInChildren<Text>(true))layouts.Add(text,new Layout(text));
                    panel.SetTextScaleForValidation(preset);
                    Check(Math.Abs(panel.ExpandedRectForValidation.localScale.x-initialScale*(1f+preset*.1f))<.000001f,"whole expanded panel keeps all hit regions at the same bounded size factor");
                    Check(Math.Abs(panel.GuideRectForValidation.localScale.x-.00085f*(1f+preset*.1f))<.000001f,"HUD, labels and hit area scale together");
                    foreach(var pair in layouts)Check(pair.Key.fontSize==pair.Value.Pixels && pair.Key.rectTransform.rect.size==pair.Value.Size,"text preset preserves logical glyph and line-break boxes");
                    for(int chapter=0;chapter<20;chapter++)
                    {
                        panel.ShowLevelPageForValidation(chapter);Canvas.ForceUpdateCanvases();int rows=0;
                        foreach(var button in panel.GetButtonsForValidation())if(button.id.StartsWith("start_level_",StringComparison.Ordinal))
                        {
                            rows++;int level=int.Parse(button.id.Substring("start_level_".Length));
                            Check(button.enabled&&button.title.Contains(OceanLevelCatalog.Get(level).Name),"all100 catalog identities remain named and selectable for an explanation");
                        }
                        Check(rows==5,"all100 level rows remain present at text preset "+preset+" chapter "+chapter);
                        foreach(var label in panel.GetComponentsInChildren<Text>())
                            if(label.gameObject.activeInHierarchy && label.name=="Level objectives and allowance")
                                Check(label.preferredHeight<=label.rectTransform.rect.height+.5f,"actual catalog objective text fits after scaling");
                    }
                    for(int level=1;level<=OceanCampaignAvailability.AvailableLevelCount;level++)
                    {
                        state.StartLevel(level);panel.OpenMenu();
                        Check(panel.ClickForValidation("show_targets"),"open actual target page at text preset "+preset+" level "+level);
                        Canvas.ForceUpdateCanvases();
                        foreach(var label in panel.GetComponentsInChildren<Text>())
                            if(label.gameObject.activeInHierarchy && (label.name=="Target species" || label.name=="This run target progress"))
                                Check(label.preferredHeight<=label.rectTransform.rect.height+.5f,"published level target card fits after scaling: level "+level+" "+label.name);
                    }
                    for(int level=OceanCampaignAvailability.AvailableLevelCount+1;level<=OceanLevelCatalog.Count;level++)
                    {
                        // Represent an authenticated old run in memory; never bypass the published UI gate.
                        state.StartLevel(level);panel.OpenMenu();
                        string preserved=JsonUtility.ToJson(state.Save);
                        int modelCount=panel.InstantiatedSpeciesModelsForValidation;
                        Check(!panel.ClickForValidation("show_targets")&&HasText(panel,"请选择开放关卡"),"archived target action remains disabled and guides selection at preset "+preset+" level "+level);
                        panel.ShowLevelPageForValidation((level-1)/5);
                        Check(panel.ClickForValidation("start_level",level)&&panel.PageNameForDiagnostics=="Levels"&&HasText(panel,OceanCampaignAvailability.UnavailableMessage),"unavailable row explains release scope without confirmation at preset "+preset+" level "+level);
                        Check(panel.ActiveSpeciesModelsForValidation==0&&panel.InstantiatedSpeciesModelsForValidation==modelCount&&JsonUtility.ToJson(state.Save)==preserved,"unavailable target request neither loads models nor alters archived progress at preset "+preset+" level "+level);
                        Canvas.ForceUpdateCanvases();
                        foreach(var label in panel.GetComponentsInChildren<Text>())
                            if(label.gameObject.activeInHierarchy&&(label.name=="Menu subtitle"||label.name=="Level objectives and allowance"))
                                Check(label.preferredHeight<=label.rectTransform.rect.height+.5f,"unavailable guidance fits after scaling at preset "+preset+" level "+level);
                    }
                    panel.OpenMenu();Check(panel.ClickForValidation("show_comfort") && panel.ClickForValidation("comfort_more"),"new text/audio page is reachable by production callbacks");
                    Canvas.ForceUpdateCanvases();
                    foreach(var label in panel.GetComponentsInChildren<Text>())if(label.gameObject.activeInHierarchy)
                        Check(label.preferredHeight<=label.rectTransform.rect.height+.5f,"ComfortAudio visible text does not truncate at preset "+preset+": "+label.name);
                    int categoryButtons=0;foreach(var button in panel.GetButtonsForValidation())if(button.id.StartsWith("comfort_audio_",StringComparison.Ordinal)&&button.enabled)categoryButtons++;
                    Check(categoryButtons==3,"all three independent sound categories have live UI controls");
                }
                panel.SetTextScaleForValidation(0);
                var graphic=root.AddComponent<OceanTutorialVisual>();graphic.Begin(view,Resources.Load<Font>("Treasure/TreasureFont"));
                graphic.Render(OceanInteractiveTutorialRules.Step.Tracking,true,false,true,false,.02f,true);
                Check(graphic.VisibleForValidation&&graphic.LeftStatusForValidation.Contains("已追踪")&&graphic.RightStatusForValidation.Contains("未追踪"),"left/right fresh tracking states remain distinct");
                CheckProjection(graphic,view);CheckTextFits(graphic);
                graphic.Render(OceanInteractiveTutorialRules.Step.SwimAndStop,true,true,true,true,.1f,true);
                Check(graphic.DemonstrationForValidation.Contains("示意")&&graphic.DemoSecondsForValidation>0f,"moving graphics are explicitly labelled as a demonstration");
                float clock=graphic.DemoSecondsForValidation;graphic.SetVisible(false);
                graphic.Render(OceanInteractiveTutorialRules.Step.SwimAndStop,false,false,false,false,.1f,false);
                Check(!graphic.VisibleForValidation&&graphic.DemoSecondsForValidation==clock,"menu/focus hiding pauses the demonstration");
                graphic.Close();Check(!graphic.HasGraphicForValidation,"lesson end releases teaching canvas and graphics");
                graphic.Begin(view,Resources.Load<Font>("Treasure/TreasureFont"));
                Check(graphic.DemoSecondsForValidation==0f,"repeated lesson starts with a fresh visual timeline");
                graphic.Close();
            }
            finally{AudioListener.volume=master;UnityEngine.Object.DestroyImmediate(root);}
            Debug.Log("[OceanComfortVisualTests] PASS "+Assertions+" assertions; actual UGUI/font/projection/lifecycle, no headset claim.");
        }
        static bool HasText(OceanCampaignPanel panel,string value)
        {foreach(var label in panel.GetComponentsInChildren<Text>())if(label.gameObject.activeInHierarchy&&label.text.Contains(value))return true;return false;}
        public static void CheckProjection(OceanTutorialVisual graphic,Camera camera)
        {
            foreach(var rect in graphic.GraphicRectForValidation.GetComponentsInChildren<RectTransform>())
            {
                if(!rect.gameObject.activeInHierarchy)continue;
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    Vector3 p=camera.WorldToViewportPoint(corner);
                    Check(p.z>=.3f&&p.z<=1.2f&&p.x>=.02f&&p.x<=.98f&&p.y>=.02f&&p.y<=.98f,"teaching diagram remains in bounded front view");
                }
            }
        }
        static void AutomaticOfferQualifications()
        {
            string folder=Path.Combine(Path.GetTempPath(),"ocean-tutorial-eligibility-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                bool loaded=OceanSaveStore.TryLoad(folder,out OceanSaveData data,out OceanSaveLoadStatus status,out _);
                Check(loaded&&status==OceanSaveLoadStatus.NewSave&&OceanInteractiveTutorial.ShouldOfferAutomatically(loaded,status),"only a genuinely absent save offers automatic teaching");
                string payload="{\"version\":3,\"bestStars\":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],\"shellMask\":0,\"fishMask\":0,\"shark\":false,\"currentRun\":null}";
                string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-","").ToLowerInvariant();
                string original=payload.Substring(0,payload.Length-1)+",\"sha256\":\""+hash+"\"}\n";
                string main=Path.Combine(folder,OceanSaveStore.FileName),backup=Path.Combine(folder,OceanSaveStore.BackupFileName);
                File.WriteAllText(main,original);
                loaded=OceanSaveStore.TryLoad(folder,out data,out status,out _);
                Check(loaded&&status==OceanSaveLoadStatus.Loaded&&data.shellMask==0&&data.fishMask==0&&!OceanInteractiveTutorial.ShouldOfferAutomatically(loaded,status),"authenticated empty v3 history migrates without being mistaken for a new user");
                Check(File.ReadAllText(main)==original,"tutorial eligibility never rewrites an old empty save");
                File.WriteAllText(backup,original);File.WriteAllText(main,"interrupted");
                loaded=OceanSaveStore.TryLoad(folder,out data,out status,out _);
                Check(loaded&&status==OceanSaveLoadStatus.RecoveredBackup&&!OceanInteractiveTutorial.ShouldOfferAutomatically(loaded,status),"recovered old blank backup remains manual-teaching only");
                File.WriteAllText(backup,"interrupted");
                loaded=OceanSaveStore.TryLoad(folder,out data,out status,out _);
                Check(!loaded&&status==OceanSaveLoadStatus.Corrupt&&!OceanInteractiveTutorial.ShouldOfferAutomatically(loaded,status),"read-only corrupt-save fallback cannot automatically launch teaching");
            }
            finally{Directory.Delete(folder,true);}
        }
        public static void CheckTextFits(OceanTutorialVisual graphic)
        {
            Canvas.ForceUpdateCanvases();foreach(var text in graphic.TextForValidation)
                Check(text.preferredHeight<=text.rectTransform.rect.height+.5f,"teaching text fits: "+text.name);
        }
    }
}
#endif
