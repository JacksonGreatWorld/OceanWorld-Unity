#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual production owners with synthetic intent/time. No player files or tracking input.</summary>
    public static class OceanCreatureResponseChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int LastAssertionCount { get; private set; }
        static void Check(bool condition, string message)
        { LastAssertionCount++; if (!condition) throw new InvalidOperationException("Creature response: " + message); }
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        static float Speed(object target) => (float)target.GetType().GetField("swimSpeed", Private).GetValue(target);

        public static void RunAll()
        {
            LastAssertionCount = 0;
            var random = UnityEngine.Random.state;
            float originalVolume = AudioListener.volume;
            try
            {
                foreach (float dt in new[] { 1f/30f, 1f/72f, 1f/90f })
                { CheckQuestApproach(dt); CheckHeroPropulsion(dt); }
                CheckHandOwnership();
                Debug.Log("CREATURE_RESPONSE_CHECKS_PASS assertions=" + LastAssertionCount +
                    "; actual quest/hero propulsion and UI hand ownership, synthetic time/observer; not tracked-hand or wearer acceptance.");
            }
            finally { UnityEngine.Random.state = random; AudioListener.volume = originalVolume; }
        }

        static void CheckQuestApproach(float dt)
        {
            var owner = new GameObject("Isolated real quest response");
            var eye = new GameObject("Synthetic response observer", typeof(Camera));
            var body = new GameObject("Synthetic quest contact body");
            OceanCampaignCreatures actors = null;
            try
            {
                eye.GetComponent<Camera>().enabled = false;
                body.transform.position = new Vector3(0f,20f,0f);
                var collider = body.AddComponent<CapsuleCollider>(); collider.isTrigger = true; collider.radius=.02f; collider.height=.04f;
                var target = new OceanCampaignCreatures.CreatureTarget();
                target.GetType().GetProperty("Species").SetValue(target, 0);
                target.GetType().GetProperty("Body").SetValue(target, collider);
                Set(target,"root",body.transform); Set(target,"anchor",body.transform.position-Vector3.right*.45f);
                Set(target,"clearanceRadius",.03f);
                var state = new OceanCampaignState(); state.StartLevel(3);
                actors=owner.AddComponent<OceanCampaignCreatures>(); actors.Initialize(null,eye.GetComponent<Camera>(),null,state,null);
                ((IList)typeof(OceanCampaignCreatures).GetField("targets",Private).GetValue(actors)).Add(target);
                eye.transform.position = new Vector3(0,20,-8);
                for(int i=0;i<Mathf.CeilToInt(1f/dt);i++)actors.TickGameplay(dt,true);
                Check(Mathf.Abs(Speed(target)-.18f)<.00001f,"quest starts with its original cruise budget");
                eye.transform.position = body.transform.position-body.transform.forward;
                Vector3 atApproach=body.transform.position; float lastSpeed=Speed(target), travel=0f;
                for(int i=0;i<Mathf.CeilToInt(.6f/dt);i++)
                {
                    Vector3 before=body.transform.position; actors.TickGameplay(dt,true);
                    float speed=Speed(target); Vector3 step=body.transform.position-before;
                    Check(speed<=lastSpeed+.00001f && lastSpeed-speed<=.65f*dt+.00002f,"near observer gives bounded deceleration");
                    if(step.sqrMagnitude>1e-10f)Check(Vector3.Dot(step.normalized,body.transform.forward)>.9995f,"braking fish still swims nose-first");
                    travel+=step.magnitude; lastSpeed=speed;
                }
                Check(travel>.001f && travel<.04f,"approach coasts a few centimetres instead of a one-frame freeze or an unreachable retreat");
                Check(Speed(target)==0f && target.FleeCount==0,"early-level calm grasp window stays exact and never becomes a fright");
                Vector3 rest=body.transform.position;
                for(int i=0;i<20;i++)actors.TickGameplay(dt,true);
                Check(body.transform.position==rest,"grasp opportunity does not creep away after braking");
                eye.transform.position = body.transform.position-Vector3.forward*8f;
                actors.TickGameplay(dt,true);
                Check(Speed(target)>0f && Speed(target)<=.26f*dt+.00001f,"leaving reach smoothly restarts propulsion");
                float paused=Speed(target); Vector3 pausedPosition=body.transform.position;
                actors.TickGameplay(dt,false);
                Check(Speed(target)==paused && body.transform.position==pausedPosition,"menu pause freezes both navigation and propulsion state");
                Check(state.Current.fishMask==0,"movement fixture cannot collect or alter history");
            }
            finally
            {
                if(actors!=null)((IList)typeof(OceanCampaignCreatures).GetField("targets",Private).GetValue(actors)).Clear();
                Object.DestroyImmediate(owner); Object.DestroyImmediate(eye); Object.DestroyImmediate(body);
            }
        }

        static void CheckHeroPropulsion(float dt)
        {
            var host=new GameObject("Isolated real hero propulsion");
            try
            {
                host.transform.position=new Vector3(0,20,0);
                Type type=typeof(UnderwaterWorld).GetNestedType("FishAgent",BindingFlags.NonPublic);
                object fish=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    new object[]{host.transform,.4f,Vector3.forward,false,-1,.15f},null);
                var advance=type.GetMethod("AdvanceFacing",Private); var speedField=type.GetField("locomotionSpeed",Private);
                float previous=.4f;
                for(int i=0;i<Mathf.CeilToInt(.6f/dt);i++)
                {
                    Vector3 at=host.transform.position;
                    advance.Invoke(fish,new object[]{Vector3.forward,dt,140f});
                    float speed=(float)speedField.GetValue(fish);
                    Check(speed>=previous-.00001f && speed-previous<=2f*dt+.00002f,"fright intent cannot instantaneously jump to 2.5x cruise");
                    Check(Vector3.Dot((host.transform.position-at).normalized,host.transform.forward)>.9995f,"accelerating hero follows its nose");
                    previous=speed;
                }
                Check(Mathf.Abs(previous-1f)<.00001f,"smoothing preserves the original burst speed");
                Vector3 stopStart=host.transform.position;
                for(int i=0;i<Mathf.CeilToInt(1f/dt);i++)
                {
                    advance.Invoke(fish,new object[]{Vector3.zero,dt,85f});
                    float speed=(float)speedField.GetValue(fish);
                    Check(speed<=previous+.00001f && previous-speed<=1.6f*dt+.00002f,"pause intent brakes without a speed discontinuity");previous=speed;
                }
                Check(previous==0f && Vector3.Distance(stopStart,host.transform.position)>.05f,"hero actually decelerates to rest");
                Vector3 rest=host.transform.position;advance.Invoke(fish,new object[]{Vector3.zero,dt,85f});
                Check(host.transform.position==rest,"resting hero cannot accumulate stale coast");
                speedField.SetValue(fish,1f);
                type.GetProperty("IsSheltered").SetValue(fish,true);
                for(int i=0;i<30;i++)advance.Invoke(fish,new object[]{Vector3.zero,dt,85f});
                Check(host.transform.position==rest&&(float)speedField.GetValue(fish)==0f,
                    "earned refuge hold discards fright momentum instead of coasting out of shelter");
                type.GetProperty("IsSheltered").SetValue(fish,false);
                advance.Invoke(fish,new object[]{Vector3.forward*.4f,dt,85f});
                Check((float)speedField.GetValue(fish)>0f&&(float)speedField.GetValue(fish)<=2f*dt+.00001f,
                    "leaving a refuge restarts smoothly without restoring stale burst speed");
            }
            finally {Object.DestroyImmediate(host);}
        }

        static void CheckHandOwnership()
        {
            var root=new GameObject("Isolated wildlife input gates");
            try
            {
                var hands=root.AddComponent<HandSwimController>();
                var gate=typeof(UnderwaterWorld).GetMethod("WildlifeHandResponsesAllowed",BindingFlags.Static|BindingFlags.NonPublic);
                bool Allowed()=> (bool)gate.Invoke(null,new object[]{hands});
                Set(hands,"appPaused",false);Set(hands,"appFocused",true);
                hands.SetInteractionSuppressed(false); hands.SetMenuLocomotionPaused(false);
                Check(Allowed(),"active gameplay can ask for fresh tracked palms");
                hands.SetMenuLocomotionPaused(true);Check(!Allowed(),"UI menu hands cannot scare wildlife");
                hands.SetMenuLocomotionPaused(false);hands.SetInteractionSuppressed(true);Check(!Allowed(),"transition/loading hands cannot scare wildlife");
                hands.SetInteractionSuppressed(false);Set(hands,"appFocused",false);Check(!Allowed(),"focus loss cannot scare wildlife");
                Set(hands,"appFocused",true);Set(hands,"appPaused",true);Check(!Allowed(),"application pause cannot scare wildlife");
                Set(hands,"appPaused",false);hands.enabled=false;Check(!Allowed(),"disabled tracker is not a wildlife threat");
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
#endif
