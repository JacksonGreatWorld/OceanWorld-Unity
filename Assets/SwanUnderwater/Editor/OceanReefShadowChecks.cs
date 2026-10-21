#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SwanUnderwater.EditorTests
{
    public static class OceanReefShadowChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition,string message){Assertions++;if(!condition)throw new InvalidOperationException("Reef shadow: "+message);}
        public static void RunAll()
        {
            Assertions=0;
            foreach(string name in new[]{"Mobile_RPAsset","PC_RPAsset"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/"+name+".asset");
                Check(asset!=null,"pipeline exists "+name);
                Check(asset.mainLightShadowmapResolution==OceanReefShadowPolicy.AtlasResolution,"2048 shared atlas "+name);
                Check(asset.shadowCascadeCount==OceanReefShadowPolicy.Cascades,"bounded two cascades "+name);
                Check(Mathf.Approximately(asset.shadowDistance,OceanReefShadowPolicy.Distance),"24m budget "+name);
                Check(Mathf.Approximately(asset.cascade2Split,OceanReefShadowPolicy.NearSplit),"near precision split "+name);
                Check(Mathf.Approximately(asset.shadowDepthBias,OceanReefShadowPolicy.DepthBias)&&Mathf.Approximately(asset.shadowNormalBias,OceanReefShadowPolicy.NormalBias),"contact bias "+name);
                Check(asset.supportsSoftShadows,"soft enabled "+name);
                var serialized=new SerializedObject(asset);
                Check(serialized.FindProperty("m_SoftShadowQuality").intValue==(int)SoftShadowQuality.Low,"mobile four-tap filter "+name);
                Check(!serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue,"no additional shadow lights "+name);
            }
            foreach(string shader in new[]{"SeabedSand","SeabedShell","CollectibleSurface"})
            {
                string text=File.ReadAllText("Assets/Resources/Shaders/"+shader+".shader");
                foreach(string keyword in new[]{"_SHADOWS_SOFT_LOW","_SHADOWS_SOFT_MEDIUM","_SHADOWS_SOFT_HIGH"})Check(text.Contains(keyword),"XR static shadow variant "+shader+" "+keyword);
                Check(text.Contains("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX"),"stereo setup "+shader);
                Check(text.Contains("InitializeInputDataFog")&&text.Contains("MixFog"),"world-distance fog "+shader);
            }
            var go=new GameObject("Temporary shadow policy check");
            try
            {
                var light=go.AddComponent<Light>();OceanReefShadowPolicy.ConfigureSun(light);
                Check(light.shadows==LightShadows.Soft&&Mathf.Approximately(light.shadowStrength,.8f),"sun uses bounded contrast soft shadow");
                var data=light.GetUniversalAdditionalLightData();Check(data.usePipelineSettings&&data.softShadowQuality==SoftShadowQuality.Low,"sun bias kernel matches mobile low quality");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            Debug.Log("[OceanReefShadowChecks] settings/shader contract PASS "+Assertions);
        }
        public static void ValidateBuilt(ReefProximityLod reef)
        {
            Check(reef!=null&&reef.RegisteredCount==302,"actual reef population");
            for(int i=0;i<reef.RegisteredCount;i++)
            {
                LOD[] lods=reef.SampleForValidation(i).group.GetLODs();
                for(int lod=0;lod<lods.Length;lod++)foreach(Renderer renderer in lods[lod].renderers)
                {
                    Check(renderer.shadowCastingMode==(lod<2?ShadowCastingMode.On:ShadowCastingMode.Off),"LOD0/1 cast, LOD2 off: "+i+"/"+lod);
                    Check(renderer.receiveShadows,"receiver remains enabled "+i+"/"+lod);
                    if(lod<2)foreach(Material material in renderer.sharedMaterials)Check(material!=null&&material.FindPass("ShadowCaster")>=0,"real source shadow caster pass "+i+"/"+lod);
                }
            }
            Check(ReefHabitat.LastGrounding.unanchoredLods==0,"shadow changes preserve actual geometry grounding");
            Debug.Log("[OceanReefShadowChecks] actual reef casters PASS "+Assertions);
        }
    }
}
#endif
