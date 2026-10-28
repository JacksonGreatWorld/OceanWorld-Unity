#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater.EditorTests
{
    /// <summary>
    /// Inventory and geometry checks for the production habitat. These inspect real meshes,
    /// not authoring estimates; visual quality, animation and device cost need rendered review.
    /// </summary>
    public static class OceanHabitatLayeringChecks
    {
        static int assertions;
        static void Check(bool condition,string message)
        { assertions++;if(!condition)throw new InvalidOperationException("Habitat layering: "+message); }

        public static int RunAll(UnderwaterWorld world)
        {
            assertions=0;
            Check(world!=null&&world.IsReady&&world.ActiveEnvironment==OceanEnvironmentKind.CoralSanctuary,
                "ready sanctuary required");
            CheckDistantRelief(world);
            CheckRestingLeaves(world);
            CheckPigmentDistribution(world);
            Debug.Log("OceanHabitatLayeringChecks PASS "+assertions+
                " assertions; actual fixed relief/opaque material/leaf envelope/region pigment checks. "+
                "Not a headset frame-time, visual-quality or comfort verdict.");
            return assertions;
        }

        static void CheckDistantRelief(UnderwaterWorld world)
        {
            Transform root=world.ActiveReefRoot.Find("Fixed distant underwater relief");
            Check(root!=null&&root.childCount==29,"existing 29 fixed relief formations retained");
            Check(root.GetComponentsInChildren<Collider>(true).Length==0,"distant relief adds no collision boundary");
            var materials=new HashSet<Material>();int triangles=0;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();
                Check(mesh!=null&&mesh.vertexCount==865&&mesh.triangles.Length/3==1656,
                    "each relief retains its original vertex/triangle budget");
                Check(renderer.sharedMaterials.Length==1&&renderer.sharedMaterial.renderQueue<2500&&
                    renderer.shadowCastingMode==ShadowCastingMode.Off&&!renderer.receiveShadows,
                    "one opaque relief draw, no new shadow work");
                materials.Add(renderer.sharedMaterial);triangles+=mesh.triangles.Length/3;
                foreach(Vector3 local in mesh.vertices)
                {
                    Check(Finite(local),"finite relief vertex");
                    Vector3 p=filter.transform.TransformPoint(local)-world.ActiveSpawn;
                    float x=Mathf.Max(0,Mathf.Abs(p.x)-110f),z=Mathf.Max(0,Mathf.Abs(p.z)-110f);
                    Check(x*x+z*z>3600f,"actual relief remains beyond the reachable map");
                }
            }
            Check(triangles==48024&&triangles==OceanSanctuaryVista.TriangleCount&&materials.Count==3,
                "complete relief retains 48024 triangles and three shared materials");
        }

        static void CheckRestingLeaves(UnderwaterWorld world)
        {
            var meadow=world.ActiveEnvironmentRoot.GetComponentInChildren<SeagrassMeadow>();
            Check(meadow!=null&&meadow.UsesSanctuaryCurrent,"sanctuary uses anchored GPU-current meadow");
            Check(meadow.GetComponentsInChildren<Collider>(true).Length==0,"curved leaves cannot obstruct swimming");
            int leaves=0,curved=0;float minimumLean=float.PositiveInfinity,maximumLean=0;
            foreach(var group in meadow.GetComponentsInChildren<LODGroup>(true))
            {
                var levels=group.GetLODs();
                Check(levels.Length==2,"existing two-level meadow retained");
                Mesh mesh=levels[0].renderers[0].GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices=mesh.vertices;Vector2[] motion=mesh.uv2;
                Check(vertices.Length%11==0,"five segmented sections plus one tip per near leaf");
                for(int i=0;i<vertices.Length;i+=11)
                {
                    Vector3 root=(vertices[i]+vertices[i+1])*.5f,tip=vertices[i+10];
                    float height=motion[i].x;
                    Vector3 horizontal=tip-root;horizontal.y=0;
                    float lean=horizontal.magnitude/height;
                    minimumLean=Mathf.Min(minimumLean,lean);maximumLean=Mathf.Max(maximumLean,lean);
                    Check(Finite(tip)&&lean>=.159f&&lean<=.421f,
                        "curved leaf stays within the bounded horizontal rest envelope");
                    Vector3 middle=(vertices[i+6]+vertices[i+7])*.5f;
                    if(Vector3.Distance(middle,Vector3.Lerp(root,tip,.6f))>height*.025f)curved++;
                    leaves++;
                }
            }
            Check(leaves==meadow.TuftCount*3&&curved>leaves*.95f,"retained three-leaf tufts use curved resting geometry");
            Check(maximumLean-minimumLean>.2f,"rest poses include different inclinations rather than identical cards");
            Debug.Log($"[HabitatLeaves] leaves={leaves}, curved={curved}, lean/height={minimumLean:F3}..{maximumLean:F3}");
        }

        static void CheckPigmentDistribution(UnderwaterWorld world)
        {
            var colors=new Dictionary<int,Color[]>();
            foreach(var filter in world.ActiveReefRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.name.StartsWith("Attached coral mosaic ",StringComparison.Ordinal))continue;
                int seed=int.Parse(filter.name.Substring(filter.name.LastIndexOf(' ')+1));
                colors.Add(seed,filter.sharedMesh.colors);
            }
            var sums=new Vector3[OceanCoralSanctuary.ExplorationZones.Count];var counts=new int[sums.Length];
            foreach(var colony in OceanCoralSanctuary.SurfaceColonies)
            {
                Check(colors.TryGetValue(colony.hostSeed,out Color[] meshColors)&&colony.vertexCount>0,
                    "surface colony resolves to actual vertex pigments");
                Vector3 pigment=Vector3.zero;
                for(int i=colony.firstVertex;i<colony.firstVertex+colony.vertexCount;i++)
                { Color c=meshColors[i];pigment+=new Vector3(c.r,c.g,c.b); }
                pigment/=colony.vertexCount;
                Vector2 p=new Vector2(colony.root.x-world.ActiveSpawn.x,colony.root.z-world.ActiveSpawn.z);
                int zone=0;float best=float.PositiveInfinity;
                for(int i=0;i<sums.Length;i++)
                { float d=(p-OceanCoralSanctuary.ExplorationZones[i].offset).sqrMagnitude;if(d<best){best=d;zone=i;} }
                sums[zone]+=pigment;counts[zone]++;
            }
            float difference=0;int populated=0;
            for(int i=0;i<sums.Length;i++)
            {
                Check(counts[i]>10,"each authored region retains surface growth");
                sums[i]/=Mathf.Max(1,counts[i]);populated++;
                for(int j=0;j<i;j++)difference=Mathf.Max(difference,Vector3.Distance(sums[i],sums[j]));
            }
            Check(populated==7&&difference>.03f,"regional pigment variation exists in actual rendered vertex data");
            Debug.Log($"[HabitatPigment] seven-region colonies={string.Join("/",counts)}, maximum mean RGB distance={difference:F3}");
        }

        static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsInfinity(value.x)&&
            !float.IsNaN(value.y)&&!float.IsInfinity(value.y)&&!float.IsNaN(value.z)&&!float.IsInfinity(value.z);
    }
}
#endif
