using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Bounded world-space vortices at the palm; no particles when tracking is lost.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class HandWaterWake : MonoBehaviour
    {
        HandSwimController hands;
        ParticleSystem left, right;
        Material material;
        Mesh quad;
        float leftClock, rightClock;
        void Start()
        {
            hands=FindFirstObjectByType<HandSwimController>();
            var shader=Shader.Find("SwanUnderwater/HandWake");
            if(hands==null||shader==null){enabled=false;return;}
            material=new Material(shader){name="Palm water vortices"};
            quad=new Mesh{name="Water wake quad"};
            quad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0)};
            quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};quad.triangles=new[]{0,2,1,2,3,1};quad.RecalculateBounds();
            left=Create("Left palm water");right=Create("Right palm water");
        }
        ParticleSystem Create(string label)
        {
            var go=new GameObject(label);go.transform.SetParent(transform,false);
            var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.loop=true;main.startSpeed=0;main.startLifetime=.75f;main.startSize=.15f;
            main.maxParticles=32;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startRotation3D=true;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.35f,1,1.8f));
            var color=ps.colorOverLifetime;color.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.75f,.12f),new GradientAlphaKey(0,1)});
            color.color=gradient;
            var r=go.GetComponent<ParticleSystemRenderer>();r.renderMode=ParticleSystemRenderMode.Mesh;r.mesh=quad;
            r.alignment=ParticleSystemRenderSpace.World;r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            ps.Play();return ps;
        }
        void LateUpdate()
        {
            if(hands==null||left==null)return;
            Emit(left,hands.LeftHandActive,hands.LeftPalmPos,hands.LeftPalmVelocity,hands.LeftStrokeStrength,ref leftClock);
            Emit(right,hands.RightHandActive,hands.RightPalmPos,hands.RightPalmVelocity,hands.RightStrokeStrength,ref rightClock);
        }
        static void Emit(ParticleSystem ps,bool tracked,Vector3 palm,Vector3 velocity,float strength,ref float clock)
        {
            float speed=velocity.magnitude;
            if(!tracked||speed<.12f){clock=0;return;}
            clock+=Time.deltaTime;
            float interval=Mathf.Lerp(.13f,.055f,Mathf.Clamp01(speed/1.4f));
            if(clock<interval)return;clock=0;
            var e=new ParticleSystem.EmitParams {position=palm-velocity.normalized*.035f,
                velocity=velocity*-.12f+Vector3.up*.025f,rotation3D=Quaternion.LookRotation(velocity.normalized).eulerAngles,
                startSize=Mathf.Lerp(.11f,.24f,Mathf.Clamp01(speed)),startLifetime=.65f+strength*.2f,
                startColor=new Color(.52f,.84f,.86f,Mathf.Lerp(.12f,.32f,Mathf.Clamp01(speed)))};
            ps.Emit(e,1);
        }
        void OnDisable(){if(left!=null)left.Clear();if(right!=null)right.Clear();}
        void OnDestroy()
        {
            if(left!=null)Destroy(left.gameObject);if(right!=null)Destroy(right.gameObject);
            if(material!=null)Destroy(material);if(quad!=null)Destroy(quad);
        }
    }
}
