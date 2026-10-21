using System;
using System.Collections.Generic;
#if OCEAN_CAMPAIGN_STANDALONE
using System.IO;
#endif

namespace SwanUnderwater
{
    public enum OceanMechanicKind { None, CurrentRiding, OxygenRelay, DeepLight, Observation, Escort, KelpMaze, ShipwreckLock, JellyfishDodge, OctopusTracking, FishHerding, CoralRepair, Cleanup, TideGates, RingRace, RunePuzzle, ElectricFinale }
    public enum OceanCampaignBiome { Reef, CurrentChannel, Abyss, KelpForest, Shipwreck, DeepRuins }

    /// <summary>One immutable rule table shared by progression, presentation and world placement.</summary>
    public sealed class OceanLevelDefinition
    {
        public readonly int Level, ShellMask, FishMask, CoinCount, SharkHits, AmmoLimit;
        public readonly string Name, Brief;
        public readonly float TimeLimitSeconds, SearchRadius, FishSpeed, FishFleeDistance;
        public readonly bool OrderedFish, OrderedShell;
        public readonly OceanMechanicKind Mechanic;
        public readonly OceanCampaignBiome Environment;
        public readonly int MechanicTarget;
        public readonly float MechanicIntensity;
        public readonly string MechanicHint;
        public bool RequiresShark => SharkHits > 0;
        public int Chapter => (Level - 1) / 5;

        internal OceanLevelDefinition(int level, string name, string brief, int minutes, int shells,
            int fish, int coins, float radius, float speed, float flee, int sharkHits = 0,
            bool orderedFish = false, bool orderedShell = false,
            OceanMechanicKind mechanic = OceanMechanicKind.None, int mechanicTarget = 0,
            float mechanicIntensity = 0f, OceanCampaignBiome environment = OceanCampaignBiome.Reef, string mechanicHint = "")
        {
            Level = level; Name = name; Brief = brief; TimeLimitSeconds = minutes * 60f;
            ShellMask = shells; FishMask = fish; CoinCount = coins; SearchRadius = radius;
            FishSpeed = speed; FishFleeDistance = flee; SharkHits = sharkHits;
            AmmoLimit = sharkHits > 0 ? 20 : 0; OrderedFish = orderedFish; OrderedShell = orderedShell;
            Mechanic = mechanic; MechanicTarget = mechanicTarget; MechanicIntensity = mechanicIntensity;
            Environment = environment; MechanicHint = mechanicHint;
        }
    }

    public static class OceanLevelCatalog
    {
        public const int Count = 100, LegacyLevelCount = 20, FishSpeciesCount = 10, CoinIdBase = 20000;
        public static readonly string[] ChapterNames = { "初识海洋", "珊瑚迷踪", "深蓝追猎", "深海之冠", "洋流航道", "氧气接力", "深渊追光", "海洋观察", "海龟护航", "海藻迷宫", "沉船宝库", "水母回廊", "章鱼潜踪", "鱼群引导", "珊瑚新生", "净海行动", "潮汐机关", "气泡竞速", "蓝渊符文", "电鳗守卫" };
        // The first four indices are durable v1 collection identities; never reorder them.
        public static readonly string[] FishNames = { "小丑鱼", "蓝倒吊", "麒麟鱼", "狮子鱼", "河豚", "海马", "比目鱼", "鮟鱇鱼", "蝠鲼", "金枪鱼" };
        public static readonly string[] FishPaths = { "Fish/01_clownfish", "Fish/02_blue_tang", "Fish/23_mandarinfish", "Fish/03_lionfish",
            "Fish/05_pufferfish_inflated", "Fish/20_seahorse", "Fish/22_flounder", "Fish/04_anglerfish", "Fish/16_manta_ray", "Fish/12_bluefin_tuna" };
        public static readonly float[] FishLengths = { .30f, .36f, .30f, .42f, .32f, .24f, .42f, .38f, .75f, .65f };
        static int M(params int[] species) { int mask = 0; foreach (int value in species) mask |= 1 << value; return mask; }
        static readonly OceanLevelDefinition[] LegacyLevels = {
            // Preserve the five published levels so existing saves remain meaningful.
            new OceanLevelDefinition(1, "贝壳寻宝", "认识五种贝壳，沿沙地与珊瑚寻找", 30, 31, 0, 1, 7f, 1f, 0f),
            new OceanLevelDefinition(2, "海底珍藏", "收齐十种贝壳，留意草丛和礁洞", 20, 1023, 0, 1, 8f, 1f, 0f),
            new OceanLevelDefinition(3, "珊瑚伙伴", "找到三种鱼和两种贝壳", 20, 3, 7, 1, 8f, 1f, 0f),
            new OceanLevelDefinition(4, "鱼群巡游", "辨认并抓到四种指定鱼", 20, 0, 15, 1, 8f, 1f, 0f),
            new OceanLevelDefinition(5, "深海挑战", "找到渔枪，任一鲨鱼单独命中十次再抓取", 0, 0, 0, 1, 8f, 1f, 0f, 10),
            new OceanLevelDefinition(6, "珊瑚秘境", "穿过天然礁拱，寻找三种贝壳、三种鱼和隐藏金币", 18, M(2,4,6), M(0,1,4), 2, 8.5f, 1.02f, .65f),
            new OceanLevelDefinition(7, "草海捉迷藏", "在草丛寻找四种贝壳和三种鱼", 17, M(5,7,8,9), M(2,4,5), 2, 10f, 1.06f, .8f),
            new OceanLevelDefinition(8, "珊瑚接力", "按图鉴顺序抓四种鱼，再找两种贝壳", 16, M(1,3), M(0,1,2,4), 2, 10.5f, 1.10f, .9f, orderedFish:true),
            new OceanLevelDefinition(9, "暮色识鱼", "寻找五种鱼和三种贝壳，辨认比目鱼", 15, M(4,6,8), M(3,4,5,6,7), 2, 11f, 1.14f, 1f),
            new OceanLevelDefinition(10, "礁堡试炼", "收集目标后挑战鲨鱼，命中十一次", 15, M(2,5,9), M(2,4), 2, 11.5f, 1.18f, 1f, 11),
            new OceanLevelDefinition(11, "深蓝图鉴", "寻找六种鱼和四种贝壳，扩大探索范围", 14, M(0,3,6,8), M(0,1,2,3,4,5), 3, 12f, 1.20f, 1.1f),
            new OceanLevelDefinition(12, "追光鱼阵", "按顺序抓六种鱼，收集三种贝壳", 13, M(3,7,9), M(1,2,4,5,6,8), 3, 12.5f, 1.23f, 1.2f, orderedFish:true),
            new OceanLevelDefinition(13, "沙海潜踪", "搜寻六种鱼和五种贝壳，留意沙地伪装", 12, M(2,4,5,8,9), M(4,5,6,7,8,9), 3, 13f, 1.26f, 1.3f),
            new OceanLevelDefinition(14, "古礁密码", "按顺序找六种贝壳，并抓五种鱼", 12, M(0,2,4,6,8,9), M(0,3,5,7,9), 3, 13.5f, 1.29f, 1.4f, orderedShell:true),
            new OceanLevelDefinition(15, "蓝渊守卫", "四鱼四贝与鲨鱼，需命中十二次", 12, M(1,3,5,7), M(1,3,5,6), 3, 14f, 1.32f, 1.4f, 12),
            new OceanLevelDefinition(16, "洋流竞逐", "按顺序抓七种鱼，寻找五种贝壳", 11, M(0,1,4,7,9), M(0,1,2,4,5,6,7), 4, 14.5f, 1.35f, 1.5f, orderedFish:true),
            new OceanLevelDefinition(17, "无声猎手", "七种鱼与七种贝壳，贝壳按顺序收集", 11, M(1,2,3,5,7,8,9), M(1,2,3,5,6,7,9), 4, 15f, 1.38f, 1.6f, orderedShell:true),
            new OceanLevelDefinition(18, "深海大巡游", "抓齐十种鱼，再收集六种贝壳", 10, M(2,3,4,6,8,9), 1023, 4, 15.5f, 1.41f, 1.7f),
            new OceanLevelDefinition(19, "失落宝库", "按顺序抓十种鱼，收齐十种贝壳", 10, 1023, 1023, 4, 16f, 1.44f, 1.8f, orderedFish:true),
            new OceanLevelDefinition(20, "深海之冠", "十鱼十贝终极探索，鲨鱼需命中十四次", 10, 1023, 1023, 5, 17f, 1.48f, 1.9f, 14)
        };
        static readonly OceanLevelDefinition[] Levels = LoadLevels();
        static OceanLevelDefinition[] LoadLevels()
        {
            string json;
#if OCEAN_CAMPAIGN_STANDALONE
            string catalogPath = System.Environment.GetEnvironmentVariable("OCEAN_CAMPAIGN_CATALOG");
            json = File.ReadAllText(string.IsNullOrEmpty(catalogPath) ? "Assets/Resources/OceanCampaign/extended-levels.json" : catalogPath);
#else
            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("OceanCampaign/extended-levels");
            if (asset == null) throw new InvalidOperationException("Missing authored OceanCampaign/extended-levels resource.");
            json = asset.text;
#endif
            return ReadExtendedLevels(json);
        }
        /// <summary>Read once at startup. Malformed authored content fails closed instead of changing old rules.</summary>
        internal static OceanLevelDefinition[] ReadExtendedLevels(string json)
        {
            var root = OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
            if (OceanLeaderboardJson.Number(root, "version") != 1) throw new FormatException("Unknown extended campaign catalog version.");
            var rows = OceanLeaderboardJson.Array(root, "levels");
            if (rows.Count != Count - LegacyLevelCount) throw new FormatException("Extended catalog must contain levels 21–100 exactly.");
            var levels = new OceanLevelDefinition[Count]; Array.Copy(LegacyLevels, levels, LegacyLevelCount);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = OceanLeaderboardJson.Object(rows[i]);
                int level = CatalogInt(row, "level", LegacyLevelCount + 1, Count);
                if (level != i + LegacyLevelCount + 1) throw new FormatException("Extended levels must be consecutive and ordered.");
                var mechanic = (OceanMechanicKind)CatalogInt(row, "mechanic", 1, 16);
                if ((int)mechanic != (level - LegacyLevelCount - 1) / 5 + 1) throw new FormatException("Every new five-level tier introduces its own mechanic.");
                int seconds = CatalogInt(row, "seconds", 60, 600);
                if (seconds % 60 != 0) throw new FormatException("Catalog time limits must be whole minutes.");
                string name = CatalogText(row, "name"), brief = CatalogText(row, "brief"), hint = CatalogText(row, "hint");
                int sharkHits = CatalogInt(row, "sharkHits", 0, 18);
                levels[level - 1] = new OceanLevelDefinition(level, name, brief, seconds / 60,
                    CatalogInt(row, "shellMask", 0, 1023), CatalogInt(row, "fishMask", 0, 1023), CatalogInt(row, "coins", 1, 5),
                    CatalogInt(row, "radiusCentimeters", 600, 1800) / 100f, CatalogInt(row, "fishSpeedPercent", 70, 148) / 100f,
                    CatalogInt(row, "fleeCentimeters", 0, 190) / 100f, sharkHits, false, false, mechanic,
                    CatalogInt(row, "target", 1, 64), CatalogInt(row, "intensityPercent", 25, 200) / 100f,
                    (OceanCampaignBiome)CatalogInt(row, "environment", 0, 5), hint);
            }
            return levels;
        }
        static int CatalogInt(Dictionary<string, object> row, string key, int minimum, int maximum)
        { long value = OceanLeaderboardJson.Number(row, key); if (value < minimum || value > maximum) throw new FormatException("Invalid campaign field: " + key); return (int)value; }
        static string CatalogText(Dictionary<string, object> row, string key)
        { string value = OceanLeaderboardJson.Text(row, key); if (string.IsNullOrWhiteSpace(value) || value.Length > 180) throw new FormatException("Invalid campaign text: " + key); return value; }
        public static OceanLevelDefinition Get(int level)
        {
            if (level < 1 || level > Count) throw new ArgumentOutOfRangeException(nameof(level));
            return Levels[level - 1];
        }
        public static int CoinId(int slot)
        {
            if (slot < 0 || slot >= 5) throw new ArgumentOutOfRangeException(nameof(slot));
            return CoinIdBase + slot;
        }
    }
}
