using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Preserve real source detail and stable oblique sampling, without artificial upscaling.</summary>
    public static class OceanClarityPolicy
    {
        static readonly HashSet<Texture2D> prepared = new HashSet<Texture2D>();
        public static void ApplyGlobal()
        {
            prepared.Clear();
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.maximumLODLevel = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Debug.Log("[OceanClarity] full source mip resolution; highest model LOD allowed; trilinear/16x anisotropic sampling; eye scale remains fixed");
        }
        public static void PrepareTexture(Texture texture)
        {
            if (!(texture is Texture2D image) || !prepared.Add(image)) return;
            image.filterMode = FilterMode.Trilinear;
            image.anisoLevel = 16;
            image.mipMapBias = 0f;
        }
        public static void ReportReady()
        {
            int mipmapped = 0, maxSize = 0;
            foreach (Texture2D texture in prepared)
            {
                if (texture == null) continue;
                if (texture.mipmapCount > 1) mipmapped++;
                maxSize = Mathf.Max(maxSize, texture.width, texture.height);
            }
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Debug.Log($"[OceanClarity] ready texturesPrepared={prepared.Count} mipmapped={mipmapped} maxSize={maxSize} sourceMipLimit={QualitySettings.globalTextureMipmapLimit} maxLOD={QualitySettings.maximumLODLevel} anisotropic={QualitySettings.anisotropicFiltering} msaa={(pipeline != null ? pipeline.msaaSampleCount : 0)}");
        }
        public static void PrepareModelTextures(GameObject model)
        {
            if (model == null) return;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) foreach (string property in material.GetTexturePropertyNames())
                        PrepareTexture(material.GetTexture(property));
        }
    }
}
