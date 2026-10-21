#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class OceanWaterOpticsTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string text)
        { Assertions++; if (!condition) throw new InvalidOperationException("Water optics: " + text); }
        public static void RunAll()
        {
            Assertions = 0;
            Color horizon = new Color(.025f, .205f, .32f);
            Color bottom = OceanWaterOptics.Bottom(OceanEnvironmentKind.CoralSanctuary);
            Check(OceanWaterOptics.Background(horizon, bottom, 0) == horizon, "horizon matches exact fog colour");
            Check(OceanWaterOptics.Background(horizon, bottom, -1) == bottom, "deep lower background matches");
            // The old distance-only floor tends to horizon at every elevation. This
            // counterexample exercises the reported seam rather than just a property.
            Color lower = OceanWaterOptics.Background(horizon, bottom, -.35f);
            Check(Vector3.Distance(new Vector3(horizon.r,horizon.g,horizon.b), new Vector3(lower.r,lower.g,lower.b)) > .05f,
                "negative control: old flat fog cannot meet a lower sky");
            float previous = 1;
            for (int metres=0; metres<=180; metres++)
            {
                float t=OceanWaterOptics.Transmission(metres,.032f);
                Check(t>=0 && t<=previous, "extinction remains monotonic"); previous=t;
                for (int angle=0; angle<=65; angle+=5)
                {
                    Color target=OceanWaterOptics.Background(horizon,bottom,-angle/100f);
                    Color floor=Color.Lerp(target,Color.white,t);
                    if (metres>=100) Check(Mathf.Abs(floor.b-target.b)<.00005f, "far opaque floor converges to directional background");
                }
            }
            Check(OceanWaterOptics.Transmission(float.NaN,.032f)==0, "invalid input cannot create bright discontinuity");
            Debug.Log("OceanWaterOpticsTests PASS: " + Assertions + " assertions; mathematical seam contract only; rendered/wearer review separate.");
        }
    }
}
#endif
