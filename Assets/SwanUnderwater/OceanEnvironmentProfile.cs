using System;

namespace SwanUnderwater
{
    public enum OceanEnvironmentKind { Classic, CoralSanctuary }

    /// <summary>Environment choice is derived from the stable level ID, never added to the save schema.</summary>
    public static class OceanEnvironmentProfile
    {
        public const int SanctuaryFirstLevel = 6;
        public const float SpawnX = 0f, SpawnZ = -4f;
        public const float SanctuaryPlayableHalfExtent = 110f;
        public const float SanctuaryBoundaryFadeStart = 96f;
        public const float SanctuaryTerrainHalfExtent = 128f;
        public const float SanctuaryMediumGridSpacing = 2f;
        // Vista silhouettes remain outside both the swimmer and the detailed floor.
        public const float SanctuaryVistaMinimumRadius = 148f;

        /// <summary>Reduce only outward travel near the edge; returning, sliding and vertical travel stay free.</summary>
        public static float LimitSanctuaryOutwardStep(float eyeFromSpawn, float step, float bodyClearance)
        {
            if (eyeFromSpawn * step <= 0f) return step;
            float safeExtent = SanctuaryPlayableHalfExtent - Math.Max(0f, bodyClearance);
            float remaining = Math.Max(0f, safeExtent - Math.Abs(eyeFromSpawn));
            float t = Math.Max(0f, Math.Min(1f, (Math.Abs(eyeFromSpawn) - SanctuaryBoundaryFadeStart) /
                Math.Max(.01f, safeExtent - SanctuaryBoundaryFadeStart)));
            float scale = 1f - t * t * (3f - 2f * t);
            return Math.Sign(step) * Math.Min(Math.Abs(step) * scale, remaining);
        }

        /// <summary>Clamp the tracked eye, rather than an XR Origin whose room-scale offset can differ.</summary>
        public static float ClampSanctuaryEyeCoordinate(float eyeFromSpawn, float bodyClearance)
        {
            float extent = SanctuaryPlayableHalfExtent - Math.Max(0f, bodyClearance);
            return Math.Max(-extent, Math.Min(extent, eyeFromSpawn));
        }

        public static OceanEnvironmentKind ForLevel(int level)
        {
            if (level < 1 || level > OceanLevelCatalog.Count) throw new ArgumentOutOfRangeException(nameof(level));
            return level < SanctuaryFirstLevel ? OceanEnvironmentKind.Classic : OceanEnvironmentKind.CoralSanctuary;
        }
    }
}
