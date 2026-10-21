#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>AVP pointer visibility survives unavailable fingertips without arming a fabricated pinch.</summary>
    public static class OceanMenuPointerTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void RunAll()
        {
            int assertions = 0;
            Action<bool,string> check=(ok,message)=> { if(!ok)throw new InvalidOperationException("OceanMenuPointerTests: "+message); assertions++; };
            Type panelType=typeof(OceanCampaignPanel);
            MethodInfo step=panelType.GetMethod("StepPointerGate",BindingFlags.Static|BindingFlags.NonPublic);
            check(step!=null,"production pointer qualification present");
            var gate=new OceanMenuPressGate();
            int Step(int frame,bool ray,bool pinch,float strength)
                => (int)step.Invoke(null,new object[]{gate,frame,.05f,ray,pinch,strength,0,true});
            check(Step(1,true,false,float.NaN)==-1 && !gate.Armed,"unavailable fingertips cannot fabricate a release");
            check(Step(2,true,false,1)==-1 && !gate.Armed,"ray-only closed hand cannot activate");
            check(Step(3,true,true,0)==-1 && Step(4,true,true,0)==-1 && Step(5,true,true,0)==-1 && gate.Armed,"arrival followed by fresh released hover qualifies normally");
            check(Step(6,true,false,1)==-1 && !gate.Armed,"tip loss disarms even with a current pointer");
            check(Step(7,true,true,1)==-1 && !gate.Armed,"closed hand after tip recovery cannot click");
            check(Step(8,true,true,0)==-1 && Step(9,true,true,0)==-1 && gate.Armed,"fresh release requalifies after tip recovery");
            check(Step(10,true,true,1)==0,"subsequent genuine pinch clicks");
            check(Step(11,true,true,1)==-1,"held pinch cannot repeat");
            check(Step(12,false,true,0)==-1 && !gate.Armed,"ray loss also disarms");

            var root=new GameObject("Pointer qualification check");
            try
            {
                var panel=root.AddComponent<OceanCampaignPanel>();
                panelType.GetField("available",Private).SetValue(panel,true);
                object input=panelType.GetField("leftHand",Private).GetValue(panel);
                Type inputType=input.GetType();
                var rayObject=new GameObject("ray");rayObject.transform.SetParent(root.transform);
                var cursorObject=new GameObject("cursor");cursorObject.transform.SetParent(root.transform);
                var ray=rayObject.AddComponent<LineRenderer>();ray.positionCount=2;
                var cursor=cursorObject.AddComponent<LineRenderer>();cursor.positionCount=12;
                inputType.GetField("ray").SetValue(input,ray);inputType.GetField("cursor").SetValue(input,cursor);
                Type targetType=panelType.GetNestedType("Target",BindingFlags.NonPublic);
                object target=Activator.CreateInstance(targetType,true);
                var button=new GameObject("hover",typeof(RectTransform));button.transform.SetParent(root.transform);
                var rect=button.GetComponent<RectTransform>();rect.position=Vector3.forward;rect.sizeDelta=Vector2.one;
                targetType.GetField("id").SetValue(target,"fixture");targetType.GetField("rect").SetValue(target,rect);
                int dispatched=0;targetType.GetField("action").SetValue(target,(Action)(()=>dispatched++));
                ((IList)panelType.GetField("targets",Private).GetValue(panel)).Add(target);
                var process=panelType.GetMethod("ProcessRaySample",Private);
                process.Invoke(panel,new object[]{input,new Ray(Vector3.zero,Vector3.forward),float.NaN,true,false});
                check(ray.enabled && cursor.enabled,"current ray and hover stay visible without pinch tips");
                check((bool)targetType.GetField("hovered").GetValue(target),"pointer-only target receives hover");
                check(panel.ActivationCountForValidation==0 && dispatched==0,"pointer-only hover does not activate an available target");
                bool finite=true;for(int i=0;i<cursor.positionCount;i++){var p=cursor.GetPosition(i);finite &= !float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z);}
                check(finite,"unavailable pinch produces finite cursor geometry");
                process.Invoke(panel,new object[]{input,new Ray(Vector3.zero,Vector3.forward),float.NaN,true,true});
                finite=true;for(int i=0;i<cursor.positionCount;i++){var p=cursor.GetPosition(i);finite &= !float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z);}
                check(finite && dispatched==0,"invalid claimed pinch remains pointer-only with finite geometry");
                process.Invoke(panel,new object[]{input,default(Ray),float.NaN,false,false});
                check(!ray.enabled && !cursor.enabled,"loss of current ray hides pointer immediately");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("OceanMenuPointerTests PASS: "+assertions+" assertions; current pointer survives unavailable tips, activation remains disarmed. No physical tracking claim.");
        }
    }
}
#endif
