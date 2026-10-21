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
