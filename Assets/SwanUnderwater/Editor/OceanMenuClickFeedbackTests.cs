#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.UI;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual UGUI callbacks/gates and bounded acknowledgement geometry; no device or user progress.</summary>
    public static class OceanMenuClickFeedbackTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int Assertions { get; private set; }
        static void Check(bool value,string message)
        { Assertions++;if(!value)throw new InvalidOperationException("Menu press feedback: "+message); }
        static void Set(OceanCampaignPanel panel,string name,object value)
            => typeof(OceanCampaignPanel).GetField(name,Private).SetValue(panel,value);
        static void Arm(OceanCampaignPanel panel)
        {
            Set(panel,"openAmount",1f);Set(panel,"blockedUntil",0d);Set(panel,"activatedFrame",-1);
            typeof(OceanCampaignPanel).GetMethod("ApplyExpansionPose",Private).Invoke(panel,null);
        }
        static void EditModeLifecycle(OceanCampaignPanel panel,string method)
        {
            // A plain runtime MonoBehaviour is deliberately not ExecuteAlways.
            // Static EditMode checks must invoke its real lifecycle implementation;
            // CheckRuntimeLifecycle below verifies Unity's actual PlayMode callbacks.
            if(!Application.isPlaying)
                typeof(OceanCampaignPanel).GetMethod(method,Private).Invoke(panel,null);
        }
        static OceanCampaignPanel.ButtonInfoForValidation Button(OceanCampaignPanel panel,string id)
        {
            foreach(var target in panel.GetButtonsForValidation())if(target.id==id)return target;
            throw new InvalidOperationException("Missing active menu button "+id);
        }
        public static void RunAll()
        {
            Assertions=0;
            var root=new GameObject("Isolated accepted UI press fixture");
            OceanMenuClickFeedback feedback=null;
            OceanCampaignPanel panel=null;Exception failure=null;
            try
            {
                var cameraObject=new GameObject("Disabled fixture camera");cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.position=Vector3.up*1.6f;
                var hands=root.AddComponent<HandSwimController>();hands.enabled=false;
                var state=new OceanCampaignState();state.Save.bestStars[0]=2;state.StartLevel(1);
                panel=root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(camera,hands,state,(action,level)=>{if(action=="resume")panel.CloseMenu();});
                panel.SetAvailable(true);panel.SkipTutorialForValidation();panel.OpenMenu();Arm(panel);
                feedback=panel.ClickFeedbackForValidation;
                Check(feedback!=null&&!feedback.IsPlaying&&!feedback.raycastTarget&&!feedback.maskable,"one inactive non-interactive reused Graphic is prepared");
                Check(feedback.transform.parent==null&&feedback.GetComponent<GraphicRaycaster>()==null,"world anchor is independent of moving panel and has no raycaster");
                Material shared=feedback.material;
                Check(shared==(Material)typeof(OceanCampaignPanel).GetField("rayMaterial",Private).GetValue(panel),"existing material is shared rather than allocated per press");
                var source=Button(panel,"show_levels");Vector2 hitSize=source.rect.rect.size;Vector3 hitScale=source.rect.localScale;
                Vector3 hit=source.rect.TransformPoint(new Vector3(19,-7,0));Vector3 expected=hit-source.rect.forward*.004f;
                foreach(object target in (IList)typeof(OceanCampaignPanel).GetField("targets",Private).GetValue(panel))
                    if((string)target.GetType().GetField("id").GetValue(target)=="show_levels")
                    {target.GetType().GetField("pointerPoint").SetValue(target,hit);target.GetType().GetField("pointerFrame").SetValue(target,Time.frameCount);}
                Check(panel.ActivateForValidation("show_levels")&&panel.PageNameForDiagnostics=="Levels","production qualified action changes page");
                Check(feedback.IsPlaying&&Vector3.Distance(feedback.transform.position,expected)<.000001f,"accepted page change acknowledges the actual hit, not camera center");
                Check(!source.rect.gameObject.activeInHierarchy&&source.rect.rect.size==hitSize&&source.rect.localScale==hitScale,"feedback survives hidden source without resizing its hit region");
                int accepted=panel.ActivationCountForValidation;
                Check(!panel.ActivateForValidation("levels_next")&&panel.ActivationCountForValidation==accepted,"second hand or repeated press in the same frame is rejected");
                Check(Vector3.Distance(feedback.transform.position,expected)<.000001f,"rejected repeat does not move the acknowledgement");
                camera.transform.position+=new Vector3(.4f,.2f,.1f);panel.ExpandedRectForValidation.position+=Vector3.right*.25f;
                typeof(OceanMenuClickFeedback).GetMethod("LateUpdate",Private).Invoke(feedback,null);
                Check(feedback.IsPlaying&&Vector3.Distance(feedback.transform.position,expected)<.000001f,"head/panel movement cannot drag the world-anchored press");
                feedback.Cancel();Set(panel,"activatedFrame",-1);Set(panel,"blockedUntil",Time.unscaledTimeAsDouble+10d);
                Check(!panel.ActivateForValidation("levels_next")&&!feedback.IsPlaying,"cooldown rejection does not show acknowledgement");
                Arm(panel);Set(panel,"openAmount",.5f);
                Check(!panel.ActivateForValidation("levels_next")&&!feedback.IsPlaying,"transition rejection does not show acknowledgement");
                Arm(panel);
                Check(!Button(panel,"levels_previous").enabled&&!panel.ActivateForValidation("levels_previous")&&!feedback.IsPlaying,"disabled navigation does not show acknowledgement");
                panel.ShowLevelPageForValidation(4);Arm(panel);string preserved=JsonUtility.ToJson(state.Save);
                Check(panel.ActivateForValidation("start_level_21")&&feedback.IsPlaying,"future level may acknowledge a recognized explanatory click");
                Check(panel.PageNameForDiagnostics=="Levels"&&JsonUtility.ToJson(state.Save)==preserved,"future level still cannot enter or mutate saved progress");
                panel.CloseMenu();Check(!feedback.IsPlaying,"explicit hiding cancels previous acknowledgement");
                panel.OpenMenu();Check(!feedback.IsPlaying,"reopening does not replay an old press");Arm(panel);
                Check(panel.ActivateForValidation("show_levels")&&feedback.IsPlaying&&feedback.material==shared,"subsequent accepted press reuses the exact Graphic/material");
                panel.SetAvailable(false);Check(!feedback.IsPlaying,"unavailable state cancels acknowledgement");
                panel.SetAvailable(true);panel.OpenMenu();Arm(panel);panel.ActivateForValidation("show_levels");
                panel.enabled=false;EditModeLifecycle(panel,"OnDisable");
                Check(!feedback.IsPlaying,"component disable implementation cancels independent world Graphic");
                panel.enabled=true;EditModeLifecycle(panel,"OnEnable");
                Check(!feedback.IsPlaying,"component enable implementation does not replay it");
                feedback.Play(Vector3.one,Quaternion.identity,Vector3.one*.001f,68,Color.white);
                feedback.SetPhaseForValidation(1f);Check(!feedback.IsPlaying,"expired phase deactivates Graphic");
                feedback.Play(new Vector3(float.NaN,0,0),Quaternion.identity,Vector3.one,68,Color.white);
                Check(!feedback.IsPlaying,"nonfinite hit is rejected without making geometry active");
                MeshBudget();
            }
            catch(Exception error) { failure=error; }
            finally
            {
                // Never replace a test failure with a secondary cleanup assertion.
                // EditMode will not call OnDestroy on this non-ExecuteAlways runtime
                // component, so exercise its idempotent production owner cleanup.
                try
                {
                    try { if(panel!=null)EditModeLifecycle(panel,"OnDestroy"); }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                        bool released=feedback==null;
                        if(feedback!=null)UnityEngine.Object.DestroyImmediate(feedback.gameObject);
                        Check(released,"destroying panel releases its independent Graphic");
                    }
                }
                catch(Exception cleanupError)
                { if(failure==null)failure=cleanupError;else Debug.LogError("Additional menu fixture cleanup failure (original failure preserved): "+cleanupError); }
            }
            if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
            Debug.Log("[OceanMenuClickFeedbackTests] PASS "+Assertions+" assertions; actual UGUI actions/gates/lifecycle and bounded geometry. No physical input or headset claim.");
        }
        /// <summary>
        /// Run from the existing PlayMode preview coroutine. Uses actual frame time,
        /// Activate and engine lifecycle callbacks; no phase injection or saved state.
        /// </summary>
        public static IEnumerator CheckRuntimeLifecycle()
        {
            Check(Application.isPlaying,"runtime lifecycle check requires actual PlayMode");
            var root=new GameObject("Isolated live UI press lifecycle fixture");
            OceanMenuClickFeedback feedback=null;
            try
            {
                var cameraObject=new GameObject("Disabled live press camera");cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.transform.position=Vector3.up*1.6f;
                var hands=root.AddComponent<HandSwimController>();hands.enabled=false;
                var state=new OceanCampaignState();state.Save.bestStars[0]=2;state.StartLevel(1);
                var panel=root.AddComponent<OceanCampaignPanel>();
                panel.Initialize(camera,hands,state,(action,level)=>{if(action=="resume")panel.CloseMenu();});
                panel.SetAvailable(true);panel.SkipTutorialForValidation();panel.OpenMenu();
                yield return WaitForMenuReady(panel);
                Check(panel.IsOpen&&!panel.IsTransitioning,"actual menu expansion and cooldown finish before resume");
                var resume=Button(panel,"resume");Vector3 position=resume.rect.position-resume.rect.forward*.004f;
                Vector2 hitSize=resume.rect.rect.size;Vector3 hitScale=resume.rect.localScale;
                feedback=panel.ClickFeedbackForValidation;
                Check(panel.ActivateForValidation("resume")&&!panel.IsOpen&&panel.IsTransitioning,"production accepted resume closes the menu");
                Check(feedback.IsPlaying&&Vector3.Distance(feedback.transform.position,position)<.000001f,"current resume acknowledgement starts after callback resets prior input");
                yield return new WaitForSecondsRealtime(.07f);
                Check(feedback.IsPlaying&&Vector3.Distance(feedback.transform.position,position)<.000001f,"current press survives menu collapse without following HUD");
                Check(resume.rect.rect.size==hitSize&&resume.rect.localScale==hitScale,"actual collapsing press does not animate its target hit rectangle");
                yield return new WaitForSecondsRealtime(.22f);yield return null;
                // Closing finishes at 260ms and cancels prior input. A single slow
                // frame can cross both thresholds, so this case proves no residue;
                // the stable-page case below isolates actual clock expiration.
                Check(!feedback.IsPlaying&&!feedback.gameObject.activeSelf,"accepted resume leaves no acknowledgement after closing");
                panel.OpenMenu();Check(!feedback.IsPlaying,"actual reopening cannot replay the expired press");
                yield return WaitForMenuReady(panel);
                int expiries=feedback.NaturalExpiryCountForValidation;
                Check(panel.ActivateForValidation("show_levels")&&feedback.IsPlaying,"later real action reuses live Graphic");
                yield return new WaitForSecondsRealtime(.29f);yield return null;
                Check(panel.IsOpen&&!panel.IsTransitioning&&!feedback.IsPlaying&&!feedback.gameObject.activeSelf&&
                    feedback.NaturalExpiryCountForValidation==expiries+1,
                    "stable open menu isolates the actual 240ms Graphic clock expiry from panel cancellation");
                yield return new WaitForSecondsRealtime(.1f);
                Check(panel.ActivateForValidation("levels_next")&&feedback.IsPlaying,"next accepted page action reuses Graphic before lifecycle check");
                panel.enabled=false;Check(!feedback.IsPlaying,"Unity PlayMode OnDisable cancels independent Graphic");
                panel.enabled=true;Check(!feedback.IsPlaying,"Unity PlayMode OnEnable never replays it");
                UnityEngine.Object.Destroy(root);root=null;
                yield return null;yield return null;
                Check(feedback==null,"Unity PlayMode owner destruction releases the independent Graphic after destruction phase");
            }
            finally { if(root!=null)UnityEngine.Object.Destroy(root); }
            Debug.Log("[OceanMenuClickFeedbackTests] Runtime lifecycle PASS: accepted resume, real clock expiry, actual disable/reopen/destroy. No headset claim.");
        }
        static IEnumerator WaitForMenuReady(OceanCampaignPanel panel)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+5d;
            while(panel.IsTransitioning&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Check(!panel.IsTransitioning,"actual expansion completes within bounded preview time");
            // Start this after transition completion, rather than assuming a fixed
            // startup duration across slow shader/import or capture frames.
            yield return new WaitForSecondsRealtime(.4f);
        }
        static void MeshBudget()
        {
            var populate=typeof(OceanMenuClickFeedback).GetMethod("Populate",BindingFlags.Static|BindingFlags.NonPublic);
            using(var helper=new VertexHelper())
            {
                foreach(float phase in new[]{0f,.25f,.75f,.99f})
                {
                    populate.Invoke(null,new object[]{helper,25f,phase,new Color(1,.87f,.58f,.92f)});
                    Check(helper.currentVertCount==112&&helper.currentIndexCount==168,"acknowledgement stays at 112 vertices/56 triangles");
                    var vertex=new UIVertex();
                    for(int i=0;i<helper.currentVertCount;i++)
                    {
                        helper.PopulateUIVertex(ref vertex,i);
                        Check(!float.IsNaN(vertex.position.x)&&!float.IsInfinity(vertex.position.y)&&vertex.position.sqrMagnitude<=625.001f,"finite geometry remains inside a 25-pixel radius");
                    }
                }
                foreach(float phase in new[]{1f,-1f,float.NaN})
                {populate.Invoke(null,new object[]{helper,25f,phase,Color.white});Check(helper.currentVertCount==0,"invalid/expired phase clears previous geometry");}
            }
        }
    }
}
#endif
