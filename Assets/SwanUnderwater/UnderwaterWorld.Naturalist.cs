using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    public sealed partial class UnderwaterWorld
    {
        // Stable per-map/slot keys keep native assets alive across retries without
        // reconstructing the environment or touching campaign collection/save state.
        readonly Dictionary<int, GameObject> naturalistResidents = new Dictionary<int, GameObject>();
        readonly Dictionary<string, GameObject> residentPrefabs = new Dictionary<string, GameObject>();
        struct ResidentPlan
        {
            public int species, slot;
            public Vector2 offset;
            public float bodyDiagonal, lift, orbit, speed;
            public ResidentPlan(int id, int index, Vector2 at, float size, float height, float radius, float cruise)
            { species=id;slot=index;offset=at;bodyDiagonal=size;lift=height;orbit=radius;speed=cruise; }
        }
        static IEnumerable<ResidentPlan> NaturalistResidentPlan()
        {
            int[] rare = { 12,13,14,7 };
            Vector2[] at = { new Vector2(-44,79),new Vector2(79,3),new Vector2(-65,20),new Vector2(52,-55) };
            for(int i=0;i<rare.Length;i++)
                yield return new ResidentPlan(rare[i],0,at[i],i==0?1.35f:i==1?.78f:i==2?1.25f:.42f,
                    i==1?.45f:1.8f,i==1?.5f:1.5f,i==1?.12f:.32f);
            var zones=OceanCoralSanctuary.ExplorationZones;
            // Two original-textured butterfly fish per district, close to swimming paths.
            for(int i=0;i<zones.Count*2;i++)
                yield return new ResidentPlan(28,i,zones[i/2].offset+new Vector2(i%2==0?-2.2f:2.2f,5f),
                    i%2==0?.38f:.42f,1.25f,1.3f,.18f);
            // Readable silver groups on the entrance and outer waterways, not a single
            // specimen hidden 20 metres away. These retain the source skeletal swim.
            for(int i=0;i<10;i++)
            {
                int zone=i<4?0:1+(i-4)%(zones.Count-1);
                yield return new ResidentPlan(27,i,zones[zone].offset+new Vector2((i%3-1)*1.6f,8f+(i%2)*1.1f),
                    .62f,2.1f,2.7f,.48f);
            }
            yield return new ResidentPlan(29,0,new Vector2(-3,11),1f,2f,3f,.25f);
            yield return new ResidentPlan(30,0,new Vector2(7,17),1.65f,4.6f,6f,.8f);
            yield return new ResidentPlan(30,1,new Vector2(10,21),1.5f,5.2f,6f,.74f);
        }
        public int ActiveNaturalistResidentCount
        {
            get { int count=0;foreach(var pair in naturalistResidents)if(pair.Value!=null&&pair.Value.activeInHierarchy)count++;return count; }
        }
        public IEnumerator PrepareNaturalistHabitats(int level, Func<bool> current)
        {
            if (ActiveEnvironment != OceanEnvironmentKind.CoralSanctuary) yield break;
            foreach(ResidentPlan plan in NaturalistResidentPlan())
            {
                if(!current())yield break;
                int id=plan.species,key=(int)ActiveEnvironment*100000+id*100+plan.slot;
                if (!OceanSpeciesCatalog.IsAvailable(id))
                {
                    if (naturalistResidents.TryGetValue(key, out GameObject retired) && retired != null) retired.SetActive(false);
                    continue;
                }
                var species=OceanSpeciesCatalog.Get(id);
                if(naturalistResidents.TryGetValue(key,out GameObject existing)&&existing!=null)
                { existing.SetActive(level>=species.FirstLevel);continue; }
                if(level<species.FirstLevel)continue;
                if(!residentPrefabs.TryGetValue(species.ResourcePath,out GameObject prefab)||prefab==null)
                {
                    ResourceRequest load=Resources.LoadAsync<GameObject>(species.ResourcePath);
                    while(!load.isDone){if(!current())yield break;yield return null;}
                    if(!current())yield break;
                    prefab=load.asset as GameObject;
                    if(prefab==null){Debug.LogError("[Naturalist] resident missing "+species.ResourcePath);continue;}
                    residentPrefabs[species.ResourcePath]=prefab;
                }
                if(!current())yield break;
                try
                {
                    GameObject model=Instantiate(prefab,ActiveEnvironmentRoot,false);
                    naturalistResidents[key]=model;model.name="Naturalist resident "+id+" #"+plan.slot;
                    bool authored=id==27||id==29||id==30;
                    Bounds navigation=default;
                    if(authored)navigation=NormalizeAuthoredResident(model,plan.bodyDiagonal,species.ResourcePath+"-geometry");
                    else if(id==28)
                    {
                        // The admitted butterfly has its own transparent-fin PBR/swim
                        // shader; replacing it with the generic GLB shader loses its fins.
                        var data=Resources.Load<TextAsset>(species.ResourcePath+"-geometry");
                        var geometry=data==null?null:JsonUtility.FromJson<OceanAuthoredFishGeometry>(data.text);
                        if(geometry==null||!geometry.IsValid)throw new InvalidOperationException("Butterfly geometry admission missing.");
                        navigation=geometry.NavigationBounds;
                        model.transform.localScale*=plan.bodyDiagonal/geometry.firstPoseDiagonal;
                    }
                    else
                    {
                        AlignSpeciesModel(model,prefab.name);NormalizeFishScale(model,plan.bodyDiagonal);
                        ApplyFishSwimShader(model,prefab.name);
                    }
                    foreach(Collider collider in model.GetComponentsInChildren<Collider>(true)){collider.enabled=false;Destroy(collider);}
                    foreach(Animator animator in model.GetComponentsInChildren<Animator>(true))
                    {
                        animator.applyRootMotion=false;animator.enabled=authored;
                        if(authored&&animator.runtimeAnimatorController==null)throw new InvalidOperationException("Native swim controller missing.");
                    }
                    Vector3 preferred=ActiveSpawn+new Vector3(plan.offset.x,0,plan.offset.y);
                    Vector3 route=ClosestSanctuaryRoutePoint(preferred),lateral=preferred-route;lateral.y=0;
                    if(lateral.sqrMagnitude>25f)preferred=route+lateral.normalized*5f;
                    model.transform.position=NaturalistWaterPoint(preferred,plan.lift,plan.bodyDiagonal*.2f);
                    model.AddComponent<OceanNaturalistTag>().Initialize(id);
                    var resident=model.AddComponent<OceanWildlifeResident>();
                    resident.Initialize(this,plan.bodyDiagonal,plan.orbit,id*37+plan.slot,authored);
                    resident.ConfigureCruise(plan.speed,.18f,id==13?.015f:.055f);
                    if(authored)resident.ConfigureNativeBounds(navigation);
                    else if(id==28)model.GetComponent<Renderer>().localBounds=navigation;
                }
                catch(Exception e)
                {
                    if(naturalistResidents.TryGetValue(key,out GameObject failed)&&failed!=null)Destroy(failed);
                    naturalistResidents.Remove(key);Debug.LogError("[Naturalist] resident omitted: "+id+" "+e);
                }
                yield return null;
            }
            Debug.Log("[Naturalist] visible resident instances="+ActiveNaturalistResidentCount+"; original skeletal swims for mackerel, turtle, dolphin");
        }
        static Bounds NormalizeAuthoredResident(GameObject model, float length, string geometryPath)
        {
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var animator = model.GetComponentInChildren<Animator>(true);
            var asset = Resources.Load<TextAsset>(geometryPath);
            var geometry = asset == null ? null : JsonUtility.FromJson<OceanAuthoredFishGeometry>(asset.text);
            if (skin == null || animator == null || geometry == null || !geometry.IsValid)
                throw new InvalidOperationException("Authored resident requires its admitted skin and Animator.");
            // An FBX Animator can expose its full imported automatic bounds on the
            // instantiation frame. That box is not the fish's physical body size.
            skin.updateWhenOffscreen = false;
            animator.Rebind(); animator.Update(0f);
            var snapshot = new Mesh();
            try
            {
                float before = AuthoredBodyDiagonal(skin, snapshot);
                if (!(before > .001f) || float.IsInfinity(before))
                    throw new InvalidOperationException("Authored resident has invalid posed geometry.");
                model.transform.localScale *= length / before;
                animator.Update(0f);
                float actual = AuthoredBodyDiagonal(skin, snapshot);
                if (!(Mathf.Abs(actual - length) <= length * .02f))
                    throw new InvalidOperationException("Authored resident uniform normalization did not match its actual skin.");
                skin.bounds = OceanAuthoredFishGeometry.TransformBounds(geometry.NavigationBounds, model.transform.localToWorldMatrix);
                Debug.Log("[Naturalist] authored body normalized=" + actual.ToString("F4") + "m; one-time CPU skin sample, no per-frame baking");
                return geometry.NavigationBounds;
            }
            finally { Destroy(snapshot); }
        }
        static float AuthoredBodyDiagonal(SkinnedMeshRenderer skin, Mesh snapshot)
        {
            // false already includes hierarchy scale; the rigid matrix applies
            // position and rotation without multiplying that scale a second time.
            skin.BakeMesh(snapshot, false);
            Vector3[] vertices = snapshot.vertices;
            if (vertices.Length == 0) return 0f;
            Matrix4x4 rigid = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            var bounds = new Bounds(rigid.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(rigid.MultiplyPoint3x4(vertices[i]));
            return bounds.size.magnitude;
        }
        Vector3 NaturalistWaterPoint(Vector3 preferred, float lift, float clearance)
        {
            var solids = ActiveSolidBounds;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                float angle = attempt * 2.39996f, r = attempt == 0 ? 0 : .7f + attempt * .35f;
                Vector3 p = preferred + new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
                p.y = SampleRenderedSeabedHeight(p.x, p.z) + Mathf.Max(lift, clearance + .15f);
                bool clear = true;
                for (int i = 0; i < solids.Count; i++)
                { Bounds b = solids[i]; b.Expand((clearance + .7f) * 2); if (b.Contains(p)) { clear = false; break; } }
                if (clear) return p;
            }
            throw new InvalidOperationException("No clear resident water near " + preferred);
        }
        public void GatherNaturalistAnimals(List<GameObject> result)
        {
            result.Clear();
            if (fishRoot != null) foreach (Transform child in fishRoot) result.Add(child.gameObject);
            if (largeCreatureRoot != null) foreach (Transform child in largeCreatureRoot) result.Add(child.gameObject);
        }
    }
}
