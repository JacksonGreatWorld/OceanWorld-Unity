using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>CPU reference for the shared opaque-floor / sky extinction contract.</summary>
    public static class OceanWaterOptics
    {
        public static Color Bottom(OceanEnvironmentKind kind) => kind == OceanEnvironmentKind.CoralSanctuary
            ? new Color(.008f, .065f, .13f) : new Color(.008f, .07f, .09f);

        public static Color Background(Color horizon, Color bottom, float elevation) =>
            Color.Lerp(horizon, bottom, Mathf.SmoothStep(0, 1, Mathf.Clamp01(-elevation / .65f)));

        public static float Transmission(float distance, float density)
        {
            if (float.IsNaN(distance) || float.IsNaN(density) || distance < 0 || density < 0) return 0;
            float opticalDepth = distance * density;
            return Mathf.Exp(-opticalDepth * opticalDepth);
        }
    }
}
