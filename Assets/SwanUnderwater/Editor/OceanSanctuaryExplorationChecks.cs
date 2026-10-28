#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real generated scene checks. Requires a loaded sanctuary; never a substitute for worn tests.</summary>
    public static class OceanSanctuaryExplorationChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition,string message)
        { Assertions++;if(!condition)throw new InvalidOperationException("Sanctuary exploration: "+message); }

        public static void RunAll(UnderwaterWorld world)
        {
            Assertions=0;
            Check(world!=null&&world.IsReady&&world.ActiveEnvironment==OceanEnvironmentKind.CoralSanctuary,"ready sanctuary required");
            CheckNestedShellExclusion();
            OceanSanctuaryExtentTests.RunAll(world);
            var shader=Resources.Load<Shader>("Shaders/OceanSanctuaryVista");
            Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"real vista shader supported without compile errors");
            Transform vista=world.ActiveReefRoot.Find("Fixed distant underwater relief");
            Check(vista!=null&&vista.childCount==29,"all fixed distant formations constructed");
            Check(vista.GetComponentsInChildren<Collider>(true).Length==0,"unreachable vista has no gameplay colliders");
            Check(OceanSanctuaryVista.MinimumPlayableClearance>60f,"vista remains far beyond all four playable sides");
            Check(OceanCoralSanctuary.CoralCount>=300,"expanded banks carry the detailed scanned colonies");
            Check(OceanCoralSanctuary.BubbleSources.Count>=20,"bubbles have homes outside the entrance");
            var hits=new Collider[64];
            CheckRoute(world,OceanCoralSanctuary.ExplorationRouteOffsets,hits,"main loop");
            CheckRoute(world,OceanCoralSanctuary.ExplorationShortcutOffsets,hits,"southern exit");
            Transform fish=world.ActiveEnvironmentRoot.Find("Free Swimming Fish");
            // Match CreateFishSchool's population policy, not the legacy JSON count or
            // ReefLife.heroResidents, which counts only fish assigned to reef pockets.
            int heroBudget=OceanResidentDensityPolicy.HeroCount(world.ActiveEnvironment,ReefExperienceSettings.Load().heroFish);
            Check(fish!=null&&fish.childCount==heroBudget,"profile hero budget preserved ("+heroBudget+")");
            var meadow=world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>();
            var shells=world.ActiveEnvironmentRoot.GetComponentInChildren<SeabedShells>();
            Check(meadow!=null&&meadow.TuftCount<=SeagrassMeadow.MaximumTufts,"grass stays within its existing budget");
            Check(shells!=null&&shells.Placements.Count<=SeabedShells.MaximumShells,"decorative shell budget preserved");
            for(int zone=1;zone<OceanCoralSanctuary.ExplorationZones.Count;zone++)
            {
                Vector2 offset=OceanCoralSanctuary.ExplorationZones[zone].offset;
                Vector2 center=new Vector2(world.ActiveSpawn.x,world.ActiveSpawn.z)+offset;
                int population=0,grass=0,shell=0;
                foreach(Transform animal in fish)
                    if(Vector2.Distance(center,new Vector2(animal.position.x,animal.position.z))<30f)population++;
                foreach(Vector2 root in meadow.RootPositions)if(Vector2.Distance(center,root)<26f)grass++;
                foreach(SeabedShells.Placement p in shells.Placements)
                    if(Vector2.Distance(center,new Vector2(p.center.x,p.center.z))<26f)shell++;
                Check(population>0,"resident hero fish in outer zone "+zone);
                Check(grass>0,"grounded seagrass in outer zone "+zone);
                Check(shell>0,"decorative shells in outer zone "+zone);
            }
            Debug.Log("OceanSanctuaryExplorationChecks PASS "+Assertions+" assertions; actual scene geometry/population and synthetic capsule route checks, not headset performance or tracked-hand acceptance.");
        }

        static void CheckNestedShellExclusion()
        {
            var host=new GameObject("Nested shell exclusion fixture");
            try
            {
                var reef=new GameObject("Coral Sanctuary");reef.transform.SetParent(host.transform,false);
                var bank=new GameObject("Nested bank");bank.transform.SetParent(reef.transform,false);
                var terrace=GameObject.CreatePrimitive(PrimitiveType.Cube);terrace.transform.SetParent(bank.transform,false);
                terrace.transform.localPosition=Vector3.left*4;
                var colony=new GameObject("Sibling coral");colony.transform.SetParent(bank.transform,false);
                var detailed=GameObject.CreatePrimitive(PrimitiveType.Cube);detailed.transform.SetParent(colony.transform,false);
                detailed.transform.localPosition=Vector3.right*4;
                var distant=GameObject.CreatePrimitive(PrimitiveType.Cube);distant.transform.SetParent(colony.transform,false);
                distant.transform.localPosition=Vector3.right*4;distant.transform.localScale=Vector3.one*.2f;
                colony.AddComponent<LODGroup>().SetLODs(new[]{
                    new LOD(.1f,new[]{detailed.GetComponent<Renderer>()}),new LOD(.01f,new[]{distant.GetComponent<Renderer>()})});
                var method=typeof(SeabedShells).GetMethod("ReefBounds",BindingFlags.Static|BindingFlags.NonPublic);
                var bounds=(List<Bounds>)method.Invoke(null,new object[]{host.transform});
                bool detailedExcluded=false,gapExcluded=false;
                foreach(Bounds b in bounds)
                {detailedExcluded|=b.Contains(new Vector3(4.4f,0,0));gapExcluded|=b.Contains(Vector3.zero);}
                Check(bounds.Count==2&&detailedExcluded,"nested sibling coral uses LOD0 extent without duplicate distant LODs");
                Check(!gapExcluded,"open sand between siblings is not replaced with a combined bank AABB");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        static void CheckRoute(UnderwaterWorld world,IReadOnlyList<Vector2> route,Collider[] hits,string label)
        {
            for(int segment=0;segment+1<route.Count;segment++)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(route[segment],route[segment+1])/1.25f));
                for(int step=0;step<=count;step++)
                {
                    Vector2 p=Vector2.Lerp(route[segment],route[segment+1],step/(float)count);
                    float x=world.ActiveSpawn.x+p.x,z=world.ActiveSpawn.z+p.y;
                    float floor=UnderwaterWorld.SampleRenderedSeabedHeight(x,z);
                    int overlap=Physics.OverlapCapsuleNonAlloc(new Vector3(x,floor+.34f,z),new Vector3(x,floor+1.38f,z),
                        .23f,hits,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
                    Check(overlap<hits.Length,"query buffer sufficient at "+label+" segment "+segment);
                    for(int i=0;i<overlap;i++)
                        Check(!hits[i].transform.IsChildOf(world.ActiveReefRoot),
                            label+" blocked at local "+p+" by "+hits[i].name);
                }
            }
        }
    }
}
#endif
