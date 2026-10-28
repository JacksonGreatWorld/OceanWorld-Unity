#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    public static class OceanTutorialExitChecks
    {
        const string SeenKey="ocean_interactive_tutorial_seen_v1";
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++;if(!value)throw new InvalidOperationException("Tutorial exit: "+message); }
        static OceanCampaignPanel.ButtonInfoForValidation Button(OceanCampaignPanel panel,string id)
        {
            foreach(var b in panel.GetButtonsForValidation())if(b.id==id)return b;
            throw new InvalidOperationException("Tutorial exit target is not visible: "+id);
        }
        static bool Has(OceanCampaignPanel panel,string id)
        { foreach(var b in panel.GetButtonsForValidation())if(b.id==id)return true;return false; }
        static void RestoreSeen(bool had,int value)
        {if(had)PlayerPrefs.SetInt(SeenKey,value);else PlayerPrefs.DeleteKey(SeenKey);PlayerPrefs.Save();}
        static void SetStep(OceanInteractiveTutorial tutorial,int step)
        {
            if(step==0)return;
            for(int i=0;i<8;i++)tutorial.Rules.Observe(.1f,true,0,0,true);
            if(step==1)return;
            tutorial.Rules.Observe(.1f,true,2,.2f,true);
            for(int i=0;i<8;i++)tutorial.Rules.Observe(.1f,true,2,0,true);
            if(step==3)Check(tutorial.Rules.PracticeGrabAccepted(true),"isolated rule setup reaches menu step without campaign reward");
        }
        public static void RunAll()
        {
            Assertions=0;bool had=PlayerPrefs.HasKey(SeenKey);int seen=PlayerPrefs.GetInt(SeenKey,0);float volume=AudioListener.volume;
            var root=new GameObject("Isolated tutorial quick exit fixture");
            try
            {
                var cameraObject=new GameObject("Tutorial exit camera");cameraObject.transform.SetParent(root.transform,false);
                var view=cameraObject.AddComponent<Camera>();view.enabled=false;view.transform.position=Vector3.up*1.6f;
                var hands=root.AddComponent<HandSwimController>();hands.enabled=false;
                var state=new OceanCampaignState();state.StartLevel(1);int resumes=0;
                var panel=root.AddComponent<OceanCampaignPanel>();panel.Initialize(view,hands,state,(action,level)=>{if(action=="resume"){resumes++;panel.CloseMenu();}});
                panel.SetAvailable(true);var tutorial=root.GetComponent<OceanInteractiveTutorial>();
                for(int preset=0;preset<3;preset++)
                {
                    panel.SetTextScaleForValidation(preset);tutorial.Begin();
                    var quick=Button(panel,"tutorial_exit");Check(quick.enabled&&quick.title=="关闭教学","quick close is explicit without entering settings");
                    Check(quick.rect.rect.width>=180&&quick.rect.rect.height>=44,"quick close has a generous hit rectangle");
                    foreach(var text in quick.rect.GetComponentsInChildren<Text>())
                    {Check(text.preferredWidth<=text.rectTransform.rect.width+.5f&&text.preferredHeight<=text.rectTransform.rect.height+.5f,"close text is not truncated");foreach(char c in text.text)Check(text.font.HasCharacter(c),"close label glyph exists");}
                    string before=JsonUtility.ToJson(state.Save);
                    Check(panel.ClickForValidation("tutorial_exit")&&!tutorial.Active&&tutorial.Rules.Current==OceanInteractiveTutorialRules.Step.Skipped,"real close callback skips rather than fabricating completed practice");
                    Check(JsonUtility.ToJson(state.Save)==before&&!Has(panel,"tutorial_exit"),"close preserves all campaign data and hides the quick target");
                }
                Check(resumes==3&&PlayerPrefs.GetInt(SeenKey,0)==1,"close resumes once and remembers the dismissed offer");
                state.StartLevel(21);string archived=JsonUtility.ToJson(state.Save);tutorial.Begin();panel.OpenMenu();
                var header=Button(panel,"close");Check(header.enabled&&header.title=="关闭教学","archived future runs still have a usable header exit");
                Check(panel.ClickForValidation("close")&&!tutorial.Active,"future run cannot trap a replayed lesson");
                Check(resumes==3&&state.Current.level==21&&JsonUtility.ToJson(state.Save)==archived,"closing a lesson cannot resume or modify an unopened archived run");
                Object.DestroyImmediate(panel.ClickFeedbackForValidation.gameObject);
                panel.SetAvailable(false);
                typeof(OceanCampaignPanel).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(panel,null);
                Check(panel.ClickFeedbackForValidation==null,"owner cleanup tolerates an acknowledgement Graphic already destroyed by scene teardown");
                Debug.Log("TUTORIAL_EXIT_CHECKS_PASS assertions="+Assertions+"; actual UI/callbacks and future-run gate, no tracked-input or device claim.");
            }
            finally{Object.DestroyImmediate(root);RestoreSeen(had,seen);AudioListener.volume=volume;}
        }

        public static IEnumerator RunInPlayMode(OceanCampaignPanel panel,Camera view,OceanCampaignState state,Action<string> capture)
        {
            Assertions=0;
            Check(Application.isPlaying,"qualified ray checks require real PlayMode");
            bool had=PlayerPrefs.HasKey(SeenKey);int seen=PlayerPrefs.GetInt(SeenKey,0);
            var tutorial=panel.GetComponent<OceanInteractiveTutorial>();
            int quickId=0;
            try
            {
                for(int preset=0;preset<3;preset++)for(int step=0;step<4;step++)for(int route=0;route<2;route++)
                {
                    state.StartLevel(1);panel.SetTextScaleForValidation(preset);tutorial.Begin();SetStep(tutorial,step);
                    yield return new WaitForSecondsRealtime(.6f);
                    Check(tutorial.Active,"all four real tutorial states retain an exit");
                    Check(tutorial.Rules.Current==(OceanInteractiveTutorialRules.Step)(step+1),"fixture reaches the exact requested lesson step");
                    var quick=Button(panel,"tutorial_exit");
                    if(quickId==0)quickId=quick.rect.GetInstanceID();
                    Check(quickId==quick.rect.GetInstanceID(),"replay reuses its one owned quick-close button");
                    CheckWithinView(view,quick.rect);
                    if(panel.GuideRectForValidation.gameObject.activeInHierarchy)
                        foreach(var text in panel.GuideRectForValidation.GetComponentsInChildren<Text>())
                            Check(text.preferredHeight<=text.rectTransform.rect.height+.5f&&text.preferredWidth<=text.rectTransform.rect.width+.5f,
                                "tutorial hint and direct-close instruction fit the actual HUD box");
                    if(route==1)
                    {panel.OpenMenu();yield return new WaitForSecondsRealtime(.6f);Check(!Has(panel,"tutorial_exit"),"open menu hides the floating close instead of covering menu choices");Check(Button(panel,"close").title=="关闭教学","header exit remains explicit in the menu");}
                    string save=JsonUtility.ToJson(state.Save);int before=panel.ActivationCountForValidation;
                    // The diagram uses explicitly synthetic status artwork, while the
                    // real disabled tracker exercises the no-hand exit visibility case.
                    var graphic=panel.GetComponent<OceanTutorialVisual>();
                    if(route==0&&graphic!=null)graphic.Render(tutorial.Rules.Current,false,false,false,false,.02f,true);
                    capture("tutorial-"+step+"-text-"+preset+"-"+(route==0?"quick":"header"));
                    yield return Press(panel,view,route==0?"tutorial_exit":"close");
                    Check(panel.ActivationCountForValidation==before+1,"one held pinch produces one accepted close");
                    Check(!tutorial.Active&&tutorial.Rules.Current==OceanInteractiveTutorialRules.Step.Skipped&&!Has(panel,"tutorial_exit"),"accepted close exits the lesson and hides its target");
                    Check(!panel.WorldInputBlocked&&!panel.IsOpen,"input returns after the existing close guard and transition finish");
                    Check(JsonUtility.ToJson(state.Save)==save,"qualified close cannot change time, progress, coins, stars or ammunition");
                    Check(graphic==null||!graphic.HasGraphicForValidation,"teaching graphics are released after exit");
                }
                foreach(bool resultPage in new[]{false,true})
                {
                    state.StartLevel(1);tutorial.Begin();panel.OpenMenu();yield return new WaitForSecondsRealtime(.6f);
                    if(resultPage)panel.ShowResult(false);else Check(panel.ClickForValidation("restart_game"),"open original confirmation without accepting a restart");
                    yield return new WaitForSecondsRealtime(.6f);
                    Check(Button(panel,"close").enabled&&Button(panel,"close").title=="关闭教学","result/confirmation page cannot hide the lesson exit");
                    string unchanged=JsonUtility.ToJson(state.Save);capture(resultPage?"tutorial-result-close":"tutorial-confirm-close");
                    yield return Press(panel,view,"close");Check(!tutorial.Active&&JsonUtility.ToJson(state.Save)==unchanged,"closing on a secondary page cannot execute its restart/result action");
                }
                state.StartLevel(1);tutorial.Begin();SetStep(tutorial,3);panel.OpenMenu();yield return new WaitForSecondsRealtime(.6f);
                string completedSave=JsonUtility.ToJson(state.Save);
                yield return Press(panel,view,"show_targets");yield return Press(panel,view,"back");
                Check(Button(panel,"resume").title=="完成教学","original success path has an explicit completion action distinct from dismiss");
                capture("tutorial-original-completion-path");yield return Press(panel,view,"resume");
                Check(tutorial.Rules.Current==OceanInteractiveTutorialRules.Step.Complete&&JsonUtility.ToJson(state.Save)==completedSave,
                    "real viewed-target/resume path still completes practice without changing campaign progress");
                tutorial.Begin();yield return new WaitForSecondsRealtime(.6f);var rect=Button(panel,"tutorial_exit").rect;
                panel.SetAvailable(false);Check(!rect.gameObject.activeInHierarchy,"unavailable app hides its independent exit");
                panel.SetAvailable(true);yield return null;Check(rect.gameObject.activeInHierarchy,"returning availability restores the active lesson exit");
                panel.enabled=false;Check(!rect.gameObject.activeInHierarchy,"real PlayMode OnDisable hides the independent exit");
                panel.enabled=true;yield return null;Check(rect.gameObject.activeInHierarchy,"OnEnable restores only an active available lesson exit");
                yield return Press(panel,view,"tutorial_exit");capture("tutorial-closed-gameplay");
                Object.Destroy(panel.ClickFeedbackForValidation.gameObject);yield return null;
                panel.enabled=false;panel.enabled=true;
                Check(panel.ClickFeedbackForValidation==null,"real PlayMode disable tolerates the independent Graphic being destroyed first");
                Debug.Log("TUTORIAL_EXIT_PLAYMODE_PASS assertions="+Assertions+"; 4 lesson steps x 3 text sizes x 2 routes, qualified synthetic ray pinches, replay and real lifecycle. No physical tracking claim.");
            }
            finally {panel.ClearValidationRays();RestoreSeen(had,seen);}
        }
        static void CheckWithinView(Camera view,RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            foreach(var corner in corners){var p=view.WorldToViewportPoint(corner);Check(p.z>0f&&p.x>.03f&&p.x<.97f&&p.y>.03f&&p.y<.97f,"close stays inside the view at every text preset");}
            foreach(var t in rect.GetComponentsInChildren<Text>())Check(t.preferredHeight<=t.rectTransform.rect.height+.5f&&t.preferredWidth<=t.rectTransform.rect.width+.5f,"close label fits its actual text box");
        }
        static IEnumerator Press(OceanCampaignPanel panel,Camera view,string id)
        {
            var rect=Button(panel,id).rect;int before=panel.ActivationCountForValidation;
            foreach(float strength in new[]{0f,1f,0f})
            {
                double until=Time.unscaledTimeAsDouble+(strength==0f?.65:.16);
                do{panel.DriveMenuRayForValidation(true,new Ray(view.transform.position,(rect.position-view.transform.position).normalized),strength,true);panel.DriveMenuRayForValidation(false,default,0,false);yield return null;}while(Time.unscaledTimeAsDouble<until);
            }
            Check(panel.ActivationCountForValidation==before+1,"qualified release-hover-pinch closes exactly once");panel.ClearValidationRays();
        }
        public static IEnumerator PressInScene(OceanCampaignPanel panel,Camera view,string id) => Press(panel,view,id);
    }
}
#endif
