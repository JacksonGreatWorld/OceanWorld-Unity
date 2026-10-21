using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.Editor
{
    /// <summary>
    /// Auto-configures texture import settings for our downloaded PBR assets.
    /// </summary>
    public sealed class TextureImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string lower = assetPath.ToLowerInvariant();
            // Launcher artwork has its own lossless import policy in BrandingSetup.
            if (lower.StartsWith("assets/branding/")) return;
            TextureImporter ti = (TextureImporter)assetImporter;

            bool isNormal = lower.Contains("normalgl") || lower.Contains("_normal") || lower.Contains("normalmap");
            bool isBubble = lower.Contains("bubble") || lower.Contains("circle_");

            if (isNormal)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.sRGBTexture = false;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = !isBubble;
            }

            ti.mipmapEnabled = !isBubble;
            // Environment PBR textures (sand/rock) at 2048; bubbles and others at 1024
            bool isEnvTex = lower.Contains("sand") || lower.Contains("ground") || lower.Contains("rock") || lower.Contains("ground101") || lower.Contains("rock019");
            ti.maxTextureSize = isEnvTex ? 2048 : 1024;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = isEnvTex ? 8 : 4;

            if (lower.Contains("sand") || lower.Contains("ground") || lower.Contains("rock"))
            {
                ti.wrapMode = TextureWrapMode.Repeat;
            }
        }
    }
}
