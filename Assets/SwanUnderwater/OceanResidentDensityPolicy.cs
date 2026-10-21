using System;

namespace SwanUnderwater
{
    /// <summary>Deterministic ambient-only population plan; never reads or unlocks collection/save state.</summary>
    public static class OceanResidentDensityPolicy
    {
        public const int ClassicHeroBudget = 120, SanctuaryHeroBudget = 144, EntranceHeroes = 30;
        public const int SchoolBudget = 18, MarineHeroSpecies = 9;
        static readonly string[] MarineNames = {
            "01_clownfish", "02_blue_tang", "23_mandarinfish", "03_lionfish", "05_pufferfish_inflated",
            "20_seahorse", "22_flounder", "21_ocean_sunfish", "04_anglerfish"
        };
        // All zones retain diversity, with a distinct first/most frequent group for their habitat.
        // IDs here are local art indices, NOT durable collection IDs.
        static readonly int[][] ZoneSpecies = {
            new[] { 0,1,2,4,3,5,6,7,8 }, // entrance reef
            new[] { 3,7,1,4,2,0,6,8,5 }, // limestone shelves
            new[] { 4,2,0,1,3,6,5,7,8 }, // sheltered courtyards
            new[] { 5,6,4,2,1,0,7,3,8 }, // seagrass / sand
            new[] { 1,4,0,2,5,6,3,7,8 }, // lagoon
            new[] { 3,2,7,1,4,0,5,8,6 }, // sea-fan / fissures
            new[] { 8,7,3,6,4,1,2,5,0 }  // deeper canyon
        };

        public static int HeroCount(OceanEnvironmentKind kind, int configured) =>
            kind == OceanEnvironmentKind.CoralSanctuary ? SanctuaryHeroBudget : ClassicHeroBudget;
        // Use the same count for both cached environments: existing member indexing uses this global field.
        public static int SchoolCount(int configured, int fishBudget) => Math.Max(1, Math.Min(fishBudget, SchoolBudget));
        public static int HeroZone(int index, int zoneCount = 7)
        {
            if (index < 0 || zoneCount < 1) throw new ArgumentOutOfRangeException();
            return zoneCount == 1 || index < EntranceHeroes ? 0 : 1 + (index - EntranceHeroes) % (zoneCount - 1);
        }
        public static int HeroLocalOrdinal(int index, int zoneCount = 7)
        {
            if (index < 0 || zoneCount < 1) throw new ArgumentOutOfRangeException();
            return zoneCount == 1 || index < EntranceHeroes ? index : (index - EntranceHeroes) / (zoneCount - 1);
        }
        public static string HeroResourceName(int index, OceanEnvironmentKind kind, int zoneCount = 7)
        {
            int zone = kind == OceanEnvironmentKind.CoralSanctuary ? HeroZone(index, zoneCount) : index % ZoneSpecies.Length;
            int ordinal = kind == OceanEnvironmentKind.CoralSanctuary ? HeroLocalOrdinal(index, zoneCount) : index / ZoneSpecies.Length;
            int[] choices = ZoneSpecies[zone % ZoneSpecies.Length];
            return MarineNames[choices[ordinal % choices.Length]];
        }
        public static int SchoolZone(int index, int zoneCount = 7)
        {
            if (index < 0 || zoneCount < 1) throw new ArgumentOutOfRangeException();
            return zoneCount == 1 || index < 3 ? 0 : 1 + (index - 3) % (zoneCount - 1);
        }
        public static bool IsSchoolReefResident(int schoolIndex) => schoolIndex != 2 && schoolIndex != 5 && schoolIndex != 8 && schoolIndex != 11;
        /// <summary>Existing normalization measures a conservative model diagonal, not anatomical nose-to-tail length.</summary>
        public static float SchoolLength(int schoolIndex, int memberIndex) => .09f + ((schoolIndex * 7 + memberIndex * 3) % 7) * .01f;
        public static bool IsBottomResident(string name) => name != null &&
            (name.IndexOf("flounder", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("moray", StringComparison.OrdinalIgnoreCase) >= 0 ||
             name.IndexOf("angler", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("seahorse", StringComparison.OrdinalIgnoreCase) >= 0);
        public static float HeroLength(string name, int index, float fallback)
        {
            float variant = .90f + (index % 5) * .05f;
            if (name == null) return fallback;
            if (name.IndexOf("sunfish", StringComparison.OrdinalIgnoreCase) >= 0) return .72f * variant;
            if (name.IndexOf("moray", StringComparison.OrdinalIgnoreCase) >= 0) return .9f * variant;
            if (name.IndexOf("seahorse", StringComparison.OrdinalIgnoreCase) >= 0) return .30f * variant;
            if (name.IndexOf("mandarinfish", StringComparison.OrdinalIgnoreCase) >= 0) return .32f * variant;
            if (name.IndexOf("clownfish", StringComparison.OrdinalIgnoreCase) >= 0) return .34f * variant;
            if (name.IndexOf("lionfish", StringComparison.OrdinalIgnoreCase) >= 0) return .58f * variant;
            if (name.IndexOf("blue_tang", StringComparison.OrdinalIgnoreCase) >= 0) return .48f * variant;
            if (name.IndexOf("flounder", StringComparison.OrdinalIgnoreCase) >= 0) return .48f * variant;
            if (name.IndexOf("puffer", StringComparison.OrdinalIgnoreCase) >= 0) return .45f * variant;
            if (name.IndexOf("angler", StringComparison.OrdinalIgnoreCase) >= 0) return .43f * variant;
            return fallback;
        }
        public static float LocalRadius(int localOrdinal) => localOrdinal % 3 == 0 ? 11f + localOrdinal % 4 * 1.8f : 3.5f + localOrdinal % 5 * 1.5f;
    }
}
