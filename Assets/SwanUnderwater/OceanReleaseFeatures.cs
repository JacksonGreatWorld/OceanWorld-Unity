namespace SwanUnderwater
{
    /// <summary>Product capabilities of this release; never overridden by saved preferences or a server.</summary>
    public static class OceanReleaseFeatures
    {
        // Rankings and nickname entry are not offered in this release. The local
        // journals remain available to collection/achievement reconciliation only.
        public static bool LeaderboardsEnabled => false;
    }
}
