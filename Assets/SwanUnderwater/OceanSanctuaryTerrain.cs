using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Fixed bathymetry, shared by the sanctuary mesh and every floor query.</summary>
    internal static class OceanSanctuaryTerrain
    {
        internal static float Relief(float worldX, float worldZ)
        {
            float x = worldX - OceanEnvironmentProfile.SpawnX;
            float z = worldZ - OceanEnvironmentProfile.SpawnZ;
            // Keep the accepted entrance, arch footings and both entry corridors exact.
            float outsideX = Mathf.Max(0f, Mathf.Abs(x) - 28f);
            float outsideZ = Mathf.Max(0f, Mathf.Max(-26f - z, z - 46f));
            if (outsideX == 0f && outsideZ == 0f) return 0f;
            if (Mathf.Abs(x) > 132f || z < -128f || z > 132f) return 0f;
            float entryBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ) / 14f);

            // Unequal, offset basins have distinct depth and direction; use the deepest
            // influence rather than adding overlapping holes into an unplayable trench.
            float cut = 3.1f * Ellipse(x, z, -61f, 8f, 27f, 23f, .24f);
            cut = Mathf.Max(cut, 4.2f * Ellipse(x, z, 4f, -66f, 30f, 24f, -.18f));
            cut = Mathf.Max(cut, 4.6f * Ellipse(x, z, 73f, 7f, 21f, 38f, -.16f));
            cut = Mathf.Max(cut, 3.5f * Ellipse(x, z, -7f, 80f, 29f, 23f, .32f));

            float northX = -6f + 12f * Mathf.Sin(z * .045f) - 5f * Mathf.Sin(z * .09f);
            float southX = 10f + 24f * Mathf.Sin((z + 10f) * .025f);
            float crossZ = -9f + 15f * Mathf.Sin((x + 6f) * .032f);
            cut = Mathf.Max(cut, 1.35f * Channel(x - northX, 12f, z, 18f, 111f));
            cut = Mathf.Max(cut, 1.65f * Channel(x - southX, 13f, z, -111f, -16f));
            cut = Mathf.Max(cut, 1.25f * Channel(z - crossZ, 11f, x, -111f, 111f));

            // Broad low limestone benches break up the sand without making another
            // ring of mountains. Compact profiles are flat at their centre and rim.
            float shelf = 1.6f * Ellipse(x, z, -80f, 39f, 22f, 31f, -.25f);
            shelf = Mathf.Max(shelf, 1.3f * Ellipse(x, z, -43f, -44f, 24f, 19f, .28f));
            shelf = Mathf.Max(shelf, 1.7f * Ellipse(x, z, 56f, -46f, 25f, 22f, -.21f));
            shelf = Mathf.Max(shelf, 1.45f * Ellipse(x, z, 43f, 57f, 23f, 30f, .22f));
            return (shelf - cut) * entryBlend;
        }

        static float Ellipse(float x, float z, float cx, float cz, float rx, float rz, float shear)
        {
            float dz = z - cz;
            float u = (x - cx + shear * dz) / rx, v = dz / rz;
            float q = u * u + v * v;
            if (q >= 1f) return 0f;
            float t = 1f - q;
            return t * t * (3f - 2f * t);
        }

        static float Channel(float across, float halfWidth, float along, float start, float end)
        {
            if (along <= start || along >= end || Mathf.Abs(across) >= halfWidth) return 0f;
            float t = 1f - (across * across) / (halfWidth * halfWidth);
            float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(along - start, end - along) / 14f);
            return t * t * (3f - 2f * t) * ends;
        }
    }
}
