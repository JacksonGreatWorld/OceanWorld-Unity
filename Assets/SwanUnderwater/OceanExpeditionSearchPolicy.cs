using System;

namespace SwanUnderwater
{
    /// <summary>Expansion search bounds follow the authored search radius, independently of the global chapter number.</summary>
    public static class OceanExpeditionSearchPolicy
    {
        public static float MinimumDistance(OceanLevelDefinition level, bool coin)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (!coin) return 3f;
            // These published layouts and save identities retain their original placement contract.
            if (level.Level <= OceanLevelCatalog.LegacyLevelCount) return 5f + level.Chapter;
            return Math.Min(8f, Math.Max(4f, level.SearchRadius * .65f));
        }
        public static bool ContainsDistance(OceanLevelDefinition level, float distance, bool coin)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return false;
            return distance >= MinimumDistance(level, coin) && distance <= level.SearchRadius + 8f;
        }
    }
}
