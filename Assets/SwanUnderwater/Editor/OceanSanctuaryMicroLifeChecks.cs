#if UNITY_EDITOR
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Run only in the isolated Editor validation, after shared capture is released.</summary>
    public static class OceanSanctuaryMicroLifeChecks
    {
        static int assertions;
        static void Check(bool value,string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("OceanSanctuaryMicroLifeChecks: " + message);
        }

        public static int RunAll()
        {
            assertions = 0;
            Vector3 reference=OceanSanctuaryMicroLife.SuspendedCurrentVelocity(Vector3.zero,0);
            for(int time=0;time<=120;time++)
            {
                foreach(Vector3 anchor in new[]{Vector3.zero,new Vector3(-65,2,20),new Vector3(79,2,3)})
                {
                    Vector3 flow=OceanSanctuaryMicroLife.SuspendedCurrentVelocity(anchor,time*.25f);
                    Check(flow.magnitude>=.045f&&flow.magnitude<=.112f,"suspended flow remains bounded in metres per second");
                    Check(Vector3.Dot(flow,new Vector3(.84f,0,.543f))>.05f,
                        "slow particles share the existing grass stream axis without abrupt reversal");
                    Check((flow-OceanSanctuaryMicroLife.SuspendedCurrentVelocity(anchor,time*.25f+.25f)).magnitude<.009f,
                        "four-Hz current refresh is gradual between native simulation steps");
                }
            }
            Check((reference-OceanSanctuaryMicroLife.SuspendedCurrentVelocity(Vector3.zero,2f)).magnitude>.005f,
                "a real low-frequency surge replaces constant straight-line particle drift");
            var parent = new GameObject("Isolated sanctuary micro-life validation");
            var observer = new GameObject("Isolated micro-life observer");
            Material borrowed = null;
            try
            {
                Shader shader = Resources.Load<Shader>("Shaders/BubbleParticle");
                Check(shader != null,"existing XR-capable depth-tested particle shader is present");
                borrowed = new Material(shader);
                borrowed.SetTexture("_BaseMap",Resources.Load<Texture2D>("Bubbles/bubble_ring_glowCircle_512"));
                Vector3 spawn = new Vector3(13,0,-7);
                observer.transform.position = spawn+new Vector3(0,1.6f,31);
                var sources = new[] {
                    spawn+new Vector3(3,.4f,31), spawn+new Vector3(-12,.5f,-62), spawn+new Vector3(79,.6f,5)
                };
                OceanSanctuaryMicroLife life = OceanSanctuaryMicroLife.Build(parent.transform,observer.transform,
                    (x,z) => .002f*x,spawn,sources,borrowed);
                Check(life != null && life.RegionCount == 7 && life.VentCount == 3,"seven fixed passages and three existing sources reused");
                Check(OceanSanctuaryMicroLife.Build(parent.transform,observer.transform,(x,z) => 0,spawn,sources,borrowed) == life,
                    "repeated build does not duplicate systems/material owners");
                ParticleSystem[] systems = life.GetComponentsInChildren<ParticleSystem>(true);
                var anchors = new Vector3[systems.Length];
                Check(systems.Length == 10,"ten authored systems remain within construction budget");
                Check(life.GetComponentsInChildren<Collider>(true).Length == 0,"discovery passages retain all movement/reach clearance");
                for (int i=0; i<systems.Length; i++)
                {
                    anchors[i] = systems[i].transform.position;
                    Check(systems[i].main.simulationSpace == ParticleSystemSimulationSpace.World,"particles drift in world space");
                    Check(!systems[i].main.prewarm && !systems[i].main.playOnAwake,"no scene-load simulation spike or unbounded automatic start");
                    Check(systems[i].main.maxParticles <= OceanSanctuaryMicroLife.ParticlesPerRegion,"per-system particles bounded");
                }
                Check(life.ActiveRendererCount > 0 && life.ActiveRendererCount <= OceanSanctuaryMicroLife.MaximumActiveRenderers,
                    "near passage has active content within renderer ceiling");
                Check(life.ActiveParticleBudget <= OceanSanctuaryMicroLife.MaximumActiveParticles,"simultaneous particle budget bounded");
                var alternate=new GameObject("Isolated art observer");alternate.transform.SetParent(parent.transform,false);
                alternate.transform.position=new Vector3(1000,40,-1000);
                Transform original=life.ObserverForValidation;
                try
                {
                    life.SetObserverForValidation(alternate.transform);
                    Check(life.ObserverForValidation==alternate.transform&&life.ActiveRendererCount==0,
                        "art observer swaps only proximity selection and clears distant simulation");
                    for(int i=0;i<systems.Length;i++)Check(systems[i].transform.position==anchors[i],
                        "art observer cannot recenter fixed world anchors");
                }
                finally {life.SetObserverForValidation(original);}
                Check(life.ObserverForValidation==observer.transform&&life.ActiveRendererCount>0,
                    "art capture restores the actual viewer and bounded nearby patches");
                observer.transform.SetPositionAndRotation(new Vector3(1000,40,-1000),Quaternion.Euler(0,145,0));
                life.RefreshVisibility();
                Check(life.ActiveRendererCount == 0 && life.ActiveParticleBudget == 0,"remote content stops instead of filling the full distant view");
                for (int i=0; i<systems.Length; i++)
                    Check(systems[i].transform.position == anchors[i],"observer movement/rotation cannot move patch anchors");
                observer.transform.position = spawn+new Vector3(0,1.6f,31);
                life.RefreshVisibility();
                Check(life.ActiveRendererCount > 0,"returning to the passage reuses existing local content");
                life.enabled = false;
                Check(life.ActiveRendererCount == 0,"disable clears local simulation/renderers");
                life.enabled = true;
                life.RefreshVisibility();
                Check(life.ActiveRendererCount > 0,"reenable restores proximity selection");
                Object.DestroyImmediate(life.gameObject);
                Check(borrowed != null,"cleanup never destroys caller-owned bubble material");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(observer);
                if (borrowed != null) Object.DestroyImmediate(borrowed);
            }
            Debug.Log("OceanSanctuaryMicroLifeChecks PASS " + assertions +
                " assertions; lifecycle and fixed-world budgets only, visual/device performance pending.");
            return assertions;
        }
    }
}
#endif
