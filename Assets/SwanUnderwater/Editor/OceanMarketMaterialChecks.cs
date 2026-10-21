#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Import/authoring guards only. GPU screenshots and stereo device validation remain separate.</summary>
    public static class OceanMarketMaterialChecks
    {
        const string Resource = "Shaders/OceanReefFan";
        static int assertions;

        static void Check(bool value,string message)
        {
            assertions++;
            if(!value)throw new InvalidOperationException("OceanMarketMaterialChecks: "+message);
        }

        public static int RunAll()
        {
            assertions=0;
            Shader shader=Resources.Load<Shader>(Resource);
            Check(shader!=null,"reef fan shader is included as an explicit resource");
            Check(shader.name=="SwanUnderwater/OceanReefFan","expected material shader identity");
            foreach(var message in ShaderUtil.GetShaderMessages(shader))
                Check(message.severity.ToString()!="Error",message.message);
            var material=new Material(shader);
            try
            {
                foreach(string property in new[]{"_BaseMap","_BaseColor","_BumpMap","_BumpScale","_NormalEncoding","_MaskMap",
                    "_OcclusionMap","_UseOcclusionMap","_OcclusionStrength","_Metallic","_Roughness","_AlphaClip","_Cutoff","_Cull",
                    "_EmissionMap","_EmissionColor","_SwayAmplitude","_SwayFrequency","_SwayHeight",
                    "_SwayRootHeight","_SwayUseUV2","_SwayPhase","_CurrentDirection",
                    "_ValidationTime","_ValidationTimeEnabled"})
                    Check(material.HasProperty(property),"baker property is present: "+property);
                Check(material.FindPass("ReefFanForward")>=0&&material.FindPass("ReefFanShadowCaster")>=0&&
                    material.FindPass("ReefFanDepthOnly")>=0,"colour, shadow and depth coverage are available");
                Check(material.passCount==3,"no extra transparent or full-screen effect pass");
                ValidateMaterial(material);
            }
            finally{Object.DestroyImmediate(material);}
            Debug.Log("OceanMarketMaterialChecks PASS: "+assertions+" shader/import assertions; not a rendered or headset test.");
            return assertions;
        }

        public static void ValidateMaterial(Material material)
        {
            Check(material!=null&&material.shader!=null&&material.shader.name=="SwanUnderwater/OceanReefFan",
                "fan material uses the admitted shader");
            foreach(string property in new[]{"_BumpScale","_NormalEncoding","_OcclusionStrength","_Metallic","_Roughness",
                "_AlphaClip","_Cutoff","_Cull","_SwayAmplitude","_SwayFrequency","_SwayHeight",
                "_SwayRootHeight","_SwayUseUV2","_SwayPhase"})
                Check(Finite(material.GetFloat(property)),"finite material value: "+property);
            Check(material.renderQueue<=(int)RenderQueue.GeometryLast,"fan stays in depth-writing opaque/cutout rendering");
            Check(material.GetFloat("_Cutoff")>=0&&material.GetFloat("_Cutoff")<=1,"opacity cutoff is in [0,1]");
            Check(material.GetFloat("_SwayAmplitude")>=0&&material.GetFloat("_SwayAmplitude")<=0.15f,
                "current displacement stays within the agreed 15 cm world-space bound");
            Check(material.GetFloat("_SwayFrequency")>=0&&material.GetFloat("_SwayFrequency")<=2,
                "water current stays slow");
            Check(material.GetFloat("_SwayHeight")>0.0001f,"root-to-tip fallback has positive measured height");
            float authored=material.GetFloat("_SwayUseUV2");
            Check(authored==0||authored==1,"motion weights use an explicit source contract");
            float clipped=material.GetFloat("_AlphaClip");
            Check(clipped==0||clipped==1,"source opaque versus MASK mode is explicit");
            float cull=material.GetFloat("_Cull");
            Check(cull==0||cull==1||cull==2,"valid source cull mode");
            float normalEncoding=material.GetFloat("_NormalEncoding");
            Check(normalEncoding==0||normalEncoding==1,"normal texture channel encoding is explicit");
            Vector4 stream=material.GetVector("_CurrentDirection");
            Check(Finite(stream.x)&&Finite(stream.y)&&Finite(stream.z)&&Finite(stream.w),"finite current direction");
        }

        // Call during baking while the normalized mesh is readable, before upload.
        // This checks actual admitted weights and rooting instead of a duplicate CPU animation model.
        public static int ValidateMesh(Mesh mesh,Material material)
        {
            int before=assertions;
            ValidateMaterial(material);
            Check(mesh!=null&&mesh.isReadable,"validate the real mesh before CPU geometry is discarded");
            Check(mesh.vertexCount>0&&mesh.normals.Length==mesh.vertexCount&&mesh.tangents.Length==mesh.vertexCount,
                "source normals and tangent basis survive normalization");
            Check(mesh.uv.Length==mesh.vertexCount,"original UV0 survives import without remapping");
            var weights=new List<Vector2>();
            bool authored=material.GetFloat("_SwayUseUV2")>0.5f;
            if(authored){mesh.GetUVs(1,weights);Check(weights.Count==mesh.vertexCount,"authored weights cover the real mesh");}
            Vector3[] points=mesh.vertices;int anchored=0,moving=0;
            float root=material.GetFloat("_SwayRootHeight"),height=material.GetFloat("_SwayHeight");
            for(int i=0;i<points.Length;i++)
            {
                Vector3 p=points[i];Check(Finite(p.x)&&Finite(p.y)&&Finite(p.z),"finite mesh vertex "+i);
                float weight=authored?weights[i].x:Mathf.Clamp01((p.y-root)/height);
                Check(Finite(weight)&&weight>=0&&weight<=1,"bounded root-to-tip weight "+i);
                if(authored)Check(Finite(weights[i].y),"finite individual phase "+i);
                if(weight<=0.00001f)anchored++;
                if(weight>=0.5f)moving++;
            }
            Check(anchored>0,"at least one real root remains stationary at all times");
            if(material.GetFloat("_SwayAmplitude")>0)
                Check(moving>0,"enabled water motion reaches the upper branches");
            return assertions-before;
        }

        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
#endif
