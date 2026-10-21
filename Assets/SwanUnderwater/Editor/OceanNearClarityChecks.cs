#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Object = UnityEngine.Object;

namespace SwanUnderwater.Editor
{
    /// <summary>Production geometry/detail switching and actual resource checks; no synthetic hand tracking.</summary>
    public static class OceanNearClarityChecks
    {
        [Serializable] sealed class MeshInfo
        { public string name; public int vertices, triangles; public Vector3 size; public float minimumY, minimumAreaSquared; }
        [Serializable] sealed class ShellInfo
        { public int kind; public string name; public MeshInfo standard, detailed; public bool hysteresis, repeatedStable; }
        [Serializable] sealed class TextureInfo
        { public string resource, material, property, texture, format, filter; public int width, height, mipmaps, anisotropy; }
        [Serializable] sealed class Report
        {
            public bool passed; public int assertions, leasesBefore, leasesAfter, globalMipmapLimit;
            public string scope = "Actual Unity production shell factories/detail switching/resource leases and loaded Fish textures. Synthetic distances and temporary photo arrangement, real materials and scene lighting. No live hand, headset acuity or device performance evidence.";
            public List<ShellInfo> shells = new List<ShellInfo>();
            public List<TextureInfo> textures = new List<TextureInfo>();
            public List<string> checks = new List<string>(), images = new List<string>();
        }
        static readonly FieldInfo Leases = typeof(CollectibleVisuals).GetField("leases", BindingFlags.Static | BindingFlags.NonPublic);

        public static void ValidateInScene(Camera head, string output)
        {
            if (head == null || OceanEntry.BlocksGameplay) throw new InvalidOperationException("Near detail validation requires the exposed world.");
            var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            if (XRSettings.isDeviceActive || displays.Exists(x => x.running)) throw new InvalidOperationException("Editor clarity validation refuses active XR.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Clarity screenshots require graphics.");
            if (Leases == null) throw new MissingFieldException("Collectible resource lease diagnostic changed.");
            Directory.CreateDirectory(output);
            var report = new Report { leasesBefore = (int)Leases.GetValue(null), globalMipmapLimit = QualitySettings.globalTextureMipmapLimit };
            void Check(bool value, string description)
            { report.assertions++; report.checks.Add(description + ": " + value); if (!value) throw new InvalidOperationException(description); }
            var temporary = new GameObject("Temporary near-clarity validation gallery");
            var shells = new GameObject[10]; GameObject coin = null, fish = null;
            Camera camera = null;
            try
            {
                Check(report.globalMipmapLimit == 0, "Global texture mipmap limit is zero");
                // Inspect before any factory that might call PrepareModelTextures: this must observe
                // the world's real preload configuration, not silently fix the resource under test.
                InspectFishTextures(report, Check);
                Material shared = null;
                for (int kind = 0; kind < shells.Length; kind++)
                {
                    GameObject shell = shells[kind] = CollectibleVisuals.CreateShell(temporary.transform, kind);
                    var instance = shell.GetComponent<CollectibleVisualInstance>();
                    MeshFilter filter = shell.GetComponent<MeshFilter>(); Renderer renderer = shell.GetComponent<Renderer>();
                    Mesh low = filter.sharedMesh, high = instance.DetailMeshForValidation;
                    Check(high != null && high != low, "Separate real high detail mesh for species " + kind);
                    var row = new ShellInfo { kind = kind, name = CollectibleVisuals.ShellNames[kind],
                        standard = InspectMesh(low, Check), detailed = InspectMesh(high, Check) };
                    report.shells.Add(row);
                    Check(high.vertexCount > low.vertexCount && high.GetIndexCount(0) > low.GetIndexCount(0), "Near mesh has finer geometry for species " + kind);
                    float lowWidth = Mathf.Max(low.bounds.size.x, low.bounds.size.z), highWidth = Mathf.Max(high.bounds.size.x, high.bounds.size.z);
                    Check(Mathf.Abs(lowWidth-highWidth) < .001f && Vector3.Distance(low.bounds.size, high.bounds.size) < .03f,
                        "Detail change preserves shell footprint and body dimensions for species " + kind);
                    if (shared == null) shared = renderer.sharedMaterial;
                    Check(renderer.sharedMaterial == shared, "Shells retain their shared production material");
                    instance.UpdateDetailForValidation(Outside(renderer, 2.70f));
                    Check(!instance.UsesDetailForValidation && filter.sharedMesh == low, "Far shell remains standard inside hysteresis band");
                    instance.UpdateDetailForValidation(Outside(renderer, 2.499f));
                    Check(instance.UsesDetailForValidation && filter.sharedMesh == high, "Within 2.5 metres enters detail");
                    instance.UpdateDetailForValidation(Outside(renderer, 2.70f));
                    Check(instance.UsesDetailForValidation, "Detailed shell remains detailed in hysteresis band");
                    instance.UpdateDetailForValidation(Outside(renderer, 3.001f));
                    Check(!instance.UsesDetailForValidation && filter.sharedMesh == low, "Beyond 3 metres returns to standard");
                    row.hysteresis = true;
                    for (int pass = 0; pass < 12; pass++)
                    {
                        instance.UpdateDetailForValidation(Outside(renderer, .20f));
                        Check(instance.UsesDetailForValidation && filter.sharedMesh == high && renderer.sharedMaterial == shared,
                            "Repeated approach reuses exact detail mesh/material");
                        instance.UpdateDetailForValidation(Outside(renderer, 3.20f));
                        Check(!instance.UsesDetailForValidation && filter.sharedMesh == low, "Repeated departure restores exact original mesh");
                    }
                    row.repeatedStable = true;
                }
                coin = CollectibleVisuals.CreateCoin(temporary.transform);
                Mesh coinMesh = coin.GetComponent<MeshFilter>().sharedMesh;
                var coinInstance = coin.GetComponent<CollectibleVisualInstance>();
                Check(coinInstance.DetailMeshForValidation == null && coinMesh.name == "Ocean Coin", "World coin keeps the stable non-shell resource slot");
                foreach (float distance in new[] { 0f, 2.49f, 2.7f, 3.01f, 0f })
                {
                    coinInstance.UpdateDetailForValidation(Outside(coin.GetComponent<Renderer>(), distance));
                    Check(!coinInstance.UsesDetailForValidation && coin.GetComponent<MeshFilter>().sharedMesh == coinMesh, "Coin never switches mesh on approach");
                }
                Check((int)Leases.GetValue(null) == report.leasesBefore + 11, "Ten shells and one coin acquire exactly eleven leases");
                CollectibleVisuals.AcquireCoinResources(out Mesh particle, out Material particleMaterial);
                try
                {
                    Check(particle != null && particle.name == "Ocean Coin Particle" && particleMaterial == shared,
                        "Reward coin retains distinct mesh and common material");
                    Check((int)Leases.GetValue(null) == report.leasesBefore + 12, "Batch coin acquires one additional lease");
                }
                finally { CollectibleVisuals.ReleaseSharedResources(); }

                camera = CreateCamera(head, temporary.transform);
                Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 centre = head.transform.position + forward * .8f - Vector3.up * .28f;
                for (int i = 0; i < shells.Length; i++)
                {
                    shells[i].transform.SetPositionAndRotation(centre + right * ((i % 5 - 2) * .31f) + forward * ((i / 5 - .5f) * .36f),
                        Quaternion.LookRotation(forward, Vector3.up));
                    shells[i].GetComponent<CollectibleVisualInstance>().UpdateDetailForValidation(shells[i].GetComponent<Renderer>().bounds.center);
                }
                coin.SetActive(false);
                camera.transform.SetPositionAndRotation(centre + Vector3.up * 1.0f - forward * .62f,
                    Quaternion.LookRotation(forward * .62f - Vector3.up, Vector3.up));
                Capture(camera, Path.Combine(output,"shells-10-near-detail.png")); report.images.Add("shells-10-near-detail.png");
                foreach (var shell in shells) shell.SetActive(false);

                var world = Object.FindFirstObjectByType<UnderwaterWorld>();
                Check(world != null && world.IsReady, "Fish close-up uses the completed world's real factory");
                fish = world.CreateCampaignCreature(OceanLevelCatalog.FishPaths[0], temporary.transform, .5f);
                fish.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(right, Vector3.up));
                Bounds fishBounds = CombinedBounds(fish);
                camera.transform.SetPositionAndRotation(fishBounds.center - forward * .42f + Vector3.up * .04f,
                    Quaternion.LookRotation(forward * .42f - Vector3.up * .04f, Vector3.up));
                Capture(camera, Path.Combine(output,"clownfish-real-material-close.png")); report.images.Add("clownfish-real-material-close.png");
            }
            finally
            {
                Object.DestroyImmediate(temporary);
                report.leasesAfter = (int)Leases.GetValue(null);
                bool balanced = report.leasesAfter == report.leasesBefore;
                report.assertions++; report.checks.Add("Temporary validation objects return every acquired lease: " + balanced);
                report.passed = balanced && report.shells.Count == 10 && report.images.Count == 2;
                File.WriteAllText(Path.Combine(output,"near-clarity.json"), JsonUtility.ToJson(report,true));
                if (!balanced) throw new InvalidOperationException("Near-detail validation leaked or over-released shared resources.");
            }
            Debug.Log("NEAR_CLARITY_CHECKS_PASSED assertions=" + report.assertions + " textures=" + report.textures.Count);
        }

        static MeshInfo InspectMesh(Mesh mesh, Action<bool,string> check)
        {
            check(mesh != null && mesh.isReadable && mesh.subMeshCount == 1, "Real shell mesh is readable and single-submesh");
            Vector3[] points = mesh.vertices; int[] indices = mesh.triangles;
            bool finite = points.Length > 0, indicesValid = indices.Length > 0 && indices.Length % 3 == 0;
            float minimumArea = float.PositiveInfinity;
            foreach (var p in points) finite &= Finite(p.x) && Finite(p.y) && Finite(p.z);
            foreach (int index in indices) indicesValid &= index >= 0 && index < points.Length;
            check(finite && indicesValid, "Every real vertex and triangle index is valid: " + mesh.name);
            for (int i = 0; i < indices.Length; i += 3)
                minimumArea = Mathf.Min(minimumArea, Vector3.Cross(points[indices[i+1]]-points[indices[i]], points[indices[i+2]]-points[indices[i]]).sqrMagnitude);
            check(minimumArea > 1e-22f, "No degenerate real triangles: " + mesh.name);
            check(Mathf.Abs(mesh.bounds.min.y) < .00001f && Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.z) >= .20f &&
                Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.z) <= .261f, "Ground pivot and real shell scale remain stable: " + mesh.name);
            return new MeshInfo { name=mesh.name,vertices=points.Length,triangles=indices.Length/3,size=mesh.bounds.size,
                minimumY=mesh.bounds.min.y,minimumAreaSquared=minimumArea };
        }

        static void InspectFishTextures(Report report, Action<bool,string> check)
        {
            for (int species=0; species<4; species++)
            {
                string resource=OceanLevelCatalog.FishPaths[species]; var prefab=Resources.Load<GameObject>(resource);
                check(prefab!=null,"Actual full-resolution fish resource exists: "+resource);
                int count=0; var seen=new HashSet<int>();
                foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                    {
                        if(material==null)continue;
                        foreach(string property in material.GetTexturePropertyNames())
                        {
                            var texture=material.GetTexture(property) as Texture2D;
                            if(texture==null||!seen.Add(texture.GetInstanceID()))continue;
                            count++;
                            report.textures.Add(new TextureInfo {resource=resource,material=material.name,property=property,texture=texture.name,
                                width=texture.width,height=texture.height,mipmaps=texture.mipmapCount,anisotropy=texture.anisoLevel,
                                filter=texture.filterMode.ToString(),format=texture.format.ToString()});
                            check(texture.width>=512&&texture.height>=512&&texture.mipmapCount>1,
                                "Near fish keeps >=512px source texture and a mip chain: "+resource+"/"+property);
                            check(texture.filterMode==FilterMode.Trilinear&&texture.anisoLevel==16,
                                "Loaded fish texture already uses trilinear/aniso16: "+resource+"/"+property);
                        }
                    }
                check(count>0,"Fish inspection found actual material textures: "+resource);
            }
        }
        static Vector3 Outside(Renderer renderer,float distance)
        { Bounds b=renderer.bounds;return new Vector3(b.max.x+distance,b.center.y,b.center.z); }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        static Bounds CombinedBounds(GameObject model)
        { Renderer[] renderers=model.GetComponentsInChildren<Renderer>();Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b; }
        static Camera CreateCamera(Camera source,Transform parent)
        {
            var host=new GameObject("Temporary near clarity mono camera");host.transform.SetParent(parent,false);
            Camera camera=host.AddComponent<Camera>();camera.CopyFrom(source);camera.scene=source.scene;camera.enabled=false;
            camera.fieldOfView=60;camera.aspect=16f/9f;camera.stereoTargetEye=StereoTargetEyeMask.None;
            camera.GetUniversalAdditionalCameraData().allowXRRendering=false;return camera;
        }
        static void Capture(Camera camera,string path)
        {
            var target=new RenderTexture(1920,1080,24);target.Create();var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply(false,false);File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
#endif
