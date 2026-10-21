#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SwanUnderwater.EditorTests;

namespace SwanUnderwater.Editor
{
    /// <summary>Actual reef sources and placement on an isolated flat diagnostic floor; never edits a saved scene.</summary>
    public static class ReefLodPreview
    {
        [Serializable] public sealed class Shot
        {
            public string image,group;public bool remoteRidge;public int forcedLod,nearGroups;
            public Vector3 eye,boundsCenter,boundsSize;public float distanceToBounds;
            public long highTriangles,lowTriangles;
        }
        [Serializable] public sealed class Report
        {
            public bool success;public string error;
            public string evidence="Actual scanned assets, production placement/grounding and LOD policy in an isolated graphical Editor scene on a flat diagnostic floor. All groups are tested for approach, retreat and revisit. Images are single-camera evidence, not headset clarity, frame time or final underwater lighting acceptance. Native fallback is reported as -1, not an inferred rendered LOD index.";
            public int assertions,groups,remoteUpgraded;public ReefHabitat.GroundingSummary grounding;
            public List<Shot> shots=new List<Shot>();
        }
        public static void Capture()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use a fresh graphical batch Editor in Edit Mode.");
            string output="Validation/iteration-26/reef-lod/unity-preview";string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="-nographics"||args[i]=="-quit")throw new ArgumentException("Preview requires graphical rendering and owns exit.");
                if(args[i]=="-codexReefLodOutput"&&i+1<args.Length)output=args[++i];
            }
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            if(File.Exists(Path.Combine(output,"report.json")))throw new IOException("Use a new preview output directory.");
            var report=new Report();GameObject root=null;RenderTexture target=null;Texture2D pixels=null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                root=new GameObject("Reef LOD isolated evidence");
                RenderSettings.fog=false;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.52f,.56f);
                var light=new GameObject("Diagnostic key light").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(45,-35,0);
                var camera=new GameObject("Reef evidence camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;
                camera.fieldOfView=50;camera.aspect=1;camera.nearClipPlane=.02f;camera.farClipPlane=250;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.09f,.12f);camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
                target=new RenderTexture(1024,1024,24,RenderTextureFormat.ARGB32);target.Create();pixels=new Texture2D(1024,1024,TextureFormat.RGB24,false);camera.targetTexture=target;
                ReefLodChecks.RunPolicyTests();
                ReefHabitat.Build(root.transform,(x,z)=>0f,Vector3.zero);
                var reef=root.GetComponentInChildren<ReefProximityLod>();
                ReefLodChecks.ValidateBuilt(reef,new Vector3(0,1.6f,0));
                report.groups=reef.RegisteredCount;report.remoteUpgraded=reef.UpgradedRemoteCount;report.grounding=ReefHabitat.LastGrounding;
                int remote=-1,coral=-1;
                for(int i=0;i<reef.RegisteredCount;i++)
                {
                    var sample=reef.SampleForValidation(i);
                    if(remote<0&&sample.remoteRidge)remote=i;
                    if(coral<0&&sample.group.name.StartsWith("Coral ",StringComparison.Ordinal))coral=i;
                }
                if(remote<0||coral<0)throw new InvalidOperationException("Missing representative remote ridge/coral.");
                CaptureViews(remote,"remote-rock",reef,camera,target,pixels,output,report);
                CaptureViews(coral,"coral",reef,camera,target,pixels,output,report);
                report.assertions=ReefLodChecks.Assertions;report.success=true;
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally
            {
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("REEF_LOD_PREVIEW "+(report.success?"PASS":"FAIL")+" assertions="+report.assertions+" groups="+report.groups);
            EditorApplication.Exit(report.success?0:1);
        }
        static void CaptureViews(int index,string label,ReefProximityLod reef,Camera camera,RenderTexture target,Texture2D pixels,string output,Report report)
        {
            Bounds bounds=reef.SampleForValidation(index).bounds;
            Vector3 face=new Vector3(bounds.center.x,bounds.center.y,bounds.min.z);
            // An oblique upper view gives the scanned surface an inspectable silhouette.
            Vector3 outward=new Vector3(0,.28f,-1).normalized;
            Vector3 near=face+outward*Mathf.Clamp(bounds.size.x*.65f,1.4f,6f);
            Vector3 far=face+outward*22f;
            Vector3[] eyes={near,far,near};string[] states={"near","far","return"};
            for(int shot=0;shot<eyes.Length;shot++)
            {
                camera.transform.position=eyes[shot];camera.transform.LookAt(bounds.center);
                // SingleCameraRequest intentionally bypasses beginCameraRendering. Call the
                // production policy explicitly for this disabled diagnostic camera only.
                reef.EvaluateForValidation(eyes[shot]);var sample=reef.SampleForValidation(index);
                bool shouldNear=shot!=1;
                if(shouldNear&&sample.forcedLod!=0)throw new InvalidOperationException("Representative near camera must use true LOD0.");
                if(!shouldNear&&sample.forcedLod!=(sample.remoteRidge?1:-1))throw new InvalidOperationException("Representative far fallback mismatch.");
                foreach(var r in sample.group.GetLODs()[0].renderers)foreach(var m in r.sharedMaterials)if(m==null||m.shader==null||!m.shader.isSupported)throw new InvalidOperationException("Unsupported reef material.");
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture previous=RenderTexture.active;
                try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1024,1024),0,0);pixels.Apply();}
                finally{RenderTexture.active=previous;}
                string file=label+"-"+states[shot]+".png";File.WriteAllBytes(Path.Combine(output,file),pixels.EncodeToPNG());
                LOD[] lods=sample.group.GetLODs();report.shots.Add(new Shot{image=file,group=sample.group.name,remoteRidge=sample.remoteRidge,forcedLod=sample.forcedLod,nearGroups=reef.ForcedNearCount,eye=eyes[shot],boundsCenter=bounds.center,boundsSize=bounds.size,distanceToBounds=Mathf.Sqrt(bounds.SqrDistance(eyes[shot])),highTriangles=ReefLodChecks.Triangles(lods[0].renderers),lowTriangles=ReefLodChecks.Triangles(lods[1].renderers)});
            }
        }
    }
}
#endif
