#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual admitted native geometry and production obstacle caching; no physics collider is added.</summary>
    public static class OceanNativeResidentClearanceChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static int Assertions { get; private set; }
        public static void RunInScene(UnderwaterWorld world)
        {
            Assertions=0;
            Check(world!=null&&world.IsReady,"actual ready environment required");
            var solids=world.ActiveSolidBounds as IList<Bounds>;
            Check(solids!=null&&!solids.IsReadOnly,"mutable scene solid cache required for isolated synchronous fixture");
            var saved=new List<Bounds>(solids);
            int originalColliders=Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
            try
            {
                foreach(string resource in new[]{"mackerel","sea-turtle","dolphin"})
                {
                    string path=resource=="mackerel"?OceanSpeciesCatalog.Get(27).ResourcePath:"MarineExpansion/"+resource;
                    GameObject prefab=Resources.Load<GameObject>(path);var json=Resources.Load<TextAsset>(path+"-geometry");
                    Check(prefab!=null&&json!=null,"actual admitted native source and geometry: "+resource);
                    var profile=JsonUtility.FromJson<OceanAuthoredFishGeometry>(json.text);
                    Check(profile!=null&&profile.IsValid,"valid native envelope: "+resource);
                    GameObject model=Object.Instantiate(prefab);
                    try
                    {
                        Check(model.GetComponentsInChildren<Collider>(true).Length==0,"native display does not block player movement");
                        float diagonal=resource=="dolphin"?1.65f:resource=="sea-turtle"?1f:.62f;
                        model.transform.localScale=Vector3.one*(diagonal/profile.firstPoseDiagonal);
                        Vector3 at=world.ActiveSpawn;at.y=UnderwaterWorld.SampleRenderedSeabedHeight(at.x,at.z)+40f;
                        model.transform.SetPositionAndRotation(at,Quaternion.identity);
                        solids.Clear();
                        var resident=model.AddComponent<OceanWildlifeResident>();resident.enabled=false;
                        resident.Initialize(world,diagonal,6f,3,true);
                        Check(resident.CachedSolidCount==0,"initial empty local cache");
                        // Metadata corners are independent geometry evidence for the actual
                        // navigation sphere, including offset centres and hierarchy scale.
                        resident.ConfigureNativeBounds(profile.NavigationBounds);
                        float clearance=(float)typeof(OceanWildlifeResident).GetField("clearance",Private).GetValue(resident);
                        Bounds envelope=profile.NavigationBounds;
                        for(int corner=0;corner<8;corner++)
                        {
                            Vector3 p=new Vector3((corner&1)==0?envelope.min.x:envelope.max.x,
                                (corner&2)==0?envelope.min.y:envelope.max.y,(corner&4)==0?envelope.min.z:envelope.max.z);
                            Check(Vector3.Distance(model.transform.TransformPoint(p),model.transform.position)<=clearance+.00001f,
                                "every real native envelope corner fits clearance: "+resource);
                        }
                        Check(clearance>diagonal*.45f,"native envelope replaces inadequate .18-length clearance");
                        // Add an actual raw box AFTER initialization. ConfigureNativeBounds
                        // must rebuild the cache with its larger envelope, not retain an old .18 box.
                        var wall=new Bounds(at+Vector3.forward*(clearance*.75f+.05f),Vector3.one*.10f);
                        solids.Add(wall);resident.ConfigureNativeBounds(profile.NavigationBounds);
                        Check(resident.CachedSolidCount==1,"native configuration rebuilds local obstacle cache");
                        Check(!Clear(resident,at),"body-overlapping box rejected while navigation point stays outside raw box");
                        Check(!wall.Contains(at),"fixture is a body-envelope regression, not point-inside-box");
                        solids.Clear();solids.Add(new Bounds(at+Vector3.forward*100f,Vector3.one));resident.ConfigureNativeBounds(profile.NavigationBounds);
                        Check(resident.CachedSolidCount==0,"distant reefs omitted from per-frame cache");
                        Check(Clear(resident,at+Vector3.forward*.1f),"clear water remains traversable; no permanent stop workaround");
                    }
                    finally { Object.DestroyImmediate(model); }
                }
                Check(Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==originalColliders,
                    "no gameplay/physics collider added to the player scene");
            }
            finally {solids.Clear();foreach(Bounds box in saved)solids.Add(box);}
            Debug.Log("OceanNativeResidentClearanceChecks PASS "+Assertions+" assertions; native metadata/scale, obstacle cache refresh and no added player collision. Synthetic obstacle, no wearer acceptance.");
        }
        static bool Clear(OceanWildlifeResident resident,Vector3 point)=>(bool)typeof(OceanWildlifeResident).GetMethod("IsClear",Private).Invoke(resident,new object[]{point});
        static void Check(bool ok,string message){Assertions++;if(!ok)throw new InvalidOperationException("Native clearance: "+message);}
    }
}
#endif
