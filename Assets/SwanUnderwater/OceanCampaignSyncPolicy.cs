using System;
using System.Collections.Generic;

namespace SwanUnderwater
{
    /// <summary>An absent/old server capability never authorizes rejecting new campaign events.</summary>
    public static class OceanCampaignSyncPolicy
    {
        public const int LegacyMaximumLevel = 20;
        public static int MaximumLevel(IDictionary<string, object> capability)
        {
            if (capability == null || !capability.TryGetValue("maxCampaignLevel", out object value)) return LegacyMaximumLevel;
            // Parser numbers are Int64 or Double. Strings and fractions are not a capability.
            if (!(value is long) && !(value is int) && !(value is double)) return LegacyMaximumLevel;
            double number = Convert.ToDouble(value);
            return double.IsNaN(number) || double.IsInfinity(number) || number != Math.Floor(number) ||
                number < LegacyMaximumLevel || number > OceanLevelCatalog.Count ? LegacyMaximumLevel : (int)number;
        }
    }
}
