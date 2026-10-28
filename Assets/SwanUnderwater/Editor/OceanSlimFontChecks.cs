#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Checks the disabled-font exclusion and real retained glyphs without creating player progress.</summary>
    public static class OceanSlimFontChecks
    {
        public static int Assertions { get; private set; }

        static void Check(bool condition, string message)
        {
            Assertions++;
            if (!condition) throw new BuildFailedException("Slim font: " + message);
        }

        [MenuItem("OceanWorld/Validation/Slim release font and glyphs")]
        public static void RunAll()
        {
            Assertions = 0;
            Check(!EditorApplication.isCompiling && !EditorApplication.isUpdating, "wait for Unity import completion");
            Check(!OceanReleaseFeatures.LeaderboardsEnabled, "leaderboard-disabled release required");
            OceanPlayerContentBuildChecks.RunAll();
            Check(Resources.Load<Font>("Leaderboard/LeaderboardFont") == null,
                "disabled full CJK font must no longer be loadable from Resources");

            Font body = RequiredFont("Treasure/TreasureFont", "Assets/Resources/Treasure/TreasureFont.otf");
            RequiredFont("Loading/OceanLoadingFont", "Assets/Resources/Loading/OceanLoadingFont.otf");
            Font title = RequiredFont("Loading/OceanTitleFont", "Assets/Resources/Loading/OceanTitleFont.ttf");
            CheckGlyphs(body, "探索菜单", "继续探索 重试本关 选择关卡 收藏 图鉴 成就 设置 教学 性能面板 开 关 未开放，敬请期待");
            for (int level=1;level<=OceanCampaignAvailability.AvailableLevelCount;level++)
            {
                OceanLevelDefinition definition=OceanLevelCatalog.Get(level);
                CheckGlyphs(body, "关卡名称 " + level, definition.Name);
                CheckGlyphs(body, "关卡说明 " + level, definition.Brief);
                CheckGlyphs(body, "探索提示 " + level, definition.MechanicHint);
            }
            CheckGlyphs(title, "主标题", "海底世界");
            int available = 0;
            foreach (var species in OceanSpeciesCatalog.All)
            {
                if (!OceanSpeciesCatalog.IsAvailable(species.Id)) continue;
                available++;
                string context = "observation " + species.Id;
                CheckGlyphs(body, context + " name", species.Name);
                CheckGlyphs(body, context + " category", species.Category);
                CheckGlyphs(body, context + " description", species.Description);
                CheckGlyphs(body, context + " habitat", species.Habitat);
                CheckGlyphs(body, context + " search hint", species.SearchHint);
            }
            Check(available == 30 && available == OceanSpeciesCatalog.AvailableCount,
                "retain all thirty currently available observation categories");
            Debug.Log("OceanSlimFontChecks PASS " + Assertions +
                " assertions; retained fonts and catalog glyphs checked. No UI rendering, headset experience or APK exclusion claim.");
        }

        static Font RequiredFont(string key, string expectedSource)
        {
            Font font = Resources.Load<Font>(key);
            Check(font != null && AssetDatabase.GetAssetPath(font) == expectedSource,
                "retain the original gameplay font at " + expectedSource);
            return font;
        }

        static void CheckGlyphs(Font font, string context, string value)
        {
            Check(font != null && value != null, context + " has a real font and text");
            var checkedCharacters = new HashSet<char>();
            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character) || !checkedCharacters.Add(character)) continue;
                Check(font.HasCharacter(character), context + " missing U+" + ((int)character).ToString("X4"));
            }
        }
    }
}
#endif
