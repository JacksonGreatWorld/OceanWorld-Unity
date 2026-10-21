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
    /// <summary>Same real reef/receiver/cameras, explicit Mobile pipeline clones; no saved scene edits.</summary>
    public static class OceanReefShadowPreview
    {
        [Serializable] public sealed class Shot
        {
            public string image,variant,view;public Vector3 eye,target;public int atlas,cascades,postSubmitGlobalTextureWidth,postSubmitGlobalTextureHeight,nearGroups;
            public float distance,split,depthBias,normalBias,strength;public bool soft,postSubmitSoftKeyword,postSubmitLowKeyword,postSubmitCascadeKeyword;
            public Vector4 postSubmitShadowParams;public int changedPixels;public float meanByteDifference;
        }
        [Serializable] public sealed class Report
        {
            public bool success,restoredContext;public string error;
            public bool shadowTelemetryValid = false;
            public string shadowTelemetryScope = "Post-submit shader globals are diagnostic snapshots only. URP RenderGraph resets global bindings after submission, so the 4x4 fallback texture and zero strength are not the in-pass shadow atlas/parameters. Configured atlas/cascade values are asset settings, not GPU measurements.";
            public string sourcePipeline="Assets/Settings/Mobile_RPAsset.asset";
            public string evidence="Single-camera graphical Editor diagnostic using explicit in-memory Mobile pipeline clones, production reef geometry/grounding and sand shader. Baseline and improved share camera, terrain, source textures, fog and lighting. Baseline restores old caster flags/settings. This is not HMD stereo, Android XR static-keyword execution, performance, or wearer acceptance. Shader pixel difference proves a rendered change, not that it is perceptually better.";
            public int assertions;public ReefHabitat.GroundingSummary grounding;public List<Shot> shots=new List<Shot>();
        }
        public static void Capture()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use a fresh graphical batch Editor in Edit Mode.");
            string output="Validation/iteration-27/reef-shadows/preview";string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="-quit"||args[i]=="-nographics")throw new ArgumentException("Preview needs graphics and owns process exit.");
                if(args[i]=="-codexReefShadowOutput"&&i+1<args.Length)output=args[++i];
            }
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"report.json")))throw new IOException("Use a fresh output directory.");
            var report=new Report();var oldQuality=QualitySettings.renderPipeline;var oldDefault=GraphicsSettings.defaultRenderPipeline;
            var oldAmbient=RenderSettings.ambientMode;var oldProbe=RenderSettings.ambientProbe;var oldSky=RenderSettings.skybox;var oldSun=RenderSettings.sun;
            Color oldAmbientLight=RenderSettings.ambientLight,oldSkyColor=RenderSettings.ambientSkyColor,oldEquator=RenderSettings.ambientEquatorColor,oldGround=RenderSettings.ambientGroundColor,oldFog=RenderSettings.fogColor;
            bool oldFogEnabled=RenderSettings.fog;float oldDensity=RenderSettings.fogDensity;var oldFogMode=RenderSettings.fogMode;
            GameObject root=null;Mesh sandMesh=null;Material sand=null;RenderTexture rt=null;Texture2D pixels=null;
            UniversalRenderPipelineAsset baseline=null,improved=null;var runtimeMaterials=new HashSet<Material>();
            try
            {
                OceanReefShadowChecks.RunAll();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var original=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(report.sourcePipeline);
                baseline=UnityEngine.Object.Instantiate(original);baseline.name="Mobile shadow baseline (unsaved)";
                improved=UnityEngine.Object.Instantiate(original);improved.name="Mobile shadow improved (unsaved)";
                baseline.mainLightShadowmapResolution=1024;baseline.shadowCascadeCount=1;baseline.shadowDistance=24;baseline.shadowDepthBias=1;baseline.shadowNormalBias=1;
                var baselineSerialized=new SerializedObject(baseline);baselineSerialized.FindProperty("m_SoftShadowsSupported").boolValue=false;baselineSerialized.ApplyModifiedPropertiesWithoutUndo();
                root=new GameObject("Reef shadow evidence (unsaved)");
                RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.038f;RenderSettings.fogColor=new Color(.032f,.19f,.235f);
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.18f,.38f,.44f);RenderSettings.ambientEquatorColor=new Color(.08f,.22f,.28f);RenderSettings.ambientGroundColor=new Color(.02f,.06f,.10f);RenderSettings.skybox=null;
                // Deterministic ambient irradiance, using the game's equatorial water tint.
                // No asynchronous sky bake differs between the two settings.
                var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(new Color(.08f,.22f,.28f));RenderSettings.ambientProbe=probe;
                var sun=new GameObject("Filtered Sunlight").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.color=new Color(1,.95f,.8f);sun.intensity=1.15f;sun.transform.rotation=Quaternion.Euler(48,-35,0);OceanReefShadowPolicy.ConfigureSun(sun);RenderSettings.sun=sun;
                var camera=new GameObject("Fixed reef shadow camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;camera.fieldOfView=68;camera.aspect=1.5f;camera.nearClipPlane=.05f;camera.farClipPlane=130;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.GetUniversalAdditionalCameraData().allowXRRendering=false;
                rt=new RenderTexture(1200,800,24,RenderTextureFormat.ARGB32);rt.Create();pixels=new Texture2D(1200,800,TextureFormat.RGB24,false);camera.targetTexture=rt;
                sandMesh=new Mesh{name="Flat diagnostic sand"};sandMesh.vertices=new[]{new Vector3(-110,0,-110),new Vector3(110,0,-110),new Vector3(110,0,110),new Vector3(-110,0,110)};sandMesh.triangles=new[]{0,2,1,0,3,2};sandMesh.normals=new[]{Vector3.up,Vector3.up,Vector3.up,Vector3.up};sandMesh.tangents=new[]{new Vector4(1,0,0,-1),new Vector4(1,0,0,-1),new Vector4(1,0,0,-1),new Vector4(1,0,0,-1)};sandMesh.uv=new[]{new Vector2(-16.5f,-16.5f),new Vector2(16.5f,-16.5f),new Vector2(16.5f,16.5f),new Vector2(-16.5f,16.5f)};sandMesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};sandMesh.RecalculateBounds();
                var ground=new GameObject("Actual SeabedSand receiver");ground.transform.SetParent(root.transform);ground.AddComponent<MeshFilter>().sharedMesh=sandMesh;
                sand=new Material(Resources.Load<Shader>("Shaders/SeabedSand"));sand.SetTexture("_BaseMap",Resources.Load<Texture2D>("Textures/Sand/Ground101_Color"));sand.SetTexture("_BumpMap",Resources.Load<Texture2D>("Textures/Sand/Ground101_NormalGL"));sand.SetColor("_BaseColor",new Color(.68f,.75f,.78f));sand.SetFloat("_BumpScale",.28f);var receiver=ground.AddComponent<MeshRenderer>();receiver.sharedMaterial=sand;receiver.receiveShadows=true;receiver.shadowCastingMode=ShadowCastingMode.On;
                ReefHabitat.Build(root.transform,(x,z)=>0f,Vector3.zero);var reef=root.GetComponentInChildren<ReefProximityLod>();OceanReefShadowChecks.ValidateBuilt(reef);report.grounding=ReefHabitat.LastGrounding;
                foreach(var r in root.GetComponentsInChildren<Renderer>())foreach(var material in r.sharedMaterials)if(material!=null&&!EditorUtility.IsPersistent(material))runtimeMaterials.Add(material);
                Vector3[] eyes={new Vector3(-.3f,1.55f,-2.7f),new Vector3(-1f,2.8f,-10)};
                Vector3[] targets={new Vector3(-3.2f,.7f,1.3f),new Vector3(0,.8f,6)};
                string[] views={"near-contact","mid-reef"};var before=new Color32[2][];
                foreach(bool improvedMode in new[]{false,true})
                {
                    var pipeline=improvedMode?improved:baseline;QualitySettings.renderPipeline=pipeline;GraphicsSettings.defaultRenderPipeline=pipeline;
                    sun.shadowStrength=improvedMode?OceanReefShadowPolicy.Strength:1f;
                    for(int i=0;i<reef.RegisteredCount;i++)
                    {
                        var sample=reef.SampleForValidation(i);LOD[] lods=sample.group.GetLODs();
                        for(int lod=0;lod<lods.Length;lod++)foreach(var renderer in lods[lod].renderers)
                            renderer.shadowCastingMode=(improvedMode?lod<2:lod==0&&!sample.remoteRidge)?ShadowCastingMode.On:ShadowCastingMode.Off;
                    }
                    for(int i=0;i<eyes.Length;i++)
                    {
                        camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));reef.EvaluateForValidation(eyes[i]);
                        // SingleCameraRequest bypasses beginCameraRendering, hence explicit production LOD evaluation.
                        // Warm each new pipeline before keeping the frame; no quality or source asset is saved.
                        for(int pass=0;pass<2;pass++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        var oldActive=RenderTexture.active;try{RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();}finally{RenderTexture.active=oldActive;}
                        string image=(improvedMode?"improved-":"baseline-")+views[i]+".png";File.WriteAllBytes(Path.Combine(output,image),pixels.EncodeToPNG());
                        var atlas=Shader.GetGlobalTexture("_MainLightShadowmapTexture");var shot=new Shot{image=image,variant=improvedMode?"improved":"baseline",view=views[i],eye=eyes[i],target=targets[i],atlas=pipeline.mainLightShadowmapResolution,cascades=pipeline.shadowCascadeCount,distance=pipeline.shadowDistance,split=pipeline.cascade2Split,depthBias=pipeline.shadowDepthBias,normalBias=pipeline.shadowNormalBias,strength=sun.shadowStrength,soft=pipeline.supportsSoftShadows,nearGroups=reef.ForcedNearCount,postSubmitShadowParams=Shader.GetGlobalVector("_MainLightShadowParams"),postSubmitSoftKeyword=Shader.IsKeywordEnabled("_SHADOWS_SOFT"),postSubmitLowKeyword=Shader.IsKeywordEnabled("_SHADOWS_SOFT_LOW"),postSubmitCascadeKeyword=Shader.IsKeywordEnabled("_MAIN_LIGHT_SHADOWS_CASCADE"),postSubmitGlobalTextureWidth=atlas!=null?atlas.width:0,postSubmitGlobalTextureHeight=atlas!=null?atlas.height:0};
                        Color32[] colors=pixels.GetPixels32();if(!improvedMode)before[i]=colors;else
                        {
                            long difference=0;for(int j=0;j<colors.Length;j++){int delta=Math.Abs(colors[j].r-before[i][j].r)+Math.Abs(colors[j].g-before[i][j].g)+Math.Abs(colors[j].b-before[i][j].b);difference+=delta;if(delta>3)shot.changedPixels++;}shot.meanByteDifference=(float)difference/(colors.Length*3);
                            if(shot.changedPixels<25)throw new InvalidOperationException("Shadow settings produced no measurable pixel change in "+views[i]);
                        }
                        report.shots.Add(shot);
                    }
                }
                report.assertions=OceanReefShadowChecks.Assertions;report.success=true;
            }
            catch(Exception exception){report.error=exception.ToString();Debug.LogException(exception);}
            finally
            {
                QualitySettings.renderPipeline=oldQuality;GraphicsSettings.defaultRenderPipeline=oldDefault;
                RenderSettings.ambientMode=oldAmbient;RenderSettings.ambientLight=oldAmbientLight;RenderSettings.ambientSkyColor=oldSkyColor;RenderSettings.ambientEquatorColor=oldEquator;RenderSettings.ambientGroundColor=oldGround;RenderSettings.ambientProbe=oldProbe;RenderSettings.skybox=oldSky;RenderSettings.sun=oldSun;RenderSettings.fog=oldFogEnabled;RenderSettings.fogMode=oldFogMode;RenderSettings.fogColor=oldFog;RenderSettings.fogDensity=oldDensity;
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);foreach(var material in runtimeMaterials)if(material!=null)UnityEngine.Object.DestroyImmediate(material);if(sand!=null)UnityEngine.Object.DestroyImmediate(sand);if(sandMesh!=null)UnityEngine.Object.DestroyImmediate(sandMesh);if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(baseline!=null)UnityEngine.Object.DestroyImmediate(baseline);if(improved!=null)UnityEngine.Object.DestroyImmediate(improved);
                report.restoredContext=QualitySettings.renderPipeline==oldQuality&&GraphicsSettings.defaultRenderPipeline==oldDefault&&RenderSettings.fog==oldFogEnabled&&RenderSettings.fogDensity==oldDensity;
                File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            }
            Debug.Log("REEF_SHADOW_PREVIEW "+(report.success?"PASS":"FAIL")+" assertions="+report.assertions);EditorApplication.Exit(report.success?0:1);
        }
    }
}
#endif
