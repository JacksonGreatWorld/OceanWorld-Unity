namespace SwanUnderwater
{
    /// <summary>Published content scope, independent of durable catalog and saved history.</summary>
    public static class OceanCampaignAvailability
    {
        public const int AvailableLevelCount = 20;
        public const string UnavailableMessage = "未开放，敬请期待";
        public static bool IsOpen(int level) => level >= 1 && level <= AvailableLevelCount;
        public static bool IsFinalLevel(int level) => level == AvailableLevelCount;
        // An archived later run stays intact while the menu uses a safe, published backdrop.
        public static int EntryLevel(int savedLevel) => IsOpen(savedLevel) ? savedLevel : 1;
        public static bool IsDeferredAchievement(string id)
        {
            switch (id)
            {
                case "biomes-3": case "biomes-5": case "levels-50": case "levels-100":
                case "stars-100": case "mechanics-1": case "mechanics-8": case "mechanics-16": case "campaign-finished":
                    return true;
                default: return false;
            }
        }
    }
}
