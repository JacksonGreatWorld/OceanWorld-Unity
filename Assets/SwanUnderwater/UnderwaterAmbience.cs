using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Two small shared meshes supply world-space shafts and layered distant water.</summary>
    public sealed class UnderwaterAmbience : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        public static void Build(Transform parent, Vector3 spawn, bool sanctuary = false)
        {
            var go = new GameObject("Sun Shafts and Deep Water");
            go.transform.SetParent(parent, false);
            go.AddComponent<UnderwaterAmbience>().Create(spawn, sanctuary);
        }
        void Create(Vector3 spawn, bool sanctuary)
        {
            var shader = Shader.Find("SwanUnderwater/WaterVolume");
            if (shader == null) { Debug.LogError("[Water] Missing volume shader"); return; }
            var light = new Material(shader) { name = "Filtered sunlight shafts" };
            light.SetColor("_BaseColor", sanctuary ? new Color(.67f,.86f,1f,.082f) : new Color(0.63f,0.87f,0.82f,0.095f));
            light.SetFloat("_StructuredLight", sanctuary ? 1f : 0f);
            owned.Add(light);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var colors = new List<Color>(); var tri = new List<int>();
            var rng = new System.Random(42019);
            int shaftCount = sanctuary ? 33 : 22;
            for(int i=0;i<shaftCount;i++)
            {
                float a = i*2.39996f, r = 7+(float)rng.NextDouble()*34;
                Vector3 bottom = sanctuary
                    ? spawn + new Vector3(-18f + (float)rng.NextDouble()*36f, -2.5f, 6f + (float)rng.NextDouble()*41f)
                    : spawn+new Vector3(Mathf.Cos(a)*r,-2.1f,Mathf.Sin(a)*r);
                if(sanctuary&&i>=15)
                {
                    Vector2 zone=OceanCoralSanctuary.ExplorationZones[1+(i-15)%6].offset;
                    bottom=spawn+new Vector3(zone.x+(float)rng.NextDouble()*22f-11f,0,
                        zone.y+(float)rng.NextDouble()*22f-11f);
                    bottom.y=UnderwaterWorld.SampleRenderedSeabedHeight(bottom.x,bottom.z)+.15f;
                }
                Vector3 top=bottom+(sanctuary ? new Vector3(-10.5f,20f,-5.7f) : new Vector3(-7,19,5));
                float width = sanctuary ? .7f+(float)rng.NextDouble()*1.35f : .5f+(float)rng.NextDouble()*1.1f;
                // Crossed, soft-edged cards remain visible when swimming around a shaft.
                for(int j=0;j<2;j++)
                {
                    Vector3 side=j==0?Vector3.right:Vector3.forward;
                    AddQuad(v,uv,colors,tri,bottom-side*width*2,bottom+side*width*2,
                        top-side*width*(sanctuary?.8f:.4f),top+side*width*(sanctuary?.8f:.4f),new Color(i/(float)shaftCount,1,1,0.7f));
                }
            }
            MakeMesh("Sunlight in suspended water",v,uv,colors,tri,light);
            if (sanctuary)
            {
                // Layered opaque reef silhouettes and continuous distance fog provide
                // depth. Avoid repeated translucent walls around the new environment.
                Debug.Log("[Water] Sanctuary: 66 fixed crossed shafts across seven zones, continuous blue fog, no distant mist walls");
                return;
            }
            var mist = new Material(shader) { name = "Distant suspended water" };
            mist.SetColor("_BaseColor", new Color(0.032f,0.19f,0.235f,0.46f));
            mist.SetFloat("_Mist",1);
            owned.Add(mist);
            v.Clear();uv.Clear();colors.Clear();tri.Clear();
            for(int layer=0;layer<3;layer++) for(int i=0;i<14;i++)
            {
                float a=(i+layer*0.37f)*Mathf.PI*2/14;
                float r=64+layer*39;
                Vector3 center=spawn+new Vector3(Mathf.Sin(a)*r,3,Mathf.Cos(a)*r);
                Vector3 side=new Vector3(Mathf.Cos(a),0,-Mathf.Sin(a))*r*0.36f;
                AddQuad(v,uv,colors,tri,center-side-Vector3.up*18,center+side-Vector3.up*18,
                    center-side+Vector3.up*28,center+side+Vector3.up*28,Color.white);
            }
            MakeMesh("Layers of deep water",v,uv,colors,tri,mist);
            Debug.Log("[Water] 44 crossed ray cards and 42 distant mist cards in 2 shared meshes; fixed world positions");
        }
        static void AddQuad(List<Vector3> v,List<Vector2> uv,List<Color> c,List<int> t,Vector3 bl,Vector3 br,Vector3 tl,Vector3 tr,Color color)
        {
            int n=v.Count;v.Add(bl);v.Add(br);v.Add(tl);v.Add(tr);
            uv.Add(new Vector2(0,0));uv.Add(new Vector2(1,0));uv.Add(new Vector2(0,1));uv.Add(new Vector2(1,1));
            for(int i=0;i<4;i++)c.Add(color);
            t.Add(n);t.Add(n+2);t.Add(n+1);t.Add(n+2);t.Add(n+3);t.Add(n+1);
        }
        void MakeMesh(string label,List<Vector3> v,List<Vector2> uv,List<Color> c,List<int> t,Material material)
        {
            var mesh=new Mesh { name=label };mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetColors(c);mesh.SetTriangles(t,0);mesh.RecalculateBounds();owned.Add(mesh);
            var go=new GameObject(label);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        void OnDestroy(){foreach(var o in owned)if(o!=null)Destroy(o);}
    }
}
