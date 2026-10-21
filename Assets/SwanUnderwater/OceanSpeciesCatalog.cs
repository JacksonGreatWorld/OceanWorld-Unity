using System;
using System.Collections.Generic;

namespace SwanUnderwater
{
    /// <summary>A stable observation category, not a claim of species-level model identification.</summary>
    public sealed class OceanSpeciesDefinition
    {
        public readonly int Id, FirstLevel;
        public readonly string Name, Category, ResourcePath, Description, Habitat, SearchHint;
        public readonly bool Rare;

        internal OceanSpeciesDefinition(int id, int firstLevel, string name, string category,
            string resourcePath, string description, string habitat, string searchHint, bool rare)
        {
            Id = id; FirstLevel = firstLevel; Name = name; Category = category;
            ResourcePath = resourcePath; Description = description; Habitat = habitat;
            SearchHint = searchHint; Rare = rare;
        }
    }

    /// <summary>Append-only IDs. IDs 0-9 match OceanLevelCatalog; 15-24 match TreasureHunt shell kinds.</summary>
    public static class OceanSpeciesCatalog
    {
        public const int CatalogVersion = 3, Count = 31;
        // Durable identities and wire counts remain append-only. ID13 is archived:
        // its static moray mesh has no verified swim, so it is not a current objective.
        public const int AvailableCount = Count - 1;
        public static bool IsAvailable(int id) => id >= 0 && id < Count && id != 13;
        public static int CountForVersion(int version) => version == 1 ? 27 : version == 2 ? 28 : version == CatalogVersion ? Count :
            throw new ArgumentOutOfRangeException(nameof(version), "Unsupported observation catalog.");
        // Empty shell paths select existing procedural geometry; coral paths require the market bake.
        // FirstLevel and SearchHint are game design metadata, not biological depth/range assertions.
        static readonly IReadOnlyList<OceanSpeciesDefinition> Definitions = Array.AsReadOnly(new[]
        {
            new OceanSpeciesDefinition(0, 1, "小丑鱼", "鱼类", "Fish/01_clownfish",
                "常在海葵附近活动，与海葵相互庇护。模型类别，未细分到种。",
                "浅水珊瑚礁与海葵周围", "沿明亮礁边慢游，留意橙白条纹。", false),
            new OceanSpeciesDefinition(1, 1, "蓝倒吊", "鱼类", "Fish/02_blue_tang",
                "蓝色体侧与黄色尾部很醒目。沿用游戏名称，模型未作精确种鉴定。",
                "暖水珊瑚礁水域", "观察珊瑚外侧的蓝色游鱼。", false),
            new OceanSpeciesDefinition(2, 3, "麒麟鱼", "鱼类", "Fish/23_mandarinfish",
                "体表花纹丰富，常在礁石附近寻找小型食物。模型类别，未细分到种。",
                "浅水礁石与碎珊瑚间隙", "放慢速度，看看礁石底部的彩色小鱼。", true),
            new OceanSpeciesDefinition(3, 4, "狮子鱼", "鱼类", "Fish/03_lionfish",
                "展开的胸鳍和长棘很显眼；现实中应保持距离，避免触碰有毒棘。模型类别。",
                "热带礁石与洞穴周围", "在礁壁阴影旁观察展开的长鳍。", false),
            new OceanSpeciesDefinition(4, 6, "河豚", "鱼类", "Fish/05_pufferfish_inflated",
                "河豚类受到威胁时可鼓起身体。此处是鼓胀外形模型，未细分到种。",
                "暖水礁区及邻近沙底", "在礁石与沙地交界处找圆鼓鼓的轮廓。", false),
            new OceanSpeciesDefinition(5, 7, "海马", "鱼类", "Fish/20_seahorse",
                "海马属于鱼类，卷曲尾巴能攀附周围物体。模型类别，未细分到种。",
                "海草、藻丛与枝状庇护物旁", "轻轻靠近草叶，留意竖直停留的小身影。", true),
            new OceanSpeciesDefinition(6, 9, "比目鱼", "鱼类", "Fish/22_flounder",
                "比目鱼类身体扁平，常贴近海床；成鱼双眼位于身体同一侧。模型类别。",
                "沙质或泥质海床", "贴着沙谷边缘观察，不要只看水中。", false),
            new OceanSpeciesDefinition(7, 9, "鮟鱇鱼", "鱼类", "Fish/04_anglerfish",
                "部分深海鮟鱇用发光诱饵吸引猎物。游戏模型呈现这类外形，未细分到种。",
                "深海低光环境；游戏中为深色观察点", "留意背光礁隙中的微小亮点。", true),
            new OceanSpeciesDefinition(8, 12, "蝠鲼", "鱼类", "Fish/16_manta_ray",
                "宽大的胸鳍像翅膀，蝠鲼类以水中的浮游食物为食。模型类别，未细分到种。",
                "礁区外侧与开阔水域", "离开低矮珊瑚，抬头观察宽翼轮廓。", true),
            new OceanSpeciesDefinition(9, 13, "金枪鱼", "鱼类", "Fish/12_bluefin_tuna",
                "金枪鱼类善于在开阔海域持续游动。模型文件称蓝鳍金枪鱼，未鉴定具体种。",
                "开阔海域", "望向外海，跟随快速巡游的梭形身影。", false),
            new OceanSpeciesDefinition(10, 5, "大白鲨", "鱼类", "Fish/15_great_white_shark",
                "深色背部与浅色腹部形成反差，是大型海洋捕食者。外形依据现有模型。",
                "沿岸至外海水域", "保持距离，在礁区外围观察大型背鳍。", false),
            new OceanSpeciesDefinition(11, 10, "双髻鲨", "鱼类", "Fish/14_hammerhead_shark",
                "头部横向展开，形成锤形轮廓。双髻鲨有多个种，本模型未细分。",
                "沿岸礁区至外海", "到开阔水道观察横向展开的头部。", true),
            new OceanSpeciesDefinition(12, 9, "剑鱼", "鱼类", "Fish/13_swordfish",
                "细长扁平的上颌像一把剑，是明显的识别特征。外形依据现有模型。",
                "开阔海域与较深水层", "在外海水道停留，观察长剑状吻部。", true),
            new OceanSpeciesDefinition(13, 12, "海鳝", "鱼类", "Fish/19_moray_eel",
                "细长身体适合穿行礁隙，张合嘴部可帮助水流经过鳃。模型类别，未细分到种。",
                "岩礁裂隙与洞口", "沿礁壁侧面寻找狭窄的背光缝隙。", true),
            new OceanSpeciesDefinition(14, 14, "翻车鱼", "鱼类", "Fish/21_ocean_sunfish",
                "扁高的身体配上显眼的背鳍和臀鳍，轮廓十分特别。翻车鱼类模型，未细分到种。",
                "开阔海域，亦会到表层活动", "探索较远的开阔水域，抬头找圆盘状身影。", true),
            new OceanSpeciesDefinition(15, 1, "扇贝", "贝壳", "",
                "扇形外壳常有放射状纹理。此条记录贝壳模型外形，不代表已鉴定的活体物种。",
                "海底贝壳形态收藏", "沿沙地寻找扇形边缘。", false),
            new OceanSpeciesDefinition(16, 1, "海螺", "贝壳", "",
                "海螺是多类海洋腹足动物的通称，壳体常呈螺旋形。本条为模型类别。",
                "海底贝壳形态收藏", "翻看礁石旁的螺旋轮廓。", false),
            new OceanSpeciesDefinition(17, 1, "宝螺", "贝壳", "",
                "圆润的壳背和狭长的壳口是宝螺类的常见外形。本条为贝壳模型类别。",
                "海底贝壳形态收藏", "在珊瑚背光处寻找圆润的小壳。", false),
            new OceanSpeciesDefinition(18, 1, "蛤蜊", "贝壳", "",
                "蛤蜊属于双壳类，软体由两片壳保护。本条为贝壳模型类别，未细分到种。",
                "海底贝壳形态收藏", "观察沙地上半露出的圆形双壳。", false),
            new OceanSpeciesDefinition(19, 1, "锥螺", "贝壳", "",
                "细长螺塔是本模型的识别点；游戏名为外形类别，不等同于芋螺等精确分类。",
                "海底贝壳形态收藏", "留意细长的螺旋尖塔。", true),
            new OceanSpeciesDefinition(20, 2, "蚶贝", "贝壳", "",
                "本模型以厚实壳体和明显放射肋表现蚶贝外形，未鉴定到种。",
                "海底贝壳形态收藏", "看看草丛边缘带粗纹的双壳。", false),
            new OceanSpeciesDefinition(21, 2, "牡蛎", "贝壳", "",
                "牡蛎是双壳类，壳形往往不规则。本条为贝壳模型类别，未细分到种。",
                "海底贝壳形态收藏", "在岩面与沙地交界找不规则壳片。", false),
            new OceanSpeciesDefinition(22, 2, "帽贝", "贝壳", "",
                "帽状外壳是这类腹足动物的醒目标志。本条为贝壳模型类别，未细分到种。",
                "海底贝壳形态收藏", "沿低矮岩面寻找小帽子状轮廓。", false),
            new OceanSpeciesDefinition(23, 2, "蛏子", "贝壳", "",
                "细长的双壳是蛏类的常见外形。本条记录空壳模型，不代表已鉴定的活体物种。",
                "海底贝壳形态收藏", "在草根与细沙间找狭长壳体。", true),
            new OceanSpeciesDefinition(24, 2, "骨螺", "贝壳", "",
                "本模型以突出棘饰和螺旋壳体表现骨螺外形，未细分到种。",
                "海底贝壳形态收藏", "从礁隙侧面观察带棘的小壳。", true),
            new OceanSpeciesDefinition(25, 6, "脑珊瑚", "珊瑚", "RealisticReef/market_coral_platygyra_lod0",
                "珊瑚由珊瑚虫构成；脑珊瑚表面有脑纹般的沟脊。源模型名为 Platygyra，未鉴定到种。",
                "暖水礁区的硬质基底", "靠近礁面，观察团块表面的迷宫纹。", false),
            new OceanSpeciesDefinition(26, 6, "海扇类", "珊瑚", "RealisticReef/market_fan_coral_lod0",
                "海扇是珊瑚动物的分枝群体，并非海草。源模型仅标注海扇类，未细分到种。",
                "有水流经过的礁面", "寻找迎着水流展开的细小分枝。", true),
            new OceanSpeciesDefinition(27, 6, "鲭鱼", "鱼类", "MarineExpansion/mackerel",
                "梭形身体适合持续游动。大西洋鲭鱼常近表层结群；模型未鉴定具体种。模型：Ishxxn（CC BY 4.0），已作游戏适配。",
                "开阔水域；游戏中为礁外观察水道", "离开礁拱，留意巡游的银色鱼。", false),
            new OceanSpeciesDefinition(28, 6, "蝶鱼", "鱼类", "MarineExpansion/butterfly-fish",
                "蝶鱼是礁区常见的鱼类，侧扁的身体和醒目花纹便于观察。模型未鉴定具体种。模型：slleggott（CC BY 4.0），已作游戏适配。",
                "珊瑚礁周边；游戏中为近礁观察水道", "沿珊瑚侧面寻找小巧而花纹鲜明的鱼。", false),
            new OceanSpeciesDefinition(29, 6, "海龟", "爬行类", "MarineExpansion/sea-turtle",
                "海龟是用肺呼吸的海洋爬行动物，需要浮上海面换气。鳍状四肢适合游泳；模型未鉴定具体种。模型：Eloi（CC BY 4.0），已作游戏适配。",
                "海洋水域；游戏中为礁区上方观察水道", "抬头留意甲壳轮廓和缓慢划动的鳍肢。", true),
            new OceanSpeciesDefinition(30, 6, "海豚", "哺乳类", "MarineExpansion/dolphin",
                "海豚是哺乳动物，用肺呼吸，并通过头顶呼吸孔换气。模型未鉴定具体种。模型：Allie2k（CC BY 4.0），已作游戏适配。",
                "海洋水域；游戏中为开阔观察水道", "望向开阔水域，留意流线身体和水平尾鳍。", true),
        });

        public static IReadOnlyList<OceanSpeciesDefinition> All => Definitions;

        public static OceanSpeciesDefinition Get(int id)
        {
            if (id < 0 || id >= Count) throw new ArgumentOutOfRangeException(nameof(id));
            return Definitions[id];
        }
    }
}
