using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SwanUnderwater
{
    /// <summary>One directional light; mobile 4-tap PCF and bounded two-cascade coverage.</summary>
    public static class OceanReefShadowPolicy
    {
        public const int AtlasResolution = 2048;
        public const int Cascades = 2;
        public const float Distance = 24f;
        public const float NearSplit = .42f;
        public const float DepthBias = .35f;
        public const float NormalBias = .4f;
        public const float Strength = .8f;

        public static void ConfigureSun(Light sun)
        {
            if (sun == null) return;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Strength;
            // URP reads bias from the active pipeline. Set Low explicitly as its
            // ShadowUtils bias calculation otherwise uses a 2.5-texel kernel for
            // UsePipelineSettings, even when the mobile pipeline selects Low.
            UniversalAdditionalLightData data = sun.GetUniversalAdditionalLightData();
            data.usePipelineSettings = true;
            data.softShadowQuality = SoftShadowQuality.Low;
        }
    }
}
