using System;

namespace SwanUnderwater
{
    /// <summary>Terrain sampling and authored biome are independent cache dimensions; legacy roots keep their identities.</summary>
    public readonly struct OceanEnvironmentIdentity : IEquatable<OceanEnvironmentIdentity>
    {
        public readonly OceanEnvironmentKind Terrain;
        public readonly OceanCampaignBiome Biome;
        public OceanEnvironmentIdentity(OceanEnvironmentKind terrain, OceanCampaignBiome biome)
        { Terrain = terrain; Biome = biome; }
        public static OceanEnvironmentIdentity ForLevel(int level) => new OceanEnvironmentIdentity(
            OceanEnvironmentProfile.ForLevel(level), level <= OceanLevelCatalog.LegacyLevelCount ? OceanCampaignBiome.Reef : OceanLevelCatalog.Get(level).Environment);
        public bool Equals(OceanEnvironmentIdentity other) => Terrain == other.Terrain && Biome == other.Biome;
        public override bool Equals(object obj) => obj is OceanEnvironmentIdentity other && Equals(other);
        public override int GetHashCode() => ((int)Terrain * 397) ^ (int)Biome;
        public override string ToString() => Terrain + " · " + Biome;
    }
}
