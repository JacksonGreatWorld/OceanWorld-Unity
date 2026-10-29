#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Sealed source preservation and one-world material ownership; no device or image-quality claim.</summary>
    public static class OceanButterflyNearChecks
    {
        const string SourcePath = "Assets/Resources/Shaders/ButterflyFishSwim.shader";
        const string NearPath = "Assets/Resources/Shaders/ButterflyFishNear.shader";
        const string OriginalShaderHash = "dea358b07a7f5220cc0c51493735d4bcb96caffcaad46a2bdb1dc06557b6f9a3";
        const string Resource = "MarineExpansion/butterfly-fish";
        static readonly string[] Maps = { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };
        static readonly string[] Floats = { "_BumpScale", "_Cutoff", "_Metallic", "_Smoothness", "_OcclusionStrength", "_SwingAmp", "_SwingFreq", "_SwingPhase", "_FishHalfLength", "_VerticalBob" };
        static readonly string[] Vectors = { "_BaseColor", "_EmissionColor", "_FishBody", "_FishFins", "_SwimMotion" };
        static int assertions;
        static void Check(bool condition, string detail)
        { assertions++; if (!condition) throw new InvalidOperationException("Butterfly near: " + detail); }
        static string Hash(string path)
        { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static string Section(string text,string start,string end)
        { int a=text.IndexOf(start,StringComparison.Ordinal),b=text.IndexOf(end,a,StringComparison.Ordinal);return text.Substring(a,b-a); }
        static Dictionary<string,string> Fingerprint()
        {
            var result=new Dictionary<string,string>();
            foreach(string path in new[]{SourcePath,"Assets/ArtBaked/MarineExpansion/butterfly-fish/butterfly-pbr.mat",
                "Assets/ArtBaked/MarineExpansion/butterfly-fish/butterfly-canonical.asset","Assets/Resources/MarineExpansion/butterfly-fish.prefab",
                "Sources/MarineExpansion/butterfly-native-admission.json"})
                if(File.Exists(path))result[path]=Hash(path);
            return result;
        }

        public static int RunAll()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode,"ownership checks require isolated Edit Mode");
            assertions=0;
            Dictionary<string,string> before=Fingerprint();
            Check(Hash(SourcePath)==OriginalShaderHash,"admitted original shader remains byte-identical");
            string original=File.ReadAllText(SourcePath),candidate=File.ReadAllText(NearPath);
            Check(Section(original,"    Properties","    SubShader")==Section(candidate,"    Properties","    SubShader"),"every original shader property stays unchanged");
            Check(Section(original,"            Varyings vert(","            half4 frag(")==Section(candidate,"            Varyings vert(","            half4 frag("),"original UV, geometry, stereo and deformation vertex path stays byte-identical");
            Check(original.Split(new[]{"SAMPLE_TEXTURE2D("},StringSplitOptions.None).Length==candidate.Split(new[]{"SAMPLE_TEXTURE2D("},StringSplitOptions.None).Length,"no extra material-map samples are added; URP shadow PCF is the sole new lighting sample");
            Check(candidate.Contains("AlphaToMask On")&&candidate.Contains("clip(albedo.a - _Cutoff)")&&candidate.Contains("Cull Off"),"authored translucent fins keep cutout, MSAA coverage and double-sided rendering");
            Shader shader=Resources.Load<Shader>("Shaders/ButterflyFishNear");
            Check(shader!=null&&!ShaderUtil.ShaderHasError(shader)&&shader.isSupported,"new near shader imports on the active graphics backend");
            GameObject prefab=Resources.Load<GameObject>(Resource);
            Check(prefab!=null,"original admitted source prefab is present");
            Renderer prefabRenderer=prefab.GetComponentInChildren<Renderer>(true);
            Material source=prefabRenderer.sharedMaterial;
            Mesh sourceMesh=prefab.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Check(source!=null&&source.shader.name==OceanMarineMaterialLibrary.ButterflySourceShader,"original source material remains on the admitted source shader");
            Check(sourceMesh!=null&&sourceMesh.vertexCount==2095&&sourceMesh.triangles.Length==11064,"the actual source mesh remains 2095 vertices/3688 triangles");
            var owner=new GameObject("Isolated world-owned butterfly material checks");owner.SetActive(false);
            var otherOwner=new GameObject("Independent butterfly material owner");otherOwner.SetActive(false);
            GameObject first=null,second=null;Material wrong=null;
            OceanMarineMaterialLibrary firstLibrary=null,secondLibrary=null;
            try
            {
                var library=OceanMarineMaterialLibrary.For(owner);firstLibrary=library;owner.SetActive(true);
                Check(ReferenceEquals(library,OceanMarineMaterialLibrary.For(owner))&&owner.GetComponents<OceanMarineMaterialLibrary>().Length==1,"one owner has exactly one material library");
                Material detailed=library.GetButterflyNear(source);
                Check(detailed!=null&&detailed!=source&&detailed.shader==shader,"only a new runtime material selects the near shader");
                Check(ReferenceEquals(detailed,library.GetButterflyNear(source))&&library.OwnedMaterialCount==1,"all residents reuse one material per source/world");
                foreach(string property in Maps)
                {
                    Check(detailed.GetTexture(property)==source.GetTexture(property),"borrowed source texture identity preserved: "+property);
                    Check(detailed.GetTextureScale(property)==source.GetTextureScale(property)&&detailed.GetTextureOffset(property)==source.GetTextureOffset(property),"source texture UV scale/offset preserved: "+property);
                }
                foreach(string property in Floats)Check(Mathf.Abs(detailed.GetFloat(property)-source.GetFloat(property))<.000001f,"source scalar preserved: "+property);
                foreach(string property in Vectors)Check((detailed.GetVector(property)-source.GetVector(property)).sqrMagnitude<.00000001f,"source vector/color preserved: "+property);
                Check(detailed.enableInstancing==source.enableInstancing&&detailed.renderQueue==source.renderQueue,"source instancing and alpha-test queue retained");
                Check(detailed.shaderKeywords.OrderBy(x=>x).SequenceEqual(source.shaderKeywords.OrderBy(x=>x)),"source material keywords retained");
                first=Object.Instantiate(prefab,owner.transform,false);second=Object.Instantiate(prefab,owner.transform,false);
                Vector3 scale=first.transform.localScale;Quaternion rotation=first.transform.localRotation;
                Renderer firstRenderer=first.GetComponentInChildren<Renderer>(true);
                var block=new MaterialPropertyBlock();var phase=new Vector4(1.37f,.85f,-.2f,1f);block.SetVector("_SwimMotion",phase);firstRenderer.SetPropertyBlock(block);
                Check(library.ApplyButterflyNear(first)==1&&library.ApplyButterflyNear(second)==1,"each real butterfly renderer binds the shared near variant");
                Check(library.ApplyButterflyNear(first)==0&&library.OwnedMaterialCount==1,"repeated binding does not allocate or duplicate a variant");
                Check(firstRenderer.sharedMaterial==second.GetComponentInChildren<Renderer>(true).sharedMaterial&&firstRenderer.receiveShadows,"real renderers share the shadow receiver");
                firstRenderer.GetPropertyBlock(block);Check(block.GetVector("_SwimMotion")==phase,"binding preserves another owner's continuous swim property block");
                Check(first.GetComponentInChildren<MeshFilter>(true).sharedMesh==sourceMesh&&first.transform.localScale==scale&&first.transform.localRotation==rotation,"binding never stretches, rotates or clones the model mesh");
                var other=OceanMarineMaterialLibrary.For(otherOwner);secondLibrary=other;otherOwner.SetActive(true);Material independent=other.GetButterflyNear(source);
                Check(independent!=detailed&&other.OwnedMaterialCount==1,"different worlds have independent cleanup ownership");
                Object.DestroyImmediate(first);first=null;Object.DestroyImmediate(second);second=null;
                library.Dispose();library.Dispose();
                Check(library.OwnedMaterialCount==0&&detailed==null&&source!=null&&independent!=null,"explicit cleanup is idempotent, frees only owned variants and retains borrowed sources/other worlds");
                Material recreated=library.GetButterflyNear(source);Check(recreated!=null&&library.OwnedMaterialCount==1,"a reused live owner recreates a cleared variant safely");
                // Normal non-ExecuteAlways components do not establish production
                // OnDestroy behavior in Edit Mode. Prove explicit cleanup here;
                // the active-host teardown contract is checked in Play Mode below.
                library.Dispose();
                Check(recreated==null&&independent!=null&&source!=null,"explicit cleanup after cache reuse frees its variant without touching source/other owner");
                Object.DestroyImmediate(owner);owner=null;
                wrong=new Material(Shader.Find("Universal Render Pipeline/Unlit"));bool rejected=false;
                try{other.GetButterflyNear(wrong);}catch(InvalidOperationException){rejected=true;}
                Check(rejected&&other.OwnedMaterialCount==1,"unrelated materials are rejected without new cache entries");
                other.Dispose();
                Check(independent==null&&source!=null,"second explicit cleanup preserves the borrowed source");
                Object.DestroyImmediate(otherOwner);otherOwner=null;
            }
            finally
            {
                if(first!=null)Object.DestroyImmediate(first);if(second!=null)Object.DestroyImmediate(second);
                if(firstLibrary!=null)firstLibrary.Dispose();if(secondLibrary!=null)secondLibrary.Dispose();
                if(owner!=null)Object.DestroyImmediate(owner);if(otherOwner!=null)Object.DestroyImmediate(otherOwner);if(wrong!=null)Object.DestroyImmediate(wrong);
            }
            foreach(var row in before)Check(File.Exists(row.Key)&&Hash(row.Key)==row.Value,"original admitted file unchanged: "+row.Key);
            Debug.Log("BUTTERFLY_NEAR_MATERIAL_CHECKS_PASS assertions="+assertions+"; source bytes/maps, shared cache and explicit EditMode cleanup only, no GPU/device quality claim");
            return assertions;
        }

        /// <summary>
        /// Invoke with yield return from an isolated PlayMode fixture. Real active
        /// owners have entered a frame before teardown; production Dispose queues
        /// Material destruction until frame end, so that deferred boundary is kept.
        /// No source asset, game save, scene world or network is initialized.
        /// </summary>
        public static IEnumerator RunLifecycleInPlayMode()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Butterfly owner lifecycle requires actual Play Mode.");
            int beforeAssertions=assertions;
            GameObject owner=null,otherOwner=null;
            OceanMarineMaterialLibrary library=null,other=null;
            Material firstMaterial=null,independent=null;
            var sourcePrefab=Resources.Load<GameObject>(Resource);
            Check(sourcePrefab!=null,"PlayMode admitted source is present");
            Material source=sourcePrefab.GetComponentInChildren<Renderer>(true).sharedMaterial;
            Dictionary<string,string> filesBefore=Fingerprint();
            try
            {
                owner=new GameObject("Active isolated butterfly owner lifecycle");
                otherOwner=new GameObject("Active isolated independent butterfly owner lifecycle");
                library=OceanMarineMaterialLibrary.For(owner);other=OceanMarineMaterialLibrary.For(otherOwner);
                firstMaterial=library.GetButterflyNear(source);independent=other.GetButterflyNear(source);
                Check(owner.activeInHierarchy&&otherOwner.activeInHierarchy&&library.enabled&&other.enabled,
                    "PlayMode owners and ordinary library components are active");
                Check(firstMaterial!=null&&independent!=null&&firstMaterial!=source&&independent!=source&&firstMaterial!=independent,
                    "two active owners hold separate runtime variants of the admitted source");
                yield return null;
                Object.DestroyImmediate(owner);owner=null;
                yield return null;
                Check(firstMaterial==null&&source!=null&&independent!=null&&other!=null&&other.OwnedMaterialCount==1,
                    "actual PlayMode OnDestroy releases the first variant after its deferred frame, retaining source and independent owner");
                Check(ReferenceEquals(independent,other.GetButterflyNear(source)),
                    "surviving PlayMode owner still reuses its live cache");
                Object.DestroyImmediate(otherOwner);otherOwner=null;
                yield return null;
                Check(independent==null&&source!=null,"actual second PlayMode teardown also releases only its owned variant");
                foreach(var row in filesBefore)Check(File.Exists(row.Key)&&Hash(row.Key)==row.Value,
                    "PlayMode owner teardown keeps admitted source bytes: "+row.Key);
                Debug.Log("BUTTERFLY_NEAR_PLAYMODE_LIFECYCLE_PASS assertions="+(assertions-beforeAssertions)+
                    "; active owner OnDestroy and real deferred material destruction; no device claim");
            }
            finally
            {
                // Failure cleanup cannot rely on EditMode events after the fixture exits.
                if(library!=null)library.Dispose();if(other!=null)other.Dispose();
                if(owner!=null)Object.DestroyImmediate(owner);if(otherOwner!=null)Object.DestroyImmediate(otherOwner);
            }
        }

        [Serializable] sealed class ImageRow
        { public string mode,view,litImage,shadowImage; public int receiverPixels,shadowChangedPixels;public float shadowMeanDifference,litLuminance,shadowLuminance,litP05,litP95; }
        [Serializable] sealed class CaptureReport
        {
            public bool success,restoredContext;public string error,unity,graphicsDevice;
            public string scope="Actual admitted butterfly mesh/maps and world-owned near variant, three fixed anatomical views, main-light caster on/off pairs, four-sample render target. No model proportions or texture samples changed. Editor render evidence, not worn-headset quality or performance acceptance.";
            public int assertions,requestedMsaa;public List<ImageRow> images=new List<ImageRow>();
        }

        // Graphical isolated batch Editor; omit -quit/-nographics. Owns exit.
        // -executeMethod SwanUnderwater.EditorTests.OceanButterflyNearChecks.Capture -oceanButterflyNearOutput <fresh directory>
        public static void Capture()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use a fresh graphical batch Editor in Edit Mode.");
            string[] args=Environment.GetCommandLineArgs();string output="Validation/butterfly-near-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            for(int i=0;i<args.Length;i++)
            {if(args[i]=="-quit"||args[i]=="-nographics")throw new ArgumentException("Capture owns exit and requires graphics");if(args[i]=="-oceanButterflyNearOutput"&&i+1<args.Length)output=args[++i];}
            output=Path.GetFullPath(output);if(Directory.Exists(output)&&Directory.GetFileSystemEntries(output).Length!=0)throw new IOException("Use a fresh output directory");Directory.CreateDirectory(output);
            var report=new CaptureReport{unity=Application.unityVersion,graphicsDevice=SystemInfo.graphicsDeviceType.ToString(),requestedMsaa=4};
            var oldQuality=QualitySettings.renderPipeline;var oldDefault=GraphicsSettings.defaultRenderPipeline;Scene oldScene=SceneManager.GetActiveScene(),scene=default;
            bool oldFog=RenderSettings.fog,oldAsync=ShaderUtil.allowAsyncCompilation;Material oldSky=RenderSettings.skybox;Light oldSun=RenderSettings.sun;
            AmbientMode oldAmbient=RenderSettings.ambientMode;Color oldLight=RenderSettings.ambientLight;SphericalHarmonicsL2 oldProbe=RenderSettings.ambientProbe;
            UniversalRenderPipelineAsset pipeline=null;RenderTexture target=null,resolved=null;Texture2D pixels=null;Material casterMaterial=null;OceanMarineMaterialLibrary library=null;
            try
            {
                report.assertions=RunAll();
                scene=EditorSceneManager.NewPreviewScene();ShaderUtil.allowAsyncCompilation=false;
                pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset"));
                QualitySettings.renderPipeline=pipeline;GraphicsSettings.defaultRenderPipeline=pipeline;pipeline.msaaSampleCount=4;
                RenderSettings.fog=false;RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.12f,.16f,.20f);
                var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(RenderSettings.ambientLight);RenderSettings.ambientProbe=probe;
                var root=new GameObject("Isolated original butterfly and near lighting");SceneManager.MoveGameObjectToScene(root,scene);
                library=OceanMarineMaterialLibrary.For(root);
                var camera=new GameObject("Butterfly paired portrait camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);
                camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=.42f;camera.aspect=16f/9f;camera.nearClipPlane=.02f;camera.farClipPlane=30f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.stereoTargetEye=StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering=false;camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                var sun=new GameObject("One real portrait sun").AddComponent<Light>();sun.transform.SetParent(root.transform,false);sun.type=LightType.Directional;
                sun.transform.rotation=Quaternion.LookRotation(new Vector3(-.5f,-.45f,-.7f));sun.intensity=1.3f;sun.color=new Color(1f,.95f,.85f);OceanReefShadowPolicy.ConfigureSun(sun);RenderSettings.sun=sun;
                Vector3 center=new Vector3(0,1f,0);
                var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.SetParent(root.transform,false);caster.transform.position=center-sun.transform.forward*1.2f;
                caster.transform.rotation=sun.transform.rotation;caster.transform.localScale=new Vector3(2.2f,2.2f,.15f);Object.DestroyImmediate(caster.GetComponent<Collider>());
                casterMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));caster.GetComponent<Renderer>().sharedMaterial=casterMaterial;caster.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;
                target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32){antiAliasing=4};Check(target.Create(),"MSAA render target allocates");
                resolved=new RenderTexture(960,540,0,RenderTextureFormat.ARGB32);Check(resolved.Create(),"resolved readback target allocates");pixels=new Texture2D(960,540,TextureFormat.RGB24,false);camera.targetTexture=target;
                var prefab=Resources.Load<GameObject>(Resource);var model=Object.Instantiate(prefab,root.transform,false);
                Renderer renderer=model.GetComponent<Renderer>();Material source=renderer.sharedMaterial;
                Bounds bounds=renderer.bounds;model.transform.localScale*=.95f/bounds.size.magnitude;bounds=renderer.bounds;model.transform.position+=center-bounds.center;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                var fixedPose=new MaterialPropertyBlock();fixedPose.SetVector("_SwimMotion",new Vector4(0,1,0,1));renderer.SetPropertyBlock(fixedPose);
                foreach(string mode in new[]{"admitted-original","near-lighting"})
                {
                    renderer.sharedMaterial=mode=="admitted-original"?source:library.GetButterflyNear(source);renderer.receiveShadows=true;
                    float[] angles={0,45,90};string[] views={"front","front45","side"};
                    for(int i=0;i<angles.Length;i++)
                    {
                        float angle=angles[i]*Mathf.Deg2Rad;Vector3 at=center+new Vector3(Mathf.Sin(angle)*3f,.08f,Mathf.Cos(angle)*3f);
                        camera.transform.SetPositionAndRotation(at,Quaternion.LookRotation(center-at,Vector3.up));
                        string stem=mode+"-"+views[i];sun.shadows=LightShadows.None;Color32[] lit=Read(camera,target,resolved,pixels);
                        var row=new ImageRow{mode=mode,view=views[i],litImage=stem+"-lit.png",shadowImage=stem+"-shadow.png"};File.WriteAllBytes(Path.Combine(output,row.litImage),pixels.EncodeToPNG());
                        sun.shadows=LightShadows.Soft;Color32[] shadow=Read(camera,target,resolved,pixels);File.WriteAllBytes(Path.Combine(output,row.shadowImage),pixels.EncodeToPNG());
                        long delta=0,litSum=0,shadowSum=0;var values=new List<int>();
                        for(int j=0;j<lit.Length;j++)
                        {
                            int bright=lit[j].r+lit[j].g+lit[j].b;if(bright<20)continue;row.receiverPixels++;litSum+=bright;shadowSum+=shadow[j].r+shadow[j].g+shadow[j].b;
                            int difference=Math.Abs(lit[j].r-shadow[j].r)+Math.Abs(lit[j].g-shadow[j].g)+Math.Abs(lit[j].b-shadow[j].b);delta+=difference;if(difference>9)row.shadowChangedPixels++;
                            values.Add(bright/3);
                        }
                        row.shadowMeanDifference=row.receiverPixels>0?delta/(row.receiverPixels*3f):0;row.litLuminance=row.receiverPixels>0?litSum/(row.receiverPixels*3f):0;row.shadowLuminance=row.receiverPixels>0?shadowSum/(row.receiverPixels*3f):0;
                        values.Sort();if(values.Count>0){row.litP05=values[Mathf.RoundToInt((values.Count-1)*.05f)];row.litP95=values[Mathf.RoundToInt((values.Count-1)*.95f)];}report.images.Add(row);
                        Check(row.receiverPixels>500,"actual fish covers meaningful pixels: "+stem);
                        if(mode=="near-lighting")Check(row.shadowChangedPixels>100&&row.shadowMeanDifference>2f&&row.shadowLuminance>2f,"near material receives a real caster while retaining indirect light: "+stem);
                        else Check(row.shadowMeanDifference<2f,"original material is a no-shadow negative control: "+stem);
                    }
                }
                report.assertions=assertions;report.success=true;
            }
            catch(Exception error){report.error=error.ToString();Debug.LogException(error);}
            finally
            {
                QualitySettings.renderPipeline=oldQuality;GraphicsSettings.defaultRenderPipeline=oldDefault;RenderSettings.fog=oldFog;RenderSettings.skybox=oldSky;RenderSettings.sun=oldSun;
                RenderSettings.ambientMode=oldAmbient;RenderSettings.ambientLight=oldLight;RenderSettings.ambientProbe=oldProbe;ShaderUtil.allowAsyncCompilation=oldAsync;
                if(library!=null)library.Dispose();if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);if(casterMaterial!=null)Object.DestroyImmediate(casterMaterial);
                if(pixels!=null)Object.DestroyImmediate(pixels);if(target!=null){target.Release();Object.DestroyImmediate(target);}if(resolved!=null){resolved.Release();Object.DestroyImmediate(resolved);}if(pipeline!=null)Object.DestroyImmediate(pipeline);
                report.restoredContext=QualitySettings.renderPipeline==oldQuality&&GraphicsSettings.defaultRenderPipeline==oldDefault&&SceneManager.GetActiveScene()==oldScene;if(!report.restoredContext)report.success=false;
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("BUTTERFLY_NEAR_CAPTURE_"+(report.success?"PASS":"FAIL")+" "+output);EditorApplication.Exit(report.success?0:1);
        }
        static Color32[] Read(Camera camera,RenderTexture target,RenderTexture resolved,Texture2D pixels)
        {
            for(int i=0;i<2;i++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            Graphics.Blit(target,resolved);RenderTexture old=RenderTexture.active;
            try{RenderTexture.active=resolved;pixels.ReadPixels(new Rect(0,0,resolved.width,resolved.height),0,0);pixels.Apply();return pixels.GetPixels32();}
            finally{RenderTexture.active=old;}
        }
    }
}
#endif
