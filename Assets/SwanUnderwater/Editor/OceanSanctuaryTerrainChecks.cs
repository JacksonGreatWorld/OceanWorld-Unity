#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Native height-field invariants; mesh seams/PhysX routes remain covered by exploration checks.</summary>
    public static class OceanSanctuaryTerrainChecks
    {
        public static int Assertions { get; private set; }

        static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new InvalidOperationException("Sanctuary terrain: " + message);
        }

        static float Offset(float x, float z)
        {
            x += OceanEnvironmentProfile.SpawnX; z += OceanEnvironmentProfile.SpawnZ;
            return UnderwaterWorld.SampleTerrainHeight(x, z, OceanEnvironmentKind.CoralSanctuary) -
                UnderwaterWorld.SampleTerrainHeight(x, z, OceanEnvironmentKind.Classic);
        }

        public static int RunAll()
        {
            Assertions = 0;
            for (float x = -28f; x <= 28f; x += 2f)
                for (float z = -26f; z <= 46f; z += 2f)
                    Check(Offset(x, z) == 0f, "accepted entrance and arch heights preserved exactly");

            float minimum = 0f, maximum = 0f, maximumGradient = 0f;
            float floorMinimum = float.PositiveInfinity, floorMaximum = float.NegativeInfinity;
            for (float x = -110f; x <= 110f; x += 2f)
                for (float z = -110f; z <= 110f; z += 2f)
                {
                    float offset = Offset(x, z);
                    Check(!float.IsNaN(offset) && !float.IsInfinity(offset), "finite fixed bathymetry");
                    Check(offset == Offset(x, z), "queries do not depend on frame or swimmer position");
                    minimum = Mathf.Min(minimum, offset); maximum = Mathf.Max(maximum, offset);
                    float dx = (Offset(x + .1f, z) - Offset(x - .1f, z)) / .2f;
                    float dz = (Offset(x, z + .1f) - Offset(x, z - .1f)) / .2f;
                    maximumGradient = Mathf.Max(maximumGradient, Mathf.Sqrt(dx * dx + dz * dz));
                    float floor = UnderwaterWorld.SampleRenderedSeabedHeight(x + OceanEnvironmentProfile.SpawnX,
                        z + OceanEnvironmentProfile.SpawnZ, OceanEnvironmentKind.CoralSanctuary);
                    floorMinimum = Mathf.Min(floorMinimum, floor); floorMaximum = Mathf.Max(floorMaximum, floor);
                }
            Check(minimum <= -4.5f && minimum >= -4.601f, "meaningful bounded ravine depth");
            Check(maximum >= 1.5f && maximum <= 1.701f, "low shelves remain below mountain scale");
            Check(maximumGradient < .65f, "new relief has gentle traversable slopes");
            Check(floorMaximum < .8f, "floor leaves headroom below the existing water ceiling");

            foreach (float x in new[] { -800f, -132.01f, 132.01f, 800f })
                foreach (float z in new[] { -800f, -132f, 0f, 132f, 800f })
                    Check(Offset(x, z) == 0f, "far boundary remains unchanged");
            foreach (float z in new[] { -128.01f, 132.01f })
                foreach (float x in new[] { -100f, 0f, 100f })
                    Check(Offset(x, z) == 0f, "no distant terrain displacement");
            Debug.Log($"OceanSanctuaryTerrainChecks PASS {Assertions} assertions; relief {minimum:F3}..{maximum:F3} m, " +
                $"maximum sampled relief slope {maximumGradient:F3}, rendered floor {floorMinimum:F3}..{floorMaximum:F3} m. " +
                "Deterministic/native height tests only; headset comfort and frame time require device review.");
            return Assertions;
        }
    }
}
#endif
